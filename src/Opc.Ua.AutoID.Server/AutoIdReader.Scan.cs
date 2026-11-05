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

namespace Opc.Ua.AutoID.Server
{
    public sealed partial class AutoIdReader
    {
        /// <summary>
        /// The outcome of a <c>Scan</c> call: the service result, the results and the status.
        /// </summary>
        private readonly struct ScanCallOutcome
        {
            public ScanCallOutcome(
                ServiceResult serviceResult,
                ArrayOf<ScanResult> results,
                AutoIdOperationStatusEnumeration status)
            {
                ServiceResult = serviceResult;
                Results = results;
                Status = status;
            }

            public ServiceResult ServiceResult { get; }

            public ArrayOf<ScanResult> Results { get; }

            public AutoIdOperationStatusEnumeration Status { get; }
        }

        /// <summary>
        /// Executes <c>Scan</c> (OPC 30010 6.1.3.4) for any reader kind.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<ScanCallOutcome> ScanCoreAsync(
            ISystemContext context,
            ScanSettings settings,
            CancellationToken cancellationToken)
        {
            IAutoIdScanProvider provider = m_scanProvider ??
                throw ServiceResultException.Create(
                    StatusCodes.BadMethodInvalid,
                    "The device does not support the Scan method.");
            ServiceResult invalid = ValidateSettings(settings, synchronous: true);
            if (ServiceResult.IsBad(invalid))
            {
                return new ScanCallOutcome(invalid, default, default);
            }
            if (!m_operations.TryEnter())
            {
                return new ScanCallOutcome(
                    BadResult(StatusCodes.BadInvalidState, "The reader is shut down."), default, default);
            }
            long generation = 0;
            try
            {
                ScanCallOutcome? refused = null;
                lock (m_lock)
                {
                    if (m_faulted)
                    {
                        refused = new ScanCallOutcome(
                            ServiceResult.Good, default, AutoIdOperationStatusEnumeration.DEVICE_NOT_READY);
                    }
                    else if (m_activity != ScanActivity.Idle || m_parameterWrite)
                    {
                        refused = new ScanCallOutcome(
                            BadResult(StatusCodes.BadInvalidState, "There is already a scan active."),
                            default,
                            default);
                    }
                    else
                    {
                        m_activity = ScanActivity.Synchronous;
                        m_scanOwner = NodeId.Null;
                        generation = ++m_generation;
                        UpdateStatusLocked();
                    }
                }
                if (refused is { } refusal)
                {
                    return refusal;
                }
                try
                {
                    await PublishStatusAsync().ConfigureAwait(false);
                    AutoIdScanOutcome outcome = await RunScanAsync(provider, settings, cancellationToken)
                        .ConfigureAwait(false);
                    ArrayOf<ScanResult> results = ValidateResults(outcome.Results);
                    if (results.Count > 0)
                    {
                        await PublishLastScanAsync(results[0], CancellationToken.None).ConfigureAwait(false);
                        await TryRaiseAccessEventAsync(
                            ClientOf(context), "Scan", AccessResultsOf(results)).ConfigureAwait(false);
                    }
                    return new ScanCallOutcome(ServiceResult.Good, results, outcome.Status);
                }
                finally
                {
                    lock (m_lock)
                    {
                        if (m_generation == generation && m_activity == ScanActivity.Synchronous)
                        {
                            m_activity = ScanActivity.Idle;
                            UpdateStatusLocked();
                        }
                    }
                    await PublishStatusAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_operations.Exit();
            }
        }

        /// <summary>
        /// Runs the provider scan under the caller's token, the shutdown token and the maximum duration.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<AutoIdScanOutcome> RunScanAsync(
            IAutoIdScanProvider provider,
            ScanSettings settings,
            CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                m_stopping.Token);
            linked.CancelAfter(Runtime.Options.MaximumScanDuration);
            try
            {
                return await provider.ScanAsync(settings, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                linked.IsCancellationRequested)
            {
                throw ServiceResultException.Create(
                    m_stopping.IsCancellationRequested ? StatusCodes.BadServerHalted : StatusCodes.BadTimeout,
                    m_stopping.IsCancellationRequested
                        ? "The reader was shut down during the scan."
                        : "The scan did not finish within the maximum scan duration.");
            }
        }

