/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;

namespace Opc.Ua.Server.Tests.SchemaRegistry
{
    /// <summary>
    /// Proves the Schema Registry spec's federation model (Annex B, §4.3): a schema hosted by
    /// another registry is represented locally by a proxy carrying an <c>ExternalReference</c>
    /// (an <see cref="ExpandedNodeId"/> whose <c>ServerIndex</c> names the remote OPC UA server
    /// via the <c>ServerArray</c>, and whose <c>NamespaceUri</c> + <c>Identifier</c> are the
    /// remote schema node's identity) and/or a <c>ResourceUrl</c> (the endpoint in string form).
    /// Because a schema's identity (its <c>SchemaId</c>) is content-derived and therefore stable
    /// across registries, the same schema federated from several endpoints keeps <b>one</b>
    /// identity and can be de-duplicated by <c>SchemaId</c>.
    /// </summary>
    internal sealed class SchemaRegistryFederationNodeManager : CustomNodeManager2
    {
        /// <summary>Provisional NodeId of the federated schema proxy object.</summary>
        public const uint ProxyObject = 64001;

        /// <summary>Provisional NodeId of the proxy's <c>ExternalReference</c> Property.</summary>
        public const uint ExternalReferenceProperty = 64002;

        /// <summary>Provisional NodeId of the proxy's <c>ResourceUrl</c> Property.</summary>
        public const uint ResourceUrlProperty = 64003;

        /// <summary>Provisional NodeId of the proxy's <c>SchemaId</c> Property.</summary>
        public const uint SchemaIdProperty = 64004;

        /// <summary>The remote registry's Schema Registry namespace URI (a peer OPC UA server).</summary>
        public const string RemoteRegistryNamespaceUri =
            "http://opcfoundation.org/UA/SchemaRegistry/";

        /// <summary>The remote registry endpoint carried by the proxy's <c>ResourceUrl</c>.</summary>
        public const string RemoteEndpointUrl = "opc.tcp://remote-registry.example:4840";

        /// <summary>The remote server's index into the local <c>ServerArray</c>.</summary>
        public const uint RemoteServerIndex = 1;

        /// <summary>The document hosted by the remote registry (federated here as a proxy).</summary>
        public static readonly byte[] FederatedDocument = System.Text.Encoding.UTF8.GetBytes(
            "{\"type\":\"record\",\"name\":\"Federated\",\"fields\":[]}");

        /// <summary>
        /// Initializes the federation node manager for the Schema Registry namespace.
        /// </summary>
        /// <param name="server">The server that owns the node manager.</param>
        /// <param name="configuration">The application configuration.</param>
        public SchemaRegistryFederationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : base(server, configuration, SchemaRegistryTestServer.SchemaRegistryNamespaceUri)
        {
        }

        /// <summary>
        /// Materializes the federated schema proxy with its <c>ExternalReference</c>,
        /// <c>ResourceUrl</c> and <c>SchemaId</c> metadata.
        /// </summary>
        /// <param name="externalReferences">External reference sink (unused).</param>
        public override void CreateAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            base.CreateAddressSpace(externalReferences);

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(
                SchemaRegistryTestServer.SchemaRegistryNamespaceUri);

            byte[] schemaIdBytes;
#pragma warning disable UA_NETStandard_Encoders // pluggable per-format fingerprint provider (§6.6)
            schemaIdBytes = SchemaIdProviders.ComputeSchemaId("avro", FederatedDocument);
#pragma warning restore UA_NETStandard_Encoders
            var schemaId = ByteString.From(schemaIdBytes);

            var proxy = new BaseObjectState(null)
            {
                NodeId = new NodeId(ProxyObject, ns),
                BrowseName = new QualifiedName("FederatedSchemaProxy", ns),
                DisplayName = new LocalizedText("FederatedSchemaProxy"),
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };

            // The federation link: ServerIndex -> remote ServerUri (via ServerArray),
            // NamespaceUri + Identifier -> the remote schema node (content-addressed by SchemaId).
            var externalReference = new ExpandedNodeId(
                schemaId, RemoteRegistryNamespaceUri, RemoteServerIndex);

            AddProperty(proxy, ExternalReferenceProperty, ns, "ExternalReference",
                DataTypeIds.ExpandedNodeId, new Variant(externalReference));
            AddProperty(proxy, ResourceUrlProperty, ns, "ResourceUrl",
                DataTypeIds.String, new Variant(RemoteEndpointUrl));
            AddProperty(proxy, SchemaIdProperty, ns, "SchemaId",
                DataTypeIds.ByteString, new Variant(schemaId));

            AddPredefinedNode(SystemContext, proxy);
        }

        private static void AddProperty(
            BaseObjectState parent,
            uint id,
            ushort ns,
            string name,
            NodeId dataType,
            Variant value)
        {
            var property = new PropertyState(parent)
            {
                NodeId = new NodeId(id, ns),
                BrowseName = new QualifiedName(name, ns),
                DisplayName = new LocalizedText(name),
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                TypeDefinitionId = VariableTypeIds.PropertyType,
                DataType = dataType,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = value
            };

            parent.AddChild(property);
        }
    }
}
