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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client
{
    public partial class Session : IReconnectParticipant, IRecreateAwareReconnectParticipant
    {
        /// <summary>
        /// Stable participant identifier used by
        /// <see cref="IClientChannelManager"/> for diagnostics. Set
        /// during construction.
        /// </summary>
        /// <remarks>
        /// The <c>"Session-"</c> prefix is significant: the channel
        /// manager strips the suffix when emitting the bounded
        /// <c>participant</c> metric tag, so the metric stays at
        /// per-kind cardinality (one series per "Session", "Client",
        /// …) instead of accumulating one permanent series per session
        /// instance. The full per-instance identifier is preserved on
        /// distributed-trace Activity tags and EventSource events so
        /// individual sessions remain correlatable in traces.
        /// </remarks>
        public string ParticipantId { get; } = "Session-" + Guid.NewGuid().ToString("N");

        /// <inheritdoc/>
        string IReconnectParticipant.Id => ParticipantId;

        /// <inheritdoc/>
        ConfiguredEndpoint IReconnectParticipant.Endpoint => ConfiguredEndpoint;

        /// <summary>
        /// The managed channel currently bound to this session, or
        /// <c>null</c> if the session was constructed against a raw
        /// <see cref="ITransportChannel"/> without a channel manager.
        /// </summary>
        public IManagedTransportChannel? ManagedChannel => m_managedChannel;

        /// <summary>
        /// The channel manager that owns this session's managed
        /// channel, or <c>null</c> if the session was constructed
        /// against a raw channel.
        /// </summary>
        public IClientChannelManager? ChannelManager => m_channelManager;

        /// <summary>
        /// Internal hook used by <see cref="CreateAsync(IClientChannelManager,
        /// ApplicationConfiguration, ConfiguredEndpoint, bool, bool, string,
        /// uint, IUserIdentity, ArrayOf{string}, ISubscriptionEngineFactory,
        /// TimeProvider, CancellationToken)"/>
        /// to bind a freshly constructed Session to its managed channel
        /// while the channel-manager participant factory is running.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        internal void BindManagedChannel(
            IClientChannelManager manager,
            IManagedTransportChannel channel)
        {
            m_channelManager = manager ?? throw new ArgumentNullException(nameof(manager));
            m_managedChannel = channel ?? throw new ArgumentNullException(nameof(channel));
        }

        /// <inheritdoc/>
        async ValueTask<ParticipantReconnectResult> IReconnectParticipant.OnReconnectAsync(
            IManagedTransportChannel channel,
            int reconnectAttempt,
            CancellationToken ct)
        {
            if (reconnectAttempt < 0)
            {
                // Final shutdown notification from the manager — the
                // channel is going away. Stop the keep-alive timer so
                // the session doesn't keep probing a dead transport.
                try
                {
                    await StopKeepAliveTimerAsync().ConfigureAwait(false);
                }
                catch
                {
                    // best effort
                }
                return ParticipantReconnectResult.Reactivated;
            }

            if (RequiresSessionRecreation(channel))
            {
                return ParticipantReconnectResult.RequiresSessionRecreate;
            }

            try
            {
                // Pass the wrapper back in as the "channel" so the
                // existing legacy path hits the "set channel" no-op
                // branch (the wrapper IS the current channel) and goes
                // straight to ActivateSession. The wrapper's
                // SendRequestAsync bypasses the ready-state gate while
                // the manager is in the participant-reactivation
                // scope, so ActivateSession can complete.
                await ReconnectAsync(connection: null, channel: channel, ct: ct)
                    .ConfigureAwait(false);
                return ParticipantReconnectResult.Reactivated;
            }
            catch (ServiceResultException sre) when (
                sre.StatusCode == StatusCodes.BadApplicationSignatureInvalid ||
                sre.StatusCode == StatusCodes.BadSecurityChecksFailed ||
                sre.StatusCode == StatusCodes.BadIdentityChangeNotSupported ||
                sre.StatusCode == StatusCodes.BadSecureChannelIdInvalid ||
                sre.StatusCode == StatusCodes.BadSessionIdInvalid ||
                sre.StatusCode == StatusCodes.BadSessionClosed ||
                sre.StatusCode == StatusCodes.BadSessionNotActivated)
            {
                m_logger.SessionSessionIdServerSideSessionLost(
                    sre,
                    SessionId);
                return ParticipantReconnectResult.RequiresSessionRecreate;
            }
            catch (ServiceResultException sre) when (
                sre.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                sre.StatusCode == StatusCodes.BadIdentityTokenInvalid ||
                sre.StatusCode == StatusCodes.BadUserAccessDenied ||
                sre.StatusCode == StatusCodes.BadCertificateUntrusted ||
                sre.StatusCode == StatusCodes.BadCertificateInvalid ||
                sre.StatusCode == StatusCodes.BadCertificateUriInvalid)
            {
                m_logger.SessionSessionIdFatalParticipantErrorDuring(
                    sre,
                    SessionId);
                return ParticipantReconnectResult.FatalForParticipant;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.SessionSessionIdTransientReactivationFailure(
                    ex,
                    SessionId);
                return ParticipantReconnectResult.TransientFailure;
            }
        }

        /// <summary>
        /// Determines whether the Session must be recreated rather than
        /// reactivated on the new channel because an Anonymous identity is used
        /// with a Sign-only SecureChannel. OPC 10000-4 §5.7.3.1 states: "If an
        /// Anonymous UserIdentityToken is used, then ActivateSession over a new
        /// SecureChannel shall fail if the SecureChannel is using Sign." The
        /// Session therefore cannot be transferred to the new channel and must be
        /// recreated with a fresh CreateSession/ActivateSession exchange.
        /// </summary>
        private bool RequiresAnonymousSignSessionRecreation()
        {
            return (m_identity?.TokenType ?? UserTokenType.Anonymous) == UserTokenType.Anonymous &&
                m_endpoint.Description.SecurityMode == MessageSecurityMode.Sign;
        }

        private bool RequiresSessionRecreation(ITransportChannel channel)
        {
            if (RequiresAnonymousSignSessionRecreation())
            {
                return true;
            }

            ByteString sessionClientCertificate;
            lock (m_lock)
            {
                sessionClientCertificate = m_sessionClientCertificate;
            }

            // A client certificate on the new channel that differs from the one
            // bound to the Session means the ApplicationInstanceCertificate was
            // rotated. OPC 10000-4 §5.7.3.1 requires the Server to verify that the
            // Certificate used to create the new SecureChannel is the same as the
            // one used for the original SecureChannel; a rotated certificate would
            // make ActivateSession transfer fail (Bad_SecurityChecksFailed), so the
            // Session must be recreated instead of reactivated.
            byte[] channelClientCertificate = channel.ClientChannelCertificate;
            return !sessionClientCertificate.IsEmpty &&
                channelClientCertificate is { Length: > 0 } &&
                sessionClientCertificate != channelClientCertificate.ToByteString();
        }

        async ValueTask IRecreateAwareReconnectParticipant.RecreateAsync(CancellationToken ct)
        {
            IManagedTransportChannel? channel = m_managedChannel;
            if (channel != null)
            {
                await RecreateInPlaceAsync(channel: channel, ct: ct).ConfigureAwait(false);
                return;
            }

            await RecreateInPlaceAsync(ct: ct).ConfigureAwait(false);
        }

        private static ValueTask ReconnectManagedChannelAsync(
            IClientChannelManager manager,
            IManagedTransportChannel channel,
            IRetryBudget? budget,
            CancellationToken ct)
        {
            if (budget == null)
            {
                return manager.ReconnectAsync(channel, ct);
            }

#if NETSTANDARD2_1 || NET8_0_OR_GREATER
            return manager.ReconnectAsync(channel, budget, ct);
#else
            return manager is ClientChannelManager clientChannelManager
                ? clientChannelManager.ReconnectAsync(channel, budget, ct)
                : manager.ReconnectAsync(channel, ct);
#endif
        }

        private IClientChannelManager? m_channelManager;
        private IManagedTransportChannel? m_managedChannel;

        /// <summary>
        /// Creates a new <see cref="Session"/> bound to a centrally
        /// managed <see cref="IClientChannelManager"/>. Multiple
        /// sessions targeting the same endpoint will share the
        /// underlying transport channel; channel reconnect is
        /// coordinated centrally by the manager and is transparent to
        /// the caller.
        /// </summary>
        /// <param name="manager">The channel manager.</param>
        /// <param name="configuration">The client application configuration.</param>
        /// <param name="endpoint">The configured endpoint to connect to.</param>
        /// <param name="updateBeforeConnect">When <c>true</c>, refresh the
        /// endpoint description from the server before creating the
        /// channel.</param>
        /// <param name="checkDomain">When <c>true</c>, validate the server
        /// certificate domain matches the endpoint URL.</param>
        /// <param name="sessionName">The session name.</param>
        /// <param name="sessionTimeout">Session timeout in milliseconds.</param>
        /// <param name="identity">Optional user identity (defaults to
        /// anonymous).</param>
        /// <param name="preferredLocales">Preferred locales.</param>
        /// <param name="engineFactory">Optional subscription engine
        /// factory (defaults to the classic engine).</param>
        /// <param name="timeProvider">Optional <see cref="TimeProvider"/>.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The opened session.</returns>
        /// <exception cref="ArgumentNullException">A required argument is
        /// <c>null</c>.</exception>
        public static async Task<Session> CreateAsync(
            IClientChannelManager manager,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect = true,
            bool checkDomain = false,
            string sessionName = "Session",
            uint sessionTimeout = 60000,
            IUserIdentity? identity = null,
            ArrayOf<string> preferredLocales = default,
            ISubscriptionEngineFactory? engineFactory = null,
            TimeProvider? timeProvider = null,
            CancellationToken ct = default)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            // Refresh endpoint description from server if requested.
            // The channel manager does not perform discovery on its
            // own, so we do it here before key computation/channel
            // acquisition.
            ServiceMessageContext probeContext = configuration.CreateMessageContext();
            if (updateBeforeConnect)
            {
                await endpoint
                    .UpdateFromServerAsync(probeContext.Telemetry, ct)
                    .ConfigureAwait(false);
            }

            // Load the client instance certificate up-front so the
            // manager can incorporate its thumbprint into the
            // ChannelKey and so the channel can be opened with a
            // matching cert if security is enabled.
            string secPolicy = endpoint.Description.SecurityPolicyUri ?? SecurityPolicies.None;
            if (secPolicy != SecurityPolicies.None)
            {
                using CertificateEntry clientEntry = await LoadInstanceCertificateEntryAsync(
                    configuration, secPolicy, probeContext.Telemetry, ct)
                    .ConfigureAwait(false);
#pragma warning disable CA2000 // ownership of the chain transfers to the channel manager, which disposes it
                manager.UpdateClientCertificate(
                    clientEntry.Certificate.AddRef(),
                    configuration.SecurityConfiguration.SendCertificateChain
                        ? BuildTransportChain(clientEntry)
                        : null);
#pragma warning restore CA2000
            }

            Session? session = null;
            IManagedTransportChannel? managedChannel = null;
            try
            {
                managedChannel = await manager.GetAsync(
                    endpoint,
                    channel =>
                    {
                        session = new Session(
                            channel,
                            configuration,
                            endpoint,
                            probeContext,
                            engineFactory,
                            timeProvider);
                        session.BindManagedChannel(manager, channel);
                        return session;
                    },
                    reverseConnection: null,
                    ct)
                    .ConfigureAwait(false);

                Session activeSession = session
                    ?? throw new InvalidOperationException("Participant factory did not create a session.");
                UserIdentity? tempIdentity = identity == null ? new UserIdentity() : null;
                try
                {
                    await activeSession
                        .OpenAsync(
                            sessionName,
                            sessionTimeout,
                            identity ?? tempIdentity!,
                            preferredLocales,
                            checkDomain,
                            ct)
                        .ConfigureAwait(false);
                }
                finally
                {
                    tempIdentity = null;
                }
                return activeSession;
            }
            catch
            {
                if (session != null)
                {
                    session.Dispose();
                }
                else
                {
                    managedChannel?.Dispose();
                }
                throw;
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="Session"/>.
    /// </summary>
    internal static partial class SessionLog
    {
        [LoggerMessage(EventId = ClientEventIds.Session + 0, Level = LogLevel.Information,
            Message = "Session {SessionId}: server-side session lost during reactivation; scheduling recreate.")]
        public static partial void SessionSessionIdServerSideSessionLost(
            this ILogger logger,
            Exception? exception,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.Session + 1, Level = LogLevel.Warning,
            Message = "Session {SessionId}: fatal participant error during reactivation.")]
        public static partial void SessionSessionIdFatalParticipantErrorDuring(
            this ILogger logger,
            Exception? exception,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.Session + 2, Level = LogLevel.Warning,
            Message = "Session {SessionId}: transient reactivation failure.")]
        public static partial void SessionSessionIdTransientReactivationFailure(
            this ILogger logger,
            Exception? exception,
            NodeId? sessionId);
    }

}