        /// <summary>
        /// Executes <c>ScanStart</c> (OPC 30010 6.1.3.5).
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<(ServiceResult Service, AutoIdOperationStatusEnumeration Status)> ScanStartCoreAsync(
            ISystemContext context,
            ScanSettings settings,
            CancellationToken cancellationToken,
            bool variableControl = false)
        {
            IAutoIdAsyncScanProvider provider = m_asyncProvider ??
                throw ServiceResultException.Create(
                    StatusCodes.BadMethodInvalid,
                    "The device does not support the ScanStart method.");
            ServiceResult invalid = ValidateSettings(settings, synchronous: false);
            if (ServiceResult.IsBad(invalid))
            {
                return (invalid, default);
            }
            NodeId owner = !variableControl && context is ISessionSystemContext { SessionId: NodeId sessionId }
                ? sessionId
                : NodeId.Null;
            string client = ClientOf(context);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                m_stopping.Token);
            try
            {
                await m_controlGate.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (m_stopping.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                return (BadResult(StatusCodes.BadInvalidState, "The reader is shut down."), default);
            }
            try
            {
                if (!m_operations.TryEnter())
                {
                    return (BadResult(StatusCodes.BadInvalidState, "The reader is shut down."), default);
                }
                try
                {
                    long generation = 0;
                    (ServiceResult Service, AutoIdOperationStatusEnumeration Status)? refused = null;
                    lock (m_lock)
                    {
                        if (variableControl && m_activity == ScanActivity.Asynchronous)
                        {
                            return (ServiceResult.Good, AutoIdOperationStatusEnumeration.SUCCESS);
                        }
                        if (variableControl)
                        {
                            settings = new ScanSettings
                            {
                                Duration = m_parameters!.Duration,
                                Cycles = m_parameters.Cycles,
                                DataAvailable = m_parameters.DataAvailable
                            };
                        }
                        if (m_faulted)
                        {
                            refused = (ServiceResult.Good, AutoIdOperationStatusEnumeration.DEVICE_NOT_READY);
                        }
                        else if (m_activity != ScanActivity.Idle)
                        {
                            refused = (BadResult(StatusCodes.BadInvalidState, "There is already a scan active."), default);
                        }
                        else
                        {
                            m_activity = ScanActivity.Asynchronous;
                            m_scanOwner = owner;
                            m_scanClient = client;
                            generation = ++m_generation;
                            UpdateStatusLocked();
                        }
                    }
                    if (refused is { } refusal)
                    {
                        return refusal;
                    }
                    await PublishStatusAsync().ConfigureAwait(false);
                    AutoIdOperationStatusEnumeration status;
                    try
                    {
                        status = await provider.StartScanAsync(settings, linked.Token).ConfigureAwait(false);
                    }
                    catch
                    {
                        await EndAsynchronousScanAsync(generation).ConfigureAwait(false);
                        throw;
                    }
                    if (status != AutoIdOperationStatusEnumeration.SUCCESS)
                    {
                        await EndAsynchronousScanAsync(generation).ConfigureAwait(false);
                    }
                    return (ServiceResult.Good, status);
                }
                finally
                {
                    m_operations.Exit();
                }
            }
            finally
            {
                m_controlGate.Release();
            }
        }

