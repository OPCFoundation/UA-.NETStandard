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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Pcap.Bindings;
using Opc.Ua.Pcap.Capture.Sources;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.Models;
using Opc.Ua.PubSub.Pcap;
using Opc.Ua.PubSub.Transports;
using UaLens.Connection;
using UaLens.Plugins.Companions;
using UaLens.Workspace;

namespace UaLens.Diagnostics.Capture
{
    /// <summary>
    /// Owns bounded packet recording, private artifacts, offline replay and explicitly authorized raw export.
    /// It borrows every network runtime and never replays packets onto the network or collects decryption keys.
    /// </summary>
    internal sealed class DiagnosticCaptureWorkspace : IAsyncDisposable
    {
        /// <summary>
        /// Creates the workspace without starting observers, opening files or creating a temporary directory.
        /// </summary>
        public DiagnosticCaptureWorkspace(
            ITelemetryContext telemetry, IChannelCaptureRegistry? clientRegistry = null,
            IConnectionWorkspace? connection = null, TimeProvider? clock = null, Func<string>? newFolder = null)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_clock = clock ?? TimeProvider.System;
            m_newFolder = newFolder ?? NewPrivateFolder;
            m_connection = connection;
            if (clientRegistry is not null)
            {
                m_sources.Add("client", new Source(
                    new DiagnosticCaptureSource("client", DiagnosticCaptureKind.Client, "Workspace UA-TCP client"),
                    (limits, folder) => new CoreDiagnosticCaptureRun(
                        clientRegistry, limits, folder, m_telemetry, m_clock)));
            }
            if (connection is not null)
            {
                m_boundSession = connection.CurrentSession;
                connection.ConnectionChangedAsync += OnConnectionChangedAsync;
            }
        }

        /// <summary>
        /// Gets currently available workspace-owned transport taps; identifiers expire with their runtime.
        /// </summary>
        public ArrayOf<DiagnosticCaptureSource> Sources
        {
            get
            {
                lock (m_gate)
                {
                    return [.. m_sources.Values.Select(source => source.Description)];
                }
            }
        }

        /// <summary>
        /// Returns redacted metadata and live pre-copy admission counts without retaining packet payloads in the view.
        /// </summary>
        public DiagnosticCaptureSnapshot Snapshot()
        {
            lock (m_gate)
            {
                if (m_work.IsFaulted && m_snapshot.Phase != DiagnosticCapturePhase.Failed)
                {
                    return m_snapshot with
                    {
                        Phase = DiagnosticCapturePhase.Failed,
                        Status = "Capture failed. Stop observes the failure; no packet export is available."
                    };
                }
                if (m_run is not { } run)
                {
                    return m_snapshot;
                }
                (long frames, long bytes) = run.Budget.Snapshot();
                return m_snapshot with { Frames = frames, PayloadBytes = bytes };
            }
        }

