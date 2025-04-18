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

namespace Opc.Ua.Server
{
    /// <summary>
    /// A generic session manager object for a server.
    /// </summary>
    public class Session : IDisposable
    {
        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="Session"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="server">The Server object.</param>
        /// <param name="serverCertificate">The server certificate.</param>
        /// <param name="authenticationToken">The unique private identifier assigned to the Session.</param>
        /// <param name="clientNonce">The client nonce.</param>
        /// <param name="serverNonce">The server nonce.</param>
        /// <param name="sessionName">The name assigned to the Session.</param>
        /// <param name="clientDescription">Application description for the client application.</param>
        /// <param name="endpointUrl">The endpoint URL.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="clientCertificateChain">The client certifiate chain</param>
        /// <param name="sessionTimeout">The session timeout.</param>
        /// <param name="maxResponseMessageSize">The maximum size of a response message</param>
        /// <param name="maxRequestAge">The max request age.</param>
        /// <param name="maxBrowseContinuationPoints">The maximum number of browse continuation points.</param>
        /// <param name="maxHistoryContinuationPoints">The maximum number of history continuation points.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        public Session(
            OperationContext context,
            IServerInternal server,
            X509Certificate2 serverCertificate,
            NodeId authenticationToken,
            byte[] clientNonce,
            Nonce serverNonce,
            string sessionName,
            ApplicationDescription clientDescription,
            string endpointUrl,
            X509Certificate2 clientCertificate,
            X509Certificate2Collection clientCertificateChain,
            double sessionTimeout,
            uint maxResponseMessageSize,
            double maxRequestAge,
            int maxBrowseContinuationPoints,
            int maxHistoryContinuationPoints)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (server == null) throw new ArgumentNullException(nameof(server));

            // verify that a secure channel was specified.
            if (context.ChannelContext == null)
            {
                throw new ServiceResultException(StatusCodes.BadSecureChannelIdInvalid);
            }

            m_server = server;
            m_authenticationToken = authenticationToken;
            m_clientNonce = clientNonce;
            m_serverNonce = serverNonce;
            m_sessionName = sessionName;
            m_serverCertificate = serverCertificate;
            m_clientCertificate = clientCertificate;

            m_clientIssuerCertificates = clientCertificateChain;

            m_secureChannelId = context.ChannelContext.SecureChannelId;
            m_maxResponseMessageSize = maxResponseMessageSize;
            m_maxRequestAge = maxRequestAge;
            m_maxBrowseContinuationPoints = maxBrowseContinuationPoints;
            m_maxHistoryContinuationPoints = maxHistoryContinuationPoints;
            m_endpoint = context.ChannelContext.EndpointDescription;

            // use anonymous the default identity.
            m_identity = new UserIdentity();

            // initialize diagnostics.
            DateTime now = DateTime.UtcNow;
            m_diagnostics = new SessionDiagnosticsDataType {
                SessionId = null,
                SessionName = sessionName,
                ClientDescription = clientDescription,
                ServerUri = null,
                EndpointUrl = endpointUrl,
                ActualSessionTimeout = sessionTimeout,
                ClientConnectionTime = now,
                ClientLastContactTime = now,
            };

            // initialize security diagnostics.
            m_securityDiagnostics = new SessionSecurityDiagnosticsDataType {
                SessionId = m_sessionId,
                ClientUserIdOfSession = m_identity.DisplayName,
                AuthenticationMechanism = m_identity.TokenType.ToString(),
                Encoding = context.ChannelContext.MessageEncoding.ToString(),
            };
            m_securityDiagnostics.ClientUserIdHistory.Add(m_identity.DisplayName);

            EndpointDescription description = context.ChannelContext.EndpointDescription;

            if (description != null)
            {
                m_securityDiagnostics.TransportProtocol = new Uri(description.EndpointUrl).Scheme;
                m_securityDiagnostics.SecurityMode = m_endpoint.SecurityMode;
                m_securityDiagnostics.SecurityPolicyUri = m_endpoint.SecurityPolicyUri;
            }

            if (clientCertificate != null)
            {
                m_securityDiagnostics.ClientCertificate = clientCertificate.RawData;
            }

            ServerSystemContext systemContext = m_server.DefaultSystemContext.Copy(context);

            // create diagnostics object.
            m_sessionId = server.DiagnosticsNodeManager.CreateSessionDiagnostics(
                systemContext,
                m_diagnostics,
                OnUpdateDiagnostics,
                m_securityDiagnostics,
                OnUpdateSecurityDiagnostics);

