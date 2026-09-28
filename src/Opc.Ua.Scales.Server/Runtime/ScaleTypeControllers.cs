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

namespace Opc.Ua.Scales.Server.Runtime
{
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
            lock (m_totalsLock)
            {
                foreach (TotalizerState totalizer in m_totalizers)
                {
                    m_totals[totalizer] += quantity;
                    PublishTotal(totalizer);
                }
            }
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
            lock (m_totalsLock)
            {
                if (m_totals.ContainsKey(totalizer))
                {
                    m_totals[totalizer] = 0;
                    PublishTotal(totalizer);
                }
            }
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
            if (scale.SetReferencePieceWeight != null)
            {
                scale.SetReferencePieceWeight.OnCall = (context, method, objectId, weight, units) =>
                    owner.Execute(ScaleCommand.SetReferencePieceWeight, () => SetReferencePieceWeightCore(weight, units));
            }
            if (scale.SetNumberOfReferencePieces != null)
            {
                scale.SetNumberOfReferencePieces.OnCall = (context, method, objectId, count) =>
                    owner.Execute(ScaleCommand.SetNumberOfReferencePieces, () => SetNumberOfReferencePiecesCore(count));
            }
            if (scale.StartReference != null)
            {
                scale.StartReference.OnCall = (context, method, objectId, count) =>
                    owner.Execute(ScaleCommand.StartReference, () => StartReferenceCore(count));
            }
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
            return m_owner.Execute(ScaleCommand.SetNumberOfReferencePieces, () => SetNumberOfReferencePiecesCore(count));
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

        internal void OnWeight(WeighingResult current)
        {
            if (!(m_referencePieceWeight > 0))
            {
                return;
            }
            double pieces = Math.Max(0, Math.Round(current.HighResolutionNet / m_referencePieceWeight));
            CurrentPieceCount = (ulong)pieces;
            ScaleValues.Set(m_owner.Services.Context, Scale.CurrentPieceCount, Variant.From(CurrentPieceCount));
            if (ActiveProduct() is { } product && product.CurrentItemCount != null)
            {
                ScaleValues.Set(m_owner.Services.Context, product.CurrentItemCount, Variant.From(CurrentPieceCount));
            }
        }

        private ServiceResult SetReferencePieceWeightCore(double weight, EUInformation units)
        {
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
            if (ActiveProduct() is { } product)
            {
                ScaleValues.Set(m_owner.Services.Context, product.ReferencePieceWeight, Variant.From(converted));
                ScaleValues.SetUnits(m_owner.Services.Context, product.ReferencePieceWeight, m_owner.Unit);
            }
            return ServiceResult.Good;
        }

        private ServiceResult SetNumberOfReferencePiecesCore(uint count)
        {
            if (count == 0)
            {
                return ServiceResult.Create(StatusCodes.BadOutOfRange, "The number of reference pieces must be positive.");
            }
            m_referencePieces = count;
            if (ActiveProduct() is { } product)
            {
                ScaleValues.Set(m_owner.Services.Context, product.NumberOfReferencePieces, Variant.From(count));
            }
            return ServiceResult.Good;
        }

