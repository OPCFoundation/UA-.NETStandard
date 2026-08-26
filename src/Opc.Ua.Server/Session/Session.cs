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
            m_securityPolicies =
                (server as ISecurityPolicyRegistryProvider)?.SecurityPolicyRegistry
                ?? SecurityPolicies.Default;
            m_logger = server.Telemetry.CreateLogger<Session>();
            m_eventLogger = server.Telemetry.CreateLogger(
                ServerCompatibilityEventIds.CategoryName);
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

            // initialize security diagnostics. The Session has no authenticated
            // user until it is activated, so ClientUserIdHistory stays empty until
            // ActivateSession records the first ClientUserId (OPC 10000-5).
            m_securityDiagnostics = new SessionSecurityDiagnosticsDataType
            {
                SessionId = Id,
                ClientUserIdOfSession = null,
                AuthenticationMechanism = Identity.TokenType.ToString(),
                Encoding = context.ChannelContext.MessageEncoding.ToString()
            };

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
        /// <exception cref="InvalidOperationException"></exception>
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
        /// Applies an update to the session diagnostics while holding the session's
        /// diagnostics lock.
        /// </summary>
        /// <remarks>
        /// Replaces the former <c>DiagnosticsLock</c> property. The session owns its lock
        /// and never hands it out, so callers cannot participate in - or deadlock against -
        /// the server's locking order. The update runs on the caller's thread; keep it
        /// short and free of I/O or callbacks into the server.
        /// </remarks>
        /// <param name="update">The mutation to apply to the diagnostics.</param>
        /// <exception cref="ArgumentNullException">Thrown if update is null.</exception>
        public void UpdateDiagnostics(Action<SessionDiagnosticsDataType> update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            lock (m_diagnosticsLock)
            {
                update.Invoke(SessionDiagnostics);
            }
        }

        /// <summary>
        /// Reads a value derived from the session diagnostics while holding the session's
        /// diagnostics lock.
        /// </summary>
        /// <remarks>
        /// Use this to take a consistent snapshot of the fields needed. Do not let the
        /// diagnostics object itself escape the callback: once the lock is released, any
        /// field read from it is unsynchronized.
        /// </remarks>
        /// <typeparam name="TResult">The type of the value produced.</typeparam>
        /// <param name="read">The projection applied to the diagnostics.</param>
        /// <exception cref="ArgumentNullException">Thrown if read is null.</exception>
        public TResult ReadDiagnostics<TResult>(Func<SessionDiagnosticsDataType, TResult> read)
        {
            if (read == null)
            {
                throw new ArgumentNullException(nameof(read));
            }

            lock (m_diagnosticsLock)
            {
                return read.Invoke(SessionDiagnostics);
            }
        }

        /// <summary>
        /// The diagnostics associated with the session.
        /// </summary>
        /// <remarks>
        /// Not on <see cref="ISession"/>: it is the mutable structure the diagnostics lock
        /// protects, so handing it out lets a caller read a field the owner may be writing.
        /// Callers reach values through <see cref="ReadDiagnostics{TResult}"/>, or through
        /// <see cref="SessionName"/> and <see cref="ClientApplicationUri"/> for the two the
        /// server itself needs.
        /// </remarks>
        public SessionDiagnosticsDataType SessionDiagnostics { get; }

        /// <inheritdoc/>
        /// <remarks>
        /// Read from the field rather than from the diagnostics: it is assigned once during
        /// construction and never changes, so no lock is involved.
        /// </remarks>
        public string SessionName => m_sessionName;

        /// <inheritdoc/>
        public string? ClientApplicationUri
            => ReadDiagnostics(diagnostics => diagnostics.ClientDescription?.ApplicationUri);

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
                lock (m_diagnosticsLock)
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
                lock (m_diagnosticsLock)
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
                lock (m_diagnosticsLock)
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
        /// Whether the session is being closed. Closing is entered once and never left, so a
        /// Session that started closing never serves new work again.
        /// </summary>
        public bool IsClosing => Volatile.Read(ref m_closing) != 0;

        /// <summary>
        /// Marks the session as being closed. The mark is one way: once a close has started the
        /// Session is on its way out, so nothing that would create new state for it is accepted
        /// again, even if the close itself fails.
        /// </summary>
        /// <returns>
        /// <c>true</c> when this call transitioned the session into the closing state;
        /// <c>false</c> when the session was already closing.
        /// </returns>
        internal bool MarkClosing()
        {
            return Interlocked.Exchange(ref m_closing, 1) == 0;
        }

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
                    lock (m_diagnosticsLock)
                    {
                        SessionDiagnostics.LocaleIds = [.. localeIds];
                    }

                    return true;
                }

                return false;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<(
            IUserIdentityTokenHandler IdentityToken,
            UserTokenPolicy? UserTokenPolicy)> ValidateBeforeActivateAsync(
                OperationContext context,
                SignatureData clientSignature,
                ExtensionObject userIdentityToken,
                SignatureData userTokenSignature,
                CancellationToken cancellationToken)
        {
            lock (m_lock)
            {
                ValidateChannelBeforeActivate(context, clientSignature);
            }

            (IUserIdentityTokenHandler identityToken, UserTokenPolicy? userTokenPolicy) =
                await ValidateUserIdentityTokenAsync(
                    context,
                    userIdentityToken,
                    userTokenSignature,
                    cancellationToken).ConfigureAwait(false);

            TraceState("VALIDATED");
            return (identityToken, userTokenPolicy);
        }

        private void ValidateChannelBeforeActivate(
            OperationContext context,
            SignatureData clientSignature)
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
            if (EndpointDescription.SecurityPolicyUri != SecurityPolicies.None &&
                (ClientCertificate == null ||
                    clientSignature == null ||
                    clientSignature.Signature.IsEmpty))
            {
                throw new ServiceResultException(
                    StatusCodes.BadApplicationSignatureInvalid);
            }

            if (ClientCertificate != null)
            {
                SecurityPolicyInfo securityPolicy = m_securityPolicies.GetInfo(
                    EndpointDescription.SecurityPolicyUri!)!;

                byte[] clientNonceData = ClientNonce.ToArray();

                byte[] dataToSign = securityPolicy.GetClientSignatureData(
                    context.ChannelContext.ChannelThumbprint,
                    m_serverNonce.Data,
                    m_serverCertificate.RawData,
                    context.ChannelContext.ServerChannelCertificate,
                    context.ChannelContext.ClientChannelCertificate,
                    clientNonceData);

                if (!m_securityPolicies.VerifySignatureData(
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

                        if (!m_securityPolicies.VerifySignatureData(
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

            if (!Activated && SecureChannelId != context.ChannelContext.SecureChannelId)
            {
                throw new ServiceResultException(StatusCodes.BadSecureChannelIdInvalid);
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
                lock (m_diagnosticsLock)
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

        /// <inheritdoc/>
        public ISessionContinuationPoints ContinuationPoints => m_continuationPoints;

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
            if (!m_eventLogger.IsEventLogEnabled())
            {
                return;
            }

            string sessionId = Id.ToString();

            m_eventLogger.CompatibilitySessionState(
                context,
                sessionId,
                m_sessionName,
                SecureChannelId,
                Identity?.DisplayName ?? "(none)");
        }

        /// <summary>
        /// Returns a copy of the current diagnostics.
        /// </summary>
        /// <remarks>
        /// <c>copy: true</c> is what makes it a copy: the default overload wraps the live
        /// structure without copying it, so the caller would read the fields after the lock
        /// was released and see them change under it.
        /// </remarks>
        private ServiceResult OnUpdateDiagnostics(
            ISystemContext context,
            NodeState node,
            ref Variant value)
        {
            lock (m_diagnosticsLock)
            {
                value = Variant.FromStructure(SessionDiagnostics, copy: true);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns a copy of the current security diagnostics.
        /// </summary>
        /// <remarks>
        /// See <see cref="OnUpdateDiagnostics"/> for why the copy is not optional.
        /// </remarks>
        private ServiceResult OnUpdateSecurityDiagnostics(
            ISystemContext context,
            NodeState node,
            ref Variant value)
        {
            lock (m_diagnosticsLock)
            {
                value = Variant.FromStructure(m_securityDiagnostics, copy: true);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Validates the identity token supplied by the client.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private IUserIdentityTokenHandler ValidateUserIdentityToken(
            ExtensionObject identityToken,
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
                token = decodedToken.AsTokenHandler(m_securityPolicies);
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

                token = userToken.AsTokenHandler(m_securityPolicies)!;
            }

            // find the user token policy.
            policy = EndpointDescription.FindUserTokenPolicy(
                token.Token.PolicyId!,
                EndpointDescription.SecurityPolicyUri!) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenInvalid,
                    "User token policy not supported.");

            token.UpdatePolicy(policy);

            if (ServerBase.RequireEncryption(EndpointDescription))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "Secure user identity validation requires the asynchronous activation path.");
            }

            // validate user identity token.
            return token;
        }

        private async ValueTask<(
            IUserIdentityTokenHandler IdentityToken,
            UserTokenPolicy? UserTokenPolicy)> ValidateUserIdentityTokenAsync(
                OperationContext context,
                ExtensionObject identityToken,
                SignatureData userTokenSignature,
                CancellationToken cancellationToken)
        {
            UserTokenPolicy? policy = null;

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

                return (AnonymousIdentityTokenHandler.Create(policy!), policy);
            }

            IUserIdentityTokenHandler token;
            // check for unrecognized token.
            if (identityToken.TryGetValue(out UserIdentityToken? decodedToken))
            {
                token = decodedToken.AsTokenHandler(m_securityPolicies);
            }
            else
            {
                // handle the use case when the UserIdentityToken is binary encoded over xml message encoding
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
                        "Opc.Ua.Server.Session.ValidateUserIdentityTokenAsync");

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

                token = userToken.AsTokenHandler(m_securityPolicies)!;
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
                m_serverCertificate ??= Certificate.FromRawData(
                    EndpointDescription.ServerCertificate) ??
                    throw ServiceResultException.ConfigurationError(
                        "ApplicationCertificate cannot be found.");

                try
                {
                    await token.DecryptAsync(
                        m_serverCertificate,
                        m_serverNonce,
                        securityPolicyUri!,
                        m_server.MessageContext,
                        m_userTokenNonce,
                        ClientCertificate,
                        m_clientIssuerCertificates,
                        ct: cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                    when (e is not ServiceResultException and not OperationCanceledException)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadIdentityTokenInvalid,
                        e,
                        "Could not decrypt identity token.");
                }

                // verify the signature.
                if (securityPolicyUri != SecurityPolicies.None)
                {
                    SecurityPolicyInfo securityPolicy = m_securityPolicies.GetInfo(
                        securityPolicyUri!)!;

                    SecureChannelContext channelContext = context.ChannelContext!;
                    byte[] clientNonceData = ClientNonce.ToArray();

                    byte[] dataToSign = securityPolicy.GetUserTokenSignatureData(
                        channelContext.ChannelThumbprint,
                        m_serverNonce.Data,
                        m_serverCertificate.RawData,
                        channelContext.ServerChannelCertificate,
                        ClientCertificate?.RawData,
                        channelContext.ClientChannelCertificate,
                        clientNonceData);

                    if (!await token.VerifyAsync(
                            dataToSign,
                            userTokenSignature,
                            securityPolicyUri!,
                            cancellationToken).ConfigureAwait(false))
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

                            if (!await token.VerifyAsync(
                                    dataToSign,
                                    userTokenSignature,
                                    securityPolicyUri!,
                                    cancellationToken).ConfigureAwait(false))
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

            return (token, policy);
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
                lock (m_diagnosticsLock)
                {
                    string? clientUserId = ClientUserIdResolver.Resolve(
                        identityToken,
                        identity);
                    m_securityDiagnostics.ClientUserIdOfSession = clientUserId;
                    m_securityDiagnostics.AuthenticationMechanism = identity.TokenType.ToString();
                    m_securityDiagnostics.ClientUserIdHistory =
                        m_securityDiagnostics.ClientUserIdHistory.AddItem(clientUserId!);
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

            lock (m_diagnosticsLock)
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

        /// <summary>
        /// Guards the session and security diagnostics, and the last-contact tick count that
        /// is updated alongside them. Never exposed: callers reach the diagnostics through
        /// <see cref="UpdateDiagnostics"/> and <see cref="ReadDiagnostics{TResult}"/>.
        /// </summary>
        private readonly Lock m_diagnosticsLock = new();
        private int m_closing;
        private readonly ILogger m_logger;
        private readonly ILogger m_eventLogger;
        private readonly IServerInternal m_server;
        private readonly TimeProvider m_timeProvider;
        private readonly ISecurityPolicyRegistry m_securityPolicies;
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

    /// <summary>
    /// Source-generated log messages for Session.
    /// </summary>
    internal static partial class SessionLog
    {
        [LoggerMessage(
            EventId = ServerCompatibilityEventIds.SessionState,
            EventName = "SessionState",
            Level = LogLevel.Information,
            Message = "Session {Context}, Id={SessionId}, Name={SessionName}, ChannelId={SecureChannelId}, " +
                "User={Identity}")]
        public static partial void CompatibilitySessionState(
            this ILogger logger,
            string context,
            string sessionId,
            string sessionName,
            string secureChannelId,
            string identity);
    }

}
