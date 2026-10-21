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
    public sealed partial class AsyncServiceExecutionProvider
    {
        /// <summary>
        /// Restores protected state, current interpretation and existing isolation charges before advertising Durable.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask InitializeDurableAsync(CancellationToken ct)
        {
            using IDisposable activity = EnterExchange();
            ct.ThrowIfCancellationRequested();
            if (!m_options.RecoveryModes.Contains(ServiceOperationRecoveryMode.Durable))
            {
                return;
            }
            if (Interlocked.Exchange(ref m_initializing, 1) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            try
            {
                ServiceMessageContext context = TransportChannelBinding.CopyContext(m_server.MessageContext);
                context.MaxMessageSize = m_maxMessageSize;
                m_durableTypes = await m_typeCompatibility.CaptureAsync(context, ct).ConfigureAwait(false);
                m_journal = new RecoverableExecutionJournal(
                    m_durableStore!, m_recordProtector!, m_durableClock!, m_ownerProvider!,
                    m_logicalServerId!, m_durableTypes.MessageContext, m_durableTypes.Witness);
                _ = await m_journal.LoadAsync(m_maxRestoredOperations, RestoreOperationAsync, ct)
                    .ConfigureAwait(false);
                lock (m_gate)
                {
                    ThrowIfStopping();
                    foreach (Operation operation in m_byToken.Values)
                    {
                        ArmTimer(operation);
                    }
                }
            }
            catch (Exception exception)
            {
                WithdrawDurableReadiness(null, exception);
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable, exception);
            }
        }

        /// <summary>
        /// Reconstitutes one operation without creating an invocation context or dispatching its saved request.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask RestoreOperationAsync(RecoverableExecutionJournal.Entry entry, CancellationToken ct)
        {
            if (entry.Forgotten)
            {
                RecordDurableTransitions(entry.InitialState, entry.InitialOutcome,
                    entry.InitialCancellationRequested, previouslyForgotten: false, entry.Current, forgotten: true);
                FlushAudits();
                entry.Dispose();
                return;
            }
            RecoverableExecutionDurableRecord record = entry.Current;
            if (!SupportedServices.Contains(record.ServiceId))
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            RecoverableExecutionTypeContext types = await RestoreDurableTypesAsync(record.MessageLimit, ct)
                .ConfigureAwait(false);
            ValidateRestoredPayloads(record, types);
            ResourceIsolationOwner owner =
                ((IRecoverableExecutionResourceIsolationProvider)m_isolation!)
                    .ClassifyRecoverableOwner(record.Owner.Id);
            Reservations? reservations = Reserve(
                owner, record.Roles, record.Owner, record.MessageLimit, ServiceOperationRecoveryMode.Durable,
                input: !record.IsReleased, outcome: !record.Response.IsNull,
                execution: !record.WorkFinished, journalPayload: !record.IsReleased);
            RecoverableExecutionRecoveredWork? recovered = null;
            Operation? operation = null;
            bool indexed = false;
            try
            {
                if (!record.WorkFinished)
                {
                    recovered = await ((IRecoverableExecutionDurableEligibilityProvider)m_eligibilityProvider!)
                        .RecoverAsync(record.Owner, record.Incarnation, ct).ConfigureAwait(false) ??
                        throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                    if (recovered.TerminationEstablished)
                    {
                        await entry.ConfirmTerminationAsync().ConfigureAwait(false);
                        await recovered.DisposeAsync().ConfigureAwait(false);
                        recovered = null;
                        record = entry.Current;
                    }
                }
                if (entry.Forgotten)
                {
                    RecordDurableTransitions(entry.InitialState, entry.InitialOutcome,
                        entry.InitialCancellationRequested, previouslyForgotten: false, entry.Current, forgotten: true);
                    FlushAudits();
                    entry.Dispose();
                    return;
                }
                operation = new Operation(this, entry, record, types, reservations)
                {
                    RecoveredWork = recovered
                };
                recovered = null;
                RecoverableExecutionTime now = await m_journal!.GetTimeAsync(ct).ConfigureAwait(false);
                lock (m_gate)
                {
                    ThrowIfStopping();
                    if (m_byClient.ContainsKey(operation.Key) || m_byToken.ContainsKey(operation.Token))
                    {
                        throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                    }
                    m_byClient.Add(operation.Key, operation);
                    m_byToken.Add(operation.Token, operation);
                    operation.Indexed = true;
                    operation.References++;
                    indexed = true;
                    ApplyDurableRecord(operation, record, now, entry.Forgotten);
                    reservations = null;
                }
                Cleanup(operation);
            }
            finally
            {
                if (!indexed && operation != null)
                {
                    await operation.DisposeUnadmittedAsync().ConfigureAwait(false);
                    if (operation.RecoveredWork != null)
                    {
                        await operation.RecoveredWork.DisposeAsync().ConfigureAwait(false);
                    }
                }
                reservations?.Dispose();
                if (recovered != null)
                {
                    await recovered.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Verifies complete request/response interpretation before retaining any persisted original payload.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void ValidateRestoredPayloads(
            RecoverableExecutionDurableRecord record, RecoverableExecutionTypeContext types)
        {
            if (record.IsReleased)
            {
                return;
            }
            IServiceRequest request = types.Decode<IServiceRequest>(record.Request);
            IServiceResponse? response = null;
            try
            {
                if (request.RequestHeader == null ||
                    ExpandedNodeId.ToNodeId(request.TypeId, types.MessageContext.NamespaceUris) != record.ServiceId)
                {
                    throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                }
                if (record.Response.IsNull)
                {
                    return;
                }
                response = types.Decode<IServiceResponse>(record.Response);
                if (response.ResponseHeader == null)
                {
                    throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                }
                bool matched = (request, response) switch
                {
                    (_, ServiceFault) => StatusCode.IsBad(response.ResponseHeader.ServiceResult),
                    (ReadRequest, ReadResponse) or (WriteRequest, WriteResponse) or (CallRequest, CallResponse) or
                    (HistoryReadRequest, HistoryReadResponse) or (HistoryUpdateRequest, HistoryUpdateResponse) or
                    (BrowseRequest, BrowseResponse) or (BrowseNextRequest, BrowseNextResponse) or
                    (TranslateBrowsePathsToNodeIdsRequest, TranslateBrowsePathsToNodeIdsResponse) or
                    (RegisterNodesRequest, RegisterNodesResponse) or
                    (UnregisterNodesRequest, UnregisterNodesResponse) or
                    (AddNodesRequest, AddNodesResponse) or (AddReferencesRequest, AddReferencesResponse) or
                    (DeleteNodesRequest, DeleteNodesResponse) or (DeleteReferencesRequest, DeleteReferencesResponse)
                        => true,
                    _ => false
                };
                if (!matched ||
                    response.ResponseHeader.RequestHandle != request.RequestHeader.RequestHandle ||
                    response.ResponseHeader.ServiceResult != record.Status)
                {
                    throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                }
            }
            finally
            {
                (response as IPooledEncodeable)?.Reuse();
                (request as IPooledEncodeable)?.Reuse();
            }
        }

        /// <summary>
        /// Rejects lost qualification rather than converting an unresolved durable lookup into Unknown.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void RequireDurableReady()
        {
            if (m_journal?.IsReady != true)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
        }

        /// <summary>
        /// Prevents cached readiness from turning unavailable durable state into a successful Unknown lookup.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask VerifyDurableReadinessAsync(CancellationToken ct)
        {
            RequireDurableReady();
            try
            {
                _ = await RestoreDurableTypesAsync(m_maxMessageSize, ct).ConfigureAwait(false);
                await m_journal!.VerifyReadinessAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                WithdrawDurableReadiness(null, exception);
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable, exception);
            }
        }

        /// <summary>
        /// Rehashes the live factory and URI tables instead of trusting a previously captured factory reference.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<RecoverableExecutionTypeContext> RestoreDurableTypesAsync(
            int limit, CancellationToken ct)
        {
            try
            {
                RecoverableExecutionTypeContext captured = m_durableTypes ??
                    throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                ServiceMessageContext current = TransportChannelBinding.CopyContext(m_server.MessageContext);
                current.MaxMessageSize = Math.Min(limit, m_maxMessageSize);
                return await m_typeCompatibility.RestoreAsync(current, captured.Witness, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                WithdrawDurableReadiness(null, exception);
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable, exception);
            }
        }

        /// <summary>
        /// Commits admission and its post-commit E origin before scheduling any original invocation.
        /// </summary>
        private async ValueTask AdmitDurableAsync(Operation operation, CancellationToken ct)
        {
            RequireDurableReady();
            var record = new RecoverableExecutionDurableRecord
            {
                Owner = operation.Owner!,
                ClientId = operation.ClientId,
                Token = operation.Token,
                Incarnation = operation.ExecutionId,
                IssuingSessionId = operation.IssuingSessionId,
                ServiceId = operation.ServiceId,
                TypeWitness = operation.Types!.Witness,
                Request = operation.Input!.Encoded,
                DiagnosticsMask = operation.Input.DiagnosticsMask,
                Roles = operation.Roles,
                MessageLimit = operation.Limit,
                Mapping = operation.Mapping,
                Execution = operation.Execution,
                Retention = operation.Retention
            };
            operation.JournalEntry = await m_journal!.AdmitAsync(
                record, () => operation.AdmissionWriteStarted = true, ct).ConfigureAwait(false);
            await RefreshDurableAsync(operation, CancellationToken.None).ConfigureAwait(false);
            lock (m_gate)
            {
                StartAdmittedOperation(operation);
            }
        }

        /// <summary>
        /// Authorizes one original dispatch durably, with elapsed-time expiry applied before returning permission.
        /// </summary>
        private async ValueTask<bool> BeginDurableDispatchAsync(Operation operation)
        {
            RequireDurableReady();
            _ = await RestoreDurableTypesAsync(operation.Limit, CancellationToken.None).ConfigureAwait(false);
            bool dispatch = await operation.JournalEntry!.BeginDispatchAsync().ConfigureAwait(false);
            await RefreshDurableAsync(operation, CancellationToken.None).ConfigureAwait(false);
            return dispatch;
        }

        /// <summary>
        /// Persists the candidate and serialized post-write E decision before publishing its unchanged R deadline.
        /// </summary>
        private async ValueTask CommitDurableOutcomeAsync(
            Operation operation, ServiceOperationOutcome kind, StatusCode status, byte[]? response)
        {
            RequireDurableReady();
            _ = await RestoreDurableTypesAsync(operation.Limit, CancellationToken.None).ConfigureAwait(false);
            await operation.JournalEntry!.CommitOutcomeAsync(
                kind, status, response == null ? default : new ByteString(response), operation.Input!.Permissions,
                workFinished: false)
                .ConfigureAwait(false);
            await RefreshDurableAsync(operation, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Applies durable elapsed-time transitions and then mirrors only fully committed state into the shared engine.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask RefreshDurableAsync(Operation operation, CancellationToken ct)
        {
            if (operation.Mode != ServiceOperationRecoveryMode.Durable)
            {
                return;
            }
            RequireDurableReady();
            _ = await RestoreDurableTypesAsync(operation.Limit, ct).ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = operation.JournalEntry ??
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            (RecoverableExecutionDurableRecord record, RecoverableExecutionTime now) =
                await entry.ObserveAsync(ct).ConfigureAwait(false);
            lock (m_gate)
            {
                ApplyDurableRecord(operation, record, now, entry.Forgotten);
                ArmTimer(operation);
            }
            Cleanup(operation);
        }

        /// <summary>
        /// Publishes a monotonic durable revision and transfers retained bytes into the existing delivery ownership.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void ApplyDurableRecord(
            Operation operation, RecoverableExecutionDurableRecord record, RecoverableExecutionTime now, bool forgotten)
        {
            if (operation.DurableRecord != null && record.Revision < operation.DurableRecord.Revision)
            {
                return;
            }
            if (record.Phase is RecoverableExecutionCommitPhase.Admission or
                RecoverableExecutionCommitPhase.OutcomePreparing)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            ServiceOperationState previousState = operation.State;
            ServiceOperationOutcome previousKind = operation.Kind;
            bool previousCancellation = operation.CancellationRequested;
            bool previouslyForgotten = operation.Forgotten;
            if (operation.DurableRecord != null &&
                previousState == ServiceOperationState.OutcomeAvailable &&
                record.State != ServiceOperationState.OutcomeAvailable)
            {
                m_retainedBytes -= (ulong)operation.Reservations.HistoryCharge;
            }
            else if ((operation.DurableRecord == null || previousState != ServiceOperationState.OutcomeAvailable) &&
                record.State == ServiceOperationState.OutcomeAvailable)
            {
                m_retainedBytes += (ulong)operation.Reservations.HistoryCharge;
            }
            operation.DurableRecord = record;
            if (operation.DurableNow.DomainId == Uuid.Empty || now.Ticks >= operation.DurableNow.Ticks)
            {
                operation.DurableNow = now;
            }
            operation.State = record.State;
            operation.Kind = record.Kind;
            operation.Status = record.Status;
            operation.CancellationRequested = record.CancellationRequested;
            operation.WorkFinished |= record.WorkFinished;
            operation.Forgotten |= forgotten || record.IsHistoryExpired(now);
            operation.Input?.Permissions = record.Permissions;
            if (record.State == ServiceOperationState.OutcomeAvailable &&
                !record.Response.IsNull &&
                operation.Outcome == null)
            {
                operation.Outcome = new OutcomeSnapshot(
                    record.Response.Span.ToArray(), operation.Encoding, operation.Reservations.OutcomeBytes!,
                    () => ReleaseOutcomeBytes(operation.Limit), operation.Types);
                operation.Reservations.OutcomeBytes = null;
                m_retainedBytes += (ulong)operation.Limit;
            }
            RecordDurableTransitions(previousState, previousKind, previousCancellation, previouslyForgotten,
                record, operation.Forgotten);
            if (record.CancellationRequested)
            {
                RequestCancellation(operation);
            }
            if (record.State != ServiceOperationState.Pending)
            {
                operation.Changed.TrySetResult(true);
            }
        }

        /// <summary>
        /// Counts and audits only newly committed transitions, independently of reconstructed current-state gauges.
        /// </summary>
        private void RecordDurableTransitions(
            ServiceOperationState previousState, ServiceOperationOutcome previousKind, bool previousCancellation,
            bool previouslyForgotten, RecoverableExecutionDurableRecord record, bool forgotten)
        {
            lock (m_gate)
            {
                if (previousKind == ServiceOperationOutcome.None && record.Kind != ServiceOperationOutcome.None)
                {
                    RecordAudit(record.ClientId, record.ServiceId, ServiceOperationAuditAction.OutcomeCommitted,
                        ServiceOperationState.OutcomeAvailable, record.Kind, record.Status, succeeded: true,
                        record.IssuingSessionId, ServiceOperationRecoveryMode.Durable);
                    if (record.Kind == ServiceOperationOutcome.ExecutionDeadlineExceeded)
                    {
                        Increment(ref m_deadlines);
                    }
                    else if (record.Kind == ServiceOperationOutcome.ExecutionIndeterminate)
                    {
                        Increment(ref m_indeterminate);
                    }
                }
                ServiceOperationAuditAction? release = null;
                if (previousState != record.State)
                {
                    switch (record.State)
                    {
                        case ServiceOperationState.Acknowledged:
                            Increment(ref m_acknowledged);
                            release = ServiceOperationAuditAction.Acknowledged;
                            break;
                        case ServiceOperationState.Abandoned:
                            Increment(ref m_abandoned);
                            release = ServiceOperationAuditAction.Abandoned;
                            break;
                        case ServiceOperationState.Expired:
                            Increment(ref m_expired);
                            release = ServiceOperationAuditAction.Expired;
                            break;
                    }
                }
                if (release.HasValue)
                {
                    RecordAudit(record.ClientId, record.ServiceId, release.Value,
                        record.State, record.Kind, record.Status, succeeded: true,
                        record.IssuingSessionId, ServiceOperationRecoveryMode.Durable);
                }
                if (!previousCancellation && record.CancellationRequested)
                {
                    RecordAudit(record.ClientId, record.ServiceId, ServiceOperationAuditAction.CancellationRequested,
                        record.State, record.Kind, record.Status, succeeded: true,
                        record.IssuingSessionId, ServiceOperationRecoveryMode.Durable);
                }
                if (!previouslyForgotten && forgotten)
                {
                    RecordAudit(record.ClientId, record.ServiceId, ServiceOperationAuditAction.HistoryForgotten,
                        record.State, record.Kind, record.Status, succeeded: true,
                        record.IssuingSessionId, ServiceOperationRecoveryMode.Durable);
                }
            }
        }

        /// <summary>
        /// Keeps the initial Submit budget at the first durable admission commit, including later write latency.
        /// </summary>
        private async Task WaitDurableAsync(
            Operation operation, TimeSpan wait, RequestLifetime lifetime, bool submitted, CancellationToken ct)
        {
            await RefreshDurableAsync(operation, ct).ConfigureAwait(false);
            RecoverableExecutionTime origin;
            lock (m_gate)
            {
                if (wait == TimeSpan.Zero || operation.State != ServiceOperationState.Pending || operation.Forgotten)
                {
                    return;
                }
                origin = submitted ? operation.DurableRecord!.AdmittedAt : operation.DurableNow;
            }
            RecoverableExecutionTime now = await m_journal!.GetTimeAsync(ct).ConfigureAwait(false);
            wait -= origin.ElapsedUntil(now);
            if (wait <= TimeSpan.Zero)
            {
                return;
            }
            lifetime.ParkSink?.NotifyParked();
            using CancellationTokenSource expiry = m_clock.CreateCancellationTokenSource(wait);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, expiry.Token, m_shutdown.Token);
            try
            {
                await operation.Changed.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                expiry.IsCancellationRequested && !ct.IsCancellationRequested && !m_shutdown.IsCancellationRequested)
            {
                // The wrapper budget ends without changing the admitted invocation.
            }
        }

        /// <summary>
        /// Commits a named action before reporting its disposition, retaining the selected control reply across expiry.
        /// </summary>
        private async ValueTask<ControlResponseDataType> ControlDurableAsync(
            OperationContext context, Operation operation, AsyncServiceAccess access, CancellationToken ct)
        {
            await RefreshDurableAsync(operation, ct).ConfigureAwait(false);
            ValidateCurrentIdentity(context);
            ServiceOperationControlResult result = await operation.JournalEntry!.ControlAsync(access, ct)
                .ConfigureAwait(false);
            await RefreshDurableAsync(operation, ct).ConfigureAwait(false);
            ValidateOwner(context, operation,
                await ResolveOwnerAsync(context, operation.Mode, ct).ConfigureAwait(false));
            lock (m_gate)
            {
                return new ControlResponseDataType
                {
                    Result = result,
                    Operation = operation.Forgotten &&
                        result is not (
                            ServiceOperationControlResult.Applied or ServiceOperationControlResult.AlreadyApplied)
                            ? Unknown(default) : Snapshot(operation)
                };
            }
        }

        /// <summary>
        /// Arms the next recorded qualified deadline; the callback rechecks time and never resets an origin.
        /// </summary>
        private static void ArmDurableTimer(Operation operation)
        {
            RecoverableExecutionDurableRecord? record = operation.DurableRecord;
            if (record == null)
            {
                return;
            }
            RecoverableExecutionTime now = operation.DurableNow;
            TimeSpan due = Timeout.InfiniteTimeSpan;
            if (!record.WorkFinished &&
                (record.State == ServiceOperationState.Pending || !record.CancellationRequested))
            {
                due = record.Execution - record.AdmittedAt.ElapsedUntil(now);
            }
            if (record.State == ServiceOperationState.OutcomeAvailable)
            {
                TimeSpan retention = record.Retention - record.OutcomeAt.ElapsedUntil(now);
                due = due == Timeout.InfiniteTimeSpan || retention < due ? retention : due;
            }
            else if (record.IsReleased && !operation.Forgotten)
            {
                TimeSpan history = record.Retention - record.ReleasedAt.ElapsedUntil(now);
                due = due == Timeout.InfiniteTimeSpan || history < due ? history : due;
            }
            operation.Timer?.Change(due == Timeout.InfiniteTimeSpan ? due : TimeSpan.FromTicks(Math.Max(0, due.Ticks)),
                Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Keeps at most one asynchronous maintenance transition per operation, tracked through shutdown.
        /// </summary>
        private void StartDurableMaintenance(Operation operation)
        {
            if (operation.MaintenancePending || m_journal?.IsReady != true)
            {
                return;
            }
            operation.MaintenancePending = true;
            OperationLease? lease = null;
            ActivityLease? activity = null;
            try
            {
                lease = RetainOperation(operation);
                activity = TrackActivity();
                OperationLease ownedLease = lease;
                ActivityLease ownedActivity = activity;
                using (ExecutionContext.SuppressFlow())
                {
                    operation.Maintenance = Task.Run(async () =>
                    {
                        using (ownedActivity)
                        using (ownedLease)
                        {
                            try
                            {
                                await RefreshDurableAsync(operation, CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (Exception exception)
                            {
                                WithdrawDurableReadiness(operation, exception);
                            }
                            finally
                            {
                                lock (m_gate)
                                {
                                    operation.MaintenancePending = false;
                                    if (m_journal?.IsReady == true)
                                    {
                                        ArmTimer(operation);
                                    }
                                }
                            }
                        }
                    }, CancellationToken.None);
                }
                lease = null;
                activity = null;
            }
            finally
            {
                lease?.Dispose();
                activity?.Dispose();
            }
        }

        /// <summary>
        /// Withdraws only qualified Durable service and wakes waits without publishing an invented outcome.
        /// </summary>
        private void WithdrawDurableReadiness(Operation? operation, Exception exception)
        {
            m_journal?.Invalidate();
            m_logger.RecoverableDurabilityUnavailable(
                operation?.ClientId ?? Uuid.Empty,
                exception is ServiceResultException failure ? failure.StatusCode : StatusCodes.BadUnexpectedError);
            lock (m_gate)
            {
                foreach (Operation pending in m_byToken.Values)
                {
                    if (pending.Mode == ServiceOperationRecoveryMode.Durable)
                    {
                        pending.Changed.TrySetResult(true);
                        pending.Timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    }
                }
            }
        }

        private readonly int m_maxRestoredOperations;
        private RecoverableExecutionTypeContext? m_durableTypes;
        private RecoverableExecutionJournal? m_journal;
        private int m_initializing;
        private ulong m_indeterminate;
    }
}
