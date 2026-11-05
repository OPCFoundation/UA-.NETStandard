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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.AMB.Server.Configuration;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Pumps.Tests
{
    /// <summary>
    /// Verifies live native-unit publication without inferring complete profiles from optional features.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    [NonParallelizable]
    public sealed class PumpsConformanceTests
    {
        /// <summary>
        /// Starts an independent runtime so all-instance rules have a known population.
        /// </summary>
        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new PumpsServerFixture();
            await m_fixture.StartAsync();
        }

        /// <summary>
        /// Releases the owner and every installed feature binding.
        /// </summary>
        [TearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync();
        }

        /// <summary>
        /// Loaded types alone do not enable units; mandatory instances do not create optional groups.
        /// </summary>
        [Test]
        public async Task UnitsRequireInstancesAndDoNotMaterializeOptionalMembersAsync()
        {
            Assert.That(new PumpsConformance(m_fixture.Manager.Runtime).ConformanceUnits.Count, Is.Zero);
            IPumpBuilder pump = await CreateAsync("Minimal");
            Assert.That(Has("Base System"), Is.True);
            Assert.That(Has("Pump PumpType Mandatory Nodes"), Is.True);
            Assert.That(Has("Pump Identification"), Is.True);
            Assert.That(Has("PumpClass Data"), Is.False);
            Assert.That(Has("Historizing"), Is.False);
            Assert.That(Has("Limit Alarm Status"), Is.False);
            Assert.That(Has("Supervision Health Status"), Is.False);
            Assert.That(pump.Pump.Configuration, Is.Null);
            Assert.That(pump.Pump.Operational, Is.Null);
            Assert.That(pump.Pump.Events, Is.Null);
            Assert.That(new PumpsConformance(m_fixture.Manager.Runtime).ServerProfiles.Count, Is.Zero);
        }

        /// <summary>
        /// Identification requires readable fields and registered mandatory nodes, not just construction history.
        /// </summary>
        [Test]
        public async Task IdentificationRequiresReadAccessAndMandatoryNodesAsync()
        {
            IPumpBuilder pump = await CreateAsync("Incomplete");
            Assert.That(Has("Pump Identification"), Is.True);
            var manufacturer = (BaseVariableState)pump.Identification.Child(Di.BrowseNames.Manufacturer)!;
            byte originalAccess = manufacturer.AccessLevel;
            manufacturer.AccessLevel = AccessLevels.None;
            Assert.That(Has("Pump Identification"), Is.False);
            Assert.That(Has("Pump PumpType Mandatory Nodes"), Is.True);
            manufacturer.AccessLevel = originalAccess;
            Assert.That(Has("Pump Identification"), Is.True);
            await m_fixture.Manager.DeleteNodeAsync(
                m_fixture.Manager.SystemContext, pump.Pump.Identification!.NodeId, CancellationToken.None);
            Assert.That(Has("Pump Identification"), Is.False);
            Assert.That(Has("Pump PumpType Mandatory Nodes"), Is.False);
            Assert.That(Has("Base System"), Is.True);
        }

        /// <summary>
        /// A matching variable type does not excuse an incompatible field data type or value rank.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task IdentificationRequiresCompatibleVariableAttributesAsync(bool changeDataType)
        {
            IPumpBuilder pump = await CreateAsync("InvalidAttributes");
            var manufacturer = (BaseVariableState)pump.Identification.Child(Di.BrowseNames.Manufacturer)!;
            NodeId originalDataType = manufacturer.DataType;
            int originalValueRank = manufacturer.ValueRank;
            Assert.That(Has("Pump Identification"), Is.True);
            if (changeDataType)
            {
                manufacturer.DataType = Opc.Ua.DataTypeIds.Boolean;
            }
            else
            {
                manufacturer.ValueRank = ValueRanks.OneDimension;
            }
            Assert.That(Has("Pump Identification"), Is.False);
            Assert.That(Has("Pump PumpType Mandatory Nodes"), Is.False);
            manufacturer.DataType = originalDataType;
            manufacturer.ValueRank = originalValueRank;
            Assert.That(Has("Pump Identification"), Is.True);
            Assert.That(Has("Pump PumpType Mandatory Nodes"), Is.True);
        }

        /// <summary>
        /// PumpClass coverage is universal, includes the last legal value, and retracts for invalid data.
        /// </summary>
        [Test]
        public async Task PumpClassRequiresEveryCurrentPumpAndLegalValuesAsync()
        {
            IPumpBuilder first = await CreateAsync("ClassFirst");
            first.Design.Set(BrowseNames.PumpClass, Variant.From(DeviceTypeEnum.Pump));
            Assert.That(Has("PumpClass Data"), Is.True);
            IPumpBuilder second = await CreateAsync("ClassSecond");
            Assert.That(Has("PumpClass Data"), Is.False);
            second.Design.Set(BrowseNames.PumpClass, Variant.From(DeviceTypeEnum.RotodynamicPump));
            Assert.That(Has("PumpClass Data"), Is.True);
            var value = (BaseVariableState)second.Design.Child(BrowseNames.PumpClass)!;
            value.WrappedValue = Variant.From((DeviceTypeEnum)7);
            Assert.That(Has("PumpClass Data"), Is.False);
        }

        /// <summary>
        /// Port and control units follow registered instances, without granting physical execution.
        /// </summary>
        [Test]
        public async Task OptionalStructureUnitsFollowLiveNodesAsync()
        {
            IPumpBuilder pump = await CreateAsync("Structure");
            Assert.That(Has("Pump Connection Port"), Is.False);
            Assert.That(Has("Pump Drive Port"), Is.False);
            Assert.That(Has("Pump Control"), Is.False);
            IPumpGroupBuilder inlet = pump.AddPort(PumpPortKind.InletConnection, "Inlet");
            pump.AddPort(PumpPortKind.Drive, "Drive");
            _ = pump.Control;
            Assert.That(Has("Pump Connection Port"), Is.True);
            Assert.That(Has("Pump Drive Port"), Is.True);
            Assert.That(Has("Pump Control"), Is.True);
            await m_fixture.Manager.DeleteNodeAsync(
                m_fixture.Manager.SystemContext, inlet.Node.NodeId, CancellationToken.None);
            Assert.That(Has("Pump Connection Port"), Is.False);
        }

        /// <summary>
        /// Every instantiated supervision signal needs both a declaration and a registered bound condition.
        /// </summary>
        [Test]
        public async Task HealthUnitRequiresAllInstantiatedSupervisionSignalsAsync()
        {
            IPumpBuilder pump = await CreateAsync("Health");
            IPumpGroupBuilder mechanics = pump.Supervision(BrowseNames.SupervisionMechanics)
                .SetDiscrete(BrowseNames.BearingFault, false);
            var bearing = (BaseVariableState)mechanics.Child(BrowseNames.BearingFault)!;
            Assert.That(Has("Supervision Health Status"), Is.False);
            PumpAlarmBinding alarm = await m_fixture.Manager.Runtime.AddDiagnosticAlarmAsync(
                mechanics, bearing, "Bearing", Di.DeviceHealthEnumeration.FAILURE);
            Assert.That(Has("Supervision Health Status"), Is.True);
            IPumpGroupBuilder fluid = pump.Supervision(BrowseNames.SupervisionProcessFluid)
                .SetDiscrete(BrowseNames.Cavitation, false);
            var cavitation = (BaseVariableState)fluid.Child(BrowseNames.Cavitation)!;
            Assert.That(Has("Supervision Health Status"), Is.False);
            await m_fixture.Manager.Runtime.AddDiagnosticAlarmAsync(
                fluid, cavitation, "Cavitation", Di.DeviceHealthEnumeration.OFF_SPEC);
            Assert.That(Has("Supervision Health Status"), Is.True);
            await m_fixture.Manager.DeleteNodeAsync(
                m_fixture.Manager.SystemContext, alarm.Alarm.NodeId, CancellationToken.None);
            Assert.That(Has("Supervision Health Status"), Is.False);
        }

        /// <summary>
        /// Measurement model declarations alone do not stand in for an installed exclusive condition.
        /// </summary>
        [Test]
        public async Task LimitUnitRequiresDeclarationsAndLiveConditionAsync()
        {
            IPumpBuilder pump = await CreateAsync("Limits");
            pump.Measurements.SetAnalog(BrowseNames.MassFlow, 5);
            var variable = (BaseVariableState)pump.Measurements.Child(BrowseNames.MassFlow)!;
            Assert.That(Has("Limit Alarm Status"), Is.False);
            PumpAlarmBinding binding = await m_fixture.Manager.Runtime.AddMeasurementAlarmAsync(
                pump.Measurements, variable, "FlowLimit", new PumpMeasurementLimits { High = 10 });
            Assert.That(Has("Limit Alarm Status"), Is.True);
            await m_fixture.Manager.DeleteNodeAsync(
                m_fixture.Manager.SystemContext, binding.Alarm.NodeId, CancellationToken.None);
            Assert.That(Has("Limit Alarm Status"), Is.False);
        }

        /// <summary>
        /// Access flags alone do not advertise history, and withdrawing a provider route removes the unit.
        /// </summary>
        [Test]
        public async Task HistoryUnitRequiresActiveDataProviderAndWithdrawsOnShutdownAsync()
        {
            IPumpBuilder pump = await CreateAsync("History");
            var variable = (BaseVariableState)pump.Measurements.Child(BrowseNames.MassFlow)!;
            variable.Historizing = true;
            variable.AccessLevel |= AccessLevels.HistoryRead;
            Assert.That(Has("Historizing"), Is.False);
            variable.Historizing = false;
            variable.AccessLevel &= unchecked((byte)~AccessLevels.HistoryRead);
            using var provider = new InMemoryHistorianProvider();
            provider.Register(variable.NodeId);
            await m_fixture.Manager.Runtime.HistorizeAsync(pump, [variable], provider, autoCapture: false);
            Assert.That(Has("Historizing"), Is.True);
            var registry = ((IHistorianRegistryProvider)m_fixture.Server).HistorianRegistry;
            registry.UnregisterForNode(variable.NodeId);
            Assert.That(Has("Historizing"), Is.False);
            registry.RegisterForNode(variable.NodeId, provider);
            Assert.That(Has("Historizing"), Is.True);
            await m_fixture.Manager.Runtime.DisposeAsync();
            Assert.That(new PumpsConformance(m_fixture.Manager.Runtime).ConformanceUnits.Count, Is.Zero);
            Assert.That(Has("Historizing"), Is.False);
        }

        /// <summary>
        /// Base and Advanced profiles follow live writable identity and each independent server facet.
        /// </summary>
        [Test]
        public async Task ProfilesRequireConfiguredIdentityAndInheritedFacetsAsync()
        {
            SetServerProfiles(PumpBaseServerProfilePrerequisites);
            IPumpBuilder pump = await CreateAsync("ProfileEligibility");
            pump.Identification.Set(
                Opc.Ua.Di.BrowseNames.ProductInstanceUri,
                Variant.From("urn:example:pump:profile-eligibility"));
            PumpIdentificationState identification = pump.Pump.Identification!;
            Assert.That(identification.ReferenceTypeId, Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasAddIn));

            var conformance = new PumpsConformance(m_fixture.Manager.Runtime);
            Assert.That(Advertises(conformance.ServerProfiles, PumpBaseServerProfile), Is.False);
            await m_fixture.Manager.Runtime.WithRetainedIdentificationAsync(
                pump, PersistentStore(), WritableIdentityPermissions);
            Assert.That(Advertises(conformance.ServerProfiles, PumpBaseServerProfile), Is.True);
            Assert.That(Advertises(
                conformance.ServerProfiles, MachineryMachineIdentificationWritableProfile), Is.True);
            Assert.That(Advertises(conformance.ServerProfiles, PumpAdvancedServerProfile), Is.False);
            Assert.That(Advertises(
                conformance.ServerProfiles, "http://opcfoundation.org/UA-Profile/CAS/Server/Base"), Is.False);

            IPumpBuilder secondPump = await CreateAsync("ProfileEligibilitySecond");
            secondPump.Identification.Set(
                Opc.Ua.Di.BrowseNames.ProductInstanceUri,
                Variant.From("urn:example:pump:profile-eligibility-second"));
            Assert.That(Advertises(conformance.ServerProfiles, PumpBaseServerProfile), Is.False);
            await m_fixture.Manager.Runtime.WithRetainedIdentificationAsync(
                secondPump, PersistentStore(), WritableIdentityPermissions);
            Assert.That(Advertises(conformance.ServerProfiles, PumpBaseServerProfile), Is.True);
            await m_fixture.Manager.DeleteNodeAsync(
                m_fixture.Manager.SystemContext, secondPump.NodeId, CancellationToken.None);
            Assert.That(Advertises(conformance.ServerProfiles, PumpBaseServerProfile), Is.True);

            for (int i = 0; i < PumpBaseServerProfilePrerequisites.Count; i++)
            {
                string missingProfile = PumpBaseServerProfilePrerequisites[i];
                SetServerProfiles(WithoutProfile(PumpBaseServerProfilePrerequisites, missingProfile));
                Assert.That(
                    Advertises(conformance.ServerProfiles, PumpBaseServerProfile),
                    Is.False,
                    $"Pump Base must require {missingProfile}.");
            }

            BaseVariableState location = (BaseVariableState)pump.Identification.Child(
                Opc.Ua.Machinery.BrowseNames.Location)!;
            byte originalAccessLevel = location.AccessLevel;
            location.AccessLevel &= unchecked((byte)~AccessLevels.CurrentRead);
            Assert.That(Advertises(conformance.ServerProfiles, PumpBaseServerProfile), Is.False);
            location.AccessLevel = originalAccessLevel;

            location.AccessLevel &= unchecked((byte)~AccessLevels.CurrentWrite);
            Assert.That(Advertises(conformance.ServerProfiles, PumpBaseServerProfile), Is.False);
            location.AccessLevel = originalAccessLevel;

            var originalWriteHandler = location.OnSimpleWriteValueAsync;
            location.OnSimpleWriteValueAsync = null;
            Assert.That(Advertises(conformance.ServerProfiles, PumpBaseServerProfile), Is.False);
            location.OnSimpleWriteValueAsync = originalWriteHandler;

            location.UserAccessLevel &= unchecked((byte)~AccessLevels.CurrentWrite);
            Assert.That(Advertises(conformance.ServerProfiles, PumpBaseServerProfile), Is.False);
            Assert.That(Advertises(conformance.ServerProfiles, PumpAdvancedServerProfile), Is.False);
            location.UserAccessLevel |= AccessLevels.CurrentWrite;

            ArrayOf<string> allRequiredProfiles =
                [.. PumpBaseServerProfilePrerequisites, .. PumpAdvancedServerProfilePrerequisites];
            SetServerProfiles(allRequiredProfiles);
            Assert.That(Advertises(conformance.ServerProfiles, PumpBaseServerProfile), Is.True);
            Assert.That(Advertises(conformance.ServerProfiles, PumpAdvancedServerProfile), Is.True);
            Assert.That(Has("Pump Control"), Is.False);

            for (int i = 0; i < PumpAdvancedServerProfilePrerequisites.Count; i++)
            {
                string missingProfile = PumpAdvancedServerProfilePrerequisites[i];
                SetServerProfiles(WithoutProfile(allRequiredProfiles, missingProfile));
                Assert.That(Advertises(conformance.ServerProfiles, PumpBaseServerProfile), Is.True);
                Assert.That(
                    Advertises(conformance.ServerProfiles, PumpAdvancedServerProfile),
                    Is.False,
                    $"Pump Advanced must require {missingProfile}.");
            }
        }

        /// <summary>
        /// Creates a pump without selecting optional groups or runtime services.
        /// </summary>
        private ValueTask<IPumpBuilder> CreateAsync(string name)
        {
            return m_fixture.Manager.CreatePumpAsync(m_fixture.PumpName(name));
        }

        /// <summary>
        /// Reads the manager contribution, including the composed runtime's exact namespace-qualified title.
        /// </summary>
        private bool Has(string title)
        {
            var expected = new QualifiedName(title, m_fixture.Manager.NamespaceIndices.Pumps);
            foreach (QualifiedName unit in m_fixture.Manager.ConformanceUnits)
            {
                if (unit == expected)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Replaces the server's independent profile evidence for a focused eligibility case.
        /// </summary>
        private void SetServerProfiles(ArrayOf<string> profiles)
        {
            m_fixture.Server.ServerObject.ServerCapabilities!.ServerProfileArray!.Value = Variant.From(profiles);
        }

        /// <summary>
        /// Checks one URI in a server profile contribution.
        /// </summary>
        private static bool Advertises(ArrayOf<string> profiles, string expected)
        {
            for (int i = 0; i < profiles.Count; i++)
            {
                if (string.Equals(profiles[i], expected, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Removes one URI while preserving the other profile evidence.
        /// </summary>
        private static ArrayOf<string> WithoutProfile(ArrayOf<string> profiles, string excluded)
        {
            var remaining = new List<string>();
            for (int i = 0; i < profiles.Count; i++)
            {
                if (!string.Equals(profiles[i], excluded, System.StringComparison.Ordinal))
                {
                    remaining.Add(profiles[i]);
                }
            }
            return [.. remaining];
        }

        /// <summary>
        /// Creates a persistent store with no previously retained identity values.
        /// </summary>
        private static Mock<IAssetConfigurationStore> PersistentStore()
        {
            var store = new Mock<IAssetConfigurationStore>(MockBehavior.Strict);
            store.SetupGet(value => value.IsPersistent).Returns(true);
            store.Setup(value => value.GetValueAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);
            return store;
        }

        /// <summary>
        /// Grants nameplate writes only to the standard configuration-administrator role.
        /// </summary>
        private static ArrayOf<RolePermissionType> WritableIdentityPermissions =>
        [
            new RolePermissionType
            {
                RoleId = ObjectIds.WellKnownRole_ConfigureAdmin,
                Permissions = (uint)(PermissionType.Browse | PermissionType.Read | PermissionType.Write)
            }
        ];

        /// <summary>The independent inherited profiles required by OPC 40223 Table 154.</summary>
        private static readonly ArrayOf<string> PumpBaseServerProfilePrerequisites =
        [
            "http://opcfoundation.org/UA-Profile/Server/EmbeddedUA2017",
            "http://opcfoundation.org/UA-Profile/Server/DataAccess",
            "http://opcfoundation.org/UA-Profile/Server/ComplexTypes2017",
            "http://opcfoundation.org/UA-Profile/Server/AggregateSubscription"
        ];

        /// <summary>The independent inherited profiles required by OPC 40223 Table 155.</summary>
        private static readonly ArrayOf<string> PumpAdvancedServerProfilePrerequisites =
        [
            "http://opcfoundation.org/UA-Profile/Server/AddressSpaceNotifier",
            "http://opcfoundation.org/UA-Profile/Server/ACAddressSpaceInstance",
            "http://opcfoundation.org/UA-Profile/Server/ACExclusiveAlarming",
            "http://opcfoundation.org/UA-Profile/Server/BaseHistoricalEvent",
            "http://opcfoundation.org/UA-Profile/Server/HistoricalRawData",
            "http://opcfoundation.org/UA-Profile/Server/AggregateHistorical"
        ];

        /// <summary>The Pump Base profile identifier from OPC 40223 Table 153.</summary>
        private const string PumpBaseServerProfile = "http://opcfoundation.org/UA-Profile/Pumps/Server/Base";

        /// <summary>The Pump Advanced profile identifier from OPC 40223 Table 153.</summary>
        private const string PumpAdvancedServerProfile =
            "http://opcfoundation.org/UA-Profile/Pumps/Server/Advanced";

        /// <summary>The inherited Machinery writable identification profile.</summary>
        private const string MachineryMachineIdentificationWritableProfile =
            "http://opcfoundation.org/UA-Profile/Machinery/Server/MachineIdentificationWritable";

        /// <summary>The independent address-space owner.</summary>
        private PumpsServerFixture m_fixture = null!;
    }
}
