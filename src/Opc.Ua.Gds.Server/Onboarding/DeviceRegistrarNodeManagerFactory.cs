/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Onboarding;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Loads the standard registrar model and binds its runtime, ticket administration, and managed authorities.
    /// The host also supplies the existing GDS application/certificate directory; this factory does not duplicate it.
    /// </summary>
    public sealed class DeviceRegistrarNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates a factory borrowing the host's runtime, validated ticket store, and certificate manager.
        /// </summary>
        public DeviceRegistrarNodeManagerFactory(
            IDeviceRegistrarRuntime runtime,
            ITicketStore ticketStore,
            IOnboardingTicketValidator ticketValidator,
            ICertificateManager certificateManager)
            : this(runtime, ticketStore, ticketValidator, certificateManager, null)
        {
        }

        /// <summary>
        /// Creates a factory that also binds an optional injected audit adapter to this host's restricted reporter.
        /// The four-argument construction path remains available for a host supplying its own audit routing.
        /// </summary>
        public DeviceRegistrarNodeManagerFactory(
            IDeviceRegistrarRuntime runtime,
            ITicketStore ticketStore,
            IOnboardingTicketValidator ticketValidator,
            ICertificateManager certificateManager,
            HostedDeviceRegistrarAuditReporter? hostedAudit)
            : this(runtime, ticketStore, ticketValidator, certificateManager, hostedAudit, null)
        {
        }

        /// <summary>
        /// Creates a factory that binds an optional filesystem lifecycle to this model's actual namespace table.
        /// </summary>
        public DeviceRegistrarNodeManagerFactory(
            IDeviceRegistrarRuntime runtime,
            ITicketStore ticketStore,
            IOnboardingTicketValidator ticketValidator,
            ICertificateManager certificateManager,
            HostedDeviceRegistrarAuditReporter? hostedAudit = null,
            FileSystemDeviceRegistrarLifecycle? hostedLifecycle = null)
        {
            m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            m_ticketStore = ticketStore ?? throw new ArgumentNullException(nameof(ticketStore));
            m_ticketValidator = ticketValidator ?? throw new ArgumentNullException(nameof(ticketValidator));
            m_certificateManager = certificateManager ?? throw new ArgumentNullException(nameof(certificateManager));
            m_hostedAudit = hostedAudit;
            m_hostedLifecycle = hostedLifecycle;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => [Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding];

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CA2000 // Ownership transfers to the server; TODO: teach the analyzer ValueTask ownership.
            return new ValueTask<IAsyncNodeManager>(
                new RegistrarNodeManager(
                    server, configuration, m_runtime, m_ticketStore, m_ticketValidator, m_certificateManager,
                    m_hostedAudit, m_hostedLifecycle));
#pragma warning restore CA2000
        }

        /// <summary>
        /// Registers only the onboarding namespace; the certificate directory retains ownership of GDS nodes.
        /// </summary>
        private sealed class RegistrarNodeManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Creates the registrar's asynchronous node manager without opening certificate stores.
            /// </summary>
            public RegistrarNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                IDeviceRegistrarRuntime runtime,
                ITicketStore ticketStore,
                IOnboardingTicketValidator ticketValidator,
                ICertificateManager certificateManager,
                HostedDeviceRegistrarAuditReporter? hostedAudit,
                FileSystemDeviceRegistrarLifecycle? hostedLifecycle)
                : base(server, configuration, server.Telemetry.CreateLogger<RegistrarNodeManager>())
            {
                m_runtime = runtime;
                m_ticketStore = ticketStore;
                m_ticketValidator = ticketValidator;
                m_certificateManager = certificateManager;
                m_hostedAudit = hostedAudit;
                m_hostedLifecycle = hostedLifecycle;
                NamespaceUris = [Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding];
                server.Factory.Builder.AddOpcUaGds().AddOpcUaOnboarding().Commit();
                server.MessageContext.Factory.Builder.AddOpcUaGds().AddOpcUaOnboarding().Commit();
            }

            /// <inheritdoc/>
            protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
                ISystemContext context,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_hostedLifecycle?.BindNamespaceTable(context.NamespaceUris);
                NodeStateCollection nodes = new NodeStateCollection().AddOpcUaOnboarding(context);
                DeviceRegistrarState registrar = nodes.OfType<DeviceRegistrarState>().Single();
                DeviceRegistrarAdminState administration = registrar.Administration ??
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                ConfigureAdministration(context, administration);
                administration.BindToTicketStore(m_ticketStore, m_ticketValidator);
                registrar.BindToRuntime(m_runtime);
                // Configure event ACLs even when the host uses a different audit sink.
                var audit = new DeviceRegistrarAuditReporter(Server, context, nodes.ToArray().ToArrayOf());
                m_authorities = new DeviceRegistrarAuthorityTrustLists(
                    administration, context, m_certificateManager, Server, Server.Telemetry);
                m_hostedAudit?.Attach(audit);
                m_audit = audit;
                return new ValueTask<NodeStateCollection>(nodes);
            }

            /// <inheritdoc/>
            protected override ValueTask DisposeAsyncCore()
            {
                if (m_audit != null)
                {
                    m_hostedAudit?.Detach(m_audit);
                }
                m_authorities?.Dispose();
                return base.DisposeAsyncCore();
            }

            /// <summary>
            /// Keeps ticket administration inaccessible to DCA and ordinary authenticated sessions.
            /// </summary>
            private static void ConfigureAdministration(
                ISystemContext context,
                DeviceRegistrarAdminState administration)
            {
                NodeId roleId = ExpandedNodeId.ToNodeId(
                    Opc.Ua.Onboarding.ObjectIds.WellKnownRole_RegistrarAdmin, context.NamespaceUris);
                administration.RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = roleId,
                        Permissions = (uint)(PermissionType.Browse | PermissionType.Call)
                    }
                ];
                ArrayOf<RolePermissionType> permissions =
                [
                    new RolePermissionType
                    {
                        RoleId = roleId,
                        Permissions = (uint)(PermissionType.Browse | PermissionType.Call)
                    }
                ];
                administration.RegisterTickets!.RolePermissions = permissions;
                administration.UnregisterTickets!.RolePermissions = permissions;
            }

            /// <summary>The borrowed runtime shared with certificate authorization.</summary>
            private readonly IDeviceRegistrarRuntime m_runtime;

            /// <summary>The borrowed validated ticket store.</summary>
            private readonly ITicketStore m_ticketStore;

            /// <summary>The borrowed ticket-validation policy.</summary>
            private readonly IOnboardingTicketValidator m_ticketValidator;

            /// <summary>The borrowed manager owning the authority stores.</summary>
            private readonly ICertificateManager m_certificateManager;

            /// <summary>The owned authority file handlers, disposed after dispatch drains.</summary>
            private DeviceRegistrarAuthorityTrustLists? m_authorities;

            /// <summary>The optional container-owned routing adapter.</summary>
            private readonly HostedDeviceRegistrarAuditReporter? m_hostedAudit;

            /// <summary>The optional lifecycle bound before registrar methods become available.</summary>
            private readonly FileSystemDeviceRegistrarLifecycle? m_hostedLifecycle;

            /// <summary>The restricted reporter attached while this model is active.</summary>
            private DeviceRegistrarAuditReporter? m_audit;
        }

        /// <summary>The shared registrar runtime.</summary>
        private readonly IDeviceRegistrarRuntime m_runtime;

        /// <summary>The optional lifecycle using this factory's model namespace table.</summary>
        private readonly FileSystemDeviceRegistrarLifecycle? m_hostedLifecycle;

        /// <summary>The shared ticket store.</summary>
        private readonly ITicketStore m_ticketStore;

        /// <summary>The shared ticket validator.</summary>
        private readonly IOnboardingTicketValidator m_ticketValidator;

        /// <summary>The shared managed authority provider.</summary>
        private readonly ICertificateManager m_certificateManager;

        /// <summary>The optional injected adapter awaiting model initialization.</summary>
        private readonly HostedDeviceRegistrarAuditReporter? m_hostedAudit;
    }
}
