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
    /// <see cref="IServiceProvider"/> gets its own singleton so
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

        /// <summary>
        /// Creates a registry seeded with raw-socket TCP factories AND
        /// any optional HTTPS / WSS listener / channel factories from
        /// <c>Opc.Ua.Bindings.Https</c> when that assembly is already
        /// loaded into the current <see cref="AppDomain"/>. The
        /// reflection probe is read-only and never triggers an assembly
        /// load, so consumers that did not reference
        /// <c>Opc.Ua.Bindings.Https</c> pay nothing for the discovery
        /// pass. This is the fallback the <see cref="ClientChannelManager"/>
        /// uses when no <see cref="ITransportChannelBindings"/> was
        /// supplied — it mirrors the pre-Phase 11 reflection-based
        /// auto-load that selected the right binding by URI scheme at
        /// first touch.
        /// </summary>
        /// <remarks>
        /// Production code should prefer the explicit DI extensions
        /// (<c>AddOpcTcpTransport()</c> / <c>AddHttpsTransport()</c> /
        /// <c>AddWssTransport()</c> on <see cref="IOpcUaBuilder"/>) which
        /// give each <see cref="IServiceProvider"/> its own
        /// scoped registry and avoid implicit reflection at all.
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
            "Auto-discovers optional HTTPS/WSS listener/channel factories from " +
            "Opc.Ua.Bindings.Https via reflection. Prefer the AddHttpsTransport / " +
            "AddWssTransport DI extensions for NativeAOT-friendly registration.")]
        public static DefaultTransportBindingRegistry WithDefaultBindings()
        {
            DefaultTransportBindingRegistry registry = WithDefaultTcp();
            // HTTPS-binary channel factories live in Opc.Ua.Core itself
            // (they wrap System.Net.Http), so they can be registered
            // without touching the optional Opc.Ua.Bindings.Https assembly.
            registry.RegisterChannelFactory(new HttpsTransportChannelFactory());
            registry.RegisterChannelFactory(new OpcHttpsTransportChannelFactory());
            // The HTTPS listener factories and the WSS channel / listener
            // factories live in Opc.Ua.Bindings.Https — discovered by
            // reflection when that assembly is in the AppDomain.
            RegisterOptionalHttpsBindings(registry);
            return registry;
        }

        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
            "Reflection-based discovery of optional HTTPS / WSS binding types.")]
        private static void RegisterOptionalHttpsBindings(DefaultTransportBindingRegistry registry)
        {
            System.Reflection.Assembly? bindings = FindOrLoadHttpsBindingsAssembly();
            if (bindings is null)
            {
                return;
            }
            TryRegisterFactory(
                bindings,
                "Opc.Ua.Bindings.HttpsTransportListenerFactory",
                factory => registry.RegisterListenerFactory((ITransportListenerFactory)factory));
            TryRegisterFactory(
                bindings,
                "Opc.Ua.Bindings.OpcHttpsTransportListenerFactory",
                factory => registry.RegisterListenerFactory((ITransportListenerFactory)factory));
            TryRegisterFactory(
                bindings,
                "Opc.Ua.Bindings.WssTransportListenerFactory",
                factory => registry.RegisterListenerFactory((ITransportListenerFactory)factory));
            TryRegisterFactory(
                bindings,
                "Opc.Ua.Bindings.OpcWssTransportListenerFactory",
                factory => registry.RegisterListenerFactory((ITransportListenerFactory)factory));
            TryRegisterFactory(
                bindings,
                "Opc.Ua.Bindings.WssTransportChannelFactory",
                factory => registry.RegisterChannelFactory((ITransportChannelFactory)factory));
            TryRegisterFactory(
                bindings,
                "Opc.Ua.Bindings.OpcWssTransportChannelFactory",
                factory => registry.RegisterChannelFactory((ITransportChannelFactory)factory));
            TryRegisterFactory(
                bindings,
                "Opc.Ua.Bindings.WssJsonTransportChannelFactory",
                factory => registry.RegisterChannelFactory((ITransportChannelFactory)factory));
        }

        /// <summary>
        /// Find the Opc.Ua.Bindings.Https assembly in the current AppDomain
        /// or, when not yet loaded, attempt a best-effort
        /// <see cref="System.Reflection.Assembly.Load(System.Reflection.AssemblyName)"/>
        /// by simple name so the auto-discovery path matches the pre-Phase 11
        /// behaviour for consumers that statically reference the binding via
        /// project / package reference (the assembly is in the probe path even
        /// though no type was statically used yet).
        /// </summary>
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
            "Best-effort load of Opc.Ua.Bindings.Https; types resolved by full name.")]
        private static System.Reflection.Assembly? FindOrLoadHttpsBindingsAssembly()
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string? name = assembly.GetName().Name;
                if (!string.IsNullOrEmpty(name) &&
                    name.EndsWith("Bindings.Https", StringComparison.Ordinal))
                {
                    return assembly;
                }
            }
            string? coreName = typeof(DefaultTransportBindingRegistry).Assembly.GetName().Name;
            if (string.IsNullOrEmpty(coreName))
            {
                return null;
            }
            int offset = coreName.IndexOf("Core", StringComparison.Ordinal);
            if (offset < 0)
            {
                return null;
            }
#pragma warning disable CA1845 // Substring+'+' kept for predictable behaviour across all TFMs
            // (the span-based string.Concat overload silently affects the
            // Assembly.Load probing path observed in CI on net10 PCap tests).
            string candidate = coreName[..offset] + "Bindings.Https";
#pragma warning restore CA1845
            try
            {
                return System.Reflection.Assembly.Load(new System.Reflection.AssemblyName(candidate));
            }
            catch
            {
                // Best-effort; the binding is optional and may simply not be
                // deployed. Consumers that need HTTPS / WSS in this case must
                // register the factories explicitly via the DI extensions on
                // Opc.Ua.Bindings.Https.
                return null;
            }
        }

        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
            "Reflection-based instantiation of binding factory types.")]
        private static void TryRegisterFactory(
            System.Reflection.Assembly assembly,
            string typeName,
            Action<object> register)
        {
            Type? type = assembly.GetType(typeName, throwOnError: false);
            if (type is null)
            {
                return;
            }
            object? instance = Activator.CreateInstance(type);
            if (instance is not null)
            {
                register(instance);
            }
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
        /// <see cref="ClientChannelManager"/> takes.
        /// </summary>
        ITransportChannel? ITransportChannelBindings.Create(
            string uriScheme,
            ITelemetryContext telemetry)
        {
            return CreateChannel(uriScheme, telemetry);
        }

        private readonly System.Threading.Lock m_lock = new();
        private readonly Dictionary<string, ITransportListenerFactory> m_listeners;
        private readonly Dictionary<string, ITransportChannelFactory> m_channels;
    }
}
