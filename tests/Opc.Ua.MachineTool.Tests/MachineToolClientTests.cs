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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises the typed client through real sessions instead of inspecting a server-side shadow.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class MachineToolClientTests
    {
        /// <summary>
        /// Observation bounds reject invalid requests without creating network or subscription resources.
        /// </summary>
        [TestCase(0, 1)]
        [TestCase(-1, 1)]
        [TestCase(60.001, 1)]
        [TestCase(1, 0)]
        [TestCase(1, 101)]
        public void ObservationBoundsRejectInvalidLimits(double seconds, int count)
        {
            var options = new MachineToolObservationOptions
            {
                Window = TimeSpan.FromSeconds(seconds),
                MaximumNotifications = count
            };

            Assert.That(options.Validate, Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Both exact upper bounds and the smallest useful finite window are accepted.
        /// </summary>
        [TestCase(60, 100)]
        [TestCase(0.001, 1)]
        public void ObservationBoundsAcceptValidLimits(double seconds, int count)
        {
            var options = new MachineToolObservationOptions
            {
                Window = TimeSpan.FromSeconds(seconds),
                MaximumNotifications = count
            };

            Assert.That(options.Validate, Throws.Nothing);
        }

        /// <summary>
        /// Discovery and generated-proxy navigation retain raw sample quality and optional absence.
        /// </summary>
        [Test]
        public async Task ClientDiscoversAndReadsMachineSamplesOverTcpAsync()
        {
            await using var server = new MachineToolRuntimeFixture();
            await server.StartAsync().ConfigureAwait(false);
            DateTimeUtc acquired = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            var provider = new InMemoryMachineToolProvider(new MachineToolSnapshot
            {
                Operation = new MachineToolOperation
                {
                    Mode = MachineOperationMode.Automatic,
                    Sample = new MachineToolSampleInfo(acquired, StatusCodes.UncertainLastUsableValue)
                },
                ActiveProgram = new MachineToolProgram
                {
                    Name = "MAIN.NC",
                    Phase = MachineToolProductionPhase.Running,
                    Sample = new MachineToolSampleInfo(acquired, StatusCodes.Good)
                }
            });
            await using MachineToolRuntime<MachineToolState> machine = await server.Context
                .AddMachineTool("ClientMachine")
                .WithProvider(provider)
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Client test");
                    identity.SerialNumber = "MT-CLIENT";
                    identity.ProductInstanceUri = "urn:test:mt-client";
                }).BuildAsync().ConfigureAwait(false);
            server.Context.DeviceSet.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, false, machine.NodeId);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            transport.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            await transport.LoadClientConfigurationAsync(server.Root, "MachineToolClient").ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await transport.ConnectAsync(server.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
                ArrayOf<MachineToolDeviceEntry> devices = await client.DiscoverAsync().ConfigureAwait(false);
                Assert.That(devices.Count, Is.EqualTo(1));
                Assert.That(devices[0].NodeId, Is.EqualTo(machine.NodeId));
                Assert.That(devices[0].Kind, Is.EqualTo(MachineToolDeviceKind.MachineTool));
                Assert.That(client.AvailableParts, Is.EqualTo(MachineToolParts.MachineTool));

                MachineToolReadSnapshot snapshot = await client.ReadSnapshotAsync(machine.NodeId).ConfigureAwait(false);
                Assert.That(snapshot.OperationMode.StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
                Assert.That(snapshot.OperationMode.SourceTimestamp, Is.EqualTo(acquired));
                Assert.That(snapshot.OperationMode.WrappedValue.TryGetValue(out int mode), Is.True);
                Assert.That(mode, Is.EqualTo((int)MachineOperationMode.Automatic));
                Assert.That(snapshot.ActiveProgramName.WrappedValue.TryGetValue(out string program), Is.True);
                Assert.That(program, Is.EqualTo("MAIN.NC"));
                Assert.That(snapshot.FeedOverride.IsNull, Is.True);
                Assert.That(snapshot.IsWarmUp.IsNull, Is.True);
                Assert.That(session.TryGetSubscriptionManager(out ISubscriptionManager? subscriptions), Is.True);
                int before = subscriptions!.Count;
                MachineToolDataObservation observation = await client.ObserveDataAsync(
                    machine.NodeId,
                    new MachineToolObservationOptions
                    {
                        Window = TimeSpan.FromSeconds(10),
                        MaximumNotifications = 3
                    }).ConfigureAwait(false);
                Assert.That(observation.Changes.Count, Is.EqualTo(3));
                Assert.That(observation.LimitReached, Is.True);
                DataValue observedMode = observation.Changes.ToArray()!
                    .Single(change => change.NodeId == machine.State.Monitoring!.MachineTool!.OperationMode!.NodeId)
                    .Value;
                Assert.That(observedMode.StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
                Assert.That(observedMode.SourceTimestamp, Is.EqualTo(acquired));
                Assert.That(subscriptions.Count, Is.EqualTo(before),
                    "One-shot observation must release only the subscription it created.");
                MachineToolDataObservation selected = await client.ObserveValuesAsync(
                    [machine.State.Monitoring!.MachineTool!.OperationMode!.NodeId],
                    new MachineToolObservationOptions
                    {
                        Window = TimeSpan.FromSeconds(10),
                        MaximumNotifications = 1
                    }).ConfigureAwait(false);
                Assert.That(selected.Changes.Count, Is.EqualTo(1));
                Assert.That(selected.Changes[0].Value.StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
                Assert.That(selected.Changes[0].Value.SourceTimestamp, Is.EqualTo(acquired));
                Assert.That(subscriptions.Count, Is.EqualTo(before));
                Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                    await client.ObserveValuesAsync(ArrayOf<NodeId>.Empty).ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentException>(async () =>
                    await client.ObserveValuesAsync([machine.NodeId, machine.NodeId]).ConfigureAwait(false));
                var oversizedSelection = new NodeId[257];
                for (int index = 0; index < oversizedSelection.Length; index++)
                {
                    oversizedSelection[index] = new NodeId((uint)(index + 1), machine.NodeId.NamespaceIndex);
                }
                Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                    await client.ObserveValuesAsync(new ArrayOf<NodeId>(oversizedSelection)).ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                    await client.DiscoverAsync(0).ConfigureAwait(false));
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await client.DiscoverAsync(cancellationToken: cancellation.Token).ConfigureAwait(false));
                using var pendingCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                Task<MachineToolDataObservation> pending = client.ObserveDataAsync(machine.NodeId,
                    new MachineToolObservationOptions { Window = TimeSpan.FromMinutes(1) },
                    pendingCancellation.Token).AsTask();
                try
                {
                    while (subscriptions.Count == before)
                    {
                        await Task.Delay(20, pendingCancellation.Token).ConfigureAwait(false);
                    }
                    await client.DisposeAsync().ConfigureAwait(false);
                    Assert.That(subscriptions.Count, Is.EqualTo(before),
                        "Disposing a facade must cancel and drain its own subscription resources.");
                    Assert.CatchAsync<OperationCanceledException>(async () =>
                        await pending.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false));
                }
                finally
                {
                    pendingCancellation.Cancel();
                    Assert.CatchAsync<OperationCanceledException>(async () =>
                        await pending.ConfigureAwait(false));
                }
                Assert.That(subscriptions.Count, Is.EqualTo(before));
                Assert.That(session.Connected, Is.True, "Disposing a borrowed facade must not close its caller's session.");
                Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await client.DiscoverAsync().ConfigureAwait(false));
            }
            finally
            {
                await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
