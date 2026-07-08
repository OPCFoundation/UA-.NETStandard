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

using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Stress.Tests.Channels.Fakes;
using Opc.Ua.Stress.Tests.Channels.Helpers;
using Opc.Ua.Stress.Tests.Channels.Integration;

namespace Opc.Ua.Stress.Tests.Channels.Chaos
{
    /// <summary>
    /// Layer-3 TCP chaos tests for block-accept reconnect handling.
    /// </summary>
    [TestFixture]
    [Category("ChannelManager")]
    [Category("ChaosTCP")]
    [NonParallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class BlockAcceptChaosTests : IntegrationTestBase
    {
        /// <summary>
        /// L3-A5: verifies a managed session survives mixed connection drops and blocked accepts.
        /// </summary>
        /// <param name="ct">Cancellation token supplied by NUnit.</param>
        /// <exception cref="ServiceResultException"></exception>
        [Test]
        [Category("ChaosTCP")]
        [CancelAfter(180_000)]
        public async Task SessionSurvivesMixedDropAndBlockAcceptAsync(CancellationToken ct)
        {
            int seed = TestRunSeed.Get();
            TestContext.Out.WriteLine(FormattableString.Invariant($"L3-A5 seed={seed}"));

            TcpChaosProxy proxy = await TcpChaosProxy.StartAsync(ServerUrl, telemetry: Telemetry)
                .ConfigureAwait(false);
            try
            {
                ClientChannelManager manager = CreateChannelManager(
                    new ExponentialBackoffChannelReconnectPolicy
                    {
                        MinDelay = TimeSpan.FromMilliseconds(200),
                        MaxDelay = TimeSpan.FromSeconds(2),
                        MaxAttempts = 20
                    });
                try
                {
                    ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.None, proxy.LocalUrl)
                        .ConfigureAwait(false);
                    endpoint.EndpointUrl = proxy.LocalUrl;
                    endpoint.Description.Server.DiscoveryUrls = [proxy.LocalUrl.ToString()];
                    endpoint.UpdateBeforeConnect = false;

                    ManagedSession? session = null;
                    StressRunner? runner = null;
                    try
                    {
                        session = await ConnectManagedSessionAsync(endpoint, manager, "L3-A5", ct)
                            .ConfigureAwait(false);

                        async Task ReadServerCurrentTimeAsync(CancellationToken workerCt)
                        {
                            DataValue value = await session
                                .ReadValueAsync(VariableIds.Server_ServerStatus_CurrentTime, workerCt)
                                .ConfigureAwait(false);
                            if (value.IsNull || StatusCode.IsBad(value.StatusCode))
                            {
                                throw new ServiceResultException(value.StatusCode);
                            }
                        }

                        async Task DispatchChaosEventAsync(ChaosEvent chaosEvent, CancellationToken workerCt)
                        {
                            workerCt.ThrowIfCancellationRequested();

                            switch (chaosEvent.Kind)
                            {
                                case ChaosEventKind.DropAllConnections:
                                    await proxy.DropAllConnectionsAsync().ConfigureAwait(false);
                                    break;
                                case ChaosEventKind.BlockAccept:
                                    await proxy.BlockAcceptAsync(BlockAcceptDuration).ConfigureAwait(false);
                                    break;
                            }
                        }

                        runner = new StressRunner(
                            [ReadServerCurrentTimeAsync],
                            concurrency: 4,
                            targetOpsPerSecond: 50);

                        await runner.StartAsync(ct).ConfigureAwait(false);
                        try
                        {
                            using var collector = new MetricsCollector();
                            ChaosSchedule schedule = CreateMixedDropAndBlockAcceptSchedule(seed);
                            var dispatcher = new ChaosScheduleRunner(schedule, DispatchChaosEventAsync);
                            try
                            {
                                await dispatcher.RunAsync(ct).ConfigureAwait(false);
                                await runner.StopAsync().ConfigureAwait(false);
                                await WaitForQuiescence.ForManagerAsync(manager, DefaultWait, ct: ct)
                                    .ConfigureAwait(false);

                                int dropEventCount = schedule.Events.Count(static ev =>
                                    ev.Kind == ChaosEventKind.DropAllConnections);
                                Assert.That(
                                    runner.FailureRate,
                                    Is.LessThan(0.10),
                                    FormattableString.Invariant(
                                        $"FailureRate={runner.FailureRate} (seed={seed})"));
                                Assert.That(
                                    collector.CountEvents("ReconnectCompleted"),
                                    Is.GreaterThanOrEqualTo(dropEventCount),
                                    FormattableString.Invariant(
                                        $"Expected reconnects ≥ drop events (seed={seed})"));
                            }
                            finally
                            {
                                await dispatcher.DisposeAsync().ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            await runner.StopAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        if (runner != null)
                        {
                            await runner.DisposeAsync().ConfigureAwait(false);
                        }

                        await CloseAndDisposeAsync(session).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await manager.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await proxy.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static ChaosSchedule CreateMixedDropAndBlockAcceptSchedule(int seed)
        {
            ChaosEventKind[] weightedKinds =
            [
                ChaosEventKind.DropAllConnections,
                ChaosEventKind.DropAllConnections,
                ChaosEventKind.DropAllConnections,
                ChaosEventKind.DropAllConnections,
                ChaosEventKind.DropAllConnections,
                ChaosEventKind.DropAllConnections,
                ChaosEventKind.DropAllConnections,
                ChaosEventKind.BlockAccept,
                ChaosEventKind.BlockAccept,
                ChaosEventKind.BlockAccept
            ];

            return new ChaosSchedule(
                seed,
                TimeSpan.FromSeconds(60),
                weightedKinds,
                minInterval: TimeSpan.FromSeconds(5),
                maxInterval: TimeSpan.FromSeconds(15));
        }

        private const string SeedEnvironmentVariableName = "OPCUA_CHAOS_SEED";
        private static readonly TimeSpan BlockAcceptDuration = TimeSpan.FromSeconds(3);

        private static class TestRunSeed
        {
            public static int Get()
            {
                string? value = Environment.GetEnvironmentVariable(SeedEnvironmentVariableName);
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
                {
                    return seed;
                }

                return RandomNumberGenerator.GetInt32(int.MaxValue);
            }
        }
    }
}
