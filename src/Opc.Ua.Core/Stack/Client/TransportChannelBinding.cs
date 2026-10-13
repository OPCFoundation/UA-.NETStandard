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

namespace Opc.Ua
{
    /// <summary>
    /// Captures dispatch against a native transport owner's replacement gate.
    /// </summary>
    internal interface ITransportChannelBindingProvider
    {
        /// <summary>
        /// Captures the currently selected transport without acquiring its lifetime ownership.
        /// </summary>
        /// <param name="ct">The cancellation token supplied by the caller.</param>
        ValueTask<TransportChannelBinding> CreateTransportBindingAsync(CancellationToken ct);
    }

    /// <summary>
    /// Retains isolated transport observations and rejects generation invalidation.
    /// </summary>
    internal sealed class TransportChannelBinding : ITransportChannel
    {
        /// <summary>
        /// Initializes a new <c>TransportChannelBinding</c> instance.
        /// </summary>
        /// <param name="source">The transport channel used to initialize the instance.</param>
        /// <param name="valid">The valid used to initialize the instance.</param>
        /// <param name="send">The send used to initialize the instance.</param>
        internal TransportChannelBinding(
            ITransportChannel source,
            Func<bool> valid,
            Func<IServiceRequest, Func<bool>, CancellationToken, ValueTask<IServiceResponse>> send)
        {
            m_sourceContext = source.MessageContext;
            m_valid = () => valid() && ReferenceEquals(source.MessageContext, m_sourceContext);
            m_send = send;
            m_channelThumbprint = [.. source.ChannelThumbprint];
            m_clientCertificate = [.. source.ClientChannelCertificate];
            m_serverCertificate = [.. source.ServerChannelCertificate];
            EndpointDescription = (EndpointDescription)source.EndpointDescription.Clone();
            EndpointConfiguration = (EndpointConfiguration)source.EndpointConfiguration.Clone();
            MessageContext = CopyContext(m_sourceContext);
            OperationTimeout = source.OperationTimeout;
        }

        /// <summary>
        /// Gets the supported features for <c>TransportChannelBinding</c>.
        /// </summary>
        public TransportChannelFeatures SupportedFeatures => TransportChannelFeatures.None;

        /// <summary>
        /// Gets the endpoint description for <c>TransportChannelBinding</c>.
        /// </summary>
        public EndpointDescription EndpointDescription { get; }

        /// <summary>
        /// Gets the endpoint configuration for <c>TransportChannelBinding</c>.
        /// </summary>
        public EndpointConfiguration EndpointConfiguration { get; }

        /// <inheritdoc/>
        public byte[] ChannelThumbprint => [.. m_channelThumbprint];

        /// <inheritdoc/>
        public byte[] ClientChannelCertificate => [.. m_clientCertificate];

        /// <inheritdoc/>
        public byte[] ServerChannelCertificate => [.. m_serverCertificate];

        /// <summary>
        /// Gets or sets the message context for <c>TransportChannelBinding</c>.
        /// </summary>
        public IServiceMessageContext MessageContext { get; private set; }

        /// <summary>
        /// Gets or sets the operation timeout for <c>TransportChannelBinding</c>.
        /// </summary>
        public int OperationTimeout { get; set; }

        /// <summary>
        /// Gets whether all captured owners remain current, latching invalidation permanently.
        /// </summary>
        internal bool IsCurrent
        {
            get
            {
                if (Volatile.Read(ref m_invalidated) != 0)
                {
                    return false;
                }
                if (!m_valid())
                {
                    Interlocked.Exchange(ref m_invalidated, 1);
                    return false;
                }
                return Volatile.Read(ref m_invalidated) == 0;
            }
        }

        /// <inheritdoc/>
        /// <param name="request">The service request.</param>
        /// <param name="ct">The cancellation token supplied by the caller.</param>
        public ValueTask<IServiceResponse> SendRequestAsync(IServiceRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfInvalid();
            return m_send(request, () => IsCurrent, ct);
        }

        /// <inheritdoc/>
        /// <param name="connection">The replacement connection, which this binding cannot accept.</param>
        /// <param name="ct">The cancellation token supplied by the caller.</param>
        public ValueTask ReconnectAsync(
            ITransportWaitingConnection? connection = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            throw new ServiceResultException(
                StatusCodes.BadNotSupported, "A captured binding does not reconnect.");
        }

        /// <inheritdoc/>
        /// <param name="ct">The cancellation token supplied by the caller.</param>
        public ValueTask CloseAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Dispose();
            return default;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Interlocked.Exchange(ref m_invalidated, 1);
        }

        /// <summary>
        /// Checks that the retained source context has not been replaced.
        /// </summary>
        /// <param name="context">The service message context.</param>
        internal bool HasSourceContext(IServiceMessageContext context)
        {
            return ReferenceEquals(m_sourceContext, context);
        }

        /// <summary>
        /// Composes another owner's validation with the captured transport.
        /// </summary>
        /// <param name="valid">The additional owner-validity check.</param>
        /// <param name="context">The service message context.</param>
        internal void AddValidation(
            Func<bool> valid,
            IServiceMessageContext? context = null)
        {
            ThrowIfInvalid();
            Func<bool> previous = m_valid;
            m_valid = () => previous() && valid();
            MessageContext = context ?? MessageContext;
        }

        /// <summary>
        /// Rejects a disposed or invalidated binding before it is used.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal void ThrowIfInvalid()
        {
            if (!IsCurrent)
            {
                throw InvalidBinding();
            }
        }

        /// <summary>
        /// Creates the explicit failure for captured-binding invalidation.
        /// </summary>
        internal static ServiceResultException InvalidBinding()
        {
            return new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed, "The captured session/channel binding is no longer current.");
        }

        /// <summary>
        /// Returns a current response or releases its pooled storage before rejecting it.
        /// </summary>
        internal static IServiceResponse ValidateResponse(
            IServiceResponse response,
            Func<bool> valid,
            CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                if (!valid())
                {
                    throw InvalidBinding();
                }
                return response;
            }
            catch
            {
                (response as IPooledEncodeable)?.Reuse();
                throw;
            }
        }

        /// <summary>
        /// Copies encoding limits and retains independent namespace and server mappings.
        /// </summary>
        /// <param name="source">The service message context.</param>
        /// <param name="namespaces">The namespace table.</param>
        /// <param name="servers">The string table.</param>
        internal static ServiceMessageContext CopyContext(
            IServiceMessageContext source,
            NamespaceTable? namespaces = null,
            StringTable? servers = null)
        {
            return new ServiceMessageContext(source.Telemetry, source.Factory)
            {
                NamespaceUris = namespaces ?? new NamespaceTable(source.NamespaceUris.ToArray()),
                ServerUris = servers ?? new StringTable(source.ServerUris.ToArray()),
                MaxStringLength = source.MaxStringLength,
                MaxByteStringLength = source.MaxByteStringLength,
                MaxArrayLength = source.MaxArrayLength,
                MaxMessageSize = source.MaxMessageSize,
                MaxEncodingNestingLevels = source.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = source.MaxDecoderRecoveries
            };
        }

        private readonly byte[] m_channelThumbprint;
        private readonly byte[] m_clientCertificate;
        private readonly byte[] m_serverCertificate;
        private readonly IServiceMessageContext m_sourceContext;
        private Func<bool> m_valid;
        private readonly Func<IServiceRequest, Func<bool>, CancellationToken, ValueTask<IServiceResponse>> m_send;
        private int m_invalidated;
    }
}