            TraceState("CREATED");
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                List<ContinuationPoint> browseCPs = null;

                lock (m_lock)
                {
                    browseCPs = m_browseContinuationPoints;
                    m_browseContinuationPoints = null;
                }

                if (browseCPs != null)
                {
                    for (int ii = 0; ii < browseCPs.Count; ii++)
                    {
                        Utils.SilentDispose(browseCPs[ii]);
                    }
                }

                List<HistoryContinuationPoint> historyCPs = null;

                lock (m_lock)
                {
                    historyCPs = m_historyContinuationPoints;
                    m_historyContinuationPoints = null;
                }

                if (historyCPs != null)
                {
                    for (int ii = 0; ii < historyCPs.Count; ii++)
                    {
                        Utils.SilentDispose(historyCPs[ii].Value);
                    }
                }
            }
        }
        #endregion

        #region Public Interface
        /// <summary>
        /// Gets the identifier assigned to the session when it was created.
        /// </summary>
        public NodeId Id
        {
            get { return m_sessionId; }
        }

        /// <summary>
        /// The user identity provided by the client.
        /// </summary>
        public IUserIdentity Identity
        {
            get { return m_identity; }
        }

        /// <summary>
        /// The application defined mapping for user identity provided by the client.
        /// </summary>
        public IUserIdentity EffectiveIdentity
        {
            get { return m_effectiveIdentity; }
        }

        /// <summary>
        /// The user identity token provided by the client.
        /// </summary>
        public UserIdentityToken IdentityToken
        {
            get { return m_identityToken; }
        }

        /// <summary>
        /// A lock which must be acquired before accessing the diagnostics.
        /// </summary>
        public object DiagnosticsLock
        {
            get { return m_diagnostics; }
        }

        /// <summary>
        /// The diagnostics associated with the session.
        /// </summary>
        public SessionDiagnosticsDataType SessionDiagnostics
        {
            get { return m_diagnostics; }
        }

        /// <summary>
        /// The client Nonce associated with the session.
        /// </summary>
        public byte[] ClientNonce
        {
            get { return m_clientNonce; }
        }

        /// <summary>
        /// The application instance certificate associated with the client.
        /// </summary>
        public X509Certificate2 ClientCertificate
        {
            get { return m_clientCertificate; }
        }

        /// <summary>
        /// The locales requested when the session was created.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] PreferredLocales
        {
            get { return m_localeIds; }
        }

        /// <summary>
        /// Whether the session timeout has elapsed since the last communication from the client.
        /// </summary>
        public bool HasExpired
        {
            get
            {
                lock (DiagnosticsLock)
                {
                    if (m_diagnostics.ClientLastContactTime.AddMilliseconds(m_diagnostics.ActualSessionTimeout) < DateTime.UtcNow)
                    {
                        return true;
                    }

                    return false;
                }
            }
        }

        /// <summary>
        /// The last time the session was contacted by the client.
        /// </summary>
        public DateTime ClientLastContactTime
        {
            get
            {
                lock (DiagnosticsLock)
                {
                    return m_diagnostics.ClientLastContactTime;
                }
            }
        }

        /// <summary>
        /// Whether the session has been activated.
        /// </summary>
        public bool Activated
        {
            get
            {
                return m_activated;
            }
        }

        /// <summary>
        /// Set the ECC security policy URI
        /// </summary>
        /// <param name="securityPolicyUri"></param>
        public virtual void SetEccUserTokenSecurityPolicy(string securityPolicyUri)
        {
            lock (m_lock)
            {
                m_eccUserTokenSecurityPolicyUri = securityPolicyUri;
                m_eccUserTokenNonce = null;
            }
        }

#if ECC_SUPPORT
        /// <summary>
        /// Create new ECC ephemeral key
        /// </summary>
        /// <returns>A new ephemeral key</returns>
        public virtual EphemeralKeyType GetNewEccKey()
        {
            lock (m_lock)
            {
                if (m_eccUserTokenSecurityPolicyUri == null)
                {
                    return null;
                }

                m_eccUserTokenNonce = Nonce.CreateNonce(m_eccUserTokenSecurityPolicyUri);

                EphemeralKeyType key = new EphemeralKeyType() {
                    PublicKey = m_eccUserTokenNonce.Data
                };

                key.Signature = EccUtils.Sign(new ArraySegment<byte>(key.PublicKey), m_serverCertificate, m_eccUserTokenSecurityPolicyUri);

                return key;
            }
        }

        /// <summary>
        /// The Server generated ephemeral key
        /// </summary>
        public EphemeralKeyType EphemeralKey
        {
            set
            {
                m_ephemeralKey = value;
            }
        }
