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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;
using static Opc.Ua.Client.Tests.Stack.Client.Fakes.ManagedSessionReconnectHarness;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Covers the session takeover capability of the managed session against a scripted transport
    /// that records every request reaching the wire.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [NonParallelizable]
    public sealed class ManagedSessionTakeoverTests
    {
        /// <summary>
        /// Retirement keeps the server session and its subscriptions: neither CloseSession nor
        /// DeleteSubscriptions reaches the server, and no recovery creates or activates another
        /// session. A graceful dispose of the same setup does close the server session, which
        /// shows the fixture observes the requests.
        /// </summary>
        /// <param name="retire">Whether to retire instead of disposing.</param>
        [TestCase(true)]
        [TestCase(false)]
        public async Task RetireAndPreserveSendsNoFurtherSessionRequestsAsync(bool retire)
        {
            await using var harness = new ManagedSessionReconnectHarness();
            await harness.ConnectAsync(false).ConfigureAwait(false);
            harness.AddSubscription();
            await harness.NextMonitoredItemsAsync().AsTask().WaitAsync(PhaseTimeout).ConfigureAwait(false);
            int mark = harness.Requests.Count;

            if (retire)
            {
                await harness.Session.RetireAndPreserveAsync().AsTask().WaitAsync(PhaseTimeout).ConfigureAwait(false);
            }
            else
            {
                await harness.Session.DisposeAsync().AsTask().WaitAsync(PhaseTimeout).ConfigureAwait(false);
            }

            IServiceRequest[] after = [.. harness.Requests.ToList().Skip(mark)];
            Assert.That(harness.Session.Disposed, Is.True);
            if (retire)
            {
                Assert.That(after, Has.None.InstanceOf<CloseSessionRequest>());
                Assert.That(after, Has.None.InstanceOf<DeleteSubscriptionsRequest>());
                Assert.That(after, Has.None.InstanceOf<CreateSessionRequest>());
                Assert.That(after, Has.None.InstanceOf<ActivateSessionRequest>());
            }
            else
            {
                Assert.That(after, Has.Some.InstanceOf<CloseSessionRequest>());
            }
        }

        /// <summary>
        /// A retirement that loses the race against a graceful disposal or close reports the failure
        /// instead of claiming the server session was retained, while a retirement that won stays
        /// idempotent for a later dispose.
        /// </summary>
        /// <param name="first">The operation that starts first.</param>
        [TestCase("dispose")]
        [TestCase("close")]
        [TestCase("retire")]
        public async Task RetireReportsWhetherTheServerSessionWasRetainedAsync(string first)
        {
            await using var harness = new ManagedSessionReconnectHarness();
            await harness.ConnectAsync(false).ConfigureAwait(false);

            if (first == "retire")
            {
                Task retirement = harness.Session.RetireAndPreserveAsync().AsTask();
                await harness.Session.DisposeAsync().AsTask().WaitAsync(PhaseTimeout).ConfigureAwait(false);
                await retirement.WaitAsync(PhaseTimeout).ConfigureAwait(false);
                Assert.That(harness.Requests.ToList(), Has.None.InstanceOf<CloseSessionRequest>());
                return;
            }

            Task closing = first == "dispose"
                ? harness.Session.DisposeAsync().AsTask()
                : harness.Session.CloseAsync(harness.CancellationToken);
            await closing.WaitAsync(PhaseTimeout).ConfigureAwait(false);
            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(
                () => harness.Session.RetireAndPreserveAsync().AsTask().WaitAsync(PhaseTimeout));
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(harness.Requests.ToList(), Has.Some.InstanceOf<CloseSessionRequest>());
        }

        /// <summary>
        /// A retirement while recovery is creating a replacement session neither lets the recovery reach
        /// the retained server session nor move or restore its subscriptions on the replacement.
        /// </summary>
        [Test]
        public async Task RetireDuringRecoveryDoesNotReachTheRetainedSessionAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness(System.TimeSpan.Zero);
            await harness.ConnectAsync(false).ConfigureAwait(false);
            harness.AddSubscription();
            await harness.NextMonitoredItemsAsync().AsTask().WaitAsync(PhaseTimeout).ConfigureAwait(false);
            NodeId retainedToken = new("token-1", 1);
            harness.TransportReconnect.Release();
            harness.RecoveryActivation.Release();

            Task recovery = harness.StartOuterRecoveryAsync();
            await harness.ReplacementSession.Entered.WaitAsync(PhaseTimeout).ConfigureAwait(false);
            int mark = harness.Requests.Count;
            Task retirement = harness.Session.RetireAndPreserveAsync().AsTask();
            harness.ReplacementSession.Release();
            await retirement.WaitAsync(PhaseTimeout).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => recovery.WaitAsync(PhaseTimeout), Throws.InstanceOf<ServiceResultException>());
            harness.Recovery = null;

            IServiceRequest[] after = [.. harness.Requests.ToList().Skip(mark)];
            Assert.That(harness.Session.Disposed, Is.True);
            Assert.That(after.Where(r => r.RequestHeader.AuthenticationToken == retainedToken), Is.Empty);
            Assert.That(after, Has.None.InstanceOf<TransferSubscriptionsRequest>());
            Assert.That(after, Has.None.InstanceOf<CreateSubscriptionRequest>());
            Assert.That(after, Has.None.InstanceOf<CreateMonitoredItemsRequest>());
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
            await using var harness = new ManagedSessionReconnectHarness(System.TimeSpan.Zero)
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
        /// Preparing closes only the helper's own server session and loads the saved state without
        /// activating it: the session reports prepared and disconnected, and service calls fail fast.
        /// The explicit reconnect then activates exactly the saved session with its token, without
        /// CreateSession or CloseSession, and normal service resumes on it.
        /// </summary>
        [Test]
        public async Task PrepareAndReconnectActivateExactlyTheSavedSessionAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness();
            await harness.ConnectAsync(false).ConfigureAwait(false);
            NodeId helperToken = new("token-1", 1);
            int mark = harness.Requests.Count;

            await harness.Session.PrepareTakeoverAsync(CreateSavedSession(harness)).AsTask()
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);

            IServiceRequest[] prepare = [.. harness.Requests.ToList().Skip(mark)];
            Assert.That(prepare.Select(request => request.GetType()), Is.EqualTo(new[] { typeof(CloseSessionRequest) }));
            var close = (CloseSessionRequest)prepare[0];
            Assert.Multiple(() =>
            {
                Assert.That(close.RequestHeader.AuthenticationToken, Is.EqualTo(helperToken));
                Assert.That(close.DeleteSubscriptions, Is.True);
                Assert.That(harness.Session.IsPrepared, Is.True);
                Assert.That(harness.Session.Connected, Is.False);
                Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Disconnected));
                Assert.That(harness.Session.SessionId, Is.EqualTo(TakeoverSessionId));
                Assert.That(harness.Session.SessionGeneration, Is.EqualTo(2));
            });
            using (var timeout = new CancellationTokenSource(PhaseTimeout))
            {
                ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await harness.Session.ReadValueAsync(
                        VariableIds.Server_ServerStatus_State, timeout.Token).ConfigureAwait(false));
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionNotActivated));
            }

            mark = harness.Requests.Count;
            await harness.Session.ReconnectAsync(null, null, harness.CancellationToken)
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);

            IServiceRequest[] activation = [.. harness.Requests.ToList().Skip(mark)];
            Assert.Multiple(() =>
            {
                Assert.That(activation.OfType<ActivateSessionRequest>().Single().RequestHeader.AuthenticationToken,
                    Is.EqualTo(TakeoverToken));
                Assert.That(activation, Has.None.InstanceOf<CreateSessionRequest>());
                Assert.That(activation, Has.None.InstanceOf<CloseSessionRequest>());
                Assert.That(harness.Session.IsPrepared, Is.False);
                Assert.That(harness.Session.Connected, Is.True);
                Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
                Assert.That(harness.Session.SessionId, Is.EqualTo(TakeoverSessionId));
                Assert.That(harness.Session.SessionGeneration, Is.EqualTo(2), "Activation keeps the generation.");
            });
            DataValue value = await harness.Session.ReadValueAsync(
                VariableIds.Server_ServerStatus_State, harness.CancellationToken)
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Requests.ToList().OfType<ReadRequest>().Last().RequestHeader.AuthenticationToken,
                Is.EqualTo(TakeoverToken));
        }

        /// <summary>
        /// Incomplete or mismatched saved state is rejected before anything changes: no request is sent
        /// and the helper session stays connected and usable.
        /// </summary>
        /// <param name="mismatch">The defect of the saved state.</param>
        [TestCase("token")]
        [TestCase("endpoint")]
        [TestCase("url")]
        [TestCase("mode")]
        [TestCase("policy")]
        public async Task PrepareRejectsInvalidSavedStateWithoutChangesAsync(string mismatch)
        {
            await using var harness = new ManagedSessionReconnectHarness();
            await harness.ConnectAsync(false).ConfigureAwait(false);
            SessionConfiguration saved = CreateSavedSession(harness);
            ConfiguredEndpoint other = SessionChannelHarness.CreateEndpoint(
                mismatch == "url" ? "opc.tcp://otherhost:4840" : "opc.tcp://localhost:4840");
            if (mismatch == "mode")
            {
                other.Description.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                other.Description.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            }
            else if (mismatch == "policy")
            {
                other.Description.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            }
            saved = mismatch switch
            {
                "token" => saved with { AuthenticationToken = NodeId.Null },
                "endpoint" => saved with { ConfiguredEndpoint = null },
                _ => saved with { ConfiguredEndpoint = other }
            };
            NodeId helper = harness.Session.SessionId;
            int mark = harness.Requests.Count;

            Assert.That(
                async () => await harness.Session.PrepareTakeoverAsync(saved).ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>());

            Assert.Multiple(() =>
            {
                IServiceRequest[] after = [.. harness.Requests.ToList().Skip(mark)];
                Assert.That(after, Has.None.InstanceOf<CloseSessionRequest>());
                Assert.That(after, Has.None.InstanceOf<ActivateSessionRequest>());
                Assert.That(after, Has.None.InstanceOf<CreateSessionRequest>());
                Assert.That(harness.Session.IsPrepared, Is.False);
                Assert.That(harness.Session.Connected, Is.True);
                Assert.That(harness.Session.SessionId, Is.EqualTo(helper));
                Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
            });
            DataValue value = await harness.Session.ReadValueAsync(
                VariableIds.Server_ServerStatus_State, harness.CancellationToken)
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// A rejected activation neither creates another session nor closes the saved one; the session
        /// stays prepared and a retry activates it.
        /// </summary>
        [Test]
        public async Task FailedActivationStaysPreparedAndCanBeRetriedAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness
            {
                TakeoverActivationStatus = StatusCodes.BadSessionIdInvalid
            };
            await harness.ConnectAsync(false).ConfigureAwait(false);
            await harness.Session.PrepareTakeoverAsync(CreateSavedSession(harness)).AsTask()
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);
            int mark = harness.Requests.Count;

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(
                () => harness.Session.ReconnectAsync(null, null, harness.CancellationToken).WaitAsync(PhaseTimeout));

            IServiceRequest[] after = [.. harness.Requests.ToList().Skip(mark)];
            Assert.Multiple(() =>
            {
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
                Assert.That(after, Has.None.InstanceOf<CreateSessionRequest>());
                Assert.That(after, Has.None.InstanceOf<CloseSessionRequest>());
                Assert.That(harness.Session.IsPrepared, Is.True);
                Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Disconnected));
            });

            harness.TakeoverActivationStatus = StatusCodes.Good;
            await harness.Session.ReconnectAsync(null, null, harness.CancellationToken)
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);
            Assert.That(harness.Session.IsPrepared, Is.False);
            Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
            Assert.That(harness.Requests.ToList().OfType<CreateSessionRequest>().Count(), Is.EqualTo(1),
                "Only the helper session was ever created.");
        }

        /// <summary>
        /// A prepared session never committed its ownership, so closing, disposing or retiring it leaves the
        /// saved server session alive: nothing is sent with the saved session's token.
        /// </summary>
        /// <param name="operation">How the prepared session is released.</param>
        [TestCase("dispose")]
        [TestCase("close")]
        [TestCase("retire")]
        public async Task ReleasingAPreparedSessionKeepsTheSavedServerSessionAsync(string operation)
        {
            await using var harness = new ManagedSessionReconnectHarness();
            await harness.ConnectAsync(false).ConfigureAwait(false);
            await harness.Session.PrepareTakeoverAsync(CreateSavedSession(harness)).AsTask()
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);
            int mark = harness.Requests.Count;

            switch (operation)
            {
                case "dispose":
                    await harness.Session.DisposeAsync().AsTask().WaitAsync(PhaseTimeout).ConfigureAwait(false);
                    break;
                case "close":
                    StatusCode status = await harness.Session.CloseAsync(harness.CancellationToken)
                        .WaitAsync(PhaseTimeout).ConfigureAwait(false);
                    Assert.That(status, Is.EqualTo(StatusCodes.Good));
                    break;
                default:
                    await harness.Session.RetireAndPreserveAsync().AsTask()
                        .WaitAsync(PhaseTimeout).ConfigureAwait(false);
                    break;
            }

            IServiceRequest[] after = [.. harness.Requests.ToList().Skip(mark)];
            Assert.That(after.Where(request => request.RequestHeader.AuthenticationToken == TakeoverToken), Is.Empty);
            Assert.That(after, Has.None.InstanceOf<CloseSessionRequest>());
            Assert.That(after, Has.None.InstanceOf<DeleteSubscriptionsRequest>());
        }

        /// <summary>
        /// Disposing the session while the activation is in flight cancels it and leaves the saved server
        /// session alive, because its ownership never committed.
        /// </summary>
        [Test]
        public async Task DisposeDuringActivationKeepsTheSavedServerSessionAsync()
        {
            var activation = new AsyncOperationGate();
            await using var harness = new ManagedSessionReconnectHarness { TakeoverActivation = activation };
            await harness.ConnectAsync(false).ConfigureAwait(false);
            await harness.Session.PrepareTakeoverAsync(CreateSavedSession(harness)).AsTask()
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);

            Task reconnect = harness.Session.ReconnectAsync(null, null, harness.CancellationToken);
            await activation.Entered.WaitAsync(PhaseTimeout).ConfigureAwait(false);
            int mark = harness.Requests.Count;
            await harness.Session.DisposeAsync().AsTask().WaitAsync(PhaseTimeout).ConfigureAwait(false);

            Assert.That(async () => await reconnect.WaitAsync(PhaseTimeout).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>().Or.InstanceOf<ServiceResultException>());
            Assert.That(activation.Cancelled.IsCompleted, Is.True);
            IServiceRequest[] after = [.. harness.Requests.ToList().Skip(mark)];
            Assert.That(after, Has.None.InstanceOf<CloseSessionRequest>());
            Assert.That(after, Has.None.InstanceOf<CreateSessionRequest>());
        }

        /// <summary>
        /// While prepared, a channel recovery by the channel manager neither activates the saved session nor
        /// starts the managed recovery; the explicit reconnect still activates it afterwards.
        /// </summary>
        [Test]
        public async Task ChannelRecoveryWhilePreparedDoesNotActivateAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness(System.TimeSpan.Zero);
            await harness.ConnectAsync(false).ConfigureAwait(false);
            await harness.Session.PrepareTakeoverAsync(CreateSavedSession(harness)).AsTask()
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);
            harness.ReleaseAllGates();
            int mark = harness.Requests.Count;

            await harness.StartRecoveryAsync().WaitAsync(PhaseTimeout).ConfigureAwait(false);
            harness.Recovery = null;

            IServiceRequest[] after = [.. harness.Requests.ToList().Skip(mark)];
            Assert.Multiple(() =>
            {
                Assert.That(after, Has.None.InstanceOf<ActivateSessionRequest>());
                Assert.That(after, Has.None.InstanceOf<CreateSessionRequest>());
                Assert.That(harness.Session.IsPrepared, Is.True);
                Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Disconnected));
            });

            await harness.Session.ReconnectAsync(null, null, harness.CancellationToken)
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);
            Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
            Assert.That(harness.Requests.ToList().OfType<ActivateSessionRequest>().Last().RequestHeader
                .AuthenticationToken, Is.EqualTo(TakeoverToken));
        }

        /// <summary>
        /// The takeover keeps the helper's server-revised session timeout instead of the configured one,
        /// through preparation and activation; a later recreation reports its newly revised timeout.
        /// </summary>
        [Test]
        public async Task TakeoverKeepsTheServerRevisedSessionTimeoutAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness(System.TimeSpan.Zero)
            {
                RequestedSessionTimeout = System.TimeSpan.FromSeconds(30),
                RevisedSessionTimeout = 1500
            };
            await harness.ConnectAsync(false).ConfigureAwait(false);
            Assert.That(harness.Session.SessionTimeout, Is.EqualTo(1500));

            await harness.Session.PrepareTakeoverAsync(CreateSavedSession(harness)).AsTask()
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);
            Assert.That(harness.Session.SessionTimeout, Is.EqualTo(1500));
            await harness.Session.ReconnectAsync(null, null, harness.CancellationToken)
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);
            Assert.That(harness.Session.SessionTimeout, Is.EqualTo(1500));

            harness.RevisedSessionTimeout = 3000;
            harness.TakeoverActivationStatus = StatusCodes.BadSessionIdInvalid;
            harness.ReleaseAllGates();
            await harness.StartOuterRecoveryAsync().WaitAsync(PhaseTimeout).ConfigureAwait(false);

            Assert.That(harness.Session.SessionId, Is.Not.EqualTo(TakeoverSessionId));
            Assert.That(harness.Session.SessionTimeout, Is.EqualTo(3000));
            Assert.That(harness.Session.SessionGeneration, Is.EqualTo(3));
        }

        /// <summary>
        /// The mirrored-session reactivation used by replica sets cannot bypass a prepared takeover: it is
        /// rejected without sending anything, and the takeover still activates normally afterwards.
        /// </summary>
        [Test]
        public async Task MirroredReactivationCannotActivateAPreparedSessionAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness();
            await harness.ConnectAsync(false).ConfigureAwait(false);
            await harness.Session.PrepareTakeoverAsync(CreateSavedSession(harness)).AsTask()
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);
            int mark = harness.Requests.Count;

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await harness.Session.ReactivateMirroredSessionAsync(
                    harness.Session.ConfiguredEndpoint, harness.CancellationToken).ConfigureAwait(false));

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(harness.Requests.ToList().Skip(mark), Has.None.InstanceOf<ActivateSessionRequest>());
            Assert.That(harness.Session.IsPrepared, Is.True);
            await harness.Session.ReconnectAsync(null, null, harness.CancellationToken)
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);
            Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
        }

        /// <summary>
        /// Creates the saved state of another owner's server session on the harness endpoint.
        /// </summary>
        private static SessionConfiguration CreateSavedSession(ManagedSessionReconnectHarness harness)
        {
            return harness.Session.SaveSessionConfiguration() with
            {
                SessionId = TakeoverSessionId,
                AuthenticationToken = TakeoverToken
            };
        }
    }
}
