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
using Microsoft.Extensions.Logging;
using Opc.Ua.AutoID.Server.Builders;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// One OPC 30010 reader device that the server hosts, and the sink through which its
    /// provider reports results.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Created by <see cref="IAutoIdReaderBuilder.BuildAsync"/>. The reader owns the device
    /// state in the address space, executes the OPC 30010 methods on behalf of its
    /// <see cref="IAutoIdReaderProvider"/> and raises the scan, access, presence and log entry
    /// events. It is disposed with the runtime that created it; disposing it earlier stops its
    /// scans, detaches the provider and rejects further calls and reports, but leaves the
    /// device nodes in the address space.
    /// </para>
    /// <para>
    /// Concurrency: a reader runs at most one scan at a time. A <c>Scan</c> or <c>ScanStart</c>
    /// call that finds another scan active fails with <c>Bad_InvalidState</c> (OPC 30010 6.1.3.4
    /// and 6.1.3.5). State transitions happen under a lock; provider calls, node updates that
    /// notify subscribers and events are made after the lock is released.
    /// </para>
    /// </remarks>
    public sealed partial class AutoIdReader : IAutoIdReaderSink, IAsyncDisposable
    {
        internal AutoIdReader(
            AutoIdRuntime runtime,
            AutoIdDeviceState device,
            IAutoIdReaderProvider provider,
            bool ownsProvider,
            bool hasDeviceLocation)
        {
            Runtime = runtime;
            Device = device;
            Provider = provider;
            m_ownsProvider = ownsProvider;
            m_hasDeviceLocation = hasDeviceLocation;
            m_logger = runtime.SystemContext.Telemetry.CreateLogger<AutoIdReader>();
            m_scanProvider = provider as IAutoIdScanProvider;
            m_asyncProvider = provider as IAutoIdAsyncScanProvider;
            m_rfidProvider = provider as IAutoIdRfidProvider;
            m_rtlsProvider = provider as IAutoIdRtlsProvider;
        }

        /// <summary>
        /// Gets the runtime that created the reader.
        /// </summary>
        public AutoIdRuntime Runtime { get; }

        /// <summary>
        /// Gets the device state in the address space.
        /// </summary>
        public AutoIdDeviceState Device { get; }

        /// <summary>
        /// Gets the NodeId of the device.
        /// </summary>
        public NodeId NodeId => Device.NodeId;

        /// <summary>
        /// Gets the browse name of the device.
        /// </summary>
        public QualifiedName Name => Device.BrowseName;

        /// <summary>
        /// Gets the reader kind.
        /// </summary>
        public AutoIdReaderKind Kind => Provider.Kind;

        /// <summary>
        /// Gets the hardware provider.
        /// </summary>
        public IAutoIdReaderProvider Provider { get; }

        internal bool SupportsScan => m_scanProvider != null;

        internal bool SupportsAsyncScan => m_asyncProvider != null;

        /// <summary>
        /// Gets the current <c>DeviceStatus</c>.
        /// </summary>
        public DeviceStatusEnumeration Status
        {
            get
            {
                lock (m_lock)
                {
                    return m_status;
                }
            }
        }

        /// <summary>
        /// Gets whether an asynchronous scan started by <c>ScanStart</c> is active.
        /// </summary>
        public bool IsAsyncScanActive
        {
            get
            {
                lock (m_lock)
                {
                    return m_activity == ScanActivity.Asynchronous;
                }
            }
        }

        /// <summary>
        /// Marks the device as faulty or repaired.
        /// </summary>
        /// <remarks>
        /// A faulty device reports <c>Error_1</c> and fails new <c>Scan</c> and <c>ScanStart</c>
        /// calls with the status <c>DEVICE_NOT_READY_17</c>; an asynchronous scan that is active
        /// is stopped. Repairing it returns <c>Idle_0</c>.
        /// </remarks>
        /// <param name="faulted">Whether the device has an error condition.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask SetFaultedAsync(bool faulted, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            bool stopAsync = false;
            lock (m_lock)
            {
                if (m_faulted == faulted)
                {
                    return;
                }
                m_faulted = faulted;
                if (faulted && m_activity == ScanActivity.Asynchronous)
                {
                    stopAsync = true;
                }
                else
                {
                    UpdateStatusLocked();
                }
            }
            PublishStatus();
            if (stopAsync)
            {
                await StopAsynchronousScanAsync(NodeId.Null, cancellationToken).ConfigureAwait(false);
                lock (m_lock)
                {
                    UpdateStatusLocked();
                }
                PublishStatus();
            }
        }

        /// <summary>
        /// Updates the <c>DeviceLocation</c> variable of a mobile reader.
        /// </summary>
        /// <param name="location">The new location.</param>
        /// <exception cref="InvalidOperationException">
        /// The reader was built without <see cref="IAutoIdReaderBuilder.WithDeviceLocation"/>.
        /// </exception>
        /// <exception cref="ArgumentNullException"></exception>
        public void SetDeviceLocation(Location location)
        {
            if (location is null)
            {
                throw new ArgumentNullException(nameof(location));
            }
            ThrowIfDisposed();
            if (!m_hasDeviceLocation || Device.DeviceLocation == null)
            {
                throw new InvalidOperationException(
                    "The reader publishes no DeviceLocation; configure it with WithDeviceLocation.");
            }
            lock (m_lock)
            {
                Device.DeviceLocation.Value = location;
                Device.DeviceLocation.Timestamp = DateTimeUtc.Now;
            }
            Device.DeviceLocation.ClearChangeMasks(Runtime.SystemContext, false);
        }

        /// <summary>
        /// Stops the scans of the reader, detaches the provider and, if the reader owns it,
        /// disposes the provider.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            bool first;
            lock (m_lock)
            {
                first = !m_disposed;
                m_disposed = true;
            }
            if (first)
            {
                // Started outside the lock: cancelling the shutdown token can run continuations of
                // other code, which must not run under the state lock.
                _ = CompleteDisposeAsync();
            }
            return new ValueTask(m_disposeCompletion.Task);
        }

        private async Task CompleteDisposeAsync()
        {
            try
            {
                await DisposeCoreAsync().ConfigureAwait(false);
                m_disposeCompletion.SetResult(true);
            }
            catch (Exception ex)
            {
                m_disposeCompletion.SetException(ex);
            }
        }

        private async Task DisposeCoreAsync()
        {
            // Cancels queued control operations and running scans and calls, then waits for them.
            await m_stopping.CancelAsync().ConfigureAwait(false);
            Task drained = m_operations.CloseAsync();
            await m_controlGate.WaitAsync().ConfigureAwait(false);
            try
            {
                bool stopScan;
                lock (m_lock)
                {
                    stopScan = m_activity == ScanActivity.Asynchronous;
                    m_activity = ScanActivity.Idle;
                    m_generation++;
                    UpdateStatusLocked();
                }
                PublishStatus();
                if (stopScan && m_asyncProvider != null)
                {
                    await TryAsync(
                        () => m_asyncProvider.StopScanAsync(CancellationToken.None),
                        "stopping the scan").ConfigureAwait(false);
                }
                await TryAsync(
                    () => Provider.DetachAsync(CancellationToken.None),
                    "detaching the provider").ConfigureAwait(false);
            }
            finally
            {
                m_controlGate.Release();
            }
            await drained.ConfigureAwait(false);
            if (m_ownsProvider)
            {
                await DisposeProviderAsync().ConfigureAwait(false);
            }
            m_controlGate.Dispose();
            m_stopping.Dispose();
        }

        private async Task DisposeProviderAsync()
        {
            try
            {
                if (Provider is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else if (Provider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                m_logger.ReaderShutdownStepFailed(ex, Name.Name, "disposing the provider");
            }
        }

        private async Task TryAsync(Func<ValueTask> step, string description)
        {
            try
            {
                await step().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.ReaderShutdownStepFailed(ex, Name.Name, description);
            }
        }

        internal async ValueTask AttachAsync(CancellationToken cancellationToken)
        {
            await Provider.AttachAsync(this, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a bad service result with a text.
        /// </summary>
        private static ServiceResult BadResult(StatusCode code, string text)
        {
            return new ServiceResult(code, new LocalizedText(text));
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed))
            {
                throw new ObjectDisposedException(nameof(AutoIdReader));
            }
        }

        /// <summary>
        /// Recomputes <c>DeviceStatus</c> from the activity. Caller holds <c>m_lock</c>.
        /// </summary>
        private void UpdateStatusLocked()
        {
            m_status = m_faulted
                ? DeviceStatusEnumeration.Error
                : m_activity switch
                {
                    ScanActivity.Synchronous => DeviceStatusEnumeration.Busy,
                    ScanActivity.Asynchronous => DeviceStatusEnumeration.Scanning,
                    _ => DeviceStatusEnumeration.Idle
                };
            if (Device.DeviceStatus != null)
            {
                Device.DeviceStatus.Value = m_status;
                Device.DeviceStatus.Timestamp = DateTimeUtc.Now;
            }
        }

        /// <summary>
        /// Notifies subscribers of the status; called after the lock is released.
        /// </summary>
        private void PublishStatus()
        {
            Device.DeviceStatus?.ClearChangeMasks(Runtime.SystemContext, false);
        }

        /// <summary>
        /// The scan the reader is running.
        /// </summary>
        private enum ScanActivity
        {
            Idle,
            Synchronous,
            Asynchronous
        }

        private readonly bool m_ownsProvider;
        private readonly bool m_hasDeviceLocation;
        private readonly ILogger m_logger;
        private readonly IAutoIdScanProvider? m_scanProvider;
        private readonly IAutoIdAsyncScanProvider? m_asyncProvider;
        private readonly IAutoIdRfidProvider? m_rfidProvider;
        private readonly IAutoIdRtlsProvider? m_rtlsProvider;
        private readonly Lock m_lock = new();
        private readonly SemaphoreSlim m_controlGate = new(1, 1);
        private readonly CancellationTokenSource m_stopping = new();
        private readonly OperationTracker m_operations = new();
        private ScanActivity m_activity;
        private DeviceStatusEnumeration m_status;
        private NodeId m_scanOwner = NodeId.Null;
        private string m_scanClient = DeviceClient;
        private long m_generation;
        private bool m_faulted;
        private bool m_disposed;

        private readonly TaskCompletionSource<bool> m_disposeCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// Source-generated logging for readers.
    /// </summary>
    internal static partial class AutoIdReaderLog
    {
        /// <summary>
        /// Reports a failed shutdown step.
        /// </summary>
        [LoggerMessage(
            EventId = AutoIdServerEventIds.AutoIdReader + 0,
            Level = LogLevel.Error,
            Message = "Reader '{Name}' failed while {Step}.")]
        public static partial void ReaderShutdownStepFailed(
            this ILogger logger,
            Exception exception,
            string? name,
            string step);

        /// <summary>
        /// Reports a scan stopped because its session closed.
        /// </summary>
        [LoggerMessage(
            EventId = AutoIdServerEventIds.AutoIdReader + 1,
            Level = LogLevel.Information,
            Message = "Reader '{Name}' stopped its asynchronous scan because the starting session closed.")]
        public static partial void ScanStoppedBySessionClose(
            this ILogger logger,
            string? name);

        /// <summary>
        /// Reports a provider that returned a result of the wrong type.
        /// </summary>
        [LoggerMessage(
            EventId = AutoIdServerEventIds.AutoIdReader + 2,
            Level = LogLevel.Error,
            Message = "The provider of reader '{Name}' returned a result of type '{Type}', which is not a '{Expected}'.")]
        public static partial void ProviderResultTypeMismatch(
            this ILogger logger,
            string? name,
            string? type,
            string expected);
    }
}
