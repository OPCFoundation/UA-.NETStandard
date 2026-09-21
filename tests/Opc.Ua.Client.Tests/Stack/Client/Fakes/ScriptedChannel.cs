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
 *
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
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.Bindings;

namespace Opc.Ua.Client.Tests.Stack.Client.Fakes
{
    internal sealed class ScriptedChannel
    {
        public ScriptedChannel(IServiceMessageContext messageContext, TimeProvider timeProvider)
        {
            m_timeProvider = timeProvider;
            Mock = new Mock<ITransportChannel>();
            Mock.As<ISecureChannel>().Setup(c => c.CurrentToken).Returns((ChannelToken?)null);
            Mock.Setup(c => c.SupportedFeatures).Returns(() => SupportedFeatures);
            Mock.Setup(c => c.EndpointDescription).Returns(() => m_description);
            Mock.Setup(c => c.EndpointConfiguration).Returns(() => m_endpointConfiguration);
            Mock.Setup(c => c.MessageContext).Returns(messageContext);
            Mock.Setup(c => c.ChannelThumbprint).Returns([]);
            Mock.Setup(c => c.ClientChannelCertificate).Returns([]);
            Mock.Setup(c => c.ServerChannelCertificate).Returns([]);
            Mock.Setup(c => c.OperationTimeout).Returns(() => m_operationTimeout);
            Mock.SetupSet(c => c.OperationTimeout = It.IsAny<int>())
                .Callback<int>(value => m_operationTimeout = value);
            Mock.As<ISecureChannel>().Setup(c => c.OpenAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<TransportChannelSettings>(),
                    It.IsAny<CancellationToken>()))
                .Returns<Uri, TransportChannelSettings, CancellationToken>(OpenAsync);
            Mock.As<ISecureChannel>().Setup(c => c.OpenAsync(
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<TransportChannelSettings>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ITransportWaitingConnection, TransportChannelSettings, CancellationToken>(OpenReverseAsync);
            Mock.Setup(c => c.ReconnectAsync(
                    It.IsAny<ITransportWaitingConnection?>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ITransportWaitingConnection?, CancellationToken>(ReconnectAsync);
            Mock.Setup(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<IServiceRequest, CancellationToken>(SendRequestAsync);
            Mock.Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns<CancellationToken>(CloseAsync);
            Mock.Setup(c => c.Dispose()).Callback(() => Interlocked.Increment(ref m_disposeCount));
        }

        public Mock<ITransportChannel> Mock { get; }

        public ITransportChannel Channel => Mock.Object;

        public TransportChannelFeatures SupportedFeatures { get; set; }

        public Func<CancellationToken, ValueTask>? OpenHandler { get; set; }

        public Func<CancellationToken, ValueTask>? ReconnectHandler { get; set; }

        public Func<IServiceRequest, CancellationToken, ValueTask<IServiceResponse>>? RequestHandler { get; set; }

        public int CloseCount => Volatile.Read(ref m_closeCount);

        public int DisposeCount => Volatile.Read(ref m_disposeCount);

        public int ReconnectCount => Volatile.Read(ref m_reconnectCount);

        public int SendRequestCount => Volatile.Read(ref m_sendRequestCount);

        public int CreateSessionCount => Volatile.Read(ref m_sessionCounter);

        public string? OpenedEndpointUrl { get; private set; }

        public async ValueTask<RequestOperation> NextOperationAsync<TRequest>(CancellationToken ct)
            where TRequest : class, IServiceRequest
        {
            while (true)
            {
                RequestOperation operation = await m_operations.Reader.ReadAsync(ct).ConfigureAwait(false);
                if (operation.Request is TRequest)
                {
                    return operation;
                }
            }
        }

        public void OpenForEndpoint(ConfiguredEndpoint endpoint)
        {
            m_description = endpoint.Description;
            m_endpointConfiguration = endpoint.Configuration!;
            OpenedEndpointUrl = endpoint.Description.EndpointUrl;
        }

        public IServiceResponse CreateResponse(IServiceRequest request)
        {
            return request switch
            {
                CreateSessionRequest => CreateSessionResponse(),
                ActivateSessionRequest => new ActivateSessionResponse
                {
                    ResponseHeader = CreateGoodHeader(),
                    ServerNonce = ByteString.Empty,
                    Results = [],
                    DiagnosticInfos = []
                },
                ReadRequest readRequest => new ReadResponse
                {
                    ResponseHeader = CreateGoodHeader(),
                    Results = CreateReadResults(readRequest),
                    DiagnosticInfos = []
                },
                CloseSessionRequest => new CloseSessionResponse { ResponseHeader = CreateGoodHeader() },
                _ => throw ServiceResultException.Create(
                    StatusCodes.BadServiceUnsupported,
                    "Unexpected request type {0}.",
                    request.GetType().Name)
            };
        }

        public ResponseHeader CreateGoodHeader()
        {
            return new ResponseHeader
            {
                ServiceResult = StatusCodes.Good,
                Timestamp = m_timeProvider.GetUtcNow().UtcDateTime
            };
        }

        private async ValueTask OpenAsync(Uri uri, TransportChannelSettings settings, CancellationToken ct)
        {
            _ = uri;
            if (OpenHandler != null)
            {
                await OpenHandler(ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            OpenWithSettings(settings);
        }

        private ValueTask OpenReverseAsync(
            ITransportWaitingConnection connection,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            _ = connection;
            return OpenAsync(new Uri(settings.Description!.EndpointUrl!), settings, ct);
        }

        private ValueTask ReconnectAsync(ITransportWaitingConnection? connection, CancellationToken ct)
        {
            _ = connection;
            Interlocked.Increment(ref m_reconnectCount);
            ct.ThrowIfCancellationRequested();
            return ReconnectHandler?.Invoke(ct) ?? default;
        }

        private ValueTask<IServiceResponse> SendRequestAsync(IServiceRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref m_sendRequestCount);
            ct.ThrowIfCancellationRequested();
            Task<IServiceResponse> operation = RequestHandler?.Invoke(request, ct).AsTask() ??
                Task.FromResult(CreateResponse(request));
            m_operations.Writer.TryWrite(new RequestOperation(request, operation, ct));
            return new ValueTask<IServiceResponse>(operation);
        }

        private ValueTask CloseAsync(CancellationToken ct)
        {
            _ = ct;
            Interlocked.Increment(ref m_closeCount);
            return default;
        }

        private void OpenWithSettings(TransportChannelSettings settings)
        {
            m_description = settings.Description
                ?? throw new InvalidOperationException("Transport settings do not include an endpoint.");
            m_endpointConfiguration = settings.Configuration
                ?? throw new InvalidOperationException("Transport settings do not include endpoint configuration.");
            OpenedEndpointUrl = m_description.EndpointUrl;
        }

        private CreateSessionResponse CreateSessionResponse()
        {
            string suffix = Interlocked.Increment(ref m_sessionCounter).ToString(CultureInfo.InvariantCulture);
            return new CreateSessionResponse
            {
                ResponseHeader = CreateGoodHeader(),
                SessionId = new NodeId($"session-{suffix}", 1),
                AuthenticationToken = new NodeId($"token-{suffix}", 1),
                RevisedSessionTimeout = 60000,
                ServerNonce = ByteString.Empty,
                ServerCertificate = ByteString.Empty,
                ServerSignature = new SignatureData(),
                ServerEndpoints = [m_description],
                MaxRequestMessageSize = 1_048_576
            };
        }

        private static ArrayOf<DataValue> CreateReadResults(ReadRequest request)
        {
            int count = request.NodesToRead.Count;
            if (count == 1 && Equals(request.NodesToRead[0].NodeId, VariableIds.Server_ServerStatus_State))
            {
                return [new DataValue(new Variant((int)ServerState.Running), StatusCodes.Good)];
            }

            if (count == 2)
            {
                return
                [
                    new DataValue(new Variant(ArrayOf.Wrapped(Namespaces.OpcUa)), StatusCodes.Good),
                    new DataValue(new Variant(ArrayOf.Wrapped("urn:localhost:server")), StatusCodes.Good)
                ];
            }

            var values = new DataValue[count];
            for (int index = 0; index < count; index++)
            {
                Variant value = count > 1 && index is 12 or 13 or 14
                    ? new Variant((ushort)0)
                    : count > 1 && index == 18 ? new Variant(0d) : new Variant(0u);
                values[index] = new DataValue(value, StatusCodes.Good);
            }
            return new ArrayOf<DataValue>(values);
        }

        internal sealed record RequestOperation(
            IServiceRequest Request,
            Task<IServiceResponse> Operation,
            CancellationToken CancellationToken);

        private readonly TimeProvider m_timeProvider;
        private readonly Channel<RequestOperation> m_operations = System.Threading.Channels.Channel
            .CreateUnbounded<RequestOperation>();
        private EndpointDescription m_description = new();
        private EndpointConfiguration m_endpointConfiguration = new();
        private int m_operationTimeout;
        private int m_sessionCounter;
        private int m_closeCount;
        private int m_disposeCount;
        private int m_reconnectCount;
        private int m_sendRequestCount;
    }
}
