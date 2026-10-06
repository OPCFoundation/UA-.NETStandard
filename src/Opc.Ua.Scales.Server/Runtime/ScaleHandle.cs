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
using System.Globalization;
using System.Threading;
using Opc.Ua.PackML;

namespace Opc.Ua.Scales.Server.Runtime
{
    /// <summary>
    /// The commands a client can invoke on a scale and its modules, as seen
    /// by <see cref="ScaleHandle.CommandInterceptor"/>.
    /// </summary>
    public enum ScaleCommand
    {
        /// <summary><c>SetZero</c> (§7.4.4).</summary>
        SetZero,

        /// <summary><c>SetTare</c> (§7.4.5).</summary>
        SetTare,

        /// <summary><c>ClearTare</c> (§7.4.6).</summary>
        ClearTare,

        /// <summary><c>SetPresetTare</c> (§7.4.7).</summary>
        SetPresetTare,

        /// <summary><c>RegisterWeight</c> (§7.4.8).</summary>
        RegisterWeight,

        /// <summary>Loss-in-weight <c>DischargeStart</c> (§7.26.4).</summary>
        DischargeStart,

        /// <summary>Loss-in-weight <c>DischargeStop</c> (§7.26.5).</summary>
        DischargeStop,

        /// <summary>Loss-in-weight <c>RefillStart</c> (§7.26.6).</summary>
        RefillStart,

        /// <summary>Loss-in-weight <c>RefillStop</c> (§7.26.7).</summary>
        RefillStop,

        /// <summary>Piece-counting <c>SetReferencePieceWeight</c> (§7.27.4).</summary>
        SetReferencePieceWeight,

        /// <summary>Piece-counting <c>SetNumberOfReferencePieces</c> (§7.27.5).</summary>
        SetNumberOfReferencePieces,

        /// <summary>Piece-counting <c>StartReference</c> (§7.27.6).</summary>
        StartReference,

        /// <summary>Laboratory <c>OpenDraftShields</c>.</summary>
        OpenDraftShields,

        /// <summary>Laboratory <c>CloseDraftShields</c>.</summary>
        CloseDraftShields,

        /// <summary>Laboratory <c>StartLeveling</c>.</summary>
        StartLeveling,

        /// <summary>Laboratory <c>StartCalibration</c>.</summary>
        StartCalibration,

        /// <summary>Laboratory <c>StartIonisator</c>.</summary>
        StartIonisator,

        /// <summary>Laboratory <c>StopIonisator</c>.</summary>
        StopIonisator,

        /// <summary>Vehicle <c>InboundWeighing</c> (§7.48.4).</summary>
        InboundWeighing,

        /// <summary>Vehicle <c>OutboundWeighing</c> (§7.48.5).</summary>
        OutboundWeighing,

        /// <summary>Vehicle <c>OnePassWeighing</c> (§7.48.6).</summary>
        OnePassWeighing,

        /// <summary>Totalizer <c>ResetTotalizer</c> (§7.53).</summary>
        ResetTotalizer,

        /// <summary>Feeder <c>SetFeederSpeed</c> (§7.1.4).</summary>
        SetFeederSpeed,

        /// <summary>Scale system <c>ResetGlobalStatistics</c> (§7.3.4).</summary>
        ResetGlobalStatistics,

        /// <summary>Recipe <c>StartRecipe</c> (§7.29.4).</summary>
        StartRecipe,

        /// <summary>Recipe <c>StopRecipe</c> (§7.29.5).</summary>
        StopRecipe,

        /// <summary>Recipe <c>ContinueRecipe</c> (§7.29.6).</summary>
        ContinueRecipe,

        /// <summary>Recipe <c>SkipCurrentRecipeElement</c> (§7.29.7).</summary>
        SkipCurrentRecipeElement,

        /// <summary>Recipe <c>AbortRecipe</c> (§7.29.8).</summary>
        AbortRecipe
    }

    /// <summary>
    /// The OPC 40001-1 <c>MachineryItemState</c> values.
    /// </summary>
    public enum ScaleItemState
    {
        /// <summary>NotAvailable.</summary>
        NotAvailable,

        /// <summary>OutOfService.</summary>
        OutOfService,

        /// <summary>NotExecuting.</summary>
        NotExecuting,

