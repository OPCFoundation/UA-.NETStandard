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
        /// If true, the server creates the FileSystem node manager that
        /// exposes the host's drives/directories/files under the Server
        /// object. This materially grows the address space — only enable
        /// it in tests / hosts that exercise FileSystem (Part 20). Default
        /// is <c>false</c> so the standard test fixtures keep a small,
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
        public Opc.Ua.Server.FileSystem.IFileSystemProvider FileSystemProvider { get; set; }

        /// <summary>
        /// The user database used for credential verification and user management.
        /// </summary>
        public IUserDatabase UserDatabase => m_userDatabase;

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
                    m_referenceNodeManager = referenceNodeManager;
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

            // OPC UA Part 17 — AliasName provider for the reference server.
            // Small static registry; always created.
            var aliasNameNodeManager = new AliasNameNodeManager(server, configuration);
            nodeManagers.Add(aliasNameNodeManager);

            if (EnableFileSystemNodeManager)
            {
                // FileSystem node manager — exposes the configured
                // provider (defaults to a temp folder) under the
                // standard Server.FileSystem object (i=16314).
                Opc.Ua.Server.FileSystem.IFileSystemProvider provider =
                    FileSystemProvider ?? CreateDefaultFileSystemProvider();
                nodeManagers.Add(new Opc.Ua.Server.FileSystem.FileSystemNodeManager(
                    server, configuration, provider));
            }

            return new MasterNodeManager(server, configuration, null, asyncNodeManagers, nodeManagers);
        }

        /// <summary>
        /// Returns a default <see cref="Opc.Ua.Server.FileSystem.PhysicalFileSystemProvider"/>
        /// rooted at a per-process temp folder. Override
        /// <see cref="FileSystemProvider"/> to mount a different
        /// backend.
        /// </summary>
        private static Opc.Ua.Server.FileSystem.IFileSystemProvider CreateDefaultFileSystemProvider()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "OpcUaReferenceServerFs");
            return new Opc.Ua.Server.FileSystem.PhysicalFileSystemProvider(
                root,
                mountName: "Temp");
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

            // request notifications when the user identity is changed. all valid users are accepted by default.
            server.SessionManager.ImpersonateUser
                += new ImpersonateEventHandler(SessionManager_ImpersonateUser);

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
        /// Called when a client tries to change its user identity.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void SessionManager_ImpersonateUser(ISession session, ImpersonateEventArgs args)
        {
            // check for a user name token.

            if (args.UserIdentityTokenHandler is UserNameIdentityTokenHandler userNameToken)
            {
                args.Identity = VerifyPassword(userNameToken);

                m_logger.LogInformation(
                    Utils.TraceMasks.Security,
                    "Username Token Accepted: {Identity}",
                    args.Identity?.DisplayName);

                return;
            }

            // check for x509 user token.

            if (args.UserIdentityTokenHandler is X509IdentityTokenHandler x509Token)
            {
                VerifyX509IdentityToken(x509Token);
                // set AuthenticatedUser role for accepted certificate authentication
                args.Identity = new RoleBasedIdentity(
                    new UserIdentity(x509Token),
                    [Role.AuthenticatedUser],
                    ServerInternal.MessageContext.NamespaceUris);
                m_logger.LogInformation(
                    Utils.TraceMasks.Security,
                    "X509 Token Accepted: {Identity}",
                    args.Identity.DisplayName);

                return;
            }

            // check for issued identity token.
            if (args.UserIdentityTokenHandler is IssuedIdentityTokenHandler issuedToken)
            {
                // set AuthenticatedUser role for accepted identity token
                args.Identity = new RoleBasedIdentity(VerifyIssuedToken(issuedToken)!,
                    [Role.AuthenticatedUser],
                    ServerInternal.MessageContext.NamespaceUris);
                return;
            }

            // check for anonymous token.
            if (args.UserIdentityTokenHandler is AnonymousIdentityTokenHandler or null)
            {
                // allow anonymous authentication and set Anonymous role for this authentication
                var identity = new UserIdentity();
                args.Identity = new RoleBasedIdentity(
                    identity,
                    [Role.Anonymous],
                    ServerInternal.MessageContext.NamespaceUris);
                return;
            }

            // unsupported identity token type.
            throw ServiceResultException.Create(
                StatusCodes.BadIdentityTokenInvalid,
                "Not supported user token type: {0}.",
                args.UserIdentityTokenHandler.TokenType);
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
            try
            {
                if (roles != null && roles.Contains(Role.SecurityAdmin))
                {
                    return new SystemConfigurationIdentity(identity);
                }

                return new RoleBasedIdentity(
                    identity,
                    roles ?? [Role.AuthenticatedUser],
                    ServerInternal.MessageContext.NamespaceUris);
            }
            catch
            {
                // UserIdentity is no longer IDisposable; nothing to release.
                throw;
            }
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

        private CertificateManager? m_userCertificateValidator;
        private readonly LinqUserDatabase m_userDatabase;
        private ReferenceNodeManager? m_referenceNodeManager;
    }
}
