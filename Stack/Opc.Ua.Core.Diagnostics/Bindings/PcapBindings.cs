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
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Bindings
{
    /// <summary>
    /// Convenience helper that installs the Pcap capture bindings into a
    /// supplied <see cref="ITransportBindingRegistry"/>. Use this when the
    /// application is not built around
    /// Microsoft.Extensions.DependencyInjection and therefore cannot rely on
    /// the <c>AddPcap</c> extension method.
    /// </summary>
    /// <remarks>
    /// Installing a binding only makes the affected channels capture-<i>aware</i>;
    /// no bytes are recorded until a capture session is started (which flips
    /// the returned <see cref="IChannelCaptureRegistry"/>'s
    /// <c>CurrentObserver</c> from <c>null</c> to a live sink). Build a
    /// <c>DefaultCaptureSourceFactory</c> over the returned registry, hand it to
    /// a <c>CaptureSessionManager</c>, and call <c>StartAsync</c> to begin
    /// recording. See <c>Docs/Diagnostics.md</c> for the full non-DI recipe.
    /// </remarks>
    public static class PcapBindings
    {
        /// <summary>
        /// Installs both the client channel binding and (when the registry
        /// exposes an <c>opc.tcp</c> listener factory) the server listener
        /// binding into the supplied <paramref name="bindingRegistry"/>. The
        /// returned <see cref="IChannelCaptureRegistry"/> is the coordination
        /// point a <c>CaptureSessionManager</c> uses to switch recording on or
        /// off.
        /// </summary>
        /// <param name="bindingRegistry">The transport binding registry to
        /// install the Pcap bindings into.</param>
        public static IChannelCaptureRegistry Install(ITransportBindingRegistry bindingRegistry)
        {
            ArgumentNullException.ThrowIfNull(bindingRegistry);
            var registry = new ChannelCaptureRegistry();
            Install(bindingRegistry, registry);
            return registry;
        }

        /// <summary>
        /// Installs both the client channel binding and the server listener
        /// binding using the supplied
        /// <see cref="IChannelCaptureRegistry"/> so a single session observer
        /// covers inbound and outbound traffic.
        /// </summary>
        public static void Install(
            ITransportBindingRegistry bindingRegistry,
            IChannelCaptureRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(bindingRegistry);
            ArgumentNullException.ThrowIfNull(registry);

            InstallClient(bindingRegistry, registry);
            InstallServer(bindingRegistry, registry);
        }

        /// <summary>
        /// Installs only the client channel binding for <c>opc.tcp</c> into
        /// the supplied <paramref name="bindingRegistry"/> so every OPC UA
        /// client channel created through <c>ClientChannelManager</c> becomes
        /// capture-aware. Use this against
        /// <see cref="Opc.Ua.ClientChannelManager.DefaultChannelBindings"/>
        /// (the non-DI client default) or a DI-resolved registry.
        /// </summary>
        public static IChannelCaptureRegistry InstallClient(ITransportBindingRegistry bindingRegistry)
        {
            ArgumentNullException.ThrowIfNull(bindingRegistry);
            var registry = new ChannelCaptureRegistry();
            InstallClient(bindingRegistry, registry);
            return registry;
        }

        /// <summary>
        /// Installs only the client channel binding using the supplied
        /// <see cref="IChannelCaptureRegistry"/>.
        /// </summary>
        public static void InstallClient(
            ITransportBindingRegistry bindingRegistry,
            IChannelCaptureRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(bindingRegistry);
            ArgumentNullException.ThrowIfNull(registry);

            bindingRegistry.RegisterChannelFactory(new PcapTransportChannelBinding(registry));
        }

        /// <summary>
        /// Installs only the server listener binding for <c>opc.tcp</c> into
        /// the supplied <paramref name="bindingRegistry"/> so every inbound
        /// client→server channel accepted by a hosted OPC UA server becomes
        /// capture-aware. Use this against
        /// <c>server.Server.TransportBindings</c> before the server is
        /// started. This is a no-op when the registry has no <c>opc.tcp</c>
        /// listener factory or the binding is already installed.
        /// </summary>
        public static IChannelCaptureRegistry InstallServer(ITransportBindingRegistry bindingRegistry)
        {
            ArgumentNullException.ThrowIfNull(bindingRegistry);
            var registry = new ChannelCaptureRegistry();
            InstallServer(bindingRegistry, registry);
            return registry;
        }

        /// <summary>
        /// Installs only the server listener binding using the supplied
        /// <see cref="IChannelCaptureRegistry"/>. The current <c>opc.tcp</c>
        /// listener factory is wrapped by a
        /// <see cref="PcapTransportListenerBinding"/>; the wrapping is skipped
        /// when no listener factory is registered or one is already installed.
        /// </summary>
        public static void InstallServer(
            ITransportBindingRegistry bindingRegistry,
            IChannelCaptureRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(bindingRegistry);
            ArgumentNullException.ThrowIfNull(registry);

            ITransportListenerFactory? inner = bindingRegistry.GetListenerFactory(Utils.UriSchemeOpcTcp);
            if (inner is null or PcapTransportListenerBinding)
            {
                return;
            }

            bindingRegistry.RegisterListenerFactory(new PcapTransportListenerBinding(inner, registry));
        }
    }
}
