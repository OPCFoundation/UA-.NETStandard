/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

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
    public interface IEncodeableType
    {
        /// <summary>
        /// System type of the encodeable.
        /// </summary>
        Type Type { get; }

        /// <summary>
        /// Create instance of type
        /// </summary>
        /// <returns></returns>
        IEncodeable CreateInstance();
    }

    /// <summary>
    /// Lookup encodeable types by type id.
    /// </summary>
    public interface IEncodeableTypeLookup
    {
        /// <summary>
        /// Returns the system type for the specified type id.
        /// </summary>
        /// <param name="typeId">The type id to return the type of</param>
        /// <param name="encodeableType">The encodeable type found</param>
        /// <returns><c>True</c> if found.</returns>
        bool TryGetEncodeableType(
            ExpandedNodeId typeId,
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
