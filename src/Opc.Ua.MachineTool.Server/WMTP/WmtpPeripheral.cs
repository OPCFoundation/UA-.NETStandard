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
using Opc.Ua.Di;
using Opc.Ua.Machinery.ProcessValues;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Server;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Owns one independent wireless peripheral, its shared ProcessValues bindings and operation lifetime.
    /// A firmware/license success means provider acceptance of bytes, never installation or activation.
    /// </summary>
    public sealed partial class WmtpPeripheral : IAsyncDisposable, IConformanceContributor
    {
        /// <summary>
        /// Creates a runtime around a validated, not yet published generated instance.
        /// </summary>
        internal WmtpPeripheral(
            IMachineryBuildContext context,
            WirelessMachineToolPeripheralState state,
            WmtpDeviceNodes nodes,
            WmtpPeripheralConfiguration configuration,
            TimeProvider timeProvider)
        {
            m_context = context;
            State = state;
            Nodes = nodes;
            m_configuration = configuration;
            TimeProvider = timeProvider;
            m_operations = new WmtpOperationCoordinator(configuration.Authorization);
            MessageContext = new ServiceMessageContext(context.Context.Telemetry, context.Context.EncodeableFactory)
            {
                NamespaceUris = context.Context.NamespaceUris
            };
        }

        /// <summary>
        /// Gets the real generated peripheral; it is never a fabricated MachineToolType child.
        /// </summary>
        public WirelessMachineToolPeripheralState State { get; }

        /// <summary>
        /// Gets the peripheral's identity.
        /// </summary>
        public NodeId NodeId => State.NodeId;

        /// <summary>
        /// Gets the selected nested generated declarations.
        /// </summary>
        public WmtpDeviceNodes Nodes { get; }

        /// <summary>
        /// Gets explicitly selected optional features.
        /// </summary>
        public WmtpFeatures Features => m_configuration.Features;

        /// <summary>
        /// Gets the bounded per-peripheral sensor handles.
        /// </summary>
        public ArrayOf<WmtpMeasurement> Measurements { get; private set; }

        /// <summary>
        /// Gets the selected work-cycle dataset.
        /// </summary>
        public WmtpRecords? WorkRecords { get; private set; }

        /// <summary>
        /// Gets the selected independent service-cycle dataset.
        /// </summary>
        public WmtpRecords? ServiceRecords { get; private set; }

        /// <summary>
        /// Gets the last age-refresh status. A tick that meets an active operation is retried by the next tick
        /// without aborting it; operations that arrive during a tick wait for it instead of being refused.
        /// </summary>
        public StatusCode QualityRefreshStatus { get; private set; } = StatusCodes.Good;

        /// <summary>
        /// Gets the existing namespace owner's context.
        /// </summary>
        internal ISystemContext Context => m_context.Context;

        /// <summary>
        /// Gets the injectable acquisition-age and duration clock.
        /// </summary>
        internal TimeProvider TimeProvider { get; }

        /// <summary>
        /// Gets the existing encodeable factory for real report-size checks.
        /// </summary>
        internal IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Acquires and validates the whole peripheral observation before publishing any of its new values.
        /// </summary>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            _ = await m_operations.RunAsync<bool>(null, WmtpOperation.ReadRecords, async token =>
            {
                WmtpDeviceSnapshot snapshot = await m_configuration.Device!.ReadAsync(token).ConfigureAwait(false);
                WmtpValidation.Device(snapshot, Features);
                var samples = new WmtpMeasurementSample[Measurements.Count];
                for (int index = 0; index < samples.Length; index++)
                {
                    samples[index] = await Measurements[index].AcquireAsync(token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                await InitializeAsync(snapshot, samples.ToArrayOf()).ConfigureAwait(false);
                await State.ClearChangeMasksAsync(Context, true, CancellationToken.None).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Re-evaluates freshness and duration counters without generating or acquiring a new sample.
        /// </summary>
        public async ValueTask RefreshQualityAsync(CancellationToken cancellationToken = default)
        {
            _ = await m_operations.RunAsync<bool>(null, WmtpOperation.ReadRecords, async _ =>
            {
                for (int index = 0; index < Measurements.Count; index++)
                {
                    WmtpMeasurement measurement = Measurements[index];
                    if (measurement.Acquisition != null)
                    {
                        await measurement.ApplyAsync(measurement.Acquisition, false).ConfigureAwait(false);
                    }
                }
                await State.ClearChangeMasksAsync(Context, true, CancellationToken.None).ConfigureAwait(false);
                return true;
            }, cancellationToken, maintenance: true).ConfigureAwait(false);
        }

        /// <summary>
        /// Changes calibration mode only after explicit provider acceptance and authorization.
        /// </summary>
        public async ValueTask SetCalibrationAsync(
            ushort targetMode, WmtpRequest request, CancellationToken cancellationToken = default)
        {
            RequireFeature(WmtpFeatures.Calibration);
            WmtpValidation.Require(targetMode <= 1, "Calibration target mode must be zero or one.");
            _ = await RunAsync(request, WmtpOperation.Calibrate, async token =>
            {
                StatusCode accepted = await m_configuration.Calibration!
                    .SetCalibrationAsync(targetMode == 1, token).ConfigureAwait(false);
                CheckAcceptance(accepted);
                token.ThrowIfCancellationRequested();
                Nodes.CalibrationMode!.Value = targetMode == 1;
                Nodes.CalibrationMode.StatusCode = StatusCodes.Good;
                Nodes.CalibrationMode.Timestamp = TimeProvider.GetUtcNow();
                await Nodes.CalibrationMode.ClearChangeMasksAsync(Context, false, CancellationToken.None)
                    .ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends explicit UTC time and time-zone metadata to the device without changing the server clock.
        /// </summary>
        public async ValueTask SetDeviceTimeAsync(
            DateTimeUtc utcTime,
            TimeZoneDataType timeZone,
            WmtpRequest request,
            CancellationToken cancellationToken = default)
        {
            RequireFeature(WmtpFeatures.DeviceTime);
            if (timeZone == null)
            {
                throw new ArgumentNullException(nameof(timeZone));
            }
            WmtpValidation.Require(!utcTime.IsNull && timeZone.Offset is >= -840 and <= 840,
                "An explicit UTC time and a valid minute offset are required.");
            var zone = (TimeZoneDataType)timeZone.Clone();
            _ = await RunAsync(request, WmtpOperation.SetTime, async token =>
            {
                StatusCode accepted = await m_configuration.DeviceTime!.SetTimeAsync(utcTime, zone, token)
                    .ConfigureAwait(false);
                CheckAcceptance(accepted);
                token.ThrowIfCancellationRequested();
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Transfers bounded bytes after authorization. Success makes no assertion about installation.
        /// The runtime clears its private byte copy after the adapter has completed.
        /// </summary>
        public async ValueTask TransferAsync(
            WmtpTransferKind kind, ByteString payload, WmtpRequest request,
            CancellationToken cancellationToken = default)
        {
            if (kind is not WmtpTransferKind.Firmware and not WmtpTransferKind.License)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            RequireFeature(kind == WmtpTransferKind.Firmware ? WmtpFeatures.Firmware : WmtpFeatures.License);
            if (payload.IsNull || payload.Length == 0 || payload.Length > m_configuration.MaximumTransferBytes)
            {
                throw new ServiceResultException(payload.Length > m_configuration.MaximumTransferBytes ?
                    StatusCodes.BadEncodingLimitsExceeded : StatusCodes.BadInvalidArgument,
                    "Transfer bytes are empty or exceed the configured budget.");
            }
            _ = await RunAsync(request, kind == WmtpTransferKind.Firmware ?
                WmtpOperation.TransferFirmware : WmtpOperation.TransferLicense, async token =>
            {
                byte[] copy = payload.ToArray();
                try
                {
                    StatusCode accepted;
                    try
                    {
                        accepted = await m_configuration.Transfers!
                            .TransferAsync(kind, new ByteString(copy), token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(token);
                    }
                    catch (ServiceResultException error)
                    {
                        throw new ServiceResultException(error.StatusCode, "The transfer provider refused the data.");
                    }
                    catch (Exception)
                    {
                        throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                            "The transfer provider failed.");
                    }
                    CheckAcceptance(accepted);
                    token.ThrowIfCancellationRequested();
                    return true;
                }
                finally
                {
                    Array.Clear(copy, 0, copy.Length);
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Explicitly aborts this peripheral's active operation after its own authorization decision.
        /// Client-local cancellation is deliberately not implemented in terms of this operation.
        /// </summary>
        public ValueTask AbortAsync(WmtpRequest request, CancellationToken cancellationToken = default)
        {
            return m_operations.AbortAsync(ValidateRequest(request), cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            EnsureCanDispose();
            TaskCompletionSource<bool>? completion = null;
            Task disposal;
            lock (m_lifetime)
            {
                if (m_disposal == null)
                {
                    completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_disposal = completion.Task;
                }
                disposal = m_disposal;
            }
            if (completion != null)
            {
                _ = CompleteDisposalAsync(completion);
            }
            await disposal.ConfigureAwait(false);
        }

        /// <summary>
        /// Configures shared ProcessValues bindings and generated methods before publication.
        /// </summary>
        internal void Configure(
            IMachineBuilder<WirelessMachineToolPeripheralState> builder,
            ArrayOf<WmtpMeasurementDefinition> definitions)
        {
            var measurements = new WmtpMeasurement[definitions.Count];
            for (int index = 0; index < measurements.Length; index++)
            {
                measurements[index] = new WmtpMeasurement(
                    this, builder, definitions[index], m_configuration.Measurements!);
            }
            Measurements = measurements.ToArrayOf();
            if (Nodes.Battery != null)
            {
                ProcessValueVariableState signal = Context.CreateInstanceOfProcessValueVariableType(
                    Nodes.Battery, new QualifiedName(Opc.Ua.PADIM.BrowseNames.AnalogSignal,
                        Context.NamespaceUris.GetIndexOrAppend(Opc.Ua.PADIM.Namespaces.PADIM)));
                signal.StatusCode = StatusCodes.BadWaitingForInitialData;
                signal.WrappedValue = Variant.Null;
                signal.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
                signal.ModellingRuleId = Opc.Ua.NodeId.Null;
                Nodes.Battery.CreateOrReplaceAnalogSignal(Context, signal);
                builder.WithProcessValue(Nodes.Battery, value =>
                    value.WithEngineeringUnits(WmtpMeasurement.Percent, new Range { Low = 0, High = 100 })
                        .Bind(out m_battery));
            }
            if ((Features & WmtpFeatures.WorkRecords) != 0)
            {
                State.AddWMTPWorkCycleData(Context);
                WorkRecords = new WmtpRecords(this, m_configuration.WorkRecords!, m_configuration.WorkFeatures,
                    m_configuration.MaximumReportBytes);
                WorkRecords.Bind(Context, State.WMTPWorkCycleData!);
            }
            if ((Features & WmtpFeatures.ServiceRecords) != 0)
            {
                State.AddWMTPServiceCycleData(Context);
                ServiceRecords = new WmtpRecords(this, m_configuration.ServiceRecords!, m_configuration.ServiceFeatures,
                    m_configuration.MaximumReportBytes);
                ServiceRecords.Bind(Context, State.WMTPServiceCycleData!);
            }
            BindConfiguration();
            MakeReadOnly(State);
        }

        /// <summary>
        /// Commits a prevalidated observation, with no cancellation point in the mutation phase.
        /// </summary>
        internal async ValueTask InitializeAsync(
            WmtpDeviceSnapshot device, ArrayOf<WmtpMeasurementSample> samples)
        {
            ApplyDevice(device);
            if (m_battery != null)
            {
                DataValue battery = device.Battery;
                await m_battery.SetValueAsync(in battery, CancellationToken.None).ConfigureAwait(false);
            }
            for (int index = 0; index < Measurements.Count; index++)
            {
                await Measurements[index].ApplyAsync(samples[index], true).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates the namespace owner's nonrecursive shutdown registration.
        /// </summary>
        internal IAsyncDisposable CreateRegistration()
        {
            return new Registration(this);
        }

        /// <summary>
        /// Adopts caller resources only after all fallible initialization and publication have succeeded.
        /// </summary>
        internal void TakeOwnership(ArrayOf<IAsyncDisposable> resources)
        {
            lock (m_lifetime)
            {
                if (m_stopping)
                {
                    throw new ObjectDisposedException(nameof(WmtpPeripheral));
                }
                m_resources = resources;
            }
        }

        /// <summary>
        /// Starts fallible freshness-only maintenance while application resources remain caller-owned.
        /// </summary>
        internal void StartMaintenance()
        {
            if (!Measurements.IsEmpty)
            {
                ITimer timer = TimeProvider.CreateTimer(static state => ((WmtpPeripheral)state!).AgeTick(),
                    this, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                lock (m_lifetime)
                {
                    if (!m_stopping)
                    {
                        m_ageTimer = timer;
                        return;
                    }
                }
                timer.Dispose();
            }
        }

        /// <summary>
        /// Runs an authorized operation against this peripheral's admission and disposal boundary.
        /// </summary>
        /// <typeparam name="T">The provider operation result.</typeparam>
        internal ValueTask<T> RunAsync<T>(
            WmtpRequest request, WmtpOperation operation, Func<CancellationToken, ValueTask<T>> execute,
            CancellationToken cancellationToken)
        {
            return m_operations.RunAsync(ValidateRequest(request), operation, execute, cancellationToken);
        }

        /// <summary>
        /// Extracts wire caller identity from the real session context.
        /// </summary>
        internal WmtpRequest Request(ISystemContext context)
        {
            var session = context as ISessionSystemContext;
            return new WmtpRequest
            {
                PeripheralId = NodeId,
                SessionId = session?.SessionId ?? Opc.Ua.NodeId.Null,
                Identity = session?.UserIdentity
            };
        }

        /// <summary>
        /// Rejects refused or uncertain provider acceptance, without publishing proposed state.
        /// </summary>
        internal static void CheckAcceptance(StatusCode result)
        {
            if (!StatusCode.IsGood(result))
            {
                throw new ServiceResultException(StatusCode.IsBad(result) ? result : StatusCodes.BadRequestNotAllowed);
            }
        }

        /// <summary>
        /// Binds optional configuration methods using the generated signatures.
        /// </summary>
        private void BindConfiguration()
        {
            if (Nodes.SwitchCalibrationMode != null)
            {
                Nodes.SwitchCalibrationMode.OnCallAsync = async (context, _, _, mode, token) =>
                {
                    await SetCalibrationAsync(mode, Request(context), token).ConfigureAwait(false);
                    return new SwitchCalibrationModeMethodStateResult { ServiceResult = ServiceResult.Good };
                };
            }
            if (Nodes.SetDeviceTime != null)
            {
                Nodes.SetDeviceTime.OnCallAsync = async (context, _, _, time, zone, token) =>
                {
                    await SetDeviceTimeAsync(time, zone, Request(context), token).ConfigureAwait(false);
                    return new SetDeviceTimeMethodStateResult { ServiceResult = ServiceResult.Good };
                };
            }
            if (Nodes.UpdateFirmware != null)
            {
                Nodes.UpdateFirmware.OnCallAsync = async (context, _, _, bytes, token) =>
                {
                    await TransferAsync(WmtpTransferKind.Firmware, bytes.ToByteString(), Request(context), token)
                        .ConfigureAwait(false);
                    return new UpdateFirmwareMethodStateResult { ServiceResult = ServiceResult.Good };
                };
            }
            if (Nodes.SendLicenseData != null)
            {
                Nodes.SendLicenseData.OnCallAsync = async (context, _, _, bytes, token) =>
                {
                    await TransferAsync(WmtpTransferKind.License, bytes.ToByteString(), Request(context), token)
                        .ConfigureAwait(false);
                    return new SendLicenseDataMethodStateResult { ServiceResult = ServiceResult.Good };
                };
            }
        }

        /// <summary>
        /// Applies device-sourced metadata and the exact standard lifetime subproperties.
        /// </summary>
        private void ApplyDevice(WmtpDeviceSnapshot device)
        {
            if (Nodes.CalibrationMode != null)
            {
                Nodes.CalibrationMode.Value = device.CalibrationMode!.Value;
                Stamp(Nodes.CalibrationMode, device.Observation);
            }
            if (Nodes.LicenseInformation != null)
            {
                Nodes.LicenseInformation.Value = device.LicenseInformation!;
                Stamp(Nodes.LicenseInformation, device.Observation);
                ApplyLifetime(Nodes.LicenseValidity!, device.LicenseValidity!, device.Observation);
            }
            if (Nodes.LifeCycleStatus != null)
            {
                Nodes.LifeCycleStatus.Value = device.LifeCycleStatus!.Value;
                Nodes.LifeCycleStatus.ValueAsText!.Value =
                    Nodes.LifeCycleStatus.EnumValues!.Value[(int)device.LifeCycleStatus.Value].DisplayName;
                Nodes.NextServiceDate!.Value = device.NextServiceDate;
                Nodes.LastService!.Value = device.LastService;
                Stamp(Nodes.LifeCycleStatus, device.Observation);
                Stamp(Nodes.NextServiceDate, device.Observation);
                Stamp(Nodes.LastService, device.Observation);
            }
            if (Nodes.RemainingLifetime != null)
            {
                ApplyLifetime(Nodes.RemainingLifetime, device.RemainingLifetime!, device.Observation);
                ApplyLifetime(Nodes.RemainingCycles!, device.RemainingCycles!, device.Observation);
                ApplyLifetime(Nodes.NextService!, device.NextService!, device.Observation);
                State.OperationCounters!.OperationDuration!.Value = device.OperationHours!.Value;
                Nodes.OperationSinceService!.Value = device.OperationSinceService!.Value;
                Stamp(State.OperationCounters.OperationDuration, device.Observation);
                Stamp(Nodes.OperationSinceService, device.Observation);
            }
        }

        /// <summary>
        /// Publishes a device-reported lifetime, rather than estimating equipment use from server uptime.
        /// </summary>
        private static void ApplyLifetime(LifetimeVariableState node, WmtpLifetime lifetime, in DataValue observation)
        {
            node.WrappedValue = lifetime.Remaining;
            node.StartValue!.WrappedValue = lifetime.Start;
            node.LimitValue!.WrappedValue = lifetime.Limit;
            node.EngineeringUnits!.Value = (EUInformation)lifetime.EngineeringUnits.Clone();
            Stamp(node, observation);
        }

        /// <summary>
        /// Retains explicit equipment acquisition quality/time.
        /// </summary>
        private static void Stamp(BaseVariableState node, in DataValue observation)
        {
            node.StatusCode = observation.StatusCode;
            node.Timestamp = observation.SourceTimestamp;
        }

        /// <summary>
        /// Requires the actual configured instance capability.
        /// </summary>
        private void RequireFeature(WmtpFeatures feature)
        {
            if ((Features & feature) == 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
        }

        /// <summary>
        /// Rejects accidental cross-peripheral direct calls and normalizes explicit local requests.
        /// </summary>
        private WmtpRequest ValidateRequest(WmtpRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (!request.PeripheralId.IsNull && request.PeripheralId != NodeId)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                    "The request targets another peripheral.");
            }
            return request with { PeripheralId = NodeId };
        }

        /// <summary>
        /// Prevents generic Write from bypassing method validation/authorization on observed or configured values.
        /// </summary>
        private void MakeReadOnly(NodeState node)
        {
            if (node is BaseVariableState variable)
            {
                variable.AccessLevel = AccessLevels.CurrentRead;
                variable.UserAccessLevel = AccessLevels.CurrentRead;
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(Context, children);
            foreach (BaseInstanceState child in children)
            {
                MakeReadOnly(child);
            }
        }

        /// <summary>
        /// Starts at most one age-only refresh and never holds a gate while invoking callbacks.
        /// </summary>
        private void AgeTick()
        {
            TaskCompletionSource<bool> completion;
            lock (m_lifetime)
            {
                if (m_stopping || !m_ageTask.IsCompleted)
                {
                    return;
                }
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_ageTask = completion.Task;
            }
            _ = CompleteAgeTickAsync(completion);
        }

        /// <summary>
        /// Captures freshness maintenance failures for diagnostics without leaking sample or transfer payloads.
        /// </summary>
        private async Task CompleteAgeTickAsync(TaskCompletionSource<bool> completion)
        {
            try
            {
                await RefreshQualityAsync().ConfigureAwait(false);
                QualityRefreshStatus = StatusCodes.Good;
            }
            catch (ServiceResultException error)
            {
                QualityRefreshStatus = error.StatusCode;
            }
            catch (Exception error) when (error is ObjectDisposedException or OperationCanceledException)
            {
                QualityRefreshStatus = StatusCodes.BadShutdown;
            }
            catch (Exception)
            {
                QualityRefreshStatus = StatusCodes.BadUnexpectedError;
            }
            finally
            {
                completion.TrySetResult(true);
            }
        }

        /// <summary>
        /// Cancels and joins provider callbacks, timer work and explicitly owned resources.
        /// </summary>
        private ValueTask StopAsync()
        {
            EnsureCanDispose();
            TaskCompletionSource<bool> completion;
            lock (m_lifetime)
            {
                if (m_stopTask != null)
                {
                    return new ValueTask(m_stopTask);
                }
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_stopTask = completion.Task;
                m_stopping = true;
            }
            _ = CompleteStopAsync(completion);
            return new ValueTask(completion.Task);
        }

        /// <summary>
        /// Completes a single shared stop task, draining every resource even after another cleanup fails.
        /// </summary>
        private async Task CompleteStopAsync(TaskCompletionSource<bool> completion)
        {
            WmtpPeripheral? previous = s_disposing.Value;
            s_disposing.Value = this;
            Task ageTask;
            ArrayOf<IAsyncDisposable> resources;
            ITimer? timer;
            lock (m_lifetime)
            {
                ageTask = m_ageTask;
                resources = m_resources;
                m_resources = default;
                timer = m_ageTimer;
                m_ageTimer = null;
            }
            var errors = new List<Exception>();
            try
            {
                await PublishConformanceAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            try
            {
                if (timer != null)
                {
                    await timer.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            try
            {
                await m_operations.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            await ageTask.ConfigureAwait(false);
            for (int index = resources.Count - 1; index >= 0; index--)
            {
                try
                {
                    await resources[index].DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }
            s_disposing.Value = previous;
            if (errors.Count == 0)
            {
                completion.TrySetResult(true);
            }
            else
            {
                completion.TrySetException(new AggregateException("Peripheral resource cleanup failed.", errors));
            }
        }

        /// <summary>
        /// Completes node removal once and preserves cleanup errors without leaving a published dead peripheral.
        /// </summary>
        private async Task CompleteDisposalAsync(TaskCompletionSource<bool> completion)
        {
            var errors = new List<Exception>();
            try
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            try
            {
                if (ReferenceEquals(m_context.Manager.FindPredefinedNode(NodeId), State))
                {
                    m_context.MachinesFolder.RemoveChild(State);
                    await m_context.Manager.DeleteNodeAsync(
                        m_context.Manager.SystemContext, NodeId, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            if (errors.Count == 0)
            {
                completion.TrySetResult(true);
            }
            else
            {
                completion.TrySetException(new AggregateException("Peripheral disposal failed.", errors));
            }
        }

        /// <summary>
        /// Rejects a callback's attempt to await its own operation or owned-resource cleanup.
        /// </summary>
        private void EnsureCanDispose()
        {
            if (m_operations.IsInCallback || ReferenceEquals(s_disposing.Value, this))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "A callback cannot await disposal of its own peripheral.");
            }
        }

        /// <summary>
        /// Stops behavior when the namespace owner removes the root, without recursively removing it.
        /// </summary>
        private sealed class Registration : IAsyncDisposable
        {
            /// <summary>
            /// Creates the registered lifetime resource.
            /// </summary>
            internal Registration(WmtpPeripheral peripheral)
            {
                m_peripheral = peripheral;
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                return m_peripheral.StopAsync();
            }

            /// <summary>The owning runtime.</summary>
            private readonly WmtpPeripheral m_peripheral;
        }

        /// <summary>The namespace owner.</summary>
        private readonly IMachineryBuildContext m_context;

        /// <summary>The explicit provider and feature selection.</summary>
        private readonly WmtpPeripheralConfiguration m_configuration;

        /// <summary>The peripheral-scoped caller admission/cancellation coordinator.</summary>
        private readonly WmtpOperationCoordinator m_operations;

        /// <summary>The short timer/resource lifetime gate.</summary>
        private readonly Lock m_lifetime = new();

        /// <summary>The owned-resource callback reentrancy marker.</summary>
        private static readonly AsyncLocal<WmtpPeripheral?> s_disposing = new();

        /// <summary>The optional shared battery ProcessValue.</summary>
        private IProcessValueHandle? m_battery;

        /// <summary>The explicitly transferred resources.</summary>
        private ArrayOf<IAsyncDisposable> m_resources;

        /// <summary>The injectable-clock freshness timer.</summary>
        private ITimer? m_ageTimer;

        /// <summary>The active asynchronous timer callback.</summary>
        private Task m_ageTask = Task.CompletedTask;

        /// <summary>The shared asynchronous stop completion.</summary>
        private Task? m_stopTask;

        /// <summary>The shared asynchronous explicit-disposal completion.</summary>
        private Task? m_disposal;

        /// <summary>Whether timer admission has closed.</summary>
        private bool m_stopping;
    }
}
