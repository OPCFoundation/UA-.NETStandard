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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Server;

namespace Opc.Ua.PADIM.Server
{
    /// <summary>
    /// Owns the serialized provider bindings of a standalone or composed PA-DIM device.
    /// </summary>
    public sealed partial class PadimDevice : IAsyncDisposable
    {
        /// <summary>
        /// Creates a handle whose lifetime is registered with the DI address-space owner.
        /// </summary>
        public PadimDevice(DiNodeManager manager, ComponentState state, TimeProvider? timeProvider = null)
        {
            Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            State = state as PADIMState ??
                throw new ArgumentException("The state must be a generated PADIMType or subtype.", nameof(state));
            TimeProvider = timeProvider ?? global::System.TimeProvider.System;
            NodeId padimType = ExpandedNodeId.ToNodeId(ObjectTypeIds.PADIMType, Context.NamespaceUris);
            if (!Context.TypeTable.IsTypeOf(state.TypeDefinitionId, padimType))
            {
                throw new ArgumentException("The device must implement PADIMType.", nameof(state));
            }
            bool added = false;
            try
            {
                s_handles.Add(State, this);
                added = true;
                PrimeAcquisition(State.DeviceHealth!);
                Manager.RegisterResource(this);
                Manager.RegisterConformanceContributor(this);
            }
            catch
            {
                if (added)
                {
                    s_handles.Remove(State);
                }
                m_providerGate.Dispose();
                m_stopping.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Gets the DI owner, also used to register newly materialized optional members.
        /// </summary>
        public DiNodeManager Manager { get; }

        /// <summary>
        /// Gets the generated device state, including analyser subtypes.
        /// </summary>
        public PADIMState State { get; }

        /// <summary>
        /// Gets the namespace-aware system context.
        /// </summary>
        public ServerSystemContext Context => Manager.SystemContext;

        /// <summary>
        /// Gets the clock used for accepted configuration changes and simulated samples.
        /// </summary>
        public TimeProvider TimeProvider { get; }

        /// <summary>
        /// Creates an analog signal whose measurement uses the selected published VariableType.
        /// </summary>
        public async ValueTask<AnalogSignalObjectState> AddMeasurementAsync(
            QualifiedName browseName,
            PadimMeasurementKind kind,
            string signalTag,
            Action<AnalogSignalObjectState>? configure = null,
            CancellationToken cancellationToken = default)
        {
            SignalState signal = await AddSignalAsync(
                browseName,
                PadimSignalKind.Analog,
                signalTag,
                created =>
                {
                    var analog = (AnalogSignalObjectState)created;
                    QualifiedName name = analog.AnalogSignal!.BrowseName;
                    AnalogSignalVariableState measurement = kind switch
                    {
                        PadimMeasurementKind.Analog => Context.CreateInstanceOfAnalogSignalVariableType(analog, name),
                        PadimMeasurementKind.Temperature =>
                            Context.CreateInstanceOfTemperatureMeasurementVariableType(analog, name),
                        PadimMeasurementKind.Pressure =>
                            Context.CreateInstanceOfPressureMeasurementVariableType(analog, name),
                        PadimMeasurementKind.Flow => Context.CreateInstanceOfFlowMeasurementVariableType(analog, name),
                        PadimMeasurementKind.Level =>
                            Context.CreateInstanceOfLevelMeasurementVariableType(analog, name),
                        PadimMeasurementKind.Density => Context.CreateInstanceOfActualDensityVariableType(analog, name),
                        PadimMeasurementKind.Totalizer => Context.CreateInstanceOfTotalizerVariableType(analog, name),
                        PadimMeasurementKind.MassFlow => Context.CreateInstanceOfMassFlowRateVariableType(analog, name),
                        PadimMeasurementKind.ActualVolumeFlow =>
                            Context.CreateInstanceOfActualVolumeFlowRateVariableType(analog, name),
                        PadimMeasurementKind.NormalizedVolumeFlow =>
                            Context.CreateInstanceOfNormalizedVolumeFlowRateVariableType(analog, name),
                        PadimMeasurementKind.Analytical =>
                            Context.CreateInstanceOfAnalyticalMeasurementVariableType(analog, name),
                        PadimMeasurementKind.Pat => Context.CreateInstanceOfPatMeasurementVariableType(analog, name),
                        _ => throw new ArgumentOutOfRangeException(nameof(kind))
                    };
                    measurement.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
                    measurement.ModellingRuleId = NodeId.Null;
                    PrimeAcquisition(measurement);
                    DisableUnboundWrites(measurement);
                    analog.CreateOrReplaceAnalogSignal(Context, measurement);
                    configure?.Invoke(analog);
                },
                cancellationToken).ConfigureAwait(false);
            return (AnalogSignalObjectState)signal;
        }

        /// <summary>
        /// Creates a standard signal family below the device's SignalSet.
        /// </summary>
        /// <remarks>
        /// Configure optional generated members before returning from the callback; the whole
        /// signal is registered afterwards so clients can browse and read those members.
        /// </remarks>
        public async ValueTask<SignalState> AddSignalAsync(
            QualifiedName browseName,
            PadimSignalKind kind,
            string signalTag,
            Action<SignalState>? configure = null,
            CancellationToken cancellationToken = default)
        {
            if (browseName.IsNull || string.IsNullOrWhiteSpace(browseName.Name))
            {
                throw new ArgumentException("A signal browse name is required.", nameof(browseName));
            }
            if (string.IsNullOrWhiteSpace(signalTag))
            {
                throw new ArgumentException("SignalTag must be nonempty.", nameof(signalTag));
            }
            SignalState? signal = null;
            await RunAsync(async ct =>
            {
                State.AddSignalSet_no_(Context);
                SignalSetState parent = State.SignalSet_no_!;
                if (parent.FindChild(Context, browseName) != null)
                {
                    throw new ServiceResultException(StatusCodes.BadBrowseNameDuplicated);
                }
                signal = PadimFactories.CreateSignal(Context, parent, browseName, kind);
                signal.SignalTag!.Value = signalTag;
                signal.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
                DisableUnboundWrites(signal);
                var children = new List<BaseInstanceState>();
                signal.GetChildren(Context, children);
                foreach (BaseInstanceState child in children)
                {
                    if (child is BaseVariableState value && child.BrowseName.Name is
                        "AnalogSignal" or "ControlSignal" or "DiscreteSignal" or
                        "TwoStateDiscreteSignal" or "MultiStateDiscreteSignal")
                    {
                        PrimeAcquisition(value);
                    }
                }
                try
                {
                    configure?.Invoke(signal);
                    parent.AddChild(signal);
                    await Manager.AddPredefinedNodeAsync(State, ct).ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    lock (m_bindingGate)
                    {
                        m_bindings.RemoveAll(binding => IsDescendantOf(binding.Variable, signal));
                    }
                    parent.RemoveChild(signal);
                    try
                    {
                        await Manager.DeleteNodeAsync(Context, signal.NodeId, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception cleanupFailure)
                    {
                        throw new AggregateException(
                            "Signal configuration and rollback failed.", failure, cleanupFailure);
                    }
                    throw;
                }
            }, cancellationToken).ConfigureAwait(false);
            return signal!;
        }

        /// <summary>
        /// Registers optional generated members configured after device creation.
        /// </summary>
        public ValueTask RegisterAsync(CancellationToken cancellationToken = default)
        {
            return RunAsync(
                async ct => await Manager.AddPredefinedNodeAsync(State, ct).ConfigureAwait(false),
                cancellationToken);
        }

        /// <summary>
        /// Binds a generated measurement, condition, calibration or nameplate variable to acquired values.
        /// </summary>
        public PadimValueBinding BindValue(
            BaseVariableState variable,
            string key,
            IPadimValueProvider provider)
        {
            EnsureOwned(variable);
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A stable provider key is required.", nameof(key));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            lock (m_bindingGate)
            {
                if (m_closed)
                {
                    throw new ObjectDisposedException(nameof(PadimDevice));
                }
                if (m_bindings.Exists(existing => ReferenceEquals(existing.Variable, variable)))
                {
                    throw new InvalidOperationException("The variable already has a provider binding.");
                }
                var binding = new PadimValueBinding(this, variable, key, provider);
                m_bindings.Add(binding);
                return binding;
            }
        }

        /// <summary>
        /// Acquires each configured field, retaining its individual quality and timestamp.
        /// </summary>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            PadimValueBinding[] bindings;
            lock (m_bindingGate)
            {
                bindings = m_bindings.ToArray();
            }
            foreach (PadimValueBinding binding in bindings)
            {
                await binding.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Binds supported locale configuration and the provider's authoritative last-change timestamp.
        /// </summary>
        public PadimDevice WithAdministration(
            IPadimValueProvider acquisition,
            IPadimWriteProvider writer,
            IPadimAuthorizer authorizer,
            string languageKey = "displayLanguage",
            string lastChangeKey = "dateOfLastChange")
        {
            if (acquisition == null)
            {
                throw new ArgumentNullException(nameof(acquisition));
            }
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }
            if (authorizer == null)
            {
                throw new ArgumentNullException(nameof(authorizer));
            }
            ValidateSignalKey(languageKey);
            ValidateSignalKey(lastChangeKey);
            State.AddDisplayLanguage(Context);
            State.AddDateOfLastChange(Context);
            BindValue(State.DisplayLanguage!, languageKey, acquisition).WithWrites(writer, authorizer);
            BindValue(State.DateOfLastChange!, lastChangeKey, acquisition);
            return this;
        }

        /// <summary>
        /// Materializes and binds device-parameter reset when the provider supports it.
        /// </summary>
        public PadimDevice WithFactoryReset(IPadimOperationProvider provider, IPadimAuthorizer authorizer)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (authorizer == null)
            {
                throw new ArgumentNullException(nameof(authorizer));
            }
            State.AddFactoryReset(Context);
            return WithFactoryReset(State.FactoryReset!, provider, authorizer);
        }

        /// <summary>
        /// Materializes and binds the analog signal's optional physical adjustment method.
        /// </summary>
        public PadimDevice WithZeroPointAdjustment(
            AnalogSignalObjectState signal,
            string signalKey,
            IPadimOperationProvider provider,
            IPadimAuthorizer authorizer)
        {
            EnsureOwned(signal);
            ValidateSignalKey(signalKey);
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (authorizer == null)
            {
                throw new ArgumentNullException(nameof(authorizer));
            }
            signal.AddZeroPointAdjustment(Context);
            return WithZeroPointAdjustment(signal.ZeroPointAdjustment!, signalKey, provider, authorizer);
        }

        /// <summary>
        /// Materializes and binds the positioner's optional commissioning method.
        /// </summary>
        public PadimDevice WithAutoAdjustPositioner(
            ControlSignalObjectState signal,
            string signalKey,
            IPadimOperationProvider provider,
            IPadimAuthorizer authorizer)
        {
            EnsureOwned(signal);
            ValidateSignalKey(signalKey);
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (authorizer == null)
            {
                throw new ArgumentNullException(nameof(authorizer));
            }
            signal.AddAutoAdjustPositioner(Context);
            return WithAutoAdjustPositioner(signal.AutoAdjustPositioner!, signalKey, provider, authorizer);
        }

        /// <summary>
        /// Binds the generated optional FactoryReset method with the published reset-mode validation.
        /// </summary>
        public PadimDevice WithFactoryReset(
            MethodState method,
            IPadimOperationProvider provider,
            IPadimAuthorizer authorizer)
        {
            return BindOperation(method, "FactoryReset", provider, authorizer, async (inputs, ct) =>
            {
                if (inputs.Count != 1 || !inputs[0].TryGetValue(out int mode) || mode is not (1 or 2712 or 2713))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                return await provider.FactoryResetAsync((ResetModeEnum)mode, ct).ConfigureAwait(false);
            });
        }

        /// <summary>
        /// Binds a generated optional zero-point adjustment to its device-owned implementation.
        /// </summary>
        public PadimDevice WithZeroPointAdjustment(
            MethodState method,
            string signalKey,
            IPadimOperationProvider provider,
            IPadimAuthorizer authorizer)
        {
            ValidateSignalKey(signalKey);
            return BindOperation(method, signalKey, provider, authorizer, async (inputs, ct) =>
            {
                if (inputs.Count != 0)
                {
                    return StatusCodes.BadInvalidArgument;
                }
                return await provider.ZeroPointAdjustmentAsync(signalKey, ct).ConfigureAwait(false);
            });
        }

        /// <summary>
        /// Binds positioner start/abort without interpreting acceptance as completed commissioning.
        /// </summary>
        public PadimDevice WithAutoAdjustPositioner(
            MethodState method,
            string signalKey,
            IPadimOperationProvider provider,
            IPadimAuthorizer authorizer)
        {
            ValidateSignalKey(signalKey);
            return BindOperation(method, signalKey, provider, authorizer, async (inputs, ct) =>
            {
                if (inputs.Count != 1 || !inputs[0].TryGetValue(out int mode) || mode is not (2 or 255))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                return await provider.AutoAdjustPositionerAsync(signalKey, (ExecutionModeEnum)mode, ct)
                    .ConfigureAwait(false);
            });
        }

        /// <summary>
        /// Cancels pending work and joins the active provider operation.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            lock (m_bindingGate)
            {
                if (m_disposeTask == null)
                {
                    m_closed = true;
                    if (m_pending == 0)
                    {
                        m_drained.TrySetResult(true);
                    }
                    m_disposeTask = DisposeCoreAsync();
                }
                return new ValueTask(m_disposeTask);
            }
        }

        /// <summary>
        /// Serializes acquisition and configuration against operations on the same device.
        /// </summary>
        internal async ValueTask RunAsync(
            Func<CancellationToken, ValueTask> action,
            CancellationToken cancellationToken)
        {
            lock (m_bindingGate)
            {
                if (m_closed)
                {
                    throw new ObjectDisposedException(nameof(PadimDevice));
                }
                m_pending++;
            }
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, m_stopping.Token);
                await m_providerGate.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    linked.Token.ThrowIfCancellationRequested();
                    await action(linked.Token).ConfigureAwait(false);
                }
                finally
                {
                    m_providerGate.Release();
                }
            }
            finally
            {
                lock (m_bindingGate)
                {
                    m_pending--;
                    if (m_closed && m_pending == 0)
                    {
                        m_drained.TrySetResult(true);
                    }
                }
            }
        }

        /// <summary>
        /// Returns the active binding handle for a state already configured through the fluent API.
        /// </summary>
        internal static bool TryGetHandle(
            ComponentState state,
            [NotNullWhen(true)] out PadimDevice? device)
        {
            device = null;
            return state is PADIMState padim && s_handles.TryGetValue(padim, out device);
        }

        /// <summary>
        /// Releases synchronization resources only after every admitted callback has left.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            try
            {
                await m_stopping.CancelAsync().ConfigureAwait(false);
            }
            finally
            {
                await m_drained.Task.ConfigureAwait(false);
                m_providerGate.Dispose();
                m_stopping.Dispose();
                s_handles.Remove(State);
            }
        }

        /// <summary>
        /// Ensures a binding cannot alter another device's variables or methods.
        /// </summary>
        private void EnsureOwned(BaseInstanceState node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            NodeState? current = node;
            while (current is BaseInstanceState instance)
            {
                if (ReferenceEquals(instance, State))
                {
                    return;
                }
                if (instance is PADIMState)
                {
                    break;
                }
                current = instance.Parent;
            }
            throw new ArgumentException("The node is not a child of this device.", nameof(node));
        }

        /// <summary>
        /// Installs a session-authorized asynchronous method callback.
        /// </summary>
        private PadimDevice BindOperation(
            MethodState method,
            string key,
            IPadimOperationProvider provider,
            IPadimAuthorizer authorizer,
            Func<ArrayOf<Variant>, CancellationToken, ValueTask<StatusCode>> execute)
        {
            EnsureOwned(method);
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (authorizer == null)
            {
                throw new ArgumentNullException(nameof(authorizer));
            }
            method.Executable = true;
            method.UserExecutable = true;
            method.OnCallMethod2Async = async (caller, _, _, inputs, _, ct) =>
            {
                StatusCode status = StatusCodes.BadUserAccessDenied;
                await RunAsync(async token =>
                {
                    if (await authorizer.AuthorizeAsync(caller, method.NodeId, key, token).ConfigureAwait(false))
                    {
                        status = await execute(inputs, token).ConfigureAwait(false);
                    }
                }, ct).ConfigureAwait(false);
                return new ServiceResult(status);
            };
            return this;
        }

        /// <summary>
        /// Rejects missing operation keys before installing callbacks.
        /// </summary>
        private static void ValidateSignalKey(string signalKey)
        {
            if (string.IsNullOrWhiteSpace(signalKey))
            {
                throw new ArgumentException("A stable signal key is required.", nameof(signalKey));
            }
        }

        /// <summary>
        /// Prevents generated default values from being mistaken for acquired process measurements.
        /// </summary>
        private static void PrimeAcquisition(BaseVariableState variable)
        {
            variable.StatusCode = StatusCodes.BadWaitingForInitialData;
            variable.AccessLevel = AccessLevels.CurrentRead;
            variable.UserAccessLevel = AccessLevels.CurrentRead;
        }

        /// <summary>
        /// Prevents generated writable parameter defaults from bypassing the application provider.
        /// </summary>
        internal void DisableUnboundWrites(NodeState node)
        {
            if (node is BaseVariableState variable)
            {
                if ((variable.AccessLevel & AccessLevels.CurrentWrite) != 0)
                {
                    variable.StatusCode = StatusCodes.BadWaitingForInitialData;
                }
                variable.AccessLevel &= unchecked((byte)~AccessLevels.CurrentWrite);
                variable.UserAccessLevel &= unchecked((byte)~AccessLevels.CurrentWrite);
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(Context, children);
            foreach (BaseInstanceState child in children)
            {
                DisableUnboundWrites(child);
            }
        }

        /// <summary>Protects the configured binding list.</summary>
        private readonly System.Threading.Lock m_bindingGate = new();

        /// <summary>The fields explicitly configured for this device.</summary>
        private readonly List<PadimValueBinding> m_bindings = [];

        /// <summary>Serializes access to the device provider.</summary>
        private readonly SemaphoreSlim m_providerGate = new(1, 1);

        /// <summary>Cancels pending callbacks when the owner stops.</summary>
        private readonly CancellationTokenSource m_stopping = new();

        /// <summary>Signals that all admitted provider callbacks have left.</summary>
        private readonly TaskCompletionSource<bool> m_drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The number of admitted callbacks, including queued acquisitions.</summary>
        private int m_pending;

        /// <summary>Prevents admitting work after shutdown begins.</summary>
        private bool m_closed;

        /// <summary>The shared, idempotent disposal task.</summary>
        private Task? m_disposeTask;

        /// <summary>Prevents independent provider pipelines from mutating the same device concurrently.</summary>
        private static readonly ConditionalWeakTable<PADIMState, PadimDevice> s_handles = new();
    }
}
