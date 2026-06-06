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


using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using ChannelCloseReason = Opc.Ua.ClientChannelManager.ChannelCloseReason;

namespace Opc.Ua
{
    internal sealed class ClientChannelManagerDiagnostics
    {
        public IReadOnlyList<ManagedChannelDiagnostic> GetDiagnostics(ChannelEntry[] snapshot)
        {
            var diagnostics = new ManagedChannelDiagnostic[snapshot.Length];
            for (int i = 0; i < snapshot.Length; i++)
            {
                diagnostics[i] = snapshot[i].GetDiagnosticSnapshot();
            }

            return diagnostics;
        }

        private const string kReconnectOutcomeSuccess = "success";
        private const string kChannelManagerDiagnosticsName = "Opc.Ua.ChannelManager";
        private const string kReconnectActivityName = "OpcUaChannelReconnect";

        public Activity? StartReconnectActivity(ChannelEntry entry)
        {
            Activity? activity = s_activitySource.StartActivity(
                kReconnectActivityName,
                ActivityKind.Internal);
            activity?.SetTag("endpoint", entry.EndpointUrl);
            activity?.SetTag("reverse", entry.IsReverse);
            s_eventSource.ReconnectStarted(entry.EndpointUrl, 0);
            return activity;
        }

        public void CompleteReconnectActivity(
            Activity? activity,
            ChannelEntry entry,
            int attemptCount,
            string outcome,
            ServiceResult? error)
        {
            activity?.SetTag("endpoint", entry.EndpointUrl);
            activity?.SetTag("attempt.count", attemptCount);
            activity?.SetTag("outcome", outcome);
            if (error != null)
            {
                activity?.SetTag("error.status_code", GetStatusCode(error));
                activity?.SetTag("error.message", error.ToString());
            }

            if (outcome == kReconnectOutcomeSuccess)
            {
                s_eventSource.ReconnectCompleted(entry.EndpointUrl, attemptCount, outcome);
                return;
            }

            s_eventSource.ReconnectFailed(
                entry.EndpointUrl,
                attemptCount,
                outcome,
                GetStatusCode(error),
                GetErrorMessage(error));
        }

        public void EmitReconnectFailed(
            ChannelEntry entry,
            int attempt,
            string outcome,
            ServiceResult? error)
        {
            s_eventSource.ReconnectFailed(
                entry.EndpointUrl,
                attempt,
                outcome,
                GetStatusCode(error),
                GetErrorMessage(error));
        }

        public void EmitStateChanged(ChannelEntry entry, ChannelStateChange change)
        {
            s_eventSource.StateChanged(
                entry.EndpointUrl,
                change.PreviousState.ToString(),
                change.NewState.ToString(),
                change.ReconnectAttempt,
                GetStatusCode(change.Error),
                GetErrorMessage(change.Error));
        }

        public void EmitChannelOpened(ChannelEntry entry)
        {
            s_eventSource.ChannelOpened(
                entry.EndpointUrl,
                entry.IsReverse,
                entry.RefCount,
                entry.ParticipantCount);
        }

        public void EmitChannelClosed(ChannelEntry entry, ChannelCloseReason reason)
        {
            s_eventSource.ChannelClosed(
                entry.EndpointUrl,
                GetCloseReason(reason),
                entry.RefCount,
                entry.ParticipantCount);
        }

        public void EmitParticipantAttached(
            ChannelEntry entry,
            string participantId,
            int refCount,
            int participantCount)
        {
            s_eventSource.ParticipantAttached(
                entry.EndpointUrl,
                participantId,
                refCount,
                participantCount);
        }

        public void EmitParticipantDetached(
            ChannelEntry entry,
            string participantId,
            int refCount,
            int participantCount)
        {
            s_eventSource.ParticipantDetached(
                entry.EndpointUrl,
                participantId,
                refCount,
                participantCount);
        }

        private static string GetStatusCode(ServiceResult? error)
        {
            return error?.StatusCode.ToString() ?? string.Empty;
        }

        private static string GetErrorMessage(ServiceResult? error)
        {
            return error?.ToString() ?? string.Empty;
        }

        private static string GetCloseReason(ChannelCloseReason reason)
        {
            return reason switch
            {
                ChannelCloseReason.LeaseReleased => "lease-released",
                ChannelCloseReason.ManagerDisposed => "manager-disposed",
                ChannelCloseReason.Faulted => "faulted",
                _ => "faulted"
            };
        }

