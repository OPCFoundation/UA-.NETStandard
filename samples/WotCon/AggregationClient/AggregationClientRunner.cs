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
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Client;

namespace AggregationClient
{
    /// <summary>
    /// Executes the real sample loader and reader workflow in process.
    /// </summary>
    public static class AggregationClientRunner
    {
        /// <summary>
        /// Builds the client host used by the workflow.
        /// </summary>
        public static IHost BuildHost(AggregationClientOptions options)
        {
            Validate(options);
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Services
                .AddOpcUa()
                .AddOpcTcpTransport()
                .AddClient(client =>
                {
                    client.ApplicationName = options.ApplicationName;
                    client.ApplicationUri =
                        $"urn:localhost:OPCFoundation:{options.ApplicationName}";
                    client.ProductUri = "uri:opcfoundation.org:AggregationClient";
                    if (!string.IsNullOrWhiteSpace(options.PkiRoot))
                    {
                        client.PkiRoot = options.PkiRoot;
                    }
                    client.AutoAcceptUntrustedCertificates = true;
                    client.Session = new ManagedSessionOptions
                    {
                        SessionName = "AggregationClient",
                        SessionTimeout = TimeSpan.FromSeconds(60)
                    };
                })
                .AddDiscoveryAndConnect(discovery =>
                {
                    discovery.DiscoveryUrl = options.AggregationEndpoint;
                    discovery.SecurityMode = MessageSecurityMode.None;
                    discovery.SecurityPolicyUri = SecurityPolicies.None;
                })
                .AddWotRegistryClient();
            return builder.Build();
        }

