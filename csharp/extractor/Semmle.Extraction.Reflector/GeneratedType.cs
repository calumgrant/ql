using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Semmle.Extraction.Reflector
{
    /// <summary>
    /// A type that has been determined by reflection.
    /// It has a corresponding C# type, a QL type and a database type.
    /// </summary>
    public interface IReflectedType : IPropertyType
    {
        IEnumerable<IReflectedProperty> Properties { get; }

        bool DefinesProperty(string name);

        IEnumerable<IReflectedType> BaseTypes { get;  }

        /// <summary>
        /// The C# type name.
        /// </summary>
        string TypeName { get; }

        string CsharpTypeName { get; }

        /// <summary>
        /// True if this type can be instantiated,
        /// false if this type must be overridden.
        /// </summary>
        bool IsInstance { get; }

        // QlType QlType { get; }

        void AddProperty(IReflectedProperty prop);

        int NumColumns { get; }

        void AddSubtype(IReflectedType derived);

        /// <summary>
        /// Holds if this property is actually stored in the "Context"
        /// variable.
        /// </summary>
        bool ProvidesContext { get; }

        void GenerateDbScheme(TextWriter writer);

        void GenerateQL(TextWriter writer);

        void GenerateCSharp(TextWriter writer);
    }

    static class TypeExtensions2
    {
        static string GetBaseName(this IReflectedType type) => type.TypeName.Replace(".", "_").Replace("+", "__");

        public static string GetTableName(this IReflectedType type) => "reflected_" + type.GetBaseName();

        public static string GetDbType(this IReflectedType type) => "@r" + type.GetBaseName();

        public static string GetDbInstanceType(this IReflectedType type) => type.GetDbType() + "_instance";

        public static bool InheritsProperty(this IReflectedType type, string name) => type.BaseTypes.Any(bt => bt.DefinesProperty(name));
    }

    class GeneratedColumn
    {
        QlType type;
        string columnName;
    }

    class GeneratedPredicate
    {
        string predicateName;

        GeneratedColumn[] columns;
    }

    public sealed class ReflectedType : IReflectedType
    {
        bool IPropertyType.IsNullable => true;

        public string DbType => this.GetDbType();

        string IPropertyType.DbStorageType => "int";

        public string QlType => TypeName.Replace(".", "_").Replace("+", "_");

        public string TypeName { get; set; }

        public string CsharpTypeName => TypeName.Replace('+', '.');

        bool IReflectedType.ProvidesContext => false;

        IEnumerable<IReflectedProperty> IReflectedType.Properties => Properties.Values;

        public ReflectedType(Type type, Model m)
        {
            TypeName = type.FullName;

            IsInstance = type.IsClass; //  && !type.IsAbstract;

            //if (!m.IsRelevantType(type))
            //   Enabled = false;



            // Static classes are this:

            // Exclude static types.

            // Generate supertypes here

            if (type.BaseType != null && m.IsRelevantType (type.BaseType) )
            {
                if(m.LookupType(type.BaseType, out var rt))
                    supertypes.Add(rt);
            }

            foreach (var t in type.GetInterfaces().Where(t => m.IsRelevantType(t)))
            {
                if (m.LookupType(t, out var rt))
                    supertypes.Add(rt);
            }

            foreach (var s in supertypes)
                s.AddSubtype(this);
        }

        public void CreateProperties(Type type, Model model)
        {
            foreach(var prop in model.GetProperties(type))
            {
                if (prop is MethodInfo mi)
                    Properties[prop.Name] = new ReflectedProperty(model, this, mi);
                else if (prop is PropertyInfo pi)
                    Properties[prop.Name] = new ReflectedProperty(model, this, pi);
            }
        }

        public bool InheritsProperty(string name) => supertypes.Any(t => t.DefinesProperty(name));

        public bool DefinesProperty(string name) => Properties.ContainsKey(name) || InheritsProperty(name);

        public override string ToString()
        {
            return TypeName;
        }

        public override int GetHashCode() => TypeName.GetHashCode();

        public override bool Equals(object obj)
        {
            return obj is ReflectedType gt && TypeName.Equals(gt.TypeName);
        }

        public List<IReflectedType> subtypes = new List<IReflectedType>();
        public List<IReflectedType> supertypes = new List<IReflectedType>();

        void IReflectedType.AddSubtype(IReflectedType sub)
        {
            subtypes.Add(sub);
        }

        public bool isMember;

        public bool HasSubTypes => subtypes.Count > 0;

        public IDictionary<string, IReflectedProperty> Properties { get; } = new Dictionary<string, IReflectedProperty>();

        public int NumColumns { get; private set; }

        void setColumns()
        {
            NumColumns = 1;
            foreach (var prop in InlineProperties)
            {
                prop.Column = NumColumns++;
            }
        }

        IEnumerable<IReflectedProperty> InlineProperties => Properties.Values.Where(p => p.IsInline);

        public void GenerateDbScheme(TextWriter writer)
        {
            writer.WriteLine($"// Type information for {TypeName}");
            writer.WriteLine();

            /*
             * Table types:
             * IsInstance: We can instantiate this
             *
             */

            if(InlineProperties.Any())
            {
                // Generate the base table
                writer.WriteLine($"{this.GetTableName()}(");
                if (IsInstance)
                    writer.Write($"  int id: {this.DbType}");
                else
                    writer.Write($"  int id: {this.DbType} ref");

                foreach (var prop in InlineProperties)
                    writer.Write($",\n  {prop.Columns.Single().DbStorageType} {prop.GetColumnName()}: {prop.Columns.Single().DbType} ref");

                writer.WriteLine(")");
                writer.WriteLine();
            }
            else
            if (IsInstance)
            {
                string instanceSuffix = subtypes.Any() ? "_instance" : "";
                writer.WriteLine($"{this.GetTableName()}{instanceSuffix}(");
                writer.WriteLine($"  int id: {this.DbType}{instanceSuffix});");
                    writer.WriteLine();
            }

            foreach (var prop in Properties.Values)
            {
                prop.GenerateDbScheme(writer);
            }

            if (HasSubTypes)
            {
                writer.WriteLine($"{this.DbType} =");
                bool first = true;

                foreach (var subtype in DbSubtypes)
                {
                    writer.WriteLine($"{(first ? ' ' : '|')} {subtype}");
                    first = false;

                } writer.WriteLine(";");
                writer.WriteLine();
            }
        }

        IEnumerable<string> DbSubtypes
        {
            get
            {
                if (IsInstance)
                    yield return this.GetDbInstanceType();
                foreach (var subtype in subtypes)
                    yield return subtype.GetDbType();
            }

        }

        public void GenerateQL(TextWriter writer)
        {
            setColumns();

            writer.WriteLine();
            writer.WriteLine($"/** Auto-generated class for `{TypeName}`. */");
            writer.WriteLine($"class {this.QlType} extends");
            foreach(var @base in supertypes)
            {
                writer.WriteLine($"  {@base.QlType},");
            }
            writer.WriteLine($"  {this.DbType}");
            writer.WriteLine("{");
            foreach (var prop in Properties.Values)
                prop.GenerateQL(writer);

            if(supertypes.Count>0)
                writer.WriteLine($"  override string toString() {{ result = \"{String}\" }}");
            else
                writer.WriteLine($"  string toString() {{ result = \"{String}\" }}");
            writer.WriteLine("}");
        }

        string String => TypeName;

        IEnumerable<IReflectedType> IReflectedType.BaseTypes => supertypes;

        public bool IsInstance { get; set;  }

        public void GenerateCSharp(TextWriter writer)
        {
            // Populates an item, on demand,
            // and returns its label.
            writer.WriteLine($"    int GetLabel({CsharpTypeName} obj)");
            writer.WriteLine("    {");
            writer.WriteLine("        if(GetOrCreateLabel(obj, out int label))");
            writer.WriteLine("        {");
            writer.Write("            ");

            foreach(var t in subtypes)
            {
                writer.WriteLine($"if(obj is {t.CsharpTypeName}) Populate(label, ({t.CsharpTypeName})obj);");
                writer.Write("            else ");
            }
            writer.WriteLine("Populate(label, obj);"); // ??

            // Dispatch to the relevant populator
            // Need to generate the label, and populate everything else
            // if(obj is T1) Populate(label, (T1)obj);
            // else if(obj is T2) Populate(label, (T2)obj);
            // else ...
            writer.WriteLine("        }");
            writer.WriteLine("        return label;");
            writer.WriteLine("    }");
            writer.WriteLine();

            writer.WriteLine($"    void Populate(int id, {CsharpTypeName} obj)");
            writer.WriteLine("    {");

            foreach (var p in Properties.Values)
            {
                p.GenerateCSharp(writer);
            }

            // Creates a label, *and* populates fields
            writer.WriteLine("        writer.Write(id);");
            writer.WriteLine("        writer.Write(\"=\");");
            writer.WriteLine("        writer.WriteLine(\"*\");");
            writer.WriteLine();
            // Populate the main table
            if (InlineProperties.Any())
            {
                writer.WriteLine($"        writer.Write(\"{ this.GetTableName()}(\");");
                writer.WriteLine($"        writer.Write(id);");
                foreach (var p in InlineProperties)
                {
                    writer.WriteLine($"        writer.Write(\",\");");
                    writer.WriteLine($"        writer.Write(prop_{p.Name});");
                }
                writer.WriteLine($"        writer.WriteLine(\")\");");
            }

            writer.WriteLine("    }");
            writer.WriteLine();
        }

        void IReflectedType.AddProperty(IReflectedProperty prop)
        {
            Properties[prop.Name] = prop;
        }
    }
}
