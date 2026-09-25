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
using Opc.Ua.Client;
using UaLens.Connection;

namespace UaLens.Plugins.Companions
{
    /// <summary>
    /// Owns bounded guided-task execution, generation changes and sample-operation
    /// authorization while borrowing, never disposing, the primary connection.
    /// </summary>
    internal sealed class CompanionWorkspace : IAsyncDisposable
    {
        public CompanionWorkspace(
            ArrayOf<ICompanionProvider> providers,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider = null,
            ICompanionDeploymentPolicy? deploymentPolicy = null)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_deploymentPolicy = deploymentPolicy ??
                new ConfiguredCompanionDeploymentPolicy(
                    timeProvider: m_timeProvider);
            var descriptors = new List<CompanionDescriptor>();
            foreach (ICompanionProvider provider in providers)
            {
                ArgumentNullException.ThrowIfNull(provider);
                CompanionDescriptor descriptor = provider.Descriptor;
                if (string.IsNullOrWhiteSpace(descriptor.Id) ||
                    string.IsNullOrWhiteSpace(descriptor.DisplayName) ||
                    !m_providers.TryAdd(descriptor.Id, provider))
                {
                    throw new ArgumentException(
                        "Companion providers require unique, nonempty identities.", nameof(providers));
                }
                descriptors.Add(descriptor);
            }
            Providers = [.. descriptors];
        }

        public ArrayOf<CompanionDescriptor> Providers { get; }

        public Task BindAsync(ISession? session, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ResetAsync(session is null ? null : new CompanionContext(session, m_telemetry));
        }

        public Task<ArrayOf<CompanionTarget>> DiscoverAsync(
            string providerId,
            CancellationToken cancellationToken = default)
        {
            InvalidatePreparation();
            ICompanionProvider provider = ResolveProvider(providerId);
            return RunAsync(
                async (context, token) =>
                {
                    InvalidatePreparation();
                    ArrayOf<CompanionTarget> targets = await provider.DiscoverAsync(context, token)
                        .ConfigureAwait(false);
                    if (targets.Count > context.MaxTargets)
                    {
                        throw new InvalidOperationException("The provider exceeded the discovery limit.");
                    }
                    var identifiers = new HashSet<NodeId>();
                    foreach (CompanionTarget target in targets)
                    {
                        if (target.NodeId.IsNull || target.ProviderId != providerId || !identifiers.Add(target.NodeId))
                        {
                            throw new InvalidOperationException(
                                "The provider returned an invalid or duplicate target.");
                        }
                    }
                    return targets;
                },
                targets =>
                {
                    ClearSelection();
                    foreach (CompanionTarget target in targets)
                    {
                        m_targets.Add(target.NodeId, target);
                    }
                },
                cancellationToken);
        }

        public Task<CompanionInspection> InspectAsync(
            CompanionTarget target,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            ICompanionProvider provider = ResolveProvider(target.ProviderId);
            InvalidatePreparation();
            return RunAsync(
                async (context, token) =>
                {
                    InvalidatePreparation();
                    RequireDiscoveredTarget(target);
                    CompanionInspection inspection = await provider.InspectAsync(context, target, token)
                        .ConfigureAwait(false);
                    ValidateInspection(context, inspection);
                    return inspection;
                },
                inspection =>
                {
                    m_preparedOperation = null;
                    m_inspectedTarget = target;
                    m_inspection = inspection;
                },
                cancellationToken);
        }

        public Task<CompanionOperationDraft> PrepareAsync(
            CompanionTarget target,
            string operationId,
            string? input,
            CancellationToken cancellationToken = default)
        {
            long preparationVersion = InvalidatePreparation();
            ArgumentNullException.ThrowIfNull(target);
            ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
            ValidateInput(input);
            ICompanionProvider provider = ResolveProvider(target.ProviderId);
            return RunAsync(
                async (context, token) =>
                {
                    RequirePreparationVersion(preparationVersion);
                    CompanionOperation operation = RequireInspectedOperation(target, operationId);
                    if (operation.HasTypedInput)
                    {
                        throw new InvalidOperationException("Use the typed task form to prepare this operation.");
                    }
                    if (!context.Session.Connected)
                    {
                        throw new InvalidOperationException("Connect the primary server before preparing a task.");
                    }
                    if (operation.Safety == CompanionOperationSafety.LocalFile && string.IsNullOrWhiteSpace(input))
                    {
                        throw new ArgumentException(
                            "Choose an explicit destination for this file operation.", nameof(input));
                    }
                    var draft = new CompanionOperationDraft(
                        target, operation, input, context.Session, m_timeProvider.GetUtcNow().AddMinutes(5));
                    await RequireCurrentOperationAsync(provider, context, draft, token).ConfigureAwait(false);
                    return await AuthorizeDraftAsync(context, draft, token).ConfigureAwait(false);
                },
                draft =>
                {
                    RequirePreparationVersion(preparationVersion);
                    m_preparedOperation = draft;
                },
                cancellationToken);
        }

