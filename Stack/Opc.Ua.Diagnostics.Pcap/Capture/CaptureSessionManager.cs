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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Diagnostics.Pcap.Capture.Sources;
using Opc.Ua.Diagnostics.Pcap.Models;

namespace Opc.Ua.Diagnostics.Pcap.Capture
{
    /// <summary>
    /// Owns the set of active <see cref="CaptureSession"/>s and gates new
    /// session creation behind safety caps (concurrent active sessions,
    /// LRU eviction of completed sessions). Registered as a singleton in
    /// DI; <see cref="StartAsync"/> and <see cref="StopAsync"/> are the
    /// primary entry points for capture tools.
    /// </summary>
    /// <remarks>
    /// Sources are created through the supplied
    /// <see cref="ICaptureSourceFactory"/>. The default factory creates
    /// the four built-in sources (NIC, in-proc client, in-proc server,
    /// replay). Custom sources can be plugged in by registering a
    /// different factory in DI.
    /// </remarks>
    public sealed class CaptureSessionManager : IAsyncDisposable
    {
        /// <summary>
        /// Maximum number of concurrent active (Starting/Running) sessions.
        /// </summary>
        public const int MaxActiveSessions = 8;

        /// <summary>
        /// Maximum number of retained sessions before LRU eviction kicks in.
        /// </summary>
        public const int MaxRetainedSessions = 32;

        private readonly ConcurrentDictionary<string, CaptureSession> m_sessions = new(
            StringComparer.OrdinalIgnoreCase);
        private readonly ICaptureSourceFactory m_sourceFactory;
        private readonly ILoggerFactory m_loggerFactory;
        private readonly ILogger m_logger;
        private readonly string m_baseFolder;
        private readonly Lock m_lock = new();
        private int m_disposed;

        /// <summary>
        /// Constructs a manager that uses the supplied source factory and
        /// stores per-session artifacts under
        /// <c>Path.GetTempPath()/opcua-pcap</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// Any argument is <c>null</c>.
        /// </exception>
        public CaptureSessionManager(
            ICaptureSourceFactory sourceFactory,
            ILoggerFactory? loggerFactory = null)
            : this(
                sourceFactory,
                Path.Combine(Path.GetTempPath(), "opcua-pcap"),
                loggerFactory)
        {
        }

        /// <summary>
        /// Constructs a manager with an explicit base folder for session
        /// artifacts.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// Any argument is <c>null</c>.
        /// </exception>
        public CaptureSessionManager(
            ICaptureSourceFactory sourceFactory,
            string baseFolder,
            ILoggerFactory? loggerFactory = null)
        {
            ArgumentNullException.ThrowIfNull(sourceFactory);
            ArgumentException.ThrowIfNullOrWhiteSpace(baseFolder);

            m_sourceFactory = sourceFactory;
            m_baseFolder = baseFolder;
            m_loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            m_logger = m_loggerFactory.CreateLogger<CaptureSessionManager>();
            Directory.CreateDirectory(m_baseFolder);
        }

        /// <summary>
        /// Creates and starts a new capture session. Throws if the active
        /// session cap would be exceeded.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="request"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="PcapDiagnosticsException">
        /// The cap on concurrent active sessions has been reached, or the
        /// requested source cannot be created.
        /// </exception>
        public async ValueTask<CaptureSession> StartAsync(
            StartCaptureRequest request,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(request);
            ThrowIfDisposed();

            int active = m_sessions.Values.Count(s
                => s.State is CaptureSessionState.Starting or CaptureSessionState.Running);
            if (active >= MaxActiveSessions)
            {
                throw new PcapDiagnosticsException(
                    $"Cannot start a new capture session: {active} sessions " +
                    $"are already active (max {MaxActiveSessions}).");
            }

            string id = Guid.NewGuid().ToString("N");
            string folder = request.SessionFolder is { Length: > 0 }
                ? request.SessionFolder
                : Path.Combine(m_baseFolder, id);
            Directory.CreateDirectory(folder);

            ICaptureSource source = m_sourceFactory.Create(
                request.Source,
                folder,
                m_loggerFactory);
            var session = new CaptureSession(
                id,
                request.Source,
                source,
                folder,
                request,
                m_loggerFactory.CreateLogger<CaptureSession>());

            EvictIfNeeded();

            try
            {
                await session.StartAsync(ct).ConfigureAwait(false);
                m_sessions[id] = session;
                return session;
            }
            catch
            {
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Looks up a session by id.
        /// </summary>
        /// <exception cref="PcapDiagnosticsException">
        /// No session with that id exists.
        /// </exception>
        public CaptureSession Get(string sessionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            if (!m_sessions.TryGetValue(sessionId, out CaptureSession? session))
            {
                throw new PcapDiagnosticsException(
                    $"Capture session '{sessionId}' was not found.");
            }
            return session;
        }

        /// <summary>
        /// Lists every session currently tracked by the manager.
        /// </summary>
        public IReadOnlyList<CaptureSession> List()
        {
            return [.. m_sessions.Values];
        }

        /// <summary>
        /// Stops the named session.
        /// </summary>
        public async ValueTask<CaptureSession> StopAsync(string sessionId, CancellationToken ct)
        {
            CaptureSession session = Get(sessionId);
            await session.StopAsync(ct).ConfigureAwait(false);
            return session;
        }

        /// <summary>
        /// Stops and removes a session by id.
        /// </summary>
        public async ValueTask RemoveAsync(string sessionId, CancellationToken ct)
        {
            if (!m_sessions.TryRemove(sessionId, out CaptureSession? session))
            {
                return;
            }
            try
            {
                await session.StopAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // Best effort.
            }
            await session.DisposeAsync().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            foreach (CaptureSession session in m_sessions.Values)
            {
                try
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // best effort
                }
            }
            m_sessions.Clear();
        }

        private void EvictIfNeeded()
        {
            lock (m_lock)
            {
                while (m_sessions.Count >= MaxRetainedSessions)
                {
                    CaptureSession? oldest = m_sessions.Values
                        .Where(s => s.State
                            is CaptureSessionState.Completed
                            or CaptureSessionState.Failed)
                        .OrderBy(s => s.StoppedAt ?? DateTimeOffset.MaxValue)
                        .FirstOrDefault();
                    if (oldest is null)
                    {
                        // No completed sessions to evict; let the new one in
                        // anyway - active cap above is the hard ceiling.
                        break;
                    }
                    m_sessions.TryRemove(oldest.Id, out _);
                    _ = oldest.DisposeAsync().AsTask();
                    m_logger.LogInformation(
                        "Evicted completed capture session {SessionId} (LRU).",
                        oldest.Id);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed != 0)
            {
                throw new ObjectDisposedException(nameof(CaptureSessionManager));
            }
        }
    }

    /// <summary>
    /// Creates <see cref="ICaptureSource"/> instances for
    /// <see cref="CaptureSessionManager"/>. The default DI registration
    /// uses <c>DefaultCaptureSourceFactory</c>.
    /// </summary>
    public interface ICaptureSourceFactory
    {
        /// <summary>
        /// Creates a source for the requested kind, writing artifacts to
        /// the supplied folder.
        /// </summary>
        ICaptureSource Create(
            CaptureSourceKind kind,
            string sessionFolder,
            ILoggerFactory loggerFactory);
    }
}
