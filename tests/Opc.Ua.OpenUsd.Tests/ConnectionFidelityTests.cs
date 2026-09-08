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
using Opc.Ua.OpenUsd.Scene;
using Opc.Ua.OpenUsd.Server.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Attribute-connection fidelity across materialize→export (§5.4, §7.4):
    /// <list type="bullet">
    ///   <item>M-2 — the exported connection sequence equals the authored order, recovered from
    ///     the materialization result's side channel rather than from hash-ordered references.</item>
    ///   <item>M-5 — a <c>.connect</c> whose target lies outside the materialized subtree (and so
    ///     has no browsable edge) still survives the round trip, the connection counterpart of a
    ///     relationship's <c>TargetPaths</c>.</item>
    ///   <item>M-4 — an attribute co-authored with both a default value and a connection reports
    ///     both after export; neither is discarded.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class ConnectionFidelityTests
    {
        // ---- M-2: exported connection order equals authored order ----------------------

        [Test]
        public void Export_TwoConnections_PreservesAuthoredOrder()
        {
            // Author the two targets in reverse-alphabetical order so authored order (b, a) is
            // distinguishable from any incidental sort or hash order (which would give a, b).
            var stage = new UsdStage("S") { DefaultPrim = "Sink" };
            var src = new UsdPrim("Src", "Xform");
            src.Attributes.Add(new UsdAttribute("a", "double") { Value = UsdValue.From(1.0) });
            src.Attributes.Add(new UsdAttribute("b", "double") { Value = UsdValue.From(2.0) });
            var sink = new UsdPrim("Sink", "Xform");
            var input = new UsdAttribute("in", "double");
            input.Connections.Add("/Src.b");
            input.Connections.Add("/Src.a");
            sink.Attributes.Add(input);
            stage.AddRootPrim(src);
            stage.AddRootPrim(sink);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            // The result overload carries the recorded authored order (M-2 fix).
            UsdStage exported = ms.Context.ExportUsdStage(ms.Result);

            UsdAttribute exportedInput = AttributeOf(exported, "Sink", "in");
            Assert.That(exportedInput.Connections, Is.EqualTo(s_stringValues1),
                "Exported connections must be in the authored order, not reference/hash order.");
        }

        [Test]
        public void Materialize_RecordsAuthoredConnectionOrder_OnResult()
        {
            // The mechanism that makes M-2/M-5 deterministic: the authored connection paths are
            // snapshotted verbatim onto the materialization result, keyed by attribute node.
            var stage = new UsdStage("S") { DefaultPrim = "Sink" };
            var src = new UsdPrim("Src", "Xform");
            src.Attributes.Add(new UsdAttribute("a", "double") { Value = UsdValue.From(1.0) });
            src.Attributes.Add(new UsdAttribute("b", "double") { Value = UsdValue.From(2.0) });
            var sink = new UsdPrim("Sink", "Xform");
            var input = new UsdAttribute("in", "double");
            input.Connections.Add("/Src.b");
            input.Connections.Add("/Src.a");
            sink.Attributes.Add(input);
            stage.AddRootPrim(src);
            stage.AddRootPrim(sink);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            UsdAttributeState node = ms.Attr("/Sink.in");
            Assert.That(ms.Result.ConnectionsByNode.ContainsKey(node), Is.True,
                "A connected attribute must be recorded on the result.");
            Assert.That(ms.Result.ConnectionsByNode[node],
                Is.EqualTo(s_stringValues1),
                "The recorded order must be the authored order.");
        }

        [Test]
        public void Materialize_TwoConnectionsFromOneAttribute_DoesNotThrow()
        {
            // Regression guard: materialized attributes share the model's placeholder NodeId
            // (xUsdAttribute_, i=6023), so two connections authored on one attribute resolve to
            // the same target NodeId. The browsable-edge builder must dedupe by target NodeId
            // rather than throwing a duplicate-reference exception. The lossless authored order is
            // preserved separately on the result (asserted by the tests above).
            var stage = new UsdStage("S") { DefaultPrim = "Sink" };
            var src = new UsdPrim("Src", "Xform");
            src.Attributes.Add(new UsdAttribute("a", "double") { Value = UsdValue.From(1.0) });
            src.Attributes.Add(new UsdAttribute("b", "double") { Value = UsdValue.From(2.0) });
            var sink = new UsdPrim("Sink", "Xform");
            var input = new UsdAttribute("in", "double");
            input.Connections.Add("/Src.b");
            input.Connections.Add("/Src.a");
            sink.Attributes.Add(input);
            stage.AddRootPrim(src);
            stage.AddRootPrim(sink);

            MaterializedScene ms = null!;
            Assert.DoesNotThrow(
                () => ms = MaterializationHarness.Materialize(stage),
                "Two resolvable connections sharing a target NodeId must not throw.");
            Assert.That(ms.Result.ConnectionsByNode[ms.Attr("/Sink.in")],
                Is.EqualTo(s_stringValues1),
                "Both authored connections must still be recorded despite the deduped edge.");
        }

        [Test]
        public void Export_UnresolvableConnectionTarget_Survives()
        {
            // The target lies outside the materialized subtree, so no browsable UsdConnection edge
            // exists for it. The recorded authored path is the only way it can round-trip.
            var stage = new UsdStage("S") { DefaultPrim = "Sink" };
            var sink = new UsdPrim("Sink", "Xform");
            var input = new UsdAttribute("in", "double");
            input.Connections.Add("/Outside/Elsewhere.output");
            sink.Attributes.Add(input);
            stage.AddRootPrim(sink);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            UsdStage exported = ms.Context.ExportUsdStage(ms.Result);

            UsdAttribute exportedInput = AttributeOf(exported, "Sink", "in");
            Assert.That(exportedInput.Connections,
                Is.EqualTo(s_stringValues2),
                "An unresolvable .connect target must not be dropped (§8.4 shall-not-drop).");
        }

        [Test]
        public void Export_MixedResolvableAndUnresolvable_PreservesBothInOrder()
        {
            // One target resolves inside the subtree, one does not; both must appear, in the exact
            // authored order, proving the side channel — not the edges — drives the export.
            var stage = new UsdStage("S") { DefaultPrim = "Sink" };
            var src = new UsdPrim("Src", "Xform");
            src.Attributes.Add(new UsdAttribute("out", "double") { Value = UsdValue.From(1.0) });
            var sink = new UsdPrim("Sink", "Xform");
            var input = new UsdAttribute("in", "double");
            input.Connections.Add("/Missing.target");
            input.Connections.Add("/Src.out");
            sink.Attributes.Add(input);
            stage.AddRootPrim(src);
            stage.AddRootPrim(sink);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            UsdStage exported = ms.Context.ExportUsdStage(ms.Result);

            UsdAttribute exportedInput = AttributeOf(exported, "Sink", "in");
            Assert.That(exportedInput.Connections,
                Is.EqualTo(s_stringValues3),
                "Both the unresolvable and the resolvable target must survive, in authored order.");
        }

        // ---- M-4: a value co-authored with a connection is not discarded ---------------

        [Test]
        public void Export_AttributeWithValueAndConnection_ReportsBoth()
        {
            // USD permits an attribute to carry both a default value and a connection; the
            // materializer must keep the value while the connection is recorded, and the exporter
            // must report both independently (§5.4, §7.2).
            var stage = new UsdStage("S") { DefaultPrim = "Sink" };
            var src = new UsdPrim("Src", "Xform");
            src.Attributes.Add(new UsdAttribute("out", "double") { Value = UsdValue.From(3.0) });
            var sink = new UsdPrim("Sink", "Xform");
            var input = new UsdAttribute("in", "double") { Value = UsdValue.From(1.5) };
            input.Connections.Add("/Src.out");
            sink.Attributes.Add(input);
            stage.AddRootPrim(src);
            stage.AddRootPrim(sink);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            // The materialized node keeps its default value even though it is connected.
            Assert.That(ms.Attr("/Sink.in").BoxedValue(), Is.EqualTo(1.5),
                "The materializer must not discard the value when a connection is present.");

            UsdStage exported = ms.Context.ExportUsdStage(ms.Result);
            UsdAttribute exportedInput = AttributeOf(exported, "Sink", "in");
            UsdTestHelpers.AssertDouble(exportedInput.Value, 1.5);
            Assert.That(exportedInput.Value.IsNull, Is.False,
                "The exported attribute must still report its default value.");
            Assert.That(exportedInput.Connections, Is.EqualTo(s_stringValues4),
                "The exported attribute must also report its connection.");
        }

        private static UsdAttribute AttributeOf(UsdStage stage, string primName, string attributeName)
        {
            foreach (UsdPrim prim in stage.RootPrims)
            {
                if (!string.Equals(prim.Name, primName, StringComparison.Ordinal))
                {
                    continue;
                }
                foreach (UsdAttribute attribute in prim.Attributes)
                {
                    if (string.Equals(attribute.Name, attributeName, StringComparison.Ordinal))
                    {
                        return attribute;
                    }
                }
            }
            Assert.Fail($"Attribute {primName}.{attributeName} was not found in the exported stage.");
            return new UsdAttribute(attributeName, string.Empty);
        }

        private static readonly string[] s_stringValues1 = ["/Src.b", "/Src.a"];
        private static readonly string[] s_stringValues2 = ["/Outside/Elsewhere.output"];
        private static readonly string[] s_stringValues3 = ["/Missing.target", "/Src.out"];
        private static readonly string[] s_stringValues4 = ["/Src.out"];
    }
}
