/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.IO;
using System.Text;
using Opc.Ua.OpenUsdScene.Scene;

namespace Opc.Ua.OpenUsdScene.Conversion
{
    /// <summary>
    /// Serialises a composed <see cref="UsdStage"/> back to a single flattened <c>.usda</c>
    /// layer, per draft OPC UA — OpenUSD Scene Materialization §7.2 (address space → export).
    /// </summary>
    /// <remarks>
    /// This is the C# port of the specification reference converter's <c>write_usda</c>,
    /// <c>_usd_val</c> writer (see <c>metaverse-specs/extras/openusd-scene/tools/scene_common.py</c>).
    /// The emitted layer is provenance-aware: a prim's recorded composition arcs are re-emitted as
    /// <c>references</c>/<c>payload</c>/<c>inherits</c>/<c>specializes</c>, and an
    /// <see cref="UsdArcKindEnum.Instance"/> arc is re-expressed as <c>instanceable = true</c>. The
    /// output re-parses (with example overlays disabled) to a composed-scene-equivalent stage under
    /// the §7.4 round-trip contract.
    /// </remarks>
    public static class UsdaWriter
    {
        private static readonly HashSet<string> s_tupleGroupTypes = new HashSet<string>(
            new[] { "color3f", "float3", "double3", "int3" }, System.StringComparer.Ordinal);

