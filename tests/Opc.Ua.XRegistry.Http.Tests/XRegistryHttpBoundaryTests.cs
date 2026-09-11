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
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http.Tests
{
    [TestFixture]
    [Category("XRegistryHttp")]
    public sealed class XRegistryHttpBoundaryTests
    {
        [TestCase(false, 1024)]
        [TestCase(false, 1025)]
        [TestCase(true, 1024)]
        [TestCase(true, 1025)]
        public async Task ClientBodyLimitAcceptsBoundaryAndRejectsNextByte(bool streaming, int size)
        {
            byte[] payload = new byte[size];
            payload[0] = 0xff;
            payload[size - 1] = 0x17;
            using var handler = new RecordingHttpHandler(message =>
            {
                if (message.RequestUri!.AbsolutePath == "/registry/model")
                {
                    return HttpTestData.JsonResponse(HttpTestData.Model);
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = streaming ? new StreamingHttpContent(payload) : new ByteArrayContent(payload)
                };
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                new XRegistryHttpOptions { MaximumBodyBytes = 1024 });
            var request = new XRegistryRequest(XRegistryAction.Read, HttpTestData.ResourcePath);

            if (size == 1024)
            {
                XRegistryResponse result = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
                Assert.That(result.Document.Span.ToArray(), Is.EqualTo(payload));
                Assert.That(result.Document.Length, Is.EqualTo(1024));
                Assert.That(result.StatusCode, Is.EqualTo(200));
            }
            else
            {
                await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(request).ConfigureAwait(false),
                    Throws.InstanceOf<IOException>().With.Message.Contains("limit")).ConfigureAwait(false);
            }
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task ContentLengthOverLimitRejectsBeforeReadingStream()
        {
            using var stream = new CancellationReadStream();
            using var handler = new RecordingHttpHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
                response.Content.Headers.ContentLength = 1025;
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                new XRegistryHttpOptions { MaximumBodyBytes = 1024 });

            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read,
                "/model"))
                .ConfigureAwait(false), Throws.InstanceOf<IOException>().With.Message.Contains("limit"))
                .ConfigureAwait(false);
            Assert.That(stream.ReadCalls, Is.Zero);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [TestCase("gzip", 1024)]
        [TestCase("gzip", 1025)]
        [TestCase("deflate", 1024)]
        [TestCase("deflate", 1025)]
        public async Task CompressedResponsesEnforceDecodedBodyBoundary(string encoding, int decodedSize)
        {
            byte[] payload = Encoding.UTF8.GetBytes(new string(' ',
                decodedSize - 7) + /*lang=json,strict*/ """{"n":7}""");
            byte[] compressed = Compress(payload, encoding);
            Assert.That(compressed, Has.Length.LessThan(1024), "The encoded stream must fit to isolate decompression.");
            using var handler = new RecordingHttpHandler(_ =>
            {
                HttpResponseMessage response = HttpTestData.BytesResponse(compressed, "application/json");
                response.Content.Headers.ContentEncoding.Add(encoding);
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                new XRegistryHttpOptions { MaximumBodyBytes = 1024 });

            if (decodedSize == 1024)
            {
                XRegistryResponse result = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read,
                    "/model"))
                    .ConfigureAwait(false);
                Assert.That(result.Metadata.GetProperty("n").GetInt32(), Is.EqualTo(7));
                Assert.That(result.Document.IsNull, Is.True);
            }
            else
            {
                await Assert.ThatAsync(
                    async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/model"))
                        .ConfigureAwait(false), Throws.InstanceOf<IOException>().With.Message.Contains("limit"))
                    .ConfigureAwait(false);
            }
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [TestCase("gzip", false, 0)]
        [TestCase("gzip", false, -1)]
        [TestCase("gzip", true, 0)]
        [TestCase("gzip", true, -1)]
        [TestCase("deflate", false, 0)]
        [TestCase("deflate", false, -1)]
        [TestCase("deflate", true, 0)]
        [TestCase("deflate", true, -1)]
        public async Task CompressedResponsesAlsoEnforceEncodedBodyBoundary(string encoding, bool streaming,
            int adjustment)
        {
            byte[] compressed = Compress(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"n":7}"""), encoding);
            using var handler = new RecordingHttpHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = streaming ? new StreamingHttpContent(compressed) : new ByteArrayContent(compressed)
                };
                response.Content.Headers.ContentEncoding.Add(encoding);
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                new XRegistryHttpOptions { MaximumBodyBytes = compressed.Length + adjustment });

            if (adjustment == 0)
            {
                XRegistryResponse result = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read,
                    "/model"))
                    .ConfigureAwait(false);
                Assert.That(result.Metadata.GetProperty("n").GetInt32(), Is.EqualTo(7));
                Assert.That(result.StatusCode, Is.EqualTo(200));
            }
            else
            {
                await Assert.ThatAsync(
                    async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/model"))
                        .ConfigureAwait(false), Throws.InstanceOf<IOException>().With.Message.Contains("limit"))
                    .ConfigureAwait(false);
            }
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [TestCase("identity")]
        [TestCase("gzip, deflate")]
        public async Task IdentityAndTwoEncodingsDecodeInReverseOrder(string encoding)
        {
            byte[] payload = Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"flag":true,"n":7}""");
            if (encoding != "identity")
            {
                payload = Compress(Compress(payload, "gzip"), "deflate");
            }
            using var handler = new RecordingHttpHandler(_ =>
            {
                HttpResponseMessage response = HttpTestData.BytesResponse(payload);
                response.Content.Headers.TryAddWithoutValidation("Content-Encoding", encoding);
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse result = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/model"))
                .ConfigureAwait(false);

            Assert.That(result.Metadata.GetProperty("flag").GetBoolean(), Is.True);
            Assert.That(result.Metadata.GetProperty("n").GetInt32(), Is.EqualTo(7));
            Assert.That(handler.Requests[0].Header("Accept-Encoding"), Is.EqualTo("gzip, deflate"));
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [TestCase("br")]
        [TestCase("made-up")]
        [TestCase("identity, identity, identity")]
        public async Task UnsupportedOrExcessiveContentEncodingsFailExplicitly(string encoding)
        {
            using var handler = new RecordingHttpHandler(_ =>
            {
                HttpResponseMessage response = HttpTestData.JsonResponse("{}");
                response.Content.Headers.TryAddWithoutValidation("Content-Encoding", encoding);
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read,
                "/model"))
                .ConfigureAwait(false), Throws.InstanceOf<IOException>().With.Message.Contains("encoding"))
                .ConfigureAwait(false);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [TestCase(/*lang=json,strict*/ """{"a":{"b":1}}""", true)]
        [TestCase(/*lang=json,strict*/ """{"a":{"b":{"c":1}}}""", false)]
        public async Task JsonDepthAcceptsBoundaryAndRejectsNextLevel(string body, bool accepted)
        {
            using var handler = new RecordingHttpHandler(_ => HttpTestData.JsonResponse(body));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                new XRegistryHttpOptions { MaximumJsonDepth = 2 });

            if (accepted)
            {
                XRegistryResponse response = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read,
                    "/model"))
                    .ConfigureAwait(false);
                Assert.That(response.Metadata.GetProperty("a").GetProperty("b").GetInt32(), Is.EqualTo(1));
                Assert.That(response.Document.IsNull, Is.True);
            }
            else
            {
                await Assert.ThatAsync(
                    async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/model"))
                        .ConfigureAwait(false), Throws.InstanceOf<JsonException>()).ConfigureAwait(false);
            }
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [TestCase(/*lang=json,strict*/ """{"a":1,"a":2}""")]
        [TestCase(/*lang=json,strict*/ """{"nested":{"a":1,"a":2}}""")]
        [TestCase(/*lang=json,strict*/ """[{"a":1,"a":2}]""")]
        public async Task DuplicateJsonPropertiesAtAnyDepthAreRejected(string body)
        {
            using var handler = new RecordingHttpHandler(_ => HttpTestData.JsonResponse(body));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read,
                "/model"))
                .ConfigureAwait(false), Throws.InstanceOf<JsonException>().With.Message.Contains("duplicate"))
                .ConfigureAwait(false);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [TestCase(2)]
        [TestCase(3)]
        public async Task ParameterBudgetAcceptsBoundaryAndRejectsNextEntry(int count)
        {
            using var handler = new RecordingHttpHandler(_ =>
                HttpTestData.JsonResponse(/*lang=json,strict*/ """{"n":7}"""));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                new XRegistryHttpOptions { MaximumParameters = 2 });
            var request = new XRegistryRequest(XRegistryAction.Read, "/model")
            {
                Parameters = count == 2
                    ? [new("inline", null), new("future", string.Empty)]
                    : [new("inline", null), new("future", string.Empty), new("last", "x")]
            };

            if (count == 2)
            {
                XRegistryResponse result = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
                Assert.That(handler.Requests[0].Uri,
                    Is.EqualTo("https://registry.example/registry/model?inline&future="));
                Assert.That(result.Metadata.GetProperty("n").GetInt32(), Is.EqualTo(7));
                Assert.That(handler.Requests, Has.Count.EqualTo(1));
            }
            else
            {
                await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(request).ConfigureAwait(false),
                    Throws.TypeOf<InvalidDataException>().With.Message.Contains("parameters")).ConfigureAwait(false);
                Assert.That(handler.Requests, Is.Empty);
            }
        }

        [TestCase(0)]
        [TestCase(-1)]
        public async Task UriBudgetIncludesCompleteEscapedTarget(int adjustment)
        {
            const string target = "https://registry.example/registry/model?future=a%20b";
            using var handler = new RecordingHttpHandler(_ =>
                HttpTestData.JsonResponse(/*lang=json,strict*/ """{"n":7}"""));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                new XRegistryHttpOptions { MaximumUriLength = target.Length + adjustment });
            var request = new XRegistryRequest(XRegistryAction.Read, "/model") { Parameters = [new("future", "a b")] };

            if (adjustment == 0)
            {
                XRegistryResponse result = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
                Assert.That(handler.Requests[0].Uri, Is.EqualTo(target));
                Assert.That(result.Metadata.GetProperty("n").GetInt32(), Is.EqualTo(7));
            }
            else
            {
                await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(request).ConfigureAwait(false),
                    Throws.TypeOf<InvalidDataException>().With.Message.Contains("URI")).ConfigureAwait(false);
                Assert.That(handler.Requests, Is.Empty);
            }
        }

        [TestCase(49, true)]
        [TestCase(48, false)]
        public async Task OutgoingHeaderByteBudgetHasExactBoundary(int limit, bool accepted)
        {
            using var handler = new RecordingHttpHandler(_ => HttpTestData.BytesResponse(Encoding.UTF8.GetBytes("{}")));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                new XRegistryHttpOptions { MaximumHeaderBytes = limit });

            if (accepted)
            {
                XRegistryResponse response = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read,
                    "/model"))
                    .ConfigureAwait(false);
                Assert.That(response.Metadata.ValueKind, Is.EqualTo(JsonValueKind.Object));
                Assert.That(handler.Requests[0].Header("Accept-Encoding"), Is.EqualTo("gzip, deflate"));
            }
            else
            {
                await Assert.ThatAsync(
                    async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/model"))
                        .ConfigureAwait(false), Throws.InstanceOf<IOException>().With.Message.Contains("headers"))
                    .ConfigureAwait(false);
                Assert.That(handler.Requests, Is.Empty);
            }
        }

        [TestCase(2, true)]
        [TestCase(1, false)]
        public async Task OutgoingHeaderCountIncludesRepeatedValues(int limit, bool accepted)
        {
            using var handler = new RecordingHttpHandler(_ => HttpTestData.BytesResponse(Encoding.UTF8.GetBytes("{}")));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                new XRegistryHttpOptions { MaximumHeaders = limit });

            if (accepted)
            {
                XRegistryResponse response = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read,
                    "/model"))
                    .ConfigureAwait(false);
                Assert.That(response.StatusCode, Is.EqualTo(200));
                Assert.That(handler.Requests[0].Header("Accept-Encoding"), Is.EqualTo("gzip, deflate"));
            }
            else
            {
                await Assert.ThatAsync(
                    async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/model"))
                        .ConfigureAwait(false), Throws.InstanceOf<IOException>().With.Message.Contains("headers"))
                    .ConfigureAwait(false);
                Assert.That(handler.Requests, Is.Empty);
            }
        }

        [Test]
        public async Task CancellationStopsActiveMutationWithoutRetry()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken observed = default;
            using var handler = new RecordingHttpHandler(async (message, token) =>
            {
                if (message.Method == HttpMethod.Get)
                {
                    return HttpTestData.InspectionResponse(message);
                }
                observed = token;
                var completion = new TaskCompletionSource<HttpResponseMessage>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using CancellationTokenRegistration registration = token.Register(() => completion.TrySetCanceled());
                entered.TrySetResult(true);
                return await completion.Task.ConfigureAwait(false);
            });
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, HttpTestData.Qualified);
            using var caller = new CancellationTokenSource();
            Task<XRegistryResponse> execution = endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Replace, "/")
            {
                Metadata = HttpTestData.Json(/*lang=json,strict*/ """{"epoch":0}""")
            }, caller.Token).AsTask();
            await entered.Task.ConfigureAwait(false);

            caller.Cancel();
            await Assert.ThatAsync(() => execution,
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(observed.IsCancellationRequested, Is.True);
            Assert.That(handler.Requests, Has.Count.EqualTo(4));
            Assert.That(handler.Requests[3].Method, Is.EqualTo("PUT"));
            Assert.That(Encoding.UTF8.GetString(handler.Requests[3].Body),
                Is.EqualTo(/*lang=json,strict*/ """{"epoch":0}"""));
        }

        [Test]
        public async Task TimeoutCoversInspectionAndResponseStreaming()
        {
            using var stream = new CancellationReadStream();
            var inspectionStreams = new List<TokenRecordingReadStream>();
            using var handler = new RecordingHttpHandler(message =>
            {
                if (message.Method != HttpMethod.Get)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
                }
                string body = message.RequestUri!.AbsolutePath switch
                {
                    "/registry/" => HttpTestData.Root,
                    "/registry/model" => HttpTestData.Model,
                    "/registry/capabilities" => HttpTestData.Capabilities,
                    _ => throw new AssertionException("Unexpected inspection path.")
                };
                var inspectionStream = new TokenRecordingReadStream(Encoding.UTF8.GetBytes(body));
                inspectionStreams.Add(inspectionStream);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(inspectionStream) };
            });
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                HttpTestData.Qualified with { RequestTimeout = TimeSpan.FromSeconds(1) });
            using var caller = new CancellationTokenSource();
            Task<XRegistryResponse> execution = endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Replace, "/")
            {
                Metadata = HttpTestData.Json(/*lang=json,strict*/ """{"epoch":0}""")
            }, caller.Token).AsTask();
            await stream.Entered.Task.ConfigureAwait(false);

            await Assert.ThatAsync(() => execution,
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(stream.ReadToken.IsCancellationRequested, Is.True);
            Assert.That(caller.IsCancellationRequested, Is.False,
                "Only the operation deadline, not the caller, expired.");
            Assert.That(stream.ReadCalls, Is.EqualTo(1));
            Assert.That(inspectionStreams, Has.Count.EqualTo(3));
            foreach (TokenRecordingReadStream inspectionStream in inspectionStreams)
            {
                Assert.That(inspectionStream.ReadToken, Is.EqualTo(stream.ReadToken),
                    "Each inspection body and the final response must use one operation-wide deadline.");
                Assert.That(inspectionStream.ReadToken.IsCancellationRequested, Is.True);
            }
            Assert.That(handler.Requests, Has.Count.EqualTo(4));
            Assert.That(handler.Requests[3].Method, Is.EqualTo("PUT"));
        }

        [TestCase(140, 3, true)]
        [TestCase(139, 3, false)]
        [TestCase(140, 2, false)]
        public async Task IncomingResponseHeaderBudgetsIncludeContentHeaders(int byteLimit, int countLimit,
            bool accepted)
        {
            using var handler = new RecordingHttpHandler(_ =>
            {
                HttpResponseMessage response = HttpTestData.JsonResponse("{}", 200);
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                    "application/json");
                response.Content.Headers.ContentLength = 2;
                response.Headers.TryAddWithoutValidation("X-Pad", new string('a', 80));
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, new XRegistryHttpOptions
            {
                MaximumHeaderBytes = byteLimit,
                MaximumHeaders = countLimit
            });

            if (accepted)
            {
                XRegistryResponse response = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read,
                    "/model"))
                    .ConfigureAwait(false);
                Assert.That(response.Metadata.GetRawText(), Is.EqualTo("{}"));
                Assert.That(response.ContentType, Is.EqualTo("application/json"));
            }
            else
            {
                await Assert.ThatAsync(
                    async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/model"))
                        .ConfigureAwait(false), Throws.InstanceOf<IOException>().With.Message.Contains("headers"))
                    .ConfigureAwait(false);
            }
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        internal static byte[] Compress(byte[] payload, string encoding)
        {
            using var buffer = new MemoryStream();
            using (Stream encoder = encoding == "gzip"
                ? new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true)
                : new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            {
                encoder.Write(payload, 0, payload.Length);
            }
            return buffer.ToArray();
        }
    }
}
