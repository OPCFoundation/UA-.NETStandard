/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Represents a cached schema entry.
    /// </summary>
    /// <param name="SchemaId">The raw schema identifier.</param>
    /// <param name="Schema">The schema bytes.</param>
    /// <param name="Format">The schema format.</param>
    public readonly record struct SchemaCacheEntry(ByteString SchemaId, ByteString Schema, string Format)
    {
        /// <summary>
        /// Gets the SchemaIdAlg name (for example SHA-256/JCS) derived from the schema
        /// <see cref="Format"/> through the registered fingerprint provider, or <c>null</c>
        /// when the format has no registered provider.
        /// </summary>
        public string? SchemaIdAlg => SchemaIdProviders.AlgorithmFor(Format);
    }
}
