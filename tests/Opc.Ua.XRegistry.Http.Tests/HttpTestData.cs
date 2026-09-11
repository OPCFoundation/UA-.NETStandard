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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.XRegistry.Http.Tests
{
    internal static class HttpTestData
    {
        public const string Root =
            /*lang=json,strict*/ """{"registryid":"golden-registry","specversion":"1.0-rc4","epoch":0}""";

        public const string Model =
            "{\"groups\":{\"schemagroups\":{\"singular\":\"schemagroup\",\"resources\":{" +
            "\"schemas\":{\"singular\":\"schema\",\"hasdocument\":true,\"attributes\":{" +
            "\"score\":{\"type\":\"integer\"},\"ratio\":{\"type\":\"decimal\"},\"active\":{\"type\":\"boolean\"}," +
            "\"scores\":{\"type\":\"map\",\"item\":{\"type\":\"integer\"}}," +
            "\"checks\":{\"type\":\"map\",\"item\":{\"type\":\"boolean\"}}," +
            "\"complex\":{\"type\":\"object\"},\"list\":{\"type\":\"array\"}}}," +
            "\"records\":{\"singular\":\"record\",\"hasdocument\":false},\"defaults\":{\"singular\":\"default\"}}}}," +
            "\"extension\":{\"number\":1.25,\"boolean\":true,\"array\":[0,false,null],\"epoch\":18446744073709551616}}";

        public const string Capabilities =
            "{\"specversions\":[\"1.0-rc4\"],\"available\":{\"entities\":{\"mutable\":true}," +
            "\"model\":{\"mutable\":false},\"modelsource\":{\"mutable\":true},\"capabilities\":{\"mutable\":true}}," +
            "\"mutable\":[]," +
            "\"flags\":[\"inline\",\"filter\",\"sort\"]," +
            "\"extension\":{\"enabled\":true,\"limit\":18446744073709551616,\"missing\":null}}";

        public const string Problem =
            "{\"type\":\"https://github.com/xregistry/spec/blob/main/core/spec.md#mismatched_epoch\"," +
            "\"title\":\"Epoch does not match\",\"detail\":\"Expected 0, found 18446744073709551616.\"," +
            "\"subject\":\"/schemagroups/g\",\"args\":{\"expected\":\"0\",\"actual\":\"18446744073709551616\"}," +
            "\"instance\":\"urn:problem:17\",\"source\":\"golden-backend\"," +
            "\"extension\":{\"retry\":false,\"attempts\":[1,null,3]}}";

        public const string ResourcePath = "/schemagroups/g/schemas/r";
        public const string RecordPath = "/schemagroups/g/records/r";

        public static Uri RegistryRoot { get; } = new("https://registry.example/registry/");

        public static XRegistryHttpOptions Qualified { get; } = new() { IsQualifiedBinding = true };

        public static JsonElement Json(string text)
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }

        public static HttpResponseMessage JsonResponse(string text, int status = 200)
        {
            return BytesResponse(Encoding.UTF8.GetBytes(text), "application/json; charset=utf-8", status);
        }

        public static HttpResponseMessage BytesResponse(byte[] bytes, string? contentType = null, int status = 200)
        {
            var response = new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new ByteArrayContent(bytes)
            };
            if (contentType is not null)
            {
                response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            }
            return response;
        }

        public static HttpResponseMessage InspectionResponse(HttpRequestMessage message)
        {
            message.ThrowIfNull(nameof(message));
            Assert.That(message.Method, Is.EqualTo(HttpMethod.Get), "Inspection must never mutate.");
            return message.RequestUri!.AbsolutePath switch
            {
                "/registry/" => JsonResponse(Root),
                "/registry/model" => JsonResponse(Model),
                "/registry/capabilities" => JsonResponse(Capabilities),
                _ => throw new AssertionException("Unexpected HTTP request: " + message.RequestUri)
            };
        }

        public static void AssertProblem(JsonElement problem)
        {
            Assert.That(problem.GetProperty("type").GetString(),
                Is.EqualTo("https://github.com/xregistry/spec/blob/main/core/spec.md#mismatched_epoch"));
            Assert.That(problem.GetProperty("title").GetString(), Is.EqualTo("Epoch does not match"));
            Assert.That(problem.GetProperty("detail").GetString(),
                Is.EqualTo("Expected 0, found 18446744073709551616."));
            Assert.That(problem.GetProperty("subject").GetString(), Is.EqualTo("/schemagroups/g"));
            Assert.That(problem.GetProperty("args").GetProperty("expected").GetString(), Is.EqualTo("0"));
            Assert.That(problem.GetProperty("args").GetProperty("actual").GetString(),
                Is.EqualTo("18446744073709551616"));
            Assert.That(problem.GetProperty("instance").GetString(), Is.EqualTo("urn:problem:17"));
            Assert.That(problem.GetProperty("source").GetString(), Is.EqualTo("golden-backend"));
            Assert.That(problem.GetProperty("extension").GetProperty("retry").GetBoolean(), Is.False);
            Assert.That(problem.GetProperty("extension").GetProperty("attempts").GetRawText(),
                Is.EqualTo("[1,null,3]"));
        }

        public static async Task<TException> CatchAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (TException exception)
            {
                return exception;
            }
            throw new AssertionException("Expected " + typeof(TException).Name + " but no exception was thrown.");
        }
    }

    internal sealed record CapturedHttpRequest(
        string Method,
        string Uri,
        bool HasContent,
        byte[] Body,
        IReadOnlyDictionary<string, string[]> Headers)
    {
        public string Header(string name)
        {
            return string.Join(", ", Headers[name]);
        }
    }

    internal sealed class RecordingHttpHandler : HttpMessageHandler
    {
        public RecordingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((message, _) => Task.FromResult(respond(message)))
        {
        }

        public RecordingHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            m_respond = respond;
        }

        public List<CapturedHttpRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(
                header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
            byte[] body = [];
            if (request.Content is not null)
            {
#if NET8_0_OR_GREATER
                body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
                body = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
                foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
                {
                    headers.Add(header.Key, [.. header.Value]);
                }
            }
            Requests.Add(new CapturedHttpRequest(request.Method.Method, request.RequestUri!.AbsoluteUri,
                request.Content is not null, body, headers));
            return await m_respond(request, cancellationToken).ConfigureAwait(false);
        }

        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> m_respond;
    }

    internal sealed class StreamingHttpContent : HttpContent
    {
        public StreamingHttpContent(byte[] bytes)
        {
            m_bytes = bytes;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(m_bytes, 0, m_bytes.Length);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return Task.FromResult<Stream>(new NonSeekableReadStream(m_bytes));
        }

        private readonly byte[] m_bytes;
    }

    internal sealed class NonSeekableReadStream : MemoryStream
    {
        public NonSeekableReadStream(byte[] bytes)
            : base(bytes, writable: false)
        {
        }

        public override bool CanSeek => false;
    }

    internal sealed class CancellationReadStream : Stream
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken ReadToken { get; private set; }

        public int ReadCalls { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadUntilCancelledAsync(cancellationToken);
        }

#if NET8_0_OR_GREATER
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(ReadUntilCancelledAsync(cancellationToken));
        }
#endif

        private async Task<int> ReadUntilCancelledAsync(CancellationToken cancellationToken)
        {
            ReadCalls++;
            ReadToken = cancellationToken;
            var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
                completion.TrySetCanceled());
            Entered.TrySetResult(true);
            return await completion.Task.ConfigureAwait(false);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new AssertionException("Transport tests must not perform synchronous I/O.");
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    internal sealed class TokenRecordingReadStream : MemoryStream
    {
        public TokenRecordingReadStream(byte[] bytes)
            : base(bytes, writable: false)
        {
        }

        public CancellationToken ReadToken { get; private set; }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ReadToken = cancellationToken;
            return base.ReadAsync(buffer, offset, count, cancellationToken);
        }

#if NET8_0_OR_GREATER
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadToken = cancellationToken;
            return base.ReadAsync(buffer, cancellationToken);
        }
#endif
    }
}
