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

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Opc.Ua.Schema.Json
{
    /// <summary>
    /// A JSON Schema (draft 2020-12) document generated for an OPC UA data type
    /// or namespace. The underlying object model is exposed through
    /// <see cref="Root"/> and is built with <see cref="System.Text.Json.Nodes"/>
    /// so that no reflection is required to construct or serialize the schema.
    /// </summary>
    public sealed class JsonSchemaDocument : IUaSchema
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="JsonSchemaDocument"/> class.
        /// </summary>
        /// <param name="format">The JSON schema format flavor.</param>
        /// <param name="targetNamespace">The document namespace or identifier.</param>
        /// <param name="root">The root JSON Schema object.</param>
        /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
        public JsonSchemaDocument(
            UaSchemaFormat format,
            string targetNamespace,
            JsonObject root)
        {
            Format = format;
            TargetNamespace = targetNamespace ?? throw new ArgumentNullException(nameof(targetNamespace));
            Root = root ?? throw new ArgumentNullException(nameof(root));
        }

        /// <inheritdoc/>
        public UaSchemaFormat Format { get; }

        /// <inheritdoc/>
        public string MediaType => "application/schema+json";

        /// <inheritdoc/>
        public string TargetNamespace { get; }

        /// <summary>
        /// The root JSON Schema object model.
        /// </summary>
        public JsonObject Root { get; }

        /// <inheritdoc/>
        public void WriteTo(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            Root.WriteTo(writer);
            writer.Flush();
        }

        /// <inheritdoc/>
        public void WriteTo(TextWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            writer.Write(ToSchemaString());
        }

        /// <inheritdoc/>
        public string ToSchemaString()
        {
            return Root.ToJsonString(s_options);
        }

        private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };
    }
}
