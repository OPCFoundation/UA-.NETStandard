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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Builders;

namespace Opc.Ua.Pumps.Tests
{
    /// <summary>
    /// Covers what the node manager owes a client: pumps of the right type,
    /// in both places OPC 40223 expects them, with the mandatory nameplate.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    public sealed class PumpsNodeManagerTests
    {
        [Test]
        public async Task LoadsTheFourModelsItDeclaresAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();

            Assert.Multiple(() =>
            {
                Assert.That(
                    fixture.Server.NamespaceUris.GetIndex(Namespaces.Pumps),
                    Is.GreaterThanOrEqualTo(0),
                    "Pumps");
                Assert.That(
                    fixture.Server.NamespaceUris.GetIndex(
                        Opc.Ua.Machinery.Namespaces.Machinery),
                    Is.GreaterThanOrEqualTo(0),
                    "Machinery");
                Assert.That(
                    fixture.Server.NamespaceUris.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi),
                    Is.GreaterThanOrEqualTo(0),
                    "DI");
                Assert.That(
                    fixture.Server.NamespaceUris.GetIndex(Opc.Ua.IA.Namespaces.IA),
                    Is.GreaterThanOrEqualTo(0),
                    "IA");
            });
        }

        [Test]
        public async Task CreatesAPumpOfPumpTypeWithItsMandatoryNameplateAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();

            IPumpBuilder builder = await fixture.Manager.CreatePumpAsync(
                fixture.PumpName("Pump_1"));
            PumpState pump = builder.Pump;

            Assert.Multiple(() =>
            {
                Assert.That(
                    pump.TypeDefinitionId,
                    Is.EqualTo(PumpsModel.TypeNodeId(
                        ObjectTypes.PumpType,
                        fixture.Server.NamespaceUris)));
                Assert.That(pump.Identification, Is.Not.Null, "Identification is mandatory");
                Assert.That(fixture.Manager.Pumps.Count, Is.EqualTo(1));
                Assert.That(
                    pump.EventNotifier & EventNotifiers.SubscribeToEvents,
                    Is.Not.Zero,
                    "a pump reports supervision events");
            });
        }

        [Test]
        public async Task OrganizesThePumpIntoBothTheDeviceSetAndTheMachinesFolderAsync()
        {
            // OPC 40223 makes a pump both a DI device and a Machinery machine.
            // A client of either specification has to find it, and the two
            // look in different folders.
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();

            IPumpBuilder builder = await fixture.Manager.CreatePumpAsync(
                fixture.PumpName("Pump_1"));

            NodeState? deviceSet = fixture.Manager.FindPredefinedNode(NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet,
                Opc.Ua.Di.Namespaces.OpcUaDi,
                fixture.Server.NamespaceUris));
            NodeState? machines = fixture.Manager.FindPredefinedNode(NodeId.Create(
                Opc.Ua.Machinery.Objects.Machines,
                Opc.Ua.Machinery.Namespaces.Machinery,
                fixture.Server.NamespaceUris));

            Assert.Multiple(() =>
            {
                Assert.That(deviceSet, Is.Not.Null, "DeviceSet");
                Assert.That(machines, Is.Not.Null, "Machines folder");
                Assert.That(
                    HasChild(fixture, deviceSet!, builder.NodeId),
                    Is.True,
                    "the pump is a child of the DeviceSet");

                // The Machinery side is a plain Organizes reference rather
                // than a child: a pump belongs to the DeviceSet, and the
                // Machines folder only points at it.
                Assert.That(
                    Organizes(fixture, machines!, builder.NodeId),
                    Is.True,
                    "the Machines folder organizes the pump");
            });
        }

        [Test]
        public async Task LeavesThePumpOutOfTheMachinesFolderWhenAskedToAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync(new PumpsServerOptions
            {
                OrganizeIntoMachinesFolder = false
            });

            IPumpBuilder builder = await fixture.Manager.CreatePumpAsync(
                fixture.PumpName("Pump_1"));
            NodeState? machines = fixture.Manager.FindPredefinedNode(NodeId.Create(
                Opc.Ua.Machinery.Objects.Machines,
                Opc.Ua.Machinery.Namespaces.Machinery,
                fixture.Server.NamespaceUris));

            Assert.That(Organizes(fixture, machines!, builder.NodeId), Is.False);
        }

        [Test]
        public async Task RejectsASecondPumpOfTheSameNameAsync()
        {
            // The check is by browse name: under counter-based NodeId minting
            // a NodeId prediction always looks free, so a duplicate name would
            // slip through and leave two pumps a client cannot tell apart.
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();

            await fixture.Manager.CreatePumpAsync(fixture.PumpName("Pump_1"));

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await fixture.Manager.CreatePumpAsync(
                    fixture.PumpName("Pump_1")));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public async Task EveryPumpGetsItsOwnInstanceNodeIdsAsync()
        {
            // Two pumps that shared the type-level NodeIds of their children
            // would be one pump as far as a client is concerned.
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();

            IPumpBuilder first = await fixture.Manager.CreatePumpAsync(
                fixture.PumpName("Pump_1"));
            IPumpBuilder second = await fixture.Manager.CreatePumpAsync(
                fixture.PumpName("Pump_2"));

            Assert.Multiple(() =>
            {
                Assert.That(first.NodeId, Is.Not.EqualTo(second.NodeId));
                Assert.That(
                    first.Pump.Identification!.NodeId,
                    Is.Not.EqualTo(second.Pump.Identification!.NodeId));
            });
        }

        [Test]
        public async Task FindsAPumpItAlreadyMaterialisedAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();

            IPumpBuilder created = await fixture.Manager.CreatePumpAsync(
                fixture.PumpName("Pump_1"));

            IPumpBuilder? found = fixture.Manager.Pump(created.NodeId);

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.Not.Null);
                Assert.That(found!.NodeId, Is.EqualTo(created.NodeId));
                Assert.That(fixture.Manager.Pump(new NodeId(999999, 99)), Is.Null);
            });
        }

        private static bool Organizes(
            PumpsServerFixture fixture,
            NodeState folder,
            NodeId target)
        {
            var references = new System.Collections.Generic.List<IReference>();
            folder.GetReferences(fixture.Manager.SystemContext, references);
            foreach (IReference reference in references)
            {
                if (!reference.IsInverse &&
                    reference.ReferenceTypeId == Opc.Ua.Types.ReferenceTypeIds.Organizes &&
                    reference.TargetId == target)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasChild(PumpsServerFixture fixture, NodeState parent, NodeId child)
        {
            var children = new System.Collections.Generic.List<BaseInstanceState>();
            parent.GetChildren(fixture.Manager.SystemContext, children);
            foreach (BaseInstanceState candidate in children)
            {
                if (candidate.NodeId == child)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
