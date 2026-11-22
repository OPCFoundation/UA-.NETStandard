// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Client;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests for discovery and session lifecycle operations.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class DiscoveryAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task GetEndpointsAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 10000;

            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                new Uri(fixture.ServerUrl),
                endpointConfiguration,
                fixture.Telemetry).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints =
                await client.GetEndpointsAsync(default, CancellationToken.None)
                    .ConfigureAwait(false);

            await Assert.That(endpoints.Count).IsGreaterThan(0);
        }

        [Test]
        public async Task FindServersAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 10000;

            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                new Uri(fixture.ServerUrl),
                endpointConfiguration,
                fixture.Telemetry).ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers =
                await client.FindServersAsync(default)
                    .ConfigureAwait(false);

            await Assert.That(servers.Count).IsGreaterThan(0);
        }

        [Test]
        public async Task ConnectAndCloseSessionAsync()
        {
            ISession session = await fixture.CreateSessionAsync("ConnectAndClose")
                .ConfigureAwait(false);

            await Assert.That(session.Connected).IsTrue();

            StatusCode result = await session
                .CloseAsync(CancellationToken.None)
                .ConfigureAwait(false);
            session.Dispose();

            await Assert.That(result).IsEqualTo(StatusCodes.Good);
        }

        [Test]
        public async Task ReadOnDiscoveryChannelAsync()
        {
            ISession session = await fixture.CreateSessionAsync("DiscoveryRead")
                .ConfigureAwait(false);

            DataValue serverStatus = await session.ReadValueAsync(
                VariableIds.Server_ServerStatus,
                CancellationToken.None).ConfigureAwait(false);

            await Assert.That(serverStatus.IsNull).IsFalse();
            await Assert.That(StatusCode.IsGood(serverStatus.StatusCode))
                .IsTrue();

            await session.CloseAsync(CancellationToken.None)
                .ConfigureAwait(false);
            session.Dispose();
        }
    }
}
