using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

#nullable enable

namespace Semmle.Extraction.CSharp.Thin
{
    struct NodeInfo
    {
        public SyntaxNodeOrToken node;
        public int parentId;
        public int index;
    }

    class AstExtractor : IDisposable
    {
        int label = 0;
        FileStream file;
        Stream stream;
        TextWriter output;
        Stack<NodeInfo> nodeList;              // An explicit stack to avoid potential C# stack overflow
        string sourceFile;
        int fileId;
        SyntaxTree tree;

        public AstExtractor(string inputFile, string outputFile)
        {
            // Dispose of file and stream ??
            file = File.OpenWrite(outputFile);
            stream = new GZipStream(file, CompressionMode.Compress);
            output = new StreamWriter(stream, System.Text.Encoding.UTF8);
            // output = Console.Out;

            sourceFile = inputFile;

            var text = File.ReadAllText(inputFile);
            tree = CSharpSyntaxTree.ParseText(text);

            nodeList = new Stack<NodeInfo>();
        }

        int MakeLocation(Location location)
        {
            int locationId = MakeId();
            var loc = location.GetMappedLineSpan();
            // !! Check for off-by-ones and order.
            // !! Need to deduplicate? :-(
            output.WriteLine($"locations(#{locationId},#{fileId},{loc.StartLinePosition.Character},{loc.StartLinePosition.Line},{loc.EndLinePosition.Character},{loc.EndLinePosition.Line})");
            return locationId;
        }

        int MakeFile(string sourceFile)
        {
            // Also archive the file !!
            int fileId = MakeId(sourceFile+";file");

            int dirId = MakeFolder(Path.GetDirectoryName(sourceFile));

            output.WriteLine($"files(#{fileId},#{dirId},\"{Path.GetFileName(sourceFile)}\")");
            return fileId;
        }

        int MakeFolder(string folder)
        {
            var folderId = MakeId(folder + ";folder");

            output.WriteLine($"folders(#{folderId},\"{Path.GetFileName(folder)}\")");

            var parent = Path.GetDirectoryName(folder);
            if(!string.IsNullOrEmpty(parent))
            {
                output.WriteLine($"folder_parent(#{folderId},#{MakeFolder(parent)})");
            }
            return folderId;
        }

        public void ExtractAll()
        {
            fileId = MakeFile(sourceFile);

            nodeList.Push(new NodeInfo() { node = tree.GetRoot(), index = 0, parentId = fileId });

            while (nodeList.Count > 0)
            {
                var info = nodeList.Pop();
                var node = info.node;

                var locationId = MakeLocation(node.GetLocation());

                int nodeId = MakeId();
                var kind = node.Kind();
                var trivia = node.GetLeadingTrivia();

                output.WriteLine($"ast_nodes(#{nodeId},#{info.parentId},{info.index},{(int)kind},#{locationId})");

                if (node.IsToken)
                {
                    // Look at the leading trivia as well
                    var token = node.AsToken();
                    var nodeText = token.ValueText;
                    switch (kind)
                    {
                        case SyntaxKind.IdentifierToken:
                        case SyntaxKind.StringLiteralToken:
                        case SyntaxKind.InterpolatedStringToken:
                        case SyntaxKind.NumericLiteralToken:
                            output.WriteLine($"ast_text(#{nodeId},\"{nodeText}\")");
                            break;
                    }

                    foreach(var item in token.LeadingTrivia.Where(i => i.Kind() != SyntaxKind.WhitespaceTrivia))
                    {
                        switch(item.Kind())
                        {
                            case SyntaxKind.SingleLineCommentTrivia:
                            case SyntaxKind.SingleLineDocumentationCommentTrivia:
                            case SyntaxKind.MultiLineCommentTrivia:
                            case SyntaxKind.MultiLineDocumentationCommentTrivia:
                                var commentId = MakeId();
                                var commentLocation = MakeLocation(item.GetLocation());

                                output.WriteLine($"ast_comment(#{commentId}, #{nodeId}, {item.Kind()}, \"{item.ToFullString()}\")");
                                if (item.HasStructure)
                                {
                                    nodeList.Push(new NodeInfo() { node = item.GetStructure(), index = 0, parentId = commentId });
                                }
                                break;
                        }
                    }
                }
                else if (node.IsNode)
                {
                    // Extract children
                    int index = 0;
                    foreach (var c in node.ChildNodesAndTokens())
                    {
                        nodeList.Push(new NodeInfo() { index = index++, node = c, parentId = nodeId });
                    }

                }
            }
        }

        public void Dispose()
        {
            output.Dispose();
            stream.Dispose();
            file.Dispose();
        }

        int MakeId()
        {
            output.Write('#');
            output.Write(label);
            output.WriteLine("=*");
            return label++;
        }

        int MakeId(string id)
        {
            output.Write('#');
            output.Write(label);
            output.Write('=');
            output.WriteLine(id);
            return label++;
        }
    }


    class Program
    {
        static IEnumerable<(string, int)> EnumValues(Type type)
        {
            var values = System.Enum.GetValues(type);
            var names = System.Enum.GetNames(type);

            for (int i = 0; i < values.Length; i++)
            {
                // Retrieve the value of the ith enum item.
                object value = values.GetValue(i);
                yield return (names[i], (int)System.Convert.ChangeType(value, typeof(int)));
            }
        }
        static void OutputDbScheme()
        {
            Console.WriteLine("case @ast_node.kind of");
            bool first = true;
            foreach(var e in EnumValues(typeof(SyntaxKind)))
            {
                Console.WriteLine($"{(first?' ':'|')} {e.Item2} = @{e.Item1}");
                first = false;
            }
            Console.WriteLine(";");
        }

        static void Main(string[] args)
        {
            if(args.Length==1 && args[0] == "--dbscheme")
            {
                OutputDbScheme();
                return;
            }
            var start = DateTime.Now;
            var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.cs", SearchOption.AllDirectories);

            Console.WriteLine($"Extracting {files.Length} files");

            var root = @"C:\Temp";

            Parallel.ForEach(files.Zip(Enumerable.Range(1, files.Length)), 
                // new ParallelOptions { MaxDegreeOfParallelism = 12 }, 
                file => {
                    Console.WriteLine($"[{file.Second}/{files.Length}] Extracting {file.First}...");
                    File.Copy(file.First, $@"C:\Temp\{file.Second}.cs", true);
                    ExtractSourceFile(file.First, $@"C:\Temp\{file.Second}.trap.gz"); });
            Console.WriteLine($"Extraction finished in {DateTime.Now - start}");
        }

        static void ExtractAssembly(string inputFile, string outputFile)
        {

        }

        static void ExtractSourceFile(string inputFile, string outputFile)
        {
            using var extractor = new AstExtractor(inputFile, outputFile);
            extractor.ExtractAll();
        }
    }
}
