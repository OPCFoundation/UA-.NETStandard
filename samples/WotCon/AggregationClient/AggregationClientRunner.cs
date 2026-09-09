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
using Opc.Ua.Samples;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Client;

namespace AggregationClient
{
    /// <summary>
    /// Executes the real sample loader and reader workflow in process.
    /// </summary>
    public static partial class AggregationClientRunner
    {
        /// <summary>
        /// Builds the client host used by the workflow.
        /// </summary>
        public static IHost BuildHost(AggregationClientOptions options)
        {
            Validate(options);
            SampleCommandLine.WriteSecurityWarnings(
                Console.Error, options.AutoAcceptUntrustedCertificates,
                options.UseSecurityPolicyNone, "server", "--security-none");
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
                    client.AutoAcceptUntrustedCertificates = options.AutoAcceptUntrustedCertificates;
                    client.Session = new ManagedSessionOptions
                    {
                        SessionName = "AggregationClient",
                        SessionTimeout = TimeSpan.FromSeconds(60),
                        IdentityProvider = options.IdentityProvider
                    };
                })
                .AddDiscoveryAndConnect(discovery =>
                {
                    discovery.DiscoveryUrl = options.AggregationEndpoint;
                    discovery.SecurityMode = options.UseSecurityPolicyNone
                        ? MessageSecurityMode.None
                        : MessageSecurityMode.SignAndEncrypt;
                    discovery.SecurityPolicyUri = options.UseSecurityPolicyNone
                        ? SecurityPolicies.None
                        : SecurityPolicies.Basic256Sha256;
                })
                .AddWotRegistryClient();
            return builder.Build();
        }

        /// <summary>
        /// Loads the linked documents, refreshes the registry and exercises the selected pump workflow.
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
                    EnsureRefreshSucceeded(loadResult, options.ExerciseControls);
                    await session.FetchNamespaceTablesAsync(cancellationToken).ConfigureAwait(false);
                    session.MessageContext.NamespaceUris.Update(
                        session.NamespaceUris.ToArray());

                    var pumps = new WotPumpResult[2];
                    for (int i = 0; i < pumps.Length; i++)
                    {
                        string name = i == 0 ? "Pump1" : "Pump2";
                        NodeId pumpNodeId = await DiscoverPumpAsync(session, name, cancellationToken)
                            .ConfigureAwait(false);
                        ArrayOf<WotPumpBrowseNode> browsedNodes = await BrowsePumpAsync(
                            session, pumpNodeId, cancellationToken).ConfigureAwait(false);
                        ArrayOf<WotPumpValueResult> values = await ReadPumpValuesAsync(
                            session, name, cancellationToken).ConfigureAwait(false);
                        pumps[i] = new WotPumpResult(name, pumpNodeId, browsedNodes, values);
                    }
                    var pumpResults = new ArrayOf<WotPumpResult>(pumps);
                    ArrayOf<WotPumpControlResult> controls = options.ExerciseControls
                        ? await ExerciseControlsAsync(
                            session, options, documents, pumpResults, cancellationToken).ConfigureAwait(false)
                        : [];
                    return new AggregationClientResult(loadResult, pumpResults, controls);
                }
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        internal static byte[] SubstituteEndpoints(string content, AggregationClientOptions options)
        {
            if (content.IndexOf("${SOURCE_A_ENDPOINT}", StringComparison.Ordinal) < 0 &&
                content.IndexOf("${SOURCE_B_ENDPOINT}", StringComparison.Ordinal) < 0)
            {
                return Encoding.UTF8.GetBytes(content);
            }
            using JsonDocument document = JsonDocument.Parse(content);
            using var output = new MemoryStream();
            bool substituted = false;
            using (var writer = new Utf8JsonWriter(output))
            {
                WriteDocumentValue(writer, document.RootElement, options, documentRoot: true, ref substituted);
            }
            return substituted ? output.ToArray() : Encoding.UTF8.GetBytes(content);
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
                byte[] json = SubstituteEndpoints(content, options);

                documents[i] = new WotRegistryDocument(
                    kind,
                    entry.GroupId,
                    entry.ResourceId,
                    ByteString.From(json));
            }
            return new ArrayOf<WotRegistryDocument>(documents);
        }

        private static void WriteDocumentValue(
            Utf8JsonWriter writer,
            JsonElement value,
            AggregationClientOptions options,
            bool documentRoot,
            ref bool substituted)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                foreach (JsonProperty member in value.EnumerateObject())
                {
                    writer.WritePropertyName(member.Name);
                    if (member.Name == "forms" && member.Value.ValueKind == JsonValueKind.Array)
                    {
                        WriteForms(writer, member.Value, options, ref substituted);
                    }
                    else if (documentRoot && member.Name is "properties" or "actions" or "events" &&
                        member.Value.ValueKind == JsonValueKind.Object)
                    {
                        writer.WriteStartObject();
                        foreach (JsonProperty affordance in member.Value.EnumerateObject())
                        {
                            writer.WritePropertyName(affordance.Name);
                            WriteDocumentValue(
                                writer, affordance.Value, options, documentRoot: false, ref substituted);
                        }
                        writer.WriteEndObject();
                    }
                    else
                    {
                        member.Value.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            else
            {
                value.WriteTo(writer);
            }
        }

        private static void WriteForms(
            Utf8JsonWriter writer,
            JsonElement forms,
            AggregationClientOptions options,
            ref bool substituted)
        {
            writer.WriteStartArray();
            foreach (JsonElement form in forms.EnumerateArray())
            {
                if (form.ValueKind != JsonValueKind.Object)
                {
                    form.WriteTo(writer);
                    continue;
                }
                writer.WriteStartObject();
                foreach (JsonProperty member in form.EnumerateObject())
                {
                    writer.WritePropertyName(member.Name);
                    string? endpoint = member.Name == "href" && member.Value.ValueKind == JsonValueKind.String
                        ? member.Value.GetString() switch
                        {
                            "${SOURCE_A_ENDPOINT}" => options.SourceAEndpoint,
                            "${SOURCE_B_ENDPOINT}" => options.SourceBEndpoint,
                            _ => null
                        }
                        : null;
                    if (endpoint is not null)
                    {
                        writer.WriteStringValue(endpoint);
                        substituted = true;
                    }
                    else
                    {
                        member.Value.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static void EnsureRefreshSucceeded(WotRegistryBulkLoadResult loadResult, bool requireComplete)
        {
            WotRegistryRefreshResult refresh = loadResult.Refresh ??
                throw new InvalidOperationException("The registry refresh did not run.");
            if (!refresh.HasFailures && !requireComplete)
            {
                return;
            }

            var details = new List<string>();
            foreach (WoTResourceLoadResultDataType resource in refresh.Results)
            {
                if (resource.Outcome is WoTOutcomeEnum.Failed or WoTOutcomeEnum.Rejected ||
                    (requireComplete && resource.Outcome == WoTOutcomeEnum.Warning))
                {
                    details.Add(
                        $"{resource.ResourceId}: {resource.Phase}/{resource.Outcome}: " +
                        resource.Message);
                }
            }
            if (details.Count > 0 || refresh.HasFailures)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "The aggregation refresh is incomplete: " + string.Join("; ", details));
            }
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

        private static async ValueTask<NodeId> DiscoverPumpAsync(
            ManagedSession session, string name, CancellationToken cancellationToken)
        {
            const string pumpNamespace =
                "urn:opcfoundation.org:UA:WotAggregation:PumpInstance";
            ushort namespaceIndex = ResolveRequiredNamespaceIndex(
                session.NamespaceUris,
                pumpNamespace);
            var expected = new NodeId(name, namespaceIndex);
            var browser = new Browser(session)
            {
                BrowseDirection = BrowseDirection.Forward,
                NodeClassMask = (uint)NodeClass.Object,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true
            };
            ArrayOf<ReferenceDescription> children = await browser.BrowseAsync(
                Opc.Ua.ObjectIds.ObjectsFolder, cancellationToken).ConfigureAwait(false);
            foreach (ReferenceDescription child in children)
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(child.NodeId, session.NamespaceUris);
                if (nodeId == expected)
                {
                    return nodeId;
                }
            }
            throw new ServiceResultException(
                StatusCodes.BadNodeIdUnknown,
                $"The materialized {name} is not browseable from the Objects folder.");
        }

        private static async ValueTask<ArrayOf<WotPumpBrowseNode>> BrowsePumpAsync(
            ManagedSession session,
            NodeId pumpNodeId,
            CancellationToken cancellationToken)
        {
            var browser = new Browser(session)
            {
                BrowseDirection = BrowseDirection.Forward,
                NodeClassMask = (uint)NodeClass.Object | (uint)NodeClass.Variable | (uint)NodeClass.Method,
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
                    if (nodeId.IsNull)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNodeIdInvalid,
                            $"The {pumpNodeId} browse returned an unresolved node '{reference.NodeId}'.");
                    }
                    if (!visited.Add(nodeId))
                    {
                        continue;
                    }
                    nodes.Add(new WotPumpBrowseNode(
                        nodeId,
                        reference.BrowseName,
                        reference.DisplayName,
                        reference.NodeClass));
                    pending.Enqueue(nodeId);
                }
            }
            return nodes.ToArray().ToArrayOf();
        }

        private static async ValueTask<ArrayOf<WotPumpValueResult>> ReadPumpValuesAsync(
            ManagedSession session,
            string pumpName,
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
                ("MotorOverheat", "Events.SupervisionPumpOperation.MotorOverheat"),
                ("Manufacturer", "Identification.Manufacturer"),
                ("SerialNumber", "Identification.SerialNumber"),
                ("ProductInstanceUri", "Identification.ProductInstanceUri"),
                ("SourceARunning", "SourceARunning"),
                ("SourceBRunning", "SourceBRunning")
            ];
            var nodesToRead = new ReadValueId[definitions.Length];
            for (int i = 0; i < definitions.Length; i++)
            {
                nodesToRead[i] = new ReadValueId
                {
                    NodeId = new NodeId($"{pumpName}.{definitions[i].Path}", namespaceIndex),
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