        private ServiceResult StartReferenceCore(uint count)
        {
            if (count == 0)
            {
                return ServiceResult.Create(StatusCodes.BadOutOfRange, "The number of reference pieces must be positive.");
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
            m_referencePieces = count;
            m_referencePieceWeight = net / count;
            if (ActiveProduct() is { } product)
            {
                ScaleValues.Set(m_owner.Services.Context, product.NumberOfReferencePieces, Variant.From(count));
                ScaleValues.Set(m_owner.Services.Context, product.ReferencePieceWeight, Variant.From(m_referencePieceWeight));
                ScaleValues.SetUnits(m_owner.Services.Context, product.ReferencePieceWeight, m_owner.Unit);
            }
            return ServiceResult.Good;
        }

        private PieceCountingProductState? ActiveProduct()
        {
            return m_owner.ProductionPreset?.ActiveProduct as PieceCountingProductState;
        }

        private readonly ScaleHandle m_owner;
        private double m_referencePieceWeight;
        private uint m_referencePieces;
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
            if (scale.OpenDraftShields != null)
            {
                scale.OpenDraftShields.OnCall = (context, method, objectId, shield) =>
                    owner.Execute(ScaleCommand.OpenDraftShields, () => SetShields(shield, closed: false));
            }
            if (scale.CloseDraftShields != null)
            {
                scale.CloseDraftShields.OnCall = (context, method, objectId, shield) =>
                    owner.Execute(ScaleCommand.CloseDraftShields, () => SetShields(shield, closed: true));
            }
            BindStart(scale.StartLeveling, ScaleCommand.StartLeveling, () => scale.LevelingRunning, CompleteLeveling);
            BindStart(scale.StartCalibration, ScaleCommand.StartCalibration, () => scale.CalibrationRunning, CompleteCalibration);
            if (scale.StartIonisator != null)
            {
                scale.StartIonisator.OnCallMethod2 = (context, method, objectId, inputs, outputs) =>
                    owner.Execute(ScaleCommand.StartIonisator, () => SetFlag(scale.IonisatorRunning, true));
            }
            if (scale.StopIonisator != null)
            {
                scale.StopIonisator.OnCallMethod2 = (context, method, objectId, inputs, outputs) =>
                    owner.Execute(ScaleCommand.StopIonisator, () => SetFlag(scale.IonisatorRunning, false));
            }
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
        internal VehicleController(ScaleHandle owner, VehicleScaleState scale)
        {
            m_owner = owner;
            Scale = scale;
            if (scale.InboundWeighing != null)
            {
                scale.InboundWeighing.OnCall = (context, method, objectId, vehicleId) =>
                    owner.Execute(ScaleCommand.InboundWeighing, () => Inbound(vehicleId));
            }
            if (scale.OutboundWeighing != null)
            {
                scale.OutboundWeighing.OnCall = (context, method, objectId, vehicleId) =>
                    owner.Execute(ScaleCommand.OutboundWeighing, () => Outbound(vehicleId, onePass: false));
            }
            if (scale.OnePassWeighing != null)
            {
                scale.OnePassWeighing.OnCall = (context, method, objectId, vehicleId) =>
                    owner.Execute(ScaleCommand.OnePassWeighing, () => Outbound(vehicleId, onePass: true));
            }
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

        private ServiceResult Inbound(string vehicleId)
        {
            if (!TryFind(vehicleId, out VehicleProductState? vehicle, out ServiceResult? error))
            {
                return error!;
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
            Publish(vehicle!.InboundWeight, current.Gross);
            ScaleValues.Set(context, vehicle.InboundScale, m_owner.NodeId);
            m_inbound[vehicleId] = current.Gross;
            return ServiceResult.Good;
        }

        private ServiceResult Outbound(string vehicleId, bool onePass)
        {
            if (!TryFind(vehicleId, out VehicleProductState? vehicle, out ServiceResult? error))
            {
                return error!;
            }
            ServiceResult check = m_owner.Engine.CanRegister();
            if (ServiceResult.IsBad(check))
            {
                return check;
            }
            double outbound = m_owner.Engine.Evaluate().Gross;
            double delta;
            if (!onePass && m_inbound.TryGetValue(vehicleId, out double inbound))
            {
                delta = inbound - outbound;
                m_inbound.Remove(vehicleId);
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

            ISystemContext context = m_owner.Services.Context;
            Ensure(vehicle!, v => v.OutboundScale, v => v.AddOutboundScale(context));
            Publish(vehicle!.OutboundWeight, outbound);
            ScaleValues.Set(context, vehicle.OutboundScale, m_owner.NodeId);
            Publish(vehicle.DeltaWeight, delta);
            if (vehicle.DeltaWeight?.FindChild(context, new QualifiedName("IsFilling", m_owner.Services.Namespaces.Scales))
                is PropertyState<bool> isFilling)
            {
                ScaleValues.Set(context, isFilling, delta < 0);
            }
            return ServiceResult.Good;
        }

        private bool TryFind(string vehicleId, out VehicleProductState? vehicle, out ServiceResult? error)
        {
            vehicle = null;
            error = null;
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

        private void Publish(WeightItemState? item, double weight)
        {
            if (item == null)
            {
                return;
            }
            item.Value = new WeightType { Gross = weight, Net = weight, Tare = 0 };
            if (item.Overload != null)
            {
                item.Overload.Value = false;
            }
            if (item.Underload != null)
            {
                item.Underload.Value = false;
            }
            ScaleValues.Touch(m_owner.Services.Context, item);
            item.ClearChangeMasks(m_owner.Services.Context, includeChildren: true);
        }

        private readonly ScaleHandle m_owner;
        private readonly Dictionary<string, double> m_inbound = new(StringComparer.Ordinal);
    }
}
