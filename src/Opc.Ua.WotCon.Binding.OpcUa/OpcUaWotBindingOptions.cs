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
using Opc.Ua.Client;

namespace Opc.Ua.WotCon.Binding.OpcUa
{
    /// <summary>
    /// Options for the OPC UA WoT binding executor. The session factory connects a
    /// client session to the target endpoint and is injectable so callers control
    /// the application configuration, security and identity.
    /// </summary>
    public sealed class OpcUaWotBindingOptions
    {
        /// <summary>
        /// Gets or sets the factory that connects an <see cref="ISession"/> to the
        /// supplied <c>opc.tcp</c> endpoint. It is required for execution.
        /// </summary>
        public Func<string, CancellationToken, ValueTask<ISession>>? SessionFactory { get; set; }

        /// <summary>
        /// Gets or sets whether the executor disposes the session when the channel
        /// is disposed. Set to <c>false</c> when a shared, caller-owned session is
        /// returned by the factory.
        /// </summary>
        public bool DisposeSession { get; set; } = true;

        /// <summary>
        /// Gets or sets the sampling / publishing interval used for observe and
        /// event subscriptions. Observe and event notifications are delivered by
        /// a native OPC UA <see cref="Client.Subscription"/> / <see cref="Client.MonitoredItem"/>
        /// pair (Part 4 §5.13 / §5.12); this bounds how fast the server samples
        /// and publishes, not a client-side poll.
        /// </summary>
        public TimeSpan ObserveInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Gets or sets the bounded monitored-item queue size requested for
        /// event subscriptions, so a burst of events cannot grow the server-side
        /// queue without bound. Property observe monitored items always request
        /// a queue size of 1 (only the latest value is relevant).
        /// </summary>
        public uint EventQueueSize { get; set; } = 10;
    }
}
