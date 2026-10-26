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
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.PubSub.Configuration;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Root-confined standard configuration transfer. File handles are never exposed to callers.
    /// </summary>
    public sealed partial class PubSubConfigurationTools
    {
        /// <summary>
        /// Exports a bounded standard configuration after stripping credentials and extensible property bags.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_export", ReadOnly = true)]
        [Description("Export remote PubSubConfiguration XML under TransferRoot, without overwrite. " +
            "Returns transfer metadata and counts of stripped credentials/opaque bags.")]
        public Task<CallToolResult> ExportAsync(
            [Description("New PubSubConfiguration XML path beneath TransferRoot, e.g. pubsub.xml; no overwrite.")]
            string filePath,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_export", ObjectIds.PublishSubscribe.ToString(),
                ObjectTypeIds.PublishSubscribeType, async (session, _, token) =>
                {
                    m_files.ValidateDownloadPath(filePath);
                    PubSubConfigurationTypeClient configuration = await McpPubSubRemote.ConfigurationAsync(
                        session, token).ConfigureAwait(false);
                    PubSubConfigurationDataType model = await ReadConfigurationAsync(
                        session, configuration.ObjectId, token).ConfigureAwait(false);
                    int redacted = McpPubSubConfigurationSafety.Sanitize(model, m_options);
                    JsonObject result = await m_files.DownloadAsync(filePath, async (destination, downloadToken) =>
                    {
                        using var encoded = new McpBoundedMemoryStream(
                            m_options.MaxConfigurationBytes, downloadToken);
                        UaPubSubConfigurationHelper.SaveConfiguration(model, encoded, session.MessageContext.Telemetry);
                        encoded.Position = 0;
                        await encoded.CopyToAsync(destination, 16384, downloadToken).ConfigureAwait(false);
                    }, token).ConfigureAwait(false);
                    result["redactedItems"] = redacted;
                    result["completeConfiguration"] = redacted == 0;
                    result["credentialsIncluded"] = false;
                    result["format"] = "OPC UA PubSubConfiguration XML";
                    return result;
                }, sessionName, ct);
        }

        /// <summary>
        /// Imports one explicit safe configuration through the standard CloseAndUpdate transaction boundary.
        /// </summary>
        [McpServerTool(Name = "pubsub_configuration_import", Destructive = true)]
        [Description("Import bounded PubSub XML from TransferRoot through standard CloseAndUpdate. Existing " +
            "elements are updated, missing elements are added, and omitted elements are retained. Rejects " +
            "credential/opaque bags. Returns the server's real applied flag/per-item statuses; no client rollback.")]
        public Task<CallToolResult> ImportAsync(
            [Description("Existing PubSubConfiguration XML beneath TransferRoot, e.g. pubsub.xml; not a URL.")]
            string filePath,
            [Description("True requests complete CloseAndUpdate; false permits partial update. " +
                "The server result establishes what applied.")]
            bool requireCompleteUpdate = true,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_configuration_import", ObjectIds.PublishSubscribe.ToString(),
                ObjectTypeIds.PublishSubscribeType, async (session, _, token) =>
                {
                    JsonObject? outcome = null;
                    await m_files.UploadAsync(filePath, async (source, uploadToken) =>
                    {
                        using var input = new McpBoundedMemoryStream(m_options.MaxConfigurationBytes, uploadToken);
                        await source.CopyToAsync(input, 16384, uploadToken).ConfigureAwait(false);
                        long length = input.Length;
                        PubSubConfigurationDataType model = McpPubSubRemote.Decode(
                            input, session.MessageContext.Telemetry);
                        if (McpPubSubConfigurationSafety.Sanitize(model, m_options) != 0)
                        {
                            throw new ArgumentException(
                                "The configuration contains credentials or unsupported extensible settings. " +
                                "Use explicit typed tools and secret references instead.");
                        }
                        McpPubSubRemoteTargets.Require(model, m_options, m_network);
                        PubSubConfigurationTypeClient target = await McpPubSubRemote.ConfigurationAsync(
                            session, uploadToken).ConfigureAwait(false);
                        PubSubConfigurationDataType current = await ReadConfigurationAsync(
                            session, target.ObjectId, uploadToken).ConfigureAwait(false);
                        using var binary = new McpBoundedMemoryStream(m_options.MaxConfigurationBytes, uploadToken);
                        McpPubSubRemote.EncodeBinary(model, session, binary);
                        binary.Position = 0;
                        outcome = await WriteConfigurationAsync(
                            session, binary, CreateReferences(model, current), requireCompleteUpdate, uploadToken)
                            .ConfigureAwait(false);
                        return length;
                    }, token).ConfigureAwait(false);
                    return outcome ?? throw new InvalidOperationException("No configuration result was returned.");
                }, sessionName, ct);
        }

        /// <summary>
        /// Reads the bounded standard file using generated FileType calls and always closes its private handle.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<PubSubConfigurationDataType> ReadConfigurationAsync(
            ISession session, NodeId node, CancellationToken ct)
        {
            var file = new FileTypeClient(session, node, session.MessageContext.Telemetry);
            uint handle = await file.OpenAsync(1, ct).ConfigureAwait(false);
            try
            {
                using var bytes = new McpBoundedMemoryStream(m_options.MaxConfigurationBytes, ct);
                while (true)
                {
                    int requested = (int)Math.Min(16384, m_options.MaxConfigurationBytes - bytes.Length + 1);
                    ByteString block = await file.ReadAsync(handle, requested, ct).ConfigureAwait(false);
                    if (block.IsEmpty)
                    {
                        break;
                    }
                    if (block.Length > requested)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "The server returned more configuration data than requested.");
                    }
                    await bytes.WriteAsync(block.Memory, ct).ConfigureAwait(false);
                }
                return McpPubSubRemote.DecodeBinary(bytes.ToArray(), session);
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await file.CloseAsync(handle, cleanup.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stages bounded blocks and preserves an acknowledged commit even when optional close cleanup fails.
        /// </summary>
        private static async ValueTask<JsonObject> WriteConfigurationAsync(
            ISession session, Stream source, ArrayOf<PubSubConfigurationRefDataType> references,
            bool requireCompleteUpdate, CancellationToken ct)
        {
            PubSubConfigurationTypeClient configuration = await McpPubSubRemote.ConfigurationAsync(
                session, ct).ConfigureAwait(false);
            var file = new FileTypeClient(session, configuration.ObjectId, session.MessageContext.Telemetry);
            uint handle = await file.OpenAsync(6, ct).ConfigureAwait(false);
            bool closed = false;
            try
            {
                byte[] block = new byte[16384];
                while (true)
                {
                    int read = await source.ReadAsync(block, ct).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }
                    await file.WriteAsync(handle, ByteString.From(block.AsSpan(0, read).ToArray()), ct)
                        .ConfigureAwait(false);
                }
                (bool applied, ArrayOf<StatusCode> statuses, ArrayOf<PubSubConfigurationValueDataType> values,
                    ArrayOf<NodeId> nodes) = await configuration.CloseAndUpdateAsync(
                        handle, requireCompleteUpdate, references, ct).ConfigureAwait(false);
                closed = true;
                JsonObject result = McpPubSubRemote.Completed();
                result["serverReportedChangesApplied"] = applied;
                result["requestedCompleteUpdate"] = requireCompleteUpdate;
                result["atomicity"] = "server-reported only; the client does not emulate rollback or retry";
                McpPubSubRemote.Statuses(result, statuses);
                result["configurationValueCount"] = values.Count;
                result["configurationValuesOmitted"] = true;
                var identifiers = new JsonArray();
                foreach (NodeId node in nodes)
                {
                    identifiers.Add((JsonNode?)JsonValue.Create(
                        NodeId.ToExpandedNodeId(node, session.NamespaceUris).ToString()));
                }
                result["changedNodeIds"] = identifiers;
                return result;
            }
            finally
            {
                if (!closed)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    try
                    {
                        await file.CloseAsync(handle, cleanup.Token).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is ServiceResultException or OperationCanceledException)
                    {
                        // CloseAndUpdate may have consumed the handle before the response was lost.
                        // The original failure remains authoritative; no transaction is replayed.
                    }
                }
            }
        }

        /// <summary>
        /// Selects every present Part 14 configuration element; server processing orders parents before children.
        /// </summary>
        private static ArrayOf<PubSubConfigurationRefDataType> CreateReferences(
            PubSubConfigurationDataType configuration, PubSubConfigurationDataType current)
        {
            PubSubConfiguration2DataType extended = configuration is PubSubConfiguration2DataType value
                ? value
                : new PubSubConfiguration2DataType
                {
                    Connections = configuration.Connections,
                    PublishedDataSets = configuration.PublishedDataSets
                };
            PubSubConfiguration2DataType existing = current is PubSubConfiguration2DataType currentExtended
                ? currentExtended
                : new PubSubConfiguration2DataType
                {
                    Connections = current.Connections,
                    PublishedDataSets = current.PublishedDataSets
                };
            var references = new System.Collections.Generic.List<PubSubConfigurationRefDataType>();
            static ushort Index(int index) => checked((ushort)index);
            static PubSubConfigurationRefDataType Reference(
                uint kind, uint operation, ushort connection = 0, ushort group = 0, ushort element = 0)
            {
                return new PubSubConfigurationRefDataType
                {
                    ConfigurationMask = kind | operation,
                    ConnectionIndex = connection,
                    GroupIndex = group,
                    ElementIndex = element
                };
            }
            static Dictionary<string, T> CreateNameMap<T>(
                ArrayOf<T> items,
                Func<T, string?> getName,
                out T? nullNameItem)
                where T : class
            {
                var result = new Dictionary<string, T>(StringComparer.Ordinal);
                nullNameItem = null;
                for (int i = 0; i < items.Count; i++)
                {
                    T item = items[i];
                    string? name = getName(item);
                    if (name is null)
                    {
                        nullNameItem ??= item;
                    }
                    else
                    {
                        result.TryAdd(name, item);
                    }
                }
                return result;
            }
            static T? FindByName<T>(Dictionary<string, T> items, T? nullNameItem, string? name)
                where T : class
            {
                if (name is null)
                {
                    return nullNameItem;
                }
                return items.TryGetValue(name, out T? item) ? item : null;
            }
            static HashSet<string?> CreateNameSet<T>(ArrayOf<T> items, Func<T, string?> getName)
            {
                var names = new HashSet<string?>(StringComparer.Ordinal);
                for (int i = 0; i < items.Count; i++)
                {
                    names.Add(getName(items[i]));
                }
                return names;
            }
            static HashSet<string?> CreateStandaloneSubscribedDataSetNameSet(
                ArrayOf<StandaloneSubscribedDataSetDataType> items)
            {
                var names = new HashSet<string?>(StringComparer.Ordinal);
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] is StandaloneSubscribedDataSetDataType standalone)
                    {
                        names.Add(standalone.Name);
                    }
                }
                return names;
            }

            Dictionary<string, PubSubConnectionDataType> connectionLookup =
                CreateNameMap(existing.Connections, static candidate => candidate.Name,
                    out PubSubConnectionDataType? connectionWithoutName);
            HashSet<string?> publishedDataSetNames =
                CreateNameSet(existing.PublishedDataSets, static candidate => candidate.Name);
            HashSet<string?> subscribedDataSetNames =
                CreateStandaloneSubscribedDataSetNameSet(existing.SubscribedDataSets);
            HashSet<string?> securityGroupNames =
                CreateNameSet(existing.SecurityGroups, static candidate => candidate.Name);
            HashSet<string?> keyPushTargetUris =
                CreateNameSet(existing.PubSubKeyPushTargets, static candidate => candidate.ApplicationUri);

            for (int connectionIndex = 0; connectionIndex < extended.Connections.Count; connectionIndex++)
            {
                PubSubConnectionDataType connection = extended.Connections[connectionIndex];
                ushort connectionId = Index(connectionIndex);
                PubSubConnectionDataType? matchingConnection =
                    FindByName(connectionLookup, connectionWithoutName, connection.Name);
                references.Add(Reference(256, matchingConnection is null ? 1u : 4u, connectionId));
                Dictionary<string, WriterGroupDataType> writerGroupLookup;
                WriterGroupDataType? writerGroupWithoutName;
                Dictionary<string, ReaderGroupDataType> readerGroupLookup;
                ReaderGroupDataType? readerGroupWithoutName;
                if (matchingConnection is null)
                {
                    writerGroupLookup = new Dictionary<string, WriterGroupDataType>(StringComparer.Ordinal);
                    writerGroupWithoutName = null;
                    readerGroupLookup = new Dictionary<string, ReaderGroupDataType>(StringComparer.Ordinal);
                    readerGroupWithoutName = null;
                }
                else
                {
                    writerGroupLookup = CreateNameMap(
                        matchingConnection.WriterGroups, static candidate => candidate.Name,
                        out writerGroupWithoutName);
                    readerGroupLookup = CreateNameMap(
                        matchingConnection.ReaderGroups, static candidate => candidate.Name,
                        out readerGroupWithoutName);
                }
                for (int groupIndex = 0; groupIndex < connection.WriterGroups.Count; groupIndex++)
                {
                    WriterGroupDataType group = connection.WriterGroups[groupIndex];
                    ushort groupId = Index(groupIndex);
                    WriterGroupDataType? matchingGroup =
                        FindByName(writerGroupLookup, writerGroupWithoutName, group.Name);
                    references.Add(Reference(64, matchingGroup is null ? 1u : 4u, connectionId, groupId));
                    HashSet<string?> writerNames = matchingGroup is null
                        ? []
                        : CreateNameSet(matchingGroup.DataSetWriters, static candidate => candidate.Name);
                    for (int elementIndex = 0; elementIndex < group.DataSetWriters.Count; elementIndex++)
                    {
                        DataSetWriterDataType writer = group.DataSetWriters[elementIndex];
                        uint operation = writerNames.Contains(writer.Name) ? 4u : 1u;
                        references.Add(Reference(16, operation, connectionId, groupId, Index(elementIndex)));
                    }
                }
                for (int groupIndex = 0; groupIndex < connection.ReaderGroups.Count; groupIndex++)
                {
                    ReaderGroupDataType group = connection.ReaderGroups[groupIndex];
                    ushort groupId = Index(groupIndex);
                    ReaderGroupDataType? matchingGroup =
                        FindByName(readerGroupLookup, readerGroupWithoutName, group.Name);
                    references.Add(Reference(128, matchingGroup is null ? 1u : 4u, connectionId, groupId));
                    HashSet<string?> readerNames = matchingGroup is null
                        ? []
                        : CreateNameSet(matchingGroup.DataSetReaders, static candidate => candidate.Name);
                    for (int elementIndex = 0; elementIndex < group.DataSetReaders.Count; elementIndex++)
                    {
                        DataSetReaderDataType reader = group.DataSetReaders[elementIndex];
                        uint operation = readerNames.Contains(reader.Name) ? 4u : 1u;
                        references.Add(Reference(32, operation, connectionId, groupId, Index(elementIndex)));
                    }
                }
            }
            for (int index = 0; index < extended.PublishedDataSets.Count; index++)
            {
                PublishedDataSetDataType dataSet = extended.PublishedDataSets[index];
                uint operation = publishedDataSetNames.Contains(dataSet.Name) ? 4u : 1u;
                references.Add(Reference(512, operation, element: Index(index)));
            }
            for (int index = 0; index < extended.SubscribedDataSets.Count; index++)
            {
                SubscribedDataSetDataType dataSet = extended.SubscribedDataSets[index];
                bool exists = dataSet is StandaloneSubscribedDataSetDataType standalone &&
                    subscribedDataSetNames.Contains(standalone.Name);
                uint operation = exists ? 4u : 1u;
                references.Add(Reference(1024, operation, element: Index(index)));
            }
            for (int index = 0; index < extended.SecurityGroups.Count; index++)
            {
                SecurityGroupDataType group = extended.SecurityGroups[index];
                uint operation = securityGroupNames.Contains(group.Name) ? 4u : 1u;
                references.Add(Reference(2048, operation, element: Index(index)));
            }
            for (int index = 0; index < extended.PubSubKeyPushTargets.Count; index++)
            {
                PubSubKeyPushTargetDataType target = extended.PubSubKeyPushTargets[index];
                uint operation = keyPushTargetUris.Contains(target.ApplicationUri) ? 4u : 1u;
                references.Add(Reference(4096, operation, element: Index(index)));
            }
            return new ArrayOf<PubSubConfigurationRefDataType>(references.ToArray());
        }
    }
}
