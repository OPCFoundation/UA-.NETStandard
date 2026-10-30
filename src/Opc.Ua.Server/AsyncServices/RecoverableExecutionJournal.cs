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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Commits protected operation transitions through the existing shared store and a qualified restart clock.
    /// No method in this module dispatches, resumes or reconstructs an original invocation.
    /// </summary>
    internal sealed class RecoverableExecutionJournal : IDisposable
    {
        /// <summary>
        /// Borrows storage, protection, ownership and time providers; the Server owns the journal itself.
        /// </summary>
        public RecoverableExecutionJournal(
            ISharedKeyValueStore store,
            IRecordProtector protector,
            IRecoverableExecutionClock clock,
            IRecoverableExecutionOwnerProvider owners,
            string logicalServerId,
            IServiceMessageContext encoding,
            ByteString typeWitness)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_protector = protector ?? throw new ArgumentNullException(nameof(protector));
            m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
            m_owners = owners ?? throw new ArgumentNullException(nameof(owners));
            if (protector is NullRecordProtector ||
                store is not ISharedKeyValueStoreDurability durability ||
                store is not ISharedKeyValueStoreConsistency consistency ||
                !Uri.TryCreate(logicalServerId, UriKind.Absolute, out _))
            {
                throw new ArgumentException("Qualified durable storage and a logical Server identity are required.");
            }
            m_durability = durability;
            m_consistency = consistency;
            m_prefix = "rse/server/" +
                Convert.ToBase64String(
                    SHA256.HashData(Encoding.UTF8.GetBytes(logicalServerId))) +
                "/";
            m_codec = new RecoverableExecutionRecordCodec(encoding, typeWitness);
        }

        /// <summary>
        /// Gets whether initialization and all subsequent storage/time evidence remain qualified.
        /// </summary>
        public bool IsReady => m_initialized && Volatile.Read(ref m_unavailable) == 0;

        /// <summary>
        /// Loads and reconciles every protected record before the caller enables new Durable admission.
        /// The caller restores the corresponding existing resource reservations before exposing the mode.
        /// </summary>
        public ValueTask<ArrayOf<Entry>> LoadAsync(int maximumRecords, CancellationToken ct)
        {
            return LoadAsync(maximumRecords, null, ct);
        }

        /// <summary>
        /// Restores each record's existing isolation reservations before loading another record.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<ArrayOf<Entry>> LoadAsync(
            int maximumRecords, Func<Entry, CancellationToken, ValueTask>? restore, CancellationToken ct)
        {
            if (maximumRecords <= 0 || m_initialized)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            var entries = new List<Entry>();
            try
            {
                await m_durability.VerifyDurabilityAsync(ct).ConfigureAwait(false);
                _ = await GetTimeAsync(ct).ConfigureAwait(false);
                await foreach (KeyValuePair<string, ByteString> item in m_store.ScanAsync(m_prefix, ct)
                    .ConfigureAwait(false))
                {
                    ct.ThrowIfCancellationRequested();
                    if (entries.Count >= maximumRecords || !m_consistency.IsLinearizable(item.Key))
                    {
                        throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                    }
                    RecoverableExecutionDurableRecord record = m_codec.Unprotect(
                        item.Value, m_protector, Context(item.Key));
                    if (!string.Equals(item.Key, Key(record), StringComparison.Ordinal))
                    {
                        throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                    }
                    await m_owners.ValidatePersistedOwnerAsync(record.Owner, ct).ConfigureAwait(false);
                    var entry = new Entry(this, item.Key, record, item.Value);
                    entries.Add(entry);
                    await entry.RecoverAsync(ct).ConfigureAwait(false);
                    if (restore != null)
                    {
                        await restore(entry, ct).ConfigureAwait(false);
                    }
                }
                m_initialized = true;
                return entries.ToArray();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                MarkUnavailable();
                foreach (Entry entry in entries)
                {
                    entry.Dispose();
                }
                throw;
            }
            catch (Exception exception)
            {
                MarkUnavailable();
                foreach (Entry entry in entries)
                {
                    entry.Dispose();
                }
                throw Unavailable(exception);
            }
        }

        /// <summary>
        /// Commits admission, samples its origin after the first commit, and persists E before dispatch is permitted.
        /// </summary>
        public ValueTask<Entry> AdmitAsync(RecoverableExecutionDurableRecord admission, CancellationToken ct)
        {
            return AdmitAsync(admission, null, ct);
        }

        /// <summary>
        /// Reports the first mutation boundary so the host can distinguish rejection from uncertain admission.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<Entry> AdmitAsync(
            RecoverableExecutionDurableRecord admission, Action? mutationStarting, CancellationToken ct)
        {
            RequireReady();
            if (admission.Phase != RecoverableExecutionCommitPhase.Admission)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            await m_owners.ValidatePersistedOwnerAsync(admission.Owner, ct).ConfigureAwait(false);
            await VerifyHealthAsync(ct).ConfigureAwait(false);
            _ = Add(await GetTimeAsync(ct).ConfigureAwait(false), admission.Execution);
            string key = Key(admission);
            ByteString encoded = Protect(key, admission);
            ct.ThrowIfCancellationRequested();
            SharedKeyValueStoreWriteResult result;
            try
            {
                RequireLinearizable(key);
                mutationStarting?.Invoke();
                result = m_store is ISharedKeyValueStoreCapacity capacity
                    ? await capacity.TryCompareAndSwapAsync(key, default, encoded, ct).ConfigureAwait(false)
                    : await m_store.CompareAndSwapAsync(key, default, encoded, ct).ConfigureAwait(false)
                        ? SharedKeyValueStoreWriteResult.Applied : SharedKeyValueStoreWriteResult.ConditionNotMet;
            }
            catch (Exception exception)
            {
                MarkUnavailable();
                throw Unavailable(exception);
            }
            if (result == SharedKeyValueStoreWriteResult.CapacityRejected)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            if (result == SharedKeyValueStoreWriteResult.ConditionNotMet)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            if (result != SharedKeyValueStoreWriteResult.Applied)
            {
                MarkUnavailable();
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            var entry = new Entry(this, key, admission, encoded);
            try
            {
                // A committed admission is independent of cancellation of the enclosing Call.
                RecoverableExecutionTime origin = await GetTimeAsync(CancellationToken.None).ConfigureAwait(false);
                _ = Add(origin, admission.Execution);
                await entry.FinishAdmissionAsync(origin).ConfigureAwait(false);
                return entry;
            }
            catch (Exception exception)
            {
                MarkUnavailable();
                entry.Dispose();
                throw Unavailable(exception);
            }
        }

        /// <summary>
        /// Verifies current store and clock health even when a lookup has no matching operation.
        /// </summary>
        public async ValueTask VerifyReadinessAsync(CancellationToken ct)
        {
            await VerifyHealthAsync(ct).ConfigureAwait(false);
            _ = await GetTimeAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads serialized qualified time and rejects resets, rollbacks and missing domains.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<RecoverableExecutionTime> GetTimeAsync(CancellationToken ct)
        {
            if (Volatile.Read(ref m_unavailable) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            await m_clockReads.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                RecoverableExecutionTime current = await m_clock.GetTimeAsync(ct).ConfigureAwait(false);
                _ = current.ElapsedUntil(current);
                if (m_lastTime.DomainId != Uuid.Empty)
                {
                    _ = m_lastTime.ElapsedUntil(current);
                }
                m_lastTime = current;
                return current;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                MarkUnavailable();
                throw Unavailable(exception);
            }
            finally
            {
                m_clockReads.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            MarkUnavailable();
            m_clockReads.Dispose();
        }

        /// <summary>
        /// Withdraws readiness when the host loses a qualification needed to interpret or serve retained state.
        /// </summary>
        public void Invalidate()
        {
            MarkUnavailable();
        }

        /// <summary>
        /// Adds a duration without accepting a wrapped or reset qualified deadline.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static RecoverableExecutionTime Add(RecoverableExecutionTime origin, TimeSpan duration)
        {
            _ = origin.ElapsedUntil(origin);
            if (duration < TimeSpan.Zero || origin.Ticks > long.MaxValue - duration.Ticks)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            return new RecoverableExecutionTime(origin.DomainId, origin.Ticks + duration.Ticks);
        }

        private string Key(RecoverableExecutionDurableRecord record)
        {
            return RecoverableExecutionRecordCodec.GetKey(m_prefix, record.Owner.Id, record.ClientId);
        }

        private static ByteString Context(string key)
        {
            return RecordProtectionContext.Create("OpcUa.RSE.Server/0.4/v1", key);
        }

        private ByteString Protect(string key, RecoverableExecutionDurableRecord record)
        {
            return m_codec.Protect(record, m_protector, Context(key));
        }

        private void RequireReady()
        {
            if (!IsReady)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
        }

        private void RequireLinearizable(string key)
        {
            if (!m_consistency.IsLinearizable(key))
            {
                MarkUnavailable();
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
        }

        private void MarkUnavailable()
        {
            Volatile.Write(ref m_unavailable, 1);
        }

        private static ServiceResultException Unavailable(Exception exception)
        {
            return exception is ServiceResultException known && known.StatusCode == StatusCodes.BadResourceUnavailable
                ? known
                : new ServiceResultException(StatusCodes.BadResourceUnavailable, exception);
        }

        private async ValueTask VerifyHealthAsync(CancellationToken ct)
        {
            RequireReady();
            try
            {
                await m_durability.VerifyDurabilityAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                MarkUnavailable();
                throw Unavailable(exception);
            }
        }

        /// <summary>
        /// Owns one operation's serialized durable decisions. Synchronization is never exposed to its callers.
        /// </summary>
        internal sealed class Entry : IDisposable
        {
            /// <summary>
            /// Captures a protected record and its exact compare-and-swap predecessor.
            /// </summary>
            public Entry(
                RecoverableExecutionJournal journal,
                string key,
                RecoverableExecutionDurableRecord record,
                ByteString protectedRecord)
            {
                m_journal = journal;
                m_key = key;
                m_record = record;
                m_protectedRecord = protectedRecord;
                InitialState = record.State;
                InitialOutcome = record.Phase is RecoverableExecutionCommitPhase.OutcomeAvailable or
                    RecoverableExecutionCommitPhase.Released ? record.Kind : ServiceOperationOutcome.None;
                InitialCancellationRequested = record.CancellationRequested;
            }

            /// <summary>
            /// Gets the stored public state before this instance made any recovery transitions.
            /// </summary>
            public ServiceOperationState InitialState { get; }

            /// <summary>
            /// Gets the previously committed outcome kind, excluding an uncommitted candidate.
            /// </summary>
            public ServiceOperationOutcome InitialOutcome { get; }

            /// <summary>
            /// Gets whether cancellation was already recorded before this instance recovered the operation.
            /// </summary>
            public bool InitialCancellationRequested { get; }

            /// <summary>
            /// Gets the latest immutable state; intermediate phases cannot be exposed as wire outcomes.
            /// </summary>
            public RecoverableExecutionDurableRecord Current => Volatile.Read(ref m_record);

            /// <summary>
            /// Gets whether expired history was durably removed after actual work completion.
            /// </summary>
            public bool Forgotten => Volatile.Read(ref m_forgotten);

            /// <summary>
            /// Completes the post-admission deadline write before the caller may attempt original dispatch.
            /// </summary>
            /// <exception cref="ServiceResultException"></exception>
            public async ValueTask FinishAdmissionAsync(RecoverableExecutionTime origin)
            {
                await m_transitions.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (m_record.Phase != RecoverableExecutionCommitPhase.Admission ||
                        m_record.AdmittedAt != default)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState);
                    }
                    await SaveAsync(m_record with
                    {
                        AdmittedAt = origin,
                        Phase = RecoverableExecutionCommitPhase.Pending
                    }).ConfigureAwait(false);
                }
                finally
                {
                    m_transitions.Release();
                }
            }

            /// <summary>
            /// Reconciles a restart without dispatch. Incomplete A or O becomes a new indeterminate outcome.
            /// </summary>
            public async ValueTask RecoverAsync(CancellationToken ct)
            {
                await m_transitions.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    RecoverableExecutionTime now = await m_journal.GetTimeAsync(ct).ConfigureAwait(false);
                    CheckDomain(now);
                    if (m_record.Phase is RecoverableExecutionCommitPhase.Admission or
                        RecoverableExecutionCommitPhase.Pending or RecoverableExecutionCommitPhase.OutcomePreparing)
                    {
                        await SelectOutcomeAsync(
                            ServiceOperationOutcome.ExecutionIndeterminate, StatusCodes.BadUnexpectedError, default,
                            m_record.Permissions, workFinished: m_record.WorkFinished || !m_record.DispatchStarted,
                            applyExecutionDeadline: false).ConfigureAwait(false);
                    }
                    else if (!m_record.DispatchStarted && !m_record.WorkFinished)
                    {
                        await SaveAsync(m_record with { WorkFinished = true }).ConfigureAwait(false);
                    }
                    _ = await AdvanceAsync(await m_journal.GetTimeAsync(ct).ConfigureAwait(false))
                        .ConfigureAwait(false);
                }
                finally
                {
                    m_transitions.Release();
                }
            }

            /// <summary>
            /// Durably marks dispatch once; an expired allowance prevents the caller from starting original work.
            /// </summary>
            /// <exception cref="ServiceResultException"></exception>
            public async ValueTask<bool> BeginDispatchAsync()
            {
                await m_transitions.WaitAsync().ConfigureAwait(false);
                try
                {
                    RequireUsable();
                    RecoverableExecutionTime now = await AdvanceAsync(
                        await m_journal.GetTimeAsync(CancellationToken.None).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    if (m_record.WorkFinished ||
                        m_record.State is not (ServiceOperationState.Pending or ServiceOperationState.Abandoned))
                    {
                        return false;
                    }
                    if (m_record.DispatchStarted)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState);
                    }
                    if (m_record.AdmittedAt.ElapsedUntil(now) >= m_record.Execution)
                    {
                        if (m_record.State == ServiceOperationState.Pending)
                        {
                            await SelectOutcomeAsync(ServiceOperationOutcome.ExecutionDeadlineExceeded,
                                StatusCodes.BadTimeout, default, default,
                                workFinished: true, applyExecutionDeadline: false).ConfigureAwait(false);
                        }
                        else
                        {
                            await SaveAsync(m_record with { WorkFinished = true }).ConfigureAwait(false);
                        }
                        return false;
                    }
                    await SaveAsync(m_record with { DispatchStarted = true }).ConfigureAwait(false);
                    now = await m_journal.GetTimeAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                    if (m_record.AdmittedAt.ElapsedUntil(now) >= m_record.Execution)
                    {
                        if (m_record.State == ServiceOperationState.Pending)
                        {
                            await SelectOutcomeAsync(ServiceOperationOutcome.ExecutionDeadlineExceeded,
                                StatusCodes.BadTimeout, default, default,
                                workFinished: true, applyExecutionDeadline: false).ConfigureAwait(false);
                        }
                        else
                        {
                            await SaveAsync(m_record with { WorkFinished = true }).ConfigureAwait(false);
                        }
                        return false;
                    }
                    return true;
                }
                finally
                {
                    m_transitions.Release();
                }
            }

            /// <summary>
            /// Commits a completed original response or protocol failure without replacing an earlier selected outcome.
            /// </summary>
            public async ValueTask CommitOutcomeAsync(
                ServiceOperationOutcome kind, StatusCode status, ByteString response,
                ArrayOf<RecoverableNodePermission> permissions, bool workFinished = true)
            {
                await m_transitions.WaitAsync().ConfigureAwait(false);
                try
                {
                    RequireUsable();
                    if (m_record.Phase == RecoverableExecutionCommitPhase.Pending)
                    {
                        await SelectOutcomeAsync(kind, status, response, permissions,
                            workFinished, applyExecutionDeadline: true).ConfigureAwait(false);
                    }
                    else if (workFinished && !m_record.WorkFinished)
                    {
                        await SaveAsync(m_record with { WorkFinished = true }).ConfigureAwait(false);
                    }
                    _ = await AdvanceAsync(await m_journal.GetTimeAsync(CancellationToken.None).ConfigureAwait(false))
                        .ConfigureAwait(false);
                }
                finally
                {
                    m_transitions.Release();
                }
            }

            /// <summary>
            /// Records independently established termination without changing or renewing a selected outcome.
            /// </summary>
            public async ValueTask ConfirmTerminationAsync()
            {
                await m_transitions.WaitAsync().ConfigureAwait(false);
                try
                {
                    m_journal.RequireLinearizable(m_key);
                    if (!m_record.WorkFinished && !Forgotten)
                    {
                        await SaveAsync(m_record with { WorkFinished = true }).ConfigureAwait(false);
                    }
                    _ = await AdvanceAsync(await m_journal.GetTimeAsync(CancellationToken.None).ConfigureAwait(false))
                        .ConfigureAwait(false);
                }
                finally
                {
                    m_transitions.Release();
                }
            }

            /// <summary>
            /// Applies elapsed-time expiry before selecting a fully durable snapshot for a caller.
            /// </summary>
            /// <exception cref="ServiceResultException"></exception>
            public async ValueTask<(RecoverableExecutionDurableRecord Record, RecoverableExecutionTime Now)>
                ObserveAsync(CancellationToken ct)
            {
                await m_transitions.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    m_journal.RequireReady();
                    m_journal.RequireLinearizable(m_key);
                    await m_journal.VerifyHealthAsync(ct).ConfigureAwait(false);
                    await m_journal.m_owners.ValidatePersistedOwnerAsync(m_record.Owner, ct).ConfigureAwait(false);
                    RecoverableExecutionTime now = await m_journal.GetTimeAsync(ct).ConfigureAwait(false);
                    CheckDomain(now);
                    now = await AdvanceAsync(now).ConfigureAwait(false);
                    return (m_record, now);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    m_journal.MarkUnavailable();
                    throw Unavailable(exception);
                }
                finally
                {
                    m_transitions.Release();
                }
            }

            /// <summary>
            /// Commits the named control action before returning its disposition, without renewing any window.
            /// </summary>
            /// <exception cref="ServiceResultException"></exception>
            public async ValueTask<ServiceOperationControlResult> ControlAsync(
                AsyncServiceAccess action, CancellationToken ct)
            {
                if (action is not (AsyncServiceAccess.Cancel or
                    AsyncServiceAccess.Acknowledge or AsyncServiceAccess.Abandon))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
                await m_transitions.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    m_journal.RequireReady();
                    m_journal.RequireLinearizable(m_key);
                    await m_journal.VerifyHealthAsync(ct).ConfigureAwait(false);
                    await m_journal.m_owners.ValidatePersistedOwnerAsync(m_record.Owner, ct).ConfigureAwait(false);
                    RecoverableExecutionTime now = await m_journal.GetTimeAsync(ct).ConfigureAwait(false);
                    CheckDomain(now);
                    now = await AdvanceAsync(now).ConfigureAwait(false);
                    if (Forgotten || m_record.IsHistoryExpired(now))
                    {
                        return ServiceOperationControlResult.UnknownOperation;
                    }
                    switch (action)
                    {
                        case AsyncServiceAccess.Cancel:
                            if (m_record.WorkFinished)
                            {
                                return ServiceOperationControlResult.NotApplicable;
                            }
                            if (m_record.CancellationRequested)
                            {
                                return ServiceOperationControlResult.AlreadyApplied;
                            }
                            await SaveAsync(m_record with { CancellationRequested = true }).ConfigureAwait(false);
                            return ServiceOperationControlResult.Applied;
                        case AsyncServiceAccess.Acknowledge:
                            if (m_record.State == ServiceOperationState.Acknowledged)
                            {
                                return ServiceOperationControlResult.AlreadyApplied;
                            }
                            if (m_record.State != ServiceOperationState.OutcomeAvailable)
                            {
                                return ServiceOperationControlResult.NotApplicable;
                            }
                            await ReleaseAsync(ServiceOperationState.Acknowledged, now).ConfigureAwait(false);
                            return ServiceOperationControlResult.Applied;
                        case AsyncServiceAccess.Abandon:
                            if (m_record.State == ServiceOperationState.Abandoned)
                            {
                                return ServiceOperationControlResult.AlreadyApplied;
                            }
                            if (m_record.State is not (
                                ServiceOperationState.Pending or ServiceOperationState.OutcomeAvailable))
                            {
                                return ServiceOperationControlResult.NotApplicable;
                            }
                            await ReleaseAsync(ServiceOperationState.Abandoned, now).ConfigureAwait(false);
                            return ServiceOperationControlResult.Applied;
                        default:
                            throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    m_journal.MarkUnavailable();
                    throw Unavailable(exception);
                }
                finally
                {
                    m_transitions.Release();
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_transitions.Dispose();
            }

            private async ValueTask SelectOutcomeAsync(
                ServiceOperationOutcome kind, StatusCode status, ByteString response,
                ArrayOf<RecoverableNodePermission> permissions, bool workFinished, bool applyExecutionDeadline)
            {
                if (applyExecutionDeadline)
                {
                    RecoverableExecutionTime before = await m_journal.GetTimeAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                    if (m_record.AdmittedAt.ElapsedUntil(before) >= m_record.Execution)
                    {
                        kind = ServiceOperationOutcome.ExecutionDeadlineExceeded;
                        status = StatusCodes.BadTimeout;
                        response = default;
                        permissions = default;
                        applyExecutionDeadline = false;
                    }
                }
                RecoverableExecutionDurableRecord prepared = m_record with
                {
                    Phase = RecoverableExecutionCommitPhase.OutcomePreparing,
                    State = ServiceOperationState.Pending,
                    Kind = kind,
                    Status = status,
                    Response = response,
                    Permissions = permissions,
                    OutcomeAt = default,
                    WorkFinished = workFinished
                };
                await SaveAsync(prepared).ConfigureAwait(false);
                RecoverableExecutionTime origin = await m_journal.GetTimeAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                if (applyExecutionDeadline &&
                    m_record.AdmittedAt.ElapsedUntil(origin) >= m_record.Execution)
                {
                    prepared = m_record with
                    {
                        Kind = ServiceOperationOutcome.ExecutionDeadlineExceeded,
                        Status = StatusCodes.BadTimeout,
                        Response = default,
                        Permissions = default
                    };
                    await SaveAsync(prepared).ConfigureAwait(false);
                    origin = await m_journal.GetTimeAsync(CancellationToken.None).ConfigureAwait(false);
                }
                try
                {
                    _ = Add(origin, m_record.Retention);
                }
                catch (Exception exception)
                {
                    m_journal.MarkUnavailable();
                    throw Unavailable(exception);
                }
                await SaveAsync(m_record with
                {
                    Phase = RecoverableExecutionCommitPhase.OutcomeAvailable,
                    State = ServiceOperationState.OutcomeAvailable,
                    OutcomeAt = origin
                }).ConfigureAwait(false);
            }

            private async ValueTask<RecoverableExecutionTime> AdvanceAsync(RecoverableExecutionTime now)
            {
                if (Forgotten)
                {
                    return now;
                }
                CheckDomain(now);
                if (!m_record.WorkFinished &&
                    m_record.DispatchStarted &&
                    !m_record.CancellationRequested &&
                    m_record.AdmittedAt.ElapsedUntil(now) >= m_record.Execution)
                {
                    await SaveAsync(m_record with { CancellationRequested = true }).ConfigureAwait(false);
                    now = await m_journal.GetTimeAsync(CancellationToken.None).ConfigureAwait(false);
                }
                if (m_record.Phase == RecoverableExecutionCommitPhase.Pending &&
                    m_record.AdmittedAt.ElapsedUntil(now) >= m_record.Execution)
                {
                    await SelectOutcomeAsync(
                        ServiceOperationOutcome.ExecutionDeadlineExceeded, StatusCodes.BadTimeout, default, default,
                        workFinished: m_record.WorkFinished || !m_record.DispatchStarted, applyExecutionDeadline: false)
                        .ConfigureAwait(false);
                    now = await m_journal.GetTimeAsync(CancellationToken.None).ConfigureAwait(false);
                }
                else if (m_record.State == ServiceOperationState.Abandoned &&
                    !m_record.DispatchStarted &&
                    !m_record.WorkFinished &&
                    m_record.AdmittedAt.ElapsedUntil(now) >= m_record.Execution)
                {
                    await SaveAsync(m_record with { WorkFinished = true }).ConfigureAwait(false);
                    now = await m_journal.GetTimeAsync(CancellationToken.None).ConfigureAwait(false);
                }
                if (m_record.Phase == RecoverableExecutionCommitPhase.OutcomeAvailable &&
                    m_record.OutcomeAt.ElapsedUntil(now) >= m_record.Retention)
                {
                    await ReleaseAsync(ServiceOperationState.Expired, Add(m_record.OutcomeAt, m_record.Retention))
                        .ConfigureAwait(false);
                    now = await m_journal.GetTimeAsync(CancellationToken.None).ConfigureAwait(false);
                }
                if (m_record.IsReleased && m_record.IsHistoryExpired(now) && m_record.WorkFinished)
                {
                    m_journal.RequireLinearizable(m_key);
                    try
                    {
                        if (!await m_journal.m_store.CompareAndSwapAsync(
                            m_key, m_protectedRecord, default, CancellationToken.None).ConfigureAwait(false))
                        {
                            throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                        }
                        Volatile.Write(ref m_forgotten, true);
                    }
                    catch (Exception exception)
                    {
                        m_journal.MarkUnavailable();
                        throw Unavailable(exception);
                    }
                }
                return now;
            }

            private ValueTask ReleaseAsync(ServiceOperationState state, RecoverableExecutionTime origin)
            {
                _ = Add(origin, m_record.Retention);
                return SaveAsync(m_record with
                {
                    Phase = RecoverableExecutionCommitPhase.Released,
                    State = state,
                    ReleasedAt = origin,
                    Request = default,
                    Response = default,
                    Permissions = default
                });
            }

            private async ValueTask SaveAsync(RecoverableExecutionDurableRecord next)
            {
                if (Volatile.Read(ref m_journal.m_unavailable) != 0 || m_record.Revision == ulong.MaxValue)
                {
                    m_journal.MarkUnavailable();
                    throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                }
                next = next with { Revision = m_record.Revision + 1 };
                try
                {
                    ByteString protectedRecord = m_journal.Protect(m_key, next);
                    m_journal.RequireLinearizable(m_key);
                    if (!await m_journal.m_store.CompareAndSwapAsync(
                        m_key, m_protectedRecord, protectedRecord, CancellationToken.None).ConfigureAwait(false))
                    {
                        throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                    }
                    m_protectedRecord = protectedRecord;
                    Volatile.Write(ref m_record, next);
                }
                catch (Exception exception)
                {
                    m_journal.MarkUnavailable();
                    throw Unavailable(exception);
                }
            }

            private void CheckDomain(RecoverableExecutionTime now)
            {
                foreach (RecoverableExecutionTime origin in new[]
                {
                    m_record.AdmittedAt, m_record.OutcomeAt, m_record.ReleasedAt
                })
                {
                    if (origin.DomainId != Uuid.Empty)
                    {
                        _ = origin.ElapsedUntil(now);
                    }
                }
            }

            private void RequireUsable()
            {
                m_journal.RequireReady();
                m_journal.RequireLinearizable(m_key);
                if (Forgotten)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
            }

            private readonly RecoverableExecutionJournal m_journal;
            private readonly string m_key;
            private readonly SemaphoreSlim m_transitions = new(1, 1);
            private RecoverableExecutionDurableRecord m_record;
            private ByteString m_protectedRecord;
            private bool m_forgotten;
        }

        private readonly ISharedKeyValueStore m_store;
        private readonly ISharedKeyValueStoreDurability m_durability;
        private readonly ISharedKeyValueStoreConsistency m_consistency;
        private readonly IRecordProtector m_protector;
        private readonly IRecoverableExecutionClock m_clock;
        private readonly IRecoverableExecutionOwnerProvider m_owners;
        private readonly RecoverableExecutionRecordCodec m_codec;
        private readonly string m_prefix;
        private readonly SemaphoreSlim m_clockReads = new(1, 1);
        private RecoverableExecutionTime m_lastTime;
        private bool m_initialized;
        private int m_unavailable;
    }
}
