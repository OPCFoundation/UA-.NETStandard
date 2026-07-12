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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Coordinates the single, server-wide PushManagement configuration
    /// transaction defined by OPC 10000-12 §§7.10.2-7.10.11.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exactly one Session may own the active transaction at a time.
    /// <see cref="ConfigurationNodeManager"/> certificate handlers and the
    /// <see cref="TrustList"/> handlers instantiated with a coordinator
    /// stage their store/registry mutations as <see cref="PushConfigurationOperation"/>
    /// instances instead of applying them immediately. The staged
    /// operations are only executed, in request order, when the owning
    /// Session calls <c>ApplyChanges</c>; they are discarded (never
    /// applied) when the owning Session calls <c>CancelChanges</c>,
    /// closes, or the node manager is disposed.
    /// </para>
    /// <para>
    /// Implementations must use <see cref="System.Threading.Lock"/> only
    /// for short, synchronous state transitions and must never hold a
    /// lock across an <see langword="await"/>.
    /// </para>
    /// </remarks>
    public interface IPushConfigurationTransactionCoordinator
    {
        /// <summary>
        /// The Session that owns the active transaction, or <see cref="NodeId.Null"/>
        /// when no transaction is active.
        /// </summary>
        NodeId OwnerSessionId { get; }

        /// <summary>
        /// <see langword="true"/> while a transaction is active (i.e. at
        /// least one operation has been staged and not yet committed or
        /// cancelled).
        /// </summary>
        bool IsTransactionActive { get; }

        /// <summary>
        /// <see langword="true"/> when at least one <see cref="TrustList"/>
        /// tracked by this coordinator is currently open for writing.
        /// </summary>
        bool HasOpenTrustListWriter { get; }

        /// <summary>
        /// Validates that <paramref name="sessionId"/> may participate in
        /// the active transaction, without starting one. Used by
        /// operations (such as opening a <see cref="TrustList"/> for
        /// writing) that must reject a conflicting Session but should not
        /// themselves be considered a staged change.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadTransactionPending"/> when
        /// another Session already owns the active transaction.
        /// </exception>
        void ValidateSessionCanParticipate(NodeId sessionId);

        /// <summary>
        /// Registers a staged operation as part of the transaction owned
        /// by <paramref name="sessionId"/>. Starts a new transaction if
        /// none is active.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadTransactionPending"/> when
        /// another Session already owns the active transaction, or with
        /// <see cref="StatusCodes.BadInvalidState"/> when the owning
        /// Session's own <see cref="ApplyChangesAsync"/> commit is still in
        /// flight.
        /// </exception>
        void Stage(NodeId sessionId, PushConfigurationOperation operation);

        /// <summary>
        /// Records that a <see cref="TrustList"/> identified by
        /// <paramref name="trustListId"/> is now open for writing (
        /// <paramref name="isOpen"/> is <see langword="true"/>) or has been
        /// closed (<see langword="false"/>).
        /// </summary>
        void SetTrustListWriteOpen(NodeId trustListId, bool isOpen);

        /// <summary>
        /// Returns a point-in-time snapshot, in request order, of every
        /// operation currently staged in the active transaction.
        /// </summary>
        /// <remarks>
        /// Used by callers that must validate an invariant across the
        /// NET effect of the whole transaction rather than only the
        /// live store/registry state as it appeared before any staging
        /// began (for example, that staging <c>DeleteCertificate</c> for
        /// every certificate type across several requests in the same
        /// transaction does not leave every certificate-group/type slot
        /// empty once every staged operation commits together).
        /// </remarks>
        ArrayOf<PushConfigurationOperation> GetStagedOperations();

        /// <summary>
        /// Commits the transaction owned by <paramref name="sessionId"/>:
        /// runs every staged operation's <see cref="PushConfigurationOperation.CommitAsync"/>
        /// delegate in request order. If a commit fails, every operation
        /// already committed is compensated via
        /// <see cref="PushConfigurationOperation.RollbackAsync"/> in
        /// reverse order. All staged resources are released before this
        /// method returns.
        /// </summary>
        /// <remarks>
        /// The transaction remains owned by <paramref name="sessionId"/>
        /// for the whole duration of this call, including while the staged
        /// operations' asynchronous commit/rollback I/O is in flight: no
        /// other Session may stage or apply until commit/rollback and
        /// diagnostics finalization both complete. Ownership is then
        /// released atomically with diagnostics finalization, so a new
        /// Session may immediately begin a new transaction once this
        /// method returns.
        /// </remarks>
        /// <returns>
        /// <see cref="StatusCodes.BadNothingToDo"/> when no transaction is
        /// active, <see cref="StatusCodes.BadSessionIdInvalid"/> when
        /// <paramref name="sessionId"/> does not own the active
        /// transaction, <see cref="StatusCodes.BadInvalidState"/> when a
        /// <see cref="TrustList"/> is still open for writing or a previous
        /// commit for the same transaction is still in flight, otherwise
        /// the result of applying every staged operation.
        /// </returns>
        ValueTask<ServiceResult> ApplyChangesAsync(NodeId sessionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Cancels (discards without applying) the transaction owned by
        /// <paramref name="sessionId"/>. Every staged operation's resources
        /// are released via <see cref="PushConfigurationOperation.DisposeStaged"/>.
        /// </summary>
        /// <returns>
        /// <see cref="StatusCodes.BadNothingToDo"/> when no transaction is
        /// active, <see cref="StatusCodes.BadSessionIdInvalid"/> when
        /// <paramref name="sessionId"/> does not own the active
        /// transaction, <see cref="StatusCodes.BadInvalidState"/> when
        /// <see cref="ApplyChangesAsync"/> already took every staged
        /// operation and is committing/rolling them back, otherwise
        /// <see cref="StatusCodes.Good"/>.
        /// </returns>
        ServiceResult CancelChanges(NodeId sessionId);

        /// <summary>
        /// Cancels the active transaction if it is owned by <paramref name="sessionId"/>;
        /// a no-op otherwise (including while <see cref="ApplyChangesAsync"/>
        /// is still committing/rolling back that same Session's
        /// transaction). Used when a Session closes so an abandoned
        /// transaction does not block every other Session indefinitely.
        /// </summary>
        void CancelForSessionClose(NodeId sessionId);

        /// <summary>
        /// Unconditionally cancels and disposes any active transaction.
        /// Used when the owning node manager is disposed or shut down.
        /// </summary>
        void Reset();

        /// <summary>
        /// Returns a point-in-time snapshot describing the active
        /// transaction, or the most recently completed one when none is
        /// active, for binding to the standard <c>TransactionDiagnostics</c>
        /// address-space node.
        /// </summary>
        PushConfigurationTransactionSnapshot GetSnapshot();
    }

    /// <summary>
    /// A single staged store/registry mutation participating in the active
    /// PushManagement transaction. Instances are created by
    /// <see cref="ConfigurationNodeManager"/> certificate handlers and by
    /// <see cref="TrustList"/> and registered with
    /// <see cref="IPushConfigurationTransactionCoordinator.Stage"/>.
    /// </summary>
    public sealed class PushConfigurationOperation
    {
        /// <summary>
        /// The certificate group NodeId affected by this operation, or
        /// <see cref="NodeId.Null"/> when this operation does not affect a
        /// certificate group.
        /// </summary>
        public NodeId AffectedCertificateGroup { get; init; } = NodeId.Null;

        /// <summary>
        /// The certificate type NodeId affected by this operation, or
        /// <see cref="NodeId.Null"/> when this operation does not target a
        /// single certificate-group slot (for example, a TrustList
        /// operation). When set together with <see cref="AffectedCertificateGroup"/>,
        /// staging a new operation for the same (group, type) pair
        /// supersedes (and disposes) any operation already staged for that
        /// pair, since a slot can only hold one pending outcome. TrustList
        /// operations leave this <see cref="NodeId.Null"/> so repeated
        /// <c>AddCertificate</c>/<c>RemoveCertificate</c> calls accumulate
        /// instead of superseding each other.
        /// </summary>
        public NodeId AffectedCertificateType { get; init; } = NodeId.Null;

        /// <summary>
        /// <see langword="true"/> when committing this operation leaves
        /// the (<see cref="AffectedCertificateGroup"/>, <see cref="AffectedCertificateType"/>)
        /// slot without a certificate (a staged <c>DeleteCertificate</c>).
        /// <see langword="false"/> (the default) for every other staged
        /// certificate operation, since <c>CreateSelfSignedCertificate</c>
        /// and <c>UpdateCertificate</c> always leave the slot occupied.
        /// Ignored when <see cref="AffectedCertificateType"/> is <see cref="NodeId.Null"/>.
        /// </summary>
        public bool LeavesCertificateSlotEmpty { get; init; }

        /// <summary>
        /// The TrustList NodeId affected by this operation, or
        /// <see cref="NodeId.Null"/> when this operation does not affect a
        /// TrustList.
        /// </summary>
        public NodeId AffectedTrustList { get; init; } = NodeId.Null;

        /// <summary>
        /// Applies the staged mutation to the store(s) and/or registry.
        /// Invoked once, in request order, by <c>ApplyChanges</c>.
        /// </summary>
        public required Func<CancellationToken, Task> CommitAsync { get; init; }

        /// <summary>
        /// Reverses the effect of a completed <see cref="CommitAsync"/> using
        /// the state captured when the operation was staged. Invoked in
        /// reverse request order only when a later operation's commit
        /// fails. May be <see langword="null"/> when the operation has
        /// nothing to compensate (for example, a validation-only stage).
        /// </summary>
        public Func<CancellationToken, Task>? RollbackAsync { get; init; }

        /// <summary>
        /// Releases every resource captured while staging this operation
        /// (certificates, issuer collections, streamed payloads, pending-key
        /// references) without performing any store I/O. Invoked exactly
        /// once per operation after the transaction resolves, regardless of
        /// whether it committed, rolled back, or was cancelled.
        /// </summary>
        public Action? DisposeStaged { get; init; }
    }

    /// <summary>
    /// Point-in-time snapshot of the active (or most recently completed)
    /// PushManagement transaction, used to populate the standard
    /// <c>TransactionDiagnostics</c> address-space node.
    /// </summary>
    public sealed class PushConfigurationTransactionSnapshot
    {
        /// <summary>
        /// <see langword="true"/> when a transaction is currently active.
        /// </summary>
        public bool IsActive { get; init; }

        /// <summary>
        /// The Session owning the active transaction, or <see cref="NodeId.Null"/>.
        /// </summary>
        public NodeId OwnerSessionId { get; init; } = NodeId.Null;

        /// <summary>
        /// UTC start time of the active, or most recently completed, transaction.
        /// </summary>
        public DateTime StartTime { get; init; }

        /// <summary>
        /// UTC end time of the most recently completed transaction.
        /// <see cref="DateTime.MinValue"/> while a transaction is active.
        /// </summary>
        public DateTime EndTime { get; init; }

        /// <summary>
        /// The result of the most recently completed transaction. Good
        /// while a transaction is active and has not yet completed.
        /// </summary>
        public StatusCode Result { get; init; }

        /// <summary>
        /// The certificate groups affected by the transaction.
        /// </summary>
        public ArrayOf<NodeId> AffectedCertificateGroups { get; init; }

        /// <summary>
        /// The TrustLists affected by the transaction.
        /// </summary>
        public ArrayOf<NodeId> AffectedTrustLists { get; init; }

        /// <summary>
        /// Per-operation errors recorded while committing the transaction.
        /// </summary>
        public ArrayOf<TransactionErrorType> Errors { get; init; }
    }
}
