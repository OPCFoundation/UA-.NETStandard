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
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;

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
        /// Creates the node managers for the server.
        /// </summary>
        /// <remarks>
        /// This method allows the sub-class create any additional node managers which it uses. The SDK
        /// always creates a CoreNodeManager which handles the built-in nodes defined by the specification.
        /// Any additional NodeManagers are expected to handle application specific nodes.
        /// </remarks>
        protected override MasterNodeManager CreateMasterNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            m_logger.LogInformation(
                Utils.TraceMasks.StartStop,
                "Creating the Reference Server Node Manager.");

            IList<INodeManager> nodeManagers;
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
                nodeManagers =
                [
                    // create the custom node manager.
                    new ReferenceNodeManager(
                        server,
                        configuration,
                        UseSamplingGroupsInReferenceNodeManager)
                ];

                foreach (INodeManagerFactory nodeManagerFactory in NodeManagerFactories)
                {
                    nodeManagers.Add(nodeManagerFactory.Create(server, configuration));
                }

                foreach (IAsyncNodeManagerFactory nodeManagerFactory in AsyncNodeManagerFactories)
                {
                    asyncNodeManagers.Add(nodeManagerFactory.CreateAsync(server, configuration).AsTask().GetAwaiter().GetResult());
                }
            }

            return new MasterNodeManager(server, configuration, null, asyncNodeManagers, nodeManagers);
        }

        protected override IMonitoredItemQueueFactory CreateMonitoredItemQueueFactory(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            if (configuration?.ServerConfiguration?.DurableSubscriptionsEnabled == true)
            {
                return new Servers.DurableMonitoredItemQueueFactory(server.Telemetry);
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
        public override UserTokenPolicyCollection GetUserTokenPolicies(
            ApplicationConfiguration configuration,
            EndpointDescription description)
        {
            UserTokenPolicyCollection policies = base.GetUserTokenPolicies(
                configuration,
                description);

            // In provisioning mode, remove anonymous authentication
            if (ProvisioningMode)
            {
                return [.. policies.Where(u => u.TokenType != UserTokenType.Anonymous)];
            }

            // sample how to modify default user token policies
            if (description.SecurityPolicyUri == SecurityPolicies.Aes256_Sha256_RsaPss &&
                description.SecurityMode == MessageSecurityMode.SignAndEncrypt)
            {
                return [.. policies.Where(u => u.TokenType != UserTokenType.Certificate)];
            }
            else if (description.SecurityPolicyUri == SecurityPolicies.Aes128_Sha256_RsaOaep &&
                description.SecurityMode == MessageSecurityMode.Sign)
            {
                return [.. policies.Where(u => u.TokenType != UserTokenType.Anonymous)];
            }
            else if (description.SecurityPolicyUri == SecurityPolicies.Aes128_Sha256_RsaOaep &&
                description.SecurityMode == MessageSecurityMode.SignAndEncrypt)
            {
                return [.. policies.Where(u => u.TokenType != UserTokenType.UserName)];
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
                    [Role.AuthenticatedUser]);
                m_logger.LogInformation(
                    Utils.TraceMasks.Security,
                    "X509 Token Accepted: {Identity}",
                    args.Identity.DisplayName);

                return;
            }

            // check for issued identity token.
            if (args.UserIdentityTokenHandler is IssuedIdentityTokenHandler issuedToken)
            {
                args.Identity = VerifyIssuedToken(issuedToken);

                // set AuthenticatedUser role for accepted identity token
                args.Identity.GrantedRoleIds.Add(ObjectIds.WellKnownRole_AuthenticatedUser);

                return;
            }

            // check for anonymous token.
            if (args.UserIdentityTokenHandler is AnonymousIdentityTokenHandler or null)
            {
                // allow anonymous authentication and set Anonymous role for this authentication
                args.Identity = new RoleBasedIdentity(new UserIdentity(), [Role.Anonymous]);
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

            // User with permission to configure server
            if (userName == "sysadmin" && Utils.IsEqual(password, "demo"u8))
            {
                return new SystemConfigurationIdentity(
                    new UserIdentity(userTokenHandler));
            }

            // standard users for CTT verification
            if (!((userName == "user1" && Utils.IsEqual(password, "password"u8)) ||
                (userName == "user2" && Utils.IsEqual(password, "password1"u8))))
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
            return new RoleBasedIdentity(
                new UserIdentity(userTokenHandler),
                [Role.AuthenticatedUser]);
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

        private ICertificateValidator m_userCertificateValidator;
    }
}
