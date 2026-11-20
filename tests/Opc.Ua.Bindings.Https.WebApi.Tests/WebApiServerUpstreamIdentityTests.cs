/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// Regression tests for the WebApi upstream-identity plumbing.
    /// <see cref="WebApiInvocationContext.Identity"/> is computed by
    /// the dispatcher and must be forwarded to
    /// <see cref="SecureChannelContext.UpstreamIdentity"/> by
    /// <see cref="WebApiServer.InvokeAsync"/>; dropping it would
    /// strip the upstream-authenticated principal before it reaches
    /// the OPC UA service pipeline (CWE-285).
    /// </summary>
    [TestFixture]
    [Category("WebApiIdentityPlumbing")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class WebApiServerUpstreamIdentityTests
    {
        [Test]
        public async Task InvokeAsyncPublishesUpstreamIdentityOnSecureChannelContextAsync()
        {
            var messageContext = ServiceMessageContext.CreateEmpty(new TelemetryStub());
            var server = new WebApiServer(messageContext, "TestListener.0");
            var capturer = new CapturingListenerCallback();
            server.Attach(capturer);
            server.UpdateDefaultEndpoint(new EndpointDescription
            {
                EndpointUrl = "https://localhost/",
                SecurityMode = MessageSecurityMode.None
            });

            var identity = new UserIdentity("alice", "secret"u8);
            var invocation = new WebApiInvocationContext
            {
                SecureChannelId = "req-1",
                Endpoint = null,
                Identity = identity
            };

            await server.InvokeAsync(
                new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 1 } },
                invocation,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(capturer.LastChannelContext, Is.Not.Null,
                "The dispatcher must forward the request to the registered callback.");
            Assert.That(capturer.LastChannelContext!.UpstreamIdentity, Is.SameAs(identity),
                "WebApiServer.InvokeAsync must publish the WebApiInvocationContext.Identity " +
                "on SecureChannelContext.UpstreamIdentity so the OPC UA service pipeline " +
                "can consume the upstream-authenticated principal.");
        }

        [Test]
        public async Task InvokeAsyncSetsUpstreamIdentityNullWhenInvocationHasNoIdentityAsync()
        {
            var messageContext = ServiceMessageContext.CreateEmpty(new TelemetryStub());
            var server = new WebApiServer(messageContext, "TestListener.0");
            var capturer = new CapturingListenerCallback();
            server.Attach(capturer);
            server.UpdateDefaultEndpoint(new EndpointDescription
            {
                EndpointUrl = "https://localhost/",
                SecurityMode = MessageSecurityMode.None
            });

            var invocation = new WebApiInvocationContext
            {
                SecureChannelId = "req-2",
                Endpoint = null,
                Identity = null
            };

            await server.InvokeAsync(
                new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 2 } },
                invocation,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(capturer.LastChannelContext!.UpstreamIdentity, Is.Null,
                "Anonymous invocations (no upstream identity) must surface as " +
                "UpstreamIdentity == null on the SecureChannelContext.");
        }

        private sealed class CapturingListenerCallback : ITransportListenerCallback
        {
            public SecureChannelContext? LastChannelContext { get; private set; }

            public ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext channelContext,
                IServiceRequest request,
                CancellationToken ct)
            {
                LastChannelContext = channelContext;
                var header = new ResponseHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = request.RequestHeader?.RequestHandle ?? 0,
                    ServiceResult = StatusCodes.Good,
                    StringTable = new ArrayOf<string>(),
                    AdditionalHeader = new ExtensionObject()
                };
                IServiceResponse response = new ReadResponse { ResponseHeader = header };
                return new ValueTask<IServiceResponse>(response);
            }

            public bool TryGetSecureChannelIdForAuthenticationToken(NodeId authenticationToken, out uint channelId)
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
