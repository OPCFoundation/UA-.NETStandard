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
using Opc.Ua.Server;

namespace Opc.Ua.CranesHoists.Server
{
    /// <summary>
    /// Owns one crane's coherent publication, authorized requests and provider lifetime.
    /// </summary>
    /// <remarks>
    /// Equipment callbacks run without holding the publication lock. Requests never overwrite
    /// provider-reported active limits. Disposing ends observation/configuration and drains owned
    /// callbacks; RemoveAsync additionally asks the existing owner to remove the native tree.
    /// </remarks>
    public sealed class CraneSystemHandle : IAsyncDisposable, IConformanceContributor
    {
        /// <summary>
        /// Binds one materialized system to its independently configured providers.
        /// </summary>
        internal CraneSystemHandle(CraneSystemBuilder definition, CraneGraph graph)
        {
            m_definition = definition;
            m_graph = graph;
            foreach (CraneSignalBinding binding in graph.Signals.Values)
            {
                InitializeGeometry(binding);
                m_values.Add(binding.Address, new DataValue(
                    binding.Node.WrappedValue, binding.Node.StatusCode, binding.Node.Timestamp));
                if (binding.Telemetry)
                {
                    m_telemetryCount++;
                }
                if (binding.Telemetry || binding.Request)
                {
                    binding.Node.OnReadValueAsync = (context, _, range, encoding, cancellationToken) =>
                        ReadAsync(binding, context, range, encoding, cancellationToken);
                }
                if (binding.Request)
                {
                    binding.Node.AccessLevel = AccessLevels.CurrentReadOrWrite;
                    binding.Node.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                    binding.Node.OnSimpleWriteValueAsync = (context, _, value, cancellationToken) =>
                        WriteAsync(binding, context, value, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Gets the native generated system state for advanced use.
        /// </summary>
        public CraneMotionDeviceSystemState State => m_graph.State;

        /// <summary>
        /// Gets the native root identifier.
        /// </summary>
        public NodeId NodeId => State.NodeId;

        /// <summary>
        /// Gets the provider's system-local root identifier.
        /// </summary>
        public string Id => m_definition.Id;

        /// <summary>
        /// Gets whether the runtime has begun shutdown.
        /// </summary>
        public bool IsDisposed
        {
            get
            {
                lock (m_gate)
                {
                    return m_stopping;
                }
            }
        }

        /// <summary>
        /// Gets the selected event notifier, or null NodeId when events were not selected.
        /// </summary>
        public NodeId EventNotifierId => m_graph.EventPublisher?.Notifier.NodeId ?? NodeId.Null;

        /// <summary>
        /// Gets the exact telemetry addresses required from the provider.
        /// </summary>
        public ArrayOf<CraneSignalAddress> TelemetrySignals
        {
            get
            {
                var result = new List<CraneSignalAddress>();
                foreach (CraneSignalBinding binding in m_graph.Signals.Values)
                {
                    if (binding.Telemetry)
                    {
                        result.Add(binding.Address);
                    }
                }
                return result.ToArrayOf();
            }
        }

        /// <inheritdoc/>
        public ArrayOf<string> ServerProfiles => IsDisposed
            ? []
            : ["http://opcfoundation.org/UA-Profile/CranesHoists/Server/Basic"];

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits
        {
            get
            {
                if (IsDisposed)
                {
                    return [];
                }
                ushort ns = CraneSignals.Index(m_graph.Context.NamespaceUris, CranesHoistsModel.NamespaceUri);
                var units = new List<QualifiedName>
                {
                    new("Cranes Base Info CraneMotionDeviceSystemType", ns),
                    new("Cranes Base Info CraneMotionDeviceType", ns),
                    new("Cranes Base Info CraneAxisType", ns),
                    new("Cranes Base Info CraneMotorType", ns),
                    new("Cranes Base Info CraneOperationalModeEnum", ns),
                    new("Crane Base Info CraneMotionDeviceCategoryEnum", ns)
                };
                bool brake = false;
                bool protection = false;
                foreach ((BaseObjectState _, CraneElementKind kind) in m_graph.Elements.Values)
                {
                    brake |= kind == CraneElementKind.Brake;
                    protection |= kind == CraneElementKind.ProtectiveFunction;
                }
                if (brake)
                {
                    units.Add(new QualifiedName("Cranes Base Info BrakeType", ns));
                }
                if (protection)
                {
                    units.Add(new QualifiedName("Cranes Base Info ProtectiveFunctionType", ns));
                }
                if (m_definition.ExternalControl)
                {
                    units.Add(new QualifiedName("Crane Base Info ExternalControlRequestEnum", ns));
                }
                bool mode = false;
                bool references = false;
                foreach (CraneProtectionBuilder selected in m_definition.Protections)
                {
                    mode |= selected.Mode.HasValue;
                    references |= selected.Axes.Count > 0;
                }
                if (mode)
                {
                    units.Add(new QualifiedName("Crane Base Info ProtectiveFunctionEnum", ns));
                }
                if (references)
                {
                    units.Add(new QualifiedName("Crane Base Info ProtectsAxis", ns));
                }
                return units.ToArrayOf();
            }
        }

        /// <summary>
        /// Gets a materialized native component without casting Variant payloads.
        /// </summary>
        public BaseObjectState GetElement(string componentId)
        {
            return m_graph.Elements.TryGetValue(componentId, out (BaseObjectState State, CraneElementKind Kind) element)
                ? element.State
                : throw ServiceResultException.Create(
                    StatusCodes.BadNotFound, "Component '{0}' is absent.", componentId);
        }

        /// <summary>
        /// Gets the real standard variable for an explicitly selected signal.
        /// </summary>
        public BaseVariableState GetSignal(CraneSignalAddress address)
        {
            return m_graph.Signals.TryGetValue(address, out CraneSignalBinding? binding)
                ? binding.Node
                : throw ServiceResultException.Create(StatusCodes.BadNotFound, "Signal '{0}' is absent.", address);
        }

        /// <summary>
        /// Returns the last complete sample, preserving quality and acquisition time.
        /// </summary>
        public DataValue ReadSignal(CraneSignalAddress address)
        {
            lock (m_gate)
            {
                if (m_stopping)
                {
                    return DataValue.FromStatusCode(StatusCodes.BadShutdown);
                }
                return m_values.TryGetValue(address, out DataValue value)
                    ? value
                    : throw ServiceResultException.Create(StatusCodes.BadNotFound, "Signal '{0}' is absent.", address);
            }
        }

        /// <summary>
        /// Acquires and atomically stages a complete provider report; a rejected report changes no signal.
        /// </summary>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            using OperationLease operation = EnterOperation();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_shutdown.Token);
            ArrayOf<CraneSample> report = await m_definition.Provider!.ReadAsync(linked.Token).ConfigureAwait(false);
            Dictionary<CraneSignalAddress, DataValue> staged = ValidateReport(report);
            linked.Token.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                ThrowIfStopping();
                var next = new Dictionary<CraneSignalAddress, DataValue>(m_values);
                foreach (KeyValuePair<CraneSignalAddress, DataValue> sample in staged)
                {
                    next[sample.Key] = sample.Value;
                    Apply(m_graph.Signals[sample.Key].Node, sample.Value);
                    if (m_geometryUpdates.TryGetValue(sample.Key, out GeometryUpdater? update))
                    {
                        update(sample.Value);
                    }
                }
                m_values = next;
            }
            if (ReferenceEquals(m_definition.Context.Robotics.Manager.FindPredefinedNode(NodeId), State))
            {
                await State.ClearChangeMasksAsync(m_graph.Context, true, linked.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Publishes an application-supplied standard event through the existing Machinery publisher.
        /// </summary>
        public async ValueTask PublishEventAsync(
            BaseEventState notification,
            CancellationToken cancellationToken = default)
        {
            if (notification == null)
            {
                throw new ArgumentNullException(nameof(notification));
            }
            using OperationLease operation = EnterOperation();
            cancellationToken.ThrowIfCancellationRequested();
            if (m_graph.EventPublisher == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadNotSupported, "Event publication was not selected.");
            }
            await m_graph.EventPublisher.PublishAsync(notification, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Removes this system through its existing owner, including inverse protection and discovery references.
        /// </summary>
        public async ValueTask RemoveAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(m_definition.Context.Robotics.Manager.FindPredefinedNode(NodeId), State))
            {
                await m_definition.Context.Robotics.Manager.DeleteNodeAsync(
                    m_definition.Context.Robotics.Manager.SystemContext, NodeId, cancellationToken)
                    .ConfigureAwait(false);
            }
            await DisposeAsync().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_operationDepth.Value != 0)
            {
                throw new InvalidOperationException("Dispose the crane outside its equipment callback.");
            }
            bool owner;
            lock (m_gate)
            {
                owner = !m_stopping;
                m_stopping = true;
                if (m_operations == 0)
                {
                    m_drained.TrySetResult(true);
                }
            }
            if (!owner)
            {
                await m_disposed.Task.ConfigureAwait(false);
                return;
            }
            Exception? error = null;
            try
            {
                try
                {
                    m_shutdown.Cancel();
                }
                catch (AggregateException cancellationError)
                {
                    error = cancellationError;
                }
                await m_drained.Task.ConfigureAwait(false);
                foreach (CraneSignalBinding binding in m_graph.Signals.Values)
                {
                    if (binding.Telemetry || binding.Request)
                    {
                        binding.Node.StatusCode = StatusCodes.BadShutdown;
                        binding.Node.AccessLevel = AccessLevels.CurrentRead;
                        binding.Node.UserAccessLevel = AccessLevels.CurrentRead;
                    }
                }
                foreach (BaseVariableState variable in m_geometryVariables)
                {
                    variable.StatusCode = StatusCodes.BadShutdown;
                    variable.AccessLevel = AccessLevels.CurrentRead;
                    variable.UserAccessLevel = AccessLevels.CurrentRead;
                }
                if (m_definition.OwnsProvider && m_definition.Provider is IAsyncDisposable provider)
                {
                    await provider.DisposeAsync().ConfigureAwait(false);
                }
                else if (m_definition.OwnsProvider && m_definition.Provider is IDisposable synchronousProvider)
                {
                    synchronousProvider.Dispose();
                }
                if (error != null)
                {
                    throw error;
                }
            }
            catch (Exception failure)
            {
                m_disposed.TrySetException(failure);
                throw;
            }
            finally
            {
                m_shutdown.Dispose();
            }
            m_disposed.TrySetResult(true);
        }

        /// <summary>
        /// Reuses generated structured-variable bindings so native geometry fields track the same acquisition.
        /// </summary>
        private void InitializeGeometry(CraneSignalBinding binding)
        {
            if (binding.Node is ThreeDVectorState vector)
            {
                var projection = new ThreeDVectorValue(vector, new ThreeDVector(), m_gate);
                ArrayOf<BaseVariableState> variables = GeometryVariables(vector, binding.Address);
                m_geometryUpdates.Add(binding.Address, (in DataValue sample) =>
                {
                    if (sample.WrappedValue.TryGetStructure<ThreeDVector>(out ThreeDVector? value))
                    {
                        projection.Value = value;
                    }
                    projection.Timestamp = sample.SourceTimestamp;
                    ApplyGeometryQuality(variables, sample);
                });
            }
            else if (binding.Node is ThreeDFrameState frame)
            {
                var projection = new ThreeDFrameValue(frame, new ThreeDFrame(), m_gate);
                var coordinates = new ThreeDCartesianCoordinatesValue(
                    frame.CartesianCoordinates!, new ThreeDCartesianCoordinates(), m_gate);
                var orientation = new ThreeDOrientationValue(
                    frame.Orientation!, new ThreeDOrientation(), m_gate);
                ArrayOf<BaseVariableState> variables = GeometryVariables(frame, binding.Address);
                m_geometryUpdates.Add(binding.Address, (in DataValue sample) =>
                {
                    if (sample.WrappedValue.TryGetStructure<ThreeDFrame>(out ThreeDFrame? value))
                    {
                        projection.Value = value;
                        coordinates.Value = value.CartesianCoordinates;
                        orientation.Value = value.Orientation;
                    }
                    projection.Timestamp = sample.SourceTimestamp;
                    coordinates.Timestamp = sample.SourceTimestamp;
                    orientation.Timestamp = sample.SourceTimestamp;
                    ApplyGeometryQuality(variables, sample);
                });
            }
        }

        /// <summary>
        /// Tracks native geometry leaves for acquisition quality and shutdown, not an alternative geometry protocol.
        /// </summary>
        private ArrayOf<BaseVariableState> GeometryVariables(
            BaseVariableState root,
            CraneSignalAddress address)
        {
            var variables = new List<BaseVariableState>();
            var pending = new Queue<NodeState>();
            pending.Enqueue(root);
            while (pending.Count > 0)
            {
                NodeState current = pending.Dequeue();
                if (current is BaseVariableState variable)
                {
                    variable.AccessLevel = AccessLevels.CurrentRead;
                    variable.UserAccessLevel = AccessLevels.CurrentRead;
                    NodeValueEventHandler? read = variable.OnReadValue;
                    if (read != null)
                    {
                        variable.OnReadValueAsync = (context, node, range, encoding, cancellationToken) =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            lock (m_gate)
                            {
                                DataValue acquired = ReadSignal(address);
                                if (StatusCode.IsBad(acquired.StatusCode))
                                {
                                    return new ValueTask<AttributeReadResult>(new AttributeReadResult(
                                        ServiceResult.Good, Variant.Null,
                                        acquired.StatusCode, acquired.SourceTimestamp));
                                }
                                Variant projected = Variant.Null;
                                StatusCode quality = acquired.StatusCode;
                                DateTimeUtc time = acquired.SourceTimestamp;
                                ServiceResult result = read(
                                    context, node, range, encoding, ref projected, ref quality, ref time);
                                return new ValueTask<AttributeReadResult>(new AttributeReadResult(
                                    result, projected,
                                    ServiceResult.IsBad(result) ? result.StatusCode : acquired.StatusCode,
                                    acquired.SourceTimestamp));
                            }
                        };
                    }
                    variables.Add(variable);
                    m_geometryVariables.Add(variable);
                }
                var children = new List<BaseInstanceState>();
                current.GetChildren(m_graph.Context, children);
                foreach (BaseInstanceState child in children)
                {
                    pending.Enqueue(child);
                }
            }
            return variables.ToArrayOf();
        }

        /// <summary>
        /// Keeps every structured projection's quality and source timestamp consistent with its parent.
        /// </summary>
        private static void ApplyGeometryQuality(ArrayOf<BaseVariableState> variables, in DataValue sample)
        {
            foreach (BaseVariableState variable in variables)
            {
                variable.StatusCode = sample.StatusCode;
                variable.Timestamp = sample.SourceTimestamp;
                variable.UpdateChangeMasks(NodeStateChangeMasks.Value);
            }
        }

        /// <summary>
        /// Preserves async-read errors and index-range/data-encoding semantics.
        /// </summary>
        private ValueTask<AttributeReadResult> ReadAsync(
            CraneSignalBinding binding,
            ISystemContext context,
            NumericRange range,
            QualifiedName encoding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DataValue sample = ReadSignal(binding.Address);
            Variant value = sample.WrappedValue;
            if (StatusCode.IsBad(sample.StatusCode))
            {
                return new ValueTask<AttributeReadResult>(new AttributeReadResult(
                    ServiceResult.Good, value, sample.StatusCode, sample.SourceTimestamp));
            }
            ServiceResult result = BaseVariableState.ApplyIndexRangeAndDataEncoding(
                context, range, encoding, ref value);
            return new ValueTask<AttributeReadResult>(new AttributeReadResult(
                result, value, ServiceResult.IsBad(result) ? result.StatusCode : sample.StatusCode,
                sample.SourceTimestamp));
        }

        /// <summary>
        /// Accepts only authorized provider-backed writes and never changes active-limit telemetry.
        /// </summary>
        private async ValueTask<AttributeWriteResult> WriteAsync(
            CraneSignalBinding binding,
            ISystemContext caller,
            Variant value,
            CancellationToken cancellationToken)
        {
            if (IsDisposed)
            {
                return new AttributeWriteResult(StatusCodes.BadShutdown);
            }
            using OperationLease operation = EnterOperation();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_shutdown.Token);
            var request = new CraneLimitRequest(Id, binding.Address, value);
            if (!await m_definition.Authorizer!.AuthorizeAsync(caller, request, linked.Token).ConfigureAwait(false))
            {
                return new AttributeWriteResult(StatusCodes.BadUserAccessDenied);
            }
            StatusCode validation = ValidateValue(binding.Address.Signal, value);
            if (StatusCode.IsBad(validation))
            {
                return new AttributeWriteResult(validation);
            }
            linked.Token.ThrowIfCancellationRequested();
            StatusCode accepted = await m_definition.LimitProvider!.RequestAsync(request, linked.Token)
                .ConfigureAwait(false);
            if (!StatusCode.IsGood(accepted))
            {
                return new AttributeWriteResult(StatusCode.IsBad(accepted)
                    ? accepted
                    : StatusCodes.BadUnexpectedError);
            }
            var sample = new DataValue(value, StatusCodes.Good,
                new DateTimeUtc(m_definition.Context.TimeProvider.GetUtcNow().UtcDateTime));
            lock (m_gate)
            {
                if (m_stopping)
                {
                    return new AttributeWriteResult(StatusCodes.BadShutdown);
                }
                var next = new Dictionary<CraneSignalAddress, DataValue>(m_values)
                {
                    [binding.Address] = sample
                };
                Apply(binding.Node, sample);
                m_values = next;
            }
            return new AttributeWriteResult(accepted);
        }

        /// <summary>
        /// Validates every selected value before any published value changes.
        /// </summary>
        private Dictionary<CraneSignalAddress, DataValue> ValidateReport(ArrayOf<CraneSample> report)
        {
            if (report.IsNull || report.Count < m_telemetryCount)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNoData, "The provider omitted configured telemetry.");
            }
            if (report.Count > 1024 || report.Count != m_telemetryCount)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTooManyOperations, "The provider report is not bounded.");
            }
            var staged = new Dictionary<CraneSignalAddress, DataValue>();
            foreach (CraneSample sample in report)
            {
                if (sample == null || !m_graph.Signals.TryGetValue(sample.Address, out CraneSignalBinding? binding) ||
                    !binding.Telemetry || staged.ContainsKey(sample.Address))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidArgument,
                        "The provider returned an unknown, duplicate or request signal.");
                }
                DataValue value = sample.Value;
                if (value.WrappedValue.IsNull && StatusCode.IsBad(value.StatusCode))
                {
                    staged.Add(sample.Address, value);
                    continue;
                }
                StatusCode error = ValidateValue(sample.Address.Signal, value.WrappedValue);
                if (StatusCode.IsBad(error))
                {
                    throw new ServiceResultException(error);
                }
                if (!StatusCode.IsBad(value.StatusCode) && value.SourceTimestamp == DateTimeUtc.MinValue)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidTimestampArgument,
                        "A usable acquisition needs its original source time.");
                }
                staged.Add(sample.Address, value);
            }
            foreach (CraneProtectionBuilder protection in m_definition.Protections)
            {
                if (!protection.StateMonitoring)
                {
                    continue;
                }
                DataValue enabled = staged[new CraneSignalAddress(protection.Id, CraneSignal.Enabled)];
                DataValue active = staged[new CraneSignalAddress(protection.Id, CraneSignal.Active)];
                if (active.WrappedValue.TryGetValue(out bool isActive) && isActive &&
                    (!enabled.WrappedValue.TryGetValue(out bool isEnabled) || !isEnabled ||
                        StatusCode.IsBad(enabled.StatusCode)))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState, "An active protective function must be enabled.");
                }
            }
            return staged;
        }

        /// <summary>
        /// Checks exact wire scalar types, finite numeric values and the specification's limit range.
        /// </summary>
        internal static StatusCode ValidateValue(CraneSignal signal, Variant value)
        {
            if (signal == CraneSignal.LoadInertia)
            {
                return value.TryGetStructure<ThreeDVector>(out ThreeDVector? _)
                    ? StatusCodes.Good
                    : StatusCodes.BadTypeMismatch;
            }
            if (signal == CraneSignal.LoadCenterOfMass)
            {
                return value.TryGetStructure<ThreeDFrame>(out ThreeDFrame? _)
                    ? StatusCodes.Good
                    : StatusCodes.BadTypeMismatch;
            }
            if (signal is CraneSignal.OperationalMode or CraneSignal.ExternalControlRequest or
                CraneSignal.SafetyOperationalMode)
            {
                int maximum = signal == CraneSignal.OperationalMode ? 5 :
                    signal == CraneSignal.ExternalControlRequest ? 3 : 4;
                return value.TryGetValue(out int mode)
                    ? mode >= 0 && mode <= maximum ? StatusCodes.Good : StatusCodes.BadOutOfRange
                    : StatusCodes.BadTypeMismatch;
            }
            if (signal == CraneSignal.AxisFunction)
            {
                if (!value.TryGetValue(out ArrayOf<ushort> functions))
                {
                    return StatusCodes.BadTypeMismatch;
                }
                if (functions.IsNull || functions.Count > 64)
                {
                    return StatusCodes.BadOutOfRange;
                }
                foreach (ushort function in functions)
                {
                    if (function > 8)
                    {
                        return StatusCodes.BadOutOfRange;
                    }
                }
                return StatusCodes.Good;
            }
            if (signal is CraneSignal.ActualPosition or CraneSignal.SpeedOverride or
                CraneSignal.DesignedWorkingPeriod or
                CraneSignal.MotorTemperature or CraneSignal.LoadMass or CraneSignal.SpeedLimitDirMinus or
                CraneSignal.SpeedLimitDirPlus or CraneSignal.SpeedLimitDirMinusSetpoint or
                CraneSignal.SpeedLimitDirPlusSetpoint)
            {
                if (!value.TryGetValue(out double number))
                {
                    return StatusCodes.BadTypeMismatch;
                }
                if (double.IsNaN(number) || double.IsInfinity(number))
                {
                    return StatusCodes.BadOutOfRange;
                }
                if (signal is CraneSignal.SpeedOverride or CraneSignal.SpeedLimitDirMinus or
                    CraneSignal.SpeedLimitDirPlus or CraneSignal.SpeedLimitDirMinusSetpoint or
                    CraneSignal.SpeedLimitDirPlusSetpoint)
                {
                    return number is >= 0 and <= 100 ? StatusCodes.Good : StatusCodes.BadOutOfRange;
                }
                return signal == CraneSignal.LoadMass && number < 0
                    ? StatusCodes.BadOutOfRange
                    : StatusCodes.Good;
            }
            return value.TryGetValue(out bool _) ? StatusCodes.Good : StatusCodes.BadTypeMismatch;
        }

        /// <summary>
        /// Publishes one already validated value, including discrete-value text metadata.
        /// </summary>
        private static void Apply(BaseVariableState node, in DataValue value)
        {
            node.WrappedValue = value.WrappedValue;
            node.StatusCode = value.StatusCode;
            node.Timestamp = value.SourceTimestamp;
            if (node is MultiStateValueDiscreteState<ArrayOf<ushort>> discrete)
            {
                Variant asText = Variant.Null;
                if (value.WrappedValue.TryGetValue(out ArrayOf<ushort> functions))
                {
                    var text = new List<LocalizedText>(functions.Count);
                    foreach (ushort function in functions)
                    {
                        text.Add(discrete.EnumValues!.Value[function].DisplayName);
                    }
                    asText = Variant.From(text.ToArrayOf());
                }
                discrete.ValueAsText!.WrappedValue = asText;
                discrete.ValueAsText.StatusCode = value.StatusCode;
                discrete.ValueAsText.Timestamp = value.SourceTimestamp;
            }
        }

        /// <summary>
        /// Starts a cancellable operation without holding a gate across an equipment callback.
        /// </summary>
        private OperationLease EnterOperation()
        {
            if (m_operationDepth.Value != 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadInvalidState, "Equipment callbacks cannot reenter.");
            }
            lock (m_gate)
            {
                ThrowIfStopping();
                m_operations++;
            }
            m_operationDepth.Value++;
            return new OperationLease(this);
        }

        /// <summary>
        /// Completes one callback and wakes shutdown only after the final callback exits.
        /// </summary>
        private void ExitOperation()
        {
            m_operationDepth.Value--;
            lock (m_gate)
            {
                m_operations--;
                if (m_stopping && m_operations == 0)
                {
                    m_drained.TrySetResult(true);
                }
            }
        }

        /// <summary>
        /// Rejects operations after shutdown starts.
        /// </summary>
        private void ThrowIfStopping()
        {
            if (m_stopping)
            {
                throw ServiceResultException.Create(StatusCodes.BadShutdown, "The crane runtime has stopped.");
            }
        }

        /// <summary>
        /// Tracks only a callback's lifetime, not its execution lock.
        /// </summary>
        private sealed class OperationLease : IDisposable
        {
            /// <summary>
            /// Starts the already counted operation.
            /// </summary>
            internal OperationLease(CraneSystemHandle owner)
            {
                m_owner = owner;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_owner.ExitOperation();
            }

            private readonly CraneSystemHandle m_owner;
        }

        /// <summary>
        /// Updates generated geometry projections from one immutable acquisition.
        /// </summary>
        private delegate void GeometryUpdater(in DataValue sample);

        private readonly CraneSystemBuilder m_definition;
        private readonly CraneGraph m_graph;
        private readonly Lock m_gate = new();
        private readonly CancellationTokenSource m_shutdown = new();
        private readonly AsyncLocal<int> m_operationDepth = new();
        private readonly TaskCompletionSource<bool> m_drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> m_disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int m_telemetryCount;
        private readonly Dictionary<CraneSignalAddress, GeometryUpdater> m_geometryUpdates = [];
        private readonly List<BaseVariableState> m_geometryVariables = [];
        private Dictionary<CraneSignalAddress, DataValue> m_values = [];
        private int m_operations;
        private bool m_stopping;
    }
}
