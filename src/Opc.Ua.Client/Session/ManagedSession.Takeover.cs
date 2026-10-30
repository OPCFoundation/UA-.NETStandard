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
 *
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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;

namespace Opc.Ua.Client
{
    public partial class ManagedSession : ISessionTakeover
    {
        /// <inheritdoc/>
        /// <remarks>
        /// The managed generation is monotonic for the lifetime of this facade, also when the
        /// inner session object is replaced.
        /// </remarks>
        public long SessionGeneration => Interlocked.Read(ref m_sessionGeneration);

        /// <inheritdoc/>
        public event EventHandler<SessionGenerationChangedEventArgs>? SessionGenerationChanged;

        /// <inheritdoc/>
        public async ValueTask<SessionHandoff> RetireAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (Volatile.Read(ref m_disposalCompletion) != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The session is already disposed or retired.");
            }
            Session session = m_session ?? throw ServiceResultException.Create(
                StatusCodes.BadNotConnected,
                "The managed session is not connected.");
            if (session.SubscriptionEngine is not DefaultSubscriptionEngine
                {
                    SubscriptionManager: SubscriptionManager manager
                })
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "Only a session that uses the default subscription engine can be handed over.");
            }
            if (!StateMachine.IsConnected)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Only a connected session can be handed over.");
            }

            // Claiming the disposal serializes the retirement with Dispose: a concurrent disposal
            // waits for this one and never closes the server session.
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.CompareExchange(ref m_disposalCompletion, completion, null) != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The session is already disposed or retired.");
            }

            SessionHandoff handoff;
            try
            {
                // The refresh loop takes the writer lock and would activate an identity.
                await StopIdentityRefreshLoopAsync().ConfigureAwait(false);
                using (await m_serviceLock.WriterLockAsync(ct).ConfigureAwait(false))
                {
                    if (!ReferenceEquals(m_session, session) || !StateMachine.IsConnected)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadInvalidState,
                            "The session is recovering and cannot be handed over.");
                    }

                    // Stop publishing and subscription updates and wait for the requests in flight,
                    // so the captured subscription state is the one the server session holds.
                    manager.SetSessionRecoveryPaused(true);
                    await manager.DrainAsync(ct).ConfigureAwait(false);
                    List<SubscriptionStateSnapshot> subscriptions = CaptureSubscriptions(manager);

                    // The fence: from here on nothing closes or reactivates the server session.
                    SessionConfiguration saved = await session.RetainServerSessionAsync(ct).ConfigureAwait(false);
                    Interlocked.Exchange(ref m_serverSessionFate, kServerSessionRetained);
                    handoff = new SessionHandoff
                    {
                        Session = saved,
                        Subscriptions = subscriptions,
                        RevisedSessionTimeout = session.SessionTimeout
                    };
                }
            }
            catch
            {
                // Nothing was handed over: the session ends like a normal disposal.
                await ReleaseResourcesAsync(completion, retain: false).ConfigureAwait(false);
                throw;
            }
            await ReleaseResourcesAsync(completion, retain: true).ConfigureAwait(false);
            return handoff;
        }

        /// <summary>
        /// Captures the server session this session is bound to, without subscriptions and
        /// without changing anything. The session keeps using its server session; the result
        /// lets a mirrored replica take it over later.
        /// </summary>
        /// <returns>The captured server session.</returns>
        /// <exception cref="ServiceResultException">The session is not connected.</exception>
        internal SessionHandoff CaptureHandoff()
        {
            Session session = InnerSession;
            session.Snapshot(out SessionConfiguration snapshot);
            return new SessionHandoff
            {
                Session = snapshot with { ConfiguredEndpoint = CoreUtils.Clone(session.ConfiguredEndpoint) },
                RevisedSessionTimeout = session.SessionTimeout
            };
        }

        /// <summary>
        /// Returns <paramref name="handoff"/> with the server session state of
        /// <paramref name="session"/>, which has just activated it, so a later takeover signs the
        /// server nonce of that activation.
        /// </summary>
        private static SessionHandoff RefreshHandoff(SessionHandoff handoff, Session session)
        {
            session.Snapshot(out SessionConfiguration snapshot);
            return handoff with
            {
                Session = snapshot with { ConfiguredEndpoint = handoff.Session.ConfiguredEndpoint }
            };
        }

        /// <summary>
        /// Creates a managed session with the settings of this one that takes over the server
        /// session of <paramref name="handoff"/>. On success the new session also owns the
        /// transport resources this session owned.
        /// </summary>
        /// <param name="handoff">The server session to take over.</param>
        /// <param name="options">The takeover options.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The connected managed session.</returns>
        /// <exception cref="ServiceResultException">This session was not created with settings
        /// that can be reused, or the takeover failed.</exception>
        internal async Task<ManagedSession> CreateTakeoverSessionAsync(
            SessionHandoff handoff,
            SessionTakeoverOptions options,
            CancellationToken ct)
        {
            Func<SessionHandoff, SessionTakeoverOptions, CancellationToken, Task<ManagedSession>> factory =
                m_takeoverFactory ?? throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The session cannot create a session that takes over a server session.");
            ValidateTakeover(handoff, options, null, SessionFactory.Telemetry);
            ManagedSession created = await factory(handoff, options, ct).ConfigureAwait(false);
            created.OwnTransportResources(m_ownedChannelManager, m_ownedTransportResources);
            OwnTransportResources(null, null);
            return created;
        }

        /// <summary>
        /// Validates a takeover request before any connection is made.
        /// </summary>
        /// <param name="handoff">The server session to take over.</param>
        /// <param name="options">The takeover options.</param>
        /// <param name="configured">The endpoint the session is configured for, or null when it
        /// is the endpoint of the handoff.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <exception cref="ArgumentNullException">The handoff has no session.</exception>
        /// <exception cref="ArgumentException">The handoff has subscriptions but the options have
        /// no subscription handler factory.</exception>
        /// <exception cref="ServiceResultException">The saved server session is invalid.</exception>
        internal static void ValidateTakeover(
            SessionHandoff handoff,
            SessionTakeoverOptions options,
            EndpointDescription? configured,
            ITelemetryContext telemetry)
        {
            if (handoff?.Session == null)
            {
                throw new ArgumentNullException(nameof(handoff), "The handoff has no saved session.");
            }
            if (!handoff.Subscriptions.IsEmpty && options.SubscriptionHandlerFactory == null)
            {
                throw new ArgumentException(
                    "A subscription handler factory is required to take over subscriptions.",
                    nameof(options));
            }
            Session.ValidateHandoff(handoff.Session, configured, telemetry);
        }

        /// <summary>
        /// Opens a session on <paramref name="endpoint"/> that takes over the server session of
        /// the pending handoff instead of creating a new one, and attaches the subscriptions of
        /// the handoff to it without any subscription service call.
        /// </summary>
        /// <param name="handoff">The server session to take over.</param>
        /// <param name="endpoint">The endpoint of the server session.</param>
        /// <param name="connection">An optional reverse connection for the channel.</param>
        /// <param name="initialIdentity">The identity the managed session would connect with.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The activated session, or null when the takeover failed and a new server
        /// session must be created instead.</returns>
        /// <exception cref="ServiceResultException">The takeover failed and the failure mode is
        /// <see cref="SessionTakeoverFailureMode.Throw"/>.</exception>
        private async Task<Session?> TakeOverServerSessionAsync(
            SessionHandoff handoff,
            ConfiguredEndpoint endpoint,
            ITransportWaitingConnection? connection,
            IUserIdentity? initialIdentity,
            CancellationToken ct)
        {
            SessionTakeoverOptions options = m_takeoverOptions ?? new SessionTakeoverOptions();
            IUserIdentity identity = options.Identity ??
                initialIdentity ??
                handoff.Session.Identity ??
                new UserIdentity();
            var activated = new StrongBox<SessionHandoff?>();
            try
            {
                Session.ValidateHandoff(handoff.Session, endpoint.Description, SessionFactory.Telemetry);
                return await OpenActivatedSessionAsync(
                    endpoint,
                    connection,
                    (session, token) => ActivateHandoffAsync(session, handoff, options, identity, activated, token),
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                options.FailureMode == SessionTakeoverFailureMode.CreateNewSession &&
                ex is not OperationCanceledException)
            {
                m_logger.ManagedSessionTakeoverContinuityLost(ex, handoff.Subscriptions.Count);
                m_takeoverRecreate = handoff.Subscriptions;
                return null;
            }
            catch (OperationCanceledException)
            {
                // The next attempt retries. Once ActivateSession succeeded, the server expects
                // the nonce it returned then, so the retry uses the refreshed handoff.
                Interlocked.CompareExchange(ref m_pendingHandoff, activated.Value ?? handoff, null);
                throw;
            }
            catch (Exception ex)
            {
                ServiceResult failure = ToAttemptFailure(ex);
                Volatile.Write(ref m_terminalConnectFailure, failure);
                m_takeoverFailed?.TrySetException(ex);
                throw;
            }
        }

        /// <summary>
        /// Creates a session that is bound to its server session by <paramref name="activate"/>.
        /// </summary>
        private async Task<Session> OpenActivatedSessionAsync(
            ConfiguredEndpoint endpoint,
            ITransportWaitingConnection? connection,
            Func<Session, CancellationToken, Task> activate,
            CancellationToken ct)
        {
            ChannelManagerSessionFactory? channelFactory = CreateChannelManagerFactory() ??
                SessionFactory as ChannelManagerSessionFactory;
            if (channelFactory != null)
            {
                return await channelFactory.CreateActivatedAsync(
                    m_configuration,
                    connection,
                    endpoint,
                    m_checkDomain,
                    activate,
                    ct).ConfigureAwait(false);
            }
            if (connection == null && m_reverseConnectManager != null)
            {
                connection = await m_reverseConnectManager.WaitForConnectionAsync(
                    endpoint.EndpointUrl!,
                    endpoint.ReverseConnect?.ServerUri,
                    ct).ConfigureAwait(false);
            }
            if (connection == null && SessionFactory is not DefaultSessionFactory)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "Taking over a server session requires the default session factory or a channel manager.");
            }
            ITransportChannel channel = await SessionFactory.CreateChannelAsync(
                m_configuration,
                connection!,
                endpoint,
                false,
                m_checkDomain,
                ct).ConfigureAwait(false);
            ISession created;
            try
            {
                created = SessionFactory.Create(
                    channel,
                    m_configuration,
                    endpoint,
                    null,
                    null,
                    endpoint.DiscoveryEndpoints);
            }
            catch
            {
                channel.Dispose();
                throw;
            }
            if (created is not Session session)
            {
                created.Dispose();
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The session factory does not create sessions that can take over a server session.");
            }
            try
            {
                session.ReturnDiagnostics = SessionFactory.ReturnDiagnostics;
                await activate(session, ct).ConfigureAwait(false);
            }
            catch
            {
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            return session;
        }

        /// <summary>
        /// Activates the saved server session on <paramref name="session"/> and adopts the
        /// subscriptions of the handoff. On failure the server session is abandoned, never closed.
        /// </summary>
        private static async Task ActivateHandoffAsync(
            Session session,
            SessionHandoff handoff,
            SessionTakeoverOptions options,
            IUserIdentity identity,
            StrongBox<SessionHandoff?> activated,
            CancellationToken ct)
        {
            // Checked before anything is sent: a session that cannot attach the subscriptions
            // must not activate the server session.
            SubscriptionManager? manager = handoff.Subscriptions.IsEmpty ? null : GetSubscriptionManager(session);
            try
            {
                await session.ActivateHandoffAsync(
                    handoff.Session,
                    handoff.RevisedSessionTimeout,
                    identity,
                    () => activated.Value = RefreshHandoff(handoff, session),
                    ct).ConfigureAwait(false);
                if (manager != null)
                {
                    await AttachSubscriptionsAsync(
                        manager,
                        handoff.Subscriptions,
                        options.SubscriptionHandlerFactory!,
                        adopt: true,
                        ct).ConfigureAwait(false);
                }
            }
            catch
            {
                session.AbandonServerSession();
                throw;
            }
        }

        /// <summary>
        /// Recreates the subscriptions of a handoff whose server session could not be taken
        /// over on the new server session.
        /// </summary>
        private async Task RecreateHandoffSubscriptionsAsync(Session session, CancellationToken ct)
        {
            ArrayOf<SubscriptionStateSnapshot> subscriptions = m_takeoverRecreate;
            if (subscriptions.IsEmpty)
            {
                return;
            }
            await AttachSubscriptionsAsync(
                GetSubscriptionManager(session),
                subscriptions,
                m_takeoverOptions!.SubscriptionHandlerFactory!,
                adopt: false,
                ct).ConfigureAwait(false);
            m_takeoverRecreate = default;
        }

        /// <summary>
        /// Gets the subscription manager of the default subscription engine of a session.
        /// </summary>
        /// <exception cref="ServiceResultException">The session uses another engine.</exception>
        private static SubscriptionManager GetSubscriptionManager(Session session)
        {
            return session.SubscriptionEngine is DefaultSubscriptionEngine
                {
                    SubscriptionManager: SubscriptionManager manager
                }
                ? manager
                : throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "Taking over subscriptions requires the default subscription engine.");
        }

        /// <summary>
        /// Attaches saved subscriptions to a subscription manager, regrouping the partitions of
        /// each logical subscription.
        /// </summary>
        /// <param name="manager">The subscription manager.</param>
        /// <param name="snapshots">The saved subscriptions.</param>
        /// <param name="handlerFactory">Creates the handler of each logical subscription.</param>
        /// <param name="adopt">Whether the subscriptions already belong to the server session
        /// of the manager and are only registered locally, or are recreated.</param>
        /// <param name="ct">The cancellation token.</param>
        private static async Task AttachSubscriptionsAsync(
            SubscriptionManager manager,
            ArrayOf<SubscriptionStateSnapshot> snapshots,
            Func<SubscriptionStateSnapshot, ISubscriptionNotificationHandler> handlerFactory,
            bool adopt,
            CancellationToken ct)
        {
            var groups = new Dictionary<string, List<SubscriptionStateSnapshot>>(StringComparer.Ordinal);
            for (int i = 0; i < snapshots.Count; i++)
            {
                SubscriptionStateSnapshot snapshot = snapshots[i];
                if (string.IsNullOrEmpty(snapshot.LogicalGroupId))
                {
                    if (adopt)
                    {
                        await manager.AdoptGroupAsync(handlerFactory(snapshot), [snapshot], ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await manager.RestoreAsync(handlerFactory(snapshot), snapshot, false, ct)
                            .ConfigureAwait(false);
                    }
                    continue;
                }
                if (!groups.TryGetValue(snapshot.LogicalGroupId!, out List<SubscriptionStateSnapshot>? bucket))
                {
                    bucket = [];
                    groups[snapshot.LogicalGroupId!] = bucket;
                }
                bucket.Add(snapshot);
            }
            foreach (KeyValuePair<string, List<SubscriptionStateSnapshot>> entry in groups)
            {
                List<SubscriptionStateSnapshot> ordered = entry.Key.ValidateAndSortGroup(entry.Value);
                if (adopt)
                {
                    await manager.AdoptGroupAsync(handlerFactory(ordered[0]), ordered, ct).ConfigureAwait(false);
                }
                else
                {
                    await manager.RestoreGroupAsync(handlerFactory(ordered[0]), ordered, false, ct)
                        .ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Captures every subscription of a subscription manager, one snapshot per partition.
        /// </summary>
        private static List<SubscriptionStateSnapshot> CaptureSubscriptions(SubscriptionManager manager)
        {
            var result = new List<SubscriptionStateSnapshot>();
            foreach (ISubscription subscription in manager.Items)
            {
                if (subscription is LogicalSubscription logical)
                {
                    result.AddRange(logical.SnapshotAllPartitions());
                }
                else if (subscription is Subscriptions.Subscription concrete)
                {
                    result.Add(concrete.Snapshot());
                }
            }
            return result;
        }

        /// <summary>
        /// Forwards a server session change of the current inner session.
        /// </summary>
        private void OnInnerSessionGenerationChanged(object? sender, SessionGenerationChangedEventArgs e)
        {
            if (ReferenceEquals(sender, m_session))
            {
                ObserveServerSession(e.SessionId);
            }
        }

        /// <summary>
        /// Records the server session the facade is now bound to and raises
        /// <see cref="SessionGenerationChanged"/> when it differs from the previous one.
        /// </summary>
        private void ObserveServerSession(NodeId sessionId)
        {
            SessionGenerationChangedEventArgs args;
            lock (m_sessionGenerationLock)
            {
                if (sessionId.IsNull || sessionId == m_observedServerSessionId)
                {
                    return;
                }
                args = new SessionGenerationChangedEventArgs(
                    Interlocked.Increment(ref m_sessionGeneration), m_observedServerSessionId, sessionId);
                m_observedServerSessionId = sessionId;
            }
            Session.RaiseSessionGenerationChanged(SessionGenerationChanged, args, this, m_logger);
        }

        /// <summary>
        /// The failure a connect, reconnect or failover attempt reports once this session is
        /// retired: it must not create or reactivate any server session.
        /// </summary>
        private ServiceResult? GetRetirementFailure()
        {
            return Volatile.Read(ref m_serverSessionFate) == kServerSessionRetained
                ? new ServiceResult(StatusCodes.BadSessionClosed, new LocalizedText(
                    "The server session was retained for another owner."))
                : null;
        }

        /// <summary>
        /// The server session may be closed or retained.
        /// </summary>
        private const int kServerSessionOpen = 0;

        /// <summary>
        /// A close was committed; the server session can no longer be retained.
        /// </summary>
        private const int kServerSessionClosing = 1;

        /// <summary>
        /// The server session is retained for another owner; nothing may close it.
        /// </summary>
        private const int kServerSessionRetained = 2;

        private readonly Lock m_sessionGenerationLock = new();
        private long m_sessionGeneration;
        private NodeId m_observedServerSessionId;

        /// <summary>
        /// The handoff the initial connect takes over, cleared by the attempt that consumes it.
        /// </summary>
        private SessionHandoff? m_pendingHandoff;

        /// <summary>
        /// The options of the takeover of <see cref="m_pendingHandoff"/>.
        /// </summary>
        private SessionTakeoverOptions? m_takeoverOptions;

        /// <summary>
        /// Completed with the error of a takeover that failed in
        /// <see cref="SessionTakeoverFailureMode.Throw"/> mode, so creation fails right away.
        /// </summary>
        private TaskCompletionSource<bool>? m_takeoverFailed;

        /// <summary>
        /// The failure every later connect attempt reports after such a takeover failure.
        /// </summary>
        private ServiceResult? m_terminalConnectFailure;

        /// <summary>
        /// The subscriptions to recreate on a new server session after a failed takeover.
        /// </summary>
        private ArrayOf<SubscriptionStateSnapshot> m_takeoverRecreate;

        /// <summary>
        /// Creates a managed session with the settings of this one that takes over a server session.
        /// </summary>
        private Func<SessionHandoff, SessionTakeoverOptions, CancellationToken, Task<ManagedSession>>?
            m_takeoverFactory;
    }
}
