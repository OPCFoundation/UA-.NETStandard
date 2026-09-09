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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Subscription-gated source and polling extensions.
    /// </summary>
    public static class MonitoredSourceBuilderExtensions
    {
        /// <summary>
        /// Registers a handoff callback for retained monitored items before
        /// the source's subscriber count is reconciled.
        /// </summary>
        public static INodeBuilder OnMonitoredItemAttached(
            this INodeBuilder builder,
            MonitoredItemAttachmentHandler handler)
        {
            GetRegistration(builder).AddAttachedHandler(handler);
            return builder;
        }

        /// <summary>
        /// Registers a handoff callback before a retained monitored item is
        /// removed from the source's subscriber count.
        /// </summary>
        public static INodeBuilder OnMonitoredItemDetached(
            this INodeBuilder builder,
            MonitoredItemAttachmentHandler handler)
        {
            GetRegistration(builder).AddDetachedHandler(handler);
            return builder;
        }

        /// <summary>
        /// Registers an attachment callback while preserving typed-variable chaining.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR value type carried by the variable.
        /// </typeparam>
        public static IVariableBuilder<TValue> OnMonitoredItemAttached<TValue>(
            this IVariableBuilder<TValue> builder,
            MonitoredItemAttachmentHandler handler)
        {
            GetRegistration(builder).AddAttachedHandler(handler);
            return builder;
        }

        /// <summary>
        /// Registers a detachment callback while preserving typed-variable chaining.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR value type carried by the variable.
        /// </typeparam>
        public static IVariableBuilder<TValue> OnMonitoredItemDetached<TValue>(
            this IVariableBuilder<TValue> builder,
            MonitoredItemAttachmentHandler handler)
        {
            GetRegistration(builder).AddDetachedHandler(handler);
            return builder;
        }

        /// <summary>
        /// Registers an attachment callback for retained virtual monitored items.
        /// </summary>
        public static IVirtualNodeBuilder OnMonitoredItemAttached(
            this IVirtualNodeBuilder builder,
            MonitoredItemAttachmentHandler handler)
        {
            GetRegistration(builder).AddAttachedHandler(handler);
            return builder;
        }

        /// <summary>
        /// Registers a detachment callback for retained virtual monitored items.
        /// </summary>
        public static IVirtualNodeBuilder OnMonitoredItemDetached(
            this IVirtualNodeBuilder builder,
            MonitoredItemAttachmentHandler handler)
        {
            GetRegistration(builder).AddDetachedHandler(handler);
            return builder;
        }

        /// <summary>
        /// Invokes <paramref name="handler"/> when the first active
        /// data-change subscriber appears.
        /// </summary>
        public static INodeBuilder OnFirstSubscriber(
            this INodeBuilder builder,
            MonitoredSourceLifecycleHandler handler)
        {
            GetRegistration(builder).SetFirstSubscriber(handler);
            return builder;
        }

        /// <summary>
        /// Invokes the handler when the first active subscriber to the selected
        /// attribute appears. The first and last handlers must select the same attribute.
        /// </summary>
        public static INodeBuilder OnFirstSubscriber(
            this INodeBuilder builder,
            uint attributeId,
            MonitoredSourceLifecycleHandler handler)
        {
            GetRegistration(builder).SetFirstSubscriber(handler, attributeId);
            return builder;
        }

        /// <summary>
        /// Typed-variable overload that preserves fluent chaining.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR value type carried by the variable.
        /// </typeparam>
        public static IVariableBuilder<TValue> OnFirstSubscriber<TValue>(
            this IVariableBuilder<TValue> builder,
            MonitoredSourceLifecycleHandler handler)
        {
            GetRegistration(builder).SetFirstSubscriber(handler);
            return builder;
        }

        /// <summary>
        /// Selects the monitored attribute while preserving typed-variable chaining.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR value type carried by the variable.
        /// </typeparam>
        public static IVariableBuilder<TValue> OnFirstSubscriber<TValue>(
            this IVariableBuilder<TValue> builder,
            uint attributeId,
            MonitoredSourceLifecycleHandler handler)
        {
            GetRegistration(builder).SetFirstSubscriber(handler, attributeId);
            return builder;
        }

        /// <summary>
        /// Invokes <paramref name="handler"/> when the last active
        /// data-change subscriber disappears.
        /// </summary>
        public static INodeBuilder OnLastSubscriber(
            this INodeBuilder builder,
            MonitoredSourceLifecycleHandler handler)
        {
            GetRegistration(builder).SetLastSubscriber(handler);
            return builder;
        }

        /// <summary>
        /// Invokes the handler when the last active subscriber to the selected
        /// attribute disappears. The first and last handlers must select the same attribute.
        /// </summary>
        public static INodeBuilder OnLastSubscriber(
            this INodeBuilder builder,
            uint attributeId,
            MonitoredSourceLifecycleHandler handler)
        {
            GetRegistration(builder).SetLastSubscriber(handler, attributeId);
            return builder;
        }

        /// <summary>
        /// Typed-variable overload that preserves fluent chaining.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR value type carried by the variable.
        /// </typeparam>
        public static IVariableBuilder<TValue> OnLastSubscriber<TValue>(
            this IVariableBuilder<TValue> builder,
            MonitoredSourceLifecycleHandler handler)
        {
            GetRegistration(builder).SetLastSubscriber(handler);
            return builder;
        }

        /// <summary>
        /// Selects the monitored attribute while preserving typed-variable chaining.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR value type carried by the variable.
        /// </typeparam>
        public static IVariableBuilder<TValue> OnLastSubscriber<TValue>(
            this IVariableBuilder<TValue> builder,
            uint attributeId,
            MonitoredSourceLifecycleHandler handler)
        {
            GetRegistration(builder).SetLastSubscriber(handler, attributeId);
            return builder;
        }

        /// <summary>
        /// Invokes <paramref name="handler"/> when the first active
        /// data-change subscriber appears on a virtual node.
        /// </summary>
        public static IVirtualNodeBuilder OnFirstSubscriber(
            this IVirtualNodeBuilder builder,
            MonitoredSourceLifecycleHandler handler)
        {
            GetRegistration(builder).SetFirstSubscriber(handler);
            return builder;
        }

        /// <summary>
        /// Invokes the handler for the first active subscriber to the selected
        /// attribute on each materialized virtual node.
        /// </summary>
        public static IVirtualNodeBuilder OnFirstSubscriber(
            this IVirtualNodeBuilder builder,
            uint attributeId,
            MonitoredSourceLifecycleHandler handler)
        {
            GetRegistration(builder).SetFirstSubscriber(handler, attributeId);
            return builder;
        }

        /// <summary>
        /// Invokes <paramref name="handler"/> when the last active
        /// data-change subscriber disappears from a virtual node.
        /// </summary>
        public static IVirtualNodeBuilder OnLastSubscriber(
            this IVirtualNodeBuilder builder,
            MonitoredSourceLifecycleHandler handler)
        {
            GetRegistration(builder).SetLastSubscriber(handler);
            return builder;
        }

        /// <summary>
        /// Invokes the handler for the last active subscriber to the selected
        /// attribute on each materialized virtual node.
        /// </summary>
        public static IVirtualNodeBuilder OnLastSubscriber(
            this IVirtualNodeBuilder builder,
            uint attributeId,
            MonitoredSourceLifecycleHandler handler)
        {
            GetRegistration(builder).SetLastSubscriber(handler, attributeId);
            return builder;
        }

        /// <summary>
        /// Polls a variable only while it has at least one active subscriber.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR value type carried by the variable.
        /// </typeparam>
        public static IVariableBuilder<TValue> PollWhileMonitored<TValue>(
            this IVariableBuilder<TValue> builder,
            TimeSpan minimumPeriod,
            Func<ISystemContext, CancellationToken, ValueTask<TValue>> sample)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }

            GetRegistration(builder).SetPoller(
                new MonitoredValuePoller<TValue>(
                    (context, source, cancellationToken) =>
                        sample(context, cancellationToken)),
                minimumPeriod);
            return builder;
        }

        /// <summary>
        /// Synchronous convenience overload for monitored polling.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR value type carried by the variable.
        /// </typeparam>
        public static IVariableBuilder<TValue> PollWhileMonitored<TValue>(
            this IVariableBuilder<TValue> builder,
            TimeSpan minimumPeriod,
            Func<ISystemContext, TValue> sample)
        {
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }
            return builder.PollWhileMonitored(
                minimumPeriod,
                (context, cancellationToken) =>
                    new ValueTask<TValue>(sample(context)));
        }

        /// <summary>
        /// Polls each materialized virtual variable only while it has an
        /// active subscriber.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR value type returned by the source.
        /// </typeparam>
        public static IVirtualNodeBuilder PollWhileMonitored<TValue>(
            this IVirtualNodeBuilder builder,
            TimeSpan minimumPeriod,
            Func<
                ISystemContext,
                NodeState,
                CancellationToken,
                ValueTask<TValue>> sample)
        {
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }
            GetRegistration(builder).SetPoller(
                new MonitoredValuePoller<TValue>(sample),
                minimumPeriod);
            return builder;
        }

        private static MonitoredSourceRegistration GetRegistration(
            INodeBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            NodeManagerBuilder concrete =
                FluentNodeManagerBase.ResolveAttachedBuilder(
                    builder.Builder,
                    "monitored source");
            return concrete.MonitoredSources!.Register(builder.Node);
        }

        private static MonitoredSourceRegistration GetRegistration(
            IVirtualNodeBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (builder is not VirtualNodeRegistration registration)
            {
                throw new ArgumentException(
                    "The virtual node builder was not created by ResolveNodes.",
                    nameof(builder));
            }
            return registration.Owner.MonitoredSources!.Register(registration);
        }
    }
}
