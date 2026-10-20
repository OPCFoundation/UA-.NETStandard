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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.Tests;

namespace Opc.Ua.Machinery.Tests.Integration
{
    /// <summary>
    /// Exercises strict reads against the generated models in a real in-process address space.
    /// </summary>
    [TestFixture]
    [Category("Machinery")]
    public sealed class MachineryStrictReadIntegrationTests
    {
        /// <summary>
        /// Model-backed identification, counters, state, process, energy and jobs remain readable.
        /// </summary>
        [Test]
        public async Task StrictReadsMatchTheMachineryAddressSpace()
        {
            var startTime = new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);
            var fixture = new MachineryServerFixture();
            await using ConfiguredAsyncDisposable _ = fixture.ConfigureAwait(false);
            await fixture.StartAsync().ConfigureAwait(false);
            using var jobs = new InMemoryIsa95JobControlProvider();
            IMachineHandle<BaseObjectState> machine = await fixture.CreateBuildContext()
                .AddMachine(new QualifiedName("Strict-Machine"))
                .WithIdentification(id =>
                {
                    id.Manufacturer = new LocalizedText("Acme");
                    id.SerialNumber = "STRICT-1";
                    id.ProductInstanceUri = "urn:acme:strict:1";
                })
                .WithMonitoring(monitoring => monitoring
                    .WithMachineryItemState(MachineryItemStateValue.NotExecuting)
                    .WithOperationMode(MachineryOperationModeValue.Setup))
                .WithOperationCounters(counters => counters.WithPowerOnDuration(12.0))
                .WithLifetimeCounters(counters => counters.AddLifetimeVariable(new QualifiedName("Seal"), 100, 50))
                .WithMachineryEquipment(equipment => equipment.AddEquipment(new QualifiedName("Die"), "urn:acme:die"))
                .WithProcessValue(new QualifiedName("Pressure"), value => value
                    .WithValue(5.0)
                    .WithSetpoint(6.0)
                    .WithStatus())
                .WithEnergy(energy => energy.AddResource(
                    MachineryEnergyCarrier.CompressedAir,
                    air => air.Main.WithApplicationTag("air").WithStartTime(startTime)
                        .WithNonElectricalEnergy(12.0, 0)))
                .WithJobManagement(builder => builder.WithJobOrderReceiver(jobs)
                    .WithJobResponseProvider(jobs)
                    .WithJobOrderCatalog(jobs))
                .BuildAsync().ConfigureAwait(false);

            Mock<ISession> session = MachineryInProcessSessionBridge.Build(fixture);
            var client = new MachineryClient(
                session.Object, NUnitTelemetryContext.Create(), new MachineryClientOptions());
            MachineIdentification? identification = await client.ReadIdentificationAsync(machine.NodeId)
                .ConfigureAwait(false);
            Assert.That(identification!.SerialNumber, Is.EqualTo("STRICT-1"));
            Assert.That(identification.Values.Count, Is.EqualTo(16));
            MachineryOperationCounters? counters = await client.ReadOperationCountersAsync(machine.NodeId)
                .ConfigureAwait(false);
            Assert.That(counters!.PowerOnDuration, Is.EqualTo(12.0));
            Assert.That(counters.Values.Count, Is.EqualTo(3));
            MachineryStateSnapshot? state = await client.ReadItemStateAsync(machine.NodeId).ConfigureAwait(false);
            MachineryStateSnapshot? mode = await client.ReadOperationModeAsync(machine.NodeId).ConfigureAwait(false);
            Assert.That(state!.State.CurrentState.Text, Is.EqualTo("NotExecuting"));
            Assert.That(mode!.State.CurrentState.Text, Is.EqualTo("Setup"));
            Assert.That(state.Values[0].SourceNodeId.IsNull, Is.False);
            Assert.That(mode.Values[0].SourceNodeId.IsNull, Is.False);

            var lifetime = new List<MachineryLifetimeVariable>();
            await foreach (MachineryLifetimeVariable value in client.ReadLifetimeCountersAsync(machine.NodeId))
            {
                lifetime.Add(value);
            }
            Assert.That(lifetime, Has.Count.EqualTo(1));
            Assert.That(lifetime[0].Values.Count, Is.EqualTo(4));
            var equipment = new List<MachineryEquipmentItem>();
            await foreach (MachineryEquipmentItem item in client.EnumerateEquipmentAsync(machine.NodeId))
            {
                equipment.Add(item);
            }
            Assert.That(equipment, Has.Count.EqualTo(1));
            Assert.That(equipment[0].MachineryEquipmentTypeId, Is.EqualTo("urn:acme:die"));
            Assert.That(equipment[0].Values.Count, Is.EqualTo(4));

            var processes = new List<MachineryProcessValue>();
            await foreach (MachineEntry entry in client.EnumerateProcessValuesAsync(machine.NodeId))
            {
                processes.Add((await client.ReadProcessValueAsync(entry.NodeId).ConfigureAwait(false))!);
            }
            Assert.That(processes, Has.Count.EqualTo(1));
            Assert.That(processes[0].Value, Is.EqualTo(5.0));
            Assert.That(processes[0].Setpoint, Is.EqualTo(6.0));
            Assert.That(processes[0].Values.Count, Is.EqualTo(11));

            var meters = new List<MachineryMeteringPoint>();
            await foreach (MachineEntry resource in client.EnumerateEnergyResourcesAsync(machine.NodeId))
            {
                meters.Add((await client.ReadMainMeteringPointAsync(resource.NodeId).ConfigureAwait(false))!);
            }
            Assert.That(meters, Has.Count.EqualTo(1));
            Assert.That(meters[0].ApplicationTag, Is.EqualTo("air"));
            Assert.That(meters[0].Measurements.Count, Is.GreaterThan(0));
            Assert.That(meters[0].Values.Count, Is.EqualTo(meters[0].Measurements.Count + 2));
            Assert.That(meters[0].Values.ToArray(), Has.Some.Matches<MachineryReadValue>(field =>
                field is not null && field.BrowsePath[^1].Name == IA.BrowseNames.StartTime &&
                field.Value.WrappedValue == Variant.From(startTime)));
            MachineryJobOrdersSnapshot jobSnapshot = await client.ReadJobOrdersSnapshotAsync(machine.NodeId)
                .ConfigureAwait(false);
            Assert.That(jobSnapshot.Values[0].IsAbsent, Is.False);
            Assert.That(jobSnapshot.JobOrders.Count, Is.Zero);
            session.Verify(s => s.Dispose(), Times.Never);
        }
    }
}
