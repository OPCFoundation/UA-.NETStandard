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

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Bindings.WebApi;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.WebApi.Tests
{
    /// <summary>
    /// End-to-end REST binding tests through a real
    /// <see cref="HttpsTransportListener"/> and Kestrel TLS endpoint.
    /// </summary>
    [TestFixture]
    [Category("RealWebApiIntegration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class RealHttpsListenerIntegrationTests
    {
        private static readonly MethodInfo s_decodeBodyMethod = typeof(WebApiBodyCodec)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m =>
                m.Name == nameof(WebApiBodyCodec.DecodeBody) &&
                m.IsGenericMethodDefinition &&
                m.GetParameters() is { Length: 3 } parameters &&
                parameters[0].ParameterType == typeof(byte[]));

        private ITelemetryContext? m_telemetry;
        private InMemoryCertificateRegistry? m_certificateRegistry;
        private StubTransportListenerCallback? m_callback;
        private WebApiServer? m_restServer;
        private HttpsTransportListener? m_listener;
        private HttpClient? m_client;
        private HttpClientHandler? m_clientHandler;

        /// <summary>
        /// All REST service route and encoding combinations.
        /// </summary>
        public static IEnumerable<TestCaseData> RouteEncodingCases()
        {
            foreach (WebApiServiceRoute route in WebApiServiceRoutes.Routes)
            {
                yield return new TestCaseData(route, WebApiEncoding.Compact)
                    .SetName($"{route.OperationId}CompactOverRealHttpsListener");
                yield return new TestCaseData(route, WebApiEncoding.Verbose)
                    .SetName($"{route.OperationId}VerboseOverRealHttpsListener");
            }
        }

        [SetUp]
        public void SetUp()
        {
            m_telemetry = new TestTelemetryContext();
            IServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(m_telemetry);
            m_callback = new StubTransportListenerCallback();
            m_restServer = new WebApiServer(messageContext, "real-rest-api");

            var factory = new HttpsTransportListenerFactory();
            factory.StartupContributors.Add(new WebApiHttpsStartupContributor(m_restServer));
            m_listener = (HttpsTransportListener)factory.Create(m_telemetry);

            Certificate certificate = CreateServerCertificate();
            try
            {
                m_certificateRegistry = new InMemoryCertificateRegistry(certificate);
            }
            finally
            {
                certificate.Dispose();
            }

            int port = FindAvailableTcpPort();
            m_listener.Open(
                new Uri($"https://localhost:{port}/"),
                CreateListenerSettings(m_certificateRegistry, port),
                m_callback);

            m_clientHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
            };
            m_client = new HttpClient(m_clientHandler)
            {
                BaseAddress = new Uri($"https://localhost:{port}/")
            };
        }

        [TearDown]
        public void TearDown()
        {
            m_client?.Dispose();
            m_client = null;
            m_clientHandler?.Dispose();
            m_clientHandler = null;
            m_listener?.Close();
            m_listener?.Dispose();
            m_listener = null;

            m_certificateRegistry?.Dispose();
            m_certificateRegistry = null;
        }

        [TestCaseSource(nameof(RouteEncodingCases))]
        public async Task ServiceRouteRoundTripsThroughRealHttpsListener(
            WebApiServiceRoute route,
            WebApiEncoding encoding)
        {
            IServiceRequest request = CreateRequest(route, 100);

            using HttpResponseMessage response = await PostAsync(
                route.Path,
                request,
                encoding)
                .ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            AssertContentType(response, encoding);

            byte[] payload = await response.Content.ReadAsByteArrayAsync()
                .ConfigureAwait(false);
            IServiceResponse decoded = DecodeResponse(route, payload);

            Assert.That(decoded, Is.InstanceOf(route.ResponseType));
            Assert.That(m_callback!.LastRequest, Is.InstanceOf(route.RequestType));
        }

        [Test]
        public async Task PublishLongPollAwaitsServerResponse()
        {
            m_callback!.Delay = TimeSpan.FromMilliseconds(500);
            var request = new PublishRequest
            {
                RequestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 200,
                    TimeoutHint = 10_000
                },
                SubscriptionAcknowledgements = new ArrayOf<SubscriptionAcknowledgement>()
            };

            var stopwatch = Stopwatch.StartNew();
            using HttpResponseMessage response = await PostAsync(
                "/publish",
                request,
                WebApiEncoding.Compact)
                .ConfigureAwait(false);
            stopwatch.Stop();
            TestContext.Out.WriteLine($"Publish long poll elapsed: {stopwatch.ElapsedMilliseconds} ms");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            byte[] payload = await response.Content.ReadAsByteArrayAsync()
                .ConfigureAwait(false);
            IServiceResponse decoded = DecodeResponse(
                WebApiServiceRoutes.Routes.Single(r => r.RequestType == typeof(PublishRequest)),
                payload);

            Assert.That(decoded, Is.InstanceOf<PublishResponse>());
            Assert.That(stopwatch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500)));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        }

        private static Certificate CreateServerCertificate()
        {
            return DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    "urn:localhost:Opc.Ua.Bindings.WebApi.Tests",
                    "Opc.Ua.Bindings.WebApi.Tests",
                    "CN=localhost",
                    ["localhost"])
                .SetLifeTime(TimeSpan.FromDays(1))
                .CreateForRSA();
        }

        private static TransportListenerSettings CreateListenerSettings(
            ICertificateRegistry certificateRegistry,
            int port)
        {
            var endpoint = new EndpointDescription
            {
                EndpointUrl = $"https://localhost:{port}/",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.HttpsBinaryTransport,
                Server = new ApplicationDescription
                {
                    ApplicationName = new LocalizedText("Opc.Ua.Bindings.WebApi.Tests"),
                    ApplicationType = ApplicationType.Server,
                    ApplicationUri = "urn:localhost:Opc.Ua.Bindings.WebApi.Tests",
                    ProductUri = "urn:opcfoundation.org:Opc.Ua.Bindings.WebApi.Tests"
                },
                UserIdentityTokens = new ArrayOf<UserTokenPolicy>()
            };

            return new TransportListenerSettings
            {
                Descriptions = [endpoint],
                Configuration = EndpointConfiguration.Create(),
                ServerCertificates = certificateRegistry,
                CertificateValidator = new AcceptAllCertificateValidator(),
                NamespaceUris = new NamespaceTable(),
                Factory = ServiceMessageContext.Create(new TestTelemetryContext()).Factory,
                HttpsMutualTls = false
            };
        }

        private static int FindAvailableTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static IServiceRequest CreateRequest(WebApiServiceRoute route, uint requestHandle)
        {
            var request = (IServiceRequest)Activator.CreateInstance(route.RequestType)!;
            route.RequestType
                .GetProperty(nameof(IServiceRequest.RequestHeader))!
                .SetValue(
                    request,
                    new RequestHeader
                    {
                        Timestamp = DateTime.UtcNow,
                        RequestHandle = requestHandle,
                        TimeoutHint = 10_000
                    });
            return request;
        }

        private async Task<HttpResponseMessage> PostAsync(
            string path,
            IServiceRequest request,
            WebApiEncoding encoding)
        {
            byte[] body = WebApiBodyCodec.EncodeBody(
                (IEncodeable)request,
                m_restServer!.MessageContext,
                WebApiMediaType.ToEncoderOptions(encoding));
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(
                WebApiMediaType.FormatContentType(encoding));

            using var message = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = content
            };
            message.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(
                WebApiMediaType.FormatContentType(encoding)));

            return await m_client!.SendAsync(message, HttpCompletionOption.ResponseContentRead)
                .ConfigureAwait(false);
        }

        private IServiceResponse DecodeResponse(WebApiServiceRoute route, byte[] payload)
        {
            return (IServiceResponse)s_decodeBodyMethod
                .MakeGenericMethod(route.ResponseType)
                .Invoke(null, [payload, m_restServer!.MessageContext, null])!;
        }

        private static void AssertContentType(HttpResponseMessage response, WebApiEncoding encoding)
        {
            MediaTypeHeaderValue? contentType = response.Content.Headers.ContentType;
            Assert.That(contentType?.MediaType, Is.EqualTo(WebApiMediaType.ContentType));

            string expectedEncoding = encoding == WebApiEncoding.Verbose
                ? WebApiMediaType.EncodingVerbose
                : WebApiMediaType.EncodingCompact;
            Assert.That(
                contentType?.Parameters,
                Has.Some.Matches<NameValueHeaderValue>(p =>
                    p.Name == WebApiMediaType.EncodingParameter &&
                    p.Value == expectedEncoding));
        }

        private sealed class StubTransportListenerCallback : ITransportListenerCallback
        {
            public IServiceRequest? LastRequest { get; private set; }
            public uint NextFault { get; set; }
            public TimeSpan Delay { get; set; }

            public async ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext secureChannelContext,
                IServiceRequest request,
                CancellationToken cancellationToken = default)
            {
                LastRequest = request;

                if (Delay > TimeSpan.Zero)
                {
                    await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
                }

                if (!WebApiServiceRoutes.TryGetByRequestType(
                    request.GetType(),
                    out WebApiServiceRoute route))
                {
                    throw new InvalidOperationException(
                        $"No matching route for {request.GetType().Name}.");
                }

                StatusCode serviceResult = NextFault != 0
                    ? new StatusCode(NextFault)
                    : StatusCodes.Good;

                IServiceResponse response = (IServiceResponse)Activator.CreateInstance(route.ResponseType)!;
                var responseHeader = new ResponseHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = request.RequestHeader?.RequestHandle ?? 0,
                    ServiceResult = serviceResult,
                    StringTable = new ArrayOf<string>(),
                    AdditionalHeader = new ExtensionObject()
                };
                route.ResponseType
                    .GetProperty(nameof(IServiceResponse.ResponseHeader))!
                    .SetValue(response, responseHeader);
                return response;
            }

            public bool TryGetSecureChannelIdForAuthenticationToken(
                NodeId authenticationToken,
                out uint channelId)
            {
                channelId = 0;
                return false;
            }

            public void ReportAuditOpenSecureChannelEvent(
                string globalChannelId,
                EndpointDescription endpointDescription,
                OpenSecureChannelRequest request,
                Certificate clientCertificate,
                Exception exception)
            {
            }

            public void ReportAuditCloseSecureChannelEvent(string globalChannelId, Exception exception)
            {
            }

            public void ReportAuditCertificateEvent(Certificate clientCertificate, Exception exception)
            {
            }
        }

        private sealed class InMemoryCertificateRegistry : ICertificateRegistry, IDisposable
        {
            private readonly CertificateEntry m_entry;
            private readonly CertificateEntry[] m_entries;

            public InMemoryCertificateRegistry(Certificate certificate)
            {
                using var issuerChain = new CertificateCollection();
                m_entry = new CertificateEntry(
                    certificate,
                    issuerChain,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType);
                m_entries = [m_entry];
            }

            public bool SendCertificateChain => false;
            public IReadOnlyList<CertificateEntry> ApplicationCertificates => m_entries;

            public CertificateEntry? GetApplicationCertificate(NodeId certificateType)
            {
                return m_entry;
            }

            public CertificateEntry? GetInstanceCertificate(string securityPolicyUri)
            {
                return m_entry;
            }

            public byte[] GetEncodedChainBlob(string securityPolicyUri)
            {
                return m_entry.GetEncodedChainBlob();
            }

            public byte[]? LoadCertificateChainRaw(Certificate certificate)
            {
                return string.Equals(
                    certificate.Thumbprint,
                    m_entry.Certificate.Thumbprint,
                    StringComparison.OrdinalIgnoreCase)
                    ? m_entry.GetEncodedChainBlob()
                    : null;
            }

            public Task<bool> GetIssuersAsync(
                Certificate certificate,
                IList<CertificateIssuerReference> issuers,
                CancellationToken ct = default)
            {
                return Task.FromResult(false);
            }

            public void Dispose()
            {
                m_entry.Dispose();
            }
        }

        private sealed class AcceptAllCertificateValidator : ICertificateValidatorEx
        {
            public Func<Certificate, ServiceResult, bool>? AcceptError { get; set; }

            public Task<Opc.Ua.CertificateValidationResult> ValidateAsync(
                CertificateCollection chain,
                TrustListIdentifier? trustList = null,
                Opc.Ua.Security.Certificates.CertificateValidationOptions? options = null,
                CancellationToken ct = default)
            {
                return Task.FromResult(Opc.Ua.CertificateValidationResult.Success);
            }

            public Task<Opc.Ua.CertificateValidationResult> ValidateAsync(
                Certificate certificate,
                TrustListIdentifier? trustList = null,
                CancellationToken ct = default)
            {
                return Task.FromResult(Opc.Ua.CertificateValidationResult.Success);
            }
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