        /// <summary>
        /// Serialises a stage to a <c>.usda</c> layer string.
        /// </summary>
        /// <param name="stage">The composed stage to serialise.</param>
        /// <returns>The <c>.usda</c> text, newline-terminated with <c>\n</c> line endings.</returns>
        public static string Write(UsdStage stage)
        {
            if (stage == null)
            {
                throw new System.ArgumentNullException(nameof(stage));
            }

            var lines = new List<string>
            {
                "#usda 1.0",
                "(",
                "    defaultPrim = \"" + stage.DefaultPrim + "\"",
                "    metersPerUnit = " + FormatDouble(stage.MetersPerUnit),
                "    upAxis = \"" + stage.UpAxis + "\"",
                ")",
                string.Empty,
            };

            foreach (UsdPrim root in stage.RootPrims)
            {
                EmitPrim(root, 0, lines);
            }

            var sb = new StringBuilder();
            foreach (string line in lines)
            {
                sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Serialises a stage and writes it to a file using <c>\n</c> line endings and UTF-8.
        /// </summary>
        /// <param name="stage">The composed stage to serialise.</param>
        /// <param name="path">The destination file path.</param>
        public static void WriteToFile(UsdStage stage, string path)
        {
            File.WriteAllText(path, Write(stage), new UTF8Encoding(false));
        }

        private static void EmitPrim(UsdPrim prim, int indent, List<string> lines)
        {
            string spec = prim.Specifier switch
            {
                UsdSpecifierEnum.Over => "over",
                UsdSpecifierEnum.Class => "class",
                _ => "def",
            };
            string typ = prim.TypeName.Length > 0 ? prim.TypeName + " " : string.Empty;
            string pad = new string(' ', 4 * indent);

            var arcLines = new List<string>();
            foreach (UsdCompositionArc arc in prim.Composition)
            {
                if (arc.ArcKind == UsdArcKindEnum.Instance)
                {
                    continue;
                }
                string? kw = arc.ArcKind switch
                {
                    UsdArcKindEnum.Reference => "references",
                    UsdArcKindEnum.Payload => "payload",
                    UsdArcKindEnum.Inherit => "inherits",
                    UsdArcKindEnum.Specialize => "specializes",
                    _ => null,
                };
                if (kw == null)
                {
                    continue;
                }
                string reference = arc.PrimPath.Length > 0
                    ? "@" + arc.AssetPath + "@<" + arc.PrimPath + ">"
                    : "@" + arc.AssetPath + "@";
                arcLines.Add(pad + "    " + ListPositionKeyword(arc.ListPosition) + " " + kw + " = " + reference);
            }

            bool instanceable = prim.Instanceable;
            foreach (UsdCompositionArc arc in prim.Composition)
            {
                if (arc.ArcKind == UsdArcKindEnum.Instance)
                {
                    instanceable = true;
                    break;
                }
            }

            var varLines = new List<string>();
            foreach (UsdVariantSet vs in prim.VariantSets)
            {
                if (vs.Selection.Length > 0)
                {
                    varLines.Add(pad + "    variants = { string " + vs.SetName + " = \"" + vs.Selection + "\" }");
                }
                varLines.Add(pad + "    prepend variantSets = \"" + vs.SetName + "\"");
            }

            bool hasDoc = prim.Documentation.Length > 0;
            bool hasMeta = prim.Kind != UsdPrimKindEnum.Unspecified || prim.ApiSchemas.Count > 0
                || arcLines.Count > 0 || instanceable || varLines.Count > 0 || hasDoc
                || prim.Metadata.Count > 0;

            lines.Add(pad + spec + " " + typ + "\"" + prim.Name + "\"" + (hasMeta ? " (" : string.Empty));
            if (hasMeta)
            {
                if (hasDoc)
                {
                    lines.Add(pad + "    doc = \"\"\"" + prim.Documentation + "\"\"\"");
                }
                if (prim.Kind != UsdPrimKindEnum.Unspecified)
                {
                    lines.Add(pad + "    kind = \"" + prim.Kind.ToString().ToLowerInvariant() + "\"");
                }
                if (prim.ApiSchemas.Count > 0)
                {
                    var names = new List<string>();
                    foreach (UsdApiSchema api in prim.ApiSchemas)
                    {
                        names.Add("\"" + api.SchemaName + "\"");
                    }
                    lines.Add(pad + "    prepend apiSchemas = [" + string.Join(", ", names) + "]");
                }
                lines.AddRange(arcLines);
                if (instanceable)
                {
                    lines.Add(pad + "    instanceable = true");
                }
                lines.AddRange(varLines);
                foreach (KeyValuePair<string, object?> entry in prim.Metadata)
                {
                    EmitMetaEntry(entry.Key, entry.Value, pad + "    ", lines, typed: false);
                }
                lines.Add(pad + ")");
            }
            lines.Add(pad + "{");
            EmitPrimBodyContent(prim, indent, lines);
            lines.Add(pad + "}");
        }

        /// <summary>
        /// Emits the body of a prim (attributes, relationships, variant-set branch blocks and child
        /// prims) at one indent level deeper than <paramref name="indent"/>. Reused for variant
        /// branch bodies so a branch round-trips with the same fidelity as a top-level prim (§5.6).
        /// </summary>
        private static void EmitPrimBodyContent(UsdPrim prim, int indent, List<string> lines)
        {
            string pad = new string(' ', 4 * indent);
            string body = pad + "    ";

            foreach (UsdAttribute attr in prim.Attributes)
            {
                string pre = (attr.Custom ? "custom " : string.Empty)
                    + (attr.Variability == UsdVariabilityEnum.Uniform ? "uniform " : string.Empty);
                string v = UsdVal(attr.Value, attr.TypeName);
                bool hasSamples = attr.TimeSamples.Count > 0;

                // A default value, time samples and connections may be co-authored on the same
                // attribute, and USD permits several connection targets, so emit the value (when
                // present), the '.timeSamples' block and every connection rather than only the
                // first (§5.4, §7.1). An attribute with none of these is still declared by name.
                if (v.Length > 0)
                {
                    lines.Add(body + pre + attr.TypeName + " " + attr.Name + " = " + v);
                }
                else if (attr.Connections.Count == 0 && !hasSamples)
                {
                    lines.Add(body + pre + attr.TypeName + " " + attr.Name);
                }

                if (hasSamples)
                {
                    EmitTimeSamples(attr, body, pre, lines);
                }

                if (attr.Connections.Count > 0)
                {
                    lines.Add(body + pre + attr.TypeName + " " + attr.Name
                        + ".connect = " + FormatConnectionTargets(attr.Connections));
                }
            }

            foreach (UsdRelationship rel in prim.Relationships)
            {
                if (rel.Targets.Count == 1)
                {
                    lines.Add(body + "rel " + rel.Name + " = <" + rel.Targets[0] + ">");
                }
                else if (rel.Targets.Count > 0)
                {
                    var targets = new List<string>();
                    foreach (string t in rel.Targets)
                    {
                        targets.Add("<" + t + ">");
                    }
                    lines.Add(body + "rel " + rel.Name + " = [" + string.Join(", ", targets) + "]");
                }
                else
                {
                    lines.Add(body + "rel " + rel.Name + " = []");
                }
            }

            // Re-emit every authored variant branch under its set so the full '<Variant>' structure
            // round-trips (§5.6). Selection-only sets (no captured branches) carry no block here;
            // their selection is already emitted in the prim metadata header.
            foreach (UsdVariantSet vs in prim.VariantSets)
            {
                if (vs.Variants.Count == 0)
                {
                    continue;
                }
                lines.Add(body + "variantSet \"" + vs.SetName + "\" = {");
                foreach (UsdPrim branch in vs.Variants)
                {
                    lines.Add(body + "    \"" + branch.Name + "\" {");
                    EmitPrimBodyContent(branch, indent + 2, lines);
                    lines.Add(body + "    }");
                }
                lines.Add(body + "}");
            }

            foreach (UsdPrim child in prim.Children)
            {
                EmitPrim(child, indent + 1, lines);
            }
        }

        private static string ListPositionKeyword(UsdListOpTypeEnum position)
        {
            return position switch
            {
                UsdListOpTypeEnum.Append => "append",
                UsdListOpTypeEnum.Delete => "delete",
                _ => "prepend",
            };
        }

        /// <summary>
        /// Renders an attribute's connection targets as authored <c>.usda</c> text. A single
        /// target is written as a bare path reference; several are written as a path-reference
        /// list, so all authored connections survive rather than only the first (§5.4).
        /// </summary>
        private static string FormatConnectionTargets(IList<string> connections)
        {
            if (connections.Count == 1)
            {
                return "<" + connections[0] + ">";
            }
            var targets = new List<string>(connections.Count);
            foreach (string target in connections)
            {
                targets.Add("<" + target + ">");
            }
            return "[" + string.Join(", ", targets) + "]";
        }

        /// <summary>
        /// Emits an attribute's authored time samples as a <c>.timeSamples = { … }</c> block
        /// (§7.1, §9). Each sample renders as <c>timeCode: value</c> using the same value renderer
        /// as the default so the two cannot drift, ordered by ascending time code (USD's composed
        /// sample order), so the block re-parses to the identical sample map.
        /// </summary>
        private static void EmitTimeSamples(UsdAttribute attr, string attrPad, string pre, List<string> lines)
        {
            lines.Add(attrPad + pre + attr.TypeName + " " + attr.Name + ".timeSamples = {");
            foreach (KeyValuePair<double, object?> sample in attr.TimeSamples)
            {
                lines.Add(attrPad + "    " + FormatTimeCode(sample.Key) + ": "
                    + UsdVal(sample.Value, attr.TypeName) + ",");
            }
            lines.Add(attrPad + "}");
        }

        /// <summary>
        /// Formats a USD time code: an integer-valued code without a decimal point (<c>0</c>,
        /// <c>24</c>, <c>-6</c>) as USD authors it, a fractional code in shortest round-trippable
        /// form (<c>0.5</c>, <c>-2.25</c>). Always invariant culture.
        /// </summary>
        private static string FormatTimeCode(double t)
        {
            if (!double.IsNaN(t) && !double.IsInfinity(t)
                && t == System.Math.Floor(t) && System.Math.Abs(t) < 1e15)
            {
                return ((long)t).ToString(CultureInfo.InvariantCulture);
            }
            return t.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Renders a scalar string attribute value. A USD asset path is authored with
        /// <c>@…@</c> delimiters (§6.2 <c>asset</c> → <c>UsdAssetPath</c>), symmetric with the
        /// reader that unwraps <c>@…@</c>; a recognised string-like scalar or a recognised array
        /// type is quoted; an unmapped (opaque) type carries its already-rendered value verbatim
        /// so a structured opaque value survives the round trip (§8.4).
        /// </summary>
        private static string RenderStringValue(string s, string typeName)
        {
            if (string.Equals(typeName, "asset", System.StringComparison.Ordinal))
            {
                return "@" + s + "@";
            }
            bool quoted =
                (typeName is "token" or "string"
                    || typeName.EndsWith("[]", System.StringComparison.Ordinal))
                && UsdValueTypeMap.IsKnown(typeName);
            return quoted ? "\"" + s + "\"" : s;
        }

        /// <summary>
        /// Renders a parsed USD value to its authored <c>.usda</c> text so an unmapped (opaque)
        /// value can be carried through materialization and re-emitted verbatim on export (§8.4).
        /// Reuses the same scalar/tuple/array rendering as <see cref="UsdVal"/> and
        /// <see cref="PyStr"/> so the carried text and a direct emission cannot drift. Returns
        /// <c>false</c> for a value this writer cannot render faithfully, so the caller can fail
        /// closed rather than publish a CLR type name.
        /// </summary>
        /// <param name="value">The parsed USD value.</param>
        /// <param name="text">The rendered <c>.usda</c> text when the method returns <c>true</c>.</param>
        /// <returns><c>true</c> when the value could be rendered.</returns>
        internal static bool TryRenderOpaqueValue(object? value, out string text)
        {
            text = string.Empty;
            switch (value)
            {
                case null:
                    return true;
                case bool b:
                    text = b ? "true" : "false";
                    return true;
                case string s:
                    text = s;
                    return true;
                case double:
                case float:
                case long:
                case int:
                    text = PyStr(value);
                    return true;
            }
            if (value is object?[] tuple)
            {
                return TryRenderOpaqueSequence(tuple, asTuple: true, out text);
            }
            if (value is List<object?> list)
            {
                return TryRenderOpaqueSequence(list, asTuple: false, out text);
            }
            return false;
        }

        private static bool TryRenderOpaqueSequence(
            IReadOnlyList<object?> items, bool asTuple, out string text)
        {
            text = string.Empty;
            var parts = new List<string>(items.Count);
            foreach (object? item in items)
            {
                if (!TryRenderOpaqueValue(item, out string part))
                {
                    return false;
                }
                // A string leaf inside a USD tuple/array is authored quoted.
                parts.Add(item is string ? "\"" + part + "\"" : part);
            }
            string joined = string.Join(", ", parts);
            text = asTuple ? "(" + joined + ")" : "[" + joined + "]";
            return true;
        }

        /// <summary>
        /// Emits one §6.3 metadata entry (a scalar/tuple/array field or a nested dictionary) into
        /// the prim's <c>( … )</c> block, symmetric with <c>UsdaReader.ApplyCustomPrimMeta</c>. A
        /// nested <see cref="IDictionary{TKey,TValue}"/> is emitted as a <c>{ … }</c> block whose
        /// entries are one indent deeper. A dictionary entry carries a USD value-type token
        /// (<paramref name="typed"/>); a top-level prim metadata field does not.
        /// </summary>
        private static void EmitMetaEntry(
            string key, object? value, string pad, List<string> lines, bool typed)
        {
            if (value is IDictionary<string, object?> dict)
            {
                lines.Add(pad + (typed ? "dictionary " : string.Empty) + key + " = {");
                foreach (KeyValuePair<string, object?> entry in dict)
                {
                    EmitMetaEntry(entry.Key, entry.Value, pad + "    ", lines, typed: true);
                }
                lines.Add(pad + "}");
                return;
            }
            bool isSequence = value is object?[] || value is List<object?>;
            string prefix = typed && !isSequence ? MetaTypeToken(value) + " " : string.Empty;
            lines.Add(pad + prefix + key + " = " + RenderMetaScalar(value));
        }

        /// <summary>
        /// Returns the USD dictionary value-type token for a scalar/dictionary metadata value. The
        /// token is only cosmetic on a round trip (the reader discards it), so an integer maps to
        /// <c>int</c> and a floating point value to <c>double</c>.
        /// </summary>
        private static string MetaTypeToken(object? value)
        {
            switch (value)
            {
                case bool _:
                    return "bool";
                case long _:
                case int _:
                    return "int";
                case double _:
                case float _:
                    return "double";
                case IDictionary<string, object?> _:
                    return "dictionary";
                default:
                    return "string";
            }
        }

        /// <summary>
        /// Renders a §6.3 metadata scalar (or a tuple/array value) to its authored text. A string is
        /// double-quoted (USD's string form), a bool uses <c>true</c>/<c>false</c>, numbers use the
        /// same invariant formatting as attribute values, and a tuple/array falls back to
        /// <see cref="PyStr"/>.
        /// </summary>
        private static string RenderMetaScalar(object? value)
        {
            switch (value)
            {
                case null:
                    return "\"\"";
                case string s:
                    return "\"" + s + "\"";
                case bool b:
                    return b ? "true" : "false";
                case double d:
                    return FormatDouble(d);
                case float f:
                    return FormatDouble(f);
                case long l:
                    return l.ToString(CultureInfo.InvariantCulture);
                case int i:
                    return i.ToString(CultureInfo.InvariantCulture);
            }
            return PyStr(value);
        }

        /// <summary>
        /// Renders an attribute value as authored <c>.usda</c> text (port of <c>_usd_val</c>).
        /// </summary>
        private static string UsdVal(object? value, string typeName)
        {
            switch (value)
            {
                case null:
                    return string.Empty;
                case string s:
                    return RenderStringValue(s, typeName);
                case bool b:
                    return b ? "true" : "false";
            }

            // Shape contract: whether a value is authored as a USD array "[...]" or as a USD tuple
            // "(...)" is decided by the ATTRIBUTE TYPE, never by the CLR container that carries the
            // value. An array-typed attribute (TypeName ending in "[]") always emits "[...]"; a
            // fixed-size math scalar (double3, color3f, matrix4d, ...) emits a single parenthesised
            // tuple "(...)" through PyStr below. Keying on the type name — rather than on
            // "value is List<object?>" — keeps this contract intact no matter which container the
            // coercion layer hands back: UsdValueCoercion.Decoerce returns object?[] for both a
            // flat array and an array-of-tuples, while the reader returns List<object?>; both must
            // round-trip to the same "[...]" text here (regression guard for the H-1 defect where
            // every exported array fell through to PyStr and was corrupted into a "(...)" tuple).
            if (typeName.EndsWith("[]", System.StringComparison.Ordinal)
                && TryGetSequence(value, out IReadOnlyList<object?> items))
            {
                string baseType = typeName.Substring(0, typeName.Length - 2);
                return RenderArray(items, baseType);
            }

            return PyStr(value);
        }

        /// <summary>
        /// Exposes a value as a sequence when it is one of the two containers the document model
        /// and coercion layer use for a USD array: <c>object?[]</c> (returned by
        /// <see cref="UsdValueCoercion.Decoerce"/>) or <c>List&lt;object?&gt;</c> (returned by the
        /// reader). Anything else is not a sequence and is rendered as a scalar/tuple by the caller.
        /// </summary>
        private static bool TryGetSequence(object? value, out IReadOnlyList<object?> items)
        {
            switch (value)
            {
                case object?[] array:
                    items = array;
                    return true;
                case List<object?> list:
                    items = list;
                    return true;
            }
            items = System.Array.Empty<object?>();
            return false;
        }

        /// <summary>
        /// Renders an array-typed value as <c>[…]</c>. A tuple-group base type
        /// (<c>color3f</c>/<c>float3</c>/<c>double3</c>/<c>int3</c>) handed back as a flat run of
        /// scalar components is regrouped into per-tuple rows so a <c>color3f[]</c> still emits
        /// <c>[(r, g, b)]</c>; when the elements are already grouped rows (each element is itself a
        /// tuple) they render one parenthesised tuple per element instead. Every string element of
        /// an <c>asset[]</c> is authored with <c>@…@</c> delimiters (§6.2); any other string element
        /// uses USD's double-quote form, never PyStr's single quotes.
        /// </summary>
        private static string RenderArray(IReadOnlyList<object?> items, string baseType)
        {
            bool assetArray = string.Equals(baseType, "asset", System.StringComparison.Ordinal);

            if (s_tupleGroupTypes.Contains(baseType)
                && items.Count > 0
                && items.Count % 3 == 0
                && !AnyElementIsSequence(items))
            {
                var groups = new List<string>(items.Count / 3);
                for (int i = 0; i < items.Count; i += 3)
                {
                    var g = new List<string>(3);
                    for (int j = i; j < i + 3; j++)
                    {
                        g.Add(PyStr(items[j]));
                    }
                    groups.Add("(" + string.Join(", ", g) + ")");
                }
                return "[" + string.Join(", ", groups) + "]";
            }

            var elems = new List<string>(items.Count);
            foreach (object? x in items)
            {
                elems.Add(RenderArrayElement(x, assetArray));
            }
            return "[" + string.Join(", ", elems) + "]";
        }

        /// <summary>
        /// Renders one element of a USD array: an <c>asset[]</c> element with <c>@…@</c> delimiters,
        /// any other string element double-quoted (USD's string/token form), and a grouped tuple row
        /// or scalar through <see cref="PyStr"/> (which parenthesises a tuple).
        /// </summary>
        private static string RenderArrayElement(object? x, bool assetArray)
        {
            if (x is string s)
            {
                return assetArray ? "@" + s + "@" : "\"" + s + "\"";
            }
            return PyStr(x);
        }

        private static bool AnyElementIsSequence(IReadOnlyList<object?> items)
        {
            foreach (object? x in items)
            {
                if (x is object?[] || x is List<object?>)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Renders a scalar or tuple element with Python <c>str()</c> semantics so the output
        /// re-parses identically (integers plain, floats keep a decimal point, tuples parenthesised).
        /// </summary>
        private static string PyStr(object? x)
        {
            switch (x)
            {
                case null:
                    return "None";
                case bool b:
                    return b ? "True" : "False";
                case string s:
                    return "'" + s + "'";
                case double d:
                    return FormatDouble(d);
                case float f:
                    return FormatDouble(f);
                case long l:
                    return l.ToString(CultureInfo.InvariantCulture);
                case int i:
                    return i.ToString(CultureInfo.InvariantCulture);
            }

            if (x is object?[] tuple)
            {
                var parts = new List<string>();
                foreach (object? e in tuple)
                {
                    parts.Add(PyStr(e));
                }
                return "(" + string.Join(", ", parts) + ")";
            }
            if (x is List<object?> generic)
            {
                var parts = new List<string>();
                foreach (object? e in generic)
                {
                    parts.Add(PyStr(e));
                }
                return "[" + string.Join(", ", parts) + "]";
            }
            return System.Convert.ToString(x, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        /// <summary>
        /// Formats a double with Python <c>str(float)</c> semantics: shortest round-trippable form,
        /// but always with a trailing <c>.0</c> for integer-valued floats.
        /// </summary>
        private static string FormatDouble(double d)
        {
            string s = d.ToString("R", CultureInfo.InvariantCulture);
            if (!double.IsNaN(d) && !double.IsInfinity(d)
                && !ContainsChar(s, '.')
                && !ContainsChar(s, 'e')
                && !ContainsChar(s, 'E'))
            {
                s += ".0";
            }
            return s;
        }

        // Ordinal single-character containment without String.Contains(char, StringComparison),
        // which is unavailable on the down-level library targets (net472/net48).
        private static bool ContainsChar(string value, char c)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == c)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
