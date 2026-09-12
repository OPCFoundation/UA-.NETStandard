/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

        private static async Task ReadServerStatusAsync(ISession session)
        {
            await Assert.That(session.Connected).IsTrue();

            DataValue status = await session.ReadValueAsync(
                VariableIds.Server_ServerStatus, CancellationToken.None).ConfigureAwait(false);

            await Assert.That(status.IsNull).IsFalse();
            await Assert.That(StatusCode.IsGood(status.StatusCode)).IsTrue();
        }

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
