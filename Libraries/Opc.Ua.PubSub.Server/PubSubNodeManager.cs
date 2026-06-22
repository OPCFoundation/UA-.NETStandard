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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.PubSub.Server.Internal;
using Opc.Ua.Server;

namespace Opc.Ua.PubSub.Server
{
    /// <summary>
    /// Mounts behaviour onto the standard <c>PublishSubscribe</c>
    /// Object (NodeId <c>i=14443</c>) loaded by the hosting server's
    /// <c>DiagnosticsNodeManager</c>: binds the
    /// <c>Status.Enable</c> / <c>Status.Disable</c> methods, the
    /// <c>AddConnection</c> / <c>RemoveConnection</c> methods, the
    /// <c>SecurityGroups</c> management methods, and the
    /// <c>GetSecurityKeys</c> SKS entry-point.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1">
    /// Part 14 §9.1 PublishSubscribe Object</see>. This manager does
    /// not own any nodes itself; the standard PublishSubscribe
    /// sub-tree is loaded by the server core from
    /// <c>Opc.Ua.NodeSet.xml</c>. The manager registers a vendor
    /// PubSub-server namespace so it has a distinct identity in
    /// <see cref="IServerInternal.NamespaceUris"/> but contains no
    /// predefined nodes.
    /// </remarks>
    public sealed class PubSubNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Vendor namespace URI registered by the PubSub server
        /// manager. The URI is added to
        /// <see cref="IServerInternal.NamespaceUris"/> so clients
        /// can discover that the OPC UA Server hosts a PubSub
        /// runtime.
        /// </summary>
        public const string NamespaceUri = "http://opcfoundation.org/UA/PubSub/Server";

        private const uint StatusEnableNodeId = 17407;
        private const uint StatusDisableNodeId = 17408;
        private const uint SetSecurityKeysNodeId = 17364;
        private const uint AddConnectionNodeId = 17366;
        private const uint RemoveConnectionNodeId = 17369;
        private const uint GetSecurityKeysNodeId = 15215;
        private const uint GetSecurityGroupNodeId = 15440;
        private const uint AddSecurityGroupNodeId = 15444;
        private const uint RemoveSecurityGroupNodeId = 15447;

        private readonly IPubSubApplication m_application;
        private readonly IPubSubKeyServiceServer? m_keyService;
        private readonly PubSubServerOptions m_options;
        private readonly ITelemetryContext m_telemetry;
        private readonly PubSubMethodHandlers m_methodHandlers;
        private readonly PubSubActionMethodRegistration[] m_actionMethodRegistrations;
        private PubSubStatusBinding? m_statusBinding;
        private bool m_methodsBound;

