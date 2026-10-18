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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Opens explicitly selected UASC data contexts without requiring a Session.
    /// </summary>
    public interface IUascDataChannelClient
    {
        /// <summary>
        /// Authenticates the selected endpoint and completes protected profile confirmation.
        /// The returned connection owns the transport and its context.
        /// </summary>
        ValueTask<UascDataChannelConnection> ConnectAsync(
            Uri endpointUrl,
            TransportChannelSettings settings,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Uses a per-host transport binding registry for raw TCP, Kestrel TCP peers or binary WSS.
    /// </summary>
    public sealed class UascDataChannelClient : IUascDataChannelClient
    {
        /// <summary>
        /// Creates an injectable client using the host's transport factories.
        /// </summary>
        public UascDataChannelClient(ITransportBindingRegistry bindings, ITelemetryContext telemetry)
        {
            m_bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Creates a direct client with one explicit transport factory and no global registration.
        /// </summary>
        public UascDataChannelClient(ITransportChannelFactory factory, ITelemetryContext telemetry)
            : this(CreateRegistry(factory), telemetry)
        {
        }

        /// <inheritdoc/>
        public async ValueTask<UascDataChannelConnection> ConnectAsync(
            Uri endpointUrl,
            TransportChannelSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (endpointUrl == null)
            {
                throw new ArgumentNullException(nameof(endpointUrl));
            }
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            cancellationToken.ThrowIfCancellationRequested();
            DataChannelCarrierKind kind = UascDataChannelEndpoint.Validate(
                endpointUrl,
                settings.Description,
                settings.DataChannels,
                settings.AllowDataChannelIntegrity) ?? throw UascDataChannelEndpoint.Unsupported();

            ITransportChannel transport = m_bindings.CreateChannel(endpointUrl.Scheme, m_telemetry) ??
                throw UascDataChannelEndpoint.Unsupported();
            try
            {
                if (transport is not IDataChannelTransportChannel dataTransport)
                {
                    throw UascDataChannelEndpoint.Unsupported();
                }
                await dataTransport.OpenAsync(endpointUrl, settings, cancellationToken).ConfigureAwait(false);
                if (dataTransport.DataChannels?.IsReady != true || dataTransport.DataChannels.CarrierKind != kind)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "The selected transport did not confirm an authenticated data context.");
                }
                return new UascDataChannelConnection(dataTransport, kind);
            }
            catch
            {
                transport.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates an isolated direct-use registry.
        /// </summary>
        private static DefaultTransportBindingRegistry CreateRegistry(ITransportChannelFactory factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            var registry = new DefaultTransportBindingRegistry();
            registry.RegisterChannelFactory(factory);
            return registry;
        }

        private readonly ITransportBindingRegistry m_bindings;
        private readonly ITelemetryContext m_telemetry;
    }

    /// <summary>
    /// Owns a confirmed UASC data connection and, for shared mode, its ordinary Service channel.
    /// </summary>
    public sealed class UascDataChannelConnection : IAsyncDisposable
    {
        /// <summary>
        /// Creates the connection only after transport authentication and profile confirmation.
        /// </summary>
        internal UascDataChannelConnection(IDataChannelTransportChannel transport, DataChannelCarrierKind kind)
        {
            m_transport = transport;
            m_kind = kind;
        }

        /// <summary>
        /// Gets the current authenticated context. A transport reconnect creates a fresh context;
        /// existing flows and grants are never transferred to it.
        /// </summary>
        public DataChannelContext Context => m_transport.DataChannels ??
            throw new ServiceResultException(StatusCodes.BadNotConnected);

        /// <summary>
        /// Gets the authenticated UASC channel identifier shared by flow and Service traffic,
        /// or zero when the physical channel is closed.
        /// </summary>
        public uint SecureChannelId => m_transport.CurrentToken?.ChannelId ?? 0;

        /// <summary>
        /// Gets the same ordinary Service transport for shared mode, or null for dedicated mode.
        /// </summary>
        public ITransportChannel? Services => m_kind == DataChannelCarrierKind.SharedUasc ? m_transport : null;

        /// <summary>
        /// Closes the authenticated transport and all its flows, preserving writer-owned buffers.
        /// </summary>
        public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref m_closed, 1) != 0)
            {
                return;
            }
            try
            {
                await m_transport.CloseAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (m_transport.IsDisposed)
            {
                // A shared Session can dispose the same transport while unwinding a failed Open.
            }
            finally
            {
                m_transport.Dispose();
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return CloseAsync();
        }

        private readonly IDataChannelTransportChannel m_transport;
        private readonly DataChannelCarrierKind m_kind;
        private int m_closed;
    }
}
