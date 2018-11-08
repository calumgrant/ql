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
        /// True if this property should be output/populated.
        /// If false, this property does not form part of the model
        /// and is not written to the DB scheme and QL.
        /// </summary>
        bool Enabled { get; set; }

        /// <summary>
        /// The type to which this property applies.
        /// </summary>
        IReflectedType DefiningType { get;  }

        /// <summary>
        /// A list of column types for the property.
        /// Usually there is just one column type, but tuples and key-value pairs
        /// can have multiple columns. Zero columns is a member predicate.
        /// </summary>
        IEnumerable<IReflectedType> Columns { get; }

        /// <summary>
        /// True if this represents members of an array,
        /// that are indexed from 0.
        /// </summary>
        bool Enumerated { get; }

        /// <summary>
        /// True if this property is assumed to not contain null,
        /// even if the type suggests that the value could be nullable.
        /// Where a null reftype is encountered, a special null-value must be created.
        /// null strings are treated as empty strings.
        /// </summary>
        bool Nullable { get; set; }

        /// <summary>
        /// For inline properties (Nullable=false), the value is stored in the parent table.
        /// Set the columns number here.
        /// </summary>
        int Column { set; }


        void GenerateDbScheme(TextWriter writer);

        void GenerateQL(TextWriter ql);
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
        IReflectedType IReflectedProperty.DefiningType => definingType;

        readonly IReflectedType definingType;
        // public readonly Type PropertyType;
        readonly string Name;
        readonly bool isIndexed;
        // QlType qlType;

        string IReflectedProperty.Name => Name;

        bool IReflectedProperty.Enumerated => isIndexed;

        // public static GeneratedProperty CreateExtension(GeneratedType attachedType, )

        public override string ToString() => Name;

        IReflectedType[] columns;

        public IEnumerable<IReflectedType> Columns => columns;

        public ReflectedProperty(Model model, IReflectedType dt, string name, Type propertyType)
        {
            definingType = dt;
            Name = name;

            var et = Model.IEnumerableType(propertyType);
            if (et != null)
                isIndexed = true;
            else
                et = propertyType;

            if (Model.IEnumerableType(et) != null)
                ;

            if (isIndexed)
                Nullable = true;
            else if (propertyType.IsNullable())
            {
                et = propertyType.GetGenericArguments()[0];
                Nullable = true;
            }
            else if (propertyType.IsString() || propertyType.IsValueType)
                Nullable = false;
            else
                Nullable = true;

            if (et.IsComposite())
            {
                columns = et.GenericTypeArguments.Where(t => model.IsRelevantType(t)).Select(t => model.LookupType(t)).ToArray();
            }
            else
            {
                columns = new IReflectedType[] { model.LookupType(et) };
            }

            // If it's an override, Enabled = false;
            Enabled = true;

            if (definingType.InheritsProperty(name))
            {
                IsOverride = true;
                Enabled = false;
            }

            if (columns.Any(t => !t.EnabledForPropertyType))
                Enabled = false;
            //if (propertyType.IsEnum || propertyType.IsString() || propertyType.IsPrimitive)
             //   Enabled = false;
        }

        bool IsOverride { get; set; }

        public ReflectedProperty(Model model, IReflectedType dt, MethodInfo info) : this(model, dt, info.Name, info.ReturnType)
        {
        }

        public ReflectedProperty(Model model, IReflectedType dt, PropertyInfo info) : this(model, dt, info.Name, info.PropertyType)
        {
        }


       // public bool IsOverride =>

        //public string TableName => definingType.TableName + "__" + Name;

        public bool Enabled { get; set; } = true;

        public bool IsInline => !Nullable;

        public string ColumnName => Name.ToLower();

        public bool Nullable { get; set; }

        public void GenerateDbScheme(TextWriter dbscheme)
        {
            if (!Enabled) return;
            if (isIndexed)
            {
                dbscheme.WriteLine("#keyset[id,index]");
                dbscheme.WriteLine($"{this.GetTableName()}(");
                dbscheme.WriteLine($"  int id: {definingType.GetDbType()} ref,");
                dbscheme.Write("  int index: int ref");
                GenerateDbArgs(dbscheme);
            }
            else if(Nullable)
            {
                dbscheme.WriteLine($"{this.GetTableName()}(");
                dbscheme.Write($"  unique int id: {definingType.GetDbType()} ref");
                GenerateDbArgs(dbscheme);
            }
        }

        public int Column { set; get; }

        void GenerateQlParams(TextWriter ql)
        {
            int column = 1;
            foreach (var t in Columns)
            {
                ql.Write($", {t.GetQlTypeName()} value{column++}");
            }
        }

        void GenerateDbArgs(TextWriter dbscheme)
        {
            int column = 1;
            foreach (var t in Columns)
            {
                dbscheme.Write($",\n  {t.GetDbTypeStorage()} value{column++}: {t.GetDbTypeName()} ref");
            }
            dbscheme.WriteLine(")");
            dbscheme.WriteLine();
        }

        public void GenerateQL(TextWriter ql)
        {
            if (!Enabled) return;

            string overrideString = IsOverride ? "override " : "";

            ql.WriteLine($"  /** Gets the `{Name}` member. */");
            if (isIndexed)
            {
                switch (columns.Length)
                {
                    case 0:
                        ql.WriteLine($"  {overrideString}predicate is{Name}(int index) {{");
                        break;
                    case 1:
                        ql.WriteLine($"  {overrideString}{columns[0].GetQlTypeName()} get{Name}(int index) {{");
                        break;
                    default:
                        ql.Write($"  {overrideString}predicate get{Name}(int index");
                        GenerateQlParams(ql);
                        break;
                }
                ql.Write($"    {this.GetTableName()}(this, index");
                if (columns.Length == 1)
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
                bool booleanPredicate = columns.Length == 1 && columns[0].TypeName == "System.Boolean" && IsInline;

                switch (columns.Length)
                {
                    case 0:
                        ql.WriteLine($"  {overrideString}predicate is{Name}() {{");
                        break;
                    case 1:
                        if (booleanPredicate)
                            ql.WriteLine($"  {overrideString}predicate is{Name}() {{");
                        else
                            ql.WriteLine($"  {overrideString}{columns[0].GetQlTypeName()} get{Name}() {{");
                        break;
                    default:
                        ql.Write($"  {overrideString}predicate get{Name}(");
                        GenerateQlParams(ql);
                        break;
                }

                if (IsInline)
                {
                    ql.Write($"    {definingType.GetTableName()}(this");
                    for (int c = 1; c < Column; ++c)
                        ql.Write(", _");
                    if (booleanPredicate)
                        ql.Write(", true");
                    else
                        ql.Write(", result");
                    for (int c = Column + 1; c < definingType.NumColumns; ++c)
                        ql.Write(", _");
                    ql.WriteLine(")");
                }
                else
                {
                    ql.Write($"    {this.GetTableName()}(this");

                    if (columns.Length == 1)
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
    }
}
