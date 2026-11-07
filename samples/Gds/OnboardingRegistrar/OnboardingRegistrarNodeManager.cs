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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Identity;
using Opc.Ua.Onboarding;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace OnboardingRegistrar
{
    /// <summary>
    /// Creates the Part 21 onboarding registrar node manager.
    /// </summary>
    public sealed class OnboardingRegistrarNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Initializes a new factory.
        /// </summary>
        public OnboardingRegistrarNodeManagerFactory(
            ITicketStore ticketStore,
            IOnboardingTicketTrustProvider ticketTrustProvider,
            ICertificateManager certificateManager)
        {
            m_ticketStore = ticketStore ??
                throw new ArgumentNullException(nameof(ticketStore));
            m_ticketTrustProvider = ticketTrustProvider ??
                throw new ArgumentNullException(nameof(ticketTrustProvider));
            m_certificateManager = certificateManager ??
                throw new ArgumentNullException(nameof(certificateManager));
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris =>
        [
            Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding,
            Opc.Ua.Gds.Namespaces.OpcUaGds
        ];

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // Ownership is transferred to the server.
            return new ValueTask<IAsyncNodeManager>(
                new OnboardingRegistrarNodeManager(
                    server,
                    configuration,
                    m_ticketStore,
                    m_ticketTrustProvider,
                    m_certificateManager));
#pragma warning restore CA2000
        }

        private readonly ITicketStore m_ticketStore;

        /// <summary>The injected managed ticket authority policy.</summary>
        private readonly IOnboardingTicketTrustProvider m_ticketTrustProvider;

        /// <summary>The borrowed manager owns the named authority stores.</summary>
        private readonly ICertificateManager m_certificateManager;
    }

    /// <summary>
    /// Loads the generated OPC 10000-21 model and binds its standard
    /// registrar administration instance to an injected ticket store.
    /// </summary>
    public sealed class OnboardingRegistrarNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public OnboardingRegistrarNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ITicketStore ticketStore,
            IOnboardingTicketTrustProvider ticketTrustProvider,
            ICertificateManager certificateManager)
            : base(
                server,
                configuration,
                server.Telemetry.CreateLogger<OnboardingRegistrarNodeManager>())
        {
            m_ticketStore = ticketStore ??
                throw new ArgumentNullException(nameof(ticketStore));
            m_ticketValidator = new OnboardingTicketValidator(ticketTrustProvider, server.MessageContext);
            m_certificateManager = certificateManager ??
                throw new ArgumentNullException(nameof(certificateManager));
            NamespaceUris =
            [
                Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding,
                Opc.Ua.Gds.Namespaces.OpcUaGds
            ];

            Server.Factory.Builder
                .AddOpcUaGds()
                .AddOpcUaOnboarding()
                .Commit();
            Server.MessageContext.Factory.Builder
                .AddOpcUaGds()
                .AddOpcUaOnboarding()
                .Commit();
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            NodeStateCollection nodes = new NodeStateCollection()
                .AddOpcUaGds(context)
                .AddOpcUaOnboarding(context);
            DeviceRegistrarState registrar =
                nodes.OfType<DeviceRegistrarState>().Single();
            DeviceRegistrarAdminState administration = registrar.Administration ??
                throw new InvalidOperationException(
                    "The generated DeviceRegistrar has no Administration child.");
            ConfigurePermissions(context, administration);
            administration.BindToTicketStore(m_ticketStore, m_ticketValidator);
            m_authorityTrustLists = new DeviceRegistrarAuthorityTrustLists(
                administration, context, m_certificateManager, Server, Server.Telemetry);
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        protected override ValueTask DisposeAsyncCore()
        {
            m_authorityTrustLists?.Dispose();
            return base.DisposeAsyncCore();
        }

        private static void ConfigurePermissions(
            ISystemContext context,
            DeviceRegistrarAdminState administration)
        {
            NodeId roleId = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.ObjectIds.WellKnownRole_RegistrarAdmin,
                context.NamespaceUris);
            administration.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = roleId,
                    Permissions = (uint)PermissionType.Browse
                }
            ];
            ArrayOf<RolePermissionType> methodPermissions =
            [
                new RolePermissionType
                {
                    RoleId = roleId,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Call)
                }
            ];
            administration.RegisterTickets!.RolePermissions = methodPermissions;
            administration.UnregisterTickets!.RolePermissions = methodPermissions;
        }

        private readonly ITicketStore m_ticketStore;

        /// <summary>The validator bound to this server's message context.</summary>
        private readonly IOnboardingTicketValidator m_ticketValidator;

        /// <summary>The borrowed manager shares validation and administration stores.</summary>
        private readonly ICertificateManager m_certificateManager;

        /// <summary>The owned authority file handlers are released after active requests drain.</summary>
        private DeviceRegistrarAuthorityTrustLists? m_authorityTrustLists;
    }

    /// <summary>
    /// Emits the demo readiness marker after the OPC UA server reaches its
    /// running state.
    /// </summary>
    public sealed class OnboardingReadyStartupTask : IServerStartupTask
    {
        /// <summary>
        /// Initializes the startup task.
        /// </summary>
        public OnboardingReadyStartupTask(string endpoint)
        {
            m_endpoint = endpoint ??
                throw new ArgumentNullException(nameof(endpoint));
        }

        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"ONBOARDING_REGISTRAR_READY {m_endpoint}");
            return default;
        }

        private readonly string m_endpoint;
    }

    /// <summary>
    /// Grants the generated RegistrarAdmin role to the one ephemeral demo
    /// account after its username/password token has been authenticated.
    /// </summary>
    public sealed class OnboardingRegistrarAdminAugmenter : IIdentityAugmenter
    {
        /// <summary>
        /// Initializes the augmenter.
        /// </summary>
        public OnboardingRegistrarAdminAugmenter(string userName)
            : this(userName, false)
        {
        }

        /// <summary>
        /// Grants GDS administration only when a connected registrar mode explicitly selects it.
        /// </summary>
        public OnboardingRegistrarAdminAugmenter(string userName, bool gdsAdministration)
            : this(userName, gdsAdministration, false)
        {
        }

        /// <summary>
        /// Optionally lets the sample administrator report an external software manager's result.
        /// SecurityAdmin is an explicit sample role mapping, not a production software-approval policy.
        /// </summary>
        public OnboardingRegistrarAdminAugmenter(
            string userName,
            bool gdsAdministration,
            bool softwareAdministration)
        {
            m_userName = userName ??
                throw new ArgumentNullException(nameof(userName));
            m_gdsAdministration = gdsAdministration;
            m_softwareAdministration = softwareAdministration;
        }

        /// <inheritdoc/>
        public ValueTask<AuthenticationResult> AugmentAsync(
            IUserIdentity identity,
            AuthenticationContext context,
            CancellationToken ct = default)
        {
            if (context.TokenHandler is not UserNameIdentityTokenHandler userNameToken ||
                !string.Equals(
                    userNameToken.UserName,
                    m_userName,
                    StringComparison.Ordinal))
            {
                return new ValueTask<AuthenticationResult>(
                    AuthenticationResult.NotHandled);
            }
            var registrarAdmin = new Role(
                Opc.Ua.Onboarding.ObjectIds.WellKnownRole_RegistrarAdmin,
                Opc.Ua.Onboarding.BrowseNames.WellKnownRole_RegistrarAdmin);
            var augmented = new RoleBasedIdentity(
                identity,
                m_softwareAdministration
                    ? [registrarAdmin, GdsRole.DiscoveryAdmin, GdsRole.CertificateAuthorityAdmin, Role.SecurityAdmin]
                    : m_gdsAdministration
                        ? [registrarAdmin, GdsRole.DiscoveryAdmin, GdsRole.CertificateAuthorityAdmin]
                        : [registrarAdmin],
                context.MessageContext.NamespaceUris);
            return new ValueTask<AuthenticationResult>(
                AuthenticationResult.Accept(augmented));
        }

        /// <summary>The authenticated sample administrator's configured account name.</summary>
        private readonly string m_userName;

        /// <summary>Whether this account administers the sample's actual GDS directory.</summary>
        private readonly bool m_gdsAdministration;

        /// <summary>Whether the sample administrator may report an external software update result.</summary>
        private readonly bool m_softwareAdministration;
    }
}
