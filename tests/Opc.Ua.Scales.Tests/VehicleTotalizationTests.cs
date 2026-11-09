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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Scales.Server.Builders;
using Opc.Ua.Scales.Server.Runtime;

namespace Opc.Ua.Scales.Tests
{
    /// <summary>
    /// Per-vehicle aggregation of completed OPC 40200 weighing transactions.
    /// </summary>
    [TestFixture]
    [Category("Scales")]
    [NonParallelizable]
    public sealed class VehicleTotalizationTests
    {
        /// <summary>
        /// Starts an isolated server fixture.
        /// </summary>
        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new ScalesServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Disposes the server fixture.
        /// </summary>
        [TearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Inbound and live readings do not complete a transaction; both delta conventions remain unchanged.
        /// </summary>
        [TestCase(VehicleWeightAggregation.Signed, 15000, 33000)]
        [TestCase(VehicleWeightAggregation.Absolute, 35000, 53000)]
        public async Task CompletedTransactionsTotalizeSignedOrAbsoluteAsync(
            VehicleWeightAggregation aggregation,
            double mixedTotal,
            double onePassTotal)
        {
            ScaleHandle scale = await CreateAsync(_ => aggregation).ConfigureAwait(false);
            VehicleController controller = scale.Vehicle!;
            var vehicle = (VehicleProductState)scale.ProductionPreset!.Find("A")!;
            DateTimeUtc resetDate = vehicle.TotalWeightResetDate!.Value;
            AssertTotal(vehicle, 0);

            scale.PublishLoad(20000);
            AssertGood(await controller.InboundWeighingAsync("A").ConfigureAwait(false));
            AssertTotal(vehicle, 0);
            scale.PublishLoad(25000);
            AssertTotal(vehicle, 0);
            scale.PublishLoad(30000);
            AssertGood(await controller.OutboundWeighingAsync("A").ConfigureAwait(false));
            Assert.That(vehicle.DeltaWeight!.Value.Net, Is.EqualTo(-10000));
            AssertTotal(vehicle, aggregation == VehicleWeightAggregation.Signed ? -10000 : 10000);

            scale.PublishLoad(40000);
            AssertGood(await controller.InboundWeighingAsync("A").ConfigureAwait(false));
            scale.PublishLoad(15000);
            AssertGood(await controller.OutboundWeighingAsync("A").ConfigureAwait(false));
            Assert.That(vehicle.DeltaWeight.Value.Net, Is.EqualTo(25000));
            AssertTotal(vehicle, mixedTotal);

            AssertGood(await controller.InboundWeighingAsync("A").ConfigureAwait(false));
            AssertGood(await controller.OutboundWeighingAsync("A").ConfigureAwait(false));
            Assert.That(vehicle.DeltaWeight.Value.Net, Is.Zero);
            AssertTotal(vehicle, mixedTotal);
            scale.PublishLoad(30000);
            AssertGood(await controller.OnePassWeighingAsync("A").ConfigureAwait(false));
            Assert.That(vehicle.DeltaWeight.Value.Net, Is.EqualTo(18000));
            AssertTotal(vehicle, onePassTotal);
            Assert.That(vehicle.TotalWeightResetDate.Value, Is.EqualTo(resetDate));
            var other = (VehicleProductState)scale.ProductionPreset.Find("B")!;
            AssertTotal(other, 0);
        }

        /// <summary>
        /// Fluent and direct configuration choose each vehicle's rule independently without resetting accumulated work.
        /// </summary>
        [Test]
        public async Task AggregationIsSelectedPerVehicleAndCanChangeWithoutResetAsync()
        {
            ScaleHandle scale = await CreateAsync(id => id == "A"
                ? VehicleWeightAggregation.Signed : VehicleWeightAggregation.Absolute).ConfigureAwait(false);
            VehicleController controller = scale.Vehicle!;
            foreach (string id in s_vehicleIds)
            {
                scale.PublishLoad(20000);
                AssertGood(await controller.InboundWeighingAsync(id).ConfigureAwait(false));
                scale.PublishLoad(30000);
                AssertGood(await controller.OutboundWeighingAsync(id).ConfigureAwait(false));
            }
            var signed = (VehicleProductState)scale.ProductionPreset!.Find("A")!;
            var absolute = (VehicleProductState)scale.ProductionPreset.Find("B")!;
            AssertTotal(signed, -10000);
            AssertTotal(absolute, 10000);
            signed.TotalWeight!.StatusCode = StatusCodes.UncertainSubNormal;
            var sourceTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            signed.TotalWeight.Timestamp = sourceTime;
            AssertGood(await controller.SetTotalWeightAggregationAsync("A", VehicleWeightAggregation.Absolute)
                .ConfigureAwait(false));
            AssertTotal(signed, -10000);
            Assert.That(signed.TotalWeight.StatusCode, Is.EqualTo(StatusCodes.UncertainSubNormal));
            Assert.That(signed.TotalWeight.Timestamp, Is.EqualTo((DateTimeUtc)sourceTime));
            scale.PublishLoad(20000);
            AssertGood(await controller.InboundWeighingAsync("A").ConfigureAwait(false));
            scale.PublishLoad(30000);
            AssertGood(await controller.OutboundWeighingAsync("A").ConfigureAwait(false));
            AssertTotal(signed, 0);
            AssertTotal(absolute, 10000);
        }

        /// <summary>
        /// A reset affects only the selected vehicle and retains its pending inbound measurement and aggregation rule.
        /// </summary>
        [TestCase(VehicleWeightAggregation.Signed, -10000)]
        [TestCase(VehicleWeightAggregation.Absolute, 10000)]
        public async Task ResetClearsOnlyTheVehicleTotalAndPreservesIncompleteJourneyAsync(
            VehicleWeightAggregation aggregation,
            double completedTotal)
        {
            ScaleHandle scale = await CreateAsync(_ => aggregation).ConfigureAwait(false);
            VehicleController controller = scale.Vehicle!;
            var vehicle = (VehicleProductState)scale.ProductionPreset!.Find("A")!;
            var other = (VehicleProductState)scale.ProductionPreset.Find("B")!;
            scale.PublishLoad(20000);
            AssertGood(await controller.OnePassWeighingAsync("A").ConfigureAwait(false));
            AssertGood(await controller.OnePassWeighingAsync("B").ConfigureAwait(false));
            AssertTotal(vehicle, 8000);
            AssertTotal(other, 8000);
            scale.PublishLoad(20000);
            AssertGood(await controller.InboundWeighingAsync("A").ConfigureAwait(false));
            DateTimeUtc before = DateTimeUtc.Now;
            AssertGood(await controller.ResetTotalWeightAsync("A").ConfigureAwait(false));
            AssertTotal(vehicle, 0);
            AssertTotal(other, 8000);
            Assert.That((DateTime)vehicle.TotalWeightResetDate!.Value,
                Is.GreaterThanOrEqualTo((DateTime)before));

            scale.PublishLoad(30000);
            AssertGood(await controller.OutboundWeighingAsync("A").ConfigureAwait(false));
            Assert.That(vehicle.DeltaWeight!.Value.Net, Is.EqualTo(-10000));
            AssertTotal(vehicle, completedTotal);
        }

        /// <summary>
        /// Rejected measurements and invalid aggregation do not change totals or consume a valid inbound measurement.
        /// </summary>
        [Test]
        public async Task RejectedCompletionRetainsInboundAndTotalAsync()
        {
            VehicleWeightAggregation aggregation = VehicleWeightAggregation.Signed;
            ScaleHandle scale = await CreateAsync(_ => aggregation).ConfigureAwait(false);
            VehicleController controller = scale.Vehicle!;
            var vehicle = (VehicleProductState)scale.ProductionPreset!.Find("A")!;
            scale.PublishLoad(40000);
            AssertGood(await controller.InboundWeighingAsync("A").ConfigureAwait(false));
            scale.PublishLoad(15000, stable: false);
            ServiceResult unstable = await controller.OutboundWeighingAsync("A").ConfigureAwait(false);
            Assert.That(unstable.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            AssertTotal(vehicle, 0);

            scale.PublishLoad(15000);
            aggregation = (VehicleWeightAggregation)99;
            ServiceResult invalid = await controller.OutboundWeighingAsync("A").ConfigureAwait(false);
            Assert.That(invalid.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            AssertTotal(vehicle, 0);
            aggregation = VehicleWeightAggregation.Absolute;
            AssertGood(await controller.OutboundWeighingAsync("A").ConfigureAwait(false));
            Assert.That(vehicle.DeltaWeight!.Value.Net, Is.EqualTo(25000));
            AssertTotal(vehicle, 25000);
        }

        /// <summary>
        /// Overflow is rejected before the incoming transaction is consumed.
        /// </summary>
        [Test]
        public async Task NonfiniteTotalDoesNotConsumeInboundAsync()
        {
            ScaleHandle scale = await CreateAsync(_ => VehicleWeightAggregation.Signed).ConfigureAwait(false);
            VehicleController controller = scale.Vehicle!;
            var vehicle = (VehicleProductState)scale.ProductionPreset!.Find("A")!;
            scale.PublishLoad(40000);
            AssertGood(await controller.InboundWeighingAsync("A").ConfigureAwait(false));
            vehicle.TotalWeight!.Value = new WeightType { Net = double.PositiveInfinity };
            scale.PublishLoad(15000);
            ServiceResult invalid = await controller.OutboundWeighingAsync("A").ConfigureAwait(false);
            Assert.That(invalid.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(vehicle.TotalWeight.Value.Net, Is.EqualTo(double.PositiveInfinity));
            AssertGood(await controller.ResetTotalWeightAsync("A").ConfigureAwait(false));
            AssertGood(await controller.OutboundWeighingAsync("A").ConfigureAwait(false));
            AssertTotal(vehicle, 25000);
        }

        /// <summary>
        /// Direct setup materializes optional totals, rejects bad inputs and observes cancellation before mutation.
        /// </summary>
        [Test]
        public async Task DirectTotalConfigurationAndResetValidateBeforeMutationAsync()
        {
            ScaleHandle scale = await CreateAsync().ConfigureAwait(false);
            VehicleController controller = scale.Vehicle!;
            var vehicle = (VehicleProductState)scale.ProductionPreset!.Find("A")!;
            Assert.That(vehicle.TotalWeight, Is.Null);
            Assert.That((await controller.ResetTotalWeightAsync("A").ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That((await controller.ResetTotalWeightAsync("unknown").ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNotFound));
            Assert.That((await controller.SetTotalWeightAggregationAsync("unknown", VehicleWeightAggregation.Signed)
                .ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await controller.SetTotalWeightAggregationAsync("A", (VehicleWeightAggregation)99));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await controller.SetTotalWeightAggregationAsync(
                    "A", VehicleWeightAggregation.Absolute, cancelled.Token));
            Assert.That(vehicle.TotalWeight, Is.Null);
            AssertGood(await controller.SetTotalWeightAggregationAsync("A", VehicleWeightAggregation.Absolute)
                .ConfigureAwait(false));
            scale.PublishLoad(20000);
            AssertGood(await controller.OnePassWeighingAsync("A").ConfigureAwait(false));
            AssertTotal(vehicle, 8000);
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await controller.ResetTotalWeightAsync("A", cancelled.Token));
            AssertTotal(vehicle, 8000);
            AssertGood(await controller.ResetTotalWeightAsync("A").ConfigureAwait(false));
            AssertTotal(vehicle, 0);
        }

        /// <summary>
        /// Public asynchronous entry points reject null identifiers, and empty ids cannot match an unset VehicleId.
        /// </summary>
        [Test]
        public async Task VehicleIdentifiersAreValidatedBeforeMutationAsync()
        {
            ScaleHandle scale = await CreateAsync(_ => VehicleWeightAggregation.Signed).ConfigureAwait(false);
            VehicleController controller = scale.Vehicle!;
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await controller.InboundWeighingAsync(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await controller.OutboundWeighingAsync(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await controller.OnePassWeighingAsync(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await controller.SetTotalWeightAggregationAsync(null!, VehicleWeightAggregation.Signed));
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await controller.ResetTotalWeightAsync(null!));
            ServiceResult empty = await controller.OutboundWeighingAsync(string.Empty).ConfigureAwait(false);
            Assert.That(empty.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            AssertTotal((VehicleProductState)scale.ProductionPreset!.Find("A")!, 0);
        }

        /// <summary>
        /// Cancelled measurements neither replace a pending inbound measurement nor add a completed transaction.
        /// </summary>
        [Test]
        public async Task CancelledVehicleMeasurementsDoNotReplaceInboundOrTotalAsync()
        {
            ScaleHandle scale = await CreateAsync(_ => VehicleWeightAggregation.Signed).ConfigureAwait(false);
            VehicleController controller = scale.Vehicle!;
            var vehicle = (VehicleProductState)scale.ProductionPreset!.Find("A")!;
            scale.PublishLoad(40000);
            AssertGood(await controller.InboundWeighingAsync("A").ConfigureAwait(false));
            scale.PublishLoad(15000);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await controller.InboundWeighingAsync("A", cancelled.Token));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await controller.OutboundWeighingAsync("A", cancelled.Token));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await controller.OnePassWeighingAsync("A", cancelled.Token));
            AssertTotal(vehicle, 0);
            AssertGood(await controller.OutboundWeighingAsync("A").ConfigureAwait(false));
            Assert.That(vehicle.DeltaWeight!.Value.Net, Is.EqualTo(25000));
            AssertTotal(vehicle, 25000);
        }

        /// <summary>
        /// Native asynchronous methods retain writer ownership until the last notification completes.
        /// </summary>
        [Test]
        public async Task NativeCompletionAwaitsNotificationsAndExcludesOverlappingWritesAsync()
        {
            ScaleHandle scale = await CreateAsync(_ => VehicleWeightAggregation.Absolute).ConfigureAwait(false);
            VehicleController controller = scale.Vehicle!;
            var vehicle = (VehicleProductState)scale.ProductionPreset!.Find("A")!;
            scale.PublishLoad(40000);
            AssertGood(await controller.InboundWeighingAsync("A").ConfigureAwait(false));
            scale.PublishLoad(15000);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            vehicle.DeltaWeight!.OnStateChangedAsync = async (_, _, _, _) =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            };
            Task<ServiceResult>? native = null;
            try
            {
                native = controller.OutboundWeighingAsync("A").AsTask();
                Assert.That(await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false), Is.True);
                Assert.That(native.IsCompleted, Is.False);
                Assert.That(vehicle.DeltaWeight.Value.Net, Is.EqualTo(25000));
                AssertTotal(vehicle, 0);
                Assert.That(controller.OutboundWeighing("A").StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                using var cancelled = new CancellationTokenSource();
                Task<ServiceResult> pending = controller.ResetTotalWeightAsync("A", cancelled.Token).AsTask();
                Assert.That(pending.IsCompleted, Is.False);
                cancelled.Cancel();
                await Assert.CatchAsync<OperationCanceledException>(async () => await pending);
                AssertTotal(vehicle, 0);
                release.TrySetResult(true);
                AssertGood(await native.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false));
                AssertTotal(vehicle, 25000);
            }
            finally
            {
                release.TrySetResult(true);
                if (native != null)
                {
                    await native.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Legacy local methods retain the same delta and total as native asynchronous methods.
        /// </summary>
        [TestCase(VehicleWeightAggregation.Signed, -10000)]
        [TestCase(VehicleWeightAggregation.Absolute, 10000)]
        public async Task LegacyVehicleCallsUseTheSameAggregationAsync(
            VehicleWeightAggregation aggregation,
            double expectedTotal)
        {
            ScaleHandle scale = await CreateAsync(_ => aggregation).ConfigureAwait(false);
            VehicleController controller = scale.Vehicle!;
            var vehicle = (VehicleProductState)scale.ProductionPreset!.Find("A")!;
            scale.PublishLoad(20000);
            AssertGood(controller.InboundWeighing("A"));
            scale.PublishLoad(30000);
            AssertGood(controller.OutboundWeighing("A"));
            Assert.That(vehicle.DeltaWeight!.Value.Net, Is.EqualTo(-10000));
            AssertTotal(vehicle, expectedTotal);
        }

        /// <summary>
        /// Product-id and vehicle-id aliases resolve the same pending transaction.
        /// </summary>
        [Test]
        public async Task VehicleAliasesShareOneIncompleteJourneyAsync()
        {
            string? selectedVehicleId = null;
            ScaleHandle scale = await CreateAsync(id =>
            {
                selectedVehicleId = id;
                return VehicleWeightAggregation.Signed;
            }).ConfigureAwait(false);
            var vehicle = (VehicleProductState)scale.ProductionPreset!.Find("A")!;
            vehicle.VehicleId!.Value = "LICENSE-A";
            scale.PublishLoad(40000);
            AssertGood(await scale.Vehicle!.InboundWeighingAsync("A").ConfigureAwait(false));
            scale.PublishLoad(15000);
            AssertGood(await scale.Vehicle.OutboundWeighingAsync("LICENSE-A").ConfigureAwait(false));
            Assert.That(vehicle.DeltaWeight!.Value.Net, Is.EqualTo(25000));
            AssertTotal(vehicle, 25000);
            Assert.That(selectedVehicleId, Is.EqualTo("LICENSE-A"));
        }

        /// <summary>
        /// Products added after startup receive configured totals and the same aggregation rule.
        /// </summary>
        [Test]
        public async Task DynamicVehicleReceivesConfiguredTotalizationAsync()
        {
            ScaleHandle scale = await CreateAsync(_ => VehicleWeightAggregation.Absolute).ConfigureAwait(false);
            var vehicle = (VehicleProductState)scale.ProductionPreset!.AddProduct(
                "C", new LocalizedText("C"), product => ConfigureVehicle(m_fixture.Context, product));
            AssertTotal(vehicle, 0);
            Assert.That(vehicle.TotalWeightResetDate, Is.Not.Null);
            scale.PublishLoad(10000);
            AssertGood(await scale.Vehicle!.OnePassWeighingAsync("C").ConfigureAwait(false));
            Assert.That(vehicle.DeltaWeight!.Value.Net, Is.EqualTo(-2000));
            AssertTotal(vehicle, 2000);
        }

        /// <summary>
        /// Builds two vehicle products with a known tare and optional fluent totalization.
        /// </summary>
        private ValueTask<ScaleHandle> CreateAsync(Func<string, VehicleWeightAggregation>? aggregation = null)
        {
            return m_fixture.Manager.CreateScaleAsync(
                m_fixture.Name("Vehicle"), ScaleKind.Vehicle, builder =>
                {
                    builder.WithIdentification(ScalesNodeManagerTests.Identity("Vehicle"))
                        .WithTypeFeatures()
                        .WithWeighingRange(new WeighingRangeDefinition(0, 60000, 20, 20))
                        .WithProductionPreset(preset => preset
                            .AddProduct("A", new LocalizedText("A"), ConfigureVehicle)
                            .AddProduct("B", new LocalizedText("B"), ConfigureVehicle));
                    if (aggregation != null)
                    {
                        builder.WithVehicleTotals(aggregation);
                    }
                });
        }

        /// <summary>
        /// Supplies a tare shared by the test vehicles.
        /// </summary>
        private static void ConfigureVehicle(ISystemContext context, ProductState product)
        {
            var vehicle = (VehicleProductState)product;
            vehicle.AddTare(context);
            vehicle.Tare!.WrappedValue = Variant.From(12000.0);
        }

        /// <summary>
        /// Asserts the complete weight structure instead of only one of its members.
        /// </summary>
        private static void AssertTotal(VehicleProductState vehicle, double total)
        {
            Assert.That(vehicle.TotalWeight, Is.Not.Null);
            Assert.That(vehicle.TotalWeight!.Value.Net, Is.EqualTo(total));
            Assert.That(vehicle.TotalWeight.Value.Gross, Is.EqualTo(total));
            Assert.That(vehicle.TotalWeight.Value.Tare, Is.Zero);
        }

        /// <summary>
        /// Requires a successful service result.
        /// </summary>
        private static void AssertGood(ServiceResult result)
        {
            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
        }

        /// <summary>The isolated server fixture.</summary>
        private ScalesServerFixture m_fixture = null!;

        /// <summary>The independent vehicles used to compare aggregation rules.</summary>
        private static readonly string[] s_vehicleIds = ["A", "B"];
    }
}