        public Task<CompanionOperationDraft> PrepareTaskAsync(
            CompanionTarget target,
            string operationId,
            ArrayOf<CompanionValue> inputs,
            CancellationToken cancellationToken = default)
        {
            long preparationVersion = InvalidatePreparation();
            ArgumentNullException.ThrowIfNull(target);
            ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
            if (ResolveProvider(target.ProviderId) is not IPreparedCompanionProvider provider)
            {
                throw new InvalidOperationException("This provider does not offer typed task preparation.");
            }
            if (inputs.Count > 32)
            {
                throw new ArgumentException("A typed task supports at most 32 input fields.", nameof(inputs));
            }
            ArrayOf<CompanionValue> captured;
            lock (m_gate)
            {
                CompanionContext current = m_context ??
                    throw new InvalidOperationException("Connect before preparing typed task input.");
                captured = CompanionInputContract.Snapshot(inputs, current.Session.MessageContext);
            }
            return RunAsync(
                async (context, token) =>
                {
                    RequirePreparationVersion(preparationVersion);
                    CompanionOperation operation = RequireInspectedOperation(target, operationId);
                    CompanionInputContract.ValidateShape(operation, captured);
                    var snapshot = new CompanionOperationDraft(
                        target, operation, null, context.Session, m_timeProvider.GetUtcNow().AddMinutes(5));
                    await RequireCurrentOperationAsync(provider, context, snapshot, token).ConfigureAwait(false);
                    ArrayOf<CompanionInputSchema> schemas = await CompanionInputContract.ValidateAsync(
                        context, operation, captured, token).ConfigureAwait(false);
                    CompanionTaskInput prepared = await provider.PrepareInputAsync(
                        context, target, operationId,
                        CompanionInputContract.Snapshot(captured, context.Session.MessageContext), token)
                        .ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (!snapshot.Matches(context.Session, m_timeProvider.GetUtcNow()) ||
                        prepared is null ||
                        string.IsNullOrWhiteSpace(prepared.Review) ||
                        prepared.Review.Length > 8192)
                    {
                        throw new InvalidOperationException(
                            "The prepared task expired or returned invalid review evidence.");
                    }
                    var draft = new CompanionOperationDraft(
                        target, operation, null, context.Session, snapshot.ExpiresAt, prepared, captured, schemas);
                    return await AuthorizeDraftAsync(context, draft, token).ConfigureAwait(false);
                },
                draft =>
                {
                    RequirePreparationVersion(preparationVersion);
                    m_preparedOperation = draft;
                },
                cancellationToken);
        }

        public Task<CompanionOperationResult> ExecuteAsync(
            CompanionOperationDraft draft,
            bool confirmLocalSample,
            CancellationToken cancellationToken = default)
        {
            return ExecuteTaskAsync(draft, confirmLocalSample, false, null, cancellationToken);
        }

        public Task<CompanionOperationResult> ExecuteAsync(
            CompanionOperationDraft draft,
            bool confirmLocalSample,
            bool confirmDeployment,
            CancellationToken cancellationToken = default)
        {
            return ExecuteTaskAsync(draft, confirmLocalSample, confirmDeployment, null, cancellationToken);
        }

        public Task<CompanionOperationResult> ExecuteTaskAsync(
            CompanionOperationDraft draft,
            bool confirmLocalSample,
            IProgress<CompanionTaskProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            return ExecuteTaskAsync(draft, confirmLocalSample, false, progress, cancellationToken);
        }

