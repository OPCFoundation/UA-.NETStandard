// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Enforces host authorization, exact-resource verification, and auditing for cleanup recovery.
    /// </summary>
    public sealed class NodeManagerCleanupRecovery : INodeManagerCleanupRecovery
    {
        /// <summary>
        /// Binds the trusted recovery adapters and creates a service for a directly constructed server.
        /// </summary>
        /// <remarks>
        /// Construct this service before starting or exposing the server. Once bound, a different set of
        /// adapters cannot be installed; starting an unconfigured server disables later recovery configuration.
        /// </remarks>
        /// <param name="server">The server retaining the NodeManager generations.</param>
        /// <param name="authorization">The trusted host authorization policy.</param>
        /// <param name="verifier">The trusted exact-resource cleanup verifier.</param>
        /// <param name="audit">The trusted host audit adapter.</param>
        public NodeManagerCleanupRecovery(
            StandardServer server,
            INodeManagerCleanupAuthorization authorization,
            INodeManagerCleanupVerifier verifier,
            INodeManagerCleanupAudit audit)
        {
            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            if (server.NodeManagerLifecycle is not NodeManagerLifecycle lifecycle)
            {
                throw new InvalidOperationException("The server does not expose a compatible lifecycle owner.");
            }
            m_authority = new NodeManagerCleanupAuthority(
                authorization ?? throw new ArgumentNullException(nameof(authorization)),
                verifier ?? throw new ArgumentNullException(nameof(verifier)),
                audit ?? throw new ArgumentNullException(nameof(audit)));
            lifecycle.BindCleanupAuthority(m_authority);
            m_getOwners = () => [lifecycle];
            m_resume = async owner =>
            {
                if (!owner.TryRetryCleanupAfterConfirmation())
                {
                    await server.DisposeAsync().ConfigureAwait(false);
                }
            };
        }

        /// <summary>
        /// Creates a recovery service for host-managed lifecycle owners.
        /// </summary>
        internal NodeManagerCleanupRecovery(
            Func<NodeManagerLifecycle[]> getOwners,
            Func<NodeManagerLifecycle, ValueTask> resume,
            NodeManagerCleanupAuthority authority)
        {
            m_getOwners = getOwners;
            m_resume = resume;
            m_authority = authority;
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<RetainedNodeManagerCleanupFailure>> DiscoverAsync(
            CancellationToken cancellationToken = default)
        {
            NodeManagerCleanupDiscoveryResult result = await DiscoverDetailedAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!result.Issues.IsEmpty)
            {
                foreach (NodeManagerCleanupDiscoveryIssue issue in result.Issues)
                {
                    if (issue.Kind == NodeManagerCleanupDiscoveryIssueKind.GenerationUnavailable)
                    {
                        throw new InvalidOperationException(
                            "The NodeManager generation is currently being cleaned up.");
                    }
                }
                throw new InvalidOperationException(
                    "Cleanup discovery was incomplete; use detailed discovery to inspect the available results.");
            }
            return result.Failures;
        }

        /// <inheritdoc/>
        public async ValueTask<NodeManagerCleanupDiscoveryResult> DiscoverDetailedAsync(
            CancellationToken cancellationToken = default)
        {
            var requestId = Guid.NewGuid();
            INodeManagerCleanupAudit audit = RequireAudit();
            NodeManagerCleanupAuthorization authorization = await AuthorizeAsync(
                audit, requestId, NodeManagerCleanupOperation.Discover, default, cancellationToken)
                .ConfigureAwait(false);
            await RecordAsync(
                audit, requestId, NodeManagerCleanupOperation.Discover, NodeManagerCleanupAuditOutcome.Requested,
                default, authorization.AuditUserId, cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var failures = new List<RetainedNodeManagerCleanupFailure>();
                var issues = new List<NodeManagerCleanupDiscoveryIssue>();
                foreach (NodeManagerGeneration generation in EnumerateCleanupGenerations())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool entered = generation.CleanupRecoveryGate.TryEnter(out GenerationGate.Lease? lease);
                    using (lease)
                    {
                        if (!entered || lease is null)
                        {
                            issues.Add(new NodeManagerCleanupDiscoveryIssue(
                                generation.CleanupRecoveryId, Guid.Empty,
                                NodeManagerCleanupDiscoveryIssueKind.GenerationUnavailable));
                            continue;
                        }
                        CaptureDiscovery(generation, failures, issues, cancellationToken);
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                await RecordAsync(
                    audit, requestId, NodeManagerCleanupOperation.Discover,
                    issues.Count == 0
                        ? NodeManagerCleanupAuditOutcome.Succeeded
                        : NodeManagerCleanupAuditOutcome.Partial,
                    default, authorization.AuditUserId, CancellationToken.None).ConfigureAwait(false);
                return new NodeManagerCleanupDiscoveryResult(
                    new ArrayOf<RetainedNodeManagerCleanupFailure>(failures.ToArray()),
                    new ArrayOf<NodeManagerCleanupDiscoveryIssue>(issues.ToArray()));
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                await RecordFailureAsync(
                    audit, requestId, NodeManagerCleanupOperation.Discover, NodeManagerCleanupAuditOutcome.Failed,
                    default, authorization.AuditUserId, failure).ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<NodeManagerCleanupFailureHandle>> DiscoverPendingConfirmationsAsync(
            CancellationToken cancellationToken = default)
        {
            var requestId = Guid.NewGuid();
            INodeManagerCleanupAudit audit = RequireAudit();
            NodeManagerCleanupAuthorization authorization = await AuthorizeAsync(
                audit, requestId, NodeManagerCleanupOperation.DiscoverPendingConfirmations, default, cancellationToken)
                .ConfigureAwait(false);
            await RecordAsync(
                audit, requestId, NodeManagerCleanupOperation.DiscoverPendingConfirmations,
                NodeManagerCleanupAuditOutcome.Requested, default, authorization.AuditUserId, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var confirmations = new List<NodeManagerCleanupFailureHandle>();
                foreach (NodeManagerGeneration generation in EnumerateCleanupGenerations())
                {
                    using GenerationGate.Lease lease = EnterRecovery(generation);
                    confirmations.AddRange(generation.SnapshotCleanupConfirmations());
                }
                await RecordAsync(
                    audit, requestId, NodeManagerCleanupOperation.DiscoverPendingConfirmations,
                    NodeManagerCleanupAuditOutcome.Succeeded, default, authorization.AuditUserId,
                    CancellationToken.None).ConfigureAwait(false);
                return new ArrayOf<NodeManagerCleanupFailureHandle>(confirmations.ToArray());
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                await RecordFailureAsync(
                    audit, requestId, NodeManagerCleanupOperation.DiscoverPendingConfirmations,
                    NodeManagerCleanupAuditOutcome.Failed, default, authorization.AuditUserId, failure)
                    .ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc/>
        public ValueTask ConfirmAsync(
            NodeManagerCleanupFailureHandle handle,
            CancellationToken cancellationToken = default)
        {
            return ConfirmCoreAsync(handle, null, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask ConfirmAsync(
            NodeManagerCleanupFailureHandle handle,
            NodeManagerCleanupVerificationContext verificationContext,
            CancellationToken cancellationToken)
        {
            return ConfirmCoreAsync(
                handle, verificationContext ?? throw new ArgumentNullException(nameof(verificationContext)),
                cancellationToken);
        }

        private async ValueTask ConfirmCoreAsync(
            NodeManagerCleanupFailureHandle handle,
            NodeManagerCleanupVerificationContext? verificationContext,
            CancellationToken cancellationToken)
        {
            var requestId = Guid.NewGuid();
            INodeManagerCleanupAudit audit = RequireAudit();
            NodeManagerCleanupAuthorization authorization = await AuthorizeAsync(
                audit, requestId, NodeManagerCleanupOperation.Confirm, handle, cancellationToken)
                .ConfigureAwait(false);
            await RecordAsync(
                audit, requestId, NodeManagerCleanupOperation.Confirm, NodeManagerCleanupAuditOutcome.Requested,
                handle, authorization.AuditUserId, cancellationToken).ConfigureAwait(false);

            CapturedFailure? captured = null;
            bool admitted = false;
            try
            {
                NodeManagerCleanupAuditOutcome failureOutcome = NodeManagerCleanupAuditOutcome.Failed;
                ConfirmationObservation confirmation;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    verificationContext?.RejectReentry(handle);
                    INodeManagerCleanupVerifier verifier = m_authority.Verifier
                        ?? throw new InvalidOperationException("A trusted cleanup verifier is not configured.");
                    bool verified;
                    var context = new NodeManagerCleanupVerificationContext(handle.GenerationId, verificationContext);
                    try
                    {
                        captured = CaptureResource(handle, context);
                        NodeManagerCleanupResource resource = captured.TakeResource();
                        try
                        {
                            verified = await verifier.VerifyAsync(
                                captured.Description, resource, context, cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            resource = default;
                        }
                    }
                    finally
                    {
                        context.Revoke();
                        captured?.ReleaseVerification();
                    }
                    if (!verified)
                    {
                        failureOutcome = NodeManagerCleanupAuditOutcome.NotVerified;
                        throw new InvalidOperationException("Actual cleanup of the retained resource was not verified.");
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    await RecordAsync(
                        audit, requestId, NodeManagerCleanupOperation.Confirm,
                        NodeManagerCleanupAuditOutcome.VerificationSucceeded, handle, authorization.AuditUserId,
                        cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!captured.Generation.TryBeginCleanupRecovery())
                    {
                        throw new InvalidOperationException(
                            "Another cleanup recovery or resumption is active for this generation.");
                    }
                    admitted = true;
                    await captured.Generation.WaitForCleanupVerificationsAsync()
                        .WaitAsync(cancellationToken).ConfigureAwait(false);
                    confirmation = ConfirmCapturedFailure(captured);
                    captured.ReleaseRecoveryLease();
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    if (failure is NodeManagerCleanupConfirmationOutcomeUnknownException)
                    {
                        failureOutcome = NodeManagerCleanupAuditOutcome.ConfirmationOutcomeUnknown;
                    }
                    await RecordFailureAsync(
                        audit, requestId, NodeManagerCleanupOperation.Confirm, failureOutcome,
                        handle, authorization.AuditUserId, failure).ConfigureAwait(false);
                    throw;
                }

                var postConfirmationFailures = new List<Exception>();
                if (confirmation.ParticipantFailure is { } participantFailure)
                {
                    postConfirmationFailures.Add(participantFailure);
                }
                if (confirmation.InspectionFailure is { } inspectionFailure)
                {
                    postConfirmationFailures.Add(inspectionFailure);
                }
                bool outcomeRecorded = false;
                try
                {
                    await RecordAsync(
                        audit, requestId, NodeManagerCleanupOperation.Confirm,
                        postConfirmationFailures.Count == 0
                            ? NodeManagerCleanupAuditOutcome.Succeeded
                            : NodeManagerCleanupAuditOutcome.ConfirmationAppliedWithPostFailure,
                        handle, authorization.AuditUserId, CancellationToken.None).ConfigureAwait(false);
                    outcomeRecorded = true;
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    postConfirmationFailures.Add(failure);
                }

                if (outcomeRecorded && confirmation.InspectionFailure is null)
                {
                    if (captured.Generation.OfferCleanupConfirmation(handle, confirmation.RemainingFailures))
                    {
                        try
                        {
                            await captured.Owner.ResumePrivateCleanupAfterConfirmationAsync(captured.Generation)
                                .ConfigureAwait(false);
                            await m_resume(captured.Owner).ConfigureAwait(false);
                        }
                        catch (Exception failure) when (failure is not OutOfMemoryException)
                        {
                            captured.Generation.WithdrawCleanupConfirmationOffer(handle);
                            postConfirmationFailures.Add(failure);
                            try
                            {
                                await RecordAsync(
                                    audit, requestId, NodeManagerCleanupOperation.Confirm,
                                    NodeManagerCleanupAuditOutcome.ResumptionFailed, handle, authorization.AuditUserId,
                                    CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (Exception auditFailure) when (auditFailure is not OutOfMemoryException)
                            {
                                postConfirmationFailures.Add(auditFailure);
                            }
                        }
                    }
                }
                if (postConfirmationFailures.Count != 0)
                {
                    throw new NodeManagerCleanupPostConfirmationException(
                        handle,
                        postConfirmationFailures.Count == 1
                            ? postConfirmationFailures[0]
                            : new AggregateException(postConfirmationFailures));
                }
            }
            finally
            {
                try
                {
                    if (admitted)
                    {
                        captured!.Generation.EndCleanupRecovery();
                    }
                }
                finally
                {
                    captured?.Dispose();
                    captured = null;
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask ReconcileAsync(
            NodeManagerCleanupFailureHandle handle,
            CancellationToken cancellationToken = default)
        {
            return ReconcileCoreAsync(handle, null, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask ReconcileAsync(
            NodeManagerCleanupFailureHandle handle,
            NodeManagerCleanupVerificationContext verificationContext,
            CancellationToken cancellationToken)
        {
            return ReconcileCoreAsync(
                handle, verificationContext ?? throw new ArgumentNullException(nameof(verificationContext)),
                cancellationToken);
        }

        private async ValueTask ReconcileCoreAsync(
            NodeManagerCleanupFailureHandle handle,
            NodeManagerCleanupVerificationContext? verificationContext,
            CancellationToken cancellationToken)
        {
            var requestId = Guid.NewGuid();
            INodeManagerCleanupAudit audit = RequireAudit();
            NodeManagerCleanupAuthorization authorization = await AuthorizeAsync(
                audit, requestId, NodeManagerCleanupOperation.Reconcile, handle, cancellationToken)
                .ConfigureAwait(false);
            await RecordAsync(
                audit, requestId, NodeManagerCleanupOperation.Reconcile, NodeManagerCleanupAuditOutcome.Requested,
                handle, authorization.AuditUserId, cancellationToken).ConfigureAwait(false);

            NodeManagerLifecycle? owner;
            NodeManagerGeneration? generation = null;
            bool admitted = false;
            try
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    verificationContext?.RejectReentry(handle);
                    (owner, generation) = FindRecoveryGeneration(handle);
                    if (!generation.TryBeginCleanupRecovery())
                    {
                        throw new InvalidOperationException(
                            "Another cleanup recovery or resumption is active for this generation.");
                    }
                    admitted = true;
                    await generation.WaitForCleanupVerificationsAsync()
                        .WaitAsync(cancellationToken).ConfigureAwait(false);
                    InspectPendingConfirmation(owner, generation, handle);
                    cancellationToken.ThrowIfCancellationRequested();
                    await RecordAsync(
                        audit, requestId, NodeManagerCleanupOperation.Reconcile, NodeManagerCleanupAuditOutcome.Succeeded,
                        handle, authorization.AuditUserId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    await RecordFailureAsync(
                        audit, requestId, NodeManagerCleanupOperation.Reconcile, NodeManagerCleanupAuditOutcome.Failed,
                        handle, authorization.AuditUserId, failure).ConfigureAwait(false);
                    throw;
                }

                if (!generation.OfferCleanupConfirmation(handle, otherFailures: false))
                {
                    return;
                }
                try
                {
                    await owner.ResumePrivateCleanupAfterConfirmationAsync(generation).ConfigureAwait(false);
                    await m_resume(owner).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    generation.WithdrawCleanupConfirmationOffer(handle);
                    Exception reportedFailure = failure;
                    try
                    {
                        await RecordFailureAsync(
                            audit, requestId, NodeManagerCleanupOperation.Reconcile,
                            NodeManagerCleanupAuditOutcome.ResumptionFailed, handle, authorization.AuditUserId, failure)
                            .ConfigureAwait(false);
                    }
                    catch (Exception auditFailure) when (auditFailure is not OutOfMemoryException)
                    {
                        reportedFailure = auditFailure;
                    }
                    throw new NodeManagerCleanupPostConfirmationException(handle, reportedFailure);
                }
            }
            finally
            {
                if (admitted)
                {
                    generation!.EndCleanupRecovery();
                }
                owner = null;
                generation = null;
            }
        }

        /// <summary>
        /// Enumerates each exact generation once after checking its bound cleanup authority.
        /// </summary>
        /// <returns>The generations currently retained by the authorized host.</returns>
        private IEnumerable<NodeManagerGeneration> EnumerateCleanupGenerations()
        {
            var seen = new HashSet<Guid>();
            foreach (NodeManagerLifecycle owner in m_getOwners())
            {
                owner.DemandCleanupAuthority(m_authority);
                foreach (NodeManagerGeneration generation in owner.SnapshotCleanupGenerations())
                {
                    if (seen.Add(generation.CleanupRecoveryId))
                    {
                        yield return generation;
                    }
                }
            }
        }

        /// <summary>
        /// Authorizes the current host caller and records denied requests.
        /// </summary>
        /// <exception cref="UnauthorizedAccessException">
        /// No authorization policy is configured, or the policy denies recovery or supplies an empty,
        /// whitespace-only or longer-than-256-character audit user identifier.
        /// </exception>
        private async ValueTask<NodeManagerCleanupAuthorization> AuthorizeAsync(
            INodeManagerCleanupAudit audit,
            Guid requestId,
            NodeManagerCleanupOperation operation,
            NodeManagerCleanupFailureHandle handle,
            CancellationToken cancellationToken)
        {
            if (m_authority.Authorization is not { } policy)
            {
                await RecordAsync(
                    audit, requestId, operation, NodeManagerCleanupAuditOutcome.Denied, handle, null,
                    CancellationToken.None).ConfigureAwait(false);
                throw new UnauthorizedAccessException("A cleanup recovery authorization policy is not configured.");
            }
            NodeManagerCleanupAuthorization authorization;
            try
            {
                authorization = await policy.AuthorizeAsync(operation, handle, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                await RecordFailureAsync(
                    audit, requestId, operation, NodeManagerCleanupAuditOutcome.Failed, handle, null, failure)
                    .ConfigureAwait(false);
                throw;
            }
            if (!authorization.IsAuthorized ||
                string.IsNullOrWhiteSpace(authorization.AuditUserId) ||
                authorization.AuditUserId.Length > 256)
            {
                await RecordAsync(
                    audit, requestId, operation, NodeManagerCleanupAuditOutcome.Denied, handle, null,
                    CancellationToken.None).ConfigureAwait(false);
                throw new UnauthorizedAccessException("The host did not authorize cleanup recovery.");
            }
            return authorization;
        }

        /// <summary>
        /// Resolves the resource only while its exact generation admits recovery operations.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The handle is invalid or no longer retained, recovery is already active, or the participant
        /// does not consume verification or return a retained resource.
        /// </exception>
        // The returned disposable owns the lease; disposing it here would allow cleanup during verification.
        // TODO: Remove this exception when CA2000 recognizes the returned owner's lease transfer.
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "The successful path transfers the lease to CapturedFailure.Dispose; finally owns failures.")]
        private CapturedFailure CaptureResource(
            NodeManagerCleanupFailureHandle handle,
            NodeManagerCleanupVerificationContext context)
        {
            if (handle.GenerationId == Guid.Empty || handle.ParticipantId == Guid.Empty || handle.FailureId == Guid.Empty)
            {
                throw new InvalidOperationException("The cleanup failure handle is invalid.");
            }
            foreach (NodeManagerLifecycle owner in m_getOwners())
            {
                owner.DemandCleanupAuthority(m_authority);
                foreach (NodeManagerGeneration generation in owner.SnapshotCleanupGenerations())
                {
                    if (generation.CleanupRecoveryId != handle.GenerationId)
                    {
                        continue;
                    }
                    GenerationGate.Lease? lease = null;
                    bool verificationAdmitted = false;
                    try
                    {
                        lease = EnterRecovery(generation);
                        if (!generation.TryBeginCleanupVerification())
                        {
                            throw new InvalidOperationException(
                                "Another cleanup recovery or resumption is active for this generation.");
                        }
                        verificationAdmitted = true;
                        INodeManagerCleanupParticipant participant = FindParticipant(generation, handle.ParticipantId);
                        NodeManagerCleanupFailure failure = FindFailure(participant, handle.FailureId, context);
                        var verification = new NodeManagerCleanupVerification(participant, failure.Id, context);
                        NodeManagerCleanupResource resource;
                        try
                        {
                            resource = participant.GetCleanupResource(verification);
                        }
                        finally
                        {
                            verification.Revoke();
                        }
                        if (!verification.IsConsumed)
                        {
                            throw new InvalidOperationException("The participant did not consume verification.");
                        }
                        if (resource.IsEmpty)
                        {
                            throw new InvalidOperationException("The participant returned no retained resource.");
                        }
                        var captured = new CapturedFailure(
                            owner, generation, participant, CreateDescription(generation, participant, failure),
                            resource, lease);
                        lease = null;
                        verificationAdmitted = false;
                        return captured;
                    }
                    finally
                    {
                        lease?.Dispose();
                        if (verificationAdmitted)
                        {
                            generation.EndCleanupVerification();
                        }
                    }
                }
            }
            throw new InvalidOperationException("The cleanup failure is no longer retained by this host.");
        }

        /// <summary>
        /// Finds the lifecycle and exact generation named by an operator handle.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The handle contains an empty generation, participant or failure identifier,
        /// or its generation is no longer retained by this host.
        /// </exception>
        private (NodeManagerLifecycle Owner, NodeManagerGeneration Generation) FindRecoveryGeneration(
            NodeManagerCleanupFailureHandle handle)
        {
            if (handle.GenerationId == Guid.Empty || handle.ParticipantId == Guid.Empty || handle.FailureId == Guid.Empty)
            {
                throw new InvalidOperationException("The cleanup failure handle is invalid.");
            }
            foreach (NodeManagerLifecycle owner in m_getOwners())
            {
                owner.DemandCleanupAuthority(m_authority);
                foreach (NodeManagerGeneration generation in owner.SnapshotCleanupGenerations())
                {
                    if (generation.CleanupRecoveryId == handle.GenerationId)
                    {
                        return (owner, generation);
                    }
                }
            }
            throw new InvalidOperationException("The cleanup generation is no longer retained by this host.");
        }

        /// <summary>
        /// Proves that an earlier verified confirmation has cleared and no other failure remains.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The generation is no longer retained, no verified confirmation awaits reconciliation,
        /// or the confirmed failure or another cleanup failure is still retained.
        /// </exception>
        private void InspectPendingConfirmation(
            NodeManagerLifecycle owner,
            NodeManagerGeneration generation,
            NodeManagerCleanupFailureHandle handle)
        {
            owner.DemandCleanupAuthority(m_authority);
            using GenerationGate.Lease lease = EnterRecovery(generation);
            if (!m_getOwners().Any(current => ReferenceEquals(current, owner)) ||
                !owner.SnapshotCleanupGenerations().Any(current => ReferenceEquals(current, generation)))
            {
                throw new InvalidOperationException("The cleanup generation is no longer retained by this host.");
            }
            if (!generation.HasCleanupConfirmation(handle))
            {
                throw new InvalidOperationException("No verified confirmation is awaiting reconciliation.");
            }
            INodeManagerCleanupParticipant participant = FindParticipant(generation, handle.ParticipantId);
            ArrayOf<NodeManagerCleanupFailure> participantFailures = CaptureFailures(participant);
            foreach (NodeManagerCleanupFailure failure in participantFailures)
            {
                if (failure.Id == handle.FailureId)
                {
                    generation.ForgetCleanupConfirmation(handle);
                    throw new InvalidOperationException(
                        "The failure is still retained; confirm it again after verifying cleanup.");
                }
            }
            if (!participantFailures.IsEmpty)
            {
                throw new InvalidOperationException("Other cleanup failures are still retained.");
            }
            foreach (INodeManagerCleanupParticipant current in GetParticipants(generation))
            {
                if (!ReferenceEquals(current, participant) && !CaptureFailures(current).IsEmpty)
                {
                    throw new InvalidOperationException("Other cleanup failures are still retained.");
                }
            }
        }

        /// <summary>
        /// Revalidates the exact failure and applies the one-use confirmation after verification.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Recovery admission or the captured generation, participant or failure is no longer current,
        /// or the participant does not consume confirmation or clear the confirmed failure.
        /// </exception>
        /// <exception cref="NodeManagerCleanupConfirmationOutcomeUnknownException">
        /// The participant consumed confirmation, but its retained-failure snapshot could not be inspected.
        /// </exception>
        private ConfirmationObservation ConfirmCapturedFailure(CapturedFailure captured)
        {
            captured.Owner.DemandCleanupAuthority(m_authority);
            if (captured.RecoveryLease is null)
            {
                throw new InvalidOperationException("The cleanup recovery admission is no longer held.");
            }
            if (!m_getOwners().Any(owner => ReferenceEquals(owner, captured.Owner)) ||
                !captured.Owner.SnapshotCleanupGenerations().Any(
                    generation => ReferenceEquals(generation, captured.Generation)))
            {
                throw new InvalidOperationException("The cleanup generation is no longer retained by this host.");
            }
            NodeManagerCleanupFailureHandle handle = captured.Description.Handle;
            INodeManagerCleanupParticipant participant = FindParticipant(captured.Generation, handle.ParticipantId);
            if (!ReferenceEquals(participant, captured.Participant))
            {
                throw new InvalidOperationException("The cleanup participant has changed.");
            }
            _ = FindFailure(participant, handle.FailureId);
            captured.Generation.ForgetCleanupConfirmation(handle);
            var confirmation = new NodeManagerCleanupConfirmation(participant, handle.FailureId);
            try
            {
                Exception? participantFailure = null;
                try
                {
                    participant.ConfirmCleanup(confirmation);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    participantFailure = failure;
                }
                finally
                {
                    confirmation.Revoke();
                    if (confirmation.IsConsumed)
                    {
                        captured.Generation.RememberCleanupConfirmation(handle);
                    }
                }
                if (!confirmation.IsConsumed)
                {
                    if (participantFailure is not null)
                    {
                        ExceptionDispatchInfo.Capture(participantFailure).Throw();
                    }
                    throw new InvalidOperationException("The participant did not consume confirmation.");
                }

                ArrayOf<NodeManagerCleanupFailure> participantFailures;
                try
                {
                    participantFailures = CaptureFailures(participant);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    throw new NodeManagerCleanupConfirmationOutcomeUnknownException(
                        handle,
                        participantFailure is null
                            ? failure
                            : new AggregateException(participantFailure, failure));
                }
                foreach (NodeManagerCleanupFailure failure in participantFailures)
                {
                    if (failure.Id == handle.FailureId)
                    {
                        captured.Generation.ForgetCleanupConfirmation(handle);
                        if (participantFailure is not null)
                        {
                            ExceptionDispatchInfo.Capture(participantFailure).Throw();
                        }
                        throw new InvalidOperationException("The participant did not clear the confirmed failure.");
                    }
                }

                bool remainingFailures = !participantFailures.IsEmpty;
                try
                {
                    foreach (INodeManagerCleanupParticipant current in GetParticipants(captured.Generation))
                    {
                        if (!ReferenceEquals(current, participant) && !CaptureFailures(current).IsEmpty)
                        {
                            remainingFailures = true;
                        }
                    }
                    return new ConfirmationObservation(remainingFailures, participantFailure, null);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    return new ConfirmationObservation(remainingFailures, participantFailure, failure);
                }
            }
            finally
            {
                confirmation.Revoke();
            }
        }

        /// <summary>
        /// Retains the exact generation during recovery operations that use participant-owned state.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The generation's cleanup recovery gate does not admit a lease.
        /// </exception>
        private static GenerationGate.Lease EnterRecovery(NodeManagerGeneration generation)
        {
            if (!generation.CleanupRecoveryGate.TryEnter(out GenerationGate.Lease? lease) || lease is null)
            {
                throw new InvalidOperationException("The NodeManager generation is currently being cleaned up.");
            }
            return lease;
        }

        /// <summary>
        /// Prevents physical cleanup while any participant still retains an unresolved resource failure.
        /// </summary>
        /// <param name="generation">The generation whose recovery admission has been closed and drained.</param>
        /// <exception cref="InvalidOperationException">
        /// A cleanup participant still reports an unresolved resource failure.
        /// </exception>
        internal static void EnsureNoRetainedFailures(NodeManagerGeneration generation)
        {
            if (HasRetainedFailures(generation))
            {
                throw new InvalidOperationException("The NodeManager retains unresolved cleanup failures.");
            }
        }

        /// <summary>
        /// Checks participant snapshots while exclusive cleanup admission is held.
        /// </summary>
        /// <param name="generation">The generation whose recovery admission has been closed and drained.</param>
        /// <returns>Whether a participant retains an unresolved resource failure.</returns>
        internal static bool HasRetainedFailures(NodeManagerGeneration generation)
        {
            foreach (INodeManagerCleanupParticipant participant in GetParticipants(generation))
            {
                if (!CaptureFailures(participant).IsEmpty)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Retains an exact manager whose private abort failed before its disposal could safely start.
        /// Confirmation requires verified cleanup of the whole manager, not merely its deletion callback.
        /// </summary>
        /// <param name="manager">The exact manager whose private abort failed.</param>
        /// <param name="generation">The generation retaining the failed abort.</param>
        /// <param name="failure">The original abort failure.</param>
        /// <param name="onConfirmed">
        /// Marks only abort and manager disposal complete after verified full cleanup.
        /// </param>
        internal static void RetainPrivateAbortFailure(
            IAsyncNodeManager manager,
            NodeManagerGeneration generation,
            Exception failure,
            Action onConfirmed)
        {
            NodeManagerCleanupResource resource = ResolveManagerDisposalResource(manager);
            generation.RetainPrivateAbortFailure(resource, failure, onConfirmed);
        }

        /// <summary>
        /// Attempts manager disposal once and retains a failed exact resource for verified recovery.
        /// </summary>
        /// <param name="manager">The manager being disposed.</param>
        /// <param name="generation">The generation retaining a failed disposal.</param>
        /// <param name="onCompleted">Marks the disposal stage complete after disposal or verified cleanup.</param>
        /// <returns>The disposal operation.</returns>
        internal static async ValueTask DisposeManagerAsync(
            IAsyncNodeManager manager,
            NodeManagerGeneration generation,
            Action onCompleted)
        {
            NodeManagerCleanupResource resource = ResolveManagerDisposalResource(manager);
            if (manager is IAsyncDisposable asyncDisposable)
            {
                NodeManagerGeneration.RetiredConsumer attempt = generation.BeginManagerCleanup(
                    resource, onCompleted);
                try
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    onCompleted();
                }
                catch (Exception failure)
                {
                    generation.CompleteManagerCleanup(attempt, failure);
                    throw;
                }
                generation.CompleteManagerCleanup(attempt, null);
            }
            else if (manager is IDisposable disposable)
            {
                NodeManagerGeneration.RetiredConsumer attempt = generation.BeginManagerCleanup(
                    resource, onCompleted);
                try
                {
                    disposable.Dispose();
                    onCompleted();
                }
                catch (Exception failure)
                {
                    generation.CompleteManagerCleanup(attempt, failure);
                    throw;
                }
                generation.CompleteManagerCleanup(attempt, null);
            }
            else
            {
                onCompleted();
            }
        }

        /// <summary>
        /// Identifies the exact disposal target of a transparent stack adapter without unwrapping custom facades.
        /// </summary>
        private static NodeManagerCleanupResource ResolveManagerDisposalResource(IAsyncNodeManager manager)
        {
            while (NodeManagerGenerationRegistry.IsStandardAdapter(manager))
            {
                // TODO: Remove this disposal bridge when the legacy adapter is removed.
#pragma warning disable CS0618, UA0031 // Unwrap only the legacy stack adapter, not custom facades.
                var adapter = (AsyncNodeManagerAdapter)manager;
#pragma warning restore CS0618, UA0031
                if (adapter.SyncNodeManager is SyncNodeManagerAdapter synchronousAdapter)
                {
                    IAsyncNodeManager wrapped = synchronousAdapter.AsyncNodeManager;
                    if (wrapped is IAsyncDisposable)
                    {
                        manager = wrapped;
                        continue;
                    }
                    if (wrapped is IDisposable wrappedDisposable)
                    {
                        return NodeManagerCleanupResource.FromDisposable(wrappedDisposable);
                    }
                    break;
                }
                if (adapter.SyncNodeManager is IAsyncDisposable asynchronous)
                {
                    return NodeManagerCleanupResource.FromAsyncDisposable(asynchronous);
                }
                if (adapter.SyncNodeManager is IDisposable synchronous)
                {
                    return NodeManagerCleanupResource.FromDisposable(synchronous);
                }
                break;
            }
            if (manager is IAsyncDisposable asyncDisposable)
            {
                return NodeManagerCleanupResource.FromAsyncDisposable(asyncDisposable);
            }
            return manager is IDisposable disposable
                ? NodeManagerCleanupResource.FromDisposable(disposable)
                : default;
        }

        /// <summary>
        /// Finds the cleanup participants of the facade and its underlying manager.
        /// </summary>
        private static List<INodeManagerCleanupParticipant> GetParticipants(NodeManagerGeneration generation)
        {
            var participants = new List<INodeManagerCleanupParticipant>();
            if (generation.NodeManager is INodeManagerCleanupParticipant facade)
            {
                participants.Add(facade);
            }
            IAsyncNodeManager resource = NodeManagerGenerationRegistry.GetIdentity(generation.NodeManager);
            if (resource is INodeManagerCleanupParticipant asyncResource &&
                !participants.Any(item => ReferenceEquals(item, asyncResource)))
            {
                participants.Add(asyncResource);
            }
            if (resource.SyncNodeManager is INodeManagerCleanupParticipant syncResource &&
                !participants.Any(item => ReferenceEquals(item, syncResource)))
            {
                participants.Add(syncResource);
            }
            participants.Add(generation);
            return participants;
        }

        /// <summary>
        /// Captures independent participants even if another participant cannot be inspected.
        /// </summary>
        private static void CaptureDiscovery(
            NodeManagerGeneration generation,
            List<RetainedNodeManagerCleanupFailure> failures,
            List<NodeManagerCleanupDiscoveryIssue> issues,
            CancellationToken cancellationToken)
        {
            var participants = new List<INodeManagerCleanupParticipant>();
            if (generation.NodeManager is INodeManagerCleanupParticipant facade)
            {
                participants.Add(facade);
            }
            try
            {
                IAsyncNodeManager resource = NodeManagerGenerationRegistry.GetIdentity(generation.NodeManager);
                if (resource is INodeManagerCleanupParticipant asyncResource &&
                    !participants.Any(item => ReferenceEquals(item, asyncResource)))
                {
                    participants.Add(asyncResource);
                }
                if (resource.SyncNodeManager is INodeManagerCleanupParticipant syncResource &&
                    !participants.Any(item => ReferenceEquals(item, syncResource)))
                {
                    participants.Add(syncResource);
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                issues.Add(new NodeManagerCleanupDiscoveryIssue(
                    generation.CleanupRecoveryId, Guid.Empty,
                    NodeManagerCleanupDiscoveryIssueKind.ParticipantUnavailable));
            }
            participants.Add(generation);

            foreach (INodeManagerCleanupParticipant participant in participants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Guid participantId = generation.GetCleanupParticipantId(participant);
                try
                {
                    foreach (NodeManagerCleanupFailure failure in CaptureFailures(participant))
                    {
                        failures.Add(CreateDescription(generation, participant, failure));
                    }
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    issues.Add(new NodeManagerCleanupDiscoveryIssue(
                        generation.CleanupRecoveryId, participantId,
                        NodeManagerCleanupDiscoveryIssueKind.FailureSnapshotUnavailable));
                }
            }
        }

        /// <summary>
        /// Finds the exact participant addressed by a generation-scoped handle.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// No participant retained by the generation has the requested participant identifier.
        /// </exception>
        private static INodeManagerCleanupParticipant FindParticipant(NodeManagerGeneration generation, Guid id)
        {
            if (generation.GetCleanupParticipantId(generation) == id)
            {
                return generation;
            }
            if (generation.NodeManager is INodeManagerCleanupParticipant facade &&
                generation.GetCleanupParticipantId(facade) == id)
            {
                return facade;
            }
            IAsyncNodeManager resource = NodeManagerGenerationRegistry.GetIdentity(generation.NodeManager);
            if (resource is INodeManagerCleanupParticipant asyncResource &&
                generation.GetCleanupParticipantId(asyncResource) == id)
            {
                return asyncResource;
            }
            if (resource.SyncNodeManager is INodeManagerCleanupParticipant syncResource &&
                generation.GetCleanupParticipantId(syncResource) == id)
            {
                return syncResource;
            }
            throw new InvalidOperationException("The cleanup participant is no longer retained.");
        }

        /// <summary>
        /// Captures and validates a detached snapshot of a participant's retained failure descriptions.
        /// </summary>
        /// <param name="participant">The participant that owns the failure registry.</param>
        /// <param name="context">The enclosing verification, if discovery is resolving its exact failure.</param>
        /// <returns>The validated descriptions in host-owned storage.</returns>
        /// <exception cref="InvalidOperationException">
        /// The participant returns a null snapshot, a null failure entry or duplicate failure identifiers.
        /// </exception>
        internal static ArrayOf<NodeManagerCleanupFailure> CaptureFailures(
            INodeManagerCleanupParticipant participant,
            NodeManagerCleanupVerificationContext? context = null)
        {
            var access = new NodeManagerCleanupAccess(participant, context);
            ArrayOf<NodeManagerCleanupFailure> failures;
            try
            {
                failures = participant.GetCleanupFailures(access);
            }
            finally
            {
                access.Revoke();
            }
            if (failures.IsNull)
            {
                throw new InvalidOperationException("The cleanup participant returned a null failure snapshot.");
            }
            NodeManagerCleanupFailure[] snapshot = failures.Span.ToArray();
            var ids = new HashSet<Guid>();
            foreach (NodeManagerCleanupFailure failure in snapshot)
            {
                if (failure is null || !ids.Add(failure.Id))
                {
                    throw new InvalidOperationException("The cleanup participant returned invalid failure IDs.");
                }
            }
            return new ArrayOf<NodeManagerCleanupFailure>(snapshot);
        }

        /// <summary>
        /// Finds one exact, currently retained participant failure.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The participant's retained-failure snapshot contains no failure with the requested identifier.
        /// </exception>
        private static NodeManagerCleanupFailure FindFailure(
            INodeManagerCleanupParticipant participant,
            Guid id,
            NodeManagerCleanupVerificationContext? context = null)
        {
            foreach (NodeManagerCleanupFailure failure in CaptureFailures(participant, context))
            {
                if (failure.Id == id)
                {
                    return failure;
                }
            }
            throw new InvalidOperationException("The cleanup failure is no longer retained.");
        }

        /// <summary>
        /// Adds the exact host-owned generation and participant identities to a provider description.
        /// </summary>
        private static RetainedNodeManagerCleanupFailure CreateDescription(
            NodeManagerGeneration generation,
            INodeManagerCleanupParticipant participant,
            NodeManagerCleanupFailure failure)
        {
            var handle = new NodeManagerCleanupFailureHandle(
                generation.CleanupRecoveryId,
                generation.GetCleanupParticipantId(participant),
                failure.Id);
            return new RetainedNodeManagerCleanupFailure(
                handle, generation.Slot.Id, generation.Number, SanitizeFailureDescription(failure));
        }

        /// <summary>
        /// Restricts participant metadata to known resource kinds and a numeric status.
        /// </summary>
        /// <param name="failure">The participant-owned description.</param>
        /// <returns>A description safe to expose through host recovery operations.</returns>
        internal static NodeManagerCleanupFailure SanitizeFailureDescription(NodeManagerCleanupFailure failure)
        {
            string kind = failure.ResourceKind switch
            {
                "DirectoryCursor" => "DirectoryCursor",
                "CompanionResource" => "CompanionResource",
                "HistoryContinuation" => "HistoryContinuation",
                "NodeManager" => "NodeManager",
                "RetiredConsumer" => "RetiredConsumer",
                _ => "Resource"
            };
            var statusCode = new StatusCode(failure.StatusCode.CodeBits, string.Empty);
            return new NodeManagerCleanupFailure(failure.Id, kind, null, statusCode);
        }

        /// <summary>
        /// Requires an audit adapter before exposing failures or applying confirmation.
        /// </summary>
        /// <exception cref="InvalidOperationException">No cleanup recovery audit adapter is configured.</exception>
        private INodeManagerCleanupAudit RequireAudit()
        {
            return m_authority.Audit
                ?? throw new InvalidOperationException("A cleanup recovery audit adapter is not configured.");
        }

        /// <summary>
        /// Records a single recovery audit event.
        /// </summary>
        private static ValueTask RecordAsync(
            INodeManagerCleanupAudit audit,
            Guid requestId,
            NodeManagerCleanupOperation operation,
            NodeManagerCleanupAuditOutcome outcome,
            NodeManagerCleanupFailureHandle handle,
            string? auditUserId,
            CancellationToken cancellationToken)
        {
            return audit.RecordAsync(
                new NodeManagerCleanupAuditRecord(requestId, operation, outcome, handle, auditUserId),
                cancellationToken);
        }

        /// <summary>
        /// Records a failed operation without hiding either the original failure or an audit failure.
        /// </summary>
        /// <exception cref="AggregateException">
        /// Recording the supplied operation failure through the audit adapter also fails.
        /// </exception>
        private static async ValueTask RecordFailureAsync(
            INodeManagerCleanupAudit audit,
            Guid requestId,
            NodeManagerCleanupOperation operation,
            NodeManagerCleanupAuditOutcome outcome,
            NodeManagerCleanupFailureHandle handle,
            string? auditUserId,
            Exception failure)
        {
            try
            {
                await RecordAsync(
                    audit, requestId, operation, outcome, handle, auditUserId, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception auditFailure) when (auditFailure is not OutOfMemoryException)
            {
                throw new AggregateException("Cleanup recovery and recording its failure both failed.",
                    failure, auditFailure);
            }
        }

        /// <summary>
        /// Holds one exact resource only for the duration of a trusted verification request.
        /// </summary>
        private sealed class CapturedFailure : IDisposable
        {
            /// <summary>
            /// Creates the exact target and its temporary resource reference.
            /// </summary>
            internal CapturedFailure(
                NodeManagerLifecycle owner,
                NodeManagerGeneration generation,
                INodeManagerCleanupParticipant participant,
                RetainedNodeManagerCleanupFailure description,
                NodeManagerCleanupResource resource,
                GenerationGate.Lease recoveryLease)
            {
                Owner = owner;
                Generation = generation;
                Participant = participant;
                Description = description;
                m_resource = resource;
                m_verificationLease = recoveryLease;
            }

            /// <summary>
            /// Gets the owner retaining the generation.
            /// </summary>
            internal NodeManagerLifecycle Owner { get; }

            /// <summary>
            /// Gets the exact generation.
            /// </summary>
            internal NodeManagerGeneration Generation { get; }

            /// <summary>
            /// Gets the exact participant.
            /// </summary>
            internal INodeManagerCleanupParticipant Participant { get; }

            /// <summary>
            /// Gets the authorized failure description.
            /// </summary>
            internal RetainedNodeManagerCleanupFailure Description { get; }

            /// <summary>
            /// Gets the recovery admission retained through confirmation.
            /// </summary>
            internal GenerationGate.Lease? RecoveryLease => Volatile.Read(ref m_verificationLease);

            /// <inheritdoc/>
            public void Dispose()
            {
                try
                {
                    ReleaseVerification();
                }
                finally
                {
                    m_resource = default;
                    ReleaseRecoveryLease();
                }
            }

            /// <summary>
            /// Transfers the temporary resource reference to the verifier.
            /// </summary>
            /// <exception cref="InvalidOperationException">
            /// The captured resource has already been taken or released.
            /// </exception>
            internal NodeManagerCleanupResource TakeResource()
            {
                NodeManagerCleanupResource resource = m_resource;
                if (resource.IsEmpty)
                {
                    throw new InvalidOperationException("The resource was already captured for verification.");
                }
                m_resource = default;
                return resource;
            }

            /// <summary>
            /// Releases verification accounting after the verifier has stopped using the resource.
            /// </summary>
            internal void ReleaseVerification()
            {
                if (Interlocked.Exchange(ref m_verificationActive, 0) != 0)
                {
                    Generation.EndCleanupVerification();
                }
            }

            /// <summary>
            /// Releases generation admission after confirmation has been observed or abandoned.
            /// </summary>
            internal void ReleaseRecoveryLease()
            {
                Interlocked.Exchange(ref m_verificationLease, null)?.Dispose();
            }

            private NodeManagerCleanupResource m_resource;
            private GenerationGate.Lease? m_verificationLease;
            private int m_verificationActive = 1;
        }

        /// <summary>
        /// Captures the observed registry state after confirmation was consumed.
        /// </summary>
        /// <param name="RemainingFailures">Whether the inspected participants retain another failure.</param>
        /// <param name="ParticipantFailure">A failure reported after the participant consumed confirmation.</param>
        /// <param name="InspectionFailure">A failure inspecting the other participants.</param>
        private readonly record struct ConfirmationObservation(
            bool RemainingFailures,
            Exception? ParticipantFailure,
            Exception? InspectionFailure);

        private readonly Func<NodeManagerLifecycle[]> m_getOwners;
        private readonly Func<NodeManagerLifecycle, ValueTask> m_resume;
        private readonly NodeManagerCleanupAuthority m_authority;
    }
}
