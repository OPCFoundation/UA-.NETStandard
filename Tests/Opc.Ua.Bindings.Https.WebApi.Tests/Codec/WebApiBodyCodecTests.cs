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
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Opc.Ua.Bindings.Https.WebApi.Tests.Codec
{
    /// <summary>
    /// Unit tests for <see cref="WebApiBodyCodec"/>: encode/decode round-trips
    /// (Compact + Verbose flavours), the <see cref="IBufferWriter{T}"/> overload,
    /// the non-generic <see cref="WebApiBodyCodec.DecodeBody(Type, byte[], IServiceMessageContext, JsonDecoderOptions?)"/>
    /// / <see cref="WebApiBodyCodec.DecodeBodyAsync(Type, Stream, IServiceMessageContext, JsonDecoderOptions?, CancellationToken)"/>
    /// paths, argument-null guards, and error-classification
    /// (malformed JSON → BadDecodingError, payload exceeds quota →
    /// BadEncodingLimitsExceeded).
    /// </summary>
    [TestFixture]
    [Category("WebApiBodyCodec")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class WebApiBodyCodecTests
    {
        private ServiceMessageContext m_context = null!;

        [SetUp]
        public void SetUp()
        {
            m_context = ServiceMessageContext.CreateEmpty(new TelemetryStub());
            m_context.Factory.Builder
                .AddEncodeableTypes(typeof(ReadRequest).Assembly)
                .Commit();
        }

        // ─────────────────── EncodeBody / DecodeBody round-trips ────────────

        [Test]
        public void EncodeBodyDecodeBodyRoundTripCompact()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 99 },
                NodesToRead = new ArrayOf<ReadValueId>()
            };

            byte[] encoded = WebApiBodyCodec.EncodeBody(
                request,
                m_context,
                WebApiMediaType.ToEncoderOptions(WebApiEncoding.Compact));

            Assert.That(encoded, Is.Not.Null);
            Assert.That(encoded, Has.Length.GreaterThan(0));

            ReadRequest decoded = WebApiBodyCodec.DecodeBody<ReadRequest>(encoded, m_context);

            Assert.That(decoded.RequestHeader.RequestHandle, Is.EqualTo(99u));
        }

        [Test]
        public void EncodeBodyDecodeBodyRoundTripVerbose()
        {
            var response = new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    RequestHandle = 77,
                    ServiceResult = StatusCodes.Good,
                    Timestamp = DateTime.UtcNow,
                    StringTable = new ArrayOf<string>(),
                    AdditionalHeader = new ExtensionObject()
                },
                Results = new ArrayOf<DataValue>()
            };

            byte[] encoded = WebApiBodyCodec.EncodeBody(
                response,
                m_context,
                WebApiMediaType.ToEncoderOptions(WebApiEncoding.Verbose));

            Assert.That(encoded, Is.Not.Null);
            Assert.That(encoded, Has.Length.GreaterThan(0));

            ReadResponse decoded = WebApiBodyCodec.DecodeBody<ReadResponse>(encoded, m_context);

            Assert.That(decoded.ResponseHeader.RequestHandle, Is.EqualTo(77u));
        }

        [Test]
        public void EncodeBodyDecodeBodyRoundTripForWriteRequest()
        {
            var request = new WriteRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 55 },
                NodesToWrite = new ArrayOf<WriteValue>()
            };

            byte[] encoded = WebApiBodyCodec.EncodeBody(request, m_context);
            WriteRequest decoded = WebApiBodyCodec.DecodeBody<WriteRequest>(encoded, m_context);

            Assert.That(decoded.RequestHeader.RequestHandle, Is.EqualTo(55u));
        }

        [Test]
        public void EncodeBodyDecodeBodyRoundTripForServiceFault()
        {
            var fault = new ServiceFault
            {
                ResponseHeader = new ResponseHeader
                {
                    RequestHandle = 11,
                    ServiceResult = StatusCodes.BadUnexpectedError,
                    Timestamp = DateTime.UtcNow,
                    StringTable = new ArrayOf<string>(),
                    AdditionalHeader = new ExtensionObject()
                }
            };

            byte[] encoded = WebApiBodyCodec.EncodeBody(fault, m_context);
            ServiceFault decoded = WebApiBodyCodec.DecodeBody<ServiceFault>(encoded, m_context);

            Assert.That(decoded.ResponseHeader.RequestHandle, Is.EqualTo(11u));
            Assert.That(decoded.ResponseHeader.ServiceResult,
                Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        // ────────────────────── EncodeBody null guards ──────────────────────

        [Test]
        public void EncodeBodyThrowsForNullValue()
        {
            Assert.That(
                () => WebApiBodyCodec.EncodeBody<ReadRequest>(null!, m_context),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("value"));
        }

        [Test]
        public void EncodeBodyThrowsForNullContext()
        {
            var request = new ReadRequest { RequestHeader = new RequestHeader() };

            Assert.That(
                () => WebApiBodyCodec.EncodeBody(request, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("context"));
        }

        // ──────────────────── EncodeBodyAsync round-trip ────────────────────

        [Test]
        public async Task EncodeBodyAsyncWritesToStreamAsync()
        {
            var request = new BrowseRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 33 },
                View = new ViewDescription(),
                NodesToBrowse = new ArrayOf<BrowseDescription>()
            };

            using var destination = new MemoryStream();

            await WebApiBodyCodec
                .EncodeBodyAsync(request, destination, m_context,
                    WebApiMediaType.ToEncoderOptions(WebApiEncoding.Compact))
                .ConfigureAwait(false);

            destination.Position = 0;
            BrowseRequest decoded = await WebApiBodyCodec
                .DecodeBodyAsync<BrowseRequest>(destination, m_context)
                .ConfigureAwait(false);

            Assert.That(decoded.RequestHeader.RequestHandle, Is.EqualTo(33u));
        }

        [Test]
        public void EncodeBodyAsyncThrowsForNullValue()
        {
            using var stream = new MemoryStream();

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await WebApiBodyCodec
                    .EncodeBodyAsync<ReadRequest>(null!, stream, m_context)
                    .ConfigureAwait(false));
        }

        [Test]
        public void EncodeBodyAsyncThrowsForNullDestination()
        {
            var request = new ReadRequest { RequestHeader = new RequestHeader() };

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await WebApiBodyCodec
                    .EncodeBodyAsync(request, null!, m_context)
                    .ConfigureAwait(false));
        }

        // ─────────────────── EncodeBody to IBufferWriter ────────────────────

        [Test]
        public void EncodeBodyToBufferWriterProducesDecodableBytes()
        {
            var request = new CallRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 66 },
                MethodsToCall = new ArrayOf<CallMethodRequest>()
            };

            var buffer = new ArrayBufferWriter<byte>();

            WebApiBodyCodec.EncodeBody(request, buffer, m_context);

            Assert.That(buffer.WrittenCount, Is.GreaterThan(0));

            var sequence = new ReadOnlySequence<byte>(buffer.WrittenMemory);
            CallRequest decoded = WebApiBodyCodec.DecodeBody<CallRequest>(sequence, m_context);

            Assert.That(decoded.RequestHeader.RequestHandle, Is.EqualTo(66u));
        }

        [Test]
        public void EncodeBodyToBufferWriterThrowsForNullValue()
        {
            var buffer = new ArrayBufferWriter<byte>();

            Assert.That(
                () => WebApiBodyCodec.EncodeBody<ReadRequest>(null!, buffer, m_context),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("value"));
        }

        [Test]
        public void EncodeBodyToBufferWriterThrowsForNullDestination()
        {
            var request = new ReadRequest { RequestHeader = new RequestHeader() };

            Assert.That(
                () => WebApiBodyCodec.EncodeBody(request, null!, m_context),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("destination"));
        }

        [Test]
        public void EncodeBodyToBufferWriterThrowsForNullContext()
        {
            var request = new ReadRequest { RequestHeader = new RequestHeader() };
            var buffer = new ArrayBufferWriter<byte>();

            Assert.That(
                () => WebApiBodyCodec.EncodeBody(request, buffer, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("context"));
        }

        // ──────────────────── DecodeBody from ReadOnlySequence ──────────────

        [Test]
        public void DecodeBodyFromSequenceRoundTrip()
        {
            var request = new GetEndpointsRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 88 }
            };

            byte[] encoded = WebApiBodyCodec.EncodeBody(request, m_context);
            var sequence = new ReadOnlySequence<byte>(encoded);

            GetEndpointsRequest decoded = WebApiBodyCodec.DecodeBody<GetEndpointsRequest>(
                sequence, m_context);

            Assert.That(decoded.RequestHeader.RequestHandle, Is.EqualTo(88u));
        }

        [Test]
        public void DecodeBodyFromSequenceThrowsForNullContext()
        {
            var sequence = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("{}"));

            Assert.That(
                () => WebApiBodyCodec.DecodeBody<ReadRequest>(sequence, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("context"));
        }

        [Test]
        public void DecodeBodyFromSequenceThrowsBadEncodingLimitsExceededWhenOversized()
        {
            byte[] big = Encoding.UTF8.GetBytes(new string('a', 1024));
            var sequence = new ReadOnlySequence<byte>(big);

            var ctx = ServiceMessageContext.CreateEmpty(new TelemetryStub());
            ctx.MaxMessageSize = 100;

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                WebApiBodyCodec.DecodeBody<ReadRequest>(sequence, ctx))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        // ──────────────────── DecodeBody from byte[] ────────────────────────

        [Test]
        public void DecodeBodyFromByteArrayThrowsForNullPayload()
        {
            Assert.That(
                () => WebApiBodyCodec.DecodeBody<ReadRequest>(null!, m_context),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("payload"));
        }

        [Test]
        public void DecodeBodyFromByteArrayRoundTrip()
        {
            var request = new CreateSessionRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 123 },
                ClientDescription = new ApplicationDescription(),
                ClientNonce = new ByteString(),
                ClientCertificate = new ByteString()
            };

            byte[] encoded = WebApiBodyCodec.EncodeBody(request, m_context);
            CreateSessionRequest decoded =
                WebApiBodyCodec.DecodeBody<CreateSessionRequest>(encoded, m_context);

            Assert.That(decoded.RequestHeader.RequestHandle, Is.EqualTo(123u));
        }

        [Test]
        public void DecodeBodyThrowsBadDecodingErrorForMalformedJson()
        {
            byte[] malformed = Encoding.UTF8.GetBytes("not-valid-json{{{");

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => WebApiBodyCodec.DecodeBody<ReadRequest>(malformed, m_context))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        // ─────────────────── DecodeBodyAsync (stream, generic) ──────────────

        [Test]
        public void DecodeBodyAsyncThrowsForNullBody()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await WebApiBodyCodec
                    .DecodeBodyAsync<ReadRequest>(null!, m_context)
                    .ConfigureAwait(false));
        }

        [Test]
        public void DecodeBodyAsyncThrowsForNullContext()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await WebApiBodyCodec
                    .DecodeBodyAsync<ReadRequest>(stream, null!)
                    .ConfigureAwait(false));
        }

        // ──────────────────── Non-generic DecodeBody (Type, byte[]) ─────────

