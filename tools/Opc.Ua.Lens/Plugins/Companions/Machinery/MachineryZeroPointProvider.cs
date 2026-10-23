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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Plugins.Companions.Providers;
using Padim = Opc.Ua.PADIM;
using ProcessValues = Opc.Ua.Machinery.ProcessValues;

namespace UaLens.Plugins.Companions.Machinery
{
    /// <summary>
    /// Offers only the selected process value's real zero-point method on an authorized owned simulator.
    /// </summary>
    internal sealed class MachineryZeroPointProvider : IPreparedCompanionProvider
    {
        /// <summary>
        /// Creates an inert, selected-item provider with injectable authorization and time.
        /// </summary>
        public MachineryZeroPointProvider(
            MachineryActionSelection selection, ICompanionDeploymentPolicy? policy = null,
            TimeProvider? timeProvider = null, CancellationToken lifetime = default)
        {
            m_selection = selection ?? throw new ArgumentNullException(nameof(selection));
            m_time = timeProvider ?? TimeProvider.System;
            m_policy = policy ?? new ConfiguredCompanionDeploymentPolicy(timeProvider: m_time);
            m_lifetime = lifetime;
        }

        /// <inheritdoc/>
        public CompanionDescriptor Descriptor { get; } = new(
            "machinery", "Machinery process task", ProcessValues.Namespaces.MachineryProcessValues,
            "Owned simulation only. No setpoint, calibration, item-state or operation-mode commands.");

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
            CompanionContext context, CancellationToken cancellationToken)
        {
            _ = await ReadBindingAsync(context, m_selection.Target, cancellationToken).ConfigureAwait(false);
            return [m_selection.Target];
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionInspection> InspectAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            MachineryZeroPointBinding binding = await ReadBindingAsync(context, target, lifetime.Token)
                .ConfigureAwait(false);
            var task = new MachineryZeroPointTask(this, m_selection, binding, m_time.GetUtcNow().AddMinutes(5));
            bool authorized;
            try
            {
                _ = await AuthorizeAsync(context, task.Draft, lifetime.Token).ConfigureAwait(false);
                authorized = true;
            }
            catch (UnauthorizedAccessException)
            {
                authorized = false;
            }
            RequireSelection(context, target);
            return new CompanionInspection(
                [
                    new("Process value", Variant.From(target.NodeId)),
                    new("Analog signal", Variant.From(binding.Signal)),
                    new("Verified method", Variant.From(binding.Method))
                ],
                authorized ? [Operation] : [],
                authorized
                    ? "The selected process exposes an executable, no-argument PADIM ZeroPointAdjustment. " +
                        "Prepare and review it, then authorize that one execution. No adjustment has been sent."
                    : "The method is exposed, but no live opted-in owned Machinery simulation authorizes it.");
        }

