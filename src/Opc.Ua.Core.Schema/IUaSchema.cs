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

using System.IO;

namespace Opc.Ua.Schema
{
    /// <summary>
    /// A generated schema document. Concrete implementations expose the
    /// underlying strongly-typed schema object model (for example an
    /// <see cref="System.Xml.Schema.XmlSchema"/> or a JSON Schema document)
    /// and can serialize the schema to text or a stream.
    /// </summary>
    public interface IUaSchema
    {
        /// <summary>
        /// The format (encoding) the schema describes.
        /// </summary>
        UaSchemaFormat Format { get; }

        /// <summary>
        /// The IANA media type of the serialized schema.
        /// </summary>
        string MediaType { get; }

        /// <summary>
        /// The target namespace (or document identifier) of the schema.
        /// </summary>
        string TargetNamespace { get; }

        /// <summary>
        /// Serializes the schema to the supplied stream.
        /// </summary>
        /// <param name="stream">The stream to write the schema to.</param>
        void WriteTo(Stream stream);

        /// <summary>
        /// Serializes the schema to the supplied text writer.
        /// </summary>
        /// <param name="writer">The text writer to write the schema to.</param>
        void WriteTo(TextWriter writer);

        /// <summary>
        /// Serializes the schema to a string.
        /// </summary>
        /// <returns>The serialized schema.</returns>
        string ToSchemaString();
    }
}
