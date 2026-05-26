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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Alarms
{
    /// <summary>
    /// Default implementation of OPC UA Part 9 alarm and condition
    /// client operations. Every method delegates to the corresponding
    /// source-generated <c>*TypeClient</c> proxy
    /// (<see cref="ConditionTypeClient"/>,
    /// <see cref="AcknowledgeableConditionTypeClient"/>,
    /// <see cref="AlarmConditionTypeClient"/>,
    /// <see cref="DialogConditionTypeClient"/>, and
    /// <see cref="ShelvedStateMachineTypeClient"/>), constructed
    /// per-call with the caller-supplied <c>conditionId</c> as the
    /// proxy's <c>ObjectId</c>.
    /// </summary>
    /// <remarks>
    /// The proxies are sufficient on their own — this client merely
    /// provides a session-scoped façade with one method per Part 9
    /// operation that takes the <c>conditionId</c> as an explicit
    /// parameter (the proxies require it at construction). Following
    /// Part 9 §5.5.4, the well-known method NodeIds are accepted by
    /// servers using the condition instance's NodeId as the
    /// <c>ObjectId</c>, so a server that does not expose condition
    /// instances as addressable nodes still services these calls.
    /// </remarks>
    public class AlarmClient :
        IAlarmOperations,
        IDialogConditionOperations
    {
        private readonly ISessionClient m_session;
        private readonly ITelemetryContext m_telemetry;

        /// <summary>
        /// Initializes a new <see cref="AlarmClient"/> over the supplied
        /// session and telemetry context. The telemetry context is
        /// forwarded to every source-generated proxy this client
        /// constructs internally.
        /// </summary>
        /// <param name="session">The client session to use.</param>
        /// <param name="telemetry">The telemetry context for diagnostics.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="session"/> or <paramref name="telemetry"/> is
        /// <c>null</c>.
        /// </exception>
        public AlarmClient(ISessionClient session, ITelemetryContext telemetry)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <inheritdoc/>
        public ValueTask EnableAsync(
            NodeId conditionId,
            CancellationToken ct = default)
            => new ConditionTypeClient(m_session, conditionId, m_telemetry)
                .EnableAsync(ct);

        /// <inheritdoc/>
        public ValueTask DisableAsync(
            NodeId conditionId,
            CancellationToken ct = default)
            => new ConditionTypeClient(m_session, conditionId, m_telemetry)
                .DisableAsync(ct);

        /// <inheritdoc/>
        public ValueTask AddCommentAsync(
            NodeId conditionId,
            ByteString eventId,
            LocalizedText comment,
            CancellationToken ct = default)
            => new ConditionTypeClient(m_session, conditionId, m_telemetry)
                .AddCommentAsync(eventId, comment, ct);

        /// <inheritdoc/>
        public ValueTask ConditionRefreshAsync(
            uint subscriptionId,
            CancellationToken ct = default)
            => new ConditionTypeClient(m_session, ObjectTypeIds.ConditionType, m_telemetry)
                .ConditionRefreshAsync(subscriptionId, ct);

        /// <inheritdoc/>
        public ValueTask ConditionRefresh2Async(
            uint subscriptionId,
            uint monitoredItemId,
            CancellationToken ct = default)
            => new ConditionTypeClient(m_session, ObjectTypeIds.ConditionType, m_telemetry)
                .ConditionRefresh2Async(subscriptionId, monitoredItemId, ct);

        /// <inheritdoc/>
        public ValueTask AcknowledgeAsync(
            NodeId conditionId,
            ByteString eventId,
            LocalizedText comment,
            CancellationToken ct = default)
            => new AcknowledgeableConditionTypeClient(m_session, conditionId, m_telemetry)
                .AcknowledgeAsync(eventId, comment, ct);

        /// <inheritdoc/>
        public ValueTask ConfirmAsync(
            NodeId conditionId,
            ByteString eventId,
            LocalizedText comment,
            CancellationToken ct = default)
            => new AcknowledgeableConditionTypeClient(m_session, conditionId, m_telemetry)
                .ConfirmAsync(eventId, comment, ct);

        /// <inheritdoc/>
        public ValueTask SilenceAsync(
            NodeId conditionId,
            CancellationToken ct = default)
            => new AlarmConditionTypeClient(m_session, conditionId, m_telemetry)
                .SilenceAsync(ct);

        /// <inheritdoc/>
        public ValueTask SuppressAsync(
            NodeId conditionId,
            LocalizedText comment = default,
            CancellationToken ct = default)
        {
            var proxy = new AlarmConditionTypeClient(m_session, conditionId, m_telemetry);
            return comment.IsNullOrEmpty
                ? proxy.SuppressAsync(ct)
                : proxy.Suppress2Async(comment, ct);
        }

        /// <inheritdoc/>
        public ValueTask UnsuppressAsync(
            NodeId conditionId,
            LocalizedText comment = default,
            CancellationToken ct = default)
        {
            var proxy = new AlarmConditionTypeClient(m_session, conditionId, m_telemetry);
            return comment.IsNullOrEmpty
                ? proxy.UnsuppressAsync(ct)
                : proxy.Unsuppress2Async(comment, ct);
        }

        /// <inheritdoc/>
        public ValueTask RemoveFromServiceAsync(
            NodeId conditionId,
            LocalizedText comment = default,
            CancellationToken ct = default)
        {
            var proxy = new AlarmConditionTypeClient(m_session, conditionId, m_telemetry);
            return comment.IsNullOrEmpty
                ? proxy.RemoveFromServiceAsync(ct)
                : proxy.RemoveFromService2Async(comment, ct);
        }

        /// <inheritdoc/>
        public ValueTask PlaceInServiceAsync(
            NodeId conditionId,
            LocalizedText comment = default,
            CancellationToken ct = default)
        {
            var proxy = new AlarmConditionTypeClient(m_session, conditionId, m_telemetry);
            return comment.IsNullOrEmpty
                ? proxy.PlaceInServiceAsync(ct)
                : proxy.PlaceInService2Async(comment, ct);
        }

        /// <inheritdoc/>
        public ValueTask ResetAsync(
            NodeId conditionId,
            LocalizedText comment = default,
            CancellationToken ct = default)
        {
            var proxy = new AlarmConditionTypeClient(m_session, conditionId, m_telemetry);
            return comment.IsNullOrEmpty
                ? proxy.ResetAsync(ct)
                : proxy.Reset2Async(comment, ct);
        }

        /// <inheritdoc/>
        public ValueTask TimedShelveAsync(
            NodeId conditionId,
            double shelvingTime,
            CancellationToken ct = default)
            => new ShelvedStateMachineTypeClient(m_session, conditionId, m_telemetry)
                .TimedShelveAsync(shelvingTime, ct);

        /// <inheritdoc/>
        public ValueTask OneShotShelveAsync(
            NodeId conditionId,
            CancellationToken ct = default)
            => new ShelvedStateMachineTypeClient(m_session, conditionId, m_telemetry)
                .OneShotShelveAsync(ct);

        /// <inheritdoc/>
        public ValueTask UnshelveAsync(
            NodeId conditionId,
            CancellationToken ct = default)
            => new ShelvedStateMachineTypeClient(m_session, conditionId, m_telemetry)
                .UnshelveAsync(ct);

        /// <inheritdoc/>
        public ValueTask<ArrayOf<NodeId>> GetGroupMembershipsAsync(
            NodeId conditionId,
            CancellationToken ct = default)
            => new AlarmConditionTypeClient(m_session, conditionId, m_telemetry)
                .GetGroupMembershipsAsync(ct);

        /// <inheritdoc/>
        public ValueTask RespondAsync(
            NodeId conditionId,
            int selectedResponse,
            CancellationToken ct = default)
            => new DialogConditionTypeClient(m_session, conditionId, m_telemetry)
                .RespondAsync(selectedResponse, ct);

        /// <inheritdoc/>
        public ValueTask Respond2Async(
            NodeId conditionId,
            int selectedResponse,
            LocalizedText comment,
            CancellationToken ct = default)
            => new DialogConditionTypeClient(m_session, conditionId, m_telemetry)
                .Respond2Async(selectedResponse, comment, ct);
    }
}
