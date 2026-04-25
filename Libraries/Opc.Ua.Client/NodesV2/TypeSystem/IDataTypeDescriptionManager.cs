#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    using Opc.Ua;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The data types description manager is caches information about data types which
    /// it makes accessible to schema generation and type discovery functionality.
    /// </summary>
    internal interface IDataTypeDescriptionManager
    {
        /// <summary>
        /// Get the data type definition and dependent definitions for a data type
        /// node id. Recursive through the cache to find all dependent types for
        /// structures fields contained in the cache.
        /// </summary>
        /// <param name="dataTypeId"></param>
        /// <param name="ct"></param>
        ValueTask<IDictionary<ExpandedNodeId, DataTypeDefinition>> GetDefinitionsAsync(
            ExpandedNodeId dataTypeId, CancellationToken ct);

        /// <summary>
        /// Load the data type definitions for the data type referenced by the provided
        /// node id. If the node is not a data type, try to resolve the data type the
        /// user intended to use.
        /// </summary>
        /// <param name="dataTypeId"></param>
        /// <param name="includeSubTypes"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="ServiceResultException"></exception>
        ValueTask PreloadDataTypeAsync(ExpandedNodeId dataTypeId,
            bool includeSubTypes = true, CancellationToken ct = default);

        /// <summary>
        /// Get a data type definition or load it if not already loaded into the cache.
        /// The method returns null if the type cannot be resolved.
        /// </summary>
        /// <param name="typeOrEncodingId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<DataTypeDescription?> GetDataTypeDescriptionAsync(
            ExpandedNodeId typeOrEncodingId, CancellationToken ct = default);
    }
}
#endif
