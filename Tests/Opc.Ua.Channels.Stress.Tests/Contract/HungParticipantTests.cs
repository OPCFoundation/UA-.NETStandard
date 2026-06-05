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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Channels.Stress.Tests.Fakes;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Channels.Stress.Tests.Contract
{
    /// <summary>
    /// Layer-1 hung-participant contract tests for managed channel reconnect cycles.
    /// </summary>
    [TestFixture]
    [Category("Contract")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class HungParticipantTests : ContractTestBase
    {
        [Test]
        [Description("L1-HUNG1: documents the unbounded participant reactivation production gap.")]
        [Explicit("Documents production gap: OnReconnectAsync has no bounded timeout. A hung participant blocks " +
            "reconnect indefinitely. When bounded timeout lands, update this test to assert the cycle aborts within " +
            "the timeout window.")]
        public async Task HungParticipantBlocksReconnectIndefinitelyAsync()
        {
            using Certificate applicationCertificate = CreateCertificate("hung-participant-explicit");
            await using ContractTestEnvironment environment = CreateEnvironment(applicationCertificate);
            ConfiguredEndpoint endpoint = CreateEndpoint("hung-participant-shared");
            var participant1 = new FakeParticipant(endpoint)
            {
                HangFor = TimeSpan.FromMinutes(10)
            };
            FakeParticipant participant2 = CreateParticipant(endpoint);

            IManagedTransportChannel ch1 = await environment.Manager.GetAsync(participant1, default)
                .ConfigureAwait(false);
            IManagedTransportChannel ch2 = await environment.Manager.GetAsync(participant2, default)
                .ConfigureAwait(false);
            Assert.That(ch2.Key, Is.EqualTo(ch1.Key));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            Task reconnectTask = environment.Manager.ReconnectAsync(ch1, cts.Token).AsTask();

            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await reconnectTask.ConfigureAwait(false));
            Assert.That(
                GetDiagnostic(environment.Manager, ch1.Key).State,
                Is.Not.EqualTo(ChannelState.Ready));
        }

        [Test]
        [CancelAfter(30_000)]
        [Description("L1-HUNG2: hung participant on one channel does not block reconnect of another channel.")]
        public async Task HungParticipantOnOneChannelDoesNotBlockOtherChannelAsync(
            CancellationToken ct)
        {
            using Certificate applicationCertificate = CreateCertificate("hung-participant-independent");
            await using ContractTestEnvironment environment = CreateEnvironment(applicationCertificate);
            var hungParticipant = new FakeParticipant(CreateEndpoint("hung-channel-a"))
            {
                HangFor = TimeSpan.FromMinutes(10)
            };
            FakeParticipant normalParticipant = CreateParticipant(CreateEndpoint("normal-channel-b"));

            IManagedTransportChannel chA = await environment.Manager.GetAsync(hungParticipant, ct)
                .ConfigureAwait(false);
            IManagedTransportChannel chB = await environment.Manager.GetAsync(normalParticipant, ct)
                .ConfigureAwait(false);

            using var ctsA = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            Task taskA = IgnoreOperationCanceledAsync(
                environment.Manager.ReconnectAsync(chA, ctsA.Token).AsTask());
            await WaitForHungReconnectAsync(environment.Manager, chA.Key, hungParticipant, ct)
                .ConfigureAwait(false);

            Stopwatch sw = Stopwatch.StartNew();
            await environment.Manager.ReconnectAsync(chB, ct).ConfigureAwait(false);
            sw.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
                Assert.That(chB.State, Is.EqualTo(ChannelState.Ready));
            });

            await taskA.ConfigureAwait(false);
        }

        private static FakeParticipant CreateParticipant(ConfiguredEndpoint endpoint)
        {
            return new FakeParticipant(endpoint);
        }

        private static async Task IgnoreOperationCanceledAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static ManagedChannelDiagnostic GetDiagnostic(
            ClientChannelManager manager,
            ManagedChannelKey key)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            return manager.GetChannelDiagnostics().Single(diagnostic => diagnostic.Key.Equals(key));
        }

        private static async Task WaitForHungReconnectAsync(
            ClientChannelManager manager,
            ManagedChannelKey key,
            FakeParticipant participant,
            CancellationToken ct)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (participant == null)
            {
                throw new ArgumentNullException(nameof(participant));
            }

            await WaitUntilAsync(
                    () => participant.NotificationCount > 0 &&
                        GetDiagnostic(manager, key).State == ChannelState.TransportConnectedSessionReactivating,
                    "Hung participant did not enter the reconnect notification path before timeout.",
                    ct)
                .ConfigureAwait(false);
        }
    }
}
