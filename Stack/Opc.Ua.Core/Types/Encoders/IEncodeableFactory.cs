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
    public interface IImmutableEncodeableDictionary
    {
        /// <summary>
        /// Create a new immutable factory based on this one and modified
        /// by the builder action.
        /// </summary>
        /// <param name="builder">The builder is passed the state of
        /// this encodeable factory which then can be modified</param>
        /// <returns>An immutable reference to the factory</returns>
        IImmutableEncodeableDictionary Update(Action<IImmutableEncodeableDictionaryBuilder> builder);

        /// <summary>
        /// Try get the system type for the specified type id.
        /// </summary>
        /// <param name="typeId">The type id to return the system-type of</param>
        /// <param name="type">The system type found</param>
        /// <returns>True if the type was found</returns>
        bool TryGetSystemType(ExpandedNodeId typeId, [NotNullWhen(true)] out Type? type);

        /// <summary>
        /// The dictionary of encodeable types.
        /// </summary>
        IReadOnlyDictionary<ExpandedNodeId, Type> EncodeableTypes { get; }
    }

    /// <summary>
    /// <para>
    /// Encodeable factory builder acts as a builder of a immutable encodeable
    /// factory.
    /// </para>
    /// <para>
    /// You can manually add types. You can also import all types from an assembly.
    /// The builder can then be used to create an immutable encodeable factory.
    /// </para>
    /// </summary>
    public interface IImmutableEncodeableDictionaryBuilder
    {
        /// <summary>
        /// Adds an extension type to the factory builder.
        /// </summary>
        /// <param name="systemType">The underlying system type to add to
        /// the factory builder</param>
        void AddEncodeableType(Type systemType);

        /// <summary>
        /// Associates an encodeable type with an encoding id.
        /// </summary>
        /// <param name="encodingId">A NodeId for a Data Type Encoding
        /// node</param>
        /// <param name="systemType">The system type to use for the
        /// specified encoding.</param>
        void AddEncodeableType(ExpandedNodeId encodingId, Type systemType);

        /// <summary>
        /// <para>
        /// Adds all encodeable types exported from an assembly to the
        /// factory builder.
        /// </para>
        /// <para>
        /// This method uses reflection on the specified assembly to export
        /// all of the types the assembly exposes, and automatically adds
        /// all types that implement the <see cref="IEncodeable"/> interface,
        /// to the factory builder.
        /// </para>
        /// </summary>
        /// <param name="assembly">The assembly containing the types to add to the
        /// factory</param>
        void AddEncodeableTypes(Assembly assembly);

        /// <summary>
        /// Adds an enumerable of extension types to the factory builder.
        /// </summary>
        /// <param name="systemTypes">The underlying system types to add
        /// to the factory builder</param>
        void AddEncodeableTypes(IEnumerable<Type> systemTypes);
    }

    /// <summary>
    /// Extensions for the encodeable factory builder.
    /// </summary>
    public static class ImmutableEncodeableDictionaryExtensions
    {
        /// <summary>
        /// Adds an extension type to the factory builder.
        /// </summary>
        /// <typeparam name="T">The underlying system type to add to
        /// the factory builder</typeparam>
        public static void AddEncodeableType<T>(this IImmutableEncodeableDictionaryBuilder builder)
            where T : IEncodeable
        {
            builder.AddEncodeableType(typeof(T));
        }

        /// <summary>
        /// Get system type
        /// </summary>
        /// <param name="factory"></param>
        /// <param name="typeId"></param>
        /// <returns></returns>
        public static Type? GetSystemType(this IImmutableEncodeableDictionary factory, ExpandedNodeId typeId)
        {
            return factory.TryGetSystemType(typeId, out Type? type) ? type : null;
        }
    }

    /// <summary>
    /// Encodeable factory which can be cloned.
    /// </summary>
    public interface IEncodeableFactory : IImmutableEncodeableDictionaryBuilder, ICloneable
    {
        /// <summary>
        /// Returns the system type for the specified type id.
        /// </summary>
        /// <param name="typeId">The type id to return the system-type of</param>
        Type? GetSystemType(ExpandedNodeId typeId);

        /// <summary>
        /// The dictionary of encodeable types.
        /// </summary>
        IReadOnlyDictionary<ExpandedNodeId, Type> EncodeableTypes { get; }
    }
}
