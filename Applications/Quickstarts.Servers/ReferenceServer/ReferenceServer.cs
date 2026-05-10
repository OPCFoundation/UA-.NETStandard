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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Gds.Server;
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
    public class ReferenceServer : ReverseConnectServer
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
        public ITokenValidator TokenValidator { get; set; }

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
                var referenceNodeManager = new ReferenceNodeManager(
                    server,
                    configuration,
                    UseSamplingGroupsInReferenceNodeManager);
                try
                {
                    asyncNodeManagers = [referenceNodeManager];
                    m_referenceNodeManager = referenceNodeManager;
                    referenceNodeManager = null;
                }
                finally
                {
                    referenceNodeManager?.Dispose();
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

            // Create role management handler and node manager so that
            // role methods and properties are registered via external
            // references and visible to Browse.
            m_roleManagement = new RoleManagementHandler(server, server.Telemetry);
            var roleNodeManager = new RoleManagementNodeManager(
                server, configuration, m_roleManagement);
            nodeManagers.Add(roleNodeManager);

            // OPC UA Part 17 — AliasName provider for the reference server.
            var aliasNameNodeManager = new AliasNameNodeManager(server, configuration);
            nodeManagers.Add(aliasNameNodeManager);

            // FileSystem node manager — exposes host drives/directories/files
            // under the Server object using FileDirectoryType / FileType.
            var fileSystemNodeManager = new Quickstarts.FileSystem.FileSystemNodeManager(server, configuration);
            nodeManagers.Add(fileSystemNodeManager);

            return new MasterNodeManager(server, configuration, null, asyncNodeManagers, nodeManagers);
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
        protected override ISubscriptionStore CreateSubscriptionStore(
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
                resourceManager.Add(id.SymbolicId, "en-US", id.SymbolicId);
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

            InitializeUserDatabase();

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
                        status.Variable.CurrentTime.MinimumSamplingInterval = 250);
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
            for (int ii = 0; ii < configuration.ServerConfiguration.UserTokenPolicies.Count; ii++)
            {
                UserTokenPolicy policy = configuration.ServerConfiguration.UserTokenPolicies[ii];

                // create a validator for a certificate token policy.
                if (policy.TokenType == UserTokenType.Certificate)
                {
                    // check if user certificate trust lists are specified in configuration.
                    if (configuration.SecurityConfiguration.TrustedUserCertificates != null &&
                        configuration.SecurityConfiguration.UserIssuerCertificates != null)
                    {
                        var certificateValidator = new CertificateValidator(MessageContext.Telemetry);
                        certificateValidator.UpdateAsync(configuration.SecurityConfiguration)
                            .Wait();
                        certificateValidator.Update(
                            configuration.SecurityConfiguration.UserIssuerCertificates,
                            configuration.SecurityConfiguration.TrustedUserCertificates,
                            configuration.SecurityConfiguration.RejectedCertificateStore);

                        // set custom validator for user certificates.
                        m_userCertificateValidator = certificateValidator.GetChannelValidator();
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
                args.Identity = new RoleBasedIdentity(VerifyIssuedToken(issuedToken),
                    [Role.AuthenticatedUser],
                    ServerInternal.MessageContext.NamespaceUris);
                return;
            }

            // check for anonymous token.
            if (args.UserIdentityTokenHandler is AnonymousIdentityTokenHandler or null)
            {
                // allow anonymous authentication and set Anonymous role for this authentication
                var identity = new UserIdentity();
                try
                {
                    args.Identity = new RoleBasedIdentity(
                        identity,
                        [Role.Anonymous],
                        ServerInternal.MessageContext.NamespaceUris);
                    identity = null;
                }
                finally
                {
                    identity?.Dispose();
                }
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
        private IUserIdentity VerifyPassword(UserNameIdentityTokenHandler userTokenHandler)
        {
            string userName = userTokenHandler.UserName;
            byte[] password = userTokenHandler.DecryptedPassword;
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
                identity.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Verifies that a certificate user token is trusted.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void VerifyX509IdentityToken(X509IdentityTokenHandler x509TokenHandler)
        {
            try
            {
                if (m_userCertificateValidator != null)
                {
                    m_userCertificateValidator.ValidateAsync(
                        x509TokenHandler.Certificate,
                        default).GetAwaiter().GetResult();
                }
                else
                {
                    CertificateValidator.ValidateAsync(
                        x509TokenHandler.Certificate,
                        default).GetAwaiter().GetResult();
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
                        x509TokenHandler.Certificate.Subject);

                    result = StatusCodes.BadIdentityTokenInvalid;
                }
                else
                {
                    // construct translation object with default text.
                    info = new TranslationInfo(
                        "UntrustedCertificate",
                        "en-US",
                        "'{0}' is not a trusted user certificate.",
                        x509TokenHandler.Certificate.Subject);
                }

                // create an exception with a vendor defined sub-code.
                throw new ServiceResultException(
                    new ServiceResult(
                        LoadServerProperties().ProductUri,
                        new StatusCode(result.Code, info.Key),
                        new LocalizedText(info)));
            }
        }

        private IUserIdentity VerifyIssuedToken(IssuedIdentityTokenHandler issuedTokenHandler)
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

        /// <summary>
        /// Initializes the user database with the default demo users.
        /// </summary>
        private void InitializeUserDatabase()
        {
            m_userDatabase = new LinqUserDatabase();

            // User with permission to configure server
            m_userDatabase.CreateUser(
                "sysadmin",
                Encoding.UTF8.GetBytes("demo"),
                [Role.SecurityAdmin, Role.ConfigureAdmin, Role.AuthenticatedUser]);

            // Standard users for CTT verification
            m_userDatabase.CreateUser(
                "user1",
                Encoding.UTF8.GetBytes("password"),
                [Role.AuthenticatedUser]);

            m_userDatabase.CreateUser(
                "user2",
                Encoding.UTF8.GetBytes("password1"),
                [Role.AuthenticatedUser]);
        }

        /// <summary>
        /// Gets the role management handler for this server.
        /// </summary>
        public RoleManagementHandler RoleManagement => m_roleManagement;

        /// <summary>
        /// Implements the AddNodes service to allow conformance tests to
        /// exercise the Node Management service set against the reference
        /// server. Newly added nodes live in the writable
        /// <see cref="ReferenceNodeManager"/> namespace regardless of the
        /// parent node's namespace; an inverse reference is also added to
        /// the parent so the new node is reachable via Browse.
        /// </summary>
        public override async ValueTask<AddNodesResponse> AddNodesAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ArrayOf<AddNodesItem> nodesToAdd,
            RequestLifetime requestLifetime)
        {
            OperationContext context = await ValidateRequestAsync(
                secureChannelContext,
                requestHeader,
                RequestType.AddNodes,
                requestLifetime).ConfigureAwait(false);

            try
            {
                ValidateOperationLimits(
                    nodesToAdd,
                    ServerInternal.ServerObject.ServerCapabilities.OperationLimits
                        .MaxNodesPerNodeManagement);

                var results = new AddNodesResult[nodesToAdd.Count];
                var diagnosticInfos = new DiagnosticInfo[nodesToAdd.Count];
                bool anyDiagnostics = false;

                for (int ii = 0; ii < nodesToAdd.Count; ii++)
                {
                    (StatusCode statusCode, NodeId addedNodeId) =
                        await TryAddNodeAsync(
                            context,
                            nodesToAdd[ii],
                            requestLifetime.CancellationToken).ConfigureAwait(false);

                    results[ii] = new AddNodesResult
                    {
                        StatusCode = statusCode,
                        AddedNodeId = addedNodeId
                    };

                    if (StatusCode.IsBad(statusCode))
                    {
                        anyDiagnostics = true;
                        diagnosticInfos[ii] = new DiagnosticInfo(
                            new ServiceResult(statusCode),
                            context.DiagnosticsMask,
                            false,
                            context.StringTable,
                            m_logger);
                    }
                }

                return new AddNodesResponse
                {
                    Results = results.ToArrayOf(),
                    DiagnosticInfos = anyDiagnostics
                        ? diagnosticInfos.ToArrayOf()
                        : default,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;
                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }
                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Implements the DeleteNodes service for the reference server. Only
        /// nodes managed by the writable <see cref="ReferenceNodeManager"/>
        /// can be removed; attempts to delete nodes from other node managers
        /// (for example, the core address space) return
        /// <see cref="StatusCodes.BadUserAccessDenied"/>.
        /// </summary>
        public override async ValueTask<DeleteNodesResponse> DeleteNodesAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ArrayOf<DeleteNodesItem> nodesToDelete,
            RequestLifetime requestLifetime)
        {
            OperationContext context = await ValidateRequestAsync(
                secureChannelContext,
                requestHeader,
                RequestType.DeleteNodes,
                requestLifetime).ConfigureAwait(false);

            try
            {
                ValidateOperationLimits(
                    nodesToDelete,
                    ServerInternal.ServerObject.ServerCapabilities.OperationLimits
                        .MaxNodesPerNodeManagement);

                var results = new StatusCode[nodesToDelete.Count];
                var diagnosticInfos = new DiagnosticInfo[nodesToDelete.Count];
                bool anyDiagnostics = false;

                for (int ii = 0; ii < nodesToDelete.Count; ii++)
                {
                    StatusCode statusCode = await TryDeleteNodeAsync(
                        context,
                        nodesToDelete[ii],
                        requestLifetime.CancellationToken).ConfigureAwait(false);

                    results[ii] = statusCode;

                    if (StatusCode.IsBad(statusCode))
                    {
                        anyDiagnostics = true;
                        diagnosticInfos[ii] = new DiagnosticInfo(
                            new ServiceResult(statusCode),
                            context.DiagnosticsMask,
                            false,
                            context.StringTable,
                            m_logger);
                    }
                }

                return new DeleteNodesResponse
                {
                    Results = results.ToArrayOf(),
                    DiagnosticInfos = anyDiagnostics
                        ? diagnosticInfos.ToArrayOf()
                        : default,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;
                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }
                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Implements the AddReferences service. Forward references are added
        /// through the master node manager which dispatches the change to the
        /// node manager that owns the source node.
        /// </summary>
        public override async ValueTask<AddReferencesResponse> AddReferencesAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ArrayOf<AddReferencesItem> referencesToAdd,
            RequestLifetime requestLifetime)
        {
            OperationContext context = await ValidateRequestAsync(
                secureChannelContext,
                requestHeader,
                RequestType.AddReferences,
                requestLifetime).ConfigureAwait(false);

            try
            {
                ValidateOperationLimits(
                    referencesToAdd,
                    ServerInternal.ServerObject.ServerCapabilities.OperationLimits
                        .MaxNodesPerNodeManagement);

                var results = new StatusCode[referencesToAdd.Count];
                var diagnosticInfos = new DiagnosticInfo[referencesToAdd.Count];
                bool anyDiagnostics = false;

                for (int ii = 0; ii < referencesToAdd.Count; ii++)
                {
                    StatusCode statusCode = await TryAddReferenceAsync(
                        referencesToAdd[ii],
                        requestLifetime.CancellationToken).ConfigureAwait(false);

                    results[ii] = statusCode;

                    if (StatusCode.IsBad(statusCode))
                    {
                        anyDiagnostics = true;
                        diagnosticInfos[ii] = new DiagnosticInfo(
                            new ServiceResult(statusCode),
                            context.DiagnosticsMask,
                            false,
                            context.StringTable,
                            m_logger);
                    }
                }

                return new AddReferencesResponse
                {
                    Results = results.ToArrayOf(),
                    DiagnosticInfos = anyDiagnostics
                        ? diagnosticInfos.ToArrayOf()
                        : default,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;
                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }
                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Implements the DeleteReferences service. The change is dispatched
        /// to the node manager that owns the source node through the master
        /// node manager.
        /// </summary>
        public override async ValueTask<DeleteReferencesResponse> DeleteReferencesAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ArrayOf<DeleteReferencesItem> referencesToDelete,
            RequestLifetime requestLifetime)
        {
            OperationContext context = await ValidateRequestAsync(
                secureChannelContext,
                requestHeader,
                RequestType.DeleteReferences,
                requestLifetime).ConfigureAwait(false);

            try
            {
                ValidateOperationLimits(
                    referencesToDelete,
                    ServerInternal.ServerObject.ServerCapabilities.OperationLimits
                        .MaxNodesPerNodeManagement);

                var results = new StatusCode[referencesToDelete.Count];
                var diagnosticInfos = new DiagnosticInfo[referencesToDelete.Count];
                bool anyDiagnostics = false;

                for (int ii = 0; ii < referencesToDelete.Count; ii++)
                {
                    StatusCode statusCode = await TryDeleteReferenceAsync(
                        referencesToDelete[ii],
                        requestLifetime.CancellationToken).ConfigureAwait(false);

                    results[ii] = statusCode;

                    if (StatusCode.IsBad(statusCode))
                    {
                        anyDiagnostics = true;
                        diagnosticInfos[ii] = new DiagnosticInfo(
                            new ServiceResult(statusCode),
                            context.DiagnosticsMask,
                            false,
                            context.StringTable,
                            m_logger);
                    }
                }

                return new DeleteReferencesResponse
                {
                    Results = results.ToArrayOf(),
                    DiagnosticInfos = anyDiagnostics
                        ? diagnosticInfos.ToArrayOf()
                        : default,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;
                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }
                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Validates a single AddNodes item and creates the node in the
        /// reference node manager when the request is acceptable.
        /// </summary>
        private async ValueTask<(StatusCode statusCode, NodeId addedNodeId)> TryAddNodeAsync(
            OperationContext context,
            AddNodesItem item,
            CancellationToken cancellationToken)
        {
            ReferenceNodeManager nodeManager = m_referenceNodeManager;
            if (nodeManager == null)
            {
                return (StatusCodes.BadNotSupported, NodeId.Null);
            }

            if (item == null || item.BrowseName.IsNull)
            {
                return (StatusCodes.BadBrowseNameInvalid, NodeId.Null);
            }

            if (!IsSupportedNodeClass(item.NodeClass))
            {
                return (StatusCodes.BadNodeClassInvalid, NodeId.Null);
            }

            // Validate the parent node id and resolve it to the local server.
            NodeId parentNodeId = ExpandedNodeId.ToNodeId(
                item.ParentNodeId,
                ServerInternal.NamespaceUris);

            if (parentNodeId.IsNull)
            {
                return (StatusCodes.BadParentNodeIdInvalid, NodeId.Null);
            }

            (object parentHandle, _) = await ServerInternal.NodeManager
                .GetManagerHandleAsync(parentNodeId, cancellationToken)
                .ConfigureAwait(false);
            if (parentHandle == null)
            {
                return (StatusCodes.BadParentNodeIdInvalid, NodeId.Null);
            }

            // Validate the reference type.
            if (item.ReferenceTypeId.IsNull ||
                !ServerInternal.TypeTree.IsKnown(item.ReferenceTypeId))
            {
                return (StatusCodes.BadReferenceTypeIdInvalid, NodeId.Null);
            }

            if (!ServerInternal.TypeTree.IsTypeOf(
                item.ReferenceTypeId,
                ReferenceTypeIds.HierarchicalReferences))
            {
                return (StatusCodes.BadReferenceNotAllowed, NodeId.Null);
            }

            // Reject client-provided NodeIds — the server assigns NodeIds.
            if (!item.RequestedNewNodeId.IsNull)
            {
                return (StatusCodes.BadNodeIdRejected, NodeId.Null);
            }

            // Validate the type definition.
            NodeId typeDefinitionId = ExpandedNodeId.ToNodeId(
                item.TypeDefinition,
                ServerInternal.NamespaceUris);

            BaseInstanceState instance;
            try
            {
                instance = CreateInstanceFromAddNodesItem(item, typeDefinitionId);
            }
            catch (ServiceResultException ex)
            {
                return (ex.StatusCode, NodeId.Null);
            }

            // Detect duplicate browse names under the same parent before adding.
            if (await BrowseNameExistsUnderParentAsync(
                    context,
                    parentNodeId,
                    item.BrowseName,
                    item.ReferenceTypeId,
                    cancellationToken).ConfigureAwait(false))
            {
                instance.Dispose();
                return (StatusCodes.BadBrowseNameDuplicated, NodeId.Null);
            }

            try
            {
                NodeId addedNodeId = await nodeManager.AddInstanceNodeAsync(
                    new ServerSystemContext(ServerInternal, context),
                    parentNodeId,
                    item.ReferenceTypeId,
                    instance,
                    cancellationToken).ConfigureAwait(false);

                return (StatusCodes.Good, addedNodeId);
            }
            catch (ServiceResultException ex)
            {
                instance.Dispose();
                return (ex.StatusCode, NodeId.Null);
            }
        }

        /// <summary>
        /// Validates a single DeleteNodes item and removes the node when it
        /// is owned by the reference node manager.
        /// </summary>
        private async ValueTask<StatusCode> TryDeleteNodeAsync(
            OperationContext context,
            DeleteNodesItem item,
            CancellationToken cancellationToken)
        {
            if (item == null || item.NodeId.IsNull)
            {
                return StatusCodes.BadNodeIdInvalid;
            }

            (object handle, IAsyncNodeManager nodeManager) = await ServerInternal
                .NodeManager.GetManagerHandleAsync(item.NodeId, cancellationToken)
                .ConfigureAwait(false);
            if (handle == null)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            // Only allow deletion in the writable reference namespace to avoid
            // breaking the core address space exposed by the SDK.
            if (nodeManager is not ReferenceNodeManager referenceNodeManager ||
                referenceNodeManager != m_referenceNodeManager)
            {
                return StatusCodes.BadUserAccessDenied;
            }

            bool removed = await referenceNodeManager.DeleteNodeAsync(
                new ServerSystemContext(ServerInternal, context),
                item.NodeId,
                cancellationToken).ConfigureAwait(false);

            return removed ? (StatusCode)StatusCodes.Good : StatusCodes.BadNodeIdUnknown;
        }

        /// <summary>
        /// Adds a single reference using the master node manager.
        /// </summary>
        private async ValueTask<StatusCode> TryAddReferenceAsync(
            AddReferencesItem item,
            CancellationToken cancellationToken)
        {
            if (item == null ||
                item.SourceNodeId.IsNull ||
                item.ReferenceTypeId.IsNull)
            {
                return StatusCodes.BadNodeIdInvalid;
            }

            if (!ServerInternal.TypeTree.IsKnown(item.ReferenceTypeId))
            {
                return StatusCodes.BadReferenceTypeIdInvalid;
            }

            (object sourceHandle, _) = await ServerInternal.NodeManager
                .GetManagerHandleAsync(item.SourceNodeId, cancellationToken)
                .ConfigureAwait(false);
            if (sourceHandle == null)
            {
                return StatusCodes.BadSourceNodeIdInvalid;
            }

            NodeId targetNodeId = ExpandedNodeId.ToNodeId(
                item.TargetNodeId,
                ServerInternal.NamespaceUris);
            if (targetNodeId.IsNull)
            {
                return StatusCodes.BadTargetNodeIdInvalid;
            }

            (object targetHandle, _) = await ServerInternal.NodeManager
                .GetManagerHandleAsync(targetNodeId, cancellationToken)
                .ConfigureAwait(false);
            if (targetHandle == null)
            {
                return StatusCodes.BadTargetNodeIdInvalid;
            }

            try
            {
                var references = new List<IReference>
                {
                    new NodeStateReference(
                        item.ReferenceTypeId,
                        !item.IsForward,
                        targetNodeId)
                };

                await ServerInternal.NodeManager.AddReferencesAsync(
                    item.SourceNodeId,
                    references,
                    cancellationToken).ConfigureAwait(false);

                return StatusCodes.Good;
            }
            catch (ServiceResultException ex)
            {
                return ex.StatusCode;
            }
        }

        /// <summary>
        /// Deletes a single reference using the master node manager.
        /// </summary>
        private async ValueTask<StatusCode> TryDeleteReferenceAsync(
            DeleteReferencesItem item,
            CancellationToken cancellationToken)
        {
            if (item == null ||
                item.SourceNodeId.IsNull ||
                item.ReferenceTypeId.IsNull)
            {
                return StatusCodes.BadNodeIdInvalid;
            }

            (object sourceHandle, IAsyncNodeManager nodeManager) = await ServerInternal
                .NodeManager.GetManagerHandleAsync(item.SourceNodeId, cancellationToken)
                .ConfigureAwait(false);
            if (sourceHandle == null)
            {
                return StatusCodes.BadSourceNodeIdInvalid;
            }

            try
            {
                ServiceResult result = await nodeManager.DeleteReferenceAsync(
                    sourceHandle,
                    item.ReferenceTypeId,
                    !item.IsForward,
                    item.TargetNodeId,
                    item.DeleteBidirectional,
                    cancellationToken).ConfigureAwait(false);

                return result == null ? StatusCodes.Good : result.StatusCode;
            }
            catch (ServiceResultException ex)
            {
                return ex.StatusCode;
            }
        }

        /// <summary>
        /// Returns true when the supplied NodeClass is one this server
        /// permits clients to add at runtime.
        /// </summary>
        private static bool IsSupportedNodeClass(NodeClass nodeClass)
        {
            return nodeClass is NodeClass.Object or NodeClass.Variable;
        }

        /// <summary>
        /// Creates the NodeState for an AddNodes request based on the
        /// requested node class and provided attributes.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// Thrown when the supplied attributes are not valid for the node
        /// class.
        /// </exception>
        private static BaseInstanceState CreateInstanceFromAddNodesItem(
            AddNodesItem item,
            NodeId typeDefinitionId)
        {
            switch (item.NodeClass)
            {
                case NodeClass.Variable:
                {
                    var variable = new BaseDataVariableState(null)
                    {
                        BrowseName = item.BrowseName,
                        DisplayName = new LocalizedText(item.BrowseName.Name),
                        TypeDefinitionId = typeDefinitionId.IsNull
                            ? VariableTypeIds.BaseDataVariableType
                            : typeDefinitionId,
                        AccessLevel = AccessLevels.CurrentReadOrWrite,
                        UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                        DataType = DataTypeIds.BaseDataType,
                        ValueRank = ValueRanks.Scalar
                    };

                    if (!item.NodeAttributes.IsNull)
                    {
                        if (!item.NodeAttributes.TryGetValue(out VariableAttributes va))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNodeAttributesInvalid);
                        }
                        ApplyVariableAttributes(variable, va);
                    }

                    return variable;
                }
                case NodeClass.Object:
                {
                    var instance = new BaseObjectState(null)
                    {
                        BrowseName = item.BrowseName,
                        DisplayName = new LocalizedText(item.BrowseName.Name),
                        TypeDefinitionId = typeDefinitionId.IsNull
                            ? ObjectTypeIds.BaseObjectType
                            : typeDefinitionId
                    };

                    if (!item.NodeAttributes.IsNull)
                    {
                        if (!item.NodeAttributes.TryGetValue(out ObjectAttributes oa))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNodeAttributesInvalid);
                        }
                        ApplyObjectAttributes(instance, oa);
                    }

                    return instance;
                }
                default:
                    throw new ServiceResultException(
                        StatusCodes.BadNodeClassInvalid);
            }
        }

        private static void ApplyVariableAttributes(
            BaseDataVariableState variable,
            VariableAttributes attributes)
        {
            uint mask = attributes.SpecifiedAttributes;
            if ((mask & (uint)NodeAttributesMask.DisplayName) != 0 &&
                !attributes.DisplayName.IsNull)
            {
                variable.DisplayName = attributes.DisplayName;
            }
            if ((mask & (uint)NodeAttributesMask.Description) != 0 &&
                !attributes.Description.IsNull)
            {
                variable.Description = attributes.Description;
            }
            if ((mask & (uint)NodeAttributesMask.DataType) != 0 &&
                !attributes.DataType.IsNull)
            {
                variable.DataType = attributes.DataType;
            }
            if ((mask & (uint)NodeAttributesMask.ValueRank) != 0)
            {
                variable.ValueRank = attributes.ValueRank;
            }
            if ((mask & (uint)NodeAttributesMask.AccessLevel) != 0)
            {
                variable.AccessLevel = attributes.AccessLevel;
            }
            if ((mask & (uint)NodeAttributesMask.UserAccessLevel) != 0)
            {
                variable.UserAccessLevel = attributes.UserAccessLevel;
            }
            if ((mask & (uint)NodeAttributesMask.Historizing) != 0)
            {
                variable.Historizing = attributes.Historizing;
            }
            if ((mask & (uint)NodeAttributesMask.MinimumSamplingInterval) != 0)
            {
                variable.MinimumSamplingInterval = attributes.MinimumSamplingInterval;
            }
            if ((mask & (uint)NodeAttributesMask.Value) != 0)
            {
                variable.Value = attributes.Value;
            }
        }

        private static void ApplyObjectAttributes(
            BaseObjectState instance,
            ObjectAttributes attributes)
        {
            uint mask = attributes.SpecifiedAttributes;
            if ((mask & (uint)NodeAttributesMask.DisplayName) != 0 &&
                !attributes.DisplayName.IsNull)
            {
                instance.DisplayName = attributes.DisplayName;
            }
            if ((mask & (uint)NodeAttributesMask.Description) != 0 &&
                !attributes.Description.IsNull)
            {
                instance.Description = attributes.Description;
            }
            if ((mask & (uint)NodeAttributesMask.EventNotifier) != 0)
            {
                instance.EventNotifier = attributes.EventNotifier;
            }
        }

        /// <summary>
        /// Returns true if a node with the requested browse name already exists
        /// directly under the parent for the given hierarchical reference type.
        /// </summary>
        private async ValueTask<bool> BrowseNameExistsUnderParentAsync(
            OperationContext context,
            NodeId parentNodeId,
            QualifiedName browseName,
            NodeId referenceTypeId,
            CancellationToken cancellationToken)
        {
            var browseDescriptions = new BrowseDescription[]
            {
                new()
                {
                    NodeId = parentNodeId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = referenceTypeId,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.BrowseName
                }
            };

            try
            {
                (ArrayOf<BrowseResult> results, _) = await ServerInternal.NodeManager
                    .BrowseAsync(
                        context,
                        null,
                        0,
                        browseDescriptions.ToArrayOf(),
                        cancellationToken).ConfigureAwait(false);

                if (results.Count == 0 || results[0].References.IsNull)
                {
                    return false;
                }

                foreach (ReferenceDescription reference in results[0].References)
                {
                    if (reference.BrowseName == browseName)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (ServiceResultException)
            {
                return false;
            }
        }

        private ICertificateValidator m_userCertificateValidator;
        private LinqUserDatabase m_userDatabase;
        private RoleManagementHandler m_roleManagement;
        private ReferenceNodeManager m_referenceNodeManager;
    }
}
