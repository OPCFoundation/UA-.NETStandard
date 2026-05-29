/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Extension methods that register an external event source against a
    /// notifier node resolved by the fluent <see cref="INodeBuilder"/>
    /// surface. The source's items are delivered through
    /// <see cref="NodeState.ReportEvent"/> on the notifier, so monitored
    /// items on the notifier (or on an ancestor that the notifier is
    /// reachable from via inverse <c>HasNotifier</c> references) receive
    /// the events using the standard OPC UA event-dispatch path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The extensions require the manager being wired to derive from
    /// <see cref="FluentNodeManagerBase"/> (the
    /// <see cref="EventSourceRegistry"/> is attached during the manager's
    /// startup). Calling <c>Publish</c> on a builder backed by a manager
    /// that does not opt in throws
    /// <see cref="ServiceResultException"/> with
    /// <see cref="StatusCodes.BadConfigurationError"/>.
    /// </para>
    /// <para>
    /// By default each registered source is lazy: the iterator only runs
    /// while at least one monitored item is interested in the notifier.
    /// See <see cref="EventPublishOptions"/> for tuning.
    /// </para>
    /// </remarks>
    public static class EventNotifierBuilderExtensions
    {
        /// <summary>
        /// Registers <paramref name="factory"/> as the event source for
        /// the resolved notifier. <paramref name="factory"/> is invoked
        /// each time the source activates (lazy default activates on the
        /// first monitored-item subscription; eager activation under
        /// <see cref="EventPublishOptions.AlwaysOn"/> activates once at
        /// builder seal time). The factory receives the notifier node,
        /// the manager's <see cref="ISystemContext"/>, and a
        /// <see cref="CancellationToken"/> that the iterator is required
        /// to honor.
        /// </summary>
        /// <typeparam name="TNotifier">
        /// Notifier node type. Constrained to
        /// <see cref="BaseObjectState"/> because only object nodes carry
        /// the <c>EventNotifier</c> attribute.
        /// </typeparam>
        /// <typeparam name="TEvent">
        /// Event payload type. Must derive from
        /// <see cref="BaseEventState"/>; the registry covariantly streams
        /// the items as <c>BaseEventState</c> through
        /// <c>ReportEvent</c>.
        /// </typeparam>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="nodeBuilder"/> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="factory"/> is null.
        /// </exception>
        public static INodeBuilder<TNotifier> Publish<TNotifier, TEvent>(
            this INodeBuilder<TNotifier> nodeBuilder,
            Func<TNotifier, ISystemContext, CancellationToken, IAsyncEnumerable<TEvent>> factory,
            EventPublishOptions? options = null)
            where TNotifier : BaseObjectState
            where TEvent : BaseEventState
        {
            if (nodeBuilder == null)
            {
                throw new ArgumentNullException(nameof(nodeBuilder));
            }
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            EventSourceRegistry registry = GetRegistryOrThrow(nodeBuilder);
            TNotifier notifier = nodeBuilder.Node;

            // IAsyncEnumerable<TEvent> is covariant on TEvent so the
            // cast at GetAsyncEnumerator time is allocation-free.
            registry.Register(
                notifier,
                (n, ctx, ct) => factory((TNotifier)n, ctx, ct),
                options);

            return nodeBuilder;
        }

        /// <summary>
        /// Registers the supplied <paramref name="source"/> as the event
        /// source for the resolved notifier. Each activation of the
        /// source calls <c>GetAsyncEnumerator</c> on the same
        /// <paramref name="source"/> instance — callers whose source is
        /// not re-iterable (e.g. a one-shot iterator) should use the
        /// factory overload instead.
        /// </summary>
        /// <typeparam name="TNotifier">
        /// Notifier node type. Constrained to
        /// <see cref="BaseObjectState"/>.
        /// </typeparam>
        /// <typeparam name="TEvent">
        /// Event payload type. Must derive from
        /// <see cref="BaseEventState"/>.
        /// </typeparam>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="nodeBuilder"/> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="source"/> is null.
        /// </exception>
        public static INodeBuilder<TNotifier> Publish<TNotifier, TEvent>(
            this INodeBuilder<TNotifier> nodeBuilder,
            IAsyncEnumerable<TEvent> source,
            EventPublishOptions? options = null)
            where TNotifier : BaseObjectState
            where TEvent : BaseEventState
        {
            if (nodeBuilder == null)
            {
                throw new ArgumentNullException(nameof(nodeBuilder));
            }
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            EventSourceRegistry registry = GetRegistryOrThrow(nodeBuilder);
            TNotifier notifier = nodeBuilder.Node;

            registry.Register(
                notifier,
                (_, _, _) => source,
                options);

            return nodeBuilder;
        }

        private static EventSourceRegistry GetRegistryOrThrow<TNotifier>(
            INodeBuilder<TNotifier> nodeBuilder)
            where TNotifier : NodeState
        {
            if (nodeBuilder.Builder is not NodeManagerBuilder concrete ||
                concrete.EventSources == null)
            {
                string managerTypeName = nodeBuilder.Builder?.NodeManager?.GetType().FullName
                    ?? "(unknown)";

                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Publish requires the node manager to derive from FluentNodeManagerBase. " +
                    "Manager type '{0}' does not opt in.",
                    managerTypeName);
            }

            return concrete.EventSources;
        }
    }
}
