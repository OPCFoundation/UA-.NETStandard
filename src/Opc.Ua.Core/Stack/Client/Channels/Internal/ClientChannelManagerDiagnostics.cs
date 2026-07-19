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
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ChannelCloseReason = Opc.Ua.ClientChannelManager.ChannelCloseReason;

namespace Opc.Ua
{
    /// <summary>
    /// Emits OPC UA channel-manager diagnostic signals through Activities and
    /// structured logs for the client channel manager.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tag values surfaced through distributed tracing are restricted to
    /// OPC UA-protocol-level details (<see cref="StatusCode"/>,
    /// <see cref="ServiceResult.SymbolicId"/>,
    /// <see cref="ServiceResult.LocalizedText"/>). Full
    /// <see cref="ServiceResult"/> contents — including
    /// <see cref="ServiceResult.AdditionalInfo"/> and inner .NET exception
    /// data such as messages, file paths and stack traces — are emitted
    /// only via local <c>ILogger</c> debug logs, never through Activity
    /// tags or structured compatibility logs, to avoid leaking internal diagnostics
    /// to external tracing backends.
    /// </para>
    /// </remarks>
    internal sealed class ClientChannelManagerDiagnostics
    {
        public ClientChannelManagerDiagnostics(ILogger logger)
        {
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

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
        private const string kReconnectActivityName = "OpcUaChannelReconnect";

        public Activity? StartReconnectActivity(ChannelEntry entry)
        {
            Activity? activity = s_activitySource.StartActivity(
                kReconnectActivityName,
                ActivityKind.Internal);
            activity?.SetTag("endpoint", entry.EndpointUrl);
            activity?.SetTag("reverse", entry.IsReverse);
            m_logger.ChannelManagerReconnectStarted(entry.EndpointUrl, 0);
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
            string statusCode = string.Empty;
            string errorMessage = string.Empty;
            if (error != null && (activity != null || m_logger.IsEnabled(LogLevel.Warning)))
            {
                statusCode = GetStatusCode(error);
                errorMessage = GetSafeErrorMessage(error);
                activity?.SetTag("error.status_code", statusCode);
                activity?.SetTag("error.message", errorMessage);
            }

            if (outcome == kReconnectOutcomeSuccess)
            {
                m_logger.ChannelManagerReconnectCompleted(entry.EndpointUrl, attemptCount, outcome);
                return;
            }

            m_logger.ChannelManagerReconnectFailed(
                entry.EndpointUrl,
                attemptCount,
                outcome,
                statusCode,
                errorMessage);
        }

        public void EmitReconnectFailed(
            ChannelEntry entry,
            int attempt,
            string outcome,
            ServiceResult? error)
        {
            if (!m_logger.IsEnabled(LogLevel.Warning))
            {
                return;
            }

            m_logger.ChannelManagerReconnectFailed(
                entry.EndpointUrl,
                attempt,
                outcome,
                GetStatusCode(error),
                GetSafeErrorMessage(error));
        }

        public void EmitStateChanged(ChannelEntry entry, ChannelStateChange change)
        {
            if (!m_logger.IsEnabled(LogLevel.Information))
            {
                return;
            }

            m_logger.ChannelManagerStateChanged(
                entry.EndpointUrl,
                change.PreviousState.ToString(),
                change.NewState.ToString(),
                change.ReconnectAttempt,
                GetStatusCode(change.Error),
                GetSafeErrorMessage(change.Error));
        }

        public void EmitChannelOpened(ChannelEntry entry)
        {
            m_logger.ChannelManagerChannelOpened(
                entry.EndpointUrl,
                entry.IsReverse,
                entry.RefCount,
                entry.ParticipantCount);
        }

        public void EmitChannelClosed(ChannelEntry entry, ChannelCloseReason reason)
        {
            if (!m_logger.IsEnabled(LogLevel.Information))
            {
                return;
            }

            m_logger.ChannelManagerChannelClosed(
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
            m_logger.ChannelManagerParticipantAttached(
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
            m_logger.ChannelManagerParticipantDetached(
                entry.EndpointUrl,
                participantId,
                refCount,
                participantCount);
        }

        private static string GetStatusCode(ServiceResult? error)
        {
            return error?.StatusCode.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Returns a SAFE error message suitable for distributed-tracing
        /// tags and structured compatibility logs. Only OPC UA-protocol-level fields
        /// are surfaced: <see cref="ServiceResult.LocalizedText"/> if
        /// present, otherwise <see cref="ServiceResult.SymbolicId"/>. The
        /// full <see cref="ServiceResult"/> (including
        /// <see cref="ServiceResult.AdditionalInfo"/> and inner .NET
        /// exception data) is intentionally never returned here — that
        /// detail is routed through local <c>ILogger.LogDebug</c> only.
        /// </summary>
        private static string GetSafeErrorMessage(ServiceResult? error)
        {
            if (error == null)
            {
                return string.Empty;
            }

            LocalizedText localized = error.LocalizedText;
            if (!localized.IsNull && !string.IsNullOrEmpty(localized.Text))
            {
                return localized.Text!;
            }

            return error.SymbolicId ?? string.Empty;
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

        private static readonly ActivitySource s_activitySource = new(
            CoreEventIds.ChannelManagerCompatibilityCategory);

        private readonly ILogger m_logger;
    }

    /// <summary>
    /// Source-generated compatibility log messages formerly emitted by Opc.Ua.ChannelManager EventSource.
    /// </summary>
    internal static partial class ClientChannelManagerDiagnosticsLog
    {
        [LoggerMessage(
            EventId = CoreEventIds.ChannelManagerChannelOpened,
            EventName = "ChannelOpened",
            Level = LogLevel.Information,
            Message = "Channel opened. Endpoint={Endpoint}, Reverse={Reverse}, " +
                "Refcount={Refcount}, Participants={ParticipantCount}")]
        public static partial void ChannelManagerChannelOpened(
            this ILogger logger,
            string endpoint,
            bool reverse,
            int refcount,
            int participantCount);

        [LoggerMessage(
            EventId = CoreEventIds.ChannelManagerChannelClosed,
            EventName = "ChannelClosed",
            Level = LogLevel.Information,
            Message = "Channel closed. Endpoint={Endpoint}, Reason={Reason}, " +
                "Refcount={Refcount}, Participants={ParticipantCount}")]
        public static partial void ChannelManagerChannelClosed(
            this ILogger logger,
            string endpoint,
            string reason,
            int refcount,
            int participantCount);

        [LoggerMessage(
            EventId = CoreEventIds.ChannelManagerStateChanged,
            EventName = "StateChanged",
            Level = LogLevel.Information,
            Message = "State changed. Endpoint={Endpoint}, Previous={PreviousState}, New={NewState}, " +
                "Attempt={ReconnectAttempt}, Status={StatusCode}, ErrorMessage={ErrorMessage}")]
        public static partial void ChannelManagerStateChanged(
            this ILogger logger,
            string endpoint,
            string previousState,
            string newState,
            int reconnectAttempt,
            string statusCode,
            string errorMessage);

        [LoggerMessage(
            EventId = CoreEventIds.ChannelManagerReconnectStarted,
            EventName = "ReconnectStarted",
            Level = LogLevel.Information,
            Message = "Reconnect started. Endpoint={Endpoint}, AttemptCount={AttemptCount}")]
        public static partial void ChannelManagerReconnectStarted(
            this ILogger logger,
            string endpoint,
            int attemptCount);

        [LoggerMessage(
            EventId = CoreEventIds.ChannelManagerReconnectCompleted,
            EventName = "ReconnectCompleted",
            Level = LogLevel.Information,
            Message = "Reconnect completed. Endpoint={Endpoint}, AttemptCount={AttemptCount}, Outcome={Outcome}")]
        public static partial void ChannelManagerReconnectCompleted(
            this ILogger logger,
            string endpoint,
            int attemptCount,
            string outcome);

        [LoggerMessage(
            EventId = CoreEventIds.ChannelManagerReconnectFailed,
            EventName = "ReconnectFailed",
            Level = LogLevel.Warning,
            Message = "Reconnect failed. Endpoint={Endpoint}, Attempt={Attempt}, Outcome={Outcome}, " +
                "Status={StatusCode}, ErrorMessage={ErrorMessage}")]
        public static partial void ChannelManagerReconnectFailed(
            this ILogger logger,
            string endpoint,
            int attempt,
            string outcome,
            string statusCode,
            string errorMessage);

        [LoggerMessage(
            EventId = CoreEventIds.ChannelManagerParticipantAttached,
            EventName = "ParticipantAttached",
            Level = LogLevel.Information,
            Message = "Participant attached. Endpoint={Endpoint}, Participant={ParticipantId}, " +
                "Refcount={Refcount}, Participants={ParticipantCount}")]
        public static partial void ChannelManagerParticipantAttached(
            this ILogger logger,
            string endpoint,
            string participantId,
            int refcount,
            int participantCount);

        [LoggerMessage(
            EventId = CoreEventIds.ChannelManagerParticipantDetached,
            EventName = "ParticipantDetached",
            Level = LogLevel.Information,
            Message = "Participant detached. Endpoint={Endpoint}, Participant={ParticipantId}, " +
                "Refcount={Refcount}, Participants={ParticipantCount}")]
        public static partial void ChannelManagerParticipantDetached(
            this ILogger logger,
            string endpoint,
            string participantId,
            int refcount,
            int participantCount);
    }
}
