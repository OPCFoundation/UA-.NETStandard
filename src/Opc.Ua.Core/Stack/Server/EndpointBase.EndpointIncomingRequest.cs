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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// An incoming request whose <see cref="IEndpointIncomingRequest.CallAsync"/> links the
    /// cancellation token it was scheduled with into its request lifetime, so the request queue
    /// need not link that token again.
    /// </summary>
    internal interface ILinksScheduledCancellation
    {
        /// <summary>
        /// The token the request was scheduled with.
        /// </summary>
        CancellationToken ScheduledCancellationToken { get; }
    }

    /// <summary>
    /// An incoming request that tells the request queue when it was received, so time spent
    /// queued counts against its <see cref="RequestHeader.TimeoutHint"/>, and that accepts a
    /// typed fault, so a queue rejection can carry a retry-after hint.
    /// </summary>
    internal interface IQueueAwareIncomingRequest
    {
        /// <summary>
        /// The <see cref="Stopwatch"/> timestamp taken when the request was
        /// received, before it was queued.
        /// </summary>
        long ReceivedTimestamp { get; }

        /// <summary>
        /// Completes the request with a fault built from <paramref name="error"/>.
        /// </summary>
        void OperationFailed(ServiceResultException error);
    }

    public abstract partial class EndpointBase
    {
        /// <summary>
        /// An object that handles an incoming request for an endpoint.
        /// </summary>
        /// <remarks>
        /// A class: the request is scheduled as an <see cref="IEndpointIncomingRequest"/> and
        /// <see cref="CallAsync"/> keeps it across awaits, so as a struct it was boxed and
        /// then copied, with write barriers, into the box and into the state machine of
        /// every request.
        /// </remarks>
        protected sealed class EndpointIncomingRequest :
            IParkableIncomingRequest,
            IEquatable<EndpointIncomingRequest>,
            ILinksScheduledCancellation,
            IQueueAwareIncomingRequest
        {
            /// <summary>
            /// Initialize the Object with a Request
            /// </summary>
            public EndpointIncomingRequest(
                EndpointBase endpoint,
                SecureChannelContext context,
                IServiceRequest request)
            {
                m_endpoint = endpoint;
                SecureChannelContext = context;
                Request = request;
                m_parkSink = request is PublishRequest || endpoint.RequestParkingPolicy?.CanPark(request) == true
                    ? new RequestParkSink()
                    : null;
                m_vts = ServiceResponsePooledValueTaskSource.Create();
                m_lease = m_vts.Lease;
                m_transportCancellationToken = default;
                m_receivedTimestamp = Stopwatch.GetTimestamp();
            }

            /// <inheritdoc/>
            public SecureChannelContext SecureChannelContext { get; }

            /// <inheritdoc/>
            public IServiceRequest Request { get; }

            /// <inheritdoc/>
            RequestParkSink? IParkableIncomingRequest.ParkSink => m_parkSink;

            /// <summary>
            /// Process an incoming request
            /// </summary>
            /// <returns></returns>
            public ValueTask<IServiceResponse> ProcessAsync(CancellationToken cancellationToken = default)
            {
                try
                {
                    m_transportCancellationToken = cancellationToken;
                    m_endpoint.ServerForContext.ScheduleIncomingRequest(this, cancellationToken);
                }
                catch (Exception e)
                {
                    m_vts.TrySetResult(m_lease, m_endpoint.CreateFault(Request, e));
                }

                return m_vts.Task;
            }

            /// <inheritdoc/>
            public async ValueTask CallAsync(CancellationToken cancellationToken = default)
            {
                // the timeout hint runs from receipt, so time spent queued counts against it
                if (!RequestQueueBudget.TryGetRemaining(this, out TimeSpan timeout))
                {
                    timeout = TimeSpan.Zero;
                }
                else if (timeout <= TimeSpan.Zero)
                {
                    m_vts.TrySetResult(
                        m_lease,
                        m_endpoint.CreateFault(Request, new ServiceResultException(StatusCodes.BadTimeout)));
                    return;
                }

                // one token source per request for the caller, the transport and the timeout hint
                using var requestLifetime = new RequestLifetime(
                    timeout,
                    cancellationToken,
                    m_transportCancellationToken);

                // Flow the park sink so a handler that parks (e.g. a held Publish
                // waiting for notifications) can release the processing worker.
                if (m_parkSink != null)
                {
                    requestLifetime.ParkSink = m_parkSink;
                }

                try
                {
                    Activity? activity = null;
                    ActivitySource activitySource = m_endpoint.RequestActivitySource;
                    if (activitySource.HasListeners())
                    {
                        // extract trace information from the request header if available
                        if (Request.RequestHeader != null &&
                            Request.RequestHeader.AdditionalHeader
                                .TryGetValue(out AdditionalParametersType? parameters) &&
                            parameters != null &&
                            TryExtractActivityContextFromParameters(
                                parameters,
                                out ActivityContext activityContext))
                        {
                            activity = activitySource.StartActivity(
                                Request.GetType().Name,
                                ActivityKind.Server,
                                activityContext);
                        }
                    }

                    using (activity)
                    {
                        ServiceDefinition service = m_endpoint.FindService(Request.TypeId);
                        IServiceResponse response = await service.InvokeAsync(Request, SecureChannelContext, requestLifetime).ConfigureAwait(false);

                        // Allow tests / diagnostics to mutate the
                        // response before dispatch via the optional
                        // ResponseMutator hook on the server.
                        IServiceResponseMutator? mutator = m_endpoint.ServerForContext?.ResponseMutator;
                        if (mutator != null)
                        {
                            response = await mutator.MutateResponseAsync(
                                Request, response, requestLifetime.CancellationToken)
                                .ConfigureAwait(false);
                        }

                        m_vts.TrySetResult(m_lease, response);
                    }
                }
                catch (Exception e)
                {
                    if (e is OperationCanceledException)
                    {
                        e = new ServiceResultException(
                            requestLifetime.StatusCode == StatusCodes.Good ? StatusCodes.BadTimeout : requestLifetime.StatusCode);
                    }
                    m_vts.TrySetResult(m_lease, m_endpoint.CreateFault(Request, e));
                }
            }

            /// <inheritdoc/>
            public void OperationCompleted(IServiceResponse? response, ServiceResult error)
            {
                if (ServiceResult.IsBad(error))
                {
                    m_vts.TrySetResult(m_lease, m_endpoint.CreateFault(Request, new ServiceResultException(error)));
                }
                else
                {
                    m_vts.TrySetResult(m_lease, response!);
                }
            }

            /// <inheritdoc/>
            void IQueueAwareIncomingRequest.OperationFailed(ServiceResultException error)
            {
                m_vts.TrySetResult(m_lease, m_endpoint.CreateFault(Request, error));
            }

            /// <inheritdoc/>
            long IQueueAwareIncomingRequest.ReceivedTimestamp => m_receivedTimestamp;

            /// <inheritdoc/>
            public override bool Equals(object? obj)
            {
                return Equals(obj as EndpointIncomingRequest);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Request.RequestHeader.GetHashCode();
            }

            /// <inheritdoc/>
            public static bool operator ==(EndpointIncomingRequest? left, EndpointIncomingRequest? right)
            {
                return left is null ? right is null : left.Equals(right);
            }

            /// <inheritdoc/>
            public static bool operator !=(EndpointIncomingRequest? left, EndpointIncomingRequest? right)
            {
                return !(left == right);
            }

            /// <inheritdoc/>
            public bool Equals(EndpointIncomingRequest? other)
            {
                return other is not null &&
                    Request.RequestHeader.Equals(other.Request.RequestHeader);
            }

            /// <inheritdoc/>
            CancellationToken ILinksScheduledCancellation.ScheduledCancellationToken =>
                m_transportCancellationToken;

            private readonly EndpointBase m_endpoint;
            private readonly ServiceResponsePooledValueTaskSource m_vts;

            /// <summary>
            /// The rental of <see cref="m_vts"/> owned by this request. Completions
            /// are accepted once and never reach a later owner of the pooled source.
            /// </summary>
            private readonly int m_lease;
            private readonly RequestParkSink? m_parkSink;

            /// <summary>
            /// The <see cref="Stopwatch"/> timestamp of receipt, before queueing.
            /// </summary>
            private readonly long m_receivedTimestamp;
            private CancellationToken m_transportCancellationToken;
        }
    }
}
