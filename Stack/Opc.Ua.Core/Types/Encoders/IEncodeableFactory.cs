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

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Opc.Ua
{
    /// <summary>
    /// Creates encodeable objects based on the type id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This factory is used to store and retrieve underlying OPC UA system types.
    /// <br/></para>
    /// <para>
    /// You can manually add types. You can also import all types from a specified assembly.
    /// Once the types exist within the factory, these types can be then easily queried.
    /// <br/></para>
    /// </remarks>
    public interface IEncodeableFactory
    {
        /// <summary>
        /// Returns the object used to synchronize access to the factory.
        /// </summary>
        /// <remarks>
        /// Returns the object used to synchronize access to the factory.
        /// </remarks>
        object SyncRoot { get; }

        /// <summary>
        /// Returns a unique identifier for the table instance. Used to debug problems with shared tables.
        /// </summary>
        int InstanceId { get; }

        /// <summary>
        /// Adds an extension type to the factory.
        /// </summary>
        /// <param name="systemType">The underlying system type to add to the factory</param>
        void AddEncodeableType(Type systemType);

        /// <summary>
        /// Associates an encodeable type with an encoding id.
        /// </summary>
        /// <param name="encodingId">A NodeId for a Data Type Encoding node</param>
        /// <param name="systemType">The system type to use for the specified encoding.</param>
        void AddEncodeableType(ExpandedNodeId encodingId, Type systemType);

        /// <summary>
        /// Adds all encodable types exported from an assembly to the factory.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Adds all encodable types exported from an assembly to the factory.
        /// <br/></para>
        /// <para>
        /// This method uses reflection on the specified assembly to export all of the
        /// types the assembly exposes, and automatically adds all types that implement
        /// the <see cref="IEncodeable"/> interface, to the factory.
        /// <br/></para>
        /// </remarks>
        /// <param name="assembly">The assembly containing the types to add to the factory</param>
        void AddEncodeableTypes(Assembly assembly);

        /// <summary>
        /// Adds an enumerable of extension types to the factory.
        /// </summary>
        /// <param name="systemTypes">The underlying system types to add to the factory</param>
        void AddEncodeableTypes(IEnumerable<Type> systemTypes);

        /// <summary>
        /// Returns the system type for the specified type id.
        /// </summary>
        /// <remarks>
        /// Returns the system type for the specified type id.
        /// </remarks>
        /// <param name="typeId">The type id to return the system-type of</param>
        Type GetSystemType(ExpandedNodeId typeId);

        /// <summary>
        /// The dictionary of encodeable types.
        /// </summary>
        IReadOnlyDictionary<ExpandedNodeId, Type> EncodeableTypes { get; }
    }
}
