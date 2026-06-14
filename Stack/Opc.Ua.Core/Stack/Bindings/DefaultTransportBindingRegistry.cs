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
using System.Collections.Generic;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Default <see cref="ITransportBindingRegistry"/> implementation.
    /// Ships seeded with the raw-socket <c>opc.tcp</c> listener and
    /// channel factories so the most common transport is available
    /// without any explicit registration.
    /// </summary>
    /// <remarks>
    /// The registry is intentionally instance-based: each
    /// <see cref="System.IServiceProvider"/> gets its own singleton so
    /// parallel test fixtures / multi-host applications cannot race on
    /// shared global state (the historical pain-point with the removed
    /// <c>TransportBindings</c> static API).
    /// </remarks>
    public sealed class DefaultTransportBindingRegistry
        : ITransportBindingRegistry, ITransportChannelBindings
    {
        /// <summary>
        /// Constructs an empty registry. Use <see cref="WithDefaultTcp"/>
        /// for the common case that ships the raw-socket TCP factories
        /// pre-installed.
        /// </summary>
        public DefaultTransportBindingRegistry()
        {
            m_listeners = new Dictionary<string, ITransportListenerFactory>(StringComparer.Ordinal);
            m_channels = new Dictionary<string, ITransportChannelFactory>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Creates a registry seeded with the raw-socket
        /// <see cref="Utils.UriSchemeOpcTcp"/> listener
        /// (<see cref="TcpTransportListenerFactory"/>) and channel
        /// (<see cref="TcpTransportChannelFactory"/>) factories. This is
        /// the registry shape that <c>services.AddOpcTcpTransport()</c>
        /// installs by default.
        /// </summary>
        public static DefaultTransportBindingRegistry WithDefaultTcp()
        {
            var registry = new DefaultTransportBindingRegistry();
            registry.RegisterListenerFactory(new TcpTransportListenerFactory());
            registry.RegisterChannelFactory(new TcpTransportChannelFactory());
            return registry;
        }

        /// <inheritdoc/>
        public void RegisterListenerFactory(ITransportListenerFactory factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            lock (m_lock)
            {
                m_listeners[factory.UriScheme] = factory;
            }
        }

        /// <inheritdoc/>
        public void RegisterChannelFactory(ITransportChannelFactory factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            lock (m_lock)
            {
                m_channels[factory.UriScheme] = factory;
            }
        }

        /// <inheritdoc/>
        public ITransportListenerFactory? GetListenerFactory(string uriScheme)
        {
            if (uriScheme == null)
            {
                throw new ArgumentNullException(nameof(uriScheme));
            }
            lock (m_lock)
            {
                return m_listeners.TryGetValue(uriScheme, out ITransportListenerFactory? factory)
                    ? factory
                    : null;
            }
        }

        /// <inheritdoc/>
        public ITransportChannelFactory? GetChannelFactory(string uriScheme)
        {
            if (uriScheme == null)
            {
                throw new ArgumentNullException(nameof(uriScheme));
            }
            lock (m_lock)
            {
                return m_channels.TryGetValue(uriScheme, out ITransportChannelFactory? factory)
                    ? factory
                    : null;
            }
        }

        /// <inheritdoc/>
        public bool HasListenerFactory(string uriScheme)
        {
            if (uriScheme == null)
            {
                throw new ArgumentNullException(nameof(uriScheme));
            }
            lock (m_lock)
            {
                return m_listeners.ContainsKey(uriScheme);
            }
        }

        /// <inheritdoc/>
        public bool HasChannelFactory(string uriScheme)
        {
            if (uriScheme == null)
            {
                throw new ArgumentNullException(nameof(uriScheme));
            }
            lock (m_lock)
            {
                return m_channels.ContainsKey(uriScheme);
            }
        }

        /// <inheritdoc/>
        public ITransportListener? CreateListener(string uriScheme, ITelemetryContext telemetry)
        {
            if (uriScheme == null)
            {
                throw new ArgumentNullException(nameof(uriScheme));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            return GetListenerFactory(uriScheme)?.Create(telemetry);
        }

        /// <inheritdoc/>
        public ITransportChannel? CreateChannel(string uriScheme, ITelemetryContext telemetry)
        {
            if (uriScheme == null)
            {
                throw new ArgumentNullException(nameof(uriScheme));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            return GetChannelFactory(uriScheme)?.Create(telemetry);
        }

        /// <summary>
        /// Implements <see cref="ITransportChannelBindings.Create"/> by
        /// forwarding to <see cref="CreateChannel"/>; lets a single
        /// registry instance satisfy both the broad
        /// <see cref="ITransportBindingRegistry"/> surface and the
        /// narrower channel-only contract that
        /// <see cref="Opc.Ua.ClientChannelManager"/> takes.
        /// </summary>
        ITransportChannel? ITransportChannelBindings.Create(
            string uriScheme,
            ITelemetryContext telemetry)
            => CreateChannel(uriScheme, telemetry);

        private readonly System.Threading.Lock m_lock = new();
        private readonly Dictionary<string, ITransportListenerFactory> m_listeners;
        private readonly Dictionary<string, ITransportChannelFactory> m_channels;
    }
}
