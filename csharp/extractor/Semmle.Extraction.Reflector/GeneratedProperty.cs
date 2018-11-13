using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Semmle.Extraction.Reflector
{
    /// <summary>
    /// A field, property or getter associated with a type.
    /// </summary>
    public interface IReflectedProperty
    {
        /// <summary>
        /// Gets the name of the property. This must be a valid QL identifier.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The type to which this property applies.
        /// </summary>
        IReflectedType DefiningType { get;  }

        IPropertyType[] Columns { get; }

        bool IsEnumerated { get; }

        bool IsInline { get; }

        /// <summary>
        /// For inline properties (Nullable=false), the value is stored in the parent table.
        /// Set the columns number here.
        /// </summary>
        int Column { set; }

        void GenerateDbScheme(TextWriter writer);

        void GenerateQL(TextWriter ql);

        void GenerateCSharp(TextWriter cs);
    }

    public static class PropertyExtensions
    {
        public static string GetTableName(this IReflectedProperty property) =>
            property.DefiningType.GetTableName() + "__" + property.Name;

        public static string GetColumnName(this IReflectedProperty property) => property.Name.ToLower();

        public static string GetFullName(this IReflectedProperty property) => property.DefiningType.TypeName + "." + property.Name;

    }

    public class ReflectedProperty : IReflectedProperty
    {
        public IPropertyType[] Columns { get; }

        public bool IsEnumerated { get; }

        public IReflectedType DefiningType { get; }

        public string Name { get; }

        public override string ToString() => Name;

        // The C# code that is run in order to get the raw value out of "obj".
        readonly string csharpAccessor;

        // The code to get each column
        // string[] csharpSomething...

        public ReflectedProperty(Model model, IReflectedType dt, string name, Type type, string accessor)
        {
            DefiningType = dt;
            Name = name;
            csharpAccessor = accessor;

            // Pick apart "type" to see if it's enumerable
            IsEnumerated = type.IsEnumerable(out var et);
            if (IsEnumerated) type = et;

            Type[] columns = type.IsGenericType ? type.GenericTypeArguments : new Type[] { type };

            Columns = columns.Select(c => model.GetPropertyType(c)).ToArray();
        }

        public ReflectedProperty(Model model, IReflectedType dt, MethodInfo info) : this(model, dt, info.Name, info.ReturnType, $"obj.{info.Name}()")
        {
        }

        public ReflectedProperty(Model model, IReflectedType dt, PropertyInfo info) : this(model, dt, info.Name, info.PropertyType, $"obj.{info.Name}")
        {
        }

        public bool IsInline => !IsEnumerated && InlineColumns.Any();  // !! Fixme -- could be partially inline

        public IEnumerable<IPropertyType> InlineColumns => Columns.Where(p => !p.IsNullable);

        public string ColumnName => Name.ToLower();

        public bool IsNullable => Columns.Length == 1 && Columns[0].IsNullable;

        public void GenerateDbScheme(TextWriter dbscheme)
        {
            if (IsEnumerated)
            {
                dbscheme.WriteLine("#keyset[id,index]");
                dbscheme.WriteLine($"{this.GetTableName()}(");
                dbscheme.WriteLine($"  int id: {DefiningType.DbType} ref,");
                dbscheme.Write("  int index: int ref");
                GenerateDbArgs(dbscheme);
            }
            else if(IsNullable)
            {
                dbscheme.WriteLine($"{this.GetTableName()}(");
                dbscheme.Write($"  unique int id: {DefiningType.GetDbType()} ref");
                GenerateDbArgs(dbscheme);
            }
        }

        public int Column { set; get; }

        void GenerateQlParams(TextWriter ql)
        {
            int column = 1;
            foreach (var t in Columns)
            {
                ql.Write($", {t.QlType} value{column++}");
            }
        }

        void GenerateDbArgs(TextWriter dbscheme)
        {
            int column = 1;
            foreach (var t in Columns)
            {
                dbscheme.Write($",\n  {t.DbStorageType} value{column++}: {t.DbType} ref");
            }
            dbscheme.WriteLine(")");
            dbscheme.WriteLine();
        }

        public void GenerateQL(TextWriter ql)
        {
            string overrideString = ""; //  IsOverride ? "override " : "";

            ql.WriteLine($"  /** Gets the `{Name}` member. */");
            if (IsEnumerated)
            {
                switch (Columns.Length)
                {
                    case 0:
                        ql.WriteLine($"  {overrideString}predicate is{Name}(int index) {{");
                        break;
                    case 1:
                        ql.WriteLine($"  {overrideString}{Columns[0].QlType} get{Name}(int index) {{");
                        break;
                    default:
                        ql.Write($"  {overrideString}predicate get{Name}(int index");
                        GenerateQlParams(ql);
                        break;
                }
                ql.Write($"    {this.GetTableName()}(this, index");
                if (Columns.Length == 1)
                {
                    ql.Write(", result");
                }
                else
                {
                    int column = 1;
                    foreach (var t in Columns)
                        ql.Write($", value{column++}");
                }
                ql.WriteLine(")");
                ql.WriteLine("  }");
                ql.WriteLine();
            }
            else
            {
                bool booleanPredicate = Columns.Length == 1 && Columns[0].DbType == "boolean" && IsInline;

                switch (Columns.Length)
                {
                    case 0:
                        ql.WriteLine($"  {overrideString}predicate is{Name}() {{");
                        break;
                    case 1:
                        if (booleanPredicate)
                            ql.WriteLine($"  {overrideString}predicate is{Name}() {{");
                        else
                            ql.WriteLine($"  {overrideString}{Columns[0].QlType} get{Name}() {{");
                        break;
                    default:
                        ql.Write($"  {overrideString}predicate get{Name}(");
                        GenerateQlParams(ql);
                        break;
                }

                if (IsInline)
                {
                    ql.Write($"    {DefiningType.GetTableName()}(this");
                    for (int c = 1; c < Column; ++c)
                        ql.Write(", _");
                    if (booleanPredicate)
                        ql.Write(", true");
                    else
                        ql.Write(", result");
                    for (int c = Column + 1; c < DefiningType.NumColumns; ++c)
                        ql.Write(", _");
                    ql.WriteLine(")");
                }
                else
                {
                    ql.Write($"    {this.GetTableName()}(this");

                    if (Columns.Length == 1)
                        ql.Write(", result");
                    else
                    {
                        int column = 0;
                        foreach (var t in Columns)
                            ql.Write($", value{column++}");
                    }
                    ql.WriteLine(")");

                }
                ql.WriteLine("  }");
                ql.WriteLine();
            }
        }

        public void GenerateCSharp(TextWriter cs)
        {
            cs.WriteLine($"        var prop_{this.Name} = {csharpAccessor};");

#if false
            if (isIndexed)
            {
                cs.WriteLine( "        c=0;");
                cs.WriteLine($"        foreach (var i in prop_{this.Name})");

                cs.WriteLine( "             writer.Write(\"predicate_name(\").Write(c++).Write(\",\").Write(i).WriteLine(\")\");");
            }
#endif
        }

    }
}
