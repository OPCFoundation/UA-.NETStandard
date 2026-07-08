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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Smoke tests for <see cref="JsonRequestMapper"/> — the shared JSON
    /// encode / decode helper used by the upcoming HTTPS-JSON and
    /// WSS opcua+uajson handlers. Comprehensive coverage lives in the
    /// p5-unit-jsonmapper expansion.
    /// </summary>
    [TestFixture]
    [Category("JsonRequestMapper")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class JsonRequestMapperTests
    {
        [Test]
        public async Task RoundTripsServiceFaultViaJsonAsync()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            var response = new ServiceFault
            {
                ResponseHeader = new ResponseHeader
                {
                    ServiceResult = StatusCodes.BadInternalError,
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 42
                }
            };

            byte[] encoded = JsonRequestMapper.EncodeResponse(response, context);
            Assert.That(encoded, Has.Length.GreaterThan(0));

            string payload = Encoding.UTF8.GetString(encoded);
            Assert.That(payload, Does.Contain("TypeId"));
            Assert.That(payload, Does.Contain("Body"));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task EncodeResponseToStreamWritesSameBytesAsArrayHelper()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            var response = new GetEndpointsResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    ServiceResult = StatusCodes.Good,
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 7
                }
            };

            byte[] expected = JsonRequestMapper.EncodeResponse(response, context);

            using var stream = new MemoryStream();
            await JsonRequestMapper.EncodeResponseAsync(response, context, stream, CancellationToken.None)
                .ConfigureAwait(false);
            byte[] actual = stream.ToArray();

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void DecodeRequestRejectsMalformedBody()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            byte[] junk = Encoding.UTF8.GetBytes("this is not json");
            using var stream = new MemoryStream(junk);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await JsonRequestMapper.DecodeRequestAsync(stream, context, CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadDecodingError));
        }

        [Test]
        public void DecodeRequestRejectsEmptyBody()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            using var stream = new MemoryStream([]);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await JsonRequestMapper.DecodeRequestAsync(stream, context, CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadDecodingError));
        }

        [Test]
        public async Task EncodeAndDecodeRoundTripsReadRequest()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            var nodesToRead = new ArrayOf<ReadValueId>(new ReadValueId[]
            {
                new ReadValueId
                {
                    NodeId = new NodeId("Var1", 2),
                    AttributeId = Attributes.Value
                },
                new ReadValueId
                {
                    NodeId = new NodeId(42u, 0),
                    AttributeId = Attributes.DisplayName
                }
            }.AsMemory());
            var original = new ReadRequest
            {
                RequestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 12345,
                    TimeoutHint = 1000,
                    AuthenticationToken = new NodeId(Guid.NewGuid(), 0)
                },
                MaxAge = 250,
                TimestampsToReturn = TimestampsToReturn.Both,
                NodesToRead = nodesToRead
            };

            byte[] encoded = EncodeRequest(original, context);
            using var stream = new MemoryStream(encoded);

            IServiceRequest decoded = await JsonRequestMapper
                .DecodeRequestAsync(stream, context, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(decoded, Is.InstanceOf<ReadRequest>());
            var read = (ReadRequest)decoded;
            Assert.That(read.RequestHeader.RequestHandle, Is.EqualTo(12345u));
            Assert.That(read.RequestHeader.TimeoutHint, Is.EqualTo(1000u));
            Assert.That(read.TimestampsToReturn, Is.EqualTo(TimestampsToReturn.Both));
            Assert.That(read.MaxAge, Is.EqualTo(250).Within(0.001));
            Assert.That(read.NodesToRead.Count, Is.EqualTo(2));
            Assert.That(read.NodesToRead[0].AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(read.NodesToRead[1].NodeId, Is.EqualTo(original.NodesToRead[1].NodeId));
        }

        [Test]
        public async Task RoundTripsReadResponseWithMultipleDataValues()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            var results = new ArrayOf<DataValue>(new DataValue[]
            {
                new DataValue(new Variant(123), StatusCodes.Good),
                new DataValue(new Variant("hello"), StatusCodes.Good),
                new DataValue(Variant.Null, StatusCodes.BadAttributeIdInvalid)
            }.AsMemory());
            var response = new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    ServiceResult = StatusCodes.Good,
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 99
                },
                Results = results
            };

            byte[] encoded = JsonRequestMapper.EncodeResponse(response, context);
            using var stream = new MemoryStream();
            await JsonRequestMapper.EncodeResponseAsync(response, context, stream, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(stream.ToArray(), Is.EqualTo(encoded));

            // The encoded shape is recognised as a service response with the
            // standard envelope (TypeId / Body) - sanity-check both elements.
            string payload = Encoding.UTF8.GetString(encoded);
            Assert.That(payload, Does.Contain("TypeId"));
            Assert.That(payload, Does.Contain("Body"));
            Assert.That(payload, Does.Contain("Results"));
        }

        [Test]
        public void DecodeRequestEnforcesMaxMessageSize()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext smallContext = ServiceMessageContext.Create(telemetry);
            smallContext.MaxMessageSize = 32; // far smaller than any real envelope

            // Encode a real request using a permissive context so payload exists.
            ServiceMessageContext fullContext = ServiceMessageContext.Create(telemetry);
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 1 }
            };
            byte[] encoded = EncodeRequest(request, fullContext);
            Assert.That(encoded, Has.Length.GreaterThan(smallContext.MaxMessageSize));

            using var stream = new MemoryStream(encoded);

            // The body is now bounded by MaxMessageSize at read time (before
            // decode), so an oversized body is rejected with BadRequestTooLarge
            // rather than buffered in full and rejected by the decoder.
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await JsonRequestMapper
                    .DecodeRequestAsync(stream, smallContext, CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadRequestTooLarge));
        }

        [Test]
        public void ReadAllBoundedRejectsBodyExceedingMaxLength()
        {
            byte[] body = new byte[1024];
            using var stream = new MemoryStream(body);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await JsonRequestMapper
                    .ReadAllBoundedAsync(stream, 256, CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadRequestTooLarge));
        }

        [Test]
        public async Task ReadAllBoundedAllowsBodyExactlyAtLimitAsync()
        {
            byte[] body = new byte[256];
            using var stream = new MemoryStream(body);

            byte[] result = await JsonRequestMapper
                .ReadAllBoundedAsync(stream, 256, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result, Has.Length.EqualTo(256));
        }

        [Test]
        public async Task ReadAllBoundedWithNonPositiveMaxReadsEntireBodyAsync()
        {
            byte[] body = new byte[4096];
            for (int i = 0; i < body.Length; i++)
            {
                body[i] = (byte)(i & 0xFF);
            }
            using var stream = new MemoryStream(body);

            byte[] result = await JsonRequestMapper
                .ReadAllBoundedAsync(stream, 0, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(body));
        }

        [Test]
        public void EncodeResponseThrowsOnNullArguments()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            var response = new ServiceFault();

            Assert.Throws<ArgumentNullException>(
                () => JsonRequestMapper.EncodeResponse(null!, context));
            Assert.Throws<ArgumentNullException>(
                () => JsonRequestMapper.EncodeResponse(response, null!));
        }

        [Test]
        public void DecodeRequestThrowsOnNullArguments()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            using var stream = new MemoryStream();

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await JsonRequestMapper
                    .DecodeRequestAsync(null!, context, CancellationToken.None)
                    .ConfigureAwait(false));
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await JsonRequestMapper
                    .DecodeRequestAsync(stream, null!, CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public void DecodeRequestPropagatesCancellation()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // A stream that always observes the cancellation token before
            // completing its read - models a network stream with no buffered bytes.
            using var stream = new CancellableStream();
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await JsonRequestMapper
                    .DecodeRequestAsync(stream, context, cts.Token)
                    .ConfigureAwait(false));
        }

        private static byte[] EncodeRequest(ReadRequest request, IServiceMessageContext context)
        {
            using var memory = new MemoryStream();
            using (var encoder = new JsonEncoder(memory, context, JsonEncoderOptions.Compact))
            {
                encoder.EncodeMessage(request, request.TypeId);
                encoder.Close();
            }
            return memory.ToArray();
        }

        /// <summary>
        /// A read-only stream that always honours its caller's cancellation
        /// token on the first asynchronous read.
        /// </summary>
        private sealed class CancellableStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }
            public override int Read(byte[] buffer, int offset, int count) => 0;

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(0);
            }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<int>(0);
            }
#endif

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
