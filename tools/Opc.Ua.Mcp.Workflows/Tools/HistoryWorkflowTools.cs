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
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.Client.Historian;
using Descriptions = Opc.Ua.Mcp.WorkflowParameterDescriptions;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Bounded Part 11 workflows using HistoryClient paging, updates and continuation cleanup.
    /// </summary>
    [McpServerToolType]
    public sealed class HistoryWorkflowTools
    {
        /// <summary>
        /// Creates historian tools with an independently owned historical cursor registry.
        /// </summary>
        public HistoryWorkflowTools(
            OpcUaSessionManager sessions, WorkflowOptions options, HistoryCursorRegistry cursors,
            IWorkflowSessionFactory? scopes = null)
        {
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_cursors = cursors ?? throw new ArgumentNullException(nameof(cursors));
            m_scopes = scopes ?? new WorkflowSessionFactory();
            options.Validate();
        }

        /// <summary>
        /// Reads quality-bearing capability fields and the server's actual aggregate catalogue.
        /// </summary>
        [McpServerTool(Name = "history_capabilities", ReadOnly = true)]
        [Description("Inspect historian support; returns quality/absence and a paged list " +
            "of advertised aggregate NodeIds.")]
        public Task<CallToolResult> CapabilitiesAsync(
            [Description(Descriptions.MaxResults)] int maxResults = 100,
            [Description(Descriptions.Offset)] int offset = 0,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WorkflowJson.Page(m_options, maxResults, offset);
                IWorkflowSessionScope scope = await m_scopes.CaptureAsync(
                    m_sessions.GetSessionInfoOrThrow(sessionName), token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                NodeId node = ObjectIds.HistoryServerCapabilities;
                JsonObject result = await WorkflowJson.PropertiesAsync(session, node,
                    [
                        new QualifiedName(BrowseNames.AccessHistoryDataCapability),
                        new QualifiedName(BrowseNames.AccessHistoryEventsCapability),
                        new QualifiedName(BrowseNames.MaxReturnDataValues),
                        new QualifiedName(BrowseNames.MaxReturnEventValues),
                        new QualifiedName(BrowseNames.InsertDataCapability),
                        new QualifiedName(BrowseNames.ReplaceDataCapability),
                        new QualifiedName(BrowseNames.UpdateDataCapability),
                        new QualifiedName(BrowseNames.DeleteRawCapability),
                        new QualifiedName(BrowseNames.DeleteAtTimeCapability),
                        new QualifiedName(BrowseNames.InsertAnnotationCapability),
                        new QualifiedName(BrowseNames.InsertEventCapability),
                        new QualifiedName(BrowseNames.ReplaceEventCapability),
                        new QualifiedName(BrowseNames.UpdateEventCapability),
                        new QualifiedName(BrowseNames.DeleteEventCapability),
                        new QualifiedName(BrowseNames.ServerTimestampSupported)
                    ], token).ConfigureAwait(false);
                NodeId aggregates = await McpModelAccess.ChildAsync(
                    session, node, new QualifiedName(BrowseNames.AggregateFunctions), token).ConfigureAwait(false);
                result["aggregateFunctionsSupported"] = !aggregates.IsNull;
                result["aggregateFunctions"] = aggregates.IsNull ? null :
                    await McpCompanionTools.PageAsync(session.EnumerateReferencesAsync(
                        aggregates, ReferenceTypeIds.HierarchicalReferences,
                        nodeClassMask: (uint)NodeClass.Object, ct: token),
                        reference => WorkflowJson.Reference(session, reference), offset, maxResults, token)
                        .ConfigureAwait(false);
                return result;
            }, ct);
        }

        /// <summary>
        /// Reads actual historical configuration links and their optional fields.
        /// </summary>
        [McpServerTool(Name = "history_configuration", ReadOnly = true)]
        [Description("Inspect HasHistoricalConfiguration; returns identities, archive bounds, " +
            "aggregate defaults and field quality/absence.")]
        public Task<CallToolResult> ConfigurationAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.MaxResults)] int maxResults = 100,
            [Description(Descriptions.Offset)] int offset = 0,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WorkflowJson.Page(m_options, maxResults, offset);
                IWorkflowSessionScope scope = await m_scopes.CaptureAsync(
                    m_sessions.GetSessionInfoOrThrow(sessionName), token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                NodeId node = WorkflowJson.Node(session, nodeId);
                JsonObject result = await WorkflowJson.AttributesAsync(
                    session, node, [Attributes.Historizing, Attributes.AccessLevel, Attributes.UserAccessLevel], token)
                    .ConfigureAwait(false);
                var configurations = new JsonArray();
                await foreach (ReferenceDescription reference in session.EnumerateReferencesAsync(
                    node, ReferenceTypeIds.HasHistoricalConfiguration, ct: token).ConfigureAwait(false))
                {
                    if (configurations.Count == 8)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "The node advertises more than eight historical configuration objects.");
                    }
                    NodeId configuration = McpModelAccess.ModelNode(session, reference.NodeId);
                    JsonObject properties = await WorkflowJson.PropertiesAsync(session, configuration,
                        [
                            new QualifiedName(BrowseNames.Stepped), new QualifiedName(BrowseNames.Definition),
                            new QualifiedName(BrowseNames.MaxTimeInterval),
                            new QualifiedName(BrowseNames.MinTimeInterval),
                            new QualifiedName(BrowseNames.ExceptionDeviation),
                            new QualifiedName(BrowseNames.ExceptionDeviationFormat),
                            new QualifiedName(BrowseNames.StartOfArchive),
                            new QualifiedName(BrowseNames.StartOfOnlineArchive),
                            new QualifiedName(BrowseNames.ServerTimestampSupported),
                            new QualifiedName(BrowseNames.MaxTimeStoredValues),
                            new QualifiedName(BrowseNames.MaxCountStoredValues),
                            new QualifiedName(BrowseNames.SortByEventFields)
                        ], token).ConfigureAwait(false);
                    properties["identity"] = WorkflowJson.Reference(session, reference);
                    NodeId aggregate = await McpModelAccess.ChildAsync(session, configuration,
                        new QualifiedName(BrowseNames.AggregateConfiguration), token).ConfigureAwait(false);
                    properties["aggregateConfiguration"] = aggregate.IsNull ? null :
                        await WorkflowJson.PropertiesAsync(session, aggregate,
                            [
                                new QualifiedName(BrowseNames.TreatUncertainAsBad),
                                new QualifiedName(BrowseNames.PercentDataBad),
                                new QualifiedName(BrowseNames.PercentDataGood),
                                new QualifiedName(BrowseNames.UseSlopedExtrapolation)
                            ], token).ConfigureAwait(false);
                    NodeId eventTypes = await McpModelAccess.ChildAsync(
                        session, configuration, new QualifiedName(BrowseNames.EventTypes), token).ConfigureAwait(false);
                    properties["eventTypesSupported"] = !eventTypes.IsNull;
                    properties["eventTypes"] = eventTypes.IsNull ? null :
                        await McpCompanionTools.PageAsync(session.EnumerateReferencesAsync(
                            eventTypes, ReferenceTypeIds.Organizes, ct: token),
                            reference => WorkflowJson.Reference(session, reference), offset, maxResults, token)
                            .ConfigureAwait(false);
                    configurations.Add((JsonNode)properties);
                }
                result["supported"] = configurations.Count != 0;
                result["configurations"] = configurations;
                return result;
            }, ct);
        }

        /// <summary>
        /// Reads raw values using the SDK's continuation-owning enumerator.
        /// </summary>
        [McpServerTool(Name = "history_read_raw", ReadOnly = true)]
        [Description("Read raw history (reverse ranges allowed); returns bounded values with " +
            "quality/timestamps and optional cursor.")]
        public Task<CallToolResult> ReadRawAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.StartTime)] string startTime,
            [Description(Descriptions.EndTime)] string endTime,
            [Description(Descriptions.ReturnBounds)] bool returnBounds = false,
            [Description(Descriptions.MaxResults)] int maxResults = 100,
            [Description(Descriptions.RetainCursor)] bool retainCursor = false,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return ReadValuesAsync(nodeId, startTime, endTime, modified: false, returnBounds,
                maxResults, retainCursor, sessionName, ct);
        }

        /// <summary>
        /// Reads the modification audit trail without discarding its per-value modification metadata.
        /// </summary>
        [McpServerTool(Name = "history_read_modified", ReadOnly = true)]
        [Description("Read modified samples; returns DataValues, ModificationInfo (time/user/update kind) " +
            "and optional cursor.")]
        public Task<CallToolResult> ReadModifiedAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.StartTime)] string startTime,
            [Description(Descriptions.EndTime)] string endTime,
            [Description(Descriptions.MaxResults)] int maxResults = 100,
            [Description(Descriptions.RetainCursor)] bool retainCursor = false,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return ReadValuesAsync(nodeId, startTime, endTime, modified: true, returnBounds: false,
                maxResults, retainCursor, sessionName, ct);
        }

        /// <summary>
        /// Reads a bounded explicit set of source timestamps.
        /// </summary>
        [McpServerTool(Name = "history_read_at_time", ReadOnly = true)]
        [Description("Read requested instants using useSimpleBounds; returns ordered quality-bearing values " +
            "and optional cursor.")]
        public Task<CallToolResult> ReadAtTimeAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.Timestamps)] ArrayOf<string> timestamps,
            [Description(Descriptions.UseSimpleBounds)] bool useSimpleBounds = false,
            [Description(Descriptions.MaxResults)] int maxResults = 100,
            [Description(Descriptions.RetainCursor)] bool retainCursor = false,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WorkflowJson.Page(m_options, maxResults);
                ArrayOf<DateTime> times = Times(timestamps);
                OpcUaSessionManager.SessionInfo info = m_sessions.GetSessionInfoOrThrow(sessionName);
                return await m_cursors.ReadAsync(info, (session, cancellation) =>
                    ProjectAsync(Client(session).ReadAtTimeAsync(
                        WorkflowJson.Node(session, nodeId), times, useSimpleBounds,
                        timestampsToReturn: TimestampsToReturn.Both, cancellationToken: cancellation),
                        value => WorkflowJson.EncodeDataValue(value, session.MessageContext), cancellation),
                    maxResults, retainCursor, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Requires a real advertised aggregate and a bounded number of processing intervals.
        /// </summary>
        [McpServerTool(Name = "history_read_processed", ReadOnly = true)]
        [Description("Read processed history using an advertised aggregate within host period limits; " +
            "returns quality-bearing values and optional cursor.")]
        public Task<CallToolResult> ReadProcessedAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.AggregateId)] string aggregateId,
            [Description(Descriptions.StartTime)] string startTime,
            [Description(Descriptions.EndTime)] string endTime,
            [Description(Descriptions.ProcessingIntervalMilliseconds)] double processingIntervalMilliseconds,
            [Description(Descriptions.MaxResults)] int maxResults = 100,
            [Description(Descriptions.RetainCursor)] bool retainCursor = false,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WorkflowJson.Page(m_options, maxResults);
                DateTime start = WorkflowJson.Utc(startTime);
                DateTime end = WorkflowJson.Utc(endTime);
                if (!double.IsFinite(processingIntervalMilliseconds) ||
                    processingIntervalMilliseconds <= 0 ||
                    Math.Ceiling(Math.Abs((end - start).TotalMilliseconds) / processingIntervalMilliseconds) >
                        m_options.MaxItems)
                {
                    throw new ArgumentException(
                        "Use a positive finite processing interval and a bounded period count.");
                }
                OpcUaSessionManager.SessionInfo info = m_sessions.GetSessionInfoOrThrow(sessionName);
                return await m_cursors.ReadAsync(info, (session, cancellation) =>
                    ProcessedSourceAsync(session, nodeId, aggregateId, start, end,
                        processingIntervalMilliseconds, cancellation),
                    maxResults, retainCursor, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Reads bounded historical event fields in caller-selected, namespace-exact order.
        /// </summary>
        [McpServerTool(Name = "history_read_events", ReadOnly = true)]
        [Description("Read selected historical event fields; returns ordered typed rows and optional cursor, " +
            "without arbitrary filters.")]
        public Task<CallToolResult> ReadEventsAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.StartTime)] string startTime,
            [Description(Descriptions.EndTime)] string endTime,
            [Description(Descriptions.Select)] ArrayOf<WorkflowEventFieldInput> select,
            [Description(Descriptions.Paths)] ArrayOf<WorkflowEventPathInput> paths = default,
            [Description(Descriptions.MaxResults)] int maxResults = 100,
            [Description(Descriptions.RetainCursor)] bool retainCursor = false,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                var input = new WorkflowHistoryEventsInput { Select = select, Paths = paths };
                WorkflowJson.Page(m_options, maxResults);
                WorkflowValues.ValidateInline(input, m_options);
                DateTime start = WorkflowJson.Utc(startTime);
                DateTime end = WorkflowJson.Utc(endTime);
                OpcUaSessionManager.SessionInfo info = m_sessions.GetSessionInfoOrThrow(sessionName);
                return await m_cursors.ReadAsync(info, (session, cancellation) =>
                    ProjectAsync(Client(session).ReadEventsAsync(WorkflowJson.Node(session, nodeId),
                        start, end, WorkflowValues.Filter(session, input.Select, input.Paths), (uint)maxResults,
                        cancellationToken: cancellation),
                        value => WorkflowJson.Encode(value, session.MessageContext), cancellation),
                    maxResults, retainCursor, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Reads the actual Annotations property's raw DataValues instead of discarding quality.
        /// </summary>
        [McpServerTool(Name = "history_read_annotations", ReadOnly = true)]
        [Description("Read Annotations with quality/timestamps and optional cursor; " +
            "returns unsupported if the property is absent.")]
        public Task<CallToolResult> ReadAnnotationsAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.StartTime)] string startTime,
            [Description(Descriptions.EndTime)] string endTime,
            [Description(Descriptions.MaxResults)] int maxResults = 100,
            [Description(Descriptions.RetainCursor)] bool retainCursor = false,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WorkflowJson.Page(m_options, maxResults);
                DateTime start = WorkflowJson.Utc(startTime);
                DateTime end = WorkflowJson.Utc(endTime);
                OpcUaSessionManager.SessionInfo info = m_sessions.GetSessionInfoOrThrow(sessionName);
                return await m_cursors.ReadAsync(info, (session, cancellation) =>
                    AnnotationSourceAsync(session, nodeId, start, end, (uint)maxResults, cancellation),
                    maxResults, retainCursor, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Inserts explicit data values.
        /// </summary>
        [McpServerTool(Name = "history_insert_values", Destructive = true)]
        [Description("Insert history; existing samples are refused. Returns ordered statuses, " +
            "not atomic; no automatic retry.")]
        public Task<CallToolResult> InsertValuesAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.Values)] ArrayOf<WorkflowDataValueInput> values,
            [Description(Descriptions.ArrayElements)] ArrayOf<WorkflowArrayElementInput> arrayElements = default,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return UpdateValuesAsync(nodeId, new WorkflowDataValuesInput
            {
                Values = values,
                ArrayElements = arrayElements
            }, PerformUpdateType.Insert, sessionName, ct);
        }

        /// <summary>
        /// Replaces existing data values.
        /// </summary>
        [McpServerTool(Name = "history_replace_values", Destructive = true)]
        [Description("Replace samples by source timestamp, never insert missing ones; " +
            "returns ordered per-item statuses. Not atomic.")]
        public Task<CallToolResult> ReplaceValuesAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.Values)] ArrayOf<WorkflowDataValueInput> values,
            [Description(Descriptions.ArrayElements)] ArrayOf<WorkflowArrayElementInput> arrayElements = default,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return UpdateValuesAsync(nodeId, new WorkflowDataValuesInput
            {
                Values = values,
                ArrayElements = arrayElements
            }, PerformUpdateType.Replace, sessionName, ct);
        }

        /// <summary>
        /// Explicitly upserts a bounded historical data batch.
        /// </summary>
        [McpServerTool(Name = "history_update_values", Destructive = true)]
        [Description("Update values (insert missing, replace existing); " +
            "returns ordered per-item statuses and atomic=false.")]
        public Task<CallToolResult> UpdateValuesAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.Values)] ArrayOf<WorkflowDataValueInput> values,
            [Description(Descriptions.ArrayElements)] ArrayOf<WorkflowArrayElementInput> arrayElements = default,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return UpdateValuesAsync(nodeId, new WorkflowDataValuesInput
            {
                Values = values,
                ArrayElements = arrayElements
            }, PerformUpdateType.Update, sessionName, ct);
        }

        /// <summary>
        /// Inserts a bounded event batch using the SDK's standard mandatory-field validation.
        /// </summary>
        [McpServerTool(Name = "history_insert_events", Destructive = true)]
        [Description("Insert event rows with EventId/EventType/Time selectors; " +
            "returns statuses in eventIndex order. Not atomic.")]
        public Task<CallToolResult> InsertEventsAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.Select)] ArrayOf<WorkflowEventFieldInput> select,
            [Description(Descriptions.Events)] ArrayOf<WorkflowHistoryEventInput> events,
            [Description(Descriptions.Paths)] ArrayOf<WorkflowEventPathInput> paths = default,
            [Description(Descriptions.ArrayElements)] ArrayOf<WorkflowArrayElementInput> arrayElements = default,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return UpdateEventsAsync(nodeId, new WorkflowHistoryEventsInput
            {
                Select = select,
                Events = events,
                Paths = paths,
                ArrayElements = arrayElements
            }, PerformUpdateType.Insert, sessionName, ct);
        }

        /// <summary>
        /// Replaces a bounded event batch.
        /// </summary>
        [McpServerTool(Name = "history_replace_events", Destructive = true)]
        [Description("Replace events by EventId, refusing missing occurrences; " +
            "returns statuses in eventIndex order. Not atomic.")]
        public Task<CallToolResult> ReplaceEventsAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.Select)] ArrayOf<WorkflowEventFieldInput> select,
            [Description(Descriptions.Events)] ArrayOf<WorkflowHistoryEventInput> events,
            [Description(Descriptions.Paths)] ArrayOf<WorkflowEventPathInput> paths = default,
            [Description(Descriptions.ArrayElements)] ArrayOf<WorkflowArrayElementInput> arrayElements = default,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return UpdateEventsAsync(nodeId, new WorkflowHistoryEventsInput
            {
                Select = select,
                Events = events,
                Paths = paths,
                ArrayElements = arrayElements
            }, PerformUpdateType.Replace, sessionName, ct);
        }

        /// <summary>
        /// Explicitly upserts historical events.
        /// </summary>
        [McpServerTool(Name = "history_update_events", Destructive = true)]
        [Description("Upsert historical events by EventId; returns statuses in eventIndex order. " +
            "Non-atomic, without automatic retry.")]
        public Task<CallToolResult> UpdateEventsAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.Select)] ArrayOf<WorkflowEventFieldInput> select,
            [Description(Descriptions.Events)] ArrayOf<WorkflowHistoryEventInput> events,
            [Description(Descriptions.Paths)] ArrayOf<WorkflowEventPathInput> paths = default,
            [Description(Descriptions.ArrayElements)] ArrayOf<WorkflowArrayElementInput> arrayElements = default,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return UpdateEventsAsync(nodeId, new WorkflowHistoryEventsInput
            {
                Select = select,
                Events = events,
                Paths = paths,
                ArrayElements = arrayElements
            }, PerformUpdateType.Update, sessionName, ct);
        }

        /// <summary>
        /// Inserts annotations at explicit annotation timestamps.
        /// </summary>
        [McpServerTool(Name = "history_insert_annotations", Destructive = true)]
        [Description("Insert annotations on the variable's Annotations property; " +
            "returns ordered per-item statuses. Not atomic.")]
        public Task<CallToolResult> InsertAnnotationsAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.Annotations)] ArrayOf<WorkflowAnnotationInput> annotations,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return UpdateAnnotationsAsync(nodeId, new WorkflowAnnotationsInput { Annotations = annotations },
                PerformUpdateType.Insert, sessionName, ct);
        }

        /// <summary>
        /// Replaces existing annotations.
        /// </summary>
        [McpServerTool(Name = "history_replace_annotations", Destructive = true)]
        [Description("Replace annotations by exact timestamp without adding missing entries; " +
            "returns ordered statuses. Not atomic.")]
        public Task<CallToolResult> ReplaceAnnotationsAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.Annotations)] ArrayOf<WorkflowAnnotationInput> annotations,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return UpdateAnnotationsAsync(nodeId, new WorkflowAnnotationsInput { Annotations = annotations },
                PerformUpdateType.Replace, sessionName, ct);
        }

        /// <summary>
        /// Explicitly upserts annotations.
        /// </summary>
        [McpServerTool(Name = "history_update_annotations", Destructive = true)]
        [Description("Upsert annotations (insert or replace); returns ordered statuses and atomic=false. " +
            "userName is attribution only.")]
        public Task<CallToolResult> UpdateAnnotationsAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.Annotations)] ArrayOf<WorkflowAnnotationInput> annotations,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return UpdateAnnotationsAsync(nodeId, new WorkflowAnnotationsInput { Annotations = annotations },
                PerformUpdateType.Update, sessionName, ct);
        }

        /// <summary>
        /// Deletes one explicit half-open range of raw or modified history.
        /// </summary>
        [McpServerTool(Name = "history_delete_range", Destructive = true)]
        [Description("Delete raw or modified samples in [startTime,endTime), start<end; " +
            "returns node status. Irreversible, not atomic.")]
        public Task<CallToolResult> DeleteRangeAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.StartTime)] string startTime,
            [Description(Descriptions.EndTime)] string endTime,
            [Description(Descriptions.DeleteModified)] bool deleteModified = false,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                DateTime start = WorkflowJson.Utc(startTime);
                DateTime end = WorkflowJson.Utc(endTime);
                if (start >= end)
                {
                    throw new ArgumentException("A delete range must have startTime before endTime.");
                }
                (IWorkflowSessionScope scope, NodeId node, HistoryClient client) =
                    await OpenMutationAsync(nodeId, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                StatusCode result = await client.DeleteRawAsync(node, start, end, deleteModified, token)
                    .ConfigureAwait(false);
                return WorkflowJson.Update(session, node, [result]);
            }, ct);
        }

        /// <summary>
        /// Deletes data samples at a bounded set of explicit source timestamps.
        /// </summary>
        [McpServerTool(Name = "history_delete_at_time", Destructive = true)]
        [Description("Delete only the supplied sample timestamps; returns ordered per-item statuses. " +
            "No range expansion.")]
        public Task<CallToolResult> DeleteAtTimeAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.Timestamps)] ArrayOf<string> timestamps,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArrayOf<DateTime> times = Times(timestamps);
                (IWorkflowSessionScope scope, NodeId node, HistoryClient client) =
                    await OpenMutationAsync(nodeId, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                return WorkflowJson.Update(session, node,
                    await client.DeleteAtTimeAsync(node, times, token).ConfigureAwait(false));
            }, ct);
        }

        /// <summary>
        /// Deletes only explicitly selected historical event occurrences.
        /// </summary>
        [McpServerTool(Name = "history_delete_events", Destructive = true)]
        [Description("Remove exact historical EventIds; returns ordered per-event statuses, " +
            "without a range delete or retry.")]
        public Task<CallToolResult> DeleteEventsAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.EventIds)] ArrayOf<string> eventIds,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                var input = new WorkflowStringsInput { Values = eventIds };
                WorkflowJson.Page(m_options, input.Values.Count);
                WorkflowValues.ValidateInline(input, m_options);
                var identifiers = new ByteString[input.Values.Count];
                for (int index = 0; index < identifiers.Length; index++)
                {
                    identifiers[index] = WorkflowValues.Bytes(input.Values[index], m_options.MaxInlineBytes);
                    if (identifiers[index].IsEmpty)
                    {
                        throw new ArgumentException("A historical event id must not be empty.");
                    }
                }
                (IWorkflowSessionScope scope, NodeId node, HistoryClient client) =
                    await OpenMutationAsync(nodeId, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                return WorkflowJson.Update(session, node,
                    await client.DeleteEventsAsync(node, identifiers.ToArrayOf(), token).ConfigureAwait(false));
            }, ct);
        }

        /// <summary>
        /// Removes annotations using the standard structure-data Remove operation.
        /// </summary>
        [McpServerTool(Name = "history_delete_annotations", Destructive = true)]
        [Description("Remove annotations by timestamp via UpdateStructureData; " +
            "returns ordered statuses without deleting data samples.")]
        public Task<CallToolResult> DeleteAnnotationsAsync(
            [Description(Descriptions.NodeId)] string nodeId,
            [Description(Descriptions.Timestamps)] ArrayOf<string> timestamps,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArrayOf<DateTime> times = Times(timestamps);
                var annotations = new Annotation[times.Count];
                for (int index = 0; index < annotations.Length; index++)
                {
                    annotations[index] = new Annotation { AnnotationTime = times[index], Message = string.Empty };
                }
                (IWorkflowSessionScope scope, NodeId node, HistoryClient client) =
                    await OpenMutationAsync(nodeId, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                return WorkflowJson.Update(session, node, await UpdateAnnotationPropertyAsync(
                    scope.Access, client, node, annotations.ToArrayOf(), PerformUpdateType.Remove, token)
                    .ConfigureAwait(false));
            }, ct);
        }

        /// <summary>
        /// Continues a caller-owned historical enumeration.
        /// </summary>
        [McpServerTool(Name = "history_next", ReadOnly = true)]
        [Description("Read the next history page on the cursor's original session generation; " +
            "returns items and remaining cursor. Expiry applies.")]
        public Task<CallToolResult> NextAsync(
            [Description(Descriptions.Cursor)] string cursor,
            [Description(Descriptions.MaxResults)] int maxResults = 100,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(
                async token => await m_cursors.NextAsync(cursor, maxResults, token).ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Releases one owned cursor immediately.
        /// </summary>
        [McpServerTool(Name = "history_release", Destructive = false)]
        [Description("Release an owned cursor and its server continuation; returns released. " +
            "Refuses foreign, expired or stale sessions.")]
        public Task<CallToolResult> ReleaseAsync(
            [Description(Descriptions.Cursor)] string cursor, CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(
                async token => await m_cursors.ReleaseAsync(cursor, token).ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Shares only the bounded read plumbing, not caller-selected method dispatch.
        /// </summary>
        private Task<CallToolResult> ReadValuesAsync(
            string nodeId, string startTime, string endTime, bool modified, bool returnBounds,
            int maxResults, bool retainCursor, string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WorkflowJson.Page(m_options, maxResults);
                DateTime start = WorkflowJson.Utc(startTime);
                DateTime end = WorkflowJson.Utc(endTime);
                OpcUaSessionManager.SessionInfo info = m_sessions.GetSessionInfoOrThrow(sessionName);
                return await m_cursors.ReadAsync(info, (session, cancellation) => modified
                    ? ProjectAsync(Client(session).ReadModifiedAsync(
                        WorkflowJson.Node(session, nodeId), start, end, (uint)maxResults,
                        timestampsToReturn: TimestampsToReturn.Both, cancellationToken: cancellation),
                        value => new JsonObject
                        {
                            ["dataValue"] = WorkflowJson.EncodeDataValue(value.Value, session.MessageContext),
                            ["modificationInfo"] = WorkflowJson.Encode(value.Info, session.MessageContext)
                        }, cancellation)
                    : ProjectAsync(Client(session).ReadRawAsync(
                        WorkflowJson.Node(session, nodeId), start, end, (uint)maxResults, returnBounds,
                        timestampsToReturn: TimestampsToReturn.Both, cancellationToken: cancellation),
                        value => WorkflowJson.EncodeDataValue(value, session.MessageContext), cancellation),
                    maxResults, retainCursor, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Converts and validates the complete data batch before sending a single update request.
        /// </summary>
        private Task<CallToolResult> UpdateValuesAsync(
            string nodeId, WorkflowDataValuesInput input, PerformUpdateType operation,
            string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                WorkflowJson.Page(m_options, input.Values.Count);
                WorkflowValues.ValidateInline(input, m_options);
                foreach (WorkflowDataValueInput item in input.Values)
                {
                    ArgumentNullException.ThrowIfNull(item);
                }
                Dictionary<int, JsonElement[]> arrays = WorkflowValues.Arrays(
                    input.Values.Count, index => input.Values[index].ArrayLength, input.ArrayElements, m_options);
                IWorkflowSessionScope scope = await m_scopes.CaptureAsync(
                    m_sessions.GetSessionInfoOrThrow(sessionName), token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo info = scope.Access;
                ISession initial = info.Session;
                var values = new DataValue[input.Values.Count];
                for (int index = 0; index < values.Length; index++)
                {
                    WorkflowDataValueInput item = input.Values[index];
                    ArgumentNullException.ThrowIfNull(item);
                    values[index] = new DataValue(
                        WorkflowValues.Flat(initial, item.Type, item.Value, index, arrays, m_options),
                        new StatusCode(item.StatusCode),
                        WorkflowJson.Utc(item.SourceTimestamp),
                        item.ServerTimestamp is null ? DateTimeUtc.MinValue :
                            (DateTimeUtc)WorkflowJson.Utc(item.ServerTimestamp));
                }
                (ISession session, NodeId node, HistoryClient client) =
                    await MutationAsync(nodeId, info, token).ConfigureAwait(false);
                ArrayOf<StatusCode> statuses = operation switch
                {
                    PerformUpdateType.Insert => await client.InsertAsync(node, values.ToArrayOf(), token)
                        .ConfigureAwait(false),
                    PerformUpdateType.Replace => await client.ReplaceAsync(node, values.ToArrayOf(), token)
                        .ConfigureAwait(false),
                    _ => await client.UpdateAsync(node, values.ToArrayOf(), token).ConfigureAwait(false)
                };
                return WorkflowJson.Update(session, node, statuses);
            }, ct);
        }

        /// <summary>
        /// Converts event rows before invoking the SDK's mandatory-field/type validation and update.
        /// </summary>
        private Task<CallToolResult> UpdateEventsAsync(
            string nodeId, WorkflowHistoryEventsInput input, PerformUpdateType operation,
            string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                WorkflowJson.Page(m_options, input.Events.Count);
                WorkflowValues.ValidateInline(input, m_options);
                IWorkflowSessionScope scope = await m_scopes.CaptureAsync(
                    m_sessions.GetSessionInfoOrThrow(sessionName), token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo info = scope.Access;
                ISession initial = info.Session;
                EventFilter filter = WorkflowValues.Filter(initial, input.Select, input.Paths);
                int eventCount = 0;
                foreach (WorkflowHistoryEventInput row in input.Events)
                {
                    ArgumentNullException.ThrowIfNull(row);
                    if (row.EventIndex < 0 ||
                        row.EventIndex >= m_options.MaxItems ||
                        row.FieldIndex < 0 ||
                        row.FieldIndex >= filter.SelectClauses.Count)
                    {
                        throw new ArgumentException("An eventIndex or fieldIndex is outside the declared batch.");
                    }
                    eventCount = Math.Max(eventCount, row.EventIndex + 1);
                }
                if (eventCount * filter.SelectClauses.Count != input.Events.Count)
                {
                    throw new ArgumentException("Each contiguous eventIndex must contain every selected field once.");
                }
                Dictionary<int, JsonElement[]> arrays = WorkflowValues.Arrays(
                    input.Events.Count, index => input.Events[index].ArrayLength, input.ArrayElements, m_options);
                var fieldValues = new Variant[eventCount][];
                for (int index = 0; index < fieldValues.Length; index++)
                {
                    fieldValues[index] = new Variant[filter.SelectClauses.Count];
                }
                var assigned = new HashSet<(int Event, int Field)>();
                for (int index = 0; index < input.Events.Count; index++)
                {
                    WorkflowHistoryEventInput row = input.Events[index];
                    if (!assigned.Add((row.EventIndex, row.FieldIndex)))
                    {
                        throw new ArgumentException("Each eventIndex/fieldIndex pair must be unique.");
                    }
                    fieldValues[row.EventIndex][row.FieldIndex] = WorkflowValues.Flat(
                        initial, row.Type, row.Value, index, arrays, m_options);
                }
                var events = new HistoryEventFieldList[eventCount];
                for (int index = 0; index < events.Length; index++)
                {
                    events[index] = new HistoryEventFieldList { EventFields = fieldValues[index].ToArrayOf() };
                }
                (ISession session, NodeId node, HistoryClient client) =
                    await MutationAsync(nodeId, info, token).ConfigureAwait(false);
                ArrayOf<StatusCode> statuses = operation switch
                {
                    PerformUpdateType.Insert => await client.InsertEventsAsync(node, filter, events.ToArrayOf(), token)
                        .ConfigureAwait(false),
                    PerformUpdateType.Replace => await client.ReplaceEventsAsync(
                        node, filter, events.ToArrayOf(), token)
                        .ConfigureAwait(false),
                    _ => await client.UpdateEventsAsync(node, filter, events.ToArrayOf(), token).ConfigureAwait(false)
                };
                return WorkflowJson.Update(session, node, statuses);
            }, ct);
        }

        /// <summary>
        /// Converts annotation timestamps and limits all text before dispatch.
        /// </summary>
        private Task<CallToolResult> UpdateAnnotationsAsync(
            string nodeId, WorkflowAnnotationsInput input, PerformUpdateType operation,
            string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                WorkflowJson.Page(m_options, input.Annotations.Count);
                WorkflowValues.ValidateInline(input, m_options);
                var annotations = new Annotation[input.Annotations.Count];
                for (int index = 0; index < annotations.Length; index++)
                {
                    WorkflowAnnotationInput item = input.Annotations[index];
                    ArgumentNullException.ThrowIfNull(item);
                    WorkflowJson.Text(item.Message, 4096, empty: true);
                    if (item.UserName is not null)
                    {
                        WorkflowJson.Text(item.UserName, 256, empty: true);
                    }
                    annotations[index] = new Annotation
                    {
                        AnnotationTime = WorkflowJson.Utc(item.AnnotationTime),
                        Message = item.Message,
                        UserName = item.UserName
                    };
                }
                (IWorkflowSessionScope scope, NodeId node, HistoryClient client) =
                    await OpenMutationAsync(nodeId, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                return WorkflowJson.Update(session, node, await UpdateAnnotationPropertyAsync(
                    scope.Access, client, node, annotations.ToArrayOf(), operation, token)
                    .ConfigureAwait(false));
            }, ct);
        }

        /// <summary>
        /// Guards the resolved annotation target and passes that same target to the existing SDK update operation.
        /// </summary>
        private async ValueTask<ArrayOf<StatusCode>> UpdateAnnotationPropertyAsync(
            OpcUaSessionManager.SessionInfo info, HistoryClient client, NodeId variable,
            ArrayOf<Annotation> annotations,
            PerformUpdateType operation, CancellationToken ct)
        {
            ISession session = info.Session;
            NodeId node = await WorkflowJson.ChildAsync(session, variable, BrowseNames.Annotations, ct)
                .ConfigureAwait(false);
            await m_sessions.UaOperationGuard.AuthorizeTargetAsync(info, node, genericCall: false, ct)
                .ConfigureAwait(false);
            var values = new DataValue[annotations.Count];
            for (int index = 0; index < values.Length; index++)
            {
                Annotation annotation = annotations[index];
                values[index] = new DataValue(Variant.FromStructure(annotation), StatusCodes.Good,
                    annotation.AnnotationTime, DateTimeUtc.MinValue);
            }
            return await client.UpdateStructureDataAsync(node, operation, values.ToArrayOf(), ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Captures a single exact session and applies shared semantic target guards before updates.
        /// </summary>
        private async ValueTask<(IWorkflowSessionScope Scope, NodeId Node, HistoryClient Client)>
            OpenMutationAsync(
            string nodeId, string? sessionName, CancellationToken ct)
        {
            IWorkflowSessionScope scope = await m_scopes.CaptureAsync(
                m_sessions.GetSessionInfoOrThrow(sessionName), ct).ConfigureAwait(false);
            try
            {
                (_, NodeId node, HistoryClient client) = await MutationAsync(nodeId, scope.Access, ct)
                    .ConfigureAwait(false);
                return (scope, node, client);
            }
            catch
            {
                await scope.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Reuses a captured session when typed values were resolved against its namespace table.
        /// </summary>
        private async ValueTask<(ISession Session, NodeId Node, HistoryClient Client)> MutationAsync(
            string nodeId, OpcUaSessionManager.SessionInfo info, CancellationToken ct)
        {
            NodeId node = WorkflowJson.Node(info.Session, nodeId);
            await m_sessions.UaOperationGuard.AuthorizeTargetAsync(info, node, genericCall: false, ct)
                .ConfigureAwait(false);
            return (info.Session, node, Client(info.Session));
        }

        /// <summary>
        /// Configures finite SDK paging; the registry additionally limits each active request's time.
        /// </summary>
        private HistoryClient Client(ISession session)
        {
            return new HistoryClient(session, new HistoryClientOptions
            {
                DefaultMaxValuesPerNode = (uint)m_options.MaxItems,
                MaxDecodedPageItems = m_options.MaxItems,
                MaxPagesPerRead = m_options.MaxHistoryPages,
                DefaultTimestampsToReturn = TimestampsToReturn.Both,
                MaxReadDuration = m_options.HistoryCursorLifetime
            });
        }

        /// <summary>
        /// Checks aggregate support and reads within one guarded service generation.
        /// </summary>
        private async IAsyncEnumerable<JsonNode?> ProcessedSourceAsync(
            ISession session, string nodeId, string aggregateId, DateTime start, DateTime end, double interval,
            [EnumeratorCancellation] CancellationToken ct)
        {
            NodeId node = WorkflowJson.Node(session, nodeId);
            NodeId aggregate = WorkflowJson.Node(session, aggregateId);
            await RequireAggregateAsync(session, aggregate, ct).ConfigureAwait(false);
            await foreach (DataValue value in Client(session).ReadProcessedAsync(
                node, aggregate, start, end, interval, timestampsToReturn: TimestampsToReturn.Both,
                cancellationToken: ct).ConfigureAwait(false))
            {
                yield return WorkflowJson.EncodeDataValue(value, session.MessageContext);
            }
        }

        /// <summary>
        /// Resolves and reads annotations through the same retained, guarded service session.
        /// </summary>
        private async IAsyncEnumerable<JsonNode?> AnnotationSourceAsync(
            ISession session, string nodeId, DateTime start, DateTime end, uint maximum,
            [EnumeratorCancellation] CancellationToken ct)
        {
            NodeId node = WorkflowJson.Node(session, nodeId);
            NodeId annotations = await WorkflowJson.ChildAsync(session, node, BrowseNames.Annotations, ct)
                .ConfigureAwait(false);
            await foreach (DataValue value in Client(session).ReadRawAsync(
                annotations, start, end, maximum, timestampsToReturn: TimestampsToReturn.Both,
                cancellationToken: ct).ConfigureAwait(false))
            {
                yield return WorkflowJson.EncodeDataValue(value, session.MessageContext);
            }
        }

        /// <summary>
        /// Converts a bounded ordered timestamp batch.
        /// </summary>
        private ArrayOf<DateTime> Times(ArrayOf<string> timestamps)
        {
            var input = new WorkflowStringsInput { Values = timestamps };
            WorkflowJson.Page(m_options, input.Values.Count);
            WorkflowValues.ValidateInline(input, m_options);
            var times = new DateTime[input.Values.Count];
            for (int index = 0; index < times.Length; index++)
            {
                times[index] = WorkflowJson.Utc(input.Values[index]);
            }
            return times.ToArrayOf();
        }

        /// <summary>
        /// Confirms a real supported aggregate without substituting a built-in list.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static async ValueTask RequireAggregateAsync(ISession session, NodeId aggregate, CancellationToken ct)
        {
            NodeId folder = await WorkflowJson.ChildAsync(
                session, ObjectIds.HistoryServerCapabilities, BrowseNames.AggregateFunctions, ct).ConfigureAwait(false);
            int examined = 0;
            await foreach (ReferenceDescription reference in session.EnumerateReferencesAsync(
                folder, ReferenceTypeIds.HierarchicalReferences, nodeClassMask: (uint)NodeClass.Object, ct: ct)
                .ConfigureAwait(false))
            {
                if (++examined > 500)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "Aggregate capability discovery exceeded the host ceiling.");
                }
                if (McpModelAccess.ModelNode(session, reference.NodeId) == aggregate)
                {
                    return;
                }
            }
            throw new ServiceResultException(StatusCodes.BadAggregateNotSupported,
                "The aggregate is not advertised by this server's history capabilities.");
        }

        /// <summary>
        /// Preserves lazy SDK paging and deterministic enumerator disposal across JSON projection.
        /// </summary>
        /// <typeparam name="T">The SDK historical value type.</typeparam>
        private static async IAsyncEnumerable<JsonNode?> ProjectAsync<T>(
            IAsyncEnumerable<T> source, Func<T, JsonNode?> project,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (T value in source.WithCancellation(ct).ConfigureAwait(false))
            {
                yield return project(value);
            }
        }

        private readonly OpcUaSessionManager m_sessions;
        private readonly WorkflowOptions m_options;
        private readonly HistoryCursorRegistry m_cursors;
        private readonly IWorkflowSessionFactory m_scopes;
    }
}
