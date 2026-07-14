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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Sealed default implementation of <see cref="IPushConfigurationTransactionCoordinator"/>.
    /// </summary>
    /// <remarks>
    /// Instances are stateful and are intended to be shared by exactly one
    /// <see cref="ConfigurationNodeManager"/> (and the <see cref="TrustList"/>
    /// instances it creates). <see cref="ConfigurationNodeManager"/> creates
    /// a private instance by default; hosts that need to observe or
    /// customize transaction behavior can supply their own via
    /// dependency injection or direct construction.
    /// </remarks>
    public sealed class PushConfigurationTransactionCoordinator : IPushConfigurationTransactionCoordinator
    {
        /// <summary>
        /// Initializes a new coordinator.
        /// </summary>
        /// <param name="telemetry">Telemetry context used to create a logger.</param>
        /// <param name="timeProvider">
        /// Optional time provider for deterministic tests. Defaults to <see cref="TimeProvider.System"/>.
        /// </param>
        public PushConfigurationTransactionCoordinator(
            ITelemetryContext telemetry,
            TimeProvider? timeProvider = null)
        {
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            m_logger = telemetry.CreateLogger<PushConfigurationTransactionCoordinator>();
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_lastResult = StatusCodes.Good;
            m_lastAffectedCertificateGroups = ArrayOf<NodeId>.Empty;
            m_lastAffectedTrustLists = ArrayOf<NodeId>.Empty;
            m_lastErrors = ArrayOf<TransactionErrorType>.Empty;
        }

        /// <inheritdoc/>
        public NodeId OwnerSessionId
        {
            get
            {
                lock (m_lock)
                {
                    return m_isActive ? m_ownerSessionId : NodeId.Null;
                }
            }
        }

        /// <inheritdoc/>
        public bool IsTransactionActive
        {
            get
            {
                lock (m_lock)
                {
                    return m_isActive;
                }
            }
        }

        /// <inheritdoc/>
        public bool HasOpenTrustListWriter
        {
            get
            {
                lock (m_lock)
                {
                    return m_openTrustListWriters.Count > 0;
                }
            }
        }

        /// <inheritdoc/>
        public void ValidateSessionCanParticipate(NodeId sessionId)
        {
            lock (m_lock)
            {
                ThrowIfConflictingOwner(sessionId);
            }
        }

        /// <inheritdoc/>
        public void Stage(NodeId sessionId, PushConfigurationOperation operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            List<PushConfigurationOperation>? superseded = null;

            lock (m_lock)
            {
                ThrowIfConflictingOwner(sessionId);

                if (m_isCommitting)
                {
                    // ApplyChangesAsync already took every staged operation
                    // for this transaction and is committing/rolling them
                    // back. Ownership (m_isActive/m_ownerSessionId) is
                    // intentionally still held by the owning Session while
                    // that commit is in flight (see ApplyChangesAsync), so
                    // ThrowIfConflictingOwner above already rejected a
                    // different Session with BadTransactionPending. A
                    // same-owner Stage this soon after ApplyChangesAsync
                    // has already taken the staged operations must not
                    // silently add to a list that will never be committed
                    // and would otherwise still be present once
                    // ApplyChangesAsync's finally block releases ownership.
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "The active PushManagement configuration transaction is being committed.");
                }

                if (!m_isActive)
                {
                    m_isActive = true;
                    m_ownerSessionId = sessionId;
                    m_startTime = m_timeProvider.GetUtcNow().UtcDateTime;
                }

                if (!operation.AffectedCertificateType.IsNull)
                {
                    // A certificate group/type slot can only hold one
                    // staged outcome. A repeated request for the same slot
                    // (e.g. UpdateCertificate called twice, or
                    // CreateSelfSignedCertificate after a prior staged
                    // UpdateCertificate for the same type) supersedes the
                    // previous staging rather than accumulating two
                    // mutations against the same slot.
                    for (int i = m_operations.Count - 1; i >= 0; i--)
                    {
                        PushConfigurationOperation existing = m_operations[i];
                        if (Utils.IsEqual(existing.AffectedCertificateGroup, operation.AffectedCertificateGroup) &&
                            Utils.IsEqual(existing.AffectedCertificateType, operation.AffectedCertificateType))
                        {
                            m_operations.RemoveAt(i);
                            (superseded ??= []).Add(existing);
                        }
                    }
                }

                m_operations.Add(operation);
            }

            if (superseded != null)
            {
                foreach (PushConfigurationOperation existing in superseded)
                {
                    try
                    {
                        existing.DisposeStaged?.Invoke();
                    }
                    catch (Exception disposeException)
                    {
                        m_logger.LogWarning(
                            disposeException,
                            "Failed to release a superseded staged PushManagement operation for {CertificateGroup}.",
                            existing.AffectedCertificateGroup);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void SetTrustListWriteOpen(NodeId trustListId, bool isOpen)
        {
            lock (m_lock)
            {
                if (isOpen)
                {
                    m_openTrustListWriters.Add(trustListId);
                }
                else
                {
                    m_openTrustListWriters.Remove(trustListId);
                }
            }
        }

        /// <inheritdoc/>
        public ArrayOf<PushConfigurationOperation> GetStagedOperations()
        {
            lock (m_lock)
            {
                return m_operations.ToArrayOf();
            }
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> ApplyChangesAsync(
            NodeId sessionId,
            CancellationToken cancellationToken = default)
        {
            return ApplyChangesCoreAsync(sessionId, committedEffects: null, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> ApplyChangesAsync(
            NodeId sessionId,
            PushConfigurationApplyEffects committedEffects,
            CancellationToken cancellationToken = default)
        {
            if (committedEffects == null)
            {
                throw new ArgumentNullException(nameof(committedEffects));
            }

            return ApplyChangesCoreAsync(sessionId, committedEffects, cancellationToken);
        }

        private async ValueTask<ServiceResult> ApplyChangesCoreAsync(
            NodeId sessionId,
            PushConfigurationApplyEffects? committedEffects,
            CancellationToken cancellationToken)
        {
            List<PushConfigurationOperation> operations;
            DateTime startTime;

            lock (m_lock)
            {
                if (m_openTrustListWriters.Count > 0)
                {
                    // §7.10.9: ApplyChanges returns Bad_InvalidState if any
                    // TrustList is still open for writing. This precedence
                    // holds even when no transaction is active, so the caller
                    // learns to close the writer and retry rather than
                    // receiving BadNothingToDo. Do not change transaction
                    // state: the caller may retry once every open TrustList
                    // handle has been closed.
                    return StatusCodes.BadInvalidState;
                }

                if (!m_isActive)
                {
                    return StatusCodes.BadNothingToDo;
                }

                if (!Utils.IsEqual(m_ownerSessionId, sessionId))
                {
                    return StatusCodes.BadSessionIdInvalid;
                }

                if (m_isCommitting)
                {
                    // A previous ApplyChanges for this same owner is still
                    // committing (e.g. a concurrent request on the same
                    // Session). Do not start a second, overlapping commit.
                    return StatusCodes.BadInvalidState;
                }

                operations = m_operations;
                m_operations = [];
                startTime = m_startTime;

                // Ownership (m_isActive/m_ownerSessionId) is intentionally
                // NOT cleared here. Clearing it now would let a second
                // Session stage/apply against the store(s)/registry while
                // this commit's I/O is still in flight. Ownership is only
                // released, atomically with the diagnostics finalization,
                // once every staged operation has committed or been rolled
                // back, in the finally block below.
                m_isCommitting = true;
            }

            var affectedCertificateGroups = new List<NodeId>();
            var affectedTrustLists = new List<NodeId>();
            var errors = new List<TransactionErrorType>();
            ServiceResult result = ServiceResult.Good;
            int committedCount = 0;

            try
            {
                // §7.10.2: verify that all staged changes are consistent and
                // can be applied without errors before mutating anything. A
                // failing PrepareAsync (e.g. deleting a still-referenced
                // certificate, §7.10.7) discards the whole transaction without
                // committing any operation, so no rollback is needed.
                foreach (PushConfigurationOperation operation in operations)
                {
                    if (operation.PrepareAsync == null)
                    {
                        continue;
                    }

                    try
                    {
                        await operation.PrepareAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        result = new ServiceResult(ex);
                        errors.Add(new TransactionErrorType
                        {
                            TargetId = operation.AffectedCertificateGroup.IsNull
                                ? operation.AffectedTrustList
                                : operation.AffectedCertificateGroup,
                            Error = result.StatusCode,
                            Message = CreateClientDiagnosticMessage(ex)
                        });
                        break;
                    }
                }

                if (ServiceResult.IsGood(result))
                {
                    foreach (PushConfigurationOperation operation in operations)
                    {
                        try
                        {
                            await operation.CommitAsync(cancellationToken).ConfigureAwait(false);
                            committedCount++;
                            RecordAffected(operation, affectedCertificateGroups, affectedTrustLists);
                        }
                        catch (Exception ex)
                        {
                            result = new ServiceResult(ex);
                            errors.Add(new TransactionErrorType
                            {
                                TargetId = operation.AffectedCertificateGroup.IsNull
                                    ? operation.AffectedTrustList
                                    : operation.AffectedCertificateGroup,
                                Error = result.StatusCode,
                                Message = CreateClientDiagnosticMessage(ex)
                            });
                            break;
                        }
                    }
                }

                if (!ServiceResult.IsGood(result))
                {
                    // Compensate in reverse order so partial state is never
                    // reported as a successful ApplyChanges.
                    for (int i = committedCount - 1; i >= 0; i--)
                    {
                        PushConfigurationOperation operation = operations[i];
                        RecordAffected(operation, affectedCertificateGroups, affectedTrustLists);
                        if (operation.RollbackAsync == null)
                        {
                            continue;
                        }

                        try
                        {
                            await operation.RollbackAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception rollbackException)
                        {
                            m_logger.LogCritical(
                                rollbackException,
                                "Failed to roll back a staged PushManagement operation for {CertificateGroup}/{TrustList}. " +
                                "Server configuration may be inconsistent.",
                                operation.AffectedCertificateGroup,
                                operation.AffectedTrustList);
                            errors.Add(new TransactionErrorType
                            {
                                TargetId = operation.AffectedCertificateGroup.IsNull
                                    ? operation.AffectedTrustList
                                    : operation.AffectedCertificateGroup,
                                Error = new ServiceResult(rollbackException).StatusCode,
                                Message = CreateClientDiagnosticMessage(rollbackException)
                            });
                        }
                    }
                }
            }
            finally
            {
                foreach (PushConfigurationOperation operation in operations)
                {
                    try
                    {
                        operation.DisposeStaged?.Invoke();
                    }
                    catch (Exception disposeException)
                    {
                        m_logger.LogWarning(
                            disposeException,
                            "Failed to release staged PushManagement resources for {CertificateGroup}/{TrustList}.",
                            operation.AffectedCertificateGroup,
                            operation.AffectedTrustList);
                    }
                }

                lock (m_lock)
                {
                    m_lastStartTime = startTime;
                    m_lastEndTime = m_timeProvider.GetUtcNow().UtcDateTime;
                    m_lastResult = result.StatusCode;
                    m_lastAffectedCertificateGroups = affectedCertificateGroups.ToArrayOf();
                    m_lastAffectedTrustLists = affectedTrustLists.ToArrayOf();
                    m_lastErrors = errors.ToArrayOf();
                    m_hasCompletedTransaction = true;

                    // Release ownership only now: commit/rollback and
                    // diagnostics finalization are both complete, so a new
                    // Session may immediately stage/apply the next
                    // transaction without observing any intermediate state.
                    m_isActive = false;
                    m_isCommitting = false;
                    m_ownerSessionId = NodeId.Null;
                }
            }

            if (committedEffects != null)
            {
                // Report the exact certificate groups and TrustLists this
                // apply committed. Captured here, before returning, from the
                // operations this commit ran - never from a later
                // GetSnapshot() that may already reflect another Session's
                // transaction started once ownership was released above.
                committedEffects.CertificateGroups = affectedCertificateGroups.ToArrayOf();
                committedEffects.TrustLists = affectedTrustLists.ToArrayOf();
            }

            return result;
        }

        /// <inheritdoc/>
        public ServiceResult CancelChanges(NodeId sessionId)
        {
            List<PushConfigurationOperation> operations;
            DateTime startTime;

            lock (m_lock)
            {
                if (!m_isActive)
                {
                    return StatusCodes.BadNothingToDo;
                }

                if (!Utils.IsEqual(m_ownerSessionId, sessionId))
                {
                    return StatusCodes.BadSessionIdInvalid;
                }

                if (m_isCommitting)
                {
                    // ApplyChanges already took every staged operation for
                    // this transaction and is committing/rolling them back;
                    // there is nothing left here to cancel. Do not touch
                    // m_isActive/m_ownerSessionId: ApplyChangesAsync's
                    // finally block owns releasing them once the commit
                    // and diagnostics finalization complete.
                    return StatusCodes.BadInvalidState;
                }

                operations = m_operations;
                m_operations = [];
                startTime = m_startTime;
                m_isActive = false;
                m_ownerSessionId = NodeId.Null;
            }

            // OPC 10000-12 §7.10.17: TransactionDiagnostics.Result is
            // Bad_RequestCancelledByClient specifically when CancelChanges
            // was called (as opposed to e.g. a Session closing, which uses
            // the more generic BadRequestCancelledByRequest below).
            DiscardOperations(operations, startTime, StatusCodes.BadRequestCancelledByClient);
            return StatusCodes.Good;
        }

        /// <inheritdoc/>
        public void CancelForSessionClose(NodeId sessionId)
        {
            List<PushConfigurationOperation> operations;
            DateTime startTime;

            lock (m_lock)
            {
                if (!m_isActive || !Utils.IsEqual(m_ownerSessionId, sessionId))
                {
                    return;
                }

                if (m_isCommitting)
                {
                    // The owning Session is closing while its own
                    // ApplyChanges commit is still in flight (e.g. the
                    // Session timed out while awaiting store/registry I/O).
                    // Every staged operation was already taken by that
                    // commit, so there is nothing left to discard here.
                    // Leave m_isActive/m_ownerSessionId alone: clearing
                    // them now would let a third Session stage/apply
                    // before the in-flight commit and its diagnostics
                    // finalization complete.
                    return;
                }

                operations = m_operations;
                m_operations = [];
                startTime = m_startTime;
                m_isActive = false;
                m_ownerSessionId = NodeId.Null;
            }

            DiscardOperations(operations, startTime, StatusCodes.BadRequestCancelledByRequest);
        }

        /// <inheritdoc/>
        public void Reset()
        {
            List<PushConfigurationOperation> operations;
            DateTime startTime;

            lock (m_lock)
            {
                operations = m_operations;
                m_operations = [];
                startTime = m_startTime;
                m_isActive = false;
                m_isCommitting = false;
                m_ownerSessionId = NodeId.Null;
                m_openTrustListWriters.Clear();
            }

            if (operations.Count == 0)
            {
                return;
            }

            DiscardOperations(operations, startTime, StatusCodes.BadRequestCancelledByRequest);
        }

        /// <inheritdoc/>
        public PushConfigurationTransactionSnapshot GetSnapshot()
        {
            lock (m_lock)
            {
                PushConfigurationTransactionState state = m_isActive
                    ? PushConfigurationTransactionState.Active
                    : m_hasCompletedTransaction
                        ? PushConfigurationTransactionState.Completed
                        : PushConfigurationTransactionState.None;

                ArrayOf<NodeId> affectedCertificateGroups;
                ArrayOf<NodeId> affectedTrustLists;
                ArrayOf<TransactionErrorType> errors;
                if (m_isActive)
                {
                    // §7.10.17: AffectedCertificateGroups/AffectedTrustLists
                    // are updated as soon as a group/TrustList is added to the
                    // active transaction, so report the currently staged
                    // operations rather than the previous transaction's
                    // committed set (which is discarded when a new transaction
                    // starts). No errors are recorded until a commit runs.
                    CollectAffected(m_operations, out affectedCertificateGroups, out affectedTrustLists);
                    errors = ArrayOf<TransactionErrorType>.Empty;
                }
                else
                {
                    affectedCertificateGroups = m_lastAffectedCertificateGroups;
                    affectedTrustLists = m_lastAffectedTrustLists;
                    errors = m_lastErrors;
                }

                return new PushConfigurationTransactionSnapshot
                {
                    State = state,
                    IsActive = m_isActive,
                    OwnerSessionId = m_isActive ? m_ownerSessionId : NodeId.Null,
                    StartTime = m_isActive ? m_startTime : m_lastStartTime,
                    EndTime = m_isActive ? DateTime.MinValue : m_lastEndTime,
                    Result = m_isActive ? StatusCodes.Good : m_lastResult,
                    AffectedCertificateGroups = affectedCertificateGroups,
                    AffectedTrustLists = affectedTrustLists,
                    Errors = errors
                };
            }
        }

        /// <summary>
        /// Throws <see cref="StatusCodes.BadTransactionPending"/> when the
        /// active transaction is owned by a Session other than
        /// <paramref name="sessionId"/>. Must be called while holding
        /// <see cref="m_lock"/>.
        /// </summary>
        private void ThrowIfConflictingOwner(NodeId sessionId)
        {
            if (m_isActive && !Utils.IsEqual(m_ownerSessionId, sessionId))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTransactionPending,
                    "Another Session already owns the active PushManagement configuration transaction.");
            }
        }

        private static void RecordAffected(
            PushConfigurationOperation operation,
            List<NodeId> affectedCertificateGroups,
            List<NodeId> affectedTrustLists)
        {
            if (!operation.AffectedCertificateGroup.IsNull &&
                !affectedCertificateGroups.Contains(operation.AffectedCertificateGroup))
            {
                affectedCertificateGroups.Add(operation.AffectedCertificateGroup);
            }

            if (!operation.AffectedTrustList.IsNull &&
                !affectedTrustLists.Contains(operation.AffectedTrustList))
            {
                affectedTrustLists.Add(operation.AffectedTrustList);
            }
        }

        private static void CollectAffected(
            List<PushConfigurationOperation> operations,
            out ArrayOf<NodeId> affectedCertificateGroups,
            out ArrayOf<NodeId> affectedTrustLists)
        {
            var groups = new List<NodeId>();
            var trustLists = new List<NodeId>();
            foreach (PushConfigurationOperation operation in operations)
            {
                RecordAffected(operation, groups, trustLists);
            }

            affectedCertificateGroups = groups.ToArrayOf();
            affectedTrustLists = trustLists.ToArrayOf();
        }

        private void DiscardOperations(
            List<PushConfigurationOperation> operations,
            DateTime startTime,
            StatusCode result)
        {
            var affectedCertificateGroups = new List<NodeId>();
            var affectedTrustLists = new List<NodeId>();

            foreach (PushConfigurationOperation operation in operations)
            {
                RecordAffected(operation, affectedCertificateGroups, affectedTrustLists);
                try
                {
                    operation.DisposeStaged?.Invoke();
                }
                catch (Exception disposeException)
                {
                    m_logger.LogWarning(
                        disposeException,
                        "Failed to release staged PushManagement resources for {CertificateGroup}/{TrustList}.",
                        operation.AffectedCertificateGroup,
                        operation.AffectedTrustList);
                }
            }

            lock (m_lock)
            {
                m_lastStartTime = startTime;
                m_lastEndTime = m_timeProvider.GetUtcNow().UtcDateTime;
                m_lastResult = result;
                m_lastAffectedCertificateGroups = affectedCertificateGroups.ToArrayOf();
                m_lastAffectedTrustLists = affectedTrustLists.ToArrayOf();
                m_lastErrors = ArrayOf<TransactionErrorType>.Empty;
                m_hasCompletedTransaction = true;
            }
        }

        private static LocalizedText CreateClientDiagnosticMessage(Exception exception)
        {
            if (exception is ServiceResultException serviceResultException)
            {
                LocalizedText message = serviceResultException.Result.LocalizedText;
                if (!message.IsNull && !string.IsNullOrEmpty(message.Text))
                {
                    return message;
                }
            }

            return LocalizedText.From("PushManagement operation failed.");
        }

        private readonly Lock m_lock = new();
        private readonly ILogger m_logger;
        private readonly TimeProvider m_timeProvider;
        private bool m_isActive;
        private bool m_isCommitting;
        private bool m_hasCompletedTransaction;
        private NodeId m_ownerSessionId = NodeId.Null;
        private DateTime m_startTime;
        private List<PushConfigurationOperation> m_operations = [];
        private readonly HashSet<NodeId> m_openTrustListWriters = [];
        private DateTime m_lastStartTime;
        private DateTime m_lastEndTime;
        private StatusCode m_lastResult;
        private ArrayOf<NodeId> m_lastAffectedCertificateGroups;
        private ArrayOf<NodeId> m_lastAffectedTrustLists;
        private ArrayOf<TransactionErrorType> m_lastErrors;
    }
}
