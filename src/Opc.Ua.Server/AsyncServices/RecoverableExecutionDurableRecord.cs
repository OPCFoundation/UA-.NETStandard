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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Local durable commit phases, not additional wire states or protocol DataTypes.
    /// </summary>
    internal enum RecoverableExecutionCommitPhase
    {
        /// <summary>
        /// Admission is durable but its post-commit execution origin has not been recorded.
        /// </summary>
        Admission,

        /// <summary>
        /// Admission and its unchanged execution deadline are durable.
        /// </summary>
        Pending,

        /// <summary>
        /// A candidate outcome is durable but its post-commit retention origin is not yet recorded.
        /// </summary>
        OutcomePreparing,

        /// <summary>
        /// The immutable outcome and its unchanged retention deadline are durable.
        /// </summary>
        OutcomeAvailable,

        /// <summary>
        /// Release reason, effective time and terminal-history deadline are durable together.
        /// </summary>
        Released
    }

    /// <summary>
    /// Immutable local persistence state. Payloads are protected by the journal and are never redispatched on recovery.
    /// </summary>
    internal sealed record RecoverableExecutionDurableRecord
    {
        /// <summary>
        /// Gets the stable application-and-user owner and its original mapping evidence.
        /// </summary>
        public required RecoverableExecutionOwner Owner { get; init; }

        /// <summary>
        /// Gets the remembered Client identity in the stable owner's shared duplicate namespace.
        /// </summary>
        public required Uuid ClientId { get; init; }

        /// <summary>
        /// Gets the protected non-bearer Server token.
        /// </summary>
        public required ByteString Token { get; init; }

        /// <summary>
        /// Gets the originating execution incarnation, which is not evidence that external work stopped.
        /// </summary>
        public required Uuid Incarnation { get; init; }

        /// <summary>
        /// Gets the issuing Session identifier for metadata-only audit correlation.
        /// </summary>
        public required NodeId IssuingSessionId { get; init; }

        /// <summary>
        /// Gets the original standard Service DataType identity.
        /// </summary>
        public required NodeId ServiceId { get; init; }

        /// <summary>
        /// Gets the persisted codec and namespace compatibility witness.
        /// </summary>
        public required ByteString TypeWitness { get; init; }

        /// <summary>
        /// Gets the original request bytes used only to revalidate retained-result access, never for redispatch.
        /// </summary>
        public required ByteString Request { get; init; }

        /// <summary>
        /// Gets immutable original response bytes, or a null ByteString for a protocol outcome or released record.
        /// </summary>
        public ByteString Response { get; init; }

        /// <summary>
        /// Gets the effective admission-time diagnostic permission independently of the unchanged original header.
        /// </summary>
        public required DiagnosticsMasks DiagnosticsMask { get; init; }

        /// <summary>
        /// Gets admission-time roles whose loss prevents disclosure.
        /// </summary>
        public required ArrayOf<NodeId> Roles { get; init; }

        /// <summary>
        /// Gets successful original permission checks that must be evaluated anew at collection.
        /// </summary>
        public ArrayOf<RecoverableNodePermission> Permissions { get; init; }

        /// <summary>
        /// Gets the accepted finite message envelope bound used for reservation and snapshot encoding.
        /// </summary>
        public required int MessageLimit { get; init; }

        /// <summary>
        /// Gets the original transport mapping used when accepting the envelope size.
        /// </summary>
        public required RequestEncoding Mapping { get; init; }

        /// <summary>
        /// Gets the exact accepted execution allowance.
        /// </summary>
        public required TimeSpan Execution { get; init; }

        /// <summary>
        /// Gets the exact accepted result and subsequent terminal-history interval.
        /// </summary>
        public required TimeSpan Retention { get; init; }

        /// <summary>
        /// Gets the local crash-consistent commit phase.
        /// </summary>
        public RecoverableExecutionCommitPhase Phase { get; init; }

        /// <summary>
        /// Gets the public state, with released states never becoming available again.
        /// </summary>
        public ServiceOperationState State { get; init; } = ServiceOperationState.Pending;

        /// <summary>
        /// Gets the selected immutable outcome kind.
        /// </summary>
        public ServiceOperationOutcome Kind { get; init; }

        /// <summary>
        /// Gets the original Service result or terminal protocol error.
        /// </summary>
        public StatusCode Status { get; init; }

        /// <summary>
        /// Gets the qualified post-admission-commit origin; a default coordinate marks an incomplete admission.
        /// </summary>
        public RecoverableExecutionTime AdmittedAt { get; init; }

        /// <summary>
        /// Gets the qualified post-outcome-commit origin; a default coordinate marks an incomplete outcome.
        /// </summary>
        public RecoverableExecutionTime OutcomeAt { get; init; }

        /// <summary>
        /// Gets the effective logical-release time committed atomically with released state and history.
        /// </summary>
        public RecoverableExecutionTime ReleasedAt { get; init; }

        /// <summary>
        /// Gets whether dispatch was durably authorized. Absence of this marker proves no original dispatch began.
        /// </summary>
        public bool DispatchStarted { get; init; }

        /// <summary>
        /// Gets whether actual invocation completion or independent fencing has been established.
        /// </summary>
        public bool WorkFinished { get; init; }

        /// <summary>
        /// Gets whether a cancellation signal has been durably recorded, not a promise of rollback or stopping.
        /// </summary>
        public bool CancellationRequested { get; init; }

        /// <summary>
        /// Gets the monotonic record revision used to reconcile concurrent state publication.
        /// </summary>
        public ulong Revision { get; init; } = 1;

        /// <summary>
        /// Gets whether only bounded terminal history remains publicly recoverable.
        /// </summary>
        public bool IsReleased => State is ServiceOperationState.Acknowledged or
            ServiceOperationState.Abandoned or ServiceOperationState.Expired;

        /// <summary>
        /// Reports elapsed history without renewing it. Active-work identity may still remain privately charged.
        /// </summary>
        public bool IsHistoryExpired(RecoverableExecutionTime now)
        {
            return IsReleased && ReleasedAt.ElapsedUntil(now) >= Retention;
        }

        /// <summary>
        /// Projects only a fully committed record into the declared wire metadata.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public ServiceOperationInfoDataType Snapshot(RecoverableExecutionTime now)
        {
            if (Phase is RecoverableExecutionCommitPhase.Admission or RecoverableExecutionCommitPhase.OutcomePreparing)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            return new ServiceOperationInfoDataType
            {
                ClientOperationId = ClientId,
                ServerToken = Token,
                ServiceId = ServiceId,
                RecoveryMode = ServiceOperationRecoveryMode.Durable,
                State = State,
                OutcomeKind = Kind,
                OutcomeStatus = Status,
                ExecutionAllowance = RecoverableExecutionDurations.ToMilliseconds(Execution),
                ResultRetentionTime = RecoverableExecutionDurations.ToMilliseconds(Retention),
                ExecutionRemaining = State == ServiceOperationState.Pending ? Remaining(AdmittedAt, Execution, now) : 0,
                ResultRemaining = State == ServiceOperationState.OutcomeAvailable
                    ? Remaining(OutcomeAt, Retention, now) : 0,
                HistoryRemaining = IsReleased ? Remaining(ReleasedAt, Retention, now) : 0,
                CancellationRequested = CancellationRequested
            };
        }

        /// <summary>
        /// Returns a conservative remaining duration from a recorded origin in the same qualified time domain.
        /// </summary>
        private static double Remaining(
            RecoverableExecutionTime origin, TimeSpan duration, RecoverableExecutionTime now)
        {
            return RecoverableExecutionDurations.RemainingMilliseconds(duration, origin.ElapsedUntil(now));
        }
    }
}
