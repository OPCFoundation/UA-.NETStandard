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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.AI.Inference;
using Opc.Ua.AI.Server;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.AI;
using ObjectIds = Opc.Ua.ObjectIds;

namespace Opc.Ua.AI.Tests
{
    /// <summary>
    /// Verifies the shape of the address space itself.
    /// </summary>
    /// <remarks>
    /// These are the faults that produce a Server which builds, starts, answers
    /// calls, and is wrong. None of them fails anything: a duplicated entry point
    /// looks like a populated Server to whoever browses the right one, and a
    /// NodeId collision looks like nothing at all until a type node has been
    /// overwritten by an instance node.
    /// </remarks>
    [TestFixture]
    [Category("AIModelManagement")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class AddressSpaceShapeTests
    {
        [Test]
        public async Task ExactlyOneEntryPointHangsOffTheServerObjectAsync()
        {
            using AINodeManager nm = await CreateAsync().ConfigureAwait(false);

            int roots = nm.CountIndexed<AiRootState>();

            // The model already declares the entry point and parents it to the
            // Server Object. Building a second one leaves two Objects with the same
            // BrowseName under the Server, one populated and one empty, and which
            // one a client finds depends on browse order - so half the time the
            // Server appears to publish nothing at all.
            Assert.That(roots, Is.EqualTo(1));

            Assert.That(
                nm.RootId,
                Is.EqualTo(new NodeId(
                    Opc.Ua.AI.Objects.AiModelManagement,
                    nm.NamespaceIndex)),
                "the entry point must be the one the model declares");
        }

        [Test]
        public async Task DynamicNodeIdsCannotCollideWithTheModelsOwnAsync()
        {
            using AINodeManager nm = await CreateAsync().ConfigureAwait(false);

            var deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);

            // Enough churn to walk a numeric counter into the model's id range,
            // which starts at 1001 and runs to 7001.
            for (int index = 0; index < 40; index++)
            {
                await deployment.BeginTransfer!.OnCallAsync!(
                    nm.SystemContext,
                    deployment.BeginTransfer,
                    nm.PrimaryDeploymentId,
                    "application/json",
                    16,
                    CancellationToken.None).ConfigureAwait(false);
            }

            // Every type the model declares must still be the type it declared. A
            // numeric counter starting at 1 overwrites these silently: the
            // predefined-node index takes the last writer, so AiRootType quietly
            // became an inference job's FinishedAt property.
            foreach (uint identifier in new uint[]
            {
                Opc.Ua.AI.ObjectTypes.AiRootType,
                Opc.Ua.AI.ObjectTypes.ModelType,
                Opc.Ua.AI.ObjectTypes.DeploymentType,
                Opc.Ua.AI.ObjectTypes.InferenceTransferType
            })
            {
                var id = new NodeId(identifier, nm.NamespaceIndex);
                NodeState? node = nm.IndexedNode(id);

                Assert.That(node, Is.Not.Null, $"{id} is missing");
                Assert.That(
                    node,
                    Is.InstanceOf<BaseObjectTypeState>(),
                    $"{id} was overwritten by an instance node");
            }
        }

        [Test]
        public async Task TheJobsFolderIsResolvableOnceSomethingIsInItAsync()
        {
            using AINodeManager nm = await CreateAsync().ConfigureAwait(false);

            var deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);

            BeginTransferMethodStateResult begun = await deployment.BeginTransfer!.OnCallAsync!(
                nm.SystemContext,
                deployment.BeginTransfer,
                nm.PrimaryDeploymentId,
                "application/json",
                16,
                CancellationToken.None).ConfigureAwait(false);

            var root = nm.FindPredefinedNode<AiRootState>(nm.RootId);

            Assert.That(root.Jobs, Is.Not.Null);

            // Created lazily on the first transfer, the folder existed on the
            // NodeState tree - so it appeared in a Browse of the root - while being
            // absent from the index, so browsing IT returned BadNodeIdUnknown. The
            // collection the specification defines was unreachable.
            Assert.That(
                nm.IndexedNode(root.Jobs!.NodeId), Is.Not.Null,
                "the Jobs folder must be indexed, not only present on the tree");

            Assert.That(nm.IndexedNode(begun.Transfer), Is.Not.Null);
        }

        [Test]
        public async Task JobsAreReclaimedRatherThanAccumulatingAsync()
        {
            using AINodeManager nm = await AIServerTestHarness
                .CreateAsync(
                    new InferenceBackends(new FakeInferenceBackend("primary")),
                    new AIOptions
                    {
                        EnableFallback = false,
                        AsyncInferenceDelay = TimeSpan.Zero,
                        MaxRetainedJobs = 3
                    })
                .ConfigureAwait(false);

            var deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);
            var started = new List<NodeId>();

            for (int index = 0; index < 6; index++)
            {
                InvokeAsyncMethodStateResult result =
                    await deployment.InvokeAsync!.OnCallAsync!(
                        nm.SystemContext,
                        deployment.InvokeAsync,
                        nm.PrimaryDeploymentId,
                        ByteString.From(Encoding.UTF8.GetBytes("{}")),
                        string.Empty,
                        "application/json",
                        ArrayOf<Opc.Ua.KeyValuePair>.Empty,
                        CancellationToken.None).ConfigureAwait(false);

                started.Add(result.Job);
            }

            int live = 0;

            foreach (NodeId job in started)
            {
                if (nm.IndexedNode(job) is not null)
                {
                    live++;
                }
            }

            // Each job retains its request and response payloads, so an uncapped set
            // grows in bytes as well as nodes - and any session that can call the
            // Method can grow it. Transfers already had a cap; jobs did not.
            Assert.That(live, Is.LessThanOrEqualTo(3));
            Assert.That(
                nm.IndexedNode(started[^1]), Is.Not.Null,
                "the most recent job must survive");
        }

        private static Task<AINodeManager> CreateAsync()
        {
            return AIServerTestHarness.CreateAsync(
                new InferenceBackends(new FakeInferenceBackend("primary")),
                new AIOptions { EnableFallback = false });
        }
    }
}
