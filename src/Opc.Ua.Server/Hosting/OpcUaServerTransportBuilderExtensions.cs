// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