        /// <inheritdoc/>
        public ValueTask<CompanionOperationResult> ExecuteAsync(
            CompanionContext context, CompanionTarget target, string operationId, string? input,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Zero-point adjustment requires the typed, one-use preparation.");
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionTaskInput> PrepareInputAsync(
            CompanionContext context, CompanionTarget target, string operationId, ArrayOf<CompanionValue> inputs,
            CancellationToken cancellationToken)
        {
            if (operationId != Operation.Id)
            {
                throw new ArgumentException("Only zero-point adjustment is supported.", nameof(operationId));
            }
            CompanionInputContract.ValidateShape(Operation, inputs);
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            DateTimeOffset expires = m_time.GetUtcNow().AddMinutes(5);
            MachineryZeroPointBinding binding = await ReadBindingAsync(context, target, lifetime.Token)
                .ConfigureAwait(false);
            var task = new MachineryZeroPointTask(this, m_selection, binding, expires);
            CompanionDeploymentGrant grant = await AuthorizeAsync(context, task.Draft, lifetime.Token)
                .ConfigureAwait(false);
            task.SetGrant(grant);
            task.RequireCurrent(context, m_time);
            return task;
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionOperationResult> ExecutePreparedAsync(
            CompanionContext context, CompanionTarget target, string operationId, CompanionTaskInput input,
            IProgress<CompanionTaskProgress>? progress, CancellationToken cancellationToken)
        {
            if (input is not MachineryZeroPointTask task)
            {
                throw new ArgumentException("Prepare the selected process task first.", nameof(input));
            }
            task.Consume(this, target, operationId);
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            task.RequireCurrent(context, m_time);
            await RequireBindingAsync(context, task, lifetime.Token).ConfigureAwait(false);
            CompanionDeploymentGrant grant = await AuthorizeAsync(context, task.Draft, lifetime.Token)
                .ConfigureAwait(false);
            if (grant != task.Grant)
            {
                throw new UnauthorizedAccessException("The owned-simulation grant changed. Prepare again.");
            }
            await RequireBindingAsync(context, task, lifetime.Token).ConfigureAwait(false);
            progress?.Report(new CompanionTaskProgress("Sending one authorized simulator zero-point adjustment."));
            using var client = new PreparedCompanionMethodClient(
                context, target.NodeId, ProcessValues.MethodIds.ProcessValueType_ZeroPointAdjustment,
                task.Binding.Method, false, () => task.RequireCurrent(context, m_time), static () => { });
            var process = new ProcessValues.ProcessValueTypeClient(client, target.NodeId, context.Telemetry);
            await process.ZeroPointAdjustmentAsync(lifetime.Token).ConfigureAwait(false);
            return new CompanionOperationResult(
                "The owned simulator accepted ZeroPointAdjustment. Inspect fresh values explicitly; " +
                "acceptance is not physical calibration evidence. This preparation cannot be replayed.",
                [new("Method result", Variant.From(client.ResultStatus))]);
        }

        private async Task RequireBindingAsync(
            CompanionContext context, MachineryZeroPointTask task, CancellationToken cancellationToken)
        {
            MachineryZeroPointBinding current = await ReadBindingAsync(
                context, task.Draft.Target, cancellationToken).ConfigureAwait(false);
            if (current != task.Binding)
            {
                throw new InvalidOperationException("The process source, method or status changed. Prepare again.");
            }
            task.RequireCurrent(context, m_time);
        }

        private async Task<MachineryZeroPointBinding> ReadBindingAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            RequireSelection(context, target);
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            await IndustrialCompanionAccess.RequireTargetAsync(
                context, target, Descriptor.Id, s_types, lifetime.Token).ConfigureAwait(false);
            NodeId signal = await IndustrialCompanionAccess.ResolveChildAsync(
                context, target.NodeId, Padim.Namespaces.PADIM, Padim.BrowseNames.AnalogSignal,
                false, lifetime.Token).ConfigureAwait(false);
            if (signal != m_selection.Item.SourceNodeId)
            {
                throw new InvalidOperationException("The selected process signal changed. Inspect again.");
            }
            DataValue signalClass = await CompanionMethodMetadata.ReadOneAsync(
                context, signal, Attributes.NodeClass, lifetime.Token).ConfigureAwait(false);
            DataValue signalType = await CompanionMethodMetadata.ReadOneAsync(
                context, signal, Attributes.DataType, lifetime.Token).ConfigureAwait(false);
            if (!signalClass.WrappedValue.TryGetValue(out int kind) ||
                kind != (int)NodeClass.Variable ||
                !signalType.WrappedValue.TryGetValue(out NodeId type) ||
                !await IsNumericDataTypeAsync(context, type, lifetime.Token).ConfigureAwait(false))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The process signal metadata changed.");
            }
            NodeId method = await CompanionMethodMetadata.RequireMethodAsync(
                context, target.NodeId, Padim.Namespaces.PADIM, Padim.BrowseNames.ZeroPointAdjustment,
                [], [], lifetime.Token).ConfigureAwait(false);
            (NodeId statusNode, ushort? status) = await ReadStatusAsync(
                context, target.NodeId, ProcessValues.BrowseNames.Status, lifetime.Token).ConfigureAwait(false);
            (NodeId suppressionNode, ushort? suppression) = await ReadStatusAsync(
                context, target.NodeId, ProcessValues.BrowseNames.AlarmSuppression, lifetime.Token)
                .ConfigureAwait(false);
            lifetime.Token.ThrowIfCancellationRequested();
            RequireSelection(context, target);
            return new MachineryZeroPointBinding(
                signal, type, method, statusNode, status, suppressionNode, suppression);
        }

