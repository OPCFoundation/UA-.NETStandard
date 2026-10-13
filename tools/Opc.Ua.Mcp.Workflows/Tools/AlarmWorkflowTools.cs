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
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Descriptions = Opc.Ua.Mcp.WorkflowParameterDescriptions;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Occurrence-aware condition commands and bounded observations, without owning shared subscriptions.
    /// </summary>
    [McpServerToolType]
    public sealed class AlarmWorkflowTools
    {
        /// <summary>
        /// Creates alarm tools with an injectable adapter to the existing managed streaming subscription.
        /// </summary>
        public AlarmWorkflowTools(
            OpcUaSessionManager sessions, ClientWorkflowOptions options, IClientWorkflowStreams? streams = null,
            IClientWorkflowSessionFactory? scopes = null)
        {
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_streams = streams ?? new ClientWorkflowStreams();
            m_scopes = scopes ?? new ClientWorkflowSessionFactory();
            options.Validate();
        }

        /// <summary>
        /// Reads a browsable condition's properties and optional command capabilities.
        /// </summary>
        [McpServerTool(Name = "alarms_inspect", ReadOnly = true)]
        [Description("Inspect a browsable ConditionType instance: occurrence/source identity, state and optional " +
            "command support/executability. Returns quality-bearing properties and method capabilities. " +
            "Example: conditionId='ns=2;s=HighTemperature'. " +
            "Virtual conditions may support event-based commands without an addressable metadata node.")]
        public Task<CallToolResult> InspectAsync(
            [Description(Descriptions.ConditionId)] string conditionId,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                IClientWorkflowSessionScope scope = await m_scopes.CaptureAsync(
                    m_sessions.GetSessionInfoOrThrow(sessionName), token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                NodeId node = WorkflowJson.Node(session, conditionId);
                _ = await McpModelAccess.RequireTypeAsync(session, node, ObjectTypeIds.ConditionType, token)
                    .ConfigureAwait(false);
                JsonObject result = await WorkflowJson.PropertiesAsync(session, node,
                    [
                        new QualifiedName(BrowseNames.EventId), new QualifiedName(BrowseNames.BranchId),
                        new QualifiedName(BrowseNames.SourceNode), new QualifiedName(BrowseNames.SourceName),
                        new QualifiedName(BrowseNames.ConditionName), new QualifiedName(BrowseNames.Retain),
                        new QualifiedName(BrowseNames.EnabledState), new QualifiedName(BrowseNames.ActiveState),
                        new QualifiedName(BrowseNames.AckedState), new QualifiedName(BrowseNames.ConfirmedState),
                        new QualifiedName(BrowseNames.SuppressedState),
                        new QualifiedName(BrowseNames.OutOfServiceState),
                        new QualifiedName(BrowseNames.MaxTimeShelved), new QualifiedName(BrowseNames.ResponseOptionSet),
                        new QualifiedName(BrowseNames.DialogState)
                    ], token).ConfigureAwait(false);
                var methods = new JsonObject();
                foreach (string method in s_methods)
                {
                    NodeId child = await McpModelAccess.ChildAsync(
                        session, node, new QualifiedName(method), token).ConfigureAwait(false);
                    methods[method] = child.IsNull
                        ? new JsonObject { ["supported"] = false }
                        : await WorkflowJson.AttributesAsync(
                            session, child, [Attributes.Executable, Attributes.UserExecutable], token)
                            .ConfigureAwait(false);
                }
                result["methods"] = methods;
                return result;
            }, ct);
        }

        /// <summary>
        /// Uses the existing typed AlarmClient group-membership operation.
        /// </summary>
        [McpServerTool(Name = "alarms_group_memberships", ReadOnly = true)]
        [Description("Read the condition's GetGroupMemberships result through AlarmClient. Returns exact group " +
            "identities and bounded live-array paging. Example: maxResults=25. Optional-method refusal is preserved.")]
        public Task<CallToolResult> GroupMembershipsAsync(
            [Description(Descriptions.ConditionId)] string conditionId,
            [Description(Descriptions.MaxResults)] int maxResults = 100,
            [Description(Descriptions.Offset)] int offset = 0,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WorkflowJson.Page(m_options, maxResults, offset);
                IClientWorkflowSessionScope scope = await m_scopes.CaptureAsync(
                    m_sessions.GetSessionInfoOrThrow(sessionName), token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                var client = new AlarmClient(session, session.MessageContext.Telemetry);
                ArrayOf<NodeId> groups = await client.GetGroupMembershipsAsync(
                    WorkflowJson.Node(session, conditionId), token).ConfigureAwait(false);
                return McpCompanionTools.Page(groups, node => WorkflowJson.Identity(session, node), offset, maxResults);
            }, ct);
        }

        /// <summary>
        /// Collects a finite live condition observation.
        /// </summary>
        [McpServerTool(Name = "alarms_observe", ReadOnly = true)]
        [Description("Observe condition notifications for a bounded duration/count, default 1 second/100 events, " +
            "maximum host 30 seconds/500. Returns exact ConditionId/BranchId/EventId/source, typed optional fields " +
            "and explicit stop/overflow metadata. Example: notifierId='i=2253', durationMilliseconds=1000.")]
        public Task<CallToolResult> ObserveAsync(
            [Description(Descriptions.NotifierId)] string notifierId = "i=2253",
            [Description(Descriptions.DurationMilliseconds)] int durationMilliseconds = 1000,
            [Description(Descriptions.MaxResults)] int maxResults = 100,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return ObserveAsync(notifierId, durationMilliseconds, maxResults, false, sessionName, ct);
        }

        /// <summary>
        /// Collects a bounded refresh snapshot delimited by actual refresh marker events.
        /// </summary>
        [McpServerTool(Name = "alarms_refresh", ReadOnly = true)]
        [Description("Create one owned event item, request ConditionRefresh after it is ready, and collect a bounded " +
            "snapshot until RefreshEnd. Returns condition events and completion/overflow metadata. " +
            "Example: durationMilliseconds=1000. Missing end marker, queue overflow or " +
            "count/timeout limits explicitly mark an incomplete snapshot. The shared subscription is not disposed.")]
        public Task<CallToolResult> RefreshAsync(
            [Description(Descriptions.NotifierId)] string notifierId = "i=2253",
            [Description(Descriptions.DurationMilliseconds)] int durationMilliseconds = 1000,
            [Description(Descriptions.MaxResults)] int maxResults = 100,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return ObserveAsync(notifierId, durationMilliseconds, maxResults, true, sessionName, ct);
        }

        /// <summary>
        /// Acknowledges one exact occurrence and reports a safely refreshed event id when addressable.
        /// </summary>
        [McpServerTool(Name = "alarms_acknowledge", Destructive = true)]
        [Description("Acknowledge the selected ConditionId/EventId occurrence; EventId is base64, not an alarm name. " +
            "Example: eventId='AQIDBA==', branchId='i=0', comment='Investigating'. Returns actual status plus a " +
            "refreshed EventId only when the same branch is verified; otherwise observe before confirming.")]
        public Task<CallToolResult> AcknowledgeAsync(
            [Description(Descriptions.ConditionId)] string conditionId,
            [Description(Descriptions.EventId)] string eventId,
            [Description(Descriptions.Comment)] string comment,
            [Description(Descriptions.BranchId)] string branchId = "i=0",
            [Description(Descriptions.Locale)] string? locale = null,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return OccurrenceAsync(conditionId, eventId, comment, branchId, locale,
                MethodIds.AcknowledgeableConditionType_Acknowledge, refresh: true, sessionName, ct);
        }

        /// <summary>
        /// Confirms the selected occurrence with its current EventId.
        /// </summary>
        [McpServerTool(Name = "alarms_confirm", Destructive = true)]
        [Description("Confirm one exact condition occurrence using its current base64 EventId, preferably refreshed " +
            "after acknowledgement. Example: eventId='AQIDBQ=='. " +
            "Returns actual status; stale EventIds are not retried.")]
        public Task<CallToolResult> ConfirmAsync(
            [Description(Descriptions.ConditionId)] string conditionId,
            [Description(Descriptions.EventId)] string eventId,
            [Description(Descriptions.Comment)] string comment,
            [Description(Descriptions.BranchId)] string branchId = "i=0",
            [Description(Descriptions.Locale)] string? locale = null,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return OccurrenceAsync(conditionId, eventId, comment, branchId, locale,
                MethodIds.AcknowledgeableConditionType_Confirm, refresh: false, sessionName, ct);
        }

        /// <summary>
        /// Adds a localized comment to one event occurrence.
        /// </summary>
        [McpServerTool(Name = "alarms_comment", Destructive = true)]
        [Description("Add one localized comment to the exact ConditionId/EventId occurrence. Example: " +
            "comment='Checked sensor', locale='en'. Returns actual status without acknowledging or confirming.")]
        public Task<CallToolResult> CommentAsync(
            [Description(Descriptions.ConditionId)] string conditionId,
            [Description(Descriptions.EventId)] string eventId,
            [Description(Descriptions.Comment)] string comment,
            [Description(Descriptions.BranchId)] string branchId = "i=0",
            [Description(Descriptions.Locale)] string? locale = null,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return OccurrenceAsync(conditionId, eventId, comment, branchId, locale,
                MethodIds.ConditionType_AddComment, refresh: false, sessionName, ct);
        }

        /// <summary>
        /// Enables the selected condition.
        /// </summary>
        [McpServerTool(Name = "alarms_enable", Destructive = true)]
        [Description("Enable one exact condition with the standard Enable method. Example: conditionId='ns=2;s=A'. " +
            "Returns actual status; optional/server access restrictions remain authoritative.")]
        public Task<CallToolResult> EnableAsync(
            [Description(Descriptions.ConditionId)] string conditionId,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CommandAsync(conditionId, MethodIds.ConditionType_Enable, [], sessionName, ct);
        }

        /// <summary>
        /// Disables the selected condition.
        /// </summary>
        [McpServerTool(Name = "alarms_disable", Destructive = true)]
        [Description("Disable one exact condition; does not acknowledge, suppress or shelve it. " +
            "Example: conditionId='ns=2;s=A'. Returns the server's actual status.")]
        public Task<CallToolResult> DisableAsync(
            [Description(Descriptions.ConditionId)] string conditionId,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CommandAsync(conditionId, MethodIds.ConditionType_Disable, [], sessionName, ct);
        }

        /// <summary>
        /// Silences the selected alarm where supported.
        /// </summary>
        [McpServerTool(Name = "alarms_silence", Destructive = true)]
        [Description("Call the optional standard Silence method on one alarm. Example: conditionId='ns=2;s=A'. " +
            "Returns actual status; unsupported functionality is never emulated.")]
        public Task<CallToolResult> SilenceAsync(
            [Description(Descriptions.ConditionId)] string conditionId,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CommandAsync(conditionId, MethodIds.AlarmConditionType_Silence, [], sessionName, ct);
        }

        /// <summary>
        /// Suppresses one alarm, selecting the comment overload only when explicitly supplied.
        /// </summary>
        [McpServerTool(Name = "alarms_suppress", Destructive = true)]
        [Description("Suppress one alarm via Suppress or, when comment is supplied, Suppress2. " +
            "Example: comment='Maintenance', locale='en'. Returns actual optional-method status.")]
        public Task<CallToolResult> SuppressAsync(
            [Description(Descriptions.ConditionId)] string conditionId,
            [Description(Descriptions.OptionalComment)] string? comment = null,
            [Description(Descriptions.Locale)] string? locale = null,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CommentCommandAsync(conditionId, comment, locale,
                MethodIds.AlarmConditionType_Suppress, MethodIds.AlarmConditionType_Suppress2, sessionName, ct);
        }

        /// <summary>
        /// Unsuppresses one alarm.
        /// </summary>
        [McpServerTool(Name = "alarms_unsuppress", Destructive = true)]
        [Description("Unsuppress one alarm using the supported standard method, with optional explicit comment. " +
            "Example: comment='Maintenance complete'. Returns status; no other state is changed implicitly.")]
        public Task<CallToolResult> UnsuppressAsync(
            [Description(Descriptions.ConditionId)] string conditionId,
            [Description(Descriptions.OptionalComment)] string? comment = null,
            [Description(Descriptions.Locale)] string? locale = null,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CommentCommandAsync(conditionId, comment, locale,
                MethodIds.AlarmConditionType_Unsuppress, MethodIds.AlarmConditionType_Unsuppress2, sessionName, ct);
        }

        /// <summary>
        /// Removes one alarm from service.
        /// </summary>
        [McpServerTool(Name = "alarms_out_of_service", Destructive = true)]
        [Description("Remove one alarm from service using RemoveFromService[/2], with an optional localized comment. " +
            "Example: comment='Device isolated'. Returns actual status, not an assumed state transition.")]
        public Task<CallToolResult> OutOfServiceAsync(
            [Description(Descriptions.ConditionId)] string conditionId,
            [Description(Descriptions.OptionalComment)] string? comment = null,
            [Description(Descriptions.Locale)] string? locale = null,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CommentCommandAsync(conditionId, comment, locale,
                MethodIds.AlarmConditionType_RemoveFromService, MethodIds.AlarmConditionType_RemoveFromService2,
                sessionName, ct);
        }

        /// <summary>
        /// Places one alarm back in service.
        /// </summary>
        [McpServerTool(Name = "alarms_in_service", Destructive = true)]
        [Description("Place one alarm in service using PlaceInService[/2]. Example: comment='Device restored'. " +
            "Returns actual server status; no automatic reset or acknowledgement.")]
        public Task<CallToolResult> InServiceAsync(
            [Description(Descriptions.ConditionId)] string conditionId,
            [Description(Descriptions.OptionalComment)] string? comment = null,
            [Description(Descriptions.Locale)] string? locale = null,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CommentCommandAsync(conditionId, comment, locale,
                MethodIds.AlarmConditionType_PlaceInService, MethodIds.AlarmConditionType_PlaceInService2,
                sessionName, ct);
        }

        /// <summary>
        /// Resets one alarm.
        /// </summary>
        [McpServerTool(Name = "alarms_reset", Destructive = true)]
        [Description("Reset one alarm using its optional Reset[/2] method. Example: comment='Cause cleared'. " +
            "Returns actual status and preserves unsupported/invalid-state refusals.")]
        public Task<CallToolResult> ResetAsync(
            [Description(Descriptions.ConditionId)] string conditionId,
            [Description(Descriptions.OptionalComment)] string? comment = null,
            [Description(Descriptions.Locale)] string? locale = null,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CommentCommandAsync(conditionId, comment, locale,
                MethodIds.AlarmConditionType_Reset, MethodIds.AlarmConditionType_Reset2, sessionName, ct);
        }

        /// <summary>
        /// Shelves an alarm through its actual ShelvingState child for a finite explicit period.
        /// </summary>
        [McpServerTool(Name = "alarms_timed_shelve", Destructive = true)]
        [Description("TimedShelve the alarm's actual ShelvingState child for a positive finite duration in ms. " +
            "Example: shelvingTimeMilliseconds=60000. Server MaxTimeShelved is authoritative. " +
            "Returns exact shelving target and method status.")]
        public Task<CallToolResult> TimedShelveAsync(
            [Description(Descriptions.ConditionId)] string conditionId,
            [Description(Descriptions.ShelvingTimeMilliseconds)] double shelvingTimeMilliseconds,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                if (!double.IsFinite(shelvingTimeMilliseconds) || shelvingTimeMilliseconds <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(shelvingTimeMilliseconds));
                }
                return await ShelveAsync(conditionId, MethodIds.ShelvedStateMachineType_TimedShelve,
                    [Variant.From(shelvingTimeMilliseconds)], sessionName, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Requests one-shot shelving on the actual shelving state machine.
        /// </summary>
        [McpServerTool(Name = "alarms_one_shot_shelve", Destructive = true)]
        [Description("OneShotShelve an alarm's actual ShelvingState child. Example: conditionId='ns=2;s=A'. " +
            "Returns exact target/status; optional absence and invalid-state refusals are preserved.")]
        public Task<CallToolResult> OneShotShelveAsync(
            [Description(Descriptions.ConditionId)] string conditionId,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token => await ShelveAsync(
                conditionId, MethodIds.ShelvedStateMachineType_OneShotShelve, [], sessionName, token)
                .ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Unshelves the selected alarm.
        /// </summary>
        [McpServerTool(Name = "alarms_unshelve", Destructive = true)]
        [Description("Unshelve the actual ShelvingState child, without resetting or acknowledging the alarm. " +
            "Example: conditionId='ns=2;s=A'. Returns actual target and method status.")]
        public Task<CallToolResult> UnshelveAsync(
            [Description(Descriptions.ConditionId)] string conditionId,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token => await ShelveAsync(
                conditionId, MethodIds.ShelvedStateMachineType_Unshelve, [], sessionName, token)
                .ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Selects a dialog response, optionally using the supported comment overload.
        /// </summary>
        [McpServerTool(Name = "alarms_respond_dialog", Destructive = true)]
        [Description("Respond to one DialogCondition with its nonnegative selectedResponse index. Explicit comment " +
            "uses Respond2; omission uses Respond. Example: selectedResponse=1. " +
            "Returns actual status; the server validates response options and dialog state.")]
        public Task<CallToolResult> RespondDialogAsync(
            [Description(Descriptions.ConditionId)] string conditionId,
            [Description(Descriptions.SelectedResponse)] int selectedResponse,
            [Description(Descriptions.OptionalComment)] string? comment = null,
            [Description(Descriptions.Locale)] string? locale = null,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentOutOfRangeException.ThrowIfNegative(selectedResponse);
                ArrayOf<Variant> arguments = comment is null
                    ? [Variant.From(selectedResponse)]
                    : [Variant.From(selectedResponse), Variant.From(BuildComment(comment, locale))];
                return await CallAsync(conditionId, comment is null
                    ? MethodIds.DialogConditionType_Respond : MethodIds.DialogConditionType_Respond2,
                    arguments, sessionName, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Collects real refresh marker events and explicit shared-stream overflow counters.
        /// </summary>
        private Task<CallToolResult> ObserveAsync(
            string notifierId, int durationMilliseconds, int maximum, bool refresh,
            string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WorkflowJson.Page(m_options, maximum);
                ArgumentOutOfRangeException.ThrowIfLessThan(durationMilliseconds, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(
                    durationMilliseconds, m_options.MaxDurationMilliseconds);
                OpcUaSessionManager.SessionInfo registration = m_sessions.GetSessionInfoOrThrow(sessionName);
                IClientWorkflowSessionScope scope = await m_scopes.CaptureAsync(registration, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                NodeId notifier = WorkflowJson.Node(session, notifierId);
                IStreamingSubscription streaming = m_streams.GetStreaming(registration.Session);
                long? droppedBefore = (streaming as StreamingSubscription)?.DroppedNotificationCount;
                using var window = CancellationTokenSource.CreateLinkedTokenSource(token);
                window.CancelAfter(durationMilliseconds);
                var events = new JsonArray();
                bool started = false;
                bool ended = false;
                bool serverOverflow = false;
                string stoppedBy = "sourceCompleted";
                try
                {
                    await foreach (EventNotification notification in m_streams.Subscribe(
                        streaming, notifier, Filter(), new MonitoringOptions
                        {
                            QueueSize = (uint)maximum,
                            DiscardOldest = true
                        }, refresh, window.Token).WithCancellation(window.Token).ConfigureAwait(false))
                    {
                        token.ThrowIfCancellationRequested();
                        if (scope.Invalidated.IsCompleted)
                        {
                            throw new ServiceResultException(StatusCodes.BadSessionIdInvalid,
                                "The observation's session generation changed.");
                        }
                        NodeId type = notification.Fields.Count > 3 &&
                            notification.Fields[3].TryGetValue(out NodeId eventType) ? eventType : NodeId.Null;
                        if (type == ObjectTypeIds.RefreshStartEventType)
                        {
                            started = true;
                            continue;
                        }
                        if (type == ObjectTypeIds.RefreshEndEventType && refresh)
                        {
                            ended = started;
                            stoppedBy = ended ? "refreshEnd" : "refreshEndWithoutStart";
                            break;
                        }
                        if (type == ObjectTypeIds.EventQueueOverflowEventType)
                        {
                            serverOverflow = true;
                        }
                        if (refresh && !started)
                        {
                            continue;
                        }
                        events.Add((JsonNode)Event(session, notifier, notification));
                        if (events.Count == maximum)
                        {
                            stoppedBy = "itemLimit";
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (
                    window.IsCancellationRequested && !token.IsCancellationRequested)
                {
                    stoppedBy = "durationLimit";
                }
                token.ThrowIfCancellationRequested();
                if (scope.Invalidated.IsCompleted)
                {
                    throw new ServiceResultException(StatusCodes.BadSessionIdInvalid,
                        "The observation's session generation changed.");
                }
                long? droppedAfter = (streaming as StreamingSubscription)?.DroppedNotificationCount;
                long? dropped = droppedAfter.HasValue && droppedBefore.HasValue
                    ? Math.Max(0, droppedAfter.Value - droppedBefore.Value) : null;
                bool overflow = serverOverflow || dropped > 0;
                return new JsonObject
                {
                    ["items"] = events,
                    ["stoppedBy"] = stoppedBy,
                    ["refreshStarted"] = refresh ? started : null,
                    ["refreshEnded"] = refresh ? ended : null,
                    ["complete"] = (refresh ? ended : stoppedBy == "sourceCompleted") && !overflow,
                    ["overflow"] = overflow,
                    ["serverQueueOverflow"] = serverOverflow,
                    ["localDroppedNotifications"] = dropped,
                    ["localOverflowScope"] = dropped.HasValue ? "sharedSubscription" : "unknown"
                };
            }, ct);
        }

        /// <summary>
        /// Uses a fixed select contract which also carries the EventType needed for refresh/overflow markers.
        /// </summary>
        internal static EventFilter Filter()
        {
            var clauses = new SimpleAttributeOperand[s_fields.Length];
            for (int index = 0; index < clauses.Length; index++)
            {
                (string name, NodeId type) = s_fields[index];
                clauses[index] = new SimpleAttributeOperand
                {
                    TypeDefinitionId = type,
                    BrowsePath = name == "ConditionId" ? [] : [new QualifiedName(name)],
                    AttributeId = name == "ConditionId" ? Attributes.NodeId : Attributes.Value
                };
            }
            return new EventFilter
            {
                SelectClauses = clauses.ToArrayOf(),
                WhereClause = new ContentFilter
                {
                    Elements =
                    [
                        Or(1, 2), Or(3, 4), Or(5, 6),
                        OfType(ObjectTypeIds.ConditionType),
                        OfType(ObjectTypeIds.RefreshStartEventType),
                        OfType(ObjectTypeIds.RefreshEndEventType),
                        OfType(ObjectTypeIds.EventQueueOverflowEventType)
                    ]
                }
            };
        }

        /// <summary>
        /// Builds a fixed binary filter node; callers never provide executable filter expressions.
        /// </summary>
        private static ContentFilterElement Or(uint left, uint right)
        {
            return new ContentFilterElement
            {
                FilterOperator = FilterOperator.Or,
                FilterOperands =
                [
                    new ExtensionObject(new ElementOperand { Index = left }),
                    new ExtensionObject(new ElementOperand { Index = right })
                ]
            };
        }

        /// <summary>
        /// Selects a standard event type and its subtypes.
        /// </summary>
        private static ContentFilterElement OfType(NodeId type)
        {
            return new ContentFilterElement
            {
                FilterOperator = FilterOperator.OfType,
                FilterOperands = [new ExtensionObject(new LiteralOperand { Value = Variant.From(type) })]
            };
        }

        /// <summary>
        /// Keeps event identity/source beside the complete typed field values, including bad optional fields.
        /// </summary>
        private static JsonObject Event(ISession session, NodeId notifier, EventNotification notification)
        {
            var fields = new JsonObject();
            for (int index = 0; index < s_fields.Length; index++)
            {
                fields[s_fields[index].Name] = index < notification.Fields.Count
                    ? WorkflowJson.EncodeVariant(notification.Fields[index], session.MessageContext) : null;
            }
            return new JsonObject
            {
                ["notifier"] = WorkflowJson.Identity(session, notifier),
                ["partitionServerId"] = notification.PartitionServerId,
                ["fields"] = fields,
                ["fieldCountMatches"] = notification.Fields.Count == s_fields.Length
            };
        }

        /// <summary>
        /// Builds a selected occurrence command and never treats the branch id as a substitute for EventId.
        /// </summary>
        private Task<CallToolResult> OccurrenceAsync(
            string conditionId, string eventId, string comment, string branchId, string? locale,
            ExpandedNodeId method, bool refresh, string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ByteString occurrence = WorkflowValues.Bytes(eventId, m_options.MaxInlineBytes);
                if (occurrence.IsEmpty)
                {
                    throw new ArgumentException("An occurrence EventId must not be empty.");
                }
                IClientWorkflowSessionScope scope = await m_scopes.CaptureAsync(
                    m_sessions.GetSessionInfoOrThrow(sessionName), token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                NodeId node = WorkflowJson.Node(session, conditionId);
                WorkflowJson.Text(branchId);
                var expandedBranch = ExpandedNodeId.Parse(branchId);
                var branch = ExpandedNodeId.ToNodeId(expandedBranch, session.NamespaceUris);
                if (expandedBranch.ServerIndex != 0 ||
                    (!expandedBranch.IsNull && branch.IsNull) ||
                    session.NamespaceUris.GetString(branch.NamespaceIndex) is null)
                {
                    throw new ArgumentException("The branch must use an actual namespace in the selected session.");
                }
                var proxy = new AcknowledgeableConditionTypeClient(session, node, session.MessageContext.Telemetry);
                CallMethodResult call = await proxy.CallWithResultAsync(
                    method, [Variant.From(occurrence), Variant.From(BuildComment(comment, locale))], token)
                    .ConfigureAwait(false);
                JsonObject result = WorkflowJson.Method(session, node, call);
                result["requestedBranchId"] = branch.ToString();
                result["eventId"] = eventId;
                if (refresh && !StatusCode.IsBad(call.StatusCode))
                {
                    await RefreshEventIdAsync(session, node, branch, occurrence, result).ConfigureAwait(false);
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Fetches post-ack metadata without allowing late cancellation or an optional read failure to mask a commit.
        /// </summary>
        private static async ValueTask RefreshEventIdAsync(
            ISession session, NodeId node, NodeId branch, ByteString previous, JsonObject result)
        {
            result["observeBeforeConfirm"] = true;
            using var bounded = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try
            {
                (NodeId branchNode, DataValue branchValue) = await McpModelAccess.PropertyAsync(
                    session, node, new QualifiedName(BrowseNames.BranchId), bounded.Token).ConfigureAwait(false);
                (NodeId eventNode, DataValue eventValue) = await McpModelAccess.PropertyAsync(
                    session, node, new QualifiedName(BrowseNames.EventId), bounded.Token).ConfigureAwait(false);
                if (!branchNode.IsNull &&
                    !eventNode.IsNull &&
                    !StatusCode.IsBad(branchValue.StatusCode) &&
                    !StatusCode.IsBad(eventValue.StatusCode) &&
                    branchValue.WrappedValue.TryGetValue(out NodeId actualBranch) &&
                    actualBranch == branch &&
                    eventValue.WrappedValue.TryGetValue(out ByteString next) &&
                    !next.IsEmpty &&
                    next != previous)
                {
                    result["refreshedEventId"] = Convert.ToBase64String(next.Span);
                    result["observeBeforeConfirm"] = false;
                }
            }
            catch (Exception exception) when (exception is ServiceResultException or OperationCanceledException or
                InvalidOperationException or ObjectDisposedException)
            {
                result["eventIdRefreshUnavailable"] = true;
            }
        }

        /// <summary>
        /// Validates comment text before selecting a fixed optional overload.
        /// </summary>
        private Task<CallToolResult> CommentCommandAsync(
            string conditionId, string? comment, string? locale,
            ExpandedNodeId withoutComment, ExpandedNodeId withComment,
            string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token => await CallAsync(
                conditionId, comment is null ? withoutComment : withComment,
                comment is null ? [] : [Variant.From(BuildComment(comment, locale))], sessionName, token)
                .ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Executes one fixed zero-argument or typed command.
        /// </summary>
        private Task<CallToolResult> CommandAsync(
            string conditionId, ExpandedNodeId method, ArrayOf<Variant> arguments,
            string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token => await CallAsync(
                conditionId, method, arguments, sessionName, token).ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Retains virtual-condition support by letting the standard server method validate the occurrence target.
        /// </summary>
        private async ValueTask<JsonObject> CallAsync(
            string conditionId, ExpandedNodeId method, ArrayOf<Variant> arguments,
            string? sessionName, CancellationToken ct)
        {
            IClientWorkflowSessionScope scope = await m_scopes.CaptureAsync(
                m_sessions.GetSessionInfoOrThrow(sessionName), ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
            ISession session = scope.Access.Session;
            NodeId node = WorkflowJson.Node(session, conditionId);
            var proxy = new AlarmConditionTypeClient(session, node, session.MessageContext.Telemetry);
            return WorkflowJson.Method(session, node,
                await proxy.CallWithResultAsync(method, arguments, ct).ConfigureAwait(false));
        }

        /// <summary>
        /// Resolves the ShelvingState object rather than sending state-machine methods to its parent alarm.
        /// </summary>
        private async ValueTask<JsonObject> ShelveAsync(
            string conditionId, ExpandedNodeId method, ArrayOf<Variant> arguments,
            string? sessionName, CancellationToken ct)
        {
            IClientWorkflowSessionScope scope = await m_scopes.CaptureAsync(
                m_sessions.GetSessionInfoOrThrow(sessionName), ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
            ISession session = scope.Access.Session;
            NodeId condition = WorkflowJson.Node(session, conditionId);
            NodeId shelving = await WorkflowJson.ChildAsync(session, condition, BrowseNames.ShelvingState, ct)
                .ConfigureAwait(false);
            _ = await McpModelAccess.RequireTypeAsync(session, shelving, ObjectTypeIds.ShelvedStateMachineType, ct)
                .ConfigureAwait(false);
            var proxy = new ShelvedStateMachineTypeClient(session, shelving, session.MessageContext.Telemetry);
            return WorkflowJson.Method(session, shelving,
                await proxy.CallWithResultAsync(method, arguments, ct).ConfigureAwait(false));
        }

        /// <summary>
        /// Preserves explicit empty comments and locales.
        /// </summary>
        private static LocalizedText BuildComment(string text, string? locale)
        {
            WorkflowJson.Text(text, 4096, empty: true);
            if (locale is not null)
            {
                WorkflowJson.Text(locale, 64, empty: true);
            }
            return new LocalizedText(locale, text);
        }

        private readonly OpcUaSessionManager m_sessions;
        private readonly ClientWorkflowOptions m_options;
        private readonly IClientWorkflowStreams m_streams;
        private readonly IClientWorkflowSessionFactory m_scopes;

        private static readonly string[] s_methods =
        [
            BrowseNames.Enable, BrowseNames.Disable, BrowseNames.AddComment, BrowseNames.Acknowledge,
            BrowseNames.Confirm, BrowseNames.Silence, BrowseNames.Suppress, BrowseNames.Suppress2,
            BrowseNames.Unsuppress, BrowseNames.Unsuppress2, BrowseNames.RemoveFromService,
            BrowseNames.RemoveFromService2, BrowseNames.PlaceInService, BrowseNames.PlaceInService2,
            BrowseNames.Reset, BrowseNames.Reset2, BrowseNames.Respond, BrowseNames.Respond2
        ];

        private static readonly (string Name, NodeId Type)[] s_fields =
        [
            ("ConditionId", ObjectTypeIds.ConditionType),
            (BrowseNames.BranchId, ObjectTypeIds.ConditionType),
            (BrowseNames.EventId, ObjectTypeIds.BaseEventType),
            (BrowseNames.EventType, ObjectTypeIds.BaseEventType),
            (BrowseNames.SourceNode, ObjectTypeIds.BaseEventType),
            (BrowseNames.SourceName, ObjectTypeIds.BaseEventType),
            (BrowseNames.Time, ObjectTypeIds.BaseEventType),
            (BrowseNames.Message, ObjectTypeIds.BaseEventType),
            (BrowseNames.Severity, ObjectTypeIds.BaseEventType),
            (BrowseNames.Retain, ObjectTypeIds.ConditionType),
            (BrowseNames.ConditionName, ObjectTypeIds.ConditionType),
            (BrowseNames.EnabledState, ObjectTypeIds.ConditionType),
            (BrowseNames.AckedState, ObjectTypeIds.AcknowledgeableConditionType),
            (BrowseNames.ConfirmedState, ObjectTypeIds.AcknowledgeableConditionType),
            (BrowseNames.ActiveState, ObjectTypeIds.AlarmConditionType),
            (BrowseNames.SuppressedState, ObjectTypeIds.AlarmConditionType),
            (BrowseNames.OutOfServiceState, ObjectTypeIds.AlarmConditionType)
        ];
    }
}
