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
        /// Create instance of structure type during
        /// decoding. Will change in future iterations.
        /// </summary>
        /// <returns></returns>
        IEncodeable CreateInstance();
    }

    /// <summary>
    /// Lookup encodeable types by type or encoding id.
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
    }
}
