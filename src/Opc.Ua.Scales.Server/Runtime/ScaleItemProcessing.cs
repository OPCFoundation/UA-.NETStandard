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
using Opc.Ua.Scales.Server.Builders;

namespace Opc.Ua.Scales.Server.Runtime
{
    /// <summary>
    /// A measured dimension or volume, with its source metadata.
    /// </summary>
    /// <param name="Value">The measured value.</param>
    /// <param name="Unit">The engineering unit.</param>
    /// <param name="Timestamp">The source timestamp.</param>
    /// <param name="StatusCode">The source quality.</param>
    public sealed record ScaleItemMeasurement(
        double Value,
        EUInformation Unit,
        DateTime Timestamp,
        StatusCode StatusCode);

    /// <summary>
    /// An authoritative equipment price, or the result of a pricing policy.
    /// </summary>
    /// <param name="Amount">The monetary amount in major currency units.</param>
    /// <param name="Currency">The ISO currency metadata.</param>
    public sealed record ScaleItemPrice(decimal Amount, CurrencyUnitType Currency);

    /// <summary>
    /// One registered equipment acquisition. Sequence numbers must increase across the scale's lifetime,
    /// including product changes and counter resets.
    /// </summary>
    public sealed record ScaleItemReport
    {
        /// <summary>
        /// Gets the nonzero acquisition sequence, used to reject repeated or out-of-order reports.
        /// </summary>
        public ulong Sequence { get; init; }

        /// <summary>
        /// Gets the product selected when the acquisition was made.
        /// </summary>
        public string ProductId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the unique equipment item identifier.
        /// </summary>
        public string ItemId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the registered gross, net and tare, source quality, timestamp and mass unit.
        /// </summary>
        public ScaleReading Weight { get; init; } = new();

        /// <summary>
        /// Gets the measured height, independently of any preset height.
        /// </summary>
        public ScaleItemMeasurement? Height { get; init; }

        /// <summary>
        /// Gets the measured length in the direction of travel.
        /// </summary>
        public ScaleItemMeasurement? Length { get; init; }

        /// <summary>
        /// Gets the measured width.
        /// </summary>
        public ScaleItemMeasurement? Width { get; init; }

        /// <summary>
        /// Gets the measured volume; no weight-to-volume conversion is implicit.
        /// </summary>
        public ScaleItemMeasurement? Volume { get; init; }

        /// <summary>
        /// Gets the equipment price, which takes precedence over calculation.
        /// </summary>
        public ScaleItemPrice? Price { get; init; }
    }

    /// <summary>
    /// An immutable input to an application pricing policy.
    /// </summary>
    /// <param name="Item">The item, normalized to the scale's mass unit.</param>
    /// <param name="UnitPrice">The configured price per scale mass unit.</param>
    /// <param name="Currency">The configured currency.</param>
    public sealed record ScalePriceRequest(ScaleItemReport Item, decimal UnitPrice, CurrencyUnitType Currency);

