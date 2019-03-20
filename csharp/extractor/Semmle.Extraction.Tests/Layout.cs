﻿using System.IO;
using Xunit;
using Semmle.Util.Logging;
using System.Runtime.InteropServices;

namespace Semmle.Extraction.Tests
{
    public class Layout
    {
        readonly ILogger Logger = new LoggerMock();

        [Fact]
        public void TestDefaultLayout()
        {
            var layout = new Semmle.Extraction.Layout(null, null, null);
            var project = layout.LookupProjectOrNull("foo.cs");

            // All files are mapped when there's no layout file.
            Assert.True(layout.FileInLayout("foo.cs"));

            // Test trap filename
            var tmpDir = Path.GetTempPath();
            Directory.SetCurrentDirectory(tmpDir);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // `Directory.SetCurrentDirectory()` doesn't seem to work on macOS,
                // so disable this test on macOS, for now
                Assert.NotEqual(Directory.GetCurrentDirectory(), tmpDir);
                return;
            }

            // Test trap file generation
            var trapwriterFilename = project.GetTrapPath("foo.cs", "", false);
            using (var trapwriter = project.CreateTrapWriter(Logger, "foo.cs", "", false, false))
            {
                trapwriter.Emit("1=*");
            }
            Assert.True(File.Exists(trapwriterFilename));
            File.Delete(trapwriterFilename);
        }

        [Fact]
        public void TestLayoutFile()
        {
            File.WriteAllLines("layout.txt", new string[]
            {
                "# Section",
                "TRAP_FOLDER=" + Path.GetFullPath("snapshot\\trap"),
                "ODASA_DB=snapshot\\db-csharp",
                "SOURCE_ARCHIVE=" + Path.GetFullPath("snapshot\\archive"),
                "ODASA_BUILD_ERROR_DIR=snapshot\build-errors",
                "-foo.cs",
                "bar.cs",
                "-excluded",
                "excluded/foo.cs",
                "included"
            });

            var layout = new Semmle.Extraction.Layout(null, null, "layout.txt");

            // Test general pattern matching
            Assert.True(layout.FileInLayout("bar.cs"));
            Assert.False(layout.FileInLayout("foo.cs"));
            Assert.False(layout.FileInLayout("goo.cs"));
            Assert.False(layout.FileInLayout("excluded/bar.cs"));
            Assert.True(layout.FileInLayout("excluded/foo.cs"));
            Assert.True(layout.FileInLayout("included/foo.cs"));

            // Test the trap file
            var project = layout.LookupProjectOrNull("bar.cs");
            var trapwriterFilename = project.GetTrapPath("bar.cs", "", false);
            Assert.Equal(
                Path.Combine(Path.GetFullPath("snapshot\\trap"), "bar-4c8229022af919bc02a2.trap.gz"),
                trapwriterFilename);

            // Test the source archive
            var trapWriter = project.CreateTrapWriter(Logger, "bar.cs", "", false, false);
            trapWriter.Archive("layout.txt", System.Text.Encoding.ASCII);
            var writtenFile = TrapWriter.NestPaths(Logger, Path.GetFullPath("snapshot\\archive"), "layout.txt", TrapWriter.InnerPathComputation.ABSOLUTE);
            Assert.True(File.Exists(writtenFile));
            File.Delete("layout.txt");
        }

        [Fact]
        public void TestTrapOverridesLayout()
        {
            // When you specify both a trap file and a layout, use the trap file.
            var layout = new Semmle.Extraction.Layout(Path.GetFullPath("snapshot\\trap"), null, "something.txt");
            Assert.True(layout.FileInLayout("bar.cs"));
            var f1 = layout.LookupProjectOrNull("foo.cs").GetTrapPath("foo.cs", "", false);
            var g1 = Path.Combine(Path.GetFullPath("snapshot\\trap"), "foo-c1f6755a968a021b94a2.trap.gz");
            Assert.Equal(f1, g1);
        }

        [Fact]
        public void TestMultipleSections()
        {
            File.WriteAllLines("layout.txt", new string[]
            {
                "# Section 1",
                "TRAP_FOLDER=" + Path.GetFullPath("snapshot\\trap1"),
                "ODASA_DB=snapshot\\db-csharp",
                "SOURCE_ARCHIVE=" + Path.GetFullPath("snapshot\\archive1"),
                "ODASA_BUILD_ERROR_DIR=snapshot\build-errors",
                "foo.cs",
                "# Section 2",
                "TRAP_FOLDER=" + Path.GetFullPath("snapshot\\trap2"),
                "ODASA_DB=snapshot\\db-csharp",
                "SOURCE_ARCHIVE=" + Path.GetFullPath("snapshot\\archive2"),
                "ODASA_BUILD_ERROR_DIR=snapshot\build-errors",
                "bar.cs",
            });

            var layout = new Semmle.Extraction.Layout(null, null, "layout.txt");

            // Use Section 2
            Assert.True(layout.FileInLayout("bar.cs"));
            var f1 = layout.LookupProjectOrNull("bar.cs").GetTrapPath("bar.cs", "", false);
            var g1 = Path.Combine(Path.GetFullPath("snapshot\\trap2"), "bar-4c8229022af919bc02a2.trap.gz");
            Assert.Equal(f1, g1);

            // Use Section 1
            Assert.True(layout.FileInLayout("foo.cs"));
            var f2 = layout.LookupProjectOrNull("foo.cs").GetTrapPath("foo.cs", "", false);
            var g2 = Path.Combine(Path.GetFullPath("snapshot\\trap1"), "foo-c1f6755a968a021b94a2.trap.gz");
            Assert.Equal(f2, g2);

            // boo.dll is not in the layout, so use layout from first section.
            Assert.False(layout.FileInLayout("boo.dll"));
            var f3 = layout.LookupProjectOrDefault("boo.dll").GetTrapPath("boo.dll", "", false);
            var g3 = Path.Combine(Path.GetFullPath("snapshot\\trap1"), "boo-ce6328de17f93913aada.trap.gz");
            Assert.Equal(f3, g3);

            // boo.cs is not in the layout, so return null
            Assert.False(layout.FileInLayout("boo.cs"));
            Assert.Null(layout.LookupProjectOrNull("boo.cs"));
        }

        [Fact]
        public void MissingLayout()
        {
            Assert.Throws<Extraction.Layout.InvalidLayoutException>(() =>
               new Semmle.Extraction.Layout(null, null, "nosuchfile.txt"));
        }

        [Fact]
        public void EmptyLayout()
        {
            File.Create("layout.txt").Close();
            Assert.Throws<Extraction.Layout.InvalidLayoutException>(() =>
                new Semmle.Extraction.Layout(null, null, "layout.txt"));
        }

        [Fact]
        public void InvalidLayout()
        {
            File.WriteAllLines("layout.txt", new string[]
            {
                "# Section 1"
            });

            Assert.Throws<Extraction.Layout.InvalidLayoutException>(() =>
                new Semmle.Extraction.Layout(null, null, "layout.txt"));
        }

        class LoggerMock : ILogger
        {
            public void Dispose() { }

            public void Log(Severity s, string text) { }

            public void Log(Severity s, string text, params object[] args) { }
        }
    }

    static class TrapWriterTestExtensions
    {
        public static void Emit(this TrapWriter tw, string s)
        {
            tw.Emit(new StringTrapEmitter(s));
        }

        class StringTrapEmitter : ITrapEmitter
        {
            string Content;
            public StringTrapEmitter(string content)
            {
                Content = content;
            }

            public void EmitToTrapBuilder(ITrapBuilder tb)
            {
                tb.Append(Content);
            }
        }
    }
}
