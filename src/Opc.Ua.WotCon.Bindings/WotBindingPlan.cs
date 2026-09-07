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
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// A request to validate and compile a single resource's interaction forms
    /// into a binding plan. The request is side-effect free and carries the
    /// extracted forms, the secret-free security definitions, the base URI and the
    /// explicit binder selection pinned on the resource.
    /// </summary>
    public sealed class WotBindingPlanRequest
    {
        /// <summary>
        /// Initializes a new plan request.
        /// </summary>
        public WotBindingPlanRequest(
            string resourceXid,
            WoTDocumentKindEnum kind,
            ImmutableArray<WotAffordanceForm> forms,
            ImmutableDictionary<string, WotSecurityDefinition>? securityDefinitions = null,
            string? baseUri = null,
            WotBindingSelectionContext? selection = null,
            ImmutableDictionary<string, string>? namespacePrefixes = null,
            WotEventSelectionCatalog? eventSelections = null)
        {
            ResourceXid = resourceXid ?? string.Empty;
            Kind = kind;
            Forms = forms.IsDefault ? [] : forms;
            SecurityDefinitions = securityDefinitions ?? ImmutableDictionary<string, WotSecurityDefinition>.Empty;
            BaseUri = baseUri;
            Selection = selection ?? WotBindingSelectionContext.Empty;
            NamespacePrefixes = namespacePrefixes ?? ImmutableDictionary<string, string>.Empty;
            EventSelections = eventSelections ?? WotEventSelectionCatalog.Empty;
            IsDeclarationContext = kind == WoTDocumentKindEnum.ThingModel;
        }

        /// <summary>
        /// Gets the resource xid.
        /// </summary>
        public string ResourceXid { get; }

        /// <summary>
        /// Gets the document kind.
        /// </summary>
        public WoTDocumentKindEnum Kind { get; }

        /// <summary>
        /// Gets the affordance forms parsed from the document.
        /// </summary>
        public ImmutableArray<WotAffordanceForm> Forms { get; }

        /// <summary>
        /// Gets the secret-free security definitions declared by the document.
        /// </summary>
        public ImmutableDictionary<string, WotSecurityDefinition> SecurityDefinitions { get; }

        /// <summary>
        /// Gets the Thing base URI used for relative href resolution, if any.
        /// </summary>
        public string? BaseUri { get; }

        /// <summary>
        /// Gets the explicit binder selection pinned on the resource.
        /// </summary>
        public WotBindingSelectionContext Selection { get; }

        /// <summary>
        /// Gets the namespace prefixes the document's <c>@context</c> binds.
        /// </summary>
        public ImmutableDictionary<string, string> NamespacePrefixes { get; }

        /// <summary>
        /// Gets the event field selections resolved from the document's
        /// EventType <c>tm:ref</c> links before planning
        /// (WoT Binding Section 6.1).
        /// </summary>
        /// <remarks>
        /// Resolving an EventType link follows document references, which is
        /// asynchronous, and <see cref="IWotBinderRegistry.Prepare"/> is
        /// deliberately synchronous and side-effect free. The links are
        /// therefore resolved once, by <see cref="FromDocumentAsync"/> or by the
        /// materialization coordinator, and planning reads the immutable result
        /// rather than performing I/O of its own. A request built without a
        /// resolver carries an empty catalog, which is exactly right for a
        /// document whose affordances state no link: the implicit
        /// <c>BaseEventType</c> default needs no resolution.
        /// </remarks>
        public WotEventSelectionCatalog EventSelections { get; }

        /// <summary>
        /// Gets the local executable declarations, separate from upstream form
        /// addresses. Empty for a type-only document or a transport-only request.
        /// </summary>
        public ArrayOf<WotProjectedAffordance> ProjectedAffordances { get; private init; } = [];

        /// <summary>
        /// Gets whether the source belongs to a type-declaration containment
        /// tree, even when a linked child document is shaped as an Object TD.
        /// </summary>
        public bool IsDeclarationContext { get; private init; }

        private ImmutableHashSet<ExpandedNodeId> LocalVariables { get; init; } = ImmutableHashSet<ExpandedNodeId>.Empty;

        /// <summary>
        /// Returns a request carrying conversion-resolved local declarations.
        /// </summary>
        public WotBindingPlanRequest WithProjectedAffordances(ArrayOf<WotProjectedAffordance> affordances)
        {
            return new WotBindingPlanRequest(
                ResourceXid, Kind, Forms, SecurityDefinitions, BaseUri, Selection, NamespacePrefixes, EventSelections)
            {
                ProjectedAffordances = affordances,
                IsDeclarationContext = IsDeclarationContext,
                LocalVariables = LocalVariables
            };
        }

        /// <summary>
        /// Returns a request with an explicitly resolved declaration context.
        /// Type definitions alone do not establish this context.
        /// </summary>
        public WotBindingPlanRequest WithDeclarationContext(bool isDeclaration)
        {
            return new WotBindingPlanRequest(
                ResourceXid, Kind, Forms, SecurityDefinitions, BaseUri, Selection, NamespacePrefixes, EventSelections)
            {
                ProjectedAffordances = ProjectedAffordances,
                IsDeclarationContext = isDeclaration,
                LocalVariables = LocalVariables
            };
        }

        /// <summary>
        /// Supplies the converted projection root for declarations that did not
        /// author a local owner identity.
        /// </summary>
        public WotBindingPlanRequest WithProjectionRoot(ExpandedNodeId rootNodeId)
        {
            return WithProjectedAffordances(ProjectedAffordances.ConvertAll(
                affordance => affordance.WithDefaultOwner(rootNodeId.ToString())));
        }

        /// <summary>
        /// Builds a plan context from this request.
        /// </summary>
        public WotBindingPlanContext CreateContext(IWotCodecRegistry codecs, WotBindingBounds bounds)
        {
            return new WotBindingPlanContext(
                SecurityDefinitions, codecs,
                IsDeclarationContext ? WoTDocumentKindEnum.ThingModel : Kind,
                BaseUri, bounds, NamespacePrefixes,
                EventSelections);
        }

        /// <summary>
        /// Builds a plan request from a WoT document: it extracts the forms, the
        /// base URI, the secret-free security definitions and the namespace
        /// prefixes the document's <c>@context</c> binds.
        /// </summary>
        /// <remarks>
        /// The request carries no resolved event selections, so an affordance
        /// that links its EventType definition with <c>tm:ref</c> is reported as
        /// unresolved rather than planned. Use <see cref="FromDocumentAsync"/>
        /// where the caller holds the sibling documents those links name.
        /// </remarks>
        public static WotBindingPlanRequest FromDocument(
            string resourceXid,
            WoTDocumentKindEnum kind,
            ReadOnlyMemory<byte> document,
            int maxJsonDepth = 64,
            WotBindingSelectionContext? selection = null)
        {
            return Build(resourceXid, kind, document, maxJsonDepth, selection, null);
        }

        /// <summary>
        /// Builds a plan request from a WoT document and an event field
        /// selection catalog a caller has already resolved.
        /// </summary>
        /// <remarks>
        /// This is the seam a host uses when it resolves the EventType links of
        /// a whole closure once and then plans each document: the catalog is
        /// immutable, so planning stays synchronous and side-effect free.
        /// </remarks>
        /// <param name="resourceXid">The resource xid.</param>
        /// <param name="kind">The document kind.</param>
        /// <param name="document">The UTF-8 document.</param>
        /// <param name="eventSelections">The resolved event field selections.</param>
        /// <param name="maxJsonDepth">The JSON depth bound.</param>
        /// <param name="selection">The explicit binder selection, if any.</param>
        /// <returns>The plan request.</returns>
        public static WotBindingPlanRequest FromDocument(
            string resourceXid,
            WoTDocumentKindEnum kind,
            ReadOnlyMemory<byte> document,
            WotEventSelectionCatalog eventSelections,
            int maxJsonDepth = 64,
            WotBindingSelectionContext? selection = null)
        {
            return Build(resourceXid, kind, document, maxJsonDepth, selection, eventSelections);
        }

        /// <summary>
        /// Builds a plan request from a WoT document, resolving the EventType
        /// definitions its event affordances link to with <c>tm:ref</c> before
        /// the synchronous planning that consumes them
        /// (WoT Binding Section 6.1).
        /// </summary>
        /// <remarks>
        /// This is the factory a standalone consumer uses: a planner never
        /// performs I/O, so a document whose events name their EventType
        /// definitions has to be prepared through a resolver that holds those
        /// documents. Resolution is bounded and local — the resolver is asked
        /// for documents it already holds and no URI is dereferenced over the
        /// network.
        /// </remarks>
        /// <param name="resourceXid">The resource xid.</param>
        /// <param name="kind">The document kind.</param>
        /// <param name="document">The UTF-8 document.</param>
        /// <param name="thingResolver">
        /// Resolves the sibling documents an EventType reference names.
        /// </param>
        /// <param name="maxJsonDepth">The JSON depth bound.</param>
        /// <param name="selection">The explicit binder selection, if any.</param>
        /// <param name="diagnostics">
        /// Receives the diagnostics resolution produced, or <c>null</c> to
        /// discard them. An unresolved link is also reported by planning, so a
        /// caller that discards them still learns that the form is unsupported.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The plan request.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="thingResolver"/> is <c>null</c>.
        /// </exception>
        public static async ValueTask<WotBindingPlanRequest> FromDocumentAsync(
            string resourceXid,
            WoTDocumentKindEnum kind,
            ReadOnlyMemory<byte> document,
            IWotThingResolver thingResolver,
            int maxJsonDepth = 64,
            WotBindingSelectionContext? selection = null,
            IList<WotDiagnostic>? diagnostics = null,
            CancellationToken cancellationToken = default)
        {
            if (thingResolver is null)
            {
                throw new ArgumentNullException(nameof(thingResolver));
            }
            WotEventSelectionCatalog catalog = await ResolveEventSelectionsAsync(
                    document, thingResolver, maxJsonDepth, diagnostics, cancellationToken)
                .ConfigureAwait(false);
            return Build(resourceXid, kind, document, maxJsonDepth, selection, catalog);
        }

        /// <summary>
        /// Resolves the event field selections a document states, so a caller
        /// that builds its own request can carry the result into it
        /// (WoT Binding Section 6.1).
        /// </summary>
        /// <param name="document">The UTF-8 document.</param>
        /// <param name="thingResolver">
        /// Resolves the sibling documents an EventType reference names.
        /// </param>
        /// <param name="maxJsonDepth">The JSON depth bound.</param>
        /// <param name="diagnostics">
        /// Receives the diagnostics resolution produced, or <c>null</c> to
        /// discard them.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>
        /// The resolved catalog, empty where the document states no selection
        /// or where resolution failed.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="thingResolver"/> is <c>null</c>.
        /// </exception>
        public static async ValueTask<WotEventSelectionCatalog> ResolveEventSelectionsAsync(
            ReadOnlyMemory<byte> document,
            IWotThingResolver thingResolver,
            int maxJsonDepth = 64,
            IList<WotDiagnostic>? diagnostics = null,
            CancellationToken cancellationToken = default)
        {
            if (thingResolver is null)
            {
                throw new ArgumentNullException(nameof(thingResolver));
            }
            var options = new WotNodeSetConverterOptions();
            if (maxJsonDepth > 0)
            {
                options.MaxJsonDepth = maxJsonDepth;
            }
            WotDocument parsed;
            try
            {
                parsed = WotDocument.Parse(document, options);
            }
            catch (Exception exception) when (exception is JsonException or FormatException)
            {
                // A malformed document produces no forms either, so planning
                // already reports it; resolution has nothing to add.
                return WotEventSelectionCatalog.Empty;
            }
            using (parsed)
            {
                var resolver = new WotEventSelectionResolver(thingResolver, options);
                WotConversionResult<WotEventSelectionCatalog> result = await resolver
                    .ResolveAsync(parsed, null, cancellationToken)
                    .ConfigureAwait(false);
                if (diagnostics is not null)
                {
                    foreach (WotDiagnostic diagnostic in result.Diagnostics)
                    {
                        diagnostics.Add(diagnostic);
                    }
                }
                return result.Value ?? WotEventSelectionCatalog.Empty;
            }
        }

        internal bool IsLocalProperty(JsonElement affordance)
        {
            if (affordance.TryGetProperty("uav:mapToNodeId", out _) ||
                affordance.TryGetProperty("uav:mapByFieldPath", out _))
            {
                return false;
            }
            if (affordance.TryGetProperty("const", out _) || affordance.TryGetProperty("default", out _))
            {
                return true;
            }
            return affordance.TryGetProperty("uav:id", out JsonElement id) &&
                id.ValueKind == JsonValueKind.String && id.GetString() is string text &&
                ExpandedNodeId.TryParse(text, out ExpandedNodeId nodeId) && LocalVariables.Contains(nodeId);
        }

        private static WotBindingPlanRequest Build(
            string resourceXid,
            WoTDocumentKindEnum kind,
            ReadOnlyMemory<byte> document,
            int maxJsonDepth,
            WotBindingSelectionContext? selection,
            WotEventSelectionCatalog? eventSelections)
        {
            ImmutableArray<WotAffordanceForm> forms = WotFormExtractor.Extract(document, maxJsonDepth);
            ImmutableDictionary<string, WotSecurityDefinition> definitions =
                ImmutableDictionary<string, WotSecurityDefinition>.Empty;
            ImmutableDictionary<string, string> prefixes = ImmutableDictionary<string, string>.Empty;
            ArrayOf<WotProjectedAffordance> projectedAffordances = [];
            ImmutableHashSet<ExpandedNodeId> localVariables = ImmutableHashSet<ExpandedNodeId>.Empty;
            string? baseUri = null;
            try
            {
                // One parse for every document-level member: the forms are
                // extracted above from their own pass, and re-parsing the whole
                // document once per member would cost a copy of it each time.
                var options = new JsonDocumentOptions { MaxDepth = maxJsonDepth <= 0 ? 64 : maxJsonDepth };
                using var json = JsonDocument.Parse(document, options);
                JsonElement root = json.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    definitions = ReadSecurityDefinitions(root);
                    baseUri = ReadBase(root);
                    prefixes = ReadNamespacePrefixes(root);
                    localVariables = ReadNativeVariableIds(root);
                    if (kind == WoTDocumentKindEnum.ThingDescription)
                    {
                        projectedAffordances = WotProjectedAffordance.Extract(root);
                    }
                }
            }
            catch (JsonException)
            {
            }
            return new WotBindingPlanRequest(
                resourceXid, kind, forms, definitions, baseUri, selection, prefixes,
                eventSelections)
            {
                ProjectedAffordances = projectedAffordances,
                LocalVariables = localVariables
            };
        }

        private static ImmutableHashSet<ExpandedNodeId> ReadNativeVariableIds(JsonElement root)
        {
            if (!root.TryGetProperty("uav:nodes", out JsonElement native) ||
                native.ValueKind != JsonValueKind.Object ||
                !native.TryGetProperty("profileVersion", out JsonElement version) ||
                version.ValueKind != JsonValueKind.String || version.GetString() != "1.0" ||
                !native.TryGetProperty("nodes", out JsonElement nodes) || nodes.ValueKind != JsonValueKind.Array)
            {
                return ImmutableHashSet<ExpandedNodeId>.Empty;
            }
            var namespaces = new NamespaceTable();
            if (native.TryGetProperty("namespaceUris", out JsonElement uris) && uris.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement uri in uris.EnumerateArray())
                {
                    if (uri.ValueKind == JsonValueKind.String && uri.GetString() is string namespaceUri)
                    {
                        namespaces.Append(namespaceUri);
                    }
                }
            }
            ImmutableHashSet<ExpandedNodeId>.Builder values = ImmutableHashSet.CreateBuilder<ExpandedNodeId>();
            foreach (JsonElement node in nodes.EnumerateArray())
            {
                if (node.ValueKind == JsonValueKind.Object &&
                    node.TryGetProperty("nodeClass", out JsonElement nodeClass) &&
                    nodeClass.ValueKind == JsonValueKind.String && nodeClass.GetString() == "Variable" &&
                    node.TryGetProperty("nodeId", out JsonElement identifier) &&
                    identifier.ValueKind == JsonValueKind.String && identifier.GetString() is string id &&
                    NodeId.TryParse(id, out NodeId local) && local.NamespaceIndex < namespaces.Count)
                {
                    values.Add(NodeId.ToExpandedNodeId(local, namespaces));
                }
            }
            return values.ToImmutable();
        }

        private static ImmutableDictionary<string, WotSecurityDefinition> ReadSecurityDefinitions(
            JsonElement root)
        {
            ImmutableDictionary<string, WotSecurityDefinition>.Builder builder =
                ImmutableDictionary.CreateBuilder<string, WotSecurityDefinition>(
                    StringComparer.Ordinal);
            if (root.TryGetProperty("securityDefinitions", out JsonElement definitions) &&
                definitions.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty definition in definitions.EnumerateObject())
                {
                    builder[definition.Name] = WotSecurityDefinition.Parse(definition.Name, definition.Value);
                }
            }
            return builder.ToImmutable();
        }

        private static string? ReadBase(JsonElement root)
        {
            return root.TryGetProperty("base", out JsonElement baseElement) &&
                baseElement.ValueKind == JsonValueKind.String
                ? baseElement.GetString()
                : null;
        }

        /// <summary>
        /// Reads the prefix bindings of the document's <c>@context</c>
        /// (WoT Binding Section 5.8). Only string-valued members are prefix
        /// bindings; a scoped context object or a keyword such as
        /// <c>@language</c> is not one.
        /// </summary>
        private static ImmutableDictionary<string, string> ReadNamespacePrefixes(JsonElement root)
        {
            if (!root.TryGetProperty("@context", out JsonElement context))
            {
                return ImmutableDictionary<string, string>.Empty;
            }
            ImmutableDictionary<string, string>.Builder builder =
                ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            CollectNamespacePrefixes(context, builder);
            return builder.ToImmutable();
        }

        private static void CollectNamespacePrefixes(
            JsonElement context, ImmutableDictionary<string, string>.Builder builder)
        {
            if (context.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in context.EnumerateArray())
                {
                    CollectNamespacePrefixes(entry, builder);
                }
                return;
            }
            if (context.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            foreach (JsonProperty member in context.EnumerateObject())
            {
                if (member.Value.ValueKind == JsonValueKind.String &&
                    member.Name.Length > 0 &&
                    member.Name[0] != '@')
                {
                    string? uri = member.Value.GetString();
                    if (!string.IsNullOrEmpty(uri))
                    {
                        builder[member.Name] = uri!;
                    }
                }
            }
        }
    }

    /// <summary>
    /// The immutable result of preparing bindings for one resource. It holds the
    /// participating capability snapshots, the compiled (executable and
    /// non-executable) forms, the forms no binder validated and the structured
    /// diagnostics. A strict closure fails when <see cref="FullySupported"/> is
    /// <c>false</c>; otherwise unsupported forms materialize as degraded nodes.
    /// </summary>
    public sealed class WotBindingPlan
    {
        /// <summary>
        /// Initializes a new immutable binding plan.
        /// </summary>
        public WotBindingPlan(
            string resourceXid,
            ImmutableArray<WoTBindingCapabilityDataType> capabilities,
            ImmutableArray<WotCompiledForm> compiledForms,
            ImmutableArray<WotAffordanceForm> unsupportedForms,
            ImmutableArray<WotBindingDiagnostic> diagnostics)
        {
            ResourceXid = resourceXid ?? string.Empty;
            Capabilities = capabilities.IsDefault
                ? [] : capabilities;
            CompiledForms = compiledForms.IsDefault
                ? [] : compiledForms;
            UnsupportedForms = unsupportedForms.IsDefault
                ? [] : unsupportedForms;
            Diagnostics = diagnostics.IsDefault
                ? [] : diagnostics;
        }

        /// <summary>
        /// An empty plan (no forms, no capabilities).
        /// </summary>
        public static WotBindingPlan Empty { get; } = new WotBindingPlan(
            string.Empty,
            [],
            [],
            [],
            []);

        /// <summary>
        /// Gets the resource xid the plan was prepared for.
        /// </summary>
        public string ResourceXid { get; }

        /// <summary>
        /// Gets the participating binding capability snapshots.
        /// </summary>
        public ImmutableArray<WoTBindingCapabilityDataType> Capabilities { get; }

        /// <summary>
        /// Gets the compiled (executable and non-executable) forms.
        /// </summary>
        public ImmutableArray<WotCompiledForm> CompiledForms { get; }

        /// <summary>
        /// Gets the forms no binder validated.
        /// </summary>
        public ImmutableArray<WotAffordanceForm> UnsupportedForms { get; }

        /// <summary>
        /// Gets the structured diagnostics produced during Prepare.
        /// </summary>
        public ImmutableArray<WotBindingDiagnostic> Diagnostics { get; }

        /// <summary>
        /// Gets the generation-local method and event declarations.
        /// </summary>
        public ArrayOf<WotProjectedAffordance> ProjectedAffordances { get; private init; } = [];

        /// <summary>
        /// Gets whether these affordances are model declarations rather than
        /// operations on materialized instances.
        /// </summary>
        public bool IsDeclarationContext { get; private init; }

        /// <summary>
        /// Gets whether every form was validated by a binder.
        /// </summary>
        public bool FullySupported => UnsupportedForms.IsEmpty;

        /// <summary>
        /// Gets whether the plan compiled at least one executable form.
        /// </summary>
        public bool HasExecutableForms => CompiledForms.Any(f => f.IsExecutable);

        /// <summary>
        /// Gets whether the plan compiled at least one non-executable form.
        /// </summary>
        public bool HasNonExecutableForms => CompiledForms.Any(f => !f.IsExecutable);

        /// <summary>
        /// Returns a plan carrying conversion-resolved local declarations.
        /// </summary>
        public WotBindingPlan WithProjectedAffordances(ArrayOf<WotProjectedAffordance> affordances)
        {
            return new WotBindingPlan(ResourceXid, Capabilities, CompiledForms, UnsupportedForms, Diagnostics)
            {
                ProjectedAffordances = affordances,
                IsDeclarationContext = IsDeclarationContext
            };
        }

        /// <summary>
        /// Returns a plan with its source's resolved declaration context.
        /// All declarations remain present; only runtime activation is excluded.
        /// </summary>
        public WotBindingPlan WithDeclarationContext(bool isDeclaration)
        {
            return new WotBindingPlan(ResourceXid, Capabilities, CompiledForms, UnsupportedForms, Diagnostics)
            {
                ProjectedAffordances = ProjectedAffordances,
                IsDeclarationContext = isDeclaration
            };
        }
    }
}
