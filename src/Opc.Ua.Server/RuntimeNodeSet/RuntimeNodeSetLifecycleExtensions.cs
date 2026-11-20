/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.RuntimeNodeSet
{
    /// <summary>
    /// Live lifecycle extensions for runtime NodeSet-backed NodeManagers.
    /// </summary>
    public static class RuntimeNodeSetLifecycleExtensions
    {
        /// <summary>
        /// Loads and publishes a runtime NodeSet on a running server.
        /// </summary>
        /// <param name="lifecycle">The lifecycle provider of the running server.</param>
        /// <param name="options">The options describing the NodeSet to load.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="lifecycle"/> or <paramref name="options"/> is <c>null</c>.
        /// </exception>
        [Obsolete("Prepare NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(options)), then commit.")]
        public static ValueTask<NodeManagerRegistration> AddRuntimeNodeSetAsync(
            this INodeManagerLifecycle lifecycle,
            RuntimeNodeSetOptions options,
            CancellationToken ct = default)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return NodeManagerLifecycleExtensions.CommitRegistrationAsync(
                lifecycle, NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(options)), ct);
        }

        /// <summary>
        /// Loads and publishes a runtime NodeSet on a running server.
        /// </summary>
        /// <param name="lifecycle">The lifecycle provider of the running server.</param>
        /// <param name="options">The options describing the NodeSet to load.</param>
        /// <param name="callerContext">The unused legacy request context.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="lifecycle"/> or <paramref name="options"/> is <c>null</c>.
        /// </exception>
        [Obsolete("Prepare a RuntimeNodeSetNodeManagerFactory and commit; the request context is no longer required.")]
        public static ValueTask<NodeManagerRegistration> AddRuntimeNodeSetAsync(
            this INodeManagerLifecycle lifecycle,
            RuntimeNodeSetOptions options,
            IOperationContext? callerContext,
            CancellationToken ct = default)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            return NodeManagerLifecycleExtensions.CommitRegistrationAsync(
                lifecycle, NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(options)), ct);
        }

        /// <summary>
        /// Reloads a live runtime NodeSet registration from replacement options.
        /// </summary>
        /// <param name="lifecycle">The lifecycle provider of the running server.</param>
        /// <param name="registration">The registration to replace.</param>
        /// <param name="replacement">The options describing the replacement NodeSet.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="lifecycle"/>, <paramref name="registration"/>, or
        /// <paramref name="replacement"/> is <c>null</c>.
        /// </exception>
        [Obsolete("Prepare NodeManagerBatchChange.Replace with a RuntimeNodeSetNodeManagerFactory, then commit.")]
        public static ValueTask<NodeManagerRegistration> ReloadRuntimeNodeSetAsync(
            this INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            RuntimeNodeSetOptions replacement,
            CancellationToken ct = default)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }
            if (replacement is null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            return NodeManagerLifecycleExtensions.CommitRegistrationAsync(
                lifecycle,
                NodeManagerBatchChange.Replace(registration, new RuntimeNodeSetNodeManagerFactory(replacement)),
                ct);
        }

        /// <summary>
        /// Reloads a live runtime NodeSet registration from replacement options.
        /// </summary>
        /// <param name="lifecycle">The lifecycle provider of the running server.</param>
        /// <param name="registration">The registration to replace.</param>
        /// <param name="replacement">The options describing the replacement NodeSet.</param>
        /// <param name="callerContext">The unused legacy request context.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="lifecycle"/>, <paramref name="registration"/>, or
        /// <paramref name="replacement"/> is <c>null</c>.
        /// </exception>
        [Obsolete("Prepare a RuntimeNodeSetNodeManagerFactory and commit; the request context is no longer required.")]
        public static ValueTask<NodeManagerRegistration> ReloadRuntimeNodeSetAsync(
            this INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            RuntimeNodeSetOptions replacement,
            IOperationContext? callerContext,
            CancellationToken ct = default)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }
            if (replacement is null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }
            return NodeManagerLifecycleExtensions.CommitRegistrationAsync(
                lifecycle,
                NodeManagerBatchChange.Replace(registration, new RuntimeNodeSetNodeManagerFactory(replacement)),
                ct);
        }

        /// <summary>
        /// Replaces a live runtime NodeSet registration using the <c>Migrate</c> transition.
        /// </summary>
        /// <remarks>
        /// Compatible monitored items move to the replacement; incompatible items are invalidated.
        /// A request already pinned to the previous routing view may finish against that view,
        /// but the previous generation is not retained for future requests, items, or continuations.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="lifecycle"/>, <paramref name="registration"/>, or
        /// <paramref name="replacement"/> is <c>null</c>.
        /// </exception>
        [Obsolete("Prepare NodeManagerBatchChange.Replace with Migrate and a RuntimeNodeSetNodeManagerFactory.")]
        public static ValueTask<NodeManagerRegistration> ShadowReloadRuntimeNodeSetAsync(
            this INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            RuntimeNodeSetOptions replacement,
            CancellationToken ct = default)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }
            if (replacement is null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            return NodeManagerLifecycleExtensions.CommitRegistrationAsync(
                lifecycle,
                NodeManagerBatchChange.Replace(registration, new RuntimeNodeSetNodeManagerFactory(replacement)),
                ct);
        }

        /// <summary>
        /// Replaces a live runtime NodeSet registration and immediately invalidates
        /// monitored items owned by the previous generation with
        /// <see cref="StatusCodes.BadNodeIdUnknown"/>.
        /// </summary>
        [Obsolete("Prepare NodeManagerBatchChange.Replace with Invalidate and a RuntimeNodeSetNodeManagerFactory.")]
        public static ValueTask<NodeManagerRegistration> ImmediateReloadRuntimeNodeSetAsync(
            this INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            RuntimeNodeSetOptions replacement,
            CancellationToken ct = default)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }
            if (replacement is null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            return NodeManagerLifecycleExtensions.CommitRegistrationAsync(
                lifecycle,
                NodeManagerBatchChange.Replace(
                    registration,
                    new RuntimeNodeSetNodeManagerFactory(replacement),
                    NodeManagerMonitoredItemTransitionMode.Invalidate),
                ct);
        }
    }
}
