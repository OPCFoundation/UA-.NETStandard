/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// <para>
    /// Immutable factory for encodeable objects based on the type id.
    /// The factory is used to store and retrieve underlying OPC UA
    /// system types.
    /// </para>
    /// <para>
    /// Use Update call to populate the factory. The immutable factory
    /// can be shared as it is thread safe (it can only be changed by
    /// producing a new reference to a new factory). Lookup is optimized
    /// internally for fast access using a ImmutableDictionary or
    /// FrozenDictionary depending on the target runtime.
    /// </para>
    /// </summary>
    public interface IEncodeableFactory : IEncodeableTypeLookup
    {
        /// <summary>
        /// Known types in the factory.
        /// </summary>
        IEnumerable<ExpandedNodeId> KnownTypeIds { get; }

        /// <summary>
        /// Get a factory builder.
        /// </summary>
        IEncodeableFactoryBuilder Builder { get; }
    }

    /// <summary>
    /// Encodeable activator
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class EncodeableType<T> : IEncodeableType
        where T : IEncodeable
    {
        /// <inheritdoc/>
        public Type Type => typeof(T);

        /// <inheritdoc/>
        public abstract XmlQualifiedName XmlName { get; }

        /// <inheritdoc/>
        public abstract IEncodeable CreateInstance();
    }

    /// <summary>
    /// Enumerated type activator
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class EnumeratedType<T> : IEnumeratedType
        where T : struct, Enum
    {
        /// <inheritdoc/>
        public Type Type => typeof(T);

        /// <inheritdoc/>
        public virtual EnumValue Default => EnumValue.GetDefault<T>();

        /// <inheritdoc/>
        public virtual bool TryGetSymbol(int value, out string? symbol)
        {
            T enumValue = EnumHelper.Int32ToEnum<T>(value);
#if NET8_0_OR_GREATER
            symbol = Enum.GetName(enumValue);
#else
            symbol = Enum.GetName(typeof(T), enumValue);
#endif
            return symbol != null;
        }

        /// <inheritdoc/>
        public virtual bool TryGetValue(string symbol, out int value)
        {
            if (!Enum.TryParse(symbol, out T enumValue))
            {
                value = default;
                return false;
            }
            value = EnumHelper.EnumToInt32(enumValue);
            return true;
        }

        /// <inheritdoc/>
        public abstract XmlQualifiedName XmlName { get; }
    }

    /// <summary>
    /// Lookup encodeable types by type or encoding id.
    /// </summary>
    public interface IEncodeableTypeLookup
    {
        /// <summary>
        /// Returns the encodeable type for the specified type id.
        /// </summary>
        /// <param name="typeId">The type id to return the type of</param>
        /// <param name="encodeableType">The encodeable type found</param>
        /// <returns><c>True</c> if found.</returns>
        bool TryGetEncodeableType(
            ExpandedNodeId typeId,
            [NotNullWhen(true)] out IEncodeableType? encodeableType);

        /// <summary>
        /// Returns the enumerated type for the specified type id.
        /// </summary>
        /// <param name="typeId">The type id to return the type of</param>
        /// <param name="enumeratedType">The enumerated type found</param>
        /// <returns><c>True</c> if found.</returns>
        bool TryGetEnumeratedType(
            ExpandedNodeId typeId,
            [NotNullWhen(true)] out IEnumeratedType? enumeratedType);

        /// <summary>
        /// Returns the type with the specified xml name.
        /// </summary>
        /// <param name="xmlName">The xml name to return the type of</param>
        /// <param name="type">The type found</param>
        /// <returns><c>True</c> if found.</returns>
        bool TryGetType(
            XmlQualifiedName xmlName,
            [NotNullWhen(true)] out IType? type);
    }

    /// <summary>
    /// <para>
    /// Encodeable factory builder acts as a builder of a immutable
    /// encodeable factory.
    /// </para>
    /// <para>
    /// You can manually add types. You can also import all types
    /// from an assembly. The builder can then be used to create an
    /// immutable encodeable factory.
    /// </para>
    /// </summary>
    public interface IEncodeableFactoryBuilder : IEncodeableTypeLookup
    {
        /// <summary>
        /// Adds a encodeable type to the factory builder.
        /// The factory builder will call
        /// <see cref="IEncodeableType.CreateInstance"/> to
        /// obtain an instance from which to obtain the type and
        /// encoding ids. All exceptions during this process are
        /// propagated to the caller.
        /// </summary>
        /// <param name="type">A encodeable type to add.</param>
        IEncodeableFactoryBuilder AddEncodeableType(
            IEncodeableType type);

        /// <summary>
        /// Associates an encodeable type with an encoding id.
        /// The builder does not check the
        /// <see cref="IEncodeableType.CreateInstance"/> method
        /// works when adding.
        /// </summary>
        /// <param name="encodingId">A NodeId for a Data Type
        /// Encoding node</param>
        /// <param name="type">The encodeable type to use for the
        /// specified encoding.</param>
        IEncodeableFactoryBuilder AddEncodeableType(
            ExpandedNodeId encodingId,
            IEncodeableType type);

        /// <summary>
        /// Adds a enumerated type to the factory builder. An
        /// enumerated type is an Enum type in .net.
        /// </summary>
        /// <param name="type">A enumerated type to add.</param>
        IEncodeableFactoryBuilder AddEnumeratedType(
            IEnumeratedType type);

        /// <summary>
        /// Associates an enumerated type with an encoding id.
        /// </summary>
        /// <param name="encodingId">A NodeId for a Data Type
        /// Encoding node</param>
        /// <param name="type">The enumerated type to use for
        /// the specified encoding.</param>
        IEncodeableFactoryBuilder AddEnumeratedType(
            ExpandedNodeId encodingId,
            IEnumeratedType type);

        /// <summary>
        /// Adds a .net type to the factory builder. The factory
        /// builder will use reflection to extract the type ids.The
        /// builder will add a wrapper to wrap the system type so it
        /// can be instantiated using Activator.CreateInstance. The
        /// factory silently discards the type if it does not match
        /// the requirements of Activator.CreateInstance returning
        /// an <see cref="IEncodeable"/> or if a null argument is
        /// passed (legacy behavior).
        /// </summary>
        /// <param name="systemType">The underlying system type to add to
        /// the factory builder</param>
        IEncodeableFactoryBuilder AddEncodeableType(
            [DynamicallyAccessedMembers(
                DynamicallyAccessedMemberTypes.PublicConstructors)]
            Type systemType);

        /// <summary>
        /// Associates an .net system type with an encoding id. The
        /// builder will add a wrapper to wrap the system type so it
        /// can be instantiated using Activator.CreateInstance. The
        /// factory silently discards the type if it does not match
        /// the requirements of Activator.CreateInstance returning
        /// an <see cref="IEncodeable"/> or if null arguments are
        /// passed (legacy behavior).
        /// </summary>
        /// <param name="encodingId">A NodeId for a Data Type Encoding
        /// node</param>
        /// <param name="systemType">The system type to use for the
        /// specified encoding.</param>
        IEncodeableFactoryBuilder AddType(
            ExpandedNodeId encodingId,
            [DynamicallyAccessedMembers(
                DynamicallyAccessedMemberTypes.PublicConstructors)]
            Type systemType);

        /// <summary>
        /// <para>
        /// Adds all encodeable .net types exported from an assembly to
        /// the factory builder. Any errors are silently discarded.
        /// </para>
        /// <para>
        /// This method uses reflection on the specified assembly to
        /// export all of the types the assembly exposes, and
        /// automatically adds all types that implement the
        /// <see cref="IEncodeable"/> interface, to the factory builder.
        /// </para>
        /// </summary>
        /// <param name="assembly">The assembly containing the types
        /// to add to the factory</param>
        [RequiresUnreferencedCode(
            "Scans assembly types via reflection.")]
        IEncodeableFactoryBuilder AddEncodeableTypes(Assembly assembly);

        /// <summary>
        /// Commit the changes to the encodeable factory. The builder
        /// can be re-used after commit to make more changes.
        /// </summary>
        void Commit();
    }

    /// <summary>
    /// Obsolete methods on the IEncodeableFactory interface.
    /// </summary>
    public static class EncodeableFactoryExtensions
    {
        /// <summary>
        /// Adds an extension type to the factory builder.
        /// </summary>
        /// <typeparam name="T">The underlying system type to add to
        /// the factory builder</typeparam>
        public static IEncodeableFactoryBuilder AddEncodeableType<
            [DynamicallyAccessedMembers(
                DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
            this IEncodeableFactoryBuilder builder)
            where T : IEncodeable
        {
            return builder.AddEncodeableType(typeof(T));
        }

        /// <summary>
        /// Returns the system type for the datatype.
        /// </summary>
        /// <param name="lookup">The lookup capability.</param>
        /// <param name="typeId">The type id.</param>
        /// <returns>The system type for the <paramref name="typeId"/>.</returns>
        public static Type? GetSystemType(
            this IEncodeableTypeLookup lookup,
            ExpandedNodeId typeId)
        {
            return lookup.TryGetType(typeId, out IType? type) ?
                type.Type :
                null;
        }

        /// <summary>
        /// Try get the type for the specified type id. The method
        /// first tries to find an encodeable type and then an
        /// enumerated type. If neither is found, it returns false.
        /// </summary>
        /// <param name="lookup">The lookup capability.</param>
        /// <param name="typeId">The type id.</param>
        /// <param name="type">The type associated with the type id.</param>
        /// <returns>True if a type is found; otherwise, false.</returns>
        public static bool TryGetType(
            this IEncodeableTypeLookup lookup,
            ExpandedNodeId typeId,
            [NotNullWhen(true)] out IType? type)
        {
            if (lookup.TryGetEncodeableType(
                typeId,
                out IEncodeableType? encodeableType))
            {
                type = encodeableType;
                return true;
            }
            if (lookup.TryGetEnumeratedType(
                typeId,
                out IEnumeratedType? enumeratedType))
            {
                type = enumeratedType;
                return true;
            }
            type = null;
            return false;
        }

        /// <summary>
        /// Checks whether the type exists in the factory lookup
        /// </summary>
        /// <returns>True if the type was found</returns>
        public static bool ContainsType(
            this IEncodeableTypeLookup lookup,
            ExpandedNodeId typeId)
        {
            return lookup.TryGetType(typeId, out _);
        }

        /// <summary>
        /// Checks whether the enumerated type exists
        /// </summary>
        /// <returns>True if the type was found</returns>
        public static bool ContainsEnumeratedType(
            this IEncodeableTypeLookup lookup,
            ExpandedNodeId typeId)
        {
            return lookup.TryGetEnumeratedType(typeId, out _);
        }

        /// <summary>
        /// Checks whether the encodeable type exists
        /// </summary>
        /// <returns>True if the type was found</returns>
        public static bool ContainsEncodeableType(
            this IEncodeableTypeLookup lookup,
            ExpandedNodeId typeId)
        {
            return lookup.TryGetEncodeableType(typeId, out _);
        }

        /// <summary>
        /// Adds an extension type to the factory builder.
        /// </summary>
        public static void AddEncodeableType(
            this IEncodeableFactory factory,
            [DynamicallyAccessedMembers(
                DynamicallyAccessedMemberTypes.PublicConstructors)]
            Type systemType)
        {
            factory.Builder.AddEncodeableType(systemType).Commit();
        }

        /// <summary>
        /// Associates an encodeable type with an encoding id.
        /// </summary>
        public static void AddEncodeableType(
            this IEncodeableFactory factory,
            ExpandedNodeId encodingId,
            [DynamicallyAccessedMembers(
                DynamicallyAccessedMemberTypes.PublicConstructors)]
            Type systemType)
        {
            factory.Builder.AddType(encodingId, systemType).Commit();
        }

        /// <summary>
        /// Adds all encodeable types exported from an assembly
        /// to the factory builder.
        /// </summary>
        [RequiresUnreferencedCode("Scans assembly types via reflection.")]
        public static void AddEncodeableTypes(
            this IEncodeableFactory factory,
            Assembly assembly)
        {
            factory.Builder.AddEncodeableTypes(assembly).Commit();
        }

        /// <summary>
        /// Adds an enumerable of extension types to the factory builder.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2072",
            Justification = "Types from IEnumerable<Type> are expected to have constructors preserved by the caller.")]
        public static void AddEncodeableTypes(
            this IEncodeableFactory factory,
            IEnumerable<Type> systemTypes)
        {
            if (systemTypes is null)
            {
                throw new ArgumentNullException(nameof(systemTypes));
            }

            IEncodeableFactoryBuilder builder = factory.Builder;
            foreach (Type systemType in systemTypes)
            {
                builder.AddEncodeableType(systemType);
            }
            builder.Commit();
        }
    }
}
