using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Semmle.Util.Logging;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;

namespace Semmle.Extraction.CSharp
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class RoslynAnalyser : DiagnosticAnalyzer
    {
        static class DiagnosticIds
        {
            // Stateless analyzer IDs.
            public const string SymbolAnalyzerRuleId = "CSS0001";
            public const string SyntaxNodeAnalyzerRuleId = "CSS0002";
            public const string SyntaxTreeAnalyzerRuleId = "CSS0003";
            public const string SemanticModelAnalyzerRuleId = "CSS0004";
            public const string CodeBlockAnalyzerRuleId = "CSS0005";
            public const string CompilationAnalyzerRuleId = "CSS0006";
            public const string IOperationAnalyzerRuleId = "CSS0007";

            // Stateful analyzer IDs.
            public const string CodeBlockStartedAnalyzerRuleId = "CSS0101";
            public const string CompilationStartedAnalyzerRuleId = "CSS0102";
            public const string CompilationStartedAnalyzerWithCompilationWideAnalysisRuleId = "CSS0103";

            // Additional File analyzer IDs.
            public const string SimpleAdditionalFileAnalyzerRuleId = "CSS0201";
            public const string XmlAdditionalFileAnalyzerRuleId = "CSS0202";
        }

        static class DiagnosticCategories
        {
            public const string Stateless = "SampleStatelessAnalyzers";
            public const string Stateful = "SampleStatefulAnalyzers";
            public const string AdditionalFile = "SampleAdditionalFileAnalyzers";
        }

        private const string Title = "";
        private const string MessageFormat = "";
        private const string Description = "Secure types must not implement interfaces with unsecure methods.";

        private static DiagnosticDescriptor Rule =
            new DiagnosticDescriptor(
                DiagnosticIds.CompilationStartedAnalyzerWithCompilationWideAnalysisRuleId,
                Title,
                MessageFormat,
                DiagnosticCategories.Stateful,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true,
                description: Description);

        // If we have no supported diagnostics, then the analyzer will not be run.
        // Therefore, create a dummy diagnostic which will never be raised.
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
           ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            logger = new FileLogger(Verbosity.Info, Extractor.GetCSharpLogPath());
            logger.Log(Severity.Info, "Initializing Semmle Roslyn Analyser");

            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationAction(Compile);
        }
        ILogger logger;

        public RoslynAnalyser()
        {
        }

        static RoslynAnalyser()
        {
            // Load the dependent assemblies from the directory of Semmle.Extraction.CSharp.dll
            // Otherwise, we risk a FileNotFoundException when attempting to use these assemblies.
            string[] assemblies = { "Semmle.Util.dll", "Semmle.Extraction.dll", "Semmle.Extraction.CIL.dll" };
            var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            foreach (var asm in assemblies)
            {
                var assemblyPath = Path.Combine(directory, asm);
                var util = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            }
        }

        private void Compile(CompilationAnalysisContext context)
        {
            var compilation = context.Compilation as CSharpCompilation;

            using (var analyser = new Analyser(new LogProgressMonitor(logger), logger))
            {
                var options = Options.CreateWithEnvironment(new string[] { "--cil" });
                analyser.InitializeStandalone(compilation, options);
                analyser.AnalyseReferences();

                foreach (var tree in compilation.SyntaxTrees)
                    analyser.AnalyseTree(tree);

                analyser.PerformExtraction(options.Threads);
            }
        }
    }
}
