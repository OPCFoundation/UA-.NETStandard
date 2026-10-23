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

#if NET10_0_OR_GREATER
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using Opc.Ua.XRegistry.Connector;

namespace Opc.Ua.Tools.Tests.XRegistryConnector
{
    [TestFixture]
    [Category("XRegistryConnector")]
    public sealed class XRegistryBearerHandlerTests
    {
        [TestCase("https://registry.example/root", "https://registry.example/root")]
        [TestCase("https://registry.example/root/", "https://registry.example/root/")]
        [TestCase("https://registry.example/root/", "https://registry.example/root/groups/g?filter=a")]
        [TestCase("https://registry.example/", "https://registry.example/groups/g")]
        [TestCase("https://registry.example:443/root/", "https://REGISTRY.EXAMPLE/root/groups/g")]
        public async Task SameRootHttpsRequestsUseRegistryCredentialInsteadOfCallerAuthorizationAsync(
            string root,
            string target)
        {
            using var configuration = new ConfigurationManager { ["Secrets:material"] = "XREG_TEST_REFERENCE" };
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), _ => k_material);
            using ISecret? secret = store.TryGet(new SecretIdentifier("material", "Environment"));
            Assert.That(secret, Is.Not.Null);
            var identifier = new SecretIdentifier("operator-profile", "TestVault", "vault/scope");
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            registry.Setup(value => value.GetAsync(identifier, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>(secret));
            var inner = new Mock<HttpMessageHandler>();
            using var expectedResponse = new HttpResponseMessage(HttpStatusCode.Accepted);
            HttpRequestMessage? observedRequest = null;
            string? scheme = null;
            string? material = null;
            CancellationToken observedToken = default;
            inner.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((message, token) =>
                {
                    observedRequest = message;
                    scheme = message.Headers.Authorization?.Scheme;
                    material = message.Headers.Authorization?.Parameter;
                    observedToken = token;
                })
                .ReturnsAsync(expectedResponse);
            using var invoker = new HttpMessageInvoker(
                new XRegistryBearerHandler(registry.Object, identifier, new Uri(root), inner.Object));
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(target));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", "caller-fixture");
            using var cancellation = new CancellationTokenSource();

            HttpResponseMessage response = await invoker.SendAsync(request, cancellation.Token).ConfigureAwait(false);

            Assert.That(response, Is.SameAs(expectedResponse));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That(observedRequest, Is.SameAs(request));
            Assert.That(observedRequest!.RequestUri, Is.EqualTo(new Uri(target)));
            Assert.That(scheme, Is.EqualTo("Bearer"));
            Assert.That(material, Is.EqualTo(k_material));
            Assert.That(material, Is.Not.EqualTo("caller-fixture"));
            Assert.That(observedToken, Is.EqualTo(cancellation.Token));
            Assert.That(() => secret!.Bytes.Length, Throws.TypeOf<ObjectDisposedException>());
            registry.Verify(value => value.GetAsync(identifier, cancellation.Token), Times.Once);
            registry.VerifyNoOtherCalls();
            inner.Protected().Verify("SendAsync", Times.Once(),
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        }

        [TestCase("https://unknown.example/root/groups/g")]
        [TestCase("http://registry.example/root/groups/g")]
        [TestCase("https://registry.example:444/root/groups/g")]
        [TestCase("https://registry.example/roots/groups/g")]
        [TestCase("https://registry.example/")]
        [TestCase("https://registry.example/Root/groups/g")]
        [TestCase("https://operator@registry.example/root/groups/g")]
        [TestCase("https://registry.example/root/../private")]
        [TestCase("https://registry.example/root/%2e%2e/private")]
        [TestCase("/root/groups/g")]
        public async Task OutsideRootRequestsNeverResolveCredentialsOrReachTransportAsync(string target)
        {
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            var inner = new Mock<HttpMessageHandler>();
            using var invoker = new HttpMessageInvoker(new XRegistryBearerHandler(registry.Object,
                new SecretIdentifier("operator-profile", "TestVault", "vault/scope"),
                new Uri("https://registry.example/root/"), inner.Object));
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(target, UriKind.RelativeOrAbsolute));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", k_material);

            await Assert.ThatAsync(async () =>
            {
                using HttpResponseMessage response = await invoker.SendAsync(request, CancellationToken.None)
                    .ConfigureAwait(false);
            }, Throws.TypeOf<InvalidOperationException>().With.Message.Not.Contains(k_material)).ConfigureAwait(false);

