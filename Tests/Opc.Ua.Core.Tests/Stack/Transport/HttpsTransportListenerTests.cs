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
using System.Collections.Generic;
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

        // Verify constructor with https scheme creates a valid instance.
        [Test]
        public void ConstructorWithHttpsSchemeCreatesInstance()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener, Is.Not.Null);
        }

        // Verify constructor with opc.https scheme creates a valid instance.
        [Test]
        public void ConstructorWithOpcHttpsSchemeCreatesInstance()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeOpcHttps, m_telemetry);
            Assert.That(listener, Is.Not.Null);
        }

        // Verify UriScheme property returns the scheme passed to the constructor.
        [Test]
        public void UriSchemeReturnsHttpsWhenConstructedWithHttps()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener.UriScheme, Is.EqualTo("https"));
        }

        // Verify UriScheme property returns opc.https when constructed with that scheme.
        [Test]
        public void UriSchemeReturnsOpcHttpsWhenConstructedWithOpcHttps()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeOpcHttps, m_telemetry);
            Assert.That(listener.UriScheme, Is.EqualTo("opc.https"));
        }

        // Verify ListenerId is null before Open is called.
        [Test]
        public void ListenerIdIsNullBeforeOpen()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener.ListenerId, Is.Null);
        }

        // Verify EndpointUrl is null before Open is called.
        [Test]
        public void EndpointUrlIsNullBeforeOpen()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener.EndpointUrl, Is.Null);
        }

        // Verify Close on an unopened listener does not throw.
        [Test]
        public void CloseOnUnopenedListenerDoesNotThrow()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.DoesNotThrow(() => listener.Close());
        }

        // Verify Dispose on an unopened listener does not throw.
        [Test]
        public void DisposeOnUnopenedListenerDoesNotThrow()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.DoesNotThrow(() => listener.Dispose());
        }

        // Verify calling Dispose twice does not throw.
        [Test]
        public void DoubleDisposeDoesNotThrow()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            listener.Dispose();
            Assert.DoesNotThrow(() => listener.Dispose());
        }

        // Verify Close followed by Dispose does not throw.
        [Test]
        public void CloseFollowedByDisposeDoesNotThrow()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            listener.Close();
            Assert.DoesNotThrow(() => listener.Dispose());
        }

        // Verify CreateReverseConnection throws NotImplementedException.
        [Test]
        public void CreateReverseConnectionThrowsNotImplementedException()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            var uri = new Uri("https://localhost:4840");
            Assert.Throws<NotImplementedException>(() => listener.CreateReverseConnection(uri, 30000));
        }

        // Verify UpdateChannelLastActiveTime does not throw on unopened listener.
        [Test]
        public void UpdateChannelLastActiveTimeDoesNotThrow()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.DoesNotThrow(() => listener.UpdateChannelLastActiveTime("test-channel-id"));
        }

        // Verify UpdateChannelLastActiveTime with null does not throw.
        [Test]
        public void UpdateChannelLastActiveTimeWithNullDoesNotThrow()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.DoesNotThrow(() => listener.UpdateChannelLastActiveTime(null));
        }

        // Verify UpdateChannelLastActiveTime with empty string does not throw.
        [Test]
        public void UpdateChannelLastActiveTimeWithEmptyStringDoesNotThrow()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.DoesNotThrow(() => listener.UpdateChannelLastActiveTime(string.Empty));
        }

        // Verify the listener implements ITransportListener.
        [Test]
        public void ListenerImplementsITransportListener()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener, Is.InstanceOf<ITransportListener>());
        }

        // Verify the listener implements IDisposable.
        [Test]
        public void ListenerImplementsIDisposable()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener, Is.InstanceOf<IDisposable>());
        }

        // Verify HttpsTransportListenerFactory creates an instance with correct scheme.
        [Test]
        public void HttpsTransportListenerFactoryCreatesListener()
        {
            var factory = new HttpsTransportListenerFactory();
            using ITransportListener listener = factory.Create(m_telemetry);
            Assert.That(listener, Is.Not.Null);
            Assert.That(listener.UriScheme, Is.EqualTo("https"));
        }

        // Verify HttpsTransportListenerFactory UriScheme property.
        [Test]
        public void HttpsTransportListenerFactoryUriSchemeIsHttps()
        {
            var factory = new HttpsTransportListenerFactory();
            Assert.That(factory.UriScheme, Is.EqualTo("https"));
        }

        // Verify OpcHttpsTransportListenerFactory creates an instance with correct scheme.
        [Test]
        public void OpcHttpsTransportListenerFactoryCreatesListener()
        {
            var factory = new OpcHttpsTransportListenerFactory();
            using ITransportListener listener = factory.Create(m_telemetry);
            Assert.That(listener, Is.Not.Null);
            Assert.That(listener.UriScheme, Is.EqualTo("opc.https"));
        }

        // Verify OpcHttpsTransportListenerFactory UriScheme property.
        [Test]
        public void OpcHttpsTransportListenerFactoryUriSchemeIsOpcHttps()
        {
            var factory = new OpcHttpsTransportListenerFactory();
            Assert.That(factory.UriScheme, Is.EqualTo("opc.https"));
        }

        // Verify factory-created listener has null ListenerId before Open.
        [Test]
        public void FactoryCreatedListenerHasNullListenerId()
        {
            var factory = new HttpsTransportListenerFactory();
            using ITransportListener listener = factory.Create(m_telemetry);
            Assert.That(listener.ListenerId, Is.Null);
        }

        // Verify CreateReverseConnection with null uri throws NotImplementedException.
        [Test]
        public void CreateReverseConnectionWithNullUriThrowsNotImplementedException()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.Throws<NotImplementedException>(() => listener.CreateReverseConnection(null, 0));
        }

        // Verify multiple Close calls on an unopened listener do not throw.
        [Test]
        public void MultipleCloseCallsDoNotThrow()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            listener.Close();
            Assert.DoesNotThrow(() => listener.Close());
        }

        // Verify Open throws when ServerCertificateTypesProvider is null.
        [Test]
        public void OpenThrowsWhenCertProviderIsNull()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            var baseAddress = new Uri("https://localhost:51000");
            var callback = new Mock<ITransportListenerCallback>();
            var settings = new TransportListenerSettings
            {
                Descriptions = new List<EndpointDescription>(),
                Configuration = EndpointConfiguration.Create(),
                ServerCertificateTypesProvider = null,
                CertificateValidator = new Mock<ICertificateValidator>().Object,
                NamespaceUris = new NamespaceTable(),
                Factory = null
            };

            Assert.That(
                () => listener.Open(baseAddress, settings, callback.Object),
                Throws.Exception);
        }

        // Verify Open sets ListenerId and EndpointUrl before Start fails.
        [Test]
        public void OpenSetsFieldsBeforeStartFails()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            var baseAddress = new Uri("https://localhost:51001");
            var callback = new Mock<ITransportListenerCallback>();
            var settings = new TransportListenerSettings
            {
                Descriptions = new List<EndpointDescription>(),
                Configuration = EndpointConfiguration.Create(),
                ServerCertificateTypesProvider = null,
                CertificateValidator = new Mock<ICertificateValidator>().Object,
                NamespaceUris = new NamespaceTable(),
                Factory = null
            };

            Assert.Catch(() => listener.Open(baseAddress, settings, callback.Object));

            Assert.That(listener.ListenerId, Is.Not.Null.And.Not.Empty);
            Assert.That(listener.EndpointUrl, Is.EqualTo(baseAddress));
        }

        // Verify Stop is equivalent to Dispose.
        [Test]
        public void StopDoesNotThrowOnUnopenedListener()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            listener.Stop();
            Assert.DoesNotThrow(() => listener.Dispose());
        }

