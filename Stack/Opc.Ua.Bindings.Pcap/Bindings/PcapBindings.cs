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

namespace Opc.Ua.Bindings.Pcap.Bindings
{
    /// <summary>
    /// Convenience helper that installs (or removes) the Pcap channel
    /// binding into the process-wide
    /// <see cref="TransportBindings.Channels"/> registry. Use this when
    /// the application is not built around
    /// Microsoft.Extensions.DependencyInjection and therefore cannot
    /// rely on the <c>AddOpcUaBindingsPcap</c> extension method.
    /// </summary>
    public static class PcapBindings
    {
        /// <summary>
        /// Installs the Pcap channel binding for <c>opc.tcp</c> into the
        /// shared <see cref="TransportBindings.Channels"/> registry. The
        /// returned <see cref="IChannelCaptureRegistry"/> is the
        /// coordination point a <c>CaptureSessionManager</c> uses to
        /// switch recording on or off.
        /// </summary>
        public static IChannelCaptureRegistry Install()
        {
            var registry = new ChannelCaptureRegistry();
            Install(registry);
            return registry;
        }

        /// <summary>
        /// Installs the Pcap channel binding using the supplied
        /// <see cref="IChannelCaptureRegistry"/>.
        /// </summary>
        public static void Install(IChannelCaptureRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            var binding = new PcapTransportChannelBinding(registry);
            ((ITransportBindings<ITransportChannelFactory>)TransportBindings.Channels)
                .SetBinding(binding);
        }
    }
}
