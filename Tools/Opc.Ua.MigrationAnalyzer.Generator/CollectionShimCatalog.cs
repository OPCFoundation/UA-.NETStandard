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

using System.Collections.Generic;

namespace Opc.Ua.MigrationAnalyzer.Generator
{
    /// <summary>
    /// Element-type override table for the 30 well-known <c>&lt;Type&gt;Collection</c>
    /// wrappers that 1.5.378 shipped under the <c>Opc.Ua</c> namespace. Each entry
    /// pins the **2.0** element type (which differs from the literal short name when
    /// the underlying type was renamed across the boundary, e.g. <c>DateTime →
    /// DateTimeUtc</c>, <c>Guid → Uuid</c>, <c>byte[] → ByteString</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generator consults this table first. For any short name not listed here
    /// (model-compiled <c>&lt;UserType&gt;Collection</c> patterns), the generator
    /// falls back to semantic lookup against the consumer's compilation.
    /// </para>
    /// <para>
    /// Catalog mirrors <c>Opc.Ua.MigrationAnalyzer.Helpers.SymbolExtensions.RemovedCollectionTypes</c>
    /// in the analyzer assembly. Kept in this assembly intentionally rather than
    /// linked across projects, because (a) the analyzer table also drives diagnostic
    /// messages and is read by the syntactic fallback, and (b) the generator only
    /// needs the override mapping, not the analyzer's full surface. Duplicating
    /// ~30 lines is preferable to introducing an extra assembly reference.
    /// </para>
    /// </remarks>
    internal static class CollectionShimCatalog
    {
        /// <summary>
        /// Maps the legacy short name (e.g. <c>"DateTimeCollection"</c>) to the
        /// fully-qualified 2.0 element type (e.g. <c>"global::Opc.Ua.DateTimeUtc"</c>).
        /// </summary>
        public static IReadOnlyDictionary<string, string> WellKnownOverrides { get; } =
            new Dictionary<string, string>(System.StringComparer.Ordinal)
            {
                ["BooleanCollection"] = "bool",
                ["SByteCollection"] = "sbyte",
                ["ByteCollection"] = "byte",
                ["Int16Collection"] = "short",
                ["UInt16Collection"] = "ushort",
                ["Int32Collection"] = "int",
                ["UInt32Collection"] = "uint",
                ["Int64Collection"] = "long",
                ["UInt64Collection"] = "ulong",
                ["FloatCollection"] = "float",
                ["DoubleCollection"] = "double",
                ["StringCollection"] = "string",
                // Renamed across the 1.5.378 → 2.0 boundary:
                ["DateTimeCollection"] = "global::Opc.Ua.DateTimeUtc",
                ["GuidCollection"] = "global::Opc.Ua.Uuid",
                ["ByteStringCollection"] = "global::Opc.Ua.ByteString",
                ["XmlElementCollection"] = "global::System.Xml.XmlElement",
                // Built-in opc types whose name didn't rename:
                ["NodeIdCollection"] = "global::Opc.Ua.NodeId",
                ["ExpandedNodeIdCollection"] = "global::Opc.Ua.ExpandedNodeId",
                ["QualifiedNameCollection"] = "global::Opc.Ua.QualifiedName",
                ["LocalizedTextCollection"] = "global::Opc.Ua.LocalizedText",
                ["StatusCodeCollection"] = "global::Opc.Ua.StatusCode",
                ["VariantCollection"] = "global::Opc.Ua.Variant",
                ["DiagnosticInfoCollection"] = "global::Opc.Ua.DiagnosticInfo",
                ["DataValueCollection"] = "global::Opc.Ua.DataValue",
                ["ExtensionObjectCollection"] = "global::Opc.Ua.ExtensionObject",
                ["ArgumentCollection"] = "global::Opc.Ua.Argument",
                ["ServerSecurityPolicyCollection"] = "global::Opc.Ua.ServerSecurityPolicy",
                ["TransportConfigurationCollection"] = "global::Opc.Ua.TransportConfiguration",
                ["ReverseConnectClientCollection"] = "global::Opc.Ua.ReverseConnectClient",
            };
    }
}
