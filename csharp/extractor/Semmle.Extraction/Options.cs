using Semmle.Util.Logging;
using Semmle.Util;
using System.IO;

namespace Semmle.Extraction
{
    /// <summary>
    /// Represents the parsed state of the command line arguments.
    /// This represents the common options.
    /// </summary>
    public abstract class CommonOptions : ICommandLineOptions
    {
        /// <summary>
        /// The specified number of threads, or the default if unspecified.
        /// </summary>
        public int Threads = Extractor.DefaultNumberOfThreads;

        /// <summary>
        /// The verbosity used in output and logging.
        /// </summary>
        public Verbosity Verbosity = Verbosity.Info;

        /// <summary>
        /// Whether to output to the console.
        /// </summary>
        public bool Console = false;

        /// <summary>
        /// Holds if CIL should be extracted.
        /// </summary>
        public bool CIL = false;

        /// <summary>
        /// Holds if assemblies shouldn't be extracted twice.
        /// </summary>
        public bool Cache = true;

        /// <summary>
        /// Whether to extract PDB information.
        /// </summary>
        public bool PDB = false;

        /// <summary>
        /// Whether "fast extraction mode" has been enabled.
        /// </summary>
        public bool Fast = false;

        /// <summary>
        /// The compression algorithm used for trap files.
        /// </summary>
        public TrapWriter.CompressionMode TrapCompression = TrapWriter.CompressionMode.Gzip;

        /// <summary>
        /// The directory containing the trap cache.
        /// </summary>
        public string TrapCacheDir = Path.Combine(Path.GetTempPath(), "CodeQLTrapCache");

        /// <summary>
        /// The maximum size of the trap cache.
        /// </summary>
        public long TrapCacheSize = 500_000_000;

        public virtual bool handleOption(string key, string value)
        {
            switch (key)
            {
                case "threads":
                    Threads = int.Parse(value);
                    return true;
                case "verbosity":
                    Verbosity = (Verbosity)int.Parse(value);
                    return true;
                case "trapCacheDir":
                    TrapCacheDir = value;
                    return true;
                case "trapCacheSize":
                    TrapCacheSize = long.Parse(value);
                    return true;
                default:
                    return false;
            }
        }

        public abstract bool handleArgument(string argument);

        public virtual bool handleFlag(string flag, bool value)
        {
            switch (flag)
            {
                case "verbose":
                    Verbosity = value ? Verbosity.Debug : Verbosity.Error;
                    return true;
                case "console":
                    Console = value;
                    return true;
                case "cache":
                    Cache = value;
                    return true;
                case "cil":
                    CIL = value;
                    return true;
                case "pdb":
                    PDB = value;
                    CIL = true;
                    return true;
                case "fast":
                    CIL = !value;
                    Fast = value;
                    return true;
                case "brotli":
                    TrapCompression = value ? TrapWriter.CompressionMode.Brotli : TrapWriter.CompressionMode.Gzip;
                    return true;
                default:
                    return false;
            }
        }

        public abstract void invalidArgument(string argument);
    }
}
