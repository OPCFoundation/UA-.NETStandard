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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Builders;

namespace Opc.Ua.Pumps.Tests
{
    /// <summary>
    /// Closes the cold guard and error branches the happy-path suites leave
    /// behind: the namespace resolver's failure, the builders' argument
    /// validation, the group builder's type mismatches, the node manager's
    /// duplicate/empty-name guards and explicit-parent overload, and the pure
    /// <see cref="PumpsModel"/> classification edges. All exercise the real
    /// runtime paths rather than reflection.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    public sealed class PumpsServerCoverageTests
    {
        [Test]
        public void PumpsModelRejectsNullNamespaceTables()
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(
                    () => PumpsModel.TypeNodeId(ObjectTypes.PumpType, null!));
                Assert.Throws<ArgumentNullException>(
                    () => PumpsModel.ClassifyPort(NodeId.Null, null!));
            });
        }

        [Test]
        public void ClassifyPortFallsThroughEveryUnknownBranch()
        {
            var withPumps = new NamespaceTable();
            withPumps.Append(Namespaces.Pumps);
            int pumpsIndex = withPumps.GetIndex(Namespaces.Pumps);

            // A table without the Pumps namespace cannot classify anything.
            var withoutPumps = new NamespaceTable();

            Assert.Multiple(() =>
            {
                // Pumps namespace absent -> index < 0.
                Assert.That(
                    PumpsModel.ClassifyPort(
                        new NodeId(ObjectTypes.DrivePortType, 1),
                        withoutPumps),
                    Is.EqualTo(PumpPortKind.Unknown));

                // A null type definition is not a port.
                Assert.That(
                    PumpsModel.ClassifyPort(NodeId.Null, withPumps),
                    Is.EqualTo(PumpPortKind.Unknown));

                // Right identifier but wrong namespace index.
                Assert.That(
                    PumpsModel.ClassifyPort(
                        new NodeId(ObjectTypes.DrivePortType, 0),
                        withPumps),
                    Is.EqualTo(PumpPortKind.Unknown));

                // A string identifier in the Pumps namespace has no numeric
                // value to switch on.
                Assert.That(
                    PumpsModel.ClassifyPort(
                        new NodeId("DrivePort", (ushort)pumpsIndex),
                        withPumps),
                    Is.EqualTo(PumpPortKind.Unknown));

                // A numeric identifier in the Pumps namespace that is not one
                // of the three port types.
                Assert.That(
                    PumpsModel.ClassifyPort(
                        new NodeId(ObjectTypes.PumpType, (ushort)pumpsIndex),
                        withPumps),
                    Is.EqualTo(PumpPortKind.Unknown));
            });
        }

        [Test]
        public void ResolveThrowsWhenAModelNamespaceIsMissing()
        {
            // A table with only the base OPC UA namespace is what a manager
            // that failed to load its models would present.
            var table = new NamespaceTable();

            ServiceResultException error = Assert.Throws<ServiceResultException>(
                () => PumpNamespaceIndices.Resolve(table))!;
            Assert.That(
                error.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
        }

        [Test]
        public void SearchOrderPutsPumpsFirstAndTheBaseNamespaceLast()
        {
            var indices = new PumpNamespaceIndices(5, 6, 7);
            Assert.That(indices.SearchOrder.ToArray(), Is.EqualTo(new ushort[] { 5, 6, 7, 0 }));
        }

        [Test]
        public async Task NodeManagerRejectsNullOptionsAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();

            Assert.Throws<ArgumentNullException>(
                () => new PumpsNodeManager(
                    fixture.Server,
                    fixture.Configuration,
                    (PumpsServerOptions)null!));
        }

        [Test]
        public async Task CreatePumpRejectsAnEmptyOrNullBrowseNameAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();

            Assert.Multiple(() =>
            {
                Assert.ThrowsAsync<ArgumentException>(
                    async () => await fixture.Manager.CreatePumpAsync(QualifiedName.Null));
                Assert.ThrowsAsync<ArgumentException>(
                    async () => await fixture.Manager.CreatePumpAsync(
                        new QualifiedName(string.Empty, fixture.Manager.InstanceNamespaceIndex)));
            });
        }

        [Test]
        public async Task PumpByStateRejectsNullAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();

            Assert.Throws<ArgumentNullException>(
                () => fixture.Manager.Pump((PumpState)null!));
        }

        [Test]
        public async Task CreatePumpBelowAnExplicitParentRegistersItThereAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();

            // The DeviceSet is a legitimate explicit parent; passing it drives
            // the parent-supplied branch rather than the ResolveDeviceSet
            // fallback, and the pump still materialises and registers.
            NodeState deviceSet = fixture.Manager.FindPredefinedNode(NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet,
                Opc.Ua.Di.Namespaces.OpcUaDi,
                fixture.Server.NamespaceUris))!;

            IPumpBuilder builder = await fixture.Manager.CreatePumpAsync(
                fixture.PumpName("Pump_Parented"),
                deviceSet);

            Assert.Multiple(() =>
            {
                Assert.That(builder, Is.Not.Null);
                Assert.That(fixture.Manager.Pump(builder.NodeId), Is.Not.Null);
                Assert.That(fixture.Manager.Pumps.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task GroupBuilderValidatesArgumentsAndChildKindsAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();
            IPumpBuilder pump = await fixture.Manager.CreatePumpAsync(
                fixture.PumpName("Pump_1"));

            IPumpGroupBuilder operational = pump.Operational;
            IPumpGroupBuilder measurements = pump.Measurements;

            Assert.Multiple(() =>
            {
                // Add validates its params array and accepts real children.
                Assert.Throws<ArgumentNullException>(() => measurements.Add(null!));
                Assert.That(
                    measurements.Add(BrowseNames.MassFlow, BrowseNames.BearingTemperature),
                    Is.SameAs(measurements));

                // With runs the delegate against a real child and rejects null.
                bool ran = false;
                Assert.That(
                    measurements.With(BrowseNames.MassFlow, _ => ran = true),
                    Is.SameAs(measurements));
                Assert.That(ran, Is.True);
                Assert.Throws<ArgumentNullException>(
                    () => measurements.With(BrowseNames.MassFlow, null!));

                // Child requires a non-empty browse name.
                Assert.Throws<ArgumentException>(() => measurements.Child(string.Empty));

                // Measurements is an object child of Operational, so it is not
                // a variable to Set/SetAnalog and carries no discrete value.
                Assert.Throws<ArgumentException>(
                    () => operational.Set(BrowseNames.Measurements, Variant.From(1.0)));
                Assert.Throws<ArgumentException>(
                    () => operational.SetAnalog(BrowseNames.Measurements, 1.0));
                Assert.Throws<ArgumentException>(
                    () => operational.SetDiscrete(BrowseNames.Measurements, true));

                // MassFlow is a variable, so it is not a group to nest into.
                Assert.Throws<ArgumentException>(
                    () => measurements.Nested(BrowseNames.MassFlow));

                // A vibration measurement needs a name.
                Assert.Throws<ArgumentException>(() => measurements.AddVibration(string.Empty));
            });
        }

        [Test]
        public async Task PumpBuilderValidatesWithAndPortArgumentsAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();
            IPumpBuilder pump = await fixture.Manager.CreatePumpAsync(
                fixture.PumpName("Pump_1"));

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(
                    () => pump.With((Action<PumpState>)null!));

                bool configured = false;
                Assert.That(
                    pump.With(_ => configured = true),
                    Is.SameAs(pump));
                Assert.That(configured, Is.True);

                Assert.Throws<ArgumentNullException>(() => pump.WithNameplate(null!));

                Assert.Throws<ArgumentException>(
                    () => pump.AddPort(PumpPortKind.Drive, string.Empty));
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => pump.AddPort((PumpPortKind)999, "Bad"));

                // The outlet-connection arm of the port switch is not touched
                // by the happy-path suites.
                Assert.That(
                    pump.AddPort(PumpPortKind.OutletConnection, "Discharge").Node,
                    Is.InstanceOf<OutletConnectionPortState>());
            });
        }

        [Test]
        public async Task AFullyPopulatedNameplateFillsEveryFieldFamilyAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();
            IPumpBuilder pump = await fixture.Manager.CreatePumpAsync(
                fixture.PumpName("Pump_1"));

            // Every optional field is present, so each SetText / SetLocalized /
            // SetValue "value present" branch materialises its node.
            pump.WithNameplate(new PumpNameplate
            {
                NodeId = NodeId.Null,
                Manufacturer = new LocalizedText("Acme Pumps"),
                SerialNumber = "SN-001",
                ManufacturerUri = "urn:acme:pumps",
                Model = new LocalizedText("PumpX-2000"),
                ProductCode = "PX-2000",
                HardwareRevision = "HW-1",
                SoftwareRevision = "SW-2",
                DeviceClass = "Pump",
                ProductInstanceUri = "urn:acme:pumps:instance:1",
                AssetId = "ASSET-1",
                ComponentName = new LocalizedText("Main Pump"),
                Location = "Plant 1 / Utility Skid",
                InitialOperationDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                YearOfConstruction = 2025,
                MonthOfConstruction = 4,
                DayOfConstruction = 15,
                ArticleNumber = "ART-4711",
                OrderProductCode = "ORD-1",
                TypeOfProduct = "Centrifugal",
                Supplier = "Acme Distribution",
                CountryOfOrigin = "DE",
                FabricationNumber = "FAB-9",
                GTINCode = "01234567890128",
                NationalStockNumber = "NSN-1",
                PhysicalAddress = new PhysicalAddressDataType()
            });

            PumpIdentificationState identification = pump.Pump.Identification!;

            Assert.Multiple(() =>
            {
                Assert.That(identification.ArticleNumber, Is.Not.Null);
                Assert.That(identification.ArticleNumber!.Value, Is.EqualTo("ART-4711"));
                Assert.That(identification.OrderProductCode!.Value, Is.EqualTo("ORD-1"));
                Assert.That(identification.TypeOfProduct!.Value, Is.EqualTo("Centrifugal"));
                Assert.That(identification.Supplier!.Value, Is.EqualTo("Acme Distribution"));
                Assert.That(identification.CountryOfOrigin!.Value, Is.EqualTo("DE"));
                Assert.That(identification.FabricationNumber!.Value, Is.EqualTo("FAB-9"));
                Assert.That(identification.GTINCode!.Value, Is.EqualTo("01234567890128"));
                Assert.That(identification.NationalStockNumber!.Value, Is.EqualTo("NSN-1"));
                Assert.That(identification.DayOfConstruction!.Value, Is.EqualTo(15));
            });
        }
    }
}
