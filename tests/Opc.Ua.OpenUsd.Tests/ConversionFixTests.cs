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
using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Regression tests for the conversion-layer conformance fixes:
    /// <list type="bullet">
    ///   <item>C2 — an unrecognised (opaque) structured value is rendered faithfully in USD
    ///     syntax rather than published as a CLR type name, and fails closed when it cannot be
    ///     rendered (§8.4).</item>
    ///   <item>H3 — <c>matrix4d</c> and every nested-tuple fixed-size type is flattened to its
    ///     leaves before the arity check, so it is no longer silently dropped.</item>
    ///   <item>M — an <c>asset</c> value is emitted with <c>@…@</c> delimiters, not as a quoted
    ///     string.</item>
    ///   <item>H4 — the writer emits a co-authored default value together with every connection
    ///     target, not just the first connection (§5.4).</item>
    ///   <item>A <c>uint64</c> above <see cref="long.MaxValue"/> survives the export round trip as
    ///     its invariant decimal text instead of wrapping into a negative integer.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class ConversionFixTests
    {
        private static bool Coerce(string typeName, UsdValue value, out Variant result)
        {
            UsdValueTypeMapping mapping = UsdValueTypeMap.Map(typeName, null);
            uint components = UsdValueTypeMap.ComponentCount(typeName);
            return UsdValueCoercion.TryCoerce(value, mapping, components, out result);
        }

        // ---- C2: opaque structured values render faithfully, never a CLR type name -----

        [Test]
        public void OpaqueTuple_RendersUsdSyntax_NotClrTypeName()
        {
            // color4f is not in the value-type table, so it is carried opaquely. A tuple must be
            // rendered as "(...)" rather than the literal "System.Object[]".
            bool ok = Coerce("color4f", UsdTestHelpers.NumberTuple(0.1, 0.2, 0.3, 1.0), out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TryGetValue(out string rendered), Is.True);
            Assert.That(rendered, Is.EqualTo("(0.1, 0.2, 0.3, 1.0)"));
            Assert.That(rendered, Does.Not.Contain("System."));
        }

        [Test]
        public void OpaqueArray_RendersUsdSyntax_NotClrTypeName()
        {
            // A [...] array is modelled as List<object?>; it must render as "[...]" rather than
            // the literal "System.Collections.Generic.List`1[System.Object]".
            bool ok = Coerce("mvtype", UsdTestHelpers.IntegerArray(1L, 2L, 3L), out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TryGetValue(out string rendered), Is.True);
            Assert.That(rendered, Is.EqualTo("[1, 2, 3]"));
            Assert.That(rendered, Does.Not.Contain("System."));
        }

        [Test]
        public void OpaqueBoolean_RendersItsUsdSpelling()
        {
            // A bool carried opaquely must author USD's "true"/"false", never a CLR spelling.
            bool ok = Coerce("mvtype", UsdValue.From(true), out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TryGetValue(out string rendered), Is.True);
            Assert.That(rendered, Is.EqualTo("true"));
        }

        [Test]
        public void OpaqueNestedTuple_RendersRecursively()
        {
            // matrix2d authored as two nested 2-tuples must render every level.
            bool ok = Coerce(
                "matrix2d",
                UsdTestHelpers.Tuple(
                    UsdTestHelpers.IntegerTuple(1L, 0L),
                    UsdTestHelpers.IntegerTuple(0L, 1L)),
                out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TryGetValue(out string rendered), Is.True);
            Assert.That(rendered, Is.EqualTo("((1, 0), (0, 1))"));
        }

        [Test]
        public void OpaqueStringLeaf_IsQuotedInsideStructure()
        {
            bool ok = Coerce("mvtype", UsdTestHelpers.StringArray("a", "b"), out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TryGetValue(out string rendered), Is.True);
            Assert.That(rendered, Is.EqualTo("[\"a\", \"b\"]"));
        }

        [Test]
        public void OpaqueValue_FailsClosed_WhenLeafCannotBeRendered()
        {
            // A leaf the writer cannot render faithfully must leave the value unresolved rather
            // than publish a plausible-but-wrong string (fail closed).
            bool ok = Coerce(
                "mvtype",
                UsdTestHelpers.Array(
                    UsdValue.From(0.1),
                    UsdValue.FromDictionary(new Dictionary<string, UsdValue>(StringComparer.Ordinal))),
                out Variant v);

            Assert.That(ok, Is.False);
            Assert.That(v.TryGetValue(out string _), Is.False);
        }

        // ---- H3: matrix4d (and nested-tuple fixed types) flatten before the arity check ----

        [Test]
        public void Matrix4d_NestedTuples_AreFlattenedAndHonoured()
        {
            UsdValue nested = UsdTestHelpers.Tuple(
                UsdTestHelpers.NumberTuple(1.0, 0.0, 0.0, 0.0),
                UsdTestHelpers.NumberTuple(0.0, 1.0, 0.0, 0.0),
                UsdTestHelpers.NumberTuple(0.0, 0.0, 1.0, 0.0),
                UsdTestHelpers.NumberTuple(0.0, 0.0, 0.0, 1.0));

            bool ok = Coerce("matrix4d", nested, out Variant v);

            Assert.That(ok, Is.True, "matrix4d authored as four nested 4-tuples must not be dropped");
            Assert.That(v.TryGetValue(out ArrayOf<double> arr), Is.True);
            double[]? flat = arr.ToArray();
            Assert.That(flat, Has.Length.EqualTo(16));
            Assert.That(flat![0], Is.EqualTo(1.0));
            Assert.That(flat[5], Is.EqualTo(1.0));
            Assert.That(flat[10], Is.EqualTo(1.0));
            Assert.That(flat[15], Is.EqualTo(1.0));
            Assert.That(flat[1], Is.Zero);
        }

        [Test]
        public void Matrix4d_AlreadyFlat_StillHonoured()
        {
            UsdValue flatAuthored = UsdTestHelpers.NumberTuple(
                1.0, 0.0, 0.0, 0.0,
                0.0, 1.0, 0.0, 0.0,
                0.0, 0.0, 1.0, 0.0,
                0.0, 0.0, 0.0, 1.0);

            bool ok = Coerce("matrix4d", flatAuthored, out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TryGetValue(out ArrayOf<double> arr), Is.True);
            Assert.That(arr.ToArray(), Has.Length.EqualTo(16));
        }

        [Test]
        public void Matrix4dArray_NestedTuples_AreFlattenedPerRow()
        {
            UsdValue identity = UsdTestHelpers.Tuple(
                UsdTestHelpers.NumberTuple(1.0, 0.0, 0.0, 0.0),
                UsdTestHelpers.NumberTuple(0.0, 1.0, 0.0, 0.0),
                UsdTestHelpers.NumberTuple(0.0, 0.0, 1.0, 0.0),
                UsdTestHelpers.NumberTuple(0.0, 0.0, 0.0, 1.0));
            UsdValue value = UsdTestHelpers.Array(identity, identity);

            bool ok = Coerce("matrix4d[]", value, out Variant v);

            Assert.That(ok, Is.True, "matrix4d[] rows authored as nested tuples must not be dropped");
            Assert.That(v.TryGetValue(out MatrixOf<double> m), Is.True);
            Assert.That(m.Dimensions, Is.EqualTo(s_intValues1));
        }

        [Test]
        public void Color3fArray_StaysGrouped_NotOverFlattened()
        {
            // The over-flatten guard: color3f[] is a sequence of 3-tuples. Flattening applies to
            // the element shape only, so the outer array must keep two rows of three, not collapse
            // to one flat run.
            UsdValue value = UsdTestHelpers.Array(
                UsdTestHelpers.NumberTuple(1.0, 2.0, 3.0),
                UsdTestHelpers.NumberTuple(4.0, 5.0, 6.0));

            bool ok = Coerce("color3f[]", value, out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TryGetValue(out MatrixOf<float> m), Is.True);
            Assert.That(m.Dimensions, Is.EqualTo(s_intValues2));
        }

        [Test]
        public void WrongArityFixedType_StillFailsClosed_AfterFlatten()
        {
            // Flattening must not paper over a genuinely wrong arity: float3 with two components
            // still cannot be honoured.
            bool ok = Coerce("float3", UsdTestHelpers.NumberTuple(1.0, 2.0), out Variant v);

            Assert.That(ok, Is.False);
            Assert.That(v.TryGetValue(out ArrayOf<float> _), Is.False);
        }

        // ---- M: asset values are emitted with @...@ delimiters -------------------------

        [Test]
        public void AssetValue_IsEmittedWithAtDelimiters_NotQuoted()
        {
            var stage = new UsdStage("Assets") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.Attributes.Add(new UsdAttribute("inputs:file", "asset") { Value = UsdValue.FromString("./pump.usda") });
            stage.AddRootPrim(prim);

            string usda = UsdaWriter.Write(stage);

            Assert.That(usda, Does.Contain("@./pump.usda@"));
            Assert.That(usda, Does.Not.Contain("\"./pump.usda\""));
        }

        [Test]
        public void AssetArray_EmitsEachElementWithAtDelimiters()
        {
            var stage = new UsdStage("Assets") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.Attributes.Add(
                new UsdAttribute("inputs:files", "asset[]")
                {
                    Value = UsdTestHelpers.AssetArray("./a.usda", "./b.usda"),
                });
            stage.AddRootPrim(prim);

            string usda = UsdaWriter.Write(stage);

            Assert.That(usda, Does.Contain("[@./a.usda@, @./b.usda@]"));
        }

        [Test]
        public void OpaqueCarriedValue_IsEmittedVerbatim_NotReQuoted()
        {
            // On export an opaque value arrives as a string on an opaque-typed attribute; the
            // writer must emit it verbatim so the structured text survives the round trip.
            var stage = new UsdStage("Opaque") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.Attributes.Add(new UsdAttribute("extent", "color4f") { Value = UsdValue.FromString("(0.1, 0.2, 0.3, 1.0)") });
            stage.AddRootPrim(prim);

            string usda = UsdaWriter.Write(stage);

            Assert.That(usda, Does.Contain("color4f extent = (0.1, 0.2, 0.3, 1.0)"));
            Assert.That(usda, Does.Not.Contain("\"(0.1, 0.2, 0.3, 1.0)\""));
        }

        // ---- H4: writer emits every connection and the co-authored value --------------

        [Test]
        public void MultipleConnections_AreAllEmitted_WithCoAuthoredValue()
        {
            var stage = new UsdStage("Conn") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            var attr = new UsdAttribute("inputs:surface", "token") { Value = UsdValue.FromString("fallback") };
            attr.Connections.Add("/P/A.outputs:surface");
            attr.Connections.Add("/P/B.outputs:surface");
            prim.Attributes.Add(attr);
            stage.AddRootPrim(prim);

            string usda = UsdaWriter.Write(stage);

            // The co-authored default value is emitted...
            Assert.That(usda, Does.Contain("token inputs:surface = \"fallback\""));
            // ...together with every connection target, not only the first.
            Assert.That(
                usda,
                Does.Contain(".connect = [</P/A.outputs:surface>, </P/B.outputs:surface>]"));
        }

        [Test]
        public void SingleConnection_IsEmittedAsBarePathReference()
        {
            var stage = new UsdStage("Conn") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            var attr = new UsdAttribute("inputs:surface", "token");
            attr.Connections.Add("/P/A.outputs:surface");
            prim.Attributes.Add(attr);
            stage.AddRootPrim(prim);

            string usda = UsdaWriter.Write(stage);

            Assert.That(usda, Does.Contain(".connect = </P/A.outputs:surface>"));
            Assert.That(usda, Does.Not.Contain("[</P/A.outputs:surface>]"));
        }

        // ---- A uint64 above long.MaxValue is preserved, never wrapped to a negative integer ----

        [Test]
        public void UInt64_WithinInt64Max_StaysIntegral()
        {
            UsdTestHelpers.AssertInteger(UsdValueCoercion.Decoerce(Variant.From(42UL)), 42L);
        }

        [Test]
        public void UInt64_AboveInt64Max_IsPreservedAsInvariantText()
        {
            UsdValue decoerced = UsdValueCoercion.Decoerce(Variant.From(ulong.MaxValue));

            // The unconditional cast this replaces authored "-1" for ulong.MaxValue.
            UsdTestHelpers.AssertToken(decoerced, "18446744073709551615");
        }

        [Test]
        public void UInt64_AboveInt64Max_RoundTripsBackToTheSameValue()
        {
            UsdValue decoerced = UsdValueCoercion.Decoerce(Variant.From(ulong.MaxValue));

            bool ok = Coerce("uint64", decoerced, out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TryGetValue(out ulong recovered), Is.True);
            Assert.That(recovered, Is.EqualTo(ulong.MaxValue));
        }

        [Test]
        public void UInt64Array_AboveInt64Max_IsPreservedElementwise()
        {
            UsdValue decoerced = UsdValueCoercion.Decoerce(
                Variant.From((ArrayOf<ulong>)new[] { 1UL, ulong.MaxValue }));

            Assert.That(decoerced.TryGetArray(out ArrayOf<UsdValue> items), Is.True);
            Assert.That(items.Count, Is.EqualTo(2));
            UsdTestHelpers.AssertInteger(items[0], 1L);
            UsdTestHelpers.AssertToken(items[1], "18446744073709551615");
        }

        [Test]
        public void UInt64_AboveInt64Max_ReparsesFromItsAuthoredLiteral()
        {
            // The authored literal must neither overflow the reader's integral parse nor lose its
            // digits to a double, so the coercion layer recovers the exact value.
            UsdValue parsed = UsdaReader.ParseValue("18446744073709551615");

            bool ok = Coerce("uint64", parsed, out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TryGetValue(out ulong recovered), Is.True);
            Assert.That(recovered, Is.EqualTo(ulong.MaxValue));
        }

        private static readonly int[] s_intValues1 = [2, 16];
        private static readonly int[] s_intValues2 = [2, 3];
    }
}
