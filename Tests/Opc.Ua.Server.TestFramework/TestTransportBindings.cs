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

using Opc.Ua.Bindings;

namespace Opc.Ua.Server.TestFramework
{
    /// <summary>
    /// Convenience builders for test-time
    /// <see cref="ITransportBindingRegistry"/> instances. Replaces the
    /// pre-Phase 11 reflection auto-load on
    /// <see cref="Utils"/> that loaded
    /// <c>Opc.Ua.Bindings.Https</c> when a <c>wss://</c> /
    /// <c>opc.https://</c> endpoint was first touched. Test fixtures call
    /// <see cref="WithAllSchemes"/> to get a registry pre-seeded with the
    /// raw-socket TCP plus the HTTPS / WSS listener and channel factories
    /// shipped from <c>Opc.Ua.Bindings.Https</c>.
    /// </summary>
    public static class TestTransportBindings
    {
        /// <summary>
        /// Constructs a fresh <see cref="DefaultTransportBindingRegistry"/>
        /// pre-populated with the raw-socket TCP listener / channel
        /// factories AND the HTTPS / WSS factories from
        /// <c>Opc.Ua.Bindings.Https</c>. This is the registry test
        /// fixtures should use when they want behaviour equivalent to the
        /// pre-Phase 11 reflection-based auto-load that selected the
        /// right binding by URI scheme at first touch.
        /// </summary>
        public static DefaultTransportBindingRegistry WithAllSchemes()
        {
            DefaultTransportBindingRegistry registry = DefaultTransportBindingRegistry
                .WithDefaultTcp();
            registry.RegisterListenerFactory(new HttpsTransportListenerFactory());
            registry.RegisterListenerFactory(new OpcHttpsTransportListenerFactory());
            registry.RegisterListenerFactory(new WssTransportListenerFactory());
            registry.RegisterListenerFactory(new OpcWssTransportListenerFactory());
            registry.RegisterChannelFactory(new HttpsTransportChannelFactory());
            registry.RegisterChannelFactory(new OpcHttpsTransportChannelFactory());
            registry.RegisterChannelFactory(new WssTransportChannelFactory());
            registry.RegisterChannelFactory(new OpcWssTransportChannelFactory());
            registry.RegisterChannelFactory(new WssJsonTransportChannelFactory());
            return registry;
        }
    }
}
