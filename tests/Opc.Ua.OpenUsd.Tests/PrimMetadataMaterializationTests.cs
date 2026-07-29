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
using Opc.Ua.OpenUsdScene.Scene;
using Opc.Ua.OpenUsdScene.Server;

namespace Opc.Ua.OpenUsdScene.Tests
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
            prim.Metadata["author"] = "Ada";
            prim.Metadata["visible"] = true;
            prim.Metadata["count"] = 7;
            prim.Metadata["huge"] = 9_000_000_000L;
            prim.Metadata["scale"] = 2.5;
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            UsdStage exported = ms.Context.ExportUsdStage(ms.Result);

            IDictionary<string, object?> md = PrimOf(exported, "P").Metadata;
            Assert.Multiple(() =>
            {
                Assert.That(md["author"], Is.EqualTo("Ada"));
                Assert.That(md["visible"], Is.True);
                Assert.That(md["count"], Is.EqualTo(7));
                Assert.That(md["huge"], Is.EqualTo(9_000_000_000L));
                Assert.That(md["scale"], Is.EqualTo(2.5));
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
            prim.Metadata["count"] = 7;
            prim.Metadata["scale"] = 2.5;
            prim.Metadata["visible"] = true;
            prim.Metadata["author"] = "Ada";
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            Assert.Multiple(() =>
            {
                PropertyState count = MetaProperty(ms, "/P", "count");
                Assert.That(count.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Int32));
                Assert.That(count.Value.AsBoxedObject(), Is.EqualTo(7));

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
            prim.Metadata["order"] = new int[] { 3, 1, 2 };
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            // The materialized value is a typed one-dimensional Int32 array, not text.
            PropertyState order = MetaProperty(ms, "/P", "order");
            Assert.That(order.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Int32));
            Assert.That(order.ValueRank, Is.EqualTo(ValueRanks.OneDimension));

            UsdStage exported = ms.Context.ExportUsdStage(ms.Result);
            IDictionary<string, object?> md = PrimOf(exported, "P").Metadata;
            Assert.That(md["order"], Is.EqualTo(new[] { 3, 1, 2 }),
                "The array must round-trip element-wise, preserving order.");
        }

        // ---- Nested metadata maps to nested folders (§6.3) -----------------------------

        [Test]
        public void Metadata_NestedDictionary_RoundTrips()
        {
            var stage = new UsdStage("S") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            var customData = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["author"] = "Ada",
                ["revision"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["major"] = 1,
                    ["minor"] = 2
                }
            };
            prim.Metadata["customData"] = customData;
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
            IDictionary<string, object?> md = PrimOf(exported, "P").Metadata;

            Assert.That(md["customData"], Is.InstanceOf<IDictionary<string, object?>>());
            var exportedCustom = (IDictionary<string, object?>)md["customData"]!;
            Assert.That(exportedCustom["author"], Is.EqualTo("Ada"));

            Assert.That(exportedCustom["revision"], Is.InstanceOf<IDictionary<string, object?>>());
            var revision = (IDictionary<string, object?>)exportedCustom["revision"]!;
            Assert.Multiple(() =>
            {
                Assert.That(revision["major"], Is.EqualTo(1));
                Assert.That(revision["minor"], Is.EqualTo(2));
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
    }
}
