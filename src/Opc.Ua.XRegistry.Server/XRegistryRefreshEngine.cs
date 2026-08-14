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
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Drives the refresh of an xRegistry into the address space: it groups the
    /// stored documents into dependency closures, projects the closures that
    /// changed, retires the ones that are gone, and reports what happened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Independent closures commit independently, so a closure that fails to
    /// validate or project keeps serving its previously active generation instead
    /// of taking the whole registry down with it. A closure whose documents did
    /// not change is not re-projected at all; it reports
    /// <see cref="XRegistryRefreshOutcome.Unchanged"/> and emits no model change,
    /// which is what makes a periodic refresh cheap.
    /// </para>
    /// <para>
    /// Everything companion-specification-specific - parsing, validation,
    /// conversion, bindings - lives behind <see cref="IXRegistryRefreshStrategy"/>.
    /// </para>
    /// </remarks>
    public sealed class XRegistryRefreshEngine : IDisposable
    {
        /// <summary>
        /// Initializes a refresh engine.
        /// </summary>
        /// <param name="strategy">The domain strategy.</param>
        /// <param name="projectionHost">The projection host to commit onto.</param>
        /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
        public XRegistryRefreshEngine(
            IXRegistryRefreshStrategy strategy,
            IXRegistryProjectionHost projectionHost)
        {
            m_strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            m_host = projectionHost ?? throw new ArgumentNullException(nameof(projectionHost));
        }

        /// <summary>
        /// Raised for each material refresh event.
        /// </summary>
        public event EventHandler<XRegistryRefreshEventArgs>? Event;

        /// <summary>
        /// Gets the current refresh generation.
        /// </summary>
        public uint Generation => m_generation;

        /// <summary>
        /// Gets or sets how a previous projection generation is retired after a
        /// successful replacement.
        /// </summary>
        public XRegistryProjectionRetirementPolicy RetirementPolicy { get; set; } =
            XRegistryProjectionRetirementPolicy.Graceful;

        /// <summary>
        /// Refreshes the registry into the address space.
        /// </summary>
        /// <param name="request">The refresh request.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The detailed refresh result.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">The engine has been disposed.</exception>
        public async ValueTask<XRegistryRefreshResult> RefreshAsync(
            XRegistryRefreshRequest request,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (!TryBeginOperation(allowDisposed: false))
            {
                throw new ObjectDisposedException(nameof(XRegistryRefreshEngine));
            }

            try
            {
                await m_mutex.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    return await RefreshCoreAsync(request, ct).ConfigureAwait(false);
                }
                finally
                {
                    m_mutex.Release();
                }
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// Removes every live projection, used while the node manager shuts down.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        public async ValueTask RemoveAllAsync(CancellationToken ct = default)
        {
            if (!TryBeginOperation(allowDisposed: true))
            {
                return;
            }

            try
            {
                await m_mutex.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    foreach (TrackedClosure tracked in m_closures.Values)
                    {
                        if (tracked.State is not null)
                        {
                            await tracked.State.DisposeAsync().ConfigureAwait(false);
                        }
                        if (tracked.Handle is not null)
                        {
                            await m_host.RemoveAsync(tracked.Handle, ct).ConfigureAwait(false);
                        }
                    }
                    m_closures.Clear();
                }
                finally
                {
                    m_mutex.Release();
                }
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// Releases the mutex used to serialize refreshes.
        /// </summary>
        public void Dispose()
        {
            bool disposeMutex;
            lock (m_lifetimeLock)
            {
                if (m_disposed != 0)
                {
                    return;
                }
                m_disposed = 1;
                disposeMutex = TryReserveMutexDisposal();
            }
            if (disposeMutex)
            {
                m_mutex.Dispose();
            }
        }

        private async ValueTask<XRegistryRefreshResult> RefreshCoreAsync(
            XRegistryRefreshRequest request,
            CancellationToken ct)
        {
            DateTime start = DateTime.UtcNow;

            if (request.ExpectedGeneration != 0 && request.ExpectedGeneration != m_generation)
            {
                return Rejected(request, start);
            }

            bool dryRun = request.DryRun;
            bool force = request.Force;

            ArrayOf<XRegistryDependencyClosure> closures = await m_strategy
                .EnumerateClosuresAsync(request, ct).ConfigureAwait(false);

            var targetKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (XRegistryDependencyClosure closure in closures)
            {
                targetKeys.Add(closure.Key);
            }
            HashSet<string> selectedXids = ResolveSelection(closures, request.Selection);

            uint newGeneration = m_generation + 1;
            var results = new List<XRegistryRefreshItemResult>();
            int succeeded = 0;
            int unchanged = 0;
            int failed = 0;
            int skipped = 0;

            // Retire tracked closures that are no longer desired - deleted,
            // disabled, or re-partitioned into a different closure - before any
            // new work commits, so a member that moved between closures is never
            // projected twice at once.
            (int retired, ArrayOf<XRegistryRefreshItemResult> retiredResults) =
                await ReconcileRetirementsAsync(targetKeys, newGeneration, dryRun, ct)
                    .ConfigureAwait(false);
            skipped += retiredResults.Count;
            results.AddRange(retiredResults);

            for (int ii = 0; ii < closures.Count; ii++)
            {
                ct.ThrowIfCancellationRequested();
                XRegistryDependencyClosure closure = closures[ii];

                bool inScope = selectedXids.Count == 0 || IsInScope(closure, selectedXids);

                ArrayOf<XRegistryRefreshItemResult> closureResults = await ProcessClosureAsync(
                    closure, newGeneration, force && inScope, dryRun, ct).ConfigureAwait(false);

                foreach (XRegistryRefreshItemResult result in closureResults)
                {
                    if (selectedXids.Count != 0 && !selectedXids.Contains(result.Xid))
                    {
                        continue;
                    }
                    results.Add(result);
                    switch (result.Outcome)
                    {
                        case XRegistryRefreshOutcome.Success:
                        case XRegistryRefreshOutcome.Warning:
                            succeeded++;
                            break;
                        case XRegistryRefreshOutcome.Unchanged:
                            unchanged++;
                            break;
                        case XRegistryRefreshOutcome.Skipped:
                            skipped++;
                            break;
                        default:
                            failed++;
                            break;
                    }
                }
            }

            var resultArray = results.ToArrayOf();
            if (!dryRun)
            {
                await m_strategy.ApplyResultsAsync(resultArray, ct).ConfigureAwait(false);
                m_generation = newGeneration;
            }

            XRegistryRefreshOutcome overall = failed > 0
                ? (succeeded > 0 ? XRegistryRefreshOutcome.Warning : XRegistryRefreshOutcome.Failed)
                : (succeeded > 0 ? XRegistryRefreshOutcome.Success : XRegistryRefreshOutcome.Unchanged);

            var summary = new XRegistryRefreshSummary
            {
                RequestId = request.RequestId ?? string.Empty,
                Generation = dryRun ? 0u : newGeneration,
                Outcome = overall,
                Atomicity = request.Atomicity,
                StartTime = start,
                EndTime = DateTime.UtcNow,
                Total = (uint)results.Count,
                Succeeded = (uint)succeeded,
                Unchanged = (uint)unchanged,
                Failed = (uint)failed,
                Skipped = (uint)skipped,
                Retired = (uint)retired
            };

            RaiseEvent(new XRegistryRefreshEventArgs(XRegistryRefreshEventKind.RefreshCompleted)
            {
                Generation = newGeneration,
                RequestId = request.RequestId ?? string.Empty,
                Outcome = overall,
                Summary = summary
            });

            return new XRegistryRefreshResult(summary, resultArray, dryRun ? 0u : newGeneration);
        }

        private async ValueTask<ArrayOf<XRegistryRefreshItemResult>> ProcessClosureAsync(
            XRegistryDependencyClosure closure,
            uint generation,
            bool force,
            bool dryRun,
            CancellationToken ct)
        {
            // An unprojectable closure - a cycle or an unresolved dependency -
            // retains its previously active generation and reports every member
            // failed, because projecting part of it would publish a graph that
            // references types the address space does not hold.
            if (!closure.IsProjectable)
            {
                string reason = string.Join("; ", closure.Diagnostics);
                var failures = new List<XRegistryRefreshItemResult>(closure.Members.Count);
                foreach (XRegistryRefreshMember member in closure.Members)
                {
                    failures.Add(Failure(
                        member, generation, XRegistryRefreshPhase.DependencyResolution, reason));
                    RaiseFailure(
                        member, generation, XRegistryRefreshEventKind.LoadFailure,
                        XRegistryRefreshPhase.DependencyResolution, reason);
                }
                return failures.ToArrayOf();
            }

            ArrayOf<XRegistryRefreshMember> members = closure.OrderedMembers;
            byte[] aggregateDigest = ComputeAggregateDigest(members);
            m_closures.TryGetValue(closure.Key, out TrackedClosure? tracked);

            if (tracked?.Handle is not null &&
                !force &&
                aggregateDigest.AsSpan().SequenceEqual(tracked.AggregateDigest))
            {
                var unchanged = new List<XRegistryRefreshItemResult>(members.Count);
                foreach (XRegistryRefreshMember member in members)
                {
                    unchanged.Add(new XRegistryRefreshItemResult
                    {
                        Xid = member.Xid,
                        GroupId = member.GroupId,
                        ResourceId = member.ResourceId,
                        VersionId = member.VersionId,
                        DocumentKind = member.DocumentKind,
                        Outcome = XRegistryRefreshOutcome.Unchanged,
                        Phase = XRegistryRefreshPhase.Activation,
                        LoadState = XRegistryLoadState.Active,
                        Generation = tracked.Generation,
                        ContentDigest = member.ContentDigest,
                        Message = "Unchanged."
                    });
                }
                return unchanged.ToArrayOf();
            }

            XRegistryClosurePreparation preparation = await m_strategy
                .PrepareClosureAsync(closure, generation, ct).ConfigureAwait(false);

            if (!preparation.Succeeded)
            {
                var failures = new List<XRegistryRefreshItemResult>(members.Count);
                foreach (XRegistryRefreshMember member in members)
                {
                    failures.Add(Failure(
                        member, generation, preparation.FailurePhase, preparation.FailureReason));
                    RaiseFailure(
                        member, generation, preparation.FailureEventKind,
                        preparation.FailurePhase, preparation.FailureReason);
                }
                return failures.ToArrayOf();
            }

            if (dryRun)
            {
                return DryRunResults(members, preparation, generation);
            }

            XRegistryProjectionHandle? handle = tracked?.Handle;
            if (preparation.Document is { } document && document.Sources.Count > 0)
            {
                try
                {
                    handle = tracked?.Handle is null
                        ? await m_host.AddAsync(document, ct).ConfigureAwait(false)
                        : RetirementPolicy == XRegistryProjectionRetirementPolicy.Immediate
                            ? await m_host
                                .ImmediateReloadAsync(tracked.Handle, document, ct)
                                .ConfigureAwait(false)
                            : await m_host
                                .ShadowReloadAsync(tracked.Handle, document, ct)
                                .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The switch never happened, so the previous generation and its
                    // domain state remain active and are deliberately not released.
                    var failures = new List<XRegistryRefreshItemResult>(members.Count);
                    foreach (XRegistryRefreshMember member in members)
                    {
                        failures.Add(Failure(
                            member, generation, XRegistryRefreshPhase.Activation, ex.Message));
                        RaiseFailure(
                            member, generation, XRegistryRefreshEventKind.LoadFailure,
                            XRegistryRefreshPhase.Activation, ex.Message);
                    }
                    return failures.ToArrayOf();
                }
            }

            var commitContext = new XRegistryClosureCommitContext(closure, preparation, generation)
            {
                Handle = handle,
                PreviousState = tracked?.State
            };
            XRegistryClosureCommitResult commit = await m_strategy
                .CommitClosureAsync(commitContext, ct).ConfigureAwait(false);

            m_closures[closure.Key] = new TrackedClosure
            {
                Key = closure.Key,
                Handle = handle,
                AggregateDigest = aggregateDigest,
                Generation = generation,
                Members = members,
                State = commit.State
            };

            string warning = handle?.Warning ?? string.Empty;
            bool degraded = preparation.Degraded || commit.Degraded || warning.Length != 0;
            string message = warning.Length != 0
                ? "Projected with warning: " + warning
                : degraded
                    ? DegradedMessage(preparation, commit)
                    : "Projected.";

            var deferred = new HashSet<string>(StringComparer.Ordinal);
            foreach (string xid in preparation.DeferredXids)
            {
                deferred.Add(xid);
            }
            Dictionary<string, XRegistryMemberProjection> projections =
                IndexProjections(preparation.MemberProjections);

            var committed = new List<XRegistryRefreshItemResult>(members.Count);
            foreach (XRegistryRefreshMember member in members)
            {
                if (deferred.Contains(member.Xid))
                {
                    continue;
                }
                projections.TryGetValue(member.Xid, out XRegistryMemberProjection? projection);
                XRegistryRefreshOutcome outcome = degraded
                    ? XRegistryRefreshOutcome.Warning
                    : XRegistryRefreshOutcome.Success;
                committed.Add(new XRegistryRefreshItemResult
                {
                    Xid = member.Xid,
                    GroupId = member.GroupId,
                    ResourceId = member.ResourceId,
                    VersionId = member.VersionId,
                    DocumentKind = member.DocumentKind,
                    Outcome = outcome,
                    Phase = XRegistryRefreshPhase.Activation,
                    LoadState = XRegistryLoadState.Active,
                    Generation = generation,
                    MaterializedNodeCount = (uint)(projection?.MaterializedNodeCount ?? 0),
                    RootNodeId = projection?.RootNodeId ?? NodeId.Null,
                    ContentDigest = member.ContentDigest,
                    Message = message
                });
                RaiseEvent(new XRegistryRefreshEventArgs(XRegistryRefreshEventKind.Resource)
                {
                    Xid = member.Xid,
                    GroupId = member.GroupId,
                    ResourceId = member.ResourceId,
                    VersionId = member.VersionId,
                    DocumentKind = member.DocumentKind,
                    Generation = generation,
                    Outcome = outcome,
                    Phase = XRegistryRefreshPhase.Activation,
                    LoadState = XRegistryLoadState.Active
                });
            }

            committed.AddRange(commit.AdditionalResults.Memory.ToArray());
            return committed.ToArrayOf();
        }

        private async ValueTask<(int Retired, ArrayOf<XRegistryRefreshItemResult> Results)>
            ReconcileRetirementsAsync(
                HashSet<string> targetKeys,
                uint generation,
                bool dryRun,
                CancellationToken ct)
        {
            var results = new List<XRegistryRefreshItemResult>();
            int retired = 0;

            string[] stale = [.. m_closures.Keys.Where(k => !targetKeys.Contains(k))];
            foreach (string key in stale)
            {
                if (!m_closures.TryGetValue(key, out TrackedClosure? tracked))
                {
                    continue;
                }

                if (tracked.Handle is not null)
                {
                    retired++;
                    foreach (XRegistryRefreshMember member in tracked.Members)
                    {
                        results.Add(new XRegistryRefreshItemResult
                        {
                            Xid = member.Xid,
                            GroupId = member.GroupId,
                            ResourceId = member.ResourceId,
                            VersionId = member.VersionId,
                            DocumentKind = member.DocumentKind,
                            Outcome = XRegistryRefreshOutcome.Skipped,
                            Phase = XRegistryRefreshPhase.Retirement,
                            LoadState = XRegistryLoadState.Retired,
                            Generation = generation,
                            ContentDigest = member.ContentDigest,
                            Message = "Retired; the resource is no longer projected."
                        });
                    }
                    if (dryRun)
                    {
                        continue;
                    }
                    if (tracked.State is not null)
                    {
                        await tracked.State.DisposeAsync().ConfigureAwait(false);
                    }
                    await m_host.RemoveAsync(tracked.Handle, ct).ConfigureAwait(false);
                }

                if (!dryRun)
                {
                    if (tracked.Handle is null && tracked.State is not null)
                    {
                        await tracked.State.DisposeAsync().ConfigureAwait(false);
                    }
                    m_closures.Remove(key);
                }
            }

            // A committed retirement is reported through the generation change, not
            // as a per-resource result; a dry run has no generation change to
            // report through, so it reports the retirements instead.
            return (retired, dryRun ? results.ToArrayOf() : []);
        }

        private static ArrayOf<XRegistryRefreshItemResult> DryRunResults(
            ArrayOf<XRegistryRefreshMember> members,
            XRegistryClosurePreparation preparation,
            uint generation)
        {
            Dictionary<string, XRegistryMemberProjection> projections =
                IndexProjections(preparation.MemberProjections);
            var results = new List<XRegistryRefreshItemResult>(members.Count);
            foreach (XRegistryRefreshMember member in members)
            {
                projections.TryGetValue(member.Xid, out XRegistryMemberProjection? projection);
                results.Add(new XRegistryRefreshItemResult
                {
                    Xid = member.Xid,
                    GroupId = member.GroupId,
                    ResourceId = member.ResourceId,
                    VersionId = member.VersionId,
                    DocumentKind = member.DocumentKind,
                    Outcome = preparation.Degraded
                        ? XRegistryRefreshOutcome.Warning
                        : XRegistryRefreshOutcome.Success,
                    Phase = XRegistryRefreshPhase.Projection,
                    LoadState = member.LoadState,
                    Generation = generation,
                    MaterializedNodeCount = (uint)(projection?.MaterializedNodeCount ?? 0),
                    ContentDigest = member.ContentDigest,
                    Message = "Dry run; no projection committed. Candidate generation " +
                        generation.ToString(CultureInfo.InvariantCulture) + "."
                });
            }
            return results.ToArrayOf();
        }

        private static Dictionary<string, XRegistryMemberProjection> IndexProjections(
            ArrayOf<XRegistryMemberProjection> projections)
        {
            var index = new Dictionary<string, XRegistryMemberProjection>(StringComparer.Ordinal);
            foreach (XRegistryMemberProjection projection in projections)
            {
                index[projection.Xid] = projection;
            }
            return index;
        }

        private static bool IsInScope(
            XRegistryDependencyClosure closure,
            HashSet<string> selectedXids)
        {
            foreach (XRegistryRefreshMember member in closure.Members)
            {
                if (selectedXids.Contains(member.Xid))
                {
                    return true;
                }
            }
            return false;
        }

        private static string DegradedMessage(
            XRegistryClosurePreparation preparation,
            XRegistryClosureCommitResult commit)
        {
            if (commit.Message.Length != 0)
            {
                return commit.Message;
            }
            return preparation.DegradedMessage.Length != 0
                ? preparation.DegradedMessage
                : "Projected in a degraded state.";
        }

        private static XRegistryRefreshItemResult Failure(
            XRegistryRefreshMember member,
            uint generation,
            XRegistryRefreshPhase phase,
            string reason)
        {
            return new XRegistryRefreshItemResult
            {
                Xid = member.Xid,
                GroupId = member.GroupId,
                ResourceId = member.ResourceId,
                VersionId = member.VersionId,
                DocumentKind = member.DocumentKind,
                Outcome = XRegistryRefreshOutcome.Failed,
                Phase = phase,
                LoadState = XRegistryLoadState.Failed,
                Generation = generation,
                ContentDigest = member.ContentDigest,
                Message = reason ?? string.Empty
            };
        }

        private XRegistryRefreshResult Rejected(XRegistryRefreshRequest request, DateTime start)
        {
            var summary = new XRegistryRefreshSummary
            {
                RequestId = request.RequestId ?? string.Empty,
                Generation = m_generation,
                Outcome = XRegistryRefreshOutcome.Failed,
                Atomicity = request.Atomicity,
                StartTime = start,
                EndTime = DateTime.UtcNow
            };
            return new XRegistryRefreshResult(summary, [], m_generation);
        }

        private HashSet<string> ResolveSelection(
            ArrayOf<XRegistryDependencyClosure> closures,
            ArrayOf<XRegistryResourceSelector> selection)
        {
            var selected = new HashSet<string>(StringComparer.Ordinal);
            if (selection.Count == 0)
            {
                return selected;
            }

            foreach (XRegistryDependencyClosure closure in closures)
            {
                foreach (XRegistryRefreshMember member in closure.Members)
                {
                    foreach (XRegistryResourceSelector selector in selection)
                    {
                        if (m_strategy.Matches(member, selector))
                        {
                            selected.Add(member.Xid);
                            break;
                        }
                    }
                }
            }
            return selected;
        }

        private static byte[] ComputeAggregateDigest(ArrayOf<XRegistryRefreshMember> members)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (XRegistryRefreshMember member in members)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(member.Xid));
                hash.AppendData(Encoding.UTF8.GetBytes(member.VersionId));
                if (!member.ContentDigest.IsNull)
                {
                    hash.AppendData(member.ContentDigest.Span);
                }
            }
            return hash.GetHashAndReset();
        }

        private void RaiseFailure(
            XRegistryRefreshMember member,
            uint generation,
            XRegistryRefreshEventKind kind,
            XRegistryRefreshPhase phase,
            string reason)
        {
            RaiseEvent(new XRegistryRefreshEventArgs(kind)
            {
                Xid = member.Xid,
                GroupId = member.GroupId,
                ResourceId = member.ResourceId,
                VersionId = member.VersionId,
                DocumentKind = member.DocumentKind,
                Generation = generation,
                Outcome = XRegistryRefreshOutcome.Failed,
                Phase = phase,
                LoadState = XRegistryLoadState.Failed,
                Reason = reason ?? string.Empty
            });
        }

        private void RaiseEvent(XRegistryRefreshEventArgs args)
        {
            Event?.Invoke(this, args);
        }

        private bool TryBeginOperation(bool allowDisposed)
        {
            lock (m_lifetimeLock)
            {
                if (m_mutexDisposed || (!allowDisposed && m_disposed != 0))
                {
                    return false;
                }
                m_activeOperations++;
                return true;
            }
        }

        private void EndOperation()
        {
            bool disposeMutex;
            lock (m_lifetimeLock)
            {
                m_activeOperations--;
                disposeMutex = TryReserveMutexDisposal();
            }
            if (disposeMutex)
            {
                m_mutex.Dispose();
            }
        }

        private bool TryReserveMutexDisposal()
        {
            if (m_disposed == 0 ||
                m_activeOperations != 0 ||
                m_closures.Count != 0 ||
                m_mutexDisposed)
            {
                return false;
            }
            m_mutexDisposed = true;
            return true;
        }

        private readonly IXRegistryRefreshStrategy m_strategy;
        private readonly IXRegistryProjectionHost m_host;
        private readonly SemaphoreSlim m_mutex = new(1, 1);
        private readonly System.Threading.Lock m_lifetimeLock = new();

        private readonly Dictionary<string, TrackedClosure> m_closures =
            new(StringComparer.Ordinal);

        private uint m_generation;
        private int m_activeOperations;
        private int m_disposed;
        private bool m_mutexDisposed;

        /// <summary>
        /// The live generation the engine tracks for one closure key.
        /// </summary>
        private sealed class TrackedClosure
        {
            public string Key { get; init; } = string.Empty;

            public XRegistryProjectionHandle? Handle { get; init; }

            public byte[] AggregateDigest { get; init; } = [];

            public uint Generation { get; init; }

            public ArrayOf<XRegistryRefreshMember> Members { get; init; } = [];

            public IXRegistryClosureState? State { get; init; }
        }
    }
}
