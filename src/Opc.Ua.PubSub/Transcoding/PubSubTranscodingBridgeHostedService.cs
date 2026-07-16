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
using Microsoft.Extensions.Hosting;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Hosted service that materialises a configured transcoding bridge
    /// once the PubSub application is available: it resolves the source
    /// and target connections by name, builds the transcoder and egress,
    /// and registers the bridge on the source connection.
    /// </summary>
    internal sealed class PubSubTranscodingBridgeHostedService
        : IHostedService, IAsyncDisposable
    {
        private readonly IServiceProvider m_serviceProvider;
        private readonly TranscodingBridgeDescriptor m_descriptor;
        private PubSubTranscodingBridge? m_bridge;

        public PubSubTranscodingBridgeHostedService(
            IServiceProvider serviceProvider,
            TranscodingBridgeDescriptor descriptor)
        {
            m_serviceProvider = serviceProvider
                ?? throw new ArgumentNullException(nameof(serviceProvider));
            m_descriptor = descriptor
                ?? throw new ArgumentNullException(nameof(descriptor));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var activator = new TranscodingBridgeActivator(m_serviceProvider);
            m_bridge = activator.Create(m_descriptor);
            m_bridge.Start();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return DisposeAsync().AsTask();
        }

        public async ValueTask DisposeAsync()
        {
            if (m_bridge is not null)
            {
                await m_bridge.DisposeAsync().ConfigureAwait(false);
                m_bridge = null;
            }
        }
    }
}
