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
    public abstract partial class EndpointBase
    {
        /// <summary>
        /// An object that handles an incoming request for an endpoint.
        /// </summary>
        protected readonly struct EndpointIncomingRequest : IEndpointIncomingRequest, IEquatable<EndpointIncomingRequest>
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
                m_vts = ServiceResponsePooledValueTaskSource.Create();
            }

            /// <inheritdoc/>
            public SecureChannelContext SecureChannelContext { get; }

            /// <inheritdoc/>
            public IServiceRequest Request { get; }

            /// <summary>
            /// Process an incoming request
            /// </summary>
            /// <returns></returns>
            public ValueTask<IServiceResponse> ProcessAsync(CancellationToken cancellationToken = default)
            {
                try
                {
                    m_endpoint.ServerForContext.ScheduleIncomingRequest(this, cancellationToken);
                }
                catch (Exception e)
                {
                    m_vts.SetResult(m_endpoint.CreateFault(Request, e));
                }

                return m_vts.Task;
            }

            /// <inheritdoc/>
            public async ValueTask CallAsync(CancellationToken cancellationToken = default)
            {
                using CancellationTokenSource timeoutHintCts = (int)Request.RequestHeader.TimeoutHint > 0 ?
                    new CancellationTokenSource((int)Request.RequestHeader.TimeoutHint) : null;

                using var requestLifetime = new RequestLifetime(
                    timeoutHintCts != null ?
                    [cancellationToken, timeoutHintCts.Token] :
                    [cancellationToken]);

                try
                {
                    Activity activity = null;
                    ActivitySource activitySource = m_endpoint.MessageContext.Telemetry
                        .GetActivitySource();
                    if (activitySource.HasListeners())
                    {
                        // extract trace information from the request header if available
                        if (Request.RequestHeader != null &&
                            Request.RequestHeader.AdditionalHeader
                                .TryGetValue(out AdditionalParametersType parameters) &&
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
                        m_vts.SetResult(response);
                    }
                }
                catch (Exception e)
                {
                    if (e is OperationCanceledException)
                    {
                        e = new ServiceResultException(
                            requestLifetime.StatusCode == StatusCodes.Good ? StatusCodes.BadTimeout : requestLifetime.StatusCode);
                    }
                    m_vts.SetResult(m_endpoint.CreateFault(Request, e));
                }
            }

            /// <inheritdoc/>
            public void OperationCompleted(IServiceResponse response, ServiceResult error)
            {
                if (ServiceResult.IsBad(error))
                {
                    m_vts.SetResult(m_endpoint.CreateFault(Request, new ServiceResultException(error)));
                }
                else
                {
                    m_vts.SetResult(response);
                }
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                if (obj is EndpointIncomingRequest other)
                {
                    return Request.RequestHeader.Equals(other.Request.RequestHeader);
                }
                return false;
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Request.RequestHeader.GetHashCode();
            }

            /// <inheritdoc/>
            public static bool operator ==(EndpointIncomingRequest left, EndpointIncomingRequest right)
            {
                return left.Equals(right);
            }

            /// <inheritdoc/>
            public static bool operator !=(EndpointIncomingRequest left, EndpointIncomingRequest right)
            {
                return !(left == right);
            }

            /// <inheritdoc/>
            public bool Equals(EndpointIncomingRequest other)
            {
                return Request.RequestHeader.Equals(other.Request.RequestHeader);
            }

            private readonly EndpointBase m_endpoint;
            private readonly ServiceResponsePooledValueTaskSource m_vts;
        }
    }
}