        /// <summary>
        /// Loads the four documents, refreshes the registry and reads the Pump.
        /// </summary>
        public static async Task<AggregationClientResult> RunAsync(
            AggregationClientOptions options,
            CancellationToken cancellationToken = default)
        {
            using IHost host = BuildHost(options);
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Func<CancellationToken, Task<ManagedSession>> connect =
                    host.Services.GetRequiredService<
                        Func<CancellationToken, Task<ManagedSession>>>();
                ManagedSession session = await connect(cancellationToken).ConfigureAwait(false);
                await using (session.ConfigureAwait(false))
                {
                    session.MessageContext.NamespaceUris.Update(
                        session.NamespaceUris.ToArray());
                    Func<ManagedSession, CancellationToken, Task<WotRegistryClient>> createClient =
                        host.Services.GetRequiredService<
                            Func<ManagedSession, CancellationToken, Task<WotRegistryClient>>>();
                    WotRegistryClient client = await createClient(session, cancellationToken)
                        .ConfigureAwait(false);
                    ArrayOf<WotRegistryDocument> documents = await LoadDocumentsAsync(
                        options,
                        cancellationToken).ConfigureAwait(false);
                    WotRegistryBulkLoadResult loadResult = await client.LoadDocumentsAsync(
                        documents,
                        refresh: true,
                        requestId: Guid.NewGuid().ToString("N"),
                        ct: cancellationToken).ConfigureAwait(false);
                    EnsureRefreshSucceeded(loadResult);
                    await session.FetchNamespaceTablesAsync(cancellationToken).ConfigureAwait(false);
                    session.MessageContext.NamespaceUris.Update(
                        session.NamespaceUris.ToArray());

                    NodeId pumpNodeId = ResolvePumpNodeId(session);
                    ArrayOf<WotPumpBrowseNode> browsedNodes = await BrowsePumpAsync(
                        session,
                        pumpNodeId,
                        cancellationToken).ConfigureAwait(false);
                    ArrayOf<WotPumpValueResult> values = await ReadPumpValuesAsync(
                        session,
                        cancellationToken).ConfigureAwait(false);
                    return new AggregationClientResult(loadResult, browsedNodes, values);
                }
            }
            finally
            {
                await host.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private static async ValueTask<ArrayOf<WotRegistryDocument>> LoadDocumentsAsync(
            AggregationClientOptions options,
            CancellationToken cancellationToken)
        {
            string manifestPath = Path.Combine(options.DocumentsDirectory, "documents.json");
            string manifestText = await ReadTextAsync(manifestPath, cancellationToken)
                .ConfigureAwait(false);
            using var manifest = JsonDocument.Parse(manifestText);
            var entries = new List<ManifestEntry>();
            foreach (JsonElement item in manifest.RootElement.EnumerateArray())
            {
                var dependencies = new List<string>();
                foreach (JsonElement dependency in item.GetProperty("dependsOn").EnumerateArray())
                {
                    dependencies.Add(dependency.GetString() ??
                        throw new InvalidDataException("A dependency id is required."));
                }

                entries.Add(new ManifestEntry(
                    item.GetProperty("documentKind").GetString() ??
                    throw new InvalidDataException("Document kind is required."),
                    item.GetProperty("groupId").GetString() ??
                    throw new InvalidDataException("Group id is required."),
                    item.GetProperty("path").GetString() ??
                    throw new InvalidDataException("Document path is required."),
                    item.GetProperty("resourceId").GetString() ??
                    throw new InvalidDataException("Resource id is required."),
                    dependencies));
            }

            List<ManifestEntry> ordered = OrderByDependencies(entries);
            var documents = new WotRegistryDocument[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                ManifestEntry entry = ordered[i];
                string content = await ReadTextAsync(
                    Path.Combine(options.DocumentsDirectory, entry.Path),
                    cancellationToken).ConfigureAwait(false);
                WoTDocumentKindEnum kind = ParseKind(entry.DocumentKind);
                if (kind == WoTDocumentKindEnum.ThingDescription &&
                    string.Equals(entry.Path, "SamplePump.td.json", StringComparison.Ordinal))
                {
#if NET8_0_OR_GREATER
                    content = content
                        .Replace(
                            "${SOURCE_A_ENDPOINT}",
                            options.SourceAEndpoint,
                            StringComparison.Ordinal)
                        .Replace(
                            "${SOURCE_B_ENDPOINT}",
                            options.SourceBEndpoint,
                            StringComparison.Ordinal);
#else
                    content = content
                        .Replace("${SOURCE_A_ENDPOINT}", options.SourceAEndpoint)
                        .Replace("${SOURCE_B_ENDPOINT}", options.SourceBEndpoint);
#endif
                }

                documents[i] = new WotRegistryDocument(
                    kind,
                    entry.GroupId,
                    entry.ResourceId,
                    ByteString.From(Encoding.UTF8.GetBytes(content)));
            }
            return new ArrayOf<WotRegistryDocument>(documents);
        }

        private static void EnsureRefreshSucceeded(WotRegistryBulkLoadResult loadResult)
        {
            WotRegistryRefreshResult refresh = loadResult.Refresh ??
                throw new InvalidOperationException("The registry refresh did not run.");
            if (!refresh.HasFailures)
            {
                return;
            }

            var details = new List<string>();
            foreach (WoTResourceLoadResultDataType resource in refresh.Results)
            {
                if (resource.Outcome is WoTOutcomeEnum.Failed or WoTOutcomeEnum.Rejected)
                {
                    details.Add(
                        $"{resource.ResourceId}: {resource.Phase}/{resource.Outcome}: " +
                        resource.Message);
                }
            }
            throw new ServiceResultException(
                StatusCodes.BadUnexpectedError,
                string.Join("; ", details));
        }

        private static List<ManifestEntry> OrderByDependencies(List<ManifestEntry> entries)
        {
            var byId = new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
            foreach (ManifestEntry entry in entries)
            {
                if (!byId.TryAdd(entry.ResourceId, entry))
                {
                    throw new InvalidDataException(
                        $"Duplicate resource id '{entry.ResourceId}' in documents.json.");
                }
            }

            var ordered = new List<ManifestEntry>(entries.Count);
            var completed = new HashSet<string>(StringComparer.Ordinal);
            while (ordered.Count < entries.Count)
            {
                bool progressed = false;
                foreach (ManifestEntry entry in entries)
                {
                    if (completed.Contains(entry.ResourceId) ||
                        !DependenciesSatisfied(entry, completed, byId))
                    {
                        continue;
                    }
                    ordered.Add(entry);
                    completed.Add(entry.ResourceId);
                    progressed = true;
                }
                if (!progressed)
                {
                    throw new InvalidDataException(
                        "documents.json contains a missing or cyclic dependency.");
                }
            }
            return ordered;
        }

        private static bool DependenciesSatisfied(
            ManifestEntry entry,
            HashSet<string> completed,
            Dictionary<string, ManifestEntry> byId)
        {
            foreach (string dependency in entry.DependsOn)
            {
                if (!byId.ContainsKey(dependency) || !completed.Contains(dependency))
                {
                    return false;
                }
            }
            return true;
        }

        private static WoTDocumentKindEnum ParseKind(string kind)
        {
            return kind switch
            {
                "ThingModel" => WoTDocumentKindEnum.ThingModel,
                "ThingDescription" => WoTDocumentKindEnum.ThingDescription,
                _ => throw new InvalidDataException($"Unsupported document kind '{kind}'.")
            };
        }

        private static NodeId ResolvePumpNodeId(ManagedSession session)
        {
            const string pumpNamespace =
                "urn:opcfoundation.org:UA:WotAggregation:PumpInstance";
            ushort namespaceIndex = ResolveRequiredNamespaceIndex(
                session.NamespaceUris,
                pumpNamespace);
            return new NodeId("Pump1", namespaceIndex);
        }

        private static async ValueTask<ArrayOf<WotPumpBrowseNode>> BrowsePumpAsync(
            ManagedSession session,
            NodeId pumpNodeId,
            CancellationToken cancellationToken)
        {
            var browser = new Browser(session)
            {
                BrowseDirection = BrowseDirection.Forward,
                NodeClassMask = (uint)NodeClass.Object | (uint)NodeClass.Variable,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true
            };
            var pending = new Queue<NodeId>();
            var visited = new HashSet<NodeId>();
            var nodes = new List<WotPumpBrowseNode>();
            pending.Enqueue(pumpNodeId);
            visited.Add(pumpNodeId);
            while (pending.Count > 0)
            {
                NodeId parent = pending.Dequeue();
                ArrayOf<ReferenceDescription> references = await browser.BrowseAsync(
                    parent,
                    cancellationToken).ConfigureAwait(false);
                foreach (ReferenceDescription reference in references)
                {
                    var nodeId = ExpandedNodeId.ToNodeId(
                        reference.NodeId,
                        session.NamespaceUris);
                    nodes.Add(new WotPumpBrowseNode(
                        nodeId,
                        reference.BrowseName,
                        reference.DisplayName,
                        reference.NodeClass));
                    if (reference.NodeClass == NodeClass.Object && visited.Add(nodeId))
                    {
                        pending.Enqueue(nodeId);
                    }
                }
            }
            return nodes.ToArray().ToArrayOf();
        }

        private static async ValueTask<ArrayOf<WotPumpValueResult>> ReadPumpValuesAsync(
            ManagedSession session,
            CancellationToken cancellationToken)
        {
            const string pumpNamespace =
                "urn:opcfoundation.org:UA:WotAggregation:PumpInstance";
            ushort namespaceIndex = ResolveRequiredNamespaceIndex(
                session.NamespaceUris,
                pumpNamespace);
            (string Name, string Path)[] definitions =
            [
                ("DifferentialPressure", "Operational.Measurements.DifferentialPressure"),
                ("FluidTemperature", "Operational.Measurements.FluidTemperature"),
                ("BearingTemperature", "Operational.Measurements.BearingTemperature"),
                ("PumpPowerInput", "Operational.Measurements.PumpPowerInput"),
                ("MassFlow", "Operational.Measurements.MassFlow"),
                ("PumpEfficiency", "Operational.Measurements.PumpEfficiency"),
                ("Level", "Operational.Measurements.Level"),
                ("NumberOfStarts", "Operational.Measurements.NumberOfStarts"),
                ("Cavitation", "Events.SupervisionProcessFluid.Cavitation"),
                ("MotorOverheat", "Events.SupervisionPumpOperation.MotorOverheat")
            ];
            var nodesToRead = new ReadValueId[definitions.Length];
            for (int i = 0; i < definitions.Length; i++)
            {
                nodesToRead[i] = new ReadValueId
                {
                    NodeId = new NodeId($"Pump1.{definitions[i].Path}", namespaceIndex),
                    AttributeId = Attributes.Value
                };
            }

            ReadResponse response = await session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ArrayOf<ReadValueId>(nodesToRead),
                cancellationToken).ConfigureAwait(false);
            var values = new WotPumpValueResult[definitions.Length];
            for (int i = 0; i < definitions.Length; i++)
            {
                DataValue value = response.Results[i];
                if (StatusCode.IsBad(value.StatusCode))
                {
                    throw new ServiceResultException(
                        value.StatusCode,
                        $"Reading materialized Pump value '{definitions[i].Name}' failed.");
                }
                values[i] = new WotPumpValueResult(
                    definitions[i].Name,
                    nodesToRead[i].NodeId,
                    value.StatusCode,
                    value.WrappedValue);
            }
            return new ArrayOf<WotPumpValueResult>(values);
        }