        /// <summary>Executing.</summary>
        Executing
    }

    /// <summary>
    /// The OPC 40001-1 <c>MachineryOperationMode</c> values.
    /// </summary>
    public enum ScaleOperationMode
    {
        /// <summary>None.</summary>
        None,

        /// <summary>Maintenance.</summary>
        Maintenance,

        /// <summary>Setup.</summary>
        Setup,

        /// <summary>Processing.</summary>
        Processing
    }

    /// <summary>
    /// The runtime of one OPC 40200 scale: the weighing behind its
    /// <c>CurrentWeight</c> and <c>RegisteredWeight</c>, its methods, events,
    /// alarms, state machines, production preset and type-specific
    /// behaviour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The application feeds the raw load with <see cref="PublishLoad"/>. The
    /// handle derives gross, net and tare, rounds them to the scale interval
    /// of the current weighing range, and publishes them together with every
    /// optional <c>WeightItemType</c> property the scale materialised. An
    /// overload or underload raises the OVERLOAD_FAULT or UNDERLOAD_FAULT
    /// alarm; a failed zero or tare setting raises ZERO_SETTING_FAULT or
    /// TARE_SETTING_FAULT (OPC 40200 Annex C).
    /// </para>
    /// <para>
    /// Every client command first passes <see cref="CommandInterceptor"/>, so
    /// an application that drives real weighing electronics can perform or
    /// veto it before the model is updated.
    /// </para>
    /// </remarks>
    public sealed class ScaleHandle
    {
        internal ScaleHandle(
            ScaleRuntimeServices services,
            ScaleDeviceState scale,
            ScaleKind kind,
            ScaleConfiguration configuration)
        {
            m_services = services;
            Scale = scale;
            Kind = kind;
            Unit = configuration.Unit;
            m_engine = new ScaleWeighingEngine(configuration.Ranges, services.Options.ZeroSettingRange)
            {
                LegalForTrade = configuration.LegalForTrade,
                Stable = true
            };
            Notifications = new ScaleNotifications(services.Context, scale, services.Register);

            // The runtime drives these two conditions itself (Annex C:
            // OVERLOAD_FAULT follows the Overload property), so they exist from
            // the start instead of appearing on the first overload.
            Notifications.EnsureAlarm(ScaleNotificationId.OverloadFault);
            Notifications.EnsureAlarm(ScaleNotificationId.UnderloadFault);

            BindBaseMethods();
            if (scale.State != null)
            {
                PackML = new PackMLStateMachineController(services.Context, scale.State);
                PackML.Initialize(services.Options.PackMLInitialState);
            }
            if (scale.ProductionPreset != null)
            {
                ProductionPreset = new ScaleProductionPreset(
                    services,
                    scale.ProductionPreset,
                    services.ScalesId(ScalesModel.ProductTypeOf(kind)),
                    () => AllowedEngineeringUnits)
                {
                    Lockable = configuration.LockableProducts
                };
                ProductionPreset.OnRegistered();
                foreach (string productId in configuration.SelectedProducts)
                {
                    ProductionPreset.Select(productId);
                }
            }
            if (scale.ProductionOutput != null)
            {
                ProductionOutput = new ScaleStatistics(services.Context, scale.ProductionOutput);
            }
            SetItemState(ScaleItemState.NotExecuting);
            SetOperationMode(ScaleOperationMode.Processing);

            switch (scale)
            {
                case LossInWeightScaleState lossInWeight:
                    LossInWeight = new LossInWeightController(this, lossInWeight);
                    Continuous = new ContinuousController(this, lossInWeight);
                    break;
                case ContinuousScaleState continuous:
                    Continuous = new ContinuousController(this, continuous);
                    break;
                case PieceCountingScaleState pieceCounting:
                    PieceCounting = new PieceCountingController(this, pieceCounting);
                    break;
                case LaboratoryScaleState laboratory:
                    Laboratory = new LaboratoryController(this, laboratory);
                    break;
                case HopperScaleState hopper:
                    Hopper = new HopperController(this, hopper);
                    break;
                case VehicleScaleState vehicle:
                    Vehicle = new VehicleController(this, vehicle);
                    break;
                case RecipeScaleState recipe:
                    Recipes = new RecipeController(this, recipe, configuration.RecipeFiles);
                    break;
                case AutomaticFillingScaleState filling:
                    AutomaticFilling = new AutomaticFillingController(this, filling);
                    break;
            }

            PublishCore();
        }

