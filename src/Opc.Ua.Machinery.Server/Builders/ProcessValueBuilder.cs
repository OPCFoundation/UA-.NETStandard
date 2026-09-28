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
using Opc.Ua.Machinery.ProcessValues;
using Opc.Ua.Server;
using PadimBrowseNames = Opc.Ua.PADIM.BrowseNames;

namespace Opc.Ua.Machinery.Server.Builders
{
    /// <summary>
    /// Assembles one OPC 40001-2 process value.
    /// </summary>
    /// <remarks>
    /// <c>ProcessValueType</c> derives from the OPC 30081 PADIM
    /// <c>AnalogSignalType</c>, and its mandatory <c>AnalogSignal</c> child
    /// keeps PADIM's <c>AnalogSignalVariableType</c> as its declared type.
    /// OPC 40001-2 adds <c>ProcessValueVariableType</c> as a subtype of it,
    /// carrying the four limits and the percentage value; an instance may use
    /// it in that slot, and the builder does, so the limits are reachable
    /// without breaking the type declaration.
    /// </remarks>
    public interface IProcessValueBuilder
    {
        /// <summary>
        /// Gets the process value being configured.
        /// </summary>
        ProcessValueState State { get; }

        /// <summary>
        /// Gets the <c>AnalogSignal</c> variable that carries the value, typed
        /// with OPC 40001-2's <c>ProcessValueVariableType</c> subtype so the
        /// limits below are reachable.
        /// </summary>
        ProcessValueVariableState Signal { get; }

        /// <summary>
        /// Writes the mandatory OPC 30081 <c>SignalTag</c> — the unique name
        /// the process value is known by. Defaults to the process value's
        /// browse name.
        /// </summary>
        /// <param name="signalTag">The tag.</param>
        IProcessValueBuilder WithSignalTag(string signalTag);

        /// <summary>
        /// Writes the engineering unit and the instrument range of the value.
        /// </summary>
        /// <param name="engineeringUnits">The engineering unit.</param>
        /// <param name="euRange">The nominal range.</param>
        IProcessValueBuilder WithEngineeringUnits(
            EUInformation engineeringUnits,
            Opc.Ua.Range? euRange = null);

        /// <summary>
        /// Adds the optional limits OPC 40001-2 declares on the signal.
        /// </summary>
        IProcessValueBuilder WithLimits(
            double? lowLow = null,
            double? low = null,
            double? high = null,
            double? highHigh = null);

        /// <summary>
        /// Adds the optional <c>ProcessValueSetpoint</c> variable.
        /// </summary>
        /// <param name="setpoint">The initial setpoint.</param>
        IProcessValueBuilder WithSetpoint(double setpoint);

        /// <summary>
        /// Adds the four deviation variables OPC 40001-2 declares on
        /// <c>ProcessValueSetpointVariableType</c>. They are deviations from
        /// the setpoint, not absolute values, and are what
        /// <see cref="WithDeviationAlarm"/> defaults its limits to.
        /// </summary>
        /// <param name="lowLow">Low-low deviation.</param>
        /// <param name="low">Low deviation.</param>
        /// <param name="high">High deviation.</param>
        /// <param name="highHigh">High-high deviation.</param>
        /// <param name="autoAdjustment">
        /// Whether the server adjusts the deviations by itself.
        /// </param>
        /// <param name="sensitivity">
        /// Optional dead band, in per mille of the deviation, before a
        /// crossing counts.
        /// </param>
        /// <exception cref="ServiceResultException">
        /// The process value publishes no setpoint.
        /// </exception>
        IProcessValueBuilder WithDeviationLimits(
            double? lowLow = null,
            double? low = null,
            double? high = null,
            double? highHigh = null,
            bool? autoAdjustment = null,
            ushort? sensitivity = null);

