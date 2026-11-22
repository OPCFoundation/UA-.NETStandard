// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Schema
{
    /// <summary>
    /// The schema format (encoding) to generate for a data type.
    /// </summary>
    public enum UaSchemaFormat
    {
        /// <summary>
        /// XML schema (XSD) for the XML data encoding.
        /// </summary>
        Xsd,

        /// <summary>
        /// OPC Binary schema (BSD) for the binary data encoding.
        /// </summary>
        Bsd,

        /// <summary>
        /// JSON Schema for the compact (reversible) JSON data encoding.
        /// </summary>
        JsonCompact,

        /// <summary>
        /// JSON Schema for the verbose JSON data encoding.
        /// </summary>
        JsonVerbose
    }

    /// <summary>
    /// The scope of a generated schema document.
    /// </summary>
    public enum UaSchemaScope
    {
        /// <summary>
        /// A schema document for a single data type and the closure of the
        /// types it depends on.
        /// </summary>
        Type,

        /// <summary>
        /// A schema document (dictionary) for all data types in a namespace.
        /// </summary>
        Namespace
    }
}
