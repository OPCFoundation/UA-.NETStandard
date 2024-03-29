/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Interface for the complex type builder.
    /// </summary>
    public interface IComplexTypeResolver
    {
        /// <summary>
        /// Gets the table of namespace uris known to the server.
        /// </summary>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// Gets the factory used to create encodeable objects that the server understands.
        /// </summary>
        IEncodeableFactory Factory { get; }

        /// <summary>
        /// Loads all dictionaries of the OPC binary or Xml schema type system.
        /// </summary>
        /// <param name="dataTypeSystem">The type system. Defaults to OPC Binary schema.</param>
        /// <param name="ct"></param>
        Task<Dictionary<NodeId, DataDictionary>> LoadDataTypeSystem(
            NodeId dataTypeSystem = null,
            CancellationToken ct = default);

        /// <summary>
        /// Browse for the type and encoding id for a dictionary component.
        /// </summary>
        /// <remarks>
        /// According to Part 5 Annex D, servers shall provide the bi-directional
        /// references between data types, data type encodings, data type description
        /// and data type dictionary.
        /// To find the typeId and encodingId for a dictionary type definition:
        /// i) inverse browse the description to get the encodingid
        /// ii) from the description inverse browse for encoding
        /// to get the subtype typeid
        /// iii) load the DataType node
        /// </remarks>
        /// <param name="nodeId"></param>
        /// <param name="ct"></param>
        /// <returns>type id, encoding id and data type node if successful, null otherwise</returns>
        Task<(ExpandedNodeId typeId, ExpandedNodeId encodingId, DataTypeNode dataTypeNode)> BrowseTypeIdsForDictionaryComponentAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default);

        /// <summary>
        /// Browse for the encodings of a datatype list.
        /// </summary>
        /// <remarks>
        /// Is called to allow for caching of encoding information on the client.
        /// </remarks>
        Task<IList<NodeId>> BrowseForEncodingsAsync(
            IList<ExpandedNodeId> nodeIds,
            string[] supportedEncodings,
            CancellationToken ct = default);

        /// <summary>
        /// Browse for the encodings of a type.
        /// </summary>
        /// <remarks>
        /// Browse for binary encoding of a structure datatype.
        /// </remarks>
        Task<(IList<NodeId> encodings, ExpandedNodeId binaryEncodingId, ExpandedNodeId xmlEncodingId)> BrowseForEncodingsAsync(
            ExpandedNodeId nodeId,
            string[] supportedEncodings,
            CancellationToken ct = default);

        /// <summary>
        /// Load all subTypes and optionally nested subtypes of a type definition.
        /// Filter for all subtypes or only subtypes outside the default namespace.
        /// </summary>
        Task<IList<INode>> LoadDataTypesAsync(
            ExpandedNodeId dataType,
            bool nestedSubTypes = false,
            bool addRootNode = false,
            bool filterUATypes = true,
            CancellationToken ct = default);

        /// <summary>
        /// Finds a node in the node set.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="ct"></param>
        /// <returns>Returns null if the node does not exist.</returns>
        Task<INode> FindAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default);

        /// <summary>
        /// Reads the enum type array of a enum type definition node.
        /// </summary>
        /// <remarks>
        /// The enum array is stored as the Value in the property
        /// reference of the enum type NodeId
        /// </remarks>
        /// <param name="nodeId">The enum type nodeId which has an enum array in the property.</param>
        /// <param name="ct"></param>
        /// <returns>
        /// The value of the nodeId, which can be an array of
        /// <see cref="ExtensionObject"/> or of <see cref="LocalizedText"/>.
        /// <c>null</c> if the enum type array does not exist.
        /// </returns>
        Task<object> GetEnumTypeArrayAsync(ExpandedNodeId nodeId, CancellationToken ct = default);

        /// <summary>
        /// Returns the immediate supertype for the type.
        /// </summary>
        /// <param name="typeId">The type identifier.</param>
        /// <param name="ct"></param>
        /// <returns>The immediate supertype identifier for <paramref name="typeId"/></returns>
        Task<NodeId> FindSuperTypeAsync(NodeId typeId, CancellationToken ct = default);

    }
}//namespace
