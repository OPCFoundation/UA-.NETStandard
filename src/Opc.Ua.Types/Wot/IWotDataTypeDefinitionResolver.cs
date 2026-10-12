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
        ValueTask<ArrayOf<WotDataTypeDefinitionSource>> ResolveDataTypeDefinitionsAsync(
            string graphId,
            CancellationToken cancellationToken = default);
    }
}
