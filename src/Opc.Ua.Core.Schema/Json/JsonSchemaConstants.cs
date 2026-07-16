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
