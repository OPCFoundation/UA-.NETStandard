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
using Opc.Ua.Server;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Serves the xRegistry federation model (Annex B, §4.3): a resource hosted by another registry
    /// is represented locally by a proxy carrying an <c>ExternalReference</c> (an
    /// <see cref="ExpandedNodeId"/> whose <c>ServerIndex</c> names the remote OPC UA server via the
    /// <c>ServerArray</c>, and whose <c>NamespaceUri</c> + <c>Identifier</c> are the remote resource
    /// node's identity) and/or a <c>ResourceUrl</c>. The proxy retains the remote resource's
    /// structural xRegistry identity while the opaque content id remains an independent lookup
    /// target carried by <c>ExternalReference</c>.
    /// </summary>
    public class XRegistryFederationNodeManager : CustomNodeManager2
    {
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
            m_groupsAttributeName = opts.GroupsAttributeName;
            m_resourcesAttributeName = opts.ResourcesAttributeName;
            m_proxyGroupId = opts.FederationProxyGroupId;
            m_proxyResourceId = opts.FederationProxyResourceId;
            m_proxyVersionId = opts.FederationProxyVersionId;
        }

        /// <summary>
        /// Loads the source-generated xRegistry companion model. The model is compiled into the
        /// assembly by the OPC UA model source generator, so no NodeSet2 XML is parsed at runtime.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <returns>The predefined nodes of the xRegistry base model.</returns>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            return new NodeStateCollection().AddOpcUaXRegistry(context);
        }

        /// <summary>
        /// Materializes the federated resource proxy with its <c>ExternalReference</c>,
        /// <c>ResourceUrl</c> and content-id metadata.
        /// </summary>
        /// <param name="externalReferences">External reference sink (unused).</param>
        /// <exception cref="InvalidOperationException">
        /// A federation proxy is published but no
        /// <see cref="XRegistryServerOptions.ContentIdProvider"/> is configured.
        /// </exception>
        public override void CreateAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            base.CreateAddressSpace(externalReferences);

            if (!m_publishProxy || m_federatedDocument.IsNull)
            {
                return;
            }

            if (m_contentIdProvider is null)
            {
                throw new InvalidOperationException(
                    "A ContentIdProvider is required to publish a federation proxy.");
            }

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
            ByteString contentId = m_contentIdProvider.ComputeContentId(
                m_federatedFormat, m_federatedDocument.Span);

            // The proxy is a real ResourceType instance, so a generic xRegistry client drives it
            // through exactly the same proxy as a locally hosted resource.
            ResourceState proxy = SystemContext.CreateInstanceOfResourceType(
                parent: null!, new QualifiedName(m_proxyBrowseName, ns));
            proxy.NodeId = new NodeId(XRegistryWellKnown.FederationProxyObject, ns);
            proxy.DisplayName = new LocalizedText(m_proxyBrowseName);
            proxy.AddExternalReference(SystemContext);
            proxy.AddResourceUrl(SystemContext);
            proxy.AddXid(SystemContext);
            proxy.AddFormat(SystemContext);
            proxy.AddEpoch(SystemContext);
            proxy.AddVersionId(SystemContext);

            // The federation link: ServerIndex -> remote ServerUri (via ServerArray),
            // NamespaceUri + Identifier -> the remote resource node (content-addressed by content-id).
            proxy.ExternalReference!.Value = new ExpandedNodeId(
                contentId, m_remoteRegistryNamespaceUri, m_remoteServerIndex);
            proxy.ResourceUrl!.Value = m_remoteEndpointUrl;
            proxy.ResourceId!.Value = m_proxyResourceId;
            proxy.VersionId!.Value = m_proxyVersionId;
            proxy.Xid!.Value =
                $"/{m_groupsAttributeName}/{m_proxyGroupId}/" +
                $"{m_resourcesAttributeName}/{m_proxyResourceId}/versions/{m_proxyVersionId}";
            proxy.Format!.Value = m_federatedFormat;
            proxy.Epoch!.Value = 1;

            AddPredefinedNode(SystemContext, proxy);
        }

        private readonly string m_namespaceUri;
        private readonly IResourceContentIdProvider? m_contentIdProvider;
        private readonly bool m_publishProxy;
        private readonly ByteString m_federatedDocument;
        private readonly string m_federatedFormat;
        private readonly string m_remoteRegistryNamespaceUri;
        private readonly string m_remoteEndpointUrl;
        private readonly uint m_remoteServerIndex;
        private readonly string m_proxyBrowseName;
        private readonly string m_groupsAttributeName;
        private readonly string m_resourcesAttributeName;
        private readonly string m_proxyGroupId;
        private readonly string m_proxyResourceId;
        private readonly string m_proxyVersionId;
    }
}
