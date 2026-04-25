#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    using Opc.Ua;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml;

    /// <summary>
    /// A dictionary data type
    /// </summary>
    /// <param name="Definition"></param>
    /// <param name="XmlName"></param>
    /// <param name="EncodingId"></param>
    internal sealed record class DictionaryDataTypeDefinition(
        DataTypeDefinition Definition, XmlQualifiedName XmlName,
        ExpandedNodeId EncodingId);

    /// <summary>
    /// Provides access to data type systems known to the client. The data type
    /// system concept predates 1.04 and is now obsolete, however many servers
    /// in the market use it to describe complex and enumeration types not defined
    /// in namespace 0.
    /// </summary>
    internal interface IDataTypeSystemManager
    {
        /// <summary>
        /// Get data type definition for an encoding of the provided data
        /// type.
        /// </summary>
        /// <param name="encoding"></param>
        /// <param name="dataTypeId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="ServiceResultException"></exception>
        ValueTask<DictionaryDataTypeDefinition?> GetDataTypeDefinitionAsync(
            QualifiedName encoding, ExpandedNodeId dataTypeId,
            CancellationToken ct = default);
    }
}
#endif
