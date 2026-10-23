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

using System.Collections.Concurrent;
using System.Linq;
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
    }
}
