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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Integrates experimental operation execution with normal validation, handlers and Server ownership.
    /// </summary>
    public partial class StandardServer
    {
        /// <summary>
        /// Gets or sets the default-off experimental feature options before startup.
        /// </summary>
        public AsyncServiceExecutionOptions AsyncServices { get; set; } = new();

        /// <summary>
        /// Gets or sets the injectable factory used when experimental execution is enabled.
        /// </summary>
        public IAsyncServiceExecutionProviderFactory AsyncServiceExecutionProviderFactory { get; set; } =
            DefaultAsyncServiceExecutionProviderFactory.Instance;

        /// <inheritdoc/>
        public override async ValueTask<SubmitServiceResponse> SubmitServiceAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            Uuid clientOperationId,
            ExtensionObject request,
            double executionAllowance,
            double resultRetentionTime,
            double waitTime,
            RequestLifetime requestLifetime)
        {
            IAsyncServiceExecutionProvider provider = RequireAsyncServices();
            using OperationContext context = await ValidateRequestAsync(
                secureChannelContext, requestHeader, RequestType.SubmitService, requestLifetime).ConfigureAwait(false);
            try
            {
                return await provider.SubmitAsync(context, new SubmitServiceRequest
                {
                    RequestHeader = requestHeader,
                    ClientOperationId = clientOperationId,
                    Request = request,
                    ExecutionAllowance = executionAllowance,
                    ResultRetentionTime = resultRetentionTime,
                    WaitTime = waitTime
                }, requestLifetime.CancellationToken).ConfigureAwait(false);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <inheritdoc/>
        public override async ValueTask<GetServiceResultResponse> GetServiceResultAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            Uuid clientOperationId,
            ByteString serverToken,
            double waitTime,
            RequestLifetime requestLifetime)
        {
            IAsyncServiceExecutionProvider provider = RequireAsyncServices();
            using OperationContext context = await ValidateRequestAsync(
                secureChannelContext, requestHeader, RequestType.GetServiceResult, requestLifetime)
                .ConfigureAwait(false);
            try
            {
                return await provider.GetAsync(context, new GetServiceResultRequest
                {
                    RequestHeader = requestHeader,
                    ClientOperationId = clientOperationId,
                    ServerToken = serverToken,
                    WaitTime = waitTime
                }, requestLifetime.CancellationToken).ConfigureAwait(false);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <inheritdoc/>
        public override async ValueTask<ControlServiceExecutionResponse> ControlServiceExecutionAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ByteString serverToken,
            ServiceOperationControlAction action,
            RequestLifetime requestLifetime)
        {
            IAsyncServiceExecutionProvider provider = RequireAsyncServices();
            using OperationContext context = await ValidateRequestAsync(
                secureChannelContext, requestHeader, RequestType.ControlServiceExecution, requestLifetime)
                .ConfigureAwait(false);
            try
            {
                return await provider.ControlAsync(context, new ControlServiceExecutionRequest
                {
                    RequestHeader = requestHeader,
                    ServerToken = serverToken,
                    Action = action
                }, requestLifetime.CancellationToken).ConfigureAwait(false);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Runs the ordinary handler with a separately tracked admission identity and cancellation lifetime.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask<IServiceResponse> ExecuteOwnedServiceAsync(
            OperationContext context, IServiceRequest request)
        {
            RequestManager requests = CurrentInstance.RequestManager;
            using IDisposable dispatch = requests.EnterServiceDispatchScope();
            using (requests.EnterValidationScope())
            {
                context.AttachRequestScope(requests.EnterRequestScope(context));
                if (SessionTermination.IsClosingOrClaimed(context.Session))
                {
                    throw new ServiceResultException(StatusCodes.BadSessionClosed);
                }
            }
            await OnRequestValidatedAsync(context).ConfigureAwait(false);
            return request switch
            {
                ReadRequest read => await ReadServiceAsync(
                    context, read.RequestHeader, read.MaxAge, read.TimestampsToReturn, read.NodesToRead)
                    .ConfigureAwait(false),
                WriteRequest write => await WriteServiceAsync(context, write.RequestHeader, write.NodesToWrite)
                    .ConfigureAwait(false),
                CallRequest call => await CallServiceAsync(context, call.RequestHeader, call.MethodsToCall)
                    .ConfigureAwait(false),
                _ => throw new ServiceResultException(StatusCodes.BadServiceUnsupported)
            };
        }

        /// <summary>
        /// Revalidates the current Session and identity after a wait without registering another exchange handle.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal ValueTask<OperationContext> RevalidateAsyncServiceContextAsync(
            OperationContext context, RequestHeader header)
        {
            if (CurrentInstance.SessionManager is not SessionManager manager)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            return manager.RevalidateRetainedResultContextAsync(context, header);
        }

        /// <summary>
        /// Installs one provider only after the ordinary Server Session and resource infrastructure is ready.
        /// </summary>
        private async ValueTask StartAsyncServicesAsync(CancellationToken ct)
        {
            if (!AsyncServices.Enabled)
            {
                return;
            }
            AsyncServiceExecutionOptions options = AsyncServices.Snapshot();
            IAsyncServiceExecutionProvider provider = AsyncServiceExecutionProviderFactory.Create(
                this, Configuration!, options, TimeProvider);
            try
            {
                if (CurrentInstance.DiagnosticsNodeManager is not DiagnosticsNodeManager diagnostics)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "The installed diagnostics manager cannot install the optional model.");
                }
                await diagnostics.InstallAsyncServiceModelAsync(
                    options, provider, Configuration!.ApplicationUri!, ct).ConfigureAwait(false);
            }
            catch
            {
                await provider.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            m_asyncServiceProvider = provider;
            m_previousAsyncParkingPolicy = RequestParkingPolicy;
            m_asyncParkingPolicy = new DelegateRequestParkingPolicy(request =>
                m_asyncServiceProvider?.CanPark(request) == true ||
                m_previousAsyncParkingPolicy?.CanPark(request) == true);
            RequestParkingPolicy = m_asyncParkingPolicy;
        }

        /// <summary>
        /// Stops admission and drains independently owned work before retiring the normal handlers.
        /// </summary>
        private async ValueTask StopAsyncServicesAsync()
        {
            IAsyncServiceExecutionProvider? provider = Interlocked.Exchange(ref m_asyncServiceProvider, null);
            if (ReferenceEquals(RequestParkingPolicy, m_asyncParkingPolicy))
            {
                RequestParkingPolicy = m_previousAsyncParkingPolicy;
            }
            m_asyncParkingPolicy = null;
            m_previousAsyncParkingPolicy = null;
            if (provider != null)
            {
                await provider.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Rejects disabled or stopped experimental Services without invoking an original handler.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private IAsyncServiceExecutionProvider RequireAsyncServices()
        {
            return Volatile.Read(ref m_asyncServiceProvider) ??
                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
        }

        // TODO: Teach disposal analysis about the Interlocked.Exchange ownership transfer in StopAsyncServicesAsync.
        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
            Justification = "StopAsyncServicesAsync owns and awaits provider disposal during Server shutdown. " +
                "TODO: make ownership analysis follow the asynchronous shutdown helper.")]
        private IAsyncServiceExecutionProvider? m_asyncServiceProvider;

        private IRequestParkingPolicy? m_previousAsyncParkingPolicy;
        private IRequestParkingPolicy? m_asyncParkingPolicy;
    }
}
