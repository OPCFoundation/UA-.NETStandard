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

#nullable enable

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
    /// Represents a real encodeable object factory managed
    /// by the encodeable factory registry.
    /// </summary>
    [Experimental("UA_NETStandard_1")]
    public interface IEncodeableType
    {
        /// <summary>
        /// System type (either enum or reference type)
        /// Used when using reflection emit to emit other
        /// encodeable types.
        /// </summary>
        Type Type { get; }

        /// <summary>
        /// Get the xml qualified name for the type.
        /// </summary>
        System.Xml.XmlQualifiedName XmlName { get; }

        /// <summary>
        /// Create instance of structure type during
        /// decoding. Will change in future iterations.
        /// </summary>
        /// <returns></returns>
        IEncodeable CreateInstance();
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
    /// Lookup encodeable types by type or encoding id.
    /// </summary>
    public interface IEncodeableTypeLookup
    {
        /// <summary>
        /// Returns the activator for the specified type id.
        /// </summary>
        /// <param name="typeId">The type id to return the type of</param>
        /// <param name="encodeableType">The encodeable type found</param>
        /// <returns><c>True</c> if found.</returns>
        bool TryGetEncodeableType(
            ExpandedNodeId typeId,
            [NotNullWhen(true)] out IEncodeableType? encodeableType);

        /// <summary>
        /// Returns the activator for the specified type.
        /// </summary>
        /// <param name="encodeableType">The encodeable type found</param>
        /// <returns><c>True</c> if found.</returns>
        /// <typeparam name="T">The type to look up</typeparam>
        bool TryGetEncodeableType<T>(
            [NotNullWhen(true)] out IEncodeableType? encodeableType);
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
        /// Adds a encodeable type to the factory builder. The factory
        /// builder will call <see cref="IEncodeableType.CreateInstance"/>
        /// to obtain an instance from which to obtain the type and
        /// encoding ids. All exceptions during this process are
        /// propagated to the caller.
        /// </summary>
        /// <param name="type">A encodeable type to add.</param>
        IEncodeableFactoryBuilder AddEncodeableType(
            IEncodeableType type);

        /// <summary>
        /// Associates an encodeable type with an encoding id. The builder
        /// does not check the <see cref="IEncodeableType.CreateInstance"/>
        /// method works when adding.
        /// </summary>
        /// <param name="encodingId">A NodeId for a Data Type Encoding
        /// node</param>
        /// <param name="type">The encodeable type to use for the
        /// specified encoding.</param>
        IEncodeableFactoryBuilder AddEncodeableType(
            ExpandedNodeId encodingId,
            IEncodeableType type);

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
        IEncodeableFactoryBuilder AddEncodeableType(
            ExpandedNodeId encodingId,
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
        public static IEncodeableFactoryBuilder AddEncodeableType<T>(
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
            return lookup.TryGetEncodeableType(typeId, out IEncodeableType? encodeableType) ?
                encodeableType.Type :
                null;
        }

        /// <summary>
        /// Adds an extension type to the factory builder.
        /// </summary>
        public static void AddEncodeableType(
            this IEncodeableFactory factory,
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
            Type systemType)
        {
            factory.Builder.AddEncodeableType(encodingId, systemType).Commit();
        }

        /// <summary>
        /// Adds all encodeable types exported from an assembly
        /// to the factory builder.
        /// </summary>
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