        private static ushort ResolveRequiredNamespaceIndex(
            NamespaceTable namespaceUris,
            string namespaceUri)
        {
            int namespaceIndex = namespaceUris.GetIndex(namespaceUri);
            if (namespaceIndex < 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown,
                    $"The materialized namespace '{namespaceUri}' is not present on the server.");
            }
            return checked((ushort)namespaceIndex);
        }

        private static async Task<string> ReadTextAsync(
            string path,
            CancellationToken cancellationToken)
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                useAsync: true);
            if (stream.Length > int.MaxValue)
            {
                throw new IOException($"Document '{path}' is too large.");
            }
            byte[] bytes = new byte[(int)stream.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
#if NET8_0_OR_GREATER
                int read = await stream.ReadAsync(
                    bytes.AsMemory(offset, bytes.Length - offset),
                    cancellationToken).ConfigureAwait(false);
#else
                int read = await stream.ReadAsync(
                    bytes,
                    offset,
                    bytes.Length - offset,
                    cancellationToken).ConfigureAwait(false);
#endif
                if (read == 0)
                {
                    throw new EndOfStreamException($"Document '{path}' ended unexpectedly.");
                }
                offset += read;
            }
            return Encoding.UTF8.GetString(bytes);
        }

        private static void Validate(AggregationClientOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (string.IsNullOrWhiteSpace(options.AggregationEndpoint) ||
                string.IsNullOrWhiteSpace(options.SourceAEndpoint) ||
                string.IsNullOrWhiteSpace(options.SourceBEndpoint) ||
                string.IsNullOrWhiteSpace(options.DocumentsDirectory))
            {
                throw new ArgumentException("All client endpoint and document options are required.");
            }
        }

        private sealed class ManifestEntry
        {
            public ManifestEntry(
                string documentKind,
                string groupId,
                string path,
                string resourceId,
                List<string> dependsOn)
            {
                DocumentKind = documentKind;
                GroupId = groupId;
                Path = path;
                ResourceId = resourceId;
                DependsOn = dependsOn;
            }

            public string DocumentKind { get; }

            public string GroupId { get; }

            public string Path { get; }

            public string ResourceId { get; }

            public List<string> DependsOn { get; }
        }
    }
}
