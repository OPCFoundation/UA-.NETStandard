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
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;
#if NET8_0_OR_GREATER
using Microsoft.AspNetCore.Http;
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
            Assert.DoesNotThrow(() => listener.UpdateChannelLastActiveTime(null));
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

            Assert.CatchAsync(async () => await listener.OpenAsync(baseAddress, settings, callback.Object).ConfigureAwait(false));

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
        /// Verify SendAsync returns InternalServerError for invalid binary payload.
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task SendAsyncReturnsInternalServerErrorForInvalidBodyAsync()
        {
            await using HttpsTransportListener listener = CreatePartiallyOpenedListener();
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = "application/octet-stream";
            context.Request.ContentLength = 4;
            context.Request.Body = new MemoryStream([0x01, 0x02, 0x03, 0x04]);
            context.Response.Body = new MemoryStream();

            await listener.SendAsync(context).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.InternalServerError));
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
            Assert.That(body, Does.Contain("UaTypeId"));
            Assert.That(body, Does.Contain("UaBody"));
            // The fault payload's StringTable carries the BadDecodingError
            // symbolic name and the mapper's failure description.
            Assert.That(body, Does.Contain("BadDecodingError"));
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

        private HttpsTransportListener CreatePartiallyOpenedListener()
        {
            var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            var baseAddress = new Uri("https://localhost:51002");
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
#endif
    }
}
