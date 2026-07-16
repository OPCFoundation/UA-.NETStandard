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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Per-host registry of OPC UA transport listener and channel
    /// factories keyed by URI scheme.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Replaces the legacy <c>TransportBindings</c> static API. The
    /// registry resolves out of the host's <see cref="System.IServiceProvider"/>
    /// so two hosts (e.g. parallel test fixtures) can install different
    /// factories without racing on shared global state.
    /// </para>
    /// <para>
    /// Consumers should not call <see cref="RegisterListenerFactory"/> /
    /// <see cref="RegisterChannelFactory"/> directly; use the
    /// <c>AddOpcTcpTransport</c> / <c>AddHttpsTransport</c> /
    /// <c>AddWssTransport</c> / <c>AddKestrelOpcTcpTransport</c> /
    /// <c>AddCustomTransport&lt;,&gt;</c> DI extensions on
    /// <see cref="IOpcUaBuilder"/> which install the right factories for
    /// their respective transport packages.
    /// </para>
    /// <para>
    /// A <see cref="DefaultTransportBindingRegistry"/> is registered as
    /// a singleton by the first DI extension that touches the registry
    /// and seeds the raw-socket TCP listener / channel factories so
    /// <c>opc.tcp://</c> is always available out of the box.
    /// </para>
    /// </remarks>
    public interface ITransportBindingRegistry
    {
        /// <summary>
        /// Installs (or overrides) the listener factory for
        /// <see cref="ITransportBindingScheme.UriScheme"/>. Last
        /// registration wins so a downstream
        /// <c>AddKestrelOpcTcpTransport()</c> after
        /// <c>AddOpcTcpTransport()</c> swaps the listener implementation.
        /// </summary>
        /// <param name="factory">The listener factory to install.</param>
        void RegisterListenerFactory(ITransportListenerFactory factory);

        /// <summary>
        /// Installs (or overrides) the channel factory for
        /// <see cref="ITransportBindingScheme.UriScheme"/>. Last
        /// registration wins.
        /// </summary>
        /// <param name="factory">The channel factory to install.</param>
        void RegisterChannelFactory(ITransportChannelFactory factory);

        /// <summary>
        /// Returns the listener factory registered for the supplied
        /// <paramref name="uriScheme"/>, or <c>null</c> if no factory is
        /// installed.
        /// </summary>
        /// <param name="uriScheme">The URI scheme of the transport.</param>
        ITransportListenerFactory? GetListenerFactory(string uriScheme);

        /// <summary>
        /// Returns the channel factory registered for the supplied
        /// <paramref name="uriScheme"/>, or <c>null</c> if no factory is
        /// installed.
        /// </summary>
        /// <param name="uriScheme">The URI scheme of the transport.</param>
        ITransportChannelFactory? GetChannelFactory(string uriScheme);

        /// <summary>
        /// Creates a transport listener instance for the supplied
        /// <paramref name="uriScheme"/>. Returns <c>null</c> when no
        /// matching factory is registered.
        /// </summary>
        /// <param name="uriScheme">The URI scheme of the transport.</param>
        /// <param name="telemetry">Telemetry context for the new listener.</param>
        ITransportListener? CreateListener(string uriScheme, ITelemetryContext telemetry);

        /// <summary>
        /// Creates a transport channel instance for the supplied
        /// <paramref name="uriScheme"/>. Returns <c>null</c> when no
        /// matching factory is registered.
        /// </summary>
        /// <param name="uriScheme">The URI scheme of the transport.</param>
        /// <param name="telemetry">Telemetry context for the new channel.</param>
        ITransportChannel? CreateChannel(string uriScheme, ITelemetryContext telemetry);

        /// <summary>
        /// Returns <c>true</c> when a listener factory is registered for
        /// the supplied <paramref name="uriScheme"/>.
        /// </summary>
        bool HasListenerFactory(string uriScheme);

        /// <summary>
        /// Returns <c>true</c> when a channel factory is registered for
        /// the supplied <paramref name="uriScheme"/>.
        /// </summary>
        bool HasChannelFactory(string uriScheme);
    }
}
