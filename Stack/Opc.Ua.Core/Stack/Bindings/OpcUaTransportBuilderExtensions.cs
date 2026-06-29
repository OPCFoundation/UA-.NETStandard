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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Bindings;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods that install OPC UA transport bindings
    /// (listener + channel factories) into the host's
    /// <see cref="ITransportBindingRegistry"/>. Each binding package
    /// (HTTPS / WSS, Kestrel-TCP, Pcap, …) ships its own
    /// <c>Add*Transport()</c> extension on <see cref="IOpcUaBuilder"/>
    /// (or on this same class for the in-Core raw-socket TCP path).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The DI extensions install <see cref="ITransportBindingConfigurator"/>
    /// instances into the service collection. The
    /// <see cref="DefaultTransportBindingRegistry"/> singleton runs
    /// every registered configurator at first resolution time, in
    /// registration order, so a downstream
    /// <c>AddKestrelOpcTcpTransport()</c> after <c>AddOpcTcpTransport()</c>
    /// swaps the raw-socket listener for the Kestrel-hosted one
    /// (last-writer-wins per URI scheme).
    /// </para>
    /// <para>
    /// Calling any <c>Add*Transport()</c> extension is idempotent — the
    /// <see cref="ITransportBindingRegistry"/> singleton is only added
    /// once via
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton{TService}(IServiceCollection, Func{IServiceProvider, TService})"/>.
    /// </para>
    /// </remarks>
    public static class OpcUaTransportBuilderExtensions
    {
        /// <summary>
        /// Registers the raw-socket <c>opc.tcp://</c> listener
        /// (<see cref="TcpTransportListenerFactory"/>) and channel
        /// (<see cref="TcpTransportChannelFactory"/>) factories on the
        /// host's <see cref="ITransportBindingRegistry"/>.
        /// </summary>
        /// <remarks>
        /// Composes with the other <c>Add*Transport()</c> overloads. To
        /// swap the raw-socket listener for the Kestrel-hosted one call
        /// <c>AddKestrelOpcTcpTransport()</c> AFTER
        /// <c>AddOpcTcpTransport()</c>.
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddOpcTcpTransport(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddTransportBindingRegistry();
            builder.Services.AddSingleton<ITransportBindingConfigurator>(
                new TransportBindingConfigurator(registry =>
                {
                    registry.RegisterListenerFactory(new TcpTransportListenerFactory());
                    registry.RegisterChannelFactory(new TcpTransportChannelFactory());
                }));
            return builder;
        }

        /// <summary>
        /// Installs a custom transport listener and channel factory pair
        /// for the URI scheme reported by
        /// <typeparamref name="TListenerFactory"/>.
        /// <typeparamref name="TListenerFactory"/> and
        /// <typeparamref name="TChannelFactory"/> must have a
        /// parameterless or constructor-injectable shape so they can be
        /// resolved out of the service provider.
        /// </summary>
        /// <typeparam name="TListenerFactory">The listener factory type.</typeparam>
        /// <typeparam name="TChannelFactory">The channel factory type.</typeparam>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        public static IOpcUaBuilder AddCustomTransport<
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
                System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)]
            TListenerFactory,
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
                System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)]
            TChannelFactory>(
            this IOpcUaBuilder builder)
            where TListenerFactory : class, ITransportListenerFactory
            where TChannelFactory : class, ITransportChannelFactory
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddTransportBindingRegistry();
            builder.Services.TryAddSingleton<TListenerFactory>();
            builder.Services.TryAddSingleton<TChannelFactory>();
            builder.Services.AddSingleton<ITransportBindingConfigurator>(provider =>
            {
                var listenerFactory = provider.GetRequiredService<TListenerFactory>();
                var channelFactory = provider.GetRequiredService<TChannelFactory>();
                return new TransportBindingConfigurator(registry =>
                {
                    registry.RegisterListenerFactory(listenerFactory);
                    registry.RegisterChannelFactory(channelFactory);
                });
            });
            return builder;
        }

        /// <summary>
        /// Registers the <see cref="ITransportBindingRegistry"/>
        /// singleton if no consumer has done so already. The singleton
        /// factory runs every registered
        /// <see cref="ITransportBindingConfigurator"/> in registration
        /// order at first resolution time so subsequent
        /// <c>Add*Transport()</c> calls compose cleanly.
        /// </summary>
        public static IServiceCollection AddTransportBindingRegistry(this IServiceCollection services)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            services.TryAddSingleton<ITransportBindingRegistry>(provider =>
            {
                var registry = new DefaultTransportBindingRegistry();
                foreach (ITransportBindingConfigurator configurator in
                    provider.GetServices<ITransportBindingConfigurator>())
                {
                    configurator.Configure(registry);
                }
                return registry;
            });
            return services;
        }
    }
}
