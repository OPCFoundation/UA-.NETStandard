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

#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Stress.Tests.Channels.Fakes;
using Opc.Ua.Stress.Tests.Channels.Helpers;

namespace Opc.Ua.Stress.Tests.Channels.Contract
{
    /// <summary>
    /// Layer-1 service-call gate and reactivation-bypass contract tests.
    /// </summary>
    [TestFixture]
    [Category("Contract")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class GateAndBypassTests : ContractTestBase
    {
        [Test]
        [CancelAfter(30_000)]
        [Description("L1-GATE1: service calls queued during reconnect release together when Ready.")]
        public async Task MultipleServiceCallsQueuedDuringReconnectReleaseTogetherWhenReadyAsync(
            CancellationToken ct)
        {
            ContractHarness harness = CreateHarness();
            await using ConfiguredAsyncDisposable harnessAsyncDisposable = harness.ConfigureAwait(false);
            FakeParticipant participant = new(harness.Endpoint);
            IManagedTransportChannel channel = await harness.Manager.GetAsync(participant, ct)
                .ConfigureAwait(false);
            ChaosBarrier reconnectBarrier = new(expectedParticipants: 1);
            Task? reconnectTask = null;

            try
            {
                Assert.That(channel.State, Is.EqualTo(ChannelState.Ready));
                harness.Bindings.ConfigureNextOpenToBlockOn(reconnectBarrier);

                var readySignal = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                long readyTimestamp = 0;
                void OnStateChanged(IManagedTransportChannel _, ChannelStateChange change)
                {
                    if (change.NewState == ChannelState.Ready)
                    {
                        Interlocked.Exchange(ref readyTimestamp, Stopwatch.GetTimestamp());
                        readySignal.TrySetResult(true);
                    }
                }

                channel.StateChanged += OnStateChanged;
                try
                {
                    reconnectTask = harness.Manager.ReconnectAsync(channel, ct).AsTask();
                    await WaitForBarrierArrivalAsync(reconnectBarrier, ct).ConfigureAwait(false);

                    long[] completionTimestamps = new long[ServiceCallCount];
                    Task[] serviceCalls = StartServiceCalls(channel, completionTimestamps, ct);
                    await Task.Delay(GateObservationWindow, ct).ConfigureAwait(false);

                    Assert.That(serviceCalls.Count(task => task.IsCompleted), Is.Zero);

                    reconnectBarrier.Release();
                    await reconnectTask.WaitAsync(DefaultWait, ct).ConfigureAwait(false);
                    await readySignal.Task.WaitAsync(DefaultWait, ct).ConfigureAwait(false);
                    await Task.WhenAll(serviceCalls).WaitAsync(DefaultWait, ct).ConfigureAwait(false);
                    await WaitForQuiescence.ForManagerAsync(harness.Manager, DefaultWait, ct: ct)
                        .ConfigureAwait(false);

                    AssertServiceCallsReleasedTogether(completionTimestamps, readyTimestamp);
                    Assert.That(
                        harness.Bindings.Created.Sum(transport => transport.RequestCount),
                        Is.EqualTo(ServiceCallCount));
                }
                finally
                {
                    channel.StateChanged -= OnStateChanged;
                }
            }
            finally
            {
                reconnectBarrier.Release();
                await IgnoreFailureAsync(reconnectTask).ConfigureAwait(false);
                await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(30_000)]
        [Description("L1-GATE2: reactivation AsyncLocal bypass does not leak to external callers.")]
        public async Task ReactivationBypassDoesNotLeakToOtherCallersAsync(CancellationToken ct)
        {
            ContractHarness harness = CreateHarness();
            await using ConfiguredAsyncDisposable harnessAsyncDisposable = harness.ConfigureAwait(false);
            ChaosBarrier reactivationBarrier = new(expectedParticipants: 1);
            FakeParticipant participant = new(harness.Endpoint);
            participant.ConfigureOnReconnect(async (channel, attempt, participantCt) =>
            {
                _ = attempt;

                await channel.SendRequestAsync(CreateServerStatusReadRequest(), participantCt)
                    .ConfigureAwait(false);
                await reactivationBarrier.SignalAndWaitForReleaseAsync(participantCt).ConfigureAwait(false);
                return ParticipantReconnectResult.Reactivated;
            });
            IManagedTransportChannel managedChannel = await harness.Manager.GetAsync(participant, ct)
                .ConfigureAwait(false);
            Task? reconnectTask = null;

            try
            {
                FakeTransport transport = AssertSingleTransport(harness);
                reconnectTask = harness.Manager.ReconnectAsync(managedChannel, ct).AsTask();

                await WaitForBarrierArrivalAsync(reactivationBarrier, ct).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(
                        GetDiagnostic(harness.Manager, managedChannel.Key).State,
                        Is.EqualTo(ChannelState.TransportConnectedSessionReactivating));
                    Assert.That(transport.RequestCount, Is.EqualTo(1));
                });

                Task<IServiceResponse> externalTask = managedChannel
                    .SendRequestAsync(CreateServerStatusReadRequest(), ct)
                    .AsTask();
                await Task.Delay(GateObservationWindow, ct).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(externalTask.IsCompleted, Is.False);
                    Assert.That(transport.RequestCount, Is.EqualTo(1));
                });

                reactivationBarrier.Release();
                await reconnectTask.WaitAsync(DefaultWait, ct).ConfigureAwait(false);
                await externalTask.WaitAsync(DefaultWait, ct).ConfigureAwait(false);
                await WaitForQuiescence.ForManagerAsync(harness.Manager, DefaultWait, ct: ct)
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(managedChannel.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(transport.RequestCount, Is.EqualTo(2));
                });
            }
            finally
            {
                reactivationBarrier.Release();
                await IgnoreFailureAsync(reconnectTask).ConfigureAwait(false);
                await managedChannel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static Task[] StartServiceCalls(
            IManagedTransportChannel channel,
            long[] completionTimestamps,
            CancellationToken ct)
        {
            return Enumerable.Range(0, completionTimestamps.Length)
                .Select(index => SendAndRecordCompletionAsync(channel, completionTimestamps, index, ct))
                .ToArray();
        }

        private static async Task SendAndRecordCompletionAsync(
            IManagedTransportChannel channel,
            long[] completionTimestamps,
            int index,
            CancellationToken ct)
        {
            await channel.SendRequestAsync(CreateServerStatusReadRequest(), ct).ConfigureAwait(false);
            Interlocked.Exchange(ref completionTimestamps[index], Stopwatch.GetTimestamp());
        }

        private static void AssertServiceCallsReleasedTogether(
            long[] completionTimestamps,
            long readyTimestamp)
        {
            Assert.That(readyTimestamp, Is.GreaterThan(0));
            Assert.That(completionTimestamps, Has.All.GreaterThan(0));

            TimeSpan lastCompletionAfterReady = ElapsedBetween(readyTimestamp, completionTimestamps.Max());
            TimeSpan completionWindow = ElapsedBetween(completionTimestamps.Min(), completionTimestamps.Max());

            Assert.Multiple(() =>
            {
                Assert.That(lastCompletionAfterReady, Is.LessThanOrEqualTo(ReadyReleaseWindow));
                Assert.That(completionWindow, Is.LessThanOrEqualTo(ServiceCallReleaseWindow));
            });
        }

        private static FakeTransport AssertSingleTransport(ContractHarness harness)
        {
            Assert.That(harness.Bindings.Created, Has.Count.EqualTo(1));
            return harness.Bindings.Created[0];
        }

        private static TimeSpan ElapsedBetween(long startTimestamp, long endTimestamp)
        {
            long elapsedTicks = endTimestamp - startTimestamp;
            double elapsedSeconds = (double)elapsedTicks / Stopwatch.Frequency;
            return TimeSpan.FromSeconds(elapsedSeconds);
        }

        private static async Task IgnoreFailureAsync(Task? task)
        {
            if (task == null)
            {
                return;
            }

            try
            {
                await task.WaitAsync(DefaultWait).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup after contract-test failures.
            }
        }

        private const int ServiceCallCount = 10;
        private static readonly TimeSpan GateObservationWindow = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan ReadyReleaseWindow = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan ServiceCallReleaseWindow = TimeSpan.FromMilliseconds(500);
    }
}

