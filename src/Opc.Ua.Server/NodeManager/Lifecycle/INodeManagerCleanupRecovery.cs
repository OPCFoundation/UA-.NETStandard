/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
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
    /// Identifies a host-authorized cleanup recovery operation.
    /// </summary>
    public enum NodeManagerCleanupOperation
    {
        /// <summary>Discover retained failures.</summary>
        Discover,

        /// <summary>Confirm verified cleanup of one retained failure.</summary>
        Confirm,

        /// <summary>Reconcile a previously verified confirmation with an uncertain outcome.</summary>
        Reconcile,

        /// <summary>Discover confirmations still owned by recovery or cleanup.</summary>
        DiscoverPendingConfirmations
    }

    /// <summary>
    /// Identifies a failure on one exact NodeManager generation.
    /// </summary>
    /// <param name="GenerationId">The opaque identity of the exact generation.</param>
    /// <param name="ParticipantId">The opaque identity of the participant within that generation.</param>
    /// <param name="FailureId">The participant-issued identity of the retained failure.</param>
    public readonly record struct NodeManagerCleanupFailureHandle(Guid GenerationId, Guid ParticipantId, Guid FailureId);

    /// <summary>
    /// Describes a failure retained by a live or retiring NodeManager generation.
    /// </summary>
    public sealed class RetainedNodeManagerCleanupFailure
    {
        /// <summary>
        /// Creates a host-scoped description of a participant's retained failure.
        /// </summary>
        internal RetainedNodeManagerCleanupFailure(
            NodeManagerCleanupFailureHandle handle,
            Guid slotId,
            long generation,
            NodeManagerCleanupFailure failure)
        {
            Handle = handle;
            SlotId = slotId;
            Generation = generation;
            Failure = failure;
        }

        /// <summary>
        /// Gets the handle that identifies this exact generation and retained failure.
        /// </summary>
        public NodeManagerCleanupFailureHandle Handle { get; }

        /// <summary>
        /// Gets the registration slot identifier, when the generation has one.
        /// </summary>
        public Guid SlotId { get; }

        /// <summary>
        /// Gets the generation number within the slot.
        /// </summary>
        public long Generation { get; }

        /// <summary>
        /// Gets the bounded, host-sanitized failure description.
        /// </summary>
        public NodeManagerCleanupFailure Failure { get; }
    }

    /// <summary>
    /// Identifies why part of a cleanup discovery snapshot could not be inspected.
    /// </summary>
    public enum NodeManagerCleanupDiscoveryIssueKind
    {
        /// <summary>
        /// The generation is not currently admitting recovery operations.
        /// </summary>
        GenerationUnavailable,

        /// <summary>
        /// A manager's underlying cleanup participants could not be identified.
        /// </summary>
        ParticipantUnavailable,

        /// <summary>
        /// A known participant's retained failures could not be captured.
        /// </summary>
        FailureSnapshotUnavailable
    }

    /// <summary>
    /// Describes an incomplete part of a cleanup discovery snapshot without exposing provider errors.
    /// </summary>
    /// <param name="GenerationId">The opaque identity of the affected generation.</param>
    /// <param name="ParticipantId">The opaque participant identity, or an empty identifier when unknown.</param>
    /// <param name="Kind">The part of discovery that could not be completed.</param>
    public readonly record struct NodeManagerCleanupDiscoveryIssue(
        Guid GenerationId,
        Guid ParticipantId,
        NodeManagerCleanupDiscoveryIssueKind Kind);

    /// <summary>
    /// Contains the available cleanup failures and any incomplete parts of discovery.
    /// </summary>
    public sealed class NodeManagerCleanupDiscoveryResult
    {
        /// <summary>
        /// Creates a detached discovery result.
        /// </summary>
        internal NodeManagerCleanupDiscoveryResult(
            ArrayOf<RetainedNodeManagerCleanupFailure> failures,
            ArrayOf<NodeManagerCleanupDiscoveryIssue> issues)
        {
            Failures = failures;
            Issues = issues;
        }

        /// <summary>
        /// Gets the failures that could be inspected.
        /// </summary>
        public ArrayOf<RetainedNodeManagerCleanupFailure> Failures { get; }

        /// <summary>
        /// Gets the parts of the snapshot that could not be inspected.
        /// </summary>
        public ArrayOf<NodeManagerCleanupDiscoveryIssue> Issues { get; }
    }

    /// <summary>
    /// Records a trusted host policy's authorization decision and audit identity.
    /// </summary>
    /// <param name="IsAuthorized">Whether the authenticated caller may perform the operation.</param>
    /// <param name="AuditUserId">The authenticated caller's audit identity, when known.</param>
    public readonly record struct NodeManagerCleanupAuthorization(bool IsAuthorized, string? AuditUserId);

    /// <summary>
    /// Authenticates and authorizes the caller through the hosting environment.
    /// </summary>
    /// <remarks>
    /// Implementations are trusted host configuration, not caller-supplied assertions. They must authenticate
    /// the current host request and authorize the requested operation. Discovery has a default failure handle;
    /// confirmation and reconciliation identify the exact failure. An authorized decision must include an audit
    /// identity.
    /// </remarks>
    public interface INodeManagerCleanupAuthorization
    {
        /// <summary>
        /// Authorizes discovery, confirmation, or reconciliation for the current host request.
        /// </summary>
        /// <param name="operation">The requested operation.</param>
        /// <param name="handle">The exact failure for confirmation or reconciliation; default for discovery.</param>
        /// <param name="cancellationToken">Cancels authorization.</param>
        /// <returns>The host's decision and audit identity.</returns>
        ValueTask<NodeManagerCleanupAuthorization> AuthorizeAsync(
            NodeManagerCleanupOperation operation,
            NodeManagerCleanupFailureHandle handle,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Verifies actual closure of the exact resource associated with a retained failure.
    /// </summary>
    /// <remarks>
    /// The host passes the exact retained resource only to this trusted adapter after authorization and
    /// an audited request. A resource name, an authenticated identity, or an operator assertion alone is not
    /// proof of closure. Return false when actual closure cannot be verified. The host retains the generation
    /// while verification runs. The verifier must not retain the resource after returning or await cleanup of
    /// that generation from this callback, and must honor cancellation. Confirmation or reconciliation for
    /// the same generation is rejected while verification is active when the caller passes the supplied
    /// context. Pass that context to nested recovery calls, including calls to another recovery provider.
    /// </remarks>
    public interface INodeManagerCleanupVerifier
    {
        /// <summary>
        /// Verifies that the retained resource has actually been cleaned up.
        /// </summary>
        /// <param name="failure">The exact generation and participant failure to verify.</param>
        /// <param name="resource">The exact retained resource, provided only to the trusted verifier.</param>
        /// <param name="context">The context to pass to any recovery call made by this callback.</param>
        /// <param name="cancellationToken">Cancels verification.</param>
        /// <returns>Whether actual cleanup was verified.</returns>
        ValueTask<bool> VerifyAsync(
            RetainedNodeManagerCleanupFailure failure,
            NodeManagerCleanupResource resource,
            NodeManagerCleanupVerificationContext context,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Identifies active verification calls so nested recovery cannot wait for itself.
    /// </summary>
    /// <remarks>
    /// This context grants no authorization and does not retain the resource, generation, or server.
    /// </remarks>
    public sealed class NodeManagerCleanupVerificationContext
    {
        /// <summary>
        /// Records the exact generation and any enclosing verification.
        /// </summary>
        internal NodeManagerCleanupVerificationContext(
            Guid generationId,
            NodeManagerCleanupVerificationContext? parent)
        {
            m_generationId = generationId;
            m_parent = parent;
        }

        /// <summary>
        /// Rejects recovery that would wait for an active verification in this call chain.
        /// </summary>
        /// <exception cref="InvalidOperationException">This call chain is already verifying the generation.</exception>
        internal void RejectReentry(NodeManagerCleanupFailureHandle handle)
        {
            for (NodeManagerCleanupVerificationContext? context = this;
                context is not null;
                context = context.m_parent)
            {
                if (context.m_generationId == handle.GenerationId && Volatile.Read(ref context.m_active) != 0)
                {
                    throw new InvalidOperationException(
                        "Cleanup recovery cannot be requested during verification for the same generation.");
                }
            }
        }

        /// <summary>
        /// Ends this verification without invalidating an enclosing verification.
        /// </summary>
        internal void Revoke()
        {
            Volatile.Write(ref m_active, 0);
        }

        private readonly Guid m_generationId;
        private readonly NodeManagerCleanupVerificationContext? m_parent;
        private int m_active = 1;
    }

    /// <summary>
    /// Identifies an auditable recovery operation outcome.
    /// </summary>
    public enum NodeManagerCleanupAuditOutcome
    {
        /// <summary>The operation was requested and authorized.</summary>
        Requested,

        /// <summary>The operation was denied by host authorization.</summary>
        Denied,

        /// <summary>The resource's actual cleanup could not be verified.</summary>
        NotVerified,

        /// <summary>The trusted verifier confirmed the exact resource's cleanup.</summary>
        VerificationSucceeded,

        /// <summary>The requested checks completed; physical cleanup may still be pending.</summary>
        Succeeded,

        /// <summary>The operation failed after it was requested.</summary>
        Failed,

        /// <summary>Confirmation or reconciliation was recorded, but lifecycle resumption failed.</summary>
        ResumptionFailed,

        /// <summary>The participant consumed confirmation but its final effect could not be established.</summary>
        ConfirmationOutcomeUnknown,

        /// <summary>The failure was cleared, but a participant callback or subsequent inspection failed.</summary>
        ConfirmationAppliedWithPostFailure,

        /// <summary>
        /// Discovery returned available failures, but could not inspect every participant.
        /// </summary>
        Partial
    }

    /// <summary>
    /// Describes a recovery audit event without resource names, exception text, or credentials.
    /// </summary>
    /// <param name="RequestId">The identifier shared by events for one request.</param>
    /// <param name="Operation">The requested recovery operation.</param>
    /// <param name="Outcome">The operation outcome.</param>
    /// <param name="Handle">The failure involved, or default for discovery.</param>
    /// <param name="AuditUserId">The authenticated audit identity, when known.</param>
    public readonly record struct NodeManagerCleanupAuditRecord(
        Guid RequestId,
        NodeManagerCleanupOperation Operation,
        NodeManagerCleanupAuditOutcome Outcome,
        NodeManagerCleanupFailureHandle Handle,
        string? AuditUserId);

    /// <summary>
    /// Records security audit events through a trusted host audit adapter.
    /// </summary>
    /// <remarks>
    /// The adapter must record the event before completing and must throw if it cannot record it.
    /// It remains available when the OPC UA server has stopped. Implementations should forward events
    /// to the host's audit infrastructure and, when available, the server's OPC UA audit-event API.
    /// </remarks>
    public interface INodeManagerCleanupAudit
    {
        /// <summary>
        /// Records a recovery operation's audit event.
        /// </summary>
        /// <param name="record">The event to record.</param>
        /// <param name="cancellationToken">Cancels recording before it completes.</param>
        /// <returns>A task completed after the event has been recorded.</returns>
        ValueTask RecordAsync(NodeManagerCleanupAuditRecord record, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Discovers and confirms retained cleanup failures through host-controlled security policy.
    /// </summary>
    /// <remarks>
    /// Discovery, confirmation and reconciliation require host authorization. Confirmation also requires
    /// a trusted verifier and recorded request and verification audit events before the participant is changed.
    /// A successful confirmation clears one participant failure; it does not mean that the generation has
    /// finished retirement or change the original retirement result.
    /// </remarks>
    public interface INodeManagerCleanupRecovery
    {
        /// <summary>
        /// Discovers the failures retained by NodeManager generations owned by this host.
        /// </summary>
        /// <param name="cancellationToken">Cancels the discovery request.</param>
        /// <returns>A non-null snapshot of the retained failures.</returns>
        /// <remarks>
        /// This method throws when discovery is incomplete. Use <see cref="DiscoverDetailedAsync"/> to inspect
        /// available failures and the incomplete parts of the snapshot.
        /// </remarks>
        ValueTask<ArrayOf<RetainedNodeManagerCleanupFailure>> DiscoverAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Discovers available failures and reports any participants that could not be inspected.
        /// </summary>
        /// <param name="cancellationToken">Cancels the discovery request.</param>
        /// <returns>The available failures and sanitized discovery issues.</returns>
        /// <remarks>
        /// An issue does not establish that the affected participant has no failures. Cleanup cannot proceed
        /// on the assumption that an uninspectable participant is empty. A generation with closed admission
        /// may already be undergoing cleanup. Authorization and audit are required even for a partial result.
        /// </remarks>
        ValueTask<NodeManagerCleanupDiscoveryResult> DiscoverDetailedAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Discovers opaque handles for confirmations that have not yet finished cleanup.
        /// </summary>
        /// <param name="cancellationToken">Cancels the discovery request.</param>
        /// <returns>A non-null snapshot of pending confirmation handles.</returns>
        /// <remarks>
        /// The snapshot includes confirmations being audited, waiting for other failures, offered to a worker,
        /// or requiring reconciliation. A handle may disappear as cleanup progresses. Discovery does not grant
        /// authority to reconcile it, and an in-progress operation may temporarily reject reconciliation.
        /// If the original failure is still retained, confirm it again with fresh verification instead.
        /// Discovery can fail while a generation has closed recovery admission; retry after that operation ends.
        /// </remarks>
        /// <exception cref="UnauthorizedAccessException">The host does not authorize the request.</exception>
        ValueTask<ArrayOf<NodeManagerCleanupFailureHandle>> DiscoverPendingConfirmationsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Confirms verified cleanup of one exact retained resource.
        /// </summary>
        /// <param name="handle">The handle returned by discovery.</param>
        /// <param name="cancellationToken">Cancels the request before confirmation is applied.</param>
        /// <exception cref="UnauthorizedAccessException">The host does not authorize the request.</exception>
        /// <exception cref="InvalidOperationException">
        /// The failure is stale, cleanup cannot be verified, or the audit request cannot be recorded.
        /// </exception>
        ValueTask ConfirmAsync(
            NodeManagerCleanupFailureHandle handle,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Confirms verified cleanup from a callback without waiting for an active verification in its call chain.
        /// </summary>
        /// <param name="handle">The handle returned by discovery.</param>
        /// <param name="verificationContext">The context supplied to the calling verification callback.</param>
        /// <param name="cancellationToken">Cancels the request before confirmation is applied.</param>
        ValueTask ConfirmAsync(
            NodeManagerCleanupFailureHandle handle,
            NodeManagerCleanupVerificationContext verificationContext,
            CancellationToken cancellationToken);

        /// <summary>
        /// Reconciles an earlier verified confirmation whose effect or cleanup resumption was not established.
        /// </summary>
        /// <param name="handle">The exact handle from the earlier confirmation or pending discovery.</param>
        /// <param name="cancellationToken">Cancels the request before cleanup is resumed.</param>
        /// <remarks>
        /// The host must authorize and audit this operation. It succeeds only while the same generation retains
        /// evidence of an earlier successful verification and consumed confirmation, the original failure is
        /// absent, and no participant in that generation reports another retained failure. Each uncertain
        /// confirmation must be reconciled separately before cleanup can resume. If the original failure is
        /// still present, confirm it again with fresh verification. This operation does not repeat disposal
        /// of the failed resource or change an earlier retirement result.
        /// </remarks>
        /// <exception cref="UnauthorizedAccessException">The host does not authorize the request.</exception>
        /// <exception cref="InvalidOperationException">
        /// No matching evidence remains, a failure is still retained, or required audit cannot be recorded.
        /// </exception>
        ValueTask ReconcileAsync(
            NodeManagerCleanupFailureHandle handle,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reconciles an earlier confirmation without waiting for an active verification in the callback's call chain.
        /// </summary>
        /// <param name="handle">The exact handle from the earlier confirmation or pending discovery.</param>
        /// <param name="verificationContext">The context supplied to the calling verification callback.</param>
        /// <param name="cancellationToken">Cancels the request before cleanup is resumed.</param>
        ValueTask ReconcileAsync(
            NodeManagerCleanupFailureHandle handle,
            NodeManagerCleanupVerificationContext verificationContext,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Reports an audit or resumption failure after the participant already accepted confirmation.
    /// </summary>
    /// <remarks>
    /// Confirmation is not rolled back. Use authorized pending-confirmation discovery to determine whether
    /// evidence still needs reconciliation; cleanup might have progressed before resumption reported a failure.
    /// </remarks>
    public sealed class NodeManagerCleanupPostConfirmationException : InvalidOperationException
    {
        /// <summary>
        /// Creates an exception without a failure handle.
        /// </summary>
        public NodeManagerCleanupPostConfirmationException()
        {
        }

        /// <summary>
        /// Creates an exception with a message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public NodeManagerCleanupPostConfirmationException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates an exception with a message and underlying failure.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The underlying failure.</param>
        public NodeManagerCleanupPostConfirmationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Creates an exception for an already-applied confirmation.
        /// </summary>
        internal NodeManagerCleanupPostConfirmationException(
            NodeManagerCleanupFailureHandle handle,
            Exception innerException)
            : base("Cleanup confirmation was applied, but a participant callback, audit or resumption failed.",
                innerException)
        {
            Handle = handle;
        }

        /// <summary>
        /// Gets the failure whose confirmation was already applied, or a default handle for a manually
        /// created exception.
        /// </summary>
        public NodeManagerCleanupFailureHandle Handle { get; }
    }

    /// <summary>
    /// Reports that a participant consumed a confirmation but its final effect could not be determined.
    /// </summary>
    /// <remarks>
    /// Rediscover the retained failures through the authorized host. If the failure remains, confirm it again
    /// with fresh verification; if it is absent, use
    /// <see cref="INodeManagerCleanupRecovery.ReconcileAsync(NodeManagerCleanupFailureHandle, CancellationToken)"/>
    /// with the same handle. This exception does not assert that the failure was either cleared or retained.
    /// </remarks>
    public sealed class NodeManagerCleanupConfirmationOutcomeUnknownException : InvalidOperationException
    {
        /// <summary>
        /// Creates an exception without a failure handle.
        /// </summary>
        public NodeManagerCleanupConfirmationOutcomeUnknownException()
        {
        }

        /// <summary>
        /// Creates an exception with a message.
        /// </summary>
        public NodeManagerCleanupConfirmationOutcomeUnknownException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates an exception with a message and underlying failure.
        /// </summary>
        public NodeManagerCleanupConfirmationOutcomeUnknownException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Creates an exception for an indeterminate participant confirmation.
        /// </summary>
        internal NodeManagerCleanupConfirmationOutcomeUnknownException(
            NodeManagerCleanupFailureHandle handle,
            Exception innerException)
            : base("The cleanup confirmation outcome is unknown; rediscover the retained failure.", innerException)
        {
            Handle = handle;
        }

        /// <summary>
        /// Gets the affected failure handle, or the default handle for a manually created exception.
        /// </summary>
        public NodeManagerCleanupFailureHandle Handle { get; }
    }
}
