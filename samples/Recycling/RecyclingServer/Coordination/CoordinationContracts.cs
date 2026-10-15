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

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Sample application permissions. Process identities never acquire operator or administrator privileges.
    /// </summary>
    public enum RecyclingAccess
    {
        /// <summary>Read-only observer.</summary>
        Observer,
        /// <summary>Own-role registration, initialization and lease maintenance only.</summary>
        ProcessView,
        /// <summary>Equipment enable/disable and explicit product disposition.</summary>
        Operator,
        /// <summary>Simulator lifecycle, bounded stepping, reset and checkpoint administration.</summary>
        SimulatorAdministrator
    }

    /// <summary>
    /// Trusted in-process result of authentication/authorization, never accepted as a method argument.
    /// </summary>
    public sealed record RecyclingPrincipal(
        string Identity,
        RecyclingAccess Access,
        RecyclingProcess Process,
        string ConnectionId,
        string CertificateSha256 = "");

    /// <summary>
    /// Typed command verbs in the sample coordination model, not added to any official companion method.
    /// </summary>
    public enum RecyclingCommandKind
    {
        /// <summary>Claims a distinct authenticated process role for the line.</summary>
        Register,
        /// <summary>Acknowledges receipt of the initial committed image.</summary>
        Initialize,
        /// <summary>Renews an initialized process view's wall-clock lease.</summary>
        Heartbeat,
        /// <summary>Explicitly relinquishes this session's availability.</summary>
        Disconnect,
        /// <summary>Freezes material time.</summary>
        Pause,
        /// <summary>Resumes a paused or deliberately stopped run.</summary>
        Resume,
        /// <summary>Stops material time without discarding inventory.</summary>
        Stop,
        /// <summary>Enables a process's equipment, without overriding availability or utility interlocks.</summary>
        EnableProcess,
        /// <summary>Disables a process's equipment.</summary>
        DisableProcess,
        /// <summary>Performs a bounded number of explicit steps while paused.</summary>
        Step,
        /// <summary>Creates a new run identity without reusing production history.</summary>
        Reset,
        /// <summary>Records an explicit authorized hold disposition.</summary>
        DisposeHold,
        /// <summary>Creates a durable checkpoint of the applied administrative revision.</summary>
        Checkpoint
    }

    /// <summary>
    /// A bounded optimistic command. Fields not used by a verb must retain their default values.
    /// </summary>
    public sealed record RecyclingCommand
    {
        /// <summary>
        /// Caller-generated stable request identifier; retries must retain the entire payload.
        /// </summary>
        public string RequestId { get; init; } = string.Empty;

        /// <summary>
        /// Intended generic line identity.
        /// </summary>
        public string LineId { get; init; } = string.Empty;

        /// <summary>
        /// Run identity observed before submitting the command.
        /// </summary>
        public string RunId { get; init; } = string.Empty;

        /// <summary>
        /// Revision observed before submitting the command.
        /// </summary>
        public long ExpectedRevision { get; init; }

        /// <summary>
        /// Explicit sample command verb.
        /// </summary>
        public RecyclingCommandKind Kind { get; init; }

        /// <summary>
        /// Process selection for membership or equipment commands.
        /// </summary>
        public RecyclingProcess Process { get; init; }

        /// <summary>
        /// Inspection identifier for a disposition command.
        /// </summary>
        public long HoldId { get; init; }

        /// <summary>
        /// Authorized release or rejection, never an implicit clearance.
        /// </summary>
        public HoldDisposition Disposition { get; init; }

        /// <summary>
        /// Bounded number of material ticks for an administrative step.
        /// </summary>
        public int Steps { get; init; }
    }

    /// <summary>
    /// Original durable outcome returned again for identical authenticated retries.
    /// </summary>
    public sealed record RecyclingCommandOutcome(
        string RequestId,
        string RunId,
        long Revision,
        long Tick,
        uint Status,
        string Message);

    /// <summary>
    /// Auditable command receipt retained with production state before acknowledgement.
    /// </summary>
    public sealed record RecyclingCommandReceipt(
        string Identity,
        RecyclingAccess Access,
        RecyclingCommand Command,
        RecyclingCommandOutcome Outcome,
        DateTime CommittedUtc);

    /// <summary>
    /// A coalescible current-state projection notification, never an alternative material/event ledger.
    /// </summary>
    public sealed record CoordinatorNotification(CommittedSnapshot Snapshot, bool IsAvailable, string Detail);

    /// <summary>
    /// Complete versioned state of one single-writer journal commit.
    /// </summary>
    public sealed record RecyclingCheckpoint
    {
        /// <summary>
        /// Persistence envelope schema.
        /// </summary>
        public int SchemaVersion { get; init; } = 2;

        /// <summary>
        /// Monotonic journal sequence independent of resettable material ticks.
        /// </summary>
        public long Sequence { get; init; }

        /// <summary>
        /// SHA-256 of the preceding committed journal payload.
        /// </summary>
        public string PreviousHash { get; init; } = string.Empty;

        /// <summary>
        /// SHA-256 configuration and compiled sample-model identity.
        /// </summary>
        public string ConfigurationHash { get; init; } = string.Empty;

        /// <summary>
        /// Persisted line identity.
        /// </summary>
        public string LineId { get; init; } = string.Empty;

        /// <summary>
        /// Complete immutable production state.
        /// </summary>
        public CommittedSnapshot Snapshot { get; init; } = new();

        /// <summary>
        /// Bounded durable idempotency and authenticated command audit history.
        /// </summary>
        public ArrayOf<RecyclingCommandReceipt> Receipts { get; init; } = [];
    }
}
