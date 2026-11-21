// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
