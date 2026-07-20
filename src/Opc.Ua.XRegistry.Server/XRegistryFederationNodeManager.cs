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

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Opc.Ua.Server;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Serves the xRegistry federation model (Annex B, §4.3): a resource hosted by another registry
    /// is represented locally by a proxy carrying an <c>ExternalReference</c> (an
    /// <see cref="ExpandedNodeId"/> whose <c>ServerIndex</c> names the remote OPC UA server via the
    /// <c>ServerArray</c>, and whose <c>NamespaceUri</c> + <c>Identifier</c> are the remote resource
    /// node's identity) and/or a <c>ResourceUrl</c>. Because a resource's identity (its content-id)
    /// is content-derived and therefore stable across registries, the same resource federated from
    /// several endpoints keeps <b>one</b> identity and can be de-duplicated by content-id.
    /// </summary>
    [Experimental("UA_NETStandard_Encoders")]
    public class XRegistryFederationNodeManager : CustomNodeManager2
    {
        private readonly string m_namespaceUri;
        private readonly IResourceContentIdProvider? m_contentIdProvider;
        private readonly bool m_publishProxy;
        private readonly byte[]? m_federatedDocument;
        private readonly string m_federatedFormat;
        private readonly string m_remoteRegistryNamespaceUri;
        private readonly string m_remoteEndpointUrl;
        private readonly uint m_remoteServerIndex;
        private readonly string m_proxyBrowseName;

        /// <summary>
        /// Initializes the federation node manager for the registry namespace.
        /// </summary>
        /// <param name="server">The server that owns the node manager.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="options">The registry server options.</param>
        public XRegistryFederationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            XRegistryServerOptions options)
            : base(server, configuration, (options ?? new XRegistryServerOptions()).RegistryNamespaceUri)
        {
            XRegistryServerOptions opts = options ?? new XRegistryServerOptions();
            m_namespaceUri = opts.RegistryNamespaceUri;
            m_contentIdProvider = opts.ContentIdProvider;
            m_publishProxy = opts.PublishFederationProxy;
            m_federatedDocument = opts.FederatedDocument;
            m_federatedFormat = opts.FederatedFormat;
            m_remoteRegistryNamespaceUri = opts.RemoteRegistryNamespaceUri;
            m_remoteEndpointUrl = opts.RemoteEndpointUrl;
            m_remoteServerIndex = opts.RemoteServerIndex;
            m_proxyBrowseName = opts.FederationProxyBrowseName;
        }

        /// <summary>
        /// Materializes the federated resource proxy with its <c>ExternalReference</c>,
        /// <c>ResourceUrl</c> and content-id metadata.
        /// </summary>
        /// <param name="externalReferences">External reference sink (unused).</param>
        public override void CreateAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            base.CreateAddressSpace(externalReferences);

            if (!m_publishProxy || m_federatedDocument is null)
            {
                return;
            }

            if (m_contentIdProvider is null)
            {
                throw new InvalidOperationException(
                    "A ContentIdProvider is required to publish a federation proxy.");
            }

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
            ByteString contentId = m_contentIdProvider.ComputeContentId(m_federatedFormat, m_federatedDocument);

            var proxy = new BaseObjectState(null)
            {
                NodeId = new NodeId(XRegistryWellKnown.FederationProxyObject, ns),
                BrowseName = new QualifiedName(m_proxyBrowseName, ns),
                DisplayName = new LocalizedText(m_proxyBrowseName),
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };

            // The federation link: ServerIndex -> remote ServerUri (via ServerArray),
            // NamespaceUri + Identifier -> the remote resource node (content-addressed by content-id).
            var externalReference = new ExpandedNodeId(
                contentId, m_remoteRegistryNamespaceUri, m_remoteServerIndex);

            AddProperty(proxy, XRegistryWellKnown.FederationExternalReferenceProperty, ns,
                "ExternalReference", DataTypeIds.ExpandedNodeId, new Variant(externalReference));
            AddProperty(proxy, XRegistryWellKnown.FederationResourceUrlProperty, ns,
                "ResourceUrl", DataTypeIds.String, new Variant(m_remoteEndpointUrl));
            AddProperty(proxy, XRegistryWellKnown.FederationContentIdProperty, ns,
                "SchemaId", DataTypeIds.ByteString, new Variant(contentId));

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
