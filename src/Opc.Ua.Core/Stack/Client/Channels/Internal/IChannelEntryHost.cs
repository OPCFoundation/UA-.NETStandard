/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Supplies manager-owned services, certificate snapshots, and diagnostics to a shared channel entry.
    /// </summary>
    internal interface IChannelEntryHost : IClientChannelManager
    {
        /// <summary>
        /// Gets the logger used for transport and participant failures.
        /// </summary>
        ILogger? Logger { get; }

        /// <summary>
        /// Gets the clock used for retry delays, timeouts, and elapsed-time metrics.
        /// </summary>
        TimeProvider TimeProvider { get; }

        /// <summary>
        /// Gets the policy that bounds transport retries and participant recovery.
        /// </summary>
        IChannelReconnectPolicy ReconnectPolicy { get; }

        /// <summary>
        /// Gets the token cancelled when the manager shuts down.
        /// </summary>
        CancellationToken ShutdownToken { get; }

        /// <summary>
        /// Gets the owner of background teardown work that must finish during manager disposal.
        /// </summary>
        BackgroundTaskScope BackgroundWork { get; }

        /// <summary>
        /// Gets the transport bindings, or null when the default bindings apply.
        /// </summary>
        Bindings.ITransportChannelBindings? ChannelFactory { get; }

        /// <summary>
        /// Gets the client application configuration used to open transports.
        /// </summary>
        ApplicationConfiguration Configuration { get; }

        /// <summary>
        /// Reports an entry state transition to manager diagnostics.
        /// </summary>
        void OnEntryStateChanged(ChannelEntry entry, ChannelStateChange change);

        /// <summary>
        /// Records transport closure and its reason without releasing participant leases.
        /// </summary>
        void OnEntryClosed(
            ChannelEntry entry,
            ClientChannelManager.ChannelCloseReason reason);

        /// <summary>
        /// Acquires a caller-owned snapshot of the current client certificate, chain, and configuration version.
        /// </summary>
        ClientChannelCertificateSnapshot SnapshotClientCertificate();

        /// <summary>
        /// Acquires newer material of the entry's certificate type, or retains its current material when none exists.
        /// </summary>
        /// <param name="current">The material currently owned by the entry.</param>
        /// <returns>A caller-owned snapshot. Disposing it does not release the entry's handles.</returns>
        ClientChannelCertificateSnapshot SnapshotClientCertificate(ClientChannelCertificateSnapshot current);

        /// <summary>
        /// Starts a trace for one coalesced entry reconnect cycle.
        /// </summary>
        Activity? StartReconnectActivity(ChannelEntry entry);

        /// <summary>
        /// Completes a reconnect trace with its attempts, outcome, and last error.
        /// </summary>
        void CompleteReconnectActivity(
            Activity? activity,
            ChannelEntry entry,
            int attemptCount,
            string outcome,
            ServiceResult? error);

        /// <summary>
        /// Reports a failed reconnect attempt without deciding whether to retry it.
        /// </summary>
        void OnEntryReconnectFailed(
            ChannelEntry entry,
            int attempt,
            string outcome,
            ServiceResult? error);

        /// <summary>
        /// Reports that an underlying transport has opened.
        /// </summary>
        void OnEntryOpened(ChannelEntry entry);

        /// <summary>
        /// Reports an attached participant with the resulting lease and participant counts.
        /// </summary>
        void OnEntryParticipantAttached(
            ChannelEntry entry,
            string participantId,
            int refCount,
            int participantCount);

        /// <summary>
        /// Reports a detached participant with the resulting lease and participant counts.
        /// </summary>
        void OnEntryParticipantDetached(
            ChannelEntry entry,
            string participantId,
            int refCount,
            int participantCount);

        /// <summary>
        /// Increments the channel-open metric for the entry.
        /// </summary>
        void RecordChannelOpen(ChannelEntry entry);

        /// <summary>
        /// Adjusts the active-channel metric by the supplied delta.
        /// </summary>
        void RecordChannelActiveChanged(ChannelEntry entry, long delta);

        /// <summary>
        /// Records a reconnect attempt and its outcome.
        /// </summary>
        void RecordReconnectAttempt(ChannelEntry entry, string outcome);

        /// <summary>
        /// Records the elapsed time and outcome of a reconnect cycle.
        /// </summary>
        void RecordReconnectDuration(
            ChannelEntry entry,
            TimeSpan duration,
            string outcome);

        /// <summary>
        /// Records how long a caller waited for the entry to become ready.
        /// </summary>
        void RecordGateWait(ChannelEntry entry, TimeSpan duration);

        /// <summary>
        /// Records a participant operation that exceeded its timeout.
        /// </summary>
        void RecordParticipantTimeout(ChannelEntry entry, string participantId);

        /// <summary>
        /// Records whether a participant's session recreation succeeded.
        /// </summary>
        void RecordParticipantRecreate(ChannelEntry entry, string participantId, bool success);

        /// <summary>
        /// Removes the key only if it still refers to this entry, preserving a concurrent replacement.
        /// </summary>
        void RemoveEntryIfPresent(ManagedChannelKey key, ChannelEntry entry);

        /// <summary>
        /// Releases the transport through its owning bindings after asynchronous closure.
        /// </summary>
        void CloseChannel(ITransportChannel channel);
    }
}