        /// <summary>
        /// Gets the scale node.
        /// </summary>
        public ScaleDeviceState Scale { get; }

        /// <summary>
        /// Gets the scale's NodeId.
        /// </summary>
        public NodeId NodeId => Scale.NodeId;

        /// <summary>
        /// Gets the kind of scale.
        /// </summary>
        public ScaleKind Kind { get; }

        /// <summary>
        /// Gets the engineering unit the scale weighs in.
        /// </summary>
        public EUInformation Unit { get; }

        /// <summary>
        /// Gets the scale's maximum capacity.
        /// </summary>
        public double Capacity => m_engine.Capacity;

        /// <summary>
        /// Gets the weighing ranges, ordered by capacity.
        /// </summary>
        public ArrayOf<WeighingRangeDefinition> WeighingRanges => m_engine.Ranges;

        /// <summary>
        /// Gets or sets a hook every client command passes before the model is
        /// updated. Return a bad result to reject the command with it - for
        /// example when the weighing electronics refused to zero.
        /// </summary>
        public Func<ScaleCommand, ServiceResult>? CommandInterceptor { get; set; }

        /// <summary>
        /// Raised after a weight was registered, by the method or the
        /// application.
        /// </summary>
        public event EventHandler<ScaleReading>? WeightRegistered;

        /// <summary>
        /// Gets the scale's event and alarm publisher.
        /// </summary>
        public ScaleNotifications Notifications { get; }

        /// <summary>
        /// Gets the PackML state machine controller, when the scale has the
        /// optional <c>State</c>.
        /// </summary>
        public PackMLStateMachineController? PackML { get; }

        /// <summary>
        /// Gets the production preset, when the scale has one.
        /// </summary>
        public ScaleProductionPreset? ProductionPreset { get; }

        /// <summary>
        /// Gets the <c>ProductionOutput</c> statistics, when the scale has
        /// them.
        /// </summary>
        public ScaleStatistics? ProductionOutput { get; }

        /// <summary>
        /// Gets the continuous-scale controller.
        /// </summary>
        public ContinuousController? Continuous { get; }

        /// <summary>
        /// Gets the loss-in-weight controller.
        /// </summary>
        public LossInWeightController? LossInWeight { get; }

        /// <summary>
        /// Gets the piece-counting controller.
        /// </summary>
        public PieceCountingController? PieceCounting { get; }

        /// <summary>
        /// Gets the laboratory-scale controller.
        /// </summary>
        public LaboratoryController? Laboratory { get; }

        /// <summary>
        /// Gets the hopper-scale controller.
        /// </summary>
        public HopperController? Hopper { get; }

        /// <summary>
        /// Gets the vehicle-scale controller.
        /// </summary>
        public VehicleController? Vehicle { get; }

        /// <summary>
        /// Gets the recipe-scale controller.
        /// </summary>
        public RecipeController? Recipes { get; }

        /// <summary>
        /// Gets the automatic-filling controller.
        /// </summary>
        public AutomaticFillingController? AutomaticFilling { get; }

        /// <summary>
        /// Gets the handles of the scale's feeder and printer modules.
        /// </summary>
        public ArrayOf<ScaleModuleHandle> Modules => m_modules;

        /// <summary>
        /// Gets the handles of the scale's weighing modules (bridges).
        /// </summary>
        public ArrayOf<ScaleHandle> WeighingModules => m_weighingModules;

        /// <summary>
        /// Gets the current reading.
        /// </summary>
        public ScaleReading CurrentReading
        {
            get
            {
                lock (m_lock)
                {
                    return ToReading(m_engine.Evaluate(), m_weightId);
                }
            }
        }

        /// <summary>
        /// Gets the last registered reading, if any.
        /// </summary>
        public ScaleReading? RegisteredReading
        {
            get
            {
                lock (m_lock)
                {
                    return m_registered;
                }
            }
        }

        /// <summary>
        /// Gets the engineering units methods accept, from
        /// <c>AllowedEngineeringUnits</c>; empty when the scale does not
        /// restrict them.
        /// </summary>
        public ArrayOf<EUInformation> AllowedEngineeringUnits =>
            Scale.AllowedEngineeringUnits?.Value ?? default;