        /// <summary>
        /// Adds the optional exclusive <c>LimitAlarm</c> and couples it to the
        /// signal.
        /// </summary>
        /// <remarks>
        /// The alarm takes the four limits already written with
        /// <see cref="WithLimits"/> unless they are overridden here, watches
        /// the signal as its <c>InputNode</c>, and activates whenever
        /// <see cref="IProcessValueHandle.SetValueAsync"/> crosses one of
        /// them. Creating the object alone would leave a node that never
        /// reports.
        /// </remarks>
        /// <param name="lowLow">Overrides the signal's low-low limit.</param>
        /// <param name="low">Overrides the signal's low limit.</param>
        /// <param name="high">Overrides the signal's high limit.</param>
        /// <param name="highHigh">Overrides the signal's high-high limit.</param>
        /// <param name="configure">Optional further configuration.</param>
        IProcessValueBuilder WithLimitAlarm(
            double? lowLow = null,
            double? low = null,
            double? high = null,
            double? highHigh = null,
            Action<ExclusiveLimitAlarmState>? configure = null);

        /// <summary>
        /// Adds the optional exclusive <c>DeviationAlarm</c>, which compares
        /// the value against the setpoint.
        /// </summary>
        /// <remarks>
        /// The limits are deviations from the setpoint, not absolute values,
        /// and the alarm's <c>SetpointNode</c> points at the
        /// <c>ProcessValueSetpoint</c> variable — so
        /// <see cref="WithSetpoint"/> has to have run first.
        /// </remarks>
        /// <param name="lowLow">The low-low deviation.</param>
        /// <param name="low">The low deviation.</param>
        /// <param name="high">The high deviation.</param>
        /// <param name="highHigh">The high-high deviation.</param>
        /// <param name="configure">Optional further configuration.</param>
        IProcessValueBuilder WithDeviationAlarm(
            double? lowLow = null,
            double? low = null,
            double? high = null,
            double? highHigh = null,
            Action<ExclusiveDeviationAlarmState>? configure = null);

        /// <summary>
        /// Adds the optional <c>PercentageValue</c> variable OPC 40001-2
        /// declares on <c>ProcessValueVariableType</c>. It is kept in step with
        /// the value and the instrument range by
        /// <see cref="IProcessValueHandle.SetValueAsync"/>.
        /// </summary>
        IProcessValueBuilder WithPercentageValue();

        /// <summary>
        /// Sets the initial reading.
        /// </summary>
        /// <param name="value">The value to publish.</param>
        IProcessValueBuilder WithValue(double value);

        /// <summary>
        /// Hands back the runtime handle the application drives the process
        /// value with after the machine is built.
        /// </summary>
        /// <param name="handle">Receives the handle.</param>
        IProcessValueBuilder Bind(out IProcessValueHandle handle);

        /// <summary>
        /// Adds the optional <c>Status</c> and <c>AlarmSuppression</c> discrete
        /// variables.
        /// </summary>
        IProcessValueBuilder WithStatus(ushort status = 0, ushort alarmSuppression = 0);

        /// <summary>
        /// Adds the optional <c>ZeroPointAdjustment</c> method OPC 30081
        /// declares on the analog signal, and reports the
        /// <c>ZeroPointAdjustmentEventType</c> event OPC 40001-2 attaches to
        /// it on every call.
        /// </summary>
        /// <remarks>
        /// <para>
        /// OPC 40001-2's conformance unit is explicit that <em>all</em>
        /// instances supporting the method generate the event, so the event is
        /// reported by the builder rather than left to the handler: the
        /// handler only decides the <c>ZeroPointAdjustmentResult</c> status it
        /// carries.
        /// </para>
        /// <para>
        /// <c>ZeroPointAdjustmentEventType</c> is abstract, so the event is
        /// reported with a concrete subtype minted into the server's instance
        /// namespace; a client filtering on a concrete type would otherwise
        /// never see it.
        /// </para>
        /// </remarks>
        /// <param name="onAdjust">
        /// Performs the adjustment and returns the status the event reports.
        /// A bad status is reported in the event and returned to the caller.
        /// </param>
        IProcessValueBuilder WithZeroPointAdjustment(
            Func<ProcessValueState, CancellationToken, ValueTask<StatusCode>> onAdjust);

