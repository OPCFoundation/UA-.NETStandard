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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Bindings;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers the experimental <c>opc.quic</c> transport.
    /// </summary>
    public static class OpcUaQuicBuilderExtensions
    {
        /// <summary>
        /// Adds the <c>opc.quic</c> listener and channel factories, so a
        /// server can publish an <c>opc.quic</c> endpoint and a client
        /// can connect to one.
        /// </summary>
        /// <param name="builder">The builder.</param>
        /// <remarks>
        /// <para>
        /// Experimental. QUIC is available only where msquic is present;
        /// <see cref="QuicTransport.IsSupported"/> reports whether it is.
        /// Registering the binding on a platform without it is harmless —
        /// opening a listener fails with Bad_NotSupported rather than at
        /// registration.
        /// </para>
        /// <para>
        /// This also registers the server-side data channel transport, so a
        /// data channel opened on an <c>opc.quic</c> SecureChannel gets its
        /// own QUIC stream as Part 6 errata §7.4 requires. A server built
        /// from this container that also publishes <c>opc.tcp</c> or
        /// <c>opc.wss</c> endpoints is unaffected: the QUIC transport
        /// declines any SecureChannel that is not <c>opc.quic</c> and the
        /// inline framing carries those instead.
        /// </para>
        /// </remarks>
        public static IOpcUaBuilder AddQuicTransport(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services
                .TryAddSingleton<IServerDataChannelTransport, QuicServerDataChannelTransport>();

            return builder
                .AddCustomTransport<QuicTransportListenerFactory, QuicTransportChannelFactory>();
        }
    }
}
