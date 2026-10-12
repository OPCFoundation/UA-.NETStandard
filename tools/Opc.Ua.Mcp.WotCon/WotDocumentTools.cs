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
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Five session-independent tools for bounded WoT documents, projection validation and planning.
    /// </summary>
    [McpServerToolType]
    public sealed class WotDocumentTools : IDisposable
    {
        /// <summary>
        /// Creates document tools over host-owned file and dependency policies, never protocol executors.
        /// </summary>
        public WotDocumentTools(
            McpFileTransfers files, OpcUaMcpOptions shared, WotMcpOptions options, IHttpClientFactory clients)
        {
            m_files = files ?? throw new ArgumentNullException(nameof(files));
            m_shared = shared ?? throw new ArgumentNullException(nameof(shared));
            ArgumentNullException.ThrowIfNull(options);
            m_clients = clients ?? throw new ArgumentNullException(nameof(clients));
            m_options = options.Snapshot();
            m_planners = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());
            m_gate = new SemaphoreSlim(m_options.MaxConcurrentOperations, m_options.MaxConcurrentOperations);
            m_lifetime = new CancellationTokenSource();
        }

        /// <summary>
        /// Parses and classifies explicit documents, returning metadata rather than unbounded document text.
        /// </summary>
        [McpServerTool(Name = "wot_inspect_document", ReadOnly = true, Destructive = false, Idempotent = true,
            OpenWorld = false)]
        [Description("Inspects WoT JSON or a document-set ZIP. Returns classification, bounded affordance names " +
            "and digests, not W3C certification. Inline text is limited to 64 KiB; files require TransferRoot. " +
            "No fetching or execution.")]
        public Task<CallToolResult> InspectDocumentAsync(
            [Description("Exactly one primary text/file source and optional explicit local dependencies.")]
            WotDocumentInput input,
            CancellationToken ct = default)
        {
            return ExecuteAsync(input, nodeSetInput: false, outputPath: null, scope =>
            {
                var documents = new JsonArray();
                foreach (WotDocumentSetEntry entry in scope.Documents.Entries)
                {
                    if (documents.Count >= WotDocumentJson.MaxItems)
                    {
                        break;
                    }
                    WotDocument document = entry.Document;
                    documents.Add(new JsonObject
                    {
                        ["reference"] = WotDocumentJson.Reference(entry.Href),
                        ["kind"] = document.Kind.ToString(),
                        ["id"] = WotDocumentJson.Text(document.Id),
                        ["title"] = WotDocumentJson.Text(document.Title),
                        ["bytes"] = document.Utf8Json.Length,
                        ["properties"] = AffordanceNames(document.Properties),
                        ["actions"] = AffordanceNames(document.Actions),
                        ["events"] = AffordanceNames(document.Events)
                    });
                }
                return ValueTask.FromResult(new JsonObject
                {
                    ["inspectionScope"] = "JSON syntax and lexical metadata only",
                    ["rootReference"] = WotDocumentJson.Reference(scope.Documents.RootHref),
                    ["documents"] = documents,
                    ["totalDocuments"] = scope.Documents.Entries.Count,
                    ["truncated"] = scope.Documents.Entries.Count > documents.Count
                });
            }, ct);
        }

        /// <summary>
        /// Checks the existing OPC UA WoT Binding projection, or reports syntax-only inspection for unknown kinds.
        /// </summary>
        [McpServerTool(Name = "wot_validate_document", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Validates WoT Binding projections and returns scoped diagnostics, not W3C certification. " +
            "Unknown kinds receive syntax-only checks. Strict authoring is explicit; remote dependencies need " +
            "host allowlisting and call opt-in. No binding execution.")]
        public Task<CallToolResult> ValidateDocumentAsync(
            [Description("Document input, supplied dependencies, and explicit authoring/resolution options.")]
            WotDocumentInput input,
            CancellationToken ct = default)
        {
            return ExecuteAsync(input, nodeSetInput: false, outputPath: null, async scope =>
            {
                bool recognized = true;
                foreach (WotDocumentSetEntry entry in scope.Documents.Entries)
                {
                    recognized &= entry.Document.Kind != WotDocumentKind.Unknown ||
                        entry.Document.TryGetEnvelope(out _) || entry.Document.TryGetNativeProjection(out _);
                }
                if (!recognized)
                {
                    return new JsonObject
                    {
                        ["valid"] = true,
                        ["projectionValidated"] = false,
                        ["validationScope"] = "JSON syntax only",
                        ["authoringChecksApplied"] = false,
                        ["message"] = "At least one document has no recognized OPC UA WoT Binding kind. " +
                            "No Binding projection or W3C TD conformance is claimed."
                    };
                }
                WotConversionResult<UANodeSet> converted = await ConvertAsync(scope).ConfigureAwait(false);
                return ConversionReport(converted.Success, converted.Diagnostics, input.StrictAuthoring);
            }, ct);
        }

        /// <summary>
        /// Prepares shipped binding plans without registering or activating an executor.
        /// </summary>
        [McpServerTool(Name = "wot_plan_bindings", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Plans WoT bindings and returns planner versions, maturity, supported forms and diagnostics. " +
            "Plans are non-executable; no protocol executor is activated. Explicit kind classifies unannotated JSON.")]
        public Task<CallToolResult> PlanBindingsAsync(
            [Description("Document input and explicit local/host-allowlisted dependencies.")]
            WotDocumentInput input,
            [Description("Optional kind for unannotated documents; must agree with any recognized document kind.")]
            WotDocumentKind kind = WotDocumentKind.Unknown,
            CancellationToken ct = default)
        {
            return ExecuteAsync(input, nodeSetInput: false, outputPath: null, async scope =>
            {
                if (kind is not (WotDocumentKind.Unknown or
                    WotDocumentKind.ThingDescription or WotDocumentKind.ThingModel))
                {
                    throw new ArgumentOutOfRangeException(nameof(kind));
                }
                var forms = new JsonArray();
                var unsupported = new JsonArray();
                var resolutionDiagnostics = new List<WotDiagnostic>();
                var bindingDiagnostics = new List<WotBindingDiagnostic>();
                int compiledCount = 0;
                int unsupportedCount = 0;
                for (int index = 0; index < scope.Documents.Entries.Count; index++)
                {
                    WotDocumentSetEntry entry = scope.Documents.Entries[index];
                    scope.Token.ThrowIfCancellationRequested();
                    WotDocumentKind selected = entry.Document.Kind;
                    if (selected != WotDocumentKind.Unknown && kind != WotDocumentKind.Unknown && kind != selected)
                    {
                        throw new ArgumentException("The explicit kind contradicts a recognized document kind.");
                    }
                    selected = selected == WotDocumentKind.Unknown ? kind : selected;
                    if (selected == WotDocumentKind.Unknown)
                    {
                        throw new ArgumentException(
                            "The document kind is unknown. Specify ThingDescription or ThingModel for planning.");
                    }
                    WotBindingPlanRequest request = await WotBindingPlanRequest.FromDocumentAsync(
                        entry.Href,
                        selected == WotDocumentKind.ThingModel
                            ? WoTDocumentKindEnum.ThingModel : WoTDocumentKindEnum.ThingDescription,
                        entry.Document.Utf8Json, scope, scope.Options.MaxJsonDepth,
                        diagnostics: resolutionDiagnostics, cancellationToken: scope.Token).ConfigureAwait(false);
                    WotBindingPlan plan = m_planners.Prepare(request);
                    scope.Token.ThrowIfCancellationRequested();
                    bindingDiagnostics.AddRange(plan.Diagnostics);
                    compiledCount += plan.CompiledForms.Length;
                    unsupportedCount += plan.UnsupportedForms.Length;
                    foreach (WotCompiledForm form in plan.CompiledForms)
                    {
                        if (forms.Count < WotDocumentJson.MaxItems)
                        {
                            forms.Add(new JsonObject
                            {
                                ["document"] = WotDocumentJson.Reference(entry.Href),
                                ["affordance"] = WotDocumentJson.Text(form.AffordanceName),
                                ["jsonPointer"] = WotDocumentJson.Text(form.JsonPointer),
                                ["binding"] = form.Binding.Id,
                                ["version"] = form.Binding.Version,
                                ["operation"] = form.OpToken,
                                ["contentType"] = WotDocumentJson.Text(form.Payload.ContentType),
                                ["isExecutable"] = false
                            });
                        }
                    }
                    foreach (WotAffordanceForm form in plan.UnsupportedForms)
                    {
                        if (unsupported.Count < WotDocumentJson.MaxItems)
                        {
                            unsupported.Add(new JsonObject
                            {
                                ["document"] = WotDocumentJson.Reference(entry.Href),
                                ["affordance"] = WotDocumentJson.Text(form.AffordanceName),
                                ["jsonPointer"] = WotDocumentJson.Text(form.JsonPointer)
                            });
                        }
                    }
                }
                JsonObject resolved = WotDocumentJson.Diagnostics(resolutionDiagnostics);
                JsonObject planned = WotDocumentJson.BindingDiagnostics(bindingDiagnostics);
                bool errors = resolved["errors"]!.GetValue<int>() != 0 || planned["errors"]!.GetValue<int>() != 0;
                return new JsonObject
                {
                    ["error"] = errors,
                    ["planningOnly"] = true,
                    ["hasExecutableForms"] = false,
                    ["fullySupported"] = !errors && unsupportedCount == 0,
                    ["planners"] = PlannerMetadata(),
                    ["compiledForms"] = forms,
                    ["compiledFormCount"] = compiledCount,
                    ["unsupportedForms"] = unsupported,
                    ["unsupportedFormCount"] = unsupportedCount,
                    ["truncated"] = compiledCount > forms.Count || unsupportedCount > unsupported.Count,
                    ["resolutionDiagnostics"] = resolved,
                    ["bindingDiagnostics"] = planned
                };
            }, ct);
        }

        /// <summary>
        /// Converts all documents in the input set to an atomically published NodeSet2 artifact.
        /// </summary>
        [McpServerTool(Name = "wot_to_nodeset", ReadOnly = false, Destructive = false, Idempotent = false)]
        [Description("Converts WoT JSON or a complete document-set ZIP to a new NodeSet2 file beneath TransferRoot. " +
            "Returns diagnostics, bytes and SHA-256. Preserves unknown vocabulary; no overwrite, partial publication " +
            "or UA connection.")]
        public Task<CallToolResult> ToNodeSetAsync(
            [Description("Document or tool-produced archive and explicit dependency/conformance choices.")]
            WotDocumentInput input,
            [Description("New NodeSet XML destination beneath TransferRoot; parent directory must already exist.")]
            string outputPath,
            CancellationToken ct = default)
        {
            string destination = outputPath ?? string.Empty;
            return ExecuteAsync(input, nodeSetInput: false, destination, async scope =>
            {
                WotConversionResult<UANodeSet> converted = await ConvertAsync(scope).ConfigureAwait(false);
                JsonObject report = ConversionReport(converted.Success, converted.Diagnostics, input.StrictAuthoring);
                if (!converted.Success)
                {
                    return report;
                }
                using var buffer = new WotDocumentBuffer(scope.Options.MaxNodeSetSize, scope.Token);
                converted.Value!.Write(buffer);
                scope.Token.ThrowIfCancellationRequested();
                report["artifact"] = await PublishAsync(destination, buffer.ToArray(), "application/xml", scope.Token)
                    .ConfigureAwait(false);
                return report;
            }, ct);
        }

        /// <summary>
        /// Converts a safely parsed NodeSet into all linked WoT documents and a single atomic ZIP artifact.
        /// </summary>
        [McpServerTool(Name = "wot_from_nodeset", ReadOnly = false, Destructive = false, Idempotent = false,
            OpenWorld = false)]
        [Description("Converts NodeSet2 into all linked WoT documents in a new root-confined ZIP. Returns verified " +
            "artifact metadata and diagnostics. Rejects DTDs/external XML; no flattening or overwrite. " +
            "Read the artifact with options.documentSetArchive=true; no UA connection or binding execution.")]
        public Task<CallToolResult> FromNodeSetAsync(
            [Description("NodeSet XML text/file plus explicit local type-declaration dependency documents.")]
            WotDocumentInput input,
            [Description("New document-set ZIP destination beneath TransferRoot.")]
            string outputPath,
            [Description("Stable root document token, without slashes, query or fragment, used for linked hrefs.")]
            string rootHref = "root",
            CancellationToken ct = default)
        {
            string destination = outputPath ?? string.Empty;
            return ExecuteAsync(input, nodeSetInput: true, destination, async scope =>
            {
                if (string.IsNullOrWhiteSpace(rootHref) || rootHref.Length > 256 ||
                    rootHref.IndexOfAny(['/', '\\', '?', '#', ':']) >= 0)
                {
                    throw new ArgumentException(
                        "rootHref must be a nonempty document token of at most 256 characters.");
                }
                UANodeSet nodeSet = await ReadNodeSetAsync(scope).ConfigureAwait(false);
                WotConversionResult<WotDocumentSet> converted = await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                    nodeSet, rootHref, options: scope.Options, nodeResolver: scope, cancellationToken: scope.Token)
                    .ConfigureAwait(false);
                using WotDocumentSet? documents = converted.Value;
                JsonObject report = ConversionReport(converted.Success, converted.Diagnostics, input.StrictAuthoring);
                if (!converted.Success || documents is null)
                {
                    return report;
                }
                using var buffer = new WotDocumentBuffer(scope.MaxTotalBytes, scope.Token);
                await WotDocumentArchive.WriteAsync(documents, buffer, scope.Options, scope.MaxTotalBytes, scope.Token)
                    .ConfigureAwait(false);
                report["documentCount"] = documents.Entries.Count;
                report["rootReference"] = WotDocumentJson.Reference(documents.RootHref);
                report["artifact"] = await PublishAsync(
                    destination, buffer.ToArray(), "application/zip", scope.Token).ConfigureAwait(false);
                return report;
            }, ct);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_state)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                m_operations++;
            }
            try
            {
                m_lifetime.Cancel();
            }
            finally
            {
                LeaveOperation();
            }
        }

        /// <summary>
        /// Shares lifetime, admission deadline, scope ownership and sanitized error translation.
        /// </summary>
        private Task<CallToolResult> ExecuteAsync(
            WotDocumentInput input, bool nodeSetInput, string? outputPath,
            Func<WotDocumentScope, ValueTask<JsonObject>> operation, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                EnterOperation();
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, m_lifetime.Token);
                    deadline.CancelAfter(m_options.OperationTimeoutMilliseconds);
                    bool admitted = false;
                    try
                    {
                        using var scope = new WotDocumentScope(
                            input, m_options, m_shared, m_files, m_clients, deadline.Token);
                        await m_gate.WaitAsync(deadline.Token).ConfigureAwait(false);
                        admitted = true;
                        if (outputPath is not null)
                        {
                            m_files.ValidateDownloadPath(outputPath);
                        }
                        await scope.InitializeAsync(nodeSetInput).ConfigureAwait(false);
                        JsonObject result = await operation(scope).ConfigureAwait(false);
                        if (result["artifact"] is null)
                        {
                            scope.Token.ThrowIfCancellationRequested();
                        }
                        result["provenance"] = scope.Provenance();
                        return result;
                    }
                    catch (OperationCanceledException) when (
                        !token.IsCancellationRequested && !m_lifetime.IsCancellationRequested)
                    {
                        return Error("OperationTimeout", "The whole document-operation deadline expired.");
                    }
                    catch (HttpRequestException)
                    {
                        return Error("DependencyHttpError",
                            "Dependency retrieval failed. Check the allowed origin, " +
                            "address policy, TLS and HTTP response.");
                    }
                    catch (JsonException)
                    {
                        return Error("InvalidJson", "The document or archive manifest is not valid bounded JSON.");
                    }
                    catch (XmlException)
                    {
                        return Error("InvalidXml", "The NodeSet XML is malformed or violates the bounded XML policy.");
                    }
                    catch (Exception exception) when (exception is ArgumentException or FormatException or
                        IOException or InvalidOperationException or
                        UnauthorizedAccessException or NotSupportedException)
                    {
                        return Error(exception.GetType().Name, WotDocumentJson.Text(exception.Message)!);
                    }
                    finally
                    {
                        if (admitted)
                        {
                            m_gate.Release();
                        }
                    }
                }
                finally
                {
                    LeaveOperation();
                }
            }, ct);
        }

        /// <summary>
        /// Starts a tracked invocation so disposal cannot release the semaphore beneath an active caller.
        /// </summary>
        private void EnterOperation()
        {
            lock (m_state)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                m_operations++;
            }
        }

        /// <summary>
        /// Releases owned synchronization resources after the last invocation unwinds.
        /// </summary>
        private void LeaveOperation()
        {
            bool release;
            lock (m_state)
            {
                m_operations--;
                release = m_disposed && m_operations == 0;
            }
            if (release)
            {
                m_gate.Dispose();
                m_lifetime.Dispose();
            }
        }

        /// <summary>
        /// Resolves supplied set-local documents before optional external documents and schemas.
        /// </summary>
        private static ValueTask<WotConversionResult<UANodeSet>> ConvertAsync(WotDocumentScope scope)
        {
            return WotNodeSetConverter.ToNodeSetWithResolversAsync(
                scope.Documents, scope, new WotExternalSchemaResolver(scope), scope.Options, scope, scope.Token);
        }

        /// <summary>
        /// Preflights XML depth, top-level node count and entity policy before the existing serializer runs.
        /// </summary>
        private static async ValueTask<UANodeSet> ReadNodeSetAsync(WotDocumentScope scope)
        {
            using var source = new MemoryStream(scope.SourceBytes.ToArray(), writable: false);
            var settings = new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = scope.Options.MaxNodeSetSize,
                CloseInput = false
            };
            int nodes = 0;
            using (XmlReader reader = XmlReader.Create(source, settings))
            {
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    scope.Token.ThrowIfCancellationRequested();
                    if (reader.Depth > scope.Options.MaxXmlDepth)
                    {
                        throw new FormatException("The NodeSet exceeds the XML depth limit.");
                    }
                    if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1 &&
                        reader.LocalName.StartsWith("UA", StringComparison.Ordinal) &&
                        ++nodes > scope.Options.MaxNodeCount)
                    {
                        throw new FormatException("The NodeSet exceeds the node count limit.");
                    }
                }
            }
            scope.Token.ThrowIfCancellationRequested();
            source.Position = 0;
            UANodeSet result = UANodeSet.Read(source) ??
                throw new FormatException("The XML does not contain a NodeSet.");
            scope.Token.ThrowIfCancellationRequested();
            return result;
        }

        /// <summary>
        /// Publishes only a fully serialized artifact using the shared no-overwrite transfer infrastructure.
        /// </summary>
        private async ValueTask<JsonObject> PublishAsync(
            string outputPath, ReadOnlyMemory<byte> bytes, string contentType, CancellationToken ct)
        {
            string digest = Convert.ToHexString(SHA256.HashData(bytes.Span));
            JsonObject result = await m_files.DownloadAsync(outputPath,
                async (target, token) => await target.WriteAsync(bytes, token).ConfigureAwait(false), ct)
                .ConfigureAwait(false);
            result["contentType"] = contentType;
            result["sha256"] = digest;
            return result;
        }

        /// <summary>
        /// Keeps projection acceptance distinct from W3C certification and strict authoring.
        /// </summary>
        private static JsonObject ConversionReport(
            bool success, IEnumerable<WotDiagnostic> diagnostics, bool authoring)
        {
            return new JsonObject
            {
                ["error"] = !success,
                ["valid"] = success,
                ["projectionValidated"] = true,
                ["validationScope"] = "OPC UA WoT Binding projection; not W3C TD certification",
                ["mode"] = authoring ? "strictAuthoring" : "consumer",
                ["diagnostics"] = WotDocumentJson.Diagnostics(diagnostics)
            };
        }

        /// <summary>
        /// Returns fixed planner capabilities without runtime executors or raw endpoint/security data.
        /// </summary>
        private JsonArray PlannerMetadata()
        {
            var result = new JsonArray();
            foreach (IWotProtocolBinder binder in m_planners.Binders)
            {
                result.Add(new JsonObject
                {
                    ["id"] = binder.Identity.Id,
                    ["version"] = binder.Identity.Version,
                    ["bindingUri"] = binder.Identity.BindingUri,
                    ["maturity"] = binder.Capability.Source.MaturityText,
                    ["specificationUri"] = binder.Capability.Source.SpecificationUri,
                    ["specificationVersion"] = binder.Capability.Source.Version,
                    ["isExecutable"] = false
                });
            }
            return result;
        }

        /// <summary>
        /// Returns bounded names and exact counts, never affordance bodies or inline credentials.
        /// </summary>
        private static JsonObject AffordanceNames(IReadOnlyDictionary<string, JsonElement> affordances)
        {
            var names = new JsonArray();
            foreach (string name in affordances.Keys)
            {
                if (names.Count >= WotDocumentJson.MaxItems)
                {
                    break;
                }
                names.Add(WotDocumentJson.Text(name));
            }
            return new JsonObject
            {
                ["names"] = names,
                ["count"] = affordances.Count,
                ["truncated"] = affordances.Count > names.Count
            };
        }

        /// <summary>
        /// Uses the shared MCP error envelope without leaking transport exception URLs.
        /// </summary>
        private static JsonObject Error(string type, string message)
        {
            return new JsonObject { ["error"] = true, ["errorType"] = type, ["message"] = message };
        }

        private readonly McpFileTransfers m_files;
        private readonly OpcUaMcpOptions m_shared;
        private readonly WotMcpOptions m_options;
        private readonly IHttpClientFactory m_clients;
        private readonly WotProtocolBinderRegistry m_planners;
        private readonly SemaphoreSlim m_gate;
        private readonly CancellationTokenSource m_lifetime;
        private readonly Lock m_state = new();
        private int m_operations;
        private bool m_disposed;
    }
}
