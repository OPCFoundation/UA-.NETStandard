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
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transcoding;

namespace RedundantPubSub
{
    /// <summary>
    /// Hosted service that registers a receive-path observer on the subscriber's real PubSub
    /// connection and feeds each observed SequenceNumber to the
    /// <see cref="SequenceContinuityMonitor"/>.
    /// </summary>
    public sealed class RawUdpSequenceMonitor : IHostedService, IReceivedNetworkMessageSink, IDisposable
    {
        /// <summary>
        /// Initializes a new <see cref="RawUdpSequenceMonitor"/>.
        /// </summary>
        /// <param name="serviceProvider">Provider used to resolve the PubSub application.</param>
        /// <param name="options">Parsed sample options describing the endpoint and writer ids.</param>
        /// <param name="monitor">Monitor that evaluates SequenceNumber continuity.</param>
        /// <param name="logger">Logger used to report listening state.</param>
        public RawUdpSequenceMonitor(
            IServiceProvider serviceProvider,
            SampleOptions options,
            SequenceContinuityMonitor monitor,
            ILogger<RawUdpSequenceMonitor> logger)
        {
            m_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Registers the receive-path observer before the PubSub application starts receiving.
        /// </summary>
        /// <param name="cancellationToken">Token signaled if startup is cancelled.</param>
        /// <returns>A completed task.</returns>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            IPubSubApplication application = m_serviceProvider.GetRequiredService<IPubSubApplication>();
            IReadOnlyList<IPubSubConnection> connections = application.Connections;
            if (connections.Count == 0)
            {
                m_logger.NoPubSubConnectionsToObserve();
                return Task.CompletedTask;
            }

            m_registrations = new IDisposable[connections.Count];
            for (int ii = 0; ii < connections.Count; ii++)
            {
                m_registrations[ii] = connections[ii].RegisterReceivedNetworkMessageSink(this);
            }

            m_logger.SequenceMonitorObserving(connections.Count, m_options.Endpoint);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Removes the receive-path observer registrations.
        /// </summary>
        /// <param name="cancellationToken">Token signaled if shutdown is cancelled.</param>
        /// <returns>A completed task.</returns>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            DisposeRegistrations();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Observes each decoded data NetworkMessage inline on the subscriber receive path.
        /// </summary>
        /// <param name="received">The decoded NetworkMessage observed by the subscriber.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A completed task.</returns>
        public ValueTask OnReceivedAsync(ReceivedNetworkMessage received, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int ii = 0; ii < received.Message.DataSetMessages.Count; ii++)
            {
                PubSubDataSetMessage dataSetMessage = received.Message.DataSetMessages[ii];
                if (dataSetMessage.DataSetWriterId == m_options.DataSetWriterId)
                {
                    m_monitor.OnSequence(dataSetMessage.SequenceNumber, dataSetMessage.Fields);
                }
            }

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Disposes the receive-path observer registrations.
        /// </summary>
        public void Dispose()
        {
            DisposeRegistrations();
        }

        private void DisposeRegistrations()
        {
            if (m_registrations is null)
            {
                return;
            }

            for (int ii = 0; ii < m_registrations.Length; ii++)
            {
                m_registrations[ii].Dispose();
            }

            m_registrations = null;
        }

        private readonly IServiceProvider m_serviceProvider;
        private readonly SampleOptions m_options;
        private readonly SequenceContinuityMonitor m_monitor;
        private readonly ILogger<RawUdpSequenceMonitor> m_logger;
        private IDisposable[]? m_registrations;
    }

    internal static partial class RawUdpSequenceMonitorLog
    {
        [LoggerMessage(EventId = RedundantPubSubEventIds.RawUdpSequenceMonitor + 0, Level = LogLevel.Warning,
            Message = "Sequence monitor found no PubSub connections to observe.")]
        public static partial void NoPubSubConnectionsToObserve(this ILogger logger);

        [LoggerMessage(EventId = RedundantPubSubEventIds.RawUdpSequenceMonitor + 1, Level = LogLevel.Information,
            Message = "Sequence monitor observing {ConnectionCount} PubSub connection(s) on {Endpoint}.")]
        public static partial void SequenceMonitorObserving(
            this ILogger logger,
            int connectionCount,
            string endpoint);
    }
}