        public Task<CompanionOperationResult> ExecuteTaskAsync(
            CompanionOperationDraft draft,
            bool confirmLocalSample,
            bool confirmDeployment,
            IProgress<CompanionTaskProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(draft);
            ICompanionProvider provider = ResolveProvider(draft.Target.ProviderId);
            lock (m_gate)
            {
                ObjectDisposedException.ThrowIf(m_closed, this);
                if (!ReferenceEquals(m_preparedOperation, draft))
                {
                    throw new InvalidOperationException("Prepare this operation again before running it.");
                }
                // Consume before queueing, including an already-canceled request.
                m_preparedOperation = null;
                m_preparationVersion++;
                return RunAsync(
                    async (context, token) =>
                    {
                        RequireInspectedOperation(draft.Target, draft.Operation.Id);
                        await RequireCurrentOperationAsync(provider, context, draft, token).ConfigureAwait(false);
                        if (draft.Operation.Safety == CompanionOperationSafety.DeploymentMutation)
                        {
                            if (!confirmDeployment || draft.DeploymentGrant is not { } preparedGrant)
                            {
                                throw new UnauthorizedAccessException(
                                    "Confirm this prepared deployment operation before running it.");
                            }
                            CompanionDeploymentGrant grant = await AuthorizeDeploymentAsync(context, draft, token)
                                .ConfigureAwait(false);
                            if (grant.RuleId != preparedGrant.RuleId || grant.Revision != preparedGrant.Revision)
                            {
                                throw new UnauthorizedAccessException(
                                    "The deployment policy changed. Inspect and prepare the operation again.");
                            }
                            await RequireCurrentOperationAsync(provider, context, draft, token).ConfigureAwait(false);
                            ValidateGrant(grant, draft);
                        }
                        return await ExecuteProviderAsync(
                            provider, context, draft.Target, draft.Operation, draft.Input,
                            confirmLocalSample, draft.TaskInput, progress,
                            draft.Operation.Safety == CompanionOperationSafety.DeploymentMutation, token)
                            .ConfigureAwait(false);
                    },
                    commit: null,
                    cancellationToken);
            }
        }

        public Task<CompanionOperationResult> ExecuteAsync(
            CompanionTarget target,
            string operationId,
            string? input,
            bool confirmLocalSample,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
            ValidateInput(input);
            ICompanionProvider provider = ResolveProvider(target.ProviderId);
            InvalidatePreparation();
            return RunAsync(
                (context, token) =>
                {
                    CompanionOperation operation = RequireInspectedOperation(target, operationId);
                    return ExecuteProviderAsync(
                        provider, context, target, operation, input, confirmLocalSample, null, null, false, token);
                },
                commit: null,
                cancellationToken);
        }

        public Task CancelAsync()
        {
            lock (m_gate)
            {
                ObjectDisposedException.ThrowIf(m_closed, this);
                return ResetCore(m_context);
            }
        }

        public void DiscardPreparation()
        {
            InvalidatePreparation();
        }

        public ValueTask DisposeAsync()
        {
            lock (m_gate)
            {
                if (m_disposal is null)
                {
                    m_closed = true;
                    m_context = null;
                    ClearSelection();
                    m_generation++;
                    m_disposal = Task.WhenAll(m_cleanup, DrainAsync(m_generationCancellation, m_tail));
                }
                return new ValueTask(m_disposal);
            }
        }

        private static void ValidateInput(string? input)
        {
            if (input?.Length > 65536)
            {
                throw new ArgumentException("Task input is limited to 65536 characters.", nameof(input));
            }
        }

        private static void ValidateInspection(CompanionContext context, CompanionInspection inspection)
        {
            if (inspection.Values.Count > context.MaxFields || inspection.Operations.Count > 32)
            {
                throw new InvalidOperationException("The provider exceeded the inspection limit.");
            }
            var operations = new HashSet<string>(StringComparer.Ordinal);
            foreach (CompanionOperation operation in inspection.Operations)
            {
                if (string.IsNullOrWhiteSpace(operation.Id) ||
                    !Enum.IsDefined(operation.Safety) ||
                    !operations.Add(operation.Id) ||
                    operation.Inputs.Count > 32)
                {
                    throw new InvalidOperationException("The provider returned an invalid operation.");
                }
                var fields = new HashSet<string>(StringComparer.Ordinal);
                foreach (CompanionInputDefinition input in operation.Inputs)
                {
                    CompanionInputContract.ValidateDefinition(input);
                    if (!fields.Add(input.Name))
                    {
                        throw new InvalidOperationException("The provider returned an invalid input definition.");
                    }
                }
            }
        }