#pragma warning disable IL2026  // RequiresUnreferencedCode: test intentionally exercises non-generic path
        [Test]
        public void NonGenericDecodeBodyRoundTrip()
        {
            var request = new BrowseNextRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 44 },
                ReleaseContinuationPoints = false,
                ContinuationPoints = new ArrayOf<ByteString>()
            };

            byte[] encoded = WebApiBodyCodec.EncodeBody(request, m_context);

#pragma warning disable CA2263  // test intentionally exercises the non-generic (Type, byte[]) path
            IEncodeable decoded = WebApiBodyCodec.DecodeBody(
                typeof(BrowseNextRequest), encoded, m_context);
#pragma warning restore CA2263

            Assert.That(decoded, Is.InstanceOf<BrowseNextRequest>());
            Assert.That(((BrowseNextRequest)decoded).RequestHeader.RequestHandle, Is.EqualTo(44u));
        }

        [Test]
        public void NonGenericDecodeBodyThrowsForNullBodyType()
        {
            byte[] payload = Encoding.UTF8.GetBytes("{}");

            Assert.That(
                () => WebApiBodyCodec.DecodeBody(null!, payload, m_context),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("bodyType"));
        }

        [Test]
        public void NonGenericDecodeBodyThrowsForNullPayload()
        {
            Assert.That(
                () => WebApiBodyCodec.DecodeBody(typeof(ReadRequest), null!, m_context),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("payload"));
        }

        [Test]
        public void NonGenericDecodeBodyThrowsForNullContext()
        {
            byte[] payload = Encoding.UTF8.GetBytes("{}");

            Assert.That(
                () => WebApiBodyCodec.DecodeBody(typeof(ReadRequest), payload, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("context"));
        }

        [Test]
        public void NonGenericDecodeBodyThrowsForNonIEncodeableType()
        {
            byte[] payload = Encoding.UTF8.GetBytes("{}");

            Assert.That(
                () => WebApiBodyCodec.DecodeBody(typeof(string), payload, m_context),
                Throws.InstanceOf<ArgumentException>()
                    .With.Property("ParamName").EqualTo("bodyType"));
        }

        // ─────────────── Non-generic DecodeBodyAsync (Type, Stream) ─────────

        [Test]
        public async Task NonGenericDecodeBodyAsyncRoundTripAsync()
        {
            var request = new TranslateBrowsePathsToNodeIdsRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 99 },
                BrowsePaths = new ArrayOf<BrowsePath>()
            };

            byte[] encoded = WebApiBodyCodec.EncodeBody(request, m_context);
            using var stream = new MemoryStream(encoded);

            IEncodeable decoded = await WebApiBodyCodec
                .DecodeBodyAsync(typeof(TranslateBrowsePathsToNodeIdsRequest), stream, m_context)
                .ConfigureAwait(false);

            Assert.That(decoded, Is.InstanceOf<TranslateBrowsePathsToNodeIdsRequest>());
            Assert.That(
                ((TranslateBrowsePathsToNodeIdsRequest)decoded).RequestHeader.RequestHandle,
                Is.EqualTo(99u));
        }

        [Test]
        public void NonGenericDecodeBodyAsyncThrowsForNullBody()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await WebApiBodyCodec
                    .DecodeBodyAsync(typeof(ReadRequest), null!, m_context)
                    .ConfigureAwait(false));
        }

        [Test]
        public void NonGenericDecodeBodyAsyncThrowsForNullContext()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await WebApiBodyCodec
                    .DecodeBodyAsync(typeof(ReadRequest), stream, null!)
                    .ConfigureAwait(false));
        }
#pragma warning restore IL2026

        // ─────────────────────────── Helpers ────────────────────────────────

        private sealed class TelemetryStub : TelemetryContextBase
        {
            public TelemetryStub()
                : base(NullLoggerFactory.Instance)
            {
            }
        }
    }
}
