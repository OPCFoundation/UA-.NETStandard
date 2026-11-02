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
                "badQuality" => Item(2) with { Weight = Item(2).Weight with { StatusCode = StatusCodes.BadNoCommunication } },
                "uncertainQuality" => Item(2) with { Weight = Item(2).Weight with { StatusCode = StatusCodes.Uncertain } },
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
            Assert.That(ScaleItemPublisher.CurrencyOf(m_fixture.Context, last.ItemPrice)!.AlphabeticCode, Is.EqualTo("EUR"));
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
                await Assert.ThatAsync(async () => await pending, Throws.TypeOf<InvalidOperationException>());
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
            await Assert.ThatAsync(async () => await pending, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(Count(scale.ProductionOutput!.Statistic), Is.Zero);
            Assert.That(scale.RegisteredReading, Is.Null);
        }

        [Test]
        public async Task RuntimeShutdownCancelsPendingPriceWithoutPublicationAsync()
        {
            var calculator = new DeferredPriceCalculator();
            ScaleHandle scale = await CreateAsync(ScaleKind.AutomaticWeightPriceLabeler, calculator);
            ConfigurePrice(m_fixture.Context,
                (AutomaticWeightPriceLabelerProductState)scale.ProductionPreset!.ActiveProduct!,
                m_fixture.Manager.NamespaceIndices.Scales);
            Task<ServiceResult> pending = scale.Catchweigher!.ReportAsync(Item(1)).AsTask();
            Assert.That(calculator.Started, Is.True);
            await m_fixture.Manager.DisposeAsync();
            await Assert.ThatAsync(async () => await pending, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(Count(scale.ProductionOutput!.Statistic), Is.Zero);
            Assert.That(scale.RegisteredReading, Is.Null);
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
            Assert.That((await scale.TotalizingHopper.ReportCompletedFillAsync(Item(3) with { ProductId = "B" })).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
            Assert.That(second.TipCounter.WrappedValue, Is.EqualTo(new Variant(ulong.MaxValue)));
            Assert.That(Count(scale.ProductionOutput!.Statistic), Is.EqualTo(2UL));
            Assert.That(Count(first.Statistic!), Is.EqualTo(1UL));
            Assert.That(Count(second.Statistic!), Is.EqualTo(1UL));
        }

        [Test]
        public async Task KindFacetsRequireTheirConfiguredRuntimeAsync()
        {
            await using var fixture = new ScalesServerFixture();
            await fixture.StartAsync();
            foreach (ScaleKind kind in new[] { ScaleKind.Catchweigher, ScaleKind.AutomaticWeightPriceLabeler, ScaleKind.TotalizingHopper })
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

        internal static void ConfigurePrice(ISystemContext context, AutomaticWeightPriceLabelerProductState product, ushort ns)
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

        private async ValueTask<ScaleHandle> CreateAsync(ScaleKind kind, IScalePriceCalculator? calculator = null)
        {
            return await m_fixture.Manager.CreateScaleAsync(
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

            public ValueTask<ScaleItemPrice?> CalculateAsync(ScalePriceRequest request, CancellationToken cancellationToken = default)
            {
                Started = true;
                Request = request;
                return new ValueTask<ScaleItemPrice?>(Result.Task.WaitAsync(cancellationToken));
            }
        }

        private static EUInformation Millimetre => ScaleUnits.Create("MMT", "mm", "millimetre");
        private ScalesServerFixture m_fixture = null!;
        private int m_scaleId;
    }
}
