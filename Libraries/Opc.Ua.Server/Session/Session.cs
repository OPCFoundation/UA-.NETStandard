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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

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
            Certificate serverCertificate,
            NodeId authenticationToken,
            ByteString clientNonce,
            Nonce serverNonce,
            string sessionName,
            ApplicationDescription clientDescription,
            string endpointUrl,
            Certificate clientCertificate,
            CertificateCollection clientCertificateChain,
            double sessionTimeout,
            int maxBrowseContinuationPoints,
            int maxHistoryContinuationPoints)
            : this(
                context,
                server,
                serverCertificate,
                authenticationToken,
                clientNonce,
                serverNonce,
                sessionName,
                clientDescription,
                endpointUrl,
                clientCertificate,
                clientCertificateChain,
                sessionTimeout,
                maxBrowseContinuationPoints,
                maxHistoryContinuationPoints,
                timeProvider: null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Session"/> class with an
        /// explicit <see cref="TimeProvider"/>.
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
        /// <param name="timeProvider">
        /// Optional <see cref="TimeProvider"/> used for monotonic timeout
        /// calculations and last-contact diagnostics. When <c>null</c>, the
        /// time provider exposed by the server (via
        /// <see cref="ITimeProviderProvider"/>) is used, falling back to
        /// <see cref="TimeProvider.System"/>.
        /// </param>
        public Session(
            OperationContext context,
            IServerInternal server,
            Certificate serverCertificate,
            NodeId authenticationToken,
            ByteString clientNonce,
            Nonce serverNonce,
            string sessionName,
            ApplicationDescription clientDescription,
            string endpointUrl,
            Certificate clientCertificate,
            CertificateCollection clientCertificateChain,
            double sessionTimeout,
            int maxBrowseContinuationPoints,
            int maxHistoryContinuationPoints,
            TimeProvider? timeProvider)
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
            m_timeProvider = timeProvider
                ?? (server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;
            m_logger = server.Telemetry.CreateLogger<Session>();
            ClientNonce = clientNonce;
            m_serverNonce = serverNonce;
            m_sessionName = sessionName;
            // The session owns an independent ref-counted handle on the server
            // certificate so it stays valid for the whole session lifetime even
            // if the certificate registry is updated.
            m_serverCertificate = serverCertificate.AddRef();
            ClientCertificate = clientCertificate;

            m_clientIssuerCertificates = clientCertificateChain;

            SecureChannelId = context.ChannelContext.SecureChannelId;
            m_continuationPoints = new SessionContinuationPoints(
                () => Id,
                maxBrowseContinuationPoints,
                maxHistoryContinuationPoints,
                server.SubscriptionStore as IContinuationPointStore);
            EndpointDescription = context.ChannelContext.EndpointDescription!;

            // use anonymous the default identity.
            Identity = new UserIdentity();

            // initialize diagnostics.
            DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;
            m_lastContactTickCount = m_timeProvider.GetTimestampMilliseconds();
            SessionDiagnostics = new SessionDiagnosticsDataType
            {
                SessionId = default,
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
            m_securityDiagnostics.ClientUserIdHistory =
                m_securityDiagnostics.ClientUserIdHistory.AddItem(Identity.DisplayName);

            EndpointDescription? description = context.ChannelContext.EndpointDescription;

            if (description != null)
            {
                m_securityDiagnostics.TransportProtocol = new Uri(description.EndpointUrl!).Scheme;
                m_securityDiagnostics.SecurityMode = EndpointDescription.SecurityMode;
                m_securityDiagnostics.SecurityPolicyUri = EndpointDescription.SecurityPolicyUri;
            }

            if (clientCertificate != null)
            {
                m_securityDiagnostics.ClientCertificate = clientCertificate.RawData.ToByteString();
            }
        }

        /// <summary>
        /// Completes session creation by registering the session diagnostics
        /// node in the address space. This is the asynchronous part of session
        /// creation and must be awaited after construction (the
        /// <see cref="SessionManager"/> does this); it sets <see cref="Id"/>.
        /// </summary>
        /// <param name="context">The operation context of the create request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask InitializeAsync(
            OperationContext context,
            CancellationToken cancellationToken = default)
        {
            // One-shot: session creation completes exactly once. Guard against a
            // second invocation (InitializeAsync is on the public ISession
            // interface) re-registering the diagnostics node and overwriting Id.
            if (!Id.IsNull)
            {
                throw new InvalidOperationException("The session has already been initialized.");
            }

            ServerSystemContext systemContext = m_server.DefaultSystemContext.Copy(context);

            // create diagnostics object.
            Id = await m_server.DiagnosticsNodeManager.CreateSessionDiagnosticsAsync(
                systemContext,
                SessionDiagnostics,
                OnUpdateDiagnostics,
                m_securityDiagnostics,
                OnUpdateSecurityDiagnostics,
                cancellationToken).ConfigureAwait(false);

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
                m_continuationPoints.Clear();
                m_userTokenNonce?.Dispose();
                m_userTokenNonce = null;

                IdentityToken = null!;

                ClientCertificate?.Dispose();
                m_clientIssuerCertificates?.Dispose();
                m_serverCertificate.Dispose();
            }
        }

        /// <summary>
        /// Gets the identifier assigned to the session when it was created.
        /// </summary>
        public NodeId Id { get; private set; }

        /// <summary>
        /// The user identity provided by the client.
        /// </summary>
        public IUserIdentity Identity { get; private set; }

        /// <summary>
        /// The application defined mapping for user identity provided by the client.
        /// </summary>
        public IUserIdentity EffectiveIdentity { get; private set; } = null!;

        /// <inheritdoc/>
        public bool IsIdentityStale => Volatile.Read(ref m_identityStale) != 0;

        /// <inheritdoc/>
        public void MarkIdentityStale()
        {
            Volatile.Write(ref m_identityStale, 1);
        }

        /// <inheritdoc/>
        public void RefreshEffectiveIdentity(IUserIdentity effectiveIdentity)
        {
            if (effectiveIdentity == null)
            {
                throw new ArgumentNullException(nameof(effectiveIdentity));
            }

            lock (m_lock)
            {
                EffectiveIdentity = effectiveIdentity;
                // Clearing the stale flag while holding the session lock
                // ensures any subsequent IsIdentityStale read observes a
                // consistent (refreshed identity, cleared flag) pair.
                Volatile.Write(ref m_identityStale, 0);
            }
        }

        /// <summary>
        /// The user identity token provided by the client.
        /// </summary>
        public IUserIdentityTokenHandler IdentityToken { get; private set; } = null!;

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
        public ByteString ClientNonce { get; }

        /// <summary>
        /// The server application instance certificate used by this session.
        /// </summary>
        public Certificate ServerCertificate => m_serverCertificate;

        /// <summary>
        /// The application instance certificate associated with the client.
        /// </summary>
        public Certificate ClientCertificate { get; }

        /// <summary>
        /// The locales requested when the session was created.
        /// </summary>
        public string[] PreferredLocales { get; private set; } = null!;

        /// <summary>
        /// Whether the session timeout has elapsed since the last communication from the client.
        /// </summary>
        public bool HasExpired
        {
            get
            {
                lock (DiagnosticsLock)
                {
                    return m_timeProvider.GetTimestampMilliseconds() - m_lastContactTickCount >
                        (long)SessionDiagnostics.ActualSessionTimeout;
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
                    return (DateTime)SessionDiagnostics.ClientLastContactTime;
                }
            }
        }

        /// <summary>
        /// The monotonic tick count (milliseconds) at the last client contact.
        /// Used for timeout calculations that are immune to system time changes.
        /// </summary>
        public long LastContactTickCount
        {
            get
            {
                lock (DiagnosticsLock)
                {
                    return m_lastContactTickCount;
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
        public virtual void SetUserTokenSecurityPolicy(string securityPolicyUri)
        {
            lock (m_lock)
            {
                m_userTokenSecurityPolicyUri = securityPolicyUri;
                m_userTokenNonce = null;
            }
        }

        /// <summary>
        /// Create new ECC ephemeral key
        /// </summary>
        /// <returns>A new ephemeral key</returns>
        public virtual EphemeralKeyType? GetNewEphemeralKey()
        {
            lock (m_lock)
            {
                if (m_userTokenSecurityPolicyUri == null)
                {
                    return null;
                }

                m_userTokenNonce = Nonce.CreateNonce(m_userTokenSecurityPolicyUri);

                return new EphemeralKeyType
                {
                    PublicKey = m_userTokenNonce.Data.ToByteString(),
                    Signature = CryptoUtils.Sign(
                        new ArraySegment<byte>(m_userTokenNonce.Data!),
                        m_serverCertificate,
                        m_userTokenSecurityPolicyUri).ToByteString()
                };
            }
        }

        /// <summary>
        /// Returns the session's endpoint
        /// </summary>
        public EndpointDescription EndpointDescription { get; } = null!;

        /// <summary>
        /// Returns the session's SecureChannelId
        /// </summary>
        public string SecureChannelId { get; private set; }

        /// <summary>
        /// allow derived classes access
        /// </summary>
        protected int MaxBrowseContinuationPoints
        {
            get => m_continuationPoints.MaxBrowse;
            set => m_continuationPoints.MaxBrowse = value;
        }

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

                    if (requestType == RequestType.CloseSession)
                    {
                        throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                    }

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
                ArrayOf<NodeId> currentRoleIds = EffectiveIdentity?.GrantedRoleIds ?? default;
                if (currentRoleIds.Contains(ObjectIds.WellKnownRole_SecurityAdmin) ||
                    currentRoleIds.Contains(ObjectIds.WellKnownRole_ConfigureAdmin))
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
        public bool UpdateLocaleIds(ArrayOf<string> localeIds)
        {
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
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            out IUserIdentityTokenHandler? identityToken,
            out UserTokenPolicy? userTokenPolicy)
        {
            lock (m_lock)
            {
                // verify that a secure channel was specified.
                if (context.ChannelContext == null)
                {
                    throw new ServiceResultException(StatusCodes.BadSecureChannelIdInvalid);
                }

                // verify that the same security policy has been used.
                EndpointDescription? endpoint = context.ChannelContext.EndpointDescription;

                if (endpoint!.SecurityPolicyUri != EndpointDescription.SecurityPolicyUri ||
                    endpoint.SecurityMode != EndpointDescription.SecurityMode)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityPolicyRejected);
                }

                // verify the client signature.
                if (ClientCertificate != null)
                {
                    if (EndpointDescription.SecurityPolicyUri != SecurityPolicies.None &&
                        clientSignature != null &&
                        clientSignature.Signature.IsEmpty)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadApplicationSignatureInvalid);
                    }

                    SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(EndpointDescription.SecurityPolicyUri!)!;

                    byte[] clientNonceData = ClientNonce.ToArray();

                    byte[] dataToSign = securityPolicy!.GetClientSignatureData(
                        context.ChannelContext.ChannelThumbprint,
                        m_serverNonce.Data,
                        m_serverCertificate.RawData,
                        context.ChannelContext.ServerChannelCertificate,
                        context.ChannelContext.ClientChannelCertificate,
                        clientNonceData);

                    if (!SecurityPolicies.VerifySignatureData(
                            clientSignature!,
                            EndpointDescription.SecurityPolicyUri!,
                            ClientCertificate,
                            dataToSign))
                    {
                        // verify for certificate chain in endpoint.
                        // validate the signature with complete chain if the check with leaf certificate failed.
                        using CertificateCollection serverCertificateChain =
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

                            dataToSign = securityPolicy.GetClientSignatureData(
                                context.ChannelContext.ChannelThumbprint,
                                m_serverNonce.Data,
                                serverCertificateChainData,
                                context.ChannelContext.ServerChannelCertificate,
                                context.ChannelContext.ClientChannelCertificate,
                                clientNonceData);

                            if (!SecurityPolicies.VerifySignatureData(
                                  clientSignature!,
                                  EndpointDescription.SecurityPolicyUri!,
                                  ClientCertificate,
                                  dataToSign))
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

                // validate the user identity token.
                identityToken = ValidateUserIdentityToken(
                    context,
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
            IUserIdentityTokenHandler identityToken,
            IUserIdentity identity,
            IUserIdentity effectiveIdentity,
            ArrayOf<string> localeIds,
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
                    // bind to the new secure channel. Activate is invoked from the
                    // session activation pipeline, which always supplies a channel context.
                    SecureChannelId = context.ChannelContext!.SecureChannelId;

                    TraceState("RE-ACTIVATION");
                }

                // update server nonce.
                m_serverNonce = serverNonce;

                // update the contact time.
                lock (DiagnosticsLock)
                {
                    SessionDiagnostics.ClientLastContactTime = m_timeProvider.GetUtcNow().UtcDateTime;
                    m_lastContactTickCount = m_timeProvider.GetTimestampMilliseconds();
                }

                // indicate whether the user context has changed.
                return changed;
            }
        }

        /// <summary>
        /// Closes a session and removes itself from the address space.
        /// </summary>
        public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            TraceState("CLOSED");

            await m_server.DiagnosticsNodeManager
                .DeleteSessionDiagnosticsAsync(m_server.DefaultSystemContext, Id, cancellationToken)
                .ConfigureAwait(false);
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
            m_continuationPoints.SaveBrowse(continuationPoint);
        }

        /// <summary>
        /// Restores a continuation point for a session.
        /// </summary>
        /// <remarks>
        /// The caller is responsible for disposing the continuation point returned.
        /// </remarks>
        public ContinuationPoint? RestoreContinuationPoint(ByteString continuationPoint)
        {
            return m_continuationPoints.RestoreBrowse(continuationPoint);
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
            m_continuationPoints.SaveHistory(id, continuationPoint);
        }

        /// <summary>
        /// Restores a previously saves history continuation point.
        /// </summary>
        /// <param name="continuationPoint">The identifier for the continuation point.</param>
        /// <returns>The save continuation point. null if not found.</returns>
        public object? RestoreHistoryContinuationPoint(ByteString continuationPoint)
        {
            return m_continuationPoints.RestoreHistory(continuationPoint);
        }

        /// <summary>
        /// Loads mirrored continuation point envelopes for a session restored on a backup replica.
        /// </summary>
        /// <param name="ownerSessionId">The original owner session id from the active replica.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask LoadMirroredContinuationPointsAsync(
            NodeId ownerSessionId,
            CancellationToken cancellationToken = default)
        {
            return m_continuationPoints.LoadMirroredAsync(ownerSessionId, cancellationToken);
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
            ref Variant value)
        {
            lock (DiagnosticsLock)
            {
                value = Variant.FromStructure(SessionDiagnostics);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns a copy of the current security diagnostics.
        /// </summary>
        private ServiceResult OnUpdateSecurityDiagnostics(
            ISystemContext context,
            NodeState node,
            ref Variant value)
        {
            lock (DiagnosticsLock)
            {
                value = Variant.FromStructure(m_securityDiagnostics);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Validates the identity token supplied by the client.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private IUserIdentityTokenHandler ValidateUserIdentityToken(
            OperationContext context,
            ExtensionObject identityToken,
            SignatureData userTokenSignature,
            out UserTokenPolicy? policy)
        {
            policy = null!;

            // check for anonymous (same as empty) token.
            if (identityToken.IsNull ||
                identityToken.TryGetValue(out AnonymousIdentityToken? _))
            {
                // check if an anonymous login is permitted.
                if (!EndpointDescription.UserIdentityTokens.IsEmpty)
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
                return AnonymousIdentityTokenHandler.Create(policy!);
            }

            IUserIdentityTokenHandler token;
            // check for unrecognized token.
            if (identityToken.TryGetValue(out UserIdentityToken? decodedToken))
            {
                // get the token.
                token = decodedToken.AsTokenHandler();
            }
            else
            {
                //handle the use case when the UserIdentityToken is binary encoded over xml message encoding
                if (identityToken.Encoding != ExtensionObjectEncoding.Binary ||
                    !identityToken.TryGetAsBinary(out ByteString _))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadUserAccessDenied,
                        "Invalid user identity token provided.");
                }
                if (BaseVariableState.DecodeExtensionObject(
                        null!,
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
                    newToken.PolicyId!,
                    EndpointDescription.SecurityPolicyUri!) ??
                    throw ServiceResultException.Create(
                        StatusCodes.BadUserAccessDenied,
                        "User token policy not supported.",
                        "Opc.Ua.Server.Session.ValidateUserIdentityToken");

                UserIdentityToken? userToken;
                switch (policy.TokenType)
                {
                    case UserTokenType.Anonymous:
                        userToken = (AnonymousIdentityToken)BaseVariableState.DecodeExtensionObject(
                            null!,
                            typeof(AnonymousIdentityToken),
                            identityToken,
                            true)!;
                        break;
                    case UserTokenType.UserName:
                        userToken = (UserNameIdentityToken)BaseVariableState.DecodeExtensionObject(
                            null!,
                            typeof(UserNameIdentityToken),
                            identityToken,
                            true)!;
                        break;
                    case UserTokenType.Certificate:
                        userToken = (X509IdentityToken)BaseVariableState.DecodeExtensionObject(
                            null!,
                            typeof(X509IdentityToken),
                            identityToken,
                            true)!;
                        break;
                    case UserTokenType.IssuedToken:
                        userToken = (IssuedIdentityToken)BaseVariableState.DecodeExtensionObject(
                            null!,
                            typeof(IssuedIdentityToken),
                            identityToken,
                            true)!;
                        break;
                    default:
                        throw ServiceResultException.Create(
                            StatusCodes.BadUserAccessDenied,
                            "Invalid user identity token provided.");
                }

                token = userToken.AsTokenHandler()!;
            }

            // find the user token policy.
            policy = EndpointDescription.FindUserTokenPolicy(
                token.Token.PolicyId!,
                EndpointDescription.SecurityPolicyUri!) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenInvalid,
                    "User token policy not supported.");

            token.UpdatePolicy(policy);

            // determine the security policy uri.
            string? securityPolicyUri = policy.SecurityPolicyUri;

            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                securityPolicyUri = EndpointDescription.SecurityPolicyUri;
            }

            if (ServerBase.RequireEncryption(EndpointDescription))
            {
                // decrypt the token.
                // check for valid certificate.
                m_serverCertificate ??= Certificate.FromRawData(
                    EndpointDescription.ServerCertificate) ??
                    throw ServiceResultException.ConfigurationError(
                        "ApplicationCertificate cannot be found.");

                try
                {
                    // Sync-completing ValueTask in current implementations;
                    // safe to block. Future async stores will require
                    // hoisting decryption out of the lock.
                    ValueTask decryptTask = token.DecryptAsync(
                        m_serverCertificate,
                        m_serverNonce,
                        securityPolicyUri!,
                        m_server.MessageContext,
                        m_userTokenNonce,
                        ClientCertificate,
                        m_clientIssuerCertificates);
                    if (!decryptTask.IsCompletedSuccessfully)
                    {
                        decryptTask.AsTask().GetAwaiter().GetResult();
                    }
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
                    SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(securityPolicyUri!)!;

                    // ValidateUserIdentityToken runs inside session activation, which
                    // always carries a channel context.
                    SecureChannelContext channelContext = context.ChannelContext!;

                    byte[] clientNonceData = ClientNonce.ToArray();

                    byte[] dataToSign = securityPolicy!.GetUserTokenSignatureData(
                        channelContext.ChannelThumbprint,
                        m_serverNonce.Data,
                        m_serverCertificate.RawData,
                        channelContext.ServerChannelCertificate,
                        ClientCertificate?.RawData,
                        channelContext.ClientChannelCertificate,
                        clientNonceData);

                    if (!VerifySync(token, dataToSign, userTokenSignature, securityPolicyUri!))
                    {
                        // verify for certificate chain in endpoint.
                        // validate the signature with complete chain if the check with leaf certificate failed.
                        using CertificateCollection serverCertificateChain =
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

                            dataToSign = securityPolicy.GetUserTokenSignatureData(
                                channelContext.ChannelThumbprint,
                                m_serverNonce.Data,
                                [.. serverCertificateChainList],
                                channelContext.ServerChannelCertificate,
                                ClientCertificate?.RawData,
                                channelContext.ClientChannelCertificate,
                                clientNonceData);

                            if (!VerifySync(token, dataToSign, userTokenSignature, securityPolicyUri!))
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
        /// Synchronously invokes <see cref="IUserIdentityTokenHandler.VerifyAsync"/>
        /// on the assumption that the underlying implementation completes
        /// synchronously (no real I/O). Used inside locked regions of
        /// <see cref="ValidateBeforeActivate"/> where awaiting is not
        /// possible. Future async-store implementations will require
        /// hoisting verification out of the lock.
        /// </summary>
        private static bool VerifySync(
            IUserIdentityTokenHandler token,
            byte[] dataToSign,
            SignatureData userTokenSignature,
            string securityPolicyUri)
        {
            ValueTask<bool> task = token.VerifyAsync(
                dataToSign,
                userTokenSignature,
                securityPolicyUri);
            return task.IsCompletedSuccessfully
                ? task.Result
                : task.AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Updates the user identity.
        /// </summary>
        /// <returns>true if the new identity is different from the old identity.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="identityToken"/> is <c>null</c>.</exception>
        [MemberNotNull(nameof(EffectiveIdentity), nameof(IdentityToken))]
        private bool UpdateUserIdentity(
            IUserIdentityTokenHandler identityToken,
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
                EffectiveIdentity = effectiveIdentity!;

                // update diagnostics.
                lock (DiagnosticsLock)
                {
                    m_securityDiagnostics.ClientUserIdOfSession = identity.DisplayName;
                    m_securityDiagnostics.AuthenticationMechanism = identity.TokenType.ToString();
                    m_securityDiagnostics.ClientUserIdHistory =
                        m_securityDiagnostics.ClientUserIdHistory.AddItem(identity.DisplayName);
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
            ServiceCounterDataType? counter = null;

            lock (DiagnosticsLock)
            {
                if (!error)
                {
                    SessionDiagnostics.ClientLastContactTime = m_timeProvider.GetUtcNow().UtcDateTime;
                    m_lastContactTickCount = m_timeProvider.GetTimestampMilliseconds();
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
                }
            }

            if (counter != null)
            {
                m_server.SessionManager.RaiseSessionDiagnosticsChangedEvent(this);
            }
        }

        private readonly Lock m_lock = new();
        private readonly ILogger m_logger;
        private readonly IServerInternal m_server;
        private readonly TimeProvider m_timeProvider;
        private readonly string m_sessionName;
        private Certificate m_serverCertificate;
        private Nonce m_serverNonce;
        private string? m_userTokenSecurityPolicyUri;
        private Nonce? m_userTokenNonce;
        private readonly CertificateCollection? m_clientIssuerCertificates;
        private readonly SessionContinuationPoints m_continuationPoints;
        private readonly SessionSecurityDiagnosticsDataType m_securityDiagnostics;
        private long m_lastContactTickCount;
        private int m_identityStale;
    }
}
