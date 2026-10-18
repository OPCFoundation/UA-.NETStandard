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
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Explicitly opens ordinary Client/Server Services on a borrowed native context.
    /// Native-only applications never need to register or invoke this service.
    /// </summary>
    public interface IDataChannelClientServerClient
    {
        /// <summary>
        /// Opens the reserved flow and completes ordinary HEL/ACK/OPN using the existing UASC stack.
        /// The caller retains the native context and connection for the returned lane's lifetime.
        /// </summary>
        ValueTask<DataChannelClientServerConnection> ConnectAsync(
            DataChannelContext context,
            Uri serviceEndpointUrl,
            TransportChannelSettings settings,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Composes the reserved flow with the existing UACP/UASC pipeline, without a TCP bridge.
    /// Inner signed application certificates must match the native peer URI and key.
    /// </summary>
    public sealed class DataChannelClientServerClient : IDataChannelClientServerClient
    {
        /// <summary>
        /// Creates an explicitly enabled lane client with injectable clock, pools and telemetry.
        /// </summary>
        public DataChannelClientServerClient(
            ITelemetryContext telemetry,
            DataChannelClientServerOptions? options = null,
            IBufferManagerFactory? bufferManagerFactory = null,
            TimeProvider? timeProvider = null)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_options = options ?? new DataChannelClientServerOptions();
            m_options.Validate();
            m_buffers = bufferManagerFactory ?? DefaultBufferManagerFactory.Instance;
            m_clock = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public async ValueTask<DataChannelClientServerConnection> ConnectAsync(
            DataChannelContext context,
            Uri serviceEndpointUrl,
            TransportChannelSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            cancellationToken.ThrowIfCancellationRequested();
            DataChannelClientServerSecurity.ValidateContext(context);
            DataChannelClientServerSecurity.ValidateEndpoint(serviceEndpointUrl, settings.Description);
            EndpointConfiguration configuration = settings.Configuration ??
                throw new ArgumentException("An endpoint configuration is required.", nameof(settings));
            if (configuration.MaxBufferSize < TcpMessageLimits.MinBufferSize || configuration.OperationTimeout <= 0 ||
                configuration.MaxMessageSize <= 0 || settings.Factory == null || settings.CertificateValidator == null ||
                settings.DataChannels != null)
            {
                throw new ArgumentException("Bounded ordinary UASC settings and certificate validation are required.",
                    nameof(settings));
            }
            DataChannelPeerIdentity identity = context.Identity.Peer;
            if (settings.Description!.Server?.ApplicationUri != identity.PeerApplicationUri)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateUriInvalid);
            }
            DataChannelClientServerSecurity.ValidateIdentity(
                settings.ClientCertificate, identity.LocalApplicationUri, identity.LocalKeyThumbprint);
            DataChannelClientServerSecurity.ValidateIdentity(
                settings.ServerCertificate, identity.PeerApplicationUri, identity.PeerKeyThumbprint);
            if (Interlocked.Increment(ref m_lanes) > m_options.MaxLanes)
            {
                Interlocked.Decrement(ref m_lanes);
                throw new ServiceResultException(StatusCodes.BadTcpNotEnoughResources);
            }

            DataChannel? flow = null;
            DataChannelClientServerByteTransportFactory? factory = null;
            UaSCUaBinaryTransportChannel? channel = null;
            try
            {
                flow = await context.OpenAsync(
                    m_options.CreateParameters(configuration.MaxBufferSize),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                int chunkSize = checked((int)flow.Parameters.MaxMessageSize);
                if (chunkSize < TcpMessageLimits.MinBufferSize)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTcpNotEnoughResources, "The admitted flow is too small for UACP.");
                }
                factory = new DataChannelClientServerByteTransportFactory(context, flow, serviceEndpointUrl);
                channel = new UaSCUaBinaryTransportChannel(factory, m_telemetry, m_clock, m_buffers);
                var boundConfiguration = (EndpointConfiguration)configuration.Clone();
                boundConfiguration.MaxBufferSize = Math.Min(chunkSize, configuration.MaxBufferSize);
                var boundSettings = new TransportChannelSettings
                {
                    Description = (EndpointDescription)settings.Description.Clone(),
                    Configuration = boundConfiguration,
                    Factory = settings.Factory,
                    NamespaceUris = settings.NamespaceUris,
                    CertificateValidator = new DataChannelClientServerSecurity(context, settings.CertificateValidator),
                    SecurityPolicyRegistry = settings.SecurityPolicyRegistry,
                    ClientCertificate = settings.ClientCertificate!.AddRef(),
                    ClientCertificateChain = settings.ClientCertificateChain?.AddRef(),
                    ServerCertificate = settings.ServerCertificate!.AddRef()
                };
                await channel.OpenAsync(serviceEndpointUrl, boundSettings, cancellationToken).ConfigureAwait(false);
                return new DataChannelClientServerConnection(
                    context, flow, channel, factory, m_options.MaxPendingRequests, ReleaseLane);
            }
            catch
            {
                try
                {
                    channel?.Dispose();
                    if (factory != null)
                    {
                        await factory.CloseAsync().ConfigureAwait(false);
                    }
                    else if (flow != null)
                    {
                        await flow.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    ReleaseLane();
                }
                throw;
            }
        }

        /// <summary>
        /// Releases the client-level reservation only after the lane has drained.
        /// </summary>
        private void ReleaseLane()
        {
            Interlocked.Decrement(ref m_lanes);
        }

        private readonly ITelemetryContext m_telemetry;
        private readonly DataChannelClientServerOptions m_options;
        private readonly IBufferManagerFactory m_buffers;
        private readonly TimeProvider m_clock;
        private int m_lanes;
    }
}
