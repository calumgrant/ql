using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Semmle.Extraction.Tests
{
    public class TrapCacheTests : IDisposable
    {
        static string tmpDir = Path.GetTempPath();
        static string cacheDir = Path.Combine(Path.GetTempPath(), "trapCacheTests");
        TrapCache cache = new TrapCache(cacheDir, 1000);
        string filename1 = Path.Combine(tmpDir, "contents1");
        string filename2 = Path.Combine(tmpDir, "contents2");
        string filename3 = Path.Combine(tmpDir, "contents3");
        string filename4 = Path.Combine(tmpDir, "contents4");

        public TrapCacheTests()
        {
            byte[] contents1 = { 1, 2, 3 };
            byte[] contents2 = { 4, 5, 6 };

            using var file1 = File.OpenWrite(filename1);  
            file1.Write(contents1);

            using var file2 = File.OpenWrite(filename2);
            file2.Write(contents2);
        }

        public void Dispose()
        {
            File.Delete(filename1);
            File.Delete(filename2);
            File.Delete(filename3);
            File.Delete(filename4);
            Directory.Delete(cacheDir, true);
        }

        [Fact]
        public void HashFile()
        {
            byte[] data = { 9 };
            var h1 = TrapCache.HashContents(filename1);
            var h2 = TrapCache.HashContents(filename2);
            var h3 = TrapCache.HashContents(filename1, data);
            var h4 = TrapCache.HashContents(filename2, data);
            Assert.NotEqual(h1, h2);
            Assert.NotEqual(h1, h3);
            Assert.NotEqual(h2, h4);
            Assert.NotEqual(h3, h4);
            Assert.False(h1.SequenceEqual(h2));
            Assert.False(h1.SequenceEqual(h3));
            Assert.False(h2.SequenceEqual(h4));
            Assert.False(h3.SequenceEqual(h4));
        }

        [Fact]
        public void Insertion()
        {
            Assert.False(cache.TryRetrieve("abcd", filename1));
            cache.Add(filename1, "abcd");
            Assert.True(cache.TryRetrieve("abcd", filename3));
        }

        [Fact]
        public void Trim1()
        {
            cache.Add(filename1, "abcd");
            cache.Trim(1000);
            Assert.True(cache.TryRetrieve("abcd", filename3));
        }

        [Fact]
        public void Trim2()
        {
            cache.Add(filename1, "abcd");
            cache.Trim(2);
            Assert.False(cache.TryRetrieve("abcd", filename3));
        }

        [Fact]
        public void Trim3()
        {
            cache.Add(filename1, "abcd");
            cache.Add(filename1, "cdef");
            cache.Trim(4);
            Assert.False(cache.TryRetrieve("abcd", filename3));
            Assert.True(cache.TryRetrieve("cdef", filename3));
        }

        [Fact]
        public void Trim4()
        {
            cache.Add(filename1, "abcd");
            cache.Add(filename1, "cdef");
            Assert.True(cache.TryRetrieve("abcd", filename3));
            cache.Trim(4);
            Assert.False(cache.TryRetrieve("cdef", filename4));
            Assert.True(cache.TryRetrieve("abcd", filename4));
        }

    }
}
