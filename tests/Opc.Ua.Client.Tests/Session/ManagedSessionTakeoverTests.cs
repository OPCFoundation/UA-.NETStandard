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
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;
using static Opc.Ua.Client.Tests.Stack.Client.Fakes.ManagedSessionReconnectHarness;
using UaManagedSession = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Covers the hand-over of a server session from a retiring managed session to a managed
    /// session that takes it over, against a scripted transport that records every request
    /// reaching the wire.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [NonParallelizable]
    public sealed class ManagedSessionTakeoverTests
    {
        /// <summary>
        /// Retirement hands over the server session and its subscriptions without closing or
        /// deleting anything, and no recovery creates or activates another session. A graceful
        /// dispose of the same setup does close the server session, which shows the fixture
        /// observes the requests.
        /// </summary>
        /// <param name="retire">Whether to retire instead of disposing.</param>
        [TestCase(true)]
        [TestCase(false)]
        public async Task RetireReturnsTheHandoffWithoutClosingTheServerSessionAsync(bool retire)
        {
            await using var harness = new ManagedSessionReconnectHarness();
            await harness.ConnectAsync(false).ConfigureAwait(false);
            harness.AddSubscription();
            await harness.NextMonitoredItemsAsync().AsTask().WaitAsync(PhaseTimeout).ConfigureAwait(false);
            NodeId sessionId = harness.Session.SessionId;
            int mark = harness.Requests.Count;

            SessionHandoff? handoff = null;
            if (retire)
            {
                handoff = await harness.Session.RetireAsync().AsTask().WaitAsync(PhaseTimeout).ConfigureAwait(false);
            }
            else
            {
                await harness.Session.DisposeAsync().AsTask().WaitAsync(PhaseTimeout).ConfigureAwait(false);
            }

            IServiceRequest[] after = [.. harness.Requests.ToList().Skip(mark)];
            Assert.That(harness.Session.Disposed, Is.True);
            if (handoff != null)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(handoff.Session.SessionId, Is.EqualTo(sessionId));
                    Assert.That(handoff.Session.AuthenticationToken, Is.EqualTo(new NodeId("token-1", 1)));
                    Assert.That(handoff.Subscriptions.Count, Is.EqualTo(1));
                    Assert.That(handoff.Subscriptions[0].ServerId, Is.EqualTo(SubscriptionId));
                    Assert.That(handoff.Subscriptions[0].MonitoredItems.Count, Is.EqualTo(1));
                    Assert.That(handoff.Subscriptions[0].HasRevisedSettings, Is.True);
                    Assert.That(handoff.Subscriptions[0].MonitoredItems[0].HasRevisedSettings, Is.True);
                    Assert.That(after, Has.None.InstanceOf<CloseSessionRequest>());
                    Assert.That(after, Has.None.InstanceOf<DeleteSubscriptionsRequest>());
                    Assert.That(after, Has.None.InstanceOf<CreateSessionRequest>());
                    Assert.That(after, Has.None.InstanceOf<ActivateSessionRequest>());
                });
            }
            else
            {
                Assert.That(after, Has.Some.InstanceOf<CloseSessionRequest>());
            }
        }

        /// <summary>
        /// A retirement that races a disposal wins or loses as one operation: a retirement that
        /// starts first never lets the disposal close the server session, and a retirement after a
        /// disposal or close fails instead of claiming the server session was retained.
        /// </summary>
        /// <param name="first">The operation that starts first.</param>
        [TestCase("dispose")]
        [TestCase("close")]
        [TestCase("retire")]
        public async Task RetireAndDisposeAreSerializedAsync(string first)
        {
            await using var harness = new ManagedSessionReconnectHarness();
            await harness.ConnectAsync(false).ConfigureAwait(false);

            if (first == "retire")
            {
                Task<SessionHandoff> retirement = harness.Session.RetireAsync().AsTask();
                Task disposal = harness.Session.DisposeAsync().AsTask();
                await Task.WhenAll(retirement, disposal).WaitAsync(PhaseTimeout).ConfigureAwait(false);
                Assert.That((await retirement.ConfigureAwait(false)).Session, Is.Not.Null);
                Assert.That(harness.Requests.ToList(), Has.None.InstanceOf<CloseSessionRequest>());
                return;
            }

            Task closing = first == "dispose"
                ? harness.Session.DisposeAsync().AsTask()
                : harness.Session.CloseAsync(harness.CancellationToken);
            await closing.WaitAsync(PhaseTimeout).ConfigureAwait(false);
            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(
                () => harness.Session.RetireAsync().AsTask().WaitAsync(PhaseTimeout));
            Assert.That(
                failure.StatusCode,
                Is.EqualTo(first == "dispose" ? StatusCodes.BadInvalidState : StatusCodes.BadNotConnected));
            Assert.That(harness.Requests.ToList(), Has.Some.InstanceOf<CloseSessionRequest>());
        }

        /// <summary>
        /// A session that is recovering cannot be retired: the retirement fails without
        /// affecting the recovery.
        /// </summary>
        [Test]
        public async Task RetireDuringRecoveryFailsAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness(TimeSpan.Zero);
            await harness.ConnectAsync(false).ConfigureAwait(false);
            harness.TransportReconnect.Release();
            harness.RecoveryActivation.Release();

            Task recovery = harness.StartOuterRecoveryAsync();
            await harness.ReplacementSession.Entered.WaitAsync(PhaseTimeout).ConfigureAwait(false);
            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(
                () => harness.Session.RetireAsync().AsTask().WaitAsync(PhaseTimeout));
            harness.ReplacementSession.Release();
            await recovery.WaitAsync(PhaseTimeout).ConfigureAwait(false);

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(harness.Session.Disposed, Is.False);
            Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
        }

        /// <summary>
        /// A session that takes over a retired one activates the saved authentication token on a
        /// new channel, creates neither a session nor subscriptions, and dispatches the
        /// notifications of the adopted subscription to the new handler.
        /// </summary>
        [Test]
        public async Task TakeOverActivatesTheSavedSessionAndAdoptsItsSubscriptionsAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness
            {
                RecoveryActivationStatus = StatusCodes.Good
            };
            await harness.ConnectAsync(false).ConfigureAwait(false);
            ISubscription original = harness.AddSubscription();
            CreateMonitoredItemsRequest items = await harness.NextMonitoredItemsAsync().AsTask()
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);
            uint clientHandle = items.ItemsToCreate[0].RequestedParameters.ClientHandle;
            SessionHandoff handoff = await harness.Session.RetireAsync().AsTask()
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);
            harness.RecoveryActivation.Release();
            int mark = harness.Requests.Count;

            UaManagedSession successor = await harness.TakeOverAsync(handoff).ConfigureAwait(false);

            IServiceRequest[] takeover = [.. harness.Requests.ToList().Skip(mark)];
            Assert.Multiple(() =>
            {
                Assert.That(takeover.OfType<ActivateSessionRequest>().First().RequestHeader.AuthenticationToken,
                    Is.EqualTo(handoff.Session.AuthenticationToken));
                Assert.That(takeover, Has.None.InstanceOf<CreateSessionRequest>());
                Assert.That(takeover, Has.None.InstanceOf<CloseSessionRequest>());
                Assert.That(takeover, Has.None.InstanceOf<CreateSubscriptionRequest>());
                Assert.That(takeover, Has.None.InstanceOf<TransferSubscriptionsRequest>());
                Assert.That(takeover, Has.None.InstanceOf<CreateMonitoredItemsRequest>());
                Assert.That(successor.SessionId, Is.EqualTo(handoff.Session.SessionId));
                Assert.That(successor.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
            });

            ReceivedData received = await ReceiveNotificationAsync(harness, original, clientHandle)
                .ConfigureAwait(false);
            Assert.That(received.Subscription, Is.Not.SameAs(original));
            Assert.That(received.Change.Value.WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(42));
        }

        /// <summary>
        /// A takeover whose activation is rejected fails the creation by default; the saved server
        /// session is neither closed nor replaced by a new one.
        /// </summary>
        [Test]
        public async Task TakeOverFailureThrowsWithoutClosingOrCreatingASessionAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness
            {
                TakeoverActivationStatus = StatusCodes.BadSessionIdInvalid
            };
            await harness.ConnectAsync(false).ConfigureAwait(false);
            SessionHandoff handoff = CreateForeignHandoff(harness);
            int mark = harness.Requests.Count;

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(
                () => harness.TakeOverAsync(handoff));

            IServiceRequest[] after = [.. harness.Requests.ToList().Skip(mark)];
            Assert.Multiple(() =>
            {
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
                Assert.That(after.OfType<ActivateSessionRequest>().Count(), Is.EqualTo(1));
                Assert.That(after, Has.None.InstanceOf<CreateSessionRequest>());
                Assert.That(after, Has.None.InstanceOf<CloseSessionRequest>());
            });
        }

        /// <summary>
        /// With <see cref="SessionTakeoverFailureMode.CreateNewSession"/> a rejected takeover
        /// creates a new server session and recreates the handed-over subscriptions on it, without
        /// closing the saved server session.
        /// </summary>
        [Test]
        public async Task TakeOverFailureCanCreateANewSessionAndRecreateSubscriptionsAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness
            {
                TakeoverActivationStatus = StatusCodes.BadSessionIdInvalid
            };
            await harness.ConnectAsync(false).ConfigureAwait(false);
            harness.AddSubscription();
            await harness.NextMonitoredItemsAsync().AsTask().WaitAsync(PhaseTimeout).ConfigureAwait(false);
            SessionHandoff retired = await harness.Session.RetireAsync().AsTask()
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);
            SessionHandoff handoff = retired with
            {
                Session = retired.Session with
                {
                    SessionId = TakeoverSessionId,
                    AuthenticationToken = TakeoverToken
                }
            };
            harness.ReleaseAllGates();
            int mark = harness.Requests.Count;
            var takeoverIdentity = new UserIdentity();

            UaManagedSession successor = await harness.TakeOverAsync(handoff, new SessionTakeoverOptions
            {
                SubscriptionHandlerFactory = _ => harness.CreateNotificationHandler(),
                Identity = takeoverIdentity,
                FailureMode = SessionTakeoverFailureMode.CreateNewSession
            }).ConfigureAwait(false);
            await harness.NextMonitoredItemsAsync().AsTask().WaitAsync(PhaseTimeout).ConfigureAwait(false);

            IServiceRequest[] after = [.. harness.Requests.ToList().Skip(mark)];
            Assert.Multiple(() =>
            {
                Assert.That(after, Has.Some.InstanceOf<CreateSessionRequest>());
                Assert.That(after, Has.Some.InstanceOf<CreateSubscriptionRequest>());
                Assert.That(after, Has.Some.InstanceOf<CreateMonitoredItemsRequest>());
                Assert.That(after, Has.None.InstanceOf<CloseSessionRequest>());
                Assert.That(successor.SessionId, Is.Not.EqualTo(TakeoverSessionId));
                Assert.That(successor.Identity, Is.SameAs(takeoverIdentity));
                Assert.That(successor.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
                Assert.That(
                    harness.Logs.Records.Any(r => r.Message.Contains("continuity", StringComparison.Ordinal)),
                    Is.True);
            });
        }

        /// <summary>
        /// After a takeover the session behaves like any other: a reconnect reactivates the
        /// taken-over server session, and when the server rejects it a new session is created.
        /// </summary>
        [Test]
        public async Task ReconnectAfterTakeoverIsNormalRecoveryAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness(TimeSpan.Zero);
            await harness.ConnectAsync(false).ConfigureAwait(false);
            SessionHandoff handoff = CreateForeignHandoff(harness);
            await harness.Session.DisposeAsync().AsTask().WaitAsync(PhaseTimeout).ConfigureAwait(false);
            UaManagedSession successor = await harness.TakeOverAsync(handoff).ConfigureAwait(false);
            long generation = successor.SessionGeneration;
            harness.ReleaseAllGates();
            int mark = harness.Requests.Count;

            await successor.ReconnectAsync(null, null, harness.CancellationToken)
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);

            IServiceRequest[] reactivation = [.. harness.Requests.ToList().Skip(mark)];
            Assert.Multiple(() =>
            {
                Assert.That(reactivation.OfType<ActivateSessionRequest>().Single().RequestHeader.AuthenticationToken,
                    Is.EqualTo(TakeoverToken));
                Assert.That(reactivation, Has.None.InstanceOf<CreateSessionRequest>());
                Assert.That(successor.SessionId, Is.EqualTo(TakeoverSessionId));
                Assert.That(successor.SessionGeneration, Is.EqualTo(generation));
            });

            harness.TakeoverActivationStatus = StatusCodes.BadSessionIdInvalid;
            mark = harness.Requests.Count;
            await successor.ReconnectAsync(null, null, harness.CancellationToken)
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);

            IServiceRequest[] recreation = [.. harness.Requests.ToList().Skip(mark)];
            Assert.Multiple(() =>
            {
                Assert.That(recreation, Has.Some.InstanceOf<CreateSessionRequest>());
                Assert.That(successor.SessionId, Is.Not.EqualTo(TakeoverSessionId));
                Assert.That(successor.SessionGeneration, Is.EqualTo(generation + 1));
                Assert.That(successor.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
            });
        }

        /// <summary>
        /// The session that takes over uses the revised session timeout of the handoff instead of
        /// its own requested timeout.
        /// </summary>
        [Test]
        public async Task TakeOverKeepsTheRevisedSessionTimeoutAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness
            {
                RevisedSessionTimeout = 1500
            };
            await harness.ConnectAsync(false).ConfigureAwait(false);
            SessionHandoff handoff = CreateForeignHandoff(harness);
            Assert.That(handoff.RevisedSessionTimeout, Is.EqualTo(1500));

            UaManagedSession successor = await harness.TakeOverAsync(handoff).ConfigureAwait(false);

            Assert.That(successor.SessionTimeout, Is.EqualTo(1500));
        }

        /// <summary>
        /// The takeover activation uses the preferred locales configured on the managed session.
        /// </summary>
        [Test]
        public async Task TakeOverUsesConfiguredPreferredLocalesAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness();
            await harness.ConnectAsync(false).ConfigureAwait(false);
            SessionHandoff handoff = CreateForeignHandoff(harness);

            await harness.TakeOverAsync(
                handoff,
                preferredLocales: ["fr-FR", "en-US"]).ConfigureAwait(false);

            ActivateSessionRequest activation = harness.Requests
                .ToList()
                .OfType<ActivateSessionRequest>()
                .Last(request => request.RequestHeader.AuthenticationToken == TakeoverToken);
            Assert.Multiple(() =>
            {
                Assert.That(activation.LocaleIds.Count, Is.EqualTo(2));
                Assert.That(activation.LocaleIds[0], Is.EqualTo("fr-FR"));
                Assert.That(activation.LocaleIds[1], Is.EqualTo("en-US"));
            });
        }

        /// <summary>
        /// A takeover created by a configured managed-session factory retains its model-change tracker.
        /// </summary>
        [Test]
        public async Task FactoryTakeoverPreservesModelChangeTrackingAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness();
            await harness.ConnectAsync(false, modelChangeTracking: true).ConfigureAwait(false);
            Assert.That(harness.Session.ModelChange, Is.Not.Null);
            SessionHandoff handoff = CreateForeignHandoff(harness);

            UaManagedSession successor = await harness.Session.CreateTakeoverSessionAsync(
                handoff,
                new SessionTakeoverOptions(),
                harness.CancellationToken).WaitAsync(PhaseTimeout).ConfigureAwait(false);
            await using (successor.ConfigureAwait(false))
            {
                Assert.That(successor.ModelChange, Is.Not.Null);
            }
        }

        /// <summary>
        /// If post-connect model-change tracking cannot start after takeover, initialization cleanup abandons
        /// rather than closes the server session.
        /// </summary>
        [Test]
        public async Task TakeOverInitializationFailureDoesNotCloseTheServerSessionAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness();
            await harness.ConnectAsync(false).ConfigureAwait(false);
            SessionHandoff handoff = CreateForeignHandoff(harness);
            int mark = harness.Requests.Count;

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => harness.TakeOverAsync(
                    handoff,
                    modelChangeTracking: true,
                    subscriptionEngineFactory: new ClassicSubscriptionEngineFactory()));

            IServiceRequest[] after = [.. harness.Requests.ToList().Skip(mark)];
            Assert.Multiple(() =>
            {
                Assert.That(after, Has.Some.InstanceOf<ActivateSessionRequest>());
                Assert.That(after, Has.None.InstanceOf<CloseSessionRequest>());
                Assert.That(after, Has.None.InstanceOf<DeleteSubscriptionsRequest>());
            });
        }

        /// <summary>
        /// Fallback identity remains available across a failed connect setup, including when the handoff has no
        /// subscriptions to recreate.
        /// </summary>
        [Test]
        public async Task FallbackIdentityIsRetainedUntilConnectSetupSucceedsAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness
            {
                TakeoverActivationStatus = StatusCodes.BadSessionIdInvalid
            };
            await harness.ConnectAsync(false).ConfigureAwait(false);
            SessionHandoff handoff = CreateForeignHandoff(harness);
            var redundancy = new Mock<IServerRedundancyHandler>();
            redundancy
                .SetupSequence(handler => handler.FetchRedundancyInfoAsync(
                    It.IsAny<ISession>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InsufficientMemoryException())
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadNotSupported));
            harness.ReleaseAllGates();
            var identity = new UserIdentity();

            UaManagedSession successor = await harness.TakeOverAsync(
                handoff,
                new SessionTakeoverOptions
                {
                    Identity = identity,
                    FailureMode = SessionTakeoverFailureMode.CreateNewSession
                },
                redundancy.Object).ConfigureAwait(false);

            Assert.That(successor.Identity, Is.SameAs(identity));
            redundancy.Verify(
                handler => handler.FetchRedundancyInfoAsync(
                    It.IsAny<ISession>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        /// <summary>
        /// A handoff that cannot be taken over is rejected before anything reaches the server.
        /// </summary>
        [Test]
        public async Task InvalidHandoffIsRejectedBeforeConnectingAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness();
            await harness.ConnectAsync(false).ConfigureAwait(false);
            SessionHandoff handoff = CreateForeignHandoff(harness);
            int mark = harness.Requests.Count;

            SessionHandoff noToken = handoff with
            {
                Session = handoff.Session with { AuthenticationToken = NodeId.Null }
            };
            await Assert.ThatAsync(() => harness.TakeOverAsync(noToken), Throws.InstanceOf<ServiceResultException>());

            SessionHandoff withSubscriptions = handoff with
            {
                Subscriptions = [new SubscriptionStateSnapshot { ServerId = SubscriptionId }]
            };
            await Assert.ThatAsync(
                () => harness.TakeOverAsync(withSubscriptions, new SessionTakeoverOptions()),
                Throws.ArgumentException);

            Assert.That(harness.Requests.Count, Is.EqualTo(mark));
        }

        /// <summary>
        /// The managed generation starts at one, does not change when recovery reactivates the
        /// same server session, and increments once when recovery has to recreate it.
        /// </summary>
        /// <param name="recreate">Whether the server rejects the reactivation.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SessionGenerationChangesOnlyWithTheServerSessionAsync(bool recreate)
        {
            await using var harness = new ManagedSessionReconnectHarness(TimeSpan.Zero)
            {
                RecoveryActivationStatus = recreate ? StatusCodes.BadSessionIdInvalid : StatusCodes.Good
            };
            await harness.ConnectAsync(false).ConfigureAwait(false);
#pragma warning disable CA1859 // Typed as the capability to test the contract consumers use.
            ISessionTakeover takeover = harness.Session;
#pragma warning restore CA1859
            NodeId initial = harness.Session.SessionId;
            Assert.That(takeover.SessionGeneration, Is.EqualTo(1));
            var changes = new ConcurrentQueue<SessionGenerationChangedEventArgs>();
            takeover.SessionGenerationChanged += (_, args) => changes.Enqueue(args);

            harness.ReleaseAllGates();
            await harness.StartOuterRecoveryAsync().WaitAsync(PhaseTimeout).ConfigureAwait(false);

            if (recreate)
            {
                Assert.That(harness.Session.SessionId, Is.Not.EqualTo(initial));
                Assert.That(takeover.SessionGeneration, Is.EqualTo(2));
                SessionGenerationChangedEventArgs change = changes.Single();
                Assert.That(change.Generation, Is.EqualTo(2));
                Assert.That(change.PreviousSessionId, Is.EqualTo(initial));
                Assert.That(change.SessionId, Is.EqualTo(harness.Session.SessionId));
            }
            else
            {
                Assert.That(harness.Session.SessionId, Is.EqualTo(initial));
                Assert.That(takeover.SessionGeneration, Is.EqualTo(1));
                Assert.That(changes, Is.Empty);
            }
        }

        /// <summary>
        /// A takeover whose setup fails after the server session was bound abandons that server
        /// session instead of closing it, and the retry takes the same server session over again
        /// instead of creating a new one.
        /// </summary>
        [Test]
        public async Task TakeoverFailingAfterBindingRetriesTheTakeoverWithoutClosingAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness();
            await harness.ConnectAsync(false).ConfigureAwait(false);
            SessionHandoff handoff = CreateForeignHandoff(harness);
            var redundancy = new Mock<IServerRedundancyHandler>();
            redundancy
                .SetupSequence(handler => handler.FetchRedundancyInfoAsync(
                    It.IsAny<ISession>(),
                    It.IsAny<CancellationToken>()))
                // The only failure the best-effort redundancy refresh does not swallow.
                .ThrowsAsync(new InsufficientMemoryException())
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadNotSupported));
            // A regression that creates a new server session must reach the assertions.
            harness.ReleaseAllGates();
            int mark = harness.Requests.Count;

            UaManagedSession successor = await harness.TakeOverAsync(handoff, null, redundancy.Object)
                .ConfigureAwait(false);

            IServiceRequest[] after = [.. harness.Requests.ToList().Skip(mark)];
            Assert.Multiple(() =>
            {
                Assert.That(
                    after.OfType<ActivateSessionRequest>().Select(request => request.RequestHeader.AuthenticationToken),
                    Is.EqualTo(new[] { TakeoverToken, TakeoverToken }));
                Assert.That(after, Has.None.InstanceOf<CloseSessionRequest>());
                Assert.That(after, Has.None.InstanceOf<CreateSessionRequest>());
                Assert.That(successor.SessionId, Is.EqualTo(TakeoverSessionId));
                Assert.That(successor.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
            });
            redundancy.Verify(
                handler => handler.FetchRedundancyInfoAsync(It.IsAny<ISession>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        /// <summary>
        /// A takeover cancelled after its ActivateSession succeeded retries with the server nonce
        /// that activation returned, which is the nonce the server expects next, not with the
        /// stale nonce of the handoff.
        /// </summary>
        [Test]
        public async Task TakeoverCancelledAfterActivationRetriesWithTheNewServerNonceAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness();
            await harness.ConnectAsync(false).ConfigureAwait(false);
            SessionHandoff handoff = CreateForeignHandoff(harness);
            var nonces = new ConcurrentQueue<ByteString>();
            harness.TakeoverServerNonce = () =>
            {
                var nonce = new ByteString(Enumerable.Repeat((byte)(nonces.Count + 1), 32).ToArray());
                nonces.Enqueue(nonce);
                return nonce;
            };
            harness.CancelNextTakeoverNamespaceRead();
            int mark = harness.Requests.Count;

            UaManagedSession successor = await harness.TakeOverAsync(handoff).ConfigureAwait(false);

            IServiceRequest[] after = [.. harness.Requests.ToList().Skip(mark)];
            // The activation request does not carry the nonce it was signed with when the
            // endpoint uses no security, so the session's record of it is read instead.
            var signed = (ByteString)typeof(global::Opc.Ua.Client.Session)
                .GetField("m_previousServerNonce", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(successor.InnerSession)!;
            ByteString[] returned = [.. nonces];
            Assert.Multiple(() =>
            {
                Assert.That(
                    after.OfType<ActivateSessionRequest>().Select(request => request.RequestHeader.AuthenticationToken),
                    Is.EqualTo(new[] { TakeoverToken, TakeoverToken }));
                Assert.That(after, Has.None.InstanceOf<CloseSessionRequest>());
                Assert.That(after, Has.None.InstanceOf<CreateSessionRequest>());
                Assert.That(returned, Has.Length.EqualTo(2));
                Assert.That(handoff.Session.ServerNonce, Is.Not.EqualTo(returned[0]));
                Assert.That(signed, Is.EqualTo(returned[0]));
                Assert.That(successor.SessionId, Is.EqualTo(TakeoverSessionId));
            });
        }

        /// <summary>
        /// Captures the server session of the harness session as the handoff of another owner's
        /// server session, whose token the script answers with the takeover activation status.
        /// </summary>
        private static SessionHandoff CreateForeignHandoff(ManagedSessionReconnectHarness harness)
        {
            SessionHandoff handoff = harness.Session.CaptureHandoff();
            return handoff with
            {
                Session = handoff.Session with
                {
                    SessionId = TakeoverSessionId,
                    AuthenticationToken = TakeoverToken
                }
            };
        }

        /// <summary>
        /// Answers publish requests with a value for <paramref name="clientHandle"/> until a
        /// subscription other than <paramref name="exclude"/> dispatches a notification.
        /// </summary>
        private static async Task<ReceivedData> ReceiveNotificationAsync(
            ManagedSessionReconnectHarness harness,
            ISubscription exclude,
            uint clientHandle)
        {
            harness.FirstNotification.Release();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(harness.CancellationToken);
            timeout.CancelAfter(PhaseTimeout);
            Task<ReceivedData> next = harness.NextNotificationAsync().AsTask();
            Task<WirePublish> publish = harness.NextWirePublishAsync().AsTask();
            uint sequenceNumber = 1;
            while (true)
            {
                Task completed = await Task.WhenAny(next, publish).WaitAsync(timeout.Token).ConfigureAwait(false);
                if (completed == next)
                {
                    ReceivedData received = await next.ConfigureAwait(false);
                    if (!ReferenceEquals(received.Subscription, exclude))
                    {
                        return received;
                    }
                    next = harness.NextNotificationAsync().AsTask();
                    continue;
                }
                WirePublish exchange = await publish.ConfigureAwait(false);
                if (!exchange.Response.IsCompleted && !exchange.CancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        exchange.ReplyWithValue(clientHandle, 42, sequenceNumber++);
                    }
                    catch (InvalidOperationException)
                    {
                        // The publish was cancelled concurrently.
                    }
                }
                publish = harness.NextWirePublishAsync().AsTask();
            }
        }
    }
}
