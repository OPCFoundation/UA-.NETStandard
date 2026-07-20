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
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.XRegistry
{
    /// <summary>
    /// Maps a registry resource document (and its format) to its content-derived identity — the
    /// fingerprint that makes a resource addressable by an Opaque NodeId and de-duplicable across
    /// registries. A concrete registry provides an implementation (for example the PubSub Schema
    /// Registry fingerprints schema documents via the pluggable SchemaId providers).
    /// </summary>
    public interface IResourceContentIdProvider
    {
        /// <summary>
        /// Computes the raw content-derived identity bytes for a resource document of a given format.
        /// </summary>
        /// <param name="format">The resource format (for example <c>avro</c>).</param>
        /// <param name="document">The resource document bytes.</param>
        /// <returns>The raw content-id bytes.</returns>
        ByteString ComputeContentId(string format, ReadOnlySpan<byte> document);

        /// <summary>
        /// Gets the algorithm name that identifies the (canonicalization, hash) used for a format,
        /// or <c>null</c> when the format is not supported.
        /// </summary>
        /// <param name="format">The resource format.</param>
        /// <returns>The algorithm name, or <c>null</c>.</returns>
        string? GetAlgorithm(string format);
    }
}
