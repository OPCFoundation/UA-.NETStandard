// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    using Opc.Ua;
    using System;

    /// <summary>
    /// Enables the codecs to resolve descriptions that are needed to parse
    /// or serialize data types that are not known at compile time. The interface
    /// is exposed as alternative interface on the EncodeableFactory provided
    /// which is part of the encoder/decoder service message context and allows
    /// the StruatureType and EnumValue objects to write and read their internal
    /// state.
    /// </summary>
    internal interface IDataTypeDescriptionResolver
    {
        /// <summary>
        /// Returns the system type for the specified type id.
        /// </summary>
        /// <param name="typeOrEncodingId"></param>
        /// <returns></returns>
        Type? GetSystemType(ExpandedNodeId typeOrEncodingId);

        /// <summary>
        /// Get the information about the enum type
        /// </summary>
        /// <param name="typeOrEncodingId"></param>
        /// <returns></returns>
        EnumDescription? GetEnumDescription(ExpandedNodeId typeOrEncodingId);

        /// <summary>
        /// Get information for the structure type
        /// </summary>
        /// <param name="typeOrEncodingId"></param>
        /// <returns></returns>
        StructureDescription? GetStructureDescription(ExpandedNodeId typeOrEncodingId);
    }
}