        /// <summary>
        /// Adds OPC 30081's <c>ActualValue</c>, <c>SimulationValue</c> and
        /// <c>SimulationState</c> to the analog signal, satisfying
        /// <c>3:PA-DIM AnalogSignalVariable Simulation</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// While <c>SimulationState</c> is set, the signal reports
        /// <c>SimulationValue</c> and <c>ActualValue</c> keeps carrying the
        /// real reading, which is what lets a client tell a simulated plant
        /// from a running one. Everything derived from the signal — the
        /// percentage, the limit alarm and the deviation alarm — follows the
        /// reported value, because that is the value the plant is acting on.
        /// </para>
        /// <para>
        /// <see cref="IProcessValueHandle.SetSimulationAsync"/> drives it at
        /// runtime.
        /// </para>
        /// </remarks>
        /// <param name="simulationValue">The initial simulated reading.</param>
        /// <param name="simulationState">
        /// Whether simulation starts active. A server that comes up simulating
        /// is unusual, so this defaults to <see langword="false"/>.
        /// </param>
        IProcessValueBuilder WithSimulation(
            double simulationValue = 0,
            bool simulationState = false);
    }

    /// <summary>
    /// The runtime surface of one OPC 40001-2 process value.
    /// </summary>
    /// <remarks>
    /// Writing a new reading through this handle is what keeps the derived
    /// state consistent: the percentage value, the limit alarm and the
    /// deviation alarm are all recomputed from it, which is the behaviour the
    /// part's alarm conformance units describe.
    /// </remarks>
    public interface IProcessValueHandle
    {
        /// <summary>
        /// Gets the process value's NodeId.
        /// </summary>
        NodeId NodeId { get; }

        /// <summary>
        /// Gets the process value object.
        /// </summary>
        ProcessValueState State { get; }

        /// <summary>
        /// Gets the <c>AnalogSignal</c> variable that carries the reading.
        /// </summary>
        ProcessValueVariableState Signal { get; }

        /// <summary>
        /// Gets the last reading written.
        /// </summary>
        double Value { get; }

        /// <summary>
        /// Gets the setpoint, or <see langword="null"/> when the process value
        /// publishes none.
        /// </summary>
        double? Setpoint { get; }

