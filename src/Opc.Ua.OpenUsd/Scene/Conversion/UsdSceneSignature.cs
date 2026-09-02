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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Scene.Conversion
{
    /// <summary>
    /// Computes the normative "composed-scene lossless" signature of a <see cref="UsdStage"/>, the
    /// equivalence oracle of draft OPC UA — OpenUSD Scene Materialization §7.4.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the C# port of the specification reference converter's <c>scene_signature</c> (see
    /// <c>metaverse-specs/extras/openusd-scene/tools/scene_common.py</c>). Two stages are §7.4
    /// round-trip equivalent when, and only when, their signatures are equal.
    /// </para>
    /// <para>
    /// The signature captures, for every prim (ordered by path): its path, type name, kind,
    /// specifier and documentation; the sorted set of attributes (name, type name, normalized value,
    /// variability, custom flag and connections); the sorted set of relationships (name and targets);
    /// the ordered list of composition arcs (kind, asset path, prim path, list position); and the
    /// ordered list of variant sets (name and selection). It deliberately excludes non-composed state
    /// such as prim activation, instanceable flags, applied API schemas, live-value flags, prim
    /// metadata and all stage-level metadata, matching the reference oracle.
    /// </para>
    /// <para>
    /// Numeric values are normalized to <see cref="double"/> and tuples and arrays are normalized to a
    /// single ordered-list form, so an <c>int</c>-versus-<c>float</c> or tuple-versus-array difference
    /// introduced by a lossless write does not perturb the signature.
    /// </para>
    /// </remarks>
    public static class UsdSceneSignature
    {
        private const char FieldSeparator = '\u001f';
        private const char UnitSeparator = '\u001e';

        /// <summary>
        /// Computes the full signature of a stage as a single string. Two stages with equal signature
        /// strings are §7.4 round-trip equivalent.
        /// </summary>
        /// <param name="stage">The stage to sign.</param>
        /// <returns>The signature string.</returns>
        public static string Compute(UsdStage stage)
        {
            return string.Join("\n", ComputeLines(stage));
        }

        /// <summary>
        /// Computes the signature of a stage as one canonical line per prim, ordered by prim path.
        /// This is the form used to produce a readable diff on a round-trip failure.
        /// </summary>
        /// <param name="stage">The stage to sign.</param>
        /// <returns>The per-prim signature lines.</returns>
        public static IReadOnlyList<string> ComputeLines(UsdStage stage)
        {
            if (stage == null)
            {
                throw new ArgumentNullException(nameof(stage));
            }

            var lines = new List<string>();
            foreach (UsdPrim prim in stage.AllPrims().OrderBy(p => p.Path, StringComparer.Ordinal))
            {
                lines.Add(SignPrim(prim));
            }
            return lines;
        }

        /// <summary>
        /// Finds the first prim-level difference between two stage signatures, if any.
        /// </summary>
        /// <param name="expected">The expected (reference) stage.</param>
        /// <param name="actual">The actual (for example re-parsed) stage.</param>
        /// <returns>A human-readable description of the first difference, or <c>null</c> when the two
        /// stages have identical signatures.</returns>
        public static string? FirstDifference(UsdStage expected, UsdStage actual)
        {
            IReadOnlyList<string> a = ComputeLines(expected);
            IReadOnlyList<string> b = ComputeLines(actual);
            int count = Math.Max(a.Count, b.Count);
            for (int i = 0; i < count; i++)
            {
                string ea = i < a.Count ? a[i] : "<missing prim>";
                string eb = i < b.Count ? b[i] : "<missing prim>";
                if (!string.Equals(ea, eb, StringComparison.Ordinal))
                {
                    return "signature differs at prim #" + i.ToString(CultureInfo.InvariantCulture)
                        + ":\n  expected: " + Readable(ea)
                        + "\n  actual:   " + Readable(eb);
                }
            }
            return null;
        }

        private static string Readable(string line)
        {
            return line.Replace(FieldSeparator, '|').Replace(UnitSeparator, '~');
        }

        private static string SignPrim(UsdPrim prim)
        {
            var attrs = prim.Attributes.Select(SignAttribute).ToList();
            attrs.Sort(StringComparer.Ordinal);

            var rels = prim.Relationships.Select(SignRelationship).ToList();
            rels.Sort(StringComparer.Ordinal);

            var arcs = prim.Composition.Select(SignArc).ToList();
            var variants = prim.VariantSets.Select(SignVariant).ToList();

            var sb = new StringBuilder();
            sb.Append(prim.Path);
            sb.Append(FieldSeparator).Append(prim.TypeName);
            sb.Append(FieldSeparator).Append(prim.Kind.ToString());
            sb.Append(FieldSeparator).Append(prim.Specifier.ToString());
            sb.Append(FieldSeparator).Append(prim.Documentation);
            sb.Append(FieldSeparator).Append("A(").Append(string.Join(UnitSeparator.ToString(), attrs)).Append(')');
            sb.Append(FieldSeparator).Append("R(").Append(string.Join(UnitSeparator.ToString(), rels)).Append(')');
            sb.Append(FieldSeparator).Append("C(").Append(string.Join(UnitSeparator.ToString(), arcs)).Append(')');
            sb.Append(FieldSeparator).Append("V(").Append(string.Join(UnitSeparator.ToString(), variants)).Append(')');
            return sb.ToString();
        }

        private static string SignAttribute(UsdAttribute attr)
        {
            var sb = new StringBuilder();
            sb.Append(attr.Name);
            sb.Append('\u0001').Append(attr.TypeName);
            sb.Append('\u0001').Append(NormalizeValue(attr.Value));
            sb.Append('\u0001').Append(attr.Variability.ToString());
            sb.Append('\u0001').Append(attr.Custom ? "1" : "0");
            sb.Append('\u0001').Append('[').Append(string.Join(",", attr.Connections)).Append(']');

            // Time samples are authored attribute data the §7.4 round trip must preserve, so they
            // are part of the signature — but only appended when present, leaving the signature of
            // a sample-less attribute byte-for-byte unchanged. The reference oracle does not model
            // time samples, so this deliberately makes the C# signature strictly stronger.
            if (attr.TimeSamples.Count > 0)
            {
                sb.Append('\u0001').Append("TS(");
                bool first = true;
                foreach (KeyValuePair<double, UsdValue> sample in attr.TimeSamples)
                {
                    if (!first)
                    {
                        sb.Append('\u0002');
                    }
                    first = false;
                    sb.Append(FormatNumber(sample.Key)).Append('=').Append(NormalizeValue(sample.Value));
                }
                sb.Append(')');
            }
            return sb.ToString();
        }

        private static string SignRelationship(UsdRelationship rel)
        {
            return rel.Name + "\u0001[" + string.Join(",", rel.Targets) + "]";
        }

        private static string SignArc(UsdCompositionArc arc)
        {
            return arc.ArcKind.ToString() + '\u0001' + arc.AssetPath + '\u0001' + arc.PrimPath
                + '\u0001' + arc.ListPosition.ToString();
        }

        /// <summary>
        /// Signs a variant set from its name and resolved selection only.
        /// </summary>
        /// <remarks>
        /// The §7.4 contract requires two composed-scene properties: the scene must be
        /// "variant-selection-equivalent" and the composition arc list must be preserved. Both are
        /// properties of the <em>composed</em> result — the selection determines which branch was
        /// composed in, and that selection is what must round-trip. The non-selected
        /// <c>&lt;Variant&gt;</c> branches captured in <see cref="UsdVariantSet.Variants"/> are
        /// un-composed authoring provenance (§5.6, Composition Provenance CU); they are deliberately
        /// out of scope for the composed-scene equivalence, matching the reference oracle's
        /// <c>scene_signature</c>, which signs only <c>(set_name, selection)</c>. Excluding them also
        /// keeps every signature that has no variant branches byte-for-byte unchanged.
        /// </remarks>
        private static string SignVariant(UsdVariantSet variant)
        {
            return variant.SetName + '\u0001' + variant.Selection;
        }

        /// <summary>
        /// Normalizes an attribute value to a canonical JSON-like string (port of <c>_norm</c> plus a
        /// deterministic serialization): numbers become doubles, tuples and arrays become a single
        /// ordered-list form, so lossless <c>int</c>/<c>float</c> and tuple/array differences collapse.
        /// </summary>
        private static string NormalizeValue(UsdValue value)
        {
            switch (value.Kind)
            {
                case UsdValueKind.Null:
                    return "null";
                case UsdValueKind.Boolean:
                    value.TryGetBoolean(out bool b);
                    return b ? "true" : "false";
                case UsdValueKind.String:
                case UsdValueKind.Token:
                case UsdValueKind.AssetPath:
                case UsdValueKind.PathReference:
                    value.TryGetText(out string s);
                    return Quote(s);
                case UsdValueKind.Double:
                    value.TryGetDouble(out double d);
                    return FormatNumber(d);
                case UsdValueKind.Integer:
                    value.TryGetInteger(out long l);
                    return FormatNumber(l);
                case UsdValueKind.Dictionary:
                    value.TryGetDictionary(out IReadOnlyDictionary<string, UsdValue> entries);
                    return "{" + string.Join(
                        ",",
                        entries
                            .OrderBy(static e => e.Key, StringComparer.Ordinal)
                            .Select(static e => Quote(e.Key) + ":" + NormalizeValue(e.Value)))
                        + "}";
                default:
                    value.TryGetItems(out ArrayOf<UsdValue> items);
                    return "[" + string.Join(
                        ",",
                        (items.ToArray() ?? []).Select(NormalizeValue)) + "]";
            }
        }

        private static string FormatNumber(double d)
        {
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
