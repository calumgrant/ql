using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.IO;
using System.Linq;

namespace Semmle.Extraction
{
    public interface ITrapCache : IDisposable
    {
        /// <summary>
        /// The directory of the cache.
        /// </summary>
        string CacheDirectory { get; }

        /// <summary>
        /// The maximum size of the cache on disk.
        /// The cache may grow to larger than this size, but is guaranteed to not
        /// exceed the given size once the cache has been disposed.
        /// </summary>
        long MaxCacheSize { get; }

        /// <summary>
        /// Adds a file to the cache.
        /// 
        /// The hash is not computed for the file, rather it is passed in in
        /// <paramref name="hash"/>.
        /// </summary>
        /// <param name="inputFile">The file to copy into the cache.</param>
        /// <param name="hash">The hash of the file.</param>
        void Add(string inputFile, string hash);

        /// <summary>
        /// Attempts to retrieve a file from the cache.
        /// 
        /// If the file matching the hash is present, then it is copied to the target location, and
        /// the method returns true.
        /// 
        /// If the file is not present, then the method returns false.
        /// </summary>
        /// <param name="hash">The hash of the file.</param>
        /// <param name="outputFile">The target to copy the file to.</param>
        /// <returns>True if the file was copied successfully.</returns>
        public bool TryRetrieve(string hash, string outputFile);
    }

    /// <summary>
    /// Implements the trap cache.
    /// 
    /// Files are organised into 256 directories
    /// 
    /// xx/xxxxxxx
    /// 
    /// Where the first two hex digits of the hash are used to construct the directory name.
    /// This is under the assmumption that too many files in a directory could slow performance.
    /// 
    /// The size of the cache is only trimmed at the end.
    /// 
    /// The cache uses the atime of the files to determine which items were recently used or recently added.
    /// 
    /// The cache assumes that multiple processes can access the filesystem concurrently.
    /// </summary>
    public sealed class TrapCache : ITrapCache
    {
        public string CacheDirectory { get; }

        public long MaxCacheSize { get; }

        /// <summary>
        /// Creates a trap cache, or opens an existing trap cache.
        /// </summary>
        /// <param name="cacheDirectory">The directory to use for the cache.</param>
        /// <param name="cacheSize">The maximum size of the cache (after cleanup).</param>
        public TrapCache(string cacheDirectory, long cacheSize)
        {
            CacheDirectory = cacheDirectory;
            MaxCacheSize = cacheSize;

            if (!Directory.Exists(CacheDirectory))
                Directory.CreateDirectory(CacheDirectory);
        }

        public void Add(string inputFile, string hash)
        {
            if (hash is null || hash.Length < 3)
                throw new ArgumentException(nameof(hash));
            string fileInCache = GetFilename(hash);
            string dir = Path.Combine(CacheDirectory, hash.Substring(0, 2));
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            if (!File.Exists(fileInCache))
            {
                try
                {
                    File.Copy(inputFile, fileInCache, false);
                }
                catch (IOException)
                {
                }
            }
        }

        /// <summary>
        /// Hash the contents of a file, together with other metadata to hash.
        /// </summary>
        /// <param name="filename">The file to hash.</param>
        /// <param name="metaData">Additional metadata, or null.</param>
        /// <returns>The hash.</returns>
        public static byte[] HashContents(string filename)
        {
            using var sha = SHA1.Create(); //  new SHA1();
            // sha.Initialize();
            using var stream = File.OpenRead(filename);
            return sha.ComputeHash(stream);
        }

        public static string HashToString(byte[] hash, byte[] metaData)
        {
            var writer = new StringBuilder();

            for(int i=0; i<hash.Length; ++i)
            {
                writer.AppendFormat("{0:x2}", hash[i]^metaData[i%metaData.Length]);
            }
            return writer.ToString();
        }

        string GetFilename(string hash) => $"{CacheDirectory}{Path.DirectorySeparatorChar}{hash.Substring(0, 2)}{Path.DirectorySeparatorChar}{hash.Substring(2)}";


        public bool TryRetrieve(string hash, string outputFile)
        {
            if (hash is null || hash.Length<3)
                throw new ArgumentException(nameof(hash));

            string fileInCache = GetFilename(hash);
            if(File.Exists(fileInCache))
            {
                try
                {
                    File.Copy(fileInCache, outputFile, true);
                    File.SetLastAccessTime(fileInCache, DateTime.Now);
                    return true;
                }
                catch(IOException)
                {
                    // The file disappeared
                    return false;
                }
            }
            return false;
        }

        IEnumerable<FileInfo> TrapFiles
        {
            get
            {
                return new DirectoryInfo(CacheDirectory).EnumerateFileSystemInfos("*", SearchOption.AllDirectories).
                    OfType<FileInfo>().
                    OrderByDescending(info => info.LastAccessTime);
            }
        }

        public void Trim() => Trim(MaxCacheSize);

        /// <summary>
        /// Trim the size of the cache.
        /// 
        /// Deletes the oldest files based on access time.
        /// </summary>
        /// <param name="remaining">The size of the cache.</param>
        public void Trim(long remaining)
        {
            // Map from directory name to number of files in the directory.
            var directories = new Dictionary<string, int>();

            foreach (var file in TrapFiles)
            {
                var dir = file.Directory.FullName;
                directories.TryAdd(dir, 0);

                remaining -= file.Length;
                if (remaining < 0)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch (IOException)
                    {
                        // Perhaps the file is being used by a different process.
                        // Not a problem.
                    }
                }
                else
                    directories[dir]++;
            }

            // Delete all directories with 0 files in them.
            foreach (var dir in directories.Where(d => d.Value == 0))
            {
                try
                {
                    Directory.Delete(dir.Key);
                }
                catch(IOException)
                {
                    // Directory not empty - another process must have used it.
                }
            }
        }

        public void Dispose()
        {
            Trim();
        }
    }

    public sealed class NoTrapCache : ITrapCache
    {
        string ITrapCache.CacheDirectory => null;

        long ITrapCache.MaxCacheSize => 0;

        void ITrapCache.Add(string inputFile, string hash)
        {
        }

        void IDisposable.Dispose()
        {
        }

        bool ITrapCache.TryRetrieve(string hash, string outputFile) => false;
    }
}
