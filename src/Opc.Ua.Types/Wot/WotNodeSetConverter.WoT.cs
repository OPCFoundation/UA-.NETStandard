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
 *
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
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// WoT Thing Model and Thing Description to NodeSet2 synthesis for the
    /// <see cref="WotNodeSetConverter"/>.
    /// </summary>
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// Restores or synthesizes the NodeSet2 document described by a WoT
        /// document, throwing on any error diagnostic.
        /// </summary>
        /// <remarks>
        /// This conversion follows no document link, so an event affordance
        /// that states its field selection with <c>tm:ref</c> or
        /// <c>uav:eventSelectClauses</c> (WoT Binding Section 6.1) is reported
        /// rather than converted without the fields the linked definition
        /// declares. Use <c>ToNodeSetResultAsync</c> with an
        /// <see cref="IWotThingResolver"/> to convert such a document.
        /// </remarks>
        /// <param name="document">The WoT document.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <returns>The restored or synthesized NodeSet2 document.</returns>
        /// <exception cref="FormatException">Thrown when the conversion fails.</exception>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="document"/> is <c>null</c>.
        /// </exception>
        public static UANodeSet ToNodeSet(
            WotDocument document,
            WotNodeSetConverterOptions? options = null)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var diagnostics = new List<WotDiagnostic>();
            UANodeSet? nodeSet = ToNodeSetCore(document, options, null, null, null, diagnostics);
            ApplyIdentifierLeniency(diagnostics, options);
            ThrowIfErrors(diagnostics);
            return nodeSet
                ?? throw new FormatException("The WoT document could not be converted to a NodeSet.");
        }

        /// <summary>
        /// Parses and restores or synthesizes a NodeSet2 document from UTF-8 WoT
        /// JSON, throwing on any error diagnostic.
        /// </summary>
        /// <param name="utf8Json">The UTF-8 encoded WoT document.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <returns>The restored or synthesized NodeSet2 document.</returns>
        public static UANodeSet ToNodeSet(
            ReadOnlyMemory<byte> utf8Json,
            WotNodeSetConverterOptions? options = null)
        {
            using var document = WotDocument.Parse(utf8Json, options);
            return ToNodeSet(document, options);
        }

        /// <summary>
        /// Restores or synthesizes the NodeSet2 document described by a WoT
        /// document, returning structured diagnostics together with the result.
        /// </summary>
        /// <param name="document">The WoT document.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <param name="thingResolver">An optional resolver for referenced TD/TM documents.</param>
        /// <param name="resolutionContext">An optional resolution context for cycle and limit tracking.</param>
        /// <param name="cancellationToken">A token that cancels asynchronous resolution.</param>
        /// <returns>The conversion result and its diagnostics.</returns>
        public static ValueTask<WotConversionResult<UANodeSet>> ToNodeSetResultAsync(
            WotDocument document,
            WotNodeSetConverterOptions? options = null,
            IWotThingResolver? thingResolver = null,
            WotResolutionContext? resolutionContext = null,
            CancellationToken cancellationToken = default)
        {
            return ToNodeSetResultAsync(
                document, options, thingResolver, resolutionContext, null, cancellationToken);
        }

        /// <summary>
        /// Restores or synthesizes the NodeSet2 document described by a WoT
        /// document, resolving names against the WoT Binding Section 5.1.5
        /// local context.
        /// </summary>
        /// <param name="document">The WoT document.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <param name="thingResolver">Resolves referenced TD/TM documents.</param>
        /// <param name="resolutionContext">The active resolution context.</param>
        /// <param name="nodeResolver">
        /// Resolves a name or identifier to the OPC UA Node it names. When
        /// omitted nothing is held, so a document that binds to an existing
        /// type is reported as unresolved rather than silently mistyped.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The conversion result and its diagnostics.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="document"/> is <c>null</c>.
        /// </exception>
        public static ValueTask<WotConversionResult<UANodeSet>> ToNodeSetResultAsync(
            WotDocument document,
            WotNodeSetConverterOptions? options,
            IWotThingResolver? thingResolver,
            WotResolutionContext? resolutionContext,
            IWotNodeResolver? nodeResolver,
            CancellationToken cancellationToken = default)
        {
            return ToNodeSetResultAsync(
                document,
                options,
                thingResolver,
                resolutionContext,
                nodeResolver,
                null,
                cancellationToken);
        }

        /// <summary>
        /// Restores or synthesizes the NodeSet2 document described by a WoT
        /// document, resolving names against the WoT Binding Section 5.1.5
        /// local context and every <c>uav:externalSchema</c> reference against
        /// the configured providers.
        /// </summary>
        /// <param name="document">The WoT document.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <param name="thingResolver">Resolves referenced TD/TM documents.</param>
        /// <param name="resolutionContext">The active resolution context.</param>
        /// <param name="nodeResolver">
        /// Resolves a name or identifier to the OPC UA Node it names. When
        /// omitted nothing is held, so a document that binds to an existing
        /// type is reported as unresolved rather than silently mistyped.
        /// </param>
        /// <param name="schemaResolver">
        /// Resolves <c>uav:externalSchema</c> references through an ordered set
        /// of providers. When omitted, or configured with no provider, a
        /// reference is carried but never fetched: an external schema reference
        /// is an arbitrary IRI in a document a consumer did not write.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The conversion result and its diagnostics.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="document"/> is <c>null</c>.
        /// </exception>
        public static async ValueTask<WotConversionResult<UANodeSet>> ToNodeSetResultAsync(
            WotDocument document,
            WotNodeSetConverterOptions? options,
            IWotThingResolver? thingResolver,
            WotResolutionContext? resolutionContext,
            IWotNodeResolver? nodeResolver,
            WotExternalSchemaResolver? schemaResolver,
            CancellationToken cancellationToken = default)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            var diagnostics = new List<WotDiagnostic>();
            WotThingCatalog? thingCatalog = null;
            WotThingCatalog? parentCatalog = null;
            WotReferenceTypeCatalog? referenceTypeCatalog = null;
            WotEventSelectionCatalog? eventSelections = null;
            bool eventSelectionsResolved = false;
            ArrayOf<WotDataTypeDefinitionSource> dataTypeSources = [];
            Dictionary<(string NamespaceUri, string Name), string?>? dataTypeNames = null;
            var documentSet = thingResolver as DocumentSetThingResolver;
            ArrayOf<WotDocument> dataTypeOwners = documentSet?.DataTypeOwners ?? [];
            if (!TakesRestorePath(document))
            {
                referenceTypeCatalog = await PreresolveReferenceTypesAsync(
                    document,
                    nodeResolver,
                    cancellationToken).ConfigureAwait(false);
                if (documentSet?.DataTypes is null)
                {
                    dataTypeSources = await PreresolveDataTypeDefinitionsAsync(
                        document, dataTypeOwners, nodeResolver, options ?? new WotNodeSetConverterOptions(),
                        diagnostics, cancellationToken).ConfigureAwait(false);
                    dataTypeNames = await PreresolveDataTypeNamesAsync(
                        document, dataTypeSources, dataTypeOwners, nodeResolver,
                        diagnostics, cancellationToken).ConfigureAwait(false);
                }
            }
            if (thingResolver is not null)
            {
                options ??= new WotNodeSetConverterOptions();
                options.Validate();
                resolutionContext ??= new WotResolutionContext(options.ToResolverOptions());
                thingCatalog = new WotThingCatalog();
                parentCatalog = new WotThingCatalog();
                await PreresolveThingReferencesAsync(
                    document,
                    options,
                    thingResolver,
                    resolutionContext,
                    thingCatalog,
                    referenceTypeCatalog,
                    diagnostics,
                    cancellationToken).ConfigureAwait(false);
                await PreresolveParentReferencesAsync(
                    document,
                    options,
                    thingResolver,
                    resolutionContext,
                    parentCatalog,
                    diagnostics,
                    cancellationToken).ConfigureAwait(false);

                // WoT Binding Section 6.1: an event affordance names the
                // EventType definition its fields are selected from, and the
                // synthesis below needs that definition's field set before it
                // can materialize the EventType's Nodes. Resolution follows
                // document links, so it happens here, once, and the synchronous
                // synthesis reads the result.
                if (!TakesRestorePath(document))
                {
                    var selectionResolver = new WotEventSelectionResolver(thingResolver, options);
                    WotConversionResult<WotEventSelectionCatalog> selectionResult =
                        await selectionResolver
                            .ResolveAsync(document, resolutionContext, cancellationToken)
                            .ConfigureAwait(false);
                    diagnostics.AddRange(selectionResult.Diagnostics);
                    eventSelections = selectionResult.Value;
                    eventSelectionsResolved = true;
                }
            }

            // WoT Binding Section 5.2.1 only applies to synthesized Nodes.
            WotTypeBinding? typeBinding = null;
            Dictionary<JsonElement, WotTypeBinding>? affordanceBindings = null;
            Dictionary<JsonElement, WotResolvedNode?>? componentTargets = null;
            WotParentPlacement? parentPlacement = null;
            WotDeclarationCatalog? declarations = null;
            if (!TakesRestorePath(document))
            {
                typeBinding = await ResolveTypeBindingAsync(
                    document,
                    nodeResolver ?? NullWotNodeResolver.Instance,
                    diagnostics,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                affordanceBindings = [];
                foreach (KeyValuePair<string, JsonElement> property in document.Properties)
                {
                    affordanceBindings[property.Value] = await ResolveTypeBindingAsync(
                        document,
                        nodeResolver ?? NullWotNodeResolver.Instance,
                        diagnostics,
                        property.Value,
                        WotExpectedNodeClass.VariableType,
                        "/properties/" + EscapeJsonPointerToken(property.Key),
                        cancellationToken).ConfigureAwait(false);
                }
                componentTargets = await PreresolveComponentTargetsAsync(
                    document, thingCatalog, nodeResolver ?? NullWotNodeResolver.Instance,
                    cancellationToken).ConfigureAwait(false);
                parentPlacement = await ResolveParentPlacementAsync(
                    document,
                    parentCatalog,
                    nodeResolver ?? NullWotNodeResolver.Instance,
                    diagnostics,
                    cancellationToken).ConfigureAwait(false);

                // The instance declarations of the bound type are resolved
                // here, once, because resolving them is asynchronous and the
                // synthesis below is not. Doing it any later would mean either
                // blocking on an asynchronous call or leaving Section 5.2.1's
                // declaration rules unevaluated.
                declarations = await PreresolveDeclarationsAsync(
                    document,
                    typeBinding,
                    nodeResolver,
                    cancellationToken).ConfigureAwait(false);
            }

            // Every uav:externalSchema reference is resolved and compared here,
            // once, for the same reason: the providers are asynchronous and the
            // synthesis is not.
            WotExternalSchemaCatalog? externalSchemas = null;
            if (schemaResolver is not null && !TakesRestorePath(document))
            {
                options ??= new WotNodeSetConverterOptions();
                options.Validate();
                resolutionContext ??= new WotResolutionContext(options.ToResolverOptions());
                externalSchemas = await PreresolveExternalSchemasAsync(
                    document,
                    options,
                    schemaResolver,
                    resolutionContext,
                    cancellationToken).ConfigureAwait(false);
            }

            UANodeSet? nodeSet = ToNodeSetCore(
                document,
                options,
                thingCatalog,
                referenceTypeCatalog,
                resolutionContext,
                diagnostics,
                typeBinding,
                parentPlacement,
                eventSelections,
                eventSelectionsResolved,
                declarations,
                externalSchemas,
                (thingResolver as DocumentSetThingResolver)?.ArchiveContext,
                affordanceBindings,
                componentTargets,
                dataTypeSources,
                dataTypeOwners,
                dataTypeNames,
                documentSet);
            ApplyIdentifierLeniency(diagnostics, options);
            return new WotConversionResult<UANodeSet>(nodeSet, diagnostics);
        }

        private static async ValueTask<ArrayOf<WotDataTypeDefinitionSource>> PreresolveDataTypeDefinitionsAsync(
            WotDocument document,
            ArrayOf<WotDocument> sharedOwners,
            IWotNodeResolver? nodeResolver,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            if (nodeResolver is not IWotDataTypeDefinitionResolver resolver)
            {
                return [];
            }
            options.Validate();
            var documents = new HashSet<WotDocument> { document };
            foreach (WotDocument owner in sharedOwners)
            {
                documents.Add(owner);
            }
            var known = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Queue<(JsonElement Root, int Depth)>();
            foreach (WotDocument owner in documents)
            {
                if (TakesRestorePath(owner))
                {
                    continue;
                }
                foreach (JsonElement definition in ReadDataTypeDefinitionOccurrences(owner.RootElement))
                {
                    if (!IsReferenceOnlyDefinition(definition) && GetElementString(definition, "@id") is { } graphId)
                    {
                        known.Add(graphId);
                    }
                }
                pending.Enqueue((owner.RootElement, 0));
            }
            var sources = new List<WotDataTypeDefinitionSource>();
            while (pending.Count != 0)
            {
                (JsonElement root, int depth) = pending.Dequeue();
                ArrayOf<JsonElement> references = ReadDataTypeDefinitionOccurrences(root);
                for (int index = 0; index < references.Count; index++)
                {
                    JsonElement reference = references[index];
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsReferenceOnlyDefinition(reference) ||
                        GetElementString(reference, "@id") is not { } graphId ||
                        !known.Add(graphId))
                    {
                        continue;
                    }
                    if (depth >= options.MaxResolverDepth || sources.Count >= options.MaxNodeCount)
                    {
                        Report(graphId, "The DataType definition closure exceeds the configured limits.");
                        continue;
                    }
                    ArrayOf<WotDataTypeDefinitionSource> matches = await resolver
                        .ResolveDataTypeDefinitionsAsync(graphId, cancellationToken).ConfigureAwait(false);
                    if (matches.Count != 1)
                    {
                        Report(graphId, matches.Count == 0
                            ? "The DataType definition is not held by the conversion closure."
                            : "More than one complete DataType definition owns this graph identity.");
                        continue;
                    }
                    WotDataTypeDefinitionSource source = matches[0];
                    if (GetElementString(source.Definition, "@id") != graphId ||
                        IsReferenceOnlyDefinition(source.Definition))
                    {
                        Report(graphId, "The provider did not return the requested complete DataType definition.");
                        continue;
                    }
                    documents.Add(source.Document);
                    if (documents.Count > options.MaxResolverDocuments)
                    {
                        Report(graphId, "The DataType definition closure exceeds the configured document limit.");
                        continue;
                    }
                    sources.Add(source);
                    foreach (JsonElement nested in ReadDataTypeDefinitionOccurrences(source.Definition))
                    {
                        if (!IsReferenceOnlyDefinition(nested) && GetElementString(nested, "@id") is { } nestedId)
                        {
                            known.Add(nestedId);
                        }
                    }
                    pending.Enqueue((source.Definition, depth + 1));
                }
            }
            return sources.ToArrayOf();

            void Report(string graphId, string message)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    message,
                    new WotLocation(reference: graphId)));
            }
        }

        private static async ValueTask<Dictionary<(string NamespaceUri, string Name), string?>>
            PreresolveDataTypeNamesAsync(
                WotDocument document,
                ArrayOf<WotDataTypeDefinitionSource> sources,
                ArrayOf<WotDocument> sharedOwners,
                IWotNodeResolver? nodeResolver,
                List<WotDiagnostic> diagnostics,
                CancellationToken cancellationToken)
        {
            var resolved = new Dictionary<(string NamespaceUri, string Name), string?>();
            if (nodeResolver is null)
            {
                return resolved;
            }
            var roots = new List<(WotDocument Document, JsonElement Root)> { (document, document.RootElement) };
            foreach (WotDocument owner in sharedOwners)
            {
                if (!ReferenceEquals(owner, document) && !TakesRestorePath(owner))
                {
                    roots.Add((owner, owner.RootElement));
                }
            }
            var localNames = new HashSet<(string NamespaceUri, string Name)>();
            foreach (WotDataTypeDefinitionSource source in sources)
            {
                roots.Add((source.Document, source.Definition));
                AddLocalName(source.Document, source.Definition);
            }
            foreach ((WotDocument owner, JsonElement root) in roots)
            {
                foreach (JsonElement definition in ReadDataTypeDefinitionOccurrences(root))
                {
                    AddLocalName(owner, definition);
                }
            }
            var requested = new HashSet<(string NamespaceUri, string Name)>();
            foreach ((WotDocument owner, JsonElement root) in roots)
            {
                foreach ((JsonElement node, string name) in ReadDataTypeNameOccurrences(root))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TrySplitCompactName(owner, name, out string namespaceUri, out string local, node) ||
                        localNames.Contains((namespaceUri, local)) ||
                        !requested.Add((namespaceUri, local)) ||
                        (namespaceUri == WotVocabulary.OpcUaNamespace &&
                            NodeSetStandardAliases.TryGetDataTypeNodeId(local, out _)) ||
                        !await nodeResolver.HoldsNamespaceAsync(namespaceUri, cancellationToken).ConfigureAwait(false))
                    {
                        continue;
                    }
                    ArrayOf<WotResolvedNode> matches = await nodeResolver.ResolveByBrowseNameAsync(
                        namespaceUri, local, WotExpectedNodeClass.DataType, cancellationToken).ConfigureAwait(false);
                    if (matches.Count == 0)
                    {
                        continue;
                    }
                    if (matches.Count == 1 && matches[0].NodeClass == WotExpectedNodeClass.DataType)
                    {
                        resolved[(namespaceUri, local)] = matches[0].NodeId;
                        continue;
                    }
                    resolved[(namespaceUri, local)] = null;
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        $"The name '{name}' does not resolve unambiguously to a DataType.",
                        new WotLocation(reference: name)));
                }
            }
            return resolved;

            void AddLocalName(WotDocument owner, JsonElement definition)
            {
                if (!IsReferenceOnlyDefinition(definition) &&
                    GetElementString(definition, "uav:dataTypeName") is { } name &&
                    TrySplitCompactName(owner, name, out string namespaceUri, out string local, definition))
                {
                    localNames.Add((namespaceUri, local));
                }
            }
        }

        private static async ValueTask<Dictionary<JsonElement, WotResolvedNode?>> PreresolveComponentTargetsAsync(
            WotDocument document,
            WotThingCatalog? thingCatalog,
            IWotNodeResolver resolver,
            CancellationToken cancellationToken)
        {
            var targets = new Dictionary<JsonElement, WotResolvedNode?>();
            var resolved = new Dictionary<string, WotResolvedNode?>(StringComparer.Ordinal);
            foreach (JsonElement link in document.Links)
            {
                if (link.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                string? target = GetElementString(link, "href");
                WotResolvedNode? type = null;
                if (target is not null && !IsNodeId(target))
                {
                    thingCatalog?.TryPeekTarget(target, out type);
                    target = type?.NodeId;
                }
                if (type is null && target is not null)
                {
                    target = NormalizeExpandedNodeId(target);
                    if (!resolved.TryGetValue(target, out type))
                    {
                        type = await resolver.ResolveByNodeIdAsync(target, cancellationToken).ConfigureAwait(false);
                        resolved.Add(target, type);
                    }
                }
                targets[link] = type;
            }
            return targets;
        }

        /// <summary>
        /// Resolves every <c>uav:externalSchema</c> a property affordance names
        /// and compares it against that affordance's canonical DataSchema.
        /// </summary>
        /// <remarks>
        /// The comparison needs the DataType the Binding derives, which is
        /// derived here from the DataSchema alone rather than from the
        /// synthesized Node: the reference is checked against what the document
        /// states, and what the document states is what the synthesis will
        /// write. Nothing here changes the DataType - Section 6.11's DataType
        /// definition and Section 5.4's definitive terms remain the statement
        /// of what the Variable is.
        /// </remarks>
        private static async ValueTask<WotExternalSchemaCatalog> PreresolveExternalSchemasAsync(
            WotDocument document,
            WotNodeSetConverterOptions options,
            WotExternalSchemaResolver schemaResolver,
            WotResolutionContext resolutionContext,
            CancellationToken cancellationToken)
        {
            var catalog = new WotExternalSchemaCatalog();
            foreach (KeyValuePair<string, JsonElement> property in document.Properties)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (GetElementString(property.Value, "uav:externalSchema")
                    is not { Length: > 0 } reference)
                {
                    continue;
                }
                string local =
                    LocalName(GetElementString(property.Value, "uav:browseName")) ??
                    property.Key;
                catalog.Add(
                    local,
                    await schemaResolver.ResolveAndCompareAsync(
                        reference,
                        property.Value,
                        ReadDeclaredDataType(property.Value),
                        resolutionContext,
                        options,
                        cancellationToken).ConfigureAwait(false));
            }
            return catalog;
        }

        /// <summary>
        /// Resolves the instance declarations of the type a document binds to,
        /// with the scope <c>uav:includeInherited</c> selects.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A document that binds to no type gets
        /// <see cref="WotDeclarationCatalog.NotBound"/>: there is no type whose
        /// declarations a member could populate, and Section 6.8's open-content
        /// rule has no declared set to close against, so both are silent rather
        /// than unevaluable.
        /// </para>
        /// <para>
        /// <c>uav:includeInherited: false</c> narrows the question to the
        /// declarations the bound type states itself. Absent or <c>true</c>, the
        /// effective closure applies, which is what OPC 10000-3 means by the
        /// declarations of a type: an instance carries the ones its type
        /// inherits just as surely as the ones it states.
        /// </para>
        /// </remarks>
        private static async ValueTask<WotDeclarationCatalog> PreresolveDeclarationsAsync(
            WotDocument document,
            WotTypeBinding? typeBinding,
            IWotNodeResolver? nodeResolver,
            CancellationToken cancellationToken)
        {
            if (typeBinding is not { Outcome: WotTypeBindingOutcome.Bound, NodeId: { } typeNodeId })
            {
                return WotDeclarationCatalog.NotBound;
            }

            WotDeclarationScope scope = ReadIncludeInherited(document) == false
                ? WotDeclarationScope.Direct
                : WotDeclarationScope.Effective;
            bool offered = OffersDeclarations(nodeResolver);
            WotTypeDeclarationSet? set = nodeResolver is IWotTypeDeclarationResolver capability
                ? await capability
                    .ResolveDeclarationsAsync(typeNodeId, scope, cancellationToken)
                    .ConfigureAwait(false)
                : null;
            return WotDeclarationCatalog.Create(typeNodeId, scope, set, offered);
        }

        /// <summary>
        /// Gets whether any part of the local context reports declarations.
        /// </summary>
        private static bool OffersDeclarations(IWotNodeResolver? nodeResolver)
        {
            return nodeResolver switch
            {
                WotCompositeNodeResolver composite => composite.OffersDeclarations(),
                IWotTypeDeclarationResolver => true,
                _ => false
            };
        }

        /// <summary>
        /// Resolves every ReferenceType a link relation names against the
        /// WoT Binding Section 5.1.5 local context, and the NodeClass the
        /// context reports for every definitive <c>uav:refId</c> a link
        /// carries, before the synchronous synthesis needs the answers.
        /// </summary>
        /// <remarks>
        /// Only a resolver that offers <see cref="IWotReferenceTypeResolver"/>
        /// contributes names: a local context describing no ReferenceType has
        /// none to offer, so nothing is gained by asking it. The standard
        /// base-namespace names stay available in either case, so a companion
        /// model's own ReferenceType resolves where the caller supplied a local
        /// context and the built-in names resolve where it did not. The
        /// identifier probe uses the ordinary
        /// <see cref="IWotNodeResolver.ResolveByNodeIdAsync"/>, because
        /// "this identifier names an ObjectType" is a fact about a Node and not
        /// about a ReferenceType name.
        /// </remarks>
        private static async ValueTask<WotReferenceTypeCatalog?> PreresolveReferenceTypesAsync(
            WotDocument document,
            IWotNodeResolver? nodeResolver,
            CancellationToken cancellationToken)
        {
            if (nodeResolver is null)
            {
                return null;
            }

            var referenceTypes = nodeResolver as IWotReferenceTypeResolver;
            WotReferenceTypeCatalog? catalog = null;
            foreach (JsonElement link in document.EnumerateLinks())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? rel = GetElementString(link, "rel");
                if (rel is not null &&
                    !IsKnownBindingRelation(rel) &&
                    TrySplitCompactModelName(rel, out string prefix, out string name) &&
                    prefix is not ("uav" or "tm") &&
                    !IsExternalRelationPrefix(prefix) &&
                    TryGetContextNamespace(document, prefix, out string namespaceUri, link) &&
                    (IsModelConceptCandidate(document, link, prefix) ||
                        await nodeResolver.HoldsNamespaceAsync(namespaceUri, cancellationToken).ConfigureAwait(false)))
                {
                    catalog ??= new WotReferenceTypeCatalog();
                    if (!catalog.Contains(namespaceUri, name))
                    {
                        WotReferenceTypeAnswer answer = await ResolveReferenceTypeAnswerAsync(
                            nodeResolver, referenceTypes, namespaceUri, name, cancellationToken).ConfigureAwait(false);
                        catalog.Add(namespaceUri, name, answer);
                        for (int index = 0; index < answer.Matches.Count; index++)
                        {
                            WotResolvedReferenceType match = answer.Matches[index];
                            if (!catalog.ContainsIdentity(match.NodeId))
                            {
                                WotResolvedNode? node = await nodeResolver
                                    .ResolveByNodeIdAsync(match.NodeId, cancellationToken).ConfigureAwait(false);
                                catalog.AddIdentity(match.NodeId, node);
                            }
                        }
                    }
                }

                string? definitive = GetElementString(link, "uav:refId");
                if (definitive is null)
                {
                    continue;
                }
                catalog ??= new WotReferenceTypeCatalog();
                if (!catalog.ContainsIdentity(definitive))
                {
                    WotResolvedNode? node = await nodeResolver
                        .ResolveByNodeIdAsync(definitive, cancellationToken)
                        .ConfigureAwait(false);
                    catalog.AddIdentity(definitive, node);
                }
            }
            return catalog;
        }

        /// <summary>
        /// Asks the local context for one name, and tells "the model does not
        /// define this" apart from "the model defines it as something that is
        /// not a ReferenceType".
        /// </summary>
        private static async ValueTask<WotReferenceTypeAnswer> ResolveReferenceTypeAnswerAsync(
            IWotNodeResolver nodeResolver,
            IWotReferenceTypeResolver? referenceTypes,
            string namespaceUri,
            string name,
            CancellationToken cancellationToken)
        {
            ArrayOf<WotResolvedReferenceType> matches = referenceTypes is null
                ? []
                : await referenceTypes
                    .ResolveReferenceTypesAsync(namespaceUri, name, cancellationToken)
                    .ConfigureAwait(false);
            if (matches.Count > 0)
            {
                return WotReferenceTypeAnswer.FromMatches(matches);
            }

            // The name is not a ReferenceType here. If the same namespace
            // exposes it as some other Node, the document named a real thing of
            // the wrong kind, which is a different report from "unknown".
            ArrayOf<WotResolvedNode> others = await nodeResolver
                .ResolveByBrowseNameAsync(
                    namespaceUri, name, WotExpectedNodeClass.Any, cancellationToken)
                .ConfigureAwait(false);
            return others.Count > 0
                ? WotReferenceTypeAnswer.NotAReferenceType
                : WotReferenceTypeAnswer.Unresolved;
        }

        /// <summary>
        /// Restores or synthesizes the NodeSet2 document described by a WoT
        /// document, returning structured diagnostics together with the result.
        /// </summary>
        /// <remarks>
        /// This conversion follows no document link, so an event affordance
        /// that states its field selection with <c>tm:ref</c> or
        /// <c>uav:eventSelectClauses</c> (WoT Binding Section 6.1) is reported
        /// with <see cref="WotDiagnosticCode.EventSelectionUnresolved"/> rather
        /// than converted without the fields the linked definition declares.
        /// Use <c>ToNodeSetResultAsync</c> with an
        /// <see cref="IWotThingResolver"/> to convert such a document.
        /// </remarks>
        /// <param name="document">The WoT document.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <returns>The conversion result and its diagnostics.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="document"/> is <c>null</c>.
        /// </exception>
        public static WotConversionResult<UANodeSet> ToNodeSetResult(
            WotDocument document,
            WotNodeSetConverterOptions? options = null)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            var diagnostics = new List<WotDiagnostic>();
            UANodeSet? nodeSet = ToNodeSetCore(
                document, options, null, null, null, diagnostics);
            ApplyIdentifierLeniency(diagnostics, options);
            return new WotConversionResult<UANodeSet>(nodeSet, diagnostics);
        }

        /// <summary>
        /// Relaxes the portable-identity rules when the caller opted in.
        /// </summary>
        /// <remarks>
        /// WoT Binding Sections 5.1.1 and 5.1.3 make the session-local
        /// <c>ns=&lt;index&gt;</c> NodeId form and a numeric namespace prefix in a
        /// browse name errors in release 1.1, where OPC 10101 v1.00 permitted
        /// both. A caller migrating a v1.00 document can set
        /// <see cref="WotNodeSetConverterOptions.AllowNonPortableIdentifiers"/>
        /// to keep reading it; the occurrences are then reported as warnings so
        /// the non-portable values still surface without failing the conversion.
        /// </remarks>
        /// <param name="diagnostics">The diagnostics collected so far.</param>
        /// <param name="options">The effective converter options.</param>
        private static void ApplyIdentifierLeniency(
            List<WotDiagnostic> diagnostics,
            WotNodeSetConverterOptions? options)
        {
            if (options?.AllowNonPortableIdentifiers != true)
            {
                return;
            }
            for (int ii = 0; ii < diagnostics.Count; ii++)
            {
                WotDiagnostic diagnostic = diagnostics[ii];
                if (diagnostic.Severity != WotDiagnosticSeverity.Error ||
                    diagnostic.Code is not (WotDiagnosticCode.NonPortableIdentity
                        or WotDiagnosticCode.NonPortableQualifiedName))
                {
                    continue;
                }
                diagnostics[ii] = new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Location);
            }
        }

        private static bool TakesRestorePath(WotDocument document)
        {
            return document.TryGetEnvelope(out _) ||
                (document.TryGetNativeProjection(out JsonElement projection) &&
                    !WotNativeProjection.HasUnsupportedProfile(projection));
        }

        /// <summary>
        /// Describes the ObjectType a Thing Model projects, without converting
        /// it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A Thing Model projects its root as a <c>UAObjectType</c>, so it is
        /// what a WoT Binding Section 5.2.1 type binding in a sibling document
        /// names. A Thing Description projects an instance and is therefore
        /// never a type-binding target, so this returns <c>false</c> for one.
        /// </para>
        /// <para>
        /// This exists so an <see cref="IWotNodeResolver"/> over a set of
        /// sibling documents can index them by identity without paying for a
        /// full conversion, and so that identity is derived by exactly the same
        /// rules the conversion uses rather than by a copy that can drift.
        /// </para>
        /// </remarks>
        /// <param name="document">The document to describe.</param>
        /// <param name="namespaceUri">
        /// The NamespaceUri the document projects into.
        /// </param>
        /// <param name="browseName">The projected type's unqualified BrowseName.</param>
        /// <param name="nodeId">
        /// The projected type's identity, as a portable ExpandedNodeId string.
        /// </param>
        /// <returns>
        /// <c>true</c> when <paramref name="document"/> is a Thing Model.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="document"/> is <c>null</c>.
        /// </exception>
        public static bool TryDescribeProjectedType(
            WotDocument document,
            out string namespaceUri,
            out string browseName,
            out string nodeId)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            namespaceUri = string.Empty;
            browseName = string.Empty;
            nodeId = string.Empty;
            if (document.Kind != WotDocumentKind.ThingModel)
            {
                return false;
            }

            namespaceUri = DeriveModelUri(document);
            browseName = LocalName(GetUavString(document, "browseName")) ??
                SanitizeName(document.Title) ?? "Thing";

            nodeId = GetUavString(document, "id") ??
                WotPortableIdentity.GenerateNodeId(
                    namespaceUri,
                    new ArrayOf<WotBrowsePathElement>(
                        [DocumentPathElement(document, document.RootElement, namespaceUri, browseName)]));
            return true;
        }

        /// <summary>
        /// Describes the ReferenceType a Thing Model projects: the two names it
        /// answers to and its identity.
        /// </summary>
        /// <remarks>
        /// <para>
        /// OPC 10000-3 gives a ReferenceType a BrowseName that reads a
        /// reference forward and an InverseName that reads the same reference
        /// backwards, and WoT Binding Section 5.1.2 lets a link <c>rel</c> use
        /// either. A local context therefore has to offer both names, which
        /// <see cref="TryDescribeProjectedType"/> cannot: it describes a Node
        /// and a Node has one BrowseName.
        /// </para>
        /// <para>
        /// This exists so an <see cref="IWotReferenceTypeResolver"/> over a set
        /// of documents can index them without paying for a full conversion,
        /// and so the names are derived by exactly the rules the conversion
        /// uses.
        /// </para>
        /// </remarks>
        /// <param name="document">The document to describe.</param>
        /// <param name="namespaceUri">The NamespaceUri the ReferenceType is in.</param>
        /// <param name="browseName">The unqualified BrowseName.</param>
        /// <param name="inverseName">
        /// The unqualified InverseName, or an empty string where the document
        /// states none.
        /// </param>
        /// <param name="isSymmetric">
        /// Whether the ReferenceType is symmetric, in which case one name reads
        /// both directions.
        /// </param>
        /// <param name="nodeId">
        /// The ReferenceType's identity, as a portable ExpandedNodeId string.
        /// </param>
        /// <returns>
        /// <c>true</c> when <paramref name="document"/> is a Thing Model
        /// describing a ReferenceType.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="document"/> is <c>null</c>.
        /// </exception>
        public static bool TryDescribeProjectedReferenceType(
            WotDocument document,
            out string namespaceUri,
            out string browseName,
            out string inverseName,
            out bool isSymmetric,
            out string nodeId)
        {
            inverseName = string.Empty;
            isSymmetric = false;
            if (!TryDescribeProjectedType(document, out namespaceUri, out browseName, out nodeId))
            {
                return false;
            }
            if (!HasTypeAnnotation(document, "uav:referenceType"))
            {
                namespaceUri = string.Empty;
                browseName = string.Empty;
                nodeId = string.Empty;
                return false;
            }
            if (document.RootElement.TryGetProperty(
                    InverseNameTerm, out JsonElement inverse) &&
                inverse.ValueKind == JsonValueKind.String)
            {
                inverseName = inverse.GetString() ?? string.Empty;
            }
            isSymmetric =
                document.RootElement.TryGetProperty(SymmetricTerm, out JsonElement symmetric) &&
                symmetric.ValueKind == JsonValueKind.True;
            return true;
        }

        private static UANodeSet? ToNodeSetCore(
            WotDocument document,
            WotNodeSetConverterOptions? options,
            WotThingCatalog? thingCatalog,
            WotReferenceTypeCatalog? referenceTypeCatalog,
            WotResolutionContext? resolutionContext,
            List<WotDiagnostic> diagnostics,
            WotTypeBinding? typeBinding = null,
            WotParentPlacement? parentPlacement = null,
            WotEventSelectionCatalog? eventSelections = null,
            bool eventSelectionsResolved = false,
            WotDeclarationCatalog? declarations = null,
            WotExternalSchemaCatalog? externalSchemas = null,
            UANodeSet? archiveContext = null,
            Dictionary<JsonElement, WotTypeBinding>? affordanceBindings = null,
            Dictionary<JsonElement, WotResolvedNode?>? componentTargets = null,
            ArrayOf<WotDataTypeDefinitionSource> dataTypeSources = default,
            ArrayOf<WotDocument> dataTypeOwners = default,
            Dictionary<(string NamespaceUri, string Name), string?>? dataTypeNames = null,
            DocumentSetThingResolver? documentSet = null)
        {
            options ??= new WotNodeSetConverterOptions();
            options.Validate();

            // Exactly one resolution context is created per top-level
            // conversion, seeded from the converter options, and threaded
            // through every context/schema/thing/link resolution below. It
            // must never be re-created per link so that depth, document
            // count, cycle and cumulative byte bounds apply across the whole
            // conversion rather than resetting for each resolved reference.
            resolutionContext ??= new WotResolutionContext(options.ToResolverOptions());

            bool hasProjection = document.TryGetNativeProjection(out JsonElement nativeProjection);
            bool unsupportedProjection = hasProjection &&
                WotNativeProjection.HasUnsupportedProfile(nativeProjection);
            if (unsupportedProjection)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.NativeProjectionInvalid,
                    "The uav:nodes profileVersion is unsupported. Its records were not read; " +
                    "the remaining document is processed independently.",
                    WotLocation.FromPointer("/uav:nodes/profileVersion")));
            }

            if (document.TryGetEnvelope(out JsonElement envelope))
            {
                UANodeSet? restored = RestoreFromEnvelope(envelope, options, diagnostics);
                if (restored is null)
                {
                    return null;
                }
                if (hasProjection && !unsupportedProjection)
                {
                    UANodeSet? projected = ValidateNativeConsistency(restored, nativeProjection, options, diagnostics);
                    if (projected is not null && documentSet?.DeferPreservedFactValidation != true)
                    {
                        ValidateNativeAffordanceCoverage(document, projected, diagnostics);
                    }
                }
                if (documentSet?.DeferPreservedFactValidation != true)
                {
                    ValidatePreservedReadableFacts(document, restored, options, diagnostics, archiveContext);
                }
                ApplyIdentifierLeniency(diagnostics, options);
                if (HasErrors(diagnostics))
                {
                    return NodeSetAliasCompleter.Complete(restored, WotNodeSetAliases.Instance);
                }
                WotJsonResidue.Replace(restored, document, options, diagnostics);
                return NodeSetAliasCompleter.Complete(restored, WotNodeSetAliases.Instance);
            }

            if (hasProjection && !unsupportedProjection)
            {
                UANodeSet? restored = WotNativeProjection.Read(
                    nativeProjection,
                    options,
                    diagnostics);
                if (restored is not null)
                {
                    if (documentSet?.DeferPreservedFactValidation != true)
                    {
                        ValidateNativeAffordanceCoverage(document, restored, diagnostics);
                        ValidatePreservedReadableFacts(document, restored, options, diagnostics, archiveContext);
                    }
                    ApplyIdentifierLeniency(diagnostics, options);
                    if (!HasErrors(diagnostics))
                    {
                        WotJsonResidue.Replace(restored, document, options, diagnostics);
                    }
                }
                return NodeSetAliasCompleter.Complete(restored, WotNodeSetAliases.Instance);
            }

            UANodeSet? synthesized =
                Synthesize(
                    document,
                    options,
                    thingCatalog,
                    referenceTypeCatalog,
                    resolutionContext,
                    diagnostics,
                    typeBinding,
                    parentPlacement,
                    eventSelections,
                    eventSelectionsResolved,
                    declarations,
                    externalSchemas,
                    affordanceBindings,
                    componentTargets,
                    dataTypeSources,
                    dataTypeOwners,
                    dataTypeNames,
                    documentSet);
            return NodeSetAliasCompleter.Complete(synthesized, WotNodeSetAliases.Instance);
        }

        /// <summary>
        /// Restores authoritative storage for type lookup without running readable checks that require the closure.
        /// </summary>
        internal static WotConversionResult<UANodeSet> ReadAuthoritativeTypeContext(WotDocument document)
        {
            var options = new WotNodeSetConverterOptions();
            var diagnostics = new List<WotDiagnostic>();
            bool hasNative = document.TryGetNativeProjection(out JsonElement projection) &&
                !WotNativeProjection.HasUnsupportedProfile(projection);
            UANodeSet? nodes = null;
            if (document.TryGetEnvelope(out JsonElement envelope))
            {
                nodes = RestoreFromEnvelope(envelope, options, diagnostics);
                if (nodes is not null && hasNative)
                {
                    ValidateNativeConsistency(nodes, projection, options, diagnostics);
                }
            }
            else if (hasNative)
            {
                nodes = WotNativeProjection.Read(projection, options, diagnostics);
            }
            return new WotConversionResult<UANodeSet>(nodes, diagnostics);
        }

        private static UANodeSet? RestoreFromEnvelope(
            JsonElement envelope,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            var location = new WotLocation(jsonPointer: "/uav:nodeSet");

            if (!TryGetString(envelope, "contentType", out string? contentType) ||
                !string.Equals(contentType, WotVocabulary.NodeSetContentType, StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.UnsupportedContentType,
                    $"Unsupported NodeSet content type '{contentType}'.",
                    location));
                return null;
            }

            if (!TryGetString(envelope, "encoding", out string? encoding) ||
                !string.Equals(encoding, WotVocabulary.Base64Encoding, StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.UnsupportedEncoding,
                    $"Unsupported NodeSet encoding '{encoding}'.",
                    location));
                return null;
            }

            if (!TryGetString(envelope, "data", out string? data) || data is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.EnvelopeInvalid,
                    "The uav:nodeSet data value is required and must be a string.",
                    location));
                return null;
            }

            byte[] nodeSetBytes;
            try
            {
                nodeSetBytes = Convert.FromBase64String(data);
            }
            catch (FormatException)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidBase64,
                    "The uav:nodeSet data is not valid base64.",
                    location));
                return null;
            }

            if (nodeSetBytes.Length > options.MaxNodeSetSize)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NodeSetTooLarge,
                    $"Decoded NodeSet exceeds the configured {options.MaxNodeSetSize} byte limit.",
                    location));
                return null;
            }

            // uav:nodeSet.sha256 is mandatory: a preservation envelope without
            // an integrity digest cannot be trusted and must not yield a
            // NodeSet, regardless of whether the payload otherwise parses.
            if (!envelope.TryGetProperty("sha256", out JsonElement digestElement) ||
                digestElement.ValueKind != JsonValueKind.String)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidDigest,
                    "The uav:nodeSet sha256 value is required and must be a string.",
                    location));
                return null;
            }

            if (!TryParseDigest(digestElement.GetString()!, out byte[] expected))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidDigest,
                    "The uav:nodeSet sha256 value is not a valid SHA-256 digest.",
                    location));
                return null;
            }

            byte[] actual = ComputeSha256(nodeSetBytes);
            if (!FixedEquals(expected, actual))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DigestMismatch,
                    "The uav:nodeSet digest does not match the payload.",
                    location));
                return null;
            }

            UANodeSet? nodeSet;
            try
            {
                using var stream = new MemoryStream(nodeSetBytes, writable: false);
                nodeSet = UANodeSet.Read(stream);
            }
            catch (Exception ex) when (
                ex is InvalidOperationException or
                    System.Xml.XmlException or
                    FormatException)
            {
                // XmlSerializer wraps parse failures in InvalidOperationException;
                // treat any deserialization failure as a structured diagnostic
                // rather than letting the exception escape the converter.
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.MalformedNodeSet,
                    $"The uav:nodeSet payload is not a valid NodeSet2 document: {ex.Message}",
                    location));
                return null;
            }
            if (nodeSet is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.MalformedNodeSet,
                    "The uav:nodeSet payload is not a valid NodeSet2 document.",
                    location));
            }
            return nodeSet;
        }

        private static UANodeSet? ValidateNativeConsistency(
            UANodeSet baseline,
            JsonElement projection,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            var nativeDiagnostics = new List<WotDiagnostic>();
            UANodeSet? projected = WotNativeProjection.Read(
                projection,
                options,
                nativeDiagnostics);
            if (projected is null || HasErrors(nativeDiagnostics))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NativeProjectionConflict,
                    FirstDiagnosticMessage(nativeDiagnostics) ??
                    "The native projection could not be reconstructed."));
                return null;
            }

            NodeSetComparisonResult comparison =
                NodeSetComparer.Compare(baseline, projected, options.ToComparisonOptions());
            if (!comparison.AreEquivalent)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NativeProjectionConflict,
                    comparison.Differences.Count > 0
                        ? comparison.Differences[0]
                        : "The native projection conflicts with the preservation baseline."));
            }
            return projected;
        }

        private static void ValidateNativeAffordanceCoverage(
            WotDocument document,
            UANodeSet projected,
            List<WotDiagnostic> diagnostics)
        {
            if (document.Properties.Count == 0 &&
                document.Actions.Count == 0 &&
                document.Events.Count == 0)
            {
                return;
            }

            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            var browseNames = new HashSet<string>(StringComparer.Ordinal);
            if (projected.Items is not null)
            {
                foreach (UANode node in projected.Items)
                {
                    if (!string.IsNullOrEmpty(node.NodeId))
                    {
                        nodeIds.Add(node.NodeId);
                    }
                    if (!string.IsNullOrEmpty(node.BrowseName))
                    {
                        browseNames.Add(node.BrowseName);
                    }
                }
            }

            var identityContext = new UANodeSet
            {
                Models = [new ModelTableEntry { ModelUri = DeriveModelUri(document) }],
                NamespaceUris = projected.NamespaceUris is null
                    ? null
                    : (string[])projected.NamespaceUris.Clone()
            };
            string rootLocal = LocalName(GetUavString(document, "browseName")) ??
                SanitizeName(document.Title) ?? "Thing";

            ValidateNativeAffordanceCoverage(
                document,
                document.Properties,
                "properties",
                "property",
                rootLocal,
                identityContext,
                nodeIds,
                browseNames,
                diagnostics);
            ValidateNativeAffordanceCoverage(
                document,
                document.Actions,
                "actions",
                "action",
                rootLocal,
                identityContext,
                nodeIds,
                browseNames,
                diagnostics);
            ValidateNativeAffordanceCoverage(
                document,
                document.Events,
                "events",
                "event",
                rootLocal,
                identityContext,
                nodeIds,
                browseNames,
                diagnostics);
        }

        private static void ValidateNativeAffordanceCoverage(
            WotDocument document,
            IReadOnlyDictionary<string, JsonElement> affordances,
            string collectionName,
            string affordanceKind,
            string rootLocal,
            UANodeSet identityContext,
            HashSet<string> nodeIds,
            HashSet<string> browseNames,
            List<WotDiagnostic> diagnostics)
        {
            foreach (KeyValuePair<string, JsonElement> affordance in affordances)
            {
                if (IsNativeAffordanceCovered(
                    document,
                    affordance.Key,
                    affordance.Value,
                    rootLocal,
                    identityContext,
                    nodeIds,
                    browseNames))
                {
                    continue;
                }

                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.NativeProjectionUncoveredAffordance,
                    "The document carries uav:nodes, but its native projection " +
                    $"does not contain a node for the {affordanceKind} affordance " +
                    $"'{affordance.Key}'. The projection is authoritative, so " +
                    "that readable affordance does not contribute to the restored NodeSet.",
                    WotLocation.FromPointer(
                        "/" + collectionName + "/" + EscapeJsonPointerToken(affordance.Key))));
            }
        }

        private static bool IsNativeAffordanceCovered(
            WotDocument document,
            string key,
            JsonElement affordance,
            string rootLocal,
            UANodeSet identityContext,
            HashSet<string> nodeIds,
            HashSet<string> browseNames)
        {
            string local = LocalName(GetElementString(affordance, "uav:browseName")) ?? key;
            string? authoredNodeId = GetElementString(affordance, "uav:id");
            string expectedNodeId = authoredNodeId is null
                ? GenerateMemberNodeId(document, identityContext, rootLocal, local, affordance)
                : ToNodeSetNodeId(authoredNodeId, identityContext, []);
            if (authoredNodeId is not null)
            {
                return nodeIds.Contains(expectedNodeId);
            }

            string? authoredBrowseName = GetElementString(affordance, "uav:browseName");
            string expectedBrowseName = authoredBrowseName is null
                ? GetOrAppendNamespaceUri(identityContext, DeriveModelUri(document))
                    .ToString(CultureInfo.InvariantCulture) +
                    ":" +
                    local
                : ToNodeSetQualifiedName(document, authoredBrowseName, identityContext, [], affordance);
            return nodeIds.Contains(expectedNodeId) || browseNames.Contains(expectedBrowseName);
        }

        private static string EscapeJsonPointerToken(string token)
        {
            if (!token.Contains('~', StringComparison.Ordinal) &&
                !token.Contains('/', StringComparison.Ordinal))
            {
                return token;
            }
            return token
                .Replace("~", "~0", StringComparison.Ordinal)
                .Replace("/", "~1", StringComparison.Ordinal);
        }

        private static UANodeSet? Synthesize(
            WotDocument document,
            WotNodeSetConverterOptions options,
            WotThingCatalog? thingCatalog,
            WotReferenceTypeCatalog? referenceTypeCatalog,
            WotResolutionContext resolutionContext,
            List<WotDiagnostic> diagnostics,
            WotTypeBinding? typeBinding,
            WotParentPlacement? parentPlacement,
            WotEventSelectionCatalog? eventSelections = null,
            bool eventSelectionsResolved = false,
            WotDeclarationCatalog? declarations = null,
            WotExternalSchemaCatalog? externalSchemas = null,
            Dictionary<JsonElement, WotTypeBinding>? affordanceBindings = null,
            Dictionary<JsonElement, WotResolvedNode?>? componentTargets = null,
            ArrayOf<WotDataTypeDefinitionSource> dataTypeSources = default,
            ArrayOf<WotDocument> dataTypeOwners = default,
            Dictionary<(string NamespaceUri, string Name), string?>? dataTypeNames = null,
            DocumentSetThingResolver? documentSet = null)
        {
            WotDocumentKind kind = document.Kind;
            if (kind == WotDocumentKind.Unknown)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NoConvertibleContent,
                    "The document is neither a Thing Model nor a Thing Description " +
                    "and carries no preservation envelope or native projection."));
                return null;
            }

            bool isThingModel = kind == WotDocumentKind.ThingModel;
            bool isEventType = isThingModel && HasEventTypeAnnotation(document);

            // Portable identity and event-annotation validation (WoT Binding
            // Sections 5.1.1 and 5.2). Runs before synthesis so a document that
            // uses the session-local ns=<index> form or contradicts itself is
            // diagnosed; the exact uav:nodeSet envelope and uav:nodes projection
            // are never reached here and keep their own namespace indices.
            ValidatePortableIdentity(document, diagnostics);
            ValidateModelConceptNames(document, diagnostics);
            ValidateModelVocabulary(document, diagnostics);
            ValidateBindingConformance(document, options, diagnostics);
            ValidateEventSelectionsResolved(document, eventSelectionsResolved, diagnostics);
            ValidateConditions(document, eventSelections, diagnostics);

            string modelUri = DeriveModelUri(document);
            string rootLocal = LocalName(GetUavString(document, "browseName")) ??
                SanitizeName(document.Title) ?? "Thing";
            string? authoredRootId = GetUavString(document, "id");

            var nodeSet = new UANodeSet
            {
                NamespaceUris = SeedNamespaceUris(document, modelUri),
                Models =
                [
                    new ModelTableEntry { ModelUri = modelUri }
                ]
            };
            DataTypeDefinitionContext dataTypes = documentSet?.DataTypes ??
                CreateDataTypeDefinitionContext(
                    document, nodeSet, dataTypeSources, dataTypeOwners, diagnostics, dataTypeNames);
            documentSet?.DataTypes = dataTypes;
            string rootNodeId = GenerateRootNodeId(document, nodeSet, rootLocal);
            if (authoredRootId is not null)
            {
                rootNodeId = ToNodeSetNodeId(authoredRootId, nodeSet, diagnostics);
            }
            else
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Info,
                    WotDiagnosticCode.GeneratedNodeId,
                    "NodeIds were generated deterministically from the target namespace and browse paths.",
                    new WotLocation(nodeId: rootNodeId)));
            }

            var items = new List<UANode>();
            var rootReferences = new List<Reference>();

            // Section 13.4 pairs a Condition Method with the event affordance
            // whose Condition it acts on. That pairing is recorded structurally
            // - the Method becomes a component of the projected EventType -
            // and the actions are synthesized before the events, so the
            // attachments are collected here and applied as each EventType is
            // created.
            var conditionMethods = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            UANode rootNode;
            string? boundType = null;
            if (isThingModel)
            {
                // A Thing Model is not always an ObjectType. §9.1 maps a
                // VariableType to one too, and the document says which through
                // its @type. Reading only "Thing Model" turns every
                // VariableType into an ObjectType: the type is lost and a
                // different one invented in its place.
                bool isVariableType = HasTypeAnnotation(document, "uav:variableType");
                bool isReferenceType = HasTypeAnnotation(document, "uav:referenceType");
                bool isDataType = HasTypeAnnotation(document, "uav:dataType");
                rootNode = isDataType
                    ? new UADataType { IsAbstract = false }
                    : isReferenceType
                        ? new UAReferenceType { IsAbstract = false }
                        : isVariableType
                            ? new UAVariableType { IsAbstract = false }
                            : new UAObjectType { IsAbstract = false };

                // WoT Binding Section 5.2.1 makes invalid and unresolved
                // type-binding outcomes document-wide. A Thing Model still
                // projects a type and derives through HasSubtype, so any
                // successfully bound NodeId is intentionally ignored here.
                boundType = ApplyTypeBinding(document, typeBinding, diagnostics);
                rootReferences.Add(new Reference
                {
                    ReferenceType = "HasSubtype",
                    IsForward = false,
                    // An event-type Thing Model (@type uav:eventType) derives
                    // from BaseEventType rather than BaseObjectType.
                    Value = isDataType
                        ? WotVocabulary.BaseDataType
                        : isReferenceType
                            ? WotVocabulary.NonHierarchicalReferences
                            : isVariableType
                                ? WotVocabulary.BaseDataVariableType
                                : isEventType
                                    ? WotVocabulary.BaseEventType
                                    : WotVocabulary.BaseObjectType
                });
            }
            else
            {
                // §9.1 maps a Variable to a Thing as well as to a property: a
                // Variable that nothing contains roots its own document. Making
                // every non-model document an Object turns such a Variable into
                // an Object, which the counts never reveal because one Node
                // still comes back for one Node.
                bool isVariable = HasTypeAnnotation(document, "uav:variable");
                rootNode = isVariable ? new UAVariable() : new UAObject();

                // WoT Binding Section 5.2.1: a document may bind its projected
                // node to a type that already exists. Only fall back to
                // BaseObjectType when it declares no binding at all - an
                // unresolved or invalid binding is reported and must not be
                // silently mistyped as BaseObjectType.
                boundType = ApplyTypeBinding(document, typeBinding, diagnostics);
                rootReferences.Add(new Reference
                {
                    ReferenceType = "HasTypeDefinition",
                    IsForward = true,
                    Value = boundType is not null
                        ? ToNodeSetNodeId(boundType, nodeSet, diagnostics)
                        : isVariable
                            ? WotVocabulary.BaseDataVariableType
                            : WotVocabulary.BaseObjectType
                });
            }

            rootNode.NodeId = rootNodeId;
            string? rootBrowseName = GetUavString(document, "browseName");
            rootNode.BrowseName = rootBrowseName is null
                ? GetOrAppendNamespaceUri(nodeSet, modelUri).ToString(CultureInfo.InvariantCulture) + ":" + rootLocal
                : ToNodeSetQualifiedName(
                    document,
                    rootBrowseName,
                    nodeSet,
                    diagnostics);

            // §9.1.1: every locale the document states survives, and the
            // default locale's entry is the one the Node's own DisplayName
            // carries.
            string? declaredLocale = GetDeclaredLocale(document);
            rootNode.DisplayName = ReadTitle(
                document.RootElement, declaredLocale, document.Title ?? rootLocal) ??
                MakeText(rootLocal);
            rootNode.Description = ReadDescription(document.RootElement, declaredLocale);
            ApplyReferenceTypeNames(rootNode, document, declaredLocale);
            if (rootNode is UAType projectedType)
            {
                projectedType.IsAbstract = GetElementBool(document.RootElement, "uav:isAbstract");
            }
            if (rootNode is UAVariableType variableType)
            {
                variableType.DataType = MapJsonSchemaToDataType(
                    document, document.RootElement, nodeSet, diagnostics, dataTypes);
                variableType.ValueRank = ReadValueRank(document.RootElement);
                variableType.ArrayDimensions = ReadArrayDimensions(document.RootElement, rootLocal, diagnostics);
            }
            else if (rootNode is UAVariable rootVariable)
            {
                rootVariable.DataType = MapJsonSchemaToDataType(
                    document, document.RootElement, nodeSet, diagnostics, dataTypes);
                rootVariable.ValueRank = ReadValueRank(document.RootElement);
                rootVariable.ArrayDimensions = ReadArrayDimensions(document.RootElement, rootLocal, diagnostics);
            }

            int affordanceCount = 0;
            var propertyNodeIds = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, JsonElement> property in document.Properties)
            {
                if (!CheckAffordanceBudget(ref affordanceCount, options, diagnostics))
                {
                    break;
                }
                WotTypeBinding? propertyBinding = null;
                affordanceBindings?.TryGetValue(property.Value, out propertyBinding);
                SynthesizeProperty(
                    document, nodeSet, property.Key, property.Value, rootLocal,
                    rootNodeId, isThingModel, declaredLocale,
                    items, rootReferences, propertyNodeIds, externalSchemas, propertyBinding,
                    referenceTypeCatalog, diagnostics, dataTypes);
            }

            // Sections 6.4 and 6.4.1 relate two affordances - the annotated one
            // and the sibling that carries its unit - so the analog Properties
            // are materialized once every affordance has become a Variable.
            CompleteAffordanceOwnership(nodeSet, items, referenceTypeCatalog);
            SynthesizeAnalogFacets(
                document, nodeSet, rootLocal, rootNodeId, propertyNodeIds,
                items, rootReferences, diagnostics);

            foreach (KeyValuePair<string, JsonElement> action in document.Actions)
            {
                if (!CheckAffordanceBudget(ref affordanceCount, options, diagnostics))
                {
                    break;
                }
                SynthesizeAction(
                    document, nodeSet, action.Key, action.Value, rootLocal,
                    rootNodeId, items, rootReferences, conditionMethods, referenceTypeCatalog, diagnostics, dataTypes);
            }

            foreach (KeyValuePair<string, JsonElement> eventAffordance in document.Events)
            {
                if (!CheckAffordanceBudget(ref affordanceCount, options, diagnostics))
                {
                    break;
                }
                SynthesizeEvent(
                    document, nodeSet, eventAffordance.Key, eventAffordance.Value,
                    rootLocal, items, rootReferences, conditionMethods, diagnostics,
                    eventSelections, dataTypes);
            }

            // Section 5.2.1: every affordance is now a Node, so a member that
            // names an instance declaration of the bound type can be matched
            // against it and populate it rather than stand beside it. The pass
            // runs before the links are synthesized so the References it
            // rewrites are the ones the affordance passes created.
            MergeInstanceDeclarations(
                document, nodeSet, items, rootReferences, rootNodeId,
                declarations, diagnostics);

            SynthesizeLinks(
                document, rootNode, items, rootReferences, thingCatalog,
                referenceTypeCatalog, resolutionContext, options, boundType, nodeSet,
                diagnostics, componentTargets);
            SynthesizeComponentArrays(
                document, rootReferences, nodeSet, diagnostics, referenceTypeCatalog);
            if (parentPlacement is { } placement)
            {
                string parentId = ToNodeSetNodeId(placement.ParentNodeId, nodeSet, diagnostics);
                if (!rootReferences.Exists(reference =>
                    !reference.IsForward &&
                    IsComponentReference(reference.ReferenceType) &&
                    reference.Value == parentId))
                {
                    rootReferences.Add(new Reference
                    {
                        ReferenceType = "HasComponent",
                        IsForward = false,
                        Value = parentId
                    });
                }
                if (rootNode is UAInstance instance)
                {
                    instance.ParentNodeId = parentId;
                }
            }

            rootNode.References = [.. rootReferences];
            items.Insert(0, rootNode);
            var nestedOnly = new HashSet<string>(StringComparer.Ordinal);
            SynthesizeDataTypeDefinitions(document, dataTypes, nodeSet, items, nestedOnly, diagnostics);
            SynthesizeInferredDataTypes(
                document, dataTypes, nodeSet, items, nestedOnly, options, diagnostics);
            ValidateNestedOnlySelection(nestedOnly, items, diagnostics);
            bool validIdentities = ValidateSynthesizedIdentities(items, diagnostics);
            ValidateDataTypeClosureOwnership(document, nodeSet, items, dataTypes, diagnostics);
            if (!validIdentities)
            {
                return null;
            }
            nodeSet.Items = [.. items];
            CompleteAffordanceOwnership(nodeSet, items, referenceTypeCatalog);
            WotJsonResidue.Replace(
                nodeSet, document, options, diagnostics,
                definition => ToPortableNodeId(
                    ResolveDataTypeReference(document, definition, dataTypes, nodeSet, diagnostics, definition),
                    nodeSet.NamespaceUris));
            return nodeSet;
        }

        private static bool ValidateSynthesizedIdentities(
            List<UANode> items,
            List<WotDiagnostic> diagnostics)
        {
            bool valid = true;
            var owners = new Dictionary<NodeId, UANode>();
            foreach (UANode node in items)
            {
                if (!NodeId.TryParse(node.NodeId ?? string.Empty, out NodeId identity) || identity.IsNull)
                {
                    valid = false;
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ValidationError,
                        $"The synthesized Node '{node.BrowseName}' has an invalid NodeId '{node.NodeId}'.",
                        new WotLocation(nodeId: node.NodeId)));
                    continue;
                }
                if (owners.TryGetValue(identity, out UANode? owner))
                {
                    valid = false;
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ValidationError,
                        $"The NodeId '{node.NodeId}' is owned by both '{owner.BrowseName}' and " +
                        $"'{node.BrowseName}'. Distinct declarations require distinct identities.",
                        new WotLocation(nodeId: node.NodeId)));
                }
                else
                {
                    owners.Add(identity, node);
                }
            }
            return valid;
        }

        private static void SynthesizeProperty(
            WotDocument document,
            UANodeSet nodeSet,
            string key,
            JsonElement schema,
            string rootLocal,
            string rootNodeId,
            bool isThingModel,
            string? declaredLocale,
            List<UANode> items,
            List<Reference> rootReferences,
            Dictionary<string, string> propertyNodeIds,
            WotExternalSchemaCatalog? externalSchemas,
            WotTypeBinding? typeBinding,
            WotReferenceTypeCatalog? referenceTypeCatalog,
            List<WotDiagnostic> diagnostics,
            DataTypeDefinitionContext dataTypes)
        {
            string local = LocalName(GetElementString(schema, "uav:browseName")) ?? key;
            string? authoredNodeId = GetElementString(schema, "uav:id");
            string nodeId = authoredNodeId is null
                ? GenerateMemberNodeId(document, nodeSet, rootLocal, local, schema)
                : ToNodeSetNodeId(authoredNodeId, nodeSet, diagnostics);
            string? authoredBrowseName = GetElementString(schema, "uav:browseName");
            var variable = new UAVariable
            {
                NodeId = nodeId,
                BrowseName = authoredBrowseName is null
                    ? GetOrAppendNamespaceUri(nodeSet, GeneratedNamespaceUri(nodeSet))
                        .ToString(CultureInfo.InvariantCulture) +
                        ":" +
                        local
                    : ToNodeSetQualifiedName(
                        document,
                        authoredBrowseName,
                        nodeSet,
                        diagnostics,
                        schema),
                ParentNodeId = rootNodeId,
                DataType = MapJsonSchemaToDataType(document, schema, nodeSet, diagnostics, dataTypes),
                AccessLevel = MapAccessLevel(schema),
                // §9.1 maps a Variable's DataType together with its ValueRank and
                // ArrayDimensions, and OPC 10000-3 tells five ranks apart that a
                // json type cannot.
                ValueRank = ReadValueRank(schema),
                ArrayDimensions = ReadArrayDimensions(schema, local, diagnostics),
                DisplayName = ReadTitle(schema, declaredLocale),
                Description = ReadDescription(schema, declaredLocale)
            };

            // §6.4.1: the affordance reads as a string at run time but the Node
            // behind it holds the EUInformation structure the term states.
            ApplyEngineeringUnits(variable, schema);

            // §9.1: a property may state the Variable it belongs to rather than
            // the Thing. Without this a Variable's own Variables — EURange and
            // EngineeringUnits below an analog Variable — come back re-parented
            // onto the Thing, one level higher than the source put them.
            string owner = ReadComponentOfParent(schema, nodeSet, diagnostics) ?? rootNodeId;
            string? typeDefinition = ApplyTypeBinding(
                document, typeBinding, diagnostics, schema,
                "/properties/" + EscapeJsonPointerToken(key), WotExpectedNodeClass.VariableType);
            if (typeDefinition is not null)
            {
                typeDefinition = ToNodeSetNodeId(typeDefinition, nodeSet, diagnostics);
            }

            // OPC 10000-3 reaches a Property through HasProperty and nothing
            // else, so an affordance that binds itself to PropertyType - which
            // is what EngineeringUnits, EURange and every other Property does -
            // is held that way rather than as a component.
            List<Reference> ownership = ReadAffordanceOwnership(
                document, schema, owner, nodeSet, diagnostics,
                string.Equals(typeDefinition, WotVocabulary.PropertyType, StringComparison.Ordinal)
                    ? "HasProperty"
                    : "HasComponent",
                referenceTypeCatalog);
            var references = new List<Reference>
            {
                new() {
                    ReferenceType = "HasTypeDefinition",
                    IsForward = true,
                    Value = typeDefinition ?? WotVocabulary.BaseDataVariableType
                }
            };
            references.AddRange(ownership);
            AddModellingRule(schema, references);
            variable.ParentNodeId = owner;
            variable.References = [.. references];
            ValidateVariableValue(schema, variable.DataType, key, diagnostics);
            variable.Value ??= BuildVariableValue(schema, variable.DataType);

            ReportUnsupportedSchema(schema, nodeId, local, variable.DataType, externalSchemas, diagnostics);

            items.Add(variable);
            propertyNodeIds[key] = nodeId;
            if (string.Equals(owner, rootNodeId, StringComparison.Ordinal))
            {
                foreach (Reference reference in ownership)
                {
                    rootReferences.Add(new Reference
                    {
                        ReferenceType = reference.ReferenceType,
                        IsForward = true,
                        Value = nodeId
                    });
                }
            }
            _ = isThingModel;
        }

        /// <summary>
        /// Gets whether the converter maps an affordance's <c>uav:componentOf</c>
        /// onto the Variable that holds it, which is what decides whether
        /// preservation must also carry the term.
        /// </summary>
        /// <remarks>
        /// One parent is one inverse component Reference, and the forward
        /// direction restates it from that Reference. A list naming more than
        /// one parent states something a single ParentNodeId cannot, so it is
        /// not mapped and is kept verbatim instead.
        /// </remarks>
        internal static bool MapsComponentOf(JsonElement affordance)
        {
            if (affordance.ValueKind != JsonValueKind.Object ||
                !affordance.TryGetProperty("uav:componentOf", out JsonElement declared) ||
                declared.ValueKind != JsonValueKind.Array ||
                declared.GetArrayLength() != 1)
            {
                return false;
            }
            foreach (JsonElement entry in declared.EnumerateArray())
            {
                return entry.ValueKind == JsonValueKind.String &&
                    entry.GetString() is { Length: > 0 };
            }
            return false;
        }

        /// <summary>
        /// Reads the parent an affordance names, where that parent is another
        /// affordance of the same Thing rather than the Thing itself.
        /// </summary>
        private static string? ReadComponentOfParent(
            JsonElement schema,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            if (!schema.TryGetProperty("uav:componentOf", out JsonElement declared) ||
                declared.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            foreach (JsonElement entry in declared.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String)
                {
                    return ToNodeSetNodeId(entry.GetString()!, nodeSet, diagnostics);
                }
            }
            return null;
        }

        /// <summary>
        /// Completes forward ownership edges before analog facets reuse their
        /// Properties and again when later-declared Method owners are available.
        /// </summary>
        private static void CompleteAffordanceOwnership(
            UANodeSet nodeSet,
            List<UANode> items,
            WotReferenceTypeCatalog? referenceTypeCatalog)
        {
            Dictionary<string, UANode> index = BuildIndex(items);
            INodeSetAliasResolver aliases = NodeSetDeclaredAliases.FromNodeSet(nodeSet, WotNodeSetAliases.Instance);
            foreach (UANode node in items)
            {
                foreach (Reference reference in node.References ?? [])
                {
                    if (reference.IsForward ||
                        reference.ReferenceType is null ||
                        reference.Value is null ||
                        (!IsComponentReference(reference.ReferenceType) &&
                            !IsHasComponentReference(reference.ReferenceType, nodeSet, referenceTypeCatalog)) ||
                        !index.TryGetValue(reference.Value, out UANode? owner))
                    {
                        continue;
                    }
                    var references = new List<Reference>(owner.References ?? []);
                    if (references.Exists(existing => existing.IsForward &&
                        existing.Value == node.NodeId &&
                        ResolveArchivedAlias(existing.ReferenceType, aliases) ==
                            ResolveArchivedAlias(reference.ReferenceType, aliases)))
                    {
                        continue;
                    }
                    references.Add(new Reference
                    {
                        ReferenceType = reference.ReferenceType,
                        IsForward = true,
                        Value = node.NodeId
                    });
                    owner.References = [.. references];
                }
            }
        }

        private static List<Reference> ReadAffordanceOwnership(
            WotDocument document,
            JsonElement affordance,
            string owner,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics,
            string defaultReferenceType,
            WotReferenceTypeCatalog? referenceTypeCatalog = null)
        {
            var references = new List<Reference>();
            foreach (JsonElement link in WotDocument.ReadArray(affordance, "links"))
            {
                string? rel = GetElementString(link, "rel");
                string? href = GetElementString(link, "href");
                if (rel is null ||
                    rel == TypeBindingRel ||
                    href is null ||
                    !LooksLikeNodeId(href) ||
                    ToNodeSetNodeId(href, nodeSet, diagnostics) != owner)
                {
                    continue;
                }
                string referenceType;
                bool isForward;
                if (rel is ComponentOfRel or ComponentOfAliasRel)
                {
                    referenceType = GetElementString(link, "uav:refId") ?? "i=47";
                    isForward = false;
                }
                else if (!TryResolveLinkReferenceType(
                    document, link, rel, referenceTypeCatalog, diagnostics, out referenceType, out isForward))
                {
                    continue;
                }
                referenceType = ToNodeSetReferenceType(referenceType, nodeSet, diagnostics);
                if (isForward ||
                    (!IsComponentReference(referenceType) &&
                        !IsHasComponentReference(referenceType, nodeSet, referenceTypeCatalog)))
                {
                    continue;
                }
                referenceType = WotVocabulary.TryGetReferenceTypeBrowseName(referenceType, out string name)
                    ? name
                    : referenceType;
                if (!references.Exists(reference => reference.ReferenceType == referenceType))
                {
                    references.Add(new Reference
                    {
                        ReferenceType = referenceType,
                        IsForward = false,
                        Value = owner
                    });
                }
            }
            if (references.Count == 0)
            {
                references.Add(new Reference
                {
                    ReferenceType = defaultReferenceType,
                    IsForward = false,
                    Value = owner
                });
            }
            return references;
        }

        /// <summary>
        /// Rebuilds a Variable's <c>Value</c> from the property's <c>const</c>
        /// or <c>default</c>.
        /// </summary>
        /// <remarks>
        /// The DataType decides the XML shape, which is why §5.4's definitive
        /// statement matters here: a JSON string alone cannot say whether it is
        /// a <c>String</c> or the <c>Text</c> of a <c>LocalizedText</c>.
        /// </remarks>
        private static System.Xml.XmlElement? BuildVariableValue(
            JsonElement schema, string? dataType)
        {
            if (schema.ValueKind != JsonValueKind.Object ||
                ReadValueRank(schema) != ScalarValueRank ||
                (!schema.TryGetProperty("const", out JsonElement constant) &&
                    !schema.TryGetProperty("default", out constant)))
            {
                return null;
            }
            string? local = VariableValueElementName(dataType);
            if (local is null || !TryReadVariableConstant(constant, local, out string textValue))
            {
                return null;
            }
            var document = new System.Xml.XmlDocument { XmlResolver = null };
            System.Xml.XmlElement element = document.CreateElement(
                "uax", local, UaXmlNamespace);
            if (string.Equals(local, "LocalizedText", StringComparison.Ordinal))
            {
                System.Xml.XmlElement text = document.CreateElement(
                    "uax", "Text", UaXmlNamespace);
                text.InnerText = textValue;
                element.AppendChild(text);
                return element;
            }
            element.InnerText = textValue;
            return element;
        }

        private static string? VariableValueElementName(string? dataType)
        {
            if (dataType is not null && WotNodeSetAliases.Instance.TryResolve(dataType, out string resolved))
            {
                dataType = resolved;
            }
            return dataType switch
            {
                "i=1" => "Boolean",
                "i=2" => "SByte",
                "i=3" => "Byte",
                "i=4" => "Int16",
                "i=5" => "UInt16",
                "i=6" => "Int32",
                "i=7" => "UInt32",
                "i=8" => "Int64",
                "i=9" => "UInt64",
                "i=10" => "Float",
                "i=11" => "Double",
                "i=12" => "String",
                "i=21" => "LocalizedText",
                _ => null
            };
        }

        private static bool TryReadVariableConstant(JsonElement constant, string local, out string text)
        {
            text = string.Empty;
            if (constant.ValueKind == JsonValueKind.String && local is "String" or "LocalizedText")
            {
                text = constant.GetString() ?? string.Empty;
                return true;
            }
            if (constant.ValueKind is JsonValueKind.True or JsonValueKind.False && local == "Boolean")
            {
                text = constant.GetBoolean() ? "true" : "false";
                return true;
            }
            if (constant.ValueKind != JsonValueKind.Number)
            {
                return false;
            }
            if (constant.TryGetInt64(out long signed) &&
                (local == "Int64" ||
                    (local == "Int32" && signed is >= int.MinValue and <= int.MaxValue) ||
                    (local == "Int16" && signed is >= short.MinValue and <= short.MaxValue) ||
                    (local == "SByte" && signed is >= sbyte.MinValue and <= sbyte.MaxValue)))
            {
                text = System.Xml.XmlConvert.ToString(signed);
                return true;
            }
            if (constant.TryGetUInt64(out ulong unsigned) &&
                (local == "UInt64" ||
                    (local == "UInt32" && unsigned <= uint.MaxValue) ||
                    (local == "UInt16" && unsigned <= ushort.MaxValue) ||
                    (local == "Byte" && unsigned <= byte.MaxValue)))
            {
                text = System.Xml.XmlConvert.ToString(unsigned);
                return true;
            }
            if (local == "Float" &&
                constant.TryGetSingle(out float single) &&
                !float.IsInfinity(single) &&
                !float.IsNaN(single))
            {
                text = System.Xml.XmlConvert.ToString(single);
                return true;
            }
            if (local == "Double" &&
                constant.TryGetDouble(out double number) &&
                !double.IsInfinity(number) &&
                !double.IsNaN(number))
            {
                text = System.Xml.XmlConvert.ToString(number);
                return true;
            }
            return false;
        }

        private static void ValidateVariableValue(
            JsonElement schema,
            string? dataType,
            string key,
            List<WotDiagnostic> diagnostics)
        {
            string? local = VariableValueElementName(dataType);
            if (local is null || ReadValueRank(schema) != ScalarValueRank)
            {
                return;
            }
            foreach (string member in new[] { "const", "default" })
            {
                if (schema.TryGetProperty(member, out JsonElement value) &&
                    !TryReadVariableConstant(value, local, out _))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ValidationError,
                        $"The {member} value cannot be represented by the Variable's {local} DataType.",
                        WotLocation.FromPointer("/properties/" + EscapeJsonPointerToken(key) + "/" + member)));
                }
            }
            if (schema.TryGetProperty("const", out JsonElement constant) &&
                schema.TryGetProperty("default", out JsonElement defaultValue) &&
                !IsArchivedJsonSubset(constant, defaultValue))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ValidationError,
                    "The const and default members state different values for the same Variable.",
                    WotLocation.FromPointer("/properties/" + EscapeJsonPointerToken(key) + "/default")));
            }
        }

        /// <summary>
        /// Gets whether the scalar value is represented by a Variable's Value
        /// Attribute rather than by opaque JSON residue.
        /// </summary>
        internal static bool MapsVariableValue(JsonElement schema)
        {
            if (schema.ValueKind != JsonValueKind.Object ||
                ReadValueRank(schema) != ScalarValueRank ||
                (!schema.TryGetProperty("const", out JsonElement constant) &&
                    !schema.TryGetProperty("default", out constant)))
            {
                return false;
            }
            string dataType = GetElementString(schema, "uav:mapToType") ??
                GetElementString(schema, "uav:dataTypeId") ??
                WotVocabulary.MapJsonTypeToDataType(
                    GetElementString(schema, "type"),
                    GetElementString(schema, "contentEncoding"),
                    GetElementString(schema, "format"));
            string? local = VariableValueElementName(ToNodeSetNodeId(dataType, new UANodeSet(), []));
            return local is not null && TryReadVariableConstant(constant, local, out _);
        }

        private const string UaXmlNamespace = "http://opcfoundation.org/UA/2008/02/Types.xsd";

        private static void SynthesizeAction(
            WotDocument document,
            UANodeSet nodeSet,
            string key,
            JsonElement action,
            string rootLocal,
            string rootNodeId,
            List<UANode> items,
            List<Reference> rootReferences,
            Dictionary<string, List<string>> conditionMethods,
            WotReferenceTypeCatalog? referenceTypeCatalog,
            List<WotDiagnostic> diagnostics,
            DataTypeDefinitionContext dataTypes)
        {
            // Section 13.4: a Condition Method is the standard Method OPC
            // 10000-9 declares on a ConditionType, not a same-named Method of
            // the projected type. Where the pairing holds, the Method takes the
            // standard BrowseName it is declared with and becomes a component
            // of the EventType the pairing names - which is what lets the
            // forward direction read the pairing back instead of guessing at
            // it. The declaration identifier is written only where the
            // ConditionType the target event projects is one this Binding knows
            // and actually declares the Method; a pairing it does not admit is
            // reported instead of being materialized against a Method that is
            // not there.
            string? declaration = ResolveConditionMethodDeclaration(
                document, action, key, nodeSet, diagnostics);
            string actsOn = string.Empty;
            bool isConditionMethod =
                TryGetNonEmptyString(action, ConditionActionTerm, out string conditionAction) &&
                IsMappedConditionAction(conditionAction) &&
                TryGetNonEmptyString(action, ActsOnTerm, out actsOn) &&
                document.Events.ContainsKey(actsOn) &&
                IsStandardConditionMethodName(action, conditionAction);
            if (!isConditionMethod)
            {
                conditionAction = string.Empty;
                actsOn = string.Empty;
            }

            string local = isConditionMethod
                ? conditionAction
                : LocalName(GetElementString(action, "uav:browseName")) ?? key;
            string? authoredNodeId = GetElementString(action, "uav:id");
            string nodeId = authoredNodeId is null
                ? GenerateMemberNodeId(document, nodeSet, rootLocal, local, action)
                : ToNodeSetNodeId(authoredNodeId, nodeSet, diagnostics);
            string? authoredBrowseName = GetElementString(action, "uav:browseName");
            var method = new UAMethod
            {
                NodeId = nodeId,
                BrowseName = isConditionMethod
                    // The standard Condition Method's BrowseName is in the base
                    // OPC UA namespace; writing the document's own namespace
                    // there would name a different QualifiedName than the one
                    // OPC 10000-9 declares.
                    ? conditionAction
                    : authoredBrowseName is null
                        ? GetOrAppendNamespaceUri(nodeSet, GeneratedNamespaceUri(nodeSet))
                            .ToString(CultureInfo.InvariantCulture) +
                            ":" +
                            local
                        : ToNodeSetQualifiedName(
                            document,
                            authoredBrowseName,
                            nodeSet,
                            diagnostics,
                            action),
                MethodDeclarationId = declaration,
                ParentNodeId = rootNodeId
            };
            string? declaredLocale = GetDeclaredLocale(document);
            method.DisplayName = ReadTitle(action, declaredLocale);
            method.Description = ReadDescription(action, declaredLocale);

            string owner = ReadComponentOfParent(action, nodeSet, diagnostics) ?? rootNodeId;
            if (isConditionMethod &&
                document.Events.TryGetValue(actsOn, out JsonElement target))
            {
                owner = EventNodeId(document, target, actsOn, rootLocal, nodeSet);
                method.ParentNodeId = owner;
                if (!conditionMethods.TryGetValue(actsOn, out List<string>? attached))
                {
                    attached = [];
                    conditionMethods[actsOn] = attached;
                }
                attached.Add(nodeId);
            }

            List<Reference> ownership = ReadAffordanceOwnership(
                document, action, owner, nodeSet, diagnostics, "HasComponent", referenceTypeCatalog);
            var references = new List<Reference>(ownership);
            AddModellingRule(action, references);
            items.Add(method);

            // §9.1: the action's input and output DataSchemas are the Method's
            // InputArguments and OutputArguments Properties. A schema that
            // cannot be mapped is reported and left to preservation rather than
            // dropped, so a Method never silently loses its signature.
            SynthesizeMethodArguments(
                document, nodeSet, action, key, nodeId, local, rootLocal,
                items, references, diagnostics, dataTypes);
            method.References = [.. references];

            if (string.Equals(owner, rootNodeId, StringComparison.Ordinal))
            {
                foreach (Reference reference in ownership)
                {
                    rootReferences.Add(new Reference
                    {
                        ReferenceType = reference.ReferenceType,
                        IsForward = true,
                        Value = nodeId
                    });
                }
            }
        }

        private static void SynthesizeEvent(
            WotDocument document,
            UANodeSet nodeSet,
            string key,
            JsonElement eventAffordance,
            string rootLocal,
            List<UANode> items,
            List<Reference> rootReferences,
            Dictionary<string, List<string>> conditionMethods,
            List<WotDiagnostic> diagnostics,
            WotEventSelectionCatalog? eventSelections,
            DataTypeDefinitionContext dataTypes)
        {
            string local = LocalName(GetElementString(eventAffordance, "uav:browseName")) ?? key;
            string? authoredNodeId = GetElementString(
                eventAffordance,
                "uav:id");
            string nodeId = authoredNodeId is null
                ? GenerateMemberNodeId(document, nodeSet, rootLocal, local, eventAffordance)
                : ToNodeSetNodeId(authoredNodeId, nodeSet, diagnostics);
            string? authoredBrowseName = GetElementString(
                eventAffordance,
                "uav:browseName");
            var eventType = new UAObjectType
            {
                NodeId = nodeId,
                BrowseName = authoredBrowseName is null
                    ? GetOrAppendNamespaceUri(nodeSet, GeneratedNamespaceUri(nodeSet))
                        .ToString(CultureInfo.InvariantCulture) +
                        ":" +
                        local
                    : ToNodeSetQualifiedName(
                        document,
                        authoredBrowseName,
                        nodeSet,
                        diagnostics,
                        eventAffordance),
                IsAbstract = false
            };
            string? declaredLocale = GetDeclaredLocale(document);
            eventType.DisplayName = ReadTitle(eventAffordance, declaredLocale);
            eventType.Description = ReadDescription(eventAffordance, declaredLocale);
            eventType.References =
            [
                new Reference
                {
                    ReferenceType = "HasSubtype",
                    IsForward = false,
                    Value = ResolveConditionSupertype(
                        document, eventAffordance, key, nodeSet, diagnostics)
                }
            ];

            items.Add(eventType);

            var eventReferences = new List<Reference>(eventType.References);

            // Section 13.3: the members of the notification's data object that
            // the projected type adds are fields of that type. The ones it
            // inherits are already declared by the type they come from and are
            // deliberately not created a second time here.
            SynthesizeEventFields(
                document, nodeSet, eventAffordance, key,
                eventType.References[0].Value!, nodeId, local, rootLocal,
                items, eventReferences, diagnostics, eventSelections, dataTypes);

            // Section 13.4: the Condition Methods that act on this event are
            // components of the type that declares the Condition, which is what
            // records the pairing the two terms state.
            if (conditionMethods.TryGetValue(key, out List<string>? attached))
            {
                foreach (string methodNodeId in attached)
                {
                    eventReferences.Add(new Reference
                    {
                        ReferenceType = "HasComponent",
                        IsForward = true,
                        Value = methodNodeId
                    });
                }
            }
            eventType.References = [.. eventReferences];

            rootReferences.Add(new Reference
            {
                ReferenceType = "GeneratesEvent",
                IsForward = true,
                Value = nodeId
            });
        }

        private static void SynthesizeLinks(
            WotDocument document,
            UANode root,
            List<UANode> items,
            List<Reference> rootReferences,
            WotThingCatalog? thingCatalog,
            WotReferenceTypeCatalog? referenceTypeCatalog,
            WotResolutionContext resolutionContext,
            WotNodeSetConverterOptions options,
            string? boundType,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics,
            Dictionary<JsonElement, WotResolvedNode?>? componentTargets)
        {
            INodeSetAliasResolver aliases = NodeSetDeclaredAliases.FromNodeSet(nodeSet, WotNodeSetAliases.Instance);
            foreach (JsonElement link in document.Links)
            {
                if (link.ValueKind == JsonValueKind.Object &&
                    link.TryGetProperty("uav:declaration", out _) &&
                    (GetElementString(link, "rel") is not { } rel ||
                        !IsReferenceRel(document, link, rel, referenceTypeCatalog) ||
                        string.IsNullOrWhiteSpace(GetElementString(link, "href"))))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ValidationError,
                        "uav:declaration requires a typed component link with a resolvable type target.",
                        new WotLocation(jsonPointer: "/links")));
                }
            }
            foreach (ResolvableThingReference thingReference in EnumerateResolvableThingReferences(
                document,
                referenceTypeCatalog,
                diagnostics))
            {
                if (thingReference.IsExtends)
                {
                    if (TryResolveTargetNodeId(
                        thingReference.Reference,
                        thingCatalog,
                        resolutionContext,
                        options,
                        diagnostics,
                        out string extendsTarget))
                    {
                        ValidateInstantiatedThingModelType(document, boundType, extendsTarget, diagnostics);
                        SetSuperType(rootReferences, ToNodeSetNodeId(extendsTarget, nodeSet, diagnostics));
                    }
                    continue;
                }

                if (TryResolveTargetNodeId(
                    thingReference.Reference,
                    thingCatalog,
                    resolutionContext,
                    options,
                    diagnostics,
                    out string linkTarget))
                {
                    string referenceType = ToNodeSetReferenceType(
                        thingReference.ReferenceType!, nodeSet, diagnostics);
                    string target = ToNodeSetNodeId(linkTarget, nodeSet, diagnostics);
                    WotResolvedNode? targetNode = null;
                    componentTargets?.TryGetValue(thingReference.SourceLink, out targetNode);
                    if (thingReference.SourceLink.TryGetProperty("uav:declaration", out JsonElement declaration))
                    {
                        SynthesizeComponentDeclaration(
                            document, root, items, rootReferences, nodeSet, thingReference,
                            declaration, targetNode, target, referenceType, diagnostics, referenceTypeCatalog);
                        continue;
                    }
                    if (thingReference.IsForward &&
                        (IsComponentReference(referenceType) ||
                            IsHasComponentReference(referenceType, nodeSet, referenceTypeCatalog)) &&
                        targetNode?.NodeClass is WotExpectedNodeClass.ObjectType or
                            WotExpectedNodeClass.VariableType or WotExpectedNodeClass.ReferenceType)
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.ValidationError,
                            "A component link cannot target a type Node directly. Use uav:declaration " +
                            "to identify a distinct InstanceDeclaration.",
                            new WotLocation(jsonPointer: "/links", reference: linkTarget)));
                        continue;
                    }
                    if (!thingReference.IsForward &&
                        document.Kind == WotDocumentKind.ThingModel &&
                        ResolveArchivedAlias(referenceType, aliases) == WotVocabulary.HasSubtype)
                    {
                        SetSuperType(rootReferences, target);
                        continue;
                    }
                    if (rootReferences.Exists(reference =>
                        reference.IsForward == thingReference.IsForward &&
                        ResolveArchivedAlias(reference.ReferenceType, aliases) ==
                            ResolveArchivedAlias(referenceType, aliases) &&
                        reference.Value is not null &&
                        AreSameExpandedNodeId(reference.Value, target)))
                    {
                        continue;
                    }
                    rootReferences.Add(new Reference
                    {
                        ReferenceType = referenceType,
                        IsForward = thingReference.IsForward,
                        Value = target
                    });
                }
            }
        }

        private static void SynthesizeComponentDeclaration(
            WotDocument document,
            UANode root,
            List<UANode> items,
            List<Reference> rootReferences,
            UANodeSet nodeSet,
            ResolvableThingReference link,
            JsonElement declaration,
            WotResolvedNode? type,
            string target,
            string referenceType,
            List<WotDiagnostic> diagnostics,
            WotReferenceTypeCatalog? referenceTypeCatalog)
        {
            var location = new WotLocation(jsonPointer: "/links", reference: link.Reference);
            if (root is not (UAObjectType or UAVariableType) ||
                !link.IsForward ||
                (!IsComponentReference(referenceType) &&
                    !IsHasComponentReference(referenceType, nodeSet, referenceTypeCatalog)) ||
                declaration.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error, WotDiagnosticCode.ValidationError,
                    "uav:declaration requires a forward component-template link on an ObjectType or VariableType.",
                    location));
                return;
            }
            if (type is null && NormalizeExpandedNodeId(link.Reference) == WotVocabulary.BaseObjectType)
            {
                type = new WotResolvedNode(WotVocabulary.BaseObjectType, WotExpectedNodeClass.ObjectType);
            }
            if (type is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error, WotDiagnosticCode.UnresolvedTypeBinding,
                    $"The component template '{link.Reference}' did not resolve to a type definition.", location));
                return;
            }
            if (type.Value.NodeClass is not (WotExpectedNodeClass.ObjectType or WotExpectedNodeClass.VariableType) ||
                !AreSameExpandedNodeId(ToNodeSetNodeId(type.Value.NodeId, nodeSet, diagnostics), target))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error, WotDiagnosticCode.InvalidTypeBinding,
                    $"The component template '{link.Reference}' is not an available ObjectType or VariableType.",
                    location));
                return;
            }
            UAInstance instance;
            if (type.Value.NodeClass == WotExpectedNodeClass.VariableType)
            {
                if (type.Value.DataTypeNodeId is null || type.Value.ValueRank is not { } valueRank)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error, WotDiagnosticCode.UnresolvedTypeBinding,
                        $"The VariableType '{type.Value.NodeId}' has no resolved DataType and ValueRank.", location));
                    return;
                }
                var dimensions = new List<string>();
                foreach (uint dimension in type.Value.ArrayDimensions)
                {
                    dimensions.Add(dimension.ToString(CultureInfo.InvariantCulture));
                }
                instance = new UAVariable
                {
                    DataType = ToNodeSetNodeId(type.Value.DataTypeNodeId, nodeSet, diagnostics),
                    ValueRank = valueRank,
                    ArrayDimensions = dimensions.Count == 0 ? null : string.Join(",", dimensions)
                };
            }
            else
            {
                if (root is not UAObjectType ||
                    IsReferenceTypeNamed(referenceType, "HasProperty", WotVocabulary.HasProperty))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error, WotDiagnosticCode.ValidationError,
                        "An Object InstanceDeclaration requires an ObjectType owner and a HasComponent relationship.",
                        location));
                    return;
                }
                instance = new UAObject();
            }
            string? browseName = GetElementString(declaration, "uav:browseName");
            if (string.IsNullOrEmpty(browseName))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error, WotDiagnosticCode.NonPortableQualifiedName,
                    "uav:declaration requires its own uav:browseName; uav:refName is only link metadata.", location));
                return;
            }
            string local = LocalName(browseName)!;
            string nodeId = GetElementString(declaration, "uav:id") is { } authoredId
                ? ToNodeSetNodeId(authoredId, nodeSet, diagnostics)
                : GenerateMemberNodeId(document, nodeSet, LocalName(root.BrowseName)!, local, declaration);
            if (AreSameExpandedNodeId(nodeId, target))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error, WotDiagnosticCode.ValidationError,
                    "A component declaration cannot reuse the NodeId of its type definition.",
                    new WotLocation(nodeId: nodeId)));
                return;
            }
            var references = new List<Reference>
            {
                new() { ReferenceType = "HasTypeDefinition", Value = target },
                new() { ReferenceType = referenceType, IsForward = false, Value = root.NodeId }
            };
            AddModellingRule(declaration, references);
            instance.NodeId = nodeId;
            instance.BrowseName = ToNodeSetQualifiedName(document, browseName, nodeSet, diagnostics, declaration);
            instance.ParentNodeId = root.NodeId;
            instance.DisplayName = MakeText(local);
            instance.References = [.. references];
            items.Add(instance);
            rootReferences.Add(new Reference { ReferenceType = referenceType, Value = nodeId });
        }

        internal static bool MapsComponentDeclaration(JsonElement declaration)
        {
            if (declaration.ValueKind != JsonValueKind.Object ||
                !WotPortableIdentity.IsPortableQualifiedName(GetElementString(declaration, "uav:browseName")))
            {
                return false;
            }
            foreach (JsonProperty member in declaration.EnumerateObject())
            {
                if (member.Name is not ("uav:id" or "uav:browseName" or "uav:modellingRule") ||
                    member.Value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }
                if (member.Name == "uav:id" && !WotPortableIdentity.IsPortableNodeId(member.Value.GetString()))
                {
                    return false;
                }
            }
            return true;
        }

        private static void ValidateInstantiatedThingModelType(
            WotDocument document,
            string? boundType,
            string extendsTarget,
            List<WotDiagnostic> diagnostics)
        {
            if (document.Kind == WotDocumentKind.ThingModel ||
                boundType is null ||
                AreSameExpandedNodeId(boundType, extendsTarget))
            {
                return;
            }

            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.InvalidTypeBinding,
                "The document instantiates a Thing Model that projects '" +
                extendsTarget +
                "', but its type binding resolves to '" +
                boundType +
                "'. The two must " +
                "resolve to the same type node (WoT Binding Section 5.2.1).",
                new WotLocation(jsonPointer: "/links")));
        }

        private static bool AreSameExpandedNodeId(string first, string second)
        {
            return string.Equals(
                NormalizeExpandedNodeId(first),
                NormalizeExpandedNodeId(second),
                StringComparison.Ordinal);
        }

        internal static string NormalizeExpandedNodeId(string nodeId)
        {
            if (nodeId.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int delimiter = nodeId.IndexOf(';', 4);
                if (delimiter > 4 && delimiter + 1 < nodeId.Length)
                {
                    string namespaceUri = CoreUtils.UnescapeUri(nodeId.AsSpan(4, delimiter - 4));
                    string identifier = NormalizeNamespaceZeroNodeId(nodeId[(delimiter + 1)..]);
                    if (string.Equals(namespaceUri, WotVocabulary.OpcUaNamespace, StringComparison.Ordinal))
                    {
                        return identifier;
                    }
                    return "nsu=" + CoreUtils.EscapeUri(namespaceUri) + ";" + identifier;
                }
            }

            return NormalizeNamespaceZeroNodeId(nodeId);
        }

        private static string NormalizeNamespaceZeroNodeId(string nodeId)
        {
            if (!NodeId.TryParse(nodeId, out NodeId parsed))
            {
                return nodeId;
            }

            var buffer = new System.Text.StringBuilder();
            NodeId.Format(
                CultureInfo.InvariantCulture,
                buffer,
                parsed.IdentifierAsString,
                parsed.IdType,
                parsed.NamespaceIndex);
            return buffer.ToString();
        }

        private static IEnumerable<ResolvableThingReference>
            EnumerateResolvableThingReferences(
                WotDocument document,
                WotReferenceTypeCatalog? referenceTypeCatalog,
                List<WotDiagnostic> diagnostics)
        {
            foreach (JsonElement link in document.Links)
            {
                string? rel = GetElementString(link, "rel");
                string? href = GetElementString(link, "href");
                if (rel is null || href is null)
                {
                    continue;
                }

                if (string.Equals(rel, "tm:extends", StringComparison.Ordinal))
                {
                    yield return new ResolvableThingReference(href, null, true, true) { SourceLink = link };
                    continue;
                }

                if (!IsReferenceRel(document, link, rel, referenceTypeCatalog))
                {
                    continue;
                }

                if (TryResolveLinkReferenceType(
                    document,
                    link,
                    rel,
                    referenceTypeCatalog,
                    diagnostics,
                    out string referenceType,
                    out bool isForward))
                {
                    yield return new ResolvableThingReference(
                        href, referenceType, false, isForward)
                    { SourceLink = link };
                }
            }
        }

        private static bool TryResolveLinkReferenceType(
            WotDocument document,
            JsonElement link,
            string rel,
            WotReferenceTypeCatalog? referenceTypeCatalog,
            List<WotDiagnostic> diagnostics,
            out string referenceType,
            out bool isForward)
        {
            isForward = true;
            string? modelName = IsModelConceptRelation(document, link, rel, referenceTypeCatalog)
                ? rel
                : null;
            string? definitive = GetElementString(link, "uav:refId");
            string? canonicalDefinitive = CanonicalReferenceType(definitive);
            if (definitive is not null && canonicalDefinitive is null)
            {
                diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.ModelConceptUnresolved,
                $"The uav:refId value '{definitive}' is not a portable " +
                "ExpandedNodeId.",
                new WotLocation(reference: definitive)));
                referenceType = string.Empty;
                return false;
            }

            // A definitive identifier the local context holds as something
            // other than a ReferenceType types nothing, so it is reported
            // before it can be used as if it did (WoT Binding Section 6.2).
            if (canonicalDefinitive is not null &&
                referenceTypeCatalog is not null &&
                referenceTypeCatalog.NamesNonReferenceType(canonicalDefinitive))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ReferenceTypeNodeClassInvalid,
                    $"The uav:refId value '{canonicalDefinitive}' names a Node " +
                    "that is not a ReferenceType, so it cannot type a relation.",
                    new WotLocation(reference: canonicalDefinitive)));
                referenceType = string.Empty;
                return false;
            }

            WotReferenceTypeAnswer answer = modelName is null
                ? WotReferenceTypeAnswer.Unresolved
                : ResolveReferenceTypeName(document, modelName, referenceTypeCatalog, link);

            if (answer.Outcome == WotReferenceTypeOutcome.Resolved)
            {
                WotResolvedReferenceType resolved = answer.Single;
                if (canonicalDefinitive is not null &&
                    !AreSameExpandedNodeId(resolved.NodeId, canonicalDefinitive))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ModelConceptConflict,
                        $"The ReferenceType model name '{modelName}' resolves to " +
                        $"'{resolved.NodeId}' but uav:refId is '{definitive}'.",
                        new WotLocation(reference: modelName)));
                    referenceType = string.Empty;
                    return false;
                }
                referenceType = resolved.NodeId;
                isForward = resolved.IsForward;
                return IsConcreteReferenceType(
                    referenceType, modelName, diagnostics, referenceTypeCatalog, canonicalDefinitive is not null);
            }

            if (answer.Outcome == WotReferenceTypeOutcome.Ambiguous)
            {
                return TrySettleAmbiguousReferenceType(
                    answer,
                    modelName!,
                    definitive,
                    canonicalDefinitive,
                    diagnostics,
                    out referenceType,
                    out isForward) &&
                    IsConcreteReferenceType(referenceType, modelName, diagnostics, referenceTypeCatalog, true);
            }

            if (answer.Outcome == WotReferenceTypeOutcome.NotAReferenceType)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ReferenceTypeNodeClassInvalid,
                    $"The relation '{modelName}' names a Node that is not a " +
                    "ReferenceType, so it cannot type a relation.",
                    new WotLocation(reference: modelName)));
                referenceType = string.Empty;
                return false;
            }

            if (canonicalDefinitive is not null)
            {
                // uav:refId is definitive and names the ReferenceType only, not
                // a direction, so the reference reads forward. This is the
                // behaviour a document authored against release 1.0 relies on.
                referenceType = canonicalDefinitive;
                return IsConcreteReferenceType(referenceType, modelName, diagnostics, referenceTypeCatalog, true);
            }

            if (modelName is not null)
            {
                diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.ModelConceptUnresolved,
                $"The ReferenceType relation '{modelName}' could not be " +
                "resolved and has no ExpandedNodeId fallback.",
                new WotLocation(reference: modelName)));
                referenceType = string.Empty;
                return false;
            }

            // With uav:componentModel gone (spec PR #19), a binding link that
            // resolves to no ReferenceType organizes rather than composes.
            // Emitted as a NodeId, not a browse name: a NodeSet may only use a
            // name that it declares in <Aliases>, and the converter declares
            // none, so a bare name would fail to load.
            referenceType = WotVocabulary.Organizes;
            return true;
        }

        private static bool IsConcreteReferenceType(
            string referenceType,
            string? modelName,
            List<WotDiagnostic> diagnostics,
            WotReferenceTypeCatalog? referenceTypeCatalog,
            bool definitive)
        {
            string identity = NormalizeExpandedNodeId(referenceType);
            if (!NodeSetStandardAliases.IsAbstractReferenceType(identity) &&
                referenceTypeCatalog?.IsAbstract(identity) != true)
            {
                return true;
            }
            diagnostics.Add(new WotDiagnostic(
                definitive ? WotDiagnosticSeverity.Error : WotDiagnosticSeverity.Warning,
                WotDiagnosticCode.ReferenceTypeProjectionInvalid,
                $"The ReferenceType '{modelName ?? referenceType}' is abstract and cannot connect Nodes directly; " +
                "the link is retained as unmapped metadata.",
                new WotLocation(reference: referenceType)));
            return false;
        }

        private static string? CanonicalReferenceType(string? referenceType)
        {
            if (string.IsNullOrEmpty(referenceType))
            {
                return null;
            }
            return IsNodeId(referenceType) ? referenceType : null;
        }

        /// <summary>
        /// Settles a relation whose name the local context matched more than
        /// once.
        /// </summary>
        /// <remarks>
        /// WoT Binding Section 6.2 makes <c>uav:refId</c> required exactly
        /// here, and it settles the name by identifying which of the candidates
        /// was meant - which also fixes the direction, because each candidate
        /// carries the name that matched it. An identifier naming none of them
        /// is a conflict, and no identifier at all leaves the relation
        /// ambiguous; neither is repaired by choosing one.
        /// </remarks>
        private static bool TrySettleAmbiguousReferenceType(
            WotReferenceTypeAnswer answer,
            string modelName,
            string? definitive,
            string? canonicalDefinitive,
            List<WotDiagnostic> diagnostics,
            out string referenceType,
            out bool isForward)
        {
            referenceType = string.Empty;
            isForward = true;
            if (canonicalDefinitive is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ReferenceTypeAmbiguous,
                    $"The ReferenceType relation '{modelName}' resolves to " +
                    $"{answer.Matches.Count} ReferenceTypes and carries no " +
                    "uav:refId to settle it (WoT Binding Section 6.2).",
                    new WotLocation(reference: modelName)));
                return false;
            }
            foreach (WotResolvedReferenceType candidate in answer.Matches)
            {
                if (AreSameExpandedNodeId(candidate.NodeId, canonicalDefinitive))
                {
                    referenceType = candidate.NodeId;
                    isForward = candidate.IsForward;
                    return true;
                }
            }
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.ModelConceptConflict,
                $"The ReferenceType model name '{modelName}' resolves to " +
                $"{answer.Matches.Count} ReferenceTypes, none of which is " +
                $"the uav:refId '{definitive}'.",
                new WotLocation(reference: modelName)));
            return false;
        }

        /// <summary>
        /// Restores the second name a projected ReferenceType answers to.
        /// </summary>
        /// <remarks>
        /// OPC 10000-3 gives a ReferenceType an InverseName and a Symmetric
        /// flag, and WoT Binding Section 5.1.2 makes the InverseName the name a
        /// link <c>rel</c> uses for the reverse direction. Restoring them is
        /// what makes a ReferenceType document a complete description of the
        /// Node - and what lets a local context built from a set of documents
        /// answer an inverse relation of a companion model.
        /// </remarks>
        private static void ApplyReferenceTypeNames(
            UANode rootNode,
            WotDocument document,
            string? declaredLocale)
        {
            if (rootNode is not UAReferenceType referenceType)
            {
                return;
            }
            if (document.RootElement.TryGetProperty(
                    InverseNameTerm, out JsonElement inverseName) &&
                inverseName.ValueKind == JsonValueKind.String &&
                inverseName.GetString() is { Length: > 0 } value)
            {
                referenceType.InverseName =
                [
                    new Export.LocalizedText
                    {
                        Locale = declaredLocale ?? string.Empty,
                        Value = value
                    }
                ];
            }
            referenceType.Symmetric =
                document.RootElement.TryGetProperty(SymmetricTerm, out JsonElement symmetric) &&
                symmetric.ValueKind == JsonValueKind.True;
        }

        /// <summary>
        /// Resolves the ReferenceType a compact model name denotes, the
        /// direction the name expressed, and how definite the answer is.
        /// </summary>
        /// <remarks>
        /// The local context of WoT Binding Section 5.1.5 is consulted first,
        /// so a companion model's own ReferenceType resolves by name wherever
        /// the caller supplied one, and the standard base-namespace names are
        /// the fallback for the reserved <c>ua</c> namespace. Both halves
        /// resolve a BrowseName <em>and</em> an InverseName: OPC 10000-3 gives
        /// a ReferenceType two names, and the one an author used is what says
        /// which way the reference runs. Nothing here is limited to a fixed
        /// table: any ReferenceType the local context holds resolves by the
        /// same rules the base-namespace ones do.
        /// </remarks>
        private static WotReferenceTypeAnswer ResolveReferenceTypeName(
            WotDocument document,
            string modelName,
            WotReferenceTypeCatalog? referenceTypeCatalog,
            JsonElement carryingNode = default)
        {
            if (!TrySplitCompactModelName(
                modelName,
                out string prefix,
                out string browseName) ||
                !TryGetContextNamespace(document, prefix, out string namespaceUri, carryingNode))
            {
                return WotReferenceTypeAnswer.Unresolved;
            }
            if (referenceTypeCatalog is not null &&
                referenceTypeCatalog.TryGet(namespaceUri, browseName, out WotReferenceTypeAnswer answer) &&
                answer.Outcome != WotReferenceTypeOutcome.Unresolved)
            {
                return answer;
            }
            if (string.Equals(
                    namespaceUri,
                    WotVocabulary.OpcUaNamespace,
                    StringComparison.Ordinal) &&
                WotVocabulary.TryResolveReferenceTypeName(
                    browseName,
                    out string standard,
                    out bool isForward))
            {
                return WotReferenceTypeAnswer.FromMatches(
                    new ArrayOf<WotResolvedReferenceType>(
                        [new WotResolvedReferenceType(standard, browseName, isForward)]));
            }
            return WotReferenceTypeAnswer.Unresolved;
        }

        private static bool TrySplitCompactModelName(
            string value,
            out string prefix,
            out string browseName)
        {
            prefix = string.Empty;
            browseName = string.Empty;
            int separator = -1;
            for (int ii = 0; ii < value.Length; ii++)
            {
                if (value[ii] == ':')
                {
                    separator = ii;
                    break;
                }
            }
            if (separator <= 0 || separator + 1 >= value.Length)
            {
                return false;
            }
            string candidate = value[..separator];
            if (!IsAsciiLetter(candidate[0]) && candidate[0] != '_')
            {
                return false;
            }
            for (int ii = 0; ii < candidate.Length; ii++)
            {
                char character = candidate[ii];
                if (!IsAsciiLetter(character) &&
                    character is not (>= '0' and <= '9') &&
                    character is not ('_' or '.' or '-'))
                {
                    return false;
                }
            }
            prefix = candidate;
            browseName = value[(separator + 1)..];
            return true;
        }

        private static bool IsAsciiLetter(char value)
        {
            return value is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');
        }

        /// <summary>
        /// Seeds the target namespace table from the document's <c>@context</c>.
        /// </summary>
        /// <remarks>
        /// <i>OPC UA — WoT Binding</i> §9.1 maps the NodeSet namespace table onto
        /// the <c>@context</c> prefix bindings keyed by namespace index, so a
        /// document written from a NodeSet carries <c>ns1</c>…<c>nsN</c> in that
        /// order. Reading them back reproduces the source table instead of
        /// rebuilding it on demand from whichever identifiers happen to be
        /// converted, which is what makes a BrowseName keep the namespace it was
        /// written with and what lets the documents of one set agree on index.
        /// A gap in the sequence stops the seed: an index is only meaningful if
        /// every index below it is bound.
        /// </remarks>
        private static string[] SeedNamespaceUris(WotDocument document, string modelUri)
        {
            var uris = new List<string>();
            for (int index = 1; ; index++)
            {
                string prefix = "ns" + index.ToString(CultureInfo.InvariantCulture);
                if (!TryGetContextNamespace(document, prefix, out string namespaceUri) ||
                    namespaceUri.Length == 0)
                {
                    break;
                }
                uris.Add(namespaceUri);
            }
            if (uris.Count == 0)
            {
                return [modelUri];
            }
            if (!uris.Contains(modelUri))
            {
                uris.Insert(0, modelUri);
            }
            return [.. uris];
        }

        private static bool TryGetContextNamespace(
            WotDocument document,
            string prefix,
            out string namespaceUri,
            JsonElement carryingNode = default)
        {
            return document.TryGetContextPrefix(prefix, out namespaceUri, carryingNode);
        }

        private static bool TryGetContextNamespace(
            JsonElement context,
            string prefix,
            out string namespaceUri)
        {
            return WotDocument.TryGetContextPrefix(context, prefix, out namespaceUri);
        }

        private static void SynthesizeComponentArrays(
            WotDocument document,
            List<Reference> rootReferences,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics,
            WotReferenceTypeCatalog? referenceTypeCatalog)
        {
            SynthesizeComponentArray(
                document, "hasComponent", true, rootReferences, nodeSet, diagnostics, referenceTypeCatalog);
            SynthesizeComponentArray(
                document, "componentOf", false, rootReferences, nodeSet, diagnostics, referenceTypeCatalog);
        }

        private static void SynthesizeComponentArray(
            WotDocument document,
            string term,
            bool isForward,
            List<Reference> rootReferences,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics,
            WotReferenceTypeCatalog? referenceTypeCatalog)
        {
            if (!document.TryGetUav(term, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            foreach (JsonElement target in array.EnumerateArray())
            {
                if (target.ValueKind != JsonValueKind.String)
                {
                    continue;
                }
                string nodeId = ToNodeSetNodeId(target.GetString()!, nodeSet, diagnostics);
                if (rootReferences.Exists(reference =>
                    reference.IsForward == isForward &&
                    reference.Value is not null &&
                    AreSameExpandedNodeId(reference.Value, nodeId) &&
                    IsHasComponentReference(reference.ReferenceType, nodeSet, referenceTypeCatalog)))
                {
                    continue;
                }
                rootReferences.Add(new Reference
                {
                    ReferenceType = "HasComponent",
                    IsForward = isForward,
                    Value = nodeId
                });
            }
        }

        private static bool IsHasComponentReference(
            string? referenceType,
            UANodeSet nodeSet,
            WotReferenceTypeCatalog? referenceTypeCatalog)
        {
            if (referenceType is "HasComponent" or WotVocabulary.HasComponent ||
                WotVocabulary.TryGetHasComponentSubtype(referenceType, out _))
            {
                return true;
            }
            return referenceType is not null &&
                ToPortableNodeId(referenceType, nodeSet.NamespaceUris) is { } identity &&
                referenceTypeCatalog?.IsHasComponentSubtype(NormalizeExpandedNodeId(identity)) == true;
        }

        /// <summary>
        /// Rewrites a resolved ReferenceType into the form a NodeSet may state
        /// it in.
        /// </summary>
        /// <remarks>
        /// A relation resolves to the portable ExpandedNodeId of WoT Binding
        /// Section 5.1.1, but a NodeSet2 document states a ReferenceType as a
        /// NodeSet-local NodeId or as a name it declares in
        /// <c>&lt;Aliases&gt;</c>, and the importer rejects anything else. A
        /// standard name such as <c>HasComponent</c> is left alone - the alias
        /// completion pass declares it - and a portable identifier is resolved
        /// through the NodeSet's own namespace table, appending the namespace
        /// when the companion model it names is not yet in it.
        /// </remarks>
        private static string ToNodeSetReferenceType(
            string referenceType,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            return referenceType.StartsWith("nsu=", StringComparison.Ordinal)
                ? ToNodeSetNodeId(referenceType, nodeSet, diagnostics)
                : referenceType;
        }

        private static async ValueTask PreresolveThingReferencesAsync(
            WotDocument document,
            WotNodeSetConverterOptions options,
            IWotThingResolver resolver,
            WotResolutionContext context,
            WotThingCatalog thingCatalog,
            WotReferenceTypeCatalog? referenceTypeCatalog,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            if (TakesRestorePath(document) ||
                document.Kind == WotDocumentKind.Unknown)
            {
                return;
            }

            var discoveryDiagnostics = new List<WotDiagnostic>();
            foreach ((string reference, _, _, _) in EnumerateResolvableThingReferences(
                document,
                referenceTypeCatalog,
                discoveryDiagnostics))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsNodeId(reference))
                {
                    continue;
                }

                WotResolvedNode? target = await ResolveTargetAsync(
                    reference,
                    resolver,
                    context,
                    options,
                    diagnostics,
                    cancellationToken).ConfigureAwait(false);
                thingCatalog.AddTarget(reference, target);
            }
        }

        /// <summary>
        /// Pre-resolves registry-document parent links without consuming the
        /// catalog used by ordinary authored references.
        /// </summary>
        private static async ValueTask PreresolveParentReferencesAsync(
            WotDocument document,
            WotNodeSetConverterOptions options,
            IWotThingResolver resolver,
            WotResolutionContext context,
            WotThingCatalog parentCatalog,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            foreach (JsonElement link in EnumerateComponentOfLinks(document))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? href = GetElementString(link, "href");
                if (href is null || IsNodeId(href))
                {
                    continue;
                }

                WotResolvedNode? target = await ResolveTargetAsync(
                    href,
                    resolver,
                    context,
                    options,
                    diagnostics,
                    cancellationToken).ConfigureAwait(false);
                parentCatalog.AddTarget(href, target);
            }
        }

        private static async ValueTask<WotParentPlacement?> ResolveParentPlacementAsync(
            WotDocument document,
            WotThingCatalog? parentCatalog,
            IWotNodeResolver nodeResolver,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            JsonElement? componentOf = null;
            foreach (JsonElement link in EnumerateComponentOfLinks(document))
            {
                componentOf = link;
                break;
            }
            if (componentOf is null)
            {
                return null;
            }

            string? href = GetElementString(componentOf.Value, "href");
            if (string.IsNullOrEmpty(href))
            {
                AddUnresolvedParentPlacement(diagnostics, href, "The uav:componentOf link has no href.");
                return null;
            }

            if (parentCatalog is not null &&
                parentCatalog.TryTake(href, out string? registryNodeId) &&
                registryNodeId is not null)
            {
                return new WotParentPlacement(registryNodeId);
            }

            if (IsNodeId(href))
            {
                WotResolvedNode? addressSpaceNode = await nodeResolver
                    .ResolveByNodeIdAsync(href, cancellationToken).ConfigureAwait(false);
                if (addressSpaceNode is not null)
                {
                    return new WotParentPlacement(addressSpaceNode.Value.NodeId);
                }
            }

            AddUnresolvedParentPlacement(
                diagnostics,
                href,
                $"The uav:componentOf parent target '{href}' could not be resolved.");
            return null;
        }

        private static IEnumerable<JsonElement> EnumerateComponentOfLinks(WotDocument document)
        {
            foreach (JsonElement link in document.Links)
            {
                string? rel = GetElementString(link, "rel");
                if (rel is not null && IsKnownBindingRelation(rel))
                {
                    yield return link;
                }
            }
        }

        private static void AddUnresolvedParentPlacement(
            List<WotDiagnostic> diagnostics,
            string? href,
            string message)
        {
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.UnresolvedParentPlacement,
                message,
                new WotLocation(jsonPointer: "/links", reference: href)));
        }

        /// <summary>
        /// Resolves a link target to the projection root the referenced document
        /// declares or synthesizes.
        /// </summary>
        /// <remarks>
        /// A single resolution: spec PR #19 removed <c>uav:congruentType</c>,
        /// which was the only term that redirected one reference to another, so
        /// a target either declares its own identity or is unresolved. The
        /// resolution context is still entered and the bytes still counted, so
        /// the per-conversion document, depth and byte bounds continue to apply.
        /// </remarks>
        private static async ValueTask<WotResolvedNode?> ResolveTargetAsync(
            string reference,
            IWotThingResolver resolver,
            WotResolutionContext context,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.TryEnter(WotResolutionKind.Thing, reference, out WotDiagnostic? blocking))
            {
                diagnostics.Add(blocking!);
                return null;
            }

            try
            {
                WotResolverResult result = await resolver.ResolveThingAsync(
                    reference,
                    context,
                    cancellationToken).ConfigureAwait(false);
                if (!result.Found)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Warning,
                        WotDiagnosticCode.ResolverNotFound,
                        $"The referenced document '{reference}' could not be resolved.",
                        new WotLocation(reference: reference)));
                    return null;
                }
                if (!context.TryAddBytes(reference, result.Content.Length, out WotDiagnostic? limit))
                {
                    diagnostics.Add(limit!);
                    return null;
                }
                using var resolved = WotDocument.Parse(result.Content, options);
                string? resolvedId = GetUavString(resolved, "id");
                if (resolvedId is not null)
                {
                    var nodes = new WotDocumentNodeResolver([resolved]);
                    return await nodes.ResolveByNodeIdAsync(resolvedId, cancellationToken).ConfigureAwait(false);
                }

                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.UnresolvedReference,
                    $"The referenced document '{reference}' does not declare a uav:id.",
                    new WotLocation(reference: reference)));
                return null;
            }
            finally
            {
                context.Leave(reference);
            }
        }

        private static bool TryResolveTargetNodeId(
            string reference,
            WotThingCatalog? thingCatalog,
            WotResolutionContext context,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            out string nodeId)
        {
            if (IsNodeId(reference))
            {
                nodeId = reference;
                return true;
            }
            nodeId = string.Empty;
            if (thingCatalog is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.UnresolvedReference,
                    $"The reference '{reference}' could not be resolved to a NodeId without an external resolver.",
                    new WotLocation(reference: reference)));
                return false;
            }

            if (thingCatalog.TryTake(reference, out string? resolvedNodeId))
            {
                if (resolvedNodeId is null)
                {
                    return false;
                }
                nodeId = resolvedNodeId;
                return true;
            }

            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Warning,
                WotDiagnosticCode.UnresolvedReference,
                $"The reference '{reference}' was not pre-resolved before synchronous conversion.",
                new WotLocation(reference: reference)));
            _ = context;
            _ = options;
            return false;
        }

        private static void ReportUnsupportedSchema(
            JsonElement schema,
            string nodeId,
            string local,
            string? dataType,
            WotExternalSchemaCatalog? externalSchemas,
            List<WotDiagnostic> diagnostics)
        {
            if (schema.TryGetProperty("uav:externalSchema", out JsonElement external) &&
                external.ValueKind == JsonValueKind.String)
            {
                ReportExternalSchema(
                    external.GetString()!,
                    nodeId,
                    local,
                    externalSchemas,
                    diagnostics);
                return;
            }
            string? type = GetElementString(schema, "type");
            if (type is "object" or "array" && dataType is WotVocabulary.Structure or WotVocabulary.BaseDataType)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.UnsupportedSchema,
                    $"The '{type}' DataSchema was mapped to a generic DataType; a custom DataType may be required.",
                    WotLocation.FromNode(nodeId)));
            }
        }

        /// <summary>
        /// Reports what resolving an <c>uav:externalSchema</c> established.
        /// </summary>
        /// <remarks>
        /// A conversion configured with no provider never fetched the
        /// reference, so it is reported exactly as it was before providers
        /// existed: carried, not inlined. A conversion that did resolve it
        /// reports whether the external description agrees with the canonical
        /// one - and only reports it, because the canonical DataSchema and the
        /// DataType definition remain the statement of what the Variable is.
        /// </remarks>
        private static void ReportExternalSchema(
            string reference,
            string nodeId,
            string local,
            WotExternalSchemaCatalog? externalSchemas,
            List<WotDiagnostic> diagnostics)
        {
            WotExternalSchemaResult? result =
                externalSchemas is not null &&
                externalSchemas.TryGet(local, out WotExternalSchemaResult found)
                    ? found
                    : null;
            if (result is null || result.Outcome == WotExternalSchemaOutcome.NotEvaluated)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.UnsupportedSchema,
                    $"The property references an external schema '{reference}' that was not inlined.",
                    WotLocation.FromNode(nodeId)));
                return;
            }

            // The result states the reason it exists for, so the four arms
            // below differ only in how serious the outcome is.
            switch (result.Outcome)
            {
                case WotExternalSchemaOutcome.Compatible:
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Info,
                        WotDiagnosticCode.UnsupportedSchema,
                        result.Reason,
                        WotLocation.FromNode(nodeId)));
                    return;
                case WotExternalSchemaOutcome.Incompatible:
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ExternalSchemaIncompatible,
                        result.Reason,
                        WotLocation.FromNode(nodeId)));
                    return;
                case WotExternalSchemaOutcome.Ambiguous:
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Warning,
                        WotDiagnosticCode.ExternalSchemaAmbiguous,
                        result.Reason,
                        WotLocation.FromNode(nodeId)));
                    return;
                default:
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Warning,
                        WotDiagnosticCode.ExternalSchemaUnresolved,
                        result.Reason,
                        WotLocation.FromNode(nodeId)));
                    return;
            }
        }

        private static void AddModellingRule(JsonElement schema, List<Reference> references)
        {
            string? rule = GetElementString(schema, "uav:modellingRule");
            if (rule is not null && WotVocabulary.TryGetModellingRuleNodeId(rule, out string ruleNodeId))
            {
                references.Add(new Reference
                {
                    ReferenceType = "HasModellingRule",
                    IsForward = true,
                    Value = ruleNodeId
                });
            }
        }

        private static void SetSuperType(List<Reference> references, string target)
        {
            for (int ii = 0; ii < references.Count; ii++)
            {
                if (string.Equals(references[ii].ReferenceType, "HasSubtype", StringComparison.Ordinal) &&
                    !references[ii].IsForward)
                {
                    references[ii].Value = target;
                    return;
                }
            }
            references.Add(new Reference
            {
                ReferenceType = "HasSubtype",
                IsForward = false,
                Value = target
            });
        }

        private static string ToNodeSetQualifiedName(
            WotDocument document,
            string rawBrowseName,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics,
            JsonElement carryingNode = default)
        {
            if (!WotPortableIdentity.TryResolveQualifiedName(
                rawBrowseName, document, carryingNode, out WotBrowsePathElement qualifiedName))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NonPortableQualifiedName,
                    $"The uav:browseName '{rawBrowseName}' is not a portable QualifiedName " +
                    "with a bound context prefix, a namespace-0 name, or nsu=<NamespaceUri>;<Name>; " +
                    "numeric NamespaceIndexes are not permitted.",
                    new WotLocation(reference: rawBrowseName)));
                return rawBrowseName;
            }
            if (string.Equals(
                qualifiedName.NamespaceUri, WotVocabulary.OpcUaNamespace, StringComparison.Ordinal))
            {
                return qualifiedName.Name;
            }
            int namespaceIndex = GetOrAppendNamespaceUri(nodeSet, qualifiedName.NamespaceUri!);
            return namespaceIndex.ToString(CultureInfo.InvariantCulture) +
                ":" +
                qualifiedName.Name;
        }

        /// <summary>
        /// Converts an authored portable NodeId into the NodeSet-local form.
        /// </summary>
        /// <remarks>
        /// WoT Binding Section 5.1.1 forbids the session-local
        /// <c>ns=&lt;index&gt;</c> form in every NodeId-valued term, because a
        /// namespace index is only meaningful for the session that read the
        /// namespace table. OPC 10101 v1.00 permitted it; release 1.1 rejects it
        /// so a document cannot silently bind to the wrong namespace when the
        /// table is reordered. Authors shall use
        /// <c>nsu=&lt;NamespaceUri&gt;;&lt;idtype&gt;=&lt;id&gt;</c> instead.
        /// </remarks>
        private static string ToNodeSetNodeId(
            string portableNodeId,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            if (!ExpandedNodeId.TryParse(portableNodeId, out _))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ValidationError,
                    $"The NodeId '{portableNodeId}' is not a well-formed ExpandedNodeId.",
                    new WotLocation(reference: portableNodeId)));
                return portableNodeId;
            }
            if (IsSessionLocalNodeId(portableNodeId))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NonPortableIdentity,
                    $"The NodeId '{portableNodeId}' uses the session-local " +
                    "ns=<index> form, which is not permitted in a persisted " +
                    "document; use nsu=<NamespaceUri>;<idtype>=<id>.",
                    new WotLocation(reference: portableNodeId)));
                return portableNodeId;
            }
            if (portableNodeId.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int delimiter = portableNodeId.IndexOf(';', 4);
                if (delimiter < 0 || delimiter + 1 >= portableNodeId.Length)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ValidationError,
                        $"The NodeId '{portableNodeId}' is not a valid " +
                        "NamespaceUri-qualified NodeId."));
                    return portableNodeId;
                }
                string namespaceUri = CoreUtils.UnescapeUri(
                    portableNodeId.AsSpan(4, delimiter - 4));
                string identifier = portableNodeId[(delimiter + 1)..];
                if (string.Equals(
                    namespaceUri,
                    WotVocabulary.OpcUaNamespace,
                    StringComparison.Ordinal))
                {
                    return identifier;
                }
                int namespaceIndex = GetOrAppendNamespaceUri(nodeSet, namespaceUri);
                return "ns=" +
                    namespaceIndex.ToString(
                        CultureInfo.InvariantCulture) +
                    ";" +
                    identifier;
            }
            return portableNodeId;
        }

        /// <summary>
        /// Determines whether a NodeId string uses the session-local
        /// <c>ns=&lt;index&gt;</c> namespace form.
        /// </summary>
        /// <param name="nodeId">The authored NodeId string.</param>
        /// <returns>
        /// <c>true</c> when the value starts with <c>ns=</c> followed by at
        /// least one digit and a <c>;</c> separator.
        /// </returns>
        private static bool IsSessionLocalNodeId(string nodeId)
        {
            return WotPortableIdentity.IsSessionLocalNodeId(nodeId);
        }

        private static int GetOrAppendNamespaceUri(
            UANodeSet nodeSet,
            string namespaceUri)
        {
            if (nodeSet.NamespaceUris is not null)
            {
                for (int ii = 0; ii < nodeSet.NamespaceUris.Length; ii++)
                {
                    if (string.Equals(
                        nodeSet.NamespaceUris[ii],
                        namespaceUri,
                        StringComparison.Ordinal))
                    {
                        return ii + 1;
                    }
                }
            }
            List<string> uris = nodeSet.NamespaceUris is null
                ? []
                : [.. nodeSet.NamespaceUris];
            uris.Add(namespaceUri);
            nodeSet.NamespaceUris = [.. uris];
            return uris.Count;
        }

        private static bool HasTypeAnnotation(WotDocument document, string annotation)
        {
            foreach (string token in document.TypeTokens)
            {
                if (string.Equals(token, annotation, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasEventTypeAnnotation(WotDocument document)
        {
            foreach (string token in document.TypeTokens)
            {
                if (string.Equals(token, WotVocabulary.EventTypeAnnotation, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Reads the definitive type binding a document declares through
        /// <c>ua:HasTypeDefinition</c> links (WoT Binding Section 5.2.1).
        /// </summary>
        /// <remarks>
        /// Section 5.2.1 names a type in either or both of two forms. This
        /// reads the definitive one: a link whose <c>rel</c> is the
        /// <c>ua:HasTypeDefinition</c> ReferenceType compact model name and
        /// whose <c>href</c> is the ExpandedNodeId of the type. An
        /// ExpandedNodeId matches exactly one Node or none, so it needs no
        /// lookup here; the readable <c>@type</c> form is a hint that has to be
        /// resolved against the local context of Section 5.1.5 and is handled
        /// separately.
        /// <para>
        /// A Node has exactly one <c>HasTypeDefinition</c>, so more than one
        /// such link makes the document invalid rather than picking a winner.
        /// </para>
        /// </remarks>
        /// <returns>
        /// The authored ExpandedNodeId, or <c>null</c> when the document
        /// declares no definitive binding or the binding is invalid.
        /// </returns>
        private static string? ReadDefinitiveTypeBinding(
            WotDocument document,
            List<WotDiagnostic> diagnostics,
            JsonElement carryingNode = default,
            string pointer = "")
        {
            if (carryingNode.ValueKind == JsonValueKind.Undefined)
            {
                carryingNode = document.RootElement;
            }
            JsonElement candidate = default;
            int count = 0;

            // Count every candidate before judging any of them. Ambiguity
            // dominates: where a document declares more than one such link it
            // is ambiguous, and validating one arbitrary candidate on top of
            // that would report a second, misleading error about a link the
            // converter was never entitled to choose.
            foreach (JsonElement link in WotDocument.ReadArray(carryingNode, "links"))
            {
                if (!IsTypeBindingLink(document, link))
                {
                    continue;
                }

                count++;
                if (count == 1)
                {
                    candidate = link;
                }
            }

            if (count > 1)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.AmbiguousTypeBinding,
                    $"A document declares {count} '{TypeBindingRel}' links, but a Node has exactly " +
                    "one HasTypeDefinition (WoT Binding Section 5.2.1).",
                    new WotLocation(jsonPointer: pointer + "/links")));
                return null;
            }

            if (count == 0)
            {
                return null;
            }

            string? href = candidate.TryGetProperty("href", out JsonElement hrefElement) &&
                hrefElement.ValueKind == JsonValueKind.String
                    ? hrefElement.GetString()
                    : null;

            if (string.IsNullOrWhiteSpace(href))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidTypeBinding,
                    $"A '{TypeBindingRel}' link (WoT Binding Section 5.2.1) must carry the " +
                    "ExpandedNodeId of the type in its 'href'.",
                    new WotLocation(jsonPointer: pointer + "/links")));
                return null;
            }

            CheckPortableValue(TypeBindingRel + " href", href, diagnostics);
            return NormalizeExpandedNodeId(href);
        }

        private static bool IsTypeBindingLink(WotDocument document, JsonElement link)
        {
            return IsReferenceTypeLink(document, link, WotVocabulary.HasTypeDefinition, true);
        }

        private static bool IsReferenceTypeLink(
            WotDocument document,
            JsonElement link,
            string referenceType,
            bool isForward)
        {
            string? rel = GetElementString(link, "rel");
            if (rel is null)
            {
                return false;
            }
            WotReferenceTypeAnswer answer = ResolveReferenceTypeName(document, rel, null, link);
            return answer.Outcome == WotReferenceTypeOutcome.Resolved &&
                answer.Single.IsForward == isForward &&
                AreSameExpandedNodeId(answer.Single.NodeId, referenceType);
        }

        private static bool TryGetStandardVariableTypeNodeId(string value, out string nodeId)
        {
            nodeId = NormalizeExpandedNodeId(value);
            if (!NodeId.TryParse(nodeId, out NodeId parsed) || parsed.NamespaceIndex != 0)
            {
                return false;
            }
            nodeId = parsed.ToString();
            return NodeSetStandardAliases.IsVariableType(nodeId);
        }

        /// <summary>
        /// The ReferenceType compact model name that carries a definitive type
        /// binding, per WoT Binding Section 5.2.1. It is an ordinary
        /// ReferenceType name used directly in <c>rel</c>, so it adds no
        /// vocabulary of its own.
        /// </summary>
        private const string TypeBindingRel = "ua:HasTypeDefinition";

        /// <summary>
        /// Turns a resolved type binding into the NodeId to emit, reporting an
        /// unresolved or invalid one.
        /// </summary>
        /// <remarks>
        /// Both forms of Section 5.2.1 resolve through the Section 5.1.5 local
        /// context, including the definitive <c>ua:HasTypeDefinition</c> link:
        /// its outcome table fails the projection for a link that "resolves to
        /// nothing" just as it does for an unresolved name. Emitting an
        /// unverified identifier would leave a dangling HasTypeDefinition,
        /// which is the silently mistyped node the clause exists to prevent.
        /// Without a local context, only an explicit standard VariableType
        /// fact can establish a property binding. Other definitive bindings
        /// fail rather than trusting the author's namespace-zero identifier.
        /// </remarks>
        private static string? ApplyTypeBinding(
            WotDocument document,
            WotTypeBinding? typeBinding,
            List<WotDiagnostic> diagnostics,
            JsonElement carryingNode = default,
            string pointer = "",
            WotExpectedNodeClass? expected = null)
        {
            if (typeBinding is null)
            {
                // Only explicit standard VariableType facts are available without a local context.
                string? link = ReadDefinitiveTypeBinding(document, diagnostics, carryingNode, pointer);
                if (link is not null)
                {
                    if (expected == WotExpectedNodeClass.VariableType &&
                        TryGetStandardVariableTypeNodeId(link, out string standard))
                    {
                        return standard;
                    }
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.UnresolvedTypeBinding,
                        $"The '{TypeBindingRel}' link names '{link}', which cannot be resolved " +
                        "without a local context (WoT Binding Section 5.1.5). Convert through " +
                        "an entry point that supplies one.",
                        new WotLocation(jsonPointer: pointer + "/links")));
                }
                return null;
            }

            switch (typeBinding.Outcome)
            {
                case WotTypeBindingOutcome.Bound:
                    return typeBinding.NodeId;
                case WotTypeBindingOutcome.Invalid:
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        // Section 5.2.1 lists an invalid document and an
                        // ambiguous name as separate outcomes, so a NodeClass
                        // mismatch or a name and link that disagree must not be
                        // reported as ambiguity.
                        typeBinding.IsAmbiguous
                            ? WotDiagnosticCode.AmbiguousTypeBinding
                            : WotDiagnosticCode.InvalidTypeBinding,
                        typeBinding.Detail!,
                        new WotLocation(jsonPointer: pointer + "/@type")));
                    return null;
                case WotTypeBindingOutcome.Unresolved:
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.UnresolvedTypeBinding,
                        typeBinding.Detail!,
                        new WotLocation(jsonPointer: pointer + "/@type")));
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Reads the compact model names in <c>@type</c> that are type
        /// bindings, per WoT Binding Section 5.2.1.
        /// </summary>
        /// <remarks>
        /// A member is a binding when its namespace is one the local context
        /// holds; every other member is ordinary semantic annotation and is
        /// retained as residue. The test is the namespace, not whether the
        /// lookup succeeds, so a name in a held namespace that resolves to
        /// nothing is a reported mistake rather than a silently ignored
        /// annotation.
        /// </remarks>
        private static async ValueTask<List<string>> ReadTypeBindingNamesAsync(
            WotDocument document,
            IWotNodeResolver resolver,
            JsonElement carryingNode,
            CancellationToken cancellationToken)
        {
            var names = new List<string>();
            foreach (string token in WotDocument.ReadStringTokens(carryingNode, "@type"))
            {
                if (!TrySplitCompactModelName(token, out string prefix, out _) ||
                    string.Equals(prefix, "uav", StringComparison.Ordinal) ||
                    string.Equals(prefix, "tm", StringComparison.Ordinal) ||
                    !TryGetContextNamespace(document, prefix, out string namespaceUri, carryingNode))
                {
                    continue;
                }

                if (await resolver.HoldsNamespaceAsync(namespaceUri, cancellationToken)
                    .ConfigureAwait(false))
                {
                    names.Add(token);
                }
            }

            return names;
        }

        /// <summary>
        /// Resolves the type binding a document declares, applying the table in
        /// WoT Binding Section 5.2.1.
        /// </summary>
        private static async ValueTask<WotTypeBinding> ResolveTypeBindingAsync(
            WotDocument document,
            IWotNodeResolver resolver,
            List<WotDiagnostic> diagnostics,
            JsonElement carryingNode = default,
            WotExpectedNodeClass? expectedClass = null,
            string pointer = "",
            CancellationToken cancellationToken = default)
        {
            if (carryingNode.ValueKind == JsonValueKind.Undefined)
            {
                carryingNode = document.RootElement;
            }
            string? link = ReadDefinitiveTypeBinding(document, diagnostics, carryingNode, pointer);
            List<string> names = await ReadTypeBindingNamesAsync(
                document, resolver, carryingNode, cancellationToken).ConfigureAwait(false);

            if (names.Count > 1)
            {
                return WotTypeBinding.Ambiguous(
                    $"{names.Count} members of '@type' are type bindings, but a Node has " +
                    "exactly one HasTypeDefinition.");
            }

            WotExpectedNodeClass expected = expectedClass ??
                (document.Kind == WotDocumentKind.ThingModel
                    ? WotExpectedNodeClass.Any
                    : HasTypeAnnotation(document, "uav:variable")
                        ? WotExpectedNodeClass.VariableType
                        : WotExpectedNodeClass.ObjectType);

            WotResolvedNode? byLink = null;
            if (link is not null)
            {
                byLink = await resolver.ResolveByNodeIdAsync(link, cancellationToken)
                    .ConfigureAwait(false);
                if (byLink is null &&
                    expected == WotExpectedNodeClass.VariableType &&
                    TryGetStandardVariableTypeNodeId(link, out string standard))
                {
                    byLink = new WotResolvedNode(standard, WotExpectedNodeClass.VariableType);
                }
            }

            if (names.Count == 0)
            {
                if (link is null)
                {
                    return WotTypeBinding.None;
                }
                return byLink is null
                    ? WotTypeBinding.Unresolved(
                        $"The '{TypeBindingRel}' link names '{link}', which the local context " +
                        "does not hold.")
                    : Accept(byLink.Value, expected, link);
            }

            string name = names[0];
            TrySplitCompactModelName(name, out string prefix, out string browseName);
            TryGetContextNamespace(document, prefix, out string namespaceUri, carryingNode);
            ArrayOf<WotResolvedNode> byName = await resolver
                .ResolveByBrowseNameAsync(namespaceUri, browseName, expected, cancellationToken)
                .ConfigureAwait(false);

            if (link is null)
            {
                return byName.Count switch
                {
                    1 => Accept(byName[0], expected, name),
                    0 => WotTypeBinding.Unresolved(
                        $"'@type' names '{name}', which the local context does not hold."),
                    _ => WotTypeBinding.Ambiguous(
                        $"'@type' names '{name}', which is ambiguous ({byName.Count} matches) and " +
                        $"carries no '{TypeBindingRel}' link to settle it.")
                };
            }

            if (byLink is null)
            {
                return WotTypeBinding.Unresolved(
                    $"The '{TypeBindingRel}' link names '{link}', which the local context does " +
                    "not hold.");
            }

            if (byName.Count == 0)
            {
                // The identifier resolves and the name does not: a name that
                // resolves to nothing is a mistake in the name rather than a
                // shorthand for the identifier.
                return WotTypeBinding.Invalid(
                    $"'@type' names '{name}', which the local context does not hold, while the " +
                    $"'{TypeBindingRel}' link resolves. The two disagree.");
            }

            foreach (WotResolvedNode candidate in byName)
            {
                if (AreSameExpandedNodeId(candidate.NodeId, byLink.Value.NodeId))
                {
                    // Either the two agree, or the link settles an ambiguous
                    // name. Both bind to the identified type.
                    return Accept(byLink.Value, expected, link);
                }
            }

            return WotTypeBinding.Invalid(
                $"'@type' names '{name}' and the '{TypeBindingRel}' link names '{link}', which " +
                "resolve to different Nodes.");
        }

        /// <summary>
        /// Accepts a resolved type, or rejects it for the wrong NodeClass.
        /// </summary>
        private static WotTypeBinding Accept(
            WotResolvedNode node,
            WotExpectedNodeClass expected,
            string named)
        {
            if (expected != WotExpectedNodeClass.Any &&
                node.NodeClass != expected)
            {
                return WotTypeBinding.Invalid(
                    $"'{named}' resolves to a {node.NodeClass}, but the document projects a node " +
                    $"that requires a {expected}.");
            }

            return WotTypeBinding.Bound(node.NodeId);
        }

        /// <summary>
        /// Determines whether the projected root ObjectType derives from
        /// BaseEventType and therefore projects a UA EventType, annotated with
        /// <c>uav:eventType</c> (WoT Binding Section 5.2).
        /// </summary>
        /// <remarks>
        /// The chain is walked through the Nodes the NodeSet holds and stops at
        /// the first type this Binding knows: a type derived from a standard
        /// ConditionType projects an EventType whether or not the source
        /// carried the base NodeSet, because the ConditionTypes of Section 13.1
        /// all derive from <c>BaseEventType</c> by definition. A chain that
        /// leaves the NodeSet through an identifier this Binding does not know
        /// is not guessed at.
        /// </remarks>
        private static bool IsEventTypeRoot(UANode? root, UANodeSet nodeSet)
        {
            if (root is not UAObjectType)
            {
                return false;
            }
            Dictionary<string, UANode> index = BuildIndex(nodeSet);
            UANode? current = root;
            int guard = index.Count + 1;
            while (current is UAObjectType && guard-- > 0)
            {
                string? superType = FindSuperTypeId(current);
                if (superType is null)
                {
                    return false;
                }
                if (string.Equals(superType, WotVocabulary.BaseEventType, StringComparison.Ordinal) ||
                    WotVocabulary.TryGetConditionTypeName(superType, out _))
                {
                    return true;
                }
                if (!index.TryGetValue(superType, out current))
                {
                    return false;
                }
            }
            return false;
        }

        private static string? FindSuperTypeId(UANode node)
        {
            if (node.References is null)
            {
                return null;
            }
            foreach (Reference reference in node.References)
            {
                if (!reference.IsForward &&
                    reference.Value is not null &&
                    (string.Equals(reference.ReferenceType, "HasSubtype", StringComparison.Ordinal) ||
                        string.Equals(reference.ReferenceType, WotVocabulary.HasSubtype, StringComparison.Ordinal)))
                {
                    return reference.Value;
                }
            }
            return null;
        }

        /// <summary>
        /// Portable identity validation (WoT Binding Section 5.1.1): every
        /// NodeId-valued term (<c>uav:id</c>, each <c>uav:hasComponent</c> /
        /// <c>uav:componentOf</c> entry, <c>uav:mapToNodeId</c>,
        /// <c>uav:mapToType</c>, <c>uav:refId</c>, the
        /// <c>uav:browsePathAnchor</c> (Section 5.1.4), and a
        /// <c>?id=</c> href) shall be a portable ExpandedNodeId, never the
        /// session-local <c>ns=&lt;index&gt;</c> form. The exact <c>uav:nodeSet</c>
        /// envelope and <c>uav:nodes</c> projection subtrees are skipped so their
        /// own namespace indices - resolved through their own NamespaceUris table -
        /// are unaffected.
        /// </summary>
        private static void ValidatePortableIdentity(
            WotDocument document,
            List<WotDiagnostic> diagnostics)
        {
            ValidatePortableIdentity(
                document.RootElement, WotAnchorScope.None, false, diagnostics);
        }

        private static void ValidatePortableIdentity(
            JsonElement element,
            WotAnchorScope outer,
            bool inSelectClauses,
            List<WotDiagnostic> diagnostics)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    // Section 5.1.4: the scope a member resolves in is the one
                    // this object states, over the one it inherited.
                    WotAnchorScope scope = outer.Enter(element);
                    if (!inSelectClauses)
                    {
                        CheckBrowsePath(element, scope, diagnostics);
                    }
                    foreach (JsonProperty member in element.EnumerateObject())
                    {
                        if (WotDocument.IsSemanticBoundary(member.Name))
                        {
                            continue;
                        }
                        CheckPortableMember(member.Name, member.Value, diagnostics);

                        // A select clause's uav:browsePath is a path within an
                        // EventType's notification, not a path to a Node: it is
                        // relative by definition and anchored by the clause's own
                        // uav:typeDefinitionReference, so the Section 5.1.4
                        // starting-Node rule does not apply to it.
                        ValidatePortableIdentity(
                            member.Value,
                            scope,
                            inSelectClauses ||
                            string.Equals(
                                member.Name,
                                WotEventSelectClauses.Term,
                                StringComparison.Ordinal),
                            diagnostics);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        ValidatePortableIdentity(item, outer, inSelectClauses, diagnostics);
                    }
                    break;
            }
        }

        /// <summary>
        /// Section 5.1.4: a browse path resolves only where it is absolute or
        /// its scope states what it is relative to, and only where no element
        /// uses a numeric NamespaceIndex.
        /// </summary>
        /// <remarks>
        /// The anchor is the nearest enclosing <c>uav:browsePathAnchor</c>, or
        /// failing that the nearest enclosing <c>uav:id</c>, so a document that
        /// identifies the Node it describes anchors its own relative paths
        /// without repeating that identity as an anchor. The predicate is
        /// <see cref="WotPortableIdentity.IsResolvableBrowsePath"/>, which is
        /// what the published Section 5.1.4 vectors are run against, so a
        /// document and a vector cannot disagree about what resolves.
        /// </remarks>
        private static void CheckBrowsePath(
            JsonElement element,
            WotAnchorScope scope,
            List<WotDiagnostic> diagnostics)
        {
            if (!element.TryGetProperty("uav:browsePath", out JsonElement pathMember) ||
                pathMember.ValueKind != JsonValueKind.String)
            {
                return;
            }
            string? path = pathMember.GetString();
            if (WotPortableIdentity.IsResolvableBrowsePath(path, scope.IsAnchored))
            {
                return;
            }
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.NonPortableIdentity,
                $"The browse path '{path}' has no starting Node: a relative path " +
                "needs a uav:browsePathAnchor or an enclosing uav:id, and no element " +
                "may use a numeric NamespaceIndex (WoT Binding Section 5.1.4).",
                new WotLocation(reference: path)));
        }

        private static void CheckPortableMember(
            string name,
            JsonElement value,
            List<WotDiagnostic> diagnostics)
        {
            switch (name)
            {
                case "uav:id":
                case "uav:mapToNodeId":
                case "uav:mapToType":
                case "uav:browsePathAnchor":
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        CheckPortableValue(name, value.GetString(), diagnostics);
                    }
                    break;
                case "uav:hasComponent":
                case "uav:componentOf":
                    if (value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement entry in value.EnumerateArray())
                        {
                            if (entry.ValueKind == JsonValueKind.String)
                            {
                                CheckPortableValue(name + " entry", entry.GetString(), diagnostics);
                            }
                        }
                    }
                    break;
                case "uav:refId":
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        CheckPortableValue(name, value.GetString(), diagnostics);
                    }
                    break;
                case "href":
                    if (value.ValueKind == JsonValueKind.String &&
                        value.GetString() is { } href)
                    {
                        int marker = href.IndexOf("?id=", StringComparison.Ordinal);
                        if (marker >= 0)
                        {
                            CheckPortableValue(
                                "href ?id=", href[(marker + 4)..], diagnostics);
                        }
                    }
                    break;
            }
        }

        private static void CheckPortableValue(
            string term,
            string? value,
            List<WotDiagnostic> diagnostics)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }
            bool portable = WotPortableIdentity.IsPortableNodeId(value);
            if (!IsNodeId(value!) ||
                !ExpandedNodeId.TryParse(value!, out _) ||
                (value!.StartsWith("nsu=", StringComparison.Ordinal) && !portable))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ValidationError,
                    $"The NodeId-valued term {term} uses '{value}', which is not " +
                    "an ExpandedNodeId.",
                    new WotLocation(reference: value)));
                return;
            }

            // The same predicate the published Section 5.1.1 vectors are run
            // against, so a document and a vector cannot disagree about what is
            // portable. It refuses the session-local ns=<index> form and the
            // svr= prefix, and a value that names no identifier type at all.
            if (portable)
            {
                return;
            }
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.NonPortableIdentity,
                $"The portable identity term {term} uses " +
                (WotPortableIdentity.IsSessionLocalNodeId(value)
                    ? $"the session-local ns=<index> form ('{value}')"
                    : $"'{value}', which is not an ExpandedNodeId a persisted document " +
                        "may carry") +
                "; a persisted document shall use an ExpandedNodeId " +
                "(nsu=<NamespaceUri>;... or namespace-0 i=...) so it survives a " +
                "namespace-table reordering (WoT Binding Section 5.1.1).",
                new WotLocation(reference: value)));
        }

        private static void ValidateModelConceptNames(
            WotDocument document,
            List<WotDiagnostic> diagnostics)
        {
            ValidateModelConceptNames(
                document,
                document.RootElement,
                diagnostics);
        }

        private static void ValidateModelConceptNames(
            WotDocument document,
            JsonElement element,
            List<WotDiagnostic> diagnostics)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                ValidateReferenceTypeRelation(document, element, diagnostics);
                ValidateModelConceptMember(
                    document,
                    element,
                    "uav:mapToTypeName",
                    requiredDefinitiveMember: "uav:mapToType",
                    diagnostics);
                foreach (JsonProperty member in element.EnumerateObject())
                {
                    if (!WotDocument.IsSemanticBoundary(member.Name))
                    {
                        ValidateModelConceptNames(
                            document,
                            member.Value,
                            diagnostics);
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    ValidateModelConceptNames(document, item, diagnostics);
                }
            }
        }

        private static void ValidateModelConceptMember(
            WotDocument document,
            JsonElement element,
            string memberName,
            string? requiredDefinitiveMember,
            List<WotDiagnostic> diagnostics)
        {
            if (!element.TryGetProperty(memberName, out JsonElement member))
            {
                return;
            }
            string? value = member.ValueKind == JsonValueKind.String
                ? member.GetString()
                : null;
            if (value is null ||
                !TrySplitCompactModelName(
                    value,
                    out string prefix,
                    out _) ||
                !TryGetContextNamespace(document, prefix, out _, element))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ModelConceptUnresolved,
                    $"The {memberName} value '{value}' is not a compact model name " +
                    "whose non-numeric prefix is bound in @context.",
                    new WotLocation(reference: value)));
            }
            if (requiredDefinitiveMember is not null &&
                !element.TryGetProperty(requiredDefinitiveMember, out _))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ModelConceptUnresolved,
                    $"{memberName} requires {requiredDefinitiveMember}.",
                    new WotLocation(reference: value)));
            }
        }

        private static void ValidateReferenceTypeRelation(
            WotDocument document,
            JsonElement element,
            List<WotDiagnostic> diagnostics)
        {
            string? rel = GetElementString(element, "rel");
            if (rel is null)
            {
                return;
            }
            if (IsKnownBindingRelation(rel) ||
                rel.StartsWith("http:", StringComparison.Ordinal) ||
                rel.StartsWith("https:", StringComparison.Ordinal) ||
                rel.StartsWith("urn:", StringComparison.Ordinal))
            {
                return;
            }
            if (!TrySplitCompactModelName(
                    rel,
                    out string prefix,
                    out _) ||
                prefix == "tm")
            {
                return;
            }
            if (prefix == "uav")
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ModelConceptUnresolved,
                    $"The Binding relation '{rel}' is not defined.",
                    new WotLocation(reference: rel)));
                return;
            }
            if (IsExternalRelationPrefix(prefix) ||
                !IsModelConceptCandidate(document, element, prefix))
            {
                return;
            }
            if (!TryGetContextNamespace(document, prefix, out _, element))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ModelConceptUnresolved,
                    $"The ReferenceType relation '{rel}' uses a prefix that is " +
                    "not bound in @context.",
                    new WotLocation(reference: rel)));
            }
        }

        private static string DeriveModelUri(WotDocument document)
        {
            string? uavId = GetUavString(document, "id");
            if (uavId is not null)
            {
                const string marker = "nsu=";
                if (uavId.StartsWith(marker, StringComparison.Ordinal))
                {
                    int semicolon = uavId.IndexOf(';', marker.Length);
                    string ns = semicolon < 0
                        ? uavId[marker.Length..]
                        : uavId[marker.Length..semicolon];
                    if (ns.Length > 0)
                    {
                        return CoreUtils.UnescapeUri(ns.AsSpan());
                    }
                }
            }
            string? id = document.Id;
            if (!string.IsNullOrEmpty(id))
            {
                return id!;
            }
            return "urn:opcua:wot:synthesized";
        }

        /// <summary>
        /// Resolves the DataType a property affordance declares.
        /// </summary>
        /// <remarks>
        /// §9.1 gives the readable mapping one channel for a DataType, the
        /// DataSchema's json type, and that channel carries six types — so a
        /// LocalizedText and a String are the same thing by the time it is read
        /// back. §5.4 states the definitive DataType at property level, so it
        /// wins where it is present. It is resolved through
        /// <see cref="ToNodeSetNodeId"/> rather than taken verbatim because that
        /// is what registers its namespace in the table and rewrites the
        /// portable <c>nsu=</c> form into the <c>ns=&lt;index&gt;</c> form a
        /// NodeSet2 <c>DataType</c> attribute is allowed to carry.
        /// </remarks>
        private static string MapJsonSchemaToDataType(
            WotDocument document,
            JsonElement schema,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics,
            DataTypeDefinitionContext? dataTypes = null)
        {
            string? mapped = GetElementString(schema, "uav:mapToType") is { } definitive
                ? ToNodeSetNodeId(definitive, nodeSet, diagnostics)
                : null;

            // A DataSchema that names a DataType definition is bound to that
            // DataType. §6.11 exists so a Variable can carry a custom Structure
            // or Enumeration; reading only the json type here would give it the
            // built-in the definition was written to replace.
            string? defined = ResolveSchemaDataTypeDefinition(
                document, schema, nodeSet, diagnostics, dataTypes);
            string? annotated = GetElementString(schema, "uav:dataTypeId") is { } id
                ? ToNodeSetNodeId(id, nodeSet, diagnostics)
                : null;

            ReportDataTypeDisagreement(mapped, defined, annotated, schema, diagnostics);

            string? selected = mapped ??
                defined ??
                annotated ??
                (dataTypes?.SchemaIdentities.TryGetValue(schema, out string? inferred) == true
                    ? ToNodeSetNodeId(inferred, nodeSet, diagnostics)
                    : null);
            if (selected is not null)
            {
                return selected;
            }
            if (GetElementString(schema, "uav:dataTypeName") is { } dataTypeName &&
                ResolveDataTypeName(document, dataTypeName, nodeSet, diagnostics, schema, dataTypes) is { } named)
            {
                return named;
            }
            if (GetElementString(schema, "type") == "array" &&
                schema.TryGetProperty("items", out JsonElement element) &&
                element.ValueKind == JsonValueKind.Object)
            {
                return MapJsonSchemaToDataType(document, element, nodeSet, diagnostics, dataTypes);
            }
            return WotVocabulary.MapJsonTypeToDataType(
                GetElementString(schema, "type"),
                GetElementString(schema, "contentEncoding"),
                GetElementString(schema, "format"));
        }

        /// <summary>
        /// Reports two definitive DataType statements that name different
        /// types.
        /// </summary>
        /// <remarks>
        /// The three definitive channels are ordered - <c>uav:mapToType</c>
        /// outranks an authored DataType definition, which outranks
        /// <c>uav:dataTypeId</c>, which outranks what the json type implies -
        /// so a reader always has one answer. The order exists to settle which
        /// statement is read, not to excuse a document that makes two
        /// different ones: silently taking the higher-ranked type would leave a
        /// Variable typed against a statement the author also contradicted, and
        /// the contradiction would only surface where a value failed to
        /// encode. Inference is deliberately excluded, because being overridden
        /// is exactly what the lowest rank means.
        /// </remarks>
        private static void ReportDataTypeDisagreement(
            string? mapped,
            string? defined,
            string? annotated,
            JsonElement schema,
            List<WotDiagnostic> diagnostics)
        {
            if (Disagrees(mapped, defined))
            {
                Report("uav:mapToType", mapped!, "uav:dataTypeDefinition", defined!);
            }
            if (Disagrees(mapped, annotated))
            {
                Report("uav:mapToType", mapped!, "uav:dataTypeId", annotated!);
            }
            if (Disagrees(defined, annotated))
            {
                Report("uav:dataTypeDefinition", defined!, "uav:dataTypeId", annotated!);
            }

            static bool Disagrees(string? left, string? right)
            {
                return left is not null &&
                    right is not null &&
                    !string.Equals(left, right, StringComparison.Ordinal);
            }

            void Report(string leftTerm, string left, string rightTerm, string right)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ValidationError,
                    $"A DataSchema states {leftTerm} '{left}' and {rightTerm} " +
                    $"'{right}'. Both are definitive statements of the DataType, " +
                    "so a document that makes two different ones is contradicting " +
                    "itself rather than choosing; the ranking between them settles " +
                    "which is read, not which is true.",
                    new WotLocation(
                        reference: GetElementString(schema, "uav:browseName") ??
                            GetElementString(schema, "title"))));
            }
        }

        /// <summary>
        /// Resolves the DataType a DataSchema's <c>uav:dataTypeDefinition</c>
        /// denotes.
        /// </summary>
        /// <remarks>
        /// The definition may be stated here or anywhere else in the document
        /// and referred to by <c>@id</c>, so an <c>@id</c>-only reference is
        /// followed to wherever the complete definition lives. The identity is
        /// then whatever §6.11.1 gives that definition, which is the same
        /// answer the materializer reaches independently.
        /// </remarks>
        private static string? ResolveSchemaDataTypeDefinition(
            WotDocument document,
            JsonElement schema,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics,
            DataTypeDefinitionContext? dataTypes = null)
        {
            if (!schema.TryGetProperty("uav:dataTypeDefinition", out JsonElement declared) ||
                declared.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            if (GetElementString(declared, "@id") is { } graphIdentity &&
                dataTypes?.Identities.TryGetValue(graphIdentity, out string? resolved) == true)
            {
                return ToNodeSetNodeId(resolved, nodeSet, diagnostics);
            }
            if (!IsReferenceOnlyDefinition(declared))
            {
                return ResolveDataTypeIdentity(document, declared, nodeSet, diagnostics);
            }
            string? graphId = GetElementString(declared, "@id");
            if (graphId is null)
            {
                return null;
            }
            var ignored = new List<WotDiagnostic>();
            Dictionary<string, JsonElement> complete =
                CollectAllDataTypeDefinitions(document, ignored);
            if (!complete.TryGetValue(graphId, out JsonElement target))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"A DataSchema names the DataType definition '{graphId}', " +
                    "which the document never states completely.",
                    new WotLocation(reference: graphId)));
                return null;
            }
            return ResolveDataTypeIdentity(document, target, nodeSet, diagnostics);
        }

        private static uint MapAccessLevel(JsonElement schema)
        {
            bool readOnly = GetElementBool(schema, "readOnly");
            bool writeOnly = GetElementBool(schema, "writeOnly");
            uint access = 0;
            if (!writeOnly)
            {
                access |= AccessLevelCurrentRead;
            }
            if (!readOnly)
            {
                access |= AccessLevelCurrentWrite;
            }
            return access == 0 ? AccessLevelCurrentRead : access;
        }

        private readonly record struct WotParentPlacement(string ParentNodeId);
    }
}
