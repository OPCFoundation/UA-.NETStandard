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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.UserDatabase;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Implements a sample Global Discovery Server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each server instance must have one instance of a StandardServer object which is
    /// responsible for reading the configuration file, creating the endpoints and dispatching
    /// incoming requests to the appropriate handler.
    /// </para>
    /// <para>
    /// This sub-class specifies non-configurable metadata such as Product Name and initializes
    /// the ApplicationNodeManager which provides access to the data exposed by the Global Discovery Server.
    /// </para>
    ///
    /// </remarks>
    public class GlobalDiscoverySampleServer : StandardServer
    {
        public GlobalDiscoverySampleServer(
            IApplicationsDatabase database,
            ICertificateRequest request,
            ICertificateGroup certificateGroup,
            IUserDatabase userDatabase,
            ITelemetryContext telemetry,
            bool autoApprove = true)
            : base(telemetry)
        {
            m_database = database;
            m_request = request;
            m_certificateGroup = certificateGroup;
            m_userDatabase = userDatabase;
            m_autoApprove = autoApprove;
        }

        /// <summary>
        /// Called after the server has been started.
        /// </summary>
        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);

            // request notifications when the user identity is changed. all valid users are accepted by default.
            server.SessionManager.ImpersonateUser += SessionManager_ImpersonateUser;
        }

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
            m_logger.LogInformation("Creating the Node Managers.");

            var nodeManagers = new List<INodeManager>
            {
                // create the custom node managers.
                new ApplicationsNodeManager(
                    server,
                    configuration,
                    m_database,
                    m_request,
                    m_certificateGroup,
                    m_autoApprove)
            };

            // create master node manager.
            return new MasterNodeManager(server, configuration, null, [.. nodeManagers]);
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
                ManufacturerName = "Some Company Inc",
                ProductName = "Global Discovery Server",
                ProductUri = "http://somecompany.com/GlobalDiscoveryServer",
                SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
                BuildNumber = Utils.GetAssemblyBuildNumber(),
                BuildDate = Utils.GetAssemblyTimestamp()
            };
        }

        /// <summary>
        /// This method is called at the being of the thread that processes a request.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected override OperationContext ValidateRequest(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            RequestType requestType)
        {
            OperationContext context = base.ValidateRequest(secureChannelContext, requestHeader, requestType);

            if (requestType == RequestType.Write)
            {
                // reject all writes if no user provided.
                if (context.UserIdentity.TokenType == UserTokenType.Anonymous)
                {
                    // construct translation object with default text.
                    var info = new TranslationInfo(
                        "NoWriteAllowed",
                        "en-US",
                        "Must provide a valid user before calling write.");

                    // create an exception with a vendor defined sub-code.
                    throw new ServiceResultException(
                        new ServiceResult(
                            Namespaces.OpcUaGds,
                            new StatusCode(StatusCodes.BadUserAccessDenied.Code, "NoWriteAllowed"),
                            new LocalizedText(info)));
                }

                // check for a user name token.
                if (context.UserIdentity.TokenType == UserTokenType.UserName)
                {
                    lock (m_contextLock)
                    {
                        m_contexts.Add(context.RequestId, new ImpersonationContext());
                    }
                }
            }

            return context;
        }

        /// <summary>
        /// This method is called in a finally block at the end of request processing (i.e. called even on exception).
        /// </summary>
        protected override void OnRequestComplete(OperationContext context)
        {
            lock (m_contextLock)
            {
                if (m_contexts.TryGetValue(
                    context.RequestId,
                    out ImpersonationContext impersonationContext))
                {
                    m_contexts.Remove(context.RequestId);
                }
            }
            base.OnRequestComplete(context);
        }

        /// <summary>
        /// Called when a client tries to change its user identity.
        /// </summary>
        private void SessionManager_ImpersonateUser(ISession session, ImpersonateEventArgs args)
        {
            // check for a user name token
            if (args.UserIdentityTokenHandler is UserNameIdentityTokenHandler userNameToken &&
                VerifyPassword(userNameToken))
            {
                IEnumerable<Role> roles = m_userDatabase.GetUserRoles(userNameToken.UserName);

                args.Identity = new GdsRoleBasedIdentity(
                    new UserIdentity(userNameToken),
                    roles);
                return;
            }

            // check for x509 user token.
            if (args.UserIdentityTokenHandler is X509IdentityToken x509Token)
            {
                VerifyX509IdentityToken(x509Token);

                // todo: is cert listed in admin list? then
                // role = GdsRole.ApplicationAdmin;

                m_logger.LogInformation(
                    "X509 Token Accepted: {Identity} as {Role}",
                    args.Identity.DisplayName,
                    Role.AuthenticatedUser);
                args.Identity = new GdsRoleBasedIdentity(
                    new UserIdentity(x509Token),
                    [Role.AuthenticatedUser]);
                return;
            }

            //check if applicable for application self admin privilege
            if (session.ClientCertificate != null && VerifiyApplicationRegistered(session))
            {
                ImpersonateAsApplicationSelfAdmin(session, args);
            }
        }

        /// <summary>
        /// Verifies if an Application is registered with the provided certificate at the GDS
        /// </summary>
        /// <param name="session">the session</param>
        private bool VerifiyApplicationRegistered(ISession session)
        {
            X509Certificate2 applicationInstanceCertificate = session.ClientCertificate;
            bool applicationRegistered = false;

            Uri applicationUri = Utils.ParseUri(
                session.SessionDiagnostics.ClientDescription.ApplicationUri);
            X509Utils.DoesUrlMatchCertificate(applicationInstanceCertificate, applicationUri);

            //get access to GDS configuration section to find out ApplicationCertificatesStorePath
            GlobalDiscoveryServerConfiguration configuration =
                Configuration.ParseExtension<GlobalDiscoveryServerConfiguration>()
                ?? new GlobalDiscoveryServerConfiguration();
            //check if application certificate is in the Store of the GDS
            var certificateStoreIdentifier = new CertificateStoreIdentifier(
                configuration.ApplicationCertificatesStorePath);
            using (ICertificateStore applicationsStore = certificateStoreIdentifier.OpenStore(MessageContext.Telemetry))
            {
                X509Certificate2Collection matchingCerts = applicationsStore
                    .FindByThumbprintAsync(applicationInstanceCertificate.Thumbprint)
                    .Result;

                if (matchingCerts.Contains(applicationInstanceCertificate))
                {
                    applicationRegistered = true;
                }
            }
            //skip revocation check if application is not registered
            if (!applicationRegistered)
            {
                return false;
            }
            //check if application certificate is revoked
            certificateStoreIdentifier = new CertificateStoreIdentifier(
                configuration.AuthoritiesStorePath);
            using (ICertificateStore authoritiesStore = certificateStoreIdentifier.OpenStore(MessageContext.Telemetry))
            {
                foreach (X509CRL crl in authoritiesStore.EnumerateCRLsAsync().Result)
                {
                    if (crl.IsRevoked(applicationInstanceCertificate))
                    {
                        applicationRegistered = false;
                    }
                }
            }
            return applicationRegistered;
        }

        /// <summary>
        /// Verifies that a certificate user token is trusted.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void VerifyX509IdentityToken(X509IdentityToken token)
        {
            using var x509TokenHandler = new X509IdentityTokenHandler(token);
            try
            {
                CertificateValidator.ValidateAsync(
                    x509TokenHandler.Certificate,
                    default).GetAwaiter().GetResult();
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

        private bool VerifyPassword(UserNameIdentityTokenHandler userTokenHandler)
        {
            return m_userDatabase.CheckCredentials(
                userTokenHandler.UserName,
                userTokenHandler.DecryptedPassword);
        }

        /// <summary>
        /// Impersonates the current Session as ApplicationSelfAdmin
        /// </summary>
        /// <param name="session">the current session</param>
        /// <param name="args">the impersonateEventArgs</param>
        private void ImpersonateAsApplicationSelfAdmin(ISession session, ImpersonateEventArgs args)
        {
            string applicationUri = session.SessionDiagnostics.ClientDescription.ApplicationUri;
            ApplicationRecordDataType[] application = m_database.FindApplications(applicationUri);
            if (application == null || application.Length != 1)
            {
                m_logger.LogInformation(
                    "Cannot login based on ApplicationInstanceCertificate, no unique result for Application with URI: {ApplicationUri}",
                    applicationUri);
                return;
            }
            NodeId applicationId = application.FirstOrDefault().ApplicationId;
            m_logger.LogInformation(
                "Application {ApplicationUri} accepted based on ApplicationInstanceCertificate as ApplicationSelfAdmin",
                applicationUri);
            args.Identity = new GdsRoleBasedIdentity(
                new UserIdentity(),
                [GdsRole.ApplicationSelfAdmin],
                applicationId);
        }

        private readonly Dictionary<uint, ImpersonationContext> m_contexts = [];
        private readonly Lock m_contextLock = new();
        private readonly IApplicationsDatabase m_database;
        private readonly ICertificateRequest m_request;
        private readonly ICertificateGroup m_certificateGroup;
        private readonly IUserDatabase m_userDatabase;
        private readonly bool m_autoApprove;
    }
}
