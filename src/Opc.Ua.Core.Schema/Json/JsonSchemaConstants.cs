/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Text.Json.Nodes;

namespace Opc.Ua.Schema.Json
{
    /// <summary>
    /// Constants and small helpers for building OPC UA JSON Schema documents
    /// according to OPC UA Part 6 (JSON encoding, Annex C).
    /// </summary>
    internal static class JsonSchemaConstants
    {
        /// <summary>
        /// The JSON Schema dialect used for all generated documents.
        /// </summary>
        public const string Dialect = "https://json-schema.org/draft/2020-12/schema";

        /// <summary>
        /// The prefix used for the keys of the standard OPC UA built-in object
        /// types that are added to the document <c>$defs</c> section.
        /// </summary>
        public const string StandardDefPrefix = "Ua_";

        /// <summary>
        /// Returns a JSON Schema reference to a definition in the current
        /// document <c>$defs</c> section.
        /// </summary>
        /// <param name="defName">The name of the definition.</param>
        /// <returns>A <c>$ref</c> schema object.</returns>
        public static JsonObject Ref(string defName)
        {
            return new JsonObject { ["$ref"] = "#/$defs/" + defName };
        }
    }
}
