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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Injects reliable-stream establishment into the existing UASC client without nesting a flow protocol.
    /// </summary>
    public sealed class UascStreamByteTransportFactory : IUaSCByteTransportFactory, IUaSCTransportProfile
    {
        /// <summary>
        /// Creates a factory whose connector transfers one independently owned bidirectional stream per client.
        /// The connector is responsible for endpoint/profile validation and authenticated transport establishment.
        /// </summary>
        public UascStreamByteTransportFactory(
            Func<Uri, CancellationToken, ValueTask<Stream>> connector,
            string implementation,
            string transportProfileUri = "",
            IDataChannelDirectBindingProvider? directBindingProvider = null)
        {
            m_connector = connector ?? throw new ArgumentNullException(nameof(connector));
            if (string.IsNullOrWhiteSpace(implementation))
            {
                throw new ArgumentException("A transport implementation name is required.", nameof(implementation));
            }
            Implementation = implementation;
            TransportProfileUri = transportProfileUri ?? throw new ArgumentNullException(nameof(transportProfileUri));
            m_directBindingProvider = directBindingProvider;
        }

        /// <inheritdoc/>
        public string Implementation { get; }

        /// <inheritdoc/>
        public string TransportProfileUri { get; }

        /// <inheritdoc/>
        public IUaSCByteTransport Create(
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry)
        {
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            return new UascStreamByteTransport(
                m_connector, bufferManager, receiveBufferSize, Implementation, m_directBindingProvider);
        }

        /// <summary>The caller's asynchronous reliable-stream connector.</summary>
        private readonly Func<Uri, CancellationToken, ValueTask<Stream>> m_connector;
        private readonly IDataChannelDirectBindingProvider? m_directBindingProvider;
    }
}