        /// <summary>
        /// Publishes a new reading and re-evaluates everything derived from
        /// it.
        /// </summary>
        /// <param name="value">The reading.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask SetValueAsync(double value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes a new setpoint and re-evaluates the deviation alarm.
        /// </summary>
        /// <param name="setpoint">The setpoint.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ServiceResultException">
        /// The process value publishes no setpoint.
        /// </exception>
        ValueTask SetSetpointAsync(
            double setpoint,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes a new OPC 40001-2 <c>Status</c> word.
        /// </summary>
        /// <param name="status">The vendor-specific status.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ServiceResultException">
        /// The process value publishes no status.
        /// </exception>
        ValueTask SetStatusAsync(ushort status, CancellationToken cancellationToken = default);

        /// <summary>
        /// Switches OPC 30081 simulation on or off and optionally writes a new
        /// simulated reading.
        /// </summary>
        /// <param name="simulationState">Whether simulation is active.</param>
        /// <param name="simulationValue">
        /// The simulated reading, or <see langword="null"/> to keep the
        /// current one.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ServiceResultException">
        /// The process value publishes no simulation members.
        /// </exception>
        ValueTask SetSimulationAsync(
            bool simulationState,
            double? simulationValue = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implements both <see cref="IProcessValueBuilder"/> and
    /// <see cref="IProcessValueHandle"/> for one OPC 40001-2 process value:
    /// stages the <c>ProcessValueType</c> object with its
    /// <c>ProcessValueVariableType</c> signal, and at runtime publishes each
    /// reading and re-evaluates the percentage value and the alarms derived
    /// from it.
    /// </summary>
    internal sealed class ProcessValueBuilder : IProcessValueBuilder, IProcessValueHandle
    {
        public static ProcessValueBuilder Create(
            MachineryBuildScope scope,
            NodeState machine,
            QualifiedName browseName)
        {
            ProcessValueState processValue = MachineryBuilderUtilities.AddComponentChild(
                scope.Context,
                machine,
                browseName,
                static (ctx, parent, name) => ctx.CreateInstanceOfProcessValueType(parent, name));

            // The generated factory materialises AnalogSignal with the declared
            // PADIM AnalogSignalVariableType. OPC 40001-2 puts its limits on
            // ProcessValueVariableType, a subtype of that - so substituting the
            // subtype in the instance is what makes LowLimit / HighLimit
            // reachable, and it stays within the type declaration because an
            // instance may always use a subtype of the declared type.
            ushort padimNamespaceIndex = MachineryBuilderUtilities.NamespaceIndex(
                scope.Context,
                Opc.Ua.PADIM.Namespaces.PADIM);
            var signalName = new QualifiedName(
                PadimBrowseNames.AnalogSignal,
                padimNamespaceIndex);
            ProcessValueVariableState signal =
                scope.Context.CreateInstanceOfProcessValueVariableType(
                    processValue,
                    signalName);
            signal.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            signal.ModellingRuleId = NodeId.Null;
            processValue.CreateOrReplaceAnalogSignal(scope.Context, signal);
            ApplyDefaultSignalTag(processValue);

            scope.RecordFacet(MachineryFacet.ProcessValues);
            return new ProcessValueBuilder(scope, processValue, signal);
        }

        /// <summary>
        /// Applies the default <c>SignalTag</c> value.
        /// </summary>
        /// <remarks>
        /// OPC 30081 makes <c>SignalTag</c> mandatory and wants it unique; the
        /// process value's own browse name is the one identifier the builder
        /// can be sure of. <see cref="WithSignalTag"/> overrides it.
        /// </remarks>
        private static void ApplyDefaultSignalTag(ProcessValueState processValue)
        {
            if (processValue.SignalTag == null ||
                !string.IsNullOrEmpty(processValue.SignalTag.Value))
            {
                return;
            }
            processValue.SignalTag.Value = processValue.BrowseName.Name ?? string.Empty;
        }

        private ProcessValueBuilder(
            MachineryBuildScope scope,
            ProcessValueState state,
            ProcessValueVariableState signal)
        {
            m_scope = scope;
            State = state;
            Signal = signal;
        }

        public ProcessValueState State { get; }

        public ProcessValueVariableState Signal { get; }

        public IProcessValueBuilder WithSignalTag(string signalTag)
        {
            m_scope.EnsureMutable();
            if (string.IsNullOrEmpty(signalTag))
            {
                throw new ArgumentException(
                    "OPC 30081 requires a non-empty SignalTag.",
                    nameof(signalTag));
            }
            State.SignalTag!.Value = signalTag;
            return this;
        }

        public IProcessValueBuilder WithSimulation(
            double simulationValue = 0,
            bool simulationState = false)
        {
            m_scope.EnsureMutable();
            ISystemContext context = m_scope.Context;
            Signal.AddActualValue(context, v => v.WrappedValue = Variant.From(m_value));
            Signal.AddSimulationValue(
                context,
                v => v.WrappedValue = Variant.From(simulationValue));
            Signal.AddSimulationState(context, v => v.Value = simulationState);
            m_simulationValue = simulationValue;
            m_simulating = simulationState;
            m_scope.RecordFacet(MachineryFacet.ProcessValuesSimulation);
            PublishReading(context);
            return this;
        }

        public IProcessValueBuilder WithEngineeringUnits(
            EUInformation engineeringUnits,
            Opc.Ua.Range? euRange = null)
        {
            m_scope.EnsureMutable();
            Signal.AddEngineeringUnits(m_scope.Context, v => v.Value = engineeringUnits);
            if (euRange != null)
            {
                Signal.AddEURange(m_scope.Context, v => v.Value = euRange);
            }
            return this;
        }

        public IProcessValueBuilder WithLimits(
            double? lowLow = null,
            double? low = null,
            double? high = null,
            double? highHigh = null)
        {
            m_scope.EnsureMutable();
            m_lowLow = lowLow ?? m_lowLow;
            m_low = low ?? m_low;
            m_high = high ?? m_high;
            m_highHigh = highHigh ?? m_highHigh;
            m_scope.RecordFacet(MachineryFacet.ProcessValueLimits);
            if (lowLow.HasValue)
            {
                Signal.AddLowLowLimit(
                    m_scope.Context,
                    v => v.WrappedValue = Variant.From(lowLow.Value));
            }
            if (low.HasValue)
            {
                Signal.AddLowLimit(
                    m_scope.Context,
                    v => v.WrappedValue = Variant.From(low.Value));
            }
            if (high.HasValue)
            {
                Signal.AddHighLimit(
                    m_scope.Context,
                    v => v.WrappedValue = Variant.From(high.Value));
            }
            if (highHigh.HasValue)
            {
                Signal.AddHighHighLimit(
                    m_scope.Context,
                    v => v.WrappedValue = Variant.From(highHigh.Value));
            }
            return this;
        }

        public IProcessValueBuilder WithSetpoint(double setpoint)
        {
            m_scope.EnsureMutable();
            State.AddProcessValueSetpoint(
                m_scope.Context,
                v => v.WrappedValue = Variant.From(setpoint));
            m_setpoint = setpoint;
            m_scope.RecordFacet(MachineryFacet.ProcessValueSetpoint);
            return this;
        }

        public IProcessValueBuilder WithDeviationLimits(
            double? lowLow = null,
            double? low = null,
            double? high = null,
            double? highHigh = null,
            bool? autoAdjustment = null,
            ushort? sensitivity = null)
        {
            m_scope.EnsureMutable();
            ProcessValueSetpointVariableState setpoint = State.ProcessValueSetpoint ??
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Deviations are declared on the setpoint, so WithSetpoint must be " +
                    "called before WithDeviationLimits.");

            m_lowLowDeviation = lowLow ?? m_lowLowDeviation;
            m_lowDeviation = low ?? m_lowDeviation;
            m_highDeviation = high ?? m_highDeviation;
            m_highHighDeviation = highHigh ?? m_highHighDeviation;

            if (lowLow.HasValue)
            {
                setpoint.AddLowLowDeviation(
                    m_scope.Context,
                    v => v.WrappedValue = Variant.From(lowLow.Value));
            }
            if (low.HasValue)
            {
                setpoint.AddLowDeviation(
                    m_scope.Context,
                    v => v.WrappedValue = Variant.From(low.Value));
            }
            if (high.HasValue)
            {
                setpoint.AddHighDeviation(
                    m_scope.Context,
                    v => v.WrappedValue = Variant.From(high.Value));
            }
            if (highHigh.HasValue)
            {
                setpoint.AddHighHighDeviation(
                    m_scope.Context,
                    v => v.WrappedValue = Variant.From(highHigh.Value));
            }
            if (autoAdjustment.HasValue)
            {
                setpoint.AddAutoDeviationAdjustment(
                    m_scope.Context,
                    v => v.Value = autoAdjustment.Value);
                m_scope.RecordFacet(MachineryFacet.ProcessValueDeviationAutoAdjustment);
            }
            if (sensitivity.HasValue)
            {
                setpoint.AddDeviationSensitivity(
                    m_scope.Context,
                    v => v.Value = sensitivity.Value);
                m_scope.RecordFacet(MachineryFacet.ProcessValueDeviationSensitivity);
            }
            if (lowLow.HasValue || low.HasValue || high.HasValue || highHigh.HasValue)
            {
                m_scope.RecordFacet(MachineryFacet.ProcessValueDeviationBase);
            }
            return this;
        }

        public IProcessValueBuilder WithLimitAlarm(
            double? lowLow = null,
            double? low = null,
            double? high = null,
            double? highHigh = null,
            Action<ExclusiveLimitAlarmState>? configure = null)
        {
            m_scope.EnsureMutable();
            State.AddLimitAlarm(m_scope.Context);
            m_limitAlarm = new MachineryProcessValueAlarm(
                m_scope,
                State,
                Signal,
                State.LimitAlarm!,
                deviation: false);

            // The limits are already on the signal; mirroring them onto the
            // alarm is what makes it fire, and is the only place OPC 10000-9
            // looks for them.
            m_limitAlarm.SetLimits(
                lowLow ?? m_lowLow,
                low ?? m_low,
                high ?? m_high,
                highHigh ?? m_highHigh);
            EnsureNotifier();
            configure?.Invoke(State.LimitAlarm!);
            m_scope.RecordFacet(MachineryFacet.ProcessValueLimitAlarm);
            return this;
        }

        public IProcessValueBuilder WithDeviationAlarm(
            double? lowLow = null,
            double? low = null,
            double? high = null,
            double? highHigh = null,
            Action<ExclusiveDeviationAlarmState>? configure = null)
        {
            m_scope.EnsureMutable();
            if (State.ProcessValueSetpoint == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "A deviation alarm measures against the setpoint, so " +
                    "WithSetpoint must be called before WithDeviationAlarm.");
            }
            State.AddDeviationAlarm(m_scope.Context);
            m_deviationAlarm = new MachineryProcessValueAlarm(
                m_scope,
                State,
                Signal,
                State.DeviationAlarm!,
                deviation: true);
            m_deviationAlarm.SetLimits(
                lowLow ?? m_lowLowDeviation,
                low ?? m_lowDeviation,
                high ?? m_highDeviation,
                highHigh ?? m_highHighDeviation);
            m_deviationAlarm.SetSetpointNode(State.ProcessValueSetpoint);
            EnsureNotifier();
            configure?.Invoke(State.DeviationAlarm!);
            m_scope.RecordFacet(MachineryFacet.ProcessValueDeviationAlarm);
            return this;
        }

        public IProcessValueBuilder WithPercentageValue()
        {
            m_scope.EnsureMutable();
            Signal.AddPercentageValue(m_scope.Context);
            m_scope.RecordFacet(MachineryFacet.ProcessValuePercentage);
            UpdatePercentage();
            return this;
        }

        public IProcessValueBuilder WithValue(double value)
        {
            m_scope.EnsureMutable();
            m_value = value;
            Signal.WrappedValue = Variant.From(Reported);
            UpdatePercentage();
            return this;
        }

        public IProcessValueBuilder Bind(out IProcessValueHandle handle)
        {
            handle = this;
            return this;
        }

        public IProcessValueBuilder WithStatus(ushort status = 0, ushort alarmSuppression = 0)
        {
            m_scope.EnsureMutable();
            State.AddStatus(m_scope.Context, v => v.Value = status);
            State.AddAlarmSuppression(m_scope.Context, v => v.Value = alarmSuppression);
            m_scope.RecordFacet(MachineryFacet.ProcessValueStatus);
            return this;
        }

        public IProcessValueBuilder WithZeroPointAdjustment(
            Func<ProcessValueState, CancellationToken, ValueTask<StatusCode>> onAdjust)
        {
            if (onAdjust == null)
            {
                throw new ArgumentNullException(nameof(onAdjust));
            }
            m_scope.EnsureMutable();

            State.AddZeroPointAdjustment(m_scope.Context);
            MethodState method = State.ZeroPointAdjustment ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The generated ProcessValueType instance is missing " +
                    "ZeroPointAdjustment.");

            // The event is reported on the process value itself, so it has to
            // be a notifier; the root-notifier registration is what makes a
            // subscription on the Server object see it.
            EnsureNotifier();

            method.OnCallMethod2Async = async (context, _, _, _, _, ct) =>
            {
                StatusCode status;
                try
                {
                    status = await onAdjust(State, ct).ConfigureAwait(false);
                }
                catch (ServiceResultException ex)
                {
                    status = ex.StatusCode;
                }
                RaiseZeroPointAdjustment(context, status);
                return StatusCode.IsBad(status)
                    ? new ServiceResult(status.Code)
                    : ServiceResult.Good;
            };

            m_scope.PostRegistrationActions.Add(BindZeroPointAdjustmentAsync);
            m_scope.RecordFacet(MachineryFacet.ZeroPointAdjustment);
            return this;
        }

