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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Connection;

namespace UaLens.Capabilities;

/// <summary>
/// Bounded primary-session evidence. Availability notifications only invalidate metadata;
/// the awaited lifecycle notification cancels and drains probes before the session is disposed.
/// </summary>
internal sealed class CapabilityService : ICapabilityService
{
    public CapabilityService(
        IConnectionWorkspace connection,
        ICapabilityProbe probe,
        TimeProvider? timeProvider = null,
        TimeSpan? cacheDuration = null,
        TimeSpan? probeTimeout = null,
        int capacity = 128,
        int maxConcurrentProbes = 4)
    {
        m_connection = connection ?? throw new ArgumentNullException(nameof(connection));
        m_probe = probe ?? throw new ArgumentNullException(nameof(probe));
        m_clock = timeProvider ?? TimeProvider.System;
        m_cacheDuration = cacheDuration ?? TimeSpan.FromSeconds(30);
        m_probeTimeout = probeTimeout ?? TimeSpan.FromSeconds(5);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrentProbes);
        if (m_cacheDuration <= TimeSpan.Zero || m_cacheDuration > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(cacheDuration));
        }
        if (m_probeTimeout <= TimeSpan.Zero || m_probeTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(probeTimeout));
        }
        m_capacity = capacity;
        m_maxConcurrentProbes = maxConcurrentProbes;
        m_connection.StateChanged += OnStateChanged;
        m_connection.ConnectionChangedAsync += OnConnectionChangedAsync;
    }

    public CapabilityResult GetCached(CapabilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            SessionContext? context = UpdateContext();
            if (context is null || request.NodeId.IsNull)
            {
                return RequiresTarget(context);
            }
            return TryGetCached(request, out CapabilityResult? result)
                ? result
                : new CapabilityResult(CapabilityState.Unknown, "Not checked for this session. Check availability.");
        }
    }

    public Task<CapabilityResult> ProbeAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ProbeWork work;
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            SessionContext? context = UpdateContext();
            if (context is null || request.NodeId.IsNull)
            {
                return Task.FromResult(RequiresTarget(context));
            }
            if (TryGetCached(request, out CapabilityResult? result))
            {
                return Task.FromResult(result);
            }
            if (m_pending.Count >= m_maxConcurrentProbes)
            {
                return Task.FromResult(new CapabilityResult(CapabilityState.Unknown,
                    "Other capability checks are in progress. Retry when they finish."));
            }
            work = new ProbeWork(context.Session, request, m_revision, m_probeTimeout, m_clock, cancellationToken);
            m_pending.Add(work);
        }
        return RunProbeAsync(work, cancellationToken);
    }

    public void Invalidate()
    {
        lock (m_gate)
        {
            InvalidateCore();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (m_gate)
        {
            if (m_disposal is null)
            {
                m_disposed = true;
                m_connection.StateChanged -= OnStateChanged;
                m_disposal = DisposeCoreAsync();
            }
            return new ValueTask(m_disposal);
        }
    }

    private async Task<CapabilityResult> RunProbeAsync(ProbeWork work, CancellationToken cancellationToken)
    {
        try
        {
            CapabilityResult result;
            try
            {
                work.Source.Token.ThrowIfCancellationRequested();
                result = await m_probe.ProbeAsync(work.Session, work.Request, work.Source.Token).ConfigureAwait(false);
                work.Source.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = new CapabilityResult(CapabilityState.Unknown,
                    "The check timed out or its session changed. Check the connection and retry.");
            }
            catch (ServiceResultException error)
            {
                result = CapabilityResult.FromFailure(error.StatusCode);
            }
            catch (Exception error) when (error is TimeoutException or IOException or SocketException)
            {
                result = new CapabilityResult(CapabilityState.Unknown,
                    "The connection did not complete the check. Check the transport and retry.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!m_disposed)
                {
                    UpdateContext();
                }
                if (m_disposed || work.Revision != m_revision)
                {
                    return new CapabilityResult(CapabilityState.Unknown,
                        "The session or its model changed during the check. Refresh to retry.");
                }
                if (work.Source.IsCancellationRequested && result.State != CapabilityState.Unknown)
                {
                    return new CapabilityResult(CapabilityState.Unknown,
                        "The check timed out or was cancelled before completion. Refresh to retry.");
                }
                if (result.State is CapabilityState.Supported or CapabilityState.Unsupported or CapabilityState.Denied)
                {
                    if (m_cache.Count >= m_capacity && !m_cache.ContainsKey(work.Request))
                    {
                        CapabilityRequest oldest = m_cache.MinBy(entry => entry.Value.CheckedAt).Key;
                        m_cache.Remove(oldest);
                    }
                    m_cache[work.Request] = new CacheEntry(result, m_clock.GetTimestamp());
                }
            }
            return result;
        }
        finally
        {
            Task cancellation;
            lock (m_gate)
            {
                work.IsCompleting = true;
                cancellation = work.Cancellation ?? Task.CompletedTask;
            }
            try
            {
                await cancellation.ConfigureAwait(false);
            }
            finally
            {
                lock (m_gate)
                {
                    work.Dispose();
                    m_pending.Remove(work);
                    work.Completed.TrySetResult();
                }
            }
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await DrainAsync().ConfigureAwait(false);
        }
        finally
        {
            m_connection.ConnectionChangedAsync -= OnConnectionChangedAsync;
        }
    }

    private Task OnConnectionChangedAsync(CancellationToken _)
    {
        // Cleanup must finish even when the transition's caller has cancelled.
        return DrainAsync();
    }

    private void OnStateChanged()
    {
        ConnectionSnapshot snapshot = m_connection.Snapshot;
        lock (m_gate)
        {
            if (m_context?.Snapshot != snapshot)
            {
                InvalidateCore();
            }
        }
    }

    private async Task DrainAsync()
    {
        List<ProbeWork> pending;
        lock (m_gate)
        {
            InvalidateCore();
            pending = [.. m_pending];
            foreach (ProbeWork work in pending)
            {
                if (!work.IsCompleting)
                {
                    work.Cancellation ??= work.Source.CancelAsync();
                }
            }
        }
        try
        {
            await Task.WhenAll(pending.Select(work => work.Cancellation ?? Task.CompletedTask)).ConfigureAwait(false);
        }
        finally
        {
            await Task.WhenAll(pending.Select(work => work.Completed.Task)).ConfigureAwait(false);
        }
    }

    private SessionContext? UpdateContext()
    {
        ConnectionSnapshot snapshot = m_connection.Snapshot;
        ISession? session = m_connection.CurrentSession;
        if (!m_connection.IsConnected || !snapshot.IsConnected || session is null)
        {
            if (m_context is not null)
            {
                InvalidateCore();
            }
            return null;
        }
        ArrayOf<string> namespaces = session.NamespaceUris is { } table ? table.ToArrayOf() : default;
        NodeId sessionId = session.SessionId;
        if (m_context is null || !ReferenceEquals(m_context.Session, session) || m_context.Snapshot != snapshot
            || m_context.SessionId != sessionId || !m_context.Namespaces.Span.SequenceEqual(namespaces.Span))
        {
            InvalidateCore();
            m_context = new SessionContext(session, snapshot, sessionId, namespaces);
        }
        return m_context;
    }

    private bool TryGetCached(
        CapabilityRequest request,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CapabilityResult? result)
    {
        if (m_cache.TryGetValue(request, out CacheEntry? entry))
        {
            if (m_clock.GetElapsedTime(entry.CheckedAt) < m_cacheDuration)
            {
                result = entry.Result;
                return true;
            }
            m_cache.Remove(request);
        }
        result = null;
        return false;
    }

    private void InvalidateCore()
    {
        m_revision++;
        m_context = null;
        m_cache.Clear();
    }

    private CapabilityResult RequiresTarget(SessionContext? context)
    {
        if (context is null
            && m_connection.Snapshot.Phase is ConnectionPhase.Connecting or ConnectionPhase.Reconnecting)
        {
            return new CapabilityResult(CapabilityState.Unknown,
                "The primary session is temporarily unavailable. Wait for connection recovery and retry.");
        }
        return new CapabilityResult(CapabilityState.RequiresConfiguration,
            context is null ? "Connect the primary server to check this operation." : "Select a concrete target node.");
    }

    private sealed record SessionContext(
        ISession Session,
        ConnectionSnapshot Snapshot,
        NodeId SessionId,
        ArrayOf<string> Namespaces);

    private sealed record CacheEntry(CapabilityResult Result, long CheckedAt);

    private sealed class ProbeWork : IDisposable
    {
        public ProbeWork(
            ISession session,
            CapabilityRequest request,
            long revision,
            TimeSpan timeout,
            TimeProvider clock,
            CancellationToken cancellationToken)
        {
            Session = session;
            Request = request;
            Revision = revision;
            Deadline = new CancellationTokenSource(timeout, clock);
            Source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Deadline.Token);
        }

        public ISession Session { get; }
        public CapabilityRequest Request { get; }
        public long Revision { get; }
        public CancellationTokenSource Deadline { get; }
        public CancellationTokenSource Source { get; }
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? Cancellation { get; set; }
        public bool IsCompleting { get; set; }

        public void Dispose()
        {
            Source.Dispose();
            Deadline.Dispose();
        }
    }

    private readonly IConnectionWorkspace m_connection;
    private readonly ICapabilityProbe m_probe;
    private readonly TimeProvider m_clock;
    private readonly TimeSpan m_cacheDuration;
    private readonly TimeSpan m_probeTimeout;
    private readonly int m_capacity;
    private readonly int m_maxConcurrentProbes;
    private readonly System.Threading.Lock m_gate = new();
    private readonly Dictionary<CapabilityRequest, CacheEntry> m_cache = [];
    private readonly HashSet<ProbeWork> m_pending = [];
    private SessionContext? m_context;
    private long m_revision;
    private Task? m_disposal;
    private bool m_disposed;
}