        /// <summary>
        /// Publishes the raw load the weighing electronics measured.
        /// </summary>
        /// <param name="rawLoad">The load before zero and tare, in <see cref="Unit"/>.</param>
        /// <param name="stable">
        /// Whether the load is stable; <see langword="null"/> when the
        /// electronics do not report stability.
        /// </param>
        public void PublishLoad(double rawLoad, bool? stable = true)
        {
            lock (m_lock)
            {
                m_engine.RawLoad = rawLoad;
                m_engine.Stable = stable;
                PublishCore();
            }
        }

        /// <summary>
        /// Sets the zero point, as the method would.
        /// </summary>
        public ServiceResult SetZero()
        {
            return Execute(ScaleCommand.SetZero, () =>
            {
                ServiceResult result = m_engine.SetZero();
                if (ServiceResult.IsBad(result))
                {
                    Notifications.RaiseEvent(
                        ScaleNotificationId.ZeroSettingFault,
                        new LocalizedText(result.LocalizedText.Text ?? "Zero setting failed."),
                        EventSeverity.MediumHigh);
                }
                return result;
            });
        }

        /// <summary>
        /// Takes the current weight as tare, as the method would.
        /// </summary>
        public ServiceResult SetTare()
        {
            return Execute(ScaleCommand.SetTare, () =>
            {
                ServiceResult result = m_engine.SetTare();
                if (ServiceResult.IsBad(result))
                {
                    Notifications.RaiseEvent(
                        ScaleNotificationId.TareSettingFault,
                        new LocalizedText(result.LocalizedText.Text ?? "Tare setting failed."),
                        EventSeverity.MediumHigh);
                }
                return result;
            });
        }

        /// <summary>
        /// Clears the tare, as the method would.
        /// </summary>
        public ServiceResult ClearTare()
        {
            return Execute(ScaleCommand.ClearTare, () =>
            {
                m_engine.ClearTare();
                return ServiceResult.Good;
            });
        }

