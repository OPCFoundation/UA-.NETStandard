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

#if NET8_0_OR_GREATER

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Opc.Ua.Bindings.Https.WebApi.Tests.WebApi
{
    /// <summary>
    /// Unit tests for <see cref="WebApiServer"/>: constructor guards,
    /// property accessors (<see cref="WebApiServer.MessageContext"/>,
    /// <see cref="WebApiServer.IsReady"/>, <see cref="WebApiServer.ListenerId"/>),
    /// lifecycle methods (<see cref="WebApiServer.Attach"/>,
    /// <see cref="WebApiServer.UpdateMessageContext"/>,
    /// <see cref="WebApiServer.UpdateListenerId"/>,
    /// <see cref="WebApiServer.UpdateDefaultEndpoint"/>),
    /// and <see cref="WebApiServer.InvokeAsync"/> dispatch paths
    /// (null callback → BadServerHalted; callback success; callback throws
    /// <see cref="ServiceResultException"/> → <see cref="ServiceFault"/>).
    /// </summary>
    [TestFixture]
    [Category("WebApiServer")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class WebApiServerTests
    {
        private IServiceMessageContext m_messageContext = null!;

        [SetUp]
        public void SetUp()
        {
            m_messageContext = ServiceMessageContext.CreateEmpty(new TelemetryStub());
        }

        // ─────────────────────────── Constructor guards ─────────────────────

        [Test]
        public void ConstructorThrowsForNullMessageContext()
        {
            Assert.That(
                () => new WebApiServer(null!, "listener-0"),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("messageContext"));
        }

        [Test]
        public void ConstructorThrowsForNullListenerId()
        {
            Assert.That(
                () => new WebApiServer(m_messageContext, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("listenerId"));
        }

        // ─────────────────────────── Property accessors ─────────────────────

        [Test]
        public void MessageContextReturnsInitialValue()
        {
            var server = new WebApiServer(m_messageContext, "id-1");

            Assert.That(server.MessageContext, Is.SameAs(m_messageContext));
        }

        [Test]
        public void IsReadyReturnsFalseBeforeAttach()
        {
            var server = new WebApiServer(m_messageContext, "id-1");

            Assert.That(server.IsReady, Is.False);
        }

        [Test]
        public void IsReadyReturnsTrueAfterAttach()
        {
            var server = new WebApiServer(m_messageContext, "id-1");
            server.Attach(new NoOpCallback());

            Assert.That(server.IsReady, Is.True);
        }

        [Test]
        public void IsReadyReturnsFalseAfterDetach()
        {
            var server = new WebApiServer(m_messageContext, "id-1");
            server.Attach(new NoOpCallback());
            server.Attach(null);  // detach

            Assert.That(server.IsReady, Is.False);
        }

        [Test]
        public void ListenerIdReturnsInitialValue()
        {
            var server = new WebApiServer(m_messageContext, "listener-abc");

            Assert.That(server.ListenerId, Is.EqualTo("listener-abc"));
        }

        // ─────────────────────────── Update methods ─────────────────────────

        [Test]
        public void UpdateMessageContextThrowsForNull()
        {
            var server = new WebApiServer(m_messageContext, "id");

            Assert.That(
                () => server.UpdateMessageContext(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("messageContext"));
        }

        [Test]
        public void UpdateMessageContextReplacesContext()
        {
            var server = new WebApiServer(m_messageContext, "id");
            IServiceMessageContext updated = ServiceMessageContext.CreateEmpty(new TelemetryStub());

            server.UpdateMessageContext(updated);

            Assert.That(server.MessageContext, Is.SameAs(updated));
        }

        [Test]
        public void UpdateListenerIdThrowsForNull()
        {
            var server = new WebApiServer(m_messageContext, "id");

            Assert.That(
                () => server.UpdateListenerId(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("listenerId"));
        }

        [Test]
        public void UpdateListenerIdReplacesId()
        {
            var server = new WebApiServer(m_messageContext, "old-id");

            server.UpdateListenerId("new-id");

            Assert.That(server.ListenerId, Is.EqualTo("new-id"));
        }

        [Test]
        public void UpdateDefaultEndpointAcceptsNull()
        {
            var server = new WebApiServer(m_messageContext, "id");

            Assert.DoesNotThrow(() => server.UpdateDefaultEndpoint(null));
        }

        [Test]
        public void UpdateDefaultEndpointAcceptsValidEndpoint()
        {
            var server = new WebApiServer(m_messageContext, "id");
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "https://localhost/",
                SecurityMode = MessageSecurityMode.None
            };

            Assert.DoesNotThrow(() => server.UpdateDefaultEndpoint(endpoint));
        }

        // ────────────────────────── InvokeAsync paths ───────────────────────

        [Test]
        public void InvokeAsyncThrowsForNullRequest()
        {
            var server = new WebApiServer(m_messageContext, "id");
            var ctx = new WebApiInvocationContext { SecureChannelId = "x" };

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await server.InvokeAsync(null!, ctx, CancellationToken.None).ConfigureAwait(false));
        }

        [Test]
        public void InvokeAsyncThrowsForNullContext()
        {
            var server = new WebApiServer(m_messageContext, "id");
            var request = new ReadRequest { RequestHeader = new RequestHeader() };

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await server.InvokeAsync(request, null!, CancellationToken.None).ConfigureAwait(false));
        }

        [Test]
        public async Task InvokeAsyncReturnsBadServerHaltedWhenNoCallbackAsync()
        {
            var server = new WebApiServer(m_messageContext, "id");
            var request = new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 11 } };
            var ctx = new WebApiInvocationContext { SecureChannelId = "x" };

            IServiceResponse response = await server
                .InvokeAsync(request, ctx, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<ServiceFault>());
            Assert.That(response.ResponseHeader.ServiceResult,
                Is.EqualTo(StatusCodes.BadServerHalted));
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(11u));
        }

        [Test]
        public async Task InvokeAsyncForwardedToCallbackOnSuccessAsync()
        {
            var server = new WebApiServer(m_messageContext, "id");
            var callback = new NoOpCallback();
            server.Attach(callback);

            var request = new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 22 } };
            var ctx = new WebApiInvocationContext { SecureChannelId = "x" };

            IServiceResponse response = await server
                .InvokeAsync(request, ctx, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<ReadResponse>());
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(22u));
            Assert.That(callback.Invocations, Is.EqualTo(1));
        }

        [Test]
        public async Task InvokeAsyncReturnsServiceFaultWhenCallbackThrowsServiceResultExceptionAsync()
        {
            var server = new WebApiServer(m_messageContext, "id");
            server.Attach(new ThrowingCallback(StatusCodes.BadUserAccessDenied));

            var request = new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 33 } };
            var ctx = new WebApiInvocationContext { SecureChannelId = "x" };

            IServiceResponse response = await server
                .InvokeAsync(request, ctx, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<ServiceFault>());
            Assert.That(response.ResponseHeader.ServiceResult,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(33u));
        }

        [Test]
        public async Task InvokeAsyncUsesDefaultEndpointWhenContextEndpointIsNullAsync()
        {
            var server = new WebApiServer(m_messageContext, "id");
            var callback = new CapturingCallback();
            server.Attach(callback);

            var defaultEndpoint = new EndpointDescription
            {
                EndpointUrl = "https://localhost/default",
                SecurityMode = MessageSecurityMode.None
            };
            server.UpdateDefaultEndpoint(defaultEndpoint);

            var request = new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 44 } };
            var ctx = new WebApiInvocationContext { SecureChannelId = "y", Endpoint = null };

            await server.InvokeAsync(request, ctx, CancellationToken.None).ConfigureAwait(false);

            Assert.That(callback.CapturedContext?.EndpointDescription,
                Is.SameAs(defaultEndpoint),
                "When context.Endpoint is null the default endpoint must be used.");
        }

        [Test]
        public async Task InvokeAsyncUsesContextEndpointOverDefaultAsync()
        {
            var server = new WebApiServer(m_messageContext, "id");
            var callback = new CapturingCallback();
            server.Attach(callback);

            var defaultEndpoint = new EndpointDescription { EndpointUrl = "https://localhost/default" };
            server.UpdateDefaultEndpoint(defaultEndpoint);

            var contextEndpoint = new EndpointDescription { EndpointUrl = "https://localhost/context" };

            var request = new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 55 } };
            var ctx = new WebApiInvocationContext { SecureChannelId = "z", Endpoint = contextEndpoint };

            await server.InvokeAsync(request, ctx, CancellationToken.None).ConfigureAwait(false);

            Assert.That(callback.CapturedContext?.EndpointDescription,
                Is.SameAs(contextEndpoint),
                "When context.Endpoint is set it must override the default endpoint.");
        }

        // ─────────────────────────── Inner stubs ────────────────────────────

        private sealed class NoOpCallback : ITransportListenerCallback
        {
            public int Invocations { get; private set; }

            public ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext channelContext,
                IServiceRequest request,
                CancellationToken ct)
            {
                Invocations++;
                var header = new ResponseHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = request.RequestHeader?.RequestHandle ?? 0,
                    ServiceResult = StatusCodes.Good,
                    StringTable = new ArrayOf<string>(),
                    AdditionalHeader = new ExtensionObject()
                };
                return new ValueTask<IServiceResponse>(
                    new ReadResponse { ResponseHeader = header });
            }

            public bool TryGetSecureChannelIdForAuthenticationToken(
                NodeId authenticationToken,
                out uint channelId)
            {
                channelId = 0;
                return false;
            }

            public void ReportAuditOpenSecureChannelEvent(
                string globalChannelId,
                EndpointDescription endpointDescription,
                OpenSecureChannelRequest request,
                Security.Certificates.Certificate clientCertificate,
                Exception exception)
            {
            }

            public void ReportAuditCloseSecureChannelEvent(string globalChannelId, Exception exception)
            {
            }

            public void ReportAuditCertificateEvent(
                Security.Certificates.Certificate clientCertificate,
                Exception exception)
            {
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance",
            "CA1812:Avoid uninstantiated internal classes",
            Justification = "Instantiated inside InvokeAsyncReturnsServiceFaultWhenCallbackThrowsServiceResultExceptionAsync.")]
        private sealed class ThrowingCallback : ITransportListenerCallback
        {
            private readonly StatusCode m_statusCode;

            public ThrowingCallback(StatusCode statusCode)
            {
                m_statusCode = statusCode;
            }

            public ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext channelContext,
                IServiceRequest request,
                CancellationToken ct)
            {
                throw new ServiceResultException((uint)m_statusCode);
            }

            public bool TryGetSecureChannelIdForAuthenticationToken(
                NodeId authenticationToken,
                out uint channelId)
            {
                channelId = 0;
                return false;
            }

            public void ReportAuditOpenSecureChannelEvent(
                string globalChannelId,
                EndpointDescription endpointDescription,
                OpenSecureChannelRequest request,
                Security.Certificates.Certificate clientCertificate,
                Exception exception)
            {
            }

            public void ReportAuditCloseSecureChannelEvent(string globalChannelId, Exception exception)
            {
            }

            public void ReportAuditCertificateEvent(
                Security.Certificates.Certificate clientCertificate,
                Exception exception)
            {
            }
        }

        private sealed class CapturingCallback : ITransportListenerCallback
        {
            public SecureChannelContext? CapturedContext { get; private set; }

            public ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext channelContext,
                IServiceRequest request,
                CancellationToken ct)
            {
                CapturedContext = channelContext;
                var header = new ResponseHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = request.RequestHeader?.RequestHandle ?? 0,
                    ServiceResult = StatusCodes.Good,
                    StringTable = new ArrayOf<string>(),
                    AdditionalHeader = new ExtensionObject()
                };
                return new ValueTask<IServiceResponse>(
                    new ReadResponse { ResponseHeader = header });
            }

            public bool TryGetSecureChannelIdForAuthenticationToken(
                NodeId authenticationToken,
                out uint channelId)
            {
                channelId = 0;
                return false;
            }

            public void ReportAuditOpenSecureChannelEvent(
                string globalChannelId,
                EndpointDescription endpointDescription,
                OpenSecureChannelRequest request,
                Security.Certificates.Certificate clientCertificate,
                Exception exception)
            {
            }

            public void ReportAuditCloseSecureChannelEvent(string globalChannelId, Exception exception)
            {
            }

            public void ReportAuditCertificateEvent(
                Security.Certificates.Certificate clientCertificate,
                Exception exception)
            {
            }
        }

        private sealed class TelemetryStub : TelemetryContextBase
        {
            public TelemetryStub()
                : base(NullLoggerFactory.Instance)
            {
            }
        }
    }
}

#endif
