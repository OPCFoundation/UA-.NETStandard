// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#if NET8_0_OR_GREATER

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.PubSub.Eth.Channels;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IEthTransportBuilder"/> extension that swaps the default
    /// native Ethernet frame backend for the SharpPcap (libpcap / Npcap)
    /// backend.
    /// </summary>
    public static class PcapEthTransportBuilderExtensions
    {
        /// <summary>
        /// Replaces the registered
        /// <see cref="IEthernetFrameChannelFactory"/> with the SharpPcap
        /// backend, enabling cross-platform / Windows Layer-2 frame I/O
        /// over libpcap / Npcap.
        /// </summary>
        /// <param name="builder">Ethernet transport builder.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IEthTransportBuilder WithPcap(this IEthTransportBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.Replace(
                ServiceDescriptor.Singleton<IEthernetFrameChannelFactory, PcapEthernetFrameChannelFactory>());
            return builder;
        }
    }
}

#endif