        /// <summary>
        /// Creates a new <see cref="PubSubNodeManager"/>.
        /// </summary>
        /// <param name="server">Hosting server.</param>
        /// <param name="configuration">Application configuration.</param>
        /// <param name="pubSubApplication">Runtime application.</param>
        /// <param name="sksServer">
        /// Optional SKS server. When non-<see langword="null"/> and
        /// <see cref="PubSubServerOptions.ExposeSecurityKeyService"/>
        /// is set, the SKS methods are bound.
        /// </param>
        /// <param name="options">Server options.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="actionMethodRegistrations">Optional PublishedActionMethod bindings.</param>
        /// <param name="pushKeyProviders">Optional SetSecurityKeys push providers.</param>
        public PubSubNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IPubSubApplication pubSubApplication,
            IPubSubKeyServiceServer? sksServer,
            PubSubServerOptions options,
            ITelemetryContext telemetry,
            IEnumerable<PubSubActionMethodRegistration>? actionMethodRegistrations = null,
            IEnumerable<PushSecurityKeyProvider>? pushKeyProviders = null)
            : base(
                  server,
                  configuration,
                  (telemetry ?? throw new ArgumentNullException(nameof(telemetry)))
                      .CreateLogger<PubSubNodeManager>(),
                  NamespaceUri)
        {
            if (pubSubApplication is null)
            {
                throw new ArgumentNullException(nameof(pubSubApplication));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            m_application = pubSubApplication;
            m_keyService = sksServer;
            m_options = options;
            m_telemetry = telemetry;
            m_actionMethodRegistrations = actionMethodRegistrations?.ToArray()
                ?? Array.Empty<PubSubActionMethodRegistration>();
            m_methodHandlers = new PubSubMethodHandlers(
                pubSubApplication,
                options.ExposeSecurityKeyService ? sksServer : null,
                options,
                telemetry,
                pushKeyProviders);
        }

        /// <summary>
        /// <see langword="true"/> once the standard PubSub method
        /// nodes have been located and bound by
        /// <see cref="CreateAddressSpaceAsync"/>. Test-only.
        /// </summary>
        internal bool AreMethodsBound => m_methodsBound;

        /// <summary>
        /// The status / diagnostics binding allocated by
        /// <see cref="CreateAddressSpaceAsync"/>; null until the
        /// address space is initialised. Test-only.
        /// </summary>
        internal PubSubStatusBinding? StatusBinding => m_statusBinding;

        /// <summary>
        /// Returns the <see cref="PubSubMethodHandlers"/> instance
        /// owned by this node manager. Test-only.
        /// </summary>
        internal PubSubMethodHandlers MethodHandlers => m_methodHandlers;

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            IDiagnosticsNodeManager? diagnosticsNodeManager = Server.DiagnosticsNodeManager;
            if (diagnosticsNodeManager is null)
            {
                m_logger.LogWarning(
                    "DiagnosticsNodeManager is not available; PubSub methods will not be bound.");
                return;
            }

            BindMethods(diagnosticsNodeManager);
            RegisterActionMethodHandlers();

            if (m_application is PubSubApplication concrete &&
                m_options.DiagnosticsExposure != PubSubDiagnosticsExposure.None)
            {
                m_statusBinding = new PubSubStatusBinding(
                    m_application,
                    concrete.Diagnostics,
                    diagnosticsNodeManager,
                    m_options.DiagnosticsExposure,
                    m_telemetry);
                m_statusBinding.Bind();
            }
            else if (m_options.DiagnosticsExposure != PubSubDiagnosticsExposure.None)
            {
                m_logger.LogDebug(
                    "IPubSubApplication implementation does not expose IPubSubDiagnostics; status binding skipped.");
            }

            if (m_options.ExposeSecurityKeyService &&
                m_keyService is not null &&
                !string.IsNullOrEmpty(m_options.DefaultSecurityGroupId))
            {
                await SeedDefaultSecurityGroupAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_statusBinding?.Dispose();
                m_statusBinding = null;
            }
            base.Dispose(disposing);
        }

        private void BindMethods(IDiagnosticsNodeManager diagnosticsNodeManager)
        {
            MethodState? enable = diagnosticsNodeManager
                .FindPredefinedNode<MethodState>(new NodeId(StatusEnableNodeId));
            MethodState? disable = diagnosticsNodeManager
                .FindPredefinedNode<MethodState>(new NodeId(StatusDisableNodeId));
            MethodState? setKeys = diagnosticsNodeManager
                .FindPredefinedNode<MethodState>(new NodeId(SetSecurityKeysNodeId));
            MethodState? addConn = diagnosticsNodeManager
                .FindPredefinedNode<MethodState>(new NodeId(AddConnectionNodeId));
            MethodState? removeConn = diagnosticsNodeManager
                .FindPredefinedNode<MethodState>(new NodeId(RemoveConnectionNodeId));

            if (enable is not null)
            {
                enable.OnCallMethod = m_methodHandlers.OnEnable;
            }
            if (disable is not null)
            {
                disable.OnCallMethod = m_methodHandlers.OnDisable;
            }
            if (m_options.ExposeConfigurationMethods)
            {
                if (setKeys is not null)
                {
                    setKeys.OnCallMethod = m_methodHandlers.OnSetSecurityKeys;
                }
                if (addConn is not null)
                {
                    addConn.OnCallMethod = m_methodHandlers.OnAddConnection;
                }
                if (removeConn is not null)
                {
                    removeConn.OnCallMethod = m_methodHandlers.OnRemoveConnection;
                }
            }

            if (m_options.ExposeSecurityKeyService && m_keyService is not null)
            {
                MethodState? getKeys = diagnosticsNodeManager
                    .FindPredefinedNode<MethodState>(new NodeId(GetSecurityKeysNodeId));
                MethodState? getGroup = diagnosticsNodeManager
                    .FindPredefinedNode<MethodState>(new NodeId(GetSecurityGroupNodeId));
                MethodState? addGroup = diagnosticsNodeManager
                    .FindPredefinedNode<MethodState>(new NodeId(AddSecurityGroupNodeId));
                MethodState? removeGroup = diagnosticsNodeManager
                    .FindPredefinedNode<MethodState>(new NodeId(RemoveSecurityGroupNodeId));
                if (getKeys is not null)
                {
                    getKeys.OnCallMethod2 = m_methodHandlers.OnGetSecurityKeys;
                }
                if (getGroup is not null)
                {
                    getGroup.OnCallMethod = m_methodHandlers.OnGetSecurityGroup;
                }
                if (addGroup is not null)
                {
                    addGroup.OnCallMethod = m_methodHandlers.OnAddSecurityGroup;
                }
                if (removeGroup is not null)
                {
                    removeGroup.OnCallMethod = m_methodHandlers.OnRemoveSecurityGroup;
                }
            }

            m_methodsBound = enable is not null || disable is not null;
        }

        private void RegisterActionMethodHandlers()
        {
            if (m_actionMethodRegistrations.Length == 0)
            {
                return;
            }

            IMasterNodeManager nodeManager = Server.NodeManager;
            for (int i = 0; i < m_actionMethodRegistrations.Length; i++)
            {
                PubSubActionMethodRegistrar.Register(
                    m_application,
                    nodeManager,
                    m_actionMethodRegistrations[i],
                    m_telemetry);
            }
        }

        private async ValueTask SeedDefaultSecurityGroupAsync(CancellationToken cancellationToken)
        {
            if (m_keyService is null || string.IsNullOrEmpty(m_options.DefaultSecurityGroupId))
            {
                return;
            }
            string id = m_options.DefaultSecurityGroupId!;
            try
            {
                SksSecurityGroup? existing = await m_keyService
                    .GetSecurityGroupAsync(id, cancellationToken)
                    .ConfigureAwait(false);
                if (existing is not null)
                {
                    return;
                }
                string policyUri = m_options.DefaultSecurityPolicyUri ?? m_methodHandlers.DefaultPolicyUri;
                var seed = new SksSecurityGroup(
                    securityGroupId: id,
                    securityPolicyUri: policyUri,
                    keyLifetime: TimeSpan.FromMilliseconds(m_options.DefaultKeyLifetimeMs),
                    maxFutureKeyCount: 4,
                    maxPastKeyCount: 4,
                    keys: Array.Empty<PubSubSecurityKey>(),
                    authorizedCallerIdentities: m_options.DefaultAuthorizedCallerIdentities ?? [],
                    rolePermissions: m_options.DefaultSecurityGroupRolePermissions ?? []);
                await m_keyService.AddSecurityGroupAsync(seed, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Seeding default SecurityGroup {Id} failed.", id);
            }
        }
    }
}
