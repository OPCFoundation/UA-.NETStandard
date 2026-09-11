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

using System;
using System.Linq;
#if NET8_0_OR_GREATER
using System.Net;
#endif
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http.Tests
{
    [TestFixture]
    [Category("XRegistryHttp")]
    public sealed class XRegistryHttpServiceCollectionExtensionsTests
    {
        [TestCase("xregistry")]
        [TestCase("isolated-registry")]
        public void RegistrationBuildsSafeNamedHandler(string name)
        {
            var services = new ServiceCollection();

            IHttpClientBuilder builder = name == "xregistry"
                ? services.AddXRegistryHttpEndpoint(HttpTestData.RegistryRoot)
                : services.AddXRegistryHttpEndpoint(HttpTestData.RegistryRoot, clientName: name);
            using ServiceProvider provider = services.BuildServiceProvider();
            HttpMessageHandler handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(name);
            while (handler is DelegatingHandler delegating)
            {
                handler = delegating.InnerHandler!;
            }

            Assert.That(builder.Name, Is.EqualTo(name));
            Assert.That(builder.Services, Is.EqualTo(services));
            Assert.That(handler, Is.TypeOf<HttpClientHandler>());
            var primary = (HttpClientHandler)handler;
            Assert.That(primary.AllowAutoRedirect, Is.False);
            Assert.That(primary.UseCookies, Is.False);
            Assert.That(primary.UseDefaultCredentials, Is.False);
            Assert.That(primary.Credentials, Is.Null);
            Assert.That(services.Single(service => service.ServiceType == typeof(XRegistryHttpEndpoint)).Lifetime,
                Is.EqualTo(ServiceLifetime.Transient));
            Assert.That(services.Single(service => service.ServiceType == typeof(IXRegistryEndpoint)).Lifetime,
                Is.EqualTo(ServiceLifetime.Transient));
        }

        [Test]
        public async Task RegistrationResolvesConcreteAndInterfaceWithConfiguredRootAndProfile()
        {
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            var services = new ServiceCollection();
            services.AddXRegistryHttpEndpoint(HttpTestData.RegistryRoot, HttpTestData.Qualified, "golden")
                .ConfigurePrimaryHttpMessageHandler(() => handler);
            using ServiceProvider provider = services.BuildServiceProvider();

            XRegistryHttpEndpoint concrete = provider.GetRequiredService<XRegistryHttpEndpoint>();
            IXRegistryEndpoint endpoint = provider.GetRequiredService<IXRegistryEndpoint>();
            XRegistryEndpointDescription description = await endpoint.InspectAsync(XRegistryCallContext.Anonymous)
                .ConfigureAwait(false);

            Assert.That(endpoint, Is.TypeOf<XRegistryHttpEndpoint>());
            Assert.That(endpoint, Is.Not.SameAs(concrete));
            Assert.That(concrete.RegistryRoot.AbsoluteUri, Is.EqualTo("https://registry.example/registry/"));
            Assert.That(description.RegistryId, Is.EqualTo("golden-registry"));
            Assert.That(description.Profile, Is.EqualTo("http-1.0-rc4-qualified"));
            Assert.That(description.SupportsAtomicMutations, Is.True);
            Assert.That(description.SupportsConditionalMutations, Is.True);
            Assert.That(description.SupportsWriteTouch, Is.True);
            Assert.That(description.SupportsOperationReplay, Is.False);
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
            Assert.That(handler.Requests[0].Uri, Is.EqualTo("https://registry.example/registry/"));
        }

        [Test]
        public void OptionsExposeDocumentedConservativeDefaults()
        {
            var options = new XRegistryHttpOptions();

            Assert.That(options.AllowLoopbackHttp, Is.False);
            Assert.That(options.IsQualifiedBinding, Is.False);
            Assert.That(options.MaximumBodyBytes, Is.EqualTo(33_554_432));
            Assert.That(options.MaximumJsonDepth, Is.EqualTo(64));
            Assert.That(options.MaximumHeaderBytes, Is.EqualTo(65_536));
            Assert.That(options.MaximumHeaders, Is.EqualTo(128));
            Assert.That(options.MaximumParameters, Is.EqualTo(128));
            Assert.That(options.MaximumUriLength, Is.EqualTo(16_384));
            Assert.That(options.RequestTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(options.Telemetry, Is.Null);
        }

        [TestCase("bodyZero")]
        [TestCase("bodyNegative")]
        [TestCase("depthZero")]
        [TestCase("depthNegative")]
        [TestCase("depthTooLarge")]
        [TestCase("headerBytesZero")]
        [TestCase("headerBytesNegative")]
        [TestCase("headerCountZero")]
        [TestCase("headerCountNegative")]
        [TestCase("parameterCountZero")]
        [TestCase("parameterCountNegative")]
        [TestCase("uriZero")]
        [TestCase("uriNegative")]
        [TestCase("timeoutZero")]
        [TestCase("timeoutNegative")]
        [TestCase("timeoutTooLarge")]
        public void OptionsRejectInvalidBoundsBeforeSending(string invalid)
        {
            XRegistryHttpOptions options = invalid switch
            {
                "bodyZero" => new() { MaximumBodyBytes = 0 },
                "bodyNegative" => new() { MaximumBodyBytes = -1 },
                "depthZero" => new() { MaximumJsonDepth = 0 },
                "depthNegative" => new() { MaximumJsonDepth = -1 },
                "depthTooLarge" => new() { MaximumJsonDepth = 1025 },
                "headerBytesZero" => new() { MaximumHeaderBytes = 0 },
                "headerBytesNegative" => new() { MaximumHeaderBytes = -1 },
                "headerCountZero" => new() { MaximumHeaders = 0 },
                "headerCountNegative" => new() { MaximumHeaders = -1 },
                "parameterCountZero" => new() { MaximumParameters = 0 },
                "parameterCountNegative" => new() { MaximumParameters = -1 },
                "uriZero" => new() { MaximumUriLength = 0 },
                "uriNegative" => new() { MaximumUriLength = -1 },
                "timeoutZero" => new() { RequestTimeout = TimeSpan.Zero },
                "timeoutNegative" => new() { RequestTimeout = TimeSpan.FromMilliseconds(-1) },
                _ => new() { RequestTimeout = TimeSpan.FromMilliseconds((long)int.MaxValue + 1) }
            };
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            using var client = new HttpClient(handler);

            Assert.That(() => new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, options),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            var services = new ServiceCollection();
            Assert.That(() => services.AddXRegistryHttpEndpoint(HttpTestData.RegistryRoot, options),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(services, Is.Empty, "Invalid registration must not leave a partial DI registration.");
            Assert.That(handler.Requests, Is.Empty);
        }

        [TestCase(1)]
        [TestCase(1024)]
        public void JsonDepthBoundsAreInclusive(int depth)
        {
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                new XRegistryHttpOptions { MaximumJsonDepth = depth });

            Assert.That(endpoint.RegistryRoot.AbsoluteUri, Is.EqualTo("https://registry.example/registry/"));
            Assert.That(handler.Requests, Is.Empty);
        }

        [TestCase(1)]
        [TestCase(int.MaxValue)]
        public void TimeoutBoundsAreInclusive(int milliseconds)
        {
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, new XRegistryHttpOptions
            {
                RequestTimeout = TimeSpan.FromMilliseconds(milliseconds)
            });

            Assert.That(endpoint.RegistryRoot.AbsoluteUri, Is.EqualTo("https://registry.example/registry/"));
            Assert.That(handler.Requests, Is.Empty);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t")]
        public void RegistrationRejectsMissingClientNameWithoutPartialRegistration(string? name)
        {
            var services = new ServiceCollection();

            Assert.That(() => services.AddXRegistryHttpEndpoint(HttpTestData.RegistryRoot, clientName: name!),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("clientName"));
            Assert.That(services, Is.Empty);
        }

        [Test]
        public void ConstructorsAndRegistrationRejectNullRequiredArguments()
        {
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            using var client = new HttpClient(handler);
            var services = new ServiceCollection();

            Assert.That(() => new XRegistryHttpEndpoint(null!, HttpTestData.RegistryRoot),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("client"));
            Assert.That(() => new XRegistryHttpEndpoint(client, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("registryRoot"));
            Assert.That(() => XRegistryHttpServiceCollectionExtensions.AddXRegistryHttpEndpoint(null!,
                HttpTestData.RegistryRoot),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("services"));
            Assert.That(() => services.AddXRegistryHttpEndpoint(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("registryRoot"));
            Assert.That(services, Is.Empty);
            Assert.That(handler.Requests, Is.Empty);
        }

        [Test]
        public async Task EndpointRejectsNullCallsAndContextsWithoutSending()
        {
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            await Assert.ThatAsync(async () => await endpoint.InspectAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("context")).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("request")).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, "/model") { Context = null! }).ConfigureAwait(false),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("Context")).ConfigureAwait(false);
            Assert.That(handler.Requests, Is.Empty);
        }

        [Test]
        public async Task EndpointLeavesInjectedHttpClientUsableAndDoesNotSerializeCallContext()
        {
            using var handler = new RecordingHttpHandler(
                _ => HttpTestData.JsonResponse(/*lang=json,strict*/ """{"n":7}"""));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse result = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/model")
            {
                Context = new XRegistryCallContext("private-subject")
                {
                    Authority = "private-authority",
                    Roles = ["private-role"],
                    SessionId = "private-session",
                    IsAuthenticated = true
                }
            }).ConfigureAwait(false);
            using HttpResponseMessage direct = await client.GetAsync(
                new Uri("https://registry.example/direct")).ConfigureAwait(false);

            Assert.That(result.Metadata.GetProperty("n").GetInt32(), Is.EqualTo(7));
            Assert.That(await direct.Content.ReadAsStringAsync().ConfigureAwait(false),
                Is.EqualTo(/*lang=json,strict*/ """{"n":7}"""));
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(handler.Requests[1].Uri, Is.EqualTo("https://registry.example/direct"));
            Assert.That(handler.Requests[0].Headers.Keys,
                Has.None.StartsWith("xRegistry-").IgnoreCase);
            Assert.That(string.Join(" ", handler.Requests[0].Headers.Values.SelectMany(value => value)),
                Does.Not.Contain("private-"));
        }

        [Test]
        public void InspectionExceptionRetainsResponseAndStandardExceptionState()
        {
            var response = new XRegistryResponse(403)
            {
                Metadata = HttpTestData.Json(HttpTestData.Problem),
                Error = new XRegistryError("mismatched_epoch", "rejected")
            };
            var failure = new XRegistryHttpException("inspection failed", response);
            var inner = new InvalidOperationException("inner detail");
            var wrapped = new XRegistryHttpException("transport failed", inner);

            Assert.That(failure.Message, Is.EqualTo("inspection failed"));
            Assert.That(failure.Response, Is.SameAs(response));
            Assert.That(failure.Response!.StatusCode, Is.EqualTo(403));
            HttpTestData.AssertProblem(failure.Response.Metadata);
            Assert.That(wrapped.Message, Is.EqualTo("transport failed"));
            Assert.That(wrapped.InnerException, Is.SameAs(inner));
            Assert.That(wrapped.Response, Is.Null);
            Assert.That(new XRegistryHttpException("only message").Message, Is.EqualTo("only message"));
            Assert.That(new XRegistryHttpException().Response, Is.Null);
            Assert.That(() => new XRegistryHttpException("missing", (XRegistryResponse)null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("response"));
        }

#if NET8_0_OR_GREATER
        [TestCase(false)]
        [TestCase(true)]
        public void InspectionExceptionModernConstructorsPreserveTransportDetails(bool classified)
        {
            var inner = new InvalidOperationException("transport cause");
            XRegistryHttpException failure = classified
                ? new XRegistryHttpException(
                    HttpRequestError.ConnectionError, "transport failed", inner, HttpStatusCode.BadGateway)
                : new XRegistryHttpException("transport failed", inner, HttpStatusCode.BadGateway);

            Assert.That(failure.Message, Is.EqualTo("transport failed"));
            Assert.That(failure.InnerException, Is.SameAs(inner));
            Assert.That(failure.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
            Assert.That(failure.HttpRequestError,
                Is.EqualTo(classified ? HttpRequestError.ConnectionError : HttpRequestError.Unknown));
            Assert.That(failure.Response, Is.Null);

            var unspecified = new XRegistryHttpException(HttpRequestError.NameResolutionError);
            Assert.That(unspecified.HttpRequestError, Is.EqualTo(HttpRequestError.NameResolutionError));
            Assert.That(unspecified.StatusCode, Is.Null);
            Assert.That(unspecified.InnerException, Is.Null);
            Assert.That(unspecified.Response, Is.Null);
        }
#else
        [Test]
        public void LegacyHttpAssemblyDoesNotReferenceOrExportAspNetHosting()
        {
            System.Reflection.Assembly assembly = typeof(XRegistryHttpEndpoint).Assembly;

            Assert.That(assembly.GetReferencedAssemblies().Select(name => name.Name),
                Has.None.StartsWith("Microsoft.AspNetCore"));
            Assert.That(assembly.GetExportedTypes().Select(type => type.Name),
                Does.Not.Contain("XRegistryHttpEndpointRouteBuilderExtensions"));
            Assert.That(assembly.GetExportedTypes().Select(type => type.Name),
                Does.Contain("XRegistryHttpEndpoint").And.Contain("XRegistryHttpServiceCollectionExtensions"));
        }
#endif
    }
}
