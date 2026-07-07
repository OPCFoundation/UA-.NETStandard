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

using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// Deterministic, socket-free unit tests for the public transport
    /// listener factories and the terminal request-dispatch routing in
    /// <see cref="HttpsTransportListener"/>. Covers the factory
    /// <c>UriScheme</c> / <c>Create</c> surface and the non-POST
    /// <c>405 Method Not Allowed</c> branch of
    /// <see cref="Startup.DispatchListenerRequestAsync"/> using an in-memory
    /// <see cref="DefaultHttpContext"/> (no Kestrel host, sockets, ports,
    /// certificates or TLS handshakes).
    /// </summary>
    [TestFixture]
    [Category("HttpsTransportListenerDeterministic")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class HttpsTransportListenerDeterministicTests
    {
        [Test]
        public void HttpsTransportListenerFactoryUriSchemeIsHttps()
        {
            var factory = new HttpsTransportListenerFactory();

            Assert.That(factory.UriScheme, Is.EqualTo(Utils.UriSchemeHttps));
        }

        [Test]
        public void OpcHttpsTransportListenerFactoryUriSchemeIsOpcHttps()
        {
            var factory = new OpcHttpsTransportListenerFactory();

            Assert.That(factory.UriScheme, Is.EqualTo(Utils.UriSchemeOpcHttps));
        }

        [Test]
        public void WssTransportListenerFactoryUriSchemeIsWss()
        {
            var factory = new WssTransportListenerFactory();

            Assert.That(factory.UriScheme, Is.EqualTo(Utils.UriSchemeWss));
        }

        [Test]
        public void OpcWssTransportListenerFactoryUriSchemeIsOpcWss()
        {
            var factory = new OpcWssTransportListenerFactory();

            Assert.That(factory.UriScheme, Is.EqualTo(Utils.UriSchemeOpcWss));
        }

        [Test]
        public async Task HttpsTransportListenerFactoryCreateReturnsHttpsListenerAsync()
        {
            var factory = new HttpsTransportListenerFactory();

            await using ITransportListener listener = factory.Create(new TestTelemetryContext());

            Assert.That(listener, Is.InstanceOf<HttpsTransportListener>());
            Assert.That(listener.UriScheme, Is.EqualTo(Utils.UriSchemeHttps));
        }

        [Test]
        public async Task OpcHttpsTransportListenerFactoryCreateReturnsOpcHttpsListenerAsync()
        {
            var factory = new OpcHttpsTransportListenerFactory();

            await using ITransportListener listener = factory.Create(new TestTelemetryContext());

            Assert.That(listener, Is.InstanceOf<HttpsTransportListener>());
            Assert.That(listener.UriScheme, Is.EqualTo(Utils.UriSchemeOpcHttps));
        }

        [Test]
        public async Task WssTransportListenerFactoryCreateReturnsWssListenerAsync()
        {
            var factory = new WssTransportListenerFactory();

            await using ITransportListener listener = factory.Create(new TestTelemetryContext());

            Assert.That(listener, Is.InstanceOf<HttpsTransportListener>());
            Assert.That(listener.UriScheme, Is.EqualTo(Utils.UriSchemeWss));
        }

        [Test]
        public async Task OpcWssTransportListenerFactoryCreateReturnsOpcWssListenerAsync()
        {
            var factory = new OpcWssTransportListenerFactory();

            await using ITransportListener listener = factory.Create(new TestTelemetryContext());

            Assert.That(listener, Is.InstanceOf<HttpsTransportListener>());
            Assert.That(listener.UriScheme, Is.EqualTo(Utils.UriSchemeOpcWss));
        }

        [TestCase("GET")]
        [TestCase("PUT")]
        [TestCase("DELETE")]
        [TestCase("HEAD")]
        public async Task DispatchListenerRequestForNonPostReturnsMethodNotAllowedAsync(string method)
        {
            var factory = new HttpsTransportListenerFactory();
            await using ITransportListener created = factory.Create(new TestTelemetryContext());
            var listener = (HttpsTransportListener)created;
            var context = new DefaultHttpContext
            {
                Request =
                {
                    Method = method
                },
                Response =
                {
                    Body = new MemoryStream()
                }
            };

            await Startup.DispatchListenerRequestAsync(listener, context).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.MethodNotAllowed));
            Assert.That(context.Response.ContentType, Is.EqualTo("text/plain"));
            Assert.That(context.Response.ContentLength, Is.Zero);
        }

        private sealed class TestTelemetryContext : TelemetryContextBase
        {
            public TestTelemetryContext()
                : base(NullLoggerFactory.Instance)
            {
            }
        }
    }
}
