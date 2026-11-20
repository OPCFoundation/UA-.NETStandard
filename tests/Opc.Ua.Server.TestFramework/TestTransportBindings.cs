// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
            var registry = DefaultTransportBindingRegistry
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
