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
using System.Runtime.InteropServices;

namespace Opc.Ua.PubSub.Eth.Channels
{
    /// <summary>
    /// Default <see cref="IEthernetFrameChannelFactory"/> that selects an
    /// in-repo native backend by operating system: Linux
    /// <c>AF_PACKET</c> and macOS BPF. On other platforms (for example
    /// Windows) it throws, directing callers to register the SharpPcap
    /// backend via <c>WithPcap()</c> or inject a custom factory.
    /// </summary>
    /// <remarks>
    /// Mirrors the platform-dispatch model used elsewhere in the stack
    /// (for example the DTLS native backend). Both built-in backends use
    /// libc P/Invoke and are NativeAOT-compatible.
    /// </remarks>
    public sealed class DefaultEthernetFrameChannelFactory : IEthernetFrameChannelFactory
    {
        /// <inheritdoc/>
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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return new AfPacketEthernetFrameChannel(parameters, telemetry, timeProvider);
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return new BpfEthernetFrameChannel(parameters, telemetry, timeProvider);
            }
            throw new PlatformNotSupportedException(
                "The native OPC UA PubSub Ethernet backend supports Linux (AF_PACKET) and macOS (BPF). " +
                "On Windows or other platforms, register the SharpPcap backend via WithPcap() or inject a " +
                "custom IEthernetFrameChannelFactory (for example the in-memory loopback backend).");
        }
    }
}
