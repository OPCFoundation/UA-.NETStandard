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
using System.Linq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.OpenUsd.Scene;
using Opc.Ua.OpenUsd.Server.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Custom prim metadata materialization and export (§6.3). Each authored metadata entry is
    /// materialized as a Property carrying its value in its own DataType — not stringified — so it
    /// round-trips through the address space with its type intact, and a nested dictionary is
    /// materialized as a nested <c>Metadata/</c> folder that recovers as a nested dictionary.
    /// </summary>
    [TestFixture]
    public class PrimMetadataMaterializationTests
    {
        // ---- Typed scalar metadata round-trips (§6.3) ----------------------------------

        [Test]
        public void Metadata_ScalarTypes_RoundTrip()
        {
            var stage = new UsdStage("S") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.Metadata["author"] = UsdValue.FromString("Ada");
            prim.Metadata["visible"] = UsdValue.From(true);
            prim.Metadata["count"] = UsdValue.From(7L);
            prim.Metadata["huge"] = UsdValue.From(9_000_000_000L);
            prim.Metadata["scale"] = UsdValue.From(2.5);
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            UsdStage exported = ms.Context.ExportUsdStage(ms.Result);

            IDictionary<string, UsdValue> md = PrimOf(exported, "P").Metadata;
            Assert.Multiple(() =>
            {
                UsdTestHelpers.AssertString(md["author"], "Ada");
                UsdTestHelpers.AssertBoolean(md["visible"], true);
                UsdTestHelpers.AssertInteger(md["count"], 7L);
                UsdTestHelpers.AssertInteger(md["huge"], 9_000_000_000L);
                UsdTestHelpers.AssertDouble(md["scale"], 2.5);
            });
        }

        [Test]
        public void Metadata_IsMaterializedTyped_NotStringified()
        {
            // The defect this pins: metadata used to be stringified, losing its type. Each leaf
            // must now carry the exact DataType and a value in that type, so a client reads the
            // authored kind rather than an opaque string.
            var stage = new UsdStage("S") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.Metadata["count"] = UsdValue.From(7L);
            prim.Metadata["scale"] = UsdValue.From(2.5);
            prim.Metadata["visible"] = UsdValue.From(true);
            prim.Metadata["author"] = UsdValue.FromString("Ada");
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            Assert.Multiple(() =>
            {
                PropertyState count = MetaProperty(ms, "/P", "count");
                Assert.That(count.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Int64));
                Assert.That(count.Value.AsBoxedObject(), Is.EqualTo(7L));

                PropertyState scale = MetaProperty(ms, "/P", "scale");
                Assert.That(scale.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Double));
                Assert.That(scale.Value.AsBoxedObject(), Is.EqualTo(2.5));

                PropertyState visible = MetaProperty(ms, "/P", "visible");
                Assert.That(visible.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Boolean));
                Assert.That(visible.Value.AsBoxedObject(), Is.True);

                PropertyState author = MetaProperty(ms, "/P", "author");
                Assert.That(author.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.String));
                Assert.That(author.Value.AsBoxedObject(), Is.EqualTo("Ada"));
            });
        }

        [Test]
        public void Metadata_IntArray_RoundTrips_AsTypedSequence()
        {
            var stage = new UsdStage("S") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.Metadata["order"] = UsdTestHelpers.IntegerArray(3L, 1L, 2L);
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            // The materialized value is a typed one-dimensional Int64 array, not text.
            PropertyState order = MetaProperty(ms, "/P", "order");
            Assert.That(order.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Int64));
            Assert.That(order.ValueRank, Is.EqualTo(ValueRanks.OneDimension));

            UsdStage exported = ms.Context.ExportUsdStage(ms.Result);
            IDictionary<string, UsdValue> md = PrimOf(exported, "P").Metadata;
            Assert.That(md["order"].TryGetArray(out ArrayOf<UsdValue> values), Is.True);
            Assert.That(values.ToArray()!.Select(v => v.TryGetInteger(out long integer) ? integer : 0L).ToArray(),
                Is.EqualTo(s_longValues1),
                "The array must round-trip element-wise, preserving order.");
        }

        // ---- Nested metadata maps to nested folders (§6.3) -----------------------------

        [Test]
        public void Metadata_NestedDictionary_RoundTrips()
        {
            var stage = new UsdStage("S") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            var revisionData = new Dictionary<string, UsdValue>(StringComparer.Ordinal)
            {
                ["major"] = UsdValue.From(1L),
                ["minor"] = UsdValue.From(2L)
            };
            var customData = new Dictionary<string, UsdValue>(StringComparer.Ordinal)
            {
                ["author"] = UsdValue.FromString("Ada"),
                ["revision"] = UsdValue.FromDictionary(revisionData)
            };
            prim.Metadata["customData"] = UsdValue.FromDictionary(customData);
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            // A nested dictionary is materialized as a nested Metadata folder (a FolderState child),
            // so the structure is browsable as authored.
            NodeState? metadataFolder = ms.Prim("/P").Metadata;
            Assert.That(metadataFolder, Is.Not.Null);
            List<FolderState> subFolders =
                MaterializationHarness.ChildrenOfType<FolderState>(ms.Context, metadataFolder!);
            Assert.That(subFolders, Has.Count.EqualTo(1),
                "The nested dictionary must materialize as one nested Metadata sub-folder.");

            UsdStage exported = ms.Context.ExportUsdStage(ms.Result);
            IDictionary<string, UsdValue> md = PrimOf(exported, "P").Metadata;

            Assert.That(md["customData"].TryGetDictionary(out IReadOnlyDictionary<string, UsdValue> exportedCustom),
                Is.True);
            UsdTestHelpers.AssertString(exportedCustom["author"], "Ada");

            Assert.That(exportedCustom["revision"].TryGetDictionary(out IReadOnlyDictionary<string, UsdValue> revision),
                Is.True);
            Assert.Multiple(() =>
            {
                UsdTestHelpers.AssertInteger(revision["major"], 1L);
                UsdTestHelpers.AssertInteger(revision["minor"], 2L);
            });
        }

        private static UsdPrim PrimOf(UsdStage stage, string name)
        {
            foreach (UsdPrim prim in stage.RootPrims)
            {
                if (string.Equals(prim.Name, name, StringComparison.Ordinal))
                {
                    return prim;
                }
            }
            Assert.Fail($"Prim {name} was not found in the exported stage.");
            return new UsdPrim(name);
        }

        private static PropertyState MetaProperty(MaterializedScene ms, string primPath, string key)
        {
            NodeState? folder = ms.Prim(primPath).Metadata;
            Assert.That(folder, Is.Not.Null, $"Prim {primPath} has no Metadata folder.");
            foreach (PropertyState property in
                MaterializationHarness.ChildrenOfType<PropertyState>(ms.Context, folder!))
            {
                if (string.Equals(property.BrowseName.Name, key, StringComparison.Ordinal))
                {
                    return property;
                }
            }
            Assert.Fail($"Metadata property {key} not found on {primPath}.");
            return null!;
        }

        private static readonly long[] s_longValues1 = [3L, 1L, 2L];
    }
}
