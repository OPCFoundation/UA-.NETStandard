// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Registers generation-owned event streams with the local notifier
    /// lifecycle. A stream is enumerated only while its notifier or an
    /// ancestor has consumers, and cancellation ends that activation.
    /// </summary>
    public interface IWotProjectionEventPublisher
    {
        /// <summary>
        /// Registers one stream for a notifier. Root registration connects the
        /// notifier chain to Server-level subscribers.
        /// </summary>
        void Register(
            INodeManagerBuilder builder,
            BaseObjectState notifier,
            Func<CancellationToken, IAsyncEnumerable<BaseEventState>> source,
            bool registerAsRootNotifier);
    }

    /// <summary>
    /// A native EventNotifier publisher whose registering node manager exposes
    /// generated out-of-band metadata and requires captured Condition actions.
    /// Headless/custom consumers retain the original publisher contract.
    /// </summary>
    public interface IWotNativeProjectionEventPublisher : IWotProjectionEventPublisher
    {
    }

    /// <summary>
    /// Uses the existing fluent event-source registry, including ancestor
    /// monitoring, cancellation, reporting and error telemetry.
    /// </summary>
    public sealed class WotProjectionEventPublisher : IWotNativeProjectionEventPublisher
    {
        /// <inheritdoc/>
        public void Register(
            INodeManagerBuilder builder,
            BaseObjectState notifier,
            Func<CancellationToken, IAsyncEnumerable<BaseEventState>> source,
            bool registerAsRootNotifier)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (notifier is null)
            {
                throw new ArgumentNullException(nameof(notifier));
            }
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            builder.Node<BaseObjectState>(notifier.NodeId).Publish(
                (_, _, token) => source(token),
                new EventPublishOptions
                {
                    SkipDefaultPopulation = true,
                    RegisterAsRootNotifier = registerAsRootNotifier
                });
        }
    }

    /// <summary>
    /// Bounds retained by each projection runtime generation.
    /// </summary>
    public sealed record WotProjectionBindingRuntimeOptions
    {
        /// <summary>
        /// Gets the maximum pending notifications per local notifier.
        /// Overflow faults the source rather than dropping an occurrence.
        /// </summary>
        public int MaxQueuedEvents { get; init; } = 1024;

        /// <summary>
        /// Gets the maximum pending observation updates per projected variable.
        /// Overflow faults the source instead of silently discarding values.
        /// </summary>
        public int MaxQueuedPropertyValues { get; init; } = 1024;

        /// <summary>
        /// Gets the maximum accepted occurrences per event declaration in one
        /// generation, including private identity, provenance and action routes.
        /// Exhaustion faults availability with BadTooManyOperations rather than
        /// evicting evidence. Exact replays do not consume another slot.
        /// </summary>
        /// <remarks>
        /// Evidence remains owned until generation disposal after lifecycle drain.
        /// Recovery requires explicit generation replacement or removal; restarting
        /// a subscription does not reset the budget. The same limit separately
        /// bounds the number of materialized source Condition instances.
        /// </remarks>
        public int MaxEventRoutes { get; init; } = 4096;
    }
}
