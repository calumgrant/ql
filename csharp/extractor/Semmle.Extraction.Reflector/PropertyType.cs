using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Semmle.Extraction.Reflector
{
    public interface IPropertyType
    {
        bool IsNullable { get; }

        string DbType { get; }

        string DbStorageType { get; }

        string QlType { get; }
    }

    class Nullable : IPropertyType
    {
        public IPropertyType UnderlyingType { get; }
        public Nullable(IPropertyType t)
        {
            UnderlyingType = t;
        }

        bool IPropertyType.IsNullable => true;

        string IPropertyType.DbType => UnderlyingType.DbType;

        string IPropertyType.DbStorageType => UnderlyingType.DbStorageType;

        string IPropertyType.QlType => UnderlyingType.QlType;
    }

    class BoolType : IPropertyType
    {
        string IPropertyType.DbType => "boolean";

        string IPropertyType.DbStorageType => "int";

        string IPropertyType.QlType => "boolean";

        bool IPropertyType.IsNullable => false;
    }


    class IntType : IPropertyType
    {
        bool IPropertyType.IsNullable => false;

        string IPropertyType.DbType => "int";

        string IPropertyType.DbStorageType => "int";

        string IPropertyType.QlType => "int";
    }

    class StringType : IPropertyType
    {
        bool IPropertyType.IsNullable => false;

        string IPropertyType.DbType => "string";

        string IPropertyType.DbStorageType => "int";

        string IPropertyType.QlType => "string";
    }

    class FloatType : IPropertyType
    {
        bool IPropertyType.IsNullable => false;

        string IPropertyType.DbType => "float";

        string IPropertyType.DbStorageType => "float";

        string IPropertyType.QlType => "float";
    }

    public enum QlType
    {
        Excluded,   // This type should not be populated
        Object,     // An object-id
        String,
        Boolean,
        Int,
        Float,
        Enumerable,
        Composite   // Tuples, Key-Value pairs etc.
    }

    static class TypeExtensions
    {
        public static bool IsString(this Type type) => type.FullName == "System.String";

        public static bool IsEnumerable(this Type type) =>
            type.Name == "IEnumerable`1" || type.GetInterfaces().Any(i => i.Name == "IEnumerable`1");

        public static bool IsNullable(this Type type) => type.Name == "Nullable`1" && type.Namespace == "System";

        public static bool IsEnumerable(this Type type, out Type enumeratedType)
        {
            if (type.IsString())
            {
                // Strings are enumerable, but we don't want to enumerate the individual characters.
                enumeratedType = null;
                return false;
            }

            var enumerable = type.Name == "IEnumerable`1" ?
                type :
                type.GetInterfaces().Where(i => i.Name == "IEnumerable`1").FirstOrDefault();

            if (enumerable != null)
            {
                enumeratedType = enumerable.GetGenericArguments()[0];
                return true;
            }
            else
            {
                enumeratedType = null;
                return false;
            }
        }

        public static bool IsComposite(this Type type)
        {
            if (type.Name == "KeyValuePair`2")
                return true;
            return false;
        }

        public static QlType GetQlType(this Type type)
        {
            if (type.IsPrimitive)
            {
                switch (type.FullName)
                {
                    case "System.String": return QlType.String;
                    case "System.Boolean": return QlType.Boolean;
                    case "System.Char":
                    case "System.UInt64":
                    case "System.Byte":
                    case "System.Int64":
                    case "System.Int16":
                    case "System.Int32": return QlType.Int;
                    case "System.Float64":
                    case "System.Double":
                    case "System.Float32": return QlType.Float;
                    case "System.IntPtr": return QlType.Excluded;
                    default:
                        throw new ArgumentException("Unhandled primitive type");
                }
            }
            else if (type.IsEnum || type.IsString())
                return QlType.String;
            else if (type.IsEnumerable())
                return QlType.Enumerable;
            else if (type.IsComposite())
                return QlType.Composite;
            else
                return QlType.Object;
        }
    }

}
