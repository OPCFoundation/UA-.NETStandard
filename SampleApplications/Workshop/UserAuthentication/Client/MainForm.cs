/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Drawing;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.IdentityModel.Tokens;
using System.IdentityModel.Claims;
using System.IdentityModel;
using System.IdentityModel.Selectors;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.IO;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using System.IdentityModel.Tokens.Jwt;

namespace Quickstarts.UserAuthenticationClient
{
    /// <summary>
    /// The main form for a simple Quickstart Client application.
    /// </summary>
    public partial class MainForm : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        private MainForm()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        /// <summary>
        /// Creates a form which uses the specified client configuration.
        /// </summary>
        /// <param name="configuration">The configuration to use.</param>
        public MainForm(ApplicationConfiguration configuration)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            ConnectServerCTRL.Configuration = m_configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62565/Quickstarts/UserAuthenticationServer";
            this.Text = m_configuration.ApplicationName;

            UserNameTB.Text = "Operator";
            PreferredLocalesTB.Text = "de,es,en";
            SetAvailableUserTokens(null);

            KerberosUserNameTB.Text = "Operator";
            KerberosPasswordTB.Text = "operator";
            KerberosDomainTB.Text = "GEMS";

            UserNameTokenLB.Text =
            "UserName/Password tokens can be used with any password based system including Windows.\r\n" +
            "The main disadvantage is client must trust the server with its password.\r\n" +
            "Password must be encrypted when sent to the server.";

            AnonymousTokenLB.Text =
            "Anonymous tokens mean no user is associated with the session.\r\n" +
            "It is used by servers that do not require user authentication.\r\n" +
            "It can also be used to logout while keeping a session active.";

            CertificateTokenLB.Text =
            "Certificate tokens use a X509 certicate associated with a user.\r\n" +
            "These could come from a smart card and identify a user account.\r\n" +
            "Tokens must be signed when sent to the server.";

            OAuth2TokenLB.Text =
            "OAuth2 tokens use Authorization Services that produce JSON Web Tokens (JWT).\r\n" +
            "These JWTs are passed as an Issued Token to an OPC UA Server\r\n" +
            "which uses the signature contained in the JWT to validate the token.";

