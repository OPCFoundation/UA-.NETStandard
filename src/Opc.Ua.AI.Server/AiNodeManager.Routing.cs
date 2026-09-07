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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.AI;
using Opc.Ua.AI.Inference;
using AIRefs = Opc.Ua.AI.ReferenceTypeIds;
using BrowseNames = Opc.Ua.AI.BrowseNames;
using ObjectIds = Opc.Ua.ObjectIds;
using ReferenceTypeIds = Opc.Ua.ReferenceTypeIds;

namespace Opc.Ua.AI.Server
{
    public sealed partial class AINodeManager
    {
        /// <summary>
        /// One inference, and the model that actually produced it.
        /// </summary>
        /// <remarks>
        /// The two travel together deliberately. Every path that produces a result
        /// has to produce the model NodeId alongside it, so there is no way to
        /// return an answer while forgetting to say where it came from.
        /// </remarks>
        private readonly record struct InferenceOutcome(
            InferenceResult Result,
            NodeId ModelUsed);

        /// <summary>
        /// Calls the deployment's backend, and the fallback's if policy allows it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Substitution happens only when <c>FallbackPolicy</c> says
        /// <c>FallBackTo</c>. <c>Fail</c> is the default and it means what it says:
        /// a caller that asked this deployment for an answer gets this deployment's
        /// answer or none, because for some callers a different model's answer is
        /// worse than no answer at all.
        /// </para>
        /// <para>
        /// A safety refusal is not a failure and is never retried elsewhere. The
        /// content filter declined, which is a result; sending the same payload to a
        /// second model until one accepts it would turn a policy into an obstacle.
        /// </para>
        /// </remarks>
        private async ValueTask<InferenceOutcome> RunWithFallbackAsync(
            DeploymentState deployment,
            ReadOnlyMemory<byte> payload,
            string contentType,
            double timeoutMilliseconds,
            CancellationToken ct)
        {
            IInferenceBackend backend = BackendFor(deployment);
            ModelState? model = ModelFor(deployment);

            var request = new InferenceRequest
            {
                Model = ModelNameFor(model),
                Payload = payload,
                ContentType = contentType,
                Timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds)
            };

            InferenceResult result = await backend
                .InvokeAsync(request, ct)
                .ConfigureAwait(false);

            RecordAttempt(deployment, result.Ok);

            if (result.Ok || !ShouldFallBack(deployment, result))
            {
                return new InferenceOutcome(result, ModelIdOf(model));
            }

            DeploymentState? substitute = m_fallback;
            IInferenceBackend? fallbackBackend = m_backends.Fallback;

            if (substitute is null ||
                fallbackBackend is null ||
                ReferenceEquals(substitute, deployment))
            {
                return new InferenceOutcome(result, ModelIdOf(model));
            }

            m_logger.LogWarning(
                "Primary deployment failed ({Reason}); falling back.",
                result.Message ?? "no detail");

            ModelState? substituteModel = ModelFor(substitute);

            InferenceResult fallbackResult = await fallbackBackend
                .InvokeAsync(
                    request with { Model = ModelNameFor(substituteModel) },
                    ct)
                .ConfigureAwait(false);

            RecordAttempt(substitute, fallbackResult.Ok);

            // The substituted model, not the one that was asked for. Reporting the
            // requested model here would be the single most damaging thing this
            // sample could get wrong, because everything downstream would look
            // correct while attributing answers to a model that never ran.
            return new InferenceOutcome(fallbackResult, ModelIdOf(substituteModel));
        }

        /// <summary>
        /// Whether a failed call may be retried on the fallback.
        /// </summary>
        private bool ShouldFallBack(DeploymentState deployment, InferenceResult result)
        {
            if (result.Finish == InferenceFinish.Filtered)
            {
                return false;
            }

            PropertyState<FallbackPolicyEnum>? policy =
                deployment.FindChild(
                    SystemContext,
                    new QualifiedName(BrowseNames.FallbackPolicy, NamespaceIndex)) as
                PropertyState<FallbackPolicyEnum>;

            return policy?.Value == FallbackPolicyEnum.FallBackTo;
        }

