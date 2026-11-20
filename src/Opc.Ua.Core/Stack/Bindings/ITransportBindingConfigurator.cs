// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Callback that configures an
    /// <see cref="ITransportBindingRegistry"/> at first resolution time.
    /// Every <c>Add*Transport()</c> DI extension adds one of these to
    /// the service collection; the
    /// <see cref="DefaultTransportBindingRegistry"/> factory iterates
    /// them in registration order so a downstream
    /// <c>AddKestrelOpcTcpTransport()</c> after
    /// <c>AddOpcTcpTransport()</c> swaps the raw-socket listener for
    /// the Kestrel-hosted one (last-writer-wins per URI scheme).
    /// </summary>
    public interface ITransportBindingConfigurator
    {
        /// <summary>
        /// Apply this configurator's listener / channel factory
        /// registrations to the supplied <paramref name="registry"/>.
        /// </summary>
        void Configure(ITransportBindingRegistry registry);
    }

    /// <summary>
    /// Adapter that wraps a configuration delegate into an
    /// <see cref="ITransportBindingConfigurator"/>.
    /// </summary>
    public sealed class TransportBindingConfigurator : ITransportBindingConfigurator
    {
        /// <summary>
        /// Constructs a configurator that runs <paramref name="configure"/>
        /// at registry-creation time.
        /// </summary>
        public TransportBindingConfigurator(Action<ITransportBindingRegistry> configure)
        {
            m_configure = configure ?? throw new ArgumentNullException(nameof(configure));
        }

        /// <inheritdoc/>
        public void Configure(ITransportBindingRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }
            m_configure(registry);
        }

        private readonly Action<ITransportBindingRegistry> m_configure;
    }
}
