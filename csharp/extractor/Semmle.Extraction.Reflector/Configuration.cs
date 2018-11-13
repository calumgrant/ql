using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Semmle.Extraction.Reflector
{
    /// <summary>
    /// Configuration for reflection-based trap generation.
    /// </summary>
    public interface IConfiguration : IEqualityComparer<object>
    {
        /// <summary>
        /// The list of assemblies that should be scanned automatically.
        /// </summary>
        IEnumerable<Assembly> Assemblies { get; }

        /// <summary>
        /// The list of assemblies that should be scanned for subtypes
        /// </summary>
        IEnumerable<Assembly> AssembliesForSubtypes { get; }

        /// <summary>
        /// The list of types that will be used to "seed" trap generation.
        /// Need to generate database types for all
        /// </summary>
        IEnumerable<Type> SeedTypes { get; }

        /// <summary>
        /// Called once for each generated type.
        /// Allows the configuration to add/remove properties.
        /// </summary>
        /// <param name="t"></param>
        void CustomizeType(IReflectedType t);

        /// <summary>
        /// Called once for each property.
        /// Allows individual properties to be tweaked or disabled.
        /// </summary>
        /// <param name="p"></param>
        void CustomizeProperty(IReflectedProperty p);

        /// <summary>
        /// Holds if a particular type should be excluded.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        bool Exclude(Type type);

        /// <summary>
        /// Holds if a particular member (property, method, field)
        /// should be excluded from extraction.
        /// </summary>
        /// <param name="info">The member to exclude.</param>
        /// <returns>True iff the member should be excluded.</returns>
        bool Exclude(MemberInfo info);

        /// <summary>
        /// Generates a label for a particular object.
        ///
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="trapFile"></param>
        void GenerateId(object obj, TextWriter trapFile);

        bool TypeHasLabel(Type t);

        // Additional getters

        // IEnumerable<IReflectedProperty> AdditionalProperties { get; }
    }
}