        private static async ValueTask<CompanionOperationResult> ExecuteProviderAsync(
            ICompanionProvider provider,
            CompanionContext context,
            CompanionTarget target,
            CompanionOperation operation,
            string? input,
            bool confirmLocalSample,
            CompanionTaskInput? taskInput,
            IProgress<CompanionTaskProgress>? progress,
            bool deploymentAuthorized,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (operation.Safety == CompanionOperationSafety.DeploymentMutation && !deploymentAuthorized)
            {
                throw new UnauthorizedAccessException(
                    "Deployment operations require a freshly prepared and authorized request.");
            }
            if (operation.Safety == CompanionOperationSafety.SampleMutation &&
                (!confirmLocalSample ||
                    !Uri.TryCreate(context.Session.Endpoint.EndpointUrl, UriKind.Absolute, out Uri? endpoint) ||
                    !endpoint.IsLoopback ||
                    !string.IsNullOrEmpty(endpoint.UserInfo) ||
                    !string.IsNullOrEmpty(endpoint.Fragment)))
            {
                throw new InvalidOperationException(
                    "Sample operations require a loopback endpoint and explicit confirmation for this connection.");
            }
            if (operation.Safety == CompanionOperationSafety.LocalFile &&
                !operation.HasTypedInput &&
                string.IsNullOrWhiteSpace(input))
            {
                throw new ArgumentException("Choose an explicit destination for this file operation.", nameof(input));
            }
            CompanionOperationResult result;
            if (operation.HasTypedInput)
            {
                if (taskInput is null || provider is not IPreparedCompanionProvider preparedProvider)
                {
                    throw new InvalidOperationException("Prepare the typed task form before executing it.");
                }
                result = await preparedProvider.ExecutePreparedAsync(
                    context, target, operation.Id, taskInput, progress, token).ConfigureAwait(false);
            }
            else
            {
                result = await provider.ExecuteAsync(
                    context, target, operation.Id, input, token).ConfigureAwait(false);
            }
            if (result.Values.Count > context.MaxFields || string.IsNullOrWhiteSpace(result.Summary))
            {
                throw new InvalidOperationException("The provider returned an invalid task outcome.");
            }
            return result;
        }

        private async ValueTask<CompanionOperationDraft> AuthorizeDraftAsync(
            CompanionContext context, CompanionOperationDraft draft, CancellationToken cancellationToken)
        {
            if (draft.Operation.Safety != CompanionOperationSafety.DeploymentMutation)
            {
                return draft;
            }
            CompanionDeploymentGrant grant = await AuthorizeDeploymentAsync(context, draft, cancellationToken)
                .ConfigureAwait(false);
            return draft.WithGrant(grant);
        }

        private async ValueTask<CompanionDeploymentGrant> AuthorizeDeploymentAsync(
            CompanionContext context, CompanionOperationDraft draft, CancellationToken cancellationToken)
        {
            if (context.Session.Endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                string.IsNullOrEmpty(context.Session.Endpoint.SecurityPolicyUri) ||
                context.Session.Endpoint.SecurityPolicyUri == SecurityPolicies.None)
            {
                throw new UnauthorizedAccessException("Deployment operations require a signed and encrypted channel.");
            }
            CompanionDeploymentGrant grant = await m_deploymentPolicy.AuthorizeAsync(context, draft, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!draft.Matches(context.Session, m_timeProvider.GetUtcNow()))
            {
                throw new UnauthorizedAccessException("The prepared deployment session changed or expired.");
            }
            ValidateGrant(grant, draft);
            return grant;
        }

        private void ValidateGrant(CompanionDeploymentGrant grant, CompanionOperationDraft draft)
        {
            if (grant is null || grant.ExpiresAt <= m_timeProvider.GetUtcNow() || grant.ExpiresAt > draft.ExpiresAt)
            {
                throw new UnauthorizedAccessException("The deployment policy returned an invalid or expired grant.");
            }
            ConnectionReference.Validate(grant.RuleId);
            ConnectionReference.Validate(grant.Revision);
        }

        private long InvalidatePreparation()
        {
            lock (m_gate)
            {
                m_preparedOperation = null;
                return ++m_preparationVersion;
            }
        }

        private void RequirePreparationVersion(long version)
        {
            lock (m_gate)
            {
                if (version != m_preparationVersion)
                {
                    throw new OperationCanceledException(
                        "The task preparation was superseded. Prepare the task again.");
                }
            }
        }

