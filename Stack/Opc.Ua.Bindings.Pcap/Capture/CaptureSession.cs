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
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings.Pcap.Models;

namespace Opc.Ua.Bindings.Pcap.Capture
{
    /// <summary>
    /// Wraps an <see cref="ICaptureSource"/> with a state machine, a
    /// per-session async lock, and lifecycle metadata. The lifecycle is:
    /// <c>Starting → Running → Stopping → (Completed | Failed) → Disposed</c>.
    /// </summary>
    /// <remarks>
    /// Sealed by repository convention. Sessions are created and owned by
    /// <see cref="CaptureSessionManager"/>; consumers should obtain
    /// references through that manager rather than constructing sessions
    /// directly.
    /// </remarks>
    public sealed class CaptureSession : IAsyncDisposable
    {
        private readonly SemaphoreSlim m_lock = new(1, 1);
        private readonly ILogger m_logger;

        /// <summary>
        /// Constructs a new capture session wrapping the given source.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// Any argument is <c>null</c>.
        /// </exception>
        public CaptureSession(
            string id,
            CaptureSourceKind sourceKind,
            ICaptureSource source,
            string sessionFolder,
            StartCaptureRequest request,
            ILogger? logger = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionFolder);
            ArgumentNullException.ThrowIfNull(request);

            Id = id;
            SourceKind = sourceKind;
            Source = source;
            SessionFolder = sessionFolder;
            Request = request;
            m_logger = logger ?? NullLogger.Instance;
        }

        /// <summary>Session id (unique within a session manager).</summary>
        public string Id { get; }

        /// <summary>The kind of source.</summary>
        public CaptureSourceKind SourceKind { get; }

        /// <summary>The underlying capture source.</summary>
        public ICaptureSource Source { get; }

        /// <summary>The folder under which session artifacts are written.</summary>
        public string SessionFolder { get; }

        /// <summary>The request that started this session.</summary>
        public StartCaptureRequest Request { get; }

        /// <summary>Current state. Mutated only by Start/Stop/Dispose.</summary>
        public CaptureSessionState State { get; private set; } = CaptureSessionState.Starting;

        /// <summary>UTC timestamp the session entered Running.</summary>
        public DateTimeOffset? StartedAt { get; private set; }

        /// <summary>UTC timestamp the session stopped.</summary>
        public DateTimeOffset? StoppedAt { get; private set; }

        /// <summary>Failure message (if state is Failed).</summary>
        public string? Error { get; private set; }

        /// <summary>
        /// Acquires the per-session async lock. The returned token must
        /// be disposed by the caller (use <c>await using</c>).
        /// </summary>
        public async ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken ct)
        {
            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            return new SessionLockReleaser(m_lock);
        }

        /// <summary>
        /// Starts the session. Idempotent if already running.
        /// </summary>
        public async ValueTask StartAsync(CancellationToken ct)
        {
            IAsyncDisposable sessionLock = await AcquireAsync(ct).ConfigureAwait(false);
            try
            {
                if (State == CaptureSessionState.Running)
                {
                    return;
                }
                if (State != CaptureSessionState.Starting)
                {
                    throw new PcapDiagnosticsException(
                        $"Session {Id} cannot be started from state {State}.");
                }
                try
                {
                    await Source.StartAsync(Request, ct).ConfigureAwait(false);
                    StartedAt = DateTimeOffset.UtcNow;
                    State = CaptureSessionState.Running;
                    m_logger.LogInformation(
                        "Capture session {SessionId} started ({Source}).",
                        Id,
                        SourceKind);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Error = ex.Message;
                    State = CaptureSessionState.Failed;
                    m_logger.LogError(
                        ex,
                        "Capture session {SessionId} failed to start.",
                        Id);
                    throw;
                }
            }
            finally
            {
                await sessionLock.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stops the session. Idempotent if already stopped.
        /// </summary>
        public async ValueTask StopAsync(CancellationToken ct)
        {
            IAsyncDisposable sessionLock = await AcquireAsync(ct).ConfigureAwait(false);
            try
            {
                if (State is CaptureSessionState.Completed
                    or CaptureSessionState.Failed
                    or CaptureSessionState.Disposed)
                {
                    return;
                }
                State = CaptureSessionState.Stopping;
                try
                {
                    await Source.StopAsync(ct).ConfigureAwait(false);
                    StoppedAt = DateTimeOffset.UtcNow;
                    State = CaptureSessionState.Completed;
                    m_logger.LogInformation(
                        "Capture session {SessionId} stopped " +
                        "({FrameCount} frames, {ByteCount} bytes).",
                        Id,
                        Source.FrameCount,
                        Source.ByteCount);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Error = ex.Message;
                    State = CaptureSessionState.Failed;
                    m_logger.LogError(
                        ex,
                        "Capture session {SessionId} failed to stop.",
                        Id);
                    throw;
                }
            }
            finally
            {
                await sessionLock.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Projects the session state into a public DTO suitable for MCP.
        /// </summary>
        public CaptureSessionInfo ToInfo()
        {
            return new CaptureSessionInfo
            {
                SessionId = Id,
                Source = SourceKind,
                State = State,
                StartedAt = StartedAt,
                StoppedAt = StoppedAt,
                FrameCount = Source.FrameCount,
                ByteCount = Source.ByteCount,
                SessionFolder = SessionFolder,
                PcapFilePath = Source.GetRawPcapFilePath(),
                KeyLogFilePath = Source.GetKeyLogFilePath(),
                Error = Error
            };
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (State is CaptureSessionState.Running
                    or CaptureSessionState.Starting)
                {
                    await StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
                // Best effort on dispose.
            }
            await Source.DisposeAsync().ConfigureAwait(false);
            m_lock.Dispose();
            State = CaptureSessionState.Disposed;
        }

        private sealed class SessionLockReleaser : IAsyncDisposable
        {
            private readonly SemaphoreSlim m_semaphore;
            private int m_released;

            public SessionLockReleaser(SemaphoreSlim semaphore)
            {
                m_semaphore = semaphore;
            }

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref m_released, 1) == 0)
                {
                    m_semaphore.Release();
                }
                return default;
            }
        }
    }
}
