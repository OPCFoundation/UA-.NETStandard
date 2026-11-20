/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
