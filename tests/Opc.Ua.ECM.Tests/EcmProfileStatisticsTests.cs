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
 *
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
using Opc.Ua.ECM;
using Opc.Ua.ECM.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.ECM.Tests
{
    /// <summary>
    /// Exercises the E2, E3 and D1 profiles, shared statistics and value ownership in memory.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class EcmProfileStatisticsTests
    {
        /// <summary>
        /// The profile catalogue declares the published member counts and statistic members.
        /// </summary>
        [Test]
        public void CatalogueDeclaresPublishedProfileMembers()
        {
            Dictionary<string, EcmProfileDefinition> profiles = EcmMeasurementProfiles.All
                .ToDictionary(profile => profile.Name, StringComparer.Ordinal);

            Assert.That(profiles.Keys, Is.EquivalentTo(new[] { "E0", "E1", "E2", "E3", "D0", "D1" }));
            Assert.That(profiles["E2"].Members.Count(member => member.IsStatistic), Is.EqualTo(2));
            Assert.That(profiles["E3"].Members.Count, Is.EqualTo(10));
            Assert.That(profiles["E3"].Members.Count(member => member.IsStatistic), Is.EqualTo(4));
            Assert.That(profiles["D1"].Members.Count, Is.EqualTo(7));
            Assert.That(profiles["D1"].Members.Count(member => member.IsStatistic), Is.EqualTo(4));
        }

        /// <summary>
        /// The profile catalogue carries the standardized MeasurementID and engineering-unit identity for every member.
        /// </summary>
        [TestCase(nameof(BrowseNames.AcCurrentPe), 1218, "http://www.opcfoundation.org/UA/units/un/cefact",
            4279632, "A", "ampere")]
        [TestCase(nameof(BrowseNames.AcActivePowerTotal), 1412, "http://www.opcfoundation.org/UA/units/un/cefact",
            5723220, "W", "watt")]
        [TestCase(nameof(BrowseNames.AcActiveEnergyTotalExportLp), 1004,
            "http://www.opcfoundation.org/UA/units/un/cefact", 5720146, "W·h", "watt hour")]
        [TestCase(nameof(BrowseNames.AcActiveEnergyTotalImportLp), 1001,
            "http://www.opcfoundation.org/UA/units/un/cefact", 5720146, "W·h", "watt hour")]
        [TestCase(nameof(BrowseNames.AcActiveEnergyTotalExportHp), 1005,
            "http://www.opcfoundation.org/UA/units/un/cefact", 5720146, "W·h", "watt hour")]
        [TestCase(nameof(BrowseNames.AcActiveEnergyTotalImportHp), 1002,
            "http://www.opcfoundation.org/UA/units/un/cefact", 5720146, "W·h", "watt hour")]
        [TestCase(nameof(BrowseNames.AcActivePowerPe), 1409, "http://www.opcfoundation.org/UA/units/un/cefact",
            5723220, "W", "watt")]
        [TestCase(nameof(BrowseNames.AcPowerFactorPe), 1709, null, null, null, null)]
        [TestCase(nameof(BrowseNames.AcReactiveEnergyTotalExportHp), 1014, "http://opcfoundation.org/UA/ECM/",
            1, "var·h", "volt ampere reactive hour")]
        [TestCase(nameof(BrowseNames.AcReactiveEnergyTotalImportHp), 1011, "http://opcfoundation.org/UA/ECM/",
            1, "var·h", "volt ampere reactive hour")]
        [TestCase(nameof(BrowseNames.AcReactivePowerPe), 1618, "http://www.opcfoundation.org/UA/units/un/cefact",
            4469812, "var", "var")]
        [TestCase(nameof(BrowseNames.AcVoltagePe), 1118, "http://www.opcfoundation.org/UA/units/un/cefact",
            5655636, "V", "volt")]
        [TestCase(nameof(BrowseNames.AcVoltagePp), 1145, "http://www.opcfoundation.org/UA/units/un/cefact",
            5655636, "V", "volt")]
        [TestCase(nameof(BrowseNames.DcCurrent), 1033, "http://www.opcfoundation.org/UA/units/un/cefact",
            4279632, "A", "ampere")]
        [TestCase(nameof(BrowseNames.DcActivePower), 1032, "http://www.opcfoundation.org/UA/units/un/cefact",
            5723220, "W", "watt")]
        [TestCase(nameof(BrowseNames.DcElectricalCharge), 1030,
            "http://www.opcfoundation.org/UA/units/un/cefact", 4279624, "A·h", "ampere hour")]
        [TestCase(nameof(BrowseNames.DcEnergyTotalExportLp), 1025,
            "http://www.opcfoundation.org/UA/units/un/cefact", 5720146, "W·h", "watt hour")]
        [TestCase(nameof(BrowseNames.DcEnergyTotalImportLp), 1022,
            "http://www.opcfoundation.org/UA/units/un/cefact", 5720146, "W·h", "watt hour")]
        [TestCase(nameof(BrowseNames.DcRelativeCharge), 1031, "http://www.opcfoundation.org/UA/units/un/cefact",
            20529, "%", "percent")]
        [TestCase(nameof(BrowseNames.DcVoltage), 1034, "http://www.opcfoundation.org/UA/units/un/cefact",
            5655636, "V", "volt")]
        public void CatalogueDeclaresStandardMeasurementMetadata(
            string name,
            int expectedMeasurementId,
            string? expectedNamespaceUri,
            int? expectedUnitId,
            string? expectedSymbol,
            string? expectedDescription)
        {
            Assert.That(EcmMeasurementProfiles.TryGetMember(name, out EcmProfileMember? member), Is.True);
            Assert.That(member!.MeasurementId, Is.EqualTo(expectedMeasurementId));

            EUInformation? engineeringUnits = EcmMeasurementProfiles.EngineeringUnitsOf(member);
            if (expectedUnitId is null)
            {
                Assert.That(engineeringUnits, Is.Null);
                return;
            }

            Assert.That(engineeringUnits, Is.Not.Null);
            Assert.That(engineeringUnits!.NamespaceUri, Is.EqualTo(expectedNamespaceUri));
            Assert.That(engineeringUnits.UnitId, Is.EqualTo(expectedUnitId));
            Assert.That(engineeringUnits.DisplayName.Text, Is.EqualTo(expectedSymbol));
            Assert.That(engineeringUnits.Description.Text, Is.EqualTo(expectedDescription));
        }

        /// <summary>
        /// Members that the profile does not declare reject AccuracyRange or EngineeringUnits.
        /// </summary>
        [Test]
        public void CatalogueRestrictsOptionalPropertiesPerPublishedMember()
        {
            Assert.That(EcmMeasurementProfiles.TryGetMember(
                nameof(BrowseNames.AcPowerFactorPe), out EcmProfileMember? factor),
                Is.True);
            Assert.That(factor!.Unit, Is.Null);
            Assert.That(factor.AllowsRange, Is.True);
            Assert.That(EcmMeasurementProfiles.TryGetMember(
                nameof(BrowseNames.DcVoltage), out EcmProfileMember? voltage),
                Is.True);
            Assert.That(voltage!.AllowsRange, Is.False);
            Assert.That(EcmMeasurementProfiles.TryGetMember(
                nameof(BrowseNames.DcCurrent), out EcmProfileMember? current),
                Is.True);
            Assert.That(current!.AllowsRange, Is.True);
        }

        /// <summary>
        /// A partial profile that is not completed by other members is declared as no profile.
        /// </summary>
        [Test]
        public void PartialProfileDeclaresNothing()
        {
            List<EcmProfileDefinition> declared = EcmMeasurementProfiles.Declared(
                new[] { nameof(BrowseNames.DcActivePower), nameof(BrowseNames.DcVoltage) });

            Assert.That(declared, Is.Empty);
        }

        /// <summary>
        /// A complete D1 set also completes D0 because DcCurrent is shared.
        /// </summary>
        [Test]
        public void CompleteD1AlsoDeclaresD0()
        {
            List<EcmProfileDefinition> declared = EcmMeasurementProfiles.Declared(
                D1Names().ToList());

            Assert.That(declared.Select(profile => profile.Name), Is.EquivalentTo(new[] { "D0", "D1" }));
        }

        /// <summary>
        /// Ownership copies AcPe values field by field into a new instance.
        /// </summary>
        [Test]
        public void OwnDetachesAcPeValue()
        {
            var source = new AcPeDataType { L1 = 1f, L2 = 2f, L3 = 3f };

            Variant owned = EcmOwnership.Own(Variant.FromStructure(source));

            Assert.That(owned.TryGetStructure(out AcPeDataType? copy), Is.True);
            Assert.That(copy, Is.Not.SameAs(source));
            Assert.That(copy!.L2, Is.EqualTo(2f));
        }

        /// <summary>
        /// Ownership rejects array values that cannot be treated as an immutable scalar or exact structure.
        /// </summary>
        [Test]
        public void OwnRejectsUnsupportedValue()
        {
            Assert.That(
                () => EcmOwnership.Own(Variant.From(new[] { 1f, 2f }.ToArrayOf())),
                Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// Ownership rejects an AcPe subclass with an ArgumentException before touching any virtual member.
        /// </summary>
        [Test]
        public void OwnRejectsAcPeSubclassWithoutVirtualCalls()
        {
            var hostile = new HostileAcPe { L1 = 1f };
            Variant value = Variant.FromStructure(hostile);
            hostile.Arm();

            Assert.That(() => EcmOwnership.Own(value), Throws.TypeOf<ArgumentException>());
            Assert.That(hostile.Touched, Is.EqualTo(0));
            Assert.That(hostile.TypeIdReads, Is.EqualTo(0));
        }

        /// <summary>
        /// Ownership passes immutable scalars and null through unchanged.
        /// </summary>
        [Test]
        public void OwnKeepsScalarsAndNull()
        {
            Assert.That(EcmOwnership.Own(Variant.From(2.5f)).TryGetValue(out float value), Is.True);
            Assert.That(value, Is.EqualTo(2.5f));
            Assert.That(EcmOwnership.Own(Variant.Null).IsNull, Is.True);
        }

        /// <summary>
        /// An E2 point publishes statistic references, StartTime and a grouped reset that replaces values.
        /// </summary>
        [Test]
        public async Task E2PublishesSharedStatisticsAndGroupedResetAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            ushort ecmNamespace = context.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var started = new DateTime(2026, 8, 14, 8, 0, 0, DateTimeKind.Utc);
            var provider = new StatisticsProvider(
                E2Snapshot(10f, 100f, 200f, acquired, started),
                new EcmStatisticsSnapshot(
                    (DateTimeUtc)new DateTime(2026, 8, 14, 9, 30, 0, DateTimeKind.Utc),
                    new[]
                    {
                        Float(nameof(BrowseNames.AcActiveEnergyTotalExportLp), 0f, acquired),
                        Float(nameof(BrowseNames.AcActiveEnergyTotalImportLp), 0f, acquired)
                    }.ToArrayOf()));
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("Device", manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Device", manager.InstanceNamespaceIndex),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            await manager.AddPredefinedNodeAsync(parent);

            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("Meter", manager.InstanceNamespaceIndex),
                provider,
                parent,
                authorize: static (_, _) => new ValueTask<bool>(true));
            EnergyMeasurementState point = binding.MeasurementPoint;

            Assert.That(
                point.ReferenceExists(
                    Opc.Ua.ReferenceTypeIds.HasInterface,
                    false,
                    ExpandedNodeId.ToNodeId(ObjectTypeIds.IEnergyProfileE2Type, context.NamespaceUris)),
                Is.True);
            var children = new List<BaseInstanceState>();
            point.GetChildren(context, children);
            MethodState? reset = children.OfType<MethodState>()
                .SingleOrDefault(method => method.BrowseName.Name == "ResetStatistics");
            PropertyState? startTime = children.OfType<PropertyState>()
                .SingleOrDefault(property => property.BrowseName.Name == "StartTime");
            Assert.That(reset, Is.Not.Null);
            Assert.That(startTime, Is.Not.Null);
            Assert.That(startTime!.WrappedValue.TryGetValue(out DateTimeUtc initial), Is.True);
            Assert.That(initial, Is.EqualTo((DateTimeUtc)started));

            ServiceResult result = await reset!.OnCallMethod2Async!(
                context, reset, point.NodeId, default, new List<Variant>(), CancellationToken.None);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(provider.ResetCount, Is.EqualTo(1));
            Assert.That(startTime.WrappedValue.TryGetValue(out DateTimeUtc after), Is.True);
            Assert.That(after, Is.EqualTo((DateTimeUtc)new DateTime(2026, 8, 14, 9, 30, 0, DateTimeKind.Utc)));
            EnergyMeasurementValueState export = (EnergyMeasurementValueState)point.FindChild(
                context, new QualifiedName(nameof(BrowseNames.AcActiveEnergyTotalExportLp), ecmNamespace))!;
            Assert.That(export.WrappedValue.TryGetValue(out float exported), Is.True);
            Assert.That(exported, Is.EqualTo(0f));
        }

        /// <summary>
        /// A denied reset leaves provider state and published values untouched.
        /// </summary>
        [Test]
        public async Task DeniedResetDoesNotInvokeProviderResetAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var provider = new StatisticsProvider(
                E2Snapshot(10f, 100f, 200f, acquired, acquired),
                new EcmStatisticsSnapshot((DateTimeUtc)acquired, default))
            {
                Authorization = new ServiceResult(StatusCodes.BadUserAccessDenied)
            };
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("Device", manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Device", manager.InstanceNamespaceIndex),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            await manager.AddPredefinedNodeAsync(parent);
            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("Meter", manager.InstanceNamespaceIndex),
                provider,
                parent,
                authorize: static (_, _) => new ValueTask<bool>(true));
            var children = new List<BaseInstanceState>();
            binding.MeasurementPoint.GetChildren(context, children);
            MethodState reset = children.OfType<MethodState>()
                .Single(method => method.BrowseName.Name == "ResetStatistics");

            ServiceResult result = await reset.OnCallMethod2Async!(
                context, reset, binding.MeasurementPoint.NodeId, default, new List<Variant>(), CancellationToken.None);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(provider.ResetCount, Is.Zero);
        }

        /// <summary>
        /// A reset whose engineering-unit metadata carries an unsupported mutable argument is rejected
        /// before formatting and leaves the published StartTime and values untouched.
        /// </summary>
        [Test]
        public async Task ResetRejectsUnsupportedLocalizedTextArgumentWithoutFormattingAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            ushort ecmNamespace = context.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var started = new DateTime(2026, 8, 14, 8, 0, 0, DateTimeKind.Utc);
            var argument = new FormatProbe();
            var units = new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = 5_720_146,
                DisplayName = new LocalizedText("unit", "en", "{0}", new object[] { argument })
            };
            EcmMeasurementReading unsafeReading = Float(
                nameof(BrowseNames.AcActiveEnergyTotalExportLp), 0f, acquired) with { EngineeringUnits = units };
            var provider = new StatisticsProvider(
                E2Snapshot(10f, 100f, 200f, acquired, started),
                new EcmStatisticsSnapshot(
                    (DateTimeUtc)new DateTime(2026, 8, 14, 9, 30, 0, DateTimeKind.Utc),
                    new[]
                    {
                        unsafeReading,
                        Float(nameof(BrowseNames.AcActiveEnergyTotalImportLp), 0f, acquired)
                    }.ToArrayOf()));
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("Device", manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Device", manager.InstanceNamespaceIndex),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            await manager.AddPredefinedNodeAsync(parent);
            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("Meter", manager.InstanceNamespaceIndex),
                provider,
                parent,
                authorize: static (_, _) => new ValueTask<bool>(true));
            EnergyMeasurementState point = binding.MeasurementPoint;
            var children = new List<BaseInstanceState>();
            point.GetChildren(context, children);
            MethodState reset = children.OfType<MethodState>()
                .Single(method => method.BrowseName.Name == "ResetStatistics");
            PropertyState startTime = children.OfType<PropertyState>()
                .Single(property => property.BrowseName.Name == "StartTime");

            await Assert.ThatAsync(
                async () => await reset.OnCallMethod2Async!(
                    context, reset, point.NodeId, default, new List<Variant>(), CancellationToken.None),
                Throws.TypeOf<ArgumentException>());
            Assert.That(argument.FormatCount, Is.Zero);
            Assert.That(startTime.WrappedValue.TryGetValue(out DateTimeUtc retained), Is.True);
            Assert.That(retained, Is.EqualTo((DateTimeUtc)started));
            EnergyMeasurementValueState export = (EnergyMeasurementValueState)point.FindChild(
                context, new QualifiedName(nameof(BrowseNames.AcActiveEnergyTotalExportLp), ecmNamespace))!;
            Assert.That(export.WrappedValue.TryGetValue(out float exported), Is.True);
            Assert.That(exported, Is.EqualTo(100f));
        }

        /// <summary>
        /// Disposing the binding removes the bound reset handler so no callback outlives its resources.
        /// </summary>
        [Test]
        public async Task DisposedBindingRemovesResetHandlerAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var provider = new StatisticsProvider(
                E2Snapshot(10f, 100f, 200f, acquired, acquired),
                new EcmStatisticsSnapshot((DateTimeUtc)acquired, default));
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("Device", manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Device", manager.InstanceNamespaceIndex),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            await manager.AddPredefinedNodeAsync(parent);
            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("Meter", manager.InstanceNamespaceIndex),
                provider,
                parent,
                authorize: static (_, _) => new ValueTask<bool>(true));
            var children = new List<BaseInstanceState>();
            binding.MeasurementPoint.GetChildren(context, children);
            MethodState reset = children.OfType<MethodState>()
                .Single(method => method.BrowseName.Name == "ResetStatistics");

            await binding.DisposeAsync();
            await binding.DisposeAsync();

            Assert.That(reset.OnCallMethod2Async, Is.Null);
        }

        /// <summary>
        /// Statistic members require a statistics provider.
        /// </summary>
        [Test]
        public async Task StatisticMembersRequireStatisticsProviderAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var provider = new PlainProvider(E2Snapshot(10f, 100f, 200f, acquired, acquired));
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("Device", manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Device", manager.InstanceNamespaceIndex),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            await manager.AddPredefinedNodeAsync(parent);

            await Assert.ThatAsync(
                async () => await manager.CreateMeasurementAsync(
                    new QualifiedName("Meter", manager.InstanceNamespaceIndex),
                    provider,
                    parent),
                Throws.TypeOf<InvalidOperationException>());
        }

        /// <summary>
        /// A partial D1 set is rejected instead of publishing an incomplete profile.
        /// </summary>
        [Test]
        public async Task PartialD1SetIsRejectedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var provider = new PlainProvider(new EcmMeasurementSnapshot(
                "tag",
                new[] { Float(nameof(BrowseNames.DcVoltage), 48f, acquired) }.ToArrayOf()));
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("Device", manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Device", manager.InstanceNamespaceIndex),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            await manager.AddPredefinedNodeAsync(parent);

            await Assert.ThatAsync(
                async () => await manager.CreateMeasurementAsync(
                    new QualifiedName("Meter", manager.InstanceNamespaceIndex),
                    provider,
                    parent),
                Throws.TypeOf<InvalidOperationException>());
        }

        /// <summary>
        /// D1-only members reject an AccuracyRange the profile does not declare.
        /// </summary>
        [Test]
        public async Task D1OnlyMemberRejectsAccuracyRangeAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            EcmMeasurementReading[] readings = D1Names()
                .Select(name => Float(name, 1f, acquired))
                .ToArray();
            int index = Array.FindIndex(readings, reading => reading.BrowseName == nameof(BrowseNames.DcVoltage));
            readings[index] = readings[index] with { AccuracyRange = 0.5f };
            var provider = new StatisticsProvider(
                new EcmMeasurementSnapshot("tag", readings.ToArrayOf())
                {
                    StatisticsStartTime = (DateTimeUtc)acquired
                },
                new EcmStatisticsSnapshot((DateTimeUtc)acquired, default));
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("Device", manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Device", manager.InstanceNamespaceIndex),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            await manager.AddPredefinedNodeAsync(parent);

            await Assert.ThatAsync(
                async () => await manager.CreateMeasurementAsync(
                    new QualifiedName("Meter", manager.InstanceNamespaceIndex),
                    provider,
                    parent),
                Throws.TypeOf<InvalidOperationException>());
        }

        /// <summary>
        /// E3 publishes typed phase structures and exposes neither MeasurementPeriod nor units where the
        /// published catalogue defines none.
        /// </summary>
        [Test]
        public async Task E3PublishesTypedPhaseValuesWithoutPeriodAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            ushort ecmNamespace = context.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var provider = new StatisticsProvider(
                E3Snapshot(acquired),
                new EcmStatisticsSnapshot((DateTimeUtc)acquired, default));
            BaseObjectState parent = await AddParentAsync(manager);

            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("Meter", manager.InstanceNamespaceIndex),
                provider,
                parent);
            EnergyMeasurementState point = binding.MeasurementPoint;

            Assert.That(
                point.ReferenceExists(
                    Opc.Ua.ReferenceTypeIds.HasInterface,
                    false,
                    ExpandedNodeId.ToNodeId(ObjectTypeIds.IEnergyProfileE3Type, context.NamespaceUris)),
                Is.True);
            var voltage = (EnergyMeasurementValueState)point.FindChild(
                context, new QualifiedName(nameof(BrowseNames.AcVoltagePp), ecmNamespace))!;
            Assert.That(voltage.WrappedValue.TryGetStructure(out AcPpDataType? lines), Is.True);
            Assert.That(lines!.L1L2, Is.EqualTo(400f));
            Assert.That(lines.L3L1, Is.EqualTo(402f));
            var factor = (EnergyMeasurementValueState)point.FindChild(
                context, new QualifiedName(nameof(BrowseNames.AcPowerFactorPe), ecmNamespace))!;
            Assert.That(factor.WrappedValue.TryGetStructure(out AcPeDataType? phases), Is.True);
            Assert.That(phases!.L2, Is.EqualTo(0.9f));
            Assert.That(factor.EngineeringUnits, Is.Null);
            var energy = (EnergyMeasurementValueState)point.FindChild(
                context, new QualifiedName(nameof(BrowseNames.AcActiveEnergyTotalExportHp), ecmNamespace))!;
            Assert.That(energy.WrappedValue.TryGetValue(out double exported), Is.True);
            Assert.That(exported, Is.EqualTo(1234.5d));
            foreach (string name in E3Names())
            {
                var value = (EnergyMeasurementValueState)point.FindChild(
                    context, new QualifiedName(name, ecmNamespace))!;
                Assert.That(value.MeasurementPeriod, Is.Null, name);
            }
        }

        /// <summary>
        /// D1 publishes its typed Float members and omits MeasurementPeriod as the published catalogue requires.
        /// </summary>
        [Test]
        public async Task D1PublishesTypedValuesWithoutPeriodAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            ushort ecmNamespace = context.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var provider = new StatisticsProvider(
                new EcmMeasurementSnapshot(
                    "tag",
                    D1Names().Select(name => Float(name, 12.5f, acquired)).ToArrayOf())
                {
                    StatisticsStartTime = (DateTimeUtc)acquired
                },
                new EcmStatisticsSnapshot((DateTimeUtc)acquired, default));
            BaseObjectState parent = await AddParentAsync(manager);

            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("Meter", manager.InstanceNamespaceIndex),
                provider,
                parent);

            foreach (string name in D1Names())
            {
                var value = (EnergyMeasurementValueState)binding.MeasurementPoint.FindChild(
                    context, new QualifiedName(name, ecmNamespace))!;
                Assert.That(value.WrappedValue.TryGetValue(out float reading), Is.True, name);
                Assert.That(reading, Is.EqualTo(12.5f), name);
                Assert.That(value.MeasurementPeriod, Is.Null, name);
            }
        }

        /// <summary>
        /// Reset requires the binding's authorization and DI lock ownership before provider permission.
        /// </summary>
        [Test]
        public async Task ResetRequiresBindingOwnershipBeforeProviderAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var provider = new StatisticsProvider(
                E2Snapshot(10f, 100f, 200f, acquired, acquired),
                new EcmStatisticsSnapshot((DateTimeUtc)acquired, default));
            var locks = new Mock<ILockService>();
            bool owner = false;
            locks.As<ILockOwnershipService>().Setup(service => service.IsLockOwner(
                It.IsAny<ISystemContext>(), It.IsAny<NodeId>())).Returns(() => owner);
            BaseObjectState parent = await AddParentAsync(manager);
            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("Meter", manager.InstanceNamespaceIndex),
                provider,
                parent,
                exclusiveAccess: locks.Object,
                authorize: static (_, _) => new ValueTask<bool>(true));
            var children = new List<BaseInstanceState>();
            binding.MeasurementPoint.GetChildren(context, children);
            MethodState reset = children.OfType<MethodState>()
                .Single(method => method.BrowseName.Name == "ResetStatistics");

            ServiceResult denied = await reset.OnCallMethod2Async!(
                context, reset, binding.MeasurementPoint.NodeId, default, new List<Variant>(), CancellationToken.None);
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(provider.ResetCount, Is.Zero);

            owner = true;
            ServiceResult granted = await reset.OnCallMethod2Async!(
                context, reset, binding.MeasurementPoint.NodeId, default, new List<Variant>(), CancellationToken.None);
            Assert.That(ServiceResult.IsGood(granted), Is.True);
            Assert.That(provider.ResetCount, Is.EqualTo(1));

            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            await Assert.ThatAsync(
                async () => await reset.OnCallMethod2Async!(
                    context, reset, binding.MeasurementPoint.NodeId, default, new List<Variant>(), cancelled.Token),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(provider.ResetCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A statistic with a null value and Uncertain quality is published without fabricating a value. The
        /// binding opts into the shared IA composition, which accepts Uncertain and Bad null readings and
        /// still rejects Good null readings. This source test has not been executed.
        /// </summary>
        [Test]
        public async Task UncertainNullStatisticIsPublishedWithoutFabricatedValueAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            ushort ecmNamespace = context.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            EcmMeasurementSnapshot snapshot = E2Snapshot(10f, 100f, 200f, acquired, acquired);
            EcmMeasurementReading[] readings = snapshot.Measurements.ToArray();
            int index = Array.FindIndex(
                readings, reading => reading.BrowseName == nameof(BrowseNames.AcActiveEnergyTotalExportLp));
            readings[index] = readings[index] with { Value = default, StatusCode = StatusCodes.UncertainInitialValue };
            var provider = new StatisticsProvider(
                snapshot with { Measurements = readings.ToArrayOf() },
                new EcmStatisticsSnapshot((DateTimeUtc)acquired, default));
            BaseObjectState parent = await AddParentAsync(manager);

            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("Meter", manager.InstanceNamespaceIndex),
                provider,
                parent);

            var export = (EnergyMeasurementValueState)binding.MeasurementPoint.FindChild(
                context, new QualifiedName(nameof(BrowseNames.AcActiveEnergyTotalExportLp), ecmNamespace))!;
            Assert.That(export.WrappedValue.IsNull, Is.True);
            Assert.That(export.StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
        }

        /// <summary>
        /// The factory-created manager publishes a statistics measurement through the same provider contract as
        /// direct construction, and statistic profiles without a statistics provider are rejected.
        /// </summary>
        [Test]
        public async Task FactoryCreatedManagerComposesStatisticsProviderAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> server = CreateServer(telemetry, queues);
            IAsyncNodeManager created = await new EcmNodeManagerFactory().CreateAsync(
                server.Object, CreateConfiguration(), CancellationToken.None);
            await using EcmNodeManager manager = (EcmNodeManager)created;
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var provider = new StatisticsProvider(
                E2Snapshot(10f, 100f, 200f, acquired, acquired),
                new EcmStatisticsSnapshot((DateTimeUtc)acquired, default));
            BaseObjectState parent = await AddParentAsync(manager);

            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("Meter", manager.InstanceNamespaceIndex),
                provider,
                parent);

            Assert.That(binding.MeasurementPoint.Parent, Is.SameAs(parent));
            await Assert.ThatAsync(
                async () => await manager.CreateMeasurementAsync(
                    new QualifiedName("Plain", manager.InstanceNamespaceIndex),
                    new PlainProvider(E2Snapshot(10f, 100f, 200f, acquired, acquired)),
                    parent),
                Throws.Exception);
        }

        /// <summary>
        /// The first statistic notification already sees the new instantaneous value and ApplicationTag.
        /// </summary>
        [Test]
        public async Task FirstStatisticSinkObservesCommittedCompanionsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            var first = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var second = first.AddMinutes(1);
            var provider = new StatisticsProvider(
                E2Snapshot(10f, 100f, 200f, first, first),
                new EcmStatisticsSnapshot((DateTimeUtc)first, default));
            EcmMeasurementBinding binding = await CreateE2BindingAsync(manager, provider);
            EnergyMeasurementValueState power = Child(binding, context, nameof(BrowseNames.AcActivePowerTotal));
            var observations = new List<(float Power, string Tag, DateTimeUtc PowerTime)>();
            NodeStateChangedAsyncHandler sink = (_, _, _, _) =>
            {
                power.WrappedValue.TryGetValue(out float value);
                observations.Add((value, binding.MeasurementPoint.ApplicationTag!.Value ?? string.Empty,
                    power.Timestamp));
                return default;
            };
            Child(binding, context, nameof(BrowseNames.AcActiveEnergyTotalExportLp)).OnStateChangedAsync = sink;
            Child(binding, context, nameof(BrowseNames.AcActiveEnergyTotalImportLp)).OnStateChangedAsync = sink;
            int powerNotifications = 0;
            int tagNotifications = 0;
            power.OnStateChangedAsync = (_, _, _, _) =>
            {
                powerNotifications++;
                return default;
            };
            binding.MeasurementPoint.ApplicationTag!.OnStateChangedAsync = (_, _, _, _) =>
            {
                tagNotifications++;
                return default;
            };
            provider.Snapshot = E2Snapshot(20f, 110f, 210f, second, first) with { ApplicationTag = "tag2" };

            await binding.RefreshAsync(CancellationToken.None);

            Assert.That(powerNotifications, Is.EqualTo(1));
            Assert.That(tagNotifications, Is.EqualTo(1));
            Assert.That(observations, Has.Count.EqualTo(2));
            Assert.That(observations[0].Power, Is.EqualTo(20f));
            Assert.That(observations[0].Tag, Is.EqualTo("tag2"));
            Assert.That(observations[0].PowerTime, Is.EqualTo((DateTimeUtc)second));
        }

        /// <summary>
        /// A reset retains the instantaneous value, ApplicationTag and their timestamps, and a provider reset that
        /// succeeds is committed even when the caller cancels during it.
        /// </summary>
        [Test]
        public async Task ResetRetainsCompanionValuesAndTimestampsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            var first = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var second = first.AddMinutes(1);
            var provider = new StatisticsProvider(
                E2Snapshot(10f, 100f, 200f, first, first),
                new EcmStatisticsSnapshot(
                    (DateTimeUtc)second.AddMinutes(1),
                    new[]
                    {
                        Float(nameof(BrowseNames.AcActiveEnergyTotalExportLp), 0f, second),
                        Float(nameof(BrowseNames.AcActiveEnergyTotalImportLp), 0f, second)
                    }.ToArrayOf()));
            EcmMeasurementBinding binding = await CreateE2BindingAsync(manager, provider);
            provider.Snapshot = E2Snapshot(20f, 110f, 210f, second, first) with { ApplicationTag = "tag2" };
            await binding.RefreshAsync(CancellationToken.None);
            EnergyMeasurementValueState power = Child(binding, context, nameof(BrowseNames.AcActivePowerTotal));
            PropertyState<string> tag = binding.MeasurementPoint.ApplicationTag!;
            DateTimeUtc tagTime = tag.Timestamp;
            var children = new List<BaseInstanceState>();
            binding.MeasurementPoint.GetChildren(context, children);
            MethodState reset = children.OfType<MethodState>()
                .Single(method => method.BrowseName.Name == "ResetStatistics");
            using var cancellation = new CancellationTokenSource();
            provider.OnReset = cancellation.Cancel;

            ServiceResult result = await reset.OnCallMethod2Async!(
                context, reset, binding.MeasurementPoint.NodeId, default, new List<Variant>(), cancellation.Token);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(power.WrappedValue.TryGetValue(out float value), Is.True);
            Assert.That(value, Is.EqualTo(20f));
            Assert.That(power.Timestamp, Is.EqualTo((DateTimeUtc)second));
            Assert.That(tag.Value, Is.EqualTo("tag2"));
            Assert.That(tag.Timestamp, Is.EqualTo(tagTime));
            Assert.That(
                Child(binding, context, nameof(BrowseNames.AcActiveEnergyTotalExportLp))
                    .WrappedValue.TryGetValue(out float exported),
                Is.True);
            Assert.That(exported, Is.Zero);
            EnergyMeasurementValueState exportNode =
                Child(binding, context, nameof(BrowseNames.AcActiveEnergyTotalExportLp));
            EnergyMeasurementValueState importNode =
                Child(binding, context, nameof(BrowseNames.AcActiveEnergyTotalImportLp));
            Assert.That(importNode.WrappedValue.TryGetValue(out float imported), Is.True);
            Assert.That(imported, Is.Zero);
            Assert.That(exportNode.Timestamp, Is.EqualTo((DateTimeUtc)second));
            Assert.That(importNode.Timestamp, Is.EqualTo((DateTimeUtc)second));
            Assert.That(exportNode.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(importNode.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(power.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Uncertain null readings are published by a reset and for an instantaneous companion.

        /// <summary>
        /// Uncertain null readings are published by a reset and for an instantaneous companion.
        /// </summary> and for an instantaneous companion.
        /// </summary>
        [Test]
        public async Task UncertainNullResetAndCompanionArePublishedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            EcmMeasurementReading unknownExport = Float(
                nameof(BrowseNames.AcActiveEnergyTotalExportLp), 0f, acquired) with
            {
                Value = default,
                StatusCode = StatusCodes.UncertainInitialValue
            };
            var provider = new StatisticsProvider(
                E2Snapshot(10f, 100f, 200f, acquired, acquired),
                new EcmStatisticsSnapshot(
                    (DateTimeUtc)acquired,
                    new[]
                    {
                        unknownExport,
                        Float(nameof(BrowseNames.AcActiveEnergyTotalImportLp), 0f, acquired)
                    }.ToArrayOf()));
            EcmMeasurementBinding binding = await CreateE2BindingAsync(manager, provider);
            EcmMeasurementReading[] readings = provider.Snapshot.Measurements.ToArray();
            int power = Array.FindIndex(readings, r => r.BrowseName == nameof(BrowseNames.AcActivePowerTotal));
            readings[power] = readings[power] with { Value = default, StatusCode = StatusCodes.UncertainInitialValue };
            provider.Snapshot = provider.Snapshot with { Measurements = readings.ToArrayOf() };
            await binding.RefreshAsync(CancellationToken.None);
            var children = new List<BaseInstanceState>();
            binding.MeasurementPoint.GetChildren(context, children);
            MethodState reset = children.OfType<MethodState>()
                .Single(method => method.BrowseName.Name == "ResetStatistics");

            ServiceResult result = await reset.OnCallMethod2Async!(
                context, reset, binding.MeasurementPoint.NodeId, default, new List<Variant>(), CancellationToken.None);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            EnergyMeasurementValueState export = Child(
                binding, context, nameof(BrowseNames.AcActiveEnergyTotalExportLp));
            Assert.That(export.WrappedValue.IsNull, Is.True);
            Assert.That(export.StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
            EnergyMeasurementValueState instantaneous = Child(binding, context, nameof(BrowseNames.AcActivePowerTotal));
            Assert.That(instantaneous.WrappedValue.IsNull, Is.True);
            Assert.That(instantaneous.StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
        }

        /// <summary>
        /// A rejected snapshot assigns no values and only marks the measurements unavailable.
        /// </summary>
        [Test]
        public async Task RejectedSnapshotRetainsValuesAndMarksUnavailableAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            var first = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var provider = new StatisticsProvider(
                E2Snapshot(10f, 100f, 200f, first, first),
                new EcmStatisticsSnapshot((DateTimeUtc)first, default));
            EcmMeasurementBinding binding = await CreateE2BindingAsync(manager, provider);
            EcmMeasurementSnapshot invalid = E2Snapshot(20f, 110f, 210f, first.AddMinutes(1), first) with
            {
                ApplicationTag = "tag2"
            };
            EcmMeasurementReading[] readings = invalid.Measurements.ToArray();
            readings[1] = readings[1] with { Value = Variant.From(1.0) };
            provider.Snapshot = invalid with { Measurements = readings.ToArrayOf() };

            await Assert.ThatAsync(
                async () => await binding.RefreshAsync(CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>());

            EnergyMeasurementValueState power = Child(binding, context, nameof(BrowseNames.AcActivePowerTotal));
            Assert.That(power.WrappedValue.TryGetValue(out float value), Is.True);
            Assert.That(value, Is.EqualTo(10f));
            Assert.That(binding.MeasurementPoint.ApplicationTag!.Value, Is.EqualTo("tag"));
            Assert.That(power.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(
                Child(binding, context, nameof(BrowseNames.AcActiveEnergyTotalExportLp))
                    .WrappedValue.TryGetValue(out float exported),
                Is.True);
            Assert.That(exported, Is.EqualTo(100f));
        }

        /// <summary>
        /// A failing statistic sink leaves the complete new snapshot committed and marks the measurements unavailable.
        /// </summary>
        [Test]
        public async Task StatisticSinkFailureRetainsFullNewSnapshotAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            var first = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var second = first.AddMinutes(1);
            var provider = new StatisticsProvider(
                E2Snapshot(10f, 100f, 200f, first, first),
                new EcmStatisticsSnapshot((DateTimeUtc)first, default));
            EcmMeasurementBinding binding = await CreateE2BindingAsync(manager, provider);
            Child(binding, context, nameof(BrowseNames.AcActiveEnergyTotalExportLp)).OnStateChangedAsync =
                (_, _, _, _) => throw new InvalidOperationException("sink");
            provider.Snapshot = E2Snapshot(20f, 110f, 210f, second, first) with { ApplicationTag = "tag2" };

            await Assert.ThatAsync(
                async () => await binding.RefreshAsync(CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>());

            EnergyMeasurementValueState power = Child(binding, context, nameof(BrowseNames.AcActivePowerTotal));
            Assert.That(power.WrappedValue.TryGetValue(out float value), Is.True);
            Assert.That(value, Is.EqualTo(20f));
            Assert.That(power.Timestamp, Is.EqualTo((DateTimeUtc)second));
            Assert.That(binding.MeasurementPoint.ApplicationTag!.Value, Is.EqualTo("tag2"));
            Assert.That(
                Child(binding, context, nameof(BrowseNames.AcActiveEnergyTotalImportLp))
                    .WrappedValue.TryGetValue(out float imported),
                Is.True);
            Assert.That(imported, Is.EqualTo(210f));
            Assert.That(power.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
        }

        /// <summary>
        /// A first refresh that fails after the topology exists is retried without duplicate nodes or callbacks.
        /// </summary>
        [Test]
        public async Task FailedFirstPublicationRetriesWithoutDuplicateTopologyAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            var first = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            BaseObjectState parent = await AddParentAsync(manager);
            EnergyMeasurementState point = context.CreateInstanceOfEnergyMeasurementType(
                parent, new QualifiedName("DirectMeter", manager.InstanceNamespaceIndex));
            var provider = new StatisticsProvider(
                E2Snapshot(10f, 100f, 200f, first, first),
                new EcmStatisticsSnapshot((DateTimeUtc)first, default));
            using var cancellation = new CancellationTokenSource();
            provider.OnRead = cancellation.Cancel;
            await using var binding = new EcmMeasurementBinding(context, point, provider);

            await Assert.ThatAsync(
                async () => await binding.RefreshAsync(cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
            provider.OnRead = null;
            await binding.RefreshAsync(CancellationToken.None);

            Assert.That(CountChildren(point, context, nameof(BrowseNames.AcActivePowerTotal)), Is.EqualTo(1));
            int notifications = 0;
            EnergyMeasurementValueState power = Child(binding, context, nameof(BrowseNames.AcActivePowerTotal));
            power.OnStateChangedAsync = (_, _, _, _) =>
            {
                notifications++;
                return default;
            };
            provider.Snapshot = E2Snapshot(20f, 110f, 210f, first.AddMinutes(1), first);
            await binding.RefreshAsync(CancellationToken.None);
            Assert.That(power.WrappedValue.TryGetValue(out float value), Is.True);
            Assert.That(value, Is.EqualTo(20f));
            Assert.That(notifications, Is.EqualTo(1));
        }

        /// <summary>
        /// A rollback that cannot remove its owned children is reported and rejects a later initialization.
        /// </summary>
        [Test]
        public async Task IncompleteRollbackRejectsRetryAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            var first = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            BaseObjectState parent = await AddParentAsync(manager);
            var point = new StickyMeasurementState(parent);
            point.Create(
                context,
                new NodeId("StickyMeter", manager.InstanceNamespaceIndex),
                new QualifiedName("StickyMeter", manager.InstanceNamespaceIndex),
                new LocalizedText("StickyMeter"),
                true);
            var provider = new StatisticsProvider(
                E2Snapshot(10f, 100f, 200f, first, first),
                new EcmStatisticsSnapshot((DateTimeUtc)first, default));
            using var cancellation = new CancellationTokenSource();
            provider.OnRead = cancellation.Cancel;
            await using var binding = new EcmMeasurementBinding(context, point, provider);

            await Assert.ThatAsync(
                async () => await binding.RefreshAsync(cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
            provider.OnRead = null;

            await Assert.ThatAsync(
                async () => await binding.RefreshAsync(CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>());
            Assert.That(CountChildren(point, context, nameof(BrowseNames.AcActivePowerTotal)), Is.EqualTo(1));
        }

        /// <summary>
        /// A measurement point whose child removal has no effect.
        /// </summary>
        private sealed class StickyMeasurementState(NodeState? parent) : EnergyMeasurementState(parent)
        {
            /// <inheritdoc/>
            public override void RemoveChild(BaseInstanceState child)
            {
            }
        }

        /// <summary>
        /// A first publication whose sink throws keeps the committed snapshot and retries on the same topology.
        /// </summary>
        [Test]
        public async Task SinkFailureOnFirstPublicationRetriesOnSameTopologyAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            var first = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            BaseObjectState parent = await AddParentAsync(manager);
            EnergyMeasurementState point = context.CreateInstanceOfEnergyMeasurementType(
                parent, new QualifiedName("DirectMeter", manager.InstanceNamespaceIndex));
            var provider = new StatisticsProvider(
                E2Snapshot(10f, 100f, 200f, first, first),
                new EcmStatisticsSnapshot((DateTimeUtc)first, default));
            int failures = 0;
            point.ApplicationTag!.OnStateChangedAsync = (_, _, _, _) =>
                failures++ == 0 ? throw new InvalidOperationException("sink") : default;
            await using var binding = new EcmMeasurementBinding(context, point, provider);
            await Assert.ThatAsync(
                async () => await binding.RefreshAsync(CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>());

            EnergyMeasurementValueState power = Child(binding, context, nameof(BrowseNames.AcActivePowerTotal));
            int childCount = CountChildren(point, context);
            Assert.That(power.WrappedValue.TryGetValue(out float committed), Is.True);
            Assert.That(committed, Is.EqualTo(10f));
            Assert.That(power.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            provider.Snapshot = E2Snapshot(20f, 110f, 210f, first.AddMinutes(1), first);

            await binding.RefreshAsync(CancellationToken.None);

            Assert.That(CountChildren(point, context), Is.EqualTo(childCount));
            Assert.That(Child(binding, context, nameof(BrowseNames.AcActivePowerTotal)), Is.SameAs(power));
            Assert.That(power.WrappedValue.TryGetValue(out float refreshed), Is.True);
            Assert.That(refreshed, Is.EqualTo(20f));
            Assert.That(power.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Published E3 structures are detached from the provider's instances.
        /// </summary>
        [Test]
        public async Task E3PublishedStructuresAreDetachedFromProviderValuesAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            EcmMeasurementSnapshot snapshot = E3Snapshot(acquired);
            var provider = new StatisticsProvider(
                snapshot,
                new EcmStatisticsSnapshot((DateTimeUtc)acquired, default));
            BaseObjectState parent = await AddParentAsync(manager);
            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("Meter", manager.InstanceNamespaceIndex),
                provider,
                parent);
            EcmMeasurementReading source = snapshot.Measurements.Single(
                reading => reading.BrowseName == nameof(BrowseNames.AcVoltagePe));
            Assert.That(source.Value.TryGetStructure(out AcPeDataType? original), Is.True);
            original!.L1 = 999f;

            EnergyMeasurementValueState voltage = Child(binding, context, nameof(BrowseNames.AcVoltagePe));
            Assert.That(voltage.WrappedValue.TryGetStructure(out AcPeDataType? published), Is.True);
            Assert.That(published, Is.Not.SameAs(original));
            Assert.That(published!.L1, Is.EqualTo(0.8f));

            EcmMeasurementReading lineSource = snapshot.Measurements.Single(
                reading => reading.BrowseName == nameof(BrowseNames.AcVoltagePp));
            Assert.That(lineSource.Value.TryGetStructure(out AcPpDataType? lineOriginal), Is.True);
            float l1l2 = lineOriginal!.L1L2;
            lineOriginal.L1L2 = 999f;
            lineOriginal.L2L3 = 999f;
            lineOriginal.L3L1 = 999f;

            EnergyMeasurementValueState lineVoltage = Child(binding, context, nameof(BrowseNames.AcVoltagePp));
            Assert.That(lineVoltage.WrappedValue.TryGetStructure(out AcPpDataType? lines), Is.True);
            Assert.That(lines, Is.Not.SameAs(lineOriginal));
            Assert.That(lines!.L1L2, Is.EqualTo(l1l2));
            Assert.That(lines.L2L3, Is.Not.EqualTo(999f));
            Assert.That(lines.L3L1, Is.Not.EqualTo(999f));
        }

        /// <summary>
        /// The first statistic notification of an E2 point with an E0 companion sees the detached current value and
        /// the new ApplicationTag, and a reset keeps the timestamps.
        /// </summary>
        [Test]
        public async Task E2WithE0FirstSinkObservesDetachedCurrentAndTagAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            var first = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var second = first.AddMinutes(1);
            EcmMeasurementSnapshot WithCurrent(EcmMeasurementSnapshot e2, AcPeDataType phases, DateTime at)
            {
                var current = new EcmMeasurementReading(
                    nameof(BrowseNames.AcCurrentPe),
                    Variant.FromStructure(phases),
                    DataTypeIds.AcPeDataType,
                    StatusCodes.Good,
                    at);
                return e2 with { Measurements = e2.Measurements.ToArray().Append(current).ToArray().ToArrayOf() };
            }
            var provider = new StatisticsProvider(
                WithCurrent(E2Snapshot(10f, 100f, 200f, first, first), new AcPeDataType { L1 = 1f }, first),
                new EcmStatisticsSnapshot((DateTimeUtc)first, default));
            EcmMeasurementBinding binding = await CreateE2BindingAsync(manager, provider);
            EnergyMeasurementValueState current = Child(binding, context, nameof(BrowseNames.AcCurrentPe));
            var observations = new List<(float L1, string Tag, DateTimeUtc CurrentTime)>();
            NodeStateChangedAsyncHandler sink = (_, _, _, _) =>
            {
                current.WrappedValue.TryGetStructure(out AcPeDataType? seen);
                observations.Add((seen!.L1, binding.MeasurementPoint.ApplicationTag!.Value ?? string.Empty,
                    current.Timestamp));
                return default;
            };
            Child(binding, context, nameof(BrowseNames.AcActiveEnergyTotalExportLp)).OnStateChangedAsync = sink;
            Child(binding, context, nameof(BrowseNames.AcActiveEnergyTotalImportLp)).OnStateChangedAsync = sink;
            var next = new AcPeDataType { L1 = 7f };
            provider.Snapshot = WithCurrent(E2Snapshot(20f, 110f, 210f, second, first), next, second)
                with { ApplicationTag = "tag2" };

            await binding.RefreshAsync(CancellationToken.None);
            next.L1 = 99f;

            Assert.That(observations, Has.Count.EqualTo(2));
            Assert.That(observations[0].L1, Is.EqualTo(7f));
            Assert.That(observations[0].Tag, Is.EqualTo("tag2"));
            Assert.That(observations[0].CurrentTime, Is.EqualTo((DateTimeUtc)second));
            Assert.That(current.WrappedValue.TryGetStructure(out AcPeDataType? published), Is.True);
            Assert.That(published!.L1, Is.EqualTo(7f));
        }

        /// <summary>
        /// A hostile AcPe subclass is rejected before formatting and leaves the committed values unchanged.
        /// </summary>
        [Test]
        public async Task HostileAcPeSubclassIsRejectedWithoutFormattingAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var provider = new StatisticsProvider(
                E3Snapshot(acquired),
                new EcmStatisticsSnapshot((DateTimeUtc)acquired, default));
            BaseObjectState parent = await AddParentAsync(manager);
            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("Meter", manager.InstanceNamespaceIndex),
                provider,
                parent);
            var hostile = new HostileAcPe { L1 = 5f, L2 = 6f, L3 = 7f };
            EcmMeasurementReading[] readings = E3Snapshot(acquired.AddMinutes(1)).Measurements.ToArray();
            int index = Array.FindIndex(readings, r => r.BrowseName == nameof(BrowseNames.AcVoltagePe));
            readings[index] = readings[index] with { Value = Variant.FromStructure(hostile) };
            provider.Snapshot = E3Snapshot(acquired.AddMinutes(1)) with { Measurements = readings.ToArrayOf() };
            hostile.Arm();

            await Assert.ThatAsync(
                async () => await binding.RefreshAsync(CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>()
                    .And.Message.Contains("ECM measurement"));

            Assert.That(hostile.Touched, Is.EqualTo(0));
            Assert.That(hostile.TypeIdReads, Is.EqualTo(0));
            EnergyMeasurementValueState voltage = Child(binding, context, nameof(BrowseNames.AcVoltagePe));
            Assert.That(voltage.WrappedValue.TryGetStructure(out AcPeDataType? retained), Is.True);
            Assert.That(retained!.L1, Is.EqualTo(0.8f));
            Assert.That(voltage.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(binding.MeasurementPoint.ApplicationTag!.Value, Is.EqualTo("tag"));
        }

        /// <summary>
        /// An E2 point that also declares E0 publishes the AcPe companion detached and keeps it across a reset.
        /// </summary>
        [Test]
        public async Task E2WithE0CompanionPublishesDetachedAcPeAndRetainsItOnResetAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            var first = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var phases = new AcPeDataType { L1 = 1f, L2 = 2f, L3 = 3f };
            EcmMeasurementSnapshot e2 = E2Snapshot(10f, 100f, 200f, first, first);
            var current = new EcmMeasurementReading(
                nameof(BrowseNames.AcCurrentPe),
                Variant.FromStructure(phases),
                DataTypeIds.AcPeDataType,
                StatusCodes.Good,
                first);
            var provider = new StatisticsProvider(
                e2 with { Measurements = e2.Measurements.ToArray().Append(current).ToArray().ToArrayOf() },
                new EcmStatisticsSnapshot(
                    (DateTimeUtc)first.AddMinutes(5),
                    new[]
                    {
                        Float(nameof(BrowseNames.AcActiveEnergyTotalExportLp), 0f, first.AddMinutes(1)),
                        Float(nameof(BrowseNames.AcActiveEnergyTotalImportLp), 0f, first.AddMinutes(1))
                    }.ToArrayOf()));
            EcmMeasurementBinding binding = await CreateE2BindingAsync(manager, provider);
            phases.L1 = 99f;

            EnergyMeasurementValueState published = Child(binding, context, nameof(BrowseNames.AcCurrentPe));
            Assert.That(published.WrappedValue.TryGetStructure(out AcPeDataType? copy), Is.True);
            Assert.That(copy!.L1, Is.EqualTo(1f));
            Assert.That(
                binding.MeasurementPoint.ReferenceExists(
                    Opc.Ua.ReferenceTypeIds.HasInterface,
                    false,
                    ExpandedNodeId.ToNodeId(ObjectTypeIds.IEnergyProfileE0Type, context.NamespaceUris)),
                Is.True);
            var children = new List<BaseInstanceState>();
            binding.MeasurementPoint.GetChildren(context, children);
            MethodState reset = children.OfType<MethodState>()
                .Single(method => method.BrowseName.Name == "ResetStatistics");

            DateTimeUtc currentTime = published.Timestamp;
            ServiceResult result = await reset.OnCallMethod2Async!(
                context, reset, binding.MeasurementPoint.NodeId, default, new List<Variant>(), CancellationToken.None);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(published.Timestamp, Is.EqualTo(currentTime));
            Assert.That(published.WrappedValue.TryGetStructure(out AcPeDataType? retained), Is.True);
            Assert.That(retained!.L1, Is.EqualTo(1f));
        }

        /// <summary>
        /// An AcPe subclass that records every virtual member the validation path must not invoke.
        /// </summary>
        private sealed class HostileAcPe : AcPeDataType
        {
            private bool m_armed;

            /// <summary>
            /// Gets the number of formatting, equality, hashing, encoding, cloning and comparison attempts.
            /// </summary>
            public int Touched { get; private set; }

            /// <summary>
            /// Gets the number of type identifier reads after arming.
            /// </summary>
            public int TypeIdReads { get; private set; }

            /// <inheritdoc/>
            public override ExpandedNodeId TypeId
            {
                get
                {
                    if (m_armed)
                    {
                        TypeIdReads++;
                    }
                    return base.TypeId;
                }
            }

            /// <summary>
            /// Starts counting after the Variant that wraps this instance has been created.
            /// </summary>
            public void Arm()
            {
                m_armed = true;
            }

            /// <inheritdoc/>
            public override string ToString()
            {
                Record();
                return "hostile";
            }

            /// <inheritdoc/>
            public override bool Equals(object? obj)
            {
                Record();
                return ReferenceEquals(this, obj);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                Record();
                return 0;
            }

            /// <inheritdoc/>
            public override void Encode(IEncoder encoder)
            {
                Record();
                throw new InvalidOperationException("Encode must not be invoked.");
            }

            /// <inheritdoc/>
            public override object Clone()
            {
                Record();
                throw new InvalidOperationException("Clone must not be invoked.");
            }

            /// <inheritdoc/>
            public override bool IsEqual(IEncodeable? encodeable)
            {
                Record();
                throw new InvalidOperationException("IsEqual must not be invoked.");
            }

            private void Record()
            {
                if (m_armed)
                {
                    Touched++;
                }
            }
        }

        /// <summary>
        /// Creates the E2 binding used by the composition tests.
        /// </summary>
        private static async Task<EcmMeasurementBinding> CreateE2BindingAsync(
            EcmNodeManager manager,
            StatisticsProvider provider)
        {
            BaseObjectState parent = await AddParentAsync(manager);
            return await manager.CreateMeasurementAsync(
                new QualifiedName("Meter", manager.InstanceNamespaceIndex),
                provider,
                parent,
                authorize: static (_, _) => new ValueTask<bool>(true));
        }

        /// <summary>
        /// Counts the children of a node, optionally restricted to one browse name.
        /// </summary>
        private static int CountChildren(NodeState node, ISystemContext context, string? name = null)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            return name == null ? children.Count : children.Count(child => child.BrowseName.Name == name);
        }

        /// <summary>
        /// Finds a published measurement child by ECM browse name.
        /// </summary>
        private static EnergyMeasurementValueState Child(
            EcmMeasurementBinding binding,
            ISystemContext context,
            string name)
        {
            ushort ecmNamespace = context.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            return (EnergyMeasurementValueState)binding.MeasurementPoint.FindChild(
                context, new QualifiedName(name, ecmNamespace))!;
        }

        /// <summary>
        /// Adds a parent object that receives the measurement point.
        /// </summary>
        private static async Task<BaseObjectState> AddParentAsync(EcmNodeManager manager)
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("Device", manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Device", manager.InstanceNamespaceIndex),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            await manager.AddPredefinedNodeAsync(parent);
            return parent;
        }

        /// <summary>
        /// Returns the E3 member names.
        /// </summary>
        private static IEnumerable<string> E3Names()
        {
            return EcmMeasurementProfiles.All.Single(profile => profile.Name == "E3").Members.Select(m => m.Name);
        }

        /// <summary>
        /// Creates an E3 snapshot with distinct typed phase values.
        /// </summary>
        private static EcmMeasurementSnapshot E3Snapshot(DateTime timestamp)
        {
            EcmMeasurementReading[] readings = EcmMeasurementProfiles.All.Single(profile => profile.Name == "E3")
                .Members.Select(member => member.Name switch
                {
                    nameof(BrowseNames.AcVoltagePp) => new EcmMeasurementReading(
                        member.Name,
                        Variant.FromStructure(new AcPpDataType { L1L2 = 400f, L2L3 = 401f, L3L1 = 402f }),
                        DataTypeIds.AcPpDataType,
                        StatusCodes.Good,
                        timestamp),
                    nameof(BrowseNames.AcPowerFactorPe) or nameof(BrowseNames.AcActivePowerPe) or
                    nameof(BrowseNames.AcCurrentPe) or nameof(BrowseNames.AcReactivePowerPe) or
                    nameof(BrowseNames.AcVoltagePe) => new EcmMeasurementReading(
                        member.Name,
                        Variant.FromStructure(new AcPeDataType { L1 = 0.8f, L2 = 0.9f, L3 = 1f }),
                        DataTypeIds.AcPeDataType,
                        StatusCodes.Good,
                        timestamp),
                    _ => new EcmMeasurementReading(
                        member.Name,
                        Variant.From(1234.5d),
                        new ExpandedNodeId(Opc.Ua.DataTypeIds.Double),
                        StatusCodes.Good,
                        timestamp)
                }).ToArray();
            return new EcmMeasurementSnapshot("tag", readings.ToArrayOf())
            {
                StatisticsStartTime = (DateTimeUtc)timestamp
            };
        }
        /// <summary>
        /// Returns the D1 member names.
        /// </summary>
        private static IEnumerable<string> D1Names()
        {
            return EcmMeasurementProfiles.All.Single(profile => profile.Name == "D1").Members.Select(m => m.Name);
        }

        /// <summary>
        /// Creates a deterministic Float reading.
        /// </summary>
        private static EcmMeasurementReading Float(string name, float value, DateTime timestamp)
        {
            return new EcmMeasurementReading(
                name,
                Variant.From(value),
                new ExpandedNodeId(Opc.Ua.DataTypeIds.Float),
                StatusCodes.Good,
                timestamp);
        }

        /// <summary>
        /// Creates an E2 snapshot.
        /// </summary>
        private static EcmMeasurementSnapshot E2Snapshot(
            float power,
            float exported,
            float imported,
            DateTime timestamp,
            DateTime started)
        {
            return new EcmMeasurementSnapshot(
                "tag",
                new[]
                {
                    Float(nameof(BrowseNames.AcActivePowerTotal), power, timestamp),
                    Float(nameof(BrowseNames.AcActiveEnergyTotalExportLp), exported, timestamp),
                    Float(nameof(BrowseNames.AcActiveEnergyTotalImportLp), imported, timestamp)
                }.ToArrayOf())
            {
                StatisticsStartTime = (DateTimeUtc)started
            };
        }

        /// <summary>
        /// Creates an in-memory ECM manager without opening a network endpoint.
        /// </summary>
        private static EcmNodeManager CreateManager(ITelemetryContext telemetry, MonitoredItemQueueFactory queues)
        {
            Mock<IServerInternal> server = CreateServer(telemetry, queues);
            return new EcmNodeManager(server.Object, CreateConfiguration());
        }

        /// <summary>
        /// Creates the application configuration shared by in-memory managers.
        /// </summary>
        private static ApplicationConfiguration CreateConfiguration()
        {
            return new ApplicationConfiguration
            {
                ApplicationUri = "urn:ecm-tests:profile-statistics",
                ApplicationName = "EcmProfileStatistics",
                ServerConfiguration = new ServerConfiguration()
            };
        }

        /// <summary>
        /// Creates a minimal in-memory server.
        /// </summary>
        private static Mock<IServerInternal> CreateServer(
            ITelemetryContext telemetry,
            MonitoredItemQueueFactory queues)
        {
            var server = new Mock<IServerInternal>();
            var namespaces = new NamespaceTable();
            IEncodeableFactory factory = EncodeableFactory.Create();
            factory.Builder.AddOpcUaECM().Commit();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
            server.SetupGet(value => value.Factory).Returns(factory);
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(queues);
            server.SetupGet(value => value.NodeManager).Returns(Mock.Of<IMasterNodeManager>());
            server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            return server;
        }


        /// <summary>
        /// A mutable format argument that counts formatting attempts.
        /// </summary>
        private sealed class FormatProbe
        {
            public int FormatCount { get; private set; }

            public override string ToString()
            {
                FormatCount++;
                return "probe";
            }
        }

        /// <summary>
        /// Supplies a fixed snapshot without statistics support.
        /// </summary>
        private sealed class PlainProvider(EcmMeasurementSnapshot snapshot) : IEcmMeasurementProvider
        {
            public ValueTask<EcmMeasurementSnapshot> ReadAsync(CancellationToken cancellationToken)
            {
                return new ValueTask<EcmMeasurementSnapshot>(snapshot);
            }

            public ValueTask WriteApplicationTagAsync(string applicationTag, CancellationToken cancellationToken)
            {
                return default;
            }
        }

        /// <summary>
        /// Supplies a fixed snapshot and a fixed reset result.
        /// </summary>
        private sealed class StatisticsProvider(EcmMeasurementSnapshot snapshot, EcmStatisticsSnapshot reset)
            : IEcmStatisticsProvider
        {
            public int ResetCount { get; private set; }

            public ServiceResult Authorization { get; set; } = ServiceResult.Good;

            public EcmMeasurementSnapshot Snapshot { get; set; } = snapshot;

            public Action? OnReset { get; set; }

            public Action? OnRead { get; set; }

            public ValueTask<EcmMeasurementSnapshot> ReadAsync(CancellationToken cancellationToken)
            {
                OnRead?.Invoke();
                return new ValueTask<EcmMeasurementSnapshot>(Snapshot);
            }

            public ValueTask WriteApplicationTagAsync(string applicationTag, CancellationToken cancellationToken)
            {
                return default;
            }

            public ValueTask<ServiceResult> AuthorizeStatisticsResetAsync(
                ISystemContext requestContext,
                CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(Authorization);
            }

            public ValueTask<EcmStatisticsSnapshot> ResetStatisticsAsync(CancellationToken cancellationToken)
            {
                ResetCount++;
                OnReset?.Invoke();
                return new ValueTask<EcmStatisticsSnapshot>(reset);
            }
        }
    }
}