        private async ValueTask BindZeroPointAdjustmentAsync(CancellationToken cancellationToken)
        {
            if (m_scope.BuildContext.Manager is not AsyncCustomNodeManager manager)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "OPC 40001-2 zero-point adjustment requires an asynchronous custom " +
                    "node manager.");
            }
            m_zeroPointEventTypeId = await MachineryConcreteEventTypes.EnsureAsync(
                manager,
                m_scope.BuildContext.InstanceNamespaceIndex,
                NodeId.Create(
                    Opc.Ua.Machinery.ProcessValues.ObjectTypes.ZeroPointAdjustmentEventType,
                    Opc.Ua.Machinery.ProcessValues.Namespaces.MachineryProcessValues,
                    m_scope.Context.NamespaceUris),
                MachineryConcreteEventTypes.ZeroPointAdjustmentEventInstanceType,
                cancellationToken).ConfigureAwait(false);
        }

        private void RaiseZeroPointAdjustment(ISystemContext context, StatusCode status)
        {
            if (m_zeroPointEventTypeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The concrete ZeroPointAdjustmentEventType subtype has not been " +
                    "registered; OPC UA forbids reporting an event with an abstract " +
                    "type definition.");
            }
            ZeroPointAdjustmentEventState eventState =
                context.CreateInstanceOfZeroPointAdjustmentEventType(State, default);
            eventState.Initialize(
                context,
                State,
                StatusCode.IsBad(status) ? EventSeverity.High : EventSeverity.Medium,
                new LocalizedText(
                    $"Zero-point adjustment of '{State.BrowseName.Name}' completed."));
            eventState.ZeroPointAdjustmentResult!.Value = status;
            eventState.TypeDefinitionId = m_zeroPointEventTypeId;
            eventState.EventType!.Value = m_zeroPointEventTypeId;
            State.ReportEvent(context, eventState);
        }

        NodeId IProcessValueHandle.NodeId => State.NodeId;

        double IProcessValueHandle.Value => m_value;

        double? IProcessValueHandle.Setpoint => m_setpoint;

        public ValueTask SetValueAsync(
            double value,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            m_value = value;
            PublishReading(m_scope.Context);
            return default;
        }

        public ValueTask SetSetpointAsync(
            double setpoint,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            if (State.ProcessValueSetpoint == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The process value '{0}' publishes no setpoint.",
                    State.BrowseName);
            }
            ISystemContext context = m_scope.Context;
            m_setpoint = setpoint;
            State.ProcessValueSetpoint.WrappedValue = Variant.From(setpoint);
            State.ProcessValueSetpoint.ClearChangeMasks(context, includeChildren: false);
            EvaluateDeviation(context);
            return default;
        }

        public ValueTask SetStatusAsync(
            ushort status,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            if (State.Status == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The process value '{0}' publishes no status.",
                    State.BrowseName);
            }
            State.Status.Value = status;
            State.Status.ClearChangeMasks(m_scope.Context, includeChildren: false);
            return default;
        }

        public ValueTask SetSimulationAsync(
            bool simulationState,
            double? simulationValue = null,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            if (Signal.SimulationState == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The process value '{0}' publishes no simulation members.",
                    State.BrowseName);
            }
            ISystemContext context = m_scope.Context;
            if (simulationValue.HasValue)
            {
                m_simulationValue = simulationValue.Value;
                Signal.SimulationValue!.WrappedValue = Variant.From(m_simulationValue);
                Signal.SimulationValue.ClearChangeMasks(context, includeChildren: false);
            }
            m_simulating = simulationState;
            Signal.SimulationState.Value = simulationState;
            Signal.SimulationState.ClearChangeMasks(context, includeChildren: false);
            PublishReading(context);
            return default;
        }

        /// <summary>
        /// Writes the reading a client sees and re-evaluates everything derived
        /// from it.
        /// </summary>
        /// <remarks>
        /// The reported value is the simulated one while OPC 30081 simulation
        /// is active and the measured one otherwise; <c>ActualValue</c>, when
        /// present, always carries the measured one.
        /// </remarks>
        private void PublishReading(ISystemContext context)
        {
            if (Signal.ActualValue != null)
            {
                Signal.ActualValue.WrappedValue = Variant.From(m_value);
                Signal.ActualValue.Timestamp = DateTime.UtcNow;
            }

            Signal.WrappedValue = Variant.From(Reported);
            Signal.Timestamp = DateTime.UtcNow;
            UpdatePercentage();
            Signal.ClearChangeMasks(context, includeChildren: true);

            m_limitAlarm?.Evaluate(context, Reported);
            EvaluateDeviation(context);
        }

        /// <summary>
        /// The reading a client sees: the simulated one while OPC 30081
        /// simulation is active, the measured one otherwise.
        /// </summary>
        private double Reported => m_simulating ? m_simulationValue : m_value;

        private void EvaluateDeviation(ISystemContext context)
        {
            if (m_deviationAlarm == null || !m_setpoint.HasValue)
            {
                return;
            }
            m_deviationAlarm.Evaluate(context, Reported - m_setpoint.Value);
        }

        /// <summary>
        /// Keeps <c>PercentageValue</c> in step with the reading. OPC 40001-2
        /// defines it against the instrument range, so it is only meaningful
        /// once <c>EURange</c> is published.
        /// </summary>
        private void UpdatePercentage()
        {
            if (Signal.PercentageValue == null ||
                Signal.EURange?.Value is not Opc.Ua.Range range ||
                range.High <= range.Low)
            {
                return;
            }
            double percentage = (Reported - range.Low) / (range.High - range.Low) * 100.0;
            Signal.PercentageValue.WrappedValue = Variant.From(percentage);
        }

        /// <summary>
        /// An alarm is only observable through a notifier, and only reaches a
        /// client subscribing on the <c>Server</c> object through a root
        /// notifier registration.
        /// </summary>
        private void EnsureNotifier()
        {
            State.EventNotifier = EventNotifiers.SubscribeToEvents;
            if (m_notifierRegistered)
            {
                return;
            }
            m_notifierRegistered = true;
            m_scope.PostRegistrationActions.Add(_ =>
            {
                m_scope.BuildContext.Nodes.NodeManager.AddRootNotifier(State);
                return default;
            });
        }

        private bool m_simulating;
        private double m_simulationValue;
        private readonly MachineryBuildScope m_scope;
        private NodeId m_zeroPointEventTypeId = NodeId.Null;
        private MachineryProcessValueAlarm? m_limitAlarm;
        private MachineryProcessValueAlarm? m_deviationAlarm;
        private double m_value;
        private double? m_setpoint;
        private double? m_lowLow;
        private double? m_low;
        private double? m_high;
        private double? m_highHigh;
        private double? m_lowLowDeviation;
        private double? m_lowDeviation;
        private double? m_highDeviation;
        private double? m_highHighDeviation;
        private bool m_notifierRegistered;
    }
}
