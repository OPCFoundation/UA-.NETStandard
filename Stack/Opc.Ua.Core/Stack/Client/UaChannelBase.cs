/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Create binary channels
    /// </summary>
    public static class UaChannelBase
    {
        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static Task<ITransportChannel> CreateUaBinaryChannelAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 clientCertificate,
            X509Certificate2Collection clientCertificateChain,
            IServiceMessageContext messageContext,
            CancellationToken ct = default)
        {
            return ClientChannelManager.CreateUaBinaryChannelAsync(
                configuration,
                connection,
                description,
                endpointConfiguration,
                clientCertificate,
                clientCertificateChain,
                messageContext,
                null,
                ct).AsTask();
        }

        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="description">The description for the endpoint.</param>
        /// <param name="endpointConfiguration">The configuration to use with the endpoint.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="clientCertificateChain">The client certificate chain.</param>
        /// <param name="messageContext">The message context to use when serializing the messages.</param>
        /// <param name="ct">The cancellation token</param>
        /// <exception cref="ServiceResultException"></exception>
        public static Task<ITransportChannel> CreateUaBinaryChannelAsync(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 clientCertificate,
            X509Certificate2Collection clientCertificateChain,
            IServiceMessageContext messageContext,
            CancellationToken ct = default)
        {
            return ClientChannelManager.CreateUaBinaryChannelAsync(
                configuration,
                description,
                endpointConfiguration,
                clientCertificate,
                clientCertificateChain,
                messageContext,
                null,
                ct).AsTask();
        }

        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="description">The description for the endpoint.</param>
        /// <param name="endpointConfiguration">The configuration to use with the endpoint.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="messageContext">The message context to use when serializing the messages.</param>
        /// <param name="ct">The cancellation token</param>
        public static Task<ITransportChannel> CreateUaBinaryChannelAsync(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 clientCertificate,
            IServiceMessageContext messageContext,
            CancellationToken ct = default)
        {
            return CreateUaBinaryChannelAsync(
                configuration,
                description,
                endpointConfiguration,
                clientCertificate,
                null,
                messageContext,
                ct);
        }

        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        [Obsolete("Use ClientChannelFactory.CreateChannelAsync instead")]
        public static ITransportChannel CreateUaBinaryChannel(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 clientCertificate,
            X509Certificate2Collection clientCertificateChain,
            IServiceMessageContext messageContext)
        {
            return CreateUaBinaryChannelAsync(
                configuration,
                connection,
                description,
                endpointConfiguration,
                clientCertificate,
                clientCertificateChain,
                messageContext,
                default).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        [Obsolete("Use ClientChannelFactory.CreateChannelAsync instead")]
        public static ITransportChannel CreateUaBinaryChannel(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 clientCertificate,
            IServiceMessageContext messageContext)
        {
            return CreateUaBinaryChannelAsync(
                configuration,
                description,
                endpointConfiguration,
                clientCertificate,
                messageContext,
                default).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        [Obsolete("Use ClientChannelFactory.CreateChannelAsync instead")]
        public static ITransportChannel CreateUaBinaryChannel(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 clientCertificate,
            X509Certificate2Collection clientCertificateChain,
            IServiceMessageContext messageContext)
        {
            return CreateUaBinaryChannelAsync(
                configuration,
                description,
                endpointConfiguration,
                clientCertificate,
                clientCertificateChain,
                messageContext,
                default).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// The base interface for client proxies.
    /// </summary>
    public interface IChannelBase : IDisposable;

    /// <summary>
    /// A base class for UA channel objects used access UA interfaces
    /// </summary>
    /// <typeparam name="TChannel"></typeparam>
    public partial class UaChannelBase<TChannel> : IChannelBase
        where TChannel : class, IChannelBase
    {
        /// <summary>
        /// This must be set by the derived class to initialize the telemtry system
        /// </summary>
        public required ITelemetryContext Telemetry
        {
            get => m_telemetry;
            init
            {
                m_telemetry = value;
                m_logger = value.CreateLogger(this);
            }
        }

        /// <summary>
        /// Initializes the object with the specified binding and endpoint address.
        /// </summary>
        protected UaChannelBase(ITelemetryContext telemetry = null)
        {
            Telemetry = telemetry;
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Utils.SilentDispose(Channel);
                Channel = null;
            }
        }

        /// <summary>
        /// Gets the inner channel.
        /// </summary>
        /// <value>The channel.</value>
        protected TChannel Channel { get; private set; }

        /// <summary>
        /// Logger to be used by the concrete channel implementation. Shall
        /// not be used outside of the channel inheritance hierarchy. Create
        /// new logger from telemetry context.
        /// </summary>
#pragma warning disable IDE1006 // Naming Styles
        protected ILogger m_logger { get; private set; } = LoggerUtils.Null.Logger;
#pragma warning restore IDE1006 // Naming Styles

        private readonly ITelemetryContext m_telemetry;
    }
}
