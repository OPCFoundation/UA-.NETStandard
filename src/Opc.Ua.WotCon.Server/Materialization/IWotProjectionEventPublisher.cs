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
    /// Uses the existing fluent event-source registry, including ancestor
    /// monitoring, cancellation, reporting and error telemetry.
    /// </summary>
    public sealed class WotProjectionEventPublisher : IWotProjectionEventPublisher
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
        /// Gets the maximum retained occurrence routes per event declaration.
        /// An evicted occurrence fails subsequent actions with BadEventIdUnknown.
        /// </summary>
        public int MaxEventRoutes { get; init; } = 4096;
    }
}
