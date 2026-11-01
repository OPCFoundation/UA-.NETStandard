/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    internal sealed partial class GenerationReconciler
    {
        /// <summary>
        /// Creates unpublished reconciliation work. Ownership transfers here only after publication.
        /// </summary>
        internal ReconciliationWork CreateReconciliation(
            Func<IDisposable> enterOperation,
            Func<bool>? isUnavailable = null)
        {
            return new ReconciliationWork(this, enterOperation, isUnavailable ?? (() => false));
        }

        /// <summary>
        /// Releases callbacks whose exact owning generation has been withdrawn by a newer publication.
        /// </summary>
        internal void SupersedeReconciliations()
        {
            ReconciliationWork[] pending;
            lock (m_reconciliationLock)
            {
                pending = [.. m_reconciliations];
            }
            foreach (ReconciliationWork work in pending)
            {
                work.Supersede();
            }
        }

        /// <summary>
        /// A result-owned status ticket. It never owns callbacks, managers, gates, or lifecycle operations.
        /// </summary>
        internal sealed class Reconciliation
        {
            /// <summary>
            /// Creates a ticket for server-owned work.
            /// </summary>
            internal Reconciliation(ReconciliationWork work)
            {
                m_work = new WeakReference<ReconciliationWork>(work);
            }

            /// <summary>
            /// Gets an immutable snapshot of current issues.
            /// </summary>
            internal ArrayOf<NodeManagerReconciliationIssue> Issues
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_issues;
                    }
                }
            }

            /// <summary>
            /// Joins one server-owned retry attempt without canceling its execution.
            /// </summary>
            internal ValueTask RetryAsync(CancellationToken cancellationToken)
            {
                if (m_work.TryGetTarget(out ReconciliationWork? work))
                {
                    return new ValueTask(work.RetryAsync().WaitAsync(cancellationToken));
                }
                lock (m_lock)
                {
                    m_issues = (m_issues.ToArray() ?? []).Select(issue => issue.IsRetryable
                        ? new NodeManagerReconciliationIssue(
                            issue.Stage,
                            issue.InitialFailure,
                            new InvalidOperationException("The reconciliation owner is no longer available."),
                            superseded: true)
                        : issue).ToArrayOf();
                }
                return default;
            }

            /// <summary>
            /// Replaces the issue snapshot.
            /// </summary>
            internal void Update(ArrayOf<NodeManagerReconciliationIssue> issues)
            {
                lock (m_lock)
                {
                    m_issues = issues;
                }
            }

            /// <summary>Protects the immutable issue snapshot.</summary>
            private readonly Lock m_lock = new();

            /// <summary>The server-owned callback state, never rooted by the result.</summary>
            private readonly WeakReference<ReconciliationWork> m_work;

            /// <summary>The latest issue snapshot.</summary>
            private ArrayOf<NodeManagerReconciliationIssue> m_issues;
        }

        /// <summary>
        /// Server-owned checkpoints for one committed unit. Successful callbacks are released immediately.
        /// </summary>
        internal sealed class ReconciliationWork
        {
            /// <summary>
            /// Creates work that acquires lifecycle ownership only during an attempt.
            /// </summary>
            internal ReconciliationWork(
                GenerationReconciler owner,
                Func<IDisposable> enterOperation,
                Func<bool> isUnavailable)
            {
                m_owner = owner;
                m_enterOperation = enterOperation;
                m_isUnavailable = isUnavailable;
                Ticket = new Reconciliation(this);
            }

            /// <summary>
            /// Gets the non-owning result ticket.
            /// </summary>
            internal Reconciliation Ticket { get; }

            /// <summary>
            /// Transfers pending callback ownership to the running server after the view swap.
            /// </summary>
            internal void Publish(long revision)
            {
                m_revision = revision;
                lock (m_owner.m_reconciliationLock)
                {
                    m_owner.m_reconciliations.Add(this);
                }
            }

            /// <summary>
            /// Records and attempts a new stage, preserving any original failure.
            /// </summary>
            internal async ValueTask RunAsync(
                NodeManagerReconciliationStage stage,
                NodeManagerGeneration[] generations,
                Func<ValueTask> action,
                bool requireOriginalView = false)
            {
                var step = new Step(stage, generations, action, requireOriginalView);
                lock (m_lock)
                {
                    m_steps.Add(step);
                }
                await ExecuteAsync(step).ConfigureAwait(false);
            }

            /// <summary>
            /// Reports a failure owned by a different cleanup path without inventing a replay callback.
            /// </summary>
            internal void ReportOwnedFailure(NodeManagerReconciliationStage stage, Exception failure)
            {
                lock (m_lock)
                {
                    m_steps.Add(new Step(stage, [], static () => default)
                    {
                        Action = null,
                        InitialFailure = failure,
                        CurrentFailure = failure
                    });
                    UpdateIssues();
                }
            }

            /// <summary>
            /// Completes the initial pass and releases successful work.
            /// </summary>
            internal void Seal()
            {
                lock (m_lock)
                {
                    m_sealed = true;
                    UpdateIssues();
                    ReleaseCompleted();
                }
            }

            /// <summary>
            /// Starts or joins one attempt. No writer is held while callbacks execute.
            /// </summary>
            internal Task RetryAsync()
            {
                TaskCompletionSource<bool> completion;
                lock (m_lock)
                {
                    if (m_attempt is { IsCompleted: false })
                    {
                        return m_attempt;
                    }
                    if (m_steps.All(step => step.Action is null))
                    {
                        return Task.CompletedTask;
                    }
                    completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_attempt = completion.Task;
                }
                _ = Task.Run(() => RetryCoreAsync(completion));
                return completion.Task;
            }

            /// <summary>
            /// Invalidates pending steps without touching executing callbacks or their admissions.
            /// </summary>
            internal void Supersede()
            {
                lock (m_lock)
                {
                    foreach (Step step in m_steps)
                    {
                        if (!step.Executing && step.Action is not null &&
                            IsSuperseded(step.Generations, step.RequireOriginalView))
                        {
                            MarkSuperseded(step);
                        }
                    }
                    UpdateIssues();
                    ReleaseCompleted();
                }
            }

            /// <summary>
            /// Executes the failed checkpoints under fresh operation ownership.
            /// </summary>
            private async Task RetryCoreAsync(TaskCompletionSource<bool> completion)
            {
                try
                {
                    using IDisposable operation = m_enterOperation();
                    Step[] steps;
                    lock (m_lock)
                    {
                        steps = [.. m_steps.Where(step => step.Action is not null)];
                    }
                    foreach (Step step in steps)
                    {
                        await ExecuteAsync(step).ConfigureAwait(false);
                    }
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    lock (m_lock)
                    {
                        foreach (Step step in m_steps.Where(step => step.Action is not null))
                        {
                            step.InitialFailure ??= failure;
                            step.CurrentFailure = failure;
                            if (failure is ObjectDisposedException || m_isUnavailable())
                            {
                                step.Superseded = true;
                                step.Action = null;
                                step.Generations = [];
                            }
                        }
                        UpdateIssues();
                    }
                }
                finally
                {
                    lock (m_lock)
                    {
                        ReleaseCompleted();
                    }
                }
                completion.TrySetResult(true);
            }

            /// <summary>
            /// Attempts one callback while holding only its exact generation gates.
            /// </summary>
            private async ValueTask ExecuteAsync(Step step)
            {
                Func<ValueTask>? action;
                NodeManagerGeneration[] generations;
                lock (m_lock)
                {
                    action = step.Action;
                    generations = step.Generations;
                    step.Executing = action is not null;
                }
                if (action is null)
                {
                    return;
                }
                var admissions = new List<(NodeManagerGeneration Generation, int Slot)>();
                try
                {
                    if (IsSuperseded(generations, step.RequireOriginalView))
                    {
                        lock (m_lock)
                        {
                            MarkSuperseded(step);
                        }
                        return;
                    }
                    foreach (NodeManagerGeneration generation in generations)
                    {
                        if (!generation.Gate.TryEnter(out int slot))
                        {
                            lock (m_lock)
                            {
                                MarkSuperseded(step);
                            }
                            return;
                        }
                        admissions.Add((generation, slot));
                    }
                    await action().ConfigureAwait(false);
                    lock (m_lock)
                    {
                        step.Action = null;
                        step.Generations = [];
                        step.CurrentFailure = null;
                    }
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    lock (m_lock)
                    {
                        step.InitialFailure ??= failure;
                        step.CurrentFailure = failure;
                    }
                }
                finally
                {
                    foreach ((NodeManagerGeneration generation, int slot) in admissions)
                    {
                        generation.Gate.Exit(slot);
                    }
                    lock (m_lock)
                    {
                        step.Executing = false;
                        if (step.Action is not null && IsSuperseded(step.Generations, step.RequireOriginalView))
                        {
                            MarkSuperseded(step);
                        }

                        UpdateIssues();
                    }
                }
            }

            /// <summary>
            /// Checks exact generation ownership and explicitly view-sensitive pending callbacks.
            /// </summary>
            private bool IsSuperseded(NodeManagerGeneration[] generations, bool requireOriginalView = false)
            {
                return IsViewSuperseded(requireOriginalView) ||
                    generations.Any(generation => generation.Gate.State != NodeManagerGenerationState.Live);
            }

            /// <summary>
            /// Checks view-sensitive callbacks independently of already admitted generation retirement.
            /// </summary>
            private bool IsViewSuperseded(bool requireOriginalView)
            {
                return requireOriginalView &&
                    (m_owner.m_server.NodeManager is not IDynamicNodeManagerBatchHost host ||
                        host.RoutingRevision.Revision != m_revision);
            }

            /// <summary>
            /// Releases all manager-reaching state for superseded work.
            /// </summary>
            private static void MarkSuperseded(Step step)
            {
                var failure = new InvalidOperationException(
                    "A newer publication superseded the exact generation required by reconciliation.");
                step.InitialFailure ??= failure;
                step.CurrentFailure = failure;
                step.Superseded = true;
                step.Action = null;
                step.Generations = [];
            }

            /// <summary>
            /// Publishes status separately from callback ownership.
            /// </summary>
            private void UpdateIssues()
            {
                Ticket.Update(m_steps.Where(step => step.CurrentFailure is not null)
                    .Select(step => new NodeManagerReconciliationIssue(
                        step.Stage, step.InitialFailure!, step.CurrentFailure!, step.Superseded,
                        step.Action is not null)).ToArrayOf());
            }

            /// <summary>
            /// Removes terminal work from the server-owned pending set.
            /// </summary>
            private void ReleaseCompleted()
            {
                if (m_sealed && m_steps.All(step => step.Action is null && !step.Executing))
                {
                    lock (m_owner.m_reconciliationLock)
                    {
                        m_owner.m_reconciliations.Remove(this);
                    }
                }
            }

            /// <summary>
            /// A stage checkpoint, owned only by the server, never by a result.
            /// </summary>
            private sealed class Step(
                NodeManagerReconciliationStage stage,
                NodeManagerGeneration[] generations,
                Func<ValueTask> action,
                bool requireOriginalView = false)
            {
                /// <summary>
                /// Gets the stage reported to callers.
                /// </summary>
                internal NodeManagerReconciliationStage Stage { get; } = stage;

                /// <summary>
                /// Gets or sets the exact generations needed by the callback.
                /// </summary>
                internal NodeManagerGeneration[] Generations { get; set; } = generations;

                /// <summary>
                /// Gets or sets the unfinished callback.
                /// </summary>
                internal Func<ValueTask>? Action { get; set; } = action;

                /// <summary>
                /// Gets or sets the first observed failure.
                /// </summary>
                internal Exception? InitialFailure { get; set; }

                /// <summary>
                /// Gets or sets the most recent failure.
                /// </summary>
                internal Exception? CurrentFailure { get; set; }

                /// <summary>
                /// Gets or sets whether withdrawal made the work terminal.
                /// </summary>
                internal bool Superseded { get; set; }

                /// <summary>
                /// Gets or sets whether a callback currently owns its admissions.
                /// </summary>
                internal bool Executing { get; set; }

                /// <summary>
                /// Gets whether a newer view invalidates this application publication callback.
                /// </summary>
                internal bool RequireOriginalView { get; } = requireOriginalView;
            }

            /// <summary>The server-side owner of pending callbacks.</summary>
            private readonly GenerationReconciler m_owner;

            /// <summary>Admits execution independently of mutation serialization.</summary>
            private readonly Func<IDisposable> m_enterOperation;

            /// <summary>Distinguishes shutdown from a retryable admission/context conflict.</summary>
            private readonly Func<bool> m_isUnavailable;

            /// <summary>Protects checkpoints and shared retry admission.</summary>
            private readonly Lock m_lock = new();

            /// <summary>The callback checkpoints.</summary>
            private readonly List<Step> m_steps = [];

            /// <summary>The shared current attempt.</summary>
            private Task? m_attempt;

            /// <summary>Whether initial reconciliation has finished.</summary>
            private bool m_sealed;

            /// <summary>The publication revision for manager-independent callbacks.</summary>
            private long m_revision;

        }

        /// <summary>Protects the server-owned reconciliation set.</summary>
        private readonly Lock m_reconciliationLock = new();

        /// <summary>Only unfinished work remains owned by the server.</summary>
        private readonly List<ReconciliationWork> m_reconciliations = [];
    }
}
