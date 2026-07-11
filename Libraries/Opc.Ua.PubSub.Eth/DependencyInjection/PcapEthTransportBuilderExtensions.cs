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
