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
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Schema
{
    /// <summary>
    /// Convenience extension methods over <see cref="ISchemaProvider"/> that
    /// make a data type "expose" its schema in a specific encoding.
    /// </summary>
    public static class SchemaProviderExtensions
    {
        /// <summary>
        /// Creates the XML schema (XSD) for the supplied data type.
        /// </summary>
        /// <param name="provider">The schema provider.</param>
        /// <param name="type">The data type to generate a schema for.</param>
        /// <param name="scope">The scope of the generated schema.</param>
        /// <returns>The generated schema.</returns>
        public static IUaSchema GetXmlSchema(
            this ISchemaProvider provider,
            UaTypeDescription type,
            UaSchemaScope scope = UaSchemaScope.Type)
        {
            return Guard(provider).CreateSchema(type, UaSchemaFormat.Xsd, scope);
        }

        /// <summary>
        /// Creates the OPC Binary schema (BSD) for the supplied data type.
        /// </summary>
        /// <param name="provider">The schema provider.</param>
        /// <param name="type">The data type to generate a schema for.</param>
        /// <param name="scope">The scope of the generated schema.</param>
        /// <returns>The generated schema.</returns>
        public static IUaSchema GetBinarySchema(
            this ISchemaProvider provider,
            UaTypeDescription type,
            UaSchemaScope scope = UaSchemaScope.Type)
        {
            return Guard(provider).CreateSchema(type, UaSchemaFormat.Bsd, scope);
        }

        /// <summary>
        /// Creates the JSON Schema for the supplied data type.
        /// </summary>
        /// <param name="provider">The schema provider.</param>
        /// <param name="type">The data type to generate a schema for.</param>
        /// <param name="verbose">Whether to use the verbose JSON encoding flavor.</param>
        /// <param name="scope">The scope of the generated schema.</param>
        /// <returns>The generated schema.</returns>
        public static IUaSchema GetJsonSchema(
            this ISchemaProvider provider,
            UaTypeDescription type,
            bool verbose = false,
            UaSchemaScope scope = UaSchemaScope.Type)
        {
            UaSchemaFormat format = verbose ? UaSchemaFormat.JsonVerbose : UaSchemaFormat.JsonCompact;
            return Guard(provider).CreateSchema(type, format, scope);
        }

        /// <summary>
        /// Resolves the supplied data type id and creates its JSON Schema.
        /// </summary>
        /// <param name="provider">The schema provider.</param>
        /// <param name="typeId">The data type id.</param>
        /// <param name="schema">The generated schema.</param>
        /// <param name="verbose">Whether to use the verbose JSON encoding flavor.</param>
        /// <param name="scope">The scope of the generated schema.</param>
        /// <returns><c>true</c> when the type was resolved and a schema produced.</returns>
        public static bool TryGetJsonSchema(
            this ISchemaProvider provider,
            ExpandedNodeId typeId,
            [NotNullWhen(true)] out IUaSchema? schema,
            bool verbose = false,
            UaSchemaScope scope = UaSchemaScope.Type)
        {
            UaSchemaFormat format = verbose ? UaSchemaFormat.JsonVerbose : UaSchemaFormat.JsonCompact;
            return Guard(provider).TryGetSchema(typeId, format, scope, out schema);
        }

        private static ISchemaProvider Guard(ISchemaProvider provider)
        {
            return provider ?? throw new ArgumentNullException(nameof(provider));
        }
    }
}
