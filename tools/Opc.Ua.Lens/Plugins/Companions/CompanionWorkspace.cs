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

namespace UaLens.Plugins.Companions;

/// <summary>
/// Owns bounded guided-task execution, generation changes and sample-operation
/// authorization while borrowing, never disposing, the primary connection.
/// </summary>
internal sealed class CompanionWorkspace : IAsyncDisposable
{
    public CompanionWorkspace(ArrayOf<ICompanionProvider> providers, ITelemetryContext telemetry)
    {
        m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        var descriptors = new List<CompanionDescriptor>();
        foreach (ICompanionProvider provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            CompanionDescriptor descriptor = provider.Descriptor;
            if (string.IsNullOrWhiteSpace(descriptor.Id) ||
                string.IsNullOrWhiteSpace(descriptor.DisplayName) ||
                !m_providers.TryAdd(descriptor.Id, provider))
            {
                throw new ArgumentException("Companion providers require unique, nonempty identities.", nameof(providers));
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
        ICompanionProvider provider = ResolveProvider(providerId);
        return RunAsync(
            async (context, token) =>
            {
                ArrayOf<CompanionTarget> targets = await provider.DiscoverAsync(context, token).ConfigureAwait(false);
                if (targets.Count > context.MaxTargets)
                {
                    throw new InvalidOperationException("The provider exceeded the discovery limit.");
                }
                var identifiers = new HashSet<NodeId>();
                foreach (CompanionTarget target in targets)
                {
                    if (target.NodeId.IsNull || target.ProviderId != providerId || !identifiers.Add(target.NodeId))
                    {
                        throw new InvalidOperationException("The provider returned an invalid or duplicate target.");
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
        return RunAsync(
            async (context, token) =>
            {
                RequireDiscoveredTarget(target);
                CompanionInspection inspection = await provider.InspectAsync(context, target, token)
                    .ConfigureAwait(false);
                if (inspection.Values.Count > context.MaxFields || inspection.Operations.Count > 32)
                {
                    throw new InvalidOperationException("The provider exceeded the inspection limit.");
                }
                var operations = new HashSet<string>(StringComparer.Ordinal);
                foreach (CompanionOperation operation in inspection.Operations)
                {
                    if (string.IsNullOrWhiteSpace(operation.Id) ||
                        !Enum.IsDefined(operation.Safety) ||
                        !operations.Add(operation.Id))
                    {
                        throw new InvalidOperationException("The provider returned an invalid operation.");
                    }
                }
                return inspection;
            },
            inspection =>
            {
                m_inspectedTarget = target;
                m_inspection = inspection;
            },
            cancellationToken);
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
        if (input?.Length > 65536)
        {
            throw new ArgumentException("Task input is limited to 65536 characters.", nameof(input));
        }
        ICompanionProvider provider = ResolveProvider(target.ProviderId);
        return RunAsync(
            async (context, token) =>
            {
                CompanionOperation operation = RequireInspectedOperation(target, operationId);
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
                if (operation.Safety == CompanionOperationSafety.LocalFile && string.IsNullOrWhiteSpace(input))
                {
                    throw new ArgumentException("Choose an explicit destination for this file operation.", nameof(input));
                }
                CompanionOperationResult result = await provider.ExecuteAsync(
                    context, target, operationId, input, token).ConfigureAwait(false);
                if (result.Values.Count > context.MaxFields || string.IsNullOrWhiteSpace(result.Summary))
                {
                    throw new InvalidOperationException("The provider returned an invalid task outcome.");
                }
                return result;
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
                m_tail, context, m_generation, operation, commit, m_generationCancellation.Token, cancellationToken);
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
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(generationToken, cancellationToken);
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
    }

    private readonly Lock m_gate = new();
    private readonly Dictionary<string, ICompanionProvider> m_providers = new(StringComparer.Ordinal);
    private readonly Dictionary<NodeId, CompanionTarget> m_targets = [];
    private readonly ITelemetryContext m_telemetry;
    private CancellationTokenSource m_generationCancellation = new();
    private CompanionContext? m_context;
    private CompanionTarget? m_inspectedTarget;
    private CompanionInspection? m_inspection;
    private long m_generation;
    private Task m_tail = Task.CompletedTask;
    private Task m_cleanup = Task.CompletedTask;
    private Task? m_disposal;
    private bool m_closed;
}
