/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.IA.Server;

namespace Opc.Ua.IA.Tests.Calibration
{
    /// <summary>
    /// Verifies calibration-target information without a physical calibration procedure.
    /// </summary>
    [TestFixture]
    public sealed class IaCalibrationTargetTests
    {
        /// <summary>
        /// All concrete categories retain their identity and enforce reusable target metadata.
        /// </summary>
        [TestCase(IaCalibrationTargetCategory.Dynamic, ObjectTypes.DynamicCalibrationTargetCategoryType, false)]
        [TestCase(IaCalibrationTargetCategory.OneTime, ObjectTypes.OneTimeCalibrationTargetCategoryType, false)]
        [TestCase(IaCalibrationTargetCategory.Reusable, ObjectTypes.ReusableCalibrationTargetCategoryType, true)]
        [TestCase(IaCalibrationTargetCategory.ReusableDevice,
            ObjectTypes.ReusableDeviceCalibrationTargetCategoryType, true)]
        public async Task CategoriesPublishRequiredMetadata(
            IaCalibrationTargetCategory category, uint typeId, bool reusable)
        {
            var fixture = new IaTestContext();
            IaCalibrationFeature feature = Feature(fixture);
            IaCalibrationTargetConfiguration configuration = Configuration(category, feature);
            IaCalibrationTargetSnapshot snapshot = Snapshot(feature, 25);
            if (reusable)
            {
                snapshot = snapshot with { LastValidationDate = DateSample() };
            }
            var provider = Provider(configuration, snapshot);
            CalibrationTargetState node = fixture.Context.CreateInstanceOfCalibrationTargetType(
                browseName: fixture.Name("Target"));
            NodeId categoryId = node.CalibrationTargetCategory!.NodeId;
            var binding = new IaCalibrationTarget(fixture.Context, node, provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            Assert.That(node.CalibrationTargetCategory.NodeId, Is.EqualTo(categoryId));
            Assert.That(node.CalibrationTargetCategory.TypeDefinitionId,
                Is.EqualTo(NodeId.Create(typeId, Namespaces.IA, fixture.Context.NamespaceUris)));
            Assert.That(node.LastValidationDate != null, Is.EqualTo(reusable));
            Assert.That(node.OperationalConditions != null, Is.EqualTo(reusable));
            Assert.That(node.Identification, Is.Not.Null);
            var identityProperties = fixture.Children(node.Identification)
                .Select(child => child.BrowseName.Name);
            Assert.That(identityProperties, Does.Contain("Manufacturer"));
            Assert.That(identityProperties, Does.Contain("Model"));
            Assert.That(identityProperties, Does.Contain("SerialNumber"));
            BaseVariableState value = fixture.Children(node.CalibrationTargetFeatures!)
                .OfType<BaseVariableState>().Single();
            Assert.That(value.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(value.WrappedValue.TryGetValue(out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(25));
        }

        /// <summary>
        /// Reusable identity and validation date cannot be silently omitted.
        /// </summary>
        [TestCase(IaCalibrationTargetCategory.Reusable)]
        [TestCase(IaCalibrationTargetCategory.ReusableDevice)]
        public void ReusableTargetsRequireManufacturedIdentity(IaCalibrationTargetCategory category)
        {
            var fixture = new IaTestContext();
            var builder = new IaCalibrationTargetBuilder(category, new IaCalibrationIdentification())
                .WithFeature(Feature(fixture));
            Assert.That(() => builder.Build(), Throws.ArgumentException);
        }

        /// <summary>
        /// Publishes units, resolution, status, timestamps and optional URI information without fetching it.
        /// </summary>
        [TestCase((byte)0)]
        [TestCase((byte)100)]
        public async Task CalibrationReadingsPreserveNativeMetadata(byte quality)
        {
            var fixture = new IaTestContext();
            IaCalibrationFeature feature = Feature(fixture) with
            { Kind = IaCalibrationFeatureKind.CapacityRange, Resolution = 0.1 };
            IaCalibrationTargetConfiguration configuration =
                Configuration(IaCalibrationTargetCategory.Reusable, feature)
                with
            { OptionalFields = IaCalibrationTargetFields.Quality | IaCalibrationTargetFields.CertificateUri };
            var reading = new DataValue(Variant.FromStructure(new Range { Low = 10, High = 40 }),
                StatusCodes.UncertainLastUsableValue, IaTestContext.Timestamp);
            IaCalibrationTargetSnapshot snapshot = new([new IaCalibrationReading(feature.BrowseName, reading)])
            {
                LastValidationDate = DateSample(),
                Quality = new IaCalibrationSample<byte>(quality, StatusCodes.Good, IaTestContext.Timestamp),
                CertificateUri = new IaCalibrationSample<string>(
                    "https://example.invalid/calibration/certificate", StatusCodes.Good, IaTestContext.Timestamp)
            };
            CalibrationTargetState node = fixture.Context.CreateInstanceOfCalibrationTargetType(
                browseName: fixture.Name("Target"));
            var binding = new IaCalibrationTarget(fixture.Context, node, Provider(configuration, snapshot).Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            CapacityRangeState value = fixture.Children(node.CalibrationTargetFeatures!)
                .OfType<CapacityRangeState>().Single();
            Assert.That(value.WrappedValue.TryGetStructure<Range>(out Range? range), Is.True);
            Assert.That(range!.Low, Is.EqualTo(10));
            Assert.That(range.High, Is.EqualTo(40));
            Assert.That(value.Resolution!.Value, Is.EqualTo(0.1));
            Assert.That(value.EngineeringUnits!.Value!.DisplayName, Is.EqualTo(new LocalizedText("mm")));
            Assert.That(value.StatusCode, Is.EqualTo(reading.StatusCode));
            Assert.That(value.Timestamp, Is.EqualTo(IaTestContext.Timestamp));
            Assert.That(node.Quality!.Value, Is.EqualTo(quality));
            Assert.That(node.CertificateUri!.Value, Is.EqualTo(snapshot.CertificateUri!.Value));
        }

        /// <summary>
        /// Invalid range bounds, optional quality and acquisition metadata leave the previous cache untouched.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task InvalidCalibrationSnapshotLeavesPreviousValues(int invalid)
        {
            var fixture = new IaTestContext();
            IaCalibrationFeature feature = Feature(fixture);
            IaCalibrationTargetConfiguration configuration =
                Configuration(IaCalibrationTargetCategory.Reusable, feature)
                with
            { OptionalFields = IaCalibrationTargetFields.Quality };
            IaCalibrationTargetSnapshot snapshot = Snapshot(feature, 10) with
            {
                LastValidationDate = DateSample(),
                Quality = new IaCalibrationSample<byte>(50, StatusCodes.Good, IaTestContext.Timestamp)
            };
            var provider = Provider(configuration, snapshot);
            provider.Setup(source => source.GetSnapshotAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<IaCalibrationTargetSnapshot>(snapshot));
            CalibrationTargetState node = fixture.Context.CreateInstanceOfCalibrationTargetType(
                browseName: fixture.Name("Target"));
            var binding = new IaCalibrationTarget(fixture.Context, node, provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            snapshot = Snapshot(feature, 99) with
            {
                LastValidationDate = invalid == 0 ? null : DateSample(),
                Quality = invalid switch
                {
                    1 => new IaCalibrationSample<byte>(101, StatusCodes.Good, IaTestContext.Timestamp),
                    2 => new IaCalibrationSample<byte>(50, StatusCodes.Good, default),
                    _ => new IaCalibrationSample<byte>(50, StatusCodes.Good, IaTestContext.Timestamp)
                }
            };
            if (invalid == 3)
            {
                snapshot = snapshot with
                { Features = [new IaCalibrationReading(feature.BrowseName, new DataValue(Variant.From(99.0)))] };
            }
            await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                Throws.InstanceOf<ArgumentException>());
            BaseVariableState value = fixture.Children(node.CalibrationTargetFeatures!)
                .OfType<BaseVariableState>().Single();
            Assert.That(value.WrappedValue, Is.EqualTo(Variant.From(10.0)));
            Assert.That(node.Quality!.Value, Is.EqualTo(50));
        }

        /// <summary>
        /// A missing numeric value is published only with an explicit bad source quality and acquisition time.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, true)]
        public async Task MissingCalibrationValuesRequireBadQuality(int qualityCase, bool accepted)
        {
            StatusCode status = qualityCase switch
            {
                0 => StatusCodes.Good,
                1 => StatusCodes.UncertainLastUsableValue,
                _ => StatusCodes.BadNoData
            };
            var fixture = new IaTestContext();
            IaCalibrationFeature feature = Feature(fixture);
            IaCalibrationTargetConfiguration configuration =
                Configuration(IaCalibrationTargetCategory.Dynamic, feature);
            IaCalibrationTargetSnapshot snapshot = Snapshot(feature, 12);
            var provider = Provider(configuration, snapshot);
            provider.Setup(source => source.GetSnapshotAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<IaCalibrationTargetSnapshot>(snapshot));
            CalibrationTargetState node = fixture.Context.CreateInstanceOfCalibrationTargetType(
                browseName: fixture.Name("Target"));
            var binding = new IaCalibrationTarget(fixture.Context, node, provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            snapshot = new IaCalibrationTargetSnapshot(
                [new IaCalibrationReading(feature.BrowseName,
                    new DataValue(Variant.Null, status, IaTestContext.Timestamp))]);
            BaseVariableState value = fixture.Children(node.CalibrationTargetFeatures!)
                .OfType<BaseVariableState>().Single();
            if (!accepted)
            {
                await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                    Throws.ArgumentException);
                Assert.That(value.WrappedValue, Is.EqualTo(Variant.From(12.0)));
                return;
            }
            await binding.RefreshAsync().ConfigureAwait(false);
            Assert.That(value.WrappedValue.IsNull, Is.True);
            Assert.That(value.StatusCode, Is.EqualTo(status));
            Assert.That(value.Timestamp, Is.EqualTo(IaTestContext.Timestamp));
        }

        /// <summary>
        /// Rejects invalid resolution, ranks and duplicate feature names at configuration time.
        /// </summary>
        [Test]
        public void InvalidFeatureConfigurationIsRejected()
        {
            var fixture = new IaTestContext();
            IaCalibrationFeature feature = Feature(fixture);
            var identity = new IaCalibrationIdentification();
            Assert.That(() => new IaCalibrationTargetBuilder(IaCalibrationTargetCategory.Dynamic, identity)
                .WithFeature(feature with { Kind = IaCalibrationFeatureKind.CapacityRange, Resolution = 0 }).Build(),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(() => new IaCalibrationTargetBuilder(IaCalibrationTargetCategory.Dynamic, identity)
                .WithFeature(feature with { ValueRank = ValueRanks.ScalarOrOneDimension }).Build(),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(() => new IaCalibrationTargetBuilder(IaCalibrationTargetCategory.Dynamic, identity)
                .WithFeature(feature).WithFeature(feature).Build(), Throws.ArgumentException);
        }

        /// <summary>
        /// Numeric quantities retain their scalar, array or matrix representation rather than becoming strings.
        /// </summary>
        [TestCase(ValueRanks.Scalar)]
        [TestCase(ValueRanks.OneDimension)]
        [TestCase(2)]
        public async Task NumericFeaturesRetainTheirNativeRank(int rank)
        {
            var fixture = new IaTestContext();
            IaCalibrationFeature feature = Feature(fixture) with { ValueRank = rank };
            ArrayOf<double> elements = [10, 20, 30, 40];
            Variant expected = rank switch
            {
                ValueRanks.Scalar => Variant.From(10.0),
                ValueRanks.OneDimension => Variant.From(elements),
                _ => Variant.From(elements.ToMatrix(2, 2))
            };
            IaCalibrationTargetSnapshot snapshot = new([new IaCalibrationReading(feature.BrowseName,
                new DataValue(expected, StatusCodes.UncertainLastUsableValue, IaTestContext.Timestamp))]);
            CalibrationTargetState node = fixture.Context.CreateInstanceOfCalibrationTargetType(
                browseName: fixture.Name("Target"));
            var binding = new IaCalibrationTarget(fixture.Context, node,
                Provider(Configuration(IaCalibrationTargetCategory.Dynamic, feature), snapshot).Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            BaseVariableState actual = fixture.Children(node.CalibrationTargetFeatures!)
                .OfType<BaseVariableState>().Single();
            Assert.That(actual.ValueRank, Is.EqualTo(rank));
            Assert.That(actual.WrappedValue.TypeInfo.ValueRank, Is.EqualTo(rank));
            Assert.That(actual.WrappedValue, Is.EqualTo(expected));
            Assert.That(actual.StatusCode, Is.EqualTo((StatusCode)StatusCodes.UncertainLastUsableValue));
        }

        /// <summary>
        /// Range endpoints are finite and inclusive; reversed endpoints cannot replace the last valid range.
        /// </summary>
        [TestCase(10.0, 10.0, true)]
        [TestCase(10.0, 40.0, true)]
        [TestCase(40.0, 10.0, false)]
        [TestCase(double.NaN, 40.0, false)]
        [TestCase(10.0, double.PositiveInfinity, false)]
        public async Task CapacityRangesRequireFiniteOrderedEndpoints(double low, double high, bool valid)
        {
            var fixture = new IaTestContext();
            IaCalibrationFeature feature = Feature(fixture) with
            { Kind = IaCalibrationFeatureKind.CapacityRange, Resolution = 0.1 };
            IaCalibrationTargetSnapshot snapshot = new([new IaCalibrationReading(feature.BrowseName,
                new DataValue(Variant.FromStructure(new Range { Low = low, High = high }),
                    StatusCodes.Good, IaTestContext.Timestamp))]);
            CalibrationTargetState node = fixture.Context.CreateInstanceOfCalibrationTargetType(
                browseName: fixture.Name("Target"));
            var binding = new IaCalibrationTarget(fixture.Context, node,
                Provider(Configuration(IaCalibrationTargetCategory.Dynamic, feature), snapshot).Object);
            BaseVariableState variable = fixture.Children(node.CalibrationTargetFeatures!)
                .OfType<BaseVariableState>().Single();
            Variant previous = variable.WrappedValue;
            if (!valid)
            {
                await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                    Throws.InstanceOf<ArgumentException>());
                Assert.That(variable.WrappedValue, Is.EqualTo(previous));
                Assert.That(variable.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadWaitingForInitialData));
                return;
            }
            await binding.RefreshAsync().ConfigureAwait(false);
            Assert.That(variable.WrappedValue.TryGetStructure<Range>(out Range? published), Is.True);
            Assert.That(published!.Low, Is.EqualTo(low));
            Assert.That(published.High, Is.EqualTo(high));
        }

        /// <summary>
        /// Cancelled acquisition and overlapping refreshes do not reorder the provider's absolute images.
        /// </summary>
        [Test]
        public async Task CancelledAndConcurrentRefreshesPreserveProviderOrdering()
        {
            var fixture = new IaTestContext();
            IaCalibrationFeature feature = Feature(fixture);
            IaCalibrationTargetSnapshot snapshot = Snapshot(feature, 25);
            var provider = Provider(Configuration(IaCalibrationTargetCategory.Dynamic, feature), snapshot);
            CalibrationTargetState node = fixture.Context.CreateInstanceOfCalibrationTargetType(
                browseName: fixture.Name("Target"));
            var binding = new IaCalibrationTarget(fixture.Context, node, provider.Object);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThatAsync(async () => await binding.RefreshAsync(cancelled.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            provider.Verify(source => source.GetSnapshotAsync(It.IsAny<CancellationToken>()), Times.Never);
            var pending = new TaskCompletionSource<IaCalibrationTargetSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.Setup(source => source.GetSnapshotAsync(It.IsAny<CancellationToken>())).Returns(() =>
            {
                entered.SetResult(true);
                return new ValueTask<IaCalibrationTargetSnapshot>(pending.Task);
            });
            Task first = binding.RefreshAsync().AsTask();
            await entered.Task.ConfigureAwait(false);
            await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                Throws.InvalidOperationException);
            pending.SetResult(snapshot);
            await first.ConfigureAwait(false);
            BaseVariableState variable = fixture.Children(node.CalibrationTargetFeatures!)
                .OfType<BaseVariableState>().Single();
            Assert.That(variable.WrappedValue, Is.EqualTo(Variant.From(25.0)));
            provider.Setup(source => source.GetSnapshotAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Storage unavailable"));
            await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                Throws.InvalidOperationException);
            Assert.That(variable.WrappedValue, Is.EqualTo(Variant.From(25.0)));
            provider.Setup(source => source.GetSnapshotAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Snapshot(feature, 30));
            await binding.RefreshAsync().ConfigureAwait(false);
            Assert.That(variable.WrappedValue, Is.EqualTo(Variant.From(30.0)));
        }

        /// <summary>
        /// Reference-instrument relationships and optional operational conditions use the published namespaces.
        /// </summary>
        [Test]
        public async Task DynamicTargetsPublishReferenceInstrumentsAndConditions()
        {
            var fixture = new IaTestContext();
            IaCalibrationFeature feature = Feature(fixture);
            IaCalibrationFeature condition = feature with { BrowseName = fixture.Name("Temperature") };
            NodeId instrument = fixture.Owner("Instrument").NodeId;
            IaCalibrationTargetConfiguration configuration =
                new IaCalibrationTargetBuilder(IaCalibrationTargetCategory.Dynamic, new IaCalibrationIdentification())
                    .WithFeature(feature).WithOperationalCondition(condition)
                    .WithReferenceMeasurementInstrument(instrument)
                    .WithOptionalFields(IaCalibrationTargetFields.NextValidationDate).Build();
            IaCalibrationTargetSnapshot snapshot = Snapshot(feature, 25) with
            {
                OperationalConditions = [new IaCalibrationReading(condition.BrowseName,
                    new DataValue(Variant.From(20.0), StatusCodes.Good, IaTestContext.Timestamp))],
                NextValidationDate = DateSample()
            };
            CalibrationTargetState node = fixture.Context.CreateInstanceOfCalibrationTargetType(
                browseName: fixture.Name("Target"));
            var binding = new IaCalibrationTarget(fixture.Context, node, Provider(configuration, snapshot).Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            Assert.That(node.CalibrationTargetCategory!.ReferenceExists(
                ExpandedNodeId.ToNodeId(ReferenceTypeIds.HasReferenceMeasurementInstrument,
                    fixture.Context.NamespaceUris), false, instrument), Is.True);
            BaseVariableState variable = fixture.Children(node.OperationalConditions!)
                .OfType<BaseVariableState>().Single();
            Assert.That(variable.BrowseName, Is.EqualTo(condition.BrowseName));
            Assert.That(variable.WrappedValue, Is.EqualTo(Variant.From(20.0)));
            Assert.That(node.NextValidationDate!.Value, Is.EqualTo(IaTestContext.Timestamp));
        }

        /// <summary>
        /// Creates one numeric feature with exact engineering-unit metadata.
        /// </summary>
        private static IaCalibrationFeature Feature(IaTestContext fixture)
        {
            return new IaCalibrationFeature(fixture.Name("Length"), IaCalibrationFeatureKind.CalibrationValue,
                new IaCalibrationUnit("http://www.opcfoundation.org/UA/units/un/cefact", 5066068,
                    new LocalizedText("mm")));
        }

        /// <summary>
        /// Creates a category configuration with a manufactured identity.
        /// </summary>
        private static IaCalibrationTargetConfiguration Configuration(
            IaCalibrationTargetCategory category, IaCalibrationFeature feature)
        {
            return new IaCalibrationTargetBuilder(category, new IaCalibrationIdentification
            {
                Manufacturer = new LocalizedText("Test Manufacturer"),
                Model = new LocalizedText("Reference Target"),
                SerialNumber = "TEST-001"
            }).WithFeature(feature).Build();
        }

        /// <summary>
        /// Creates one absolute feature snapshot.
        /// </summary>
        private static IaCalibrationTargetSnapshot Snapshot(IaCalibrationFeature feature, double value)
        {
            return new IaCalibrationTargetSnapshot([new IaCalibrationReading(feature.BrowseName,
                new DataValue(Variant.From(value), StatusCodes.Good, IaTestContext.Timestamp))]);
        }

        /// <summary>
        /// Creates deterministic validation-date metadata.
        /// </summary>
        private static IaCalibrationSample<DateTimeUtc> DateSample()
        {
            return new IaCalibrationSample<DateTimeUtc>(
                IaTestContext.Timestamp, StatusCodes.Good, IaTestContext.Timestamp);
        }

        /// <summary>
        /// Creates a provider whose lifetime remains with the application.
        /// </summary>
        private static Mock<IIaCalibrationTargetProvider> Provider(
            IaCalibrationTargetConfiguration configuration, IaCalibrationTargetSnapshot snapshot)
        {
            var provider = new Mock<IIaCalibrationTargetProvider>();
            provider.SetupGet(source => source.Configuration).Returns(configuration);
            provider.Setup(source => source.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
            return provider;
        }
    }
}
