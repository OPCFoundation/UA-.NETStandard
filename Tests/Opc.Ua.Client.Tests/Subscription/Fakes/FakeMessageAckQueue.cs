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
#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Subscriptions.Fakes
{
    /// <summary>
    /// Hand-rolled fake for <see cref="IMessageAckQueue"/>. Records every
    /// invocation and lets tests override return behaviour via callback
    /// fields. Replaces <c>Mock&lt;IMessageAckQueue&gt;</c>.
    /// </summary>
    internal sealed class FakeMessageAckQueue : IMessageAckQueue
    {
        public List<SubscriptionAcknowledgement> QueuedAcks { get; } = [];
        public List<uint> CompletedSubscriptions { get; } = [];
        public int UpdateCalls { get; private set; }

        /// <summary>
        /// Optional override for <see cref="QueueAsync"/>. If null, returns
        /// completed.
        /// </summary>
        public Func<SubscriptionAcknowledgement, CancellationToken, ValueTask>? OnQueueAsync { get; set; }

        /// <summary>
        /// Optional override for <see cref="CompleteAsync"/>. If null,
        /// returns completed.
        /// </summary>
        public Func<uint, CancellationToken, ValueTask>? OnCompleteAsync { get; set; }

        public ValueTask QueueAsync(SubscriptionAcknowledgement ack,
            CancellationToken ct = default)
        {
            QueuedAcks.Add(ack);
            return OnQueueAsync?.Invoke(ack, ct) ?? default;
        }

        public ValueTask CompleteAsync(uint subscriptionId,
            CancellationToken ct = default)
        {
            CompletedSubscriptions.Add(subscriptionId);
            return OnCompleteAsync?.Invoke(subscriptionId, ct) ?? default;
        }

        public void Update()
        {
            UpdateCalls++;
        }
    }
}
