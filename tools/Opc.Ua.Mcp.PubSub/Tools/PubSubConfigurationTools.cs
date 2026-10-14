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
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Explicit standard PubSub model operations on an existing encrypted server session.
    /// </summary>
    [McpServerToolType]
    public sealed partial class PubSubConfigurationTools
    {
        /// <summary>
        /// Creates remote configuration tools without opening a client connection or launching a server.
        /// </summary>
        public PubSubConfigurationTools(
            OpcUaSessionManager sessions, McpPubSubOptions options, McpFileTransfers files, McpNetworkPolicy network)
        {
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_files = files ?? throw new ArgumentNullException(nameof(files));
            m_network = network ?? throw new ArgumentNullException(nameof(network));
            options.Validate();
        }

        /// <summary>
        /// Reads bounded model identity and optional version/state properties.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_inspect", ReadOnly = true)]
        [Description("Inspect remote PublishSubscribe; returns identity, optional configuration version and status. " +
            "Does not activate a runtime.")]
        public Task<CallToolResult> InspectAsync(
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_inspect", ObjectIds.PublishSubscribe.ToString(),
                ObjectTypeIds.PublishSubscribeType, async (session, node, token) =>
                {
                    JsonObject result = await McpPubSubRemote.PropertiesAsync(session, node,
                        [BrowseNames.ConfigurationVersion, BrowseNames.SupportedTransportProfiles], token)
                        .ConfigureAwait(false);
                    var proxy = new PublishSubscribeTypeClient(session, node, session.MessageContext.Telemetry);
                    PubSubStatusTypeClient? status = await proxy.GetStatusAsync(
                        session.MessageContext.Telemetry, token).ConfigureAwait(false);
                    result["status"] = status is null ? null :
                        await McpPubSubRemote.PropertiesAsync(session, status.ObjectId,
                            [BrowseNames.State], token).ConfigureAwait(false);
                    result["serverProcessStarted"] = false;
                    return result;
                }, sessionName, ct);
        }

        /// <summary>
        /// Adds one connection from an explicit typed plan.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_add_connection", Destructive = true)]
        [Description("Add a remote PubSub connection from the typed plan; returns its NodeId. " +
            "No local runtime or atomic multi-component transaction.")]
        public Task<CallToolResult> AddConnectionAsync(
            [Description("Application name, mode, publisher identity and group settings.")]
            PubSubApplicationSettingsInput input,
            [Description("Exact remote transport endpoint, interface and broker topics.")]
            PubSubConnectionInput connection,
            [Description(PubSubMcpFilters.FieldsDescription)]
            ArrayOf<PubSubFieldInput> fields,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            return AddConnectionAsync(input.ToApplication(connection, fields), sessionName, ct);
        }

        /// <summary>
        /// Adds a connection from an existing direct application plan.
        /// </summary>
        public Task<CallToolResult> AddConnectionAsync(
            PubSubApplicationInput input, string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_add_connection", ObjectIds.PublishSubscribe.ToString(),
                ObjectTypeIds.PublishSubscribeType, async (session, node, token) =>
                {
                    PubSubConnectionDataType connection = Configuration(input, session).Connections[0];
                    connection.Name = input.Name;
                    NodeId added = await new PublishSubscribeTypeClient(
                        session, node, session.MessageContext.Telemetry).AddConnectionAsync(connection, token)
                        .ConfigureAwait(false);
                    return Added(added, session);
                }, sessionName, ct);
        }

        /// <summary>
        /// Removes one explicit connection.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_remove_connection", Destructive = true)]
        [Description("Remove a remote connection by NodeId; returns method completion. " +
            "The server controls child removal; no retries.")]
        public Task<CallToolResult> RemoveConnectionAsync(
            [Description("Exact PubSubConnection NodeId to remove, e.g. ns=2;s=Connection1; not its display name.")]
            string connectionId,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_remove_connection", ObjectIds.PublishSubscribe.ToString(),
                ObjectTypeIds.PublishSubscribeType, async (session, node, token) =>
                {
                    NodeId target = await McpPubSubRemote.TargetAsync(
                        session, connectionId, ObjectTypeIds.PubSubConnectionType, token).ConfigureAwait(false);
                    await new PublishSubscribeTypeClient(session, node, session.MessageContext.Telemetry)
                        .RemoveConnectionAsync(target, token).ConfigureAwait(false);
                    return McpPubSubRemote.Completed();
                }, sessionName, ct);
        }

        /// <summary>
        /// Adds one writer group.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_add_writer_group", Destructive = true)]
        [Description("Add a WriterGroup beneath connectionId with exact ids, encoding and security; " +
            "returns the created node.")]
        public Task<CallToolResult> AddWriterGroupAsync(
            [Description("Exact parent PubSubConnection NodeId on the selected server, e.g. ns=2;s=Connection1.")]
            string connectionId,
            [Description("Name of the new WriterGroup, 1..128 characters, e.g. WriterGroup1; not a NodeId.")]
            string name,
            [Description("Publisher identity, group identifiers and security settings.")]
            PubSubApplicationSettingsInput input,
            [Description("Authorized endpoint and native transport settings.")]
            PubSubConnectionInput connection,
            [Description(PubSubMcpFilters.FieldsDescription)]
            ArrayOf<PubSubFieldInput> fields,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            return AddWriterGroupAsync(connectionId, name, input.ToApplication(connection, fields), sessionName, ct);
        }

        /// <summary>
        /// Adds a writer group from an existing direct application plan.
        /// </summary>
        public Task<CallToolResult> AddWriterGroupAsync(
            string connectionId, string name, PubSubApplicationInput input,
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_add_writer_group", connectionId,
                ObjectTypeIds.PubSubConnectionType, async (session, node, token) =>
                {
                    _ = Configuration(input, session);
                    WriterGroupDataType group = McpPubSubConfiguration.WriterGroup(input);
                    group.Name = PubSubInputValidation.Text(name, 128);
                    NodeId added = await new PubSubConnectionTypeClient(session, node, session.MessageContext.Telemetry)
                        .AddWriterGroupAsync(group, token).ConfigureAwait(false);
                    return Added(added, session);
                }, sessionName, ct);
        }

        /// <summary>
        /// Adds one reader group.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_add_reader_group", Destructive = true)]
        [Description("Configure a ReaderGroup using explicit PublisherId filters and metadata, not discovery; " +
            "returns its node.")]
        public Task<CallToolResult> AddReaderGroupAsync(
            [Description("Exact parent PubSubConnection NodeId on the selected server, e.g. ns=2;s=Connection1.")]
            string connectionId,
            [Description("Name of the new ReaderGroup, 1..128 characters, e.g. ReaderGroup1; not a NodeId.")]
            string name,
            [Description("Publisher filters, group identifiers and security settings.")]
            PubSubApplicationSettingsInput input,
            [Description("Authorized endpoint and native transport settings.")]
            PubSubConnectionInput connection,
            [Description(PubSubMcpFilters.FieldsDescription)]
            ArrayOf<PubSubFieldInput> fields,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            return AddReaderGroupAsync(connectionId, name, input.ToApplication(connection, fields), sessionName, ct);
        }

        /// <summary>
        /// Adds a reader group from an existing direct application plan.
        /// </summary>
        public Task<CallToolResult> AddReaderGroupAsync(
            string connectionId, string name, PubSubApplicationInput input,
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_add_reader_group", connectionId,
                ObjectTypeIds.PubSubConnectionType, async (session, node, token) =>
                {
                    _ = Configuration(input, session);
                    ReaderGroupDataType group = McpPubSubConfiguration.ReaderGroup(input, Metadata(input, session));
                    group.Name = PubSubInputValidation.Text(name, 128);
                    return Added(await new PubSubConnectionTypeClient(session, node, session.MessageContext.Telemetry)
                        .AddReaderGroupAsync(group, token).ConfigureAwait(false), session);
                }, sessionName, ct);
        }

        /// <summary>
        /// Removes one explicitly selected group.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_remove_group", Destructive = true)]
        [Description("Remove a WriterGroup or ReaderGroup through its parent connection; returns method completion. " +
            "Server membership checks apply.")]
        public Task<CallToolResult> RemoveGroupAsync(
            [Description("Exact parent PubSubConnection NodeId on the selected server, e.g. ns=2;s=Connection1.")]
            string connectionId,
            [Description("Exact child WriterGroup or ReaderGroup NodeId to remove, e.g. ns=2;s=WriterGroup1.")]
            string groupId,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_remove_group", connectionId,
                ObjectTypeIds.PubSubConnectionType, async (session, node, token) =>
                {
                    await new PubSubConnectionTypeClient(session, node, session.MessageContext.Telemetry)
                        .RemoveGroupAsync(McpPubSubRemote.Node(session, groupId), token).ConfigureAwait(false);
                    return McpPubSubRemote.Completed();
                }, sessionName, ct);
        }

        /// <summary>
        /// Adds a DataSetWriter with explicit native message and broker settings.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_add_writer", Destructive = true)]
        [Description("Attach a DataSetWriter to groupId using an existing PublishedDataSet; returns writer NodeId. " +
            "No implicit dataset creation.")]
        public Task<CallToolResult> AddWriterAsync(
            [Description("Exact parent WriterGroup NodeId on the selected server, e.g. ns=2;s=WriterGroup1.")]
            string groupId,
            [Description("Name of the new DataSetWriter, 1..128 characters, e.g. Writer1; not a NodeId.")]
            string name,
            [Description("Publisher, writer and existing dataset identity.")]
            PubSubApplicationSettingsInput input,
            [Description("Authorized native transport and broker settings.")]
            PubSubConnectionInput connection,
            [Description(PubSubMcpFilters.FieldsDescription)]
            ArrayOf<PubSubFieldInput> fields,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            return AddWriterAsync(groupId, name, input.ToApplication(connection, fields), sessionName, ct);
        }

        /// <summary>
        /// Adds a writer from an existing direct application plan.
        /// </summary>
        public Task<CallToolResult> AddWriterAsync(
            string groupId, string name, PubSubApplicationInput input,
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_add_writer", groupId, ObjectTypeIds.WriterGroupType,
                async (session, node, token) =>
                {
                    _ = Configuration(input, session);
                    DataSetWriterDataType writer = McpPubSubConfiguration.WriterGroup(input).DataSetWriters[0];
                    writer.Name = PubSubInputValidation.Text(name, 128);
                    return Added(await new WriterGroupTypeClient(session, node, session.MessageContext.Telemetry)
                        .AddDataSetWriterAsync(writer, token).ConfigureAwait(false), session);
                }, sessionName, ct);
        }

        /// <summary>
        /// Removes one DataSetWriter.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_remove_writer", Destructive = true)]
        [Description("Remove the selected DataSetWriter from its declared WriterGroup; " +
            "returns method completion or server refusal.")]
        public Task<CallToolResult> RemoveWriterAsync(
            [Description("Exact parent WriterGroup NodeId on the selected server, e.g. ns=2;s=WriterGroup1.")]
            string groupId,
            [Description("Exact child DataSetWriter NodeId to remove, e.g. ns=2;s=Writer1; not a DataSetWriterId.")]
            string writerId,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_remove_writer", groupId, ObjectTypeIds.WriterGroupType,
                async (session, node, token) =>
                {
                    await new WriterGroupTypeClient(session, node, session.MessageContext.Telemetry)
                        .RemoveDataSetWriterAsync(McpPubSubRemote.Node(session, writerId), token).ConfigureAwait(false);
                    return McpPubSubRemote.Completed();
                }, sessionName, ct);
        }

        /// <summary>
        /// Adds a DataSetReader with explicit filters and metadata.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_add_reader", Destructive = true)]
        [Description("Create a remote DataSetReader with exact publisher/group/writer filters; " +
            "returns reader NodeId, not a local subscriber.")]
        public Task<CallToolResult> AddReaderAsync(
            [Description("Exact parent ReaderGroup NodeId on the selected server, e.g. ns=2;s=ReaderGroup1.")]
            string groupId,
            [Description("Name of the new DataSetReader, 1..128 characters, e.g. Reader1; not a NodeId.")]
            string name,
            [Description("Exact publisher, group and writer filters.")]
            PubSubApplicationSettingsInput input,
            [Description("Authorized native transport and broker settings.")]
            PubSubConnectionInput connection,
            [Description(PubSubMcpFilters.FieldsDescription)]
            ArrayOf<PubSubFieldInput> fields,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            return AddReaderAsync(groupId, name, input.ToApplication(connection, fields), sessionName, ct);
        }

        /// <summary>
        /// Adds a reader from an existing direct application plan.
        /// </summary>
        public Task<CallToolResult> AddReaderAsync(
            string groupId, string name, PubSubApplicationInput input,
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_add_reader", groupId, ObjectTypeIds.ReaderGroupType,
                async (session, node, token) =>
                {
                    _ = Configuration(input, session);
                    DataSetReaderDataType reader = McpPubSubConfiguration.ReaderGroup(input, Metadata(input, session))
                        .DataSetReaders[0];
                    reader.Name = PubSubInputValidation.Text(name, 128);
                    return Added(await new ReaderGroupTypeClient(session, node, session.MessageContext.Telemetry)
                        .AddDataSetReaderAsync(reader, token).ConfigureAwait(false), session);
                }, sessionName, ct);
        }

        /// <summary>
        /// Removes one DataSetReader.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_remove_reader", Destructive = true)]
        [Description("Remove the selected DataSetReader from its ReaderGroup; " +
            "returns server completion without retrying.")]
        public Task<CallToolResult> RemoveReaderAsync(
            [Description("Exact parent ReaderGroup NodeId on the selected server, e.g. ns=2;s=ReaderGroup1.")]
            string groupId,
            [Description("Exact child DataSetReader NodeId to remove, e.g. ns=2;s=Reader1; not its display name.")]
            string readerId,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_remove_reader", groupId, ObjectTypeIds.ReaderGroupType,
                async (session, node, token) =>
                {
                    await new ReaderGroupTypeClient(session, node, session.MessageContext.Telemetry)
                        .RemoveDataSetReaderAsync(McpPubSubRemote.Node(session, readerId), token).ConfigureAwait(false);
                    return McpPubSubRemote.Completed();
                }, sessionName, ct);
        }

        /// <summary>
        /// Adds a PublishedDataItems DataSet with per-field server outcomes.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_add_dataset", Destructive = true)]
        [Description("Add a PublishedDataItems DataSet from node/attribute mappings; " +
            "returns configuration version and per-field statuses, possibly partial.")]
        public Task<CallToolResult> AddDataSetAsync(
            [Description("Exact parent DataSetFolder NodeId on the selected server, e.g. ns=2;s=DataSets.")]
            string folderId,
            [Description("Name of the new PublishedDataItems dataset, 1..128 characters, e.g. Line1Data.")]
            string name,
            [Description("Ordered aliases and source NodeIds, e.g. ns=2;s=Count; attributeId defaults to 13 (Value). " +
                "Aliases must be unique; promoted marks fields for NetworkMessage metadata.")]
            ArrayOf<PubSubVariableInput> variables,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return AddDataSetAsync(folderId, name, new PubSubVariablesInput { Variables = variables }, sessionName, ct);
        }

        /// <summary>
        /// Adds a dataset from an existing direct variable mapping.
        /// </summary>
        public Task<CallToolResult> AddDataSetAsync(
            string folderId, string name, PubSubVariablesInput input,
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_add_dataset", folderId, ObjectTypeIds.DataSetFolderType,
                async (session, node, token) =>
                {
                    ArrayOf<PublishedVariableDataType> variables = McpPubSubRemote.Variables(session, input, m_options);
                    (NodeId added, ConfigurationVersionDataType version, ArrayOf<StatusCode> statuses) =
                        await new DataSetFolderTypeClient(session, node, session.MessageContext.Telemetry)
                            .AddPublishedDataItemsAsync(PubSubInputValidation.Text(name, 128),
                                new ArrayOf<string>(input.Variables.ToList().Select(value => value.Name).ToArray()),
                                new ArrayOf<ushort>(input.Variables.ToList()
                                    .Select(value => (ushort)(value.Promoted ? 1 : 0))
                                    .ToArray()), variables, token).ConfigureAwait(false);
                    JsonObject result = Added(added, session);
                    result["configurationVersion"] = McpCompanionJson.Encode(version, session.MessageContext);
                    McpPubSubRemote.Statuses(result, statuses);
                    return result;
                }, sessionName, ct);
        }

        /// <summary>
        /// Removes one published DataSet.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_remove_dataset", Destructive = true)]
        [Description("Remove a published DataSet from folderId; returns method completion. " +
            "The server validates remaining writer references.")]
        public Task<CallToolResult> RemoveDataSetAsync(
            [Description("Exact parent DataSetFolder NodeId on the selected server, e.g. ns=2;s=DataSets.")]
            string folderId,
            [Description("Exact child PublishedDataSet NodeId to remove, e.g. ns=2;s=Line1Data; not its dataset name.")]
            string dataSetId,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_remove_dataset", folderId, ObjectTypeIds.DataSetFolderType,
                async (session, node, token) =>
                {
                    await new DataSetFolderTypeClient(session, node, session.MessageContext.Telemetry)
                        .RemovePublishedDataSetAsync(McpPubSubRemote.Node(session, dataSetId), token)
                        .ConfigureAwait(false);
                    return McpPubSubRemote.Completed();
                }, sessionName, ct);
        }

        /// <summary>
        /// Adds exactly one DataSet folder.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_add_folder", Destructive = true)]
        [Description("Create one child DataSetFolder; returns NodeId. " +
            "Its parent must exist; no recursive creation or inferred paths.")]
        public Task<CallToolResult> AddFolderAsync(
            [Description("Exact parent DataSetFolder NodeId on the selected server, e.g. ns=2;s=DataSets.")]
            string folderId,
            [Description("Name of the new child DataSetFolder, 1..128 characters, e.g. Line1; not a filesystem path.")]
            string name,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_add_folder", folderId, ObjectTypeIds.DataSetFolderType,
                async (session, node, token) => Added(await new DataSetFolderTypeClient(
                    session, node, session.MessageContext.Telemetry)
                    .AddDataSetFolderAsync(PubSubInputValidation.Text(name, 128), token).ConfigureAwait(false),
                    session),
                sessionName, ct);
        }

        /// <summary>
        /// Removes one explicitly named child folder.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_remove_folder", Destructive = true)]
        [Description("Request deletion of one child DataSetFolder through parentId; returns completion. " +
            "The server decides whether contents permit removal.")]
        public Task<CallToolResult> RemoveFolderAsync(
            [Description("Exact parent DataSetFolder NodeId through which removal is requested, e.g. ns=2;s=DataSets.")]
            string parentId,
            [Description("Exact child DataSetFolder NodeId to remove, e.g. ns=2;s=Line1; not a path.")]
            string folderId,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_remove_folder", parentId, ObjectTypeIds.DataSetFolderType,
                async (session, node, token) =>
                {
                    await new DataSetFolderTypeClient(session, node, session.MessageContext.Telemetry)
                        .RemoveDataSetFolderAsync(McpPubSubRemote.Node(session, folderId), token).ConfigureAwait(false);
                    return McpPubSubRemote.Completed();
                }, sessionName, ct);
        }

        /// <summary>
        /// Adds fields against the caller's real observed server configuration version.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_add_fields", Destructive = true)]
        [Description("Add variable fields at the supplied server configuration version; " +
            "returns the new version and per-field statuses, not a client CAS revision.")]
        public Task<CallToolResult> AddFieldsAsync(
            [Description("Exact PublishedDataItems NodeId to modify on the selected server, e.g. ns=2;s=Line1Data.")]
            string dataSetId,
            [Description("Actual server-observed UInt32 majorVersion/minorVersion pair; copy both values unchanged.")]
            PubSubVersionInput version,
            [Description("Ordered unique field aliases and source NodeIds to add, e.g. ns=2;s=Count; " +
                "attributeId defaults to 13 (Value). promoted marks fields for NetworkMessage metadata.")]
            ArrayOf<PubSubVariableInput> variables,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return AddFieldsAsync(dataSetId, version,
                new PubSubVariablesInput { Variables = variables }, sessionName, ct);
        }

        /// <summary>
        /// Adds fields from an existing direct variable mapping.
        /// </summary>
        public Task<CallToolResult> AddFieldsAsync(
            string dataSetId, PubSubVersionInput version, PubSubVariablesInput input,
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_add_fields", dataSetId, ObjectTypeIds.PublishedDataItemsType,
                async (session, node, token) =>
                {
                    ArrayOf<PublishedVariableDataType> variables = McpPubSubRemote.Variables(session, input, m_options);
                    (ConfigurationVersionDataType next, ArrayOf<StatusCode> statuses) =
                        await new PublishedDataItemsTypeClient(session, node, session.MessageContext.Telemetry)
                            .AddVariablesAsync(Version(version),
                                new ArrayOf<string>(input.Variables.ToList().Select(value => value.Name).ToArray()),
                                new ArrayOf<bool>(input.Variables.ToList().Select(value => value.Promoted).ToArray()),
                                variables, token).ConfigureAwait(false);
                    return Changed(next, statuses, session);
                }, sessionName, ct);
        }

        /// <summary>
        /// Removes exact field indexes against a real server version.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_remove_fields", Destructive = true)]
        [Description("Remove exact field indexes at the observed server version; " +
            "returns the new configuration version and per-field partial outcomes.")]
        public Task<CallToolResult> RemoveFieldsAsync(
            [Description("Exact PublishedDataItems NodeId to modify on the selected server, e.g. ns=2;s=Line1Data.")]
            string dataSetId,
            [Description("Actual server-observed UInt32 majorVersion/minorVersion pair; copy both values unchanged.")]
            PubSubVersionInput version,
            [Description("Ordered zero-based field indexes to remove; server per-item outcomes remain authoritative.")]
            ArrayOf<PubSubFieldIndexInput> fields,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return RemoveFieldsAsync(
                dataSetId, version, new PubSubFieldIndicesInput { Fields = fields }, sessionName, ct);
        }

        /// <summary>
        /// Removes fields from an existing direct index selection.
        /// </summary>
        public Task<CallToolResult> RemoveFieldsAsync(
            string dataSetId, PubSubVersionInput version, PubSubFieldIndicesInput input,
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_remove_fields", dataSetId, ObjectTypeIds.PublishedDataItemsType,
                async (session, node, token) =>
                {
                    ArgumentNullException.ThrowIfNull(input);
                    ArgumentOutOfRangeException.ThrowIfLessThan(input.Fields.Count, 1);
                    ArgumentOutOfRangeException.ThrowIfGreaterThan(input.Fields.Count, m_options.MaxFields);
                    (ConfigurationVersionDataType next, ArrayOf<StatusCode> statuses) =
                        await new PublishedDataItemsTypeClient(session, node, session.MessageContext.Telemetry)
                            .RemoveVariablesAsync(Version(version),
                                new ArrayOf<uint>(input.Fields.ToList().Select(field => field.Index).ToArray()), token)
                            .ConfigureAwait(false);
                    return Changed(next, statuses, session);
                }, sessionName, ct);
        }

        /// <summary>
        /// Explicitly enables one standard PubSubStatus object.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_enable", Destructive = true)]
        [Description("Enable a remote PubSubStatus component; may start its activity, never a new server process. " +
            "Returns completion, not a fresh state read.")]
        public Task<CallToolResult> EnableAsync(
            [Description("Exact PubSubStatus object NodeId to enable, e.g. ns=2;s=Connection1.Status; " +
                "not its State variable.")]
            string statusId,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_enable", statusId, ObjectTypeIds.PubSubStatusType,
                async (session, node, token) =>
                {
                    await new PubSubStatusTypeClient(session, node, session.MessageContext.Telemetry)
                        .EnableAsync(token).ConfigureAwait(false);
                    return McpPubSubRemote.Completed();
                }, sessionName, ct);
        }

        /// <summary>
        /// Explicitly disables one standard PubSubStatus object.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_disable", Destructive = true)]
        [Description("Disable the selected remote PubSubStatus component; returns method completion. " +
            "Other components and the borrowed session remain untouched.")]
        public Task<CallToolResult> DisableAsync(
            [Description("Exact PubSubStatus object NodeId to disable, e.g. ns=2;s=Connection1.Status; " +
                "not its State variable.")]
            string statusId,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_disable", statusId, ObjectTypeIds.PubSubStatusType,
                async (session, node, token) =>
                {
                    await new PubSubStatusTypeClient(session, node, session.MessageContext.Telemetry)
                        .DisableAsync(token).ConfigureAwait(false);
                    return McpPubSubRemote.Completed();
                }, sessionName, ct);
        }

        /// <summary>
        /// Composes a specific generated-client call after independent host/session/model validation.
        /// </summary>
        private Task<CallToolResult> ExecuteAsync(
            string operation, string target, ExpandedNodeId type,
            Func<ISession, NodeId, CancellationToken, ValueTask<JsonObject>> execute,
            string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                OpcUaSessionManager.SessionInfo info = McpPubSubRemote.Session(
                    m_sessions, sessionName, McpCapability.PubSubConfiguration);
                NodeId node = McpPubSubRemote.Node(info.Session, target);
                McpPubSubRemote.RequireTarget(info, node, operation, m_options);
                _ = await McpModelAccess.RequireTypeAsync(info.Session, node, type, token).ConfigureAwait(false);
                return await execute(info.Session, node, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Builds the standard remote configuration without serializing credential references.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private PubSubConfigurationDataType Configuration(PubSubApplicationInput input, ISession session)
        {
            PubSubInputValidation.Validate(input, m_options);
            if (input.Connection.CredentialReference is not null)
            {
                throw new ArgumentException("Runtime credential references cannot be deployed through configuration.");
            }
            PubSubConfigurationDataType configuration = McpPubSubConfiguration.Build(input, Metadata(input, session));
            McpPubSubRemoteTargets.Require(configuration, m_options, m_network);
            return configuration;
        }

        /// <summary>
        /// Builds stable DataSet metadata from the bounded input.
        /// </summary>
        private DataSetMetaDataType Metadata(PubSubApplicationInput input, ISession session)
        {
            return new McpPubSubDataSetSource(input, m_options, session.MessageContext, TimeProvider.System)
                .BuildMetaData();
        }

        /// <summary>
        /// Projects the actual server-created identity.
        /// </summary>
        private static JsonObject Added(NodeId added, ISession session)
        {
            JsonObject result = McpPubSubRemote.Completed();
            result["nodeId"] = added.ToString();
            result["expandedNodeId"] = NodeId.ToExpandedNodeId(added, session.NamespaceUris).ToString();
            return result;
        }

        /// <summary>
        /// Converts the supplied observed version without pre-reading or inventing a revision.
        /// </summary>
        private static ConfigurationVersionDataType Version(PubSubVersionInput version)
        {
            ArgumentNullException.ThrowIfNull(version);
            return new ConfigurationVersionDataType
            {
                MajorVersion = version.MajorVersion,
                MinorVersion = version.MinorVersion
            };
        }

        /// <summary>
        /// Projects actual version and per-field server outcomes.
        /// </summary>
        private static JsonObject Changed(
            ConfigurationVersionDataType version, ArrayOf<StatusCode> statuses, ISession session)
        {
            JsonObject result = McpPubSubRemote.Completed();
            result["configurationVersion"] = McpCompanionJson.Encode(version, session.MessageContext);
            McpPubSubRemote.Statuses(result, statuses);
            return result;
        }

        private readonly OpcUaSessionManager m_sessions;
        private readonly McpPubSubOptions m_options;
        private readonly McpFileTransfers m_files;
        private readonly McpNetworkPolicy m_network;
    }
}
