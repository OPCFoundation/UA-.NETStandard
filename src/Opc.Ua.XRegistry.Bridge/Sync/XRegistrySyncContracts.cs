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
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    /// <summary>
    /// Selects how divergent initial states or changes on both sides are arbitrated.
    /// Endpoint clocks and epochs never determine a winner.
    /// </summary>
    public enum XRegistrySyncConflictPolicy
    {
        /// <summary>
        /// Retain both observations as a conflict and await an explicit resolution request.
        /// This is the default policy.
        /// </summary>
        Manual,

        /// <summary>
        /// Prefer the OPC UA observation while retaining the HTTP evidence and enforcing all mutation guards.
        /// </summary>
        PreferOpcUa,

        /// <summary>
        /// Prefer the HTTP observation while retaining the OPC UA evidence and enforcing all mutation guards.
        /// </summary>
        PreferHttp
    }

    /// <summary>
    /// Identifies an endpoint's role in a synchronization pair, not an ordering of its epochs.
    /// </summary>
    public enum XRegistrySyncSide
    {
        /// <summary>
        /// The endpoint accessed through the OPC UA side of the bridge.
        /// </summary>
        OpcUa,

        /// <summary>
        /// The endpoint accessed through the HTTP side of the bridge.
        /// </summary>
        Http
    }

    /// <summary>
    /// Identifies a model-aware entity included in the full synchronization inventory.
    /// </summary>
    public enum XRegistrySyncEntityKind
    {
        /// <summary>
        /// The registry root's meaningful metadata, excluding deployment identity, model, and capabilities.
        /// </summary>
        Registry,

        /// <summary>
        /// A group's metadata at its exact model-defined collection path.
        /// </summary>
        Group,

        /// <summary>
        /// Resource-wide Meta, including the default Version selection and its policy.
        /// </summary>
        ResourceMeta,

        /// <summary>
        /// A specifically identified Version's metadata and any document, not a default Version projection.
        /// </summary>
        Version
    }

    /// <summary>
    /// Describes the overall result of one bounded pass. Numeric values are the report's process exit codes.
    /// </summary>
    public enum XRegistrySyncStatus
    {
        /// <summary>
        /// Exit code 0: inventories and scope checks completed, with no exhausted operation budget,
        /// active conflicts, or pending operations.
        /// </summary>
        Succeeded,

        /// <summary>
        /// Exit code 1: the pass completed its bounded work but active conflicts or pending operations remain.
        /// </summary>
        Conflicts,

        /// <summary>
        /// Exit code 2: inventory or scope validation is incomplete, or the operation budget left work remaining.
        /// </summary>
        Incomplete,

        /// <summary>
        /// Exit code 3: state ownership, integrity, scope binding, quota, or durability prevented a safe pass.
        /// </summary>
        Failed
    }

    /// <summary>
    /// Classifies one ordered observation, action, or held-work record in a synchronization report.
    /// </summary>
    public enum XRegistrySyncRecordKind
    {
        /// <summary>
        /// Matching meaningful state or a recovered operation's convergence or absence was verified.
        /// Readback alone does not attribute an unobserved server response.
        /// </summary>
        Converged,

        /// <summary>
        /// A creation or replacement dispatched in this pass was verified against both endpoints.
        /// </summary>
        Applied,

        /// <summary>
        /// A guarded deletion dispatched in this pass was verified absent at both endpoints.
        /// </summary>
        Deleted,

        /// <summary>
        /// A dry run identified a mutation without dispatching it or persisting state.
        /// </summary>
        Planned,

        /// <summary>
        /// Divergent observations or a failed safety guard require a retained conflict record.
        /// </summary>
        Conflict,

        /// <summary>
        /// An unresolved durable intent holds overlapping work; its mutation will not be resent.
        /// </summary>
        Pending,

        /// <summary>
        /// Work was held because a required inventory was incomplete, deletion was disabled, or a budget was reached.
        /// </summary>
        Skipped,

        /// <summary>
        /// The requested change is outside the supported profile or lacks qualified atomic and conditional guards.
        /// </summary>
        Unsupported,

        /// <summary>
        /// An observation, verification, or state operation failed; the report status describes the overall result.
        /// </summary>
        Failure
    }

    /// <summary>
    /// Describes the lifecycle of retained conflict evidence, which is not discarded after resolution.
    /// </summary>
    public enum XRegistrySyncConflictStatus
    {
        /// <summary>
        /// The conflict remains unresolved and has no pending explicit resolution request.
        /// </summary>
        Active,

        /// <summary>
        /// An endpoint preference is recorded and awaits revalidation of both saved fingerprints and local epochs.
        /// </summary>
        ResolutionRequested,

        /// <summary>
        /// The conflict no longer holds work, but its observations remain in the history.
        /// </summary>
        Resolved,

        /// <summary>
        /// Changed observations or newer conflict evidence replaced this record; any saved decision is no longer valid.
        /// </summary>
        Superseded
    }

    /// <summary>
    /// One registry pair and authenticated scope. Endpoint identities must include
    /// the configured transport address and registry root, not just a display name.
    /// </summary>
    public sealed record XRegistrySyncOptions
    {
        /// <summary>
        /// Binds a synchronization job to its configured OPC UA and HTTP registry identities.
        /// </summary>
        /// <param name="jobId">A stable, nonblank identity for this job and its retained state.</param>
        /// <param name="opcUaEndpointIdentity">
        /// The nonblank OPC UA transport address and registry root identity, not merely a display name.
        /// </param>
        /// <param name="httpEndpointIdentity">
        /// The nonblank HTTP transport address and registry root identity, not merely a display name.
        /// </param>
        /// <exception cref="ArgumentException">A job or endpoint identity is null, empty, or whitespace.</exception>
        public XRegistrySyncOptions(string jobId, string opcUaEndpointIdentity, string httpEndpointIdentity)
        {
            JobId = RequireIdentity(jobId, nameof(jobId));
            OpcUaEndpointIdentity = RequireIdentity(opcUaEndpointIdentity, nameof(opcUaEndpointIdentity));
            HttpEndpointIdentity = RequireIdentity(httpEndpointIdentity, nameof(httpEndpointIdentity));
        }

        /// <summary>
        /// Gets the stable job identity checked when opening retained state.
        /// </summary>
        public string JobId { get; }

        /// <summary>
        /// Gets the configured OPC UA transport and registry root identity bound into the state scope.
        /// </summary>
        public string OpcUaEndpointIdentity { get; }

        /// <summary>
        /// Gets the configured HTTP transport and registry root identity bound into the state scope.
        /// </summary>
        public string HttpEndpointIdentity { get; }

        /// <summary>
        /// Gets the arbitration policy for divergent observations. Defaults to
        /// <see cref="XRegistrySyncConflictPolicy.Manual"/>; preferences never bypass mutation or recovery guards.
        /// </summary>
        public XRegistrySyncConflictPolicy ConflictPolicy { get; init; }

        /// <summary>
        /// Gets whether guarded deletion of previously synchronized empty groups is enabled. Defaults to true.
        /// Disabling it retains live baselines rather than propagating absence.
        /// </summary>
        /// <remarks>
        /// Deletion requires a baseline, complete inventories, confirmed absence and empty child collections,
        /// and the destination's observed epoch. Resource, nonempty subtree, and exact-Version deletion remain held.
        /// </remarks>
        public bool PropagateDeletes { get; init; } = true;

        /// <summary>
        /// Gets the caller context used for OPC UA requests. Defaults to anonymous; subject, authority,
        /// authentication state, and roles bind the retained scope, while reconnect session IDs do not.
        /// </summary>
        public XRegistryCallContext OpcUaContext { get; init; } = XRegistryCallContext.Anonymous;

        /// <summary>
        /// Gets the caller context used for HTTP requests. Defaults to anonymous; subject, authority,
        /// authentication state, and roles bind the retained scope, while reconnect session IDs do not.
        /// </summary>
        public XRegistryCallContext HttpContext { get; init; } = XRegistryCallContext.Anonymous;

        /// <summary>
        /// Gets the positive limit on observed entities per endpoint inventory and entries per collection.
        /// Defaults to 10,000.
        /// </summary>
        public int MaximumEntities { get; init; } = 10_000;

        /// <summary>
        /// Gets the positive limit on collection pages read per endpoint in a pass, including membership rechecks.
        /// Defaults to 1,000.
        /// </summary>
        public int MaximumPages { get; init; } = 1_000;

        /// <summary>
        /// Gets the positive limit on mutation attempts and pending-operation recovery attempts per pass.
        /// Dry-run planning also consumes this budget. Defaults to 100.
        /// </summary>
        public int MaximumOperations { get; init; } = 100;

        /// <summary>
        /// Gets the maximum bytes in one document. Defaults to 16,777,216 bytes (16 MiB);
        /// zero disallows nonempty documents, and negative limits are invalid.
        /// </summary>
        public int MaximumDocumentBytes { get; init; } = 16_777_216;

        /// <summary>
        /// Gets the positive encoded-byte limit for each endpoint's inventory and bounded metadata/model payloads.
        /// Defaults to 67,108,864 bytes (64 MiB), including serialized observation and document overhead.
        /// </summary>
        public int MaximumInventoryBytes { get; init; } = 67_108_864;

        /// <summary>
        /// Gets the positive encoded-byte limit for a complete state snapshot. Defaults to 67,108,864 bytes (64 MiB).
        /// Exhaustion stops persistence rather than discarding intents, outcomes, conflicts, or tombstones.
        /// </summary>
        public int MaximumStateBytes { get; init; } = 67_108_864;

        /// <summary>
        /// Gets the JSON nesting limit for bounded metadata and state encoding or decoding.
        /// Defaults to 64 and must be between 1 and 256, inclusive.
        /// </summary>
        public int MaximumJsonDepth { get; init; } = 64;

        /// <summary>
        /// Gets the deadline for each endpoint request, inspection, or operation-journal lookup.
        /// Defaults to 30 seconds and must be positive and no greater than <see cref="int.MaxValue"/> milliseconds.
        /// </summary>
        public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

        internal void Validate()
        {
            OpcUaContext.ThrowIfNull(nameof(OpcUaContext));
            HttpContext.ThrowIfNull(nameof(HttpContext));
            if (ConflictPolicy is < XRegistrySyncConflictPolicy.Manual or > XRegistrySyncConflictPolicy.PreferHttp)
            {
                throw new ArgumentOutOfRangeException(nameof(ConflictPolicy));
            }
            if (MaximumEntities <= 0 ||
                MaximumPages <= 0 ||
                MaximumOperations <= 0 ||
                MaximumDocumentBytes < 0 ||
                MaximumInventoryBytes <= 0 ||
                MaximumStateBytes <= 0 ||
                MaximumJsonDepth is < 1 or > 256 ||
                RequestTimeout <= TimeSpan.Zero ||
                RequestTimeout.TotalMilliseconds > int.MaxValue)
            {
                throw new ArgumentException("Synchronization limits must be finite and positive.");
            }
        }

        private static string RequireIdentity(string value, string parameterName)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("A stable identity is required.", parameterName)
                : value;
        }
    }

    /// <summary>
    /// A bounded observation. Metadata contains client-meaningful attributes only;
    /// Epoch is the independent, exact unsigned counter at this endpoint.
    /// Null and empty documents remain distinct.
    /// </summary>
    public sealed record XRegistrySyncObservation
    {
        internal XRegistrySyncObservation(
            string path,
            XRegistrySyncEntityKind kind,
            string fingerprint,
            string epoch,
            JsonElement metadata,
            ByteString document)
        {
            Path = path;
            Kind = kind;
            Fingerprint = fingerprint;
            Epoch = epoch;
            Metadata = metadata.Clone();
            Document = document.IsNull ? default : ByteString.From(document.Span);
        }

        /// <summary>
        /// Gets the exact registry-relative, collection-aware entity path; the registry root is <c>/</c>.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the model-aware entity kind represented by this observation.
        /// </summary>
        public XRegistrySyncEntityKind Kind { get; }

        /// <summary>
        /// Gets the canonical fingerprint of the path, kind, meaningful metadata, and exact document bytes.
        /// Object property ordering and equivalent JSON number spellings do not change it.
        /// </summary>
        /// <remarks>
        /// Top-level generated counters, timestamps, links, default projections, and correlation fields are excluded.
        /// Identically named fields in nested user metadata remain meaningful.
        /// </remarks>
        public string Fingerprint { get; }

        /// <summary>
        /// Gets this endpoint's exact unsigned epoch as a decimal string, including zero and values beyond UInt32.
        /// It guards only this endpoint's observation and is never ordered against the other registry's epoch.
        /// </summary>
        public string Epoch { get; }

        /// <summary>
        /// Gets a detached metadata snapshot with generated top-level attributes and collection projections removed.
        /// Nested user-defined attributes are retained.
        /// </summary>
        public JsonElement Metadata { get; }

        /// <summary>
        /// Gets the exact document bytes, or a null <see cref="ByteString"/> for an entity without a document.
        /// An empty document is distinct from a null document.
        /// </summary>
        public ByteString Document { get; }
    }

    /// <summary>
    /// Durable evidence of both observations, including the losing observation
    /// when a preference is applied. A resolution is a request, not a forced write.
    /// </summary>
    public sealed record XRegistrySyncConflict
    {
        internal XRegistrySyncConflict(string id, string path, string reason, DateTimeOffset observedAt)
        {
            Id = id;
            Path = path;
            Reason = reason;
            ObservedAt = observedAt;
        }

        /// <summary>
        /// Gets the job-local identity used to list and request resolution of this retained conflict.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the collection-aware entity path, or <c>/</c> for a registry-wide scope conflict.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the machine-readable reason code describing why the observations or operation were held.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// Gets the UTC time at which this conflict evidence was recorded by the configured time provider.
        /// </summary>
        public DateTimeOffset ObservedAt { get; }

        /// <summary>
        /// Gets the saved OPC UA observation, or null when no observation was available.
        /// Null alone is not proof of absence from an incomplete inventory.
        /// </summary>
        public XRegistrySyncObservation? OpcUa { get; internal init; }

        /// <summary>
        /// Gets the saved HTTP observation, or null when no observation was available.
        /// Null alone is not proof of absence from an incomplete inventory.
        /// </summary>
        public XRegistrySyncObservation? Http { get; internal init; }

        /// <summary>
        /// Gets the last confirmed common fingerprint for the path, or null if no live baseline existed.
        /// </summary>
        public string? BaselineFingerprint { get; internal init; }

        /// <summary>
        /// Gets the related job-local durable intent identity, or null for a conflict not tied to an intent.
        /// This does not imply that an operation identity was sent to an endpoint.
        /// </summary>
        public string? OperationId { get; internal init; }

        /// <summary>
        /// Gets whether the conflict is active, awaiting decision revalidation, resolved, or superseded.
        /// </summary>
        public XRegistrySyncConflictStatus Status { get; internal init; }

        /// <summary>
        /// Gets the requested endpoint preference, or <see cref="XRegistrySyncConflictPolicy.Manual"/>
        /// when no offline resolution request has been recorded. The value alone does not authorize a write.
        /// </summary>
        public XRegistrySyncConflictPolicy Resolution { get; internal init; }

        /// <summary>
        /// Gets the UTC time of the most recent offline resolution request, or null if none has been recorded.
        /// </summary>
        public DateTimeOffset? ResolutionRequestedAt { get; internal init; }
    }

    /// <summary>
    /// Describes one entry in a pass's ordered report, optionally linked to retained operation or conflict evidence.
    /// </summary>
    /// <param name="Path">The exact collection-aware entity path, or <c>/</c> for a job-wide record.</param>
    /// <param name="Kind">The observation, action, or held-work classification.</param>
    /// <param name="Detail">A human-readable explanation of the observation, action, or safety restriction.</param>
    public sealed record XRegistrySyncRecord(
        string Path,
        XRegistrySyncRecordKind Kind,
        string Detail)
    {
        /// <summary>
        /// Gets the related job-local intent identity, or null if no intent is associated with this record.
        /// It is not evidence that an operation ID was sent upstream.
        /// </summary>
        public string? OperationId { get; init; }

        /// <summary>
        /// Gets the related conflict identity, or null when the record does not refer to conflict evidence.
        /// </summary>
        public string? ConflictId { get; init; }
    }

    /// <summary>
    /// Structured output for a single bounded polling pass. Nonzero exit codes
    /// distinguish unresolved conflicts, incomplete inventories, and failed state.
    /// </summary>
    /// <remarks>
    /// Dry-run counts and records describe a preview, not durable changes. Readback-only recovery contributes
    /// convergence records rather than claiming a mutation was applied or deleted during this pass.
    /// </remarks>
    public sealed record XRegistrySyncReport
    {
        /// <summary>
        /// Gets the overall result, with failed state taking precedence over incomplete work and then conflicts.
        /// </summary>
        public XRegistrySyncStatus Status { get; internal init; }

        /// <summary>
        /// Gets the process exit code: 0 for succeeded, 1 for conflicts or pending operations,
        /// 2 for incomplete or bounded work remaining, and 3 for failed state.
        /// </summary>
        public int ExitCode => (int)Status;

        /// <summary>
        /// Gets whether the pass only previewed work without modifying endpoints or persistent artifacts.
        /// </summary>
        public bool DryRun { get; internal init; }

        /// <summary>
        /// Gets whether both full inventories and scope checks completed without exhausting the operation budget.
        /// A false value cannot authorize absence-based creation or deletion.
        /// </summary>
        public bool InventoryComplete { get; internal init; }

        /// <summary>
        /// Gets the number of distinct entity paths present in either endpoint's inventory at the end of the pass.
        /// Paths present on both sides are counted once.
        /// </summary>
        public int Observed { get; internal init; }

        /// <summary>
        /// Gets the number of distinct paths with convergence records, including verified recovery.
        /// </summary>
        public int Converged { get; internal init; }

        /// <summary>
        /// Gets the number of creation or replacement operations dispatched and verified in this pass.
        /// A nested creation is counted as one operation, not one per contained entity.
        /// </summary>
        public int Applied { get; internal init; }

        /// <summary>
        /// Gets the number of guarded deletion operations dispatched and verified in this pass.
        /// </summary>
        public int Deleted { get; internal init; }

        /// <summary>
        /// Gets the number of mutations planned, but not dispatched, during a dry run.
        /// </summary>
        public int Planned { get; internal init; }

        /// <summary>
        /// Gets the number of active or resolution-requested conflicts at the end of the pass.
        /// </summary>
        public int Conflicts { get; internal init; }

        /// <summary>
        /// Gets the number of retained intents whose outcomes or readback verification remain unresolved.
        /// </summary>
        public int Pending { get; internal init; }

        /// <summary>
        /// Gets the number of failure records, not the number of conflicts or pending operations.
        /// </summary>
        public int Failures { get; internal init; }

        /// <summary>
        /// Gets all records in the order produced by inventory, recovery, and reconciliation.
        /// </summary>
        public ArrayOf<XRegistrySyncRecord> Records { get; internal init; }
    }

    /// <summary>
    /// Single-writer state ownership. Read-only sessions must not create or update
    /// any persistent artifact. A failed or uncertain commit must fail closed.
    /// </summary>
    /// <remarks>
    /// The host owns this store's lifetime and must dispose its sessions before disposing the store.
    /// Restart durability depends on the implementation; an in-memory store provides none.
    /// </remarks>
    public interface IXRegistrySyncStateStore : IAsyncDisposable
    {
        /// <summary>
        /// Opens a state session, acquiring exclusive writer ownership unless a read-only session is requested.
        /// </summary>
        /// <param name="readOnly">
        /// True for a reader that cannot commit or create persistent artifacts; false for a single-writer session.
        /// </param>
        /// <param name="cancellationToken">Cancels opening or waiting for state ownership.</param>
        /// <returns>A session whose disposal releases any writer ownership acquired for it.</returns>
        /// <exception cref="IOException">
        /// Writer ownership is unavailable or the store cannot be used safely.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
        ValueTask<IXRegistrySyncStateSession> OpenAsync(
            bool readOnly = false,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// An exclusive writer or a read-only snapshot reader. Null data means proven
    /// pristine storage, never a failed read, unsupported schema, or corrupt state.
    /// </summary>
    public interface IXRegistrySyncStateSession : IAsyncDisposable
    {
        /// <summary>
        /// Gets whether this session is restricted to reading and cannot commit state.
        /// </summary>
        bool IsReadOnly { get; }

        /// <summary>
        /// Reads the currently published state snapshot without changing persistent artifacts.
        /// </summary>
        /// <param name="cancellationToken">Cancels the snapshot read.</param>
        /// <returns>
        /// The encoded snapshot, or a null <see cref="ByteString"/> only when storage is proven pristine.
        /// A read failure or invalid stored data must not be returned as empty state.
        /// </returns>
        /// <remarks>
        /// Consumers validate the snapshot's schema, checksum, and job binding before using its contents.
        /// Read the current state before the writer session's first commit.
        /// </remarks>
        /// <exception cref="IOException">The snapshot could not be read safely.</exception>
        /// <exception cref="InvalidDataException">
        /// Stored state or recovery artifacts prevent a valid, bounded snapshot from being read.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The session or store has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
        ValueTask<ByteString> ReadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically publishes a complete replacement snapshot through the writer session.
        /// </summary>
        /// <param name="state">A nonnull, nonempty encoded snapshot within the store's byte quota.</param>
        /// <param name="cancellationToken">
        /// Cancels work before publication; cancellation is not proof of rollback.
        /// </param>
        /// <returns>
        /// A task that completes after publication and any durability barriers required by the store have completed.
        /// </returns>
        /// <remarks>
        /// Read the session's current state before its first commit. An uncertain publication must fail closed,
        /// preserving recovery evidence rather than resetting the job or authorizing mutation retries.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The session is read-only or its required initial state read has not completed.
        /// </exception>
        /// <exception cref="InvalidDataException">
        /// The snapshot is empty, exceeds quota, or stored state is invalid.
        /// </exception>
        /// <exception cref="IOException">
        /// Ownership, atomic publication, or required durability could not be confirmed.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The session or store has been disposed.</exception>
        /// <exception cref="OperationCanceledException">
        /// The cancellation token was canceled before publication.
        /// </exception>
        ValueTask CommitAsync(ByteString state, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Additional guarantees that IFileSystem alone cannot express. A nonlocal
    /// filesystem requires an explicitly qualified implementation; merely flushing
    /// a Stream is not a durable commit or distributed fencing.
    /// </summary>
    /// <remarks>
    /// Qualification must cover exclusive ownership, durable file and directory barriers, and atomic replacement
    /// by the supplied filesystem. Custom, network, or device-backed storage is not qualified implicitly.
    /// </remarks>
    public interface IXRegistrySyncFileDurability
    {
        /// <summary>
        /// Acquires qualified single-writer ownership of a state directory for the returned lifetime.
        /// </summary>
        /// <param name="fileSystem">
        /// The filesystem whose ownership and publication guarantees have been qualified.
        /// </param>
        /// <param name="directory">
        /// The full path of the state directory; writer initialization may create artifacts.
        /// </param>
        /// <param name="cancellationToken">Cancels writer acquisition.</param>
        /// <returns>A nonnull ownership handle that releases the writer claim when asynchronously disposed.</returns>
        /// <exception cref="ArgumentNullException">The filesystem or directory is null.</exception>
        /// <exception cref="ArgumentException">
        /// The filesystem or directory is not supported by this provider.
        /// </exception>
        /// <exception cref="IOException">
        /// Exclusive ownership or a required directory barrier cannot be obtained.
        /// </exception>
        /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
        ValueTask<IAsyncDisposable> AcquireWriterAsync(
            IFileSystem fileSystem,
            string directory,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Completes the qualified storage barrier for written file data without closing the stream.
        /// </summary>
        /// <param name="stream">An open writable stream from the qualified filesystem.</param>
        /// <param name="cancellationToken">
        /// Cancels the flush where the storage implementation supports cancellation.
        /// </param>
        /// <returns>A task that completes only after file data has met the provider's durability guarantee.</returns>
        /// <exception cref="ArgumentNullException">The stream is null.</exception>
        /// <exception cref="IOException">The stream is unsupported or durable file flushing failed.</exception>
        /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
        ValueTask FlushFileAsync(Stream stream, CancellationToken cancellationToken = default);

        /// <summary>
        /// Completes the qualified directory barrier for preceding creation, replacement, or deletion of entries.
        /// </summary>
        /// <param name="directory">The full path of the directory whose entry changes must be durable.</param>
        /// <param name="cancellationToken">
        /// Cancels the barrier where the storage implementation supports cancellation.
        /// </param>
        /// <returns>
        /// A task that completes only after directory entry changes have met the durability guarantee.
        /// </returns>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        /// <exception cref="IOException">The required directory barrier could not be completed.</exception>
        /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
        ValueTask FlushDirectoryAsync(string directory, CancellationToken cancellationToken = default);
    }
}
