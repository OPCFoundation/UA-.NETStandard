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
using Opc.Ua.Scales.Server.Builders;

namespace Opc.Ua.Scales.Server.Runtime
{
    /// <summary>
    /// Records equipment-supplied checkweigher dispositions in scale totals and the selected product's statistics.
    /// </summary>
    public sealed class CheckweigherController
    {
        /// <summary>
        /// Creates a direct reporting controller for a configured checkweigher.
        /// </summary>
        /// <param name="owner">The scale whose preset and write admission own the reports.</param>
        public CheckweigherController(ScaleHandle owner)
        {
            m_owner = owner ?? throw new ArgumentNullException(nameof(owner));
            if (owner.Kind != ScaleKind.Checkweigher)
            {
                throw new ArgumentException("Checkweigher reporting requires a checkweigher scale.", nameof(owner));
            }
        }

        /// <summary>
        /// Records a package that the equipment accepted without deriving its disposition from its weight.
        /// </summary>
        /// <param name="weight">The measured package weight in the scale's unit.</param>
        /// <param name="belowLowerToleranceLimit1">Whether the accepted package is in the TU1-TU2 band.</param>
        /// <param name="itemId">The equipment item identifier.</param>
        /// <param name="cancellationToken">Cancels before write admission.</param>
        /// <returns>The publication result.</returns>
        public ValueTask<ServiceResult> RecordAcceptedAsync(
            double weight,
            bool belowLowerToleranceLimit1 = false,
            string? itemId = null,
            CancellationToken cancellationToken = default)
        {
            if (double.IsNaN(weight) || double.IsInfinity(weight))
            {
                return new ValueTask<ServiceResult>(ServiceResult.Create(
                    StatusCodes.BadInvalidArgument, "An accepted package requires a finite measured weight."));
            }
            return RecordAsync(true, weight, belowLowerToleranceLimit1, CheckweigherRejectReason.Other,
                itemId, cancellationToken);
        }

