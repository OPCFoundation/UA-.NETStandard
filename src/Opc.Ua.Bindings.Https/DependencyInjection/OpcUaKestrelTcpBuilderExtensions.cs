// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            builder.Services.AddSingleton<ITransportBindingConfigurator>(provider =>
            {
                IBufferManagerFactory bufferManagerFactory =
                    provider.GetRequiredService<IBufferManagerFactory>();
                return new TransportBindingConfigurator(
                    registry => registry.RegisterListenerFactory(
                        new KestrelTcpTransportListenerFactory(bufferManagerFactory)));
            });
            return builder;
        }

        /// <summary>
        /// Registers the Kestrel-hosted <c>opc.tcp://</c> listener and returns
        /// the same specialized builder.
        /// </summary>
        /// <typeparam name="TBuilder">The specialized transport builder type.</typeparam>
        /// <param name="builder">The OPC UA transport builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static TBuilder AddKestrelOpcTcpTransport<TBuilder>(this TBuilder builder)
            where TBuilder : IOpcUaTransportBuilder
        {
            ArgumentNullException.ThrowIfNull(builder);

            IOpcUaBuilder rootBuilder = builder.Services.AddOpcUa();
            rootBuilder.AddKestrelOpcTcpTransport();
            return builder;
        }
    }
}
#endif // NET8_0_OR_GREATER
