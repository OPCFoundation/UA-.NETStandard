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

namespace Opc.Ua.SchemaRegistry
{
    /// <summary>
    /// A schema document's syntax, native representation, selection and fingerprint rules.
    /// Implementations perform no network resolution or registry mutation.
    /// </summary>
    public interface ISchemaFormatProvider
    {
        /// <summary>
        /// Gets the canonical advertised format identifier.
        /// </summary>
        string Format { get; }

        /// <summary>
        /// Gets the schema document's media type, not the data-message media type.
        /// </summary>
        string ContentType { get; }

        /// <summary>
        /// Gets the algorithm name associated with computed SchemaIds.
        /// </summary>
        string SchemaIdAlgorithm { get; }

        /// <summary>
        /// Validates and projects document bytes without rewriting the caller's stored bytes.
        /// </summary>
        SchemaContentDataType Parse(ReadOnlySpan<byte> document);

        /// <summary>
        /// Serializes supported native content without losing values or extensions.
        /// </summary>
        ByteString Serialize(SchemaContentDataType content);

        /// <summary>
        /// Computes the format-defined SchemaId of the supplied document bytes.
        /// </summary>
        ByteString ComputeSchemaId(ReadOnlySpan<byte> document);

        /// <summary>
        /// Selects an explicit format-owned object without fetching a reference.
        /// </summary>
        IEncodeable Select(SchemaContentDataType content, string selector);
    }
}
