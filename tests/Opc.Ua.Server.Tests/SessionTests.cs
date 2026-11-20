// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Event")]
    public class SessionTests
    {
        [Test]
        public async Task UpdateDiagnosticCounters_RaisesEvent_WhenPerRequestCounterChangedAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            await fixture.StartAsync().ConfigureAwait(false);
            StandardServer? server = null;
            SecureChannelContext? secureChannelContext = null;
            RequestHeader? requestHeader = null;

            try
            {
                server = fixture.Server;

                (requestHeader, secureChannelContext) =
                    await server.CreateAndActivateSessionAsync("UpdateDiagnosticCountersTest").ConfigureAwait(false);

                ISession session = server.CurrentInstance.SessionManager.GetSession(requestHeader.AuthenticationToken)!;
                Assert.That(session, Is.Not.Null, "Session should exist after Create/Activate.");

                bool eventRaised = false;

                server.CurrentInstance.SessionManager.SessionDiagnosticsChanged += (s, reason)
                    => eventRaised = true;

                uint before = session.ReadDiagnostics(d => d.ReadCount.TotalCount);

                // Call ValidateRequest for a request type that maps to a counter (Read).
                session.ValidateRequest(requestHeader, secureChannelContext, RequestType.Read);

                Assert.That(eventRaised, Is.True, "SessionDiagnosticsChanged event should be raised when a per-request counter changes.");
                Assert.That(session.ReadDiagnostics(d => d.ReadCount.TotalCount), Is.GreaterThan(before), "ReadCount.TotalCount should have incremented.");
            }
            finally
            {
                await CloseSessionAsync(server!, secureChannelContext!, requestHeader!).ConfigureAwait(false);
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Category("Event")]
        [TestCase(RequestType.Unknown)]
        [TestCase(RequestType.FindServers)]
        [TestCase(RequestType.GetEndpoints)]
        [TestCase(RequestType.CreateSession)]
        [TestCase(RequestType.ActivateSession)]
        [TestCase(RequestType.CloseSession)]
        [TestCase(RequestType.Cancel)]
        public async Task UpdateDiagnosticCounters_DoesNotRaiseEvent_ForIgnoredRequestTypesAsync(RequestType requestType)
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            await fixture.StartAsync().ConfigureAwait(false);
            StandardServer? server = null;
            SecureChannelContext? secureChannelContext = null;
            RequestHeader? requestHeader = null;

            try
            {
                server = fixture.Server;

                (requestHeader, secureChannelContext) =
                    await server.CreateAndActivateSessionAsync("UpdateDiagnosticCountersIgnoredTest").ConfigureAwait(false);

                ISession session = server.CurrentInstance.SessionManager.GetSession(requestHeader.AuthenticationToken)!;
                Assert.That(session, Is.Not.Null, "Session should exist after Create/Activate.");

                bool eventRaised = false;

                server.CurrentInstance.SessionManager.SessionDiagnosticsChanged += (s, reason)
                    => eventRaised = true;

                // Capture total requests before; UpdateDiagnosticCounters always increments TotalRequestCount.
                uint totalBefore = session.ReadDiagnostics(d => d.TotalRequestCount.TotalCount);

                // Call ValidateRequest with one of the ignored request types.
                session.ValidateRequest(requestHeader, secureChannelContext, requestType);

                Assert.That(
                    session.ReadDiagnostics(d => d.TotalRequestCount.TotalCount),
                    Is.EqualTo(totalBefore + 1),
                    "TotalRequestCount should increment for all request types.");

                Assert.That(eventRaised, Is.False, $"SessionDiagnosticsChanged event must NOT be raised for request type {requestType}.");
            }
            finally
            {
                await CloseSessionAsync(server!, secureChannelContext!, requestHeader!).ConfigureAwait(false);
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        // ------------------------------------------------------------
        // Part 18 §4.4.1 — live identity re-evaluation contract
        // ------------------------------------------------------------

        [Test]
        public async Task IsIdentityStale_FreshSession_StartsFalseAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            await fixture.StartAsync().ConfigureAwait(false);
            StandardServer? server = null;
            SecureChannelContext? secureChannelContext = null;
            RequestHeader? requestHeader = null;
            try
            {
                server = fixture.Server;
                (requestHeader, secureChannelContext) =
                    await server.CreateAndActivateSessionAsync("StaleFresh").ConfigureAwait(false);

                ISession session = server.CurrentInstance.SessionManager
                    .GetSession(requestHeader.AuthenticationToken)!;

                Assert.That(session!.IsIdentityStale, Is.False,
                    "Newly activated sessions must not be marked stale.");
            }
            finally
            {
                await CloseSessionAsync(server!, secureChannelContext!, requestHeader!).ConfigureAwait(false);
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task MarkIdentityStale_SetsFlag_AndRefreshClearsItAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            await fixture.StartAsync().ConfigureAwait(false);
            StandardServer? server = null;
            SecureChannelContext? secureChannelContext = null;
            RequestHeader? requestHeader = null;
            try
            {
                server = fixture.Server;
                (requestHeader, secureChannelContext) =
                    await server.CreateAndActivateSessionAsync("StaleFlag").ConfigureAwait(false);

                ISession session = server.CurrentInstance.SessionManager
                    .GetSession(requestHeader.AuthenticationToken)!;

                IUserIdentity original = session!.EffectiveIdentity;
                Assert.That(original, Is.Not.Null);

                session.MarkIdentityStale();
                Assert.That(session.IsIdentityStale, Is.True);

                // Calling MarkIdentityStale again is idempotent.
                session.MarkIdentityStale();
                Assert.That(session.IsIdentityStale, Is.True);

                session.RefreshEffectiveIdentity(original);
                Assert.That(session.IsIdentityStale, Is.False,
                    "RefreshEffectiveIdentity must clear the stale flag.");
                Assert.That(session.EffectiveIdentity, Is.SameAs(original),
                    "Refresh must replace the EffectiveIdentity atomically.");
            }
            finally
            {
                await CloseSessionAsync(server!, secureChannelContext!, requestHeader!).ConfigureAwait(false);
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RefreshEffectiveIdentity_NullIdentity_ThrowsAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            await fixture.StartAsync().ConfigureAwait(false);
            StandardServer? server = null;
            SecureChannelContext? secureChannelContext = null;
            RequestHeader? requestHeader = null;
            try
            {
                server = fixture.Server;
                (requestHeader, secureChannelContext) =
                    await server.CreateAndActivateSessionAsync("StaleArg").ConfigureAwait(false);

                ISession session = server.CurrentInstance.SessionManager
                    .GetSession(requestHeader.AuthenticationToken)!;

                Assert.Throws<System.ArgumentNullException>(
                    () => session!.RefreshEffectiveIdentity(null!),
                    "Passing a null identity must throw to prevent corrupt session state.");
            }
            finally
            {
                await CloseSessionAsync(server!, secureChannelContext!, requestHeader!).ConfigureAwait(false);
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TryRefreshEffectiveIdentityRejectsAnObsoleteIdentityGenerationAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            await fixture.StartAsync().ConfigureAwait(false);
            StandardServer? server = null;
            SecureChannelContext? secureChannelContext = null;
            RequestHeader? requestHeader = null;
            try
            {
                server = fixture.Server;
                (requestHeader, secureChannelContext) =
                    await server.CreateAndActivateSessionAsync("IdentityGeneration").ConfigureAwait(false);

                ISession session = server.CurrentInstance.SessionManager
                    .GetSession(requestHeader.AuthenticationToken)!;
                IdentityRefreshSnapshot snapshot = session!.CaptureIdentityRefreshSnapshot();
                session.MarkIdentityStale();

                bool refreshed = session.TryRefreshEffectiveIdentity(
                    snapshot.Identity,
                    snapshot.Generation,
                    new UserIdentity());

                Assert.That(refreshed, Is.False);
                Assert.That(session.IsIdentityStale, Is.True);
            }
            finally
            {
                await CloseSessionAsync(server!, secureChannelContext!, requestHeader!).ConfigureAwait(false);
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The identity state is read without the session lock while identity changes race with
        /// it: every reader sees the generation only grow, a snapshot taken right after a change
        /// is current, and the last refresh leaves the identity fresh.
        /// </summary>
        [Test]
        public async Task IdentityRefreshStateReadsStayConsistentWhileIdentityChangesAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            await fixture.StartAsync().ConfigureAwait(false);
            StandardServer? server = null;
            SecureChannelContext? secureChannelContext = null;
            RequestHeader? requestHeader = null;
            try
            {
                server = fixture.Server;
                (requestHeader, secureChannelContext) =
                    await server.CreateAndActivateSessionAsync("IdentityRace").ConfigureAwait(false);
                ISession session = server.CurrentInstance.SessionManager
                    .GetSession(requestHeader.AuthenticationToken)!;

                const int changes = 2000;
                using var stop = new CancellationTokenSource();
                Task<string?>[] readers = [.. Enumerable.Range(0, 4).Select(reader => Task.Run(() =>
                {
                    long last = session.CaptureIdentityRefreshSnapshot().Generation;
                    while (!stop.IsCancellationRequested)
                    {
                        _ = session.IsIdentityStale;
                        IdentityRefreshSnapshot snapshot = session.CaptureIdentityRefreshSnapshot();
                        if (snapshot.Identity == null)
                        {
                            return "identity missing";
                        }
                        if (snapshot.Generation < last)
                        {
                            return $"generation went back from {last} to {snapshot.Generation}";
                        }
                        last = snapshot.Generation;
                    }
                    return (string?)null;
                }))];

                long before = session.CaptureIdentityRefreshSnapshot().Generation;
                for (int ii = 0; ii < changes; ii++)
                {
                    session.MarkIdentityStale();
                    Assert.That(session.IsIdentityStale, Is.True);
                    IdentityRefreshSnapshot current = session.CaptureIdentityRefreshSnapshot();
                    Assert.That(
                        session.TryRefreshEffectiveIdentity(current.Identity, current.Generation, new UserIdentity()),
                        Is.True,
                        "A snapshot taken right after the change must be current.");
                    Assert.That(session.IsIdentityStale, Is.False);
                }
                stop.Cancel();
                string?[] failures = await Task.WhenAll(readers).ConfigureAwait(false);

                Assert.That(failures.Where(f => f != null), Is.Empty);
                Assert.That(session.CaptureIdentityRefreshSnapshot().Generation, Is.EqualTo(before + changes));
            }
            finally
            {
                await CloseSessionAsync(server!, secureChannelContext!, requestHeader!).ConfigureAwait(false);
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The subscriptions of one session publish and acknowledge in parallel: a notification
        /// operation does not wait for another one of the same session, but an identity commit
        /// waits until the running operations have left.
        /// </summary>
        [Test]
        public async Task NotificationOperationsRunConcurrentlyAndIdentityCommitExcludesThemAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            await fixture.StartAsync().ConfigureAwait(false);
            StandardServer? server = null;
            SecureChannelContext? secureChannelContext = null;
            RequestHeader? requestHeader = null;
            using var firstEntered = new ManualResetEventSlim();
            using var releaseFirst = new ManualResetEventSlim();
            try
            {
                server = fixture.Server;
                (requestHeader, secureChannelContext) =
                    await server.CreateAndActivateSessionAsync("NotificationGate").ConfigureAwait(false);
                var session = (Opc.Ua.Server.Session)server.CurrentInstance.SessionManager
                    .GetSession(requestHeader.AuthenticationToken)!;

                Task<bool> first = Task.Run(() => session.ExecuteNotificationOperation(() =>
                {
                    firstEntered.Set();
                    return releaseFirst.Wait(System.TimeSpan.FromSeconds(30));
                }));
                Assert.That(firstEntered.Wait(System.TimeSpan.FromSeconds(10)), Is.True);

                Task<bool> second = Task.Run(() => session.ExecuteNotificationOperation(() => true));
                Assert.That(
                    second.Wait(System.TimeSpan.FromSeconds(10)),
                    Is.True,
                    "A notification operation must not wait for another one of the same session.");

                Task commit = Task.Run(session.MarkIdentityStale);
                await Task.Delay(200).ConfigureAwait(false);
                Assert.That(commit.IsCompleted, Is.False, "An identity commit must wait for a running operation.");

                releaseFirst.Set();
                Assert.That(await first.ConfigureAwait(false), Is.True);
                await commit.ConfigureAwait(false);
                Assert.That(session.IsIdentityStale, Is.True);
            }
            finally
            {
                releaseFirst.Set();
                await CloseSessionAsync(server!, secureChannelContext!, requestHeader!).ConfigureAwait(false);
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private static async Task CloseSessionAsync(
            StandardServer server,
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader)
        {
            if (server == null || secureChannelContext == null || requestHeader == null)
            {
                return;
            }

            await server
                .CloseSessionAsync(secureChannelContext, requestHeader, true, RequestLifetime.None)
                .ConfigureAwait(false);
        }
    }
}
