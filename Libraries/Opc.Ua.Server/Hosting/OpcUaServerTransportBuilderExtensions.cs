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
 *
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
using Opc.Ua;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Server-builder forwarding overloads for transport registrations.
    /// </summary>
    public static class OpcUaServerTransportBuilderExtensions
    {
        /// <summary>
        /// Adds the raw-socket OPC TCP transport and returns the server builder.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddOpcTcpTransport(this IOpcUaServerBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            new BuilderAdapter(builder.Services).AddOpcTcpTransport();
            return builder;
        }
#if !NOHTTPS
        /// <summary>
        /// Adds the HTTPS transport and returns the server builder.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddHttpsTransport(this IOpcUaServerBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            new BuilderAdapter(builder.Services).AddHttpsTransport();
            return builder;
        }

        /// <summary>
        /// Adds the WSS transport and returns the server builder.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddWssTransport(this IOpcUaServerBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            new BuilderAdapter(builder.Services).AddWssTransport();
            return builder;
        }
#if NET8_0_OR_GREATER
        /// <summary>
        /// Adds the Kestrel-hosted OPC TCP transport and returns the server builder.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddKestrelOpcTcpTransport(this IOpcUaServerBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            new BuilderAdapter(builder.Services).AddKestrelOpcTcpTransport();
            return builder;
        }

        /// <summary>
        /// Adds the OPC UA REST transport and returns the server builder.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddWebApiTransport(this IOpcUaServerBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            new BuilderAdapter(builder.Services).AddWebApiTransport();
            return builder;
        }
#endif
#endif
        private sealed class BuilderAdapter : IOpcUaBuilder
        {
            public BuilderAdapter(IServiceCollection services)
            {
                Services = services;
            }
            public IServiceCollection Services { get; }
        }
    }
}
