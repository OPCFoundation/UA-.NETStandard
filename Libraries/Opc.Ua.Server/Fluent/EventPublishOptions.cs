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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Optional knobs for
    /// <c>EventNotifierBuilderExtensions.Publish</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All properties are init-only so the same options instance can safely be
    /// reused across multiple <c>Publish</c> registrations.
    /// </para>
    /// <para>
    /// Default behavior (no options supplied): lazy activation, default-field
    /// population enabled, error policy logs and continues, not registered as
    /// a root notifier.
    /// </para>
    /// </remarks>
    public sealed record EventPublishOptions
    {
        /// <summary>
        /// When <c>true</c>, the source's iterator is pumped immediately at
        /// builder seal time and stays active until the manager disposes —
        /// regardless of whether any client is monitoring the node. Useful
        /// for diagnostic event streams whose backpressure or history-storage
        /// guarantees must be preserved across reconnect storms. Default
        /// <c>false</c> (lazy activation: events are pulled only while
        /// <see cref="NodeState.AreEventsMonitored"/> is true).
        /// </summary>
        public bool AlwaysOn { get; init; }

        /// <summary>
        /// When <c>true</c>, the registry does NOT auto-populate
        /// <c>EventId</c>, <c>EventType</c>, <c>SourceNode</c>,
        /// <c>SourceName</c>, <c>Time</c>, <c>ReceiveTime</c>,
        /// <c>Severity</c>, and <c>Message</c> on each yielded event. Use
        /// this when your iterator emits fully-populated events
        /// (e.g. replayed from a history store). Default <c>false</c>
        /// (matches <c>BaseEventState.Initialize</c>).
        /// </summary>
        public bool SkipDefaultPopulation { get; init; }

        /// <summary>
        /// When <c>true</c>, the notifier node is added to the manager's
        /// root-notifier collection so its events propagate to clients that
        /// subscribe to the <c>Server</c> object. Default <c>false</c>:
        /// only direct subscribers to the notifier (or to an ancestor that
        /// the notifier is reachable from via inverse <c>HasNotifier</c>
        /// references) receive events.
        /// </summary>
        /// <remarks>
        /// Enabling this option overwrites the notifier's
        /// <see cref="NodeState.OnReportEvent"/> with the manager's
        /// root-notifier handler, which is incompatible with attaching an
        /// <c>OnEvent</c> interceptor or with direct per-node event monitored
        /// items on the same notifier; the runtime emits a debug-level log
        /// line documenting the trade-off.
        /// </remarks>
        public bool RegisterAsRootNotifier { get; init; }

        /// <summary>
        /// Maximum time the registry waits for the iterator to honor its
        /// <see cref="System.Threading.CancellationToken"/> when the source
        /// deactivates or the manager disposes. After the timeout, the source
        /// is flagged as leaked: any further events the iterator yields are
        /// silently dropped. Default <c>5 seconds</c>.
        /// </summary>
        public TimeSpan CancellationTimeout { get; init; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Optional sink that observes exceptions thrown by the factory
        /// invocation, the iterator <c>MoveNextAsync</c> call, or
        /// <see cref="NodeState.ReportEvent"/>. The registry already logs at
        /// <c>Error</c> level using the manager's logger; this hook is for
        /// callers that want to bubble the exception into their own
        /// telemetry. Default <c>null</c>.
        /// </summary>
        public Action<Exception> OnError { get; init; }
    }
}
