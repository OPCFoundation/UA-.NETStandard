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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Serves the xRegistry federation model (Annex B, §4.3): a resource hosted by another registry
    /// is represented locally by a proxy carrying an <c>ExternalReference</c> (an
    /// <see cref="ExpandedNodeId"/> whose <c>ServerIndex</c> names the remote OPC UA server via the
    /// <c>ServerArray</c>, and whose <c>NamespaceUri</c> + <c>Identifier</c> are the remote resource
    /// node's identity), a locator <c>ResourceUrl</c> and an immutable trusted <c>OriginRegistry</c>.
    /// The proxy retains its own structural identity. Content lookup and its lifetime remain
    /// independent of the remote logical Resource reference.
    /// </summary>
    public class XRegistryFederationNodeManager : AsyncCustomNodeManager
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
            : base(
                server,
                configuration,
                server.Telemetry.CreateLogger<XRegistryFederationNodeManager>(),
                GetNamespaceUris(options))
        {
            XRegistryServerOptions opts = options ?? new XRegistryServerOptions();
            opts.Validate();
            m_namespaceUri = opts.RegistryNamespaceUri;
            m_publishProxy = opts.PublishFederationProxy;
            m_federatedFormat = opts.FederatedFormat;
            m_remoteEndpointUrl = opts.RemoteEndpointUrl;
            m_target = opts.FederationTarget;
            m_provider = opts.FederationProvider;
            m_proxyBrowseName = opts.FederationProxyBrowseName;
            m_groupsAttributeName = opts.GroupsAttributeName;
            m_resourcesAttributeName = opts.ResourcesAttributeName;
            m_proxyGroupId = opts.FederationProxyGroupId;
            m_proxyResourceId = opts.FederationProxyResourceId;
            m_proxyVersionId = opts.FederationProxyVersionId;
        }

        /// <summary>
        /// Materializes the federated resource proxy with its <c>ExternalReference</c>,
        /// <c>ResourceUrl</c> and trusted origin metadata after provider verification.
        /// </summary>
        /// <param name="externalReferences">External reference sink (unused).</param>
        /// <param name="cancellationToken">Cancels address-space creation.</param>
        /// <exception cref="ServiceResultException">
        /// The provider cannot verify the configured logical Resource.
        /// </exception>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await m_operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await XRegistryNodeManagerStartup.RunAsync(
                    externalReferences,
                    InitializeAddressSpaceAsync,
                    DeleteProxyAsync,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_operationGate.Release();
            }
        }

        /// <inheritdoc/>
        public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            await m_operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await DeleteProxyAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_operationGate.Release();
            }
        }

        /// <summary>
        /// Updates only the authorized locator after verifying a provider against the existing
        /// immutable origin, logical Resource and Xid binding.
        /// </summary>
        /// <param name="endpointUrl">The replacement authorized locator.</param>
        /// <param name="provider">
        /// The provider for the replacement, already trusted Session or connection policy.
        /// </param>
        /// <param name="cancellationToken">Cancels verification before the locator is changed.</param>
        /// <exception cref="ServiceResultException">The proxy is unavailable or verification fails.</exception>
        /// <exception cref="ArgumentNullException">The verification provider is null.</exception>
        /// <exception cref="ArgumentException">The replacement locator is invalid.</exception>
        public async ValueTask UpdateEndpointAsync(
            string endpointUrl,
            IXRegistryFederationProvider provider,
            CancellationToken cancellationToken = default)
        {
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            cancellationToken.ThrowIfCancellationRequested();
            XRegistryFederationTarget.ValidateEndpoint(endpointUrl);
            if (!m_publishProxy)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "Federation is disabled.");
            }
            await m_operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ResourceState proxy = m_proxy ??
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The federation proxy is not published.");
                await provider.VerifyLogicalResourceAsync(m_target!, endpointUrl, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                proxy.ResourceUrl!.Value = endpointUrl;
                proxy.ResourceUrl.ClearChangeMasks(SystemContext, false);
            }
            finally
            {
                m_operationGate.Release();
            }
        }

        /// <summary>
        /// Loads the source-generated xRegistry companion model. The model is compiled into the
        /// assembly by the OPC UA model source generator, so no NodeSet2 XML is parsed at runtime.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="cancellationToken">Cancels model loading.</param>
        /// <returns>The predefined nodes of the xRegistry base model.</returns>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<NodeStateCollection>(
                new NodeStateCollection().AddOpcUaXRegistry(context));
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_operationGate.Dispose();
            }
            base.Dispose(disposing);
        }

        private async ValueTask DeleteProxyAsync(CancellationToken cancellationToken)
        {
            m_proxy = null;
            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask InitializeAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (m_publishProxy)
            {
                if (string.IsNullOrEmpty(Server.ServerUris.GetString(0)))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadServerUriInvalid, "The local ServerArray has no application identity.");
                }
                await m_provider!.VerifyLogicalResourceAsync(
                    m_target!, m_remoteEndpointUrl, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                Server.ServerUris.GetIndexOrAppend(m_target!.ServerUri);
            }
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            if (!m_publishProxy)
            {
                return;
            }

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);

            ResourceState proxy = SystemContext.CreateInstanceOfResourceType(
                parent: null!, new QualifiedName(m_proxyBrowseName, ns));
            proxy.NodeId = new NodeId(XRegistryWellKnown.FederationProxyObject, ns);
            proxy.DisplayName = new LocalizedText(m_proxyBrowseName);
            proxy.AddExternalReference(SystemContext)
                .AddOriginRegistry(SystemContext)
                .AddResourceUrl(SystemContext)
                .AddXid(SystemContext)
                .AddFormat(SystemContext)
                .AddEpoch(SystemContext)
                .AddVersionId(SystemContext);

            proxy.ExternalReference!.Value = GetExternalReference();
            proxy.ExternalReference.OnSimpleReadValue = ReadExternalReference;
            proxy.OriginRegistry!.Value = m_target!.OriginRegistry;
            proxy.OriginRegistry.AccessLevel = AccessLevels.CurrentRead;
            proxy.OriginRegistry.UserAccessLevel = AccessLevels.CurrentRead;
            proxy.ResourceUrl!.Value = m_remoteEndpointUrl;
            proxy.ResourceId!.Value = m_proxyResourceId;
            proxy.VersionId!.Value = m_proxyVersionId;
            proxy.Xid!.Value =
                $"/{m_groupsAttributeName}/{m_proxyGroupId}/" +
                $"{m_resourcesAttributeName}/{m_proxyResourceId}/versions/{m_proxyVersionId}";
            proxy.Format!.Value = m_federatedFormat;
            proxy.Epoch!.Value = 1;

            await AddPredefinedNodeAsync(SystemContext, proxy, cancellationToken)
                .ConfigureAwait(false);
            m_proxy = proxy;
        }

        private ExpandedNodeId GetExternalReference()
        {
            int index = Server.ServerUris.GetIndex(m_target!.ServerUri);
            if (index < 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadServerUriInvalid, "The trusted federation application is absent from ServerArray.");
            }
            return m_target.ResourceNodeId.WithServerIndex((uint)index);
        }

        private ServiceResult ReadExternalReference(ISystemContext context, NodeState node, ref Variant value)
        {
            value = new Variant(GetExternalReference());
            return ServiceResult.Good;
        }

        private static string[] GetNamespaceUris(XRegistryServerOptions? options)
        {
            string registryNamespace = options?.RegistryNamespaceUri ?? XRegistryWellKnown.XRegistryNamespaceUri;
            return string.Equals(registryNamespace, XRegistryWellKnown.XRegistryNamespaceUri, StringComparison.Ordinal)
                ? [registryNamespace]
                : [registryNamespace, XRegistryWellKnown.XRegistryNamespaceUri];
        }

        private readonly string m_namespaceUri;
        private readonly bool m_publishProxy;
        private readonly string m_federatedFormat;
        private readonly string m_remoteEndpointUrl;
        private readonly XRegistryFederationTarget? m_target;
        private readonly IXRegistryFederationProvider? m_provider;
        private readonly SemaphoreSlim m_operationGate = new(1, 1);
        private ResourceState? m_proxy;
        private readonly string m_proxyBrowseName;
        private readonly string m_groupsAttributeName;
        private readonly string m_resourcesAttributeName;
        private readonly string m_proxyGroupId;
        private readonly string m_proxyResourceId;
        private readonly string m_proxyVersionId;
    }
}
