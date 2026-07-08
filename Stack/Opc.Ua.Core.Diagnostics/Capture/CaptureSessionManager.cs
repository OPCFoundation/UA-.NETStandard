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
using Opc.Ua.Pcap.Audit;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Capture
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
        /// Default cap on the number of concurrent active
        /// (Starting/Running) sessions when none is supplied to the
        /// constructor.
        /// </summary>
        public const int DefaultMaxActiveSessions = 8;

        /// <summary>
        /// Maximum number of retained sessions before LRU eviction kicks in.
        /// </summary>
        public const int MaxRetainedSessions = 32;

        private readonly ConcurrentDictionary<string, CaptureSession> m_sessions = new(
            StringComparer.OrdinalIgnoreCase);

        private readonly ICaptureSourceFactory m_sourceFactory;
        private readonly ILoggerFactory m_loggerFactory;
        private readonly ILogger m_logger;
        private readonly IPcapAuditSink? m_auditSink;
        private readonly IKeyEscrowProvider? m_escrowProvider;
        private readonly string m_baseFolder;
        private readonly Lock m_lock = new();
        private int m_disposed;

        private readonly ConcurrentDictionary<string, IKeyEscrowSession> m_escrowSessions = new(
            StringComparer.OrdinalIgnoreCase);

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
                loggerFactory,
                maxActiveSessions: null,
                auditSink: null,
                escrowProvider: null)
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
            : this(
                sourceFactory,
                baseFolder,
                loggerFactory,
                maxActiveSessions: null,
                auditSink: null,
                escrowProvider: null)
        {
        }

        /// <summary>
        /// Constructs a manager with an explicit base folder and an
        /// optional cap on the number of concurrent active sessions.
        /// Passing <c>null</c> for <paramref name="maxActiveSessions"/>
        /// uses <see cref="DefaultMaxActiveSessions"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="sourceFactory"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="baseFolder"/> is null or whitespace.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="maxActiveSessions"/> is less than 1.
        /// </exception>
        public CaptureSessionManager(
            ICaptureSourceFactory sourceFactory,
            string baseFolder,
            ILoggerFactory? loggerFactory,
            int? maxActiveSessions)
            : this(
                sourceFactory,
                baseFolder,
                loggerFactory,
                maxActiveSessions,
                auditSink: null,
                escrowProvider: null)
        {
        }

        /// <summary>
        /// Constructs a manager with an explicit base folder, an optional
        /// cap on the number of concurrent active sessions, and an optional
        /// audit sink for security-sensitive lifecycle events.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="sourceFactory"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="baseFolder"/> is null or whitespace.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="maxActiveSessions"/> is less than 1.
        /// </exception>
        public CaptureSessionManager(
            ICaptureSourceFactory sourceFactory,
            string baseFolder,
            ILoggerFactory? loggerFactory,
            int? maxActiveSessions,
            IPcapAuditSink? auditSink,
            IKeyEscrowProvider? escrowProvider = null)
        {
            ArgumentNullException.ThrowIfNull(sourceFactory);
            ArgumentException.ThrowIfNullOrWhiteSpace(baseFolder);
            int limit = maxActiveSessions ?? DefaultMaxActiveSessions;
            if (limit < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxActiveSessions),
                    limit,
                    "Maximum active sessions must be at least 1.");
            }

            m_sourceFactory = sourceFactory;
            m_baseFolder = baseFolder;
            m_loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            m_logger = m_loggerFactory.CreateLogger<CaptureSessionManager>();
            m_auditSink = auditSink;
            m_escrowProvider = escrowProvider;
            MaxActiveSessions = limit;
            Directory.CreateDirectory(m_baseFolder);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    m_baseFolder,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        /// <summary>
        /// Maximum number of concurrent active (Starting/Running)
        /// sessions enforced by this manager instance. Defaults to
        /// <see cref="DefaultMaxActiveSessions"/>.
        /// </summary>
        public int MaxActiveSessions { get; }

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
        /// <exception cref="ArgumentException">
        /// <paramref name="request"/> specifies a session folder outside the
        /// configured base folder.
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
            folder = ValidateAndResolveSessionFolder(folder, m_baseFolder);
            Directory.CreateDirectory(folder);
            StartCaptureRequest resolvedRequest = new()
            {
                Source = request.Source,
                InterfaceName = request.InterfaceName,
                BpfFilter = request.BpfFilter,
                Promiscuous = request.Promiscuous,
                PcapFilePath = request.PcapFilePath,
                KeyLogFilePath = request.KeyLogFilePath,
                MaxBytes = request.MaxBytes,
                MaxFrames = request.MaxFrames,
                MaxDurationSeconds = request.MaxDurationSeconds,
                SessionFolder = folder
            };

            ICaptureSource source = m_sourceFactory.Create(
                request.Source,
                folder,
                m_loggerFactory);
            IKeyEscrowSession? escrowSession = null;
            var session = new CaptureSession(
                id,
                request.Source,
                source,
                folder,
                resolvedRequest,
                m_loggerFactory.CreateLogger<CaptureSession>());

            EvictIfNeeded();

            try
            {
                if (m_escrowProvider is not null)
                {
                    escrowSession = await m_escrowProvider.BeginSessionAsync(id, folder, ct).ConfigureAwait(false);
                }

                await session.StartAsync(ct).ConfigureAwait(false);
                m_sessions[id] = session;
                if (escrowSession is not null)
                {
                    m_escrowSessions[id] = escrowSession;
                    escrowSession = null;
                }

                await AuditAsync(
                    PcapAuditEventKind.StartCapture,
                    id,
                    folder,
                    remoteEndpoint: null,
                    properties: CreateStartCaptureProperties(request),
                    ct).ConfigureAwait(false);
                return session;
            }
            catch
            {
                // CA1508: escrowSession is non-null on the path where BeginSessionAsync
                // succeeded but a subsequent StartAsync or dictionary insert threw; the
                // analyzer does not model that path.
#pragma warning disable CA1508
                if (escrowSession is not null)
                {
                    await escrowSession.DisposeAsync().ConfigureAwait(false);
                }
#pragma warning restore CA1508

                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Validates that <paramref name="folder"/> resolves to a path under
        /// <paramref name="baseFolder"/>. Defends against path-traversal in
        /// user-supplied <c>SessionFolder</c> values that could otherwise
        /// write capture artifacts (including keylog) to arbitrary filesystem
        /// locations.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="folder"/> resolves outside
        /// <paramref name="baseFolder"/>.
        /// </exception>
        private static string ValidateAndResolveSessionFolder(
            string folder,
            string baseFolder)
        {
            string fullBase = Path.GetFullPath(baseFolder);
            string rootedFolder = Path.IsPathRooted(folder)
                ? folder
                : Path.Combine(fullBase, folder);
            string fullFolder = Path.GetFullPath(rootedFolder);

            if (!fullBase.EndsWith(Path.DirectorySeparatorChar))
            {
                fullBase += Path.DirectorySeparatorChar;
            }

            if (!fullFolder.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fullFolder + Path.DirectorySeparatorChar, fullBase, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"SessionFolder '{folder}' resolves to '{fullFolder}' which is " +
                    $"outside the configured BaseFolder '{baseFolder}'. Capture " +
                    "artifacts must remain inside the base folder.",
                    nameof(folder));
            }

            return fullFolder;
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
            await StopSessionAsync(session, ct).ConfigureAwait(false);
            await AuditAsync(
                PcapAuditEventKind.StopCapture,
                session.Id,
                session.SessionFolder,
                remoteEndpoint: null,
                properties: CreateStopCaptureProperties(session),
                ct).ConfigureAwait(false);
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

            bool shouldAuditStop = session.State is CaptureSessionState.Starting or CaptureSessionState.Running;
            try
            {
                await StopSessionAsync(session, ct).ConfigureAwait(false);
                if (shouldAuditStop)
                {
                    await AuditAsync(
                        PcapAuditEventKind.StopCapture,
                        session.Id,
                        session.SessionFolder,
                        remoteEndpoint: null,
                        properties: CreateStopCaptureProperties(session),
                        ct).ConfigureAwait(false);
                }
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
                    bool shouldAuditStop = session.State is
                        CaptureSessionState.Starting or CaptureSessionState.Running;
                    if (shouldAuditStop)
                    {
                        await StopSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
                    }
                    else
                    {
                        await DisposeEscrowSessionAsync(session.Id, CancellationToken.None).ConfigureAwait(false);
                    }

                    await session.DisposeAsync().ConfigureAwait(false);
                    if (shouldAuditStop)
                    {
                        await AuditAsync(
                            PcapAuditEventKind.StopCapture,
                            session.Id,
                            session.SessionFolder,
                            remoteEndpoint: null,
                            properties: CreateStopCaptureProperties(session),
                            CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // best effort
                }
            }
            m_sessions.Clear();
            m_escrowSessions.Clear();
        }

        private async ValueTask StopSessionAsync(CaptureSession session, CancellationToken ct)
        {
            await session.StopAsync(ct).ConfigureAwait(false);
            await EscrowCapturedKeyMaterialAsync(session, ct).ConfigureAwait(false);
            await DisposeEscrowSessionAsync(session.Id, ct).ConfigureAwait(false);
        }

        private async ValueTask EscrowCapturedKeyMaterialAsync(CaptureSession session, CancellationToken ct)
        {
            if (m_escrowProvider is null ||
                m_escrowProvider is DiskKeyEscrowProvider ||
                !m_escrowSessions.TryGetValue(session.Id, out IKeyEscrowSession? escrowSession))
            {
                return;
            }

            await foreach (ChannelKeyMaterial material in session.Source.ReadKeyMaterialAsync(ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                await escrowSession.EscrowAsync(material, ct).ConfigureAwait(false);
            }
        }

        private async ValueTask DisposeEscrowSessionAsync(string sessionId, CancellationToken ct)
        {
            if (!m_escrowSessions.TryRemove(sessionId, out IKeyEscrowSession? escrowSession))
            {
                return;
            }

            try
            {
                await escrowSession.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                await escrowSession.DisposeAsync().ConfigureAwait(false);
            }
        }

        private ValueTask AuditAsync(
            PcapAuditEventKind kind,
            string sessionId,
            string resourcePath,
            string? remoteEndpoint,
            IReadOnlyDictionary<string, string>? properties,
            CancellationToken ct)
        {
            if (m_auditSink is null)
            {
                return ValueTask.CompletedTask;
            }

            return m_auditSink.OnEventAsync(
                new PcapAuditEvent(
                    kind,
                    DateTimeOffset.UtcNow,
                    sessionId,
                    resourcePath,
                    remoteEndpoint,
                    properties),
                ct);
        }

        private static Dictionary<string, string> CreateStartCaptureProperties(StartCaptureRequest request)
        {
            var properties = new Dictionary<string, string>
            {
                ["Source"] = request.Source.ToString()
            };

            AddIfNotEmpty(properties, "InterfaceName", request.InterfaceName);
            AddIfNotEmpty(properties, "PcapFilePath", request.PcapFilePath);
            return properties;
        }

        private static Dictionary<string, string> CreateStopCaptureProperties(CaptureSession session)
        {
            return new Dictionary<string, string>
            {
                ["Source"] = session.SourceKind.ToString(),
                ["FrameCount"] = session.Source.FrameCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["ByteCount"] = session.Source.ByteCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
        }

        private static void AddIfNotEmpty(Dictionary<string, string> properties, string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                properties[name] = value;
            }
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
