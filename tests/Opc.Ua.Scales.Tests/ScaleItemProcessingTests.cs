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
using NUnit.Framework;
using Opc.Ua.Scales.Server;
using Opc.Ua.Scales.Server.Builders;
using Opc.Ua.Scales.Server.Runtime;

namespace Opc.Ua.Scales.Tests
{
    /// <summary>
    /// Registered equipment acquisitions, independent of the scalar scale engine.
    /// </summary>
    [TestFixture]
    [Category("Scales")]
    [NonParallelizable]
    public sealed class ScaleItemProcessingTests
    {
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_fixture = new ScalesServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task StopAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        [TestCase(0.999, "Low")]
        [TestCase(1.0, "High")]
        [TestCase(1.001, "High")]
        [TestCase(2.0, "High")]
        [TestCase(2.001, null)]
        public async Task CatchweigherClassifiesBoundariesAndPreservesRegisteredItemAsync(double net, string? zone)
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Catchweigher);
            var product = (CatchweigherProductState)scale.ProductionPreset!.ActiveProduct!;
            scale.ProductionPreset.AddZone(product, new LocalizedText("Low"), 0, 1000, ScaleUnits.Gram);
            scale.ProductionPreset.AddZone(product, new LocalizedText("High"), 1, 2, ScaleUnits.Kilogram);
            DateTime timestamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            ScaleItemReport report = Item(1, net * 1000) with
            {
                Weight = Item(1, net * 1000).Weight with { EngineeringUnits = ScaleUnits.Gram, Timestamp = timestamp },
                Length = new ScaleItemMeasurement(12, Millimetre, timestamp, StatusCodes.Uncertain)
            };
            int registered = 0;
            scale.WeightRegistered += (_, _) => registered++;
            AssertGood(await scale.Catchweigher!.ReportAsync(report));
            Assert.That(scale.CurrentReading.Net, Is.Zero, "the equipment report does not feed the scalar engine");
            Assert.That(scale.RegisteredReading!.Net, Is.EqualTo(net).Within(1e-12));
            Assert.That(product.LastItem!.MeasuredWeight!.Value.Net, Is.EqualTo(net).Within(1e-12));
            Assert.That((DateTime)product.LastItem.MeasuredWeight.Timestamp, Is.EqualTo(timestamp));
            Assert.That(product.LastItem.ItemId!.Value, Is.EqualTo("item-1"));
            Assert.That(product.LastItem.ZoneName!.Value, Is.EqualTo(zone));
            Assert.That(product.LastItem.ZoneName.StatusCode,
                Is.EqualTo((StatusCode)(zone == null ? StatusCodes.BadNoData : StatusCodes.Good)));
            Assert.That(product.LastItem.MeasuredLength!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
            Assert.That((DateTime)product.LastItem.MeasuredLength.Timestamp, Is.EqualTo(timestamp));
            Assert.That(Count(product.Statistic!), Is.EqualTo(1UL));
            Assert.That(Count(scale.ProductionOutput!.Statistic), Is.EqualTo(1UL));
            Assert.That(registered, Is.EqualTo(1));
            Assert.That(product.LastItem.MeasuredLength.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(product.LastItem.MeasuredLength.DataType, Is.EqualTo(Ua.DataTypeIds.Number));
        }

        [Test]
        public async Task OmittedMeasurementsClearLastItemWithoutChangingPresetsAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Catchweigher);
            var product = (CatchweigherProductState)scale.ProductionPreset!.ActiveProduct!;
            product.AddPresetLength(m_fixture.Context);
            product.PresetLength!.WrappedValue = new Variant(99.0);
            AssertGood(await scale.Catchweigher!.ReportAsync(Item(1) with
            {
                Length = new ScaleItemMeasurement(12, Millimetre, DateTime.UtcNow, StatusCodes.Good)
            }));
            AssertGood(await scale.Catchweigher.ReportAsync(Item(2)));
            Assert.That(product.LastItem!.MeasuredLength!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNoData));
            Assert.That(product.LastItem.MeasuredLength.WrappedValue.IsNull, Is.True);
            Assert.That(product.PresetLength.WrappedValue.TryGetValue(out double preset), Is.True);
            Assert.That(preset, Is.EqualTo(99));
        }

        [TestCase("overlap")]
        [TestCase("unit")]
        [TestCase("order")]
        public async Task InvalidZonesRejectAcquisitionWithoutChangingCountersAsync(string invalid)
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Catchweigher);
            var product = (CatchweigherProductState)scale.ProductionPreset!.ActiveProduct!;
            ZoneState first = scale.ProductionPreset.AddZone(
                product, new LocalizedText("First"), 0, 1, ScaleUnits.Kilogram);
            if (invalid == "overlap")
            {
                scale.ProductionPreset.AddZone(product, new LocalizedText("Second"), 0.5, 2, ScaleUnits.Kilogram);
            }
            else if (invalid == "unit")
            {
                first.LowerLimit!.EngineeringUnits!.Value = Millimetre;
            }
            else
            {
                first.LowerLimit!.WrappedValue = new Variant(2.0);
            }
            ServiceResult result = await scale.Catchweigher!.ReportAsync(Item(1));
            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
            Assert.That(Count(scale.ProductionOutput!.Statistic), Is.Zero);
            Assert.That(scale.RegisteredReading, Is.Null);
        }

        [TestCase("duplicate")]
        [TestCase("wrongProduct")]
        [TestCase("badQuality")]
        [TestCase("uncertainQuality")]
        [TestCase("overflow")]
        [TestCase("overload")]
        [TestCase("inconsistent")]
        [TestCase("negative")]
        [TestCase("infinite")]
        [TestCase("missingTime")]
        [TestCase("unit")]
        [TestCase("zeroSequence")]
        [TestCase("dimension")]
        [TestCase("negativeDimension")]
        [TestCase("missingDimensionUnit")]
        [TestCase("missingDimensionTime")]
        public async Task InvalidAcquisitionsDoNotMutateThePreviousItemAsync(string invalid)
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Catchweigher);
            AssertGood(await scale.Catchweigher!.ReportAsync(Item(1)));
            ScaleItemReport item = invalid switch
            {
                "duplicate" => Item(1),
                "wrongProduct" => Item(2) with { ProductId = "other" },
                "badQuality" => Item(2) with
                {
                    Weight = Item(2).Weight with { StatusCode = StatusCodes.BadNoCommunication }
                },
                "uncertainQuality" => Item(2) with
                {
                    Weight = Item(2).Weight with { StatusCode = StatusCodes.Uncertain }
                },
                "overflow" => Item(2) with
                {
                    Weight = Item(2).Weight with { StatusCode = ((StatusCode)StatusCodes.Good).SetOverflow(true) }
                },
                "overload" => Item(2) with { Weight = Item(2).Weight with { Overload = true } },
                "inconsistent" => Item(2) with { Weight = Item(2).Weight with { Gross = 42 } },
                "negative" => Item(2, -1),
                "infinite" => Item(2, double.PositiveInfinity),
                "missingTime" => Item(2) with { Weight = Item(2).Weight with { Timestamp = default } },
                "unit" => Item(2) with { Weight = Item(2).Weight with { EngineeringUnits = Millimetre } },
                "zeroSequence" => Item(0),
                "negativeDimension" => Item(2) with
                {
                    Length = new ScaleItemMeasurement(-1, Millimetre, DateTime.UtcNow, 0)
                },
                "missingDimensionUnit" => Item(2) with
                {
                    Length = new ScaleItemMeasurement(1, null!, DateTime.UtcNow, 0)
                },
                "missingDimensionTime" => Item(2) with
                {
                    Length = new ScaleItemMeasurement(1, Millimetre, default, 0)
                },
                _ => Item(2) with { Length = new ScaleItemMeasurement(double.NaN, Millimetre, DateTime.UtcNow, 0) }
            };
            Assert.That(ServiceResult.IsBad(await scale.Catchweigher.ReportAsync(item)), Is.True);
            Assert.That(Count(scale.ProductionOutput!.Statistic), Is.EqualTo(1UL));
            Assert.That(scale.RegisteredReading!.WeightId, Is.EqualTo("item-1"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task PricesUseEquipmentOrExplicitRoundingAndPublishMandatoryCurrencyAsync(bool equipment)
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.AutomaticWeightPriceLabeler,
                new RoundedScalePriceCalculator(2, MidpointRounding.AwayFromZero));
            var product = (AutomaticWeightPriceLabelerProductState)scale.ProductionPreset!.ActiveProduct!;
            ConfigurePrice(m_fixture.Context, product, m_fixture.Manager.NamespaceIndices.Scales);
            ScaleItemReport report = Item(1, 1005) with
            {
                Weight = Item(1, 1005).Weight with { EngineeringUnits = ScaleUnits.Gram },
                Price = equipment ? new ScaleItemPrice(3.21m, Euro()) : null
            };
            AssertGood(await scale.Catchweigher!.ReportAsync(report));
            Assert.That(product.LastItem, Is.TypeOf<PriceItemState>());
            var last = (PriceItemState)product.LastItem!;
            Assert.That(last.ItemPrice!.WrappedValue.TryGetValue(out double price), Is.True);
            Assert.That(price, Is.EqualTo(equipment ? 3.21 : 1.01));
            Assert.That(last.ItemPrice.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(ScaleItemPublisher.CurrencyOf(m_fixture.Context, last.ItemPrice)!.AlphabeticCode,
                Is.EqualTo("EUR"));
            Assert.That(Count(product.Statistic!), Is.EqualTo(1UL));
        }

        [TestCase(1.005, 1.0, 2, MidpointRounding.ToEven, 1.0)]
        [TestCase(1.005, 1.0, 2, MidpointRounding.AwayFromZero, 1.01)]
        [TestCase(2.5, 1.0, 0, MidpointRounding.ToEven, 2.0)]
        [TestCase(2.5, 1.0, 0, MidpointRounding.AwayFromZero, 3.0)]
        [TestCase(2.0, 1.5, 2, MidpointRounding.ToEven, 3.0)]
        [TestCase(0.125, 2.0, 28, MidpointRounding.ToEven, 0.25)]
        public async Task PricePolicyMultipliesAndUsesTheSelectedRoundingRuleAsync(
            double net, double unitPrice, int precision, MidpointRounding rounding, double expected)
        {
            var calculator = new RoundedScalePriceCalculator(precision, rounding);
            ScaleItemPrice? price = await calculator.CalculateAsync(
                new ScalePriceRequest(Item(1, net), (decimal)unitPrice, Euro()));
            Assert.That(price, Is.Not.Null);
            Assert.That(price!.Amount, Is.EqualTo((decimal)expected));
            Assert.That(price.Currency.AlphabeticCode, Is.EqualTo("EUR"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PriceProviderFailuresOrMissingResultsDoNotInventAPriceAsync(bool fails)
        {
            var calculator = new DeferredPriceCalculator();
            ScaleHandle scale = await CreateAsync(ScaleKind.AutomaticWeightPriceLabeler, calculator);
            var product = (AutomaticWeightPriceLabelerProductState)scale.ProductionPreset!.ActiveProduct!;
            ConfigurePrice(m_fixture.Context, product, m_fixture.Manager.NamespaceIndices.Scales);
            Task<ServiceResult> pending = scale.Catchweigher!.ReportAsync(Item(1)).AsTask();
            Assert.That(calculator.Started, Is.True);
            if (fails)
            {
                calculator.Result.SetException(new InvalidOperationException("The pricing provider failed."));
                await Assert.ThatAsync(() => pending, Throws.TypeOf<InvalidOperationException>());
                Assert.That(Count(scale.ProductionOutput!.Statistic), Is.Zero);
            }
            else
            {
                calculator.Result.SetResult(null);
                AssertGood(await pending);
                Assert.That(Count(scale.ProductionOutput!.Statistic), Is.EqualTo(1UL));
            }
            var last = (PriceItemState)product.LastItem!;
            Assert.That(last.ItemPrice!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNoData));
            Assert.That(last.ItemPrice.WrappedValue.IsNull, Is.True);
        }

        [Test]
        public async Task SuppliedPricesRequireValidNonnegativeCurrencyAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.AutomaticWeightPriceLabeler);
            CurrencyUnitType invalidCode = Euro();
            invalidCode.AlphabeticCode = "eur";
            CurrencyUnitType invalidNumber = Euro();
            invalidNumber.NumericCode = 1000;
            foreach (ScaleItemPrice price in new[]
            {
                new ScaleItemPrice(-1, Euro()),
                new ScaleItemPrice(1, invalidCode),
                new ScaleItemPrice(1, invalidNumber),
                new ScaleItemPrice(1, null!)
            })
            {
                Assert.That(ServiceResult.IsBad(await scale.Catchweigher!.ReportAsync(
                    Item(1) with { Price = price })), Is.True);
                Assert.That(Count(scale.ProductionOutput!.Statistic), Is.Zero);
            }
            AssertGood(await scale.Catchweigher!.ReportAsync(Item(1) with { Price = new ScaleItemPrice(0, Euro()) }));
            var product = (AutomaticWeightPriceLabelerProductState)scale.ProductionPreset!.ActiveProduct!;
            var last = (PriceItemState)product.LastItem!;
            Assert.That(last.ItemPrice!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(last.ItemPrice.WrappedValue.TryGetValue(out double amount), Is.True);
            Assert.That(amount, Is.Zero, "an explicit zero price is not the same as a missing price");
        }

        [Test]
        public async Task MissingPricesRemainMissingAndConflictingCurrencyRejectsAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.AutomaticWeightPriceLabeler);
            var product = (AutomaticWeightPriceLabelerProductState)scale.ProductionPreset!.ActiveProduct!;
            ConfigurePrice(m_fixture.Context, product, m_fixture.Manager.NamespaceIndices.Scales);
            AssertGood(await scale.Catchweigher!.ReportAsync(Item(1)));
            var last = (PriceItemState)product.LastItem!;
            Assert.That(last.ItemPrice!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNoData));
            Assert.That(last.ItemPrice.WrappedValue.IsNull, Is.True);
            CurrencyUnitType usd = Euro();
            usd.AlphabeticCode = "USD";
            usd.NumericCode = 840;
            Assert.That(ServiceResult.IsBad(await scale.Catchweigher.ReportAsync(
                Item(2) with { Price = new ScaleItemPrice(10, usd) })), Is.True);
            Assert.That(Count(scale.ProductionOutput!.Statistic), Is.EqualTo(1UL));
        }

        [TestCase("product")]
        [TestCase("price")]
        [TestCase("currency")]
        public async Task ConfigurationChangesDuringPricingRejectWithoutCommitAsync(string changed)
        {
            var calculator = new DeferredPriceCalculator();
            ScaleHandle scale = await CreateAsync(ScaleKind.AutomaticWeightPriceLabeler, calculator);
            var product = (AutomaticWeightPriceLabelerProductState)scale.ProductionPreset!.ActiveProduct!;
            ConfigurePrice(m_fixture.Context, product, m_fixture.Manager.NamespaceIndices.Scales);
            Task<ServiceResult> pending = scale.Catchweigher!.ReportAsync(Item(1)).AsTask();
            Assert.That(calculator.Started, Is.True);
            if (changed == "product")
            {
                AssertGood(scale.ProductionPreset.Switch("B"));
            }
            else if (changed == "price")
            {
                product.UnitPrice!.WrappedValue = new Variant(2.0);
            }
            else
            {
                CurrencyUnitType currency = ScaleItemPublisher.CurrencyOf(m_fixture.Context, product.UnitPrice)!;
                currency.AlphabeticCode = "USD";
            }
            calculator.Result.SetResult(new ScaleItemPrice(1, Euro()));
            Assert.That((await pending).StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(Count(scale.ProductionOutput!.Statistic), Is.Zero);
        }

        [Test]
        public async Task PricingPolicyCannotMutateCapturedMeasurementMetadataAsync()
        {
            var calculator = new DeferredPriceCalculator();
            ScaleHandle scale = await CreateAsync(ScaleKind.AutomaticWeightPriceLabeler, calculator);
            var product = (AutomaticWeightPriceLabelerProductState)scale.ProductionPreset!.ActiveProduct!;
            ConfigurePrice(m_fixture.Context, product, m_fixture.Manager.NamespaceIndices.Scales);
            ScaleItemReport item = Item(1) with
            {
                Length = new ScaleItemMeasurement(12, Millimetre, DateTime.UtcNow, StatusCodes.Good)
            };
            Task<ServiceResult> pending = scale.Catchweigher!.ReportAsync(item).AsTask();
            Assert.That(calculator.Request, Is.Not.Null);
            calculator.Request!.Item.Weight.EngineeringUnits!.UnitId = ScaleUnits.Gram.UnitId;
            calculator.Request.Item.Length!.Unit.UnitId = ScaleUnits.Kilogram.UnitId;
            calculator.Request.Currency.AlphabeticCode = "USD";
            calculator.Result.SetResult(new ScaleItemPrice(1, Euro()));
            AssertGood(await pending);
            Assert.That(product.LastItem!.MeasuredWeight!.EngineeringUnits!.Value.UnitId,
                Is.EqualTo(ScaleUnits.Kilogram.UnitId));
            Assert.That(product.LastItem.MeasuredLength!.EngineeringUnits!.Value.UnitId, Is.EqualTo(Millimetre.UnitId));
            Assert.That(scale.RegisteredReading!.EngineeringUnits!.UnitId, Is.EqualTo(ScaleUnits.Kilogram.UnitId));
            Assert.That(ScaleItemPublisher.CurrencyOf(m_fixture.Context, product.UnitPrice)!.AlphabeticCode,
                Is.EqualTo("EUR"));
        }

        [Test]
        public async Task CancellationPreventsPendingPriceCommitAsync()
        {
            var calculator = new DeferredPriceCalculator();
            ScaleHandle scale = await CreateAsync(ScaleKind.AutomaticWeightPriceLabeler, calculator);
            ConfigurePrice(m_fixture.Context,
                (AutomaticWeightPriceLabelerProductState)scale.ProductionPreset!.ActiveProduct!,
                m_fixture.Manager.NamespaceIndices.Scales);
            using var cancellation = new CancellationTokenSource();
            Task<ServiceResult> pending = scale.Catchweigher!.ReportAsync(Item(1), cancellation.Token).AsTask();
            Assert.That(calculator.Started, Is.True);
            cancellation.Cancel();
            await Assert.ThatAsync(() => pending, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(Count(scale.ProductionOutput!.Statistic), Is.Zero);
            Assert.That(scale.RegisteredReading, Is.Null);
        }

        [Test]
        public async Task RuntimeShutdownCancelsPendingPriceWithoutPublicationAsync()
        {
            await using var fixture = new ScalesServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var calculator = new DeferredPriceCalculator();
            ScaleHandle scale = await fixture.Manager.CreateScaleAsync(
                fixture.Name("ShutdownPrice"), ScaleKind.AutomaticWeightPriceLabeler,
                builder => Configure(builder, ScaleKind.AutomaticWeightPriceLabeler, calculator));
            ConfigurePrice(fixture.Context,
                (AutomaticWeightPriceLabelerProductState)scale.ProductionPreset!.ActiveProduct!,
                fixture.Manager.NamespaceIndices.Scales);
            Task<ServiceResult> pending = scale.Catchweigher!.ReportAsync(Item(1)).AsTask();
            Assert.That(calculator.Started, Is.True);
            await fixture.Manager.DisposeAsync();
            await Assert.ThatAsync(() => pending, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(Count(scale.ProductionOutput!.Statistic), Is.Zero);
            Assert.That(scale.RegisteredReading, Is.Null);
        }

        /// <summary>
        /// Equipment callback failures cannot bypass construction draining or owned resource cleanup.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ThrowingProviderCancellationStillDrainsCreationsAndReleasesResourcesAsync(bool cleanupFails)
        {
            var fixture = new ScalesServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var failure = new InvalidOperationException("Equipment cancellation callback failed.");
            var cleanupFailure = new InvalidOperationException("Owned resource cleanup failed.");
            Exception[] expected = cleanupFails ? [failure, cleanupFailure] : [failure];
            using var calculator = new ThrowingCancellationPriceCalculator(failure);
            using var entered = new SemaphoreSlim(0, 1);
            using var resume = new ManualResetEventSlim(false);
            Task<ScaleHandle>? creation = null;
            try
            {
                ScaleHandle scale = await fixture.Manager.CreateScaleAsync(
                    fixture.Name("ThrowingPrice"), ScaleKind.AutomaticWeightPriceLabeler,
                    builder => Configure(builder, ScaleKind.AutomaticWeightPriceLabeler, calculator));
                ConfigurePrice(fixture.Context,
                    (AutomaticWeightPriceLabelerProductState)scale.ProductionPreset!.ActiveProduct!,
                    fixture.Manager.NamespaceIndices.Scales);
                var resource = new ShutdownProbe();
                scale.Services.OwnResource(resource);
                var failingResource = new ShutdownProbe(cleanupFails ? cleanupFailure : null);
                scale.Services.OwnResource(failingResource);
                Task<ServiceResult> pending = scale.Catchweigher!.ReportAsync(Item(1)).AsTask();
                Assert.That(calculator.Started, Is.True);
                creation = Task.Run(() => fixture.Manager.CreateScaleAsync(
                    fixture.Name("InFlightShutdown"), ScaleKind.Simple, builder =>
                    {
                        ScalesNodeManagerTests.FullScale(builder, "InFlightShutdown");
                        entered.Release();
                        if (!resume.Wait(TimeSpan.FromSeconds(15)))
                        {
                            throw new TimeoutException("The construction was not released.");
                        }
                    }).AsTask());
                Assert.That(await entered.WaitAsync(TimeSpan.FromSeconds(15)), Is.True);
                Task stopping = fixture.Manager.Runtime.DisposeAsync().AsTask();
                Assert.That(calculator.CancellationCount, Is.EqualTo(1));
                Assert.That(stopping.IsCompleted, Is.False, "cancellation errors must not bypass construction drain");
                Assert.That(resource.Disposed, Is.False, "the admitted construction still owns the dependencies");
                resume.Set();
                await Assert.ThatAsync(
                    () => creation, Throws.InstanceOf<OperationCanceledException>());
                AggregateException? thrown = await Assert.ThrowsAsync<AggregateException>(
                    async () => await stopping.WaitAsync(TimeSpan.FromSeconds(15)));
                Assert.That(thrown!.Flatten().InnerExceptions, Is.EquivalentTo(expected));
                Assert.That(resource.Disposed, Is.True, "resource cleanup follows the failed cancellation");
                Assert.That(failingResource.Disposed, Is.True, "all owned cleanup is attempted");
                Assert.That(() => scale.Services.CreateFileBinder(), Throws.TypeOf<ObjectDisposedException>());
                AggregateException? repeated = await Assert.ThrowsAsync<AggregateException>(
                    async () => await fixture.Manager.Runtime.DisposeAsync());
                Assert.That(repeated, Is.SameAs(thrown), "later callers retain the original shutdown error");
                await Assert.ThatAsync(() => pending, Throws.InstanceOf<OperationCanceledException>());
                Assert.That(Count(scale.ProductionOutput!.Statistic), Is.Zero);
                Assert.That(scale.RegisteredReading, Is.Null);
            }
            finally
            {
                resume.Set();
                try
                {
                    if (creation != null)
                    {
                        try
                        {
                            await creation.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            // Shutdown deliberately cancels the admitted construction.
                        }
                    }
                }
                finally
                {
                    try
                    {
                        await fixture.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (AggregateException ex) when (ex.Flatten().InnerExceptions.Count == expected.Length &&
                        ex.Flatten().InnerExceptions.Contains(failure) &&
                        (!cleanupFails || ex.Flatten().InnerExceptions.Contains(cleanupFailure)))
                    {
                        // The runtime retains the injected failure for every subsequent shutdown.
                    }
                }
            }
        }

        [Test]
        public async Task AcquisitionSequenceSurvivesProductAndStatisticsPeriodsAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Catchweigher);
            AssertGood(await scale.Catchweigher!.ReportAsync(Item(10)));
            scale.ProductionOutput!.Reset();
            AssertGood(scale.ProductionPreset!.Switch("B"));
            Assert.That((await scale.Catchweigher.ReportAsync(Item(10) with { ProductId = "B" })).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEntryExists));
            AssertGood(await scale.Catchweigher.ReportAsync(Item(11) with { ProductId = "B" }));
            Assert.That(Count(scale.ProductionOutput.Statistic), Is.EqualTo(1UL));
        }

        [Test]
        public async Task ProductSelectionChangesWaitForCommittingReportsAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Catchweigher);
            ScaleProductionPreset preset = scale.ProductionPreset!;
            var first = (CatchweigherProductState)preset.ActiveProduct!;
            ScaleProductionPreset.ActiveProductReservation reservation = preset.ReserveActiveProduct();
            Assert.That(reservation.Product, Is.SameAs(first));

            Assert.That(preset.Switch("B").StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            AssertGood(await scale.Catchweigher!.ReportAsync(Item(1)));
            Assert.That(preset.CurrentProducts.ToArray(), Is.EqualTo(FirstProductSelection));

            reservation.Dispose();
            reservation.Dispose();
            AssertGood(preset.Switch("B"));
            Assert.That((await scale.Catchweigher.ReportAsync(Item(2))).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(Count(first.Statistic!), Is.EqualTo(1UL));
            AssertGood(preset.Switch("A"));
        }

        [Test]
        public async Task ReservationsPinNothingWithoutASingleProductInProcessingAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.TotalizingHopper);
            ScaleProductionPreset preset = scale.ProductionPreset!;
            using (ScaleProductionPreset.ActiveProductReservation pinned = preset.ReserveActiveProduct())
            {
                Assert.That(pinned.Product, Is.Not.Null);
                Assert.That(preset.Select("B").StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(preset.Deselect("A").StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                AssertGood(preset.Deselect("B"));
            }
            AssertGood(preset.Select("B"));
            using (ScaleProductionPreset.ActiveProductReservation none = preset.ReserveActiveProduct())
            {
                Assert.That(none.Product, Is.Null);
                AssertGood(preset.Deselect("B"));
            }
            AssertGood(await scale.TotalizingHopper!.ReportCompletedFillAsync(Item(1)));
        }

        [Test]
        public async Task ConcurrentReportsAndSwitchesCommitOnlyToTheProductInProcessingAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Catchweigher);
            ScaleProductionPreset preset = scale.ProductionPreset!;
            var products = new[]
            {
                (CatchweigherProductState)preset.Find("A")!,
                (CatchweigherProductState)preset.Find("B")!
            };
            const int iterations = 200;
            int[] committed = new int[2];
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task reports = Task.Run(async () =>
            {
                await start.Task.ConfigureAwait(false);
                for (int i = 0; i < iterations; i++)
                {
                    int index = i % 2;
                    ServiceResult result = await scale.Catchweigher!.ReportAsync(
                        Item((ulong)i + 1) with { ProductId = index == 0 ? "A" : "B" });
                    if (ServiceResult.IsGood(result))
                    {
                        committed[index]++;
                    }
                    else
                    {
                        Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                    }
                }
            });
            Task switches = Task.Run(async () =>
            {
                await start.Task.ConfigureAwait(false);
                for (int i = 0; i < iterations; i++)
                {
                    ServiceResult result = preset.Switch(i % 2 == 0 ? "B" : "A");
                    Assert.That(result.StatusCode == StatusCodes.Good ||
                        result.StatusCode == StatusCodes.BadInvalidState, Is.True, result.ToString());
                    await Task.Yield();
                }
            });
            start.SetResult(true);
            await Task.WhenAll(reports, switches);

            Assert.That(Count(products[0].Statistic!), Is.EqualTo((ulong)committed[0]));
            Assert.That(Count(products[1].Statistic!), Is.EqualTo((ulong)committed[1]));
            Assert.That(Count(scale.ProductionOutput!.Statistic), Is.EqualTo((ulong)(committed[0] + committed[1])));
            AssertGood(preset.Switch("A"));
        }

        [Test]
        public async Task SequenceAtUInt64MaximumCannotWrapOrReplayAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Catchweigher);
            AssertGood(await scale.Catchweigher!.ReportAsync(Item(ulong.MaxValue)));
            Assert.That((await scale.Catchweigher.ReportAsync(Item(ulong.MaxValue))).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEntryExists));
            Assert.That((await scale.Catchweigher.ReportAsync(Item(1))).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEntryExists));
            Assert.That(Count(scale.ProductionOutput!.Statistic), Is.EqualTo(1UL));
        }

        [Test]
        public async Task DynamicProductsReceiveOptionalItemAndCurrencyMembersBeforePublicationAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.AutomaticWeightPriceLabeler);
            ProductState product = scale.ProductionPreset!.AddProduct("C", new LocalizedText("Third"));
            Assert.That(product, Is.TypeOf<AutomaticWeightPriceLabelerProductState>());
            var labeler = (AutomaticWeightPriceLabelerProductState)product;
            Assert.That(labeler.LastItem, Is.TypeOf<PriceItemState>());
            ConfigurePrice(m_fixture.Context, labeler, m_fixture.Manager.NamespaceIndices.Scales);
            AssertGood(scale.ProductionPreset.Switch("C"));
            AssertGood(await scale.Catchweigher!.ReportAsync(
                Item(1) with { ProductId = "C", Price = new ScaleItemPrice(2, Euro()) }));
            Assert.That(Count(product.Statistic!), Is.EqualTo(1UL));
        }

        [Test]
        public void CurrencyRoundingIsExplicitAndCustomUnitNamespacesCannotSpoofMassConversion()
        {
            Assert.That(() => new RoundedScalePriceCalculator(-1, MidpointRounding.ToEven),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new RoundedScalePriceCalculator(29, MidpointRounding.ToEven),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new RoundedScalePriceCalculator(2, (MidpointRounding)99),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            var customGram = new EUInformation
            {
                NamespaceUri = "urn:custom:units", UnitId = ScaleUnits.Gram.UnitId
            };
            Assert.That(ScaleUnits.TryConvertMass(1000, customGram, ScaleUnits.Kilogram, out _), Is.False);
        }

        [Test]
        public async Task CompletedHopperFillsRespectDuplicatesResetsProductsAndOverflowAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.TotalizingHopper);
            var first = (TotalizingHopperProductState)scale.ProductionPreset!.ActiveProduct!;
            AssertGood(await scale.TotalizingHopper!.ReportCompletedFillAsync(Item(1)));
            Assert.That(ScaleItemPublisher.TryUnsigned(first.TipCounter!.WrappedValue, out ulong tips), Is.True);
            Assert.That(tips, Is.EqualTo(1UL));
            first.TipCounter.WrappedValue = new Variant(0UL);
            Assert.That(ServiceResult.IsBad(await scale.TotalizingHopper.ReportCompletedFillAsync(Item(1))), Is.True);
            Assert.That(first.TipCounter.WrappedValue, Is.EqualTo(new Variant(0UL)));
            AssertGood(scale.ProductionPreset.Switch("B"));
            Assert.That(ServiceResult.IsBad(await scale.TotalizingHopper.ReportCompletedFillAsync(Item(2))), Is.True);
            AssertGood(await scale.TotalizingHopper.ReportCompletedFillAsync(Item(2) with { ProductId = "B" }));
            var second = (TotalizingHopperProductState)scale.ProductionPreset.ActiveProduct!;
            second.TipCounter!.WrappedValue = new Variant(ulong.MaxValue);
            Assert.That((await scale.TotalizingHopper.ReportCompletedFillAsync(
                Item(3) with { ProductId = "B" })).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
            Assert.That(second.TipCounter.WrappedValue, Is.EqualTo(new Variant(ulong.MaxValue)));
            Assert.That(Count(scale.ProductionOutput!.Statistic), Is.EqualTo(2UL));
            Assert.That(Count(first.Statistic!), Is.EqualTo(1UL));
            Assert.That(Count(second.Statistic!), Is.EqualTo(1UL));
        }

        /// <summary>
        /// Holds an asynchronous live-node notification and verifies that synchronous writers reject without mutation.
        /// </summary>
        [Test]
        public async Task CatchweigherNotificationRejectsOverlappingSynchronousWritersAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Catchweigher);
            var product = (CatchweigherProductState)scale.ProductionPreset!.ActiveProduct!;
            ZoneState zone = scale.ProductionPreset.AddZone(
                product, new LocalizedText("Observed"), 0, 2, ScaleUnits.Kilogram);
            scale.PublishLoad(1);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var setterOrder = new List<string>();
            BaseVariableState gross = product.LastItem!.MeasuredWeight!.Gross!;
            BaseVariableState itemId = product.LastItem.ItemId!;
            Observe(gross, "last-gross", pause: true);
            Observe(itemId, "last-itemId");
            Observe(scale.Scale.RegisteredWeight!.Gross!, "registered-gross");
            Observe(scale.ProductionOutput!.Statistic.TotalPackages!.ItemCount!, "production-count");
            Observe(product.Statistic!.TotalPackages!.ItemCount!, "product-count");
            if (zone.ZoneStatistic is { } zoneStatistic)
            {
                Observe(zoneStatistic.ItemCount!, "zone-count");
            }
            BaseVariableState? zoneCount = zone.ZoneStatistic?.ItemCount;
            scale.WeightRegistered += (_, _) => setterOrder.Add("registered-event");

            void Observe(BaseVariableState variable, string name, bool pause = false)
            {
                variable.OnStateChangedAsync = async (_, _, _, _) =>
                {
                    setterOrder.Add(name);
                    if (pause)
                    {
                        entered.TrySetResult(true);
                        await release.Task.ConfigureAwait(false);
                    }
                };
            }

            try
            {
                Task<ServiceResult> pending = scale.Catchweigher!.ReportAsync(Item(1)).AsTask();
                Assert.That(await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)), Is.True);
                Assert.That(pending.IsCompleted, Is.False);
                Assert.That(gross.WrappedValue.TryGetValue(out double published), Is.True);
                Assert.That(published, Is.EqualTo(1));
                Assert.That(product.LastItem!.ItemId!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNoData));
                if (zoneCount != null)
                {
                    ServiceResult rejectedZoneValueWrite = zoneCount.WriteAttribute(
                        m_fixture.Context,
                        Attributes.Value,
                        NumericRange.Null,
                        new DataValue(Variant.From(9UL)));
                    Assert.That(
                        rejectedZoneValueWrite.StatusCode,
                        Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                    Assert.That(Count(zone.ZoneStatistic!), Is.Zero);
                }
                ServiceResult rejectedValueWrite = gross.WriteAttribute(
                    m_fixture.Context,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(Variant.From(2.0)));
                Assert.That(
                    rejectedValueWrite.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(gross.WrappedValue.TryGetValue(out published), Is.True);
                Assert.That(published, Is.EqualTo(1));
                AssertBadInvalidState(() => scale.PublishLoad(2));
                MethodState setZero = scale.Scale.SetZero!;
                ServiceResult commandResult = setZero.OnCallMethod2!(
                    m_fixture.Context, setZero, scale.Scale.NodeId, [], []);
                Assert.That(commandResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(scale.CurrentReading.Net, Is.EqualTo(1));
                Assert.That(scale.ProductionPreset!.Switch("B").StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(Count(scale.ProductionOutput!.Statistic), Is.Zero);
                AssertBadInvalidState(() => scale.ProductionOutput!.Record(9));
                AssertBadInvalidState(() => scale.ProductionOutput!.Reset());
                Assert.That(Count(scale.ProductionOutput!.Statistic), Is.Zero);
                release.TrySetResult(true);
                AssertGood(await pending.WaitAsync(TimeSpan.FromSeconds(15)));
                var expectedOrder = new List<string>
                {
                    "last-gross",
                    "last-itemId",
                    "registered-gross",
                    "production-count",
                    "product-count"
                };
                if (zone.ZoneStatistic != null)
                {
                    expectedOrder.Add("zone-count");
                }
                expectedOrder.Add("registered-event");
                Assert.That(setterOrder, Is.EqualTo(expectedOrder));
                Assert.That(product.LastItem!.ItemId!.Value, Is.EqualTo("item-1"));
                Assert.That(Count(scale.ProductionOutput!.Statistic), Is.EqualTo(1UL));
                Assert.That((await scale.Catchweigher!.ReportAsync(Item(1))).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadEntryExists));
                scale.PublishLoad(2);
                Assert.That(scale.CurrentReading.Net, Is.EqualTo(2));
            }
            finally
            {
                release.TrySetResult(true);
            }

            static void AssertBadInvalidState(Action write)
            {
                ServiceResultException exception = Assert.Throws<ServiceResultException>(write)!;
                Assert.That(exception.Result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            }
        }

        /// <summary>
        /// Verifies async reports serialize, cancellation while waiting admits no reservation,
        /// and release permits switching.
        /// </summary>
        [Test]
        public async Task CatchweigherReportsSerializeAndWaitingCancellationReleasesAdmissionAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Catchweigher);
            var product = (CatchweigherProductState)scale.ProductionPreset!.ActiveProduct!;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            product.LastItem!.MeasuredWeight!.Gross!.OnStateChangedAsync = async (_, _, _, _) =>
            {
                if (entered.TrySetResult(true))
                {
                    await release.Task.ConfigureAwait(false);
                }
            };

            try
            {
                Task<ServiceResult> first = scale.Catchweigher!.ReportAsync(Item(1)).AsTask();
                Assert.That(await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)), Is.True);
                Task<ServiceResult> second = scale.Catchweigher!.ReportAsync(Item(2)).AsTask();
                using var cancellation = new CancellationTokenSource();
                Task<ServiceResult> cancelled = scale.Catchweigher!.ReportAsync(
                    Item(3), cancellation.Token).AsTask();
                Assert.That(second.IsCompleted, Is.False);
                Assert.That(cancelled.IsCompleted, Is.False);
                Assert.That(
                    product.LastItem!.MeasuredWeight!.Gross!.WrappedValue.TryGetValue(out double firstWeight),
                    Is.True);
                Assert.That(firstWeight, Is.EqualTo(1));
                cancellation.Cancel();
                await Assert.ThatAsync(
                    async () => await cancelled.WaitAsync(TimeSpan.FromSeconds(15)),
                    Throws.InstanceOf<OperationCanceledException>());
                Assert.That(Count(scale.ProductionOutput!.Statistic), Is.Zero);
                release.TrySetResult(true);
                AssertGood(await first.WaitAsync(TimeSpan.FromSeconds(15)));
                AssertGood(await second.WaitAsync(TimeSpan.FromSeconds(15)));
                Assert.That(Count(scale.ProductionOutput!.Statistic), Is.EqualTo(2UL));
                AssertGood(scale.ProductionPreset!.Switch("B"));
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        /// <summary>
        /// Verifies failed notification leaves the sequence available for retry without registering the partial item.
        /// </summary>
        [Test]
        public async Task CatchweigherNotificationFailureLeavesSequenceRetryableAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Catchweigher);
            var product = (CatchweigherProductState)scale.ProductionPreset!.ActiveProduct!;
            int calls = 0;
            product.LastItem!.MeasuredWeight!.Gross!.OnStateChangedAsync = (_, _, _, _) =>
                Interlocked.Increment(ref calls) == 1
                    ? new ValueTask(Task.FromException(new InvalidOperationException("notification failed")))
                    : default;
            int registered = 0;
            scale.WeightRegistered += (_, _) => registered++;

            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await scale.Catchweigher!.ReportAsync(Item(1)));
            Assert.That(failure!.Message, Is.EqualTo("notification failed"));
            Assert.That(
                product.LastItem!.MeasuredWeight!.Gross!.WrappedValue.TryGetValue(out double partialWeight),
                Is.True);
            Assert.That(partialWeight, Is.EqualTo(1));
            Assert.That(Count(scale.ProductionOutput!.Statistic), Is.Zero);
            Assert.That(scale.RegisteredReading, Is.Null);
            Assert.That(registered, Is.Zero);

            AssertGood(await scale.Catchweigher!.ReportAsync(Item(1)));
            Assert.That(Count(scale.ProductionOutput!.Statistic), Is.EqualTo(1UL));
            Assert.That(registered, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies completed-fill publication retains TipCounter-first ordering and rejects
        /// a concurrent synchronous write.
        /// </summary>
        [Test]
        public async Task HopperNotificationRejectsOverlapAfterTipCounterSetterAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.TotalizingHopper);
            var product = (TotalizingHopperProductState)scale.ProductionPreset!.ActiveProduct!;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            product.TipCounter!.OnStateChangedAsync = async (_, _, _, _) =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            };

            try
            {
                Task<ServiceResult> pending = scale.TotalizingHopper!.ReportCompletedFillAsync(Item(1)).AsTask();
                Assert.That(await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)), Is.True);
                Assert.That(pending.IsCompleted, Is.False);
                Assert.That(ScaleItemPublisher.TryUnsigned(product.TipCounter.WrappedValue, out ulong tips), Is.True);
                Assert.That(tips, Is.EqualTo(1UL));
                Assert.That(scale.RegisteredReading, Is.Null);
                Assert.That(Count(scale.ProductionOutput!.Statistic), Is.Zero);
                ServiceResultException overlap = Assert.Throws<ServiceResultException>(
                    () => scale.PublishLoad(2))!;
                Assert.That(overlap.Result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                release.TrySetResult(true);
                AssertGood(await pending.WaitAsync(TimeSpan.FromSeconds(15)));
                Assert.That(scale.RegisteredReading!.Net, Is.EqualTo(1));
                Assert.That(Count(scale.ProductionOutput!.Statistic), Is.EqualTo(1UL));
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        /// <summary>
        /// Verifies synchronous Value writes are admitted concurrently while an earlier handler is pending.
        /// </summary>
        [Test]
        public async Task ProductValueWritesPreserveConcurrentSynchronousAdmissionAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Catchweigher);
            BaseVariableState variable = ((CatchweigherProductState)scale.ProductionPreset!.ActiveProduct!)
                .LastItem!.MeasuredWeight!.Gross!;
            using var bothHandlersEntered = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            int handlerCount = 0;
            variable.OnWriteValue = (
                ISystemContext _,
                NodeState _,
                NumericRange _,
                QualifiedName _,
                ref Variant _,
                ref StatusCode _,
                ref DateTimeUtc _) =>
            {
                if (Interlocked.Increment(ref handlerCount) == 2)
                {
                    bothHandlersEntered.Set();
                }
                return release.Wait(TimeSpan.FromSeconds(15))
                    ? ServiceResult.Good
                    : ServiceResult.Create(StatusCodes.BadTimeout);
            };

            DataValue firstValue = new(Variant.From(2.0));
            DataValue secondValue = new(Variant.From(3.0));
            Task<ServiceResult> first = Task.Factory.StartNew(
                () => variable.WriteAttribute(
                    m_fixture.Context, Attributes.Value, NumericRange.Null, firstValue),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            Task<ServiceResult> second = Task.Factory.StartNew(
                () => variable.WriteAttribute(
                    m_fixture.Context, Attributes.Value, NumericRange.Null, secondValue),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            try
            {
                Assert.That(bothHandlersEntered.Wait(TimeSpan.FromSeconds(15)), Is.True);
            }
            finally
            {
                release.Set();
            }

            AssertGood(await first.WaitAsync(TimeSpan.FromSeconds(15)));
            AssertGood(await second.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.That(handlerCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies asynchronous product Value writes wait for acquisition publication and own cache commit.
        /// </summary>
        [Test]
        public async Task ProductValueWriteAsyncWaitsForAcquisitionAndRejectsSyncOverlapAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Catchweigher);
            var product = (CatchweigherProductState)scale.ProductionPreset!.ActiveProduct!;
            BaseVariableState gross = product.LastItem!.MeasuredWeight!.Gross!;
            var acquisitionEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var acquisitionRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var writeEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var writeRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int writeHandlerCalls = 0;
            int synchronousHandlerCalls = 0;
            gross.OnStateChangedAsync = async (_, _, _, _) =>
            {
                if (acquisitionEntered.TrySetResult(true))
                {
                    await acquisitionRelease.Task.ConfigureAwait(false);
                }
            };
            gross.OnWriteValueAsync = async (_, _, _, _, cancellationToken) =>
            {
                Interlocked.Increment(ref writeHandlerCalls);
                writeEntered.TrySetResult(true);
                await writeRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new AttributeWriteResult(ServiceResult.Good);
            };
            gross.OnWriteValue = (
                ISystemContext _,
                NodeState _,
                NumericRange _,
                QualifiedName _,
                ref Variant _,
                ref StatusCode _,
                ref DateTimeUtc _) =>
            {
                Interlocked.Increment(ref synchronousHandlerCalls);
                return ServiceResult.Good;
            };

            Task<ServiceResult> acquisition = scale.Catchweigher!.ReportAsync(Item(1)).AsTask();
            Task<ServiceResult>? valueWrite = null;
            try
            {
                Assert.That(await acquisitionEntered.Task.WaitAsync(TimeSpan.FromSeconds(15)), Is.True);
                Assert.That(gross.WrappedValue.TryGetValue(out double duringAcquisition), Is.True);
                Assert.That(duringAcquisition, Is.EqualTo(1));
                BaseVariableState currentGross = scale.Scale.CurrentWeight!.Gross!;
                BaseVariableState registeredGross = scale.Scale.RegisteredWeight!.Gross!;
                Variant currentGrossBefore = currentGross.WrappedValue;
                Variant registeredGrossBefore = registeredGross.WrappedValue;
                ServiceResult rejectedCurrentWeightWrite = currentGross.WriteAttribute(
                    m_fixture.Context,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(Variant.From(20.0)));
                ServiceResult rejectedRegisteredWeightWrite = registeredGross.WriteAttribute(
                    m_fixture.Context,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(Variant.From(21.0)));
                Assert.That(
                    rejectedCurrentWeightWrite.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(
                    rejectedRegisteredWeightWrite.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(currentGross.WrappedValue, Is.EqualTo(currentGrossBefore));
                Assert.That(registeredGross.WrappedValue, Is.EqualTo(registeredGrossBefore));
                ServiceResult syncRejected = gross.WriteAttribute(
                    m_fixture.Context,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(Variant.From(2.0)));
                Assert.That(syncRejected.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(gross.WrappedValue.TryGetValue(out duringAcquisition), Is.True);
                Assert.That(duringAcquisition, Is.EqualTo(1));

                using var cancellation = new CancellationTokenSource();
                Task<ServiceResult> cancelledWrite = gross.WriteAttributeAsync(
                    m_fixture.Context,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(Variant.From(2.5)),
                    cancellation.Token).AsTask();
                cancellation.Cancel();
                await Assert.ThatAsync(
                    async () => await cancelledWrite.WaitAsync(TimeSpan.FromSeconds(15)),
                    Throws.InstanceOf<OperationCanceledException>());
                Assert.That(writeHandlerCalls, Is.Zero);
                Assert.That(gross.WrappedValue.TryGetValue(out duringAcquisition), Is.True);
                Assert.That(duringAcquisition, Is.EqualTo(1));

                valueWrite = gross.WriteAttributeAsync(
                    m_fixture.Context,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(Variant.From(3.0))).AsTask();
                Assert.That(valueWrite.IsCompleted, Is.False);
                Assert.That(writeHandlerCalls, Is.Zero);
                acquisitionRelease.TrySetResult(true);
                AssertGood(await acquisition.WaitAsync(TimeSpan.FromSeconds(15)));
                Assert.That(await writeEntered.Task.WaitAsync(TimeSpan.FromSeconds(15)), Is.True);
                Assert.That(valueWrite.IsCompleted, Is.False);
                DataValue beforeWrite = new();
                _ = gross.ReadAttribute(
                    m_fixture.Context,
                    Attributes.Value,
                    NumericRange.Null,
                    QualifiedName.Null,
                    ref beforeWrite);
                DataValue duringWrite = new();
                ServiceResult readResult = gross.ReadAttribute(
                    m_fixture.Context,
                    Attributes.Value,
                    NumericRange.Null,
                    QualifiedName.Null,
                    ref duringWrite);
                Assert.That(readResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(duringWrite.WrappedValue, Is.EqualTo(beforeWrite.WrappedValue));
                Assert.That(duringWrite.StatusCode, Is.EqualTo(beforeWrite.StatusCode));
                Assert.That(duringWrite.SourceTimestamp, Is.EqualTo(beforeWrite.SourceTimestamp));
                Assert.That(duringWrite.WrappedValue.TryGetValue(out duringAcquisition), Is.True);
                Assert.That(duringAcquisition, Is.EqualTo(1));

                ServiceResult asyncOverlapRejected = gross.WriteAttribute(
                    m_fixture.Context,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(Variant.From(4.0)));
                Assert.That(
                    asyncOverlapRejected.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(synchronousHandlerCalls, Is.Zero);
                Assert.That(gross.WrappedValue.TryGetValue(out duringAcquisition), Is.True);
                Assert.That(duringAcquisition, Is.EqualTo(1));
                writeRelease.TrySetResult(true);
                AssertGood(await valueWrite!.WaitAsync(TimeSpan.FromSeconds(15)));
                Assert.That(gross.WrappedValue.TryGetValue(out double committed), Is.True);
                Assert.That(committed, Is.EqualTo(3));
                Assert.That(writeHandlerCalls, Is.EqualTo(1));
                Assert.That(synchronousHandlerCalls, Is.Zero);
            }
            finally
            {
                acquisitionRelease.TrySetResult(true);
                writeRelease.TrySetResult(true);
                if (valueWrite is not null)
                {
                    await valueWrite.WaitAsync(TimeSpan.FromSeconds(15));
                }
            }
        }

        /// <summary>
        /// Verifies async writes reject synchronous fallback after acquisition and synchronous writes still reenter.
        /// </summary>
        [Test]
        public async Task ValueWriteAsyncRejectsSynchronousFallbackAndSyncWriteStillReentersAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Catchweigher);
            BaseVariableState gross = scale.ProductionPreset!.ActiveProduct!
                .LastItem!.MeasuredWeight!.Gross!;
            var acquisitionEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var acquisitionRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            gross.OnStateChangedAsync = async (_, _, _, _) =>
            {
                if (acquisitionEntered.TrySetResult(true))
                {
                    await acquisitionRelease.Task.ConfigureAwait(false);
                }
            };

            int writeHandlerCalls = 0;
            gross.OnWriteValue = (
                ISystemContext _,
                NodeState _,
                NumericRange _,
                QualifiedName _,
                ref Variant _,
                ref StatusCode _,
                ref DateTimeUtc _) =>
            {
                if (Interlocked.Increment(ref writeHandlerCalls) == 1)
                {
                    scale.PublishLoad(5);
                    ServiceResult nestedWrite = gross.WriteAttribute(
                        m_fixture.Context,
                        Attributes.Value,
                        NumericRange.Null,
                        new DataValue(Variant.From(3.0)));
                    AssertGood(nestedWrite);
                }
                return ServiceResult.Good;
            };

            Task<ServiceResult> acquisition = scale.Catchweigher!.ReportAsync(Item(1)).AsTask();
            Task<ServiceResult>? valueWrite = null;
            try
            {
                Assert.That(await acquisitionEntered.Task.WaitAsync(TimeSpan.FromSeconds(15)), Is.True);
                valueWrite = gross.WriteAttributeAsync(
                    m_fixture.Context,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(Variant.From(2.0))).AsTask();
                Assert.That(valueWrite.IsCompleted, Is.False);
                Assert.That(writeHandlerCalls, Is.Zero);

                acquisitionRelease.TrySetResult(true);
                AssertGood(await acquisition.WaitAsync(TimeSpan.FromSeconds(15)));
                Assert.That(
                    (await valueWrite.WaitAsync(TimeSpan.FromSeconds(15))).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
                Assert.That(writeHandlerCalls, Is.Zero);
                Assert.That(gross.WrappedValue.TryGetValue(out double unchanged), Is.True);
                Assert.That(unchanged, Is.EqualTo(1));
                AssertGood(gross.WriteAttribute(
                    m_fixture.Context,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(Variant.From(2.0))));
            }
            finally
            {
                acquisitionRelease.TrySetResult(true);
                if (valueWrite != null)
                {
                    _ = await valueWrite.WaitAsync(TimeSpan.FromSeconds(15));
                }
                _ = await acquisition.WaitAsync(TimeSpan.FromSeconds(15));
            }

            Assert.That(writeHandlerCalls, Is.EqualTo(2));
            Assert.That(scale.CurrentReading.Net, Is.EqualTo(5));
            Assert.That(gross.WrappedValue.TryGetValue(out double committed), Is.True);
            Assert.That(committed, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies rejected base writes retain exclusive async ownership until the custom core returns.
        /// </summary>
        [Test]
        public async Task UnsupportedAsyncValueWriteRetainsOwnershipUntilCoreReturnsAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Catchweigher);
            var variable = new UnsupportedAsyncVariableState
            {
                NodeId = new NodeId("UnsupportedAsync", 0),
                BrowseName = new QualifiedName("UnsupportedAsync", 0),
                DisplayName = new LocalizedText("UnsupportedAsync"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                Value = new Variant(1.0),
                StatusCode = StatusCodes.Uncertain,
                Timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 1)
            };
            variable.BindWriteOperationExecutor(scale.WriteOperations.NodeWriteExecutor);
            var coreResumed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var coreRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            variable.CoreResumed = coreResumed;
            variable.CoreRelease = coreRelease;

            int synchronousCalls = 0;
            variable.OnWriteValue = (
                ISystemContext _,
                NodeState _,
                NumericRange _,
                QualifiedName _,
                ref Variant _,
                ref StatusCode _,
                ref DateTimeUtc _) =>
            {
                synchronousCalls++;
                return ServiceResult.Good;
            };

            Task<ServiceResult>? pending = null;
            try
            {
                pending = variable.WriteAttributeAsync(
                    m_fixture.Context,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(new Variant(2.0))).AsTask();
                Assert.That(await coreResumed.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(pending.IsCompleted, Is.False);
                Assert.That(synchronousCalls, Is.Zero);
                Assert.That(variable.Value.GetDouble(), Is.EqualTo(1.0));
                Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
                Assert.That(variable.Timestamp, Is.EqualTo(new DateTimeUtc(2026, 1, 1, 0, 0, 1)));
                Assert.That(
                    scale.WriteOperations.TryExecuteSynchronously(() => true, out _),
                    Is.False);
                ServiceResult rejected = variable.WriteAttribute(
                    m_fixture.Context,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(new Variant(3.0)));
                Assert.That(rejected.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(synchronousCalls, Is.Zero);
                Assert.That(variable.Value.GetDouble(), Is.EqualTo(1.0));
                coreRelease.TrySetResult(true);
                Assert.That(
                    (await pending.WaitAsync(TimeSpan.FromSeconds(5))).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
                Assert.That(variable.Value.GetDouble(), Is.EqualTo(1.0));
                Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
                Assert.That(variable.Timestamp, Is.EqualTo(new DateTimeUtc(2026, 1, 1, 0, 0, 1)));
                Assert.That(scale.WriteOperations.TryExecuteSynchronously(() => true, out bool admitted), Is.True);
                Assert.That(admitted, Is.True);
                AssertGood(variable.WriteAttribute(
                    m_fixture.Context,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(new Variant(4.0))));
                Assert.That(synchronousCalls, Is.EqualTo(1));
                Assert.That(variable.Value.GetDouble(), Is.EqualTo(4.0));
            }
            finally
            {
                coreRelease.TrySetResult(true);
                if (pending != null)
                {
                    _ = await pending.WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
        }

        [Test]
        public async Task KindFacetsRequireTheirConfiguredRuntimeAsync()
        {
            await using var fixture = new ScalesServerFixture();
            await fixture.StartAsync();
            foreach (ScaleKind kind in new[]
            {
                ScaleKind.Catchweigher, ScaleKind.AutomaticWeightPriceLabeler, ScaleKind.TotalizingHopper
            })
            {
                ScaleHandle scale = await fixture.Manager.CreateScaleAsync(
                    fixture.Name(kind.ToString()), kind, b => Configure(b, kind, null));
                Assert.That(fixture.Manager.ServerProfiles.ToArray(), Does.Contain(ScalesProfiles.FacetOf(scale.Kind)));
            }
        }

        internal static void Configure(IScaleBuilder builder, ScaleKind kind, IScalePriceCalculator? calculator)
        {
            builder.WithIdentification(ScalesNodeManagerTests.Identity("EQUIPMENT"))
                .WithWeighingRange(new WeighingRangeDefinition(0, 100, 0.001, 0.001))
                .WithProductionPreset(p => p.AllowSelection().AllowManagement().WithLocking()
                    .AddProduct("A", new LocalizedText("First"))
                    .AddProduct("B", new LocalizedText("Second")).Select("A"));
            if (kind == ScaleKind.TotalizingHopper)
            {
                builder.WithCompletedFills();
            }
            else
            {
                builder.WithItemProcessing(calculator);
            }
        }

        internal static void ConfigurePrice(
            ISystemContext context, AutomaticWeightPriceLabelerProductState product, ushort ns)
        {
            product.UnitPrice!.WrappedValue = new Variant(1.0);
            var currency = (BaseVariableState)product.UnitPrice.FindChild(
                context, new QualifiedName(BrowseNames.CurrencyUnit, ns))!;
            currency.WrappedValue = new Variant(new ExtensionObject(Euro()));
        }

        internal static ScaleItemReport Item(ulong sequence, double net = 1)
        {
            return new ScaleItemReport
            {
                Sequence = sequence,
                ProductId = "A",
                ItemId = "item-" + sequence,
                Weight = new ScaleReading
                {
                    Gross = net,
                    Net = net,
                    Tare = 0,
                    EngineeringUnits = ScaleUnits.Kilogram,
                    Timestamp = DateTime.UtcNow,
                    StatusCode = StatusCodes.Good
                }
            };
        }

        internal static CurrencyUnitType Euro()
        {
            return new CurrencyUnitType
            {
                AlphabeticCode = "EUR", NumericCode = 978, Exponent = 2, Currency = new LocalizedText("Euro")
            };
        }

        private ValueTask<ScaleHandle> CreateAsync(ScaleKind kind, IScalePriceCalculator? calculator = null)
        {
            return m_fixture.Manager.CreateScaleAsync(
                m_fixture.Name("Acquisition" + ++m_scaleId), kind, b => Configure(b, kind, calculator));
        }

        private static ulong Count(StatisticState statistics)
        {
            Assert.That(ScaleItemPublisher.TryUnsigned(
                statistics.TotalPackages!.ItemCount!.WrappedValue, out ulong count), Is.True);
            return count;
        }

        private static void AssertGood(ServiceResult result)
        {
            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
        }

        private sealed class DeferredPriceCalculator : IScalePriceCalculator
        {
            public bool Started { get; private set; }

            public ScalePriceRequest? Request { get; private set; }

            public TaskCompletionSource<ScaleItemPrice?> Result { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask<ScaleItemPrice?> CalculateAsync(
                ScalePriceRequest request, CancellationToken cancellationToken = default)
            {
                Started = true;
                Request = request;
                return new ValueTask<ScaleItemPrice?>(Result.Task.WaitAsync(cancellationToken));
            }
        }

        /// <summary>
        /// Pauses after the native base core rejects while the public async write retains admission.
        /// </summary>
        private sealed class UnsupportedAsyncVariableState : BaseDataVariableState
        {
            /// <summary>
            /// Creates a test variable without a parent.
            /// </summary>
            internal UnsupportedAsyncVariableState()
                : base(null)
            {
            }

            /// <summary>
            /// Gets or sets the signal raised after the native base core returns.
            /// </summary>
            internal TaskCompletionSource<bool>? CoreResumed { get; set; }

            /// <summary>
            /// Gets or sets the signal that releases the asynchronous core.
            /// </summary>
            internal TaskCompletionSource<bool>? CoreRelease { get; set; }

            /// <summary>
            /// Runs the native write core and pauses before returning to the public entry.
            /// </summary>
            protected override async ValueTask<ServiceResult> WriteAttributeCoreAsync(
                ISystemContext context,
                uint attributeId,
                NumericRange indexRange,
                DataValue value,
                CancellationToken cancellationToken = default)
            {
                ServiceResult result = await base.WriteAttributeCoreAsync(
                    context,
                    attributeId,
                    indexRange,
                    value,
                    cancellationToken).ConfigureAwait(false);
                CoreResumed?.TrySetResult(true);
                if (CoreRelease is { } release)
                {
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                return result;
            }
        }

        /// <summary>
        /// Cancels pending equipment work, then throws from the cancellation callback.
        /// </summary>
        private sealed class ThrowingCancellationPriceCalculator(InvalidOperationException failure)
            : IScalePriceCalculator, IDisposable
        {
            /// <summary>
            /// Whether the pending report reached the equipment provider.
            /// </summary>
            public bool Started { get; private set; }

            /// <summary>
            /// Number of equipment cancellation callbacks invoked.
            /// </summary>
            public int CancellationCount { get; private set; }

            /// <inheritdoc/>
            public ValueTask<ScaleItemPrice?> CalculateAsync(
                ScalePriceRequest request, CancellationToken cancellationToken = default)
            {
                var result = new TaskCompletionSource<ScaleItemPrice?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                m_registration = cancellationToken.Register(() =>
                {
                    CancellationCount++;
                    result.TrySetCanceled(cancellationToken);
                    throw failure;
                });
                Started = true;
                return new ValueTask<ScaleItemPrice?>(result.Task);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_registration.Dispose();
            }

            /// <summary>The equipment cancellation registration.</summary>
            private CancellationTokenRegistration m_registration;
        }

        /// <summary>
        /// Records whether shutdown reached controller-owned cleanup.
        /// </summary>
        private sealed class ShutdownProbe(InvalidOperationException? failure = null) : IDisposable
        {
            /// <summary>
            /// Whether cleanup reached this resource.
            /// </summary>
            public bool Disposed { get; private set; }

            /// <inheritdoc/>
            public void Dispose()
            {
                Disposed = true;
                if (failure != null)
                {
                    throw failure;
                }
            }
        }

        private static EUInformation Millimetre => ScaleUnits.Create("MMT", "mm", "millimetre");
        /// <summary>The selected product while the first product reservation is active.</summary>
        private static readonly string[] FirstProductSelection = ["A"];

        private ScalesServerFixture m_fixture = null!;
        private int m_scaleId;
    }
}
