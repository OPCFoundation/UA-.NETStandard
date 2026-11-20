/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// A DataType definition together with the document owning its lexical context.
    /// </summary>
    public sealed class WotDataTypeDefinitionSource
    {
        /// <summary>
        /// Initializes a borrowed definition whose owning document remains alive during conversion.
        /// </summary>
        /// <param name="document">The document retaining the definition's original context and base.</param>
        /// <param name="definition">The complete definition element from <paramref name="document"/>.</param>
        public WotDataTypeDefinitionSource(WotDocument document, JsonElement definition)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            if (definition.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("A DataType definition must be an object.", nameof(definition));
            }
            Definition = definition;
        }

        /// <summary>
        /// Gets the owning document; ownership is not transferred to the converter.
        /// </summary>
        public WotDocument Document { get; }

        /// <summary>
        /// Gets the complete definition in its owning document.
        /// </summary>
        public JsonElement Definition { get; }

        /// <summary>
        /// Gets whether this definition is emitted by another activation in the same publication.
        /// False keeps resolution-only definitions with the consuming projection.
        /// </summary>
        public bool ProjectedSeparately { get; init; }
    }

    /// <summary>
    /// Supplies already-captured declaration inputs on a Thing resolver without acquiring another document.
    /// </summary>
    public interface IWotCapturedDataTypeDefinitions
    {
        /// <summary>
        /// Gets complete definitions whose documents remain alive for the entire conversion.
        /// These inputs retain their owning context and do not become activation roots.
        /// </summary>
        ArrayOf<WotDataTypeDefinitionSource> DataTypeDefinitions { get; }
    }

    /// <summary>
    /// Supplies complete DataType definitions already held by a part of the conversion context.
    /// </summary>
    /// <remarks>
    /// Implement this optional capability alongside <see cref="IWotNodeResolver"/> and pass that provider
    /// to <see cref="WotNodeSetConverter.ToNodeSetResultAsync(WotDocument, WotNodeSetConverterOptions,
    /// IWotThingResolver, WotResolutionContext, IWotNodeResolver, CancellationToken)"/>.
    /// <see cref="WotDocumentNodeResolver"/> supplies it for sibling documents and
    /// <see cref="WotCompositeNodeResolver"/> retains first-resolving-provider precedence.
    /// This capability does not retrieve Thing documents or transfer ownership of returned documents.
    /// An empty result is unresolved; multiple matches remain ambiguous rather than being merged.
    /// </remarks>
    public interface IWotDataTypeDefinitionResolver
    {
        /// <summary>
        /// Resolves a DataType definition's JSON-LD graph identity.
        /// </summary>
        /// <param name="graphId">The graph ID.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        ValueTask<ArrayOf<WotDataTypeDefinitionSource>> ResolveDataTypeDefinitionsAsync(
            string graphId,
            CancellationToken cancellationToken = default);
    }
}