        /// <summary>
        /// Records a package that the equipment rejected for its explicitly supplied reason.
        /// </summary>
        /// <param name="weight">The package weight, or NaN when the equipment did not weigh it.</param>
        /// <param name="reason">The equipment's rejection reason.</param>
        /// <param name="itemId">The equipment item identifier.</param>
        /// <param name="cancellationToken">Cancels before write admission.</param>
        /// <returns>The publication result.</returns>
        public ValueTask<ServiceResult> RecordRejectedAsync(
            double weight,
            CheckweigherRejectReason reason,
            string? itemId = null,
            CancellationToken cancellationToken = default)
        {
            if (double.IsInfinity(weight) || (uint)reason > (uint)CheckweigherRejectReason.Length)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Create(
                    StatusCodes.BadInvalidArgument, "The rejected weight or rejection reason is invalid."));
            }
            return RecordAsync(false, weight, false, reason, itemId, cancellationToken);
        }

        /// <summary>
        /// Resets the selected product's statistics and the scale production period.
        /// </summary>
        /// <param name="resetCondition">The equipment's reset reason.</param>
        /// <param name="cancellationToken">Cancels before write admission.</param>
        /// <returns>The reset result.</returns>
        public ValueTask<ServiceResult> ResetAsync(
            string? resetCondition = null, CancellationToken cancellationToken = default)
        {
            return m_owner.ExecuteWriteOperationAsync(async _ =>
            {
                using ScaleProductionPreset.ActiveProductReservation? reservation =
                    m_owner.ProductionPreset?.ReserveActiveProduct();
                ScaleStatistics? output = m_owner.ProductionOutput;
                if (reservation?.Product is not CheckweigherProductState product ||
                    product.Statistic is not CheckweigherStatisticState statistic || output == null)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState, "Checkweigher reset requires selected product statistics.");
                }
                ScaleStatistics recorder = await RecorderAsync(statistic).ConfigureAwait(false);
                await recorder.ResetAsync(resetCondition).ConfigureAwait(false);
                await output.ResetAsync(resetCondition).ConfigureAwait(false);
                return ServiceResult.Good;
            }, cancellationToken);
        }

        /// <summary>
        /// Initializes product-local nodes before the product is registered.
        /// </summary>
        internal static void InitializeProduct(ISystemContext context, CheckweigherProductState product)
        {
            product.AddStatistic(context);
            if (product.Statistic is not CheckweigherStatisticState statistic)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "A checkweigher product requires CheckweigherStatisticType statistics.");
            }
            ScaleBuilder.AddStatisticMembers(context, statistic);
            statistic.AddTotalPackagesAccepted(context);
            statistic.AddTotalPackagesRejected(context);
            statistic.AddPackagesAcceptedWithLowerToleranceLimit1(context);
            statistic.AddPackagesRejectedByLowerToleranceLimit1(context);
            statistic.AddPackagesRejectedByLowerToleranceLimit2(context);
            statistic.AddPackagesRejectedByMeanValueRequirement(context);
            statistic.AddPackagesRejectedByMetal(context);
            statistic.AddPackagesRejectedByVision(context);
            statistic.AddPackagesRejectedByXRay(context);
            statistic.AddPackagesRejectedByDistanceFault(context);
            statistic.AddPackagesRejectedByLength(context);
            statistic.AddPercentageLowerToleranceLimit(context);
            statistic.AddGiveAway(context);
            statistic.TotalPackagesAccepted!.AddMeanValue(context).AddStandardDeviation(context);
            statistic.TotalPackagesAccepted.StandardDeviation!.StatusCode = StatusCodes.BadNoData;
            ScaleValues.Set(context, statistic.TotalPackagesAccepted.Weighed, true);
            ScaleValues.Set(context, statistic.PackagesAcceptedWithLowerToleranceLimit1!.Weighed, true);
        }

        /// <summary>
        /// Publishes both targets while retaining the selected product and asynchronous write admission.
        /// </summary>
        private ValueTask<ServiceResult> RecordAsync(
            bool accepted, double weight, bool belowTolerance, CheckweigherRejectReason reason,
            string? itemId, CancellationToken cancellationToken)
        {
            return m_owner.ExecuteWriteOperationAsync(async _ =>
            {
                using ScaleProductionPreset.ActiveProductReservation? reservation =
                    m_owner.ProductionPreset?.ReserveActiveProduct();
                ScaleStatistics? output = m_owner.ProductionOutput;
                if (reservation?.Product is not CheckweigherProductState product ||
                    product.Statistic is not CheckweigherStatisticState statistic || output == null)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState, "Checkweigher reporting requires selected product statistics.");
                }
                ScaleStatistics recorder = await RecorderAsync(statistic).ConfigureAwait(false);
                if (accepted)
                {
                    await recorder.RecordAcceptedAsync(weight, belowTolerance, itemId).ConfigureAwait(false);
                    await output.RecordAcceptedAsync(weight, belowTolerance, itemId)
                        .ConfigureAwait(false);
                }
                else
                {
                    await recorder.RecordRejectedAsync(weight, reason, itemId).ConfigureAwait(false);
                    await output.RecordRejectedAsync(weight, reason, itemId).ConfigureAwait(false);
                }
                return ServiceResult.Good;
            }, cancellationToken);
        }

        /// <summary>
        /// Retains one recorder per product statistic so switching products preserves their independent periods.
        /// </summary>
        private async ValueTask<ScaleStatistics> RecorderAsync(CheckweigherStatisticState statistic)
        {
            if (!m_owner.CheckweigherStatistics.TryGetValue(statistic, out ScaleStatistics? recorder))
            {
                recorder = new ScaleStatistics(m_owner.Services.Context, statistic, m_owner.WriteOperations,
                    initialize: false, observationMode: m_owner.StatisticsObservationMode);
                m_owner.CheckweigherStatistics.Add(statistic, recorder);
                await recorder.InitializeAsync().ConfigureAwait(false);
            }
            return recorder;
        }

        /// <summary>The scale owning publication and product-selection admission.</summary>
        private readonly ScaleHandle m_owner;

    }

    /// <summary>
    /// Runtime of a <c>ContinuousScaleType</c> (OPC 40200 §7.24): the flow
    /// rate, the rate-control figures and the totalizers.
    /// </summary>
    public sealed class ContinuousController
    {
        internal ContinuousController(ScaleHandle owner, ContinuousScaleState scale)
        {
            m_owner = owner;
            Scale = scale;
            ISystemContext context = owner.Services.Context;
            var children = new List<BaseInstanceState>();
            scale.GetChildren(context, children);
            foreach (TotalizerState totalizer in children.OfType<TotalizerState>())
            {
                m_totals[totalizer] = 0;
                if (totalizer.ResetTotalizer != null)
                {
                    TotalizerState captured = totalizer;
                    totalizer.ResetTotalizer.OnCallMethod2 = (ctx, method, objectId, inputs, outputs) =>
                        owner.Execute(ScaleCommand.ResetTotalizer, () =>
                        {
                            ResetTotalizer(captured);
                            return ServiceResult.Good;
                        });
                }
                PublishTotal(totalizer);
            }
            m_totalizers = m_totals.Keys.ToArrayOf();
        }

        /// <summary>
        /// Gets the continuous scale node.
        /// </summary>
        public ContinuousScaleState Scale { get; }

        /// <summary>
        /// Gets the totalizers, including the master totalizer.
        /// </summary>
        public ArrayOf<TotalizerState> Totalizers => m_totalizers;

        /// <summary>
        /// Publishes the current flow rate and, when present, the load,
        /// speed and control magnitude.
        /// </summary>
        /// <param name="flowRate">The flow rate (mandatory <c>FlowRate</c>).</param>
        /// <param name="load">The belt load, when measured.</param>
        /// <param name="speed">The belt speed, when measured.</param>
        /// <param name="controlMagnitude">The controller output, when published.</param>
        public void PublishFlow(double flowRate, double? load = null, double? speed = null, double? controlMagnitude = null)
        {
            m_owner.Locked(() =>
            {
                ISystemContext context = m_owner.Services.Context;
                ScaleValues.Set(context, Scale.FlowRate, Variant.From(flowRate));
                if (load.HasValue)
                {
                    ScaleValues.Set(context, Scale.Load, Variant.From(load.Value));
                }
                if (speed.HasValue)
                {
                    ScaleValues.Set(context, Scale.Speed, Variant.From(speed.Value));
                }
                if (controlMagnitude.HasValue)
                {
                    ScaleValues.Set(context, Scale.ControlMagnitude, Variant.From(controlMagnitude.Value));
                }
            });
        }

        /// <summary>
        /// Adds a delivered quantity to every totalizer.
        /// </summary>
        /// <param name="quantity">The delivered weight.</param>
        public void Totalize(double quantity)
        {
            m_owner.Locked(() =>
            {
                lock (m_totalsLock)
                {
                    foreach (TotalizerState totalizer in m_totalizers)
                    {
                        m_totals[totalizer] += quantity;
                        PublishTotal(totalizer);
                    }
                }
            });
        }

        /// <summary>
        /// Gets the total of a totalizer.
        /// </summary>
        /// <param name="totalizer">The totalizer.</param>
        public double TotalOf(TotalizerState totalizer)
        {
            lock (m_totalsLock)
            {
                return m_totals.TryGetValue(totalizer, out double total) ? total : 0;
            }
        }

        /// <summary>
        /// Resets one totalizer.
        /// </summary>
        /// <param name="totalizer">The totalizer.</param>
        public void ResetTotalizer(TotalizerState totalizer)
        {
            m_owner.Locked(() =>
            {
                lock (m_totalsLock)
                {
                    if (m_totals.ContainsKey(totalizer))
                    {
                        m_totals[totalizer] = 0;
                        PublishTotal(totalizer);
                    }
                }
            });
        }

        private void PublishTotal(TotalizerState totalizer)
        {
            if (totalizer.TotalizedValue == null)
            {
                return;
            }
            double total = m_totals[totalizer];
            totalizer.TotalizedValue.WrappedValue = Variant.From(
                new ExtensionObject(new WeightType { Gross = total, Net = total, Tare = 0 }));
            ScaleValues.Touch(m_owner.Services.Context, totalizer.TotalizedValue);
        }

        private readonly ScaleHandle m_owner;
        private readonly Dictionary<TotalizerState, double> m_totals = [];
        private readonly ArrayOf<TotalizerState> m_totalizers;
        private readonly Lock m_totalsLock = new();
    }

    /// <summary>
    /// Runtime of a <c>LossInWeightScaleType</c> (OPC 40200 §7.26): the
    /// discharge and refill methods and the hopper figures.
    /// </summary>
    public sealed class LossInWeightController
    {
        internal LossInWeightController(ScaleHandle owner, LossInWeightScaleState scale)
        {
            m_owner = owner;
            Scale = scale;
            Bind(scale.DischargeStart, ScaleCommand.DischargeStart, discharging: true, refilling: null);
            Bind(scale.DischargeStop, ScaleCommand.DischargeStop, discharging: false, refilling: null);
            Bind(scale.RefillStart, ScaleCommand.RefillStart, discharging: null, refilling: true);
            Bind(scale.RefillStop, ScaleCommand.RefillStop, discharging: null, refilling: false);
            ScaleValues.Set(owner.Services.Context, scale.Discharging, false);
            ScaleValues.Set(owner.Services.Context, scale.Refilling, false);
        }

        /// <summary>
        /// Gets the loss-in-weight scale node.
        /// </summary>
        public LossInWeightScaleState Scale { get; }

        /// <summary>
        /// Gets whether the hopper is being discharged.
        /// </summary>
        public bool Discharging => Scale.Discharging?.Value == true;

        /// <summary>
        /// Gets whether the hopper is being refilled.
        /// </summary>
        public bool Refilling => Scale.Refilling?.Value == true;

        /// <summary>
        /// Publishes the hopper figures.
        /// </summary>
        /// <param name="hopperWeight">The weight in the hopper (mandatory).</param>
        /// <param name="fillLevelPercent">The fill level in percent (mandatory).</param>
        /// <param name="binWeight">The weight in the refill bin, when measured.</param>
        public void PublishHopper(double hopperWeight, double fillLevelPercent, double? binWeight = null)
        {
            m_owner.Locked(() =>
            {
                ISystemContext context = m_owner.Services.Context;
                ScaleValues.Set(context, Scale.HopperWeight, Variant.From(hopperWeight));
                ScaleValues.Set(context, Scale.HopperFillLevel, Variant.From(fillLevelPercent));
                if (binWeight.HasValue)
                {
                    ScaleValues.Set(context, Scale.BinWeight, Variant.From(binWeight.Value));
                }
            });
        }

        /// <summary>
        /// Ends a discharge or refill from the equipment side, for example
        /// when the hopper ran empty or full.
        /// </summary>
        /// <param name="discharging">The new discharging state, or null to keep it.</param>
        /// <param name="refilling">The new refilling state, or null to keep it.</param>
        public void SetActivity(bool? discharging, bool? refilling)
        {
            m_owner.Locked(() =>
            {
                ISystemContext context = m_owner.Services.Context;
                if (discharging.HasValue)
                {
                    ScaleValues.Set(context, Scale.Discharging, discharging.Value);
                }
                if (refilling.HasValue)
                {
                    ScaleValues.Set(context, Scale.Refilling, refilling.Value);
                }
            });
        }

        private void Bind(MethodState? method, ScaleCommand command, bool? discharging, bool? refilling)
        {
            if (method == null)
            {
                return;
            }
            method.OnCallMethod2 = (context, called, objectId, inputs, outputs) =>
                m_owner.Execute(command, () =>
                {
                    // Starting what already runs, or stopping what does not,
                    // is a state error rather than a silent no-op: the client
                    // is acting on a picture of the hopper that is out of date.
                    if (discharging.HasValue && Discharging == discharging.Value)
                    {
                        return ServiceResult.Create(
                            StatusCodes.BadInvalidState,
                            discharging.Value ? "The hopper is already discharging." : "The hopper is not discharging.");
                    }
                    if (refilling.HasValue && Refilling == refilling.Value)
                    {
                        return ServiceResult.Create(
                            StatusCodes.BadInvalidState,
                            refilling.Value ? "The hopper is already refilling." : "The hopper is not refilling.");
                    }
                    SetActivity(discharging, refilling);
                    return ServiceResult.Good;
                });
        }

        private readonly ScaleHandle m_owner;
    }

    /// <summary>
    /// Runtime of a <c>PieceCountingScaleType</c> (OPC 40200 §7.27): the
    /// reference methods and the live piece count.
    /// </summary>
    /// <remarks>
    /// The reference data belongs to the product in processing (§7.28): the
    /// methods write its <c>ReferencePieceWeight</c> and
    /// <c>NumberOfReferencePieces</c>, and <c>CurrentPieceCount</c> is the net
    /// weight divided by that reference piece weight.
    /// </remarks>
    public sealed class PieceCountingController
    {
        internal PieceCountingController(ScaleHandle owner, PieceCountingScaleState scale)
        {
            m_owner = owner;
            Scale = scale;
            if (owner.ProductionPreset is { } preset)
            {
                preset.CurrentProductsChanged += OnCurrentProductsChanged;
                if (preset.ActiveProduct is PieceCountingProductState product &&
                    TryReadReference(product, out double referenceWeight))
                {
                    m_referencePieceWeight = referenceWeight;
                }
            }
            scale.SetReferencePieceWeight?.OnCall = (context, method, objectId, weight, units) =>
                    owner.Execute(
                        ScaleCommand.SetReferencePieceWeight,
                        () => SetReferencePieceWeightCore(weight, units, context),
                        context);
            scale.SetNumberOfReferencePieces?.OnCall = (context, method, objectId, count) =>
                    owner.Execute(
                        ScaleCommand.SetNumberOfReferencePieces,
                        () => SetNumberOfReferencePiecesCore(count, context),
                        context);
            scale.StartReference?.OnCall = (context, method, objectId, count) =>
                    owner.Execute(ScaleCommand.StartReference, () => StartReferenceCore(count, context), context);
        }

        /// <summary>
        /// Gets the piece-counting scale node.
        /// </summary>
        public PieceCountingScaleState Scale { get; }

        /// <summary>
        /// Gets the reference piece weight in use, or 0 when none is set.
        /// </summary>
        public double ReferencePieceWeight => m_referencePieceWeight;

        /// <summary>
        /// Gets the current piece count.
        /// </summary>
        public ulong CurrentPieceCount { get; private set; }

        /// <summary>
        /// Sets the reference piece weight, as <c>SetReferencePieceWeight</c>
        /// would (§7.27.4), for a local operator panel.
        /// </summary>
        /// <remarks>
        /// The method argument is a UInt32 in the NodeSet; the local call
        /// takes the real weight.
        /// </remarks>
        /// <param name="weight">The weight of one piece.</param>
        /// <param name="units">The unit of <paramref name="weight"/>, or null for the scale's unit.</param>
        public ServiceResult SetReferencePieceWeight(double weight, EUInformation? units = null)
        {
            return m_owner.Execute(
                ScaleCommand.SetReferencePieceWeight,
                () => SetReferencePieceWeightCore(weight, units ?? m_owner.Unit));
        }

        /// <summary>
        /// Sets the number of reference pieces, as
        /// <c>SetNumberOfReferencePieces</c> would (§7.27.5).
        /// </summary>
        /// <param name="count">The number of reference pieces.</param>
        public ServiceResult SetNumberOfReferencePieces(uint count)
        {
            return m_owner.Execute(
                ScaleCommand.SetNumberOfReferencePieces,
                () => SetNumberOfReferencePiecesCore(count));
        }

        /// <summary>
        /// Takes the reference from the pieces on the scale, as
        /// <c>StartReference</c> would (§7.27.6).
        /// </summary>
        /// <param name="count">The number of pieces on the scale.</param>
        public ServiceResult StartReference(uint count)
        {
            return m_owner.Execute(ScaleCommand.StartReference, () => StartReferenceCore(count));
        }

        /// <summary>
        /// Publishes the count to the product captured by the caller or by a trusted equipment report.
        /// </summary>
        internal void OnWeight(WeighingResult current, PieceCountingProductState? product)
        {
            if (!TryCount(current, product, out uint pieces))
            {
                SetUnavailable(Scale.CurrentPieceCount);
                SetUnavailable(product?.CurrentItemCount);
                CurrentPieceCount = 0;
                return;
            }
            CurrentPieceCount = pieces;
            ScaleValues.Set(m_owner.Services.Context, Scale.CurrentPieceCount, Variant.From(pieces));
            if (product?.CurrentItemCount != null)
            {
                ScaleValues.Set(m_owner.Services.Context, product.CurrentItemCount, Variant.From(pieces));
            }
        }

        /// <summary>
        /// Adds one accepted registration to the selected product's configured count and weight totals.
        /// </summary>
        internal ServiceResult OnRegistered(WeighingResult current, PieceCountingProductState? product)
        {
            if (product == null)
            {
                return ServiceResult.Good;
            }
            bool hasCount = TryCount(current, product, out uint pieces);
            uint itemTotal = 0;
            bool updateItemTotal = hasCount && product.TotalizedItemCount != null;
            if (updateItemTotal)
            {
                if (!TryReadUnsigned(product.TotalizedItemCount!.WrappedValue, out uint existing))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadTypeMismatch, "The totalized piece count is not a UInt32 value.");
                }
                if (uint.MaxValue - existing < pieces)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadOutOfRange, "The totalized piece count exceeds UInt32.");
                }
                itemTotal = existing + pieces;
            }
            WeightType? weightTotal = null;
            if (product.TotalizedWeight != null)
            {
                double existing = product.TotalizedWeight.Value.Net;
                double total = existing + current.Net;
                if (double.IsNaN(total) || double.IsInfinity(total))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadOutOfRange, "The totalized weight is not finite.");
                }
                weightTotal = new WeightType { Gross = total, Net = total, Tare = 0 };
            }

            if (hasCount)
            {
                CurrentPieceCount = pieces;
                ScaleValues.Set(m_owner.Services.Context, product.RegisteredPieceCount, Variant.From(pieces));
            }
            else
            {
                SetUnavailable(product.RegisteredPieceCount);
            }
            if (updateItemTotal)
            {
                ScaleValues.Set(m_owner.Services.Context, product.TotalizedItemCount, Variant.From(itemTotal));
            }
            if (weightTotal != null)
            {
                ScaleValues.Set(
                    m_owner.Services.Context,
                    product.TotalizedWeight,
                    Variant.From(new ExtensionObject(weightTotal)));
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Resets one product's optional totals after asynchronous writer admission.
        /// </summary>
        /// <param name="productId">The product whose totals are reset.</param>
        /// <param name="cancellationToken">Cancels while waiting to enter.</param>
        public ValueTask<ServiceResult> ResetTotalsAsync(
            string productId,
            CancellationToken cancellationToken = default)
        {
            productId.ThrowIfNull(nameof(productId));
            return m_owner.ExecuteWriteOperationAsync(async ct =>
            {
                ct.ThrowIfCancellationRequested();
                if (m_owner.ProductionPreset?.Find(productId) is not PieceCountingProductState product)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadNotFound,
                        "Piece-counting product '{0}' not found.",
                        productId);
                }
                if (product.TotalizedItemCount == null && product.TotalizedWeight == null)
                {
                    return ServiceResult.Create(StatusCodes.BadNotSupported, "Product totals are not enabled.");
                }
                await ResetTotalsAsync(product).ConfigureAwait(false);
                return ServiceResult.Good;
            }, cancellationToken);
        }

        /// <summary>
        /// Adds required registered count and the configured optional totals before a product is published.
        /// </summary>
        internal static void InitializeProduct(
            ISystemContext context,
            PieceCountingProductState product,
            bool totalsEnabled,
            EUInformation units)
        {
            product.ReferencePieceWeight!.WrappedValue = Variant.From(0.0);
            ScaleValues.SetUnits(context, product.ReferencePieceWeight, units);
            product.NumberOfReferencePieces!.WrappedValue = Variant.From(0u);
            if (product.CurrentItemCount == null)
            {
                product.AddCurrentItemCount(context);
                product.CurrentItemCount!.WrappedValue = Variant.From(0u);
            }
            product.RegisteredPieceCount!.WrappedValue = Variant.From(0u);
            if (!totalsEnabled)
            {
                return;
            }
            if (product.TotalizedItemCount == null)
            {
                product.AddTotalizedItemCount(context);
                product.TotalizedItemCount!.WrappedValue = Variant.From(0u);
            }
            if (product.TotalizedWeight == null)
            {
                product.AddTotalizedWeight(context);
                product.TotalizedWeight!.WrappedValue = Variant.From(new ExtensionObject(new WeightType()));
                ScaleValues.SetUnits(context, product.TotalizedWeight, units);
            }
        }

        /// <summary>
        /// Resets totals for the newly selected product as required when processing changes product.
        /// </summary>
        private void OnCurrentProductsChanged(object? sender, EventArgs e)
        {
            if (m_owner.ProductionPreset?.ActiveProduct is PieceCountingProductState product)
            {
                m_referencePieceWeight = TryReadReference(product, out double reference) ? reference : 0;
                ResetTotals(product);
            }
            else
            {
                m_referencePieceWeight = 0;
            }
        }

        /// <summary>
        /// Clears available totals and notifies their consumers.
        /// </summary>
        private void ResetTotals(PieceCountingProductState product)
        {
            ISystemContext context = m_owner.Services.Context;
            if (product.TotalizedItemCount != null)
            {
                ScaleValues.Set(context, product.TotalizedItemCount, Variant.From(0u));
            }
            if (product.TotalizedWeight != null)
            {
                ScaleValues.Set(
                    context,
                    product.TotalizedWeight,
                    Variant.From(new ExtensionObject(new WeightType())));
            }
        }

        /// <summary>
        /// Clears available totals and awaits notification for an explicit user reset.
        /// </summary>
        private async ValueTask ResetTotalsAsync(PieceCountingProductState product)
        {
            ISystemContext context = m_owner.Services.Context;
            if (product.TotalizedItemCount != null)
            {
                await ScaleValues.SetAsync(context, product.TotalizedItemCount, Variant.From(0u))
                    .ConfigureAwait(false);
            }
            if (product.TotalizedWeight != null)
            {
                await ScaleValues.SetAsync(
                    context,
                    product.TotalizedWeight,
                    Variant.From(new ExtensionObject(new WeightType()))).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Computes a count only when this product has a valid reference piece weight.
        /// </summary>
        private bool TryCount(WeighingResult current, PieceCountingProductState? product, out uint pieces)
        {
            pieces = 0;
            double referenceWeight = product != null && TryReadReference(product, out double productReference)
                ? productReference
                : product == null ? m_referencePieceWeight : 0;
            if (!(referenceWeight > 0) || double.IsNaN(referenceWeight) || double.IsInfinity(referenceWeight))
            {
                return false;
            }
            double count = Math.Max(0, Math.Round(current.HighResolutionNet / referenceWeight));
            if (double.IsNaN(count) || double.IsInfinity(count) || count > uint.MaxValue)
            {
                return false;
            }
            pieces = (uint)count;
            return true;
        }

        /// <summary>
        /// Reads a product's configured reference without substituting a controller-wide value.
        /// </summary>
        private static bool TryReadReference(PieceCountingProductState product, out double referenceWeight)
        {
            referenceWeight = 0;
            return product.ReferencePieceWeight != null &&
                product.ReferencePieceWeight.WrappedValue.TryGetValue(out referenceWeight) &&
                !double.IsNaN(referenceWeight) && !double.IsInfinity(referenceWeight) && referenceWeight > 0;
        }

        /// <summary>
        /// Reads an unsigned OPC UA count in its common integer encodings.
        /// </summary>
        private static bool TryReadUnsigned(in Variant value, out uint count)
        {
            if (value.TryGetValue(out uint u32))
            {
                count = u32;
                return true;
            }
            if (value.TryGetValue(out ushort u16))
            {
                count = u16;
                return true;
            }
            if (value.TryGetValue(out byte u8))
            {
                count = u8;
                return true;
            }
            count = 0;
            return false;
        }

        /// <summary>
        /// Marks an uncomputable count as unavailable rather than retaining a stale number.
        /// </summary>
        private void SetUnavailable(BaseVariableState? variable)
        {
            if (variable == null)
            {
                return;
            }
            variable.WrappedValue = Variant.Null;
            variable.StatusCode = StatusCodes.BadNoData;
            variable.Timestamp = DateTimeUtc.Now;
            variable.ClearChangeMasks(m_owner.Services.Context, includeChildren: false);
        }

        private ServiceResult SetReferencePieceWeightCore(
            double weight,
            EUInformation units,
            ISystemContext? callContext = null)
        {
            PieceCountingProductState? product = ActiveProduct();
            ServiceResult access = CheckProductLock(callContext, product);
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            ServiceResult unit = ScaleValues.CheckUnit(
                units,
                m_owner.AllowedEngineeringUnits,
                m_owner.Services.Options.RequireSiUnits);
            if (ServiceResult.IsBad(unit))
            {
                return unit;
            }
            if (!(weight > 0) || double.IsInfinity(weight))
            {
                return ServiceResult.Create(StatusCodes.BadOutOfRange, "A reference piece weight must be positive.");
            }
            if (!ScaleUnits.TryConvertMass(weight, units, m_owner.Unit, out double converted))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "The unit '{0}' cannot be converted to the scale's unit.",
                    units.DisplayName.Text ?? string.Empty);
            }
            m_referencePieceWeight = converted;
            if (product != null)
            {
                ScaleValues.Set(m_owner.Services.Context, product.ReferencePieceWeight, Variant.From(converted));
                ScaleValues.SetUnits(m_owner.Services.Context, product.ReferencePieceWeight, m_owner.Unit);
            }
            return ServiceResult.Good;
        }

        private ServiceResult SetNumberOfReferencePiecesCore(uint count, ISystemContext? callContext = null)
        {
            PieceCountingProductState? product = ActiveProduct();
            ServiceResult access = CheckProductLock(callContext, product);
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            if (count == 0)
            {
                return ServiceResult.Create(
                    StatusCodes.BadOutOfRange,
                    "The number of reference pieces must be positive.");
            }
            if (product != null)
            {
                ScaleValues.Set(m_owner.Services.Context, product.NumberOfReferencePieces, Variant.From(count));
            }
            return ServiceResult.Good;
        }

        private ServiceResult StartReferenceCore(uint count, ISystemContext? callContext = null)
        {
            PieceCountingProductState? product = ActiveProduct();
            ServiceResult access = CheckProductLock(callContext, product);
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            if (count == 0)
            {
                return ServiceResult.Create(
                    StatusCodes.BadOutOfRange,
                    "The number of reference pieces must be positive.");
            }
            ServiceResult stable = m_owner.Engine.CanRegister();
            if (ServiceResult.IsBad(stable))
            {
                return stable;
            }
            double net = m_owner.Engine.Evaluate().HighResolutionNet;
            if (!(net > 0))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidState,
                    "Reference weighing needs the reference pieces on the scale.");
            }
            m_referencePieceWeight = net / count;
            if (product != null)
            {
                ScaleValues.Set(m_owner.Services.Context, product.NumberOfReferencePieces, Variant.From(count));
                ScaleValues.Set(
                    m_owner.Services.Context,
                    product.ReferencePieceWeight,
                    Variant.From(m_referencePieceWeight));
                ScaleValues.SetUnits(m_owner.Services.Context, product.ReferencePieceWeight, m_owner.Unit);
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Checks remote callers against the captured product while keeping equipment-side updates trusted.
        /// </summary>
        private ServiceResult CheckProductLock(ISystemContext? callContext, PieceCountingProductState? product)
        {
            return callContext != null && product != null
                ? ScaleValues.CheckLock(m_owner.Services, callContext, product.NodeId)
                : ServiceResult.Good;
        }

        private PieceCountingProductState? ActiveProduct()
        {
            return m_owner.ProductionPreset?.ActiveProduct as PieceCountingProductState;
        }

        private readonly ScaleHandle m_owner;
        private double m_referencePieceWeight;
    }

    /// <summary>
    /// Runtime of a <c>LaboratoryScaleType</c> (OPC 40200 §7.46): draft
    /// shields, leveling, calibration and the ionisator.
    /// </summary>
    /// <remarks>
    /// Leveling and calibration take time on real equipment: the methods set
    /// <c>LevelingRunning</c> / <c>CalibrationRunning</c>, and the application
    /// reports completion with <see cref="CompleteLeveling"/> and
    /// <see cref="CompleteCalibration"/>. Setting
    /// <see cref="CompleteImmediately"/> finishes them at once, for
    /// simulations.
    /// </remarks>
    public sealed class LaboratoryController
    {
        internal LaboratoryController(ScaleHandle owner, LaboratoryScaleState scale)
        {
            m_owner = owner;
            Scale = scale;
            scale.OpenDraftShields?.OnCall = (context, method, objectId, shield) =>
                    owner.Execute(ScaleCommand.OpenDraftShields, () => SetShields(shield, closed: false));
            scale.CloseDraftShields?.OnCall = (context, method, objectId, shield) =>
                    owner.Execute(ScaleCommand.CloseDraftShields, () => SetShields(shield, closed: true));
            BindStart(scale.StartLeveling, ScaleCommand.StartLeveling, () => scale.LevelingRunning, CompleteLeveling);
            BindStart(scale.StartCalibration, ScaleCommand.StartCalibration, () => scale.CalibrationRunning, CompleteCalibration);
            scale.StartIonisator?.OnCallMethod2 = (context, method, objectId, inputs, outputs) =>
                    owner.Execute(ScaleCommand.StartIonisator, () => SetFlag(scale.IonisatorRunning, true));
            scale.StopIonisator?.OnCallMethod2 = (context, method, objectId, inputs, outputs) =>
                    owner.Execute(ScaleCommand.StopIonisator, () => SetFlag(scale.IonisatorRunning, false));
        }

        /// <summary>
        /// Gets the laboratory scale node.
        /// </summary>
        public LaboratoryScaleState Scale { get; }

        /// <summary>
        /// Gets or sets whether leveling and calibration complete as soon as
        /// they start. Defaults to <see langword="false"/>.
        /// </summary>
        public bool CompleteImmediately { get; set; }

        /// <summary>
        /// Reports that leveling finished.
        /// </summary>
        public void CompleteLeveling()
        {
            m_owner.Locked(() => ScaleValues.Set(m_owner.Services.Context, Scale.LevelingRunning, false));
        }

        /// <summary>
        /// Reports that calibration finished; clears <c>CalibrationNeeded</c>.
        /// </summary>
        public void CompleteCalibration()
        {
            m_owner.Locked(() =>
            {
                ScaleValues.Set(m_owner.Services.Context, Scale.CalibrationRunning, false);
                ScaleValues.Set(m_owner.Services.Context, Scale.CalibrationNeeded, false);
            });
        }

        /// <summary>
        /// Publishes whether the scale needs a calibration.
        /// </summary>
        /// <param name="needed">Whether calibration is needed.</param>
        public void SetCalibrationNeeded(bool needed)
        {
            m_owner.Locked(() => ScaleValues.Set(m_owner.Services.Context, Scale.CalibrationNeeded, needed));
        }

        private ServiceResult SetShields(DraftShieldType shield, bool closed)
        {
            PropertyState<bool>?[] targets = shield switch
            {
                DraftShieldType.Right_0 => [Scale.DraftShieldRightClosed],
                DraftShieldType.Left_1 => [Scale.DraftShieldLeftClosed],
                DraftShieldType.Top_2 => [Scale.DraftShieldTopClosed],
                DraftShieldType.All_3 => [Scale.DraftShieldRightClosed, Scale.DraftShieldLeftClosed, Scale.DraftShieldTopClosed],
                _ => []
            };
            if (targets.Length == 0)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidArgument, "Unknown draft shield {0}.", shield);
            }
            if (targets.All(t => t == null))
            {
                return ServiceResult.Create(StatusCodes.BadNotSupported, "This scale has no {0} draft shield.", shield);
            }
            foreach (PropertyState<bool>? target in targets)
            {
                ScaleValues.Set(m_owner.Services.Context, target, closed);
            }
            return ServiceResult.Good;
        }

        private void BindStart(MethodState? method, ScaleCommand command, Func<PropertyState<bool>?> flag, Action complete)
        {
            if (method == null)
            {
                return;
            }
            method.OnCallMethod2 = (context, called, objectId, inputs, outputs) =>
                m_owner.Execute(command, () =>
                {
                    if (flag()?.Value == true)
                    {
                        return ServiceResult.Create(StatusCodes.BadInvalidState, "{0} is already running.", command);
                    }
                    ServiceResult result = SetFlag(flag(), true);
                    if (CompleteImmediately)
                    {
                        complete();
                    }
                    return result;
                });
        }

        private ServiceResult SetFlag(PropertyState<bool>? flag, bool value)
        {
            ScaleValues.Set(m_owner.Services.Context, flag, value);
            return ServiceResult.Good;
        }

        private readonly ScaleHandle m_owner;
    }

    /// <summary>
    /// Runtime of a <c>HopperScaleType</c> (OPC 40200 §7.47): the level
    /// limits.
    /// </summary>
    public sealed class HopperController
    {
        internal HopperController(ScaleHandle owner, HopperScaleState scale)
        {
            m_owner = owner;
            Scale = scale;
            ScaleValues.Set(owner.Services.Context, scale.LimitMax, false);
            ScaleValues.Set(owner.Services.Context, scale.LimitMin, false);
        }

        /// <summary>
        /// Gets the hopper scale node.
        /// </summary>
        public HopperScaleState Scale { get; }

        /// <summary>
        /// Publishes whether the maximum and minimum levels are reached, and
        /// the levels themselves when present.
        /// </summary>
        /// <param name="limitMax">Whether the maximum level is reached (mandatory).</param>
        /// <param name="limitMin">Whether the minimum level is reached (mandatory).</param>
        /// <param name="levelMax">The maximum level, when published.</param>
        /// <param name="levelMin">The minimum level, when published.</param>
        public void PublishLevels(bool limitMax, bool limitMin, double? levelMax = null, double? levelMin = null)
        {
            m_owner.Locked(() =>
            {
                ISystemContext context = m_owner.Services.Context;
                ScaleValues.Set(context, Scale.LimitMax, limitMax);
                ScaleValues.Set(context, Scale.LimitMin, limitMin);
                if (levelMax.HasValue)
                {
                    ScaleValues.Set(context, Scale.LevelMax, Variant.From(levelMax.Value));
                }
                if (levelMin.HasValue)
                {
                    ScaleValues.Set(context, Scale.LevelMin, Variant.From(levelMin.Value));
                }
            });
        }

        private readonly ScaleHandle m_owner;
    }

    /// <summary>
    /// Runtime of an <c>AutomaticFillingScaleType</c> (OPC 40200 §7.9): the
    /// deviation of a filling from its target and the tolerance state.
    /// </summary>
    public sealed class AutomaticFillingController
    {
        internal AutomaticFillingController(ScaleHandle owner, AutomaticFillingScaleState scale)
        {
            m_owner = owner;
            Scale = scale;
        }

        /// <summary>
        /// Gets the automatic filling scale node.
        /// </summary>
        public AutomaticFillingScaleState Scale { get; }

        /// <summary>
        /// Evaluates a completed filling against the target weight and
        /// tolerances of the product in processing, and publishes the
        /// deviation and tolerance state.
        /// </summary>
        /// <param name="filledWeight">The net weight filled.</param>
        /// <returns>The tolerance state.</returns>
        public ToleranceState EvaluateFilling(double filledWeight)
        {
            return m_owner.Locked(() => EvaluateFillingCore(filledWeight));
        }

        private ToleranceState EvaluateFillingCore(double filledWeight)
        {
            double target = double.NaN;
            double plus = 0;
            double minus = 0;
            if (m_owner.ProductionPreset?.ActiveProduct is AutomaticFillingProductState product &&
                product.TargetWeight is { } targetWeight)
            {
                target = ToDouble(targetWeight.WrappedValue);
                plus = ToDouble(targetWeight.PlusTolerance?.WrappedValue ?? Variant.Null);
                minus = ToDouble(targetWeight.MinusTolerance?.WrappedValue ?? Variant.Null);
            }
            if (double.IsNaN(target))
            {
                return ToleranceState.In_0;
            }
            double deviation = filledWeight - target;
            ToleranceState state = deviation > plus && plus >= 0 && !double.IsNaN(plus)
                ? ToleranceState.Over_2
                : deviation < -minus && minus >= 0 && !double.IsNaN(minus)
                    ? ToleranceState.Under_1
                    : ToleranceState.In_0;
            ScaleValues.Set(m_owner.Services.Context, Scale.Deviation, Variant.From(deviation));
            ScaleValues.Set(m_owner.Services.Context, Scale.ToleranceState, state);
            return state;
        }

        private static double ToDouble(Variant value)
        {
            return value.TryGetValue(out double d) ? d
                : value.TryGetValue(out float f) ? f
                : value.TryGetValue(out int i) ? i
                : value.TryGetValue(out uint u) ? u
                : double.NaN;
        }

        private readonly ScaleHandle m_owner;
    }

    /// <summary>
    /// Runtime of a <c>VehicleScaleType</c> (OPC 40200 §7.48): inbound,
    /// outbound and one-pass weighing of the vehicle products.
    /// </summary>
    /// <remarks>
    /// A vehicle is the vehicle product whose <c>VehicleId</c> (or, when
    /// that is not set, whose product id) matches. Inbound weighing stores
    /// the gross weight in <c>InboundWeight</c>; outbound weighing stores it
    /// in <c>OutboundWeight</c> and computes <c>DeltaWeight</c> as inbound
    /// minus outbound, or outbound minus the preset <c>Tare</c> when there was
    /// no inbound weighing; one-pass weighing always uses the preset tare.
    /// </remarks>
    public sealed class VehicleController
    {
        /// <summary>
        /// Binds vehicle weighing methods and the optional application aggregation policy.
        /// </summary>
        internal VehicleController(
            ScaleHandle owner,
            VehicleScaleState scale,
            Func<string, VehicleWeightAggregation>? aggregation = null)
        {
            m_owner = owner;
            m_aggregation = aggregation;
            Scale = scale;
            scale.InboundWeighing?.OnCall = (context, method, objectId, vehicleId) =>
                    owner.Execute(ScaleCommand.InboundWeighing, () => Inbound(vehicleId, context));
            scale.OutboundWeighing?.OnCall = (context, method, objectId, vehicleId) =>
                    owner.Execute(
                        ScaleCommand.OutboundWeighing,
                        () => Outbound(vehicleId, onePass: false, context));
            scale.OnePassWeighing?.OnCall = (context, method, objectId, vehicleId) =>
                    owner.Execute(
                        ScaleCommand.OnePassWeighing,
                        () => Outbound(vehicleId, onePass: true, context));
            scale.InboundWeighing?.OnCallMethod2Async = async (context, method, objectId, inputs, outputs, ct) =>
                {
                    if (inputs.Count != 1 || !inputs[0].TryGetValue(out string? vehicleId))
                    {
                        return ServiceResult.Create(StatusCodes.BadInvalidArgument, "A vehicle id is required.");
                    }
                    return await InboundAsync(vehicleId, context, ct).ConfigureAwait(false);
                };
            scale.OutboundWeighing?.OnCallMethod2Async = async (context, method, objectId, inputs, outputs, ct) =>
                {
                    if (inputs.Count != 1 || !inputs[0].TryGetValue(out string? vehicleId))
                    {
                        return ServiceResult.Create(StatusCodes.BadInvalidArgument, "A vehicle id is required.");
                    }
                    return await OutboundAsync(vehicleId, onePass: false, context, ct).ConfigureAwait(false);
                };
            scale.OnePassWeighing?.OnCallMethod2Async = async (context, method, objectId, inputs, outputs, ct) =>
                {
                    if (inputs.Count != 1 || !inputs[0].TryGetValue(out string? vehicleId))
                    {
                        return ServiceResult.Create(StatusCodes.BadInvalidArgument, "A vehicle id is required.");
                    }
                    return await OutboundAsync(vehicleId, onePass: true, context, ct).ConfigureAwait(false);
                };
        }

        /// <summary>
        /// Gets the vehicle scale node.
        /// </summary>
        public VehicleScaleState Scale { get; }

        /// <summary>
        /// Weighs a vehicle on its way in, as <c>InboundWeighing</c> would
        /// (§7.48.4), for a local operator panel.
        /// </summary>
        /// <param name="vehicleId">The vehicle.</param>
        public ServiceResult InboundWeighing(string vehicleId)
        {
            return m_owner.Execute(ScaleCommand.InboundWeighing, () => Inbound(vehicleId));
        }

        /// <summary>
        /// Weighs a vehicle on its way out and computes <c>DeltaWeight</c>, as
        /// <c>OutboundWeighing</c> would (§7.48.5).
        /// </summary>
        /// <param name="vehicleId">The vehicle.</param>
        public ServiceResult OutboundWeighing(string vehicleId)
        {
            return m_owner.Execute(ScaleCommand.OutboundWeighing, () => Outbound(vehicleId, onePass: false));
        }

        /// <summary>
        /// Weighs a vehicle once against its preset tare, as
        /// <c>OnePassWeighing</c> would (§7.48.6).
        /// </summary>
        /// <param name="vehicleId">The vehicle.</param>
        public ServiceResult OnePassWeighing(string vehicleId)
        {
            return m_owner.Execute(ScaleCommand.OnePassWeighing, () => Outbound(vehicleId, onePass: true));
        }

        /// <summary>
        /// Registers an inbound weighing and awaits its state notifications without totalizing an incomplete journey.
        /// </summary>
        /// <param name="vehicleId">The vehicle.</param>
        /// <param name="cancellationToken">Cancels while waiting for scale admission.</param>
        public ValueTask<ServiceResult> InboundWeighingAsync(
            string vehicleId,
            CancellationToken cancellationToken = default)
        {
            vehicleId.ThrowIfNull(nameof(vehicleId));
            return InboundAsync(vehicleId, null, cancellationToken);
        }

        /// <summary>
        /// Completes an outbound weighing, preserving its signed delta and applying the vehicle's aggregation rule.
        /// </summary>
        /// <param name="vehicleId">The vehicle.</param>
        /// <param name="cancellationToken">Cancels while waiting for scale admission.</param>
        public ValueTask<ServiceResult> OutboundWeighingAsync(
            string vehicleId,
            CancellationToken cancellationToken = default)
        {
            vehicleId.ThrowIfNull(nameof(vehicleId));
            return OutboundAsync(vehicleId, onePass: false, null, cancellationToken);
        }

        /// <summary>
        /// Completes a one-pass weighing against the preset tare and applies the vehicle's aggregation rule.
        /// </summary>
        /// <param name="vehicleId">The vehicle.</param>
        /// <param name="cancellationToken">Cancels while waiting for scale admission.</param>
        public ValueTask<ServiceResult> OnePassWeighingAsync(
            string vehicleId,
            CancellationToken cancellationToken = default)
        {
            vehicleId.ThrowIfNull(nameof(vehicleId));
            return OutboundAsync(vehicleId, onePass: true, null, cancellationToken);
        }

        /// <summary>
        /// Selects signed or absolute aggregation for one vehicle without resetting its accumulated weight.
        /// </summary>
        /// <param name="vehicleId">The vehicle.</param>
        /// <param name="aggregation">The rule for subsequent completed transactions.</param>
        /// <param name="cancellationToken">Cancels while waiting for scale admission.</param>
        public ValueTask<ServiceResult> SetTotalWeightAggregationAsync(
            string vehicleId,
            VehicleWeightAggregation aggregation,
            CancellationToken cancellationToken = default)
        {
            vehicleId.ThrowIfNull(nameof(vehicleId));
            if (aggregation is not (VehicleWeightAggregation.Signed or VehicleWeightAggregation.Absolute))
            {
                throw new ArgumentOutOfRangeException(nameof(aggregation));
            }
            return m_owner.ExecuteWriteOperationAsync(async ct =>
            {
                ct.ThrowIfCancellationRequested();
                if (!TryFind(vehicleId, out VehicleProductState? vehicle, out ServiceResult? error))
                {
                    return error!;
                }
                bool addTotal = vehicle!.TotalWeight == null;
                bool addResetDate = vehicle.TotalWeightResetDate == null;
                EnsureTotals(vehicle);
                m_vehicleAggregations[vehicle!.NodeId] = aggregation;
                if (addTotal)
                {
                    await ScaleValues.TouchAsync(m_owner.Services.Context, vehicle.TotalWeight).ConfigureAwait(false);
                }
                if (addResetDate)
                {
                    await ScaleValues.TouchAsync(m_owner.Services.Context, vehicle.TotalWeightResetDate)
                        .ConfigureAwait(false);
                }
                return ServiceResult.Good;
            }, cancellationToken);
        }

        /// <summary>
        /// Resets one vehicle's total and reset date without discarding an unfinished inbound weighing.
        /// </summary>
        /// <param name="vehicleId">The vehicle.</param>
        /// <param name="cancellationToken">Cancels while waiting for scale admission.</param>
        public ValueTask<ServiceResult> ResetTotalWeightAsync(
            string vehicleId,
            CancellationToken cancellationToken = default)
        {
            vehicleId.ThrowIfNull(nameof(vehicleId));
            return m_owner.ExecuteWriteOperationAsync(async ct =>
            {
                ct.ThrowIfCancellationRequested();
                if (!TryFind(vehicleId, out VehicleProductState? vehicle, out ServiceResult? error))
                {
                    return error!;
                }
                if (vehicle!.TotalWeight == null)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadNotSupported, "Vehicle '{0}' has no total weight.", vehicleId);
                }
                EnsureTotals(vehicle);
                ISystemContext context = m_owner.Services.Context;
                await ScaleValues.SetAsync(context, vehicle.TotalWeight, new WeightType()).ConfigureAwait(false);
                await ScaleValues.SetAsync(context, vehicle.TotalWeightResetDate, DateTimeUtc.Now)
                    .ConfigureAwait(false);
                return ServiceResult.Good;
            }, cancellationToken);
        }

        /// <summary>
        /// Creates configured total properties before a vehicle enters the address space.
        /// </summary>
        internal static void InitializeTotals(ISystemContext context, VehicleProductState vehicle)
        {
            DateTimeUtc timestamp = DateTimeUtc.Now;
            if (vehicle.TotalWeight == null)
            {
                vehicle.AddTotalWeight(context);
                vehicle.TotalWeight!.Value = new WeightType();
                vehicle.TotalWeight.Timestamp = timestamp;
                vehicle.TotalWeight.StatusCode = StatusCodes.Good;
            }
            if (vehicle.TotalWeightResetDate == null)
            {
                vehicle.AddTotalWeightResetDate(context);
                vehicle.TotalWeightResetDate!.Value = timestamp;
                vehicle.TotalWeightResetDate.Timestamp = timestamp;
                vehicle.TotalWeightResetDate.StatusCode = StatusCodes.Good;
            }
        }

        /// <summary>
        /// Retains synchronous local-call compatibility without blocking on asynchronous notifications.
        /// </summary>
        private ServiceResult Inbound(string vehicleId, ISystemContext? callContext = null)
        {
            ServiceResult result = PrepareInbound(
                vehicleId, callContext, out VehicleProductState? vehicle, out double gross);
            if (ServiceResult.IsBad(result))
            {
                return result;
            }
            Publish(vehicle!.InboundWeight, gross);
            ScaleValues.Set(m_owner.Services.Context, vehicle.InboundScale, m_owner.NodeId);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Validates and retains an inbound weighing while the caller owns the scale writer.
        /// </summary>
        private ServiceResult PrepareInbound(
            string vehicleId,
            ISystemContext? callContext,
            out VehicleProductState? vehicle,
            out double gross)
        {
            gross = 0;
            if (!TryFind(vehicleId, out vehicle, out ServiceResult? error))
            {
                return error!;
            }
            ServiceResult access = CheckProductLock(callContext, vehicle!);
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            ServiceResult check = m_owner.Engine.CanRegister();
            if (ServiceResult.IsBad(check))
            {
                return check;
            }
            WeighingResult current = m_owner.Engine.Evaluate();
            ISystemContext context = m_owner.Services.Context;
            Ensure(vehicle!, v => v.InboundWeight, v => v.AddInboundWeight(context));
            Ensure(vehicle!, v => v.InboundScale, v => v.AddInboundScale(context));
            gross = current.Gross;
            m_inbound[vehicle!.NodeId] = gross;
            return ServiceResult.Good;
        }

        /// <summary>
        /// Publishes a completed transaction for existing synchronous callers.
        /// </summary>
        private ServiceResult Outbound(string vehicleId, bool onePass, ISystemContext? callContext = null)
        {
            ServiceResult result = PrepareOutbound(vehicleId, onePass, callContext, out VehicleCompletion? completion);
            if (ServiceResult.IsBad(result))
            {
                return result;
            }
            VehicleProductState vehicle = completion!.Vehicle;
            ISystemContext context = m_owner.Services.Context;
            Publish(vehicle.OutboundWeight, completion.Outbound);
            ScaleValues.Set(context, vehicle.OutboundScale, m_owner.NodeId);
            Publish(vehicle.DeltaWeight, completion.Delta);
            if (GetIsFilling(vehicle) is { } isFilling)
            {
                ScaleValues.Set(context, isFilling, completion.Delta < 0);
            }
            if (completion.Total != null)
            {
                ScaleValues.Set(context, vehicle.TotalWeight, completion.Total);
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Validates a completed transaction before consuming its inbound weighing or changing any published result.
        /// </summary>
        private ServiceResult PrepareOutbound(
            string vehicleId,
            bool onePass,
            ISystemContext? callContext,
            out VehicleCompletion? completion)
        {
            completion = null;
            if (!TryFind(vehicleId, out VehicleProductState? vehicle, out ServiceResult? error))
            {
                return error!;
            }
            ServiceResult access = CheckProductLock(callContext, vehicle!);
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            ServiceResult check = m_owner.Engine.CanRegister();
            if (ServiceResult.IsBad(check))
            {
                return check;
            }
            double outbound = m_owner.Engine.Evaluate().Gross;
            double delta;
            bool hasInbound = !onePass && m_inbound.TryGetValue(vehicle!.NodeId, out _);
            if (hasInbound)
            {
                delta = m_inbound[vehicle!.NodeId] - outbound;
            }
            else if (TryReadTare(vehicle!, out double tare))
            {
                delta = outbound - tare;
            }
            else
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidState,
                    onePass
                        ? "One-pass weighing needs the vehicle's preset tare."
                        : "Vehicle '{0}' has neither an inbound weighing nor a preset tare.",
                    vehicleId);
            }

            WeightType? total = null;
            if (vehicle!.TotalWeight != null)
            {
                string? id = vehicle.VehicleId?.Value;
                string aggregationId = string.IsNullOrEmpty(id) ? vehicle.ProductId!.Value : id;
                VehicleWeightAggregation aggregation = m_vehicleAggregations.TryGetValue(
                    vehicle.NodeId, out VehicleWeightAggregation configured)
                        ? configured
                        : m_aggregation?.Invoke(aggregationId) ?? VehicleWeightAggregation.Signed;
                if (aggregation is not (VehicleWeightAggregation.Signed or VehicleWeightAggregation.Absolute))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadConfigurationError, "The vehicle aggregation rule is invalid.");
                }
                double weight = aggregation == VehicleWeightAggregation.Absolute ? Math.Abs(delta) : delta;
                double sum = (vehicle.TotalWeight.Value?.Net ?? 0) + weight;
                if (double.IsNaN(sum) || double.IsInfinity(sum))
                {
                    return ServiceResult.Create(StatusCodes.BadOutOfRange, "The vehicle total is not finite.");
                }
                total = new WeightType { Gross = sum, Net = sum, Tare = 0 };
            }

            ISystemContext context = m_owner.Services.Context;
            Ensure(vehicle!, v => v.OutboundScale, v => v.AddOutboundScale(context));
            if (hasInbound)
            {
                m_inbound.Remove(vehicle.NodeId);
            }
            completion = new VehicleCompletion(vehicle, outbound, delta, total);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Registers an inbound weighing through asynchronous admission and notifications.
        /// </summary>
        private ValueTask<ServiceResult> InboundAsync(
            string vehicleId,
            ISystemContext? callContext,
            CancellationToken cancellationToken)
        {
            VehicleProductState? vehicle = null;
            double gross = 0;
            return ExecuteAsync(
                ScaleCommand.InboundWeighing,
                () => PrepareInbound(vehicleId, callContext, out vehicle, out gross),
                async () =>
                {
                    await PublishAsync(vehicle!.InboundWeight, gross).ConfigureAwait(false);
                    await ScaleValues.SetAsync(m_owner.Services.Context, vehicle.InboundScale, m_owner.NodeId)
                        .ConfigureAwait(false);
                },
                cancellationToken);
        }

        /// <summary>
        /// Completes a weighing through asynchronous admission and notifications.
        /// </summary>
        private ValueTask<ServiceResult> OutboundAsync(
            string vehicleId,
            bool onePass,
            ISystemContext? callContext,
            CancellationToken cancellationToken)
        {
            VehicleCompletion? completion = null;
            return ExecuteAsync(
                onePass ? ScaleCommand.OnePassWeighing : ScaleCommand.OutboundWeighing,
                () => PrepareOutbound(vehicleId, onePass, callContext, out completion),
                async () =>
                {
                    VehicleProductState vehicle = completion!.Vehicle;
                    ISystemContext context = m_owner.Services.Context;
                    await PublishAsync(vehicle.OutboundWeight, completion.Outbound).ConfigureAwait(false);
                    await ScaleValues.SetAsync(context, vehicle.OutboundScale, m_owner.NodeId).ConfigureAwait(false);
                    await PublishAsync(vehicle.DeltaWeight, completion.Delta).ConfigureAwait(false);
                    if (GetIsFilling(vehicle) is { } isFilling)
                    {
                        await ScaleValues.SetAsync(context, isFilling, completion.Delta < 0).ConfigureAwait(false);
                    }
                    if (completion.Total != null)
                    {
                        await ScaleValues.SetAsync(context, vehicle.TotalWeight, completion.Total)
                            .ConfigureAwait(false);
                    }
                },
                cancellationToken);
        }

        /// <summary>
        /// Checks the command interceptor and holds admission through the final asynchronous notification.
        /// </summary>
        private ValueTask<ServiceResult> ExecuteAsync(
            ScaleCommand command,
            Func<ServiceResult> prepare,
            Func<ValueTask> publish,
            CancellationToken cancellationToken)
        {
            return m_owner.ExecuteWriteOperationAsync(async ct =>
            {
                ct.ThrowIfCancellationRequested();
                ServiceResult veto = m_owner.CommandInterceptor?.Invoke(command) ?? ServiceResult.Good;
                if (ServiceResult.IsBad(veto))
                {
                    return veto;
                }
                ServiceResult result = prepare();
                if (ServiceResult.IsGood(result))
                {
                    await publish().ConfigureAwait(false);
                }
                return result;
            }, cancellationToken);
        }

        /// <summary>
        /// Gets the optional flag without changing the existing signed delta convention.
        /// </summary>
        private PropertyState<bool>? GetIsFilling(VehicleProductState vehicle)
        {
            return vehicle.DeltaWeight?.FindChild(
                m_owner.Services.Context,
                new QualifiedName("IsFilling", m_owner.Services.Namespaces.Scales)) as PropertyState<bool>;
        }

        /// <summary>
        /// Materializes and registers totals enabled after the product was created.
        /// </summary>
        private void EnsureTotals(VehicleProductState vehicle)
        {
            bool addTotal = vehicle.TotalWeight == null;
            bool addResetDate = vehicle.TotalWeightResetDate == null;
            InitializeTotals(m_owner.Services.Context, vehicle);
            if (addTotal)
            {
                m_owner.Services.Register(vehicle.TotalWeight!);
            }
            if (addResetDate)
            {
                m_owner.Services.Register(vehicle.TotalWeightResetDate!);
            }
            if (addTotal || addResetDate)
            {
                m_owner.Services.BindProductWriteGuards(vehicle, m_owner.WriteOperations);
            }
        }

        /// <summary>
        /// Authorizes remote weighing against the selected vehicle before changing its values or inbound cache.
        /// </summary>
        private ServiceResult CheckProductLock(ISystemContext? callContext, VehicleProductState vehicle)
        {
            return callContext != null
                ? ScaleValues.CheckLock(m_owner.Services, callContext, vehicle.NodeId)
                : ServiceResult.Good;
        }

        /// <summary>
        /// Resolves a nonempty vehicle or product identifier without matching an unset vehicle id.
        /// </summary>
        private bool TryFind(string vehicleId, out VehicleProductState? vehicle, out ServiceResult? error)
        {
            vehicle = null;
            error = null;
            if (string.IsNullOrEmpty(vehicleId))
            {
                error = ServiceResult.Create(StatusCodes.BadInvalidArgument, "A vehicle id is required.");
                return false;
            }
            ScaleProductionPreset? preset = m_owner.ProductionPreset;
            if (preset == null)
            {
                error = ServiceResult.Create(StatusCodes.BadInvalidState, "The vehicle scale has no production preset.");
                return false;
            }
            foreach (ProductState product in preset.Products.Values)
            {
                if (product is VehicleProductState candidate &&
                    (string.Equals(candidate.VehicleId?.Value, vehicleId, StringComparison.Ordinal) ||
                     string.Equals(candidate.ProductId?.Value, vehicleId, StringComparison.Ordinal)))
                {
                    vehicle = candidate;
                    return true;
                }
            }
            error = ServiceResult.Create(StatusCodes.BadNotFound, "No vehicle with id '{0}' is known.", vehicleId);
            return false;
        }

        private static bool TryReadTare(VehicleProductState vehicle, out double tare)
        {
            tare = 0;
            Variant value = vehicle.Tare?.WrappedValue ?? Variant.Null;
            if (value.TryGetValue(out double d))
            {
                tare = d;
                return true;
            }
            if (value.TryGetValue(out float f))
            {
                tare = f;
                return true;
            }
            return false;
        }

        private void Ensure<T>(VehicleProductState vehicle, Func<VehicleProductState, T?> get, Action<VehicleProductState> add)
            where T : NodeState
        {
            if (get(vehicle) == null)
            {
                add(vehicle);
                if (get(vehicle) is { } node)
                {
                    m_owner.Services.Register(node);
                }
            }
        }

        /// <summary>
        /// Publishes a vehicle measurement for existing synchronous callers.
        /// </summary>
        private void Publish(WeightItemState? item, double weight)
        {
            ScaleItemPublisher.PublishWeight(m_owner.Services.Context, item, Reading(weight));
        }

        /// <summary>
        /// Publishes an asynchronously completed vehicle measurement.
        /// </summary>
        private ValueTask PublishAsync(WeightItemState? item, double weight)
        {
            return ScaleItemPublisher.PublishWeightAsync(m_owner.Services.Context, item, Reading(weight));
        }

        /// <summary>
        /// Represents the accepted vehicle measurement with the scale's engineering units.
        /// </summary>
        private ScaleReading Reading(double weight)
        {
            return new ScaleReading
            {
                Gross = weight,
                Net = weight,
                Tare = 0,
                Stable = true,
                EngineeringUnits = m_owner.Unit,
                Timestamp = (DateTime)DateTimeUtc.Now,
                StatusCode = StatusCodes.Good
            };
        }

        /// <summary>
        /// The validated values of one completed transaction.
        /// </summary>
        /// <param name="Vehicle">The matched product.</param>
        /// <param name="Outbound">The outbound gross measurement.</param>
        /// <param name="Delta">The unchanged signed transaction delta.</param>
        /// <param name="Total">The new total, when enabled.</param>
        private sealed record VehicleCompletion(
            VehicleProductState Vehicle,
            double Outbound,
            double Delta,
            WeightType? Total);

        private readonly ScaleHandle m_owner;

        /// <summary>The fluent per-vehicle application policy.</summary>
        private readonly Func<string, VehicleWeightAggregation>? m_aggregation;

        /// <summary>Incomplete journeys keyed by resolved product identity, including vehicle-id aliases.</summary>
        private readonly Dictionary<NodeId, double> m_inbound = [];

        /// <summary>Explicit application overrides for individual vehicles.</summary>
        private readonly Dictionary<NodeId, VehicleWeightAggregation> m_vehicleAggregations = [];
    }

    /// <summary>
    /// How a vehicle total incorporates the signed net weight of a completed transaction.
    /// </summary>
    public enum VehicleWeightAggregation
    {
        /// <summary>Sum signed deltas, so loading and unloading can offset each other.</summary>
        Signed,

        /// <summary>Sum absolute deltas, so loading and unloading both increase the total.</summary>
        Absolute
    }
}