            registry.VerifyNoOtherCalls();
            inner.Protected().Verify("SendAsync", Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        }

        [Test]
        public async Task MissingRequestUriNeverResolvesCredentialsOrSendsAsync()
        {
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            var inner = new Mock<HttpMessageHandler>();
            using var invoker = new HttpMessageInvoker(new XRegistryBearerHandler(registry.Object,
                new SecretIdentifier("operator-profile", "TestVault"),
                new Uri("https://registry.example/root/"), inner.Object));
            using var request = new HttpRequestMessage();

            await Assert.ThatAsync(async () =>
            {
                using HttpResponseMessage response = await invoker.SendAsync(request, CancellationToken.None)
                    .ConfigureAwait(false);
            }, Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

            registry.VerifyNoOtherCalls();
            inner.Protected().Verify("SendAsync", Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        }

        [Test]
        public async Task MissingOperatorSecretDoesNotFallBackToCallerCredentialsAsync()
        {
            var identifier = new SecretIdentifier("operator-profile", "TestVault", "vault/scope");
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            registry.Setup(value => value.GetAsync(identifier, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>((ISecret?)null));
            var inner = new Mock<HttpMessageHandler>();
            using var invoker = new HttpMessageInvoker(new XRegistryBearerHandler(
                registry.Object, identifier, new Uri("https://registry.example/root/"), inner.Object));
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://registry.example/root/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", k_material);

            await Assert.ThatAsync(async () =>
            {
                using HttpResponseMessage response = await invoker.SendAsync(request, CancellationToken.None)
                    .ConfigureAwait(false);
            }, Throws.TypeOf<UnauthorizedAccessException>().With.Message.Not.Contains(k_material))
                .ConfigureAwait(false);

            registry.Verify(value => value.GetAsync(identifier, CancellationToken.None), Times.Once);
            registry.VerifyNoOtherCalls();
            inner.Protected().Verify("SendAsync", Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        }

        [Test]
        public async Task EmptyProviderCredentialIsRejectedBeforeTransportAsync()
        {
            var identifier = new SecretIdentifier("operator-profile", "TestVault");
            var store = new InMemorySecretStore("TestVault");
            await store.SetAsync(identifier, ReadOnlyMemory<byte>.Empty).ConfigureAwait(false);
            using ISecret? secret = await store.GetAsync(identifier).ConfigureAwait(false);
            Assert.That(secret, Is.Not.Null);
            Assert.That(secret!.Bytes.Length, Is.Zero);
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            registry.Setup(value => value.GetAsync(identifier, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>(secret));
            var inner = new Mock<HttpMessageHandler>();
            using var fallbackResponse = new HttpResponseMessage(HttpStatusCode.OK);
            inner.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(fallbackResponse);
            using var invoker = new HttpMessageInvoker(new XRegistryBearerHandler(
                registry.Object, identifier, new Uri("https://registry.example/root/"), inner.Object));
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://registry.example/root/");

            await Assert.ThatAsync(async () =>
            {
                using HttpResponseMessage response = await invoker.SendAsync(request, CancellationToken.None)
                    .ConfigureAwait(false);
            }, Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);

            registry.Verify(value => value.GetAsync(identifier, CancellationToken.None), Times.Once);
            registry.VerifyNoOtherCalls();
            inner.Protected().Verify("SendAsync", Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        }

        [Test]
        public async Task CancelledSecretLookupPreservesCancellationAndNeverSendsAsync()
        {
            using var configuration = new ConfigurationManager { ["Secrets:operator"] = "XREG_TEST_REFERENCE" };
            int reads = 0;
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), _ =>
            {
                reads++;
                return k_material;
            });
            var identifier = new SecretIdentifier("operator", "Environment");
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            registry.Setup(value => value.GetAsync(identifier, It.IsAny<CancellationToken>()))
                .Returns<SecretIdentifier, CancellationToken>((id, token) => store.GetAsync(id, token));
            var inner = new Mock<HttpMessageHandler>();
            using var invoker = new HttpMessageInvoker(new XRegistryBearerHandler(
                registry.Object, identifier, new Uri("https://registry.example/root/"), inner.Object));
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://registry.example/root/");
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            await Assert.ThatAsync(async () =>
            {
                using HttpResponseMessage response = await invoker.SendAsync(request, cancellation.Token)
                    .ConfigureAwait(false);
            }, Throws.InstanceOf<OperationCanceledException>()
                .With.Property("CancellationToken").EqualTo(cancellation.Token)).ConfigureAwait(false);

            Assert.That(reads, Is.Zero);
            registry.Verify(value => value.GetAsync(identifier, cancellation.Token), Times.Once);
            registry.VerifyNoOtherCalls();
            inner.Protected().Verify("SendAsync", Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        }

        [Test]
        public async Task TransportFailurePropagatesAndDisposesAcquiredSecretAsync()
        {
            using var configuration = new ConfigurationManager { ["Secrets:material"] = "XREG_TEST_REFERENCE" };
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), _ => k_material);
            using ISecret? secret = store.TryGet(new SecretIdentifier("material", "Environment"));
            Assert.That(secret, Is.Not.Null);
            var identifier = new SecretIdentifier("operator-profile", "TestVault", "vault/scope");
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            registry.Setup(value => value.GetAsync(identifier, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>(secret));
            var failure = new IOException("Transport failed.");
            var inner = new Mock<HttpMessageHandler>();
            inner.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns(Task.FromException<HttpResponseMessage>(failure));
            using var invoker = new HttpMessageInvoker(new XRegistryBearerHandler(
                registry.Object, identifier, new Uri("https://registry.example/root/"), inner.Object));
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://registry.example/root/");

            await Assert.ThatAsync(async () =>
            {
                using HttpResponseMessage response = await invoker.SendAsync(request, CancellationToken.None)
                    .ConfigureAwait(false);
            }, Throws.Exception.SameAs(failure)).ConfigureAwait(false);

            Assert.That(() => secret!.Bytes.Length, Throws.TypeOf<ObjectDisposedException>());
            registry.Verify(value => value.GetAsync(identifier, CancellationToken.None), Times.Once);
            registry.VerifyNoOtherCalls();
            inner.Protected().Verify("SendAsync", Times.Once(),
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        }

        [Test]
        public async Task TransportCancellationUsesCallerTokenAndDisposesAcquiredSecretAsync()
        {
            using var configuration = new ConfigurationManager { ["Secrets:material"] = "XREG_TEST_REFERENCE" };
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), _ => k_material);
            using ISecret? secret = store.TryGet(new SecretIdentifier("material", "Environment"));
            Assert.That(secret, Is.Not.Null);
            var identifier = new SecretIdentifier("operator-profile", "TestVault");
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            registry.Setup(value => value.GetAsync(identifier, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>(secret));
            using var cancellation = new CancellationTokenSource();
            var inner = new Mock<HttpMessageHandler>();
            inner.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (_, token) =>
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                });
            using var invoker = new HttpMessageInvoker(new XRegistryBearerHandler(
                registry.Object, identifier, new Uri("https://registry.example/root/"), inner.Object));
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://registry.example/root/");

            await Assert.ThatAsync(async () =>
            {
                using HttpResponseMessage response = await invoker.SendAsync(request, cancellation.Token)
                    .ConfigureAwait(false);
            }, Throws.InstanceOf<OperationCanceledException>()
                .With.Property("CancellationToken").EqualTo(cancellation.Token)).ConfigureAwait(false);

            Assert.That(() => secret!.Bytes.Length, Throws.TypeOf<ObjectDisposedException>());
            registry.Verify(value => value.GetAsync(identifier, cancellation.Token), Times.Once);
            registry.VerifyNoOtherCalls();
            inner.Protected().Verify("SendAsync", Times.Once(),
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.Is<CancellationToken>(token => token == cancellation.Token));
        }

        [TestCase("fixture-private-prefix\r\nextra")]
        [TestCase("=")]
        [TestCase("fixture=tail")]
        [TestCase("fixture material")]
        [TestCase("fixture-\u00e9")]
        public async Task InvalidHeaderMaterialIsNotIncludedInErrorsAndNeverSentAsync(string material)
        {
            using var configuration = new ConfigurationManager { ["Secrets:material"] = "XREG_TEST_REFERENCE" };
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"),
                _ => material);
            using ISecret? secret = store.TryGet(new SecretIdentifier("material", "Environment"));
            Assert.That(secret, Is.Not.Null);
            var identifier = new SecretIdentifier("operator-profile", "TestVault");
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            registry.Setup(value => value.GetAsync(identifier, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>(secret));
            var inner = new Mock<HttpMessageHandler>();
            using var invoker = new HttpMessageInvoker(new XRegistryBearerHandler(
                registry.Object, identifier, new Uri("https://registry.example/root/"), inner.Object));
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://registry.example/root/");

            await Assert.ThatAsync(async () =>
            {
                using HttpResponseMessage response = await invoker.SendAsync(request, CancellationToken.None)
                    .ConfigureAwait(false);
            }, Throws.TypeOf<UnauthorizedAccessException>().With.Message.Not.Contains(material)
                .And.Message.Not.Contains("fixture-private-prefix"))
                .ConfigureAwait(false);

            Assert.That(() => secret!.Bytes.Length, Throws.TypeOf<ObjectDisposedException>());
            registry.Verify(value => value.GetAsync(identifier, CancellationToken.None), Times.Once);
            registry.VerifyNoOtherCalls();
            inner.Protected().Verify("SendAsync", Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        }

        [TestCase(65536, true)]
        [TestCase(65537, false)]
        public async Task CredentialByteLimitIncludesBoundaryAndRejectsAdjacentValueAsync(int length, bool allowed)
        {
            using var configuration = new ConfigurationManager { ["Secrets:material"] = "XREG_TEST_REFERENCE" };
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"),
                _ => new string('a', length));
            using ISecret? secret = store.TryGet(new SecretIdentifier("material", "Environment"));
            Assert.That(secret, Is.Not.Null);
            var identifier = new SecretIdentifier("operator-profile", "TestVault");
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            registry.Setup(value => value.GetAsync(identifier, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>(secret));
            var inner = new Mock<HttpMessageHandler>();
            using var expectedResponse = new HttpResponseMessage(HttpStatusCode.NoContent);
            inner.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(expectedResponse);
            using var invoker = new HttpMessageInvoker(new XRegistryBearerHandler(
                registry.Object, identifier, new Uri("https://registry.example/root/"), inner.Object));
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://registry.example/root/");

            if (allowed)
            {
                HttpResponseMessage response = await invoker.SendAsync(request, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(response, Is.SameAs(expectedResponse));
                Assert.That(request.Headers.Authorization?.Scheme, Is.EqualTo("Bearer"));
                Assert.That(request.Headers.Authorization?.Parameter, Has.Length.EqualTo(65536));
            }
            else
            {
                await Assert.ThatAsync(async () =>
                {
                    using HttpResponseMessage response = await invoker.SendAsync(request, CancellationToken.None)
                        .ConfigureAwait(false);
                }, Throws.TypeOf<UnauthorizedAccessException>().With.Message.Not.Contains("aaaa"))
                    .ConfigureAwait(false);
            }

            Assert.That(() => secret!.Bytes.Length, Throws.TypeOf<ObjectDisposedException>());
            registry.Verify(value => value.GetAsync(identifier, CancellationToken.None), Times.Once);
            registry.VerifyNoOtherCalls();
            inner.Protected().Verify("SendAsync", allowed ? Times.Once() : Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        }

        [TestCase("/root")]
        [TestCase("http://registry.example/root/")]
        [TestCase("ftp://registry.example/root/")]
        [TestCase("https://operator@registry.example/root/")]
        [TestCase("https://registry.example/root/?query=1")]
        [TestCase("https://registry.example/root/#fragment")]
        public void ConstructorRequiresAnAbsoluteHttpsRoot(string root)
        {
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            using HttpMessageHandler inner = new Mock<HttpMessageHandler>().Object;

            Assert.That(() => new XRegistryBearerHandler(registry.Object,
                new SecretIdentifier("operator-profile", "TestVault"),
                new Uri(root, UriKind.RelativeOrAbsolute), inner),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("root"));
            registry.VerifyNoOtherCalls();
        }

        [Test]
        public void ConstructorRejectsMissingRegistryIdentifierAndRoot()
        {
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            var identifier = new SecretIdentifier("operator-profile", "TestVault");
            var root = new Uri("https://registry.example/root/");
            using HttpMessageHandler inner = new Mock<HttpMessageHandler>().Object;

            Assert.That(() => new XRegistryBearerHandler(null!, identifier, root, inner),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("secrets"));
            Assert.That(() => new XRegistryBearerHandler(registry.Object, null!, root, inner),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("secretId"));
            Assert.That(() => new XRegistryBearerHandler(registry.Object, identifier, null!, inner),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("root"));
            registry.VerifyNoOtherCalls();
        }

        private const string k_material = "fixture.auth_material~09+AZ/==";
    }
}
#endif
