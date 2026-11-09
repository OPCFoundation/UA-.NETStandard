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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Tests boundary, invalid-type and fail-before-publication partitions through the public APIs.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class WmtpValidationTests
    {
        /// <summary>
        /// Gets independent invalid measurement definitions, one observable invariant per case.
        /// </summary>
        public static IEnumerable<TestCaseData> InvalidDefinitions
        {
            get
            {
                WmtpMeasurementDefinition valid = WmtpTestEquipment.Definition();
                yield return new TestCaseData(valid with { Name = string.Empty })
                    .SetName("WmtpDefinitionRejectsEmptyName");
                yield return new TestCaseData(valid with { Name = new string('x', 257) })
                    .SetName("WmtpDefinitionRejectsOversizedName");
                yield return new TestCaseData(valid with { Kind = (WmtpMeasurementKind)8 })
                    .SetName("WmtpDefinitionRejectsUnknownMeasurement");
                yield return new TestCaseData(valid with { SampleKind = (WmtpSampleKind)7 })
                    .SetName("WmtpDefinitionRejectsUnknownSample");
                yield return new TestCaseData(valid with { DataType = BuiltInType.Boolean })
                    .SetName("WmtpDefinitionRejectsNonnumericType");
                yield return new TestCaseData(valid with { Features = (WmtpMeasurementFeatures)32 })
                    .SetName("WmtpDefinitionRejectsUndefinedFeatures");
                yield return new TestCaseData(valid with { EngineeringUnits = new EUInformation() })
                    .SetName("WmtpDefinitionRejectsUnspecifiedUnits");
                yield return new TestCaseData(valid with { EURange = new Range { Low = 2, High = 1 } })
                    .SetName("WmtpDefinitionRejectsReversedRange");
                yield return new TestCaseData(valid with { InstrumentRange = new Range { Low = 0, High = 5 } })
                    .SetName("WmtpDefinitionRejectsNominalRangeOutsideInstrument");
                yield return new TestCaseData(valid with { ValuePrecision = -1 })
                    .SetName("WmtpDefinitionRejectsNegativePrecision");
                yield return new TestCaseData(valid with { RelativeUncertainty = double.NaN })
                    .SetName("WmtpDefinitionRejectsNonfiniteRelativeUncertainty");
                yield return new TestCaseData(valid with { AbsoluteUncertainty = -1 })
                    .SetName("WmtpDefinitionRejectsNegativeAbsoluteUncertainty");
                yield return new TestCaseData(valid with { InternalUpdateInterval = double.PositiveInfinity })
                    .SetName("WmtpDefinitionRejectsNonfiniteUpdateInterval");
                yield return new TestCaseData(valid with { StaleAfter = TimeSpan.Zero })
                    .SetName("WmtpDefinitionRejectsZeroFreshnessWindow");
                yield return new TestCaseData(valid with { High = double.NaN })
                    .SetName("WmtpDefinitionRejectsNonfiniteHigh");
                yield return new TestCaseData(valid with { HighHigh = double.PositiveInfinity })
                    .SetName("WmtpDefinitionRejectsNonfiniteHighHigh");
                yield return new TestCaseData(valid with { High = 91, HighHigh = 90 })
                    .SetName("WmtpDefinitionRejectsReversedLimits");
                yield return new TestCaseData(valid with { TriggerMilliseconds = 0 })
                    .SetName("WmtpDefinitionRejectsBothZeroTriggers");
                yield return new TestCaseData(valid with { Delta = Variant.From(-1d) })
                    .SetName("WmtpDefinitionRejectsNegativeDelta");
                yield return new TestCaseData(valid with { Delta = Variant.From(1u) })
                    .SetName("WmtpDefinitionRejectsMismatchedDeltaType");
                yield return new TestCaseData(valid with { Delta = Variant.From(new double[] { 1 }.ToArrayOf()) })
                    .SetName("WmtpDefinitionRejectsArrayDelta");
                yield return new TestCaseData(valid with { Definition = new string('x', 4097) })
                    .SetName("WmtpDefinitionRejectsOversizedDescription");
            }
        }

        /// <summary>
        /// Invalid sensor definitions fail before accessing the model, equipment or allocator.
        /// </summary>
        [TestCaseSource(nameof(InvalidDefinitions))]
        public void DefinitionValidationRejectsEachInvalidPartition(WmtpMeasurementDefinition definition)
        {
            var context = new Mock<IMachineryBuildContext>(MockBehavior.Strict);
            context.SetupGet(build => build.InstanceNamespaceIndex).Returns(3);
            var builder = new WmtpPeripheralBuilder(context.Object, "Definition");
            Assert.That(() => builder.AddMeasurement(definition), Throws.TypeOf<ServiceResultException>());
        }

        /// <summary>
        /// Bounds and null collaborators are validated without hidden provider discovery or allocation.
        /// </summary>
        [Test]
        public void BuilderAndStoreConstructorsRejectInvalidInputs()
        {
            var context = new Mock<IMachineryBuildContext>(MockBehavior.Strict);
            context.SetupGet(build => build.InstanceNamespaceIndex).Returns(3);
            Assert.That(() => new WmtpPeripheralBuilder(null!, "name"), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new WmtpPeripheralBuilder(context.Object, string.Empty),
                Throws.TypeOf<ArgumentException>());
            var builder = new WmtpPeripheralBuilder(context.Object, "name");
            Assert.That(() => builder.WithProvider(null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => builder.WithAuthorization(null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => builder.WithMeasurementProvider(null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => builder.WithCalibrationProvider(null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => builder.WithDeviceTimeProvider(null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => builder.WithTransferProvider(null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => builder.WithIdentification(null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => builder.WithClock(null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => builder.WithOwnedResource(null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => builder.WithFeatures((WmtpFeatures)512), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => builder.WithByteLimits(0, 256), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => builder.WithByteLimits(1, 255), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => builder.WithWorkRecords(null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => builder.WithServiceRecords(null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => builder.WithWorkRecords(Mock.Of<IWmtpRecordStore>(), (WmtpRecordFeatures)256),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new InMemoryWmtpRecordStore(null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create(), retention: TimeSpan.Zero),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            builder.AddMeasurement(WmtpTestEquipment.Definition());
            Assert.That(() => builder.AddMeasurement(WmtpTestEquipment.Definition()),
                Throws.TypeOf<ServiceResultException>());
        }

        /// <summary>
        /// All ten scalar numeric types are preserved; every released sample-category value is accepted.
        /// </summary>
        [Test]
        public async Task AllScalarNumericTypesAndSampleCategoriesRemainExactAsync()
        {
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var equipment = new WmtpTestEquipment();
            Variant[] values =
            [
                Variant.From((sbyte)1), Variant.From((byte)2), Variant.From((short)3), Variant.From((ushort)4),
                Variant.From(5), Variant.From(6u), Variant.From(7L),
                Variant.From(8ul), Variant.From(9f), Variant.From(10d)
            ];
            WmtpPeripheralBuilder builder = equipment.Builder(fixture.Context);
            for (int index = 0; index < values.Length; index++)
            {
                string name = $"Value{index}";
                equipment.Samples[name] = equipment.Sample(values[index]);
                builder.AddMeasurement(WmtpTestEquipment.Definition() with
                {
                    Name = name, DataType = values[index].TypeInfo.BuiltInType,
                    Delta = values[index], SampleKind = (WmtpSampleKind)(index % 7)
                });
            }
            await using WmtpPeripheral peripheral = await builder.BuildAsync().ConfigureAwait(false);
            for (int index = 0; index < values.Length; index++)
            {
                Assert.That(peripheral.Measurements[index].Sample.WrappedValue, Is.EqualTo(values[index]));
                Assert.That(peripheral.Measurements[index].Sample.WrappedValue.TypeInfo.BuiltInType,
                    Is.EqualTo(values[index].TypeInfo.BuiltInType));
                Assert.That(peripheral.Measurements[index].State.TypeOfSample!.Value, Is.EqualTo((uint)(index % 7)));
                await peripheral.Measurements[index].SetDeltaAsync(values[index], new WmtpRequest())
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Failed acquisition validation changes neither an earlier sensor nor the device metadata.
        /// </summary>
        [Test]
        public async Task InvalidLaterAcquisitionDoesNotPartiallyPublishEarlierSensorsAsync()
        {
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var equipment = new WmtpTestEquipment();
            equipment.Samples["First"] = equipment.Sample(Variant.From(1d));
            equipment.Samples["Last"] = equipment.Sample(Variant.From(2d));
            await using WmtpPeripheral peripheral = await equipment.Builder(fixture.Context)
                .AddMeasurement(WmtpTestEquipment.Definition() with { Name = "First" })
                .AddMeasurement(WmtpTestEquipment.Definition() with { Name = "Last" })
                .BuildAsync().ConfigureAwait(false);
            equipment.Samples["First"] = equipment.Sample(Variant.From(99d));
            foreach (WmtpMeasurementSample invalid in new WmtpMeasurementSample[]
            {
                new() { Value = DataValue.Null },
                new() { Value = new DataValue(Variant.Null, StatusCodes.Good) },
                equipment.Sample(Variant.From(true)),
                equipment.Sample(Variant.From("invalid")),
                equipment.Sample(Variant.From(double.NaN)),
                new() { Value = new DataValue(Variant.From(3d)) },
                equipment.Sample(Variant.From(3d)) with { MeasurementPeriod = -1 }
            })
            {
                equipment.Samples["Last"] = invalid;
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await peripheral.RefreshAsync().ConfigureAwait(false));
                Assert.That(peripheral.Measurements[0].Sample.WrappedValue, Is.EqualTo(Variant.From(1d)));
                Assert.That(peripheral.Measurements[1].Sample.WrappedValue, Is.EqualTo(Variant.From(2d)));
            }
        }

        /// <summary>
        /// Explicitly selected observations cannot silently default to valid-looking device state.
        /// </summary>
        [TestCase(WmtpFeatures.Calibration)]
        [TestCase(WmtpFeatures.Battery)]
        [TestCase(WmtpFeatures.License)]
        [TestCase(WmtpFeatures.ServiceInformation)]
        [TestCase(WmtpFeatures.AdvancedServiceInformation)]
        public async Task MissingSelectedObservationsRejectSetupWithoutPublishingAsync(WmtpFeatures feature)
        {
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var equipment = new WmtpTestEquipment();
            WmtpPeripheralBuilder builder = equipment.Builder(fixture.Context).WithFeatures(feature);
            await Assert.ThrowsAsync<ServiceResultException>(async () => await builder.BuildAsync().ConfigureAwait(false));
            equipment.FullSnapshot();
            await using WmtpPeripheral peripheral = await builder.BuildAsync().ConfigureAwait(false);
            Assert.That(peripheral.Features, Is.EqualTo(feature));
        }

        /// <summary>
        /// Work and service stores must be independent, and disabled optional methods remain unavailable.
        /// </summary>
        [Test]
        public async Task DatasetSharingAndUnselectedMethodsAreExplicitlyRejectedAsync()
        {
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var equipment = new WmtpTestEquipment();
            await using var store = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create());
            WmtpPeripheralBuilder shared = equipment.Builder(fixture.Context)
                .WithWorkRecords(store).WithServiceRecords(store);
            await Assert.ThrowsAsync<ServiceResultException>(async () => await shared.BuildAsync().ConfigureAwait(false));
            await using WmtpPeripheral peripheral = await equipment.Builder(fixture.Context).WithWorkRecords(store)
                .BuildAsync().ConfigureAwait(false);
            var index = new WmtpRecordQuery { Selection = WmtpRecordSelection.Index };
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await peripheral.WorkRecords!.ReadAsync(index, new WmtpRequest()).ConfigureAwait(false));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await peripheral.WorkRecords!.CountAsync(index, new WmtpRequest()).ConfigureAwait(false));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await peripheral.WorkRecords!.DeleteAsync(new WmtpRecordQuery(), new WmtpRequest())
                    .ConfigureAwait(false));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await peripheral.SetCalibrationAsync(0, new WmtpRequest()).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await peripheral.WorkRecords!.CountAsync(new WmtpRecordQuery(), null!).ConfigureAwait(false));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await peripheral.WorkRecords!.CountAsync(new WmtpRecordQuery(), new WmtpRequest(), cancelled.Token)
                    .ConfigureAwait(false));
        }
    }
}
