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
using System.Diagnostics.CodeAnalysis;
using Opc.Ua.PubSub.Eth.Channels;

namespace Opc.Ua.PubSub.Eth.Channels.Pcap
{
    /// <summary>
    /// <see cref="IEthernetFrameChannelFactory"/> that creates SharpPcap
    /// (libpcap / Npcap) frame channels. Registered through
    /// <c>WithPcap()</c> to provide cross-platform / Windows Layer-2 frame
    /// I/O without the privileged native AF_PACKET / BPF backends.
    /// </summary>
    /// <remarks>
    /// SharpPcap dynamically loads the native libpcap / Npcap library. The
    /// SharpPcap surface is isolated to this type and
    /// <see cref="PcapEthernetFrameChannel"/>; the suppression keeps the
    /// rest of the assembly trim / NativeAOT clean while the
    /// <c>Opc.Ua.Aot.Tests</c> evaluation verifies the backend actually
    /// runs under NativeAOT.
    /// </remarks>
    public sealed class PcapEthernetFrameChannelFactory : IEthernetFrameChannelFactory
    {
        /// <inheritdoc/>
        [UnconditionalSuppressMessage(
            "AOT",
            "IL3050:RequiresDynamicCode",
            Justification = "SharpPcap dynamically loads native libpcap/Npcap; verified by Opc.Ua.Aot.Tests.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2026:RequiresUnreferencedCode",
            Justification = "SharpPcap dynamically loads native libpcap/Npcap; verified by Opc.Ua.Aot.Tests.")]
        public IEthernetFrameChannel Create(
            EthChannelParameters parameters,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            if (parameters is null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            return new PcapEthernetFrameChannel(parameters, telemetry, timeProvider);
        }
    }
}

#endif
