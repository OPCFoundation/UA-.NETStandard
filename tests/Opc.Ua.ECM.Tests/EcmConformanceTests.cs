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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Di;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.ECM;
using Opc.Ua.ECM.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.ECM.Tests
{
    /// <summary>
    /// Pins down the runtime ECM conformance units and inherited server profiles.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class EcmConformanceTests
    {
        private static readonly string[] s_baseUnits =
        [
            "ECM Energy Measurement Common DataTypes",
            "ECM Accuracy Domain Percent full scale",
            "ECM Accuracy Domain Percent actual reading",
            "ECM Accuracy Domain IEC",
            "ECM Accuracy Domain EN"
        ];

        private static readonly string[] s_baselineProfiles =
        [
            "http://opcfoundation.org/UA-Profile/Server/NanoEmbeddedDevice2017",
            "http://opcfoundation.org/UA-Profile/Server/DataAccess",
            "http://opcfoundation.org/UA-Profile/Server/ComplexTypes2017",
            "http://opcfoundation.org/UA-Profile/ECM/Server/EnergyManagement"
        ];

        private static readonly string[] s_controlProfiles =
        [
            .. s_baselineProfiles,
            "http://opcfoundation.org/UA-Profile/DI/Server/Locking",
            "http://opcfoundation.org/UA-Profile/ECM/Server/EnergyController",
            "http://opcfoundation.org/UA-Profile/Server/EmbeddedUA2017",
            "http://opcfoundation.org/UA-Profile/ECM/Server/EnergyManagementControl"
        ];

        private static int s_measurementInstanceId;

        /// <summary>
        /// An owner with no live ECM bindings still reports the five model data-type units only.
        /// </summary>
        [Test]
        public async Task NoCapabilityReportsOnlyBaseUnitsAndNoProfiles()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);

            AssertReport(
                EcmConformanceContributor.For(manager),
                s_baseUnits,
                []);
        }

        /// <summary>
        /// Each complete profile and their union advertise exactly the declared profile units.
        /// </summary>
        [TestCase("E0")]
        [TestCase("E1")]
        [TestCase("E2")]
        [TestCase("E3")]
        [TestCase("D0")]
        [TestCase("D1")]
        [TestCase("All")]
        public async Task MeasurementOnlyReportsDeclaredProfilesAndMandatoryInheritance(string selection)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            string[] inputProfiles = InputProfiles(selection);
            string[] expectedProfiles = ExpectedMeasurementProfiles(selection);
            await using EcmMeasurementBinding binding = await CreateMeasurementBindingAsync(
                manager,
                inputProfiles).ConfigureAwait(false);
            EcmConformanceContributor contributor = EcmConformanceContributor.For(manager);
            contributor.Register(binding);

            Assert.That(binding.ProfileNames, Is.EquivalentTo(expectedProfiles));
            AssertReport(
                contributor,
                WithProfiles(
                    "ECM Energy Management Support",
                    "ECM Energy Measurement",
                    expectedProfiles),
                s_baselineProfiles);
        }

        /// <summary>
        /// WOL-only reports sleep support whether or not its binding receives an independent lock service.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task WolOnlyReportsSleepCapabilityAndMandatoryInheritance(bool locking)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            await using EcmWolBinding binding = CreateWolBinding(manager, locking);
            EcmConformanceContributor contributor = EcmConformanceContributor.For(manager);
            contributor.Register(binding);

            Assert.That(binding.IsDisposed, Is.False);
            AssertReport(
                contributor,
                WithCapabilities("ECM Energy Management Support", "ECM Sleep Mode WOL"),
                s_baselineProfiles);
        }

        /// <summary>
        /// Standby without WOL or DI locking reports the control units and only baseline profiles.
        /// </summary>
        [Test]
        public async Task StandbyWithoutLockReportsControlUnitsWithoutControlProfiles()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            await using EcmPowerBinding binding = CreatePowerBinding(manager, supportsWol: false, locking: false);
            EcmConformanceContributor contributor = EcmConformanceContributor.For(manager);
            contributor.Register(binding);

            Assert.That(binding.SupportsWol, Is.False);
            Assert.That(binding.SupportsLocking, Is.False);
            AssertReport(
                contributor,
                WithCapabilities(
                    "ECM Energy Management Support",
                    "ECM Standby Management",
                    "ECM Standby Management Control"),
                s_baselineProfiles);
        }

        /// <summary>
        /// Standby with both WOL and DI locking reports all capability units and all inherited profiles.
        /// </summary>
        [Test]
        public async Task StandbyWithWolAndLockReportsFullControlInheritance()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            await using EcmPowerBinding binding = CreatePowerBinding(manager, supportsWol: true, locking: true);
            EcmConformanceContributor contributor = EcmConformanceContributor.For(manager);
            contributor.Register(binding);

            Assert.That(binding.SupportsWol, Is.True);
            Assert.That(binding.SupportsLocking, Is.True);
            AssertReport(
                contributor,
                WithCapabilities(
                    "ECM Energy Management Support",
                    "ECM Standby Management",
                    "ECM Standby Management Control",
                    "ECM Sleep Mode WOL"),
                s_controlProfiles);
        }

        /// <summary>
        /// Standby locking activates the control profile chain even when the binding has no WOL object.
        /// </summary>
        [Test]
        public async Task StandbyWithLockWithoutWolReportsControlInheritance()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            await using EcmPowerBinding binding = CreatePowerBinding(manager, supportsWol: false, locking: true);
            EcmConformanceContributor contributor = EcmConformanceContributor.For(manager);
            contributor.Register(binding);

            Assert.That(binding.SupportsWol, Is.False);
            Assert.That(binding.SupportsLocking, Is.True);
            AssertReport(
                contributor,
                WithCapabilities(
                    "ECM Energy Management Support",
                    "ECM Standby Management",
                    "ECM Standby Management Control"),
                s_controlProfiles);
        }

        /// <summary>
        /// WOL on standby does not activate the control profiles unless standby also has DI locking.
        /// </summary>
        [Test]
        public async Task StandbyWithWolWithoutLockDoesNotReportControlInheritance()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            await using EcmPowerBinding binding = CreatePowerBinding(manager, supportsWol: true, locking: false);
            EcmConformanceContributor contributor = EcmConformanceContributor.For(manager);
            contributor.Register(binding);

            Assert.That(binding.SupportsWol, Is.True);
            Assert.That(binding.SupportsLocking, Is.False);
            AssertReport(
                contributor,
                WithCapabilities(
                    "ECM Energy Management Support",
                    "ECM Standby Management",
                    "ECM Standby Management Control",
                    "ECM Sleep Mode WOL"),
                s_baselineProfiles);
        }

        /// <summary>
        /// The explicit power-binding overload adds its configured capabilities to the owner's report.
        /// </summary>
        [Test]
        public async Task RegisterPowerBindingUpdatesSharedConformanceReport()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            await using EcmPowerBinding binding = CreatePowerBinding(manager, supportsWol: true, locking: true);

            manager.RegisterEcmBinding(binding);

            AssertReport(
                EcmConformanceContributor.For(manager),
                WithCapabilities(
                    "ECM Energy Management Support",
                    "ECM Standby Management",
                    "ECM Standby Management Control",
                    "ECM Sleep Mode WOL"),
                s_controlProfiles);
        }

        /// <summary>
        /// The explicit standalone-WOL overload updates the report without advertising standby control.
        /// </summary>
        [Test]
        public async Task RegisterWolBindingUpdatesSharedConformanceReport()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            await using EcmWolBinding binding = CreateWolBinding(manager, locking: false);

            manager.RegisterEcmBinding(binding);

            AssertReport(
                EcmConformanceContributor.For(manager),
                WithCapabilities("ECM Energy Management Support", "ECM Sleep Mode WOL"),
                s_baselineProfiles);
        }

        /// <summary>
        /// The explicit measurement overload contributes complete profile units to the shared report.
        /// </summary>
        [Test]
        public async Task RegisterMeasurementBindingUpdatesSharedConformanceReport()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            await using EcmMeasurementBinding binding = await CreateMeasurementBindingAsync(
                manager,
                "E0").ConfigureAwait(false);

            manager.RegisterEcmBinding(binding);

            Assert.That(binding.ProfileNames, Is.EquivalentTo(InputProfiles("E0")));
            AssertReport(
                EcmConformanceContributor.For(manager),
                WithProfiles(
                    "ECM Energy Management Support",
                    "ECM Energy Measurement",
                    "E0"),
                s_baselineProfiles);
        }

        /// <summary>
        /// Repeated registrations and overlapping bindings produce one exact union of units and profiles.
        /// </summary>
        [Test]
        public async Task RepeatedAndOverlappingBindingsDoNotDuplicateAdvertisement()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            EcmConformanceContributor contributor = EcmConformanceContributor.For(manager);
            await using EcmPowerBinding power = CreatePowerBinding(manager, supportsWol: true, locking: true);
            await using EcmWolBinding wol = CreateWolBinding(manager, locking: false);
            await using EcmMeasurementBinding e0 = await CreateMeasurementBindingAsync(
                manager,
                "E0").ConfigureAwait(false);
            await using EcmMeasurementBinding duplicateE0 = await CreateMeasurementBindingAsync(
                manager,
                "E0").ConfigureAwait(false);
            await using EcmMeasurementBinding dc0 = await CreateMeasurementBindingAsync(
                manager,
                "D0").ConfigureAwait(false);

            contributor.Register(power);
            contributor.Register(power);
            contributor.Register(wol);
            contributor.Register(wol);
            contributor.Register(e0);
            contributor.Register(e0);
            contributor.Register(duplicateE0);
            contributor.Register(dc0);

            AssertReport(
                contributor,
                WithCapabilities(
                    "ECM Energy Management Support",
                    "ECM Standby Management",
                    "ECM Standby Management Control",
                    "ECM Sleep Mode WOL",
                    "ECM Energy Measurement",
                    "ECM EnergyProfile E0",
                    "ECM EnergyProfile D0"),
                s_controlProfiles);
        }

        /// <summary>
        /// Disposing standby removes its unique units and inherited control profiles but retains WOL and metering.
        /// </summary>
        [Test]
        public async Task DisposingOneBindingRetainsCapabilitiesFromOtherBindings()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            EcmConformanceContributor contributor = EcmConformanceContributor.For(manager);
            await using EcmPowerBinding power = CreatePowerBinding(manager, supportsWol: true, locking: true);
            await using EcmWolBinding wol = CreateWolBinding(manager, locking: false);
            await using EcmMeasurementBinding measurement = await CreateMeasurementBindingAsync(
                manager,
                "E1").ConfigureAwait(false);
            contributor.Register(power);
            contributor.Register(wol);
            contributor.Register(measurement);

            AssertReport(
                contributor,
                WithCapabilities(
                    "ECM Energy Management Support",
                    "ECM Standby Management",
                    "ECM Standby Management Control",
                    "ECM Sleep Mode WOL",
                    "ECM Energy Measurement",
                    "ECM EnergyProfile E1"),
                s_controlProfiles);

            await power.DisposeAsync().ConfigureAwait(false);

            Assert.That(power.IsDisposed, Is.True);
            AssertReport(
                contributor,
                WithCapabilities(
                    "ECM Energy Management Support",
                    "ECM Sleep Mode WOL",
                    "ECM Energy Measurement",
                    "ECM EnergyProfile E1"),
                s_baselineProfiles);
        }

        /// <summary>
        /// Disposing every binding restores the same owner's base-only report.
        /// </summary>
        [Test]
        public async Task DisposingAllBindingsRestoresBaseOnlyReport()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            EcmConformanceContributor contributor = EcmConformanceContributor.For(manager);
            await using EcmPowerBinding power = CreatePowerBinding(manager, supportsWol: true, locking: true);
            await using EcmWolBinding wol = CreateWolBinding(manager, locking: false);
            await using EcmMeasurementBinding measurement = await CreateMeasurementBindingAsync(
                manager,
                "All").ConfigureAwait(false);
            contributor.Register(power);
            contributor.Register(wol);
            contributor.Register(measurement);

            AssertReport(
                contributor,
                WithCapabilities(
                    "ECM Energy Management Support",
                    "ECM Standby Management",
                    "ECM Standby Management Control",
                    "ECM Sleep Mode WOL",
                    "ECM Energy Measurement",
                    "ECM EnergyProfile E0",
                    "ECM EnergyProfile E1",
                    "ECM EnergyProfile E2",
                    "ECM EnergyProfile E3",
                    "ECM EnergyProfile D0",
                    "ECM EnergyProfile D1"),
                s_controlProfiles);

            await power.DisposeAsync().ConfigureAwait(false);
            await wol.DisposeAsync().ConfigureAwait(false);
            await measurement.DisposeAsync().ConfigureAwait(false);

            Assert.That(power.IsDisposed, Is.True);
            Assert.That(wol.IsDisposed, Is.True);
            Assert.That(measurement.IsDisposed, Is.True);
            AssertReport(contributor, s_baseUnits, []);
        }

        private static EcmNodeManager CreateManager(
            ITelemetryContext telemetry,
            MonitoredItemQueueFactory queues)
        {
            var server = new Mock<IServerInternal>();
            var namespaces = new NamespaceTable();
            IEncodeableFactory factory = EncodeableFactory.Create();
            factory.Builder.AddOpcUaECM().Commit();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.TypeTree).Returns(EcmTestTypeTable.Create(namespaces));
            server.SetupGet(value => value.Factory).Returns(factory);
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(queues);
            server.SetupGet(value => value.NodeManager).Returns(Mock.Of<IMasterNodeManager>());
            server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            return new EcmNodeManager(server.Object, new ApplicationConfiguration
            {
                ApplicationUri = "urn:ecm-tests:conformance",
                ApplicationName = "EcmConformance",
                ServerConfiguration = new ServerConfiguration()
            });
        }

        private static EcmPowerBinding CreatePowerBinding(
            EcmNodeManager manager,
            bool supportsWol,
            bool locking)
        {
            ISystemContext context = manager.SystemContext;
            EnergyStandbyManagementState standby = context.CreateInstanceOfEnergyStandbyManagementType(
                browseName: new QualifiedName("Power", manager.InstanceNamespaceIndex));
            EnergyDevicePowerOffState? powerOff = supportsWol
                ? context.CreateInstanceOfEnergyDevicePowerOffType(
                    browseName: new QualifiedName("Sleep", manager.InstanceNamespaceIndex))
                : null;
            return new EcmPowerBinding(
                context,
                standby,
                Mock.Of<IEcmStandbyProvider>(),
                powerOff,
                supportsWol ? CreateWolProvider().Object : null,
                locking ? Mock.Of<ILockService>() : null);
        }

        private static EcmWolBinding CreateWolBinding(EcmNodeManager manager, bool locking)
        {
            ISystemContext context = manager.SystemContext;
            EnergyDevicePowerOffState powerOff = context.CreateInstanceOfEnergyDevicePowerOffType(
                browseName: new QualifiedName("StandaloneSleep", manager.InstanceNamespaceIndex));
            return new EcmWolBinding(
                context,
                powerOff,
                CreateWolProvider().Object,
                exclusiveAccess: locking ? Mock.Of<ILockService>() : null);
        }

        private static Mock<IEcmWolProvider> CreateWolProvider()
        {
            var provider = new Mock<IEcmWolProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.MacAddress)
                .Returns(ByteString.From(new byte[] { 2, 3, 4, 5, 6, 7 }.AsSpan()));
            provider.SetupGet(value => value.RegularTimeToOperate).Returns(2_000);
            provider.SetupGet(value => value.TimeMinPause).Returns(6_000);
            provider.SetupGet(value => value.ModePowerConsumption).Returns(3);
            return provider;
        }

        private static async Task<EcmMeasurementBinding> CreateMeasurementBindingAsync(
            EcmNodeManager manager,
            params string[] profileNames)
        {
            bool allProfiles = profileNames.Length == 1 && profileNames[0] == "All";
            var selected = new HashSet<string>(profileNames, StringComparer.Ordinal);
            EcmProfileDefinition[] definitions = allProfiles
                ? EcmMeasurementProfiles.All.ToArray()
                : EcmMeasurementProfiles.All.Where(profile => selected.Contains(profile.Name)).ToArray();
            if (!allProfiles && definitions.Length != selected.Count)
            {
                throw new ArgumentException("Every requested profile must be in the ECM catalogue.", nameof(profileNames));
            }

            var memberNames = new SortedSet<string>(
                definitions.SelectMany(profile => profile.Members).Select(member => member.Name),
                StringComparer.Ordinal);
            var timestamp = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            EcmMeasurementReading[] readings = memberNames.Select(name => CreateReading(name, timestamp)).ToArray();
            var snapshot = new EcmMeasurementSnapshot(
                "conformance/test-meter",
                readings.ToArrayOf())
            {
                StatisticsStartTime = (DateTimeUtc)new DateTime(
                    2026,
                    8,
                    14,
                    8,
                    0,
                    0,
                    DateTimeKind.Utc)
            };
            var provider = new ConformanceMeasurementProvider(snapshot);
            ISystemContext context = manager.SystemContext;
            string measurementName = "Meter" + string.Join(string.Empty, profileNames) +
                Interlocked.Increment(ref s_measurementInstanceId);
            EnergyMeasurementState point = context.CreateInstanceOfEnergyMeasurementType(
                browseName: new QualifiedName(measurementName, manager.InstanceNamespaceIndex));
            var binding = new EcmMeasurementBinding(context, point, provider);
            await binding.RefreshAsync().ConfigureAwait(false);
            return binding;
        }

        private static EcmMeasurementReading CreateReading(string name, DateTime timestamp)
        {
            if (!EcmMeasurementProfiles.TryGetMember(name, out EcmProfileMember? member))
            {
                throw new ArgumentException($"Unknown ECM measurement {name}.", nameof(name));
            }
            Variant value = member.Kind switch
            {
                EcmMeasurementKind.Float => Variant.From(12.5f),
                EcmMeasurementKind.Double => Variant.From(24.75d),
                EcmMeasurementKind.AcPe => Variant.FromStructure(
                    new AcPeDataType { L1 = 1.25f, L2 = 2.5f, L3 = 3.75f }),
                EcmMeasurementKind.AcPp => Variant.FromStructure(
                    new AcPpDataType { L1L2 = 208f, L2L3 = 209f, L3L1 = 207f }),
                _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown ECM measurement kind.")
            };
            return new EcmMeasurementReading(
                name,
                value,
                EcmMeasurementProfiles.DataTypeOf(member.Kind),
                StatusCodes.Good,
                timestamp);
        }

        private static string[] InputProfiles(string selection)
        {
            return selection == "All"
                ? ["E0", "E1", "E2", "E3", "D0", "D1"]
                : [selection];
        }

        private static string[] ExpectedMeasurementProfiles(string selection)
        {
            return selection switch
            {
                "E0" => ["E0"],
                "E1" => ["E1"],
                "E2" => ["E1", "E2"],
                "E3" => ["E0", "E3"],
                "D0" => ["D0"],
                "D1" => ["D0", "D1"],
                "All" => ["E0", "E1", "E2", "E3", "D0", "D1"],
                _ => throw new ArgumentOutOfRangeException(nameof(selection))
            };
        }

        private static string[] WithCapabilities(params string[] capabilities)
        {
            return [.. s_baseUnits, .. capabilities];
        }

        private static string[] WithProfiles(
            string supportUnit,
            string measurementUnit,
            params string[] profileNames)
        {
            return
            [
                .. s_baseUnits,
                supportUnit,
                measurementUnit,
                .. profileNames.Select(profile => $"ECM EnergyProfile {profile}")
            ];
        }

        private static void AssertReport(
            EcmConformanceContributor contributor,
            IEnumerable<string> expectedUnitNames,
            IEnumerable<string> expectedProfiles)
        {
            string[] expectedUnits = expectedUnitNames.ToArray();
            string[] expectedProfileArray = expectedProfiles.ToArray();
            QualifiedName[] actualUnits = contributor.ConformanceUnits.Span.ToArray();
            string[] actualProfiles = contributor.ServerProfiles.Span.ToArray();

            Assert.That(actualUnits.Select(unit => unit.Name), Is.EquivalentTo(expectedUnits));
            Assert.That(actualUnits, Has.Length.EqualTo(expectedUnits.Length));
            Assert.That(actualUnits.Distinct().Count(), Is.EqualTo(actualUnits.Length));
            Assert.That(actualUnits.All(unit => unit.NamespaceIndex == 0), Is.True);
            Assert.That(actualProfiles, Is.EquivalentTo(expectedProfileArray));
            Assert.That(actualProfiles, Has.Length.EqualTo(expectedProfileArray.Length));
            Assert.That(actualProfiles.Distinct().Count(), Is.EqualTo(actualProfiles.Length));
        }

        private sealed class ConformanceMeasurementProvider(EcmMeasurementSnapshot snapshot)
            : IEcmStatisticsProvider
        {
            public ValueTask<EcmMeasurementSnapshot> ReadAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<EcmMeasurementSnapshot>(snapshot);
            }

            public ValueTask WriteApplicationTagAsync(
                string applicationTag,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return default;
            }

            public ValueTask<ServiceResult> AuthorizeStatisticsResetAsync(
                ISystemContext requestContext,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<EcmStatisticsSnapshot> ResetStatisticsAsync(CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("The conformance fixture does not reset statistics.");
            }
        }
    }
}
