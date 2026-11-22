// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace Opc.Ua.MigrationAnalyzer.Generator
{
    /// <summary>
    /// Element-type override table for legacy <c>&lt;Type&gt;Collection</c> wrappers
    /// whose underlying element type **renamed** across the 1.5.378 → 2.0 boundary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generator consults this table first. For any short name not listed here,
    /// the generator falls back to semantic lookup against the consumer's
    /// compilation — which is sufficient for:
    /// </para>
    /// <list type="bullet">
    /// <item>Primitive-typed wrappers (<c>BooleanCollection</c>, <c>Int32Collection</c>,
    /// <c>StringCollection</c>, ...) — semantic lookup resolves to <c>System.Boolean</c>,
    /// <c>System.Int32</c>, etc. which behave identically to the C# aliases.</item>
    /// <item>Built-in OPC UA-typed wrappers whose element name didn't change
    /// (<c>NodeIdCollection</c>, <c>VariantCollection</c>, <c>DataValueCollection</c>,
    /// <c>EndpointDescriptionCollection</c>, <c>ReadValueIdCollection</c>,
    /// <c>BrowsePathCollection</c>, ...) — semantic lookup resolves uniquely to the
    /// 2.0 <c>Opc.Ua.</c>* type.</item>
    /// <item>Model-compiled <c>&lt;UserType&gt;Collection</c> patterns — the
    /// historical model-compiler convention emitted <c>Foo.BarCollection</c> for
    /// any complex type <c>Foo.Bar</c>; semantic lookup resolves <c>Bar</c> uniquely
    /// in the same namespace.</item>
    /// </list>
    /// <para>
    /// The catalog therefore only needs entries where semantic lookup would resolve
    /// to the **wrong** type (because of a 1.5.378 → 2.0 rename), or where it would
    /// resolve to multiple candidates (e.g. <c>XmlElement</c> matches both
    /// <c>System.Xml.XmlElement</c> and <c>Opc.Ua.XmlElement</c>).
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
                // Element type renamed across the 1.5.378 → 2.0 boundary:
                // 1.5.378's DateTimeCollection wrapped System.DateTime; 2.0 wraps Opc.Ua.DateTimeUtc.
                ["DateTimeCollection"] = "global::Opc.Ua.DateTimeUtc",
                // 1.5.378's GuidCollection wrapped System.Guid; 2.0 wraps Opc.Ua.Uuid.
                ["GuidCollection"] = "global::Opc.Ua.Uuid",
                // 1.5.378's ByteStringCollection wrapped byte[]; 2.0 wraps Opc.Ua.ByteString.
                ["ByteStringCollection"] = "global::Opc.Ua.ByteString",
                // 1.5.378's XmlElementCollection wrapped System.Xml.XmlElement. In 2.0 a
                // value-typed Opc.Ua.XmlElement exists too, so the bare short name
                // "XmlElement" resolves ambiguously. Pin the legacy interpretation
                // (System.Xml.XmlElement) so the shim drops in for 1.5.378 callers.
                ["XmlElementCollection"] = "global::System.Xml.XmlElement"
            };
    }
}
