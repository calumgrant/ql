using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Semmle.Extraction.Reflector
{
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

            foreach (var t in config.SeedTypes)
                LookupType(t, out var _);

            // Get all subtypes
            foreach(var asm in config.AssembliesForSubtypes)
                foreach(var type in asm.DefinedTypes)
                {
                    // Are any base types or interfaces relevant
                    if (!types.ContainsKey(type.FullName) && ExtendsRelevantType(type))
                        LookupType(type, out var _);
                }
        }

        // Index types and assemblies by string, because sometimes dependent assemblies resolve different types.
        HashSet<string> includedAssemblies;
        HashSet<string> singletons;

        bool ExtendsRelevantType(Type type)
        {
            if (type == null) return false;
            if (!IsRelevantType(type)) return false;
            if (types.ContainsKey(type.FullName)) return true;
            if (ExtendsRelevantType(type.BaseType)) return true;
            return type.GetInterfaces().Any(ExtendsRelevantType);
        }

        private readonly Dictionary<string, IReflectedType> types = new Dictionary<string, IReflectedType>();

        private bool AddType(Type t, out IReflectedType retType)
        {
            if (!GenerateType(t))
            {
                retType = null;
                return false;
            }

            var rt = new ReflectedType(t, this);
            Configuration.CustomizeType(rt);
            types[t.FullName] = rt;
            rt.CreateProperties(t, this);
            foreach (var prop in rt.Properties)
                Configuration.CustomizeProperty(prop.Value);
            retType = rt;
            return true;
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

        public bool LookupType(Type type, out IReflectedType rt)
        {
            return types.TryGetValue(type.FullName, out rt) || AddType(type, out rt);
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
            return !info.HasDefaultValue; //  && !singletons.Contains(info.ParameterType.FullName);
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

        /// <summary>
        /// Holds if this type should be generated.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public bool GenerateType(Type type)
        {
            string name = type.FullName;

            if (type.IsEnum)
                return false;
            if (name == "System.Object")
                return false;

            switch (type.GetQlType())
            {
                case QlType.Excluded:
                case QlType.String:
                case QlType.Int:
                case QlType.Boolean:
                    return false;
            }

            if (type.IsGenericType)
                return false;

            if (type.IsPointer)
                return false;

            for (Type t = type; t != null; t = t.DeclaringType)
            {
                if (!t.IsPublic)
                    return false;
            }

            if (type.IsAbstract && type.IsSealed)
                return false;

            if (Configuration.Exclude(type))
                return false;

            if (!GetProperties(type).Any())
                return false;

            return true;
        }

        public bool ValidPropertyType(Type type)
        {
            switch (type.GetQlType())
            {
                case QlType.Boolean:
                case QlType.String:
                case QlType.Int:
                case QlType.Float:
                case QlType.Composite:
                    return true;
                case QlType.Object:
                    return GenerateType(type);
                case QlType.Excluded:
                    return false;
                default:
                    throw new ArgumentException("Invalid property type");
            }
        }

        /// <summary>
        /// Gets the potentially interesting properties of this type.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public IEnumerable<MemberInfo> GetProperties(Type type)
        {
            return type.GetMembers().Where(info => GenerateProperty(info));
        }

        /// <summary>
        /// Holds if this property is interesting and should be generated.
        /// </summary>
        /// <param name="info"></param>
        /// <returns></returns>
        public bool GenerateProperty(MemberInfo info)
        {
            Type propertyType;

            if (info is PropertyInfo pi)
            {
                if (pi.GetGetMethod().IsStatic)
                    return false;
                propertyType = pi.PropertyType;

                if (pi.GetGetMethod().GetBaseDefinition() != pi.GetGetMethod())
                    return false;   // This is an override -- ignore

                if (pi.GetIndexParameters().Length != 0)
                    return false;   // An indexer -- ignore
            }
            else if(info is MethodInfo mi)
            {
                propertyType = mi.ReturnType;

                if (mi.GetBaseDefinition() != null)
                    return false;   // This is an override -- ignore

                if (!IsInterestingGetter(mi))
                    return false;

                if (!IsPropertyMethod(mi, out var targetType) || targetType == propertyType)
                    return false;
            }
            else
            {
                return false;
            }

            var et = Model.IEnumerableType(propertyType) ?? propertyType;

            if (!ValidPropertyType(et)) return false;

            if (Configuration.Exclude(info))
                return false;

            //if (columns.Any(t => !t.EnabledForPropertyType))
            //    return false;

           return true;
        }

        // Convert a Type into a property type
        public IPropertyType GetPropertyType(Type t)
        {
            switch(t.GetQlType())
            {
                case QlType.Boolean: return new BoolType();
                case QlType.Float: return new FloatType();
                case QlType.Int: return new IntType();
                case QlType.String: return new StringType();
                case QlType.Object:
                    if (LookupType(t, out var generatedType))
                        return generatedType;
                    break;
            }
            throw new ArgumentException("Invalid type");
        }

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
            writer.WriteLine("partial class Populator");
            writer.WriteLine("{");

            foreach (var type in types)
            {
                type.Value.GenerateCSharp(writer);
            }

            writer.WriteLine("}");
        }
    }
}
