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
using Opc.Ua;
using Opc.Ua.AMB.Client;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed partial class AmbCompanionProvider
    {
        /// <summary>
        /// Creates a read-only provider unless the host explicitly supplies its deployment policy.
        /// The same policy must also authorize the workspace's one-use confirmation.
        /// </summary>
        public AmbCompanionProvider(
            ICompanionDeploymentPolicy? deploymentPolicy = null, TimeProvider? timeProvider = null)
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_deploymentPolicy = deploymentPolicy ??
                new ConfiguredCompanionDeploymentPolicy(timeProvider: m_timeProvider);
            m_tasksConfigured = deploymentPolicy is not null;
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionTaskInput> PrepareInputAsync(
            CompanionContext context, CompanionTarget target, string operationId, ArrayOf<CompanionValue> inputs,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime =
                CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
            RequireTaskConfiguration();
            CompanionOperation operation = FindTask(operationId);
            MutationInput payload = ParseTaskInput(context, target, operation, inputs);
            var draft = new CompanionOperationDraft(
                target, operation, null, context.Session, m_timeProvider.GetUtcNow().AddMinutes(5),
                payload, payload.Inputs);
            var session = new MutationSession(context, draft);
            TaskEvidence evidence = await ReadTaskEvidenceAsync(
                context, target, operationId, payload, lifetime.Token).ConfigureAwait(false);
            CompanionDeploymentGrant grant = await m_deploymentPolicy.AuthorizeAsync(
                context, draft, lifetime.Token).ConfigureAwait(false);
            ValidateTaskGrant(draft, grant);
            session.RequireCurrent(context, m_timeProvider);
            lifetime.Token.ThrowIfCancellationRequested();
            return new PreparedMutation(this, draft.WithGrant(grant), session, payload, evidence);
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionOperationResult> ExecutePreparedAsync(
            CompanionContext context, CompanionTarget target, string operationId, CompanionTaskInput input,
            IProgress<CompanionTaskProgress>? progress, CancellationToken cancellationToken)
        {
            if (input is not PreparedMutation prepared)
            {
                throw new ArgumentException("Prepare the exact typed AMB task first.", nameof(input));
            }
            prepared.Consume(this, target, operationId);
            using CancellationTokenSource lifetime =
                CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
            RequireTaskConfiguration();
            prepared.RequireCurrent(context, m_timeProvider);
            await RequireCurrentEvidenceAsync(context, prepared, lifetime.Token).ConfigureAwait(false);
            CompanionDeploymentGrant grant = await m_deploymentPolicy.AuthorizeAsync(
                context, prepared.Draft, lifetime.Token).ConfigureAwait(false);
            ValidateTaskGrant(prepared.Draft, grant);
            if (grant != prepared.Draft.DeploymentGrant)
            {
                throw new UnauthorizedAccessException("The AMB deployment grant changed. Prepare again.");
            }
            progress?.Report(new CompanionTaskProgress("Rechecking the exact AMB edit before one dispatch."));
            await RequireCurrentEvidenceAsync(context, prepared, lifetime.Token).ConfigureAwait(false);
            prepared.RequireCurrent(context, m_timeProvider);
            lifetime.Token.ThrowIfCancellationRequested();
            CompanionOperationResult result = await DispatchTaskAsync(
                context, prepared, lifetime.Token).ConfigureAwait(false);
            prepared.RequireCurrent(context, m_timeProvider);
            lifetime.Token.ThrowIfCancellationRequested();
            return result;
        }

        private async ValueTask<(ArrayOf<CompanionOperation> Operations, string Status)> ReadTaskOperationsAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            if (!m_tasksConfigured)
            {
                return (s_readOperations, "No host policy is configured; metadata mutations are unavailable.");
            }
            using CancellationTokenSource lifetime =
                CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
            List<CompanionOperation> operations = [.. s_readOperations];
            var unavailable = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < s_tasks.Count; index++)
            {
                CompanionOperation operation = s_tasks[index];
                try
                {
                    var draft = new CompanionOperationDraft(
                        target, operation, null, context.Session, m_timeProvider.GetUtcNow().AddMinutes(5));
                    CompanionDeploymentGrant grant = await m_deploymentPolicy.AuthorizeAsync(
                        context, draft, lifetime.Token).ConfigureAwait(false);
                    ValidateTaskGrant(draft, grant);
                    var session = new MutationSession(context, draft);
                    if (await IsTaskSupportedAsync(context, target, operation.Id, lifetime.Token)
                        .ConfigureAwait(false))
                    {
                        session.RequireCurrent(context, m_timeProvider);
                        ValidateTaskGrant(draft, grant);
                        operations.Add(operation);
                    }
                    else
                    {
                        unavailable.Add("not exposed or not applicable");
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    unavailable.Add("no current host authorization");
                }
                catch (ServiceResultException error)
                {
                    unavailable.Add(error.StatusCode.ToString());
                }
            }
            lifetime.Token.ThrowIfCancellationRequested();
            return ([.. operations], unavailable.Count == 0
                ? "Supported metadata tasks still require preparation and fresh confirmation."
                : "Some metadata tasks are unavailable: " + string.Join(", ", unavailable) + ".");
        }

        private async ValueTask RequireCurrentEvidenceAsync(
            CompanionContext context, PreparedMutation prepared, CancellationToken cancellationToken)
        {
            prepared.RequireCurrent(context, m_timeProvider);
            TaskEvidence current = await ReadTaskEvidenceAsync(
                context, prepared.Draft.Target, prepared.Draft.Operation.Id, prepared.Payload, cancellationToken)
                .ConfigureAwait(false);
            if (!prepared.Evidence.Matches(current))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "The asset, alias set, edit target, old value, event or method metadata changed. Prepare again.");
            }
            prepared.RequireCurrent(context, m_timeProvider);
            cancellationToken.ThrowIfCancellationRequested();
        }

        private void RequireTaskConfiguration()
        {
            if (!m_tasksConfigured)
            {
                throw new UnauthorizedAccessException("No host deployment policy authorizes AMB mutations.");
            }
        }

        private void ValidateTaskGrant(CompanionOperationDraft draft, CompanionDeploymentGrant grant)
        {
            if (grant is null ||
                string.IsNullOrWhiteSpace(grant.RuleId) ||
                string.IsNullOrWhiteSpace(grant.Revision) ||
                grant.ExpiresAt <= m_timeProvider.GetUtcNow() ||
                grant.ExpiresAt > draft.ExpiresAt)
            {
                throw new UnauthorizedAccessException("The AMB deployment grant is missing, invalid or expired.");
            }
        }

        private async ValueTask<CompanionOperationResult> DispatchTaskAsync(
            CompanionContext context, PreparedMutation prepared, CancellationToken cancellationToken)
        {
            AmbClient client = CreateClient(context);
            MutationInput payload = prepared.Payload;
            NodeId subject = prepared.Evidence.Subject;
            switch (prepared.Draft.Operation.Id)
            {
                case "set-asset-id":
                    await client.WriteAssetIdAsync(
                        prepared.Draft.Target.NodeId, payload.Text("assetId"), cancellationToken).ConfigureAwait(false);
                    break;
                case "add-link":
                    subject = await client.AddDocumentationLinkAsync(
                        prepared.Draft.Target.NodeId, payload.Text("uri"), payload.Name,
                        payload.DisplayName, payload.Description, cancellationToken).ConfigureAwait(false);
                    break;
                case "write-link":
                    await client.WriteDocumentationLinkAsync(
                        subject, payload.Text("uri"), cancellationToken).ConfigureAwait(false);
                    break;
                case "remove-link":
                    await client.RemoveDocumentationLinkAsync(
                        prepared.Draft.Target.NodeId, subject, cancellationToken).ConfigureAwait(false);
                    break;
                case "acknowledge-health":
                case "acknowledge-maintenance":
                    await AcknowledgeTaskAsync(context, prepared, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            return new CompanionOperationResult(
                "The server accepted the reviewed AMB request. Refresh to observe the result. " +
                "This preparation is consumed, including after an unknown outcome. " +
                "Metadata was rechecked, not atomically compared and exchanged. No URI content was requested.",
                [new CompanionValue("Reviewed operation", Variant.From(prepared.Draft.Operation.Id)),
                    new CompanionValue("Target", Variant.From(subject))]);
        }

        private readonly ICompanionDeploymentPolicy m_deploymentPolicy;
        private readonly TimeProvider m_timeProvider;
        private readonly bool m_tasksConfigured;
    }
}
