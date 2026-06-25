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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.PubSub.Eth;
using Opc.Ua.PubSub.Eth.Channels;
using Opc.Ua.PubSub.Transports;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IPubSubBuilder"/> extensions that register the
    /// <see cref="EthPubSubTransportFactory"/> and the default native
    /// <see cref="IEthernetFrameChannelFactory"/> with the OPC UA PubSub
    /// DI surface.
    /// </summary>
    /// <remarks>
    /// Implements the OPC UA Part 14 Ethernet mapping registration. The
    /// default channel factory uses the in-repo native AF_PACKET (Linux)
    /// / BPF (macOS) backends; call <c>WithPcap()</c> to substitute the
    /// SharpPcap backend for Windows / cross-platform support, or register
    /// a custom <see cref="IEthernetFrameChannelFactory"/> before
    /// <see cref="AddEthTransport(IPubSubBuilder, Action{EthTransportOptions})"/>
    /// to override it.
    /// </remarks>
    public static class EthTransportServiceCollectionExtensions
    {
        /// <summary>
        /// Default configuration section name read by
        /// <see cref="AddEthTransport(IPubSubBuilder, IConfiguration)"/>.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:PubSub:Eth";

        /// <summary>
        /// Registers the <see cref="EthPubSubTransportFactory"/> as a
        /// singleton <see cref="IPubSubTransportFactory"/> and binds
        /// <see cref="EthTransportOptions"/> via the optional
        /// <paramref name="configure"/> callback.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="configure">Optional options callback.</param>
        public static IEthTransportBuilder AddEthTransport(
            this IPubSubBuilder builder,
            Action<EthTransportOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                builder.Services.AddOptions<EthTransportOptions>();
            }
            else
            {
                builder.Services.AddOptions<EthTransportOptions>().Configure(configure);
            }
            RegisterServices(builder.Services);
            return CreateEthTransportBuilder(builder);
        }

        /// <summary>
        /// Registers the <see cref="EthPubSubTransportFactory"/> and binds
        /// <see cref="EthTransportOptions"/> from <paramref name="configuration"/>.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="configuration">Root configuration.</param>
        public static IEthTransportBuilder AddEthTransport(
            this IPubSubBuilder builder,
            IConfiguration configuration)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            return builder.AddEthTransport(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers the <see cref="EthPubSubTransportFactory"/> and binds
        /// <see cref="EthTransportOptions"/> from the supplied section.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="section">Configuration section.</param>
        public static IEthTransportBuilder AddEthTransport(
            this IPubSubBuilder builder,
            IConfigurationSection section)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }
            builder.Services.AddOptions<EthTransportOptions>().Bind(section);
            RegisterServices(builder.Services);
            return CreateEthTransportBuilder(builder);
        }

        private static void RegisterServices(IServiceCollection services)
        {
            services.TryAddSingleton<IEthernetFrameChannelFactory, DefaultEthernetFrameChannelFactory>();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IPubSubTransportFactory, EthPubSubTransportFactory>());
        }

        private static IEthTransportBuilder CreateEthTransportBuilder(IPubSubBuilder builder)
        {
            return builder as IEthTransportBuilder ?? new EthTransportBuilder(builder);
        }
    }
}
