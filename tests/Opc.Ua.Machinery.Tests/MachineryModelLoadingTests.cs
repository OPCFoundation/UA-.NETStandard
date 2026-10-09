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
using Opc.Ua.Machinery.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Pins what a Machinery node manager actually loads and publishes.
    /// </summary>
    [TestFixture]
    [Category("Machinery")]
    public sealed class MachineryModelLoadingTests
    {
        [Test]
        public void TypeSystemLoadsEveryPart()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(true);
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.Append(Opc.Ua.Di.Namespaces.OpcUaDi);
            ArrayOf<string> modelNamespaces =
                MachineryServer.GetNamespaceUris(MachineryParts.All);
            for (int ii = 0; ii < modelNamespaces.Count; ii++)
            {
                messageContext.NamespaceUris.Append(modelNamespaces[ii]);
            }
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };
            var nodes = new NodeStateCollection();
            int added = nodes.AddMachineryTypeSystem(context, MachineryParts.All);

            Assert.That(added, Is.GreaterThan(0));
            Assert.That(nodes, Has.Count.GreaterThan(added), "DI must be loaded as well.");
        }

        [Test]
        public void ResultPartLoadsWithoutDeviceIntegration()
        {
            Assert.That(
                MachineryServer.RequiresDeviceIntegration(MachineryParts.Result),
                Is.False,
                "OPC 40001-101 needs UA core only.");
            Assert.That(
                MachineryServer.RequiresDeviceIntegration(MachineryParts.Jobs),
                Is.False,
                "OPC 40001-3 needs ISA-95 Job Control V2 only.");
            Assert.That(
                MachineryServer.RequiresDeviceIntegration(MachineryParts.BuildingBlocks),
                Is.True);
        }

        [Test]
        public void NamespacesFollowTheSelectedParts()
        {
            ArrayOf<string> buildingBlocks =
                MachineryServer.GetNamespaceUris(MachineryParts.BuildingBlocks);
            Assert.That(buildingBlocks.ToArray(), Contains.Item(Namespaces.Machinery));
            Assert.That(buildingBlocks.ToArray(), Contains.Item(Opc.Ua.IA.Namespaces.IA));

            ArrayOf<string> result = MachineryServer.GetNamespaceUris(MachineryParts.Result);
            Assert.That(
                result.ToArray(),
                Is.EqualTo(
                    new[] { Opc.Ua.Machinery.Result.Namespaces.MachineryResult }));
        }

        [Test]
        public void ProcessValuesWithoutBuildingBlocksIsRejected()
        {
            var options = new MachineryServerOptions { Parts = MachineryParts.ProcessValues };
            ServiceResultException exception =
                Assert.Throws<ServiceResultException>(options.Validate)!;
            Assert.That(exception.Message, Does.Contain("BuildingBlocks"));
        }

        [Test]
        public void InstanceNamespaceMustNotBeAModelNamespace()
        {
            var options = new MachineryServerOptions
            {
                InstanceNamespaceUri = Namespaces.Machinery
            };
            Assert.Throws<ServiceResultException>(options.Validate);
        }

        [Test]
        public async Task ManagerPublishesTheMachinesFolder()
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();

            var machinesId = NodeId.Create(
                Objects.Machines,
                Namespaces.Machinery,
                fixture.Manager.Server.NamespaceUris);
            NodeState? machines = fixture.Manager.FindPredefinedNode(machinesId);

            Assert.That(machines, Is.Not.Null);
            Assert.That(machines!.BrowseName.Name, Is.EqualTo(BrowseNames.Machines));
        }

        [Test]
        public async Task ManagerAdvertisesFindMachinesOnce()
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();

            QualifiedName[] units = [.. fixture.Manager.ConformanceUnits];
            Assert.That(units, Contains.Item(new QualifiedName("Machinery Find Machines")));
            Assert.That(
                units,
                Has.No.Member(new QualifiedName("Machinery Machine Identification")),
                "A unit must not be advertised before its structure exists.");
        }
    }
}
