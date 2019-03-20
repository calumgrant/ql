using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Semmle.Util;
using Semmle.Util.Logging;

namespace Semmle.Extraction
{
    public interface ITrapEmitter
    {
        void EmitToTrapBuilder(ITrapBuilder tb);
    }

    class TrapCache
    {
        private string CacheDirectory { get; }

        public TrapCache(string directory)
        {
            CacheDirectory = directory;
        }

        /// <summary>
        /// Try to retrieve the file from the cache.
        /// </summary>
        /// <param name="trapDir">The directory to </param>
        /// <param name="filename"></param>
        /// <returns></returns>
        public bool CopyFromCache(string trapDir, string filename)
        {
            var source = Path.Combine(CacheDirectory, filename);

            if (!File.Exists(source))
                return false;

            var dest = Path.Combine(trapDir, filename);
            if (!File.Exists(dest))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(source, dest);
            }

            return true;
        }

        public void CopyToCache(string trapDir, string filename)
        {
            var source = Path.Combine(trapDir, filename);
            var dest = Path.Combine(CacheDirectory, filename);
            if (!File.Exists(dest))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Move(source, dest);
            }
        }
    }

    public sealed class TrapWriter : IDisposable
    {
        //#################### ENUMERATIONS ####################
        #region

        public enum InnerPathComputation
        {
            ABSOLUTE,
            RELATIVE
        }

        #endregion

        //#################### PRIVATE VARIABLES ####################

        /// <summary>
        /// The location of the src_archive directory.
        /// </summary>
        private readonly string archive;
        private static readonly Encoding UTF8 = new UTF8Encoding(false);

        //#################### PROPERTIES ####################

        public int IdCounter { get; set; } = 1;

        readonly Lazy<StreamWriter> WriterLazy;

        readonly Lazy<TrapBuilder> BuilderLazy;
        TrapBuilder Builder => BuilderLazy.Value;

        readonly ILogger Logger;

        //#################### CONSTRUCTORS ####################

        readonly string CacheDir;
        readonly string TrapDir;
        readonly string RelativeTrapname;

        readonly TrapCache TrapCache;
        readonly bool CacheThisFile;

        public TrapWriter(ILogger logger, string filePath, string trapFolder, string archive, string options, bool hashFileContents, bool cache)
        {
            CacheThisFile = cache;
            if (CacheThisFile)
            {
                CacheDir = @"W:\Temp\Cache";  // !!
                TrapCache = new TrapCache(CacheDir);
            }

            if (string.IsNullOrEmpty(trapFolder))
            {
                trapFolder = Path.Combine(Directory.GetCurrentDirectory(), "trap");
                Directory.CreateDirectory(trapFolder);
            }

            TrapDir = trapFolder;

            Logger = logger;

            RelativeTrapname = GetTrapName(filePath, options, hashFileContents);
            TrapFile = Path.Combine(TrapDir, RelativeTrapname);

            UpToDate = File.Exists(TrapFile);

            if(!UpToDate && CacheThisFile)
            {
                // Attempt to fetch the file from the cache.
                if (TrapCache.CopyFromCache(trapFolder, RelativeTrapname))
                    UpToDate = true;
            }

            WriterLazy = new Lazy<StreamWriter>(() =>
            {
                var tempPath = trapFolder ?? Path.GetTempPath();

                do
                {
                    /*
                     * Write the trap to a random filename in the trap folder.
                     * Since the trap path can be very long, we need to deal with the possibility of
                     * PathTooLongExceptions. So we use a short filename in the trap folder,
                     * then move it later.
                     *
                     * Although GetRandomFileName() is cryptographically secure,
                     * there's a tiny chance the file could already exists.
                     */
                    tmpFile = Path.Combine(tempPath, Path.GetRandomFileName());
                }
                while (File.Exists(tmpFile));

                var fileStream = new FileStream(tmpFile, FileMode.CreateNew, FileAccess.Write);
                var compressionStream = new GZipStream(fileStream, CompressionMode.Compress);
                return new StreamWriter(compressionStream, UTF8, 2000000);
            });
            BuilderLazy = new Lazy<TrapBuilder>(() => new TrapBuilder(WriterLazy.Value));
            this.archive = archive;
        }

        /// <summary>
        /// True if the trap file exists already.
        /// </summary>
        public bool UpToDate { get; }

        /// <summary>
        /// The output filename of the trap.
        /// </summary>
        public readonly string TrapFile;
        string tmpFile;     // The temporary file which is moved to trapFile once written.

        //#################### PUBLIC METHODS ####################

        /// <summary>
        /// Adds the specified input file to the source archive. It may end up in either the normal or long path area
        /// of the source archive, depending on the length of its full path.
        /// </summary>
        /// <param name="inputPath">The path to the input file.</param>
        /// <param name="inputEncoding">The encoding used by the input file.</param>
        public void Archive(string inputPath, Encoding inputEncoding)
        {
            if (string.IsNullOrEmpty(archive)) return;

            // Calling GetFullPath makes this use the canonical capitalisation, if the file exists.
            string fullInputPath = Path.GetFullPath(inputPath);

            ArchivePath(fullInputPath, inputEncoding);
        }

        /// <summary>
        /// Archive a file given the file contents.
        /// </summary>
        /// <param name="inputPath">The path of the file.</param>
        /// <param name="contents">The contents of the file.</param>
        public void Archive(string inputPath, string contents)
        {
            if (string.IsNullOrEmpty(archive)) return;

            // Calling GetFullPath makes this use the canonical capitalisation, if the file exists.
            string fullInputPath = Path.GetFullPath(inputPath);

            ArchiveContents(fullInputPath, contents);
        }

        /// <summary>
        /// Try to move a file from sourceFile to destFile.
        /// If successful returns true,
        /// otherwise returns false and leaves the file in its original place.
        /// </summary>
        /// <param name="sourceFile">The source filename.</param>
        /// <param name="destFile">The destination filename.</param>
        /// <returns>true if the file was moved.</returns>
        static bool TryMove(string sourceFile, string destFile)
        {
            try
            {
                // Prefer to avoid throwing an exception
                if (File.Exists(destFile))
                    return false;

                Directory.CreateDirectory(Path.GetDirectoryName(destFile));
                File.Move(sourceFile, destFile);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>
        /// Creates an empty trap file.
        /// </summary>
        public void WriteEmptyFile()
        {
            // A empty trap file incorrectly generates a zero-length .gz file.
            // Add some content to prevent this.
            WriterLazy.Value.WriteLine("// Empty file");
        }

        /// <summary>
        /// Close the trap file, and move it to the right place in the trap directory.
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (WriterLazy.IsValueCreated)
                {
                    WriterLazy.Value.Close();

                    if (TryMove(tmpFile, TrapFile))
                    {
                        if (CacheThisFile)
                            TrapCache.CopyToCache(TrapDir, RelativeTrapname);
                        return;
                    }

                    FileUtils.TryDelete(tmpFile);
                }
            }
            catch (Exception ex)  // lgtm[cs/catch-of-all-exceptions]
            {
                Logger.Log(Severity.Error, "Failed to move the trap file from {0} to {1} because {2}", tmpFile, TrapFile, ex);
            }
        }

        public void Emit(ITrapEmitter emitter)
        {
            emitter.EmitToTrapBuilder(Builder);
        }

        //#################### PRIVATE METHODS ####################

        class TrapBuilder : ITrapBuilder
        {
            readonly StreamWriter StreamWriter;

            public TrapBuilder(StreamWriter sw)
            {
                StreamWriter = sw;
            }

            public ITrapBuilder Append(object arg)
            {
                StreamWriter.Write(arg);
                return this;
            }

            public ITrapBuilder Append(string arg)
            {
                StreamWriter.Write(arg);
                return this;
            }

            public ITrapBuilder AppendLine()
            {
                StreamWriter.WriteLine();
                return this;
            }
        }

        /// <summary>
        /// Attempts to archive the specified input file to the normal area of the source archive.
        /// The file's path must be sufficiently short so as to render the path of its copy in the
        /// source archive less than the system path limit of 260 characters.
        /// </summary>
        /// <param name="fullInputPath">The full path to the input file.</param>
        /// <param name="inputEncoding">The encoding used by the input file.</param>
        /// <exception cref="PathTooLongException">If the output path in the source archive would
        /// exceed the system path limit of 260 characters.</exception>
        private void ArchivePath(string fullInputPath, Encoding inputEncoding)
        {
            string contents = File.ReadAllText(fullInputPath, inputEncoding);
            ArchiveContents(fullInputPath, contents);
        }

        private void ArchiveContents(string fullInputPath, string contents)
        {
            string dest = NestPaths(Logger, archive, fullInputPath, InnerPathComputation.ABSOLUTE);
            string tmpSrcFile = Path.GetTempFileName();
            File.WriteAllText(tmpSrcFile, contents, UTF8);
            try
            {
                FileUtils.MoveOrReplace(tmpSrcFile, dest);
            }
            catch (IOException ex)
            {
                // If this happened, it was probably because the same file was compiled multiple times.
                // In any case, this is not a fatal error.
                Logger.Log(Severity.Warning, "Problem archiving " + dest + ": " + ex);
            }
        }

        public static string NestPaths(ILogger logger, string outerpath, string innerpath, InnerPathComputation innerPathComputation)
        {
            string nested = innerpath;
            if (!string.IsNullOrEmpty(outerpath))
            {
                if (!Path.IsPathRooted(innerpath) && innerPathComputation == InnerPathComputation.ABSOLUTE)
                    innerpath = Path.GetFullPath(innerpath);

                // Remove all leading path separators / or \
                // For example, UNC paths have two leading \\
                innerpath = innerpath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (innerpath.Length > 1 && innerpath[1] == ':')
                    innerpath = innerpath[0] + "_" + innerpath.Substring(2);

                nested = Path.Combine(outerpath, innerpath);
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(nested));
            }
            catch (PathTooLongException)
            {
                logger.Log(Severity.Warning, "Failed to create parent directory of '" + nested + "': Path too long.");
                throw;
            }
            return nested;
        }

        public static string TrapPath(string folder, string filename, string options, bool hashFileContents)
        {
            if (string.IsNullOrEmpty(folder))
                folder = Path.Combine(Directory.GetCurrentDirectory(), "trap");
            return Path.Combine(folder, GetTrapName(filename, options, hashFileContents));
        }

        public static string GetTrapName(string filePath, string options, bool hashFileContents)
        {
            var filename = new StringBuilder();
            using (var shaAlg = new SHA256Managed())
            {
                var sha1 = shaAlg.ComputeHash(Encoding.ASCII.GetBytes(options));
                byte[] sha2;
                if (hashFileContents)
                {
                    // Compute a hash of the file contents and the options
                    using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        sha2 = shaAlg.ComputeHash(fileStream);
                    }
                }
                else
                {
                    sha2 = shaAlg.ComputeHash(Encoding.ASCII.GetBytes(filePath));
                }

                foreach (var b in sha1.Zip(sha2, (a, b) => a ^ b).Take(1))
                    filename.AppendFormat("{0:x2}", b);
                filename.Append(Path.DirectorySeparatorChar);
                filename.Append(Path.GetFileNameWithoutExtension(filePath));
                filename.Append('-');

                foreach (var b in sha1.Zip(sha2, (a, b) => a ^ b).Take(10))
                    filename.AppendFormat("{0:x2}", b);
                filename.Append(".trap.gz");
            }
            return filename.ToString();
        }
    }
}
