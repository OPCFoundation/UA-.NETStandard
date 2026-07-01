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

#if NET8_0_OR_GREATER
using System;
using Opc.Ua;
using Opc.Ua.Bindings;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods that install the Kestrel-hosted
    /// <c>opc.tcp://</c> listener (<see cref="KestrelTcpTransportListenerFactory"/>)
    /// into the host's <see cref="ITransportBindingRegistry"/>. Call
    /// AFTER <c>AddOpcTcpTransport()</c> to swap the raw-socket
    /// listener for the Kestrel-hosted one (last-writer-wins per URI
    /// scheme — channel factory stays the
    /// <see cref="TcpTransportChannelFactory"/> registered by
    /// <c>AddOpcTcpTransport()</c>).
    /// </summary>
    public static class OpcUaKestrelTcpBuilderExtensions
    {
        /// <summary>
        /// Registers the Kestrel-hosted <c>opc.tcp://</c> listener on
        /// the host's <see cref="ITransportBindingRegistry"/>. Composes
        /// with <c>AddOpcTcpTransport()</c>; the Kestrel listener
        /// overrides the raw-socket one when called second.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddKestrelOpcTcpTransport(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddTransportBindingRegistry();
            builder.Services.AddSingleton<ITransportBindingConfigurator>(
                new TransportBindingConfigurator(registry =>
                {
                    registry.RegisterListenerFactory(new KestrelTcpTransportListenerFactory());
                }));
            return builder;
        }
    }
}
#endif // NET8_0_OR_GREATER
