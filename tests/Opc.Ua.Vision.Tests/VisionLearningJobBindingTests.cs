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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.Server;
using Opc.Ua.Vision.Server.Builders;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Tests the optional <c>LearningJob</c> binding on Vision inference pipelines.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class VisionLearningJobBindingTests
    {
        [Test]
        public async Task WithLearningJobCreatesTypedBrowsablePropertyWithValue()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            var learningJobNodeId = new NodeId("learning-job-1", 3);
            NodeId learningJobPropertyId = NodeId.Null;

            await fixture.Manager.ConfigureVisionAsync(context =>
            {
                context.Nodes.AddPipeline("Detector", p => p
                    .WithPipelineId("detector")
                    .WithLearningJob(learningJobNodeId));

                NodeState pipeline = FindChild(context.Root.Pipelines!, "Detector");
                learningJobPropertyId = FindChild(pipeline, BrowseNames.LearningJob).NodeId;
            }).ConfigureAwait(false);

            NodeState? registered = fixture.Manager.FindPredefinedNode<NodeState>(learningJobPropertyId);

            Assert.Multiple(() =>
            {
                Assert.That(registered, Is.Not.Null,
                    "the LearningJob property must be reachable by its own NodeId " +
                    "so a client can resolve and read it.");
                Assert.That(registered, Is.InstanceOf<BaseVariableState>());
            });

            var learningJobProperty = (BaseVariableState)registered!;
            Assert.Multiple(() =>
            {
                Assert.That(learningJobProperty.Value, Is.EqualTo(learningJobNodeId));
                Assert.That(learningJobProperty.ReferenceTypeId,
                    Is.EqualTo(global::Opc.Ua.ReferenceTypeIds.HasProperty),
                    "LearningJob is a property of the pipeline, not a component or external reference.");
                Assert.That(learningJobProperty.TypeDefinitionId,
                    Is.EqualTo(global::Opc.Ua.VariableTypeIds.PropertyType),
                    "clients filtering by PropertyType must not silently skip the generated child.");
            });
        }

        [Test]
        public async Task PipelineBuiltWithoutLearningJobLeavesOptionalChildAbsent()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            InferencePipelineState? pipeline = null;

            await fixture.Manager.ConfigureVisionAsync(context =>
            {
                context.Nodes.AddPipeline("Detector", p => p
                    .WithPipelineId("detector"));

                pipeline = (InferencePipelineState)FindChild(context.Root.Pipelines!, "Detector");
            }).ConfigureAwait(false);

            Assert.That(pipeline, Is.Not.Null);
            Assert.That(TryFindChild(pipeline!, BrowseNames.LearningJob, out NodeState? learningJob), Is.False,
                "LearningJob is optional and should be absent unless the host binds one.");
            Assert.That(learningJob, Is.Null);
        }

        [Test]
        public async Task PipelineClientReadsLearningJobId()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline();
            var learningJobNodeId = new NodeId("learning-job-1", 3);
            harness.AddValueChild(harness.PipelineNodeId, BrowseNames.LearningJob,
                new NodeId(3014u, 3), learningJobNodeId);

            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            VisionPipelineSnapshot snapshot = await pipeline.ReadAsync().ConfigureAwait(false);

            Assert.That(snapshot.LearningJobId, Is.EqualTo(learningJobNodeId));
        }

        private static NodeState FindChild(NodeState parent, string browseName)
        {
            Assert.That(TryFindChild(parent, browseName, out NodeState? match), Is.True,
                $"'{browseName}' must exist below '{parent.BrowseName.Name}'.");
            return match!;
        }

        private static bool TryFindChild(NodeState parent, string browseName, out NodeState? match)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(null!, children);
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (children[ii].BrowseName.Name == browseName)
                {
                    match = children[ii];
                    return true;
                }
            }
            match = null;
            return false;
        }
    }
}
