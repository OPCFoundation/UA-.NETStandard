/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Server;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Server.UserDatabase;
using System.Linq;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Implements a sample Global Discovery Server.
    /// </summary>
    /// <remarks>
    /// Each server instance must have one instance of a StandardServer object which is
    /// responsible for reading the configuration file, creating the endpoints and dispatching
    /// incoming requests to the appropriate handler.
    /// 
    /// This sub-class specifies non-configurable metadata such as Product Name and initializes
    /// the ApplicationNodeManager which provides access to the data exposed by the Global Discovery Server.
    /// 
    /// </remarks>
    public class GlobalDiscoverySampleServer : StandardServer
    {
        public GlobalDiscoverySampleServer(
            IApplicationsDatabase database,
            ICertificateRequest request,
            ICertificateGroup certificateGroup,
            IUserDatabase userDatabase,
            bool autoApprove = true,
            bool createStandardUsers = true
            )
        {
            m_database = database;
            m_request = request;
            m_certificateGroup = certificateGroup;
            m_userDatabase = userDatabase;
            m_autoApprove = autoApprove;
            m_createStandardUsers = createStandardUsers;
        }

        #region Overridden Methods
        /// <summary>
        /// Called after the server has been started.
        /// </summary>
        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);

            //ToDo delete this code in a production environment as this creates hardcoded passwords
            if (m_createStandardUsers)
            {

                RegisterDefaultUsers();
            }
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
        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            Utils.LogInfo("Creating the Node Managers.");

            List<INodeManager> nodeManagers = new List<INodeManager>
            {
                // create the custom node managers.
                new ApplicationsNodeManager(server, configuration, m_database, m_request, m_certificateGroup, m_autoApprove)
            };

            // create master node manager.
            return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
        }

        /// <summary>
        /// Loads the non-configurable properties for the application.
        /// </summary>
        /// <remarks>
        /// These properties are exposed by the server but cannot be changed by administrators.
        /// </remarks>
        protected override ServerProperties LoadServerProperties()
        {
            ServerProperties properties = new ServerProperties {
                ManufacturerName = "Some Company Inc",
                ProductName = "Global Discovery Server",
                ProductUri = "http://somecompany.com/GlobalDiscoveryServer",
                SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
                BuildNumber = Utils.GetAssemblyBuildNumber(),
                BuildDate = Utils.GetAssemblyTimestamp()
            };

            return properties;
        }

        /// <summary>
        /// This method is called at the being of the thread that processes a request.
        /// </summary>
        protected override OperationContext ValidateRequest(RequestHeader requestHeader, RequestType requestType)
        {
            OperationContext context = base.ValidateRequest(requestHeader, requestType);

            if (requestType == RequestType.Write)
            {
                // reject all writes if no user provided.
                if (context.UserIdentity.TokenType == UserTokenType.Anonymous)
                {
                    // construct translation object with default text.
                    TranslationInfo info = new TranslationInfo(
                        "NoWriteAllowed",
                        "en-US",
                        "Must provide a valid user before calling write.");

                    // create an exception with a vendor defined sub-code.
                    throw new ServiceResultException(new ServiceResult(
                        StatusCodes.BadUserAccessDenied,
                        "NoWriteAllowed",
                        Opc.Ua.Gds.Namespaces.OpcUaGds,
                        new LocalizedText(info)));
                }

                UserIdentityToken securityToken = context.UserIdentity.GetIdentityToken();

                // check for a user name token.
                UserNameIdentityToken userNameToken = securityToken as UserNameIdentityToken;
                if (userNameToken != null)
                {
                    lock (Lock)
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
            ImpersonationContext impersonationContext = null;

            lock (Lock)
            {
                if (m_contexts.TryGetValue(context.RequestId, out impersonationContext))
                {
                    m_contexts.Remove(context.RequestId);
                }
            }

            base.OnRequestComplete(context);
        }

        /// <summary>
        /// Called when a client tries to change its user identity.
        /// </summary>
        private void SessionManager_ImpersonateUser(Session session, ImpersonateEventArgs args)
        {
            // check for a user name token
            UserNameIdentityToken userNameToken = args.NewIdentity as UserNameIdentityToken;
            if (userNameToken != null)
            {
                if (VerifyPassword(userNameToken))
                {
                    IEnumerable<Role> roles = m_userDatabase.GetUserRoles(userNameToken.UserName);

                    args.Identity = new GdsRoleBasedIdentity(new UserIdentity(userNameToken), roles);
                    return;
                }
            }

            // check for x509 user token.
            X509IdentityToken x509Token = args.NewIdentity as X509IdentityToken;
            if (x509Token != null)
            {
                VerifyUserTokenCertificate(x509Token.Certificate);

                // todo: is cert listed in admin list? then 
                // role = GdsRole.ApplicationAdmin;

                Utils.LogInfo("X509 Token Accepted: {0} as {1}", args.Identity.DisplayName, Role.AuthenticatedUser);
                args.Identity = new GdsRoleBasedIdentity(new UserIdentity(x509Token), new List<Role> { Role.AuthenticatedUser });
                return;
            }

            //check if applicable for application self admin privilege
            if (session.ClientCertificate != null)
            {
                if (VerifiyApplicationRegistered(session))
                {
                    ImpersonateAsApplicationSelfAdmin(session, args);
                }
            }
        }

        /// <summary>
        /// Verifies if an Application is registered with the provided certificate at at the GDS
        /// </summary>
        /// <param name="session">the session</param>
        /// <returns></returns>
        private bool VerifiyApplicationRegistered(Session session)
        {
            X509Certificate2 applicationInstanceCertificate = session.ClientCertificate;
            bool applicationRegistered = false;

            Uri applicationUri = Utils.ParseUri(session.SessionDiagnostics.ClientDescription.ApplicationUri);
            X509Utils.DoesUrlMatchCertificate(applicationInstanceCertificate, applicationUri);

            //get access to GDS configuration section to find out ApplicationCertificatesStorePath
            GlobalDiscoveryServerConfiguration configuration = Configuration.ParseExtension<GlobalDiscoveryServerConfiguration>();
            if (configuration == null)
            {
                configuration = new GlobalDiscoveryServerConfiguration();
            }
            //check if application certificate is in the Store of the GDS
            using (ICertificateStore ApplicationsStore = CertificateStoreIdentifier.OpenStore(configuration.ApplicationCertificatesStorePath))
            {
                var matchingCerts = ApplicationsStore.FindByThumbprint(applicationInstanceCertificate.Thumbprint).Result;

                if (matchingCerts.Contains(applicationInstanceCertificate))
                    applicationRegistered = true;
            }
            //check if application certificate is revoked
            using (ICertificateStore AuthoritiesStore = CertificateStoreIdentifier.OpenStore(configuration.AuthoritiesStorePath))
            {
                var crls = AuthoritiesStore.EnumerateCRLs().Result;
                foreach (X509CRL crl in crls)
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
        private void VerifyUserTokenCertificate(X509Certificate2 certificate)
        {
            try
            {
                CertificateValidator.Validate(certificate);
            }
            catch (Exception e)
            {
                TranslationInfo info;
                StatusCode result = StatusCodes.BadIdentityTokenRejected;
                ServiceResultException se = e as ServiceResultException;
                if (se != null && se.StatusCode == StatusCodes.BadCertificateUseNotAllowed)
                {
                    info = new TranslationInfo(
                        "InvalidCertificate",
                        "en-US",
                        "'{0}' is an invalid user certificate.",
                        certificate.Subject);

                    result = StatusCodes.BadIdentityTokenInvalid;
                }
                else
                {
                    // construct translation object with default text.
                    info = new TranslationInfo(
                        "UntrustedCertificate",
                        "en-US",
                        "'{0}' is not a trusted user certificate.",
                        certificate.Subject);
                }

                // create an exception with a vendor defined sub-code.
                throw new ServiceResultException(new ServiceResult(
                    result,
                    info.Key,
                    LoadServerProperties().ProductUri,
                    new LocalizedText(info)));
            }
        }

        private bool VerifyPassword(UserNameIdentityToken userNameToken)
        {
            return m_userDatabase.CheckCredentials(userNameToken.UserName, userNameToken.DecryptedPassword);
        }

        /// <summary>
        /// registers the default GDS users
        /// ToDo delete this in a production environment
        /// </summary>
        private void RegisterDefaultUsers()
        {
            m_userDatabase.CreateUser("sysadmin", "demo", new List<Role> { GdsRole.CertificateAuthorityAdmin, GdsRole.DiscoveryAdmin, Role.SecurityAdmin, Role.ConfigureAdmin });
            m_userDatabase.CreateUser("appadmin", "demo", new List<Role> { Role.AuthenticatedUser, GdsRole.CertificateAuthorityAdmin, GdsRole.DiscoveryAdmin });
            m_userDatabase.CreateUser("appuser", "demo", new List<Role> { Role.AuthenticatedUser });

            m_userDatabase.CreateUser("DiscoveryAdmin", "demo", new List<Role> { Role.AuthenticatedUser, GdsRole.DiscoveryAdmin });
            m_userDatabase.CreateUser("CertificateAuthorityAdmin", "demo", new List<Role> { Role.AuthenticatedUser, GdsRole.CertificateAuthorityAdmin });
        }

        /// <summary>
        /// Impersonates the current Session as ApplicationSelfAdmin
        /// </summary>
        /// <param name="session">the current session</param>
        /// <param name="args">the impersonateEventArgs</param>
        private void ImpersonateAsApplicationSelfAdmin(Session session, ImpersonateEventArgs args)
        {
            string applicationUri = session.SessionDiagnostics.ClientDescription.ApplicationUri;
            ApplicationRecordDataType[] application = m_database.FindApplications(applicationUri);
            if (application == null || application.Length != 1)
            {
                Utils.LogInfo("Cannot login based on ApplicationInstanceCertificate, no unique result for Application with URI: {0}", applicationUri);
                return;
            }
            NodeId applicationId = application.FirstOrDefault().ApplicationId;
            Utils.LogInfo("Application {0} accepted based on ApplicationInstanceCertificate as ApplicationSelfAdmin",
                applicationUri);
            args.Identity = new GdsRoleBasedIdentity(new UserIdentity(), new List<Role> { GdsRole.ApplicationSelfAdmin }, applicationId);
            return;
        }

        #endregion

        #region Private Fields
        private Dictionary<uint, ImpersonationContext> m_contexts = new Dictionary<uint, ImpersonationContext>();
        private IApplicationsDatabase m_database = null;
        private ICertificateRequest m_request = null;
        private ICertificateGroup m_certificateGroup = null;
        private IUserDatabase m_userDatabase = null;
        private bool m_autoApprove;
        private bool m_createStandardUsers;
        #endregion 
    }
}