        /// <summary>
        /// Registers a capture-aware UDP/UADP runtime without recording traffic or owning the wrapped factory.
        /// </summary>
        public DiagnosticPubSubCaptureRegistration RegisterPubSub(
            IPubSubTransportFactory factory, string name)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (factory.TransportProfileUri != Profiles.PubSubUdpUadpTransport)
            {
                throw new NotSupportedException(
                    "The packet writer supports UDP/UADP, not broker or DTLS wire capture.");
            }
            string id = Guid.NewGuid().ToString("N");
            var registry = new PubSubCaptureRegistry();
            var description = new DiagnosticCaptureSource(
                id, DiagnosticCaptureKind.PubSub, $"PubSub {name} ({id[..8]})");
            lock (m_gate)
            {
                ObjectDisposedException.ThrowIf(m_disposal is not null, this);
                m_sources.Add(id, new Source(description,
                    (limits, folder) => new PubSubDiagnosticCaptureRun(
                        registry, limits, folder, m_telemetry, m_clock)));
            }
            return new DiagnosticPubSubCaptureRegistration(
                new CapturingPubSubTransportFactory(factory, registry, m_telemetry.LoggerFactory),
                () => UnregisterAsync(id));
        }

        /// <summary>
        /// Starts one explicitly authorized bounded observer. Completion of this method means recording has started.
        /// </summary>
        public async Task StartAsync(
            string sourceId, DiagnosticCaptureLimits limits, bool allowPacketData,
            CancellationToken cancellationToken = default)
        {
            RequireConsent(limits, allowPacketData);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task work;
            lock (m_gate)
            {
                RequireIdle();
                if (!m_sources.TryGetValue(sourceId, out Source? source))
                {
                    throw new InvalidOperationException("The selected capture source is no longer available.");
                }
                m_sourceId = sourceId;
                m_cancellation?.Dispose();
                m_replayDeadline?.Dispose();
                m_replayDeadline = null;
                m_cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                m_snapshot = NewSnapshot(source.Description.Kind);
                work = m_work = RecordAsync(source, limits, started, m_cancellation.Token);
            }
            Task completed = await Task.WhenAny(started.Task, work).ConfigureAwait(false);
            await completed.ConfigureAwait(false);
        }

        /// <summary>
        /// Copies and reads one bounded local PCAP through the shared replay source; no network runtime is used.
        /// </summary>
        public Task ReplayAsync(
            Stream source, DiagnosticCaptureLimits limits, bool allowPacketData,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            RequireConsent(limits, allowPacketData);
            lock (m_gate)
            {
                RequireIdle();
                m_sourceId = string.Empty;
                m_cancellation?.Dispose();
                m_replayDeadline?.Dispose();
                m_replayDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(limits.DurationSeconds), m_clock);
                m_cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, m_replayDeadline.Token);
                m_snapshot = NewSnapshot(DiagnosticCaptureKind.Replay);
                m_work = ReplayCoreAsync(source, limits, m_cancellation.Token);
                return m_work;
            }
        }

        /// <summary>
        /// Cancels and drains the active recording or replay without closing any borrowed network connection.
        /// Expected recording failures remain visible in the snapshot; Stop does not claim a successful capture.
        /// </summary>
        public async Task StopAsync()
        {
            Task work;
            Task cancellation;
            CancellationToken lifetime;
            lock (m_gate)
            {
                work = m_work;
                lifetime = !work.IsCompleted && m_cancellation is { } active
                    ? active.Token : CancellationToken.None;
                if (!work.IsCompleted)
                {
                    m_run?.Budget.Stop();
                }
                cancellation = !work.IsCompleted && m_cancellation is { } source
                    ? source.CancelAsync() : Task.CompletedTask;
            }
            await cancellation.ConfigureAwait(false);
            try
            {
                await work.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested || work.IsCanceled)
            {
                // The initiating operation retains its canceled task; Stop only observes and drains it.
            }
            catch (Exception error) when (IsExpected(error))
            {
                lock (m_gate)
                {
                    if (ReferenceEquals(m_work, work))
                    {
                        m_snapshot = m_snapshot with
                        {
                            Phase = DiagnosticCapturePhase.Failed,
                            Status = CorrelatedDiagnostics.Failure(error)
                        };
                    }
                }
            }
        }

        /// <summary>
        /// Publishes the selected completed PCAP to a new explicit destination, never an accompanying key file.
        /// </summary>
        public Task ExportAsync(
            Guid captureId, string destination, bool allowPacketData, CancellationToken cancellationToken = default)
        {
            if (!allowPacketData)
            {
                throw new InvalidOperationException("Explicit packet-data export consent is required.");
            }
            return m_fileOperations.ExecuteAsync(
                token => new ValueTask(ExportCoreAsync(captureId, destination, token)), cancellationToken).AsTask();
        }

        /// <summary>
        /// Clears only this workspace's private retained artifact after its active work has drained.
        /// </summary>
        public Task ClearAsync()
        {
            return m_fileOperations.ExecuteAsync(token => new ValueTask(ClearCoreAsync(token)), CancellationToken.None)
                .AsTask();
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_gate)
            {
                m_disposal ??= DisposeCoreAsync();
                return new ValueTask(m_disposal);
            }
        }

        private async Task ExportCoreAsync(
            Guid captureId, string destination, CancellationToken cancellationToken)
        {
            await m_files.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string path;
                void RequireCurrent()
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lock (m_gate)
                    {
                        ObjectDisposedException.ThrowIf(m_disposal is not null, this);
                        if (m_snapshot.Id != captureId ||
                            m_snapshot.Phase != DiagnosticCapturePhase.Completed ||
                            !m_work.IsCompletedSuccessfully ||
                            m_path is null)
                        {
                            throw new InvalidOperationException("The selected completed capture changed.");
                        }
                    }
                }
                RequireCurrent();
                lock (m_gate)
                {
                    path = m_path!;
                }
                var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using (source.ConfigureAwait(false))
                {
                    ByteString digest = ByteString.From(await SHA256.HashDataAsync(source, cancellationToken)
                        .ConfigureAwait(false));
                    RequireCurrent();
                    if (Convert.ToHexString(digest.Span) != Snapshot().Sha256)
                    {
                        throw new IOException("The private capture artifact changed; export was rejected.");
                    }
                    source.Position = 0;
                    await CompanionLocalExport.CopyAsync(destination, source, DiagnosticCaptureLimits.MaximumFileBytes,
                        RequireCurrent, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                m_files.Release();
            }
        }

        private async Task ClearCoreAsync(CancellationToken cancellationToken)
        {
            Guid selected = Snapshot().Id;
            await StopAsync().ConfigureAwait(false);
            await m_files.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (m_gate)
                {
                    if (m_snapshot.Id != selected || !m_work.IsCompleted)
                    {
                        throw new InvalidOperationException("A newer capture replaced the selected recording.");
                    }
                    DeleteOwnedFolder();
                    m_work = Task.CompletedTask;
                    m_snapshot = DiagnosticCaptureSnapshot.Empty;
                }
            }
            finally
            {
                m_files.Release();
            }
        }

        private async Task RecordAsync(
            Source source, DiagnosticCaptureLimits limits, TaskCompletionSource started, CancellationToken token)
        {
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            bool acquired = false;
            try
            {
                await m_files.WaitAsync(token).ConfigureAwait(false);
                acquired = true;
                string folder = CreateOwnedFolder();
                IDiagnosticCaptureRun run = source.Create(limits, folder);
                lock (m_gate)
                {
                    m_run = run;
                }
                bool activated = false;
                Exception? failure = null;
                string? artifact = null;
                try
                {
                    await run.StartAsync(token).ConfigureAwait(false);
                    activated = true;
                    token.ThrowIfCancellationRequested();
                    SetPhase(DiagnosticCapturePhase.Recording,
                        "Recording bounded packet payloads. No decryption keys are collected.");
                    started.TrySetResult();
                    Task duration = Task.Delay(TimeSpan.FromSeconds(limits.DurationSeconds), m_clock, token);
                    Task stopped = await Task.WhenAny(duration, run.Budget.Reached).ConfigureAwait(false);
                    await stopped.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    started.TrySetCanceled(token);
                }
                catch (Exception error) when (IsExpected(error))
                {
                    failure = error;
                    started.TrySetException(error);
                }
                finally
                {
                    SetPhase(DiagnosticCapturePhase.Stopping, "Detaching capture and flushing owned buffers.");
                    try
                    {
                        if (activated)
                        {
                            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            artifact = await run.StopAsync(cleanup.Token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception error) when (IsExpected(error))
                    {
                        failure = Combine(failure, error);
                    }
                    finally
                    {
                        try
                        {
                            await run.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception error) when (IsExpected(error))
                        {
                            failure = Combine(failure, error);
                        }
                    }
                }
                (long frames, long bytes) = run.Budget.Snapshot();
                lock (m_gate)
                {
                    m_run = null;
                }
                if (failure is not null)
                {
                    SetPhase(DiagnosticCapturePhase.Failed, CorrelatedDiagnostics.Failure(failure));
                    return;
                }
                if (artifact is null)
                {
                    SetPhase(DiagnosticCapturePhase.Empty, "Capture was canceled before recording started.");
                    return;
                }
                using var finishing = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await RetainAsync(artifact, limits, frames, bytes, finishing.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (IsExpected(error))
            {
                SetPhase(DiagnosticCapturePhase.Failed, CorrelatedDiagnostics.Failure(error));
                started.TrySetException(error);
                throw;
            }
            finally
            {
                lock (m_gate)
                {
                    m_run = null;
                }
                if (acquired)
                {
                    m_files.Release();
                }
            }
        }

        private async Task ReplayCoreAsync(Stream source, DiagnosticCaptureLimits limits, CancellationToken token)
        {
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            bool acquired = false;
            try
            {
                await m_files.WaitAsync(token).ConfigureAwait(false);
                acquired = true;
                string path = Path.Combine(CreateOwnedFolder(), "replay.pcap");
                await CompanionLocalExport.CopyAsync(
                    path, source, DiagnosticCaptureLimits.MaximumFileBytes, token.ThrowIfCancellationRequested, token)
                    .ConfigureAwait(false);
                await RetainAsync(path, limits, null, null, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                SetPhase(DiagnosticCapturePhase.Empty,
                    "Offline replay was canceled; no completed artifact was published.");
                throw;
            }
            catch (Exception error) when (IsExpected(error))
            {
                SetPhase(DiagnosticCapturePhase.Failed, CorrelatedDiagnostics.Failure(error));
                throw;
            }
            finally
            {
                if (acquired)
                {
                    m_files.Release();
                }
            }
        }

        private async Task RetainAsync(
            string path, DiagnosticCaptureLimits limits, long? frames, long? payloadBytes, CancellationToken token)
        {
            long fileBytes = new FileInfo(path).Length;
            if (fileBytes > DiagnosticCaptureLimits.MaximumFileBytes)
            {
                throw new IOException("The PCAP exceeds the bounded file-size allowance.");
            }
            var replay = new ReplayCaptureSource(m_telemetry.LoggerFactory);
            var rows = new List<DiagnosticPacketRow>();
            long recordedBytes = 0;
            await using (replay.ConfigureAwait(false))
            {
                await replay.StartAsync(new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay, PcapFilePath = path, RequireCompleteReplay = true
                }, token).ConfigureAwait(false);
                await foreach (CaptureFrame frame in replay.ReadCapturedFramesAsync(limits.MaxFrames + 1L, token)
                    .ConfigureAwait(false))
                {
                    if (rows.Count == limits.MaxFrames)
                    {
                        throw new IOException("The PCAP exceeds the selected packet-record limit.");
                    }
                    recordedBytes += frame.Data.Length;
                    rows.Add(new DiagnosticPacketRow(rows.Count + 1, frame.Timestamp, frame.Data.Length,
                        Convert.ToHexString(SHA256.HashData(frame.Data.Span))));
                }
                await replay.StopAsync(token).ConfigureAwait(false);
            }
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            string hash;
            await using (stream.ConfigureAwait(false))
            {
                hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
            }
            token.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                m_path = path;
                m_snapshot = m_snapshot with
                {
                    Phase = DiagnosticCapturePhase.Completed,
                    Frames = frames ?? rows.Count,
                    PayloadBytes = payloadBytes ?? recordedBytes,
                    FileBytes = fileBytes,
                    Sha256 = hash,
                    Packets = [.. rows],
                    Status = "Completed. PCAP headers may be synthetic. " +
                        "No keys or network replay; encrypted data is opaque."
                };
            }
        }

        private Task OnConnectionChangedAsync(CancellationToken cancellationToken)
        {
            ISession? current = m_connection?.CurrentSession;
            ISession? previous = Interlocked.Exchange(ref m_boundSession, current);
            return previous is not null &&
                !ReferenceEquals(previous, current) &&
                Snapshot().Kind == DiagnosticCaptureKind.Client
                ? StopAsync() : Task.CompletedTask;
        }

        private async ValueTask UnregisterAsync(string id)
        {
            bool stop;
            lock (m_gate)
            {
                m_sources.Remove(id);
                stop = m_sourceId == id;
            }
            if (stop)
            {
                await StopAsync().ConfigureAwait(false);
            }
        }

        private async Task DisposeCoreAsync()
        {
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            if (m_connection is not null)
            {
                m_connection.ConnectionChangedAsync -= OnConnectionChangedAsync;
            }
            try
            {
                await m_fileOperations.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await StopAsync().ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        DeleteOwnedFolder();
                    }
                    finally
                    {
                        m_cancellation?.Dispose();
                        m_replayDeadline?.Dispose();
                        m_files.Dispose();
                    }
                }
            }
        }

        private string CreateOwnedFolder()
        {
            DeleteOwnedFolder();
            string folder = Path.GetFullPath(m_newFolder());
            if (Directory.Exists(folder) || File.Exists(folder))
            {
                throw new IOException("The capture folder must be new and owned by this recording.");
            }
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                Directory.CreateDirectory(
                    folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            m_folder = folder;
            return folder;
        }

        private void DeleteOwnedFolder()
        {
            m_path = null;
            if (m_folder is { } folder)
            {
                Directory.Delete(folder, recursive: true);
                m_folder = null;
            }
        }

        private void RequireIdle()
        {
            ObjectDisposedException.ThrowIf(m_disposal is not null, this);
            if (!m_work.IsCompleted)
            {
                throw new InvalidOperationException("Stop the active recording or replay before replacing it.");
            }
        }

        private void SetPhase(DiagnosticCapturePhase phase, string status)
        {
            lock (m_gate)
            {
                m_snapshot = m_snapshot with { Phase = phase, Status = status };
            }
        }

        private static DiagnosticCaptureSnapshot NewSnapshot(DiagnosticCaptureKind kind)
        {
            return DiagnosticCaptureSnapshot.Empty with
            {
                Id = Guid.NewGuid(), Kind = kind, Phase = DiagnosticCapturePhase.Starting,
                Status = "Starting the explicitly authorized recording or offline replay."
            };
        }

        private static void RequireConsent(DiagnosticCaptureLimits limits, bool allowPacketData)
        {
            ArgumentNullException.ThrowIfNull(limits);
            limits.RequireValid();
            if (!allowPacketData)
            {
                throw new InvalidOperationException(
                    "Explicit consent is required because packet payloads may contain secrets.");
            }
        }

        private static bool IsExpected(Exception error)
        {
            return error is IOException or UnauthorizedAccessException or InvalidOperationException or
                ArgumentException or OperationCanceledException or ServiceResultException or AggregateException or
                NotSupportedException or OverflowException or FormatException;
        }

        private static Exception Combine(Exception? first, Exception second)
        {
            return first is null ? second : new AggregateException("Capture and cleanup both failed.", first, second);
        }

        private static string NewPrivateFolder()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local))
            {
                throw new InvalidOperationException("A private per-user capture folder must be configured.");
            }
            return Path.Combine(local, "OPC Foundation", "UaLens", "captures", Guid.NewGuid().ToString("N"));
        }

        private readonly Lock m_gate = new();
        private readonly SemaphoreSlim m_files = new(1, 1);
        private readonly OwnedOperationScope m_fileOperations = new("The capture workspace has closed.");
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_clock;
        private readonly Func<string> m_newFolder;
        private readonly IConnectionWorkspace? m_connection;
        private readonly Dictionary<string, Source> m_sources = new(StringComparer.Ordinal);
        private DiagnosticCaptureSnapshot m_snapshot = DiagnosticCaptureSnapshot.Empty;
        private Task m_work = Task.CompletedTask;
        private Task? m_disposal;
        private CancellationTokenSource? m_cancellation;
        private CancellationTokenSource? m_replayDeadline;
        private ISession? m_boundSession;
        private IDiagnosticCaptureRun? m_run;
        private string m_sourceId = string.Empty;
        private string? m_folder;
        private string? m_path;

        private sealed record Source(
            DiagnosticCaptureSource Description, Func<DiagnosticCaptureLimits, string, IDiagnosticCaptureRun> Create);
    }

    /// <summary>
    /// Removes exactly one runtime's capture target after any active capture has stopped.
    /// </summary>
    internal sealed class DiagnosticPubSubCaptureRegistration(
        IPubSubTransportFactory factory, Func<ValueTask> release) : IAsyncDisposable
    {
        /// <summary>
        /// Gets the repository's capturing decorator over the caller-owned transport factory.
        /// </summary>
        public IPubSubTransportFactory Factory { get; } = factory;

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_gate)
            {
                m_disposal ??= release().AsTask();
                return new ValueTask(m_disposal);
            }
        }

        private readonly Lock m_gate = new();
        private Task? m_disposal;
    }
}