    /// <summary>
    /// Calculates prices without performing printing or fiscal certification.
    /// </summary>
    public interface IScalePriceCalculator
    {
        /// <summary>
        /// Calculates the item price, or returns null when a price is unavailable.
        /// </summary>
        /// <param name="request">The captured product and measurement inputs.</param>
        /// <param name="cancellationToken">Cancels the calculation before publication.</param>
        ValueTask<ScaleItemPrice?> CalculateAsync(
            ScalePriceRequest request,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Multiplies normalized net mass by unit price using explicit monetary rounding.
    /// </summary>
    public sealed class RoundedScalePriceCalculator : IScalePriceCalculator
    {
        /// <summary>
        /// Creates a policy with an application-selected precision and rounding rule.
        /// </summary>
        /// <param name="decimalPlaces">The number of decimal places, from zero through 28.</param>
        /// <param name="rounding">The monetary rounding rule.</param>
        public RoundedScalePriceCalculator(int decimalPlaces, MidpointRounding rounding)
        {
            if (decimalPlaces is < 0 or > 28)
            {
                throw new ArgumentOutOfRangeException(nameof(decimalPlaces));
            }
            if (rounding is not (MidpointRounding.ToEven or MidpointRounding.AwayFromZero))
            {
                throw new ArgumentOutOfRangeException(nameof(rounding));
            }
            m_decimalPlaces = decimalPlaces;
            m_rounding = rounding;
        }

        /// <inheritdoc/>
        public ValueTask<ScaleItemPrice?> CalculateAsync(
            ScalePriceRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            cancellationToken.ThrowIfCancellationRequested();
            decimal amount = decimal.Round(
                checked((decimal)request.Item.Weight.Net * request.UnitPrice),
                m_decimalPlaces,
                m_rounding);
            return new ValueTask<ScaleItemPrice?>(new ScaleItemPrice(amount, request.Currency));
        }

        /// <summary>The selected monetary precision.</summary>
        private readonly int m_decimalPlaces;

        /// <summary>The selected rounding rule.</summary>
        private readonly MidpointRounding m_rounding;
    }

    /// <summary>
    /// Publishes registered catchweigher and price-labeler items. Reports describe observations,
    /// not commands to a sorter or printer.
    /// </summary>
    public sealed class CatchweigherController
    {
        /// <summary>
        /// Creates the controller for an explicitly enabled scale.
        /// </summary>
        internal CatchweigherController(ScaleHandle owner, IScalePriceCalculator? calculator)
        {
            m_owner = owner;
            m_calculator = calculator;
        }

        /// <summary>
        /// Gets whether the item pipeline can use an injected price calculation policy.
        /// Equipment-supplied prices are accepted independently.
        /// </summary>
        public bool HasPriceCalculator => m_calculator != null;

        /// <summary>
        /// Publishes a single equipment acquisition against its selected product.
        /// Duplicate sequences, invalid zones and incompatible units are rejected without changing counters.
        /// A missing price publishes BadNoData rather than zero.
        /// </summary>
        /// <param name="report">The completed acquisition.</param>
        /// <param name="cancellationToken">Cancels before committing the report.</param>
        public async ValueTask<ServiceResult> ReportAsync(
            ScaleItemReport report,
            CancellationToken cancellationToken = default)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }
            using var pending = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_owner.Services.StoppingToken);
            cancellationToken = pending.Token;
            cancellationToken.ThrowIfCancellationRequested();
            ServiceResult validation = ScaleItemPublisher.Normalize(report, m_owner.Unit, out ScaleItemReport normalized);
            if (ServiceResult.IsBad(validation))
            {
                return validation;
            }
            ScaleProductionPreset? preset = m_owner.ProductionPreset;
            if (preset == null)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState, "No production preset is configured.");
            }
            ProductState? captured = null;
            Variant unitPrice = Variant.Null;
            CurrencyUnitType? currency = null;
            preset.WithActiveProduct(product =>
            {
                captured = product;
                if (product is AutomaticWeightPriceLabelerProductState labeler)
                {
                    unitPrice = labeler.UnitPrice?.WrappedValue ?? Variant.Null;
                    CurrencyUnitType? configuredCurrency = ScaleItemPublisher.CurrencyOf(
                        m_owner.Services.Context, labeler.UnitPrice);
                    currency = configuredCurrency == null ? null : ScaleItemPublisher.CopyCurrency(configuredCurrency);
                }
                return true;
            });
            if (captured is not CatchweigherProductState product ||
                !string.Equals(product.ProductId?.Value, report.ProductId, StringComparison.Ordinal))
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState, "The acquisition product is not in processing.");
            }
            ScaleItemPrice? price = normalized.Price;
            if (m_owner.Kind != ScaleKind.AutomaticWeightPriceLabeler && price != null)
            {
                return ServiceResult.Create(StatusCodes.BadNotSupported, "Only a price labeler publishes prices.");
            }
            if (price == null && m_calculator != null && currency != null &&
                ScaleItemPublisher.ValidCurrency(currency) &&
                ScaleItemPublisher.TryNumber(unitPrice, out double configured) &&
                configured >= 0 && configured < (double)decimal.MaxValue)
            {
                price = await m_calculator.CalculateAsync(
                    new ScalePriceRequest(ScaleItemPublisher.CopyReport(normalized),
                        (decimal)configured, ScaleItemPublisher.CopyCurrency(currency)),
                    cancellationToken).ConfigureAwait(false);
            }
            if (price != null && (!ScaleItemPublisher.ValidCurrency(price.Currency) ||
                price.Amount < 0 || (currency != null && ScaleItemPublisher.ValidCurrency(currency) &&
                    !ScaleItemPublisher.SameCurrency(currency, price.Currency))))
            {
                return ServiceResult.Create(StatusCodes.BadInvalidArgument, "The item price or currency is incompatible.");
            }
            if (price != null)
            {
                price = price with { Currency = ScaleItemPublisher.CopyCurrency(price.Currency) };
            }
            cancellationToken.ThrowIfCancellationRequested();
            ServiceResult result = m_owner.Locked(() => preset.WithActiveProduct(current =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ReferenceEquals(current, product) ||
                    !string.Equals(current?.ProductId?.Value, report.ProductId, StringComparison.Ordinal))
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState, "The product changed during acquisition.");
                }
                if (normalized.Sequence <= m_lastSequence)
                {
                    return ServiceResult.Create(StatusCodes.BadEntryExists, "The acquisition was already superseded.");
                }
                if (product is AutomaticWeightPriceLabelerProductState labeler &&
                    ((labeler.UnitPrice?.WrappedValue ?? Variant.Null) != unitPrice ||
                    !ScaleItemPublisher.SameOptionalCurrency(
                        currency, ScaleItemPublisher.CurrencyOf(m_owner.Services.Context, labeler.UnitPrice))))
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState, "The unit price changed during calculation.");
                }
                ServiceResult zones = Classify(product, normalized.Weight.Net, out ZoneState? zone);
                if (ServiceResult.IsBad(zones))
                {
                    return zones;
                }
                string? zoneName = zone?.Name?.Value.Text;
                ScaleItemPublisher.Publish(m_owner.Services.Context, product.LastItem, normalized, zoneName, price);
                m_owner.RecordAcquisition(normalized, zoneName, price);
                if (product.Statistic != null)
                {
                    if (!m_statistics.TryGetValue(product.Statistic, out ScaleStatistics? statistics))
                    {
                        statistics = new ScaleStatistics(m_owner.Services.Context, product.Statistic);
                        m_statistics.Add(product.Statistic, statistics);
                    }
                    statistics.RecordItem(normalized, zoneName, price);
                }
                if (zone?.ZoneStatistic is { } counter)
                {
                    if (product.Statistic != null &&
                        m_statistics.TryGetValue(product.Statistic, out ScaleStatistics? statistics))
                    {
                        statistics.RecordCounter(counter, normalized.Weight.Net);
                    }
                }
                m_lastSequence = normalized.Sequence;
                return ServiceResult.Good;
            }));
            if (ServiceResult.IsGood(result))
            {
                m_owner.NotifyAcquisition(normalized.Weight);
            }
            return result;
        }

        /// <summary>
        /// Validates all zone ranges before choosing one, including shared-boundary precedence.
        /// </summary>
        private ServiceResult Classify(CatchweigherProductState product, double net, out ZoneState? selected)
        {
            selected = null;
            var children = new List<BaseInstanceState>();
            product.GetChildren(m_owner.Services.Context, children);
            var ranges = new List<(ZoneState Zone, double Low, double High)>();
            foreach (BaseInstanceState child in children)
            {
                if (child is not ZoneState zone)
                {
                    continue;
                }
                EUInformation? lowerUnit = ScaleValues.UnitsOf(m_owner.Services.Context, zone.LowerLimit);
                EUInformation? upperUnit = ScaleValues.UnitsOf(m_owner.Services.Context, zone.UpperLimit);
                if (!ScaleUnits.SameUnit(lowerUnit, upperUnit) || lowerUnit == null ||
                    !ScaleItemPublisher.TryNumber(zone.LowerLimit?.WrappedValue ?? Variant.Null, out double lower) ||
                    !ScaleItemPublisher.TryNumber(zone.UpperLimit?.WrappedValue ?? Variant.Null, out double upper) ||
                    !ScaleUnits.TryConvertMass(lower, lowerUnit, m_owner.Unit, out lower) ||
                    !ScaleUnits.TryConvertMass(upper, upperUnit, m_owner.Unit, out upper) || !(lower < upper))
                {
                    return ServiceResult.Create(StatusCodes.BadConfigurationError, "A zone has incompatible limits.");
                }
                foreach ((ZoneState _, double low, double high) in ranges)
                {
                    if (Math.Max(low, lower) < Math.Min(high, upper))
                    {
                        return ServiceResult.Create(StatusCodes.BadConfigurationError, "Zone ranges overlap.");
                    }
                }
                ranges.Add((zone, lower, upper));
            }
            foreach ((ZoneState zone, double lower, double upper) in ranges)
            {
                if (net >= lower && net <= upper &&
                    (selected == null || net == lower))
                {
                    selected = zone;
                }
            }
            return ServiceResult.Good;
        }

        /// <summary>The owning scale.</summary>
        private readonly ScaleHandle m_owner;

        /// <summary>The optional application policy.</summary>
        private readonly IScalePriceCalculator? m_calculator;

        /// <summary>Product-local accumulators.</summary>
        private readonly Dictionary<StatisticState, ScaleStatistics> m_statistics = [];

        /// <summary>The committed acquisition high-water mark, independent of production counters.</summary>
        private ulong m_lastSequence;
    }

    /// <summary>
    /// Counts completed totalizing-hopper fillings, independently of ordinary hopper level reporting.
    /// </summary>
    public sealed class TotalizingHopperController
    {
        /// <summary>
        /// Creates a controller for an explicitly configured totalizing hopper.
        /// </summary>
        internal TotalizingHopperController(ScaleHandle owner)
        {
            m_owner = owner;
        }

        /// <summary>
        /// Reports one completed downpour. Partial or interrupted fills must not be submitted.
        /// Counter resets do not reset acquisition sequence ownership.
        /// </summary>
        /// <param name="report">The completed fill, including its selected product and source weight.</param>
        /// <param name="cancellationToken">Cancels before publication.</param>
        public ValueTask<ServiceResult> ReportCompletedFillAsync(
            ScaleItemReport report,
            CancellationToken cancellationToken = default)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }
            cancellationToken.ThrowIfCancellationRequested();
            m_owner.Services.StoppingToken.ThrowIfCancellationRequested();
            ServiceResult validation = ScaleItemPublisher.Normalize(report, m_owner.Unit, out ScaleItemReport normalized);
            if (ServiceResult.IsBad(validation))
            {
                return new ValueTask<ServiceResult>(validation);
            }
            if (report.Price != null)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Create(
                    StatusCodes.BadNotSupported, "A totalizing hopper does not publish item prices."));
            }
            ScaleProductionPreset? preset = m_owner.ProductionPreset;
            if (preset == null)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Create(
                    StatusCodes.BadInvalidState, "No production preset is configured."));
            }
            ServiceResult result = m_owner.Locked(() => preset.WithActiveProduct(current =>
            {
                m_owner.Services.StoppingToken.ThrowIfCancellationRequested();
                cancellationToken.ThrowIfCancellationRequested();
                if (current is not TotalizingHopperProductState product ||
                    !string.Equals(product.ProductId?.Value, report.ProductId, StringComparison.Ordinal))
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState, "The fill product is not in processing.");
                }
                if (report.Sequence <= m_lastSequence)
                {
                    return ServiceResult.Create(StatusCodes.BadEntryExists, "The fill was already superseded.");
                }
                if (!ScaleItemPublisher.TryUnsigned(product.TipCounter!.WrappedValue, out ulong tips) ||
                    tips == ulong.MaxValue)
                {
                    return ServiceResult.Create(StatusCodes.BadOutOfRange, "The fill counter cannot be incremented.");
                }
                ScaleValues.Set(m_owner.Services.Context, product.TipCounter, Variant.From(tips + 1));
                m_owner.RecordAcquisition(normalized);
                if (product.Statistic != null)
                {
                    if (!m_statistics.TryGetValue(product.Statistic, out ScaleStatistics? statistics))
                    {
                        statistics = new ScaleStatistics(m_owner.Services.Context, product.Statistic);
                        m_statistics.Add(product.Statistic, statistics);
                    }
                    statistics.RecordItem(normalized, null, null);
                }
                m_lastSequence = report.Sequence;
                return ServiceResult.Good;
            }));
            if (ServiceResult.IsGood(result))
            {
                m_owner.NotifyAcquisition(normalized.Weight);
            }
            return new ValueTask<ServiceResult>(result);
        }

        /// <summary>The owning totalizing hopper.</summary>
        private readonly ScaleHandle m_owner;

        /// <summary>Product-local accumulators.</summary>
        private readonly Dictionary<StatisticState, ScaleStatistics> m_statistics = [];

        /// <summary>The committed fill sequence, retained across counter resets and product switches.</summary>
        private ulong m_lastSequence;
    }

    /// <summary>
    /// Shared initialization, validation and source-preserving publication for acquired items.
    /// </summary>
    internal static class ScaleItemPublisher
    {
        /// <summary>
        /// Materializes item members before the product is registered.
        /// </summary>
        internal static void Initialize(ISystemContext context, ProductState product)
        {
            product.AddStatistic(context);
            ScaleBuilder.AddStatisticMembers(context, product.Statistic!);
            if (product is AutomaticWeightPriceLabelerProductState labeler)
            {
                labeler.AddLastItem(context);
                labeler.AddUnitPrice(context);
                if (labeler.LastItem is not PriceItemState price)
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        "A price-labeler product requires a PriceItemType LastItem.");
                }
                price.AddItemPrice(context);
                Missing(price.ItemPrice);
            }
            if (product is CatchweigherProductState catchweigher)
            {
                catchweigher.AddLastItem(context);
                InitializeObservation(context, catchweigher.LastItem!);
            }
            if (product is TotalizingHopperProductState hopper && hopper.TipCounter!.WrappedValue.IsNull)
            {
                ScaleValues.Set(context, hopper.TipCounter, Variant.From(0UL));
            }
            InitializeObservation(context, product.Statistic!.LastItem!);
        }

        /// <summary>
        /// Adds the observation metadata to an item; absent observations start with BadNoData.
        /// </summary>
        internal static void InitializeObservation(ISystemContext context, WeighingItemState item)
        {
            item.AddItemId(context);
            item.AddZoneName(context);
            item.AddMeasuredHeight(context);
            item.AddMeasuredLength(context);
            item.AddMeasuredWidth(context);
            item.AddMeasuredVolume(context);
            item.MeasuredWeight!.AddGross(context);
            item.MeasuredWeight.AddNet(context);
            item.MeasuredWeight.AddTare(context);
            item.MeasuredWeight.AddWeightStable(context);
            item.MeasuredWeight.AddInsideZero(context);
            item.MeasuredWeight.AddCurrentRangeId(context);
            item.MeasuredWeight.AddWeightId(context);
            Missing(item.MeasuredWeight);
            Missing(item.MeasuredHeight);
            Missing(item.MeasuredLength);
            Missing(item.MeasuredWidth);
            Missing(item.MeasuredVolume);
        }

        /// <summary>
        /// Normalizes only known mass units, preserving source quality and acquisition time.
        /// </summary>
        internal static ServiceResult Normalize(ScaleItemReport report, EUInformation unit, out ScaleItemReport normalized)
        {
            normalized = report;
            ScaleReading weight = report.Weight;
            if (weight == null || report.Sequence == 0 || string.IsNullOrEmpty(report.ItemId) ||
                string.IsNullOrEmpty(report.ProductId) || weight.Timestamp == default ||
                weight.EngineeringUnits == null ||
                !ScaleUnits.TryConvertMass(weight.Gross, weight.EngineeringUnits, unit, out double gross) ||
                !ScaleUnits.TryConvertMass(weight.Net, weight.EngineeringUnits, unit, out double net) ||
                !ScaleUnits.TryConvertMass(weight.Tare, weight.EngineeringUnits, unit, out double tare) ||
                !Finite(gross) || !Finite(net) || !Finite(tare) || net < 0 || tare < 0 ||
                Math.Abs(gross - net - tare) > Math.Max(1, Math.Abs(gross)) * 1e-10)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidArgument, "The acquisition has invalid weight or identity.");
            }
            if (!StatusCode.IsGood(weight.StatusCode) || weight.StatusCode.Overflow || weight.Overload || weight.Underload)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState, "The acquisition is not a valid registered weight.");
            }
            if (report.Price != null && !ValidCurrency(report.Price.Currency))
            {
                return ServiceResult.Create(StatusCodes.BadInvalidArgument, "The price currency is invalid.");
            }
            foreach (ScaleItemMeasurement? dimension in new[] { report.Height, report.Length, report.Width, report.Volume })
            {
                if (dimension != null && (!Finite(dimension.Value) || dimension.Value < 0 ||
                    dimension.Unit == null || dimension.Timestamp == default))
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidArgument, "A measured dimension is invalid.");
                }
            }
            normalized = report with
            {
                Weight = weight with
                {
                    Gross = gross,
                    Net = net,
                    Tare = tare,
                    EngineeringUnits = CopyUnit(unit),
                    WeightId = weight.WeightId ?? report.ItemId
                },
                Height = CopyMeasurement(report.Height),
                Length = CopyMeasurement(report.Length),
                Width = CopyMeasurement(report.Width),
                Volume = CopyMeasurement(report.Volume),
                Price = report.Price == null ? null : report.Price with { Currency = CopyCurrency(report.Price.Currency) }
            };
            return ServiceResult.Good;
        }

        /// <summary>
        /// Separates an application's pricing input from the pending acquisition's mutable metadata.
        /// </summary>
        internal static ScaleItemReport CopyReport(ScaleItemReport report)
        {
            return report with
            {
                Weight = report.Weight with { EngineeringUnits = CopyUnit(report.Weight.EngineeringUnits!) },
                Height = CopyMeasurement(report.Height),
                Length = CopyMeasurement(report.Length),
                Width = CopyMeasurement(report.Width),
                Volume = CopyMeasurement(report.Volume),
                Price = report.Price == null ? null : report.Price with { Currency = CopyCurrency(report.Price.Currency) }
            };
        }

        /// <summary>
        /// Publishes one complete observation, clearing optional data not supplied for this item.
        /// </summary>
        internal static void Publish(
            ISystemContext context,
            WeighingItemState? item,
            ScaleItemReport report,
            string? zoneName,
            ScaleItemPrice? price)
        {
            if (item == null)
            {
                return;
            }
            PublishWeight(context, item.MeasuredWeight, report.Weight);
            SetSource(context, item.ItemId, Variant.From(report.ItemId), report.Weight.Timestamp, StatusCodes.Good);
            SetSource(context, item.ZoneName, zoneName == null ? Variant.Null : Variant.From(zoneName),
                report.Weight.Timestamp, zoneName == null ? StatusCodes.BadNoData : StatusCodes.Good);
            PublishMeasurement(context, item.MeasuredHeight, report.Height);
            PublishMeasurement(context, item.MeasuredLength, report.Length);
            PublishMeasurement(context, item.MeasuredWidth, report.Width);
            PublishMeasurement(context, item.MeasuredVolume, report.Volume);
            if (item is PriceItemState priced)
            {
                SetSource(context, priced.ItemPrice, price == null ? Variant.Null : Variant.From((double)price.Amount),
                    report.Weight.Timestamp, price == null ? StatusCodes.BadNoData : StatusCodes.Good);
                if (priced.ItemPrice?.FindChild(context, new QualifiedName(
                    BrowseNames.CurrencyUnit, item.BrowseName.NamespaceIndex)) is BaseVariableState currency)
                {
                    SetSource(context, currency,
                        price == null ? Variant.Null : Variant.From(new ExtensionObject(CopyCurrency(price.Currency))),
                        report.Weight.Timestamp, price == null ? StatusCodes.BadNoData : StatusCodes.Good);
                }
            }
        }

        /// <summary>
        /// Writes the registered structure without recomputing an equipment acquisition.
        /// </summary>
        internal static void PublishWeight(ISystemContext context, WeightItemState? item, ScaleReading weight)
        {
            if (item == null)
            {
                return;
            }
            item.Value = new WeightType { Gross = weight.Gross, Net = weight.Net, Tare = weight.Tare };
            SetSource(context, item.Overload, Variant.From(weight.Overload), weight.Timestamp, weight.StatusCode);
            SetSource(context, item.Underload, Variant.From(weight.Underload), weight.Timestamp, weight.StatusCode);
            SetSource(context, item.TareMode, Variant.From((int)weight.TareMode), weight.Timestamp, weight.StatusCode);
            SetSource(context, item.WeightId, Variant.From(weight.WeightId ?? string.Empty),
                weight.Timestamp, weight.StatusCode);
            SetSource(context, item.Gross, Variant.From(weight.Gross), weight.Timestamp, weight.StatusCode);
            SetSource(context, item.Net, Variant.From(weight.Net), weight.Timestamp, weight.StatusCode);
            SetSource(context, item.Tare, Variant.From(weight.Tare), weight.Timestamp, weight.StatusCode);
            SetSource(context, item.WeightStable,
                weight.Stable.HasValue ? Variant.From(weight.Stable.Value) : Variant.Null,
                weight.Timestamp, weight.Stable.HasValue ? weight.StatusCode : StatusCodes.BadNoData);
            SetSource(context, item.InsideZero,
                weight.InsideZero.HasValue ? Variant.From(weight.InsideZero.Value) : Variant.Null,
                weight.Timestamp, weight.InsideZero.HasValue ? weight.StatusCode : StatusCodes.BadNoData);
            SetSource(context, item.CurrentRangeId,
                weight.CurrentRangeId.HasValue ? Variant.From(weight.CurrentRangeId.Value) : Variant.Null,
                weight.Timestamp, weight.CurrentRangeId.HasValue ? weight.StatusCode : StatusCodes.BadNoData);
            ScaleValues.SetUnits(context, item,
                weight.EngineeringUnits == null ? null : CopyUnit(weight.EngineeringUnits));
            item.Timestamp = (DateTimeUtc)weight.Timestamp;
            item.StatusCode = weight.StatusCode;
            item.ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Reads currency metadata from the standard Scales property.
        /// </summary>
        internal static CurrencyUnitType? CurrencyOf(ISystemContext context, BaseVariableState? variable)
        {
            var children = new List<BaseInstanceState>();
            variable?.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is BaseVariableState property && child.BrowseName.Name == BrowseNames.CurrencyUnit &&
                    property.WrappedValue.TryGetValue(out ExtensionObject extension) &&
                    extension.TryGetValue(out CurrencyUnitType? currency))
                {
                    return currency;
                }
            }
            return null;
        }

        /// <summary>
        /// Compares the ISO identity and exponent, not localized display names.
        /// </summary>
        internal static bool SameCurrency(CurrencyUnitType left, CurrencyUnitType right)
        {
            return left.NumericCode == right.NumericCode && left.Exponent == right.Exponent &&
                string.Equals(left.AlphabeticCode, right.AlphabeticCode, StringComparison.Ordinal);
        }

        /// <summary>
        /// Compares the captured optional currency with current product configuration.
        /// </summary>
        internal static bool SameOptionalCurrency(CurrencyUnitType? left, CurrencyUnitType? right)
        {
            return left == null || right == null ? left == right : SameCurrency(left, right);
        }

        /// <summary>
        /// Checks that a supplied price carries usable ISO currency metadata.
        /// </summary>
        internal static bool ValidCurrency(CurrencyUnitType currency)
        {
            return currency != null && currency.AlphabeticCode is { Length: 3 } code &&
                code[0] is >= 'A' and <= 'Z' && code[1] is >= 'A' and <= 'Z' &&
                code[2] is >= 'A' and <= 'Z' && currency.NumericCode is >= 0 and <= 999;
        }

        /// <summary>
        /// Takes an owned copy so providers and publishers do not share mutable currency metadata.
        /// </summary>
        internal static CurrencyUnitType CopyCurrency(CurrencyUnitType currency)
        {
            return new CurrencyUnitType
            {
                NumericCode = currency.NumericCode,
                AlphabeticCode = currency.AlphabeticCode,
                Exponent = currency.Exponent,
                Currency = currency.Currency
            };
        }

        /// <summary>
        /// Reads supported Number representations without boxing the Variant.
        /// </summary>
        internal static bool TryNumber(Variant value, out double number)
        {
            number = value.TryGetValue(out double d) ? d
                : value.TryGetValue(out float f) ? f
                : value.TryGetValue(out sbyte i8) ? i8
                : value.TryGetValue(out byte u8) ? u8
                : value.TryGetValue(out short i16) ? i16
                : value.TryGetValue(out ushort u16) ? u16
                : value.TryGetValue(out int i) ? i
                : value.TryGetValue(out uint u) ? u
                : value.TryGetValue(out long l) ? l
                : value.TryGetValue(out ulong ul) ? ul
                : double.NaN;
            return Finite(number);
        }

        /// <summary>
        /// Reads all concrete unsigned integer representations permitted by UInteger.
        /// </summary>
        internal static bool TryUnsigned(Variant value, out ulong number)
        {
            if (value.TryGetValue(out number))
            {
                return true;
            }
            if (value.TryGetValue(out uint u))
            {
                number = u;
                return true;
            }
            if (value.TryGetValue(out ushort us))
            {
                number = us;
                return true;
            }
            if (value.TryGetValue(out byte b))
            {
                number = b;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Publishes an optional measurement without substituting a preset dimension.
        /// </summary>
        private static void PublishMeasurement(
            ISystemContext context,
            BaseVariableState? variable,
            ScaleItemMeasurement? measurement)
        {
            if (measurement != null)
            {
                ScaleValues.SetUnits(context, variable, CopyUnit(measurement.Unit));
            }
            SetSource(context, variable, measurement == null ? Variant.Null : Variant.From(measurement.Value),
                measurement?.Timestamp ?? DateTime.UtcNow, measurement?.StatusCode ?? StatusCodes.BadNoData);
        }

        /// <summary>
        /// Copies observation metadata before crossing an asynchronous provider boundary.
        /// </summary>
        private static ScaleItemMeasurement? CopyMeasurement(ScaleItemMeasurement? measurement)
        {
            return measurement == null ? null : measurement with { Unit = CopyUnit(measurement.Unit) };
        }

        /// <summary>
        /// Separates application-owned units from published configuration.
        /// </summary>
        private static EUInformation CopyUnit(EUInformation unit)
        {
            return new EUInformation
            {
                NamespaceUri = unit.NamespaceUri,
                UnitId = unit.UnitId,
                DisplayName = unit.DisplayName,
                Description = unit.Description
            };
        }

        /// <summary>
        /// Preserves the source metadata instead of using the runtime clock.
        /// </summary>
        private static void SetSource(
            ISystemContext context, BaseVariableState? variable, Variant value, DateTime timestamp, StatusCode status)
        {
            if (variable != null)
            {
                variable.WrappedValue = value;
                variable.Timestamp = (DateTimeUtc)timestamp;
                variable.StatusCode = status;
                variable.ClearChangeMasks(context, includeChildren: false);
            }
        }

        /// <summary>
        /// Marks an optional acquisition member as not yet observed.
        /// </summary>
        private static void Missing(BaseVariableState? variable)
        {
            if (variable != null)
            {
                variable.WrappedValue = Variant.Null;
                variable.StatusCode = StatusCodes.BadNoData;
            }
        }

        /// <summary>
        /// Tests numerical validity on every supported target framework.
        /// </summary>
        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
