// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene;
using Opc.Ua.OpenUsd.Server.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Connections carry their authored SdfPaths on the attribute node itself, in
    /// <c>ConnectionPaths</c> — the counterpart of a relationship's <c>TargetPaths</c>
    /// (draft OPC UA — OpenUSD Scene Materialization §5.4, §5.5, §7.4).
    /// </summary>
    /// <remarks>
    /// These cases pin the properties the browsable <c>UsdConnection</c> edges alone cannot
    /// provide: materialized attributes deliberately share the model's placeholder NodeId, so the
    /// edges are ambiguous and carry neither authored order nor a target outside the materialized
    /// subtree. They also cover the <b>bare</b> export overload — the one with no side channel —
    /// because that is where the loss used to occur.
    /// </remarks>
    [TestFixture]
    public class ConnectionPathsMemberTests
    {
        private static UsdStage BuildStage(params string[] connections)
        {
            var stage = new UsdStage("Cs");
            var root = new UsdPrim("Root", "Xform");
            var shader = new UsdPrim("Shader", "Shader");
            var a = new UsdAttribute("outputs:a", "token");
            var b = new UsdAttribute("outputs:b", "token");
            var sink = new UsdAttribute("inputs:sink", "token");
            foreach (string c in connections)
            {
                sink.Connections.Add(c);
            }
            shader.Attributes.Add(a);
            shader.Attributes.Add(b);
            shader.Attributes.Add(sink);
            root.AddChild(shader);
            stage.AddRootPrim(root);
            return stage;
        }

        private static UsdAttribute ExportedSink(UsdStage exported)
        {
            return exported.AllPrims()
                .SelectMany(p => p.Attributes)
                .Single(a => a.Name == "inputs:sink");
        }

        [Test]
        public void ConnectionPaths_IsAuthoredOnTheNode()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(
                BuildStage("/Root/Shader.outputs:a", "/Root/Shader.outputs:b"));

            UsdAttributeState node = ms.Result.AttributesByPath["/Root/Shader.inputs:sink"];
            Assert.That(node.ConnectionPaths, Is.Not.Null);
            ArrayOf<string> paths = node.ConnectionPaths!.Value;

            Assert.That(paths.ToArray(), Is.EqualTo(s_stringValues1));
        }

        [Test]
        public void BareOverload_RecoversAuthoredOrder()
        {
            // The bare overload has no side channel; before ConnectionPaths existed it could only
            // read the ambiguous edges and could not reproduce the authored sequence.
            MaterializedScene ms = MaterializationHarness.Materialize(
                BuildStage("/Root/Shader.outputs:b", "/Root/Shader.outputs:a"));

            UsdStage exported = ms.Context.ExportUsdStage(ms.Result.Stage);

            Assert.That(ExportedSink(exported).Connections, Is.EqualTo(s_stringValues2));
        }

        [Test]
        public void BareOverload_KeepsTargetOutsideTheMaterializedSubtree()
        {
            // No node exists for this path, so there is no browsable edge to rebuild it from.
            MaterializedScene ms = MaterializationHarness.Materialize(
                BuildStage("/Elsewhere/Other.outputs:surface"));

            UsdStage exported = ms.Context.ExportUsdStage(ms.Result.Stage);

            Assert.That(ExportedSink(exported).Connections,
                Is.EqualTo(s_stringValues3));
        }

        [Test]
        public void BareOverload_KeepsDuplicateTargets()
        {
            // Deduped as browsable edges, but the authored multiplicity must survive.
            MaterializedScene ms = MaterializationHarness.Materialize(
                BuildStage("/Root/Shader.outputs:a", "/Root/Shader.outputs:a"));

            UsdStage exported = ms.Context.ExportUsdStage(ms.Result.Stage);

            Assert.That(ExportedSink(exported).Connections, Has.Count.EqualTo(2));
        }

        [Test]
        public void AttributeWithoutConnections_ExportsNone()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(BuildStage());

            UsdStage exported = ms.Context.ExportUsdStage(ms.Result.Stage);

            Assert.That(ExportedSink(exported).Connections, Is.Empty);
        }

        private static readonly string[] s_stringValues1 = ["/Root/Shader.outputs:a", "/Root/Shader.outputs:b"];
        private static readonly string[] s_stringValues2 = ["/Root/Shader.outputs:b", "/Root/Shader.outputs:a"];
        private static readonly string[] s_stringValues3 = ["/Elsewhere/Other.outputs:surface"];
    }
}
