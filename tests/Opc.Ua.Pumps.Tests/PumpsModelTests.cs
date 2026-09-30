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
using NUnit.Framework;

namespace Opc.Ua.Pumps.Tests
{
    /// <summary>
    /// Guards the generated OPC 40223 model against drift from the OPC
    /// Foundation publication it is built from.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    public sealed class PumpsModelTests
    {
        [Test]
        public void NamespaceMatchesTheSpecification()
        {
            Assert.That(Namespaces.Pumps, Is.EqualTo("http://opcfoundation.org/UA/Pumps/"));
            Assert.That(
                Namespaces.Machinery,
                Is.EqualTo("http://opcfoundation.org/UA/Machinery/"));
            Assert.That(Namespaces.OpcUaDi, Is.EqualTo("http://opcfoundation.org/UA/DI/"));
        }

        [Test]
        public void PumpTypeAndItsSevenGroupsAreGenerated()
        {
            var pump = new PumpState(null);

            // The four groups OPC 40223 takes from OPC 10000-100 §5.6 and the
            // three it declares itself. All are optional bar Identification,
            // so they start null and are materialised on demand.
            Assert.Multiple(() =>
            {
                Assert.That(pump.Identification, Is.Null, "Identification");
                Assert.That(pump.Configuration, Is.Null, "Configuration");
                Assert.That(pump.Operational, Is.Null, "Operational");
                Assert.That(pump.Maintenance, Is.Null, "Maintenance");
                Assert.That(pump.Events, Is.Null, "Events");
                Assert.That(pump.Ports, Is.Null, "Ports");
                Assert.That(pump.Documentation, Is.Null, "Documentation");
            });
        }

        [Test]
        public void EveryObjectTypeOfTheSpecificationIsGenerated()
        {
            // Spot-checks across the whole type surface rather than all 51:
            // one per structural area, so a model that loses an area fails.
            Assert.Multiple(() =>
            {
                Assert.That(ObjectTypes.PumpType, Is.EqualTo(1052u));
                Assert.That(ObjectTypes.PumpIdentificationType, Is.EqualTo(1005u));
                Assert.That(ObjectTypes.DesignType, Is.EqualTo(1020u));
                Assert.That(ObjectTypes.ImplementationType, Is.EqualTo(1023u));
                Assert.That(ObjectTypes.SystemRequirementsType, Is.EqualTo(1022u));
                Assert.That(ObjectTypes.MeasurementsType, Is.EqualTo(1054u));
                Assert.That(ObjectTypes.SignalsType, Is.EqualTo(1033u));
                Assert.That(ObjectTypes.ControlType, Is.EqualTo(1021u));
                Assert.That(ObjectTypes.PumpActuationType, Is.EqualTo(1028u));
                Assert.That(ObjectTypes.MultiPumpType, Is.EqualTo(1039u));
                Assert.That(ObjectTypes.SupervisionType, Is.EqualTo(1019u));
                Assert.That(ObjectTypes.MaintenanceGroupType, Is.EqualTo(1011u));
                Assert.That(ObjectTypes.PortsGroupType, Is.EqualTo(1034u));
                Assert.That(ObjectTypes.DrivePortType, Is.EqualTo(1038u));
                Assert.That(ObjectTypes.InletConnectionPortType, Is.EqualTo(1036u));
                Assert.That(ObjectTypes.OutletConnectionPortType, Is.EqualTo(1037u));
            });
        }

        [Test]
        public void EveryEnumerationOfTheSpecificationIsGenerated()
        {
            Assert.Multiple(() =>
            {
                Assert.That(IsDefined(ControlModeEnum.SpeedControl), Is.True);
                Assert.That(ValuesOf<OperationModeEnum>(), Is.Not.Empty);
                Assert.That(ValuesOf<PumpRoleEnum>(), Is.Not.Empty);
                Assert.That(ValuesOf<PumpKickModeEnum>(), Is.Not.Empty);
                Assert.That(ValuesOf<MultiPumpOperationModeEnum>(), Is.Not.Empty);
                Assert.That(ValuesOf<DistributionTypeEnum>(), Is.Not.Empty);
                Assert.That(ValuesOf<ExchangeModeEnum>(), Is.Not.Empty);
                Assert.That(ValuesOf<FieldbusEnum>(), Is.Not.Empty);
                Assert.That(ValuesOf<MaintenanceLevelEnum>(), Is.Not.Empty);
                Assert.That(ValuesOf<StateOfTheItemEnum>(), Is.Not.Empty);
                Assert.That(ValuesOf<PortDirectionEnum>(), Is.Not.Empty);
            });
        }

        [Test]
        public void OptionSetsAndStructuresAreGenerated()
        {
            Assert.Multiple(() =>
            {
                Assert.That(new ExplosionZoneOptionSet(), Is.Not.Null);
                Assert.That(new ExplosionProtectionOptionSet(), Is.Not.Null);
                Assert.That(new OfferedControlModesOptionSet(), Is.Not.Null);
                Assert.That(new OfferedFieldbusesOptionSet(), Is.Not.Null);
                Assert.That(new DeclarationOfConformityOptionSet(), Is.Not.Null);
                Assert.That(new PhysicalAddressDataType(), Is.Not.Null);
            });
        }

        [Test]
        public void TheFourDeviceIntegrationGroupNamesAreNotPumpsBrowseNames()
        {
            // Identification, Configuration, Maintenance and Operational are
            // OPC 10000-100 §5.6 instance names in the DI namespace, so the
            // Pumps generator never emits constants for them - which is the
            // whole reason PumpsModel.DiGroups exists. If a future model
            // revision did emit them, this test fails and the duplicate can
            // be removed.
            Assert.Multiple(() =>
            {
                Assert.That(PumpsModel.DiGroups.Identification, Is.EqualTo("Identification"));
                Assert.That(PumpsModel.DiGroups.Configuration, Is.EqualTo("Configuration"));
                Assert.That(PumpsModel.DiGroups.Maintenance, Is.EqualTo("Maintenance"));
                Assert.That(PumpsModel.DiGroups.Operational, Is.EqualTo("Operational"));

                // These three are Pumps nodes and do have generated constants.
                Assert.That(BrowseNames.Events, Is.EqualTo("Events"));
                Assert.That(BrowseNames.Ports, Is.EqualTo("Ports"));
                Assert.That(BrowseNames.Documentation, Is.EqualTo("Documentation"));
            });
        }

        [Test]
        public void ClassifyPortRecognisesTheThreePortTypes()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append(Namespaces.Pumps);

            Assert.Multiple(() =>
            {
                Assert.That(
                    PumpsModel.ClassifyPort(
                        PumpsModel.TypeNodeId(ObjectTypes.DrivePortType, namespaceUris),
                        namespaceUris),
                    Is.EqualTo(PumpPortKind.Drive));
                Assert.That(
                    PumpsModel.ClassifyPort(
                        PumpsModel.TypeNodeId(
                            ObjectTypes.InletConnectionPortType,
                            namespaceUris),
                        namespaceUris),
                    Is.EqualTo(PumpPortKind.InletConnection));
                Assert.That(
                    PumpsModel.ClassifyPort(
                        PumpsModel.TypeNodeId(
                            ObjectTypes.OutletConnectionPortType,
                            namespaceUris),
                        namespaceUris),
                    Is.EqualTo(PumpPortKind.OutletConnection));

                // A vendor subtype, an unrelated type and a null all fall
                // through to Unknown rather than throwing.
                Assert.That(
                    PumpsModel.ClassifyPort(
                        PumpsModel.TypeNodeId(ObjectTypes.PumpType, namespaceUris),
                        namespaceUris),
                    Is.EqualTo(PumpPortKind.Unknown));
                Assert.That(
                    PumpsModel.ClassifyPort(NodeId.Null, namespaceUris),
                    Is.EqualTo(PumpPortKind.Unknown));
            });
        }

        [Test]
        public void TypeNodeIdReturnsNullWhenThePumpsNamespaceIsAbsent()
        {
            var namespaceUris = new NamespaceTable();
            Assert.That(
                PumpsModel.TypeNodeId(ObjectTypes.PumpType, namespaceUris),
                Is.EqualTo(NodeId.Null));
        }

        private static TEnum[] ValuesOf<TEnum>() where TEnum : struct, Enum
        {
#if NET5_0_OR_GREATER
            return Enum.GetValues<TEnum>();
#else
            return (TEnum[])Enum.GetValues(typeof(TEnum));
#endif
        }

        private static bool IsDefined<TEnum>(TEnum value) where TEnum : struct, Enum
        {
#if NET5_0_OR_GREATER
            return Enum.IsDefined(value);
#else
            return Enum.IsDefined(typeof(TEnum), value);
#endif
        }
    }
}
