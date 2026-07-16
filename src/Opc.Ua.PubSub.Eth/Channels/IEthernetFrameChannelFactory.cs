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

namespace Opc.Ua.PubSub.Eth.Channels
{
    /// <summary>
    /// DI-resolvable factory that creates an
    /// <see cref="IEthernetFrameChannel"/> for a resolved network
    /// interface. One implementation is registered with the DI
    /// container; <c>AddEthTransport()</c> registers the default native
    /// factory, and <c>WithPcap()</c> replaces it with the SharpPcap
    /// backend.
    /// </summary>
    public interface IEthernetFrameChannelFactory
    {
        /// <summary>
        /// Creates a frame channel bound to <paramref name="parameters"/>.
        /// The returned channel is not yet open; callers invoke
        /// <see cref="IEthernetFrameChannel.OpenAsync"/>.
        /// </summary>
        /// <param name="parameters">Resolved channel parameters.</param>
        /// <param name="telemetry">Telemetry context for logging.</param>
        /// <param name="timeProvider">Clock used by the channel.</param>
        /// <returns>A channel ready to be opened.</returns>
        IEthernetFrameChannel Create(
            EthChannelParameters parameters,
            ITelemetryContext telemetry,
            TimeProvider timeProvider);
    }
}
