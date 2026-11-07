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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Pumps.Server.Builders;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Pumps.Server
{
    /// <summary>
    /// Attaches standard alarm conditions to explicitly configured pump inputs.
    /// </summary>
    public static class PumpsAlarmExtensions
    {
        /// <summary>
        /// Creates a DI NE107 diagnostic alarm for a Boolean supervision input.
        /// The application explicitly selects the diagnostic category; no category is inferred from a browse name.
        /// Evaluate the returned binding after updating the input.
        /// </summary>
        /// <param name="runtime">The owning pump runtime.</param>
        /// <param name="group">The supervision group containing the input.</param>
        /// <param name="input">The actual Boolean variable, not its containing object.</param>
        /// <param name="browseName">A unique alarm name in the instance namespace.</param>
        /// <param name="category">One of the four non-normal NE107 categories.</param>
        /// <param name="activeValue">The input value indicating an abnormal condition.</param>
        /// <param name="cancellationToken">Cancels creation before publication.</param>
        /// <returns>The condition and its explicit evaluation binding.</returns>
        public static ValueTask<PumpAlarmBinding> AddDiagnosticAlarmAsync(
            this PumpsRuntime runtime,
            IPumpGroupBuilder group,
            BaseVariableState input,
            string browseName,
            DeviceHealthEnumeration category,
            bool activeValue = true,
            CancellationToken cancellationToken = default)
        {
            return runtime.AddDiagnosticAlarmAsync(
                group, input, browseName, category, new PumpAlarmOptions(), activeValue, cancellationToken);
        }

        /// <summary>
        /// Creates a diagnostic with explicit severity and optional standard suppression/shelving support.
        /// </summary>
        public static async ValueTask<PumpAlarmBinding> AddDiagnosticAlarmAsync(
            this PumpsRuntime runtime,
            IPumpGroupBuilder group,
            BaseVariableState input,
            string browseName,
            DeviceHealthEnumeration category,
            PumpAlarmOptions options,
            bool activeValue = true,
            CancellationToken cancellationToken = default)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            options.Validate();
            NodeManagerBuilder builder;
            AlarmConditionState alarm;
            lock (s_registrationGates.GetValue(runtime, static _ => new Lock()))
            {
                Validate(runtime, group, input, browseName, cancellationToken);
                if (!input.WrappedValue.TryGetValue(out bool _))
                {
                    throw new ArgumentException("The supervision input must contain a Boolean value.", nameof(input));
                }
                Func<NodeState, AlarmConditionState> factory = category switch
                {
                    DeviceHealthEnumeration.FAILURE => static parent => new FailureAlarmState(parent),
                    DeviceHealthEnumeration.CHECK_FUNCTION => static parent => new CheckFunctionAlarmState(parent),
                    DeviceHealthEnumeration.OFF_SPEC => static parent => new OffSpecAlarmState(parent),
                    DeviceHealthEnumeration.MAINTENANCE_REQUIRED =>
                        static parent => new MaintenanceRequiredAlarmState(parent),
                    _ => throw new ArgumentOutOfRangeException(nameof(category))
                };
                builder = runtime.Manager.CreateFluentBuilder(runtime.InstanceNamespaceIndex);
                alarm = builder.Node(group.Node.NodeId)
                    .CreateAlarm(new QualifiedName(browseName, runtime.InstanceNamespaceIndex),
                        parent => WithLifecycle(runtime, factory(parent), options))
                    .MonitorVariable(input).Alarm;
                Initialize(runtime, group.Node, input, alarm);
                ConfigureSeverityAndShelving(runtime, alarm, options);
            }
            await builder.SealAsync(CancellationToken.None).ConfigureAwait(false);
            return await RegisterBindingAsync(runtime, input, alarm, activeValue, null, options.ActiveSeverity)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Creates an exclusive limit alarm for a Double measurement input.
        /// Crossings are strict; equality with an outer limit can remain beyond a configured inner limit.
        /// Evaluate the returned binding after updating the input.
        /// </summary>
        /// <param name="runtime">The owning pump runtime.</param>
        /// <param name="group">The measurement group containing the input.</param>
        /// <param name="input">The actual Double measurement variable.</param>
        /// <param name="browseName">A unique alarm name in the instance namespace.</param>
        /// <param name="limits">Explicit, ordered measurement limits.</param>
        /// <param name="cancellationToken">Cancels creation before publication.</param>
        /// <returns>The condition and its explicit evaluation binding.</returns>
        public static ValueTask<PumpAlarmBinding> AddMeasurementAlarmAsync(
            this PumpsRuntime runtime,
            IPumpGroupBuilder group,
            BaseVariableState input,
            string browseName,
            PumpMeasurementLimits limits,
            CancellationToken cancellationToken = default)
        {
            return runtime.AddMeasurementAlarmAsync(
                group, input, browseName, limits, new PumpAlarmOptions(), cancellationToken);
        }

        /// <summary>
        /// Creates an exclusive limit condition with explicit severity and optional standard lifecycle support.
        /// </summary>
        public static async ValueTask<PumpAlarmBinding> AddMeasurementAlarmAsync(
            this PumpsRuntime runtime,
            IPumpGroupBuilder group,
            BaseVariableState input,
            string browseName,
            PumpMeasurementLimits limits,
            PumpAlarmOptions options,
            CancellationToken cancellationToken = default)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            options.Validate();
            NodeManagerBuilder builder;
            ExclusiveLimitAlarmState alarm;
            lock (s_registrationGates.GetValue(runtime, static _ => new Lock()))
            {
                Validate(runtime, group, input, browseName, cancellationToken);
                if (limits == null)
                {
                    throw new ArgumentNullException(nameof(limits));
                }
                limits.Validate();
                if (!input.WrappedValue.TryGetValue(out double _))
                {
                    throw new ArgumentException("The measurement input must contain a Double value.", nameof(input));
                }
                builder = runtime.Manager.CreateFluentBuilder(runtime.InstanceNamespaceIndex);
                alarm = builder.Node(group.Node.NodeId)
                    .CreateAlarm(new QualifiedName(browseName, runtime.InstanceNamespaceIndex),
                        parent => WithLifecycle(runtime, new ExclusiveLimitAlarmState(parent), options))
                    .WithLimits(limits.HighHigh, limits.High, limits.Low, limits.LowLow)
                    .MonitorVariable(input).Alarm;
                Initialize(runtime, group.Node, input, alarm);
                ConfigureSeverityAndShelving(runtime, alarm, options);
                uint[] measurementTypes =
                [
                    ObjectTypes.MeasurementsType,
                    ObjectTypes.DriveMeasurementsType,
                    ObjectTypes.InletConnectionMeasurementsType,
                    ObjectTypes.OutletConnectionMeasurementsType
                ];
                foreach (uint identifier in measurementTypes)
                {
                    NodeId typeId = PumpsModel.TypeNodeId(identifier, runtime.SystemContext.NamespaceUris);
                    if (runtime.Manager.FindPredefinedNode(typeId) is not NodeState type)
                    {
                        throw new InvalidOperationException("The Pumps measurement type model is not loaded.");
                    }
                    AddGeneratesEvent(type, alarm.TypeDefinitionId);
                }
            }
            await builder.SealAsync(CancellationToken.None).ConfigureAwait(false);
            return await RegisterBindingAsync(runtime, input, alarm, true, limits, options.ActiveSeverity)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Gives the binding the node manager's lifetime and releases it if registration fails.
        /// </summary>
        private static async ValueTask<PumpAlarmBinding> RegisterBindingAsync(
            PumpsRuntime runtime,
            BaseVariableState input,
            AlarmConditionState alarm,
            bool activeValue,
            PumpMeasurementLimits? limits,
            EventSeverity activeSeverity)
        {
            var binding = new PumpAlarmBinding(runtime.SystemContext, input, alarm, activeValue, limits, activeSeverity);
            try
            {
                runtime.Manager.RegisterMachineryResource(binding);
                return binding;
            }
            catch
            {
                await binding.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Materializes optional children before the shared builder initializes their standard method handlers.
        /// </summary>
        /// <typeparam name="TAlarm">The concrete standard alarm condition type.</typeparam>
        private static TAlarm WithLifecycle<TAlarm>(
            PumpsRuntime runtime, TAlarm alarm, PumpAlarmOptions options)
            where TAlarm : AlarmConditionState
        {
            if (options.Suppression)
            {
                alarm.AddSuppressedState(runtime.SystemContext);
                alarm.AddSuppress(runtime.SystemContext);
                alarm.AddUnsuppress(runtime.SystemContext);
            }
            if (options.Shelving)
            {
                alarm.AddShelvingState(runtime.SystemContext);
                alarm.AddMaxTimeShelved(runtime.SystemContext);
            }
            return alarm;
        }

        /// <summary>
        /// Applies configured values after type initialization and before address-space registration.
        /// </summary>
        private static void ConfigureSeverityAndShelving(
            PumpsRuntime runtime, AlarmConditionState alarm, PumpAlarmOptions options)
        {
            alarm.SetSeverity(runtime.SystemContext, EventSeverity.Min);
            if (options.Shelving)
            {
                alarm.MaxTimeShelved!.Value = options.MaximumShelvingTime.TotalMilliseconds;
                alarm.OnShelve = static (context, condition, shelving, oneShot, duration) =>
                {
                    condition.SetShelvingState(context, shelving, oneShot, duration);
                    return ServiceResult.Good;
                };
                alarm.OnTimedUnshelve = static (context, condition) =>
                {
                    condition.SetShelvingState(context, false, false, 0);
                    return ServiceResult.Good;
                };
                alarm.SetShelvingState(runtime.SystemContext, false, false, 0);
            }
        }

        /// <summary>
        /// Rejects foreign inputs and duplicate names before changing the address space.
        /// </summary>
        private static void Validate(
            PumpsRuntime runtime,
            IPumpGroupBuilder group,
            BaseVariableState input,
            string browseName,
            CancellationToken cancellationToken)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            if (group == null)
            {
                throw new ArgumentNullException(nameof(group));
            }
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }
            if (string.IsNullOrWhiteSpace(browseName))
            {
                throw new ArgumentException("An alarm name is required.", nameof(browseName));
            }
            cancellationToken.ThrowIfCancellationRequested();
            NodeState? ancestor = input.Parent;
            while (ancestor != null && !ReferenceEquals(ancestor, group.Node))
            {
                ancestor = (ancestor as BaseInstanceState)?.Parent;
            }
            if (ancestor == null || !ReferenceEquals(runtime.Manager.FindPredefinedNode(input.NodeId), input))
            {
                throw new ArgumentException("The input must belong to this runtime and group.", nameof(input));
            }
            while (ancestor != null && ancestor is not PumpState)
            {
                ancestor = (ancestor as BaseInstanceState)?.Parent;
            }
            if (ancestor is not PumpState pump || runtime.Pump(pump.NodeId) == null)
            {
                throw new ArgumentException("The group must belong to a registered pump.", nameof(group));
            }
            var children = new List<BaseInstanceState>();
            group.Node.GetChildren(runtime.SystemContext, children);
            var name = new QualifiedName(browseName, runtime.InstanceNamespaceIndex);
            if (children.Exists(child => child.BrowseName == name))
            {
                throw new ArgumentException("The alarm name is already in use.", nameof(browseName));
            }
        }

        /// <summary>
        /// Establishes standard lifecycle state and advertises the concrete event type.
        /// </summary>
        private static void Initialize(
            PumpsRuntime runtime,
            NodeState source,
            BaseVariableState input,
            AlarmConditionState alarm)
        {
            alarm.AutoReportStateChanges = true;
            alarm.SetActiveState(runtime.SystemContext, false);
            alarm.SetAcknowledgedState(runtime.SystemContext, true);
            if (alarm.SupportsConfirm())
            {
                alarm.SetConfirmedState(runtime.SystemContext, true);
            }
            alarm.EventType!.Value = alarm.TypeDefinitionId;
            alarm.EventId!.Value = Uuid.NewUuid().ToByteString();
            alarm.Quality!.Value = StatusCodes.Good;
            AddGeneratesEvent(source, alarm.TypeDefinitionId);
            AddGeneratesEvent(input, alarm.TypeDefinitionId);
            if (source is BaseInstanceState instance &&
                runtime.Manager.FindPredefinedNode(instance.TypeDefinitionId) is NodeState type)
            {
                AddGeneratesEvent(type, alarm.TypeDefinitionId);
            }
        }

        /// <summary>
        /// Adds one idempotent event-type declaration.
        /// </summary>
        private static void AddGeneratesEvent(NodeState source, NodeId eventType)
        {
            if (!source.ReferenceExists(Opc.Ua.Types.ReferenceTypeIds.GeneratesEvent, false, eventType))
            {
                source.AddReference(Opc.Ua.Types.ReferenceTypeIds.GeneratesEvent, false, eventType);
            }
        }

        /// <summary>Serializes registration per runtime without retaining disposed owners.</summary>
        private static readonly ConditionalWeakTable<PumpsRuntime, Lock> s_registrationGates = new();
    }

    /// <summary>
    /// Application-selected alarm severity and optional standard lifecycle capabilities.
    /// </summary>
    public sealed record PumpAlarmOptions
    {
        /// <summary>
        /// Gets the severity of an active diagnostic or exceeded limit.
        /// </summary>
        public EventSeverity ActiveSeverity { get; init; } = EventSeverity.High;

        /// <summary>
        /// Gets whether standard suppression and unsuppression methods are exposed.
        /// </summary>
        public bool Suppression { get; init; }

        /// <summary>
        /// Gets whether standard one-shot, timed shelving and unshelving methods are exposed.
        /// </summary>
        public bool Shelving { get; init; }

        /// <summary>
        /// Gets the explicit positive shelving-time limit required when shelving is enabled.
        /// </summary>
        public TimeSpan MaximumShelvingTime { get; init; }

        /// <summary>
        /// Rejects invalid lifecycle options before creating any condition.
        /// </summary>
        internal void Validate()
        {
            if (ActiveSeverity is < EventSeverity.Min or > EventSeverity.Max)
            {
                throw new ArgumentOutOfRangeException(nameof(ActiveSeverity));
            }
            if (Shelving && MaximumShelvingTime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumShelvingTime));
            }
        }
    }

    /// <summary>
    /// Explicit measurement thresholds. NaN omits a threshold; configured thresholds must be strictly increasing.
    /// </summary>
    public sealed record PumpMeasurementLimits
    {
        /// <summary>
        /// Gets the optional low-low threshold.
        /// </summary>
        public double LowLow { get; init; } = double.NaN;

        /// <summary>
        /// Gets the optional low threshold.
        /// </summary>
        public double Low { get; init; } = double.NaN;

        /// <summary>
        /// Gets the optional high threshold.
        /// </summary>
        public double High { get; init; } = double.NaN;

        /// <summary>
        /// Gets the optional high-high threshold.
        /// </summary>
        public double HighHigh { get; init; } = double.NaN;

        /// <summary>
        /// Validates the configured thresholds.
        /// </summary>
        internal void Validate()
        {
            double previous = double.NegativeInfinity;
            bool any = false;
            foreach (double value in new[] { LowLow, Low, High, HighHigh })
            {
                if (double.IsNaN(value))
                {
                    continue;
                }
                if (double.IsInfinity(value) || value <= previous)
                {
                    throw new ArgumentException("Configured limits must be finite and strictly increasing.");
                }
                previous = value;
                any = true;
            }
            if (!any)
            {
                throw new ArgumentException("At least one measurement limit is required.");
            }
        }
    }

    /// <summary>
    /// Evaluates a pump input while delegating acknowledgement, enable/disable, retain and refresh to ConditionState.
    /// </summary>
    public sealed class PumpAlarmBinding : IAsyncDisposable
    {
        /// <summary>
        /// Creates an evaluation binding for an initialized condition.
        /// </summary>
        internal PumpAlarmBinding(
            ISystemContext context,
            BaseVariableState input,
            AlarmConditionState alarm,
            bool activeValue,
            PumpMeasurementLimits? limits,
            EventSeverity activeSeverity)
        {
            m_context = context;
            Input = input;
            Alarm = alarm;
            m_activeValue = activeValue;
            m_limits = limits;
            m_activeSeverity = activeSeverity;
        }

        /// <summary>
        /// Gets the registered standard condition, including its client-callable lifecycle methods.
        /// </summary>
        public AlarmConditionState Alarm { get; }

        /// <summary>
        /// Gets the actual input variable also exposed by the condition's InputNode.
        /// </summary>
        public BaseVariableState Input { get; }

        /// <summary>
        /// Evaluates the current input and reports only changed condition state.
        /// Bad-quality or non-finite readings update Quality without fabricating a normal measurement.
        /// This method does not overwrite input values or enable a condition disabled by a client.
        /// </summary>
        /// <param name="cancellationToken">Cancels before evaluating.</param>
        public async ValueTask EvaluateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lifetimeGate)
            {
                if (m_disposeCompletion is not null)
                {
                    throw new ObjectDisposedException(nameof(PumpAlarmBinding));
                }
                m_evaluations++;
            }
            await EvaluateCoreAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops new evaluations and drains admitted evaluations, including event reports, before releasing the gate.
        /// The node manager calls this automatically. Notification sinks must not await disposal of their binding.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            lock (m_lifetimeGate)
            {
                if (m_disposeCompletion is null)
                {
                    m_disposeCompletion = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    if (m_evaluations == 0)
                    {
                        m_gate.Dispose();
                        m_disposeCompletion.SetResult(true);
                    }
                }
                return new ValueTask(m_disposeCompletion.Task);
            }
        }

        /// <summary>
        /// Releases the admitted evaluation's lifetime hold after state notifications and event reporting finish.
        /// </summary>
        private async ValueTask EvaluateCoreAsync(CancellationToken cancellationToken)
        {
            try
            {
                await EvaluateStateAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (m_lifetimeGate)
                {
                    m_evaluations--;
                    if (m_evaluations == 0 && m_disposeCompletion is not null)
                    {
                        m_gate.Dispose();
                        m_disposeCompletion.SetResult(true);
                    }
                }
            }
        }

        /// <summary>
        /// Serializes live state notifications and reports the resulting event outside the evaluation gate.
        /// </summary>
        private async ValueTask EvaluateStateAsync(CancellationToken cancellationToken)
        {
            bool report = false;
            await m_gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                LimitAlarmStates state = LimitAlarmStates.Inactive;
                StatusCode quality = Input.StatusCode;
                if (StatusCode.IsGood(quality))
                {
                    if (m_limits == null)
                    {
                        if (!Input.WrappedValue.TryGetValue(out bool value))
                        {
                            quality = StatusCodes.BadTypeMismatch;
                        }
                        else if (value == m_activeValue)
                        {
                            state = LimitAlarmStates.High;
                        }
                    }
                    else if (!Input.WrappedValue.TryGetValue(out double value))
                    {
                        quality = StatusCodes.BadTypeMismatch;
                    }
                    else if (double.IsNaN(value) || double.IsInfinity(value))
                    {
                        quality = StatusCodes.BadOutOfRange;
                    }
                    else
                    {
                        state = value > m_limits.HighHigh ? LimitAlarmStates.HighHigh
                            : value > m_limits.High ? LimitAlarmStates.High
                            : value < m_limits.LowLow ? LimitAlarmStates.LowLow
                            : value < m_limits.Low ? LimitAlarmStates.Low
                            : LimitAlarmStates.Inactive;
                    }
                }
                if (!StatusCode.IsGood(quality))
                {
                    state = m_lastState;
                }
                if (m_updatePending || !m_evaluated || state != m_lastState || quality != m_lastQuality)
                {
                    m_updatePending = true;
                    bool newlyActive = state != LimitAlarmStates.Inactive && state != m_lastState;
                    Alarm.Quality!.Value = quality;
                    await Alarm.SetSeverityAsync(
                        m_context,
                        state == LimitAlarmStates.Inactive ? EventSeverity.Min : m_activeSeverity,
                        CancellationToken.None).ConfigureAwait(false);
                    if (newlyActive)
                    {
                        await Alarm.SetAcknowledgedStateAsync(
                            m_context, false, CancellationToken.None).ConfigureAwait(false);
                        if (Alarm.SupportsConfirm())
                        {
                            await Alarm.SetConfirmedStateAsync(
                                m_context, false, CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                    if (Alarm is ExclusiveLimitAlarmState limitAlarm)
                    {
                        await limitAlarm.SetLimitStateAsync(
                            m_context, state, CancellationToken.None).ConfigureAwait(false);
                    }
                    else
                    {
                        await Alarm.SetActiveStateAsync(
                            m_context, state != LimitAlarmStates.Inactive, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    Alarm.Message!.Value = new LocalizedText(
                        state == LimitAlarmStates.Inactive
                            ? "Input is normal." : "Input is outside its configured normal state.");
                    InstanceStateSnapshot? snapshot = null;
                    if (Alarm.EnabledState!.Id!.Value)
                    {
                        Alarm.EventId!.Value = Uuid.NewUuid().ToByteString();
                        Alarm.ReceiveTime!.Value = DateTimeUtc.Now;
                        Alarm.Time!.Value = Input.Timestamp == DateTimeUtc.MinValue
                            ? Alarm.ReceiveTime.Value
                            : Input.Timestamp;
                        snapshot = new InstanceStateSnapshot();
                        snapshot.Initialize(m_context, Alarm);
                    }
                    await Alarm.ClearChangeMasksAsync(m_context, true, CancellationToken.None).ConfigureAwait(false);
                    m_lastState = state;
                    m_lastQuality = quality;
                    m_evaluated = true;
                    m_updatePending = false;
                    if (snapshot is not null)
                    {
                        m_pendingReports.Enqueue(snapshot);
                    }
                }
                if (!m_reporting && m_pendingReports.Count != 0)
                {
                    m_reporting = true;
                    report = true;
                }
            }
            finally
            {
                m_gate.Release();
            }
            if (report)
            {
                await ReportPendingEventsAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Delivers queued snapshots in order, retaining the first failed report for the next evaluation to retry.
        /// </summary>
        private async ValueTask ReportPendingEventsAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    InstanceStateSnapshot snapshot;
                    await m_gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    try
                    {
                        snapshot = m_pendingReports.Peek();
                    }
                    finally
                    {
                        m_gate.Release();
                    }
                    await Alarm.ReportEventAsync(m_context, snapshot, cancellationToken).ConfigureAwait(false);
                    await m_gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    try
                    {
                        m_pendingReports.Dequeue();
                        if (m_pendingReports.Count == 0)
                        {
                            m_reporting = false;
                            return;
                        }
                    }
                    finally
                    {
                        m_gate.Release();
                    }
                }
            }
            catch
            {
                await m_gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    m_reporting = false;
                }
                finally
                {
                    m_gate.Release();
                }
                throw;
            }
        }

        /// <summary>The owning server context.</summary>
        private readonly ISystemContext m_context;

        /// <summary>The configured abnormal Boolean value.</summary>
        private readonly bool m_activeValue;

        /// <summary>The configured measurement limits, or null for a diagnostic.</summary>
        private readonly PumpMeasurementLimits? m_limits;

        /// <summary>The explicitly selected active severity.</summary>
        private readonly EventSeverity m_activeSeverity;

        /// <summary>Serializes evaluations, including their asynchronous state notifications.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>Serializes evaluation admission and disposal without holding across callbacks.</summary>
        private readonly Lock m_lifetimeGate = new();

        /// <summary>Completes once all admitted evaluations have drained and the gate has been disposed.</summary>
        private TaskCompletionSource<bool>? m_disposeCompletion;

        /// <summary>The number of admitted evaluations, including queued evaluations and event reports.</summary>
        private int m_evaluations;

        /// <summary>The last evaluated limit or diagnostic state.</summary>
        private LimitAlarmStates m_lastState;

        /// <summary>The quality of the last successfully completed condition update.</summary>
        private StatusCode m_lastQuality;

        /// <summary>Whether a failed update left live state that must be completed on the next evaluation.</summary>
        private bool m_updatePending;

        /// <summary>Completed condition updates waiting for successful event delivery.</summary>
        private readonly Queue<InstanceStateSnapshot> m_pendingReports = new();

        /// <summary>Whether an admitted evaluation is currently delivering queued reports.</summary>
        private bool m_reporting;

        /// <summary>Whether a baseline evaluation has completed.</summary>
        private bool m_evaluated;
    }
}