        private static async ValueTask<bool> IsNumericDataTypeAsync(
            CompanionContext context, NodeId type, CancellationToken cancellationToken)
        {
            if (type.NamespaceIndex == 0 && type.TryGetValue(out uint identifier))
            {
                if (identifier is (>= DataTypes.SByte and <= DataTypes.Double) or
                    DataTypes.Number or DataTypes.Integer or DataTypes.UInteger)
                {
                    return true;
                }
                if (identifier <= DataTypes.DiagnosticInfo)
                {
                    return false;
                }
            }
            return !type.IsNull &&
                await context.Session.NodeCache.IsTypeOfAsync(
                    type, DataTypeIds.Number, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<(NodeId Node, ushort? Value)> ReadStatusAsync(
            CompanionContext context, NodeId owner, string name, CancellationToken cancellationToken)
        {
            NodeId node = await IndustrialCompanionAccess.ResolveChildAsync(
                context, owner, ProcessValues.Namespaces.MachineryProcessValues, name, true, cancellationToken)
                .ConfigureAwait(false);
            if (node.IsNull)
            {
                return (NodeId.Null, null);
            }
            DataValue read = await CompanionMethodMetadata.ReadOneAsync(
                context, node, Attributes.Value, cancellationToken).ConfigureAwait(false);
            if (!read.WrappedValue.TryGetValue(out ushort status))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The process status is not UInt16.");
            }
            return (node, status);
        }

        private void RequireSelection(CompanionContext context, CompanionTarget target)
        {
            m_lifetime.ThrowIfCancellationRequested();
            m_selection.RequireCurrent();
            if (!ReferenceEquals(context.Session, m_selection.Context.Session) ||
                target != m_selection.Target ||
                m_selection.Section != MachinerySection.ProcessValues ||
                m_selection.Item.SourceNodeId.IsNull)
            {
                throw new InvalidOperationException("Inspect and select this process value before opening its task.");
            }
        }

        private CancellationTokenSource Begin(CompanionContext context, CancellationToken cancellationToken)
        {
            RequireSelection(context, m_selection.Target);
            cancellationToken.ThrowIfCancellationRequested();
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime);
            lifetime.CancelAfter(TimeSpan.FromSeconds(30));
            return lifetime;
        }

        private async Task<CompanionDeploymentGrant> AuthorizeAsync(
            CompanionContext context, CompanionOperationDraft draft, CancellationToken cancellationToken)
        {
            if (context.Session.Endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                context.Session.Endpoint.SecurityPolicyUri is not (SecurityPolicies.Basic256Sha256 or
                    SecurityPolicies.Aes128_Sha256_RsaOaep or SecurityPolicies.Aes256_Sha256_RsaPss))
            {
                throw new UnauthorizedAccessException(
                    "A signed and encrypted owned-simulation connection is required.");
            }
            CompanionDeploymentGrant grant = await m_policy.AuthorizeAsync(context, draft, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            RequireSelection(context, draft.Target);
            if (grant is null ||
                !grant.RuleId.StartsWith("owned-machinery-", StringComparison.Ordinal) ||
                grant.Revision != "owned-machinery-v1" ||
                grant.ExpiresAt <= m_time.GetUtcNow() ||
                grant.ExpiresAt > draft.ExpiresAt)
            {
                throw new UnauthorizedAccessException("The owned Machinery operation grant is invalid or expired.");
            }
            return grant;
        }

        /// <summary>The only supported, no-argument simulator operation.</summary>
        internal static readonly CompanionOperation Operation = new(
            "zero-point-adjustment", "Zero-point adjustment (owned simulation)",
            CompanionOperationSafety.DeploymentMutation)
        {
            Inputs = []
        };

        private readonly MachineryActionSelection m_selection;
        private readonly ICompanionDeploymentPolicy m_policy;
        private readonly TimeProvider m_time;
        private readonly CancellationToken m_lifetime;

        private static readonly ArrayOf<IndustrialCompanionType> s_types =
            [new(ProcessValues.ObjectTypeIds.ProcessValueType, "Process value")];
    }