        /// <summary>
        /// Sets a preset tare, as the method would.
        /// </summary>
        /// <param name="presetTare">The preset tare.</param>
        /// <param name="engineeringUnits">
        /// The unit of <paramref name="presetTare"/>, or <see langword="null"/>
        /// for the scale's unit.
        /// </param>
        public ServiceResult SetPresetTare(double presetTare, EUInformation? engineeringUnits = null)
        {
            EUInformation unit = engineeringUnits ?? Unit;
            ServiceResult check = ScaleValues.CheckUnit(
                unit,
                AllowedEngineeringUnits,
                m_services.Options.RequireSiUnits);
            if (ServiceResult.IsBad(check))
            {
                return check;
            }
            if (!ScaleUnits.TryConvertMass(presetTare, unit, Unit, out double converted))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "The unit '{0}' cannot be converted to the scale's unit '{1}'.",
                    unit.DisplayName.Text ?? string.Empty,
                    Unit.DisplayName.Text ?? string.Empty);
            }
            return Execute(ScaleCommand.SetPresetTare, () =>
            {
                ServiceResult result = m_engine.SetPresetTare(converted);
                if (ServiceResult.IsBad(result))
                {
                    Notifications.RaiseEvent(
                        ScaleNotificationId.TareSettingFault,
                        new LocalizedText(result.LocalizedText.Text ?? "Preset tare setting failed."),
                        EventSeverity.MediumHigh);
                }
                return result;
            });
        }

        /// <summary>
        /// Registers the current weight, as the method would.
        /// </summary>
        public ServiceResult RegisterWeight()
        {
            ScaleReading? registered = null;
            ServiceResult result = Execute(ScaleCommand.RegisterWeight, () =>
            {
                ServiceResult check = m_engine.CanRegister();
                if (ServiceResult.IsBad(check))
                {
                    return check;
                }
                m_weightCounter++;
                m_weightId = m_weightCounter.ToString(CultureInfo.InvariantCulture);
                WeighingResult current = m_engine.Evaluate();
                registered = ToReading(current, m_weightId);
                m_registered = registered;
                if (Scale.RegisteredWeight != null)
                {
                    PublishWeight(Scale.RegisteredWeight, current, m_weightId);
                }
                ProductionOutput?.Record(current.Net, m_weightId);
                return ServiceResult.Good;
            });
            if (registered != null)
            {
                WeightRegistered?.Invoke(this, registered);
            }
            return result;
        }

        /// <summary>
        /// Sets the OPC 40001-1 <c>MachineryItemState</c>, when the scale has it.
        /// </summary>
        /// <param name="state">The new state.</param>
        public void SetItemState(ScaleItemState state)
        {
            lock (m_lock)
            {
                ScaleMachineryStates.SetItemState(m_services, Scale.MachineryItemState, state);
            }
        }

        /// <summary>
        /// Sets the OPC 40001-1 <c>MachineryOperationMode</c>, when the scale has it.
        /// </summary>
        /// <param name="mode">The new mode.</param>
        public void SetOperationMode(ScaleOperationMode mode)
        {
            lock (m_lock)
            {
                ScaleMachineryStates.SetOperationMode(m_services, Scale.MachineryOperationMode, mode);
            }
        }

        /// <summary>
        /// Publishes the process state (<c>ProcessStateId</c>,
        /// <c>ProcessStateMessage</c>), when the scale has it.
        /// </summary>
        /// <param name="processStateId">The machine-readable state id.</param>
        /// <param name="processStateMessage">The human-readable state message.</param>
        public void SetProcessState(string? processStateId, LocalizedText processStateMessage)
        {
            lock (m_lock)
            {
                if (processStateId != null)
                {
                    ScaleValues.Set(m_services.Context, Scale.ProcessStateId, processStateId);
                }
                ScaleValues.Set(m_services.Context, Scale.ProcessStateMessage, processStateMessage);
            }
        }

        /// <summary>
        /// Runs application code that writes several values of the scale -
        /// for example typed nodes reached through <c>With&lt;TState&gt;</c> -
        /// as one step no method call or other runtime member interleaves with.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The scale's own members, its type controllers and its modules take
        /// the scale lock, so they are safe to call from any thread and do not
        /// interleave with <paramref name="update"/>. The lock is re-entrant;
        /// <paramref name="update"/> may call them.
        /// </para>
        /// <para>
        /// The recipe controller, the production preset, the statistics, the
        /// notifications, the PackML controller and the totalizers of a
        /// continuous scale synchronise on locks of their own. They are
        /// thread-safe too, but <see cref="Update"/> does not keep their calls
        /// out.
        /// </para>
        /// <para>
        /// <paramref name="update"/> runs under the scale lock and holds up
        /// every method call on the scale while it runs. Keep it short and
        /// synchronous: no I/O, no waiting, and no calls into other locks.
        /// </para>
        /// </remarks>
        /// <param name="update">The writes to perform.</param>
        public void Update(Action update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }
            lock (m_lock)
            {
                update();
            }
        }

        /// <summary>
        /// Runs an equipment-side report under the scale lock. Unlike
        /// <see cref="Execute"/> it bypasses the <see cref="CommandInterceptor"/>:
        /// the equipment reports what happened, it does not ask.
        /// </summary>
        internal void Locked(Action action)
        {
            lock (m_lock)
            {
                action();
            }
        }

        /// <inheritdoc cref="Locked(Action)"/>
        internal T Locked<T>(Func<T> action)
        {
            lock (m_lock)
            {
                return action();
            }
        }

        internal ScaleRuntimeServices Services => m_services;

        internal ScaleWeighingEngine Engine => m_engine;

        internal void AddModule(ScaleModuleHandle module)
        {
            m_modules = m_modules.AddItem(module);
        }

        internal void AddWeighingModule(ScaleHandle module)
        {
            m_weighingModules = m_weighingModules.AddItem(module);
        }

        /// <summary>
        /// Runs a command through the interceptor and republishes the weight
        /// when it succeeded.
        /// </summary>
        internal ServiceResult Execute(ScaleCommand command, Func<ServiceResult> action)
        {
            if (CommandInterceptor != null)
            {
                ServiceResult veto = CommandInterceptor(command);
                if (ServiceResult.IsBad(veto))
                {
                    return veto;
                }
            }
            lock (m_lock)
            {
                ServiceResult result = action();
                if (ServiceResult.IsGood(result))
                {
                    PublishCore();
                }
                return result;
            }
        }

        private void BindBaseMethods()
        {
            if (Scale.SetZero != null)
            {
                Scale.SetZero.OnCallMethod2 = (context, method, objectId, inputs, outputs) => SetZero();
            }
            if (Scale.SetTare != null)
            {
                Scale.SetTare.OnCallMethod2 = (context, method, objectId, inputs, outputs) => SetTare();
            }
            if (Scale.ClearTare != null)
            {
                Scale.ClearTare.OnCallMethod2 = (context, method, objectId, inputs, outputs) => ClearTare();
            }
            if (Scale.SetPresetTare != null)
            {
                Scale.SetPresetTare.OnCall = (context, method, objectId, presetTare, engineeringUnits) =>
                    SetPresetTare(presetTare, engineeringUnits);
            }
            if (Scale.RegisterWeight != null)
            {
                Scale.RegisterWeight.OnCallMethod2 = (context, method, objectId, inputs, outputs) =>
                    RegisterWeight();
            }
        }

        private void PublishCore()
        {
            WeighingResult current = m_engine.Evaluate();
            if (Scale.CurrentWeight != null)
            {
                PublishWeight(Scale.CurrentWeight, current, weightId: null);
            }
            Notifications.SetAlarm(
                ScaleNotificationId.OverloadFault,
                current.Overload,
                new LocalizedText(current.Overload
                    ? "The maximum capacity of the scale is exceeded."
                    : "The scale is back within its capacity."));
            Notifications.SetAlarm(
                ScaleNotificationId.UnderloadFault,
                current.Underload,
                new LocalizedText(current.Underload
                    ? "The weight is below the minimum of the scale."
                    : "The weight is back above the minimum of the scale."));
            PieceCounting?.OnWeight(current);
        }

        private void PublishWeight(WeightItemState item, WeighingResult result, string? weightId)
        {
            ISystemContext context = m_services.Context;
            item.Value = new WeightType { Gross = result.Gross, Net = result.Net, Tare = result.Tare };
            item.StatusCode = StatusCodes.Good;
            item.Timestamp = DateTimeUtc.Now;

            SetFlag(item.Overload, result.Overload);
            SetFlag(item.Underload, result.Underload);
            if (item.TareMode != null)
            {
                item.TareMode.Value = m_engine.TareMode;
            }
            SetNumber(item.Gross, result.Gross);
            SetNumber(item.Net, result.Net);
            SetNumber(item.Tare, result.Tare);
            SetFlag(item.InsideZero, result.InsideZero);
            SetFlag(item.CenterOfZero, result.CenterOfZero);
            SetFlag(item.GrossNegative, result.GrossNegative);
            SetFlag(item.LegalForTrade, m_engine.LegalForTrade);
            if (item.WeightStable != null && m_engine.Stable.HasValue)
            {
                item.WeightStable.Value = m_engine.Stable.Value;
            }
            if (item.CurrentRangeId != null)
            {
                item.CurrentRangeId.Value = (ushort)(result.RangeIndex + 1);
            }
            if (item.WeightId != null && weightId != null)
            {
                item.WeightId.Value = weightId;
            }
            if (item.HighResolutionValue != null)
            {
                item.HighResolutionValue.Value = new WeightType
                {
                    Gross = result.HighResolutionGross,
                    Net = result.HighResolutionNet,
                    Tare = result.HighResolutionTare
                };
            }
            if (item.PrintableValue != null)
            {
                string format = "F" + DecimalsOf(result.Interval).ToString(CultureInfo.InvariantCulture);
                item.PrintableValue.Value = new PrintableWeightType
                {
                    Gross = result.Gross.ToString(format, CultureInfo.InvariantCulture),
                    Net = result.Net.ToString(format, CultureInfo.InvariantCulture),
                    Tare = result.Tare.ToString(format, CultureInfo.InvariantCulture)
                };
            }
            item.ClearChangeMasks(context, includeChildren: true);
        }

        private static void SetFlag(PropertyState<bool>? property, bool value)
        {
            if (property != null)
            {
                property.Value = value;
            }
        }

        private static void SetNumber(PropertyState<double>? property, double value)
        {
            if (property != null)
            {
                property.Value = value;
            }
        }

        private static int DecimalsOf(double interval)
        {
            if (!(interval > 0) || interval >= 1)
            {
                return 0;
            }
            return Math.Min(9, (int)Math.Ceiling(-Math.Log10(interval) - 1e-9));
        }

        private ScaleReading ToReading(WeighingResult result, string? weightId)
        {
            return new ScaleReading
            {
                Gross = result.Gross,
                Net = result.Net,
                Tare = result.Tare,
                TareMode = m_engine.TareMode,
                Overload = result.Overload,
                Underload = result.Underload,
                Stable = m_engine.Stable,
                InsideZero = result.InsideZero,
                CurrentRangeId = (ushort)(result.RangeIndex + 1),
                WeightId = weightId,
                EngineeringUnits = Unit,
                Timestamp = DateTime.UtcNow,
                StatusCode = StatusCodes.Good
            };
        }

        private readonly ScaleRuntimeServices m_services;
        private readonly ScaleWeighingEngine m_engine;
        private readonly Lock m_lock = new();
        private ArrayOf<ScaleModuleHandle> m_modules = [];
        private ArrayOf<ScaleHandle> m_weighingModules = [];
        private ScaleReading? m_registered;
        private string? m_weightId;
        private ulong m_weightCounter;
    }

    /// <summary>
    /// What a scale builder hands the runtime: the configuration that is not
    /// itself part of the address space.
    /// </summary>
    /// <param name="Ranges">The weighing ranges.</param>
    /// <param name="Unit">The unit the scale weighs in.</param>
    /// <param name="LegalForTrade">Whether the scale is verified.</param>
    /// <param name="LockableProducts">Whether products carry a DI lock.</param>
    /// <param name="SelectedProducts">The products in processing at start.</param>
    /// <param name="RecipeFiles">Whether recipes carry a <c>RecipeFile</c>.</param>
    internal sealed record ScaleConfiguration(
        IReadOnlyList<WeighingRangeDefinition> Ranges,
        EUInformation Unit,
        bool LegalForTrade,
        bool LockableProducts,
        IReadOnlyList<string> SelectedProducts,
        bool RecipeFiles = false);

    /// <summary>
    /// Sets the OPC 40001-1 state machines, which the model supplies without
    /// state tables.
    /// </summary>
    internal static class ScaleMachineryStates
    {
        public static void SetItemState(
            ScaleRuntimeServices services,
            Opc.Ua.Machinery.MachineryItemState_StateMachineState? machine,
            ScaleItemState state)
        {
            if (machine == null)
            {
                return;
            }
            uint id = state switch
            {
                ScaleItemState.NotAvailable => Opc.Ua.Machinery.Objects.MachineryItemState_StateMachineType_NotAvailable,
                ScaleItemState.OutOfService => Opc.Ua.Machinery.Objects.MachineryItemState_StateMachineType_OutOfService,
                ScaleItemState.Executing => Opc.Ua.Machinery.Objects.MachineryItemState_StateMachineType_Executing,
                _ => Opc.Ua.Machinery.Objects.MachineryItemState_StateMachineType_NotExecuting
            };
            ScaleValues.SetCurrentState(
                services.Context,
                machine,
                new NodeId(id, services.Namespaces.Machinery),
                state.ToString());
        }

        public static void SetOperationMode(
            ScaleRuntimeServices services,
            Opc.Ua.Machinery.MachineryOperationModeStateMachineState? machine,
            ScaleOperationMode mode)
        {
            if (machine == null)
            {
                return;
            }
            uint id = mode switch
            {
                ScaleOperationMode.Maintenance => Opc.Ua.Machinery.Objects.MachineryOperationModeStateMachineType_Maintenance,
                ScaleOperationMode.Setup => Opc.Ua.Machinery.Objects.MachineryOperationModeStateMachineType_Setup,
                ScaleOperationMode.Processing => Opc.Ua.Machinery.Objects.MachineryOperationModeStateMachineType_Processing,
                _ => Opc.Ua.Machinery.Objects.MachineryOperationModeStateMachineType_None
            };
            ScaleValues.SetCurrentState(
                services.Context,
                machine,
                new NodeId(id, services.Namespaces.Machinery),
                mode.ToString());
        }
    }
}
