using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Semmle.Extraction.Reflector
{
    /// <summary>
    /// A type that has been determined by reflection.
    /// It has a corresponding C# type, a QL type and a database type.
    /// </summary>
    public interface IReflectedType
    {
        /// <summary>
        /// Whether this type should be output to the DB scheme, QL and populator.
        /// </summary>
        bool Enabled { get; set; }

        bool EnabledForPropertyType { get; }

        IEnumerable<IReflectedProperty> Properties { get; }

        bool DefinesProperty(string name);

        IEnumerable<IReflectedType> BaseTypes { get;  }

        /// <summary>
        /// The C# type name.
        /// </summary>
        string TypeName { get; }

        /// <summary>
        /// True if this type can be instantiated,
        /// false if this type must be overridden.
        /// </summary>
        bool IsInstance { get; }

        QlType QlType { get; }

        void AddProperty(IReflectedProperty prop);

        int NumColumns { get; }

        void AddSubtype(IReflectedType derived);
    }

    static class TypeExtensions2
    {
        static string GetBaseName(this IReflectedType type) => type.TypeName.Replace(".", "_").Replace("+", "__");

        public static string GetTableName(this IReflectedType type) => "reflected_" + type.GetBaseName();

        public static string GetDbType(this IReflectedType type) => "@r" + type.GetBaseName();

        public static string GetDbInstanceType(this IReflectedType type) => type.GetDbType() + "_instance";

        public static string GetQlTypeName(this IReflectedType type)
        {
            switch(type.QlType)
            {
                case QlType.Boolean:
                    return "boolean";
                case QlType.Float:
                    return "float";
                case QlType.Int:
                    return "int";
                case QlType.String:
                    return "string";
                case QlType.Object:
                    return type.GetBaseName();
                default:
                    throw new ArgumentException($"Unhandled ql typename {type.QlType}");
            }
        }

        public static string GetDbTypeStorage(this IReflectedType type)
        {
            switch (type.QlType)
            {
                case QlType.Boolean:
                    return "boolean";
                case QlType.Float:
                    return "float";
                case QlType.Int:
                    return "int";
                case QlType.String:
                    return "string";
                case QlType.Object:
                    return "int";
                default:
                    throw new ArgumentException($"Unhandled ql storage for {type.QlType}");
            }
        }

        public static string GetDbTypeName(this IReflectedType type)
        {
            switch (type.QlType)
            {
                case QlType.Boolean:
                    return "boolean";
                case QlType.Float:
                    return "float";
                case QlType.Int:
                    return "int";
                case QlType.String:
                    return "string";
                case QlType.Object:
                    return type.GetDbType();
                default:
                    throw new ArgumentException($"Unhandled ql storage for {type.QlType}");
            }
        }

        public static bool InheritsProperty(this IReflectedType type, string name) => type.BaseTypes.Any(bt => bt.DefinesProperty(name));
    }

    public sealed class ReflectedType : IReflectedType
    {
        public string TypeName { get; set; }

        IEnumerable<IReflectedProperty> IReflectedType.Properties => Properties.Values;

        public bool Enabled { get; set; } = true;

        public bool EnabledForPropertyType
        {
            get
            {
                switch(QlType)
                {
                    case QlType.Boolean:
                    case QlType.String:
                    case QlType.Int:
                    case QlType.Float:
                        return true;
                    case QlType.Object:
                        return Enabled;
                    case QlType.Excluded:
                        return false;
                    default:
                        throw new ArgumentException("Invalid property type");
                }
            }
        }

        public QlType QlType { get; set; }

        public ReflectedType(Type type, Model m)
        {
            TypeName = type.FullName;
            QlType = type.GetQlType();

            IsInstance = type.IsClass; //  && !type.IsAbstract;

            //if (!m.IsRelevantType(type))
            //   Enabled = false;

            if (type.IsEnum)
                Enabled = false;
            if (TypeName == "System.Object")
                Enabled = false;

            switch(QlType)
            {
                case QlType.Excluded:
                case QlType.String:
                case QlType.Int:
                case QlType.Boolean:
                    Enabled = false;
                    break;
            }

            if (type.IsGenericType)
                Enabled = false;

            if (type.IsPointer)
                Enabled = false;

            // Generate supertypes here

            if (type.BaseType != null && m.IsRelevantType (type.BaseType) )
            {
                var rt = m.LookupType(type.BaseType);
                if(rt.Enabled)
                    supertypes.Add(rt);
            }

            foreach (var t in type.GetInterfaces().Where(t => m.IsRelevantType(t)).Select(t => m.LookupType(t)))
                supertypes.Add(t);

            foreach (var s in supertypes)
                s.AddSubtype(this);
        }

        public void CreateProperties(Type type, Model model)
        {
            foreach(var prop in type.GetProperties().Where(p=>p.DeclaringType == type && p.GetIndexParameters().Length==0))
            {
                bool isOverride = prop.GetGetMethod() != prop.GetGetMethod().GetBaseDefinition();

                if(!isOverride)
                    Properties[prop.Name] = new ReflectedProperty(model, this, prop);

                if (!Properties.Any())
                {
                    Enabled = false;
                }
            }
            foreach (var prop in type.GetMethods().Where(p => p.DeclaringType == type && model.IsInterestingGetter(p)))
            {
                var @base = prop.GetBaseDefinition();
                bool isOverride = prop != prop.GetBaseDefinition();

                if(!isOverride)
                    Properties[prop.Name] = new ReflectedProperty(model, this, prop);
            }

            // Look for properties that could apply to other types
            foreach (var prop in type.GetMethods().Where(p => p.DeclaringType == type))
            {
                if (model.IsPropertyMethod(prop, out var targetType) && targetType != type)
                {
                    // Bug - it also attaches methods from base types.
                    var targetGt = model.LookupType(targetType);
                    var member = new ReflectedProperty(model, targetGt, prop);
                    targetGt.AddProperty(member);
                }
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
            foreach (var prop in Properties.Values.Where(p => p.Enabled && !p.Nullable))
            {
                prop.Column = NumColumns++;
            }
        }

        IEnumerable<IReflectedProperty> InlineProperties => Properties.Values.Where(p => p.Enabled && !p.Nullable);

        public void GenerateDbScheme(TextWriter writer)
        {
            if (!Enabled)
                return;

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
                    writer.Write($"  int id: {this.GetDbTypeName()}");
                else
                    writer.Write($"  int id: {this.GetDbTypeName()} ref");

                foreach (var prop in Properties.Values.Where(p => p.Enabled && !p.Nullable))
                    writer.Write($",\n  {prop.Columns.Single().GetDbTypeStorage()} {prop.GetColumnName()}: {prop.Columns.Single().GetDbTypeName()} ref");

                writer.WriteLine(")");
                writer.WriteLine();
            }
            else
            if (IsInstance)
            {
                string instanceSuffix = subtypes.Any() ? "_instance" : "";
                writer.WriteLine($"{this.GetTableName()}{instanceSuffix}(");
                writer.WriteLine($"  int id: {this.GetDbTypeName()}{instanceSuffix});");
                    writer.WriteLine();
            }

            foreach (var prop in Properties.Values)
            {
                prop.GenerateDbScheme(writer);
            }

            if (HasSubTypes)
            {
                writer.WriteLine($"{this.GetDbTypeName()} =");
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
            if (!Enabled) return;
            setColumns();

            writer.WriteLine();
            writer.WriteLine($"/** Auto-generated class for `{TypeName}`. */");
            writer.WriteLine($"class {this.GetQlTypeName()} extends");
            foreach(var @base in supertypes)
            {
                writer.WriteLine($"  {@base.GetQlTypeName()},");
            }
            writer.WriteLine($"  {this.GetDbType()}");
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
            writer.WriteLine($"    void Populate(int id, {TypeName} obj, Populator populator)");
            writer.WriteLine("    {");
            writer.WriteLine("    }");
            writer.WriteLine();
        }

        void IReflectedType.AddProperty(IReflectedProperty prop)
        {
            Properties[prop.Name] = prop;
        }
    }
}