        private async ValueTask RequireCurrentOperationAsync(
            ICompanionProvider provider,
            CompanionContext context,
            CompanionOperationDraft draft,
            CancellationToken cancellationToken)
        {
            if (!draft.Matches(context.Session, m_timeProvider.GetUtcNow()))
            {
                throw new InvalidOperationException(
                    "The prepared session changed or expired. Inspect and prepare again.");
            }
            CompanionInspection current = await provider.InspectAsync(
                context, draft.Target, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateInspection(context, current);
            if (!draft.Matches(context.Session, m_timeProvider.GetUtcNow()) ||
                !current.Operations.Contains(operation => operation == draft.Operation))
            {
                throw new InvalidOperationException("The operation or session changed. Inspect and prepare again.");
            }
            if (!draft.InputSchemas.IsNull)
            {
                ArrayOf<CompanionInputSchema> schemas = await CompanionInputContract.ValidateAsync(
                    context, draft.Operation, draft.Inputs, cancellationToken).ConfigureAwait(false);
                if (schemas != draft.InputSchemas || !draft.Matches(context.Session, m_timeProvider.GetUtcNow()))
                {
                    throw new InvalidOperationException(
                        "The input metadata or session changed. Inspect and prepare again.");
                }
            }
        }

        private Task ResetAsync(CompanionContext? context)
        {
            lock (m_gate)
            {
                ObjectDisposedException.ThrowIf(m_closed, this);
                return ResetCore(context);
            }
        }

        private Task ResetCore(CompanionContext? context)
        {
            CancellationTokenSource previous = m_generationCancellation;
            m_generationCancellation = new CancellationTokenSource();
            m_generation++;
            m_context = context;
            ClearSelection();
            m_cleanup = Task.WhenAll(m_cleanup, DrainAsync(previous, m_tail));
            return m_cleanup;
        }

        private static async Task DrainAsync(CancellationTokenSource cancellation, Task pending)
        {
            await Task.Yield();
            try
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
            finally
            {
                await pending.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                cancellation.Dispose();
            }
        }

        private Task<TResult> RunAsync<TResult>(
            Func<CompanionContext, CancellationToken, ValueTask<TResult>> operation,
            Action<TResult>? commit,
            CancellationToken cancellationToken)
        {
            lock (m_gate)
            {
                ObjectDisposedException.ThrowIf(m_closed, this);
                cancellationToken.ThrowIfCancellationRequested();
                CompanionContext context = m_context ??
                    throw new InvalidOperationException("Connect the primary server before running a companion task.");
                Task<TResult> work = RunCoreAsync(
                    m_tail, context, m_generation, operation, commit, m_generationCancellation
                        .Token, cancellationToken);
                m_tail = work;
                return work;
            }
        }

        private async Task<TResult> RunCoreAsync<TResult>(
            Task previous,
            CompanionContext context,
            long generation,
            Func<CompanionContext, CancellationToken, ValueTask<TResult>> operation,
            Action<TResult>? commit,
            CancellationToken generationToken,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            // Each caller observes its own failure; one failed task must not poison the queue.
            await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                generationToken,
                cancellationToken);
            cancellation.Token.ThrowIfCancellationRequested();
            TResult result = await operation(context, cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (generation != m_generation || m_closed)
                {
                    throw new OperationCanceledException("The connection changed while the task was running.");
                }
                commit?.Invoke(result);
            }
            return result;
        }

        private ICompanionProvider ResolveProvider(string providerId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
            return m_providers.TryGetValue(providerId, out ICompanionProvider? provider)
                ? provider
                : throw new ArgumentException("The companion provider is not registered.", nameof(providerId));
        }

        private void RequireDiscoveredTarget(CompanionTarget target)
        {
            lock (m_gate)
            {
                if (!m_targets.TryGetValue(target.NodeId, out CompanionTarget? known) || known != target)
                {
                    throw new InvalidOperationException("Discover the target on the current connection first.");
                }
            }
        }

        private CompanionOperation RequireInspectedOperation(CompanionTarget target, string operationId)
        {
            lock (m_gate)
            {
                if (m_inspectedTarget == target && m_inspection is not null)
                {
                    foreach (CompanionOperation operation in m_inspection.Operations)
                    {
                        if (operation.Id == operationId)
                        {
                            return operation;
                        }
                    }
                }
                throw new InvalidOperationException("Inspect the target again before running this operation.");
            }
        }

        private void ClearSelection()
        {
            m_targets.Clear();
            m_inspectedTarget = null;
            m_inspection = null;
            InvalidatePreparation();
        }

        private readonly Lock m_gate = new();
        private readonly Dictionary<string, ICompanionProvider> m_providers = new(StringComparer.Ordinal);
        private readonly Dictionary<NodeId, CompanionTarget> m_targets = [];
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_timeProvider;
        private readonly ICompanionDeploymentPolicy m_deploymentPolicy;
        private CancellationTokenSource m_generationCancellation = new();
        private CompanionContext? m_context;
        private CompanionTarget? m_inspectedTarget;
        private CompanionInspection? m_inspection;
        private CompanionOperationDraft? m_preparedOperation;
        private long m_generation;
        private long m_preparationVersion;
        private Task m_tail = Task.CompletedTask;
        private Task m_cleanup = Task.CompletedTask;
        private Task? m_disposal;
        private bool m_closed;
    }
}