        /// <summary>
        /// Which backend sits behind a deployment.
        /// </summary>
        private IInferenceBackend BackendFor(DeploymentState deployment)
        {
            return ReferenceEquals(deployment, m_fallback) && m_backends.Fallback is not null
                ? m_backends.Fallback
                : m_backends.Primary;
        }

        /// <summary>
        /// Which model a deployment runs, followed through <c>UsesModel</c> rather
        /// than remembered separately.
        /// </summary>
        /// <remarks>
        /// The reference is the specification's answer to the provenance question,
        /// so following it here means the sample's own routing is exercising the
        /// same path an auditing client walks. A private lookup table beside it
        /// could disagree with the address space, and would eventually.
        /// </remarks>
        private ModelState? ModelFor(DeploymentState deployment)
        {
            var references = new List<IReference>();
            deployment.GetReferences(SystemContext, references);

            NodeId usesModel = RefType(AIRefs.UsesModel);

            foreach (IReference reference in references)
            {
                if (reference.IsInverse || reference.ReferenceTypeId != usesModel)
                {
                    continue;
                }

                NodeId targetId = ExpandedNodeId.ToNodeId(
                    reference.TargetId,
                    Server.NamespaceUris);

                if (PredefinedNodes.TryGetValue(targetId, out NodeState? node) &&
                    node is ModelState model)
                {
                    return model;
                }
            }

            return null;
        }

        private NodeId ModelIdOf(ModelState? model)
        {
            return model?.NodeId ?? NodeId.Null;
        }

        /// <summary>
        /// The name the backend knows a model by.
        /// </summary>
        /// <remarks>
        /// The address space identifies a model as publisher/name:version, which is
        /// the durable identity. An endpoint usually wants the bare name, so the
        /// translation happens here rather than by publishing the endpoint's name as
        /// though it were the identity.
        /// </remarks>
        private string ModelNameFor(ModelState? model)
        {
            if (model is null)
            {
                return string.Empty;
            }

            var name = model.FindChild(
                SystemContext,
                new QualifiedName(BrowseNames.Name, NamespaceIndex)) as PropertyState<LocalizedText>;

            return name is null ? string.Empty : name.Value.Text ?? string.Empty;
        }

        /// <summary>
        /// Resolves the deployment a method was invoked on.
        /// </summary>
        private DeploymentState? FindDeployment(NodeId objectId)
        {
            return PredefinedNodes.TryGetValue(objectId, out NodeState? node)
                ? node as DeploymentState
                : null;
        }

        /// <summary>
        /// Updates the health a deployment publishes after an attempt.
        /// </summary>
        /// <remarks>
        /// <c>ConsecutiveFailures</c> resets on success rather than decrementing,
        /// because the question it answers is "is it failing now", not "how often
        /// has it ever failed".
        /// </remarks>
        private void RecordAttempt(DeploymentState deployment, bool succeeded)
        {
            var failures = deployment.FindChild(
                SystemContext,
                new QualifiedName(BrowseNames.ConsecutiveFailures, NamespaceIndex)) as
                PropertyState<uint>;

            if (failures is not null)
            {
                failures.Value = succeeded ? 0 : failures.Value + 1;
                failures.ClearChangeMasks(SystemContext, false);
            }

            if (succeeded)
            {
                var lastSuccess = deployment.FindChild(
                    SystemContext,
                    new QualifiedName(BrowseNames.LastSuccessAt, NamespaceIndex)) as
                    PropertyState<DateTimeUtc>;

                if (lastSuccess is not null)
                {
                    lastSuccess.Value = DateTime.UtcNow;
                    lastSuccess.ClearChangeMasks(SystemContext, false);
                }
            }

            UpdateReachability(deployment, succeeded);
        }

        private void UpdateReachability(DeploymentState deployment, bool reachable)
        {
            if (!(deployment.FindChild(
                SystemContext,
                new QualifiedName(BrowseNames.Reachability, NamespaceIndex)) is PropertyState<ReachabilityEnum> state))
            {
                return;
            }

            state.Value = reachable ? ReachabilityEnum.Reachable : ReachabilityEnum.Unreachable;
            state.ClearChangeMasks(SystemContext, false);
        }
    }
}
