/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client.WebApi;

namespace Opc.Ua.Client.Tests.WebApi
{
    [TestFixture]
    [Category("Client")]
    [Category("WebApi")]
    public sealed class WebApiClientTests
    {
        [Test]
        public void ConstructorAppliesBearerAuthenticationAndAcceptHeader()
        {
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://localhost:4843/")
            };
            var options = new WebApiClientOptions
            {
                BearerToken = "token",
                Encoding = WebApiEncoding.Verbose,
                RequestTimeout = TimeSpan.FromSeconds(7)
            };

            using var client = new WebApiClient(httpClient, options);

            Assert.That(client.BaseAddress, Is.EqualTo(new Uri("https://localhost:4843/")));
            Assert.That(client.Encoding, Is.EqualTo(WebApiEncoding.Verbose));
            Assert.That(httpClient.Timeout, Is.EqualTo(TimeSpan.FromSeconds(7)));
            Assert.That(httpClient.DefaultRequestHeaders.Authorization?.Scheme, Is.EqualTo("Bearer"));
            Assert.That(httpClient.DefaultRequestHeaders.Authorization?.Parameter, Is.EqualTo("token"));
            Assert.That(httpClient.DefaultRequestHeaders.Accept, Has.Count.EqualTo(1));
        }

        [Test]
        public void ConstructorAppliesBasicAuthentication()
        {
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://localhost:4843/")
            };
            var options = new WebApiClientOptions
            {
                BasicCredentials = ("user", "pass")
            };

            using var client = new WebApiClient(httpClient, options);

            Assert.That(client.BaseAddress, Is.EqualTo(new Uri("https://localhost:4843/")));
            Assert.That(httpClient.DefaultRequestHeaders.Authorization?.Scheme, Is.EqualTo("Basic"));
            Assert.That(httpClient.DefaultRequestHeaders.Authorization?.Parameter, Is.Not.Empty);
        }

        [Test]
        public void ConstructorRejectsConflictingAuthenticationOptions()
        {
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://localhost:4843/")
            };
            var options = new WebApiClientOptions
            {
                BearerToken = "token",
                BasicCredentials = ("user", "pass")
            };

            Assert.That(
                () => new WebApiClient(httpClient, options),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void CreateNormalizesOpcUaSchemes()
        {
            using WebApiClient webApiClient = WebApiClient.Create(
                new Uri("opc.https+webapi://localhost:4843/ua"));
            using WebApiClient httpsClient = WebApiClient.Create(
                new Uri("opc.https://localhost:4843/ua"));

            Assert.That(webApiClient.BaseAddress.Scheme, Is.EqualTo(Uri.UriSchemeHttps));
            Assert.That(httpsClient.BaseAddress.Scheme, Is.EqualTo(Uri.UriSchemeHttps));
        }

        [Test]
        public void BaseAddressThrowsWhenHttpClientHasNoBaseAddress()
        {
            using var httpClient = new HttpClient();
            using var client = new WebApiClient(httpClient);

            Assert.That(
                () => _ = client.BaseAddress,
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void InvokeAsyncRejectsNullRequest()
        {
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://localhost:4843/")
            };
            using var client = new WebApiClient(httpClient);

            Assert.That(
                async () => await client.InvokeAsync<ReadRequest, ReadResponse>(null!, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void InvokeRouteAsyncRejectsMismatchedRequestType()
        {
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://localhost:4843/")
            };
            using var client = new WebApiClient(httpClient);

            Assert.That(
                async () => await client.InvokeRouteAsync(
                        new WebApiServiceRoute("/read", "Read", typeof(ReadRequest), typeof(ReadResponse)),
                        new WriteRequest(),
                        CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void DisposePreventsFurtherInvocations()
        {
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://localhost:4843/")
            };
            var client = new WebApiClient(httpClient);
            client.Dispose();
            client.Dispose();

            Assert.That(
                async () => await client.InvokeAsync<ReadRequest, ReadResponse>(
                        new ReadRequest(),
                        CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }
    }
}
