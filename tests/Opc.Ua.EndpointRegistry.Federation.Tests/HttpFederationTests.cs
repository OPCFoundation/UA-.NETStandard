/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry;
using static Opc.Ua.EndpointRegistry.Federation.Tests.FederationTestSupport;

namespace Opc.Ua.EndpointRegistry.Federation.Tests
{
    [TestFixture]
    [Category("EndpointRegistry")]
    public sealed class HttpFederationTests
    {
        private const string MetadataUrl = "https://localhost/catalog/messages/m";
        private const string ObservationUrl = "https://localhost/observations/messages/m";
        private const string Manifest =
            """{"OriginUri":"urn:test:one","Xid":"/messagegroups/g/messages/m","VersionId":"1","Epoch":3,"HasDocument":false,"MaxVersions":1}""";

        [Test]
        public async Task HttpPreloadPreservesExactMetadataAndResolverDoesNotFetch()
        {
            const string raw = """{"x-n":1.00,"x-negativezero":-0,"x-null":null,"x-object":{"values":[true,9007199254740993]}}""";
            RegistryEntityReferenceDataType source = Source(locator: MetadataUrl, ua: false);
            using var handler = new LoopbackHandler((request, _) => Task.FromResult(JsonResponse(request,
                request.RequestUri!.OriginalString == ObservationUrl ? Manifest : raw)));
            using var client = new HttpClient(handler);
            HttpFederationProvider provider = Provider(client, source);
            var empty = new FederationResolutionCache();
            FederationResolutionCache cache = await provider.PreloadAsync(empty, new FederationSourceKey(source))
                .ConfigureAwait(false);
            Assert.That(handler.Calls, Is.EqualTo(2));
            Assert.That(empty.Count, Is.Zero);

            NativeMessageResolutionResultDataType result = await ResolveAsync(cache, Request(source), source).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo("complete"));
                Assert.That(RegistryValues.Identical(Mapper.Restore(result.Definition), Json(raw)), Is.True);
                Assert.That(result.Sources[0].HasNativeTarget, Is.False);
                Assert.That(result.Sources[0].NativeTarget.IsNull, Is.True);
                Assert.That(handler.Calls, Is.EqualTo(2));
            });
            NativeMessageResolutionResultDataType missing = await ResolveAsync(cache,
                new MessageResolutionRequestDataType { Reference = "https://untrusted.example.test/message" }, source)
                .ConfigureAwait(false);
            Assert.That(missing.Status, Is.EqualTo("missing-inputs"));
            Assert.That(missing.Issues[0].Code, Is.EqualTo("E_REFERENCE_MISSING"));
            Assert.That(missing.Definition, Is.Null);
            Assert.That(handler.Calls, Is.EqualTo(2));
        }

        [TestCase("redirect")]
        [TestCase("authority")]
        [TestCase("origin")]
        [TestCase("max-versions-bool")]
        [TestCase("missing-observation")]
        [TestCase("document")]
        [TestCase("native-target")]
        public void HttpProviderRejectsUntrustedEvidenceAndRoutes(string mutation)
        {
            RegistryEntityReferenceDataType source = Source(locator: MetadataUrl, ua: false);
            using var handler = new LoopbackHandler((request, _) =>
            {
                HttpResponseMessage response = JsonResponse(request,
                    mutation == "origin" ? Manifest.Replace("urn:test:one", "urn:test:wrong", StringComparison.Ordinal)
                    : mutation == "max-versions-bool" ? Manifest.Replace("\"MaxVersions\":1", "\"MaxVersions\":true", StringComparison.Ordinal)
                    : mutation == "missing-observation" ? "{}"
                    : mutation == "document" ? Manifest.Replace("\"HasDocument\":false", "\"HasDocument\":true", StringComparison.Ordinal)
                    : Manifest);
                if (mutation == "redirect")
                {
                    response.StatusCode = HttpStatusCode.Redirect;
                    response.Headers.Location = new Uri("https://other.example.test/message");
                }
                if (mutation == "authority")
                {
                    response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://other.example.test/message");
                }
#pragma warning disable CA2025 // The handler transfers response ownership to HttpClient and completes synchronously.
                return Task.FromResult(response);
#pragma warning restore CA2025
            });
            using var client = new HttpClient(handler);
            if (mutation == "native-target")
            {
                source.HasNativeTarget = true;
                source.NativeTarget = Target;
                Assert.Throws<ArgumentException>(() => Provider(client, source));
                Assert.That(handler.Calls, Is.Zero);
                return;
            }
            HttpFederationProvider provider = Provider(client, source);
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await provider.PreloadAsync(new FederationResolutionCache(), new FederationSourceKey(source)).ConfigureAwait(false));
            Assert.That(handler.Calls, Is.EqualTo(1), "Rejected observation must precede metadata retrieval.");
        }

        [TestCase(1)]
        [TestCase(128)]
        public void HttpProviderEnforcesAdvertisedAndStreamingSizeBounds(int limit)
        {
            RegistryEntityReferenceDataType source = Source(locator: MetadataUrl, ua: false);
            using var handler = new LoopbackHandler((request, _) => Task.FromResult(JsonResponse(request,
                new string(' ', 1000) + Manifest)));
            using var client = new HttpClient(handler);
            HttpFederationProvider provider = Provider(client, source, maxBytes: limit);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.PreloadAsync(new FederationResolutionCache(), new FederationSourceKey(source)).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(handler.Calls, Is.EqualTo(1));
        }

        [Test]
        public void HttpProviderHonorsCancellationAndTimeout()
        {
            RegistryEntityReferenceDataType source = Source(locator: MetadataUrl, ua: false);
            using var handler = new LoopbackHandler(async (request, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                return JsonResponse(request, Manifest);
            });
            using var client = new HttpClient(handler);
            HttpFederationProvider provider = Provider(client, source, timeout: TimeSpan.FromMilliseconds(20));
            Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await provider.PreloadAsync(new FederationResolutionCache(), new FederationSourceKey(source)).ConfigureAwait(false));
            Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await provider.PreloadAsync(new FederationResolutionCache(), new FederationSourceKey(source),
                    new CancellationToken(true)).ConfigureAwait(false));
            Assert.That(handler.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task ExplicitSchemaAdapterReceivesSelectedOriginWithoutInferredFragmentAssociation()
        {
            RegistryEntityReferenceDataType source = Source(locator: MetadataUrl, ua: false);
            var schema = new RecordingSchema();
            FederationResolutionCache cache = new FederationResolutionCache(schema).WithObservation(
                Binding(source, application: string.Empty), Evidence(source, application: string.Empty),
                Observation(source,
                    """
                    {"dataschemauri":"https://schemas.example.test/s#Metrics","dataschemaxid":"/schemagroups/g/schemas/s",
                     "dataschemaformat":"JsonSchema/2020-12"}
                    """));
            MessageResolutionRequestDataType request = Request(source);
            request.CheckSchema = true;
            NativeMessageResolutionResultDataType result = await ResolveAsync(cache, request, source).ConfigureAwait(false);
            Assert.That(result.Status, Is.EqualTo("complete"));
            Assert.That(schema.SelectedOrigin!.OriginUri, Is.EqualTo(source.OriginUri));
            Assert.That(schema.SelectedMessage!.DataSchemaUri, Is.EqualTo("https://schemas.example.test/s#Metrics"));
            Assert.That(schema.SelectedMessage.DataSchemaXid, Is.EqualTo("/schemagroups/g/schemas/s"));
            Assert.That(result.Schema, Is.Not.Null);
            RegistryEntityReferenceDataType other = Source("urn:test:other", locator: MetadataUrl, ua: false);
            request.References[0].Target = other;
            NativeMessageResolutionResultDataType wrong = await ResolveAsync(cache, request, source).ConfigureAwait(false);
            Assert.That(wrong.Issues[0].Code, Is.EqualTo("E_REFERENCE_ORIGIN"));
            Assert.That(schema.Calls, Is.EqualTo(1));
        }

        private static HttpFederationProvider Provider(HttpClient client, RegistryEntityReferenceDataType source,
            int maxBytes = 4096, TimeSpan? timeout = null)
        {
            return new HttpFederationProvider(client,
                Binding(source, application: string.Empty, locators: [MetadataUrl, ObservationUrl]),
                [new HttpFederationRoute(source, ObservationUrl)], NUnitTelemetryContext.Create(), maxBytes, timeout);
        }

        private static ValueTask<NativeMessageResolutionResultDataType> ResolveAsync(
            FederationResolutionCache cache, MessageResolutionRequestDataType request, RegistryEntityReferenceDataType origin) =>
            new EndpointRegistryMessageResolver().ResolveAsync(request,
                new EndpointRegistryMessageResolutionContext { LocalOrigin = origin, Mapper = Mapper, Provider = cache });

        private static HttpResponseMessage JsonResponse(HttpRequestMessage request, string json) => new(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        private sealed class LoopbackHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
        {
            public int Calls { get; private set; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                return respond(request, cancellationToken);
            }
        }

        private sealed class RecordingSchema : IFederationSchemaProvider
        {
            public RegistryEntityReferenceDataType? SelectedOrigin { get; private set; }
            public MessageDefinitionDataType? SelectedMessage { get; private set; }
            public int Calls { get; private set; }
            public ValueTask<SchemaDocumentDataType?> ResolveAsync(
                MessageDefinitionDataType definition, RegistryEntityReferenceDataType messageOrigin, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls++;
                SelectedOrigin = messageOrigin;
                SelectedMessage = definition;
                return new ValueTask<SchemaDocumentDataType?>(new SchemaDocumentDataType { Epoch = 1 });
            }
        }
    }
}
