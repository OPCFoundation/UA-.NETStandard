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
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;
#if NET8_0_OR_GREATER
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Opc.Ua.Security.Certificates;
#endif

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Tests for the <see cref="HttpsTransportListener"/> class.
    /// </summary>
    [TestFixture]
    [Category("HttpsTransportListenerTests")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class HttpsTransportListenerTests
    {
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
        }

        [SetUp]
        protected void SetUp()
        {
        }

        [TearDown]
        protected void TearDown()
        {
        }

        /// <summary>
        /// Verify constructor with https scheme creates a valid instance.
        /// </summary>
        [Test]
        public async Task ConstructorWithHttpsSchemeCreatesInstanceAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener, Is.Not.Null);
        }

        /// <summary>
        /// Verify constructor with opc.https scheme creates a valid instance.
        /// </summary>
        [Test]
        public async Task ConstructorWithOpcHttpsSchemeCreatesInstanceAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeOpcHttps, m_telemetry);
            Assert.That(listener, Is.Not.Null);
        }

        /// <summary>
        /// Verify UriScheme property returns the scheme passed to the constructor.
        /// </summary>
        [Test]
        public async Task UriSchemeReturnsHttpsWhenConstructedWithHttpsAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener.UriScheme, Is.EqualTo("https"));
        }

        /// <summary>
        /// Verify UriScheme property returns opc.https when constructed with that scheme.
        /// </summary>
        [Test]
        public async Task UriSchemeReturnsOpcHttpsWhenConstructedWithOpcHttpsAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeOpcHttps, m_telemetry);
            Assert.That(listener.UriScheme, Is.EqualTo("opc.https"));
        }

        /// <summary>
        /// Verify ListenerId is null before Open is called.
        /// </summary>
        [Test]
        public async Task ListenerIdIsNullBeforeOpenAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener.ListenerId, Is.Null);
        }

        /// <summary>
        /// Verify EndpointUrl is null before Open is called.
        /// </summary>
        [Test]
        public async Task EndpointUrlIsNullBeforeOpenAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener.EndpointUrl, Is.Null);
        }

        /// <summary>
        /// Verify Close on an unopened listener does not throw.
        /// </summary>
        [Test]
        public async Task CloseOnUnopenedListenerDoesNotThrowAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(async () => await listener.CloseAsync().ConfigureAwait(false), Throws.Nothing);
        }

        /// <summary>
        /// Verify Dispose on an unopened listener does not throw.
        /// </summary>
        [Test]
        public async Task DisposeOnUnopenedListenerDoesNotThrowAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(async () => await listener.DisposeAsync().ConfigureAwait(false), Throws.Nothing);
        }

        /// <summary>
        /// Verify calling Dispose twice does not throw.
        /// </summary>
        [Test]
        public async Task DoubleDisposeDoesNotThrowAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            await listener.DisposeAsync().ConfigureAwait(false);
            Assert.That(async () => await listener.DisposeAsync().ConfigureAwait(false), Throws.Nothing);
        }

        /// <summary>
        /// Verify Close followed by Dispose does not throw.
        /// </summary>
        [Test]
        public async Task CloseFollowedByDisposeDoesNotThrowAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            await listener.CloseAsync().ConfigureAwait(false);
            Assert.That(async () => await listener.DisposeAsync().ConfigureAwait(false), Throws.Nothing);
        }

        /// <summary>
        /// Verify CreateReverseConnection throws NotImplementedException
        /// for the HTTPS binary variants (Part 6 §7.4.4 does not define
        /// reverse connect for HTTPS-uabinary or HTTPS-JSON).
        /// </summary>
        [Test]
        public async Task CreateReverseConnectionThrowsNotImplementedExceptionAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            var uri = new Uri("https://localhost:4840");
            Assert.Throws<NotImplementedException>(() => listener.CreateReverseConnection(uri, 30000));
        }

        /// <summary>
        /// Verify CreateReverseConnection accepts the WSS variants but
        /// fails fast with BadInvalidState until the listener is opened
        /// (m_bufferManager/m_quotas are still null).
        /// </summary>
        [Test]
        public async Task CreateReverseConnectionForWssRequiresOpenedListenerAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeOpcWss, m_telemetry);
            var uri = new Uri("opc.wss://localhost:4840/SomeClient");
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => listener.CreateReverseConnection(uri, 30000))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        /// <summary>
        /// Null URL must be rejected early with ArgumentNullException
        /// regardless of scheme.
        /// </summary>
        [Test]
        public async Task CreateReverseConnectionRejectsNullUrlAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeOpcWss, m_telemetry);
            Assert.Throws<ArgumentNullException>(
                () => listener.CreateReverseConnection(null!, 30000));
        }

        /// <summary>
        /// Verify UpdateChannelLastActiveTime does not throw on unopened listener.
        /// </summary>
        [Test]
        public async Task UpdateChannelLastActiveTimeDoesNotThrowAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.DoesNotThrow(() => listener.UpdateChannelLastActiveTime("test-channel-id"));
        }

        /// <summary>
        /// Verify UpdateChannelLastActiveTime with null does not throw.
        /// </summary>
        [Test]
        public async Task UpdateChannelLastActiveTimeWithNullDoesNotThrowAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.DoesNotThrow(() => listener.UpdateChannelLastActiveTime(null!));
        }

        /// <summary>
        /// Verify UpdateChannelLastActiveTime with empty string does not throw.
        /// </summary>
        [Test]
        public async Task UpdateChannelLastActiveTimeWithEmptyStringDoesNotThrowAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.DoesNotThrow(() => listener.UpdateChannelLastActiveTime(string.Empty));
        }

        /// <summary>
        /// Verify the listener implements ITransportListener.
        /// </summary>
        [Test]
        public async Task ListenerImplementsITransportListenerAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener, Is.InstanceOf<ITransportListener>());
        }

        /// <summary>
        /// Verify the listener implements IDisposable.
        /// </summary>
        [Test]
        public async Task ListenerImplementsIAsyncDisposableAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener, Is.InstanceOf<IAsyncDisposable>());
        }

        /// <summary>
        /// Verify HttpsTransportListenerFactory creates an instance with correct scheme.
        /// </summary>
        [Test]
        public async Task HttpsTransportListenerFactoryCreatesListenerAsync()
        {
            var factory = new HttpsTransportListenerFactory();
            await using ITransportListener listener = factory.Create(m_telemetry);
            Assert.That(listener, Is.Not.Null);
            Assert.That(listener.UriScheme, Is.EqualTo("https"));
        }

        /// <summary>
        /// Verify HttpsTransportListenerFactory UriScheme property.
        /// </summary>
        [Test]
        public async Task HttpsTransportListenerFactoryUriSchemeIsHttpsAsync()
        {
            var factory = new HttpsTransportListenerFactory();
            Assert.That(factory.UriScheme, Is.EqualTo("https"));
        }

        /// <summary>
        /// Verify OpcHttpsTransportListenerFactory creates an instance with correct scheme.
        /// </summary>
        [Test]
        public async Task OpcHttpsTransportListenerFactoryCreatesListenerAsync()
        {
            var factory = new OpcHttpsTransportListenerFactory();
            await using ITransportListener listener = factory.Create(m_telemetry);
            Assert.That(listener, Is.Not.Null);
            Assert.That(listener.UriScheme, Is.EqualTo("opc.https"));
        }

        /// <summary>
        /// Verify OpcHttpsTransportListenerFactory UriScheme property.
        /// </summary>
        [Test]
        public async Task OpcHttpsTransportListenerFactoryUriSchemeIsOpcHttpsAsync()
        {
            var factory = new OpcHttpsTransportListenerFactory();
            Assert.That(factory.UriScheme, Is.EqualTo("opc.https"));
        }

        /// <summary>
        /// Verify factory-created listener has null ListenerId before Open.
        /// </summary>
        [Test]
        public async Task FactoryCreatedListenerHasNullListenerIdAsync()
        {
            var factory = new HttpsTransportListenerFactory();
            await using ITransportListener listener = factory.Create(m_telemetry);
            Assert.That(listener.ListenerId, Is.Null);
        }

        /// <summary>
        /// Verify CreateReverseConnection with null uri throws
        /// ArgumentNullException (the null-arg guard now precedes the
        /// scheme/unsupported-scheme NotImplementedException check).
        /// </summary>
        [Test]
        public async Task CreateReverseConnectionWithNullUriThrowsArgumentNullExceptionAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.Throws<ArgumentNullException>(() => listener.CreateReverseConnection(null!, 0));
        }

        /// <summary>
        /// Verify multiple Close calls on an unopened listener do not throw.
        /// </summary>
        [Test]
        public async Task MultipleCloseCallsDoNotThrowAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            await listener.CloseAsync().ConfigureAwait(false);
            Assert.That(async () => await listener.CloseAsync().ConfigureAwait(false), Throws.Nothing);
        }

        /// <summary>
        /// Verify Open throws when ServerCertificates is null.
        /// </summary>
        [Test]
        public async Task OpenThrowsWhenCertProviderIsNullAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            var baseAddress = new Uri("https://localhost:51000");
            var callback = new Mock<ITransportListenerCallback>();
            var settings = new TransportListenerSettings
            {
                Descriptions = [],
                Configuration = EndpointConfiguration.Create(),
                ServerCertificates = null,
                CertificateValidator = new Mock<ICertificateValidatorEx>().Object,
                NamespaceUris = new NamespaceTable(),
                Factory = null
            };

            Assert.That(async () => await listener.OpenAsync(baseAddress, settings, callback.Object).ConfigureAwait(false), Throws.Exception);
        }

        /// <summary>
        /// Verify Open sets ListenerId and EndpointUrl before Start fails.
        /// </summary>
        [Test]
        public async Task OpenSetsFieldsBeforeStartFailsAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            var baseAddress = new Uri("https://localhost:51001");
            var callback = new Mock<ITransportListenerCallback>();
            var settings = new TransportListenerSettings
            {
                Descriptions = [],
                Configuration = EndpointConfiguration.Create(),
                ServerCertificates = null,
                CertificateValidator = new Mock<ICertificateValidatorEx>().Object,
                NamespaceUris = new NamespaceTable(),
                Factory = null
            };

            await Assert.CatchAsync(async () => await listener.OpenAsync(baseAddress, settings, callback.Object).ConfigureAwait(false));

            Assert.That(listener.ListenerId, Is.Not.Null.And.Not.Empty);
            Assert.That(listener.EndpointUrl, Is.EqualTo(baseAddress));
        }

        /// <summary>
        /// Verify Stop is equivalent to Dispose.
        /// </summary>
        [Test]
        public async Task StopDoesNotThrowOnUnopenedListenerAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            await listener.StopAsync().ConfigureAwait(false);
            Assert.That(async () => await listener.DisposeAsync().ConfigureAwait(false), Throws.Nothing);
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Verify SendAsync returns 501 NotImplemented when callback is null.
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task SendAsyncReturnsNotImplementedWhenCallbackIsNullAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Response.Body = new MemoryStream();

            await listener.SendAsync(context).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.NotImplemented));
        }

        /// <summary>
        /// Verify SendAsync returns BadRequest for unsupported content type.
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task SendAsyncReturnsBadRequestForWrongContentTypeAsync()
        {
            await using HttpsTransportListener listener = CreatePartiallyOpenedListener();
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = "text/xml";
            context.Response.Body = new MemoryStream();

            await listener.SendAsync(context).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.BadRequest));
        }

        /// <summary>
        /// Verify SendAsync returns BadRequest when buffer length does not match.
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task SendAsyncReturnsBadRequestForBufferLengthMismatchAsync()
        {
            await using HttpsTransportListener listener = CreatePartiallyOpenedListener();
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = "application/octet-stream";
            context.Request.ContentLength = 100;
            context.Request.Body = new MemoryStream([0x01, 0x02]);
            context.Response.Body = new MemoryStream();

            await listener.SendAsync(context).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.BadRequest));
        }

        /// <summary>
        /// Part 6 7.4.1: HTTPS applications shall support HTTP chunking. A chunked
        /// body has no Content-Length and must be read and decoded, not rejected
        /// as a length mismatch.
        /// </summary>
        [Test]
        public async Task SendAsyncAcceptsChunkedBodyWithoutContentLengthAsync()
        {
            await using HttpsTransportListener listener = CreatePartiallyOpenedListener();
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = "application/octet-stream";
            context.Request.Headers["Transfer-Encoding"] = "chunked";
            context.Request.Body = new MemoryStream([0x01, 0x02, 0x03, 0x04]);
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await listener.SendAsync(context).ConfigureAwait(false);

            // the body was read and decoded: the undecodable payload gets a ServiceFault.
            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.OK));
            ServiceFault fault = BinaryDecoder.DecodeMessage<ServiceFault>(
                responseBody.ToArray(),
                ServiceMessageContext.Create(m_telemetry));
            Assert.That(StatusCode.IsBad(fault.ResponseHeader.ServiceResult), Is.True);
        }

        /// <summary>
        /// A chunked body is bounded by MaxMessageSize while it is read.
        /// </summary>
        [Test]
        public async Task SendAsyncRejectsChunkedBodyAboveMaxMessageSizeAsync()
        {
            await using HttpsTransportListener listener = CreatePartiallyOpenedListener(maxMessageSize: 1024);
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = "application/octet-stream";
            context.Request.Headers["Transfer-Encoding"] = "chunked";
            context.Request.Body = new MemoryStream(new byte[4096]);
            context.Response.Body = new MemoryStream();

            await listener.SendAsync(context).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.RequestEntityTooLarge));
        }

        /// <summary>
        /// Verify SendAsync answers a binary payload that cannot be decoded with a
        /// ServiceFault (like the JSON path) instead of an HTTP error.
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task SendAsyncRespondsWithServiceFaultForInvalidBodyAsync()
        {
            await using HttpsTransportListener listener = CreatePartiallyOpenedListener();
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = "application/octet-stream";
            context.Request.ContentLength = 4;
            context.Request.Body = new MemoryStream([0x01, 0x02, 0x03, 0x04]);
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await listener.SendAsync(context).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.OK));
            ServiceFault fault = BinaryDecoder.DecodeMessage<ServiceFault>(
                responseBody.ToArray(),
                ServiceMessageContext.Create(m_telemetry));
            Assert.That(StatusCode.IsBad(fault.ResponseHeader.ServiceResult), Is.True);
            Assert.That(fault.ResponseHeader.RequestHandle, Is.Zero);
        }

        /// <summary>
        /// A binary request the decoder rejects for a string above MaxStringLength
        /// is answered with a ServiceFault that echoes its RequestHandle
        /// (OPC 10000-4 §7.33).
        /// </summary>
        [Test]
        public async Task SendAsyncServiceFaultEchoesRequestHandleOfUndecodableRequestAsync()
        {
            await using HttpsTransportListener listener = CreatePartiallyOpenedListener();
            ServiceMessageContext encodeContext = ServiceMessageContext.Create(m_telemetry);
            encodeContext.MaxStringLength = 0;
            byte[] body = BinaryEncoder.EncodeMessage(
                CreateReadRequestWithLongNodeId(4711, DefaultEncodingLimits.MaxStringLength + 100),
                encodeContext);
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = "application/octet-stream";
            context.Request.ContentLength = body.Length;
            context.Request.Body = new MemoryStream(body);
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await listener.SendAsync(context).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.OK));
            ServiceFault fault = BinaryDecoder.DecodeMessage<ServiceFault>(
                responseBody.ToArray(),
                ServiceMessageContext.Create(m_telemetry));
            Assert.That(
                fault.ResponseHeader.ServiceResult,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(fault.ResponseHeader.RequestHandle, Is.EqualTo(4711u));
            Assert.That((DateTime)fault.ResponseHeader.Timestamp, Is.GreaterThan(DateTime.UtcNow.AddMinutes(-1)));
        }

        /// <summary>
        /// Verify SendAsync response body contains an error message for wrong content type.
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task SendAsyncWritesErrorMessageForWrongContentTypeAsync()
        {
            await using HttpsTransportListener listener = CreatePartiallyOpenedListener();
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = "text/html";
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await listener.SendAsync(context).ConfigureAwait(false);

            responseBody.Position = 0;
            using var reader = new StreamReader(responseBody);
            string body = await reader.ReadToEndAsync().ConfigureAwait(false);
            Assert.That(body, Does.Contain("Unsupported content type"));
        }

        /// <summary>
        /// SendJsonAsync returns 501 NotImplemented when no listener callback
        /// has been wired (mirror of the binary path).
        /// </summary>
        [Test]
        public async Task SendJsonAsyncReturnsNotImplementedWhenCallbackIsNullAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = Profiles.OpcUaJsonContentType;
            context.Response.Body = new MemoryStream();

            await listener.SendJsonAsync(context).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.NotImplemented));
        }

        /// <summary>
        /// SendJsonAsync responds with an OPC UA JSON ServiceFault carrying
        /// BadDecodingError when the request body is not a valid JSON envelope.
        /// </summary>
        [Test]
        public async Task SendJsonAsyncRespondsWithServiceFaultForMalformedBodyAsync()
        {
            await using HttpsTransportListener listener = CreatePartiallyOpenedListener();
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = Profiles.OpcUaJsonContentType;
            context.Request.ContentLength = 5;
            context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("junk!"));
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await listener.SendJsonAsync(context).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.OK));
            Assert.That(context.Response.ContentType, Is.EqualTo(Profiles.OpcUaJsonContentType));

            responseBody.Position = 0;
            using var reader = new StreamReader(responseBody);
            string body = await reader.ReadToEndAsync().ConfigureAwait(false);
            // The fault is an ExtensionObject with its fields inline (Part 6 5.4.9).
            Assert.That(body, Does.Contain("UaTypeId"));
            Assert.That(body, Does.Not.Contain("UaBody"));
            Assert.That(body, Does.Contain("ResponseHeader"));
            // The fault payload's StringTable carries the BadDecodingError
            // symbolic name and the mapper's failure description.
            Assert.That(body, Does.Contain("BadDecodingError"));
        }

        /// <summary>
        /// A JSON request that cannot be decoded after its RequestHeader is
        /// answered with a ServiceFault that echoes the RequestHandle
        /// (OPC 10000-4 §7.33).
        /// </summary>
        [Test]
        public async Task SendJsonAsyncServiceFaultEchoesRequestHandleOfUndecodableRequestAsync()
        {
            await using HttpsTransportListener listener = CreatePartiallyOpenedListener();
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(
                "{\"UaTypeId\":\"i=4294967\",\"RequestHeader\":{\"RequestHandle\":4711}}");
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = Profiles.OpcUaJsonContentType;
            context.Request.ContentLength = payload.Length;
            context.Request.Body = new MemoryStream(payload);
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await listener.SendJsonAsync(context).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.OK));
            IServiceResponse response = JsonDecoder.DecodeMessage<IServiceResponse>(
                responseBody.ToArray(),
                ServiceMessageContext.Create(m_telemetry));
            Assert.That(response, Is.InstanceOf<ServiceFault>());
            Assert.That(StatusCode.IsBad(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(4711u));
        }

        /// <summary>
        /// Without a matching JSON endpoint the listener only serves discovery;
        /// the fault for another decoded request echoes its RequestHandle.
        /// </summary>
        [Test]
        public async Task SendJsonAsyncDiscoveryOnlyFaultEchoesRequestHandleAsync()
        {
            await using HttpsTransportListener listener = CreatePartiallyOpenedListener();
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { Timestamp = DateTime.UtcNow, RequestHandle = 2024 },
                NodesToRead = [new ReadValueId { NodeId = new NodeId(1u), AttributeId = Attributes.Value }]
            };
            byte[] payload;
            using (var memory = new MemoryStream())
            {
                using (var encoder = new JsonEncoder(memory, ServiceMessageContext.Create(m_telemetry), JsonEncoderOptions.Compact))
                {
                    encoder.EncodeMessage(request, request.TypeId);
                }
                payload = memory.ToArray();
            }
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = Profiles.OpcUaJsonContentType;
            context.Request.ContentLength = payload.Length;
            context.Request.Body = new MemoryStream(payload);
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await listener.SendJsonAsync(context).ConfigureAwait(false);

            IServiceResponse response = JsonDecoder.DecodeMessage<IServiceResponse>(
                responseBody.ToArray(),
                ServiceMessageContext.Create(m_telemetry));
            Assert.That(response, Is.InstanceOf<ServiceFault>());
            Assert.That(
                response.ResponseHeader.ServiceResult,
                Is.EqualTo((StatusCode)StatusCodes.BadSecurityPolicyRejected));
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(2024u));
        }

        private static ReadRequest CreateReadRequestWithLongNodeId(uint requestHandle, int nodeIdLength)
        {
            return new ReadRequest
            {
                RequestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = requestHandle
                },
                NodesToRead =
                [
                    new ReadValueId
                    {
                        NodeId = new NodeId(new string('n', nodeIdLength), 1),
                        AttributeId = Attributes.Value
                    }
                ]
            };
        }

        /// <summary>
        /// AcceptWebSocketAsync returns 501 NotImplemented when no listener
        /// callback has been wired (analogous to SendAsync's behaviour).
        /// </summary>
        [Test]
        public async Task AcceptWebSocketAsyncReturnsNotImplementedWhenCallbackIsNullAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();

            await listener.AcceptWebSocketAsync(context).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.NotImplemented));
        }

        /// <summary>
        /// With mutual TLS, a CreateSession ClientCertificate that carries the leaf
        /// followed by its issuers matches the TLS client certificate (the leaf).
        /// The request then reaches endpoint selection instead of an HTTP 401.
        /// </summary>
        [Test]
        public async Task SendAsyncMutualTlsAcceptsCreateSessionCertificateChainWithTlsLeafAsync()
        {
            using X509Certificate2 leaf = CreateTlsTestCertificate("CN=Https Leaf");
            using X509Certificate2 issuer = CreateTlsTestCertificate("CN=Https Issuer");
            byte[] chain = [.. leaf.RawData, .. issuer.RawData];

            ServiceFault fault = await SendCreateSessionWithMutualTlsAsync(leaf, chain).ConfigureAwait(false);

            // the certificate check passed; the listener has no endpoints, so the
            // request is refused by the discovery-only gate that follows it.
            Assert.That(
                fault.ResponseHeader.ServiceResult,
                Is.EqualTo((StatusCode)StatusCodes.BadSecurityPolicyRejected));
        }

        /// <summary>
        /// A CreateSession ClientCertificate whose leaf differs from the TLS client
        /// certificate is answered with a ServiceFault, not an HTTP 401.
        /// </summary>
        [Test]
        public async Task SendAsyncMutualTlsRejectsMismatchedCreateSessionCertificateWithServiceFaultAsync()
        {
            using X509Certificate2 tls = CreateTlsTestCertificate("CN=Https Tls");
            using X509Certificate2 other = CreateTlsTestCertificate("CN=Https Other");
            byte[] chain = [.. other.RawData, .. tls.RawData];

            ServiceFault fault = await SendCreateSessionWithMutualTlsAsync(tls, chain).ConfigureAwait(false);

            Assert.That(
                fault.ResponseHeader.ServiceResult,
                Is.EqualTo((StatusCode)StatusCodes.BadSecurityChecksFailed));
            Assert.That(fault.ResponseHeader.RequestHandle, Is.EqualTo(42u));
        }

        private async Task<ServiceFault> SendCreateSessionWithMutualTlsAsync(
            X509Certificate2 tlsCertificate,
            byte[] clientCertificate)
        {
            await using HttpsTransportListener listener = CreatePartiallyOpenedListener(mutualTls: true);
            byte[] body = BinaryEncoder.EncodeMessage(
                new CreateSessionRequest
                {
                    RequestHeader = new RequestHeader { RequestHandle = 42 },
                    ClientCertificate = clientCertificate.ToByteString(),
                    ClientNonce = Nonce.CreateRandomNonceData(32).ToByteString(),
                    RequestedSessionTimeout = 60000
                },
                ServiceMessageContext.Create(m_telemetry));
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = "application/octet-stream";
            context.Request.ContentLength = body.Length;
            context.Request.Body = new MemoryStream(body);
            context.Connection.ClientCertificate = tlsCertificate;
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await listener.SendAsync(context).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.OK));
            return BinaryDecoder.DecodeMessage<ServiceFault>(
                responseBody.ToArray(),
                ServiceMessageContext.Create(m_telemetry));
        }

        /// <summary>
        /// The listener pins its TLS certificate before Kestrel binds. When the
        /// bind fails because the port is taken, the failed start stops the
        /// listener again and releases the certificate, so a caller that drops
        /// the listener does not leak it.
        /// </summary>
        [Test]
        public async Task OpenOnTakenPortReleasesPinnedTlsCertificateAsync()
        {
            using Certificate tlsCertificate = CertificateBuilder.Create("CN=Https Taken Port").CreateForRSA();
            var registry = new Mock<ICertificateRegistry>();
            registry.Setup(value => value.AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Https))
                .Returns(() => new CertificateEntry(
                    tlsCertificate, new CertificateCollection(), ObjectTypeIds.RsaSha256ApplicationCertificateType));
            var settings = new TransportListenerSettings
            {
                Descriptions = [],
                Configuration = EndpointConfiguration.Create(),
                ServerCertificates = registry.Object,
                CertificateValidator = new Mock<ICertificateValidatorEx>().Object,
                NamespaceUris = new NamespaceTable()
            };

            // Dual-mode on [::], so it holds the port Kestrel binds for a host name.
            TcpListener blocker = TcpListener.Create(0);
            blocker.Start();
            try
            {
                int port = ((IPEndPoint)blocker.LocalEndpoint).Port;
                await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);

                await Assert.CatchAsync<IOException>(async () => await listener.OpenAsync(
                    new Uri($"https://localhost:{port}/taken"),
                    settings,
                    Mock.Of<ITransportListenerCallback>()).ConfigureAwait(false));

                Assert.That(GetPrivateField(listener, "m_pinnedServerCert"), Is.Null);
                Assert.That(GetPrivateField(listener, "m_pinnedServerCertX509"), Is.Null);
            }
            finally
            {
                blocker.Stop();
            }
        }

        /// <summary>
        /// When the cleanup after a failed start fails too, here because the
        /// host throws on dispose, the pinned TLS certificate is still released
        /// and both errors are reported.
        /// </summary>
        [Test]
        public async Task FailedStartWithFailingCleanupStillReleasesPinnedTlsCertificateAsync()
        {
            using Certificate tlsCertificate = CertificateBuilder.Create("CN=Https Failed Cleanup").CreateForRSA();
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            var startError = new InvalidOperationException("No TLS certificate.");
            var registry = new Mock<ICertificateRegistry>();
            registry.Setup(value => value.AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Https))
                .Throws(startError);
            var cleanupError = new InvalidOperationException("Host dispose failed.");
            var host = new Mock<IHost>();
            host.Setup(value => value.Dispose()).Throws(cleanupError);
            SetPrivateField(listener, "m_serverCertProvider", registry.Object);
            SetPrivateField(listener, "m_host", host.Object);
            SetPrivateField(listener, "m_pinnedServerCert", tlsCertificate.AddRef());

            AggregateException error = await Assert.ThrowsAsync<AggregateException>(
                async () => await listener.StartAsync().ConfigureAwait(false));

            Assert.That(error.InnerExceptions, Is.EqualTo(new Exception[] { startError, cleanupError }));
            Assert.That(GetPrivateField(listener, "m_pinnedServerCert"), Is.Null);
        }

        private static object? GetPrivateField(HttpsTransportListener listener, string name)
        {
            return typeof(HttpsTransportListener)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(listener);
        }

        private static void SetPrivateField(HttpsTransportListener listener, string name, object value)
        {
            typeof(HttpsTransportListener)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(listener, value);
        }

        private static X509Certificate2 CreateTlsTestCertificate(string subject)
        {
            using Certificate certificate = CertificateBuilder.Create(subject)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            return certificate.AsX509Certificate2();
        }

        private HttpsTransportListener CreatePartiallyOpenedListener(
            int maxMessageSize = 0,
            bool mutualTls = false)
        {
            var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            var baseAddress = new Uri("https://localhost:51002");
            var callback = new Mock<ITransportListenerCallback>();
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            if (maxMessageSize > 0)
            {
                configuration.MaxMessageSize = maxMessageSize;
            }
            var settings = new TransportListenerSettings
            {
                Descriptions = [],
                Configuration = configuration,
                ServerCertificates = null,
                CertificateValidator = new Mock<ICertificateValidatorEx>().Object,
                NamespaceUris = new NamespaceTable(),
                Factory = null,
                HttpsMutualTls = mutualTls
            };

            try
            {
                listener.OpenAsync(baseAddress, settings, callback.Object).AsTask().GetAwaiter().GetResult();
            }
            catch (NullReferenceException)
            {
                // Expected: ServerCertificates is null, Start() fails.
            }

            return listener;
        }

        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
        [Category("SecurityRegression")]
        public async Task SecurityR01JsonWithoutClientCertificateNeverDispatchesAsync(bool json, int operation)
        {
            int dispatches = 0;
            var callback = new Mock<ITransportListenerCallback>();
            callback.Setup(value => value.ProcessRequestAsync(
                    It.IsAny<SecureChannelContext>(), It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                .Returns<SecureChannelContext, IServiceRequest, CancellationToken>((_, _, _) =>
                {
                    dispatches++;
                    return new ValueTask<IServiceResponse>(new ReadResponse());
                });
            await using HttpsTransportListener listener = CreateSecurityListener(callback.Object, mutualTls: true);
            IServiceRequest request = operation switch
            {
                0 => new CreateSessionRequest(),
                1 => new ActivateSessionRequest(),
                _ => new ReadRequest()
            };
            request.RequestHeader = new RequestHeader
            {
                RequestHandle = 819,
                AuthenticationToken = new NodeId(1u, 0)
            };

            IServiceResponse response = await DispatchSecurityRequestAsync(listener, request, json).ConfigureAwait(false);

            Assert.That(response, Is.TypeOf<ServiceFault>());
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(819u));
            Assert.That(dispatches, Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        [Category("SecurityRegression")]
        public async Task SecurityR01MatchingValidatedCertificatePreservesAnonymousSessionAsync(bool json)
        {
            using X509Certificate2 certificate = CreateTlsTestCertificate("CN=Https Json Client");
            string? channelId = null;
            int dispatches = 0;
            var callback = new Mock<ITransportListenerCallback>();
            callback.Setup(value => value.ProcessRequestAsync(
                    It.IsAny<SecureChannelContext>(), It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                .Returns<SecureChannelContext, IServiceRequest, CancellationToken>((channel, request, _) =>
                {
                    Assert.That(channelId ??= channel.SecureChannelId, Is.EqualTo(channel.SecureChannelId));
                    Assert.That(channel.ClientChannelCertificate.ToArray(), Is.EqualTo(certificate.RawData));
                    dispatches++;
                    IServiceResponse response = request switch
                    {
                        CreateSessionRequest => new CreateSessionResponse(),
                        ActivateSessionRequest => new ActivateSessionResponse(),
                        _ => new ReadResponse { Results = [new DataValue(Variant.From(42))] }
                    };
                    response.ResponseHeader.RequestHandle = request.RequestHeader.RequestHandle;
                    return new ValueTask<IServiceResponse>(response);
                });
            await using HttpsTransportListener listener = CreateSecurityListener(callback.Object, mutualTls: true);
            using X509Certificate2 issuer = CreateTlsTestCertificate("CN=Https Json Issuer");
            IServiceRequest[] requests =
            [
                new CreateSessionRequest { ClientCertificate = ByteString.From([.. certificate.RawData, .. issuer.RawData]) },
                new ActivateSessionRequest { UserIdentityToken = new ExtensionObject(new AnonymousIdentityToken()) },
                new ReadRequest()
            ];
            foreach (IServiceRequest request in requests)
            {
                request.RequestHeader = new RequestHeader { RequestHandle = (uint)dispatches + 1 };
                IServiceResponse response = await DispatchSecurityRequestAsync(listener, request, json, certificate)
                    .ConfigureAwait(false);
                Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                if (response is ReadResponse read)
                {
                    Assert.That(read.Results[0].WrappedValue.TryGetValue(out int marker), Is.True);
                    Assert.That(marker, Is.EqualTo(42));
                }
            }
            Assert.That(dispatches, Is.EqualTo(3));
            Assert.That(channelId, Is.EqualTo(listener.ListenerId));
        }

        [TestCase(true, true, true)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(false, false, false)]
        [Category("SecurityRegression")]
        public async Task SecurityR01DiscoveryAndExplicitTlsOnlyModeRetainSupportedAccessAsync(
            bool json, bool mutualTls, bool discovery)
        {
            int dispatches = 0;
            var callback = new Mock<ITransportListenerCallback>();
            callback.Setup(value => value.ProcessRequestAsync(
                    It.IsAny<SecureChannelContext>(), It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                .Returns<SecureChannelContext, IServiceRequest, CancellationToken>((_, request, _) =>
                {
                    dispatches++;
                    IServiceResponse response = discovery ? new GetEndpointsResponse() : new ReadResponse();
                    response.ResponseHeader.RequestHandle = request.RequestHeader.RequestHandle;
                    return new ValueTask<IServiceResponse>(response);
                });
            await using HttpsTransportListener listener = CreateSecurityListener(callback.Object, mutualTls);
            IServiceRequest request = discovery ? new GetEndpointsRequest() : new ReadRequest();
            request.RequestHeader = new RequestHeader { RequestHandle = 127 };
            IServiceResponse response = await DispatchSecurityRequestAsync(listener, request, json).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(127u));
            Assert.That(dispatches, Is.EqualTo(1));
        }

        [TestCase(true)]
        [TestCase(false)]
        [Category("SecurityRegression")]
        public async Task SecurityR01MismatchedApplicationLeafNeverDispatchesAsync(bool json)
        {
            using X509Certificate2 certificate = CreateTlsTestCertificate("CN=Https Actual Leaf");
            using X509Certificate2 other = CreateTlsTestCertificate("CN=Https Other Leaf");
            var callback = new Mock<ITransportListenerCallback>(MockBehavior.Strict);
            await using HttpsTransportListener listener = CreateSecurityListener(callback.Object, mutualTls: true);
            var request = new CreateSessionRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 128 },
                ClientCertificate = ByteString.From([.. other.RawData, .. certificate.RawData])
            };
            IServiceResponse response = await DispatchSecurityRequestAsync(listener, request, json, certificate)
                .ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(128u));
            callback.VerifyNoOtherCalls();
        }

        private HttpsTransportListener CreateSecurityListener(ITransportListenerCallback callback, bool mutualTls)
        {
            var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            listener.Initialize(new Uri("https://localhost:51002"), new TransportListenerSettings
            {
                Descriptions =
                [
                    new EndpointDescription
                    {
                        EndpointUrl = "https://localhost:51002",
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None,
                        TransportProfileUri = Profiles.HttpsBinaryTransport,
                        UserIdentityTokens = [new UserTokenPolicy(UserTokenType.Anonymous)]
                    },
                    new EndpointDescription
                    {
                        EndpointUrl = "https://localhost:51002",
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None,
                        TransportProfileUri = Profiles.HttpsJsonTransport,
                        UserIdentityTokens = [new UserTokenPolicy(UserTokenType.Anonymous)]
                    }
                ],
                Configuration = EndpointConfiguration.Create(),
                HttpsMutualTls = mutualTls
            }, callback);
            return listener;
        }

        private async Task<IServiceResponse> DispatchSecurityRequestAsync(
            HttpsTransportListener listener, IServiceRequest request, bool json, X509Certificate2? certificate = null)
        {
            byte[] payload;
            if (json)
            {
                using var body = new MemoryStream();
                using (var encoder = new JsonEncoder(
                    body, ServiceMessageContext.Create(m_telemetry), JsonEncoderOptions.Compact))
                {
                    encoder.EncodeMessage(request, request.TypeId);
                }
                payload = body.ToArray();
            }
            else
            {
                payload = BinaryEncoder.EncodeMessage(request, ServiceMessageContext.Create(m_telemetry));
            }
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = json ? Profiles.OpcUaJsonContentType : "application/octet-stream";
            context.Request.ContentLength = payload.Length;
            using var requestBody = new MemoryStream(payload);
            context.Request.Body = requestBody;
            context.Connection.ClientCertificate = certificate;
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;
            if (json)
            {
                await listener.SendJsonAsync(context).ConfigureAwait(false);
                return JsonDecoder.DecodeMessage<IServiceResponse>(
                    responseBody.ToArray(), ServiceMessageContext.Create(m_telemetry));
            }
            await listener.SendBinaryAsync(context).ConfigureAwait(false);
            return BinaryDecoder.DecodeMessage<IServiceResponse>(
                responseBody.ToArray(), ServiceMessageContext.Create(m_telemetry));
        }
#endif
    }
}