            KereberosTokenLB.Text =
            "Kereberos tokens allow use of Windows domain credentials without\r\n" +
            "requiring the client to explictly enter a password.\r\n" +
            "The token must be encrypted when sent to the server.";
        }
        #endregion

        #region Private Fields
        private ApplicationConfiguration m_configuration;
        private Session m_session;
        private Subscription m_subscription;
        private MonitoredItem m_monitoredItem;
        private bool m_connectedOnce;

        // hard code for convience only valid when connecting to UserAuthenticationServer.
        private NodeId m_logFileNodeId = new NodeId(2, 2);
        #endregion

        #region Private Methods
        #endregion

        #region Event Handlers
        /// <summary>
        /// Connects to a server.
        /// </summary>
        private async void Server_ConnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ConnectServerCTRL.Connect();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Disconnects from the current session.
        /// </summary>
        private void Server_DisconnectMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Disconnect();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Prompts the user to choose a server on another host.
        /// </summary>
        private void Server_DiscoverMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Discover(null);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after connecting to or disconnecting from the server.
        /// </summary>
        private void Server_ConnectComplete(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;

                if (m_session == null)
                {
                    return;
                }

                // set a suitable initial state.
                if (m_session != null && !m_connectedOnce)
                {
                    m_connectedOnce = true;
                }

                m_session.RenewUserIdentity += new Session.RenewUserIdentityEventHandler(Session_RenewUserIdentity);

                // set the available tokens.
                SetAvailableUserTokens(m_session.ConfiguredEndpoint.Description);
                ReadLogFilePath();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after a communicate error was detected.
        /// </summary>
        private void Server_ReconnectStarting(object sender, EventArgs e)
        {
            try
            {
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after reconnecting to the server.
        /// </summary>
        private void Server_ReconnectComplete(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;

                foreach (Subscription subscription in m_session.Subscriptions)
                {
                    m_subscription = subscription;
                    break;
                }

                if (m_subscription != null)
                {
                    foreach (MonitoredItem monitoredItem in m_subscription.MonitoredItems)
                    {
                        m_monitoredItem = monitoredItem;
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Cleans up when the main form closes.
        /// </summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            ConnectServerCTRL.Disconnect();
        }
        #endregion

        #region Private Methods

        private async Task<IUserIdentity> GetOAuth2Token()
        {
            UserTokenPolicy policy = (UserTokenPolicy)OAuth2TAB.Tag;

            if (policy == null)
            {
                return null;
            }

            if (policy.TokenType == UserTokenType.IssuedToken)
            {
                if (policy.IssuedTokenType == Profiles.JwtUserToken)
                {
                    JwtEndpointParameters parameters = new JwtEndpointParameters();
                    parameters.FromJson(policy.IssuerEndpointUrl);

                    // choose a default resource id for the target server.
                    if (parameters.ResourceId == null)
                    {
                        parameters.ResourceId = m_session.Endpoint.Server.ApplicationUri;
                    }

                    // check if target server expects clients to use an OPCUA authorization service. 
                    if (parameters.AuthorityProfileUri == Profiles.OpcUaAuthorization)
                    {
                        return await GetJwtWithOpcUa(m_configuration, policy, parameters);
                    }
                }
            }

            throw new NotSupportedException();
        }

        private async Task<IUserIdentity> GetJwtWithOpcUa(ApplicationConfiguration configuration, UserTokenPolicy policy, JwtEndpointParameters parameters)
        {
            // find the client's information needed to connect the authorization service.
            var credentials = OAuth2CredentialCollection.FindByAuthorityUrl(configuration, parameters.AuthorityUrl);

            if (credentials == null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError, "No configuration specified for the selected Authority.");
            }

            // this identity is used by the client to access the authorization service.
            var requestorIdentity = new UserIdentity(credentials.ClientId, credentials.ClientSecret);

            var endpoint = CoreClientUtils.SelectEndpoint(parameters.AuthorityUrl, true);
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(configuration);
            ConfiguredEndpoint ce = new ConfiguredEndpoint(null, endpoint, endpointConfiguration);

            Session session = null;

            try
            {
                // connect to the authorization service.
                session = await Session.Create(
                    configuration,
                    ce,
                    false,
                    false,
                    configuration.ApplicationName,
                    60000,
                    requestorIdentity,
                    null);

                var eid = ExpandedNodeId.Parse(parameters.TokenEndpoint);
                var nid = ExpandedNodeId.ToNodeId(eid, session.NamespaceUris);

                byte[] continuationPoint = null;
                ReferenceDescriptionCollection references = null;

                // find the UserTokenPolicies node.
                session.Browse(
                    null,
                    null,
                    nid,
                    0,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.HasProperty,
                    false,
                    0,
                    out continuationPoint,
                    out references);

                NodeId policiesNodeId = null;

                if (references != null || references.Count > 0)
                {
                    foreach (var reference in references)
                    {
                        if (reference.BrowseName.Name == Opc.Ua.Gds.BrowseNames.UserTokenPolicies)
                        {
                            policiesNodeId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                            break;
                        }
                    }
                }

                var dv = session.ReadValue(policiesNodeId);
                var policies = (UserTokenPolicy[])ExtensionObject.ToArray(dv.Value, typeof(UserTokenPolicy));
                UserTokenPolicy selectedPolicy = null;

                if (policies != null)
                {
                    foreach (var serverPolicy in policies)
                    {
                        if (serverPolicy.IssuedTokenType == policy.IssuedTokenType)
                        {
                            selectedPolicy = serverPolicy;
                            break;
                        }
                    }
                }

                if (selectedPolicy == null)
                {
                    // the authorization service does not support the requested policy.
                    throw new NotSupportedException();
                }

                JwtEndpointParameters serverParameters = new JwtEndpointParameters();
                serverParameters.FromJson(selectedPolicy.IssuerEndpointUrl);
                UserIdentity identity = null;

                // create the identity for authorization service.
                if (serverParameters.AuthorityProfileUri == Profiles.AzureAuthorization)
                {
                    identity = GetTokenFromAzure(configuration, parameters.ResourceId, selectedPolicy, serverParameters);
                }

                if (identity == null)
                {
                    // token not received from Azure.
                    throw new NotSupportedException();
                }

                // encrypt or sign the credentials.
                var token = identity.GetIdentityToken();

                token.Encrypt(
                    new X509Certificate2(endpoint.ServerCertificate),
                    endpoint.ServerCertificate,
                    selectedPolicy.SecurityPolicyUri);

                // find the RequestAccessToken method node.
                session.Browse(
                    null,
                    null,
                    nid,
                    0,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.HasComponent,
                    false,
                    0,
                    out continuationPoint,
                    out references);

                NodeId requestTokenNodeId = null;

                if (references != null || references.Count > 0)
                {
                    foreach (var reference in references)
                    {
                        if (reference.BrowseName.Name == Opc.Ua.Gds.BrowseNames.RequestAccessToken)
                        {
                            requestTokenNodeId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                            break;
                        }
                    }
                }

                var outputArguments = session.Call(
                   nid,
                   requestTokenNodeId,
                   token,
                   parameters.ResourceId);

                // return the new access token.
                var accessToken = outputArguments[0] as string;
                JwtSecurityToken securityToken = new JwtSecurityToken(accessToken);
                return new UserIdentity(securityToken) { PolicyId = policy.PolicyId };
            }
            finally
            {
                if (session != null)
                {
                    session.Close();
                    session.Dispose();
                    session = null;
                }
            }
        }

        private UserIdentity GetTokenFromAzure(ApplicationConfiguration configuration, string resourceId, UserTokenPolicy policy, JwtEndpointParameters parameters)
        {
            // find the client's information needed to connect to the Azure identity provider.
            var credentials = OAuth2CredentialCollection.FindByAuthorityUrl(configuration, parameters.AuthorityUrl);

            if (credentials == null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError, "No OAuth2 configuration specified for the selected authority.");
            }

            credentials.AuthorizationEndpoint = parameters.AuthorizationEndpoint;
            credentials.TokenEndpoint = parameters.TokenEndpoint;
            credentials.ServerResourceId = parameters.ResourceId;

            if (parameters.RequestTypes.Contains(Opc.Ua.JwtConstants.OAuth2AuthorizationCode))
            {
                var accessToken = new OAuth2CredentialsDialog().ShowDialog(credentials);

                if (accessToken == null)
                {
                    return null;
                }

                var jwt = new JwtSecurityToken(accessToken.AccessToken);
                return new UserIdentity(jwt) { PolicyId = policy.PolicyId };
            }

            return null;
        }


        /// <summary>
        /// Creates a SAML token for the specified email address.
        /// </summary>
        public static async System.Threading.Tasks.Task<UserIdentity> CreateSAMLTokenAsync(string emailAddress)
        {
            // Normally this would be done by a server that is capable of verifying that
            // the user is a legimate holder of email address. Using a local certficate to
            // signed the SAML token is a short cut that would never be done in a real system.
            CertificateIdentifier userid = new CertificateIdentifier();

            userid.StoreType = CertificateStoreType.X509Store;
            userid.StorePath = "LocalMachine\\My";
            userid.SubjectName = "UA Sample Client";

            X509Certificate2 certificate = await userid.Find();
            X509SecurityToken signingToken = new X509SecurityToken(certificate);

            // Create list of confirmation strings
            List<string> confirmations = new List<string>();

            // Add holder-of-key string to list of confirmation strings
            confirmations.Add("urn:oasis:names:tc:SAML:1.0:cm:bearer");

            // Create SAML subject statement based on issuer member variable, confirmation string collection 
            // local variable and proof key identifier parameter
            SamlSubject subject = new SamlSubject("urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress", null, emailAddress);

            // Create a list of SAML attributes
            List<SamlAttribute> attributes = new List<SamlAttribute>();
            Claim claim = Claim.CreateNameClaim(emailAddress);
            attributes.Add(new SamlAttribute(claim));

            // Create list of SAML statements
            List<SamlStatement> statements = new List<SamlStatement>();

            // Add a SAML attribute statement to the list of statements. Attribute statement is based on 
            // subject statement and SAML attributes resulting from claims
            statements.Add(new SamlAttributeStatement(subject, attributes));

            // Create a valid from/until condition
            DateTime validFrom = DateTime.UtcNow;
            DateTime validTo = DateTime.UtcNow.AddHours(12);

            SamlConditions conditions = new SamlConditions(validFrom, validTo);

            // Create the SAML assertion
            SamlAssertion assertion = new SamlAssertion(
                "_" + Guid.NewGuid().ToString(),
                signingToken.Certificate.Subject,
                validFrom,
                conditions,
                null,
                statements);

            SecurityKey signingKey = new System.IdentityModel.Tokens.RsaSecurityKey((RSA)signingToken.Certificate.PrivateKey);

            // Set the signing credentials for the SAML assertion
            assertion.SigningCredentials = new SigningCredentials(
                signingKey,
                System.IdentityModel.Tokens.SecurityAlgorithms.RsaSha1Signature,
                System.IdentityModel.Tokens.SecurityAlgorithms.Sha1Digest,
                new SecurityKeyIdentifier(signingToken.CreateKeyIdentifierClause<X509ThumbprintKeyIdentifierClause>()));
            // TODO
            // return new UserIdentity(new SamlSecurityToken(assertion));
            throw new NotImplementedException();
        }

        private IUserIdentity GetKerberosToken()
        {
            // need to get the service principal name from the user token policy.
            UserTokenPolicy policy = (UserTokenPolicy)KerberosTAB.Tag;

            if (policy == null)
            {
                return null;
            }

            // The ServicePrincipalName (SPN) for the UA Server must be specified as the IssuerEndpointUrl

            // The ServicePrincipalName (SPN) must be registered with the Kerberos Ticket Granting Server (e.g. Windows Domain Controller).
            // The SPN identifies the host that UA server is running on and the name of the application.
            // A domain admin must grant delegate permission to the domain account that the UA server runs under.
            // That can be done with the setspn.exe utility:

            // setspn -U -S <hostname>/<exename> <domain accountname>
            // setspn -C -S <hostname>/<exename> <hostname>

            // The latter form is used if the UA server runs a Windows Service using the builtin Windows Service account.   

            // NOTE: Using the KerberosSecurityTokenProvider without the NetworkCredential parameter will use the 
            // the credentials of the client process,

            // create the token provider.
            KerberosSecurityTokenProvider provider = new KerberosSecurityTokenProvider(
                policy.IssuerEndpointUrl,
                System.Security.Principal.TokenImpersonationLevel.Impersonation,
                new System.Net.NetworkCredential(KerberosUserNameTB.Text, KerberosPasswordTB.Text, KerberosDomainTB.Text));

            // create the token (1 minute timeout looking for the server).
            KerberosRequestorSecurityToken token = (KerberosRequestorSecurityToken)provider.GetToken(new TimeSpan(0, 1, 0));

            // TODO
            // return new UserIdentity(token);
            throw new NotImplementedException();
        }

        /// <summary>
        /// Called when a Kerberos token needs to be renewed before reconnect.
        /// </summary>
        IUserIdentity Session_RenewUserIdentity(Session session, IUserIdentity identity)
        {
            if (identity == null || identity.TokenType != UserTokenType.IssuedToken)
            {
                return identity;
            }

            return GetKerberosToken();
        }

        /// <summary>
        /// Sets the available user tokens.
        /// </summary>
        /// <param name="endpointDescription">The endpoint description.</param>
        private void SetAvailableUserTokens(EndpointDescription endpointDescription)
        {
            AnonymousTAB.Enabled = false;
            UserNameTAB.Enabled = false;
            CertificateTAB.Enabled = false;
            KerberosTAB.Enabled = false;
            OAuth2TAB.Enabled = false;

            if (endpointDescription == null)
            {
                return;
            }

            foreach (UserTokenPolicy policy in endpointDescription.UserIdentityTokens)
            {
                if (policy.TokenType == UserTokenType.Anonymous)
                {
                    if (!AnonymousTAB.Enabled)
                    {
                        AnonymousTAB.Tag = policy;
                        AnonymousTAB.Enabled = true;
                    }
                }

                if (policy.TokenType == UserTokenType.UserName)
                {
                    if (!UserNameTAB.Enabled)
                    {
                        UserNameTAB.Tag = policy;
                        UserNameTAB.Enabled = true;
                    }
                }

                if (policy.TokenType == UserTokenType.Certificate)
                {
                    if (!CertificateTAB.Enabled)
                    {
                        CertificateTAB.Tag = policy;
                        CertificateTAB.Enabled = true;
                    }
                }

                if (policy.TokenType == UserTokenType.IssuedToken)
                {
                    if (!KerberosTAB.Enabled)
                    {
                        if (policy.IssuedTokenType == "http://docs.oasis-open.org/wss/oasis-wss-kerberos-token-profile-1.1")
                        {
                            KerberosTAB.Tag = policy;
                            KerberosTAB.Enabled = true;
                        }
                    }

                    if (!OAuth2TAB.Enabled)
                    {
                        if (policy.IssuedTokenType == Profiles.JwtUserToken)
                        {
                            OAuth2TAB.Tag = policy;
                            OAuth2TAB.Enabled = true;
                        }
                    }
                }
            }
        }
#endregion

#region Event Handlers
        private void UserNameImpersonateBTN_Click(object sender, EventArgs e)
        {
            if (m_session == null)
            {
                return;
            }

            try
            {
                // want to get error text for this call.
                m_session.ReturnDiagnostics = DiagnosticsMasks.All;

                UserIdentity identity = new UserIdentity(UserNameTB.Text, PasswordTB.Text);
                string[] preferredLocales = PreferredLocalesTB.Text.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                m_session.UpdateSession(identity, preferredLocales);

                MessageBox.Show("User identity changed.", "Impersonate User", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
            finally
            {
                m_session.ReturnDiagnostics = DiagnosticsMasks.None;
            }
        }

        private void CertificateImpersonateBTN_Click(object sender, EventArgs e)
        {
            if (m_session == null)
            {
                return;
            }

            try
            {
                // load the certficate.
                X509Certificate2 certificate = new X509Certificate2(
                    CertificateTB.Text, 
                    CertificatePasswordTB.Text, 
                    X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);

                // want to get error text for this call.
                m_session.ReturnDiagnostics = DiagnosticsMasks.All;

                UserIdentity identity = new UserIdentity(certificate);
                string[] preferredLocales = PreferredLocalesTB.Text.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                m_session.UpdateSession(identity, preferredLocales);

                MessageBox.Show("User identity changed.", "Impersonate User", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
            finally
            {
                m_session.ReturnDiagnostics = DiagnosticsMasks.None;
            }
        }

        private void AnonymousImpersonateBTN_Click(object sender, EventArgs e)
        {
            if (m_session == null)
            {
                return;
            }

            try
            {
                // want to get error text for this call.
                m_session.ReturnDiagnostics = DiagnosticsMasks.All;
                
                string[] preferredLocales = PreferredLocalesTB.Text.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                m_session.UpdateSession(new UserIdentity(new AnonymousIdentityToken()), preferredLocales);

                MessageBox.Show("User identity changed.", "Impersonate User", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
            finally
            {
                m_session.ReturnDiagnostics = DiagnosticsMasks.None;
            }
        }

        private void OAuth2ImpersonateBTN_Click(object sender, EventArgs e)
        {
            if (m_session == null)
            {
                return;
            }

            try
            {
                // request the token.
                IUserIdentity identity = GetOAuth2Token().Result;

                // want to get error text for this call.
                m_session.ReturnDiagnostics = DiagnosticsMasks.All;

                string[] preferredLocales = PreferredLocalesTB.Text.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                m_session.UpdateSession(identity, preferredLocales);

                MessageBox.Show("User identity changed.", "Impersonate User", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
            finally
            {
                m_session.ReturnDiagnostics = DiagnosticsMasks.None;
            }
        }

        private void KerberosImpersonateBTN_Click(object sender, EventArgs e)
        {
            if (m_session == null)
            {
                return;
            }

            try
            {
                // request the token.
                IUserIdentity identity = GetKerberosToken();

                // want to get error text for this call.
                m_session.ReturnDiagnostics = DiagnosticsMasks.All;

                string[] preferredLocales = PreferredLocalesTB.Text.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                m_session.UpdateSession(identity, preferredLocales);

                MessageBox.Show("User identity changed.", "Impersonate User", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
            finally
            {
                m_session.ReturnDiagnostics = DiagnosticsMasks.None;
            }
        }

        /// <summary>
        /// Reads the log file path.
        /// </summary>
        private void ReadLogFilePath()
        {
            if (m_session == null)
            {
                return;
            }

            try
            {
                // want to get error text for this call.
                m_session.ReturnDiagnostics = DiagnosticsMasks.All;

                ReadValueId value = new ReadValueId();
                value.NodeId = m_logFileNodeId;
                value.AttributeId = Attributes.Value;

                ReadValueIdCollection valuesToRead = new ReadValueIdCollection();
                valuesToRead.Add(value);

                DataValueCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                ResponseHeader responseHeader = m_session.Read(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    valuesToRead,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, valuesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToRead);

                if (StatusCode.IsBad(results[0].StatusCode))
                {
                    throw ServiceResultException.Create(results[0].StatusCode, 0, diagnosticInfos, responseHeader.StringTable);
                }

                LogFilePathTB.Text = results[0].GetValue<string>("");
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
            finally
            {
                m_session.ReturnDiagnostics = DiagnosticsMasks.None;
            }
        }

        private void ChangeLogFileBTN_Click(object sender, EventArgs e)
        {
            if (m_session == null)
            {
                return;
            }

            try
            {
                // want to get error text for this call.
                m_session.ReturnDiagnostics = DiagnosticsMasks.All;

                WriteValue value = new WriteValue();
                value.NodeId = m_logFileNodeId;
                value.AttributeId = Attributes.Value;
                value.Value.Value = LogFilePathTB.Text;

                WriteValueCollection valuesToWrite = new WriteValueCollection();
                valuesToWrite.Add(value);

                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                ResponseHeader responseHeader = m_session.Write(
                    null,
                    valuesToWrite,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, valuesToWrite);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToWrite);

                if (StatusCode.IsBad(results[0]))
                {
                    throw ServiceResultException.Create(results[0], 0, diagnosticInfos, responseHeader.StringTable);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
            finally
            {
                m_session.ReturnDiagnostics = DiagnosticsMasks.None;
            }
        }
        #endregion
    }
}
