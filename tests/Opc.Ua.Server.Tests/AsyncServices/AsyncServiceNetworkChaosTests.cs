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
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.TestModels.AsyncExecution;
using Model = Opc.Ua.TestModels.AsyncExecution;
using WireContext = Opc.Ua.Server.Tests.AsyncServices.AsyncServiceTransportTests.WireContext;

namespace Opc.Ua.Server.Tests.AsyncServices
{
    /// <summary>
    /// Opt-in, fail-fast recovery soak over encrypted binary and HTTPS JSON native connections.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class AsyncServiceNetworkChaosTests
    {
        /// <summary>
        /// Repeatedly loses replies and disrupts pending work without replay, replacement Sessions or mutable results.
        /// </summary>
        [Test]
        [Explicit("One-hour network chaos campaign; select this test explicitly.")]
        [Category("Soak")]
        [Category("LongRunning")]
        [CancelAfter(3_750_000)]
        public async Task RecoverableExecutionSurvivesSeededNetworkChaos(CancellationToken ct)
        {
            int seconds = ReadSetting("OPCUA_RSE_SOAK_SECONDS", 3600, 1, 3600);
            int seed = ReadSetting("OPCUA_CHAOS_SEED", 4813, 0, int.MaxValue);
            var random = new UnsecureRandom(seed);
            TimeSpan duration = TimeSpan.FromSeconds(seconds);
            var scenarios = new[] { new AsyncExecutionScenario { Delay = true }, new() { Delay = true } };
            await using WireContext binary = await WireContext.CreateAsync(
                false, scenarios[0], lifetime: duration + TimeSpan.FromMinutes(2), networkChaos: true)
                .ConfigureAwait(false);
            await using WireContext json = await WireContext.CreateAsync(
                true, scenarios[1], lifetime: duration + TimeSpan.FromMinutes(2), networkChaos: true)
                .ConfigureAwait(false);
            WireContext[] peers = [binary, json];
            NodeId[] sessions = [binary.Owner.SessionId, json.Owner.SessionId];
            int[] completed = [0, 0];
            int[] faults = [0, 0, 0, 0];
            var clock = Stopwatch.StartNew();
            int cycles = 0;
            TestContext.Progress.WriteLine($"RSE chaos started seed={seed} seconds={seconds}");
            try
            {
                while (clock.Elapsed < duration)
                {
                    int peer = cycles % peers.Length;
                    int mode = cycles / peers.Length % faults.Length;
                    WireContext wire = peers[peer];
                    AsyncExecutionScenario scenario = scenarios[peer];
                    using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    watchdog.CancelAfter(TimeSpan.FromSeconds(45));
                    CancellationToken cycleToken = watchdog.Token;
                    scenario.ResetGate();
                    int amount = random.Next(1, 100);
                    int expected = checked(scenario.Counter + amount);
                    var probe = ExpandedNodeId.ToNodeId(Model.ObjectIds.Probe, wire.Owner.NamespaceUris);
                    var client = new AsyncServiceClient();
                    await using AsyncServiceOperation<int> operation = await client.CreateMethodAsync(
                        wire.Owner,
                        (session, token) => new ProbeTypeClient(session, probe, wire.Telemetry)
                            .DelayedAddAsync(amount, token),
                        cycleToken).ConfigureAwait(false);
                    operation.RequireExplicitAcknowledgement();
                    int drops = wire.Faults.Drops;
                    string point = mode switch { 0 => "submit", 1 => "result", 2 => "ack", _ => string.Empty };
                    if (point.Length != 0)
                    {
                        wire.Faults.Arm(point);
                    }
                    if (mode == 0)
                    {
                        _ = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await operation.SubmitAsync(60_000, 60_000, ct: cycleToken).ConfigureAwait(false))
                            .ConfigureAwait(false);
                        Assert.That(operation.ServerToken.IsNull, Is.True, "Lost Submit must recover by Client ID.");
                        await wire.RecoverAsync(cycleToken).ConfigureAwait(false);
                    }
                    else
                    {
                        _ = await operation.SubmitAsync(60_000, 60_000, ct: cycleToken).ConfigureAwait(false);
                    }
                    await scenario.Entered.WaitAsync(cycleToken).ConfigureAwait(false);
                    if (mode == 3)
                    {
                        if (random.Next(2) == 0)
                        {
                            Task unblock = wire.Proxy!.BlockAcceptAsync(
                                TimeSpan.FromMilliseconds(random.Next(100, 501)));
                            await wire.Proxy.DropAllConnectionsAsync().ConfigureAwait(false);
                            await unblock.WaitAsync(cycleToken).ConfigureAwait(false);
                        }
                        else
                        {
                            wire.Proxy!.StallForwarding = true;
                            try
                            {
                                _ = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                                    await operation.CollectAsync(ct: cycleToken).ConfigureAwait(false))
                                    .ConfigureAwait(false);
                            }
                            finally
                            {
                                wire.Proxy.StallForwarding = false;
                                await wire.Proxy.DropAllConnectionsAsync().ConfigureAwait(false);
                            }
                        }
                        await wire.RecoverAsync(cycleToken).ConfigureAwait(false);
                        ServiceOperationInfoDataType pending = await operation.CollectAsync(ct: cycleToken)
                            .ConfigureAwait(false);
                        Assert.That(pending.State, Is.EqualTo(ServiceOperationState.Pending));
                        Assert.That(pending.CancellationRequested, Is.False);
                    }
                    scenario.Release();
                    if (mode == 1)
                    {
                        _ = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await operation.CollectAsync(2000, cycleToken).ConfigureAwait(false))
                            .ConfigureAwait(false);
                        await wire.RecoverAsync(cycleToken).ConfigureAwait(false);
                    }
                    _ = await operation.CollectAsync(2000, cycleToken).ConfigureAwait(false);
                    AsyncServiceResult<int> result = await operation.Completion.WaitAsync(cycleToken)
                        .ConfigureAwait(false);
                    Assert.That(result.GetValueOrThrow(), Is.EqualTo(expected));

                    // A fresh collection must return the identical retained original, not run the Method again.
                    _ = await operation.CollectAsync(2000, cycleToken).ConfigureAwait(false);
                    byte[][] originals = [.. wire.Faults.OriginalResponses];
                    Assert.That(originals, Has.Length.GreaterThanOrEqualTo(2));
                    Assert.That(originals, Is.All.EqualTo(originals[0]));
                    double previous = double.MaxValue;
                    while (wire.Faults.CollectionRetention.TryDequeue(out double remaining))
                    {
                        Assert.That(remaining, Is.LessThanOrEqualTo(previous));
                        previous = remaining;
                    }
                    wire.Faults.OriginalResponses.Clear();
                    wire.Faults.CollectionHandles.Clear();
                    if (mode == 2)
                    {
                        _ = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await operation.AcknowledgeAsync(cycleToken).ConfigureAwait(false)).ConfigureAwait(false);
                        await wire.RecoverAsync(cycleToken).ConfigureAwait(false);
                    }
                    ControlResponseDataType ack = await operation.AcknowledgeAsync(cycleToken).ConfigureAwait(false);
                    Assert.That(ack.Result, Is.EqualTo(mode == 2
                        ? ServiceOperationControlResult.AlreadyApplied : ServiceOperationControlResult.Applied));
                    Assert.That(ack.Operation.State, Is.EqualTo(ServiceOperationState.Acknowledged));
                    Assert.That(operation.Invalidated.IsCompleted, Is.False);
                    Assert.That(wire.Owner.SessionId, Is.EqualTo(sessions[peer]));
                    Assert.That(wire.Faults.Drops, Is.EqualTo(drops + (mode == 3 ? 0 : 1)));
                    completed[peer]++;
                    faults[mode]++;
                    cycles++;
                    Assert.That(scenario.Invocations, Is.EqualTo(completed[peer]), "Original Method replayed.");
                    Assert.That(wire.Faults.Submissions, Is.EqualTo(completed[peer]), "Submit replayed.");
                    Assert.That(scenario.Counter, Is.EqualTo(expected));
                    Assert.That(wire.Proxy!.ActiveConnections, Is.GreaterThan(0));
                    if (cycles % 16 == 0)
                    {
                        TestContext.Progress.WriteLine(
                            $"RSE chaos seed={seed} elapsed={clock.Elapsed} cycles={cycles}");
                    }
                }
                Assert.That(completed, Is.All.GreaterThan(0));
                Assert.That(faults, Is.All.GreaterThan(0), "Campaign must exercise all four recovery paths.");
                Assert.That(binary.Proxy!.TotalAccepted, Is.GreaterThan(1));
                Assert.That(json.Proxy!.TotalAccepted, Is.GreaterThan(1));
                Assert.That(binary.Proxy.BytesForwarded, Is.GreaterThan(0));
                Assert.That(json.Proxy.BytesForwarded, Is.GreaterThan(0));
            }
            finally
            {
                TestContext.Progress.WriteLine(
                    $"RSE chaos seed={seed} elapsed={clock.Elapsed} cycles={cycles} " +
                    $"binary={completed[0]} json={completed[1]} faults={string.Join(",", faults)}");
            }
        }

        /// <summary>
        /// Reads a bounded reproducibility setting without silently accepting a malformed campaign.
        /// </summary>
        private static int ReadSetting(string name, int fallback, int minimum, int maximum)
        {
            string? text = Environment.GetEnvironmentVariable(name);
            if (text == null)
            {
                return fallback;
            }
            Assert.That(int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) &&
                value >= minimum && value <= maximum, Is.True, $"Invalid {name}.");
            return value;
        }
    }
}
