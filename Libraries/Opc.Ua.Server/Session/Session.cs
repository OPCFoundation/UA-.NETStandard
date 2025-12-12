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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A generic session manager object for a server.
    /// </summary>
    public class Session : ISession
    {
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
        /// <param name="maxBrowseContinuationPoints">The maximum number of browse continuation points.</param>
        /// <param name="maxHistoryContinuationPoints">The maximum number of history continuation points.</param>
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
            int maxBrowseContinuationPoints,
            int maxHistoryContinuationPoints)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // verify that a secure channel was specified.
            if (context.ChannelContext == null)
            {
                throw new ServiceResultException(StatusCodes.BadSecureChannelIdInvalid);
            }

            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_logger = server.Telemetry.CreateLogger<Session>();
            ClientNonce = clientNonce;
            m_serverNonce = serverNonce;
            m_sessionName = sessionName;
            m_serverCertificate = serverCertificate;
            ClientCertificate = clientCertificate;

            m_clientIssuerCertificates = clientCertificateChain;

            SecureChannelId = context.ChannelContext.SecureChannelId;
            MaxBrowseContinuationPoints = maxBrowseContinuationPoints;
            m_maxHistoryContinuationPoints = maxHistoryContinuationPoints;
            EndpointDescription = context.ChannelContext.EndpointDescription;

            // use anonymous the default identity.
            Identity = new UserIdentity();

            // initialize diagnostics.
            DateTime now = DateTime.UtcNow;
            SessionDiagnostics = new SessionDiagnosticsDataType
            {
                SessionId = null,
                SessionName = sessionName,
                ClientDescription = clientDescription,
                ServerUri = null,
                EndpointUrl = endpointUrl,
                ActualSessionTimeout = sessionTimeout,
                ClientConnectionTime = now,
                ClientLastContactTime = now
            };

            // initialize security diagnostics.
            m_securityDiagnostics = new SessionSecurityDiagnosticsDataType
            {
                SessionId = Id,
                ClientUserIdOfSession = Identity.DisplayName,
                AuthenticationMechanism = Identity.TokenType.ToString(),
                Encoding = context.ChannelContext.MessageEncoding.ToString()
            };
            m_securityDiagnostics.ClientUserIdHistory.Add(Identity.DisplayName);

            EndpointDescription description = context.ChannelContext.EndpointDescription;

            if (description != null)
            {
                m_securityDiagnostics.TransportProtocol = new Uri(description.EndpointUrl).Scheme;
                m_securityDiagnostics.SecurityMode = EndpointDescription.SecurityMode;
                m_securityDiagnostics.SecurityPolicyUri = EndpointDescription.SecurityPolicyUri;
            }

            if (clientCertificate != null)
            {
                m_securityDiagnostics.ClientCertificate = clientCertificate.RawData;
            }

            ServerSystemContext systemContext = m_server.DefaultSystemContext.Copy(context);

            // create diagnostics object.
            Id = server.DiagnosticsNodeManager.CreateSessionDiagnostics(
                systemContext,
                SessionDiagnostics,
                OnUpdateDiagnostics,
                m_securityDiagnostics,
                OnUpdateSecurityDiagnostics);

            TraceState("CREATED");
        }

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

        /// <summary>
        /// Gets the identifier assigned to the session when it was created.
        /// </summary>
        public NodeId Id { get; }

        /// <summary>
        /// The user identity provided by the client.
        /// </summary>
        public IUserIdentity Identity { get; private set; }

        /// <summary>
        /// The application defined mapping for user identity provided by the client.
        /// </summary>
        public IUserIdentity EffectiveIdentity { get; private set; }

        /// <summary>
        /// The user identity token provided by the client.
        /// </summary>
        public UserIdentityToken IdentityToken { get; private set; }

        /// <summary>
        /// A lock which must be acquired before accessing the diagnostics.
        /// </summary>
        public object DiagnosticsLock => SessionDiagnostics;

        /// <summary>
        /// The diagnostics associated with the session.
        /// </summary>
        public SessionDiagnosticsDataType SessionDiagnostics { get; }

        /// <summary>
        /// The client Nonce associated with the session.
        /// </summary>
        public byte[] ClientNonce { get; }

        /// <summary>
        /// The application instance certificate associated with the client.
        /// </summary>
        public X509Certificate2 ClientCertificate { get; }

        /// <summary>
        /// The locales requested when the session was created.
        /// </summary>
        public string[] PreferredLocales { get; private set; }

        /// <summary>
        /// Whether the session timeout has elapsed since the last communication from the client.
        /// </summary>
        public bool HasExpired
        {
            get
            {
                lock (DiagnosticsLock)
                {
                    return SessionDiagnostics.ClientLastContactTime.AddMilliseconds(
                            SessionDiagnostics.ActualSessionTimeout
                        ) < DateTime.UtcNow;
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
                    return SessionDiagnostics.ClientLastContactTime;
                }
            }
        }

        /// <summary>
        /// Whether the session has been activated.
        /// </summary>
        public bool Activated { get; private set; }

        /// <summary>
        /// Set the ECC security policy URI
        /// </summary>
        public virtual void SetEccUserTokenSecurityPolicy(string securityPolicyUri)
        {
            lock (m_lock)
            {
                m_eccUserTokenSecurityPolicyUri = securityPolicyUri;
                m_eccUserTokenNonce = null;
            }
        }

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

                var key = new EphemeralKeyType { PublicKey = m_eccUserTokenNonce.Data };

                key.Signature = EccUtils.Sign(
                    new ArraySegment<byte>(key.PublicKey),
                    m_serverCertificate,
                    m_eccUserTokenSecurityPolicyUri);

                return key;
            }
        }

        /// <summary>
        /// Returns the session's endpoint
        /// </summary>
        public EndpointDescription EndpointDescription { get; }

        /// <summary>
        /// Returns the session's SecureChannelId
        /// </summary>
        public string SecureChannelId { get; private set; }

        /// <summary>
        /// allow derived classes access
        /// </summary>
        protected int MaxBrowseContinuationPoints { get; set; }

        /// <summary>
        /// Validates the request.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="requestHeader"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public virtual void ValidateRequest(RequestHeader requestHeader, SecureChannelContext secureChannelContext, RequestType requestType)
        {
            if (requestHeader == null)
            {
                throw new ArgumentNullException(nameof(requestHeader));
            }

            lock (m_lock)
            {
                if (secureChannelContext == null || !IsSecureChannelValid(secureChannelContext.SecureChannelId))
                {
                    UpdateDiagnosticCounters(requestType, true, true);
                    throw new ServiceResultException(StatusCodes.BadSecureChannelIdInvalid);
                }

                // verify that session has been activated.
                if (!Activated && requestType != RequestType.CloseSession)
                {
                    UpdateDiagnosticCounters(requestType, true, true);
                    throw new ServiceResultException(StatusCodes.BadSessionNotActivated);
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
            const uint additionalInfoDiagnosticsMask = (uint)(
                DiagnosticsMasks.ServiceAdditionalInfo | DiagnosticsMasks.OperationAdditionalInfo);
            if ((requestHeader.ReturnDiagnostics & additionalInfoDiagnosticsMask) != 0)
            {
                NodeIdCollection currentRoleIds = EffectiveIdentity?.GrantedRoleIds;
                if ((currentRoleIds?.Contains(ObjectIds.WellKnownRole_SecurityAdmin)) == true ||
                    (currentRoleIds?.Contains(ObjectIds.WellKnownRole_ConfigureAdmin)) == true)
                {
                    requestHeader.ReturnDiagnostics
                        |= (uint)DiagnosticsMasks.UserPermissionAdditionalInfo;
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
                return SecureChannelId == secureChannelId;
            }
        }

        /// <summary>
        /// Updates the requested locale ids.
        /// </summary>
        /// <returns>true if the new locale ids are different from the old locale ids.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="localeIds"/> is <c>null</c>.</exception>
        public bool UpdateLocaleIds(StringCollection localeIds)
        {
            if (localeIds == null)
            {
                throw new ArgumentNullException(nameof(localeIds));
            }

            lock (m_lock)
            {
                string[] ids = [.. localeIds];

                if (!Utils.IsEqual(ids, PreferredLocales))
                {
                    PreferredLocales = ids;

                    // update diagnostics.
                    lock (DiagnosticsLock)
                    {
                        SessionDiagnostics.LocaleIds = [.. localeIds];
                    }

                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Activates the session and binds it to the current secure channel.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
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

                if (endpoint.SecurityPolicyUri != EndpointDescription.SecurityPolicyUri ||
                    endpoint.SecurityMode != EndpointDescription.SecurityMode)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityPolicyRejected);
                }

                // verify the client signature.
                if (ClientCertificate != null)
                {
                    if (EndpointDescription.SecurityPolicyUri != SecurityPolicies.None &&
                        clientSignature != null &&
                        clientSignature.Signature == null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadApplicationSignatureInvalid);
                    }

                    byte[] dataToSign = Utils.Append(
                        m_serverCertificate.RawData,
                        m_serverNonce.Data);

                    if (!SecurityPolicies.Verify(
                            ClientCertificate,
                            EndpointDescription.SecurityPolicyUri,
                            dataToSign,
                            clientSignature))
                    {
                        // verify for certificate chain in endpoint.
                        // validate the signature with complete chain if the check with leaf certificate failed.
                        X509Certificate2Collection serverCertificateChain =
                            Utils.ParseCertificateChainBlob(
                                EndpointDescription.ServerCertificate,
                                m_server.Telemetry);

                        if (serverCertificateChain.Count > 1)
                        {
                            var serverCertificateChainList = new List<byte>();

                            for (int i = 0; i < serverCertificateChain.Count; i++)
                            {
                                serverCertificateChainList.AddRange(
                                    serverCertificateChain[i].RawData);
                            }

                            byte[] serverCertificateChainData = [.. serverCertificateChainList];

                            dataToSign = Utils.Append(
                                serverCertificateChainData,
                                m_serverNonce.Data);

                            if (!SecurityPolicies.Verify(
                                    ClientCertificate,
                                    EndpointDescription.SecurityPolicyUri,
                                    dataToSign,
                                    clientSignature))
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadApplicationSignatureInvalid);
                            }
                        }
                        else
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadApplicationSignatureInvalid);
                        }
                    }
                }

                if (!Activated)
                {
                    // must active the session on the channel that was used to create it.
                    if (SecureChannelId != context.ChannelContext.SecureChannelId)
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
                identityToken = ValidateUserIdentityToken(
                    userIdentityToken,
                    userTokenSignature,
                    out userTokenPolicy);

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

                if (identityToken != null &&
                    UpdateUserIdentity(identityToken, identity, effectiveIdentity))
                {
                    changed = true;
                }

                // update local ids.
                if (UpdateLocaleIds(localeIds))
                {
                    changed = true;
                }

                if (!Activated)
                {
                    // toggle the activated flag.
                    Activated = true;

                    TraceState("FIRST ACTIVATION");
                }
                else
                {
                    // bind to the new secure channel.
                    SecureChannelId = context.ChannelContext.SecureChannelId;

                    TraceState("RE-ACTIVATION");
                }

                // update server nonce.
                m_serverNonce = serverNonce;

                // build list of signed certificates for audit event.
                var signedSoftwareCertificates = new List<SignedSoftwareCertificate>();

                if (clientSoftwareCertificates != null)
                {
                    foreach (SoftwareCertificate softwareCertificate in clientSoftwareCertificates)
                    {
                        var item = new SignedSoftwareCertificate
                        {
                            CertificateData = softwareCertificate.SignedCertificate.RawData
                        };
                        signedSoftwareCertificates.Add(item);
                    }
                }

                // update the contact time.
                lock (DiagnosticsLock)
                {
                    SessionDiagnostics.ClientLastContactTime = DateTime.UtcNow;
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

            m_server.DiagnosticsNodeManager
                .DeleteSessionDiagnostics(m_server.DefaultSystemContext, Id);
        }

        /// <summary>
        /// Saves a continuation point for a session.
        /// </summary>
        /// <remarks>
        /// If the session has too many continuation points the oldest one is dropped.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="continuationPoint"/> is <c>null</c>.</exception>
        public void SaveContinuationPoint(ContinuationPoint continuationPoint)
        {
            if (continuationPoint == null)
            {
                throw new ArgumentNullException(nameof(continuationPoint));
            }

            lock (m_lock)
            {
                m_browseContinuationPoints ??= [];

                // remove the first continuation point if too many points.
                while (m_browseContinuationPoints.Count > MaxBrowseContinuationPoints)
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

                var id = new Guid(continuationPoint);

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
        /// <exception cref="ArgumentNullException"><paramref name="continuationPoint"/> is <c>null</c>.</exception>
        public void SaveHistoryContinuationPoint(Guid id, object continuationPoint)
        {
            if (continuationPoint == null)
            {
                throw new ArgumentNullException(nameof(continuationPoint));
            }

            lock (m_lock)
            {
                m_historyContinuationPoints ??= [];

                // remove existing continuation point if space needed.
                while (m_historyContinuationPoints.Count >= m_maxHistoryContinuationPoints)
                {
                    HistoryContinuationPoint oldCP = m_historyContinuationPoints[0];
                    m_historyContinuationPoints.RemoveAt(0);
                    Utils.SilentDispose(oldCP.Value);
                }

                // create the cp.
                var cp = new HistoryContinuationPoint
                {
                    Id = id,
                    Value = continuationPoint,
                    Timestamp = DateTime.UtcNow
                };

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

                var id = new Guid(continuationPoint);

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

        /// <summary>
        /// Dumps the current state of the session queue.
        /// </summary>
        internal void TraceState(string context)
        {
            // Legacy event source logging
            ServerUtils.EventLog.SessionState(
                context,
                Id.ToString(),
                m_sessionName,
                SecureChannelId,
                Identity?.DisplayName ?? "(none)");

            m_logger.LogInformation(
                "Session {Context}, Id={SessionId}, Name={Name}, ChannelId={ChannelId}, User={User}",
                context,
                Id.ToString(),
                m_sessionName,
                SecureChannelId,
                Identity?.DisplayName ?? "(none)");
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
                value = Utils.Clone(SessionDiagnostics);
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
        /// <exception cref="ServiceResultException"></exception>
        private UserIdentityToken ValidateUserIdentityToken(
            ExtensionObject identityToken,
            SignatureData userTokenSignature,
            out UserTokenPolicy policy)
        {
            policy = null;

            // check for empty token.
            if (identityToken == null ||
                identityToken.Body == null ||
                identityToken.Body.GetType() == typeof(AnonymousIdentityToken))
            {
                // check if an anonymous login is permitted.
                if (EndpointDescription.UserIdentityTokens != null &&
                    EndpointDescription.UserIdentityTokens.Count > 0)
                {
                    bool found = false;

                    for (int ii = 0; ii < EndpointDescription.UserIdentityTokens.Count; ii++)
                    {
                        if (EndpointDescription.UserIdentityTokens[ii]
                            .TokenType == UserTokenType.Anonymous)
                        {
                            found = true;
                            policy = EndpointDescription.UserIdentityTokens[ii];
                            break;
                        }
                    }

                    if (!found)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadIdentityTokenRejected,
                            "Anonymous user token policy not supported.");
                    }
                }

                // create an anonymous token to use for subsequent validation.
                return new AnonymousIdentityToken { PolicyId = policy.PolicyId };
            }

            UserIdentityToken token;
            // check for unrecognized token.
            if (!typeof(UserIdentityToken).IsInstanceOfType(identityToken.Body))
            {
                //handle the use case when the UserIdentityToken is binary encoded over xml message encoding
                if (identityToken.Encoding == ExtensionObjectEncoding.Binary &&
                    typeof(byte[]).IsInstanceOfType(identityToken.Body))
                {
                    if (BaseVariableState.DecodeExtensionObject(
                            null,
                            typeof(UserIdentityToken),
                            identityToken,
                            false)
                        is not UserIdentityToken newToken)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadUserAccessDenied,
                            "Invalid user identity token provided.");
                    }

                    policy = EndpointDescription.FindUserTokenPolicy(
                        newToken.PolicyId,
                        EndpointDescription.SecurityPolicyUri);
                    if (policy == null)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadUserAccessDenied,
                            "User token policy not supported.",
                            "Opc.Ua.Server.Session.ValidateUserIdentityToken");
                    }
                    switch (policy.TokenType)
                    {
                        case UserTokenType.Anonymous:
                            token =
                                BaseVariableState.DecodeExtensionObject(
                                    null,
                                    typeof(AnonymousIdentityToken),
                                    identityToken,
                                    true
                                ) as AnonymousIdentityToken;
                            break;
                        case UserTokenType.UserName:
                            token =
                                BaseVariableState.DecodeExtensionObject(
                                    null,
                                    typeof(UserNameIdentityToken),
                                    identityToken,
                                    true
                                ) as UserNameIdentityToken;
                            break;
                        case UserTokenType.Certificate:
                            token =
                                BaseVariableState.DecodeExtensionObject(
                                    null,
                                    typeof(X509IdentityToken),
                                    identityToken,
                                    true
                                ) as X509IdentityToken;
                            break;
                        case UserTokenType.IssuedToken:
                            token =
                                BaseVariableState.DecodeExtensionObject(
                                    null,
                                    typeof(IssuedIdentityToken),
                                    identityToken,
                                    true
                                ) as IssuedIdentityToken;
                            break;
                        default:
                            throw ServiceResultException.Create(
                                StatusCodes.BadUserAccessDenied,
                                "Invalid user identity token provided.");
                    }
                }
                else
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadUserAccessDenied,
                        "Invalid user identity token provided.");
                }
            }
            else
            {
                // get the token.
                token = (UserIdentityToken)identityToken.Body;
            }

            // find the user token policy.
            policy = EndpointDescription.FindUserTokenPolicy(
                token.PolicyId,
                EndpointDescription.SecurityPolicyUri);

            if (policy == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenInvalid,
                    "User token policy not supported.");
            }

            if (token is IssuedIdentityToken issuedToken &&
                policy.IssuedTokenType == Profiles.JwtUserToken)
            {
                issuedToken.IssuedTokenType = IssuedTokenType.JWT;
            }

            // determine the security policy uri.
            string securityPolicyUri = policy.SecurityPolicyUri;

            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                securityPolicyUri = EndpointDescription.SecurityPolicyUri;
            }

            if (ServerBase.RequireEncryption(EndpointDescription))
            {
                // decrypt the token.
                if (m_serverCertificate == null)
                {
                    m_serverCertificate = X509CertificateLoader.LoadCertificate(
                        EndpointDescription.ServerCertificate);

                    // check for valid certificate.
                    if (m_serverCertificate == null)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadConfigurationError,
                            "ApplicationCertificate cannot be found.");
                    }
                }

                try
                {
                    token.Decrypt(
                        m_serverCertificate,
                        m_serverNonce,
                        securityPolicyUri,
                        m_server.MessageContext,
                        m_eccUserTokenNonce,
                        ClientCertificate,
                        m_clientIssuerCertificates);
                }
                catch (Exception e) when (e is not ServiceResultException)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadIdentityTokenInvalid,
                        e,
                        "Could not decrypt identity token.");
                }

                // verify the signature.
                if (securityPolicyUri != SecurityPolicies.None)
                {
                    byte[] dataToSign = Utils.Append(
                        m_serverCertificate.RawData,
                        m_serverNonce.Data);

                    if (!token.Verify(dataToSign, userTokenSignature, securityPolicyUri, m_server.Telemetry))
                    {
                        // verify for certificate chain in endpoint.
                        // validate the signature with complete chain if the check with leaf certificate failed.
                        X509Certificate2Collection serverCertificateChain =
                            Utils.ParseCertificateChainBlob(
                                EndpointDescription.ServerCertificate,
                                m_server.Telemetry);

                        if (serverCertificateChain.Count > 1)
                        {
                            var serverCertificateChainList = new List<byte>();

                            for (int i = 0; i < serverCertificateChain.Count; i++)
                            {
                                serverCertificateChainList.AddRange(
                                    serverCertificateChain[i].RawData);
                            }

                            byte[] serverCertificateChainData = [.. serverCertificateChainList];

                            dataToSign = Utils.Append(
                                serverCertificateChainData,
                                m_serverNonce.Data);

                            if (!token.Verify(dataToSign, userTokenSignature, securityPolicyUri, m_server.Telemetry))
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadIdentityTokenRejected,
                                    "Invalid user signature!");
                            }
                        }
                        else
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadIdentityTokenRejected,
                                "Invalid user signature!");
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
        /// <exception cref="ArgumentNullException"><paramref name="identityToken"/> is <c>null</c>.</exception>
        private bool UpdateUserIdentity(
            UserIdentityToken identityToken,
            IUserIdentity identity,
            IUserIdentity effectiveIdentity)
        {
            if (identityToken == null)
            {
                throw new ArgumentNullException(nameof(identityToken));
            }

            lock (m_lock)
            {
                bool changed = EffectiveIdentity == null && effectiveIdentity != null;

                if (EffectiveIdentity != null)
                {
                    changed = !EffectiveIdentity.Equals(effectiveIdentity);
                }

                // always save the new identity since it may have additional information that does not affect equality.
                IdentityToken = identityToken;
                Identity = identity;
                EffectiveIdentity = effectiveIdentity;

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
        /// <exception cref="ServiceResultException"></exception>
        private void UpdateDiagnosticCounters(
            RequestType requestType,
            bool error,
            bool authorizationError)
        {
            lock (DiagnosticsLock)
            {
                if (!error)
                {
                    SessionDiagnostics.ClientLastContactTime = DateTime.UtcNow;
                }

                SessionDiagnostics.TotalRequestCount.TotalCount++;

                if (error)
                {
                    SessionDiagnostics.TotalRequestCount.ErrorCount++;

                    if (authorizationError)
                    {
                        SessionDiagnostics.UnauthorizedRequestCount++;
                    }
                }

                ServiceCounterDataType counter = null;

                switch (requestType)
                {
                    case RequestType.Read:
                        counter = SessionDiagnostics.ReadCount;
                        break;
                    case RequestType.HistoryRead:
                        counter = SessionDiagnostics.HistoryReadCount;
                        break;
                    case RequestType.Write:
                        counter = SessionDiagnostics.WriteCount;
                        break;
                    case RequestType.HistoryUpdate:
                        counter = SessionDiagnostics.HistoryUpdateCount;
                        break;
                    case RequestType.Call:
                        counter = SessionDiagnostics.CallCount;
                        break;
                    case RequestType.CreateMonitoredItems:
                        counter = SessionDiagnostics.CreateMonitoredItemsCount;
                        break;
                    case RequestType.ModifyMonitoredItems:
                        counter = SessionDiagnostics.ModifyMonitoredItemsCount;
                        break;
                    case RequestType.SetMonitoringMode:
                        counter = SessionDiagnostics.SetMonitoringModeCount;
                        break;
                    case RequestType.SetTriggering:
                        counter = SessionDiagnostics.SetTriggeringCount;
                        break;
                    case RequestType.DeleteMonitoredItems:
                        counter = SessionDiagnostics.DeleteMonitoredItemsCount;
                        break;
                    case RequestType.CreateSubscription:
                        counter = SessionDiagnostics.CreateSubscriptionCount;
                        break;
                    case RequestType.ModifySubscription:
                        counter = SessionDiagnostics.ModifySubscriptionCount;
                        break;
                    case RequestType.SetPublishingMode:
                        counter = SessionDiagnostics.SetPublishingModeCount;
                        break;
                    case RequestType.Publish:
                        counter = SessionDiagnostics.PublishCount;
                        break;
                    case RequestType.Republish:
                        counter = SessionDiagnostics.RepublishCount;
                        break;
                    case RequestType.TransferSubscriptions:
                        counter = SessionDiagnostics.TransferSubscriptionsCount;
                        break;
                    case RequestType.DeleteSubscriptions:
                        counter = SessionDiagnostics.DeleteSubscriptionsCount;
                        break;
                    case RequestType.AddNodes:
                        counter = SessionDiagnostics.AddNodesCount;
                        break;
                    case RequestType.AddReferences:
                        counter = SessionDiagnostics.AddReferencesCount;
                        break;
                    case RequestType.DeleteNodes:
                        counter = SessionDiagnostics.DeleteNodesCount;
                        break;
                    case RequestType.DeleteReferences:
                        counter = SessionDiagnostics.DeleteReferencesCount;
                        break;
                    case RequestType.Browse:
                        counter = SessionDiagnostics.BrowseCount;
                        break;
                    case RequestType.BrowseNext:
                        counter = SessionDiagnostics.BrowseNextCount;
                        break;
                    case RequestType.TranslateBrowsePathsToNodeIds:
                        counter = SessionDiagnostics.TranslateBrowsePathsToNodeIdsCount;
                        break;
                    case RequestType.QueryFirst:
                        counter = SessionDiagnostics.QueryFirstCount;
                        break;
                    case RequestType.QueryNext:
                        counter = SessionDiagnostics.QueryNextCount;
                        break;
                    case RequestType.RegisterNodes:
                        counter = SessionDiagnostics.RegisterNodesCount;
                        break;
                    case RequestType.UnregisterNodes:
                        counter = SessionDiagnostics.UnregisterNodesCount;
                        break;
                    case RequestType.Unknown:
                    case RequestType.FindServers:
                    case RequestType.GetEndpoints:
                    case RequestType.CreateSession:
                    case RequestType.ActivateSession:
                    case RequestType.CloseSession:
                    case RequestType.Cancel:
                        break;
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected RequestType {requestType}");
                }

                if (counter != null)
                {
                    counter.TotalCount++;

                    if (error)
                    {
                        counter.ErrorCount++;
                    }

                    m_server.SessionManager.UpdateSessionDiagnostics(this);
                }
            }
        }

        private readonly Lock m_lock = new();
        private readonly ILogger m_logger;
        private readonly IServerInternal m_server;
        private readonly string m_sessionName;
        private X509Certificate2 m_serverCertificate;
        private Nonce m_serverNonce;
        private string m_eccUserTokenSecurityPolicyUri;
        private Nonce m_eccUserTokenNonce;
        private readonly X509Certificate2Collection m_clientIssuerCertificates;
        private readonly int m_maxHistoryContinuationPoints;
        private readonly SessionSecurityDiagnosticsDataType m_securityDiagnostics;
        private List<ContinuationPoint> m_browseContinuationPoints;
        private List<HistoryContinuationPoint> m_historyContinuationPoints;
    }
}
