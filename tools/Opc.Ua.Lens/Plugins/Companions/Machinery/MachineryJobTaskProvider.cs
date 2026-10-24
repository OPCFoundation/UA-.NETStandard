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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Plugins.Companions.Providers;
using Jobs = Opc.Ua.Machinery.Jobs;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace UaLens.Plugins.Companions.Machinery
{
    /// <summary>
    /// Restricts the existing ISA-95 workflow to the current Machinery list's real order-control source.
    /// </summary>
    internal sealed class MachineryJobTaskProvider : IPreparedCompanionProvider
    {
        /// <summary>
        /// Captures navigation context only; neither discovery nor authorization starts here.
        /// </summary>
        public MachineryJobTaskProvider(
            MachineryActionSelection selection, IPreparedCompanionProvider? provider = null,
            CancellationToken lifetime = default)
        {
            m_selection = selection ?? throw new ArgumentNullException(nameof(selection));
            m_provider = provider ?? new Isa95CompanionProvider();
            m_lifetime = lifetime;
            if (m_provider.Descriptor.Id != "isa95")
            {
                throw new ArgumentException(
                    "Use the existing ISA-95 provider for Machinery job tasks.", nameof(provider));
            }
        }

        /// <inheritdoc/>
        public CompanionDescriptor Descriptor => m_provider.Descriptor;

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
            CompanionContext context, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            (NodeId control, _) = await ResolveControlAsync(context, lifetime.Token).ConfigureAwait(false);
            ArrayOf<CompanionTarget> targets = await m_provider.DiscoverAsync(context, lifetime.Token)
                .ConfigureAwait(false);
            RequireCurrent(context, lifetime.Token);
            CompanionTarget[] selected = [.. targets.ToList().Where(target =>
                target.ProviderId == "isa95" && target.NodeId == control)];
            if (selected.Length != 1)
            {
                throw new InvalidOperationException("The selected job list has no unique discovered ISA-95 control.");
            }
            m_target = selected[0];
            return [m_target];
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionInspection> InspectAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            await RequireTargetAsync(context, target, lifetime.Token).ConfigureAwait(false);
            CompanionInspection inspection = await m_provider.InspectAsync(context, target, lifetime.Token)
                .ConfigureAwait(false);
            RequireCurrent(context, lifetime.Token);
            return inspection with
            {
                Operations = inspection.Operations.ToList().Where(operation =>
                    (operation.Id == "refresh" && operation.Safety == CompanionOperationSafety.ReadOnly) ||
                    (IsJobOperation(operation.Id) &&
                        operation.HasTypedInput &&
                        operation.Safety == CompanionOperationSafety.DeploymentMutation)).ToArrayOf()
            };
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionOperationResult> ExecuteAsync(
            CompanionContext context, CompanionTarget target, string operationId, string? input,
            CancellationToken cancellationToken)
        {
            if (operationId != "refresh")
            {
                throw new InvalidOperationException("Use the existing typed ISA-95 Prepare and Review workflow.");
            }
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            await RequireTargetAsync(context, target, lifetime.Token).ConfigureAwait(false);
            CompanionOperationResult result = await m_provider.ExecuteAsync(
                context, target, operationId, input, lifetime.Token).ConfigureAwait(false);
            RequireCurrent(context, lifetime.Token);
            return result;
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionTaskInput> PrepareInputAsync(
            CompanionContext context, CompanionTarget target, string operationId, ArrayOf<CompanionValue> inputs,
            CancellationToken cancellationToken)
        {
            RequireOperation(operationId);
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            NodeId management = await RequireTargetAsync(context, target, lifetime.Token).ConfigureAwait(false);
            CompanionTaskInput prepared = await m_provider.PrepareInputAsync(
                context, target, operationId, inputs, lifetime.Token).ConfigureAwait(false);
            RequireCurrent(context, lifetime.Token);
            RequireMachineryTask(prepared, target, operationId, management);
            return prepared;
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionOperationResult> ExecutePreparedAsync(
            CompanionContext context, CompanionTarget target, string operationId, CompanionTaskInput input,
            IProgress<CompanionTaskProgress>? progress, CancellationToken cancellationToken)
        {
            RequireOperation(operationId);
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            NodeId management = await RequireTargetAsync(context, target, lifetime.Token).ConfigureAwait(false);
            RequireMachineryTask(input, target, operationId, management);
            CompanionOperationResult result = await m_provider.ExecutePreparedAsync(
                context, target, operationId, input, progress, lifetime.Token).ConfigureAwait(false);
            RequireCurrent(context, lifetime.Token);
            return result;
        }

        private static void RequireMachineryTask(
            CompanionTaskInput input, CompanionTarget target, string operationId, NodeId management)
        {
            if (input is not Isa95PreparedTask task ||
                !task.Version2 ||
                task.Target != target ||
                task.OperationId != operationId ||
                task.Binding.MachineryOwner != management)
            {
                throw new InvalidOperationException(
                    "The prepared ISA-95 task belongs to another job-management source.");
            }
        }

        private CancellationTokenSource Begin(CompanionContext context, CancellationToken cancellationToken)
        {
            RequireCurrent(context, cancellationToken);
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime);
            lifetime.CancelAfter(TimeSpan.FromSeconds(30));
            return lifetime;
        }

        private void RequireCurrent(CompanionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            m_lifetime.ThrowIfCancellationRequested();
            m_selection.RequireCurrent();
            if (!ReferenceEquals(context.Session, m_selection.Context.Session) ||
                m_selection.Section != MachinerySection.Jobs)
            {
                throw new InvalidOperationException(
                    "Select the current Machinery job list before opening its workflow.");
            }
        }

        private async Task<NodeId> RequireTargetAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            RequireCurrent(context, cancellationToken);
            if (m_target is null || target != m_target)
            {
                throw new InvalidOperationException("Discover the selected Machinery job-control source first.");
            }
            (NodeId control, NodeId management) = await ResolveControlAsync(context, cancellationToken)
                .ConfigureAwait(false);
            if (control != target.NodeId)
            {
                throw new InvalidOperationException("The selected Machinery job-control source changed.");
            }
            return management;
        }

        private async Task<(NodeId Control, NodeId Management)> ResolveControlAsync(
            CompanionContext context, CancellationToken cancellationToken)
        {
            NodeId management = await IndustrialCompanionAccess.ResolveChildAsync(
                context, m_selection.Machine.NodeId, Jobs.Namespaces.MachineryJobs, Jobs.BrowseNames.JobManagement,
                false, cancellationToken).ConfigureAwait(false);
            NodeId control = await IndustrialCompanionAccess.ResolveChildAsync(
                context, management, Jobs.Namespaces.MachineryJobs, Jobs.BrowseNames.JobOrderControl,
                false, cancellationToken).ConfigureAwait(false);
            NodeId list = await IndustrialCompanionAccess.ResolveChildAsync(
                context, control, V2.Namespaces.ISA95JobControlV2, V2.BrowseNames.JobOrderList,
                false, cancellationToken).ConfigureAwait(false);
            RequireCurrent(context, cancellationToken);
            if (list != m_selection.Item.NodeId ||
                (!m_selection.Item.SourceNodeId.IsNull && list != m_selection.Item.SourceNodeId))
            {
                throw new InvalidOperationException(
                    "The selected job is not from this Machinery control's JobOrderList.");
            }
            return (control, management);
        }

        private static bool IsJobOperation(string operationId)
        {
            return operationId is "store-job" or "update-job" or "start-job" or "cancel-job" or
                "clear-job" or "pause-job" or "resume-job" or "abort-job";
        }

        private static void RequireOperation(string operationId)
        {
            if (!IsJobOperation(operationId))
            {
                throw new InvalidOperationException("This operation is not a selected Machinery job task.");
            }
        }

        private readonly MachineryActionSelection m_selection;
        private readonly IPreparedCompanionProvider m_provider;
        private readonly CancellationToken m_lifetime;
        private CompanionTarget? m_target;
    }
}
