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

using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.AI;
using Opc.Ua.AI.Inference;
using Opc.Ua.AI.Server;

namespace Opc.Ua.AI.Tests
{
    /// <summary>
    /// Verifies that a substituted model is reported as one.
    /// </summary>
    /// <remarks>
    /// This is the fixture worth having. A fallback that answers without saying so
    /// is indistinguishable from a healthy primary at every layer above it: the call
    /// succeeds, the payload is well formed, and the caller attributes a smaller
    /// model's answer to the model it asked for. Nothing else in the sample fails
    /// this quietly.
    /// </remarks>
    [TestFixture]
    [Category("AIModelManagement")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class FallbackTests
    {
        [Test]
        public async Task AHealthyPrimaryAnswersAndReportsItsOwnModelAsync()
        {
            var primary = new FakeInferenceBackend("primary");
            var fallback = new FakeInferenceBackend("fallback");

            using AINodeManager nm = await CreateAsync(primary, fallback)
                .ConfigureAwait(false);

            InvokeMethodStateResult result = await InvokeAsync(nm, nm.PrimaryDeploymentId)
                .ConfigureAwait(false);

            var primaryModel = nm.FindPredefinedNode<ModelState>(ModelOf(nm, nm.PrimaryDeploymentId));

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(AnsweredBy(result), Is.EqualTo("primary"));
                Assert.That(result.ModelUsed, Is.EqualTo(primaryModel.NodeId));
                Assert.That(fallback.Requests, Is.Empty, "the fallback must not be consulted");
            });
        }

        [Test]
        public async Task AFailedPrimaryFallsBackAndReportsTheSubstitutedModelAsync()
        {
            var primary = new FakeInferenceBackend("primary") { Healthy = false };
            var fallback = new FakeInferenceBackend("fallback");

            using AINodeManager nm = await CreateAsync(primary, fallback)
                .ConfigureAwait(false);

            InvokeMethodStateResult result = await InvokeAsync(nm, nm.PrimaryDeploymentId)
                .ConfigureAwait(false);

            NodeId primaryModelId = ModelOf(nm, nm.PrimaryDeploymentId);
            NodeId fallbackModelId = ModelOf(nm, nm.FallbackDeploymentId);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(AnsweredBy(result), Is.EqualTo("fallback"));

                // The claim this whole fixture exists for.
                Assert.That(
                    result.ModelUsed,
                    Is.EqualTo(fallbackModelId),
                    "ModelUsed must name the model that actually answered");
                Assert.That(
                    result.ModelUsed,
                    Is.Not.EqualTo(primaryModelId),
                    "reporting the requested model would hide the substitution");
            });
        }

        [Test]
        public async Task FallbackIsNotTakenWhenThePolicySaysFailAsync()
        {
            var primary = new FakeInferenceBackend("primary") { Healthy = false };
            var fallback = new FakeInferenceBackend("fallback");

            // No fallback deployment means no FallsBackTo and the policy stays Fail,
            // which is the default a caller gets unless someone chose otherwise.
            using AINodeManager nm = await CreateAsync(
                primary,
                fallback,
                new AIOptions { EnableFallback = false })
                .ConfigureAwait(false);

            InvokeMethodStateResult result = await InvokeAsync(nm, nm.PrimaryDeploymentId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.False);
                Assert.That(fallback.Requests, Is.Empty);
                Assert.That(nm.FallbackDeploymentId, Is.EqualTo(NodeId.Null));
            });
        }

        [Test]
        public async Task ASafetyRefusalIsNotRetriedOnTheFallbackAsync()
        {
            var primary = new FakeInferenceBackend("primary")
            {
                Healthy = false,
                FailureKind = InferenceFinish.Filtered
            };
            var fallback = new FakeInferenceBackend("fallback");

            using AINodeManager nm = await CreateAsync(primary, fallback)
                .ConfigureAwait(false);

            await InvokeAsync(nm, nm.PrimaryDeploymentId).ConfigureAwait(false);

            // A content filter declining is a result, not a fault. Sending the same
            // payload to a second model until one accepts it would turn a policy
            // into an obstacle, so the substitution must not happen here even
            // though the deployment is configured to fall back.
            Assert.That(
                fallback.Requests,
                Is.Empty,
                "a filtered response must not be retried elsewhere");
        }

        [Test]
        public async Task ConsecutiveFailuresResetOnSuccessAsync()
        {
            var primary = new FakeInferenceBackend("primary") { Healthy = false };
            var fallback = new FakeInferenceBackend("fallback");

            using AINodeManager nm = await CreateAsync(primary, fallback)
                .ConfigureAwait(false);

            await InvokeAsync(nm, nm.PrimaryDeploymentId).ConfigureAwait(false);
            await InvokeAsync(nm, nm.PrimaryDeploymentId).ConfigureAwait(false);

            Assert.That(FailuresOf(nm, nm.PrimaryDeploymentId), Is.EqualTo(2u));

            primary.Healthy = true;
            await InvokeAsync(nm, nm.PrimaryDeploymentId).ConfigureAwait(false);

            // Resets rather than decrements, because the question the member answers
            // is "is it failing now", not "how often has it ever failed".
            Assert.That(FailuresOf(nm, nm.PrimaryDeploymentId), Is.Zero);
        }

        private static Task<AINodeManager> CreateAsync(
            IInferenceBackend primary,
            IInferenceBackend fallback,
            AIOptions? options = null)
        {
            return AIServerTestHarness.CreateAsync(
                new InferenceBackends(primary, fallback),
                options);
        }

        private static async Task<InvokeMethodStateResult> InvokeAsync(
            AINodeManager nm,
            NodeId deploymentId)
        {
            var deployment = nm.FindPredefinedNode<DeploymentState>(deploymentId);

            return await deployment.Invoke!.OnCallAsync!(
                nm.SystemContext,
                deployment.Invoke,
                deploymentId,
                ByteString.From(Encoding.UTF8.GetBytes("{}")),
                string.Empty,
                "application/json",
                ArrayOf<Opc.Ua.KeyValuePair>.Empty,
                5000,
                CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Follows UsesModel, the way an auditing client would.
        /// </summary>
        private static NodeId ModelOf(AINodeManager nm, NodeId deploymentId)
        {
            var deployment = nm.FindPredefinedNode<DeploymentState>(deploymentId);
            var references = new System.Collections.Generic.List<IReference>();
            deployment.GetReferences(nm.SystemContext, references);

            NodeId usesModel = ExpandedNodeId.ToNodeId(
                Opc.Ua.AI.ReferenceTypeIds.UsesModel,
                nm.SystemContext.NamespaceUris);

            foreach (IReference reference in references)
            {
                if (!reference.IsInverse && reference.ReferenceTypeId == usesModel)
                {
                    return ExpandedNodeId.ToNodeId(
                        reference.TargetId,
                        nm.SystemContext.NamespaceUris);
                }
            }

            return NodeId.Null;
        }

        private static uint FailuresOf(AINodeManager nm, NodeId deploymentId)
        {
            var deployment = nm.FindPredefinedNode<DeploymentState>(deploymentId);
            return deployment.ConsecutiveFailures?.Value ?? 0;
        }

        private static string AnsweredBy(InvokeMethodStateResult result)
        {
            string json = Encoding.UTF8.GetString(result.ResponsePayload.Span);
            return json.Contains("\"fallback\"", System.StringComparison.Ordinal)
                ? "fallback"
                : "primary";
        }
    }
}