#endif
        /// <summary>
        /// Returns the session's endpoint
        /// </summary>
        public EndpointDescription EndpointDescription
        {
            get
            {
                return m_endpoint;
            }
        }

        /// <summary>
        /// Returns the session's SecureChannelId
        /// </summary>
        public string SecureChannelId
        {
            get
            {
                return m_secureChannelId;
            }
        }



        /// <summary>
        /// allow derived classes access
        /// </summary>
        protected int MaxBrowseContinuationPoints { get => m_maxBrowseContinuationPoints; set => m_maxBrowseContinuationPoints = value; }

        /// <summary>
        /// Validates the request.
        /// </summary>
        public virtual void ValidateRequest(RequestHeader requestHeader, RequestType requestType)
        {
            if (requestHeader == null) throw new ArgumentNullException(nameof(requestHeader));

            lock (m_lock)
            {
                // get the request context for the current thread.
                SecureChannelContext context = SecureChannelContext.Current;

                if (context == null || !IsSecureChannelValid(context.SecureChannelId))
                {
                    UpdateDiagnosticCounters(requestType, true, true);
                    throw new ServiceResultException(StatusCodes.BadSecureChannelIdInvalid);
                }

                // verify that session has been activated.
                if (!m_activated)
                {
                    if (requestType != RequestType.CloseSession)
                    {
                        UpdateDiagnosticCounters(requestType, true, true);
                        throw new ServiceResultException(StatusCodes.BadSessionNotActivated);
                    }
                }

                // request accepted.
                UpdateDiagnosticCounters(requestType, false, false);
            }
        }

        /// <summary>
        /// Validate the diagnostic info.
        /// </summary>
        public virtual void ValidateDiagnosticInfo(RequestHeader requestHeader)
        {
            const uint additionalInfoDiagnosticsMask = (uint)(DiagnosticsMasks.ServiceAdditionalInfo | DiagnosticsMasks.OperationAdditionalInfo);
            if ((requestHeader.ReturnDiagnostics & additionalInfoDiagnosticsMask) != 0)
            {
                var currentRoleIds = m_effectiveIdentity?.GrantedRoleIds;
                if ((currentRoleIds?.Contains(ObjectIds.WellKnownRole_SecurityAdmin)) == true ||
                    (currentRoleIds?.Contains(ObjectIds.WellKnownRole_ConfigureAdmin)) == true)
                {
                    requestHeader.ReturnDiagnostics |= (uint)DiagnosticsMasks.UserPermissionAdditionalInfo;
                }
            }
        }

        /// <summary>
        /// Checks if the secure channel is currently valid.
        /// </summary>
        public virtual bool IsSecureChannelValid(string secureChannelId)
        {
            lock (m_lock)
            {
                return (m_secureChannelId == secureChannelId);
            }
        }

        /// <summary>
        /// Updates the requested locale ids.
        /// </summary>
        /// <returns>true if the new locale ids are different from the old locale ids.</returns>
        public bool UpdateLocaleIds(StringCollection localeIds)
        {
            if (localeIds == null) throw new ArgumentNullException(nameof(localeIds));

            lock (m_lock)
            {
                string[] ids = localeIds.ToArray();

                if (!Utils.IsEqual(ids, m_localeIds))
                {
                    m_localeIds = ids;

                    // update diagnostics.
                    lock (DiagnosticsLock)
                    {
                        m_diagnostics.LocaleIds = new StringCollection(localeIds);
                    }

                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Activates the session and binds it to the current secure channel.
        /// </summary>
        public void ValidateBeforeActivate(
            OperationContext context,
            SignatureData clientSignature,
            List<SoftwareCertificate> clientSoftwareCertificates,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            out UserIdentityToken identityToken,
            out UserTokenPolicy userTokenPolicy)
        {
            lock (m_lock)
            {
                // verify that a secure channel was specified.
                if (context.ChannelContext == null)
                {
                    throw new ServiceResultException(StatusCodes.BadSecureChannelIdInvalid);
                }

                // verify that the same security policy has been used.
                EndpointDescription endpoint = context.ChannelContext.EndpointDescription;

                if (endpoint.SecurityPolicyUri != m_endpoint.SecurityPolicyUri || endpoint.SecurityMode != m_endpoint.SecurityMode)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityPolicyRejected);
                }

                // verify the client signature.
                if (m_clientCertificate != null)
                {
                    if (m_endpoint.SecurityPolicyUri != SecurityPolicies.None && clientSignature != null && clientSignature.Signature == null)
                    {
                        throw new ServiceResultException(StatusCodes.BadApplicationSignatureInvalid);
                    }

                    byte[] dataToSign = Utils.Append(m_serverCertificate.RawData, m_serverNonce.Data);

                    if (!SecurityPolicies.Verify(m_clientCertificate, m_endpoint.SecurityPolicyUri, dataToSign, clientSignature))
                    {
                        // verify for certificate chain in endpoint.
                        // validate the signature with complete chain if the check with leaf certificate failed.
                        X509Certificate2Collection serverCertificateChain = Utils.ParseCertificateChainBlob(m_endpoint.ServerCertificate);

                        if (serverCertificateChain.Count > 1)
                        {
                            List<byte> serverCertificateChainList = new List<byte>();

                            for (int i = 0; i < serverCertificateChain.Count; i++)
                            {
                                serverCertificateChainList.AddRange(serverCertificateChain[i].RawData);
                            }

                            byte[] serverCertificateChainData = serverCertificateChainList.ToArray();

                            dataToSign = Utils.Append(serverCertificateChainData, m_serverNonce.Data);

                            if (!SecurityPolicies.Verify(m_clientCertificate, m_endpoint.SecurityPolicyUri, dataToSign, clientSignature))
                            {
                                throw new ServiceResultException(StatusCodes.BadApplicationSignatureInvalid);
                            }
                        }
                        else
                        {
                            throw new ServiceResultException(StatusCodes.BadApplicationSignatureInvalid);
                        }
                    }
                }

                if (!m_activated)
                {
                    // must active the session on the channel that was used to create it.
                    if (m_secureChannelId != context.ChannelContext.SecureChannelId)
                    {
                        throw new ServiceResultException(StatusCodes.BadSecureChannelIdInvalid);
                    }
                }
                else
                {
                    // cannot change the certificates after activation.
                    if (clientSoftwareCertificates != null && clientSoftwareCertificates.Count > 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                    }
                }

                // validate the user identity token.
                identityToken = ValidateUserIdentityToken(userIdentityToken, userTokenSignature, out userTokenPolicy);

                TraceState("VALIDATED");
            }
        }

        /// <summary>
        /// Activates the session and binds it to the current secure channel.
        /// </summary>
        public bool Activate(
            OperationContext context,
            List<SoftwareCertificate> clientSoftwareCertificates,
            UserIdentityToken identityToken,
            IUserIdentity identity,
            IUserIdentity effectiveIdentity,
            StringCollection localeIds,
            Nonce serverNonce)
        {
            lock (m_lock)
            {
                // update user identity.
                bool changed = false;

                if (identityToken != null)
                {
                    if (UpdateUserIdentity(identityToken, identity, effectiveIdentity))
                    {
                        changed = true;
                    }
                }

                // update local ids.
                if (UpdateLocaleIds(localeIds))
                {
                    changed = true;
                }

                if (!m_activated)
                {
                    // toggle the activated flag.
                    m_activated = true;

                    // save the software certificates.
                    m_softwareCertificates = clientSoftwareCertificates;

                    TraceState("FIRST ACTIVATION");
                }
                else
                {
                    // bind to the new secure channel.
                    m_secureChannelId = context.ChannelContext.SecureChannelId;

                    TraceState("RE-ACTIVATION");
                }

                // update server nonce.
                m_serverNonce = serverNonce;

                // build list of signed certificates for audit event.
                List<SignedSoftwareCertificate> signedSoftwareCertificates = new List<SignedSoftwareCertificate>();

                if (clientSoftwareCertificates != null)
                {
                    foreach (SoftwareCertificate softwareCertificate in clientSoftwareCertificates)
                    {
                        SignedSoftwareCertificate item = new SignedSoftwareCertificate();
                        item.CertificateData = softwareCertificate.SignedCertificate.RawData;
                        signedSoftwareCertificates.Add(item);
                    }
                }

                // update the contact time.
                lock (DiagnosticsLock)
                {
                    m_diagnostics.ClientLastContactTime = DateTime.UtcNow;
                }

                // indicate whether the user context has changed.
                return changed;
            }
        }

        /// <summary>
        /// Closes a session and removes itself from the address space.
        /// </summary>
        public void Close()
        {
            TraceState("CLOSED");

            m_server.DiagnosticsNodeManager.DeleteSessionDiagnostics(
                m_server.DefaultSystemContext,
                m_sessionId);
        }

        /// <summary>
        /// Saves a continuation point for a session.
        /// </summary>
        /// <remarks>
        /// If the session has too many continuation points the oldest one is dropped.
        /// </remarks>
        public void SaveContinuationPoint(ContinuationPoint continuationPoint)
        {
            if (continuationPoint == null) throw new ArgumentNullException(nameof(continuationPoint));

            lock (m_lock)
            {
                if (m_browseContinuationPoints == null)
                {
                    m_browseContinuationPoints = new List<ContinuationPoint>();
                }

                // remove the first continuation point if too many points.
                while (m_browseContinuationPoints.Count > m_maxBrowseContinuationPoints)
                {
                    ContinuationPoint cp = m_browseContinuationPoints[0];
                    m_browseContinuationPoints.RemoveAt(0);
                    Utils.SilentDispose(cp);
                }

                // add to end of list.
                m_browseContinuationPoints.Add(continuationPoint);
            }
        }

        /// <summary>
        /// Restores a continuation point for a session.
        /// </summary>
        /// <remarks>
        /// The caller is responsible for disposing the continuation point returned.
        /// </remarks>
        public ContinuationPoint RestoreContinuationPoint(byte[] continuationPoint)
        {
            lock (m_lock)
            {
                if (m_browseContinuationPoints == null)
                {
                    return null;
                }

                if (continuationPoint == null || continuationPoint.Length != 16)
                {
                    return null;
                }

                Guid id = new Guid(continuationPoint);

                for (int ii = 0; ii < m_browseContinuationPoints.Count; ii++)
                {
                    if (m_browseContinuationPoints[ii].Id == id)
                    {
                        ContinuationPoint cp = m_browseContinuationPoints[ii];
                        m_browseContinuationPoints.RemoveAt(ii);
                        return cp;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Saves a continuation point used for historical reads.
        /// </summary>
        /// <param name="id">The identifier for the continuation point.</param>
        /// <param name="continuationPoint">The continuation point.</param>
        /// <remarks>
        /// If the continuationPoint implements IDisposable it will be disposed when
        /// the Session is closed or discarded.
        /// </remarks>
        public void SaveHistoryContinuationPoint(Guid id, object continuationPoint)
        {
            if (continuationPoint == null) throw new ArgumentNullException(nameof(continuationPoint));

            lock (m_lock)
            {
                if (m_historyContinuationPoints == null)
                {
                    m_historyContinuationPoints = new List<HistoryContinuationPoint>();
                }

                // remove existing continuation point if space needed.
                while (m_historyContinuationPoints.Count >= m_maxHistoryContinuationPoints)
                {
                    HistoryContinuationPoint oldCP = m_historyContinuationPoints[0];
                    m_historyContinuationPoints.RemoveAt(0);
                    Utils.SilentDispose(oldCP.Value);
                }

                // create the cp.
                HistoryContinuationPoint cp = new HistoryContinuationPoint();

                cp.Id = id;
                cp.Value = continuationPoint;
                cp.Timestamp = DateTime.UtcNow;

                m_historyContinuationPoints.Add(cp);
            }
        }

        /// <summary>
        /// Restores a previously saves history continuation point.
        /// </summary>
        /// <param name="continuationPoint">The identifier for the continuation point.</param>
        /// <returns>The save continuation point. null if not found.</returns>
        public object RestoreHistoryContinuationPoint(byte[] continuationPoint)
        {
            lock (m_lock)
            {
                if (m_historyContinuationPoints == null)
                {
                    return null;
                }

                if (continuationPoint == null || continuationPoint.Length != 16)
                {
                    return null;
                }

                Guid id = new Guid(continuationPoint);

                for (int ii = 0; ii < m_historyContinuationPoints.Count; ii++)
                {
                    HistoryContinuationPoint cp = m_historyContinuationPoints[ii];

                    if (cp.Id == id)
                    {
                        m_historyContinuationPoints.RemoveAt(ii);
                        return cp.Value;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Stores a continuation point used for historial reads.
        /// </summary>
        private class HistoryContinuationPoint
        {
            public Guid Id;
            public object Value;
            public DateTime Timestamp;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Dumps the current state of the session queue.
        /// </summary>
        internal void TraceState(string context)
        {
            ServerUtils.EventLog.SessionState(context, m_sessionId.ToString(), m_sessionName,
                m_secureChannelId, m_identity?.DisplayName ?? "(none)");
        }

        /// <summary>
        /// Returns a copy of the current diagnostics.
        /// </summary>
        private ServiceResult OnUpdateDiagnostics(
            ISystemContext context,
            NodeState node,
            ref object value)
        {
            lock (DiagnosticsLock)
            {
                value = Utils.Clone(m_diagnostics);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns a copy of the current security diagnostics.
        /// </summary>
        private ServiceResult OnUpdateSecurityDiagnostics(
            ISystemContext context,
            NodeState node,
            ref object value)
        {
            lock (DiagnosticsLock)
            {
                value = Utils.Clone(m_securityDiagnostics);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Validates the identity token supplied by the client.
        /// </summary>
        private UserIdentityToken ValidateUserIdentityToken(
            ExtensionObject identityToken,
            SignatureData userTokenSignature,
            out UserTokenPolicy policy)
        {
            policy = null;

            // check for empty token.
            if (identityToken == null || identityToken.Body == null ||
                identityToken.Body.GetType() == typeof(Opc.Ua.AnonymousIdentityToken))
            {
                // check if an anonymous login is permitted.
                if (m_endpoint.UserIdentityTokens != null && m_endpoint.UserIdentityTokens.Count > 0)
                {
                    bool found = false;

                    for (int ii = 0; ii < m_endpoint.UserIdentityTokens.Count; ii++)
                    {
                        if (m_endpoint.UserIdentityTokens[ii].TokenType == UserTokenType.Anonymous)
                        {
                            found = true;
                            policy = m_endpoint.UserIdentityTokens[ii];
                            break;
                        }
                    }

                    if (!found)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadIdentityTokenRejected, "Anonymous user token policy not supported.");
                    }
                }

                // create an anonymous token to use for subsequent validation.
                AnonymousIdentityToken anonymousToken = new AnonymousIdentityToken();
                anonymousToken.PolicyId = policy.PolicyId;
                return anonymousToken;
            }

            UserIdentityToken token = null;
            // check for unrecognized token.
            if (!typeof(UserIdentityToken).IsInstanceOfType(identityToken.Body))
            {
                //handle the use case when the UserIdentityToken is binary encoded over xml message encoding
                if (identityToken.Encoding == ExtensionObjectEncoding.Binary && typeof(byte[]).IsInstanceOfType(identityToken.Body))
                {
                    UserIdentityToken newToken = BaseVariableState.DecodeExtensionObject(null, typeof(UserIdentityToken), identityToken, false) as UserIdentityToken;
                    if (newToken == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUserAccessDenied, "Invalid user identity token provided.");
                    }

                    policy = m_endpoint.FindUserTokenPolicy(newToken.PolicyId, m_endpoint.SecurityPolicyUri);
                    if (policy == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUserAccessDenied, "User token policy not supported.", "Opc.Ua.Server.Session.ValidateUserIdentityToken");
                    }
                    switch (policy.TokenType)
                    {
                        case UserTokenType.Anonymous:
                            token = BaseVariableState.DecodeExtensionObject(null, typeof(AnonymousIdentityToken), identityToken, true) as AnonymousIdentityToken;
                            break;
                        case UserTokenType.UserName:
                            token = BaseVariableState.DecodeExtensionObject(null, typeof(UserNameIdentityToken), identityToken, true) as UserNameIdentityToken;
                            break;
                        case UserTokenType.Certificate:
                            token = BaseVariableState.DecodeExtensionObject(null, typeof(X509IdentityToken), identityToken, true) as X509IdentityToken;
                            break;
                        case UserTokenType.IssuedToken:
                            token = BaseVariableState.DecodeExtensionObject(null, typeof(IssuedIdentityToken), identityToken, true) as IssuedIdentityToken;
                            break;
                        default:
                            throw ServiceResultException.Create(StatusCodes.BadUserAccessDenied, "Invalid user identity token provided.");
                    }
                }
                else
                {
                    throw ServiceResultException.Create(StatusCodes.BadUserAccessDenied, "Invalid user identity token provided.");
                }
            }
            else
            {
                // get the token.
                token = (UserIdentityToken)identityToken.Body;
            }

            // find the user token policy.
            policy = m_endpoint.FindUserTokenPolicy(token.PolicyId, m_endpoint.SecurityPolicyUri);

            if (policy == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadIdentityTokenInvalid, "User token policy not supported.");
            }

            if (token is IssuedIdentityToken issuedToken)
            {
                if (policy.IssuedTokenType == Profiles.JwtUserToken)
                {
                    issuedToken.IssuedTokenType = IssuedTokenType.JWT;
                }
            }

            // determine the security policy uri.
            string securityPolicyUri = policy.SecurityPolicyUri;

            if (String.IsNullOrEmpty(securityPolicyUri))
            {
                securityPolicyUri = m_endpoint.SecurityPolicyUri;
            }

            if (ServerBase.RequireEncryption(m_endpoint))
            {
                // decrypt the token.
                if (m_serverCertificate == null)
                {
                    m_serverCertificate = CertificateFactory.Create(m_endpoint.ServerCertificate, true);

                    // check for valid certificate.
                    if (m_serverCertificate == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "ApplicationCertificate cannot be found.");
                    }
                }

                try
                {
                    token.Decrypt(m_serverCertificate,
                        m_serverNonce,
                        securityPolicyUri,
                        m_eccUserTokenNonce,
                        m_clientCertificate,
                        m_clientIssuerCertificates);
                }
                catch (Exception e)
                {
                    if (e is ServiceResultException)
                    {
                        throw;
                    }

                    throw ServiceResultException.Create(StatusCodes.BadIdentityTokenInvalid, e, "Could not decrypt identity token.");
                }

                // verify the signature.
                if (securityPolicyUri != SecurityPolicies.None)
                {

                    byte[] dataToSign = Utils.Append(m_serverCertificate.RawData, m_serverNonce.Data);

                    if (!token.Verify(dataToSign, userTokenSignature, securityPolicyUri))
                    {
                        // verify for certificate chain in endpoint.
                        // validate the signature with complete chain if the check with leaf certificate failed.
                        X509Certificate2Collection serverCertificateChain = Utils.ParseCertificateChainBlob(m_endpoint.ServerCertificate);

                        if (serverCertificateChain.Count > 1)
                        {
                            List<byte> serverCertificateChainList = new List<byte>();

                            for (int i = 0; i < serverCertificateChain.Count; i++)
                            {
                                serverCertificateChainList.AddRange(serverCertificateChain[i].RawData);
                            }

                            byte[] serverCertificateChainData = serverCertificateChainList.ToArray();

                            dataToSign = Utils.Append(serverCertificateChainData, m_serverNonce.Data);

                            if (!token.Verify(dataToSign, userTokenSignature, securityPolicyUri))
                            {
                                throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected, "Invalid user signature!");
                            }
                        }
                        else
                        {
                            throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected, "Invalid user signature!");
                        }
                    }
                }
            }

            // validate user identity token.
            return token;
        }

        /// <summary>
        /// Updates the user identity.
        /// </summary>
        /// <returns>true if the new identity is different from the old identity.</returns>
        private bool UpdateUserIdentity(
            UserIdentityToken identityToken,
            IUserIdentity identity,
            IUserIdentity effectiveIdentity)
        {
            if (identityToken == null) throw new ArgumentNullException(nameof(identityToken));

            lock (m_lock)
            {
                bool changed = m_effectiveIdentity == null && effectiveIdentity != null;

                if (m_effectiveIdentity != null)
                {
                    changed = !m_effectiveIdentity.Equals(effectiveIdentity);
                }

                // always save the new identity since it may have additional information that does not affect equality.
                m_identityToken = identityToken;
                m_identity = identity;
                m_effectiveIdentity = effectiveIdentity;

                // update diagnostics.
                lock (DiagnosticsLock)
                {
                    m_securityDiagnostics.ClientUserIdOfSession = identity.DisplayName;
                    m_securityDiagnostics.AuthenticationMechanism = identity.TokenType.ToString();

                    m_securityDiagnostics.ClientUserIdHistory.Add(identity.DisplayName);
                }

                return changed;
            }
        }

        /// <summary>
        /// Updates the diagnostic counters associated with the request.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        private void UpdateDiagnosticCounters(RequestType requestType, bool error, bool authorizationError)
        {
            lock (DiagnosticsLock)
            {
                if (!error)
                {
                    m_diagnostics.ClientLastContactTime = DateTime.UtcNow;
                }

                m_diagnostics.TotalRequestCount.TotalCount++;

                if (error)
                {
                    m_diagnostics.TotalRequestCount.ErrorCount++;

                    if (authorizationError)
                    {
                        m_diagnostics.UnauthorizedRequestCount++;
                    }
                }

                ServiceCounterDataType counter = null;

                switch (requestType)
                {
                    case RequestType.Read: { counter = m_diagnostics.ReadCount; break; }
                    case RequestType.HistoryRead: { counter = m_diagnostics.HistoryReadCount; break; }
                    case RequestType.Write: { counter = m_diagnostics.WriteCount; break; }
                    case RequestType.HistoryUpdate: { counter = m_diagnostics.HistoryUpdateCount; break; }
                    case RequestType.Call: { counter = m_diagnostics.CallCount; break; }
                    case RequestType.CreateMonitoredItems: { counter = m_diagnostics.CreateMonitoredItemsCount; break; }
                    case RequestType.ModifyMonitoredItems: { counter = m_diagnostics.ModifyMonitoredItemsCount; break; }
                    case RequestType.SetMonitoringMode: { counter = m_diagnostics.SetMonitoringModeCount; break; }
                    case RequestType.SetTriggering: { counter = m_diagnostics.SetTriggeringCount; break; }
                    case RequestType.DeleteMonitoredItems: { counter = m_diagnostics.DeleteMonitoredItemsCount; break; }
                    case RequestType.CreateSubscription: { counter = m_diagnostics.CreateSubscriptionCount; break; }
                    case RequestType.ModifySubscription: { counter = m_diagnostics.ModifySubscriptionCount; break; }
                    case RequestType.SetPublishingMode: { counter = m_diagnostics.SetPublishingModeCount; break; }
                    case RequestType.Publish: { counter = m_diagnostics.PublishCount; break; }
                    case RequestType.Republish: { counter = m_diagnostics.RepublishCount; break; }
                    case RequestType.TransferSubscriptions: { counter = m_diagnostics.TransferSubscriptionsCount; break; }
                    case RequestType.DeleteSubscriptions: { counter = m_diagnostics.DeleteSubscriptionsCount; break; }
                    case RequestType.AddNodes: { counter = m_diagnostics.AddNodesCount; break; }
                    case RequestType.AddReferences: { counter = m_diagnostics.AddReferencesCount; break; }
                    case RequestType.DeleteNodes: { counter = m_diagnostics.DeleteNodesCount; break; }
                    case RequestType.DeleteReferences: { counter = m_diagnostics.DeleteReferencesCount; break; }
                    case RequestType.Browse: { counter = m_diagnostics.BrowseCount; break; }
                    case RequestType.BrowseNext: { counter = m_diagnostics.BrowseNextCount; break; }
                    case RequestType.TranslateBrowsePathsToNodeIds: { counter = m_diagnostics.TranslateBrowsePathsToNodeIdsCount; break; }
                    case RequestType.QueryFirst: { counter = m_diagnostics.QueryFirstCount; break; }
                    case RequestType.QueryNext: { counter = m_diagnostics.QueryNextCount; break; }
                    case RequestType.RegisterNodes: { counter = m_diagnostics.RegisterNodesCount; break; }
                    case RequestType.UnregisterNodes: { counter = m_diagnostics.UnregisterNodesCount; break; }
                }

                if (counter != null)
                {
                    counter.TotalCount = counter.TotalCount + 1;

                    if (error)
                    {
                        counter.ErrorCount = counter.ErrorCount + 1;
                    }
                }
            }
        }
        #endregion

        #region Private Fields
        private readonly object m_lock = new object();
        private NodeId m_sessionId;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        private NodeId m_authenticationToken;
        private IServerInternal m_server;

        private UserIdentityToken m_identityToken;
        private IUserIdentity m_identity;
        private IUserIdentity m_effectiveIdentity;
        private bool m_activated;

        private X509Certificate2 m_clientCertificate;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        private List<SoftwareCertificate> m_softwareCertificates;
        private byte[] m_clientNonce;
        private string m_sessionName;
        private string m_secureChannelId;
        private EndpointDescription m_endpoint;
        private X509Certificate2 m_serverCertificate;

        private Nonce m_serverNonce;
        private string m_eccUserTokenSecurityPolicyUri;
        private Nonce m_eccUserTokenNonce;
        private X509Certificate2Collection m_clientIssuerCertificates;

        private string[] m_localeIds;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        private uint m_maxResponseMessageSize;
        private double m_maxRequestAge;
        private int m_maxBrowseContinuationPoints;
        private int m_maxHistoryContinuationPoints;

        private SessionDiagnosticsDataType m_diagnostics;
        private SessionSecurityDiagnosticsDataType m_securityDiagnostics;
        private List<ContinuationPoint> m_browseContinuationPoints;
        private List<HistoryContinuationPoint> m_historyContinuationPoints;

#if ECC_SUPPORT
        private EphemeralKeyType m_ephemeralKey;
#endif

        #endregion
    }
}
