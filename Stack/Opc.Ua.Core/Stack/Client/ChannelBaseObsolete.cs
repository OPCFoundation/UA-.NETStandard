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
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// The base interface for client proxies.
    /// </summary>
    [Obsolete("Use ITransportChannel instead.")]
    public interface IChannelBase : IDisposable;

    /// <summary>
    /// Obsolete, use transport channel instead
    /// </summary>
    [Obsolete("Use ITransportChannel instead.")]
    public interface ISessionChannel : IChannelBase;

    /// <summary>
    /// Obsolete, use transport channel instead
    /// </summary>
    [Obsolete("Use ITransportChannel instead.")]
    public interface IDiscoveryChannel : IChannelBase;

    /// <summary>
    /// Obsolete, use transport channel instead
    /// </summary>
    [Obsolete("Use ITransportChannel instead.")]
    public interface IRegistrationChannel : IChannelBase;

    /// <summary>
    /// Obsolete Session channel methods
    /// </summary>
    [Obsolete("Use UaChannelBase methods instead.")]
    public static class SessionChannel
    {
        /// <summary>
        /// Creates a new transport channel.
        /// </summary>
        [Obsolete("Use ClientChannelFactory.CreateChannelAsync method instead.")]
        public static ITransportChannel Create(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 clientCertificate,
            IServiceMessageContext messageContext)
        {
            return ClientChannelManager.CreateUaBinaryChannelAsync(
                configuration,
                description,
                endpointConfiguration,
                clientCertificate,
                null,
                messageContext,
                null).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a new transport channel.
        /// </summary>
        [Obsolete("Use ClientChannelFactory.CreateChannelAsync method instead.")]
        public static ITransportChannel Create(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 clientCertificate,
            X509Certificate2Collection clientCertificateChain,
            IServiceMessageContext messageContext)
        {
            return ClientChannelManager.CreateUaBinaryChannelAsync(
                configuration,
                description,
                endpointConfiguration,
                clientCertificate,
                clientCertificateChain,
                messageContext,
                null).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a new transport channel.
        /// </summary>
        [Obsolete("Use ClientChannelFactory.CreateChannelAsync method instead.")]
        public static ITransportChannel Create(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 clientCertificate,
            X509Certificate2Collection clientCertificateChain,
            IServiceMessageContext messageContext)
        {
            // create a UA binary channel.
            return ClientChannelManager.CreateUaBinaryChannelAsync(
                configuration,
                connection,
                description,
                endpointConfiguration,
                clientCertificate,
                clientCertificateChain,
                messageContext,
                null).AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Obsolete discovery channel methods
    /// </summary>
    [Obsolete("Use DiscoveryClient.CreateAsync instead to create a discovery client.")]
    public static class DiscoveryChannel
    {
        /// <summary>
        /// Creates a new transport channel for discovery
        /// </summary>
        [Obsolete("Use DiscoveryClient.CreateAsync instead to create a discovery client.")]
        public static ITransportChannel Create(
            Uri discoveryUrl,
            EndpointConfiguration endpointConfiguration,
            IServiceMessageContext messageContext,
            X509Certificate2 clientCertificate = null)
        {
            return DiscoveryClient.CreateChannelAsync(
                discoveryUrl,
                endpointConfiguration,
                messageContext,
                clientCertificate).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a new transport channel for discovery
        /// </summary>
        [Obsolete("Use CreateAsync instead.")]
        public static ITransportChannel Create(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            EndpointConfiguration endpointConfiguration,
            IServiceMessageContext messageContext,
            X509Certificate2 clientCertificate = null)
        {
            return DiscoveryClient.CreateChannelAsync(
                configuration,
                connection,
                endpointConfiguration,
                messageContext,
                clientCertificate).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a new transport channel for discovery
        /// </summary>
        [Obsolete("Use CreateAsync instead.")]
        public static ITransportChannel Create(
            ApplicationConfiguration configuration,
            Uri discoveryUrl,
            EndpointConfiguration endpointConfiguration,
            IServiceMessageContext messageContext,
            X509Certificate2 clientCertificate = null)
        {
            return DiscoveryClient.CreateChannelAsync(
                configuration,
                discoveryUrl,
                endpointConfiguration,
                messageContext,
                clientCertificate).AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Obsolete Registration channel methods
    /// </summary>
    [Obsolete("Use RegistrationClient.CreateAsync instead to create a registrations client.")]
    public static class RegistrationChannel
    {
        /// <summary>
        /// Creates a new transport channel that supports registration
        /// </summary>
        [Obsolete("Use ClientChannelFactory.CreateChannelAsync instead.")]
        public static ITransportChannel Create(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 clientCertificate,
            IServiceMessageContext messageContext)
        {
            return ClientChannelManager.CreateUaBinaryChannelAsync(
                configuration,
                description,
                endpointConfiguration,
                clientCertificate,
                null,
                messageContext,
                null).AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// A base class for UA channel objects used access UA interfaces
    /// </summary>
    /// <typeparam name="TChannel"></typeparam>
    [Obsolete("Use UAChannelBase.")]
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

    /// <summary>
    /// Legacy api to be removed
    /// </summary>
    public static class ChannelBaseObsolete
    {
        /// <summary>
        /// Schedules an outgoing request.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        [Obsolete("WCF channels are not supported anymore.")]
        public static void ScheduleOutgoingRequest(
            this IChannelBase channel,
            IChannelOutgoingRequest request)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// The client side implementation of the InvokeService service contract.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        [Obsolete("WCF channels are not supported anymore.")]
        public static InvokeServiceResponseMessage InvokeService(
            this IChannelBase channel,
            InvokeServiceMessage request)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// The operation contract for the InvokeService service.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        [Obsolete("WCF channels are not supported anymore.")]
        public static IAsyncResult BeginInvokeService(
            this IChannelBase channel,
            InvokeServiceMessage request,
            AsyncCallback callback,
            object asyncState)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// The method used to retrieve the results of a InvokeService service request.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        [Obsolete("WCF channels are not supported anymore.")]
        public static InvokeServiceResponseMessage EndInvokeService(
            this IChannelBase channel,
            IAsyncResult result)
        {
            throw new NotImplementedException();
        }
    }

    public partial class UaChannelBase<TChannel> : IChannelBase
        where TChannel : class, IChannelBase
    {
        /// <summary>
        /// An async result object that wraps the UA channel.
        /// Satisfies the model compiler generating channel base classes.
        /// When removing also remove the entire Opc.Ua.Channels file.
        /// </summary>
        [Obsolete("WCF channels are not supported anymore.")]
        protected class UaChannelAsyncResult : AsyncResultBase
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="UaChannelAsyncResult"/> class.
            /// </summary>
            /// <param name="channel">The channel.</param>
            /// <param name="callback">The callback.</param>
            /// <param name="callbackData">The callback data.</param>
            /// <param name="logger">A contextual logger to log to</param>
            public UaChannelAsyncResult(
                TChannel channel,
                AsyncCallback callback,
                object callbackData,
                ILogger logger = null)
                : base(callback, callbackData, 0, logger)
            {
                Channel = channel;
            }

            /// <summary>
            /// Gets the wrapped channel.
            /// </summary>
            /// <value>The wrapped channel.</value>
            public TChannel Channel { get; }

            /// <summary>
            /// Called when asynchronous operation completes.
            /// </summary>
            /// <param name="ar">The asynchronous result object.</param>
            public void OnOperationCompleted(IAsyncResult ar)
            {
                try
                {
                    // check if the begin operation has had a chance to complete.
                    lock (Lock)
                    {
                        InnerResult ??= ar;
                    }

                    // signal that the operation is complete.
                    OperationCompleted();
                }
                catch (Exception e)
                {
                    m_logger.LogError(
                        e,
                        "Unexpected exception invoking UaChannelAsyncResult callback function.");
                }
            }

            /// <summary>
            /// Checks for a valid IAsyncResult object and waits for the operation to complete.
            /// </summary>
            /// <param name="ar">The IAsyncResult object for the operation.</param>
            /// <returns>The oject that </returns>
            /// <exception cref="ArgumentException"></exception>
            /// <exception cref="ServiceResultException"></exception>
            public static new UaChannelAsyncResult WaitForComplete(IAsyncResult ar)
            {
                if (ar is not UaChannelAsyncResult asyncResult)
                {
                    throw new ArgumentException(
                        "End called with an invalid IAsyncResult object.",
                        nameof(ar));
                }

                if (!asyncResult.WaitForComplete())
                {
                    throw new ServiceResultException(StatusCodes.BadTimeout);
                }

                return asyncResult;
            }
        }
    }

    /// <summary>
    /// An interface to an object that manages a request received from a client.
    /// </summary>
    [Obsolete("WCF channels are no more supported.")]
    public interface IChannelOutgoingRequest
    {
        /// <summary>
        /// Gets the request.
        /// </summary>
        /// <value>The request.</value>
        IServiceRequest Request { get; }

        /// <summary>
        /// Gets the handler that must be used to send the request.
        /// </summary>
        /// <value>The send request handler.</value>
        ChannelSendRequestEventHandler Handler { get; }

        /// <summary>
        /// Used to call the default synchronous handler.
        /// </summary>
        /// <remarks>
        /// This method may block the current thread so the caller must not call in the
        /// thread that calls IServerBase.ScheduleIncomingRequest().
        /// This method always traps any exceptions and reports them to the client as a fault.
        /// </remarks>
        void CallSynchronously();

        /// <summary>
        /// Used to indicate that the asynchronous operation has completed.
        /// </summary>
        /// <param name="response">The response. May be null if an error is provided.</param>
        /// <param name="error">An error to result as a fault.</param>
        void OperationCompleted(IServiceResponse response, ServiceResult error);
    }

    /// <summary>
    /// A delegate used to dispatch outgoing service requests.
    /// </summary>
    [Obsolete("WCF channels are not supported anymore.")]
    public delegate IServiceResponse ChannelSendRequestEventHandler(IServiceRequest request);
}
