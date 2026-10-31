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
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using Opc.Ua.XRegistry.Connector;
using HttpStatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace Opc.Ua.Tools.Tests.XRegistryConnector
{
    [TestFixture]
    [Category("XRegistryConnector")]
    public sealed class XRegistryConnectorAuthenticationTests
    {
        [TestCase("GET", true, true)]
        [TestCase("HEAD", true, true)]
        [TestCase("OPTIONS", true, true)]
        [TestCase("POST", true, false)]
        [TestCase("PUT", true, false)]
        [TestCase("PATCH", true, false)]
        [TestCase("DELETE", true, false)]
        [TestCase("TRACE", true, false)]
        [TestCase("CONNECT", true, false)]
        [TestCase("CUSTOM", true, false)]
        [TestCase("GET", false, false)]
        [TestCase("HEAD", false, false)]
        [TestCase("OPTIONS", false, false)]
        public async Task AnonymousAccessIsRestrictedToEnabledReadMethodsWithoutTrustingIdentityHeadersAsync(
            string method,
            bool allowAnonymous,
            bool allowed)
        {
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            var middleware = new XRegistryConnectorAuthentication(registry.Object, null, allowAnonymous);
            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Request.Method = method;
            context.Request.Headers["X-User"] = "header-attacker";
            context.Request.Headers["X-Roles"] = "admin,xregistry.write";
            context.Request.Headers["X-Api-Key"] = k_material;
            int nextCalls = 0;

            await middleware.InvokeAsync(context, nextContext =>
            {
                nextCalls++;
                Assert.That(nextContext, Is.SameAs(context));
                Assert.That(nextContext.User.Identity?.IsAuthenticated, Is.False);
                Assert.That(nextContext.User.IsInRole("xregistry.write"), Is.False);
                nextContext.Response.StatusCode = HttpStatusCodes.Status202Accepted;
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            Assert.That(nextCalls, Is.EqualTo(allowed ? 1 : 0));
            Assert.That(context.Response.StatusCode,
                Is.EqualTo(allowed ? HttpStatusCodes.Status202Accepted : HttpStatusCodes.Status401Unauthorized));
            Assert.That(context.User.Identity?.IsAuthenticated, Is.False);
            Assert.That(context.User.Claims, Is.Empty);
            Assert.That(context.User.IsInRole("admin"), Is.False);
            if (!allowed)
            {
                Assert.That(context.Response.Headers.WWWAuthenticate.ToString(), Does.StartWith("Bearer "));
            }
            registry.VerifyNoOtherCalls();
        }

        [TestCase("http")]
        [TestCase("https")]
        public async Task EmptyAuthorizationOnEnabledReadRemainsAnonymousWithoutCredentialLookupAsync(string scheme)
        {
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            var middleware = new XRegistryConnectorAuthentication(registry.Object,
                new SecretIdentifier("api-operator", "TestVault"), allowAnonymousReads: true);
            var context = new DefaultHttpContext();
            context.Request.Scheme = scheme;
            context.Request.Method = HttpMethods.Get;
            context.Request.Headers.Authorization = string.Empty;
            int nextCalls = 0;

            await middleware.InvokeAsync(context, nextContext =>
            {
                nextCalls++;
                nextContext.Response.StatusCode = HttpStatusCodes.Status202Accepted;
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            Assert.That(nextCalls, Is.EqualTo(1));
            Assert.That(context.Response.StatusCode, Is.EqualTo(HttpStatusCodes.Status202Accepted));
            Assert.That(context.User.Identity?.IsAuthenticated, Is.False);
            Assert.That(context.User.IsInRole("xregistry.write"), Is.False);
            Assert.That(context.Response.Headers.WWWAuthenticate.ToString(), Is.Empty);
            registry.VerifyNoOtherCalls();
        }

        [TestCase(" ")]
        [TestCase("Basic Zml4dHVyZQ==")]
        [TestCase("Digest fixture")]
        [TestCase("Bearer")]
        [TestCase("Bearer ")]
        [TestCase("Bearer fixture-private-prefix\r\nextra")]
        public async Task MalformedOrUnsupportedCredentialsAreRejectedBeforeLookupOrNextAsync(string authorization)
        {
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            var middleware = new XRegistryConnectorAuthentication(registry.Object,
                new SecretIdentifier("api-operator", "TestVault", "vault/scope"), allowAnonymousReads: true);
            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Request.Method = HttpMethods.Get;
            context.Request.Headers.Authorization = authorization;
            using var body = new MemoryStream();
            context.Response.Body = body;
            int nextCalls = 0;

            await middleware.InvokeAsync(context, _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo(HttpStatusCodes.Status401Unauthorized));
            Assert.That(nextCalls, Is.Zero);
            Assert.That(context.User.Identity?.IsAuthenticated, Is.False);
            Assert.That(context.User.IsInRole("xregistry.write"), Is.False);
            string challenge = context.Response.Headers.WWWAuthenticate.ToString();
            Assert.That(challenge, Does.StartWith("Bearer "));
            Assert.That(challenge, Does.Not.Contain("fixture-private-prefix"));
            Assert.That(Encoding.UTF8.GetString(body.ToArray()), Does.Not.Contain("fixture-private-prefix"));
            registry.VerifyNoOtherCalls();
        }

        [Test]
        public async Task UnconfiguredBearerCredentialsCannotFallBackToAnonymousOrReachNextAsync()
        {
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            var middleware = new XRegistryConnectorAuthentication(registry.Object, null, allowAnonymousReads: true);
            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Request.Method = HttpMethods.Get;
            context.Request.Headers.Authorization = "Bearer " + k_material;
            using var body = new MemoryStream();
            context.Response.Body = body;
            int nextCalls = 0;

            await middleware.InvokeAsync(context, _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo(HttpStatusCodes.Status401Unauthorized));
            Assert.That(nextCalls, Is.Zero);
            Assert.That(context.User.Identity?.IsAuthenticated, Is.False);
            Assert.That(context.Response.Headers.WWWAuthenticate.ToString(), Does.Not.Contain(k_material));
            Assert.That(Encoding.UTF8.GetString(body.ToArray()), Does.Not.Contain(k_material));
            registry.VerifyNoOtherCalls();
        }

        [TestCase("GET", "Bearer")]
        [TestCase("PATCH", "Bearer")]
        [TestCase("DELETE", "bEaReR")]
        public async Task ConfiguredHttpsCredentialCreatesOnlyLogicalIdentityAndWriteRoleAsync(
            string method,
            string scheme)
        {
            using var configuration = new ConfigurationManager { ["Secrets:material"] = "XREG_TEST_REFERENCE" };
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), _ => k_material);
            using ISecret? secret = store.TryGet(new SecretIdentifier("material", "Environment"));
            Assert.That(secret, Is.Not.Null);
            var identifier = new SecretIdentifier("api-operator", "TestVault", "vault/scope");
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            registry.Setup(value => value.GetAsync(identifier, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>(secret));
            var middleware = new XRegistryConnectorAuthentication(
                registry.Object, identifier, allowAnonymousReads: false);
            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Request.Method = method;
            context.Request.Headers.Authorization = scheme + " " + k_material;
            context.Request.Headers["X-User"] = "header-attacker";
            context.Request.Headers["X-Roles"] = "admin";
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "prior-user"), new Claim(ClaimTypes.Role, "admin")], "prior-identity"));
            using var cancellation = new CancellationTokenSource();
            context.RequestAborted = cancellation.Token;
            int nextCalls = 0;

            await middleware.InvokeAsync(context, nextContext =>
            {
                nextCalls++;
                Assert.That(nextContext.User.Identity?.IsAuthenticated, Is.True);
                Assert.That(nextContext.User.IsInRole("xregistry.write"), Is.True);
                Assert.That(nextContext.User.IsInRole("admin"), Is.False);
                nextContext.Response.StatusCode = HttpStatusCodes.Status204NoContent;
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            Assert.That(nextCalls, Is.EqualTo(1));
            Assert.That(context.Response.StatusCode, Is.EqualTo(HttpStatusCodes.Status204NoContent));
            Assert.That(context.User.Identity?.AuthenticationType, Is.EqualTo("xregistry-secret"));
            Assert.That(context.User.Identity?.Name, Is.EqualTo("api-operator"));
            Assert.That(context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, Is.EqualTo("api-operator"));
            Assert.That(context.User.FindAll(ClaimTypes.Role).Count(), Is.EqualTo(1));
            Assert.That(context.User.FindFirst(ClaimTypes.Role)?.Value, Is.EqualTo("xregistry.write"));
            Assert.That(context.User.Claims.Count(), Is.EqualTo(3));
            Assert.That(context.User.Claims.Select(claim => claim.Value), Does.Not.Contain(k_material));
            Assert.That(context.Response.Headers.WWWAuthenticate.ToString(), Is.Empty);
            Assert.That(() => secret!.Bytes.Length, Throws.TypeOf<ObjectDisposedException>());
            registry.Verify(value => value.GetAsync(identifier, cancellation.Token), Times.Once);
            registry.VerifyNoOtherCalls();
        }

        [Test]
        public async Task HttpCredentialsAreRejectedEvenWhenForwardedProtoClaimsHttpsAsync()
        {
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            var middleware = new XRegistryConnectorAuthentication(registry.Object,
                new SecretIdentifier("api-operator", "TestVault"), allowAnonymousReads: true);
            var context = new DefaultHttpContext();
            context.Request.Scheme = "http";
            context.Request.Method = HttpMethods.Get;
            context.Request.Headers.Authorization = "Bearer " + k_material;
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            int nextCalls = 0;

            await middleware.InvokeAsync(context, _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo(HttpStatusCodes.Status401Unauthorized));
            Assert.That(nextCalls, Is.Zero);
            Assert.That(context.User.Identity?.IsAuthenticated, Is.False);
            Assert.That(context.User.IsInRole("xregistry.write"), Is.False);
            Assert.That(context.Response.Headers.WWWAuthenticate.ToString(), Does.Not.Contain(k_material));
            registry.VerifyNoOtherCalls();
        }

        [TestCase("Xixture-auth-material")]
        [TestCase("fixture-auth-materiaX")]
        [TestCase("fixture-auth-material-extra")]
        [TestCase("short")]
        public async Task MismatchedCredentialReturns401WithoutForwardingOrEchoingMaterialAsync(string supplied)
        {
            using var configuration = new ConfigurationManager { ["Secrets:material"] = "XREG_TEST_REFERENCE" };
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), _ => k_material);
            using ISecret? secret = store.TryGet(new SecretIdentifier("material", "Environment"));
            Assert.That(secret, Is.Not.Null);
            var identifier = new SecretIdentifier("api-operator", "TestVault", "vault/scope");
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            registry.Setup(value => value.GetAsync(identifier, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>(secret));
            var middleware = new XRegistryConnectorAuthentication(
                registry.Object, identifier, allowAnonymousReads: true);
            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Request.Method = HttpMethods.Get;
            context.Request.Headers.Authorization = "Bearer " + supplied;
            using var body = new MemoryStream();
            context.Response.Body = body;
            int nextCalls = 0;

            await middleware.InvokeAsync(context, _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo(HttpStatusCodes.Status401Unauthorized));
            Assert.That(nextCalls, Is.Zero);
            Assert.That(context.User.Identity?.IsAuthenticated, Is.False);
            Assert.That(context.User.IsInRole("xregistry.write"), Is.False);
            Assert.That(context.Response.Headers.WWWAuthenticate.ToString(), Does.StartWith("Bearer "));
            Assert.That(context.Response.Headers.WWWAuthenticate.ToString(), Does.Not.Contain(supplied));
            Assert.That(context.Response.Headers.WWWAuthenticate.ToString(), Does.Not.Contain(k_material));
            Assert.That(Encoding.UTF8.GetString(body.ToArray()), Does.Not.Contain(supplied));
            Assert.That(Encoding.UTF8.GetString(body.ToArray()), Does.Not.Contain(k_material));
            Assert.That(() => secret!.Bytes.Length, Throws.TypeOf<ObjectDisposedException>());
            registry.Verify(value => value.GetAsync(identifier, CancellationToken.None), Times.Once);
            registry.VerifyNoOtherCalls();
        }

        [Test]
        public async Task MissingConfiguredSecretReturns401WithoutReachingNextAsync()
        {
            var identifier = new SecretIdentifier("api-operator", "TestVault", "vault/scope");
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            registry.Setup(value => value.GetAsync(identifier, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>((ISecret?)null));
            var middleware = new XRegistryConnectorAuthentication(
                registry.Object, identifier, allowAnonymousReads: true);
            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Request.Method = HttpMethods.Get;
            context.Request.Headers.Authorization = "Bearer " + k_material;
            int nextCalls = 0;

            await middleware.InvokeAsync(context, _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo(HttpStatusCodes.Status401Unauthorized));
            Assert.That(nextCalls, Is.Zero);
            Assert.That(context.User.Identity?.IsAuthenticated, Is.False);
            Assert.That(context.Response.Headers.WWWAuthenticate.ToString(), Does.Not.Contain(k_material));
            registry.Verify(value => value.GetAsync(identifier, CancellationToken.None), Times.Once);
            registry.VerifyNoOtherCalls();
        }

        [Test]
        public async Task AbortedRequestCancelsRegistryLookupWithoutMaterializingOrForwardingAsync()
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
            var middleware = new XRegistryConnectorAuthentication(
                registry.Object, identifier, allowAnonymousReads: false);
            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Request.Method = HttpMethods.Patch;
            context.Request.Headers.Authorization = "Bearer " + k_material;
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            context.RequestAborted = cancellation.Token;
            int nextCalls = 0;

            await Assert.ThatAsync(() => middleware.InvokeAsync(context, _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            }), Throws.InstanceOf<OperationCanceledException>()
                .With.Property("CancellationToken").EqualTo(cancellation.Token)).ConfigureAwait(false);

            Assert.That(reads, Is.Zero);
            Assert.That(nextCalls, Is.Zero);
            Assert.That(context.User.Identity?.IsAuthenticated, Is.False);
            Assert.That(context.Response.StatusCode, Is.EqualTo(HttpStatusCodes.Status200OK));
            Assert.That(context.Response.Headers.WWWAuthenticate.ToString(), Is.Empty);
            registry.Verify(value => value.GetAsync(identifier, cancellation.Token), Times.Once);
            registry.VerifyNoOtherCalls();
        }

        [Test]
        public async Task NextDelegateFailurePropagatesAfterAuthenticationAndSecretDisposalAsync()
        {
            using var configuration = new ConfigurationManager { ["Secrets:material"] = "XREG_TEST_REFERENCE" };
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), _ => k_material);
            using ISecret? secret = store.TryGet(new SecretIdentifier("material", "Environment"));
            Assert.That(secret, Is.Not.Null);
            var identifier = new SecretIdentifier("api-operator", "TestVault");
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            registry.Setup(value => value.GetAsync(identifier, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>(secret));
            var middleware = new XRegistryConnectorAuthentication(
                registry.Object, identifier, allowAnonymousReads: false);
            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Request.Method = HttpMethods.Post;
            context.Request.Headers.Authorization = "Bearer " + k_material;
            var failure = new IOException("Downstream failed.");
            int nextCalls = 0;

            await Assert.ThatAsync(() => middleware.InvokeAsync(context, _ =>
            {
                nextCalls++;
                return Task.FromException(failure);
            }), Throws.Exception.SameAs(failure)).ConfigureAwait(false);

            Assert.That(nextCalls, Is.EqualTo(1));
            Assert.That(context.User.Identity?.IsAuthenticated, Is.True);
            Assert.That(context.User.IsInRole("xregistry.write"), Is.True);
            Assert.That(context.Response.Headers.WWWAuthenticate.ToString(), Is.Empty);
            Assert.That(() => secret!.Bytes.Length, Throws.TypeOf<ObjectDisposedException>());
            registry.Verify(value => value.GetAsync(identifier, CancellationToken.None), Times.Once);
            registry.VerifyNoOtherCalls();
        }

        [Test]
        public async Task AnonymousNextCancellationIsNotConvertedToUnauthorizedAsync()
        {
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            var middleware = new XRegistryConnectorAuthentication(registry.Object, null, allowAnonymousReads: true);
            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Request.Method = HttpMethods.Get;
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            context.RequestAborted = cancellation.Token;
            int nextCalls = 0;

            await Assert.ThatAsync(() => middleware.InvokeAsync(context, nextContext =>
            {
                nextCalls++;
                return Task.FromCanceled(nextContext.RequestAborted);
            }), Throws.InstanceOf<OperationCanceledException>()
                .With.Property("CancellationToken").EqualTo(cancellation.Token)).ConfigureAwait(false);

            Assert.That(nextCalls, Is.EqualTo(1));
            Assert.That(context.User.Identity?.IsAuthenticated, Is.False);
            Assert.That(context.Response.StatusCode, Is.EqualTo(HttpStatusCodes.Status200OK));
            Assert.That(context.Response.Headers.WWWAuthenticate.ToString(), Is.Empty);
            registry.VerifyNoOtherCalls();
        }

        private const string k_material = "fixture-auth-material";
    }
}
#endif
