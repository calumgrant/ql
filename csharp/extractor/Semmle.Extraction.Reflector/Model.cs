using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Semmle.Extraction.Reflector
{
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

        public static bool IsNullable(this Type type) => type.Name == "Nullable`1" && type.Namespace=="System";

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

            if(enumerable != null)
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



    /// <summary>
    /// A data model derived from type information.
    /// </summary>
    public class Model
    {
        public IConfiguration Configuration { get; }

        public Model(IConfiguration config)
        {
            Configuration = config;
            includedAssemblies = new HashSet<string>(Configuration.Assemblies.Select(a=>a.GetName().Name));
            singletons = new HashSet<string>(Configuration.Singletons.Select(t=>t.FullName));

            foreach (var t in config.SeedTypes)
                LookupType(t);

            // Get all subtypes
            foreach(var asm in config.AssembliesForSubtypes)
                foreach(var type in asm.DefinedTypes)
                {
                    // Are any base types or interfaces relevant
                    if (!types.ContainsKey(type.FullName) && ExtendsRelevantType(type))
                        LookupType(type);
                }
        }

        // Index types and assemblies by string, because sometimes dependent assemblies resolve different types.
        HashSet<string> includedAssemblies;
        HashSet<string> singletons;

        bool ExtendsRelevantType(Type type)
        {
            if (type == null) return false;
            if (!IsRelevantType(type)) return false;
            if (types.ContainsKey(type.FullName)) return LookupType(type).Enabled;
            if (ExtendsRelevantType(type.BaseType)) return true;
            return type.GetInterfaces().Any(ExtendsRelevantType);
        }

        private readonly Dictionary<string, ReflectedType> types = new Dictionary<string, ReflectedType>();

        private ReflectedType AddType(Type t)
        {
            var rt = new ReflectedType(t, this);
            Configuration.CustomizeType(rt);
            types[t.FullName] = rt;
            if (rt.Enabled)
            {
                rt.CreateProperties(t, this);
                foreach (var prop in rt.Properties)
                    Configuration.CustomizeProperty(prop.Value);
            }
            return rt;
        }

        void CustomizeProperty(IReflectedProperty prop)
        {

        }

        public static Type IEnumerableType(Type type)
        {
            // Do not turn a string into an enumerable.
            if (type.FullName == "System.String") return null;

            var enumerable = type.Name == "IEnumerable`1" ?
                type :
                type.GetInterfaces().Where(i => i.Name == "IEnumerable`1").FirstOrDefault();
            return enumerable?.GetGenericArguments()[0];
        }

        public bool IsRelevantType(Type type)
        {
            if (type.IsGenericType) return false;
            if (type.IsNotPublic) return false;
            if (!includedAssemblies.Contains(type.Assembly.GetName().Name)) return false;
            return true;
        }

        public IReflectedType LookupType(Type type)
        {
            // type = IEnumerableType(type) ?? type;

            if (!types.TryGetValue(type.FullName, out var gt))
            {
                gt = AddType(type);
            }
            return gt;
        }

        private void AddAllTypes(Assembly asm)
        {
            foreach (var type in asm.DefinedTypes.Where(t => t.IsPublic && !t.IsValueType))
                AddType(type);
        }

        bool InterestingField(FieldInfo field)
        {
            return field.IsPublic;
        }

        public bool IsInterestingPropertyType(Type t)
        {
            return types.ContainsKey(t.FullName) || t.IsEnum || t.IsPrimitive || t.FullName == "System.String";
        }

        /// <summary>
        /// The
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public string GetDbTypeName1(Type t)
        {
            if (t.IsPrimitive)
                return GetPrimitiveTypeName(t);
            else if (t.IsEnum || t.IsString())
                return "varchar(1000)";  // Encode enums as their string values
            else if (types.TryGetValue(t.FullName, out var gt))
                return "int";
            else
                throw new ArgumentException("Type t is not of interest");
        }

        public string GetPrimitiveTypeName(Type type)
        {
            if (type.IsEnum) return "string";

            switch (type.FullName)
            {
                case "System.String": return "string";
                case "System.Boolean": return "boolean";
                case "System.Char":
                case "System.UInt64":
                case "System.Byte":
                case "System.Int32": return "int";
                case "System.Float64":
                case "System.Float32": return "float";
                default:
                    throw new ArgumentException("Unhandled primitive type");
            }
        }


        public ReflectedType GetGeneratedType(Type t)
        {
            return types[t.FullName];
        }


        bool isNullable(Type t)
        {
            if (t.FullName == "System.String") return false;    // Encode null strings as ""
            if (t.IsValueType) return false;
            if (t.IsPrimitive) return false;

            // !! Nullabletype

            return true;
        }

        public bool IsInterestingGetter(MethodInfo method)
        {
            if (method.Name == "ToString") return false;
            if (method.Name == "GetHashCode") return false;

            if (method.GetParameters().Any(p => !p.HasDefaultValue))
                return false;
            // if (method.GetParameters().Length > 0) return false;

            //if (!IsInterestingFieldType(method.ReturnType)) return false;

            if (!method.IsPublic) return false;
            if (method.IsSpecialName) return false;  // Property getter probably

            if (method.Name.StartsWith("Get")) return true;

            if (interestingGetters.Contains(method.Name)) return true;

            return false;
        }

        bool parameterNeedsValue(ParameterInfo info)
        {
            return !info.HasDefaultValue && !singletons.Contains(info.ParameterType.FullName);
        }

        /// <summary>
        /// Determines if a particular method returns an interesting property.
        /// There must be one "free variable" in either its parameters or qualifier.
        /// </summary>
        ///
        /// <param name="info"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public bool IsPropertyMethod(MethodInfo info, out Type type)
        {
            type = null;
            int requiredParams = 0;

            if (!info.Name.StartsWith("Get")) return false;

            if (!info.IsStatic && !singletons.Contains(info.DeclaringType.FullName))
            {
                return false;

                // ??
                ++requiredParams;
                type = info.DeclaringType;
            }

            // There is one free argument, whose type is "type"
            foreach(var t in info.GetParameters())
            {
                if(parameterNeedsValue(t) && IsRelevantType(t.ParameterType))
                {
                    ++requiredParams;
                    type = t.ParameterType;
                }
            }
            return requiredParams == 1;
        }

        HashSet<string> interestingGetters = new HashSet<string>();

        public void GenerateDbScheme(TextWriter writer)
        {
            writer.WriteLine();

            foreach (var type in types.OrderBy(t=>t.Key))
            {
                type.Value.GenerateDbScheme(writer);
            }
        }

        public void GenerateQL(TextWriter writer)
        {
            foreach (var type in types.OrderBy(t=>t.Key))
            {
                type.Value.GenerateQL(writer);
            }
        }

        public void GenerateCSharp(TextWriter writer)
        {
            writer.WriteLine("namespace GeneratedModels");
            writer.WriteLine("{");
            foreach (var type in types)
            {
                type.Value.GenerateCSharp(writer);
            }
            writer.WriteLine("}");
        }
    }
}