        /// <summary>
        /// Executes <c>ScanStop</c> (OPC 30010 6.1.3.6).
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<ServiceResult> ScanStopCoreAsync(CancellationToken cancellationToken)
        {
            if (m_asyncProvider == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadMethodInvalid,
                    "The device does not support the ScanStop method.");
            }
            bool stopped = await StopAsynchronousScanAsync(NodeId.Null, cancellationToken).ConfigureAwait(false);
            return stopped
                ? ServiceResult.Good
                : BadResult(StatusCodes.BadInvalidState, "There is no scan active.");
        }

        /// <summary>
        /// Stops the active asynchronous scan, if there is one and, when
        /// <paramref name="onlyOwner"/> is set, it was started by that session.
        /// </summary>
        /// <returns><see langword="true"/> when a scan was stopped.</returns>
        private async ValueTask<bool> StopAsynchronousScanAsync(NodeId onlyOwner, CancellationToken cancellationToken)
        {
            IAutoIdAsyncScanProvider? provider = m_asyncProvider;
            if (provider == null)
            {
                return false;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                m_stopping.Token);
            try
            {
                await m_controlGate.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (m_stopping.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            try
            {
                if (!m_operations.TryEnter())
                {
                    return false;
                }
                try
                {
                    long generation;
                    lock (m_lock)
                    {
                        if (m_activity != ScanActivity.Asynchronous ||
                            (!onlyOwner.IsNull && m_scanOwner != onlyOwner))
                        {
                            return false;
                        }
                        generation = m_generation;
                    }
                    // A rejected or cancelled stop does not establish that the hardware stopped.
                    await provider.StopScanAsync(linked.Token).ConfigureAwait(false);
                    await EndAsynchronousScanAsync(generation).ConfigureAwait(false);
                    return true;
                }
                finally
                {
                    m_operations.Exit();
                }
            }
            finally
            {
                m_controlGate.Release();
            }
        }

        /// <summary>
        /// Ends the asynchronous scan of <paramref name="generation"/> if it is still the active one.
        /// </summary>
        private async ValueTask<bool> EndAsynchronousScanAsync(long generation)
        {
            bool ended = false;
            lock (m_lock)
            {
                if (m_generation == generation && m_activity == ScanActivity.Asynchronous)
                {
                    m_activity = ScanActivity.Idle;
                    m_scanOwner = NodeId.Null;
                    m_generation++;
                    ended = true;
                    UpdateStatusLocked();
                }
            }
            if (ended)
            {
                await PublishStatusAsync().ConfigureAwait(false);
            }
            return ended;
        }

        /// <summary>
        /// Stops the asynchronous scan that a closing session started (OPC 30010 6.1.3.5).
        /// </summary>
        internal async ValueTask OnSessionClosingAsync(NodeId sessionId, CancellationToken cancellationToken)
        {
            if (sessionId.IsNull || Volatile.Read(ref m_disposed))
            {
                return;
            }
            if (await StopAsynchronousScanAsync(sessionId, cancellationToken).ConfigureAwait(false))
            {
                m_logger.ScanStoppedBySessionClose(Name.Name);
            }
        }

        /// <inheritdoc/>
        public ValueTask ReportScanAsync(
            ArrayOf<ScanResult> results,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            ArrayOf<ScanResult> validated = ValidateResults(results);
            if (validated.Count == 0)
            {
                throw new ArgumentException("A scan report needs at least one result.", nameof(results));
            }
            if (!m_operations.TryEnter())
            {
                throw new ObjectDisposedException(nameof(AutoIdReader));
            }
            return ReportScanCoreAsync(validated, cancellationToken);
        }

        /// <summary>
        /// Publishes an acquired scan and keeps the reader operation active through all awaited sinks.
        /// </summary>
        private async ValueTask ReportScanCoreAsync(
            ArrayOf<ScanResult> validated, CancellationToken cancellationToken)
        {
            try
            {
                // The results of an asynchronous scan were requested by the client that started it;
                // anything else the device acquired on its own.
                string client;
                lock (m_lock)
                {
                    client = m_activity == ScanActivity.Asynchronous ? m_scanClient : DeviceClient;
                }
                await PublishLastScanAsync(validated[0], cancellationToken).ConfigureAwait(false);
                await RaiseScanEventAsync(validated, cancellationToken).ConfigureAwait(false);
                await RaiseAccessEventAsync(
                    client,
                    "Scan",
                    AccessResultsOf(validated),
                    tolerateDiagnosticCopyFailure: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_operations.Exit();
            }
        }

        /// <inheritdoc/>
        public ValueTask CompleteScanAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_operations.TryEnter())
            {
                throw new ObjectDisposedException(nameof(AutoIdReader));
            }
            bool ended = false;
            try
            {
                lock (m_lock)
                {
                    if (m_activity == ScanActivity.Asynchronous)
                    {
                        m_activity = ScanActivity.Idle;
                        m_scanOwner = NodeId.Null;
                        m_generation++;
                        ended = true;
                        UpdateStatusLocked();
                    }
                }
            }
            catch
            {
                m_operations.Exit();
                throw;
            }
            if (ended)
            {
                return PublishCompletionStatusAsync();
            }
            m_operations.Exit();
            return default;
        }

        /// <summary>
        /// Publishes completion status and keeps the reader operation active through all awaited sinks.
        /// </summary>
        private async ValueTask PublishCompletionStatusAsync()
        {
            try
            {
                await PublishStatusAsync().ConfigureAwait(false);
            }
            finally
            {
                m_operations.Exit();
            }
        }

        /// <summary>
        /// Validates the scan settings of a call. A synchronous scan must not be infinite.
        /// </summary>
        private ServiceResult ValidateSettings(ScanSettings? settings, bool synchronous)
        {
            if (settings is null)
            {
                return BadResult(StatusCodes.BadInvalidArgument, "The scan settings are required.");
            }
            if (double.IsNaN(settings.Duration) || double.IsInfinity(settings.Duration) || settings.Duration < 0)
            {
                return BadResult(StatusCodes.BadInvalidArgument, "The scan duration must be a finite number of milliseconds, 0 for infinite.");
            }
            if (settings.Cycles < 0)
            {
                return BadResult(StatusCodes.BadInvalidArgument, "The scan cycles must not be negative, 0 is infinite.");
            }
            if ((settings.EncodingMask & (uint)ScanSettingsFields.LocationType) != 0 &&
                !AutoIdCompat.IsDefined(settings.LocationType))
            {
                return BadResult(StatusCodes.BadInvalidArgument, "The scan location type is not defined.");
            }
            if (synchronous)
            {
                if (settings.Duration == 0 && settings.Cycles == 0 && !settings.DataAvailable)
                {
                    return BadResult(StatusCodes.BadInvalidArgument, "A synchronous scan needs a finite termination condition.");
                }
                if (settings.Duration > Runtime.Options.MaximumScanDuration.TotalMilliseconds)
                {
                    return BadResult(StatusCodes.BadInvalidArgument, "The scan duration exceeds the maximum scan duration of the server.");
                }
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Verifies the provider results: bounded in number and of the type of the reader kind.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private ArrayOf<ScanResult> ValidateResults(ArrayOf<ScanResult> results)
        {
            if (results.Count > Runtime.Options.MaximumScanResults)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadResponseTooLarge,
                    "The scan returned {0} results, more than the limit of {1}.",
                    results.Count,
                    Runtime.Options.MaximumScanResults);
            }
            Type expected = ResultTypeOf(Kind);
            for (int index = 0; index < results.Count; index++)
            {
                ScanResult? result = results[index];
                if (result is null || !expected.IsInstanceOfType(result))
                {
                    m_logger.ProviderResultTypeMismatch(Name.Name, result?.GetType().Name, expected.Name);
                    throw ServiceResultException.Create(
                        StatusCodes.BadInternalError,
                        "A scan result of the '{0}' reader is not a {1}.",
                        Name.Name ?? string.Empty,
                        expected.Name);
                }
            }
            return results;
        }

        private static Type ResultTypeOf(AutoIdReaderKind kind)
        {
            return kind switch
            {
                AutoIdReaderKind.Rfid => typeof(RfidScanResult),
                AutoIdReaderKind.Ocr => typeof(OcrScanResult),
                AutoIdReaderKind.Optical => typeof(OpticalScanResult),
                AutoIdReaderKind.OpticalVerifier => typeof(OpticalVerifierScanResult),
                AutoIdReaderKind.Rtls => typeof(RtlsLocationResult),
                _ => typeof(ScanResult)
            };
        }

        /// <summary>
        /// Copies the typed results into the array a method output or event property needs.
        /// </summary>
        /// <typeparam name="T">The result type of the reader kind.</typeparam>
        private static ArrayOf<T> Cast<T>(ArrayOf<ScanResult> results)
            where T : ScanResult
        {
            var typed = new T[results.Count];
            for (int index = 0; index < typed.Length; index++)
            {
                typed[index] = (T)results[index];
            }
            return typed.ToArrayOf();
        }

        /// <summary>
        /// Publishes the first result as <c>LastScanData</c> (OPC 30010 6.1.3.9 and 6.1.3.10).
        /// </summary>
        private async ValueTask PublishLastScanAsync(ScanResult result, CancellationToken cancellationToken)
        {
            DateTimeUtc timestamp = result.Timestamp.IsNull ? DateTimeUtc.Now : result.Timestamp;
            lock (m_lock)
            {
                // The value and the timestamp of one scan are written together so a concurrent
                // scan cannot interleave them; LastScanTimestamp equals the SourceTimestamp
                // of LastScanData.
                if (Device.LastScanData != null)
                {
                    Device.LastScanData.Value = VariantOf(result.ScanData);
                    Device.LastScanData.StatusCode = StatusCodes.Good;
                    Device.LastScanData.Timestamp = timestamp;
                }
                if (Device.LastScanTimestamp != null)
                {
                    Device.LastScanTimestamp.Value = timestamp;
                    Device.LastScanTimestamp.StatusCode = StatusCodes.Good;
                    Device.LastScanTimestamp.Timestamp = timestamp;
                }
                if (Device is RfidReaderDeviceState rfid && result is RfidScanResult rfidResult)
                {
                    RfidSighting? sighting = rfidResult.Sighting.Count > 0 ? rfidResult.Sighting[0] : null;
                    if (rfid.LastScanAntenna != null)
                    {
                        rfid.LastScanAntenna.Value = sighting?.Antenna ?? 0;
                        rfid.LastScanAntenna.StatusCode = sighting == null ? StatusCodes.BadNoData : StatusCodes.Good;
                        rfid.LastScanAntenna.Timestamp = timestamp;
                    }
                    if (rfid.LastScanRSSI != null)
                    {
                        rfid.LastScanRSSI.Value = sighting?.Strength ?? 0;
                        rfid.LastScanRSSI.StatusCode = sighting == null ? StatusCodes.BadNoData : StatusCodes.Good;
                        rfid.LastScanRSSI.Timestamp = timestamp;
                    }
                }
            }
            ISystemContext context = Runtime.SystemContext;
            if (Device.LastScanData != null)
            {
                await Device.LastScanData.ClearChangeMasksAsync(context, false, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (Device.LastScanTimestamp != null)
            {
                await Device.LastScanTimestamp.ClearChangeMasksAsync(context, false, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (Device is RfidReaderDeviceState rfidDevice)
            {
                if (rfidDevice.LastScanAntenna != null)
                {
                    await rfidDevice.LastScanAntenna.ClearChangeMasksAsync(context, false, cancellationToken)
                        .ConfigureAwait(false);
                }
                if (rfidDevice.LastScanRSSI != null)
                {
                    await rfidDevice.LastScanRSSI.ClearChangeMasksAsync(context, false, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Converts the identifier of a scan to the variant OPC 30010 6.1.3.9 allows
        /// (one arm of the <c>ScanData</c> union).
        /// </summary>
        internal static Variant VariantOf(ScanData? data)
        {
            if (data is null)
            {
                return Variant.Null;
            }
            return data.SwitchField switch
            {
                ScanDataFields.ByteString => Variant.From(data.ByteString),
                ScanDataFields.String => Variant.From(data.String ?? string.Empty),
                ScanDataFields.Epc => Variant.FromStructure(data.Epc),
                ScanDataFields.Custom => data.Custom,
                _ => Variant.Null
            };
        }
    }
}