#if NET8_0_OR_GREATER
        // Verify SendAsync returns 501 NotImplemented when callback is null.
        [Test]
        public async Task SendAsyncReturnsNotImplementedWhenCallbackIsNullAsync()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Response.Body = new MemoryStream();

            await listener.SendAsync(context).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.NotImplemented));
        }

        // Verify SendAsync returns BadRequest for unsupported content type.
        [Test]
        public async Task SendAsyncReturnsBadRequestForWrongContentTypeAsync()
        {
            using HttpsTransportListener listener = CreatePartiallyOpenedListener();
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = "text/xml";
            context.Response.Body = new MemoryStream();

            await listener.SendAsync(context).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.BadRequest));
        }

        // Verify SendAsync returns BadRequest when buffer length does not match.
        [Test]
        public async Task SendAsyncReturnsBadRequestForBufferLengthMismatchAsync()
        {
            using HttpsTransportListener listener = CreatePartiallyOpenedListener();
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = "application/octet-stream";
            context.Request.ContentLength = 100;
            context.Request.Body = new MemoryStream(new byte[] { 0x01, 0x02 });
            context.Response.Body = new MemoryStream();

            await listener.SendAsync(context).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.BadRequest));
        }

        // Verify SendAsync returns InternalServerError for invalid binary payload.
        [Test]
        public async Task SendAsyncReturnsInternalServerErrorForInvalidBodyAsync()
        {
            using HttpsTransportListener listener = CreatePartiallyOpenedListener();
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = "application/octet-stream";
            context.Request.ContentLength = 4;
            context.Request.Body = new MemoryStream(new byte[] { 0x01, 0x02, 0x03, 0x04 });
            context.Response.Body = new MemoryStream();

            await listener.SendAsync(context).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.InternalServerError));
        }

        // Verify SendAsync response body contains an error message for wrong content type.
        [Test]
        public async Task SendAsyncWritesErrorMessageForWrongContentTypeAsync()
        {
            using HttpsTransportListener listener = CreatePartiallyOpenedListener();
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

        private HttpsTransportListener CreatePartiallyOpenedListener()
        {
            var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            var baseAddress = new Uri("https://localhost:51002");
            var callback = new Mock<ITransportListenerCallback>();
            var settings = new TransportListenerSettings
            {
                Descriptions = new List<EndpointDescription>(),
                Configuration = EndpointConfiguration.Create(),
                ServerCertificateTypesProvider = null,
                CertificateValidator = new Mock<ICertificateValidator>().Object,
                NamespaceUris = new NamespaceTable(),
                Factory = null
            };

            try
            {
                listener.Open(baseAddress, settings, callback.Object);
            }
            catch (NullReferenceException)
            {
                // Expected: ServerCertificateTypesProvider is null, Start() fails.
            }

            return listener;
        }
#endif
    }
}