    /// <summary>
    /// Exact method/source and optional status evidence, without inventing a machinery state transition.
    /// </summary>
    internal sealed record MachineryZeroPointBinding(
        NodeId Signal, NodeId SignalType, NodeId Method, NodeId StatusNode, ushort? Status,
        NodeId SuppressionNode, ushort? Suppression);

    /// <summary>
    /// A single-use prepared adjustment tied to the inspected process and fixed owned-run deadline.
    /// </summary>
    internal sealed class MachineryZeroPointTask : CompanionTaskInput
    {
        /// <summary>
        /// Records already-validated instance metadata without dispatch.
        /// </summary>
        public MachineryZeroPointTask(
            MachineryZeroPointProvider provider, MachineryActionSelection selection,
            MachineryZeroPointBinding binding, DateTimeOffset expires)
        {
            m_provider = provider;
            m_selection = selection;
            Binding = binding;
            Draft = new CompanionOperationDraft(
                selection.Target, MachineryZeroPointProvider.Operation, null, selection.Context.Session, expires, this);
        }

        /// <inheritdoc/>
        public override string Review =>
            $"Selected process: {Draft.Target.DisplayName} ({Draft.Target.NodeId})\n" +
            $"Analog signal: {Binding.Signal}; actual method: {Binding.Method}\n" +
            $"Status: {Binding.Status?.ToString(CultureInfo.InvariantCulture) ?? "not exposed"}; " +
            $"alarm suppression: {Binding.Suppression?.ToString(CultureInfo.InvariantCulture) ?? "not exposed"}.\n" +
            "One owned-simulator ZeroPointAdjustment only. No setpoint write or state-machine command.";

        /// <summary>
        /// Gets the exact instance evidence.
        /// </summary>
        public MachineryZeroPointBinding Binding { get; }

        /// <summary>
        /// Gets the session-bound request used for policy checks.
        /// </summary>
        public CompanionOperationDraft Draft { get; private set; }

        /// <summary>
        /// Gets the fixed owned-run grant.
        /// </summary>
        public CompanionDeploymentGrant? Grant { get; private set; }

        /// <summary>
        /// Shortens the preparation to its owned-run deadline.
        /// </summary>
        public void SetGrant(CompanionDeploymentGrant grant)
        {
            Grant = grant;
            Draft = Draft.WithGrant(grant);
        }

        /// <summary>
        /// Lets the owned policy reject raw, mismatched or untyped machinery requests.
        /// </summary>
        public bool MatchesAuthorization(CompanionOperationDraft draft)
        {
            return ReferenceEquals(draft.TaskInput, this) &&
                draft.Target == Draft.Target &&
                draft.Operation == MachineryZeroPointProvider.Operation &&
                !Binding.Signal.IsNull &&
                !Binding.Method.IsNull;
        }

        /// <summary>
        /// Rechecks selection, peer and fixed preparation expiry.
        /// </summary>
        /// <exception cref="UnauthorizedAccessException"></exception>
        public void RequireCurrent(CompanionContext context, TimeProvider time)
        {
            m_selection.RequireCurrent();
            if (!ReferenceEquals(context.Session, m_selection.Context.Session) ||
                !Draft.Matches(context.Session, time.GetUtcNow()))
            {
                throw new UnauthorizedAccessException(
                    "The prepared Machinery selection or grant is no longer current.");
            }
        }

        /// <summary>
        /// Consumes before checking cancellation or dispatch, including rejected and unknown outcomes.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public void Consume(MachineryZeroPointProvider provider, CompanionTarget target, string operationId)
        {
            if (Interlocked.Exchange(ref m_consumed, 1) != 0)
            {
                throw new InvalidOperationException("The zero-point preparation was consumed. Do not replay it.");
            }
            if (!ReferenceEquals(provider, m_provider) || target != Draft.Target || operationId != Draft.Operation.Id)
            {
                throw new ArgumentException("The zero-point preparation belongs to another selected process task.");
            }
        }

        private readonly MachineryZeroPointProvider m_provider;
        private readonly MachineryActionSelection m_selection;
        private int m_consumed;
    }
}
