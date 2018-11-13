using Semmle.Extraction.Reflector;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Semmle.Extraction.CSharp.Reflector
{
    public class Configuration : IConfiguration
    {
        public Configuration(string codeAnalysisAssembly, string csharpAssembly, string metadataAssembly)
        {
            MicrosoftCodeAnalysisAssembly = Assembly.LoadFile(codeAnalysisAssembly);
            MicrosoftCodeAnalysisCSharpAssembly = Assembly.LoadFile(csharpAssembly);
            SystemReflectionMetadataAssembly = Assembly.LoadFile(metadataAssembly);
        }

        public Configuration(string csharpDirectory) : this(
            Path.Combine(csharpDirectory, "Microsoft.CodeAnalysis.dll"),
            Path.Combine(csharpDirectory, "Microsoft.CodeAnalysis.CSharp.dll"),
            Path.Combine(csharpDirectory, "System.Reflection.Metadata.dll"))
        {
        }

        public Configuration()
        {
            MicrosoftCodeAnalysisAssembly = typeof(Microsoft.CodeAnalysis.Accessibility).Assembly;
            MicrosoftCodeAnalysisCSharpAssembly = typeof(Microsoft.CodeAnalysis.CSharp.Conversion).Assembly;
            SystemReflectionMetadataAssembly = typeof(System.Reflection.Metadata.ModuleReference).Assembly;
        }

        Assembly MicrosoftCodeAnalysisAssembly, MicrosoftCodeAnalysisCSharpAssembly, SystemReflectionMetadataAssembly;

        public IEnumerable<Assembly> Assemblies
        {
            get
            {
                yield return MicrosoftCodeAnalysisAssembly;
                yield return MicrosoftCodeAnalysisCSharpAssembly;
                // yield return SystemReflectionMetadataAssembly;
            }
        }

        public IEnumerable<Type> SeedTypes
        {
            get
            {
                yield return MicrosoftCodeAnalysisCSharpAssembly.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCompilation");
                yield return MicrosoftCodeAnalysisAssembly.GetType("Microsoft.CodeAnalysis.SemanticModel");
                yield return MicrosoftCodeAnalysisCSharpAssembly.GetType("Microsoft.CodeAnalysis.CSharp.CSharpExtensions");
                yield return MicrosoftCodeAnalysisAssembly.GetType("Microsoft.CodeAnalysis.SyntaxToken");
            }
        }

        public IEnumerable<Assembly> AssembliesForSubtypes => Assemblies;

        public bool Exclude(Type t)
        {
            var typename = t.FullName;
            if (typename.EndsWith("Resolver") ||
                typename.EndsWith("Comparer") ||
                typename.StartsWith("System.Reflection") ||
                typename.StartsWith("System.Text"))
            {
                return true;
            }

            switch (typename)
            {
                case "Microsoft.CodeAnalysis.CSharp.Symbols.CSharpAttributeData":
                case "Microsoft.CodeAnalysis.Operations.IAnonymousFunctionOperation":
                case "Microsoft.CodeAnalysis.Operations.IAssignmentOperation":
                    break;
                case "Microsoft.CodeAnalysis.ScriptCompilationInfo":
                case "Microsoft.CodeAnalysis.AssemblyIdentityComparer":
                case "Microsoft.CodeAnalysis.MetadataReferenceResolver":
                case "Microsoft.CodeAnalysis.IOperation":
                    return true;
                    break;
            }
            return false;
        }

        public void CustomizeType(IReflectedType t)
        {
        }

        public new bool Equals(object x, object y)
        {
            return Object.Equals(x, y);
        }

        public void GenerateId(object obj, TextWriter trapFile)
        {
            throw new NotImplementedException();
        }

        public int GetHashCode(object obj)
        {
            return obj.GetHashCode();
        }

        public bool TypeHasLabel(Type t)
        {
            throw new NotImplementedException();
        }

        public void CustomizeProperty(IReflectedProperty p)
        {
            switch(p.GetFullName())
            {
                case "Microsoft.CodeAnalysis.AssemblyIdentity.Version":
                    // Store this field inline.
                    // p.IsNullable = false;
                    return;
            }
        }

        public bool Exclude(MemberInfo info)
        {
            switch (info.Name)
            {
                case "PublicKeyToken":
                case "PublicKey":
                case "CryptoPublicKey":
                    return true;
                default:
                    return false;
            }
        }

        // public void Exclude()

        Microsoft.CodeAnalysis.AttributeData d;
        Microsoft.CodeAnalysis.Operations.IAnonymousFunctionOperation op;
    }
}
