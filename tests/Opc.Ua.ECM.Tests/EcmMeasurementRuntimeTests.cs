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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.ECM;
using Opc.Ua.ECM.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.ECM.Tests
{
    /// <summary>
    /// Exercises independent ECM measurement profiles, quality, accuracy and persistence in memory.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class EcmMeasurementRuntimeTests
    {
        /// <summary>
        /// Initial provider refresh publishes exactly the typed E0, E1 and D0 values and their profile references.
        /// </summary>
        [Test]
        public async Task InitialRefreshPublishesTypedE0E1D0ValuesQualityAndProfileReferences()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            ushort ecmNamespace = context.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            DateTimeUtc acquiredTimestamp = (DateTimeUtc)acquired;
            var provider = new TestMeasurementProvider(Snapshot(
                "plant/line-1",
                new EcmMeasurementReading(
                    nameof(BrowseNames.AcCurrentPe),
                    Variant.From(new AcPeDataType { L1 = 1.25f, L2 = 2.5f, L3 = 3.75f }),
                    new ExpandedNodeId(DataTypeIds.AcPeDataType, Namespaces.ECM),
                    StatusCodes.Good,
                    acquired),
                FloatReading(nameof(BrowseNames.AcActivePowerTotal), 15.5f,
                    StatusCodes.UncertainLastUsableValue, acquired),
                FloatReading(nameof(BrowseNames.DcCurrent), 24.75f,
                    StatusCodes.Good, acquired)));
            BaseObjectState parent = await AddParentAsync(manager, "Device");

            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("Meter", manager.InstanceNamespaceIndex),
                provider,
                parent);
            EnergyMeasurementState point = binding.MeasurementPoint;

            EnergyMeasurementValueState acCurrentPe = RequiredMeasurement(
                point, context, nameof(BrowseNames.AcCurrentPe), ecmNamespace);
            Assert.That(
                acCurrentPe.DataType,
                Is.EqualTo(new NodeId(DataTypeIds.AcPeDataType, ecmNamespace)));
            Assert.That(acCurrentPe.ValueRank, Is.EqualTo(Opc.Ua.ValueRanks.Scalar));
            Assert.That(acCurrentPe.WrappedValue.TryGetValue(out AcPeDataType? phaseCurrent), Is.True);
            Assert.That(phaseCurrent!.L1, Is.EqualTo(1.25f));
            Assert.That(phaseCurrent.L2, Is.EqualTo(2.5f));
            Assert.That(phaseCurrent.L3, Is.EqualTo(3.75f));
            Assert.That(acCurrentPe.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(acCurrentPe.Timestamp, Is.EqualTo(acquiredTimestamp));

            EnergyMeasurementValueState activePower = RequiredMeasurement(
                point, context, nameof(BrowseNames.AcActivePowerTotal), ecmNamespace);
            Assert.That(activePower.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Float));
            Assert.That(activePower.ValueRank, Is.EqualTo(Opc.Ua.ValueRanks.Scalar));
            Assert.That(activePower.WrappedValue.TryGetValue(out float activePowerValue), Is.True);
            Assert.That(activePowerValue, Is.EqualTo(15.5f));
            Assert.That(activePower.StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
            Assert.That(activePower.Timestamp, Is.EqualTo(acquiredTimestamp));

            EnergyMeasurementValueState dcCurrent = RequiredMeasurement(
                point, context, nameof(BrowseNames.DcCurrent), ecmNamespace);
            Assert.That(dcCurrent.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Float));
            Assert.That(dcCurrent.ValueRank, Is.EqualTo(Opc.Ua.ValueRanks.Scalar));
            Assert.That(dcCurrent.WrappedValue.TryGetValue(out float dcCurrentValue), Is.True);
            Assert.That(dcCurrentValue, Is.EqualTo(24.75f));
            Assert.That(dcCurrent.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(dcCurrent.Timestamp, Is.EqualTo(acquiredTimestamp));

            PropertyState<string> applicationTag = point.ApplicationTag!;
            Assert.That(applicationTag.Value, Is.EqualTo("plant/line-1"));
            Assert.That(applicationTag.StatusCode, Is.EqualTo(StatusCodes.Good));
            AssertProfileReference(point, ObjectTypeIds.IEnergyProfileE0Type, ecmNamespace);
            AssertProfileReference(point, ObjectTypeIds.IEnergyProfileE1Type, ecmNamespace);
            AssertProfileReference(point, ObjectTypeIds.IEnergyProfileD0Type, ecmNamespace);
            Assert.That(provider.ReadCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Each supported profile can be hosted alone without requiring another profile's value or interface.
        /// </summary>
        [TestCase("E0")]
        [TestCase("E1")]
        [TestCase("D0")]
        public async Task EachProfileCanBePublishedIndependently(string profile)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            ushort ecmNamespace = context.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            var acquired = new DateTime(2026, 8, 14, 9, 20, 0, DateTimeKind.Utc);
            EcmMeasurementReading reading;
            uint profileType;
            string measurementName;
            ExpandedNodeId dataType;
            NodeId expectedDataType;
            switch (profile)
            {
                case "E0":
                    measurementName = nameof(BrowseNames.AcCurrentPe);
                    profileType = ObjectTypeIds.IEnergyProfileE0Type;
                    dataType = new ExpandedNodeId(DataTypeIds.AcPeDataType, Namespaces.ECM);
                    expectedDataType = new NodeId(DataTypeIds.AcPeDataType, ecmNamespace);
                    reading = new EcmMeasurementReading(
                        measurementName,
                        Variant.From(new AcPeDataType { L1 = 2f, L2 = 4f, L3 = 6f }),
                        dataType,
                        StatusCodes.Good,
                        acquired);
                    break;
                case "E1":
                    measurementName = nameof(BrowseNames.AcActivePowerTotal);
                    profileType = ObjectTypeIds.IEnergyProfileE1Type;
                    dataType = new ExpandedNodeId(new NodeId(Opc.Ua.DataTypeIds.Float));
                    expectedDataType = new NodeId(Opc.Ua.DataTypeIds.Float);
                    reading = FloatReading(measurementName, 18f, StatusCodes.Good, acquired);
                    break;
                case "D0":
                    measurementName = nameof(BrowseNames.DcCurrent);
                    profileType = ObjectTypeIds.IEnergyProfileD0Type;
                    dataType = new ExpandedNodeId(new NodeId(Opc.Ua.DataTypeIds.Float));
                    expectedDataType = new NodeId(Opc.Ua.DataTypeIds.Float);
                    reading = FloatReading(measurementName, 9f, StatusCodes.Good, acquired);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(profile));
            }

            var provider = new TestMeasurementProvider(Snapshot("standalone/" + profile, reading));
            BaseObjectState parent = await AddParentAsync(manager, "Independent" + profile);
            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("Meter" + profile, manager.InstanceNamespaceIndex),
                provider,
                parent);
            EnergyMeasurementValueState measurement = RequiredMeasurement(
                binding.MeasurementPoint, context, measurementName, ecmNamespace);

            Assert.That(measurement.DataType, Is.EqualTo(expectedDataType));
            Assert.That(measurement.ValueRank, Is.EqualTo(Opc.Ua.ValueRanks.Scalar));
            Assert.That(measurement.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent));
            Assert.That(measurement.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(measurement.Timestamp, Is.EqualTo((DateTimeUtc)acquired));
            AssertProfileReference(binding.MeasurementPoint, profileType, ecmNamespace);
            Assert.That(
                parent.FindChild(context, new QualifiedName("Meter" + profile, manager.InstanceNamespaceIndex)),
                Is.SameAs(binding.MeasurementPoint));
            if (profile == "E0")
            {
                Assert.That(measurement.WrappedValue.TryGetValue(out AcPeDataType? phaseCurrent), Is.True);
                Assert.That(phaseCurrent!.L1, Is.EqualTo(2f));
                Assert.That(phaseCurrent.L2, Is.EqualTo(4f));
                Assert.That(phaseCurrent.L3, Is.EqualTo(6f));
            }
            else
            {
                Assert.That(measurement.WrappedValue.TryGetValue(out float value), Is.True);
                Assert.That(value, Is.EqualTo(profile == "E1" ? 18f : 9f));
            }

            uint[] otherProfiles = profile switch
            {
                "E0" => [ObjectTypeIds.IEnergyProfileE1Type, ObjectTypeIds.IEnergyProfileD0Type],
                "E1" => [ObjectTypeIds.IEnergyProfileE0Type, ObjectTypeIds.IEnergyProfileD0Type],
                _ => [ObjectTypeIds.IEnergyProfileE0Type, ObjectTypeIds.IEnergyProfileE1Type]
            };
            foreach (uint otherProfile in otherProfiles)
            {
                Assert.That(
                    binding.MeasurementPoint.ReferenceExists(
                        Opc.Ua.ReferenceTypeIds.HasInterface,
                        false,
                        new NodeId(otherProfile, ecmNamespace)),
                    Is.False);
            }
        }

        /// <summary>
        /// Explicit refresh updates readings, status, timestamps and the provider-owned tag in place.
        /// </summary>
        [Test]
        public async Task ExplicitRefreshUpdatesValueQualityTimestampAndTagWithoutReplacingNode()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ushort ecmNamespace = manager.SystemContext.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            var firstTime = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            var secondTime = new DateTime(2026, 8, 14, 9, 10, 12, DateTimeKind.Utc);
            DateTimeUtc secondTimestamp = (DateTimeUtc)secondTime;
            var provider = new TestMeasurementProvider(
                Snapshot("meter/initial",
                    FloatReading(nameof(BrowseNames.AcActivePowerTotal), 10f,
                        StatusCodes.Good, firstTime)),
                Snapshot("meter/refreshed",
                    FloatReading(nameof(BrowseNames.AcActivePowerTotal), 12.5f,
                        StatusCodes.UncertainLastUsableValue, secondTime)));
            BaseObjectState parent = await AddParentAsync(manager, "RefreshDevice");

            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("RefreshMeter", manager.InstanceNamespaceIndex),
                provider,
                parent);
            EnergyMeasurementValueState original = RequiredMeasurement(
                binding.MeasurementPoint,
                manager.SystemContext,
                nameof(BrowseNames.AcActivePowerTotal),
                ecmNamespace);

            await binding.RefreshAsync();

            EnergyMeasurementValueState refreshed = RequiredMeasurement(
                binding.MeasurementPoint,
                manager.SystemContext,
                nameof(BrowseNames.AcActivePowerTotal),
                ecmNamespace);
            Assert.That(refreshed, Is.SameAs(original));
            Assert.That(refreshed.WrappedValue.TryGetValue(out float value), Is.True);
            Assert.That(value, Is.EqualTo(12.5f));
            Assert.That(refreshed.StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
            Assert.That(refreshed.Timestamp, Is.EqualTo(secondTimestamp));
            Assert.That(
                binding.MeasurementPoint.ApplicationTag!.Value,
                Is.EqualTo("meter/refreshed"));
            Assert.That(provider.ReadCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Refresh accepts equivalent engineering-unit metadata from a newly allocated provider value.
        /// </summary>
        [Test]
        public async Task RefreshAcceptsEquivalentEngineeringUnitsFromNewInstances()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ushort ecmNamespace = manager.SystemContext.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            var firstUnits = new EUInformation("kW", "kilowatt", "http://www.opcfoundation.org/UA/units/un/cefact")
            {
                UnitId = 0x4B5754
            };
            var nextUnits = new EUInformation("kW", "kilowatt", "http://www.opcfoundation.org/UA/units/un/cefact")
            {
                UnitId = 0x4B5754
            };
            var provider = new TestMeasurementProvider(
                Snapshot("meter/units",
                    FloatReading(nameof(BrowseNames.AcActivePowerTotal), 10f,
                        StatusCodes.Good, new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc))
                    with { EngineeringUnits = firstUnits }),
                Snapshot("meter/units",
                    FloatReading(nameof(BrowseNames.AcActivePowerTotal), 12f,
                        StatusCodes.Good, new DateTime(2026, 8, 14, 9, 10, 12, DateTimeKind.Utc))
                    with { EngineeringUnits = nextUnits }));
            BaseObjectState parent = await AddParentAsync(manager, "EquivalentUnitsDevice");
            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("EquivalentUnitsMeter", manager.InstanceNamespaceIndex),
                provider,
                parent);
            EnergyMeasurementValueState original = RequiredMeasurement(
                binding.MeasurementPoint,
                manager.SystemContext,
                nameof(BrowseNames.AcActivePowerTotal),
                ecmNamespace);

            await binding.RefreshAsync();

            EnergyMeasurementValueState refreshed = RequiredMeasurement(
                binding.MeasurementPoint,
                manager.SystemContext,
                nameof(BrowseNames.AcActivePowerTotal),
                ecmNamespace);
            Assert.That(refreshed, Is.SameAs(original));
            Assert.That(refreshed.WrappedValue.TryGetValue(out float value), Is.True);
            Assert.That(value, Is.EqualTo(12f));
            Assert.That(refreshed.Timestamp, Is.EqualTo((DateTimeUtc)new DateTime(
                2026, 8, 14, 9, 10, 12, DateTimeKind.Utc)));
            Assert.That(provider.ReadCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Published structured values and engineering-unit metadata do not retain provider-owned objects.
        /// </summary>
        [Test]
        public async Task MeasurementPublicationDetachesProviderOwnedValuesAndMetadata()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ushort ecmNamespace = manager.SystemContext.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            var firstTimestamp = new DateTime(2026, 8, 14, 9, 20, 0, DateTimeKind.Utc);
            var secondTimestamp = new DateTime(2026, 8, 14, 9, 20, 1, DateTimeKind.Utc);
            var firstValue = new AcPeDataType { L1 = 1.25f, L2 = 2.5f, L3 = 3.75f };
            var firstUnits = new EUInformation("A", "ampere", "http://www.opcfoundation.org/UA/units/un/cefact")
            {
                UnitId = 0x414D50
            };
            var secondValue = new AcPeDataType { L1 = 4f, L2 = 5f, L3 = 6f };
            var secondUnits = new EUInformation("A", "ampere", "http://www.opcfoundation.org/UA/units/un/cefact")
            {
                UnitId = 0x414D50
            };
            var provider = new TestMeasurementProvider(
                Snapshot("meter/initial",
                    new EcmMeasurementReading(
                        nameof(BrowseNames.AcCurrentPe),
                        Variant.FromStructure(firstValue),
                        new ExpandedNodeId(DataTypeIds.AcPeDataType, Namespaces.ECM),
                        StatusCodes.Good,
                        firstTimestamp,
                        EngineeringUnits: firstUnits)),
                Snapshot("meter/refreshed",
                    new EcmMeasurementReading(
                        nameof(BrowseNames.AcCurrentPe),
                        Variant.FromStructure(secondValue),
                        new ExpandedNodeId(DataTypeIds.AcPeDataType, Namespaces.ECM),
                        StatusCodes.Good,
                        secondTimestamp,
                        EngineeringUnits: secondUnits)));
            BaseObjectState parent = await AddParentAsync(manager, "DetachedMeasurementDevice");
            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("DetachedMeasurementMeter", manager.InstanceNamespaceIndex),
                provider,
                parent);
            EnergyMeasurementValueState measurement = RequiredMeasurement(
                binding.MeasurementPoint,
                manager.SystemContext,
                nameof(BrowseNames.AcCurrentPe),
                ecmNamespace);

            firstValue.L1 = 99f;
            firstUnits.UnitId = 999;
            firstUnits.DisplayName = new LocalizedText("changed");

            Assert.That(measurement.WrappedValue.TryGetValue(out AcPeDataType? initialValue), Is.True);
            Assert.That(initialValue!.L1, Is.EqualTo(1.25f));
            Assert.That(measurement.EngineeringUnits!.Value.UnitId, Is.EqualTo(0x414D50));
            Assert.That(measurement.EngineeringUnits.Value.DisplayName.Text, Is.EqualTo("A"));

            await binding.RefreshAsync();
            secondValue.L2 = 99f;
            secondUnits.UnitId = 998;
            secondUnits.Description = new LocalizedText("changed");

            Assert.That(measurement.WrappedValue.TryGetValue(out AcPeDataType? refreshedValue), Is.True);
            Assert.That(refreshedValue!.L1, Is.EqualTo(4f));
            Assert.That(refreshedValue.L2, Is.EqualTo(5f));
            Assert.That(measurement.EngineeringUnits!.Value.UnitId, Is.EqualTo(0x414D50));
            Assert.That(measurement.EngineeringUnits.Value.Description.Text, Is.EqualTo("ampere"));
            Assert.That(measurement.Timestamp, Is.EqualTo((DateTimeUtc)secondTimestamp));
        }

        /// <summary>
        /// A provider cannot change the published profile set or invariant metadata during refresh.
        /// </summary>
        [TestCase("add-profile")]
        [TestCase("remove-profile")]
        [TestCase("change-metadata")]
        public async Task InvalidRefreshSnapshotRetainsPublishedMeasurement(string change)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ushort ecmNamespace = manager.SystemContext.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            var acquired = new DateTime(2026, 8, 14, 9, 40, 0, DateTimeKind.Utc);
            EcmMeasurementReading originalReading = FloatReading(
                nameof(BrowseNames.AcActivePowerTotal), 10f, StatusCodes.Good, acquired);
            EcmMeasurementReading changedReading = originalReading with { Value = Variant.From(99f) };
            EcmMeasurementSnapshot changedSnapshot = change switch
            {
                "add-profile" => Snapshot("meter/changed",
                    changedReading,
                    FloatReading(nameof(BrowseNames.DcCurrent), 99f, StatusCodes.Good, acquired)),
                "remove-profile" => Snapshot("meter/empty"),
                "change-metadata" => Snapshot("meter/metadata",
                    changedReading with { MeasurementId = 12 }),
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            };
            var provider = new TestMeasurementProvider(
                Snapshot("meter/original", originalReading),
                changedSnapshot);
            BaseObjectState parent = await AddParentAsync(manager, "ImmutableDevice");
            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("ImmutableMeter", manager.InstanceNamespaceIndex),
                provider,
                parent);
            EnergyMeasurementValueState measurement = RequiredMeasurement(
                binding.MeasurementPoint,
                manager.SystemContext,
                nameof(BrowseNames.AcActivePowerTotal),
                ecmNamespace);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await binding.RefreshAsync());

            Assert.That(measurement.WrappedValue.TryGetValue(out float value), Is.True);
            Assert.That(value, Is.EqualTo(10f));
            Assert.That(measurement.Timestamp, Is.EqualTo((DateTimeUtc)acquired));
            Assert.That(measurement.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(
                binding.MeasurementPoint.ApplicationTag!.Value,
                Is.EqualTo("meter/original"));
            Assert.That(
                binding.MeasurementPoint.FindChild(
                    manager.SystemContext,
                    new QualifiedName(nameof(BrowseNames.DcCurrent), ecmNamespace)),
                Is.Null);
            Assert.That(provider.ReadCount, Is.EqualTo(2));
        }

        /// <summary>
        /// A later invalid reading cannot partially publish an earlier reading from the same snapshot.
        /// </summary>
        [Test]
        public async Task InvalidLaterReadingRetainsEarlierMeasurementValueAndTimestamp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ushort ecmNamespace = manager.SystemContext.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            var firstTimestamp = new DateTime(2026, 8, 14, 9, 40, 0, DateTimeKind.Utc);
            var nextTimestamp = new DateTime(2026, 8, 14, 9, 40, 1, DateTimeKind.Utc);
            EcmMeasurementReading initialPower = FloatReading(
                nameof(BrowseNames.AcActivePowerTotal), 10f, StatusCodes.Good, firstTimestamp);
            EcmMeasurementReading initialCurrent = FloatReading(
                nameof(BrowseNames.DcCurrent), 20f, StatusCodes.Good, firstTimestamp);
            var provider = new TestMeasurementProvider(
                Snapshot("meter/original", initialPower, initialCurrent),
                Snapshot("meter/partial",
                    FloatReading(nameof(BrowseNames.AcActivePowerTotal), 99f,
                        StatusCodes.Good, nextTimestamp),
                    FloatReading(nameof(BrowseNames.DcCurrent), 200f,
                        StatusCodes.Good, nextTimestamp) with { MeasurementId = 12 }));
            BaseObjectState parent = await AddParentAsync(manager, "AtomicRefreshDevice");
            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("AtomicRefreshMeter", manager.InstanceNamespaceIndex),
                provider,
                parent);
            EnergyMeasurementValueState power = RequiredMeasurement(
                binding.MeasurementPoint,
                manager.SystemContext,
                nameof(BrowseNames.AcActivePowerTotal),
                ecmNamespace);
            EnergyMeasurementValueState current = RequiredMeasurement(
                binding.MeasurementPoint,
                manager.SystemContext,
                nameof(BrowseNames.DcCurrent),
                ecmNamespace);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await binding.RefreshAsync());

            Assert.That(power.WrappedValue.TryGetValue(out float powerValue), Is.True);
            Assert.That(powerValue, Is.EqualTo(10f));
            Assert.That(power.Timestamp, Is.EqualTo((DateTimeUtc)firstTimestamp));
            Assert.That(power.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(current.WrappedValue.TryGetValue(out float currentValue), Is.True);
            Assert.That(currentValue, Is.EqualTo(20f));
            Assert.That(current.Timestamp, Is.EqualTo((DateTimeUtc)firstTimestamp));
            Assert.That(current.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(
                binding.MeasurementPoint.ApplicationTag!.Value,
                Is.EqualTo("meter/original"));
        }

        /// <summary>
        /// Accuracy domains expose EnumValues and measurements point to the exact declared domain and class.
        /// </summary>
        [Test]
        public async Task AccuracyDomainEnumValuesMatchMeasurementAccuracyMetadata()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ISystemContext context = manager.SystemContext;
            ushort ecmNamespace = context.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            await AddAccuracyDomainsFolderAsync(manager, ecmNamespace);
            ArrayOf<EnumValueType> classes = new EnumValueType[]
            {
                new() { Value = 1, DisplayName = new LocalizedText("Class A") },
                new() { Value = 2, DisplayName = new LocalizedText("Class B") }
            }.ToArrayOf();

            AccuracyDomainState domain = await manager.CreateAccuracyDomainAsync(
                new QualifiedName("MeterAccuracy", ecmNamespace),
                classes);
            var acquired = new DateTime(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);
            var reading = new EcmMeasurementReading(
                nameof(BrowseNames.AcActivePowerTotal),
                Variant.From(8.5f),
                new ExpandedNodeId(new NodeId(Opc.Ua.DataTypeIds.Float)),
                StatusCodes.Good,
                acquired,
                AccuracyClass: 2,
                AccuracyDomain: domain.NodeId,
                AccuracyRange: 0.25f);
            var provider = new TestMeasurementProvider(Snapshot("meter/accuracy", reading));
            BaseObjectState parent = await AddParentAsync(manager, "AccuracyDevice");

            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("AccuracyMeter", manager.InstanceNamespaceIndex),
                provider,
                parent);
            EnergyMeasurementValueState measurement = RequiredMeasurement(
                binding.MeasurementPoint,
                context,
                nameof(BrowseNames.AcActivePowerTotal),
                ecmNamespace);

            Assert.That(domain.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.Organizes));
            Assert.That(domain.EnumValues!.Value.Count, Is.EqualTo(2));
            Assert.That(domain.EnumValues.Value[0]!.Value, Is.EqualTo(1L));
            Assert.That(domain.EnumValues.Value[0]!.DisplayName.Text, Is.EqualTo("Class A"));
            Assert.That(domain.EnumValues.Value[1]!.Value, Is.EqualTo(2L));
            Assert.That(domain.EnumValues.Value[1]!.DisplayName.Text, Is.EqualTo("Class B"));
            Assert.That(ReadProperty<uint>(measurement, context, "AccuracyClass"), Is.EqualTo(2u));
            Assert.That(ReadProperty<NodeId>(measurement, context, "AccuracyDomain"), Is.EqualTo(domain.NodeId));
            Assert.That(ReadProperty<float>(measurement, context, "AccuracyRange"), Is.EqualTo(0.25f));
        }

        /// <summary>
        /// Accuracy-domain publication detaches its EnumValues and localized text from caller-owned objects.
        /// </summary>
        [Test]
        public async Task AccuracyDomainPublicationDetachesCallerOwnedEnumValues()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ushort ecmNamespace = manager.SystemContext.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            await AddAccuracyDomainsFolderAsync(manager, ecmNamespace);
            var sourceValue = new EnumValueType
            {
                Value = 1,
                DisplayName = new LocalizedText("Class A"),
                Description = new LocalizedText("Nominal accuracy")
            };
            ArrayOf<EnumValueType> sourceValues = new[] { sourceValue }.ToArrayOf();

            AccuracyDomainState domain = await manager.CreateAccuracyDomainAsync(
                new QualifiedName("DetachedAccuracyDomain", ecmNamespace),
                sourceValues);

            sourceValue.Value = 2;
            sourceValue.DisplayName = new LocalizedText("Changed class");
            sourceValue.Description = new LocalizedText("Changed description");

            EnumValueType publishedValue = domain.EnumValues!.Value[0]!;
            Assert.That(publishedValue, Is.Not.SameAs(sourceValue));
            Assert.That(publishedValue.Value, Is.EqualTo(1));
            Assert.That(publishedValue.DisplayName.Text, Is.EqualTo("Class A"));
            Assert.That(publishedValue.Description.Text, Is.EqualTo("Nominal accuracy"));
        }

        /// <summary>
        /// Accuracy classes must be declared by the referenced domain before a measurement graph is published.
        /// </summary>
        [Test]
        public async Task UndeclaredAccuracyClassRollsBackMeasurementCreation()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ushort ecmNamespace = manager.SystemContext.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            await AddAccuracyDomainsFolderAsync(manager, ecmNamespace);
            AccuracyDomainState domain = await manager.CreateAccuracyDomainAsync(
                new QualifiedName("SingleClassDomain", ecmNamespace),
                new EnumValueType[]
                {
                    new() { Value = 1, DisplayName = new LocalizedText("Class A") }
                }.ToArrayOf());
            EcmMeasurementReading invalidReading = FloatReading(
                nameof(BrowseNames.AcActivePowerTotal),
                2f,
                StatusCodes.Good,
                new DateTime(2026, 8, 14, 10, 30, 0, DateTimeKind.Utc)) with
            {
                AccuracyClass = 2,
                AccuracyDomain = domain.NodeId
            };
            var provider = new TestMeasurementProvider(Snapshot("meter/invalid-class", invalidReading));
            BaseObjectState parent = await AddParentAsync(manager, "InvalidAccuracyDevice");
            var browseName = new QualifiedName("InvalidAccuracyMeter", manager.InstanceNamespaceIndex);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager.CreateMeasurementAsync(browseName, provider, parent));

            Assert.That(parent.FindChild(manager.SystemContext, browseName), Is.Null);
            Assert.That(provider.ReadCount, Is.EqualTo(1));
            Assert.That(provider.Disposed, Is.False);
        }

        /// <summary>
        /// Accuracy domains reject empty or ambiguous EnumValues before adding a folder child.
        /// </summary>
        [TestCase("empty")]
        [TestCase("duplicate-values")]
        [TestCase("duplicate-names")]
        [TestCase("blank-name")]
        public async Task AccuracyDomainRejectsInvalidEnumValues(string definition)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ushort ecmNamespace = manager.SystemContext.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            await AddAccuracyDomainsFolderAsync(manager, ecmNamespace);
            EnumValueType[] values = definition switch
            {
                "empty" => [],
                "duplicate-values" =>
                [
                    new EnumValueType { Value = 1, DisplayName = new LocalizedText("Class A") },
                    new EnumValueType { Value = 1, DisplayName = new LocalizedText("Class B") }
                ],
                "duplicate-names" =>
                [
                    new EnumValueType { Value = 1, DisplayName = new LocalizedText("Class A") },
                    new EnumValueType { Value = 2, DisplayName = new LocalizedText("Class A") }
                ],
                "blank-name" =>
                [
                    new EnumValueType { Value = 1, DisplayName = new LocalizedText(" ") }
                ],
                _ => throw new ArgumentOutOfRangeException(nameof(definition))
            };
            var browseName = new QualifiedName("InvalidDomain", ecmNamespace);

            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await manager.CreateAccuracyDomainAsync(browseName, values.ToArrayOf()));

            NodeState? folder = manager.FindPredefinedNode(NodeId.Create(
                Opc.Ua.ECM.Objects.AccuracyDomains,
                Namespaces.ECM,
                manager.SystemContext.NamespaceUris));
            Assert.That(folder?.FindChild(manager.SystemContext, browseName), Is.Null);
        }

        /// <summary>
        /// ApplicationTag is persisted before publication, and a failed persistence leaves the published tag unchanged.
        /// </summary>
        [Test]
        public async Task ApplicationTagWritePersistsBeforePublishingAndRetainsValueOnFailure()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            var provider = new TestMeasurementProvider(
                Snapshot("meter/original",
                    FloatReading(nameof(BrowseNames.AcActivePowerTotal), 3f,
                        StatusCodes.Good, new DateTime(2026, 8, 14, 11, 0, 0, DateTimeKind.Utc))));
            BaseObjectState parent = await AddParentAsync(manager, "TagDevice");
            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("TagMeter", manager.InstanceNamespaceIndex),
                provider,
                parent,
                authorize: static (_, _) => new ValueTask<bool>(true));
            PropertyState<string> applicationTag = binding.MeasurementPoint.ApplicationTag!;
            Assert.That(applicationTag.AccessLevel, Is.EqualTo(AccessLevels.CurrentReadOrWrite));
            Assert.That(applicationTag.UserAccessLevel, Is.EqualTo(AccessLevels.CurrentReadOrWrite));
            string? observedBeforePersist = null;
            provider.BeforePersist = _ => observedBeforePersist = applicationTag.Value;

            AttributeWriteResult rejectedType = await applicationTag.OnSimpleWriteValueAsync!(
                manager.SystemContext,
                applicationTag,
                Variant.From(123),
                CancellationToken.None);
            Assert.That(rejectedType.Result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(provider.WriteCount, Is.Zero);
            Assert.That(applicationTag.Value, Is.EqualTo("meter/original"));

            AttributeWriteResult write = await applicationTag.OnSimpleWriteValueAsync!(
                manager.SystemContext,
                applicationTag,
                Variant.From("meter/renamed"),
                CancellationToken.None);

            Assert.That(write.Result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(write.SuppressCacheUpdate, Is.True);
            Assert.That(observedBeforePersist, Is.EqualTo("meter/original"));
            Assert.That(provider.PersistedApplicationTag, Is.EqualTo("meter/renamed"));
            Assert.That(applicationTag.Value, Is.EqualTo("meter/renamed"));

            provider.WriteFailure = new InvalidOperationException("Persistence failed.");
            AttributeWriteResult failedWrite = await applicationTag.OnSimpleWriteValueAsync!(
                manager.SystemContext,
                applicationTag,
                Variant.From("meter/not-persisted"),
                CancellationToken.None);

            Assert.That(failedWrite.Result.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(failedWrite.SuppressCacheUpdate, Is.True);
            Assert.That(provider.PersistedApplicationTag, Is.EqualTo("meter/renamed"));
            Assert.That(applicationTag.Value, Is.EqualTo("meter/renamed"));
            Assert.That(provider.WriteCount, Is.EqualTo(2));
        }

        /// <summary>
        /// A denied ApplicationTag write does not call the provider or change the address-space value.
        /// </summary>
        [Test]
        public async Task ApplicationTagWriteRequiresAuthorization()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            var provider = new TestMeasurementProvider(
                Snapshot("meter/authorized",
                    FloatReading(nameof(BrowseNames.DcCurrent), 4f,
                        StatusCodes.Good, new DateTime(2026, 8, 14, 11, 30, 0, DateTimeKind.Utc))));
            BaseObjectState parent = await AddParentAsync(manager, "DeniedTagDevice");
            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("DeniedTagMeter", manager.InstanceNamespaceIndex),
                provider,
                parent,
                authorize: static (_, _) => new ValueTask<bool>(false));

            AttributeWriteResult write = await binding.MeasurementPoint.ApplicationTag!.OnSimpleWriteValueAsync!(
                manager.SystemContext,
                binding.MeasurementPoint.ApplicationTag,
                Variant.From("meter/denied"),
                CancellationToken.None);

            Assert.That(write.Result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(provider.WriteCount, Is.Zero);
            Assert.That(provider.PersistedApplicationTag, Is.EqualTo("meter/authorized"));
            Assert.That(binding.MeasurementPoint.ApplicationTag.Value, Is.EqualTo("meter/authorized"));
        }

        /// <summary>
        /// ApplicationTag writes recheck snapshot ownership after waiting behind a refresh.
        /// </summary>
        [Test]
        public async Task ApplicationTagWriteRechecksOwnershipAfterQueuedRefresh()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            var provider = new TestMeasurementProvider(
                Snapshot("meter/initial",
                    FloatReading(nameof(BrowseNames.DcCurrent), 4f,
                        StatusCodes.Good, new DateTime(2026, 8, 14, 11, 40, 0, DateTimeKind.Utc))),
                Snapshot("meter/initial",
                    FloatReading(nameof(BrowseNames.DcCurrent), 5f,
                        StatusCodes.Good, new DateTime(2026, 8, 14, 11, 40, 1, DateTimeKind.Utc))));
            BaseObjectState parent = await AddParentAsync(manager, "QueuedTagDevice");
            var locks = new Mock<ILockService>(MockBehavior.Strict);
            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("QueuedTagMeter", manager.InstanceNamespaceIndex),
                provider,
                parent,
                exclusiveAccess: locks.Object,
                authorize: static (_, _) => new ValueTask<bool>(true));
            var readStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.BeforeRead = _ =>
            {
                readStarted.TrySetResult(true);
                return new ValueTask(releaseRead.Task);
            };
            bool ownsLock = true;
            int ownershipChecks = 0;
            var firstOwnershipCheck = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            locks.As<ILockOwnershipService>().Setup(service =>
                service.IsLockOwner(manager.SystemContext, binding.MeasurementPoint.NodeId))
                .Returns(() =>
                {
                    bool ownedAtCheck = ownsLock;
                    if (Interlocked.Increment(ref ownershipChecks) == 1)
                    {
                        firstOwnershipCheck.TrySetResult(true);
                    }
                    return ownedAtCheck;
                });

            Task refresh = binding.RefreshAsync().AsTask();
            await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            PropertyState<string> applicationTag = binding.MeasurementPoint.ApplicationTag!;
            Task<AttributeWriteResult> write = applicationTag.OnSimpleWriteValueAsync!(
                manager.SystemContext,
                applicationTag,
                Variant.From("meter/denied-after-queue"),
                CancellationToken.None).AsTask();
            try
            {
                await firstOwnershipCheck.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.That(write.IsCompleted, Is.False);
                ownsLock = false;
            }
            finally
            {
                releaseRead.TrySetResult(true);
            }

            await refresh.WaitAsync(TimeSpan.FromSeconds(10));
            AttributeWriteResult result = await write.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(result.Result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(provider.WriteCount, Is.Zero);
            Assert.That(provider.PersistedApplicationTag, Is.EqualTo("meter/initial"));
            Assert.That(applicationTag.Value, Is.EqualTo("meter/initial"));
            Assert.That(ownershipChecks, Is.EqualTo(2));
            locks.As<ILockOwnershipService>().Verify(service =>
                service.IsLockOwner(manager.SystemContext, binding.MeasurementPoint.NodeId),
                Times.Exactly(2));
        }

        /// <summary>
        /// Invalid or unsupported initial snapshots are rejected without publishing a partial measurement graph.
        /// </summary>
        [TestCase("null-snapshot")]
        [TestCase("empty")]
        [TestCase("unsupported-name")]
        [TestCase("duplicate-name")]
        [TestCase("wrong-data-type")]
        [TestCase("wrong-value-type")]
        [TestCase("local-timestamp")]
        [TestCase("negative-accuracy-range")]
        [TestCase("nan-accuracy-range")]
        [TestCase("infinite-accuracy-range")]
        [TestCase("missing-accuracy-domain")]
        [TestCase("unknown-accuracy-domain")]
        [TestCase("wrong-structured-value")]
        [TestCase("late-invalid-reading")]
        public async Task InvalidSnapshotsRollBackMeasurementCreation(string snapshotKind)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ushort ecmNamespace = manager.SystemContext.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            var provider = new TestMeasurementProvider(InvalidSnapshot(snapshotKind, ecmNamespace));
            BaseObjectState parent = await AddParentAsync(manager, "RollbackDevice");
            var browseName = new QualifiedName("InvalidMeter", manager.InstanceNamespaceIndex);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager.CreateMeasurementAsync(browseName, provider, parent));
            Assert.That(parent.FindChild(manager.SystemContext, browseName), Is.Null);
            Assert.That(provider.ReadCount, Is.EqualTo(1));
            Assert.That(provider.Disposed, Is.False);
        }

        /// <summary>
        /// A failed provider refresh marks readings unavailable without replacing the last value or acquisition time.
        /// </summary>
        [Test]
        public async Task ProviderRefreshFailureRetainsLastValueAndTimestampAsUnavailable()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            ushort ecmNamespace = manager.SystemContext.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            var acquired = new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);
            DateTimeUtc acquiredTimestamp = (DateTimeUtc)acquired;
            var provider = new TestMeasurementProvider(
                Snapshot("meter/available",
                    FloatReading(nameof(BrowseNames.DcCurrent), 6.25f,
                        StatusCodes.Good, acquired)));
            BaseObjectState parent = await AddParentAsync(manager, "FailureDevice");
            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("FailureMeter", manager.InstanceNamespaceIndex),
                provider,
                parent);
            EnergyMeasurementValueState measurement = RequiredMeasurement(
                binding.MeasurementPoint,
                manager.SystemContext,
                nameof(BrowseNames.DcCurrent),
                ecmNamespace);
            provider.ReadFailure = new InvalidOperationException("Acquisition failed.");

            InvalidOperationException exception = (await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await binding.RefreshAsync()))!;

            Assert.That(exception.Message, Is.EqualTo("Acquisition failed."));
            Assert.That(measurement.WrappedValue.TryGetValue(out float value), Is.True);
            Assert.That(value, Is.EqualTo(6.25f));
            Assert.That(measurement.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(measurement.Timestamp, Is.EqualTo(acquiredTimestamp));
            Assert.That(binding.MeasurementPoint.ApplicationTag!.Value, Is.EqualTo("meter/available"));
            Assert.That(binding.MeasurementPoint.ApplicationTag.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(provider.ReadCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Manager shutdown removes provider callbacks but does not dispose the application-owned provider.
        /// </summary>
        [Test]
        public async Task ManagerDisposesMeasurementBindingWithoutDisposingProvider()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            var provider = new TestMeasurementProvider(
                Snapshot("meter/owned-by-application",
                    FloatReading(nameof(BrowseNames.DcCurrent), 4f,
                        StatusCodes.Good, new DateTime(2026, 8, 14, 14, 0, 0, DateTimeKind.Utc))));
            BaseObjectState parent = await AddParentAsync(manager, "OwnershipDevice");
            EcmMeasurementBinding binding = await manager.CreateMeasurementAsync(
                new QualifiedName("OwnershipMeter", manager.InstanceNamespaceIndex),
                provider,
                parent);
            PropertyState<string> applicationTag = binding.MeasurementPoint.ApplicationTag!;
            Assert.That(applicationTag.OnSimpleWriteValueAsync, Is.Not.Null);

            await manager.DisposeAsync();

            Assert.That(applicationTag.OnSimpleWriteValueAsync, Is.Null);
            Assert.That(provider.Disposed, Is.False);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await binding.RefreshAsync());
        }

        /// <summary>
        /// Creates an in-memory ECM manager without opening a network endpoint.
        /// </summary>
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
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
            server.SetupGet(value => value.Factory).Returns(factory);
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(queues);
            server.SetupGet(value => value.NodeManager).Returns(Mock.Of<IMasterNodeManager>());
            server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            return new EcmNodeManager(server.Object, new ApplicationConfiguration
            {
                ApplicationUri = "urn:ecm-tests:measurement-runtime",
                ApplicationName = "EcmMeasurementRuntime",
                ServerConfiguration = new ServerConfiguration()
            });
        }

        /// <summary>
        /// Adds an isolated application parent to the in-memory address space.
        /// </summary>
        private static async Task<BaseObjectState> AddParentAsync(EcmNodeManager manager, string name)
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId(name, manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName(name, manager.InstanceNamespaceIndex),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            await manager.AddPredefinedNodeAsync(parent);
            return parent;
        }

        /// <summary>
        /// Registers the standard ECM accuracy-domain folder for the manager's creation API.
        /// </summary>
        private static async Task AddAccuracyDomainsFolderAsync(EcmNodeManager manager, ushort ecmNamespace)
        {
            var folder = new FolderState(null)
            {
                NodeId = NodeId.Create(
                    Opc.Ua.ECM.Objects.AccuracyDomains,
                    Namespaces.ECM,
                    manager.SystemContext.NamespaceUris),
                BrowseName = new QualifiedName("AccuracyDomains", ecmNamespace),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.FolderType
            };
            await manager.AddPredefinedNodeAsync(folder);
        }

        /// <summary>
        /// Creates a deterministic Float profile sample.
        /// </summary>
        private static EcmMeasurementReading FloatReading(
            string browseName,
            float value,
            StatusCode statusCode,
            DateTime sourceTimestamp)
        {
            return new EcmMeasurementReading(
                browseName,
                Variant.From(value),
                new ExpandedNodeId(new NodeId(Opc.Ua.DataTypeIds.Float)),
                statusCode,
                sourceTimestamp);
        }

        /// <summary>
        /// Creates a provider snapshot with the requested ordered readings.
        /// </summary>
        private static EcmMeasurementSnapshot Snapshot(
            string applicationTag,
            params EcmMeasurementReading[] readings)
        {
            return new EcmMeasurementSnapshot(applicationTag, readings.ToArrayOf());
        }

        /// <summary>
        /// Resolves a required generated measurement child.
        /// </summary>
        private static EnergyMeasurementValueState RequiredMeasurement(
            EnergyMeasurementState point,
            ISystemContext context,
            string name,
            ushort ecmNamespace)
        {
            return point.FindChild(context, new QualifiedName(name, ecmNamespace))
                as EnergyMeasurementValueState ??
                throw new InvalidOperationException($"Measurement {name} was not published.");
        }

        /// <summary>
        /// Verifies the measurement point advertises the expected standardized interface.
        /// </summary>
        private static void AssertProfileReference(EnergyMeasurementState point, uint typeId, ushort ecmNamespace)
        {
            Assert.That(
                point.ReferenceExists(
                    Opc.Ua.ReferenceTypeIds.HasInterface,
                    false,
                    new NodeId(typeId, ecmNamespace)),
                Is.True);
        }

        /// <summary>
        /// Reads an ECM metadata property without depending on generated property accessors.
        /// </summary>
        private static T ReadProperty<T>(
            EnergyMeasurementValueState measurement,
            ISystemContext context,
            string name)
        {
            var children = new List<BaseInstanceState>();
            measurement.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is BaseVariableState variable &&
                    string.Equals(variable.BrowseName.Name, name, StringComparison.Ordinal) &&
                    variable.WrappedValue.TryGetValue(out T? value))
                {
                    return value!;
                }
            }
            throw new InvalidOperationException($"Measurement property {name} was not published.");
        }

        /// <summary>
        /// Builds one malformed provider snapshot for each validation partition.
        /// </summary>
        private static EcmMeasurementSnapshot? InvalidSnapshot(string kind, ushort ecmNamespace)
        {
            var utc = new DateTime(2026, 8, 14, 13, 0, 0, DateTimeKind.Utc);
            EcmMeasurementReading valid = FloatReading(
                nameof(BrowseNames.AcActivePowerTotal), 1f, StatusCodes.Good, utc);
            return kind switch
            {
                "null-snapshot" => null,
                "empty" => Snapshot("invalid/empty"),
                "unsupported-name" => Snapshot("invalid/name",
                    FloatReading("UnsupportedReading", 1f, StatusCodes.Good, utc)),
                "duplicate-name" => Snapshot("invalid/duplicate",
                    valid, valid with { Value = Variant.From(2f) }),
                "wrong-data-type" => Snapshot("invalid/type",
                    valid with { DataType = new ExpandedNodeId(new NodeId(Opc.Ua.DataTypeIds.Double)) }),
                "wrong-value-type" => Snapshot("invalid/value",
                    valid with { Value = Variant.From(1d) }),
                "local-timestamp" => Snapshot("invalid/time",
                    valid with { SourceTimestamp = DateTime.SpecifyKind(utc, DateTimeKind.Local) }),
                "negative-accuracy-range" => Snapshot("invalid/range",
                    valid with { AccuracyRange = -0.1f }),
                "nan-accuracy-range" => Snapshot("invalid/range",
                    valid with { AccuracyRange = float.NaN }),
                "infinite-accuracy-range" => Snapshot("invalid/range",
                    valid with { AccuracyRange = float.PositiveInfinity }),
                "missing-accuracy-domain" => Snapshot("invalid/domain",
                    valid with { AccuracyClass = 1 }),
                "unknown-accuracy-domain" => Snapshot("invalid/domain",
                    valid with
                    {
                        AccuracyDomain = new NodeId("missing-domain", ecmNamespace)
                    }),
                "wrong-structured-value" => Snapshot("invalid/structured",
                    new EcmMeasurementReading(
                        nameof(BrowseNames.AcCurrentPe),
                        Variant.From(1f),
                        new ExpandedNodeId(DataTypeIds.AcPeDataType, Namespaces.ECM),
                        StatusCodes.Good,
                        utc)),
                "late-invalid-reading" => Snapshot("invalid/late-reading",
                    valid,
                    FloatReading(nameof(BrowseNames.DcCurrent), 2f,
                        StatusCodes.Good, utc) with
                    {
                        DataType = new ExpandedNodeId(new NodeId(Opc.Ua.DataTypeIds.Double))
                    }),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        /// <summary>
        /// Implements deterministic snapshots and a persistent tag without owning address-space nodes.
        /// </summary>
        private sealed class TestMeasurementProvider : IEcmMeasurementProvider, IDisposable
        {
            public TestMeasurementProvider(params EcmMeasurementSnapshot?[] snapshots)
            {
                m_snapshots = new Queue<EcmMeasurementSnapshot?>(snapshots);
                PersistedApplicationTag = snapshots.Length > 0 ? snapshots[0]?.ApplicationTag : null;
            }

            public int ReadCount { get; private set; }

            public int WriteCount { get; private set; }

            public bool Disposed { get; private set; }

            public string? PersistedApplicationTag { get; private set; }

            public Exception? ReadFailure { get; set; }

            public Exception? WriteFailure { get; set; }

            public Action<string>? BeforePersist { get; set; }

            public Func<CancellationToken, ValueTask>? BeforeRead { get; set; }

            public async ValueTask<EcmMeasurementSnapshot> ReadAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadCount++;
                if (ReadFailure is Exception readFailure)
                {
                    throw readFailure;
                }
                if (BeforeRead is { } beforeRead)
                {
                    await beforeRead(cancellationToken).ConfigureAwait(false);
                }
                if (m_snapshots.Count == 0)
                {
                    throw new InvalidOperationException("No provider snapshot is configured.");
                }
                return m_snapshots.Dequeue()!;
            }

            public ValueTask WriteApplicationTagAsync(
                string applicationTag,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteCount++;
                BeforePersist?.Invoke(applicationTag);
                if (WriteFailure is Exception writeFailure)
                {
                    throw writeFailure;
                }
                PersistedApplicationTag = applicationTag;
                return default;
            }

            public void Dispose()
            {
                Disposed = true;
            }

            private readonly Queue<EcmMeasurementSnapshot?> m_snapshots;
        }
    }
}
