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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// One-release source compatibility for the former single-operation lifecycle API.
    /// Every operation prepares and commits through <see cref="INodeManagerLifecycle"/>.
    /// Legacy request contexts are ignored.
    /// </summary>
    public static class NodeManagerLifecycleExtensions
    {
        /// <summary>
        /// Adds an asynchronous factory through a prepared transaction.
        /// </summary>
        [Obsolete("Use PrepareAsync with NodeManagerBatchChange.Add, then CommitAsync.")]
        public static ValueTask<NodeManagerRegistration> AddAsync(
            this INodeManagerLifecycle lifecycle,
            IAsyncNodeManagerFactory factory,
            CancellationToken ct = default)
        {
            return CommitRegistrationAsync(lifecycle, NodeManagerBatchChange.Add(factory), ct);
        }

        /// <summary>
        /// Adds a synchronous factory through the asynchronous manager adapter.
        /// </summary>
        [Obsolete("Use an IAsyncNodeManagerFactory with PrepareAsync, then CommitAsync.")]
        public static ValueTask<NodeManagerRegistration> AddAsync(
            this INodeManagerLifecycle lifecycle,
            INodeManagerFactory factory,
            CancellationToken ct = default)
        {
            return CommitRegistrationAsync(
                lifecycle, NodeManagerBatchChange.Add(new SyncFactoryAdapter(factory)), ct);
        }

        /// <summary>
        /// Adds an asynchronous factory, ignoring the legacy request context.
        /// </summary>
        [Obsolete("Use PrepareAsync with NodeManagerBatchChange.Add, then CommitAsync; no context is required.")]
        public static ValueTask<NodeManagerRegistration> AddAsync(
            this INodeManagerLifecycle lifecycle,
            IAsyncNodeManagerFactory factory,
            IOperationContext? callerContext,
            CancellationToken ct = default)
        {
            return CommitRegistrationAsync(lifecycle, NodeManagerBatchChange.Add(factory), ct);
        }

        /// <summary>
        /// Adds a synchronous factory, ignoring the legacy request context.
        /// </summary>
        [Obsolete("Use an IAsyncNodeManagerFactory with PrepareAsync, then CommitAsync; no context is required.")]
        public static ValueTask<NodeManagerRegistration> AddAsync(
            this INodeManagerLifecycle lifecycle,
            INodeManagerFactory factory,
            IOperationContext? callerContext,
            CancellationToken ct = default)
        {
            return CommitRegistrationAsync(
                lifecycle, NodeManagerBatchChange.Add(new SyncFactoryAdapter(factory)), ct);
        }

        /// <summary>
        /// Replaces a registration using the Migrate transition.
        /// </summary>
        [Obsolete("Use PrepareAsync with NodeManagerBatchChange.Replace, then CommitAsync.")]
        public static ValueTask<NodeManagerRegistration> ReloadAsync(
            this INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            IAsyncNodeManagerFactory replacement,
            CancellationToken ct = default)
        {
            return CommitRegistrationAsync(
                lifecycle,
                NodeManagerBatchChange.Replace(
                    factory: RequireReplacement(replacement), current: RequireRegistration(registration)),
                ct);
        }

        /// <summary>
        /// Replaces a registration using the synchronous factory adapter and Migrate transition.
        /// </summary>
        [Obsolete("Use an IAsyncNodeManagerFactory with PrepareAsync, then CommitAsync.")]
        public static ValueTask<NodeManagerRegistration> ReloadAsync(
            this INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
            CancellationToken ct = default)
        {
            return CommitRegistrationAsync(
                lifecycle,
                NodeManagerBatchChange.Replace(
                    factory: AdaptReplacement(replacement), current: RequireRegistration(registration)),
                ct);
        }

        /// <summary>
        /// Replaces a registration using Migrate, ignoring the legacy request context.
        /// </summary>
        [Obsolete("Use PrepareAsync with NodeManagerBatchChange.Replace, then CommitAsync; no context is required.")]
        public static ValueTask<NodeManagerRegistration> ReloadAsync(
            this INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            IAsyncNodeManagerFactory replacement,
            IOperationContext? callerContext,
            CancellationToken ct = default)
        {
            return CommitRegistrationAsync(
                lifecycle,
                NodeManagerBatchChange.Replace(
                    factory: RequireReplacement(replacement), current: RequireRegistration(registration)),
                ct);
        }

        /// <summary>
        /// Replaces a synchronous factory using Migrate, ignoring the legacy request context.
        /// </summary>
        [Obsolete("Use an IAsyncNodeManagerFactory with PrepareAsync, then CommitAsync; no context is required.")]
        public static ValueTask<NodeManagerRegistration> ReloadAsync(
            this INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
            IOperationContext? callerContext,
            CancellationToken ct = default)
        {
            return CommitRegistrationAsync(
                lifecycle,
                NodeManagerBatchChange.Replace(
                    factory: AdaptReplacement(replacement), current: RequireRegistration(registration)),
                ct);
        }

        /// <summary>
        /// Replaces a registration using the Migrate transition.
        /// </summary>
        [Obsolete("Use PrepareAsync with NodeManagerBatchChange.Replace and Migrate, then CommitAsync.")]
        public static ValueTask<NodeManagerRegistration> ShadowReloadAsync(
            this INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            IAsyncNodeManagerFactory replacement,
            CancellationToken ct = default)
        {
            return CommitRegistrationAsync(
                lifecycle,
                NodeManagerBatchChange.Replace(
                    factory: RequireReplacement(replacement), current: RequireRegistration(registration)),
                ct);
        }

        /// <summary>
        /// Replaces a synchronous factory using the Migrate transition.
        /// </summary>
        [Obsolete("Use an IAsyncNodeManagerFactory with PrepareAsync and Migrate, then CommitAsync.")]
        public static ValueTask<NodeManagerRegistration> ShadowReloadAsync(
            this INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
            CancellationToken ct = default)
        {
            return CommitRegistrationAsync(
                lifecycle,
                NodeManagerBatchChange.Replace(
                    factory: AdaptReplacement(replacement), current: RequireRegistration(registration)),
                ct);
        }

        /// <summary>
        /// Replaces a registration using the Invalidate transition.
        /// </summary>
        [Obsolete("Use PrepareAsync with NodeManagerBatchChange.Replace and Invalidate, then CommitAsync.")]
        public static ValueTask<NodeManagerRegistration> ImmediateReloadAsync(
            this INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            IAsyncNodeManagerFactory replacement,
            CancellationToken ct = default)
        {
            return CommitRegistrationAsync(
                lifecycle,
                NodeManagerBatchChange.Replace(
                    factory: RequireReplacement(replacement),
                    current: RequireRegistration(registration),
                    transitionMode: NodeManagerMonitoredItemTransitionMode.Invalidate),
                ct);
        }

        /// <summary>
        /// Replaces a synchronous factory using the Invalidate transition.
        /// </summary>
        [Obsolete("Use an IAsyncNodeManagerFactory with PrepareAsync and Invalidate, then CommitAsync.")]
        public static ValueTask<NodeManagerRegistration> ImmediateReloadAsync(
            this INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
            CancellationToken ct = default)
        {
            return CommitRegistrationAsync(
                lifecycle,
                NodeManagerBatchChange.Replace(
                    factory: AdaptReplacement(replacement),
                    current: RequireRegistration(registration),
                    transitionMode: NodeManagerMonitoredItemTransitionMode.Invalidate),
                ct);
        }

        /// <summary>
        /// Removes a registration without waiting for asynchronous generation retirement.
        /// </summary>
        [Obsolete("Use PrepareAsync with NodeManagerBatchChange.Remove, then CommitAsync.")]
        public static ValueTask RemoveAsync(
            this INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            CancellationToken ct = default)
        {
            return CommitRemovalAsync(lifecycle, registration, ct);
        }

        /// <summary>
        /// Removes a registration, ignoring the legacy request context.
        /// </summary>
        [Obsolete("Use PrepareAsync with NodeManagerBatchChange.Remove, then CommitAsync; no context is required.")]
        public static ValueTask RemoveAsync(
            this INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            IOperationContext? callerContext,
            CancellationToken ct = default)
        {
            return CommitRemovalAsync(lifecycle, registration, ct);
        }

        /// <summary>
        /// Publishes one addition or replacement and preserves typed committed-failure semantics.
        /// </summary>
        internal static async ValueTask<NodeManagerRegistration> CommitRegistrationAsync(
            INodeManagerLifecycle lifecycle,
            NodeManagerBatchChange change,
            CancellationToken ct)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }

            IPreparedNodeManagerBatch prepared =
                await lifecycle.PrepareAsync([change], ct).ConfigureAwait(false);
            await using var preparedLifetime = prepared.ConfigureAwait(false);
            NodeManagerBatchResult result =
                await prepared.CommitAsync(static _ => default, ct).ConfigureAwait(false);
            ObserveRetirement(result.Retirement);
            NodeManagerRegistration registration = result.Registrations[0];
            if (result.CleanupFailure is { } failure)
            {
                failure = UnwrapFailure(failure);
                throw change.Current is null
                    ? new NodeManagerAddCommittedException(
                        registration,
                        result,
                        "The NodeManager was added, but post-commit binding, readiness or notification failed. " +
                        "The published generation was retained and remains available from Registrations.",
                        failure)
                    : new NodeManagerReloadCommittedException(
                        registration,
                        result,
                        "The replacement NodeManager is live and the replacement generation was retained, but " +
                        "post-commit reconciliation failed.",
                        failure);
            }
            return registration;
        }

        /// <summary>
        /// Publishes one removal and preserves the committed result if reconciliation fails.
        /// </summary>
        internal static async ValueTask CommitRemovalAsync(
            INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            CancellationToken ct)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }

            IPreparedNodeManagerBatch prepared = await lifecycle
                .PrepareAsync([NodeManagerBatchChange.Remove(RequireRegistration(registration))], ct)
                .ConfigureAwait(false);
            await using var preparedLifetime = prepared.ConfigureAwait(false);
            NodeManagerBatchResult result =
                await prepared.CommitAsync(static _ => default, ct).ConfigureAwait(false);
            ObserveRetirement(result.Retirement);
            if (result.CleanupFailure is { } failure)
            {
                throw new NodeManagerRemovalCommittedException(registration, result, failure);
            }
        }

        /// <summary>
        /// Observes asynchronous compatibility-path retirement without waiting for request drain.
        /// </summary>
        private static void ObserveRetirement(Task retirement)
        {
            _ = retirement.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Preserves the original exception for a single reconciliation failure.
        /// </summary>
        private static Exception UnwrapFailure(Exception failure)
        {
            return failure is AggregateException aggregate && aggregate.Flatten().InnerExceptions.Count == 1
                ? aggregate.Flatten().InnerExceptions[0]
                : failure;
        }

        /// <summary>
        /// Preserves the former replacement argument name in compatibility validation failures.
        /// </summary>
        private static IAsyncNodeManagerFactory RequireReplacement(IAsyncNodeManagerFactory replacement)
        {
            return replacement ?? throw new ArgumentNullException(nameof(replacement));
        }

        /// <summary>
        /// Preserves the former registration argument name in compatibility validation failures.
        /// </summary>
        private static NodeManagerRegistration RequireRegistration(NodeManagerRegistration registration)
        {
            return registration ?? throw new ArgumentNullException(nameof(registration));
        }

        /// <summary>
        /// Adapts a synchronous replacement while preserving its compatibility argument name.
        /// </summary>
        private static SyncFactoryAdapter AdaptReplacement(INodeManagerFactory replacement)
        {
            return new SyncFactoryAdapter(replacement ?? throw new ArgumentNullException(nameof(replacement)));
        }

        /// <summary>
        /// Adapts legacy factory creation to the canonical asynchronous preparation contract.
        /// </summary>
        private sealed class SyncFactoryAdapter(INodeManagerFactory factory) : IAsyncNodeManagerFactory
        {
            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris => m_factory.NamespacesUris;

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return new ValueTask<IAsyncNodeManager>(
                    m_factory.Create(server, configuration).ToAsyncNodeManager());
            }

            private readonly INodeManagerFactory m_factory = factory
                ?? throw new ArgumentNullException(nameof(factory));
        }
    }
}
