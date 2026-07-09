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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

// CA2000: the HttpClient, message handler and response messages are owned by
// the system-under-test (or the test itself) and released deterministically
// per test; there is no cross-test resource leak. Suppressed file-level.
#pragma warning disable CA2000

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Kubernetes.Tests
{
    /// <summary>
    /// Unit tests for the in-cluster Kubernetes API HTTP client. All HTTP traffic is served by a
    /// <see cref="StubHandler"/> so the request construction and response parsing paths are exercised
    /// without a real cluster.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class KubernetesHttpApiClientTests
    {
        private static readonly Uri s_baseAddress = new("https://k8s.test/");

        [Test]
        public async Task GetLeaseAsyncReturnsDeserializedLeaseOnSuccessAsync()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, Serialize(NewLease("pod-a"))));
            using KubernetesHttpApiClient api = NewClient(handler);

            KubernetesLease? lease = await api.GetLeaseAsync("ns", "opcua", CancellationToken.None).ConfigureAwait(false);

            Assert.That(lease, Is.Not.Null);
            Assert.That(lease!.Spec.HolderIdentity, Is.EqualTo("pod-a"));
            Assert.That(handler.LastMethod, Is.EqualTo(HttpMethod.Get));
            Assert.That(
                handler.LastRequestUri!.PathAndQuery,
                Is.EqualTo("/apis/coordination.k8s.io/v1/namespaces/ns/leases/opcua"));
        }

        [Test]
        public async Task GetLeaseAsyncReturnsNullWhenNotFoundAsync()
        {
            var handler = new StubHandler(_ => TextResponse(HttpStatusCode.NotFound, string.Empty));
            using KubernetesHttpApiClient api = NewClient(handler);

            KubernetesLease? lease = await api.GetLeaseAsync("ns", "opcua", CancellationToken.None).ConfigureAwait(false);

            Assert.That(lease, Is.Null);
        }

        [Test]
        public void GetLeaseThrowsOnServerError()
        {
            var handler = new StubHandler(_ => TextResponse(HttpStatusCode.InternalServerError, "boom"));
            using KubernetesHttpApiClient api = NewClient(handler);

            Assert.That(
                async () => await api.GetLeaseAsync("ns", "opcua", CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<HttpRequestException>()
                    .With.Property(nameof(HttpRequestException.StatusCode)).EqualTo(HttpStatusCode.InternalServerError)
                    .And.Message.EqualTo("boom"));
        }

        [Test]
        public async Task CreateLeaseAsyncPostsSerializedLeaseAsync()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.Created, Serialize(NewLease("pod-a"))));
            using KubernetesHttpApiClient api = NewClient(handler);

            KubernetesLease result = await api.CreateLeaseAsync("ns", NewLease("pod-a"), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Spec.HolderIdentity, Is.EqualTo("pod-a"));
            Assert.That(handler.LastMethod, Is.EqualTo(HttpMethod.Post));
            Assert.That(
                handler.LastRequestUri!.PathAndQuery,
                Is.EqualTo("/apis/coordination.k8s.io/v1/namespaces/ns/leases"));
            Assert.That(handler.LastContentType, Is.EqualTo("application/json"));
            Assert.That(handler.LastRequestBody, Does.Contain("\"holderIdentity\":\"pod-a\""));
        }

        [Test]
        public void CreateLeaseThrowsHttpRequestExceptionOnConflict()
        {
            var handler = new StubHandler(_ => TextResponse(HttpStatusCode.Conflict, "already held"));
            using KubernetesHttpApiClient api = NewClient(handler);

            Assert.That(
                async () => await api.CreateLeaseAsync("ns", NewLease("pod-a"), CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<HttpRequestException>()
                    .With.Property(nameof(HttpRequestException.StatusCode)).EqualTo(HttpStatusCode.Conflict)
                    .And.Message.EqualTo("already held"));
        }

        [Test]
        public void CreateLeaseThrowsWhenBodyIsEmpty()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "null"));
            using KubernetesHttpApiClient api = NewClient(handler);

            Assert.That(
                async () => await api.CreateLeaseAsync("ns", NewLease("pod-a"), CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<HttpRequestException>().With.Message.Contains("empty body"));
        }

        [Test]
        public async Task ReplaceLeaseAsyncPutsSerializedLeaseAsync()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, Serialize(NewLease("pod-a"))));
            using KubernetesHttpApiClient api = NewClient(handler);

            KubernetesLease result = await api.ReplaceLeaseAsync("ns", "opcua", NewLease("pod-a"), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Spec.HolderIdentity, Is.EqualTo("pod-a"));
            Assert.That(handler.LastMethod, Is.EqualTo(HttpMethod.Put));
            Assert.That(
                handler.LastRequestUri!.PathAndQuery,
                Is.EqualTo("/apis/coordination.k8s.io/v1/namespaces/ns/leases/opcua"));
        }

        [Test]
        public async Task DeleteLeaseAsyncSendsDeleteRequestAsync()
        {
            var handler = new StubHandler(_ => TextResponse(HttpStatusCode.OK, string.Empty));
            using KubernetesHttpApiClient api = NewClient(handler);

            await api.DeleteLeaseAsync("ns", "opcua", CancellationToken.None).ConfigureAwait(false);

            Assert.That(handler.LastMethod, Is.EqualTo(HttpMethod.Delete));
            Assert.That(
                handler.LastRequestUri!.PathAndQuery,
                Is.EqualTo("/apis/coordination.k8s.io/v1/namespaces/ns/leases/opcua"));
        }

        [Test]
        public async Task DeleteLeaseAsyncIgnoresNotFoundAsync()
        {
            var handler = new StubHandler(_ => TextResponse(HttpStatusCode.NotFound, string.Empty));
            using KubernetesHttpApiClient api = NewClient(handler);

            await api.DeleteLeaseAsync("ns", "opcua", CancellationToken.None).ConfigureAwait(false);

            Assert.That(handler.LastMethod, Is.EqualTo(HttpMethod.Delete));
        }

        [Test]
        public void DeleteLeaseThrowsOnErrorStatus()
        {
            var handler = new StubHandler(_ => TextResponse(HttpStatusCode.InternalServerError, "boom"));
            using KubernetesHttpApiClient api = NewClient(handler);

            Assert.That(
                async () => await api.DeleteLeaseAsync("ns", "opcua", CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<HttpRequestException>()
                    .With.Property(nameof(HttpRequestException.StatusCode)).EqualTo(HttpStatusCode.InternalServerError));
        }

        [Test]
        public async Task ListEndpointSlicesAsyncReturnsListAndBuildsLabelSelectorAsync()
        {
            var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, Serialize(NewSliceList())));
            using KubernetesHttpApiClient api = NewClient(handler);

            KubernetesEndpointSliceList result = await api.ListEndpointSlicesAsync("ns", "svc", CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Items, Has.Count.EqualTo(1));
            Assert.That(handler.LastMethod, Is.EqualTo(HttpMethod.Get));
            Assert.That(
                handler.LastRequestUri!.PathAndQuery,
                Is.EqualTo("/apis/discovery.k8s.io/v1/namespaces/ns/endpointslices" +
                    "?labelSelector=kubernetes.io%2Fservice-name%3Dsvc"));
        }

        [Test]
        public async Task TokenFileIsReReadBetweenRequestsAsync()
        {
            string tokenPath = WriteTestFile("token-a");
            try
            {
                var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, Serialize(NewLease("pod-a"))));
                using KubernetesHttpApiClient api = NewClient(handler, tokenPath: tokenPath);

                await api.GetLeaseAsync("ns", "opcua", CancellationToken.None).ConfigureAwait(false);
                Assert.That(handler.LastAuthorizationScheme, Is.EqualTo("Bearer"));
                Assert.That(handler.LastAuthorizationParameter, Is.EqualTo("token-a"));

                File.WriteAllText(tokenPath, "token-b");

                await api.GetLeaseAsync("ns", "opcua", CancellationToken.None).ConfigureAwait(false);
                Assert.That(handler.LastAuthorizationScheme, Is.EqualTo("Bearer"));
                Assert.That(handler.LastAuthorizationParameter, Is.EqualTo("token-b"));
            }
            finally
            {
                if (File.Exists(tokenPath))
                {
                    File.Delete(tokenPath);
                }
            }
        }

        [Test]
        public void ConstructorRejectsNullHttpClient()
        {
            Assert.That(
                () => new KubernetesHttpApiClient(null!, "ns"),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorRejectsNullNamespace()
        {
            using var httpClient = new HttpClient { BaseAddress = s_baseAddress };

            Assert.That(
                () => new KubernetesHttpApiClient(httpClient, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void IsInClusterIsTrueAndNamespaceExposed()
        {
            var handler = new StubHandler(_ => TextResponse(HttpStatusCode.OK, string.Empty));
            using KubernetesHttpApiClient api = NewClient(handler, "kube-system");

            Assert.That(api.IsInCluster, Is.True);
            Assert.That(api.DefaultNamespace, Is.EqualTo("kube-system"));
        }

        [Test]
        public void DisposeDisposesOwnedHttpClient()
        {
            var handler = new StubHandler(_ => TextResponse(HttpStatusCode.OK, string.Empty));
            var httpClient = new HttpClient(handler) { BaseAddress = s_baseAddress };
            var api = new KubernetesHttpApiClient(httpClient, "ns", ownsClient: true);

            api.Dispose();

            Assert.That(handler.Disposed, Is.True);
        }

        [Test]
        public void DisposeLeavesBorrowedHttpClientAlone()
        {
            var handler = new StubHandler(_ => TextResponse(HttpStatusCode.OK, string.Empty));
            using var httpClient = new HttpClient(handler) { BaseAddress = s_baseAddress };
            var api = new KubernetesHttpApiClient(httpClient, "ns", ownsClient: false);

            api.Dispose();

            Assert.That(handler.Disposed, Is.False);
        }

        [Test]
        public void PublicConstructorWithoutCaFileBuildsClient()
        {
            using var api = new KubernetesHttpApiClient(
                "k8s.test",
                6443,
                "ns",
                "token",
                Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".crt"));

            Assert.That(api.IsInCluster, Is.True);
            Assert.That(api.DefaultNamespace, Is.EqualTo("ns"));
        }

        [Test]
        public void PublicConstructorWithCaFileConfiguresValidation()
        {
            string caPath = Path.Combine(Path.GetTempPath(), "k8s-ca-" + Guid.NewGuid().ToString("N") + ".crt");
            File.WriteAllBytes(caPath, CreateCertificateAuthorityDer());
            try
            {
                using var api = new KubernetesHttpApiClient("k8s.test", 6443, "ns", "token", caPath);

                Assert.That(api.DefaultNamespace, Is.EqualTo("ns"));
            }
            finally
            {
                File.Delete(caPath);
            }
        }

        private static KubernetesHttpApiClient NewClient(
            StubHandler handler,
            string namespaceName = "ns",
            string? tokenPath = null,
            string token = "token")
        {
            var httpClient = new HttpClient(handler) { BaseAddress = s_baseAddress };
            return new KubernetesHttpApiClient(
                httpClient,
                namespaceName,
                ownsClient: true,
                token: token,
                tokenPath: tokenPath);
        }

        private static KubernetesLease NewLease(string holder)
        {
            return new KubernetesLease
            {
                Metadata = new KubernetesObjectMetadata
                {
                    Name = "opcua",
                    Namespace = "ns",
                    Labels = new Dictionary<string, string> { ["app"] = "opcua" }
                },
                Spec = new KubernetesLeaseSpec
                {
                    HolderIdentity = holder,
                    LeaseDurationSeconds = 30
                }
            };
        }

        private static KubernetesEndpointSliceList NewSliceList()
        {
            return new KubernetesEndpointSliceList
            {
                Items =
                [
                    new KubernetesEndpointSlice
                    {
                        Endpoints = [new KubernetesEndpoint { Addresses = ["10.0.0.2"] }],
                        Ports = [new KubernetesEndpointPort { Name = "opcua-tcp", Port = 4840 }]
                    }
                ]
            };
        }

        private static string Serialize(KubernetesLease lease)
        {
            return JsonSerializer.Serialize(lease, KubernetesJsonContext.Default.KubernetesLease);
        }

        private static string Serialize(KubernetesEndpointSliceList list)
        {
            return JsonSerializer.Serialize(list, KubernetesJsonContext.Default.KubernetesEndpointSliceList);
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        private static HttpResponseMessage TextResponse(HttpStatusCode status, string body)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }

        private static byte[] CreateCertificateAuthorityDer()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=Test Kubernetes CA",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, 0, true));
            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(1));
            return certificate.Export(X509ContentType.Cert);
        }

        private static string WriteTestFile(string content)
        {
            string path = Path.Combine(
                AppContext.BaseDirectory,
                "k8s-http-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, content);
            return path;
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                m_responder = responder;
            }

            public HttpMethod? LastMethod { get; private set; }

            public Uri? LastRequestUri { get; private set; }

            public string? LastRequestBody { get; private set; }

            public string? LastContentType { get; private set; }

            public string? LastAuthorizationScheme { get; private set; }

            public string? LastAuthorizationParameter { get; private set; }

            public bool Disposed { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                LastMethod = request.Method;
                LastRequestUri = request.RequestUri;
                LastContentType = request.Content?.Headers.ContentType?.MediaType;
                LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
                LastAuthorizationParameter = request.Headers.Authorization?.Parameter;
                if (request.Content != null)
                {
                    LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }
                return m_responder(request);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Disposed = true;
                }
                base.Dispose(disposing);
            }

            private readonly Func<HttpRequestMessage, HttpResponseMessage> m_responder;
        }
    }
}
