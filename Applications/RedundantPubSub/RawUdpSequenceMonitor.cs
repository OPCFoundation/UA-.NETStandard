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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Udp;

namespace RedundantPubSub
{
    /// <summary>
    /// Background service that joins the PubSub multicast group and decodes UADP network
    /// messages directly off the wire, feeding each observed SequenceNumber to the
    /// <see cref="SequenceContinuityMonitor"/>. Every subscriber replica runs this monitor, so
    /// the raw multicast stream is visible on all of them regardless of activation role.
    /// </summary>
    public sealed class RawUdpSequenceMonitor : BackgroundService
    {
        /// <summary>
        /// Initializes a new <see cref="RawUdpSequenceMonitor"/>.
        /// </summary>
        /// <param name="options">Parsed sample options describing the endpoint and writer ids.</param>
        /// <param name="monitor">Monitor that evaluates SequenceNumber continuity.</param>
        /// <param name="logger">Logger used to report listening state.</param>
        public RawUdpSequenceMonitor(
            SampleOptions options,
            SequenceContinuityMonitor monitor,
            ILogger<RawUdpSequenceMonitor> logger)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Receives and decodes multicast UADP messages until cancellation is requested.
        /// </summary>
        /// <param name="stoppingToken">Token signaled when the service is stopping.</param>
        /// <returns>A task that completes when the receive loop ends.</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse(m_options.Endpoint);
            using UdpClient client = CreateClient(endpoint);
            PubSubNetworkMessageContext context = CreateContext();
            m_logger.LogInformation("Sequence monitor listening on {Endpoint}.", m_options.Endpoint);

            while (!stoppingToken.IsCancellationRequested)
            {
                UdpReceiveResult result = await client.ReceiveAsync(stoppingToken).ConfigureAwait(false);
                PubSubNetworkMessage? message = UadpDecoder.Decode(result.Buffer, context);
                if (message is null)
                {
                    continue;
                }

                for (int ii = 0; ii < message.DataSetMessages.Count; ii++)
                {
                    PubSubDataSetMessage dataSetMessage = message.DataSetMessages[ii];
                    if (dataSetMessage.DataSetWriterId == m_options.DataSetWriterId)
                    {
                        m_monitor.OnSequence(dataSetMessage.SequenceNumber, dataSetMessage.Fields);
                    }
                }
            }
        }

        private UdpClient CreateClient(UdpEndpoint endpoint)
        {
            var client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.ExclusiveAddressUse = false;
            client.Client.Bind(new IPEndPoint(IPAddress.Any, endpoint.Port));
            if (endpoint.AddressType == UdpAddressType.Multicast)
            {
                client.JoinMulticastGroup(endpoint.Address);
            }
            return client;
        }

        private PubSubNetworkMessageContext CreateContext()
        {
            var registry = new DataSetMetaDataRegistry();
            DataSetMetaDataType metaData = HaDataSetSource.BuildMetaDataCore();
            registry.Register(
                new DataSetMetaDataKey(
                    PublisherId.FromUInt16(m_options.PublisherId),
                    m_options.WriterGroupId,
                    m_options.DataSetWriterId,
                    Uuid.Empty,
                    metaData.ConfigurationVersion?.MajorVersion ?? 1),
                metaData);
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(DefaultTelemetry.Create(builder => builder.SetMinimumLevel(LogLevel.Warning))),
                registry,
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                TimeProvider.System);
        }

        private readonly SampleOptions m_options;
        private readonly SequenceContinuityMonitor m_monitor;
        private readonly ILogger<RawUdpSequenceMonitor> m_logger;
    }
}
