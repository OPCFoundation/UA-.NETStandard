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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Creates a transport channel with UA-TCP transport, UA-SC security
    /// and UA Binary encoding.
    /// </summary>
    public class TcpTransportChannel : UaSCUaBinaryTransportChannel
    {
        /// <summary>
        /// Create a Tcp transport channel.
        /// </summary>
        public TcpTransportChannel(ITelemetryContext telemetry)
            : this(telemetry, DefaultBufferManagerFactory.Instance)
        {
        }

        /// <summary>
        /// Creates a TCP transport channel with a buffer-manager factory.
        /// </summary>
        /// <param name="telemetry">Telemetry context to use.</param>
        /// <param name="bufferManagerFactory">Factory used to create channel buffer managers.</param>
        public TcpTransportChannel(
            ITelemetryContext telemetry,
            IBufferManagerFactory bufferManagerFactory)
            : base(
                new TcpByteTransportFactory(telemetry),
                telemetry,
                timeProvider: null,
                bufferManagerFactory: bufferManagerFactory)
        {
        }
    }

    /// <summary>
    /// Creates a new <see cref="TcpTransportChannel"/> with <see cref="ITransportChannel"/> interface.
    /// </summary>
    public class TcpTransportChannelFactory : ITransportChannelFactory
    {
        /// <summary>
        /// Creates a factory using the default buffer-manager factory.
        /// </summary>
        public TcpTransportChannelFactory()
            : this(DefaultBufferManagerFactory.Instance)
        {
        }

        /// <summary>
        /// Creates a factory using the specified buffer-manager factory.
        /// </summary>
        /// <param name="bufferManagerFactory">Factory used to create channel buffer managers.</param>
        public TcpTransportChannelFactory(IBufferManagerFactory bufferManagerFactory)
        {
            m_bufferManagerFactory = bufferManagerFactory ??
                throw new System.ArgumentNullException(nameof(bufferManagerFactory));
        }

        /// <summary>
        /// The protocol supported by the channel.
        /// </summary>
        public string UriScheme => Utils.UriSchemeOpcTcp;

        /// <summary>
        /// Creates a new instance of a TCP transport channel.
        /// </summary>
        /// <returns>The transport channel.</returns>
        public ITransportChannel Create(ITelemetryContext telemetry)
        {
            return new TcpTransportChannel(telemetry, m_bufferManagerFactory);
        }

        private readonly IBufferManagerFactory m_bufferManagerFactory;
    }
}
