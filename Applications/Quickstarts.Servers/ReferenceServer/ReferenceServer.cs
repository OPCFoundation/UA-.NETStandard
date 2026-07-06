/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Gds.Server;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// Implements the Quickstart Reference Server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each server instance must have one instance of a StandardServer object which is
    /// responsible for reading the configuration file, creating the endpoints and dispatching
    /// incoming requests to the appropriate handler.
    /// </para>
    /// <para>
    /// This sub-class specifies non-configurable metadata such as Product Name and initializes
    /// the EmptyNodeManager which provides access to the data exposed by the Server.
    /// </para>
    /// </remarks>
    public partial class ReferenceServer : ReverseConnectServer
    {
        /// <summary>
        /// Create reference server
        /// </summary>
        public ReferenceServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
            m_userDatabase = new LinqUserDatabase();
            m_userDatabase.CreateUser("sysadmin", "demo"u8, [Role.SecurityAdmin, Role.AuthenticatedUser]);
            m_userDatabase.CreateUser("user1", "password"u8, [Role.AuthenticatedUser]);
            m_userDatabase.CreateUser("user2", "password1"u8, [Role.AuthenticatedUser]);
            m_userDatabase.CreateUser(
                   "SystemAdmin",
                   Encoding.UTF8.GetBytes("demo"),
                   [GdsRole.CertificateAuthorityAdmin, GdsRole.DiscoveryAdmin, Role.SecurityAdmin,
                       Role.ConfigureAdmin, Role.AuthenticatedUser]);
            m_userDatabase.CreateUser(
                "AppAdmin",
                Encoding.UTF8.GetBytes("demo"),
                [Role.AuthenticatedUser, GdsRole.CertificateAuthorityAdmin,
                    GdsRole.DiscoveryAdmin, Role.AuthenticatedUser]);
            m_userDatabase.CreateUser(
                "DiscoveryAdmin",
                Encoding.UTF8.GetBytes("demo"),
                [Role.AuthenticatedUser, GdsRole.DiscoveryAdmin, Role.AuthenticatedUser]);
            m_userDatabase.CreateUser(
                "CertificateAuthorityAdmin",
                Encoding.UTF8.GetBytes("demo"),
                [Role.AuthenticatedUser, GdsRole.CertificateAuthorityAdmin, Role.AuthenticatedUser]);

            m_userManagement = new UserManagement(m_userDatabase, new Opc.Ua.Range(256, 1));
        }

        /// <summary>
        /// Token validator
        /// </summary>
        public ITokenValidator? TokenValidator { get; set; }

        /// <summary>
        /// If true the ReferenceNodeManager is set to work with a sampling group mechanism
        /// for managing monitored items instead of a Monitored Node mechanism
        /// </summary>
        public bool UseSamplingGroupsInReferenceNodeManager { get; set; }

        /// <summary>
        /// If true, the server starts in provisioning mode with limited namespace
        /// and requires authenticated user access for certificate provisioning
        /// </summary>
        public bool ProvisioningMode { get; set; }

        /// <summary>
        /// The user database used for credential verification and user management.
        /// </summary>
        public IUserDatabase UserDatabase => m_userDatabase;

        /// <summary>
        /// If true, the server creates the FileSystem node manager that
        /// exposes the configured <see cref="FileSystemProvider"/> under
        /// the standard <c>Server.FileSystem</c> object (<c>i=16314</c>).
        /// This materially grows the address space — only enable it in
        /// tests / hosts that exercise FileSystem (Part 20). Default is
        /// <c>false</c> so the standard test fixtures keep a small,
        /// browse-friendly address space.
        /// </summary>
        public bool EnableFileSystemNodeManager { get; set; }

        /// <summary>
        /// Provider that backs the FileSystem node manager when
        /// <see cref="EnableFileSystemNodeManager"/> is <c>true</c>.
        /// When <c>null</c>, the server falls back to a
        /// <see cref="Opc.Ua.Server.FileSystem.PhysicalFileSystemProvider"/>
        /// rooted at <c>%TEMP%/OpcUaReferenceServerFs</c>.
        /// </summary>
        public Opc.Ua.Server.FileSystem.IFileSystemProvider? FileSystemProvider { get; set; }

        /// <summary>
        /// Creates the node managers for the server.
        /// </summary>
        /// <remarks>
        /// This method allows the sub-class create any additional node managers which it uses. The SDK
        /// always creates a CoreNodeManager which handles the built-in nodes defined by the specification.
        /// Any additional NodeManagers are expected to handle application specific nodes.
        /// </remarks>
        protected override IMasterNodeManager CreateMasterNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            m_logger.LogInformation(
                Utils.TraceMasks.StartStop,
                "Creating the Reference Server Node Manager.");

            var nodeManagers = new List<INodeManager>();
            var asyncNodeManagers = new List<IAsyncNodeManager>();

            if (ProvisioningMode)
            {
                m_logger.LogInformation(
                    Utils.TraceMasks.StartStop,
                    "Server is in provisioning mode - limited namespace enabled.");
                nodeManagers = [];
            }
            else
            {
                ReferenceNodeManager? referenceNodeManager = null;
                try
                {
                    // CA2000: ownership-transfer pattern — nulled after handoff to asyncNodeManagers.
#pragma warning disable CA2000
                    referenceNodeManager = new ReferenceNodeManager(
                        server,
                        configuration,
                        UseSamplingGroupsInReferenceNodeManager);
#pragma warning restore CA2000
                    asyncNodeManagers = [referenceNodeManager];
                    referenceNodeManager = null;
                }
                finally
                {
                    // CA1508: only non-null on the exceptional path (try block clears it on success).
#pragma warning disable CA1508
                    referenceNodeManager?.Dispose();
#pragma warning restore CA1508
                }

                foreach (INodeManagerFactory nodeManagerFactory in NodeManagerFactories)
                {
                    nodeManagers.Add(nodeManagerFactory.Create(server, configuration));
                }

                foreach (IAsyncNodeManagerFactory nodeManagerFactory in AsyncNodeManagerFactories)
                {
                    asyncNodeManagers.Add(nodeManagerFactory.CreateAsync(server, configuration).AsTask().GetAwaiter().GetResult());
                }
            }

            if (EnableFileSystemNodeManager)
            {
                // FileSystem node manager — exposes the configured
                // provider (defaults to a temp folder) under the standard
                // Server.FileSystem object (i=16314).
                Opc.Ua.Server.FileSystem.IFileSystemProvider provider =
                    FileSystemProvider ?? CreateDefaultFileSystemProvider();
                asyncNodeManagers.Add(new Opc.Ua.Server.FileSystem.FileSystemNodeManager(
                    server, configuration, provider));
            }

            // OPC UA Part 17 — AliasName provider for the reference server.
            //
            // Registers a Part 17 InMemoryAliasNameStore directly with the
            // server-wide IAliasNameStoreRegistry so the standard
            // well-known FindAlias methods on TagVariables (i=23485) and
            // Topics (i=23494) — wired by DiagnosticsNodeManager — return
            // the sample tag/topic aliases below.
            //
            // No standalone AliasNameNodeManager is needed for read-only
            // service of standard categories; apps wanting their own
            // categories with full Add/Delete support add an
            // AliasNameNodeManager configured with their own namespace.
            ConfigureAliasNameStore(server);

            ServerInternal.SetUserManagement(m_userManagement);

            return new MasterNodeManager(server, configuration, null, asyncNodeManagers, nodeManagers);
        }

        private static void ConfigureAliasNameStore(IServerInternal server)
        {
            if (server is not Opc.Ua.Server.AliasNames.IAliasNameStoreRegistryProvider provider)
            {
                return;
            }

            var tagVariables = new Opc.Ua.Server.AliasNames.AliasNameCategoryDescriptor(
                ObjectIds.TagVariables,
                QualifiedName.From(BrowseNames.TagVariables),
                Opc.Ua.Server.AliasNames.AliasNameCapabilities.FindAliasVerbose);
            var topics = new Opc.Ua.Server.AliasNames.AliasNameCategoryDescriptor(
                ObjectIds.Topics,
                QualifiedName.From(BrowseNames.Topics),
                Opc.Ua.Server.AliasNames.AliasNameCapabilities.FindAliasVerbose);

            // Root the Aliases (i=23470) object too so FindAlias /
            // FindAliasVerbose / LastChange dispatched against it
            // aggregate the TagVariables / Topics sub-categories
            // (per Part 17 §6.3.2 recursive matching semantics).
            // AddAliasesToCategory / DeleteAliasesFromCategory are enabled
            // on the in-memory store so server-side test/admin code can
            // bump LastChange via store.AddAliasesAsync / DeleteAliasesAsync.
            // The standard well-known Aliases (i=23470) node does not
            // instantiate Add/Delete method nodes (per the OPC UA NodeSet),
            // so this is a server-side capability only and does not
            // expose mutation methods over the wire on the standard node.
            var aliases = new Opc.Ua.Server.AliasNames.AliasNameCategoryDescriptor(
                ObjectIds.Aliases,
                QualifiedName.From(BrowseNames.Aliases),
                Opc.Ua.Server.AliasNames.AliasNameCapabilities.FindAliasVerbose |
                Opc.Ua.Server.AliasNames.AliasNameCapabilities.LastChange |
                Opc.Ua.Server.AliasNames.AliasNameCapabilities.AddAliasesToCategory |
                Opc.Ua.Server.AliasNames.AliasNameCapabilities.DeleteAliasesFromCategory,
                subCategories: [tagVariables, topics]);

            // CA2000: ownership transferred to the registry which disposes
            // the store along with itself.
#pragma warning disable CA2000
            var store = new Opc.Ua.Server.AliasNames.InMemoryAliasNameStore(
                [aliases]);
#pragma warning restore CA2000

            int refServerNsIndex = server.NamespaceUris.GetIndex(
                Namespaces.ReferenceServer);
            ushort refServerNs = refServerNsIndex >= 0
                ? (ushort)refServerNsIndex
                : ushort.MaxValue;

            NodeId aliasFor = ReferenceTypeIds.AliasFor;

            if (refServerNs != ushort.MaxValue)
            {
                SeedTag(store, "TIC101_Setpoint",
                    new ExpandedNodeId("Scalar_Static_Double", refServerNs));
                SeedTag(store, "TIC101_PV",
                    new ExpandedNodeId("Scalar_Static_Float", refServerNs));
                SeedTag(store, "FIC202_Flow",
                    new ExpandedNodeId("Scalar_Simulation_Double", refServerNs));
                SeedTag(store, "Pump1_Status",
                    new ExpandedNodeId("Scalar_Static_Boolean", refServerNs));
                SeedTag(store, "Heater_Power",
                    new ExpandedNodeId("Scalar_Static_Int32", refServerNs));
                SeedTag(store, "MultiRefAlias",
                    new ExpandedNodeId("Scalar_Static_Double", refServerNs));
                SeedTag(store, "MultiRefAlias",
                    new ExpandedNodeId("Scalar_Static_Int32", refServerNs));
            }

            store.Seed(ObjectIds.Topics, "ServerEvents",
                ObjectIds.Server, serverUri: null, referenceTypeId: aliasFor);
            store.Seed(ObjectIds.Topics, "AuditEvents",
                new ExpandedNodeId(ObjectTypes.AuditEventType),
                serverUri: null, referenceTypeId: aliasFor);

            provider.AliasNameStoreRegistry.Register(store);

            static void SeedTag(
                Opc.Ua.Server.AliasNames.InMemoryAliasNameStore store,
                string name,
                ExpandedNodeId target)
            {
                store.Seed(ObjectIds.TagVariables, name, target,
                    serverUri: null,
                    referenceTypeId: ReferenceTypeIds.AliasFor);
            }
        }

        /// <summary>
        /// Overrides the SDK default factory to plug in a
        /// reference-server-specific <see cref="ConfigurationNodeManager"/>
        /// that applies a few CTT-only address-space tweaks (Server-node
        /// RolePermissions, HasAddIn instance, optional EngineeringUnits
        /// on AnalogItemType). Keeping these out of the SDK avoids
        /// polluting the standard nodeset for non-CTT hosts.
        /// </summary>
        protected override IMainNodeManagerFactory CreateMainNodeManagerFactory(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return new ReferenceServerMainNodeManagerFactory(configuration, server);
        }

        /// <summary>
        /// Returns a default <see cref="Opc.Ua.Server.FileSystem.PhysicalFileSystemProvider"/>
        /// rooted at a per-process temp folder. Override
        /// <see cref="FileSystemProvider"/> to mount a different backend.
        /// </summary>
        private static Opc.Ua.Server.FileSystem.PhysicalFileSystemProvider CreateDefaultFileSystemProvider()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "OpcUaReferenceServerFs");
            return new Opc.Ua.Server.FileSystem.PhysicalFileSystemProvider(
                root,
                mountName: "Temp");
        }

        protected override IMonitoredItemQueueFactory CreateMonitoredItemQueueFactory(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            if (configuration?.ServerConfiguration?.DurableSubscriptionsEnabled == true)
            {
                return new Servers.DurableMonitoredItemQueueFactory(server.Telemetry, server.MessageContext);
            }
            return new MonitoredItemQueueFactory(server.Telemetry);
        }

        /// <summary>
        /// Creates the subscriptionStore for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns a subscriptionStore for a server, the return type is <seealso cref="ISubscriptionStore"/>.</returns>
        protected override ISubscriptionStore? CreateSubscriptionStore(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            if (configuration?.ServerConfiguration?.DurableSubscriptionsEnabled == true)
            {
                return new Servers.SubscriptionStore(server);
            }
            return null;
        }

        /// <summary>
        /// Loads the non-configurable properties for the application.
        /// </summary>
        /// <remarks>
        /// These properties are exposed by the server but cannot be changed by administrators.
        /// </remarks>
        protected override ServerProperties LoadServerProperties()
        {
            return new ServerProperties
            {
                ManufacturerName = "OPC Foundation",
                ProductName = "Quickstart Reference Server",
                ProductUri = "http://opcfoundation.org/Quickstart/ReferenceServer/v1.04",
                SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
                BuildNumber = Utils.GetAssemblyBuildNumber(),
                BuildDate = Utils.GetAssemblyTimestamp()
            };
        }

        /// <summary>
        /// Creates the resource manager for the server.
        /// </summary>
        protected override ResourceManager CreateResourceManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            var resourceManager = new ResourceManager(configuration);

            foreach (StatusCode id in StatusCode.InternedStatusCodes)
            {
                if (id.SymbolicId is { } symbolicId)
                {
                    resourceManager.Add(symbolicId, "en-US", symbolicId);
                }
            }

            return resourceManager;
        }

        /// <summary>
        /// Initializes the server before it starts up.
        /// </summary>
        /// <remarks>
        /// This method is called before any startup processing occurs. The sub-class may update the
        /// configuration object or do any other application specific startup tasks.
        /// </remarks>
        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            base.OnServerStarting(configuration);

            m_logger.LogInformation(Utils.TraceMasks.StartStop, "The server is starting.");

            // it is up to the application to decide how to validate user identity tokens.
            // this function creates validator for X509 identity tokens.
            CreateUserIdentityValidators(configuration);
        }

        /// <summary>
        /// Called after the server has been started.
        /// </summary>
        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);

            RegisterIdentityAuthenticators(server);

            PublishConformanceUnits(server);

            try
            {
                ServerInternal.UpdateServerStatus(
                    status =>
                        // allow a faster sampling interval for CurrentTime node.
                        status.Variable!.CurrentTime!.MinimumSamplingInterval = 250);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Publishes the conformance units the server supports on
        /// Server/ServerCapabilities/ConformanceUnits. The list mirrors the
        /// required conformance units of the profiles declared in
        /// ServerProfileArray (per OPC UA Part 7).
        /// </summary>
        private static void PublishConformanceUnits(IServerInternal server)
        {
            BaseVariableState? conformanceUnits = server.DiagnosticsNodeManager
                .FindPredefinedNode<BaseVariableState>(
                    VariableIds.Server_ServerCapabilities_ConformanceUnits);

            if (conformanceUnits != null)
            {
                conformanceUnits.Value = Variant.From(s_conformanceUnits);
                conformanceUnits.ClearChangeMasks(server.DefaultSystemContext, false);
            }
        }

        /// <summary>
        /// The required conformance units of the profiles declared in
        /// ServerProfileArray (StandardUA2022, DataAccess, Methods2022,
        /// ReverseConnect and ClientRedundancy). Sourced from the OPC UA
        /// profile registry (UACore 1.05 ProfileSet).
        /// </summary>
        private static readonly ArrayOf<QualifiedName> s_conformanceUnits = new QualifiedName[]
        {
            new("Address Space Atomicity"),
            new("Address Space Base"),
            new("Address Space Full Array Only"),
            new("Address Space Method"),
            new("Attribute Read"),
            new("Base Info Base Types"),
            new("Base Info Core Structure 2"),
            new("Base Info Core Types Folders"),
            new("Base Info Date DataTypes"),
            new("Base Info Decimal DataType"),
            new("Base Info GetMonitoredItems Method"),
            new("Base Info Method Argument DataType"),
            new("Base Info Method Capabilities"),
            new("Base Info ResendData Method"),
            new("Base Info SemanticChange Bit"),
            new("Base Info Server Capabilities 2"),
            new("Base Info Server Capabilities MaxMonitoredItemsQueueSize"),
            new("Base Info Server Capabilities Subscriptions"),
            new("Base Info ServerType"),
            new("Base Info Type Information"),
            new("Data Access DataItems"),
            new("Discovery Find Servers Self"),
            new("Discovery Get Endpoints"),
            new("Discovery Register"),
            new("Discovery Register2"),
            new("Documentation - Core Capacities"),
            new("Method Call"),
            new("Monitor Basic"),
            new("Monitor Items 2"),
            new("Monitor Queueing"),
            new("Monitor Triggering"),
            new("Monitor Value Change V2"),
            new("Monitored Items Deadband Filter"),
            new("Protocol Reverse Connect Server"),
            new("Protocol UA TCP"),
            new("Push Model for Global Certificate and TrustList Management"),
            new("Security Default ApplicationInstance Certificate"),
            new("Security ECC Policy"),
            new("Security Invalid user token"),
            new("Security Policy Required"),
            new("Security User Name Password 2"),
            new("Security User X509"),
            new("SecurityPolicy Support"),
            new("Session Base"),
            new("Session Cancel"),
            new("Session General Service Behaviour"),
            new("Session Multiple"),
            new("Subscription Basic"),
            new("Subscription Multiple"),
            new("Subscription Publish Basic"),
            new("Subscription PublishRequest Queue Overflow"),
            new("Subscription Retransmission Queue"),
            new("Subscription Transfer"),
            new("Time Sync - Support"),
            new("UA Binary Encoding"),
            new("UA Secure Conversation"),
            new("View Basic 2"),
            new("View RegisterNodes"),
            new("View TranslateBrowsePath")
        }.ToArrayOf();

        /// <summary>
        /// Override some of the default user token policies for some endpoints.
        /// </summary>
        /// <remarks>
        /// Sample to show how to override default user token policies.
        /// </remarks>
        public override ArrayOf<UserTokenPolicy> GetUserTokenPolicies(
            ApplicationConfiguration configuration,
            EndpointDescription description)
        {
            ArrayOf<UserTokenPolicy> policies = base.GetUserTokenPolicies(
                configuration,
                description);

            // In provisioning mode, remove anonymous authentication
            if (ProvisioningMode)
            {
                return policies.Filter(u => u.TokenType != UserTokenType.Anonymous);
            }

            // sample how to modify default user token policies
            if (description.SecurityPolicyUri == SecurityPolicies.Aes256_Sha256_RsaPss &&
                description.SecurityMode == MessageSecurityMode.SignAndEncrypt)
            {
                return policies.Filter(u => u.TokenType != UserTokenType.Certificate);
            }
            else if (description.SecurityPolicyUri == SecurityPolicies.Aes128_Sha256_RsaOaep &&
                description.SecurityMode == MessageSecurityMode.Sign)
            {
                return policies.Filter(u => u.TokenType != UserTokenType.Anonymous);
            }
            else if (description.SecurityPolicyUri == SecurityPolicies.Aes128_Sha256_RsaOaep &&
                description.SecurityMode == MessageSecurityMode.SignAndEncrypt)
            {
                return policies.Filter(u => u.TokenType != UserTokenType.UserName);
            }
            return policies;
        }

        /// <summary>
        /// Creates the objects used to validate the user identity tokens supported by the server.
        /// </summary>
        private void CreateUserIdentityValidators(ApplicationConfiguration configuration)
        {
            ServerConfiguration serverConfiguration = configuration.ServerConfiguration!;
            for (int ii = 0; ii < serverConfiguration.UserTokenPolicies.Count; ii++)
            {
                UserTokenPolicy policy = serverConfiguration.UserTokenPolicies[ii];

                // create a validator for a certificate token policy.
                if (policy.TokenType == UserTokenType.Certificate)
                {
                    // check if user certificate trust lists are specified in configuration.
                    if (configuration.SecurityConfiguration.TrustedUserCertificates != null &&
                        configuration.SecurityConfiguration.UserIssuerCertificates != null)
                    {
                        // The server's CertificateManager already maps
                        // TrustedUserCertificates / UserIssuerCertificates to the
                        // Users trust list during MapFromSecurityConfiguration().
                        // Use the CertificateManager directly and validate against
                        // the Users trust list per call.
                        m_userCertificateValidator = CertificateManager;
                    }
                }
            }
        }

        /// <summary>
        /// Validates the password for a username token.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private RoleBasedIdentity VerifyPassword(UserNameIdentityTokenHandler userTokenHandler)
        {
            string userName = userTokenHandler.UserName;
            byte[]? password = userTokenHandler.DecryptedPassword;
            if (string.IsNullOrEmpty(userName))
            {
                // an empty username is not accepted.
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenInvalid,
                    "Security token is not a valid username token. An empty username is not accepted.");
            }

            if (Utils.Utf8IsNullOrEmpty(password))
            {
                // an empty password is not accepted.
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenRejected,
                    "Security token is not a valid username token. An empty password is not accepted.");
            }

            if (!m_userDatabase.CheckCredentials(userName, password))
            {
                // construct translation object with default text.
                var info = new TranslationInfo(
                    "InvalidPassword",
                    "en-US",
                    "Invalid username or password.",
                    userName);

                // create an exception with a vendor defined sub-code.
                throw new ServiceResultException(
                    new ServiceResult(
                        LoadServerProperties().ProductUri,
                        new StatusCode(StatusCodes.BadUserAccessDenied.Code, "InvalidPassword"),
                        new LocalizedText(info)));
            }

            ICollection<Role> roles = m_userDatabase.GetUserRoles(userName);
            var identity = new UserIdentity(userTokenHandler);
            List<Role> effectiveRoles = roles is { Count: > 0 } ? [.. roles] : [Role.AuthenticatedUser];
            if (effectiveRoles.Contains(Role.SecurityAdmin) && !effectiveRoles.Contains(Role.ConfigureAdmin))
            {
                effectiveRoles.Add(Role.ConfigureAdmin);
            }

            return new RoleBasedIdentity(
                identity,
                effectiveRoles,
                ServerInternal.MessageContext.NamespaceUris);
        }

        /// <summary>
        /// Verifies that a certificate user token is trusted.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void VerifyX509IdentityToken(X509IdentityTokenHandler x509TokenHandler)
        {
            var wireToken = (X509IdentityToken)x509TokenHandler.Token;
            using Certificate? userCertificate = wireToken.CertificateData.IsEmpty
                ? null
                : Certificate.FromRawData(wireToken.CertificateData);
            try
            {
                if (m_userCertificateValidator != null)
                {
                    // CA2025: task awaited via GetAwaiter().GetResult(); the disposable's
                    // using scope extends past the await.
#pragma warning disable CA2025
                    Opc.Ua.CertificateValidationResult userCertResult = m_userCertificateValidator
                        .ValidateAsync(
                            userCertificate!,
                            TrustListIdentifier.Users,
                            default)
                        .GetAwaiter()
                        .GetResult();
#pragma warning restore CA2025
                    if (!userCertResult.IsValid)
                    {
                        throw new ServiceResultException(userCertResult.StatusCode);
                    }
                }
                else
                {
                    // CA2025: task awaited via GetAwaiter().GetResult(); the disposable's
                    // using scope extends past the await.
#pragma warning disable CA2025
                    Opc.Ua.CertificateValidationResult fallbackCertResult = CertificateManager!
                        .ValidateAsync(
                            userCertificate!,
                            TrustListIdentifier.Users,
                            default)
                        .GetAwaiter()
                        .GetResult();
#pragma warning restore CA2025
                    if (!fallbackCertResult.IsValid)
                    {
                        throw new ServiceResultException(fallbackCertResult.StatusCode);
                    }
                }
            }
            catch (Exception e)
            {
                TranslationInfo info;
                StatusCode result = StatusCodes.BadIdentityTokenRejected;
                if (e is ServiceResultException se &&
                    se.StatusCode == StatusCodes.BadCertificateUseNotAllowed)
                {
                    info = new TranslationInfo(
                        "InvalidCertificate",
                        "en-US",
                        "'{0}' is an invalid user certificate.",
                        userCertificate?.Subject ?? string.Empty);

                    result = StatusCodes.BadIdentityTokenInvalid;
                }
                else
                {
                    // construct translation object with default text.
                    info = new TranslationInfo(
                        "UntrustedCertificate",
                        "en-US",
                        "'{0}' is not a trusted user certificate.",
                        userCertificate?.Subject ?? string.Empty);
                }

                // create an exception with a vendor defined sub-code.
                throw new ServiceResultException(
                    new ServiceResult(
                        LoadServerProperties().ProductUri,
                        new StatusCode(result.Code, info.Key),
                        new LocalizedText(info)));
            }
        }

        private IUserIdentity? VerifyIssuedToken(IssuedIdentityTokenHandler issuedTokenHandler)
        {
            if (TokenValidator == null)
            {
                m_logger.LogWarning(Utils.TraceMasks.Security, "No TokenValidator is specified.");
                return null;
            }
            try
            {
                if (issuedTokenHandler.IssuedTokenType == IssuedTokenType.JWT)
                {
                    m_logger.LogDebug(Utils.TraceMasks.Security, "VerifyIssuedToken: ValidateToken");
                    return TokenValidator.ValidateToken(issuedTokenHandler);
                }

                return null;
            }
            catch (Exception e)
            {
                TranslationInfo info;
                StatusCode result = StatusCodes.BadIdentityTokenRejected;
                if (e is ServiceResultException se &&
                    se.StatusCode == StatusCodes.BadIdentityTokenInvalid)
                {
                    info = new TranslationInfo(
                        "IssuedTokenInvalid",
                        "en-US",
                        "token is an invalid issued token.");
                    result = StatusCodes.BadIdentityTokenInvalid;
                }
                else // Rejected
                {
                    // construct translation object with default text.
                    info = new TranslationInfo(
                        "IssuedTokenRejected",
                        "en-US",
                        "token is rejected.");
                }

                m_logger.LogWarning(
                    Utils.TraceMasks.Security,
                    "VerifyIssuedToken: Throw ServiceResultException 0x{Result:x}", result);

                throw new ServiceResultException(
                    new ServiceResult(
                        LoadServerProperties().ProductUri,
                        new StatusCode(result.Code, info.Key),
                        new LocalizedText(info)));
            }
        }

        private void RegisterIdentityAuthenticators(IServerInternal server)
        {
            server.IdentityRegistry.RegisterDefaultAuthenticators(
                VerifyUserNameIdentityAsync,
                VerifyX509IdentityAsync,
                VerifyJwtIdentityAsync);
        }

        private ValueTask<IUserIdentity> VerifyUserNameIdentityAsync(
            UserNameIdentityTokenHandler userNameToken,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            RoleBasedIdentity identity = VerifyPassword(userNameToken);
            m_logger.LogInformation(
                Utils.TraceMasks.Security,
                "Username Token Accepted: {Identity}",
                identity.DisplayName);
            return new ValueTask<IUserIdentity>(identity);
        }

        private ValueTask<IUserIdentity> VerifyX509IdentityAsync(
            X509IdentityTokenHandler x509Token,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            VerifyX509IdentityToken(x509Token);
            var identity = new RoleBasedIdentity(
                new UserIdentity(x509Token),
                [Role.AuthenticatedUser],
                ServerInternal.MessageContext.NamespaceUris);
            m_logger.LogInformation(
                Utils.TraceMasks.Security,
                "X509 Token Accepted: {Identity}",
                identity.DisplayName);
            return new ValueTask<IUserIdentity>(identity);
        }

        private ValueTask<IUserIdentity?> VerifyJwtIdentityAsync(
            IssuedIdentityTokenHandler issuedToken,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            IUserIdentity? identity = VerifyIssuedToken(issuedToken);
            return new ValueTask<IUserIdentity?>(identity == null
                ? null
                : new RoleBasedIdentity(
                    identity,
                    [Role.AuthenticatedUser],
                    ServerInternal.MessageContext.NamespaceUris));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_userManagement.Dispose();
            }

            base.Dispose(disposing);
        }

        private CertificateManager? m_userCertificateValidator;
        private readonly LinqUserDatabase m_userDatabase;
        private readonly UserManagement m_userManagement;
    }
}
