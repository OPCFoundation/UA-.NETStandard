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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    /// <summary>
    /// Counts from a published state snapshot, without contacting either upstream endpoint.
    /// Retained intents include verified and rejected outcomes, not just pending work.
    /// </summary>
    public sealed record XRegistrySyncStateStatus
    {
        /// <summary>
        /// Gets the snapshot generation advanced by each saved state change, or zero for pristine storage.
        /// Offline resolution requests also advance this generation.
        /// </summary>
        public long Generation { get; internal init; }

        /// <summary>
        /// Gets the number of live, confirmed observation pairs used as three-way synchronization baselines.
        /// </summary>
        public int Baselines { get; internal init; }

        /// <summary>
        /// Gets the total retained mutation intents, including verified, rejected, and conflicted operations.
        /// </summary>
        public int Intents { get; internal init; }

        /// <summary>
        /// Gets the number of intents with an actual saved endpoint or journal response, including rejections.
        /// Readback-only verification does not create a server outcome.
        /// </summary>
        public int Outcomes { get; internal init; }

        /// <summary>
        /// Gets the number of intents whose convergence or deletion has been verified.
        /// Verification alone does not imply a recorded server response or attribution.
        /// </summary>
        public int Verified { get; internal init; }

        /// <summary>
        /// Gets the number of intents with unresolved outcomes or verification; their mutations are not retried.
        /// </summary>
        public int Pending { get; internal init; }

        /// <summary>
        /// Gets the number of active conflicts, including resolution requests awaiting revalidation.
        /// Resolved and superseded history is not included.
        /// </summary>
        public int Conflicts { get; internal init; }

        /// <summary>
        /// Gets the number of retained deletion records preserving former baselines against unsafe resurrection.
        /// </summary>
        public int Tombstones { get; internal init; }
    }

    /// <summary>
    /// Offline conflict administration. This class deliberately has no endpoint
    /// dependency; resolving only records a decision against the saved observations.
    /// </summary>
    /// <remarks>
    /// The caller owns the state store's lifetime. Listing and status reads open read-only sessions and never create
    /// persistent artifacts. Invalid state is reported as a failure, not interpreted as an empty job.
    /// </remarks>
    public sealed class XRegistrySyncStateManager
    {
        /// <summary>
        /// Creates an offline state administrator for one job without opening its store or contacting endpoints.
        /// </summary>
        /// <param name="stateStore">The caller-owned store containing the job's retained evidence.</param>
        /// <param name="jobId">The stable, nonblank job identity that persisted snapshots must match.</param>
        /// <param name="timeProvider">
        /// The clock for resolution requests, or null to use <see cref="TimeProvider.System"/>.
        /// </param>
        /// <param name="maximumBytes">The positive encoded-state limit; defaults to 67,108,864 bytes (64 MiB).</param>
        /// <param name="maximumDepth">The JSON nesting limit, from 1 through 256 inclusive; defaults to 64.</param>
        /// <exception cref="ArgumentNullException">The state store is null.</exception>
        /// <exception cref="ArgumentException">The job identity is null, empty, or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The byte limit or JSON nesting limit is invalid.</exception>
        public XRegistrySyncStateManager(
            IXRegistrySyncStateStore stateStore,
            string jobId,
            TimeProvider? timeProvider = null,
            int maximumBytes = 67_108_864,
            int maximumDepth = 64)
        {
            stateStore.ThrowIfNull(nameof(stateStore));
            if (string.IsNullOrWhiteSpace(jobId))
            {
                throw new ArgumentException("A job identity is required.", nameof(jobId));
            }
            if (maximumBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }
            if (maximumDepth is < 1 or > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDepth));
            }
            m_store = stateStore;
            m_jobId = jobId;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_codec = new XRegistrySyncStateCodec(maximumBytes, maximumDepth);
        }

        /// <summary>
        /// Lists active and resolution-requested conflicts without upstream contact or persistent changes.
        /// </summary>
        /// <param name="cancellationToken">Cancels opening or reading the state snapshot.</param>
        /// <returns>
        /// The unresolved evidence, or an empty collection for pristine storage or a job with no active conflicts.
        /// Resolved and superseded history is not included.
        /// </returns>
        /// <exception cref="InvalidDataException">
        /// The snapshot is invalid, exceeds limits, or belongs to another job.
        /// </exception>
        /// <exception cref="IOException">The state snapshot cannot be read safely.</exception>
        /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
        public async ValueTask<ArrayOf<XRegistrySyncConflict>> ListConflictsAsync(
            CancellationToken cancellationToken = default)
        {
            IXRegistrySyncStateSession session =
                await m_store.OpenAsync(readOnly: true, cancellationToken).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable sessionLifetime = session.ConfigureAwait(false);
            XRegistrySyncState? state = await LoadAsync(session, cancellationToken).ConfigureAwait(false);
            return state is null ? [] : [.. state.Conflicts.Values.Where(IsActive)];
        }

        /// <summary>
        /// Reads the published generation and journal counts without contacting endpoints or creating state artifacts.
        /// </summary>
        /// <param name="cancellationToken">Cancels opening or reading the state snapshot.</param>
        /// <returns>The saved counts, or zero-valued status for storage proven pristine.</returns>
        /// <exception cref="InvalidDataException">
        /// The snapshot is invalid, exceeds limits, or belongs to another job.
        /// </exception>
        /// <exception cref="IOException">The state snapshot cannot be read safely.</exception>
        /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
        public async ValueTask<XRegistrySyncStateStatus> ReadStatusAsync(
            CancellationToken cancellationToken = default)
        {
            IXRegistrySyncStateSession session =
                await m_store.OpenAsync(readOnly: true, cancellationToken).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable sessionLifetime = session.ConfigureAwait(false);
            XRegistrySyncState? state = await LoadAsync(session, cancellationToken).ConfigureAwait(false);
            return state is null ? new XRegistrySyncStateStatus() : new XRegistrySyncStateStatus
            {
                Generation = state.Generation,
                Baselines = state.Baselines.Count,
                Intents = state.Intents.Count,
                Outcomes = state.Intents.Values.Count(intent => intent.Response is not null),
                Verified = state.Intents.Values.Count(intent => intent.State == XRegistrySyncIntentState.Verified),
                Pending = state.Intents.Values.Count(intent => intent.Pending),
                Conflicts = state.Conflicts.Values.Count(IsActive),
                Tombstones = state.Tombstones.Count
            };
        }

        /// <summary>
        /// Persists an endpoint preference against an active conflict's saved observations without dispatching a write.
        /// </summary>
        /// <param name="conflictId">
        /// The nonblank identity of an active or resolution-requested conflict in this job.
        /// </param>
        /// <param name="resolution">
        /// The explicit <see cref="XRegistrySyncConflictPolicy.PreferOpcUa"/> or
        /// <see cref="XRegistrySyncConflictPolicy.PreferHttp"/> decision; manual arbitration is not a resolution.
        /// </param>
        /// <param name="cancellationToken">Cancels loading or saving the resolution request.</param>
        /// <returns>The retained conflict updated with the requested preference and its request time.</returns>
        /// <remarks>
        /// The next polling pass revalidates both saved fingerprints and each endpoint's own epoch.
        /// Newer edits invalidate the decision instead of being overwritten. A preference cannot force an ambiguous
        /// pending creation to be repeated. No endpoint dependency or upstream communication is required here.
        /// </remarks>
        /// <exception cref="ArgumentException">The conflict identity is null, empty, or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The resolution is not an explicit endpoint preference.
        /// </exception>
        /// <exception cref="KeyNotFoundException">The conflict does not exist in this job's saved state.</exception>
        /// <exception cref="InvalidOperationException">The conflict is already resolved or superseded.</exception>
        /// <exception cref="InvalidDataException">
        /// The snapshot is invalid, exceeds limits, or belongs to another job.
        /// </exception>
        /// <exception cref="IOException">State ownership, reading, or durable publication failed.</exception>
        /// <exception cref="OverflowException">The saved generation cannot be incremented.</exception>
        /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
        public async ValueTask<XRegistrySyncConflict> ResolveConflictAsync(
            string conflictId,
            XRegistrySyncConflictPolicy resolution,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(conflictId))
            {
                throw new ArgumentException("A conflict identity is required.", nameof(conflictId));
            }
            if (resolution is not (XRegistrySyncConflictPolicy.PreferOpcUa or XRegistrySyncConflictPolicy.PreferHttp))
            {
                throw new ArgumentOutOfRangeException(nameof(resolution),
                    "An explicit endpoint preference is required.");
            }
            IXRegistrySyncStateSession preview =
                await m_store.OpenAsync(readOnly: true, cancellationToken).ConfigureAwait(false);
            await using (preview.ConfigureAwait(false))
            {
                XRegistrySyncState? observed = await LoadAsync(preview, cancellationToken).ConfigureAwait(false);
                if (observed is null ||
                    !observed.Conflicts.TryGetValue(conflictId,
                        out XRegistrySyncConflict? candidate))
                {
                    throw new KeyNotFoundException("The conflict does not exist in this synchronization job.");
                }
                if (!IsActive(candidate))
                {
                    throw new InvalidOperationException("This conflict is no longer active.");
                }
            }
            IXRegistrySyncStateSession session =
                await m_store.OpenAsync(readOnly: false, cancellationToken).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable sessionLifetime = session.ConfigureAwait(false);
            XRegistrySyncState? state = await LoadAsync(session, cancellationToken).ConfigureAwait(false);
            if (state is null || !state.Conflicts.TryGetValue(conflictId, out XRegistrySyncConflict? conflict))
            {
                throw new KeyNotFoundException("The conflict does not exist in this synchronization job.");
            }
            if (!IsActive(conflict))
            {
                throw new InvalidOperationException("This conflict is no longer active.");
            }
            XRegistrySyncConflict updated = conflict with
            {
                Status = XRegistrySyncConflictStatus.ResolutionRequested,
                Resolution = resolution,
                ResolutionRequestedAt = m_timeProvider.GetUtcNow()
            };
            state.Conflicts[conflictId] = updated;
            state.Generation = checked(state.Generation + 1);
            await session.CommitAsync(m_codec.Encode(state), cancellationToken).ConfigureAwait(false);
            return updated;
        }

        internal static bool IsActive(XRegistrySyncConflict conflict)
        {
            return conflict.Status is XRegistrySyncConflictStatus.Active or
                XRegistrySyncConflictStatus.ResolutionRequested;
        }

        private async ValueTask<XRegistrySyncState?> LoadAsync(
            IXRegistrySyncStateSession session,
            CancellationToken cancellationToken)
        {
            ByteString bytes = await session.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (bytes.IsNull)
            {
                return null;
            }
            XRegistrySyncState state = m_codec.Decode(bytes);
            if (state.JobId != m_jobId)
            {
                throw new InvalidDataException("The state belongs to another synchronization job.");
            }
            return state;
        }

        private readonly IXRegistrySyncStateStore m_store;
        private readonly string m_jobId;
        private readonly TimeProvider m_timeProvider;
        private readonly XRegistrySyncStateCodec m_codec;
    }
}
