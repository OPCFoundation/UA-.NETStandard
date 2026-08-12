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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.AI.Inference;
using Opc.Ua.AI.Server;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.AI;

namespace Opc.Ua.AI.Tests
{
    /// <summary>
    /// Verifies that a result can be traced back to the artefact that produced it.
    /// </summary>
    /// <remarks>
    /// The walk is result to <c>ModelUsed</c>, model to <c>Digest</c>, and model to
    /// the source it was <c>ImportedFrom</c>. Every link has to be present and
    /// resolvable, because a chain that breaks anywhere answers nothing at all: the
    /// question it exists for - "which weights produced this output" - has no
    /// partial answer.
    /// </remarks>
    [TestFixture]
    [Category("AIModelManagement")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ProvenanceTests
    {
        [Test]
        public async Task AResultResolvesToAModelThatCarriesItsIdentityAsync()
        {
            using AINodeManager nm = await CreateAsync().ConfigureAwait(false);

            var deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);

            InvokeMethodStateResult result = await deployment.Invoke!.OnCallAsync!(
                nm.SystemContext,
                deployment.Invoke,
                nm.PrimaryDeploymentId,
                ByteString.From(Encoding.UTF8.GetBytes("{}")),
                string.Empty,
                "application/json",
                ArrayOf<Opc.Ua.KeyValuePair>.Empty,
                5000,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ModelUsed, Is.Not.EqualTo(NodeId.Null));

            // The step that matters: the NodeId a caller was handed must resolve.
            var model = nm.FindPredefinedNode<ModelState>(result.ModelUsed);

            Assert.Multiple(() =>
            {
                Assert.That(model, Is.Not.Null);
                Assert.That(model.ModelId, Is.Not.Null);
                Assert.That(model.ModelId!.Value, Is.Not.Empty);
                Assert.That(model.Digest, Is.Not.Null, "the walk terminates at a digest");
                Assert.That(model.DigestAlgorithm, Is.Not.Null);
            });
        }

        [Test]
        public async Task EveryPublishedModelIsReachableFromTheSourceItCameFromAsync()
        {
            using AINodeManager nm = await CreateAsync().ConfigureAwait(false);

            var deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);
            NodeId modelId = TargetOf(
                nm, deployment, Opc.Ua.AI.ReferenceTypeIds.UsesModel);

            Assert.That(modelId, Is.Not.EqualTo(NodeId.Null), "UsesModel must be present");

            var model = nm.FindPredefinedNode<ModelState>(modelId);
            NodeId sourceId = TargetOf(
                nm, model, Opc.Ua.AI.ReferenceTypeIds.ImportedFrom);

            Assert.That(
                sourceId,
                Is.Not.EqualTo(NodeId.Null),
                "a model this Server did not author must say where it came from");

            var source = nm.FindPredefinedNode<ModelSourceState>(sourceId);

            Assert.Multiple(() =>
            {
                Assert.That(source, Is.Not.Null);
                Assert.That(source.EndpointUri, Is.Not.Null);
                Assert.That(source.SourceId!.Value, Is.Not.Empty);
            });
        }

        [Test]
        public async Task ADigestIsEmptyRatherThanInventedWhenTheBackendDeclaresNoneAsync()
        {
            using AINodeManager nm = await CreateAsync().ConfigureAwait(false);

            var deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);
            var model = nm.FindPredefinedNode<ModelState>(
                TargetOf(nm, deployment, Opc.Ua.AI.ReferenceTypeIds.UsesModel));

            // A hosted endpoint that will not say which weights answered cannot be
            // made to say so by hashing its name. A digest that looks like an
            // artefact digest but is not one is worse than none, because it will be
            // compared against a real one and appear to disagree.
            Assert.Multiple(() =>
            {
                Assert.That(model.Digest!.Value.Length, Is.Zero);
                Assert.That(model.DigestAlgorithm!.Value, Is.Empty);
            });
        }

        [Test]
        public async Task ADeclaredDigestIsPublishedVerbatimAsync()
        {
            byte[] digest = [1, 2, 3, 4];

            var backendOptions = new InferenceBackendOptions();
            backendOptions.Models.Add(new BackendModel
            {
                Publisher = "contoso",
                Name = "weld-inspect",
                Version = "2.1.0",
                Digest = digest,
                DigestAlgorithm = "SHA-256"
            });

            using AINodeManager nm = await AIServerTestHarness
                .CreateAsync(
                    new InferenceBackends(new FakeInferenceBackend("primary")),
                    new AIOptions { EnableFallback = false },
                    backendOptions)
                .ConfigureAwait(false);

            var deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);
            var model = nm.FindPredefinedNode<ModelState>(
                TargetOf(nm, deployment, Opc.Ua.AI.ReferenceTypeIds.UsesModel));

            Assert.Multiple(() =>
            {
                Assert.That(model.Digest!.Value.ToArray(), Is.EqualTo(digest));
                Assert.That(model.DigestAlgorithm!.Value, Is.EqualTo("SHA-256"));
                Assert.That(
                    model.ModelId!.Value,
                    Is.EqualTo("contoso/weld-inspect:2.1.0"),
                    "identity is the publisher, name and version triple");
            });
        }

        private static Task<AINodeManager> CreateAsync()
        {
            return AIServerTestHarness.CreateAsync(
                new InferenceBackends(
                    new FakeInferenceBackend("primary"),
                    new FakeInferenceBackend("fallback")));
        }

        /// <summary>
        /// Follows one forward reference of the given type.
        /// </summary>
        private static NodeId TargetOf(
            AINodeManager nm,
            NodeState node,
            ExpandedNodeId referenceTypeId)
        {
            var references = new List<IReference>();
            node.GetReferences(nm.SystemContext, references);

            NodeId wanted = ExpandedNodeId.ToNodeId(
                referenceTypeId, nm.SystemContext.NamespaceUris);

            foreach (IReference reference in references)
            {
                if (!reference.IsInverse && reference.ReferenceTypeId == wanted)
                {
                    return ExpandedNodeId.ToNodeId(
                        reference.TargetId, nm.SystemContext.NamespaceUris);
                }
            }

            return NodeId.Null;
        }
    }
}
