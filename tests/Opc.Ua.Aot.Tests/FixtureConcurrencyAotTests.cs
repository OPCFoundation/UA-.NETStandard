/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Opc.Ua.Client;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Verifies that the shared fixture supports overlapping discovery and
    /// independent sessions without exhausting a small test-only channel quota.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public sealed class FixtureConcurrencyAotTests(AotTestFixture fixture)
    {
        /// <summary>
        /// Verifies discovery and new sessions can overlap without disconnecting sessions already held by the fixture.
        /// </summary>
        [Test]
        public async Task ConcurrentDiscoveryAndSessionCreationPreserveExistingSessionsAsync()
        {
            const int heldSessionCount = 9;
            var sessions = new List<ISession>(heldSessionCount + 1);
            try
            {
                for (int i = 0; i < heldSessionCount; i++)
                {
                    sessions.Add(await fixture.CreateSessionAsync($"HeldSession{i}")
                        .ConfigureAwait(false));
                }

                Task<ISession> newSession = fixture.CreateSessionAsync("ConcurrentSession");
                try
                {
                    await Task.WhenAll(
                        GetEndpointsAsync(),
                        newSession,
                        ReadServerStatusAsync(fixture.Session)).ConfigureAwait(false);
                }
                finally
                {
                    if (newSession.IsCompletedSuccessfully)
                    {
                        sessions.Add(await newSession.ConfigureAwait(false));
                    }
                }

                await Assert.That(sessions.Count).IsEqualTo(heldSessionCount + 1);
                await Assert.That(sessions.Select(session => session.SessionId)
                    .Append(fixture.Session.SessionId).Distinct().Count())
                    .IsEqualTo(heldSessionCount + 2);

                StatusCode status = await sessions[0].CloseAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                await Assert.That(status).IsEqualTo(StatusCodes.Good);
                await Assert.That(sessions[0].Connected).IsFalse();
                await sessions[0].DisposeAsync().ConfigureAwait(false);
                sessions.RemoveAt(0);

                await Task.WhenAll(sessions.Append(fixture.Session)
                    .Select(ReadServerStatusAsync)).ConfigureAwait(false);
            }
            finally
            {
                await Task.WhenAll(sessions.Select(CloseAndDisposeAsync)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Runs an independent discovery request and verifies the shared server still advertises endpoints.
        /// </summary>
        private async Task GetEndpointsAsync()
        {
            var configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 10000;

            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                new Uri(fixture.ServerUrl), configuration, fixture.Telemetry)
                .ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            await Assert.That(endpoints.Count).IsGreaterThan(0);
            await client.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Confirms an established session remains connected and can read a good server status value.
        /// </summary>
        private static async Task ReadServerStatusAsync(ISession session)
        {
            await Assert.That(session.Connected).IsTrue();

            DataValue status = await session.ReadValueAsync(
                VariableIds.Server_ServerStatus, CancellationToken.None).ConfigureAwait(false);

            await Assert.That(status.IsNull).IsFalse();
            await Assert.That(StatusCode.IsGood(status.StatusCode)).IsTrue();
        }

        /// <summary>
        /// Closes a held test session and disposes it even if closing its subscriptions fails.
        /// </summary>
        private static async Task CloseAndDisposeAsync(ISession session)
        {
            await using (session.ConfigureAwait(false))
            {
                if (session.Connected)
                {
                    session.DeleteSubscriptionsOnClose = true;
                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
    }
}
