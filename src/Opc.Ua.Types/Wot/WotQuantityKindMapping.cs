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

namespace Opc.Ua.Wot
{
    /// <summary>
    /// A declaration-specific quantity-kind annotation with its evidence and optional source warning.
    /// </summary>
    public sealed class WotQuantityKindMapping
    {
        /// <summary>
        /// Creates a verified annotation or a warning that prevents quantity-kind enrichment.
        /// </summary>
        /// <param name="sourceUri">The source establishing the declaration's semantics.</param>
        /// <param name="quantityKindIri">The quantity-kind IRI, or null when no kind is established.</param>
        /// <param name="warning">An optional explanation of inconsistent source metadata.</param>
        public WotQuantityKindMapping(string sourceUri, string? quantityKindIri = null, string? warning = null)
        {
            SourceUri = sourceUri ?? throw new ArgumentNullException(nameof(sourceUri));
            QuantityKindIri = quantityKindIri;
            Warning = warning;
        }

        /// <summary>
        /// Gets the source establishing the declaration's semantics.
        /// </summary>
        public string SourceUri { get; }

        /// <summary>
        /// Gets the quantity-kind IRI, or null when no kind is established.
        /// </summary>
        public string? QuantityKindIri { get; }

        /// <summary>
        /// Gets the optional explanation of inconsistent source metadata.
        /// </summary>
        public string? Warning { get; }
    }
}