        private sealed class ChannelManagerEventSource : EventSource
        {
            public const int ChannelOpenedId = 1;
            public const int ChannelClosedId = ChannelOpenedId + 1;
            public const int StateChangedId = ChannelClosedId + 1;
            public const int ReconnectStartedId = StateChangedId + 1;
            public const int ReconnectCompletedId = ReconnectStartedId + 1;
            public const int ReconnectFailedId = ReconnectCompletedId + 1;
            public const int ParticipantAttachedId = ReconnectFailedId + 1;
            public const int ParticipantDetachedId = ParticipantAttachedId + 1;

            public ChannelManagerEventSource()
                : base(kChannelManagerDiagnosticsName)
            {
            }

            [Event(
                ChannelOpenedId,
                Message = "Channel opened. Endpoint={0}, Reverse={1}, Refcount={2}, Participants={3}",
                Level = EventLevel.Informational)]
            public void ChannelOpened(
                string endpoint,
                bool reverse,
                int refcount,
                int participantCount)
            {
                WriteEvent(ChannelOpenedId, endpoint, reverse, refcount, participantCount);
            }

            [Event(
                ChannelClosedId,
                Message = "Channel closed. Endpoint={0}, Reason={1}, Refcount={2}, Participants={3}",
                Level = EventLevel.Informational)]
            public void ChannelClosed(
                string endpoint,
                string reason,
                int refcount,
                int participantCount)
            {
                WriteEvent(ChannelClosedId, endpoint, reason, refcount, participantCount);
            }

            [Event(
                StateChangedId,
                Message = "State changed. Endpoint={0}, Previous={1}, New={2}, Attempt={3}, Status={4}",
                Level = EventLevel.Informational)]
            public void StateChanged(
                string endpoint,
                string previousState,
                string newState,
                int reconnectAttempt,
                string statusCode,
                string errorMessage)
            {
                WriteEvent(
                    StateChangedId,
                    endpoint,
                    previousState,
                    newState,
                    reconnectAttempt,
                    statusCode,
                    errorMessage);
            }

            [Event(
                ReconnectStartedId,
                Message = "Reconnect started. Endpoint={0}, AttemptCount={1}",
                Level = EventLevel.Informational)]
            public void ReconnectStarted(string endpoint, int attemptCount)
            {
                WriteEvent(ReconnectStartedId, endpoint, attemptCount);
            }

            [Event(
                ReconnectCompletedId,
                Message = "Reconnect completed. Endpoint={0}, AttemptCount={1}, Outcome={2}",
                Level = EventLevel.Informational)]
            public void ReconnectCompleted(string endpoint, int attemptCount, string outcome)
            {
                WriteEvent(ReconnectCompletedId, endpoint, attemptCount, outcome);
            }

            [Event(
                ReconnectFailedId,
                Message = "Reconnect failed. Endpoint={0}, Attempt={1}, Outcome={2}, Status={3}",
                Level = EventLevel.Warning)]
            public void ReconnectFailed(
                string endpoint,
                int attempt,
                string outcome,
                string statusCode,
                string errorMessage)
            {
                WriteEvent(
                    ReconnectFailedId,
                    endpoint,
                    attempt,
                    outcome,
                    statusCode,
                    errorMessage);
            }

            [Event(
                ParticipantAttachedId,
                Message = "Participant attached. Endpoint={0}, Participant={1}, Refcount={2}, Participants={3}",
                Level = EventLevel.Informational)]
            public void ParticipantAttached(
                string endpoint,
                string participantId,
                int refcount,
                int participantCount)
            {
                WriteEvent(ParticipantAttachedId, endpoint, participantId, refcount, participantCount);
            }

            [Event(
                ParticipantDetachedId,
                Message = "Participant detached. Endpoint={0}, Participant={1}, Refcount={2}, Participants={3}",
                Level = EventLevel.Informational)]
            public void ParticipantDetached(
                string endpoint,
                string participantId,
                int refcount,
                int participantCount)
            {
                WriteEvent(ParticipantDetachedId, endpoint, participantId, refcount, participantCount);
            }
        }

        private static readonly ActivitySource s_activitySource = new(kChannelManagerDiagnosticsName);
        private static readonly ChannelManagerEventSource s_eventSource = new();
    }
}
