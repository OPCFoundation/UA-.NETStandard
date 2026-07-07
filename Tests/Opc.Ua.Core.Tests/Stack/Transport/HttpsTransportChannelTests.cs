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

#nullable enable

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Unit tests for <see cref="HttpsTransportChannel"/> and the two
    /// associated factories (<see cref="HttpsTransportChannelFactory"/> /
    /// <see cref="OpcHttpsTransportChannelFactory"/>). Covers constructor,
    /// lifecycle (Open / Close / Reconnect / Dispose), property accessors,
    /// and pre-Open guard behaviour without hitting a real HTTP server.
    /// </summary>
    [TestFixture]
    [Category("HttpsTransportChannel")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class HttpsTransportChannelTests
    {
        private ITelemetryContext m_telemetry = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        /// <summary>
        /// Constructor with the <c>https</c> scheme must succeed.
        /// </summary>
        [Test]
        public void ConstructorWithHttpsSchemeCreatesInstance()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(channel, Is.Not.Null);
        }

        /// <summary>
        /// Constructor with the <c>opc.https</c> scheme must succeed.
        /// </summary>
        [Test]
        public void ConstructorWithOpcHttpsSchemeCreatesInstance()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeOpcHttps, m_telemetry);
            Assert.That(channel, Is.Not.Null);
        }

        /// <summary>
        /// <see cref="HttpsTransportChannel.UriScheme"/> must reflect the
        /// scheme passed to the constructor.
        /// </summary>
        [Test]
        public void UriSchemeReturnsConstructorScheme()
        {
            using var httpsChannel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            using var opcHttpsChannel = new HttpsTransportChannel(Utils.UriSchemeOpcHttps, m_telemetry);

            Assert.That(httpsChannel.UriScheme, Is.EqualTo("https"));
            Assert.That(opcHttpsChannel.UriScheme, Is.EqualTo("opc.https"));
        }

        /// <summary>
        /// <see cref="HttpsTransportChannel.SupportedFeatures"/> must be
        /// <see cref="TransportChannelFeatures.None"/> (no persistent
        /// connection to reconnect or reuse).
        /// </summary>
        [Test]
        public void SupportedFeaturesIsNone()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(channel.SupportedFeatures, Is.EqualTo(TransportChannelFeatures.None));
        }

        /// <summary>
        /// Certificate thumbprint properties must be empty arrays before Open.
        /// </summary>
        [Test]
        public void ThumbprintPropertiesReturnEmptyBeforeOpen()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(channel.ChannelThumbprint, Is.Not.Null.And.Empty);
            Assert.That(channel.ClientChannelCertificate, Is.Not.Null.And.Empty);
            Assert.That(channel.ServerChannelCertificate, Is.Not.Null.And.Empty);
        }

        /// <summary>
        /// <see cref="HttpsTransportChannel.CurrentToken"/> must return a
        /// non-null struct even before Open.
        /// </summary>
        [Test]
        public void CurrentTokenIsNonNullBeforeOpen()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            ChannelToken token = channel.CurrentToken;
            Assert.That(token, Is.Not.Null);
        }

        /// <summary>
        /// <see cref="HttpsTransportChannel.EndpointDescription"/> before Open
        /// must throw <see cref="StatusCodes.BadNotConnected"/>.
        /// </summary>
        [Test]
        public void EndpointDescriptionBeforeOpenThrowsBadNotConnected()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => _ = channel.EndpointDescription)!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNotConnected));
        }

        /// <summary>
        /// <see cref="HttpsTransportChannel.EndpointConfiguration"/> before
        /// Open must throw <see cref="StatusCodes.BadNotConnected"/>.
        /// </summary>
        [Test]
        public void EndpointConfigurationBeforeOpenThrowsBadNotConnected()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => _ = channel.EndpointConfiguration)!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNotConnected));
        }

        /// <summary>
        /// <see cref="HttpsTransportChannel.MessageContext"/> before Open
        /// must throw <see cref="StatusCodes.BadNotConnected"/>.
        /// </summary>
        [Test]
        public void MessageContextBeforeOpenThrowsBadNotConnected()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => _ = channel.MessageContext)!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNotConnected));
        }

        /// <summary>
        /// <see cref="HttpsTransportChannel.ReconnectAsync"/> must throw
        /// because the HTTPS channel creates a fresh HTTP connection per request.
        /// </summary>
        [Test]
        public void ReconnectAsyncThrowsException()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            Assert.CatchAsync(
                async () => await channel.ReconnectAsync(
                    connection: null,
                    ct: CancellationToken.None).ConfigureAwait(false));
        }

        /// <summary>
        /// <see cref="HttpsTransportChannel.SendRequestAsync"/> before Open
        /// must throw <see cref="StatusCodes.BadNotConnected"/>.
        /// </summary>
        [Test]
        public void SendRequestAsyncBeforeOpenThrowsBadNotConnected()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await channel.SendRequestAsync(
                    new ReadRequest(),
                    CancellationToken.None).ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNotConnected));
        }

        /// <summary>
        /// <see cref="HttpsTransportChannel.OpenAsync(Uri, TransportChannelSettings, CancellationToken)"/>
        /// must store the settings and expose them through the property accessors.
        /// </summary>
        [Test]
        public async Task OpenAsyncStoresSettingsAndExposesPropertiesAsync()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            var url = new Uri("https://localhost:4840/UA");
            var desc = new EndpointDescription { EndpointUrl = url.ToString() };
            var config = EndpointConfiguration.Create();
            config.OperationTimeout = 5000;

            var settings = new TransportChannelSettings
            {
                Description = desc,
                Configuration = config,
                Factory = ServiceMessageContext.CreateEmpty(m_telemetry).Factory,
                NamespaceUris = new NamespaceTable()
            };

            await channel.OpenAsync(url, settings, CancellationToken.None).ConfigureAwait(false);

            Assert.That(channel.EndpointDescription, Is.SameAs(desc));
            Assert.That(channel.EndpointConfiguration, Is.SameAs(config));
            Assert.That(channel.MessageContext, Is.Not.Null);
            Assert.That(channel.OperationTimeout, Is.EqualTo(5000));
        }

        /// <summary>
        /// The connection-overload of
        /// <see cref="HttpsTransportChannel.OpenAsync(ITransportWaitingConnection, TransportChannelSettings, CancellationToken)"/>
        /// must behave identically to the Uri overload.
        /// </summary>
        [Test]
        public async Task OpenAsyncConnectionOverloadStoresSettingsAsync()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            var url = new Uri("https://localhost:4840/UA");
            var desc = new EndpointDescription { EndpointUrl = url.ToString() };
            var config = EndpointConfiguration.Create();
            var settings = new TransportChannelSettings
            {
                Description = desc,
                Configuration = config,
                Factory = ServiceMessageContext.CreateEmpty(m_telemetry).Factory,
                NamespaceUris = new NamespaceTable()
            };

            var connection = new FakeWaitingConnection(url);
            await channel.OpenAsync(connection, settings, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(channel.EndpointDescription, Is.SameAs(desc));
        }

        /// <summary>
        /// <see cref="HttpsTransportChannel.CloseAsync"/> on a just-opened
        /// channel must complete without throwing.
        /// </summary>
        [Test]
        public async Task CloseAsyncAfterOpenDoesNotThrowAsync()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            var url = new Uri("https://localhost:4840/UA");
            var settings = CreateMinimalSettings(url);
            await channel.OpenAsync(url, settings, CancellationToken.None).ConfigureAwait(false);
            await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Calling Dispose before Open must not throw.
        /// </summary>
        [Test]
        public void DisposeBeforeOpenDoesNotThrow()
        {
            var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            Assert.DoesNotThrow(channel.Dispose);
        }

        /// <summary>
        /// Calling Dispose twice must not throw (idempotent contract).
        /// </summary>
        [Test]
        public void DoubleDisposeDoesNotThrow()
        {
            var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            channel.Dispose();
            Assert.DoesNotThrow(channel.Dispose);
        }

        /// <summary>
        /// <see cref="HttpsTransportChannel.CloseAsync"/> on a disposed channel
        /// must throw <see cref="ObjectDisposedException"/>.
        /// </summary>
        [Test]
        public void CloseAsyncAfterDisposeThrowsObjectDisposedException()
        {
            var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            channel.Dispose();
            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false));
        }

        /// <summary>
        /// The OnTokenActivated event add/remove accessors must not throw.
        /// </summary>
        [Test]
        public void OnTokenActivatedEventIsAddRemoveSafe()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            ChannelTokenActivatedEventHandler handler = (_, _, _) => { };
            Assert.DoesNotThrow(() => channel.OnTokenActivated += handler);
            Assert.DoesNotThrow(() => channel.OnTokenActivated -= handler);
        }

        /// <summary>
        /// <see cref="HttpsTransportChannel.OperationTimeout"/> must be
        /// readable and writable before Open.
        /// </summary>
        [Test]
        public void OperationTimeoutIsReadWriteBeforeOpen()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            channel.OperationTimeout = 9999;
            Assert.That(channel.OperationTimeout, Is.EqualTo(9999));
        }

        /// <summary>
        /// <see cref="HttpsTransportChannelFactory.UriScheme"/> must be "https".
        /// </summary>
        [Test]
        public void HttpsTransportChannelFactoryUriSchemeIsHttps()
        {
            var factory = new HttpsTransportChannelFactory();
            Assert.That(factory.UriScheme, Is.EqualTo("https"));
        }

        /// <summary>
        /// <see cref="HttpsTransportChannelFactory.Create"/> must return
        /// an <see cref="HttpsTransportChannel"/> instance.
        /// </summary>
        [Test]
        public void HttpsTransportChannelFactoryCreatesCorrectType()
        {
            var factory = new HttpsTransportChannelFactory();
            using ITransportChannel channel = factory.Create(m_telemetry);
            Assert.That(channel, Is.InstanceOf<HttpsTransportChannel>());
        }

        /// <summary>
        /// <see cref="OpcHttpsTransportChannelFactory.UriScheme"/> must be "opc.https".
        /// </summary>
        [Test]
        public void OpcHttpsTransportChannelFactoryUriSchemeIsOpcHttps()
        {
            var factory = new OpcHttpsTransportChannelFactory();
            Assert.That(factory.UriScheme, Is.EqualTo("opc.https"));
        }

        /// <summary>
        /// <see cref="OpcHttpsTransportChannelFactory.Create"/> must return
        /// an <see cref="HttpsTransportChannel"/> instance.
        /// </summary>
        [Test]
        public void OpcHttpsTransportChannelFactoryCreatesCorrectType()
        {
            var factory = new OpcHttpsTransportChannelFactory();
            using ITransportChannel channel = factory.Create(m_telemetry);
            Assert.That(channel, Is.InstanceOf<HttpsTransportChannel>());
        }

        /// <summary>
        /// After Close a SendRequest must throw <see cref="StatusCodes.BadNotConnected"/>
        /// because the internal <c>m_client</c> is set to null.
        /// </summary>
        [Test]
        public async Task SendRequestAsyncAfterCloseThrowsBadNotConnectedAsync()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
            var url = new Uri("https://localhost:4840/UA");
            var settings = CreateMinimalSettings(url);
            await channel.OpenAsync(url, settings, CancellationToken.None).ConfigureAwait(false);
            await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await channel.SendRequestAsync(
                    new ReadRequest(),
                    CancellationToken.None).ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNotConnected));
        }

        /// <summary>
        /// The opc.https scheme URL prefix ("opc.") must be stripped during
        /// Open so that the underlying HttpClient receives a canonical
        /// <c>https://</c> URL.
        /// </summary>
        [Test]
        public async Task OpenAsyncStripsOpcPrefixFromOpcHttpsSchemeAsync()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeOpcHttps, m_telemetry);
            var url = new Uri("opc.https://localhost:4840/UA");
            var settings = CreateMinimalSettings(url);

            // OpenAsync must succeed without throwing even for opc.https URLs.
            await channel.OpenAsync(url, settings, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// <see cref="HttpsTransportChannel.GetRetryAfter"/> must read a
        /// delta-seconds HTTP <c>Retry-After</c> header.
        /// </summary>
        [Test]
        public void GetRetryAfterReadsDeltaSeconds()
        {
            using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter =
                new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));

            Assert.That(
                HttpsTransportChannel.GetRetryAfter(response),
                Is.EqualTo(TimeSpan.FromSeconds(5)));
        }

        /// <summary>
        /// <see cref="HttpsTransportChannel.GetRetryAfter"/> must read an
        /// HTTP-date <c>Retry-After</c> header as a forward-looking delay.
        /// </summary>
        [Test]
        public void GetRetryAfterReadsHttpDate()
        {
            using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            DateTimeOffset when = DateTimeOffset.UtcNow.AddSeconds(30);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(when);

            TimeSpan? delay = HttpsTransportChannel.GetRetryAfter(response);

            Assert.That(delay, Is.Not.Null);
            Assert.That(
                delay!.Value,
                Is.GreaterThan(TimeSpan.FromSeconds(20))
                    .And.LessThanOrEqualTo(TimeSpan.FromSeconds(31)));
        }

        /// <summary>
        /// <see cref="HttpsTransportChannel.GetRetryAfter"/> must return
        /// <c>null</c> when no <c>Retry-After</c> header is present.
        /// </summary>
        [Test]
        public void GetRetryAfterReturnsNullWhenAbsent()
        {
            using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            Assert.That(HttpsTransportChannel.GetRetryAfter(response), Is.Null);
        }

        /// <summary>
        /// <see cref="HttpsTransportChannel.CreateServerTooBusyException"/> must
        /// map a throttled response to <see cref="StatusCodes.BadServerTooBusy"/>
        /// and carry the retry-after as a machine-readable token.
        /// </summary>
        [Test]
        public void CreateServerTooBusyExceptionCarriesRetryAfterToken()
        {
            using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter =
                new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));

            ServiceResultException ex =
                HttpsTransportChannel.CreateServerTooBusyException(response);

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadServerTooBusy));
            Assert.That(ex.Result.AdditionalInfo, Does.Contain("RetryAfterMs=2000"));
        }

        /// <summary>
        /// <see cref="HttpsTransportChannel.CreateServerTooBusyException"/> must
        /// still map to <see cref="StatusCodes.BadServerTooBusy"/> when no
        /// <c>Retry-After</c> header is present.
        /// </summary>
        [Test]
        public void CreateServerTooBusyExceptionWithoutHintMapsBusy()
        {
            using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            ServiceResultException ex =
                HttpsTransportChannel.CreateServerTooBusyException(response);

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadServerTooBusy));
        }

        /// <summary>
        /// Creates a minimal <see cref="TransportChannelSettings"/> sufficient
        /// to call <see cref="HttpsTransportChannel.OpenAsync"/>.
        /// </summary>
        private TransportChannelSettings CreateMinimalSettings(Uri endpointUrl)
        {
            var config = EndpointConfiguration.Create();
            return new TransportChannelSettings
            {
                Description = new EndpointDescription { EndpointUrl = endpointUrl.ToString() },
                Configuration = config,
                Factory = ServiceMessageContext.CreateEmpty(m_telemetry).Factory,
                NamespaceUris = new NamespaceTable()
            };
        }

        /// <summary>
        /// Minimal stub for <see cref="ITransportWaitingConnection"/>.
        /// </summary>
        private sealed class FakeWaitingConnection : ITransportWaitingConnection
        {
            private readonly Uri m_url;

            internal FakeWaitingConnection(Uri url)
            {
                m_url = url;
            }

            public string ServerUri => string.Empty;

            public Uri EndpointUrl => m_url;

#pragma warning disable CS8603 // Possible null reference return.
            public object Handle => null!;
#pragma warning restore CS8603
        }
    }
}
