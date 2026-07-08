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
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.WebApi
{
    /// <summary>
    /// Unit tests for the <see cref="WebApiBodyCodec"/> — envelope-less
    /// OPC UA JSON encode / decode used by the HTTPS REST binding
    /// (Part 6 §G.3 "OpenAPI Mapping").
    /// </summary>
    [TestFixture]
    [Category("WebApiBodyCodec")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class WebApiBodyCodecTests
    {
        [Test]
        public void EncodeReadRequestProducesEnvelopeLessRoot()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(
                NUnitTelemetryContext.Create());
            ReadRequest request = BuildReadRequest();

            byte[] payload = WebApiBodyCodec.EncodeBody(request, context, JsonEncoderOptions.Compact);

            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            Assert.That(root.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(
                root.TryGetProperty("UaTypeId", out _),
                Is.False,
                "REST body must not carry the {UaTypeId, UaBody} envelope written by JsonEncoder.EncodeMessage");
            Assert.That(
                root.TryGetProperty("UaBody", out _),
                Is.False,
                "REST body must not carry the {UaTypeId, UaBody} envelope written by JsonEncoder.EncodeMessage");
            Assert.That(
                root.TryGetProperty("RequestHeader", out _),
                Is.True,
                "REST body root object should expose the request's own fields directly");
        }

        [Test]
        public void EncodeAndDecodeRoundTripsReadRequestInCompact()
        {
            AssertReadRequestRoundTrip(JsonEncoderOptions.Compact);
        }

        [Test]
        public void EncodeAndDecodeRoundTripsReadRequestInVerbose()
        {
            AssertReadRequestRoundTrip(JsonEncoderOptions.Verbose);
        }

        [Test]
        public async Task DecodeBodyAsyncReadsFromStream()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(
                NUnitTelemetryContext.Create());
            ReadRequest original = BuildReadRequest();

            byte[] payload = WebApiBodyCodec.EncodeBody(
                original, context, JsonEncoderOptions.Compact);

            using var stream = new MemoryStream(payload);
            ReadRequest decoded = await WebApiBodyCodec
                .DecodeBodyAsync<ReadRequest>(stream, context)
                .ConfigureAwait(false);

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.RequestHeader.RequestHandle, Is.EqualTo(original.RequestHeader.RequestHandle));
            Assert.That(decoded.NodesToRead, Has.Count.EqualTo(original.NodesToRead.Count));
        }

        [Test]
        public async Task EncodeBodyAsyncWritesToStream()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(
                NUnitTelemetryContext.Create());
            ReadRequest original = BuildReadRequest();

            using var destination = new MemoryStream();
            await WebApiBodyCodec
                .EncodeBodyAsync(original, destination, context, JsonEncoderOptions.Compact)
                .ConfigureAwait(false);

            destination.Position = 0;
            ReadRequest decoded = await WebApiBodyCodec
                .DecodeBodyAsync<ReadRequest>(destination, context)
                .ConfigureAwait(false);

            Assert.That(decoded.RequestHeader.RequestHandle, Is.EqualTo(original.RequestHeader.RequestHandle));
        }

        [Test]
        public void EncodeAndDecodeRoundTripsReadResponseWithVariantPayloads()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(
                NUnitTelemetryContext.Create());

            DateTimeUtc sourceTimestamp = DateTimeUtc.Now;
            var original = new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 1000,
                    ServiceResult = StatusCodes.Good
                },
                Results = new ArrayOf<DataValue>(new DataValue[]
                {
                    new(
                        new Variant(42),
                        StatusCodes.Good,
                        sourceTimestamp),
                    new(
                        new Variant("hello"),
                        StatusCodes.GoodEntryInserted)
                }.AsMemory())
            };

            byte[] payload = WebApiBodyCodec.EncodeBody(
                original, context, JsonEncoderOptions.Compact);

            ReadResponse decoded = WebApiBodyCodec.DecodeBody<ReadResponse>(payload, context);

            Assert.That(decoded.ResponseHeader.RequestHandle, Is.EqualTo(1000u));
            Assert.That(decoded.Results, Has.Count.EqualTo(2));
            Assert.That(decoded.Results[0].WrappedValue.TryGetValue(out int intValue), Is.True);
            Assert.That(intValue, Is.EqualTo(42));
            Assert.That(decoded.Results[1].WrappedValue.TryGetValue(out string? stringValue), Is.True);
            Assert.That(stringValue, Is.EqualTo("hello"));
        }

        [Test]
        public void DecodeBodyThrowsBadDecodingErrorOnMalformedJson()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(
                NUnitTelemetryContext.Create());
            byte[] payload = Encoding.UTF8.GetBytes("not a json document");

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => WebApiBodyCodec.DecodeBody<ReadRequest>(payload, context))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadDecodingError));
        }

        [Test]
        public void DecodeBodyThrowsBadEncodingLimitsExceededWhenPayloadTooLarge()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(
                NUnitTelemetryContext.Create());
            context.MaxMessageSize = 16;

            byte[] payload = Encoding.UTF8.GetBytes("{\"NodesToRead\": []}");
            Assert.That(payload, Has.Length.GreaterThan(context.MaxMessageSize));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => WebApiBodyCodec.DecodeBody<ReadRequest>(payload, context))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void DecodeBodyAcceptsEmptyJsonObject()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(
                NUnitTelemetryContext.Create());
            byte[] payload = Encoding.UTF8.GetBytes("{}");

            ReadRequest decoded = WebApiBodyCodec.DecodeBody<ReadRequest>(payload, context);

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.NodesToRead, Has.Count.Zero);
        }

        [Test]
        public void EncodeBodyNullValueThrowsArgumentNullException()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(
                NUnitTelemetryContext.Create());

            Assert.Throws<ArgumentNullException>(
                () => WebApiBodyCodec.EncodeBody<ReadRequest>(null!, context));
        }

        [Test]
        public void EncodeBodyNullContextThrowsArgumentNullException()
        {
            ReadRequest request = BuildReadRequest();

            Assert.Throws<ArgumentNullException>(
                () => WebApiBodyCodec.EncodeBody(request, null!));
        }

        [Test]
        public void DecodeBodyNullPayloadThrowsArgumentNullException()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(
                NUnitTelemetryContext.Create());

            Assert.Throws<ArgumentNullException>(
                () => WebApiBodyCodec.DecodeBody<ReadRequest>(null!, context));
        }

        [Test]
        public void DecodeBodyAsyncNullStreamThrowsArgumentNullException()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(
                NUnitTelemetryContext.Create());

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await WebApiBodyCodec
                    .DecodeBodyAsync<ReadRequest>(null!, context, ct: CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public void EncodeBodyCompactOmitsDefaultValuesPerSpec()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(
                NUnitTelemetryContext.Create());

            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader(),
                NodesToRead = new ArrayOf<ReadValueId>()
            };

            byte[] compactBytes = WebApiBodyCodec.EncodeBody(request, context, JsonEncoderOptions.Compact);
            byte[] verboseBytes = WebApiBodyCodec.EncodeBody(request, context, JsonEncoderOptions.Verbose);

            Assert.That(
                compactBytes,
                Has.Length.LessThan(verboseBytes.Length),
                "Compact must omit default values per Part 6 §5.4.9");
        }

        private static ReadRequest BuildReadRequest()
        {
            return new ReadRequest
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
                NodesToRead = new ArrayOf<ReadValueId>(new ReadValueId[]
                {
                    new() {
                        NodeId = new NodeId("Var1", 2),
                        AttributeId = Attributes.Value
                    },
                    new() {
                        NodeId = new NodeId(42u, 0),
                        AttributeId = Attributes.DisplayName
                    }
                }.AsMemory())
            };
        }

        private static void AssertReadRequestRoundTrip(JsonEncoderOptions options)
        {
            ServiceMessageContext context = ServiceMessageContext.Create(
                NUnitTelemetryContext.Create());
            ReadRequest original = BuildReadRequest();

            byte[] payload = WebApiBodyCodec.EncodeBody(original, context, options);
            ReadRequest decoded = WebApiBodyCodec.DecodeBody<ReadRequest>(payload, context);

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.RequestHeader.RequestHandle, Is.EqualTo(12345u));
            Assert.That(decoded.RequestHeader.TimeoutHint, Is.EqualTo(1000u));
            Assert.That(decoded.MaxAge, Is.EqualTo(250d));
            Assert.That(decoded.TimestampsToReturn, Is.EqualTo(TimestampsToReturn.Both));
            Assert.That(decoded.NodesToRead, Has.Count.EqualTo(2));
            Assert.That(decoded.NodesToRead[0].NodeId, Is.EqualTo(original.NodesToRead[0].NodeId));
            Assert.That(decoded.NodesToRead[0].AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(decoded.NodesToRead[1].NodeId, Is.EqualTo(original.NodesToRead[1].NodeId));
            Assert.That(decoded.NodesToRead[1].AttributeId, Is.EqualTo(Attributes.DisplayName));
        }

        [Test]
        public void DecodeBodyNonGenericReturnsConcreteType()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(
                NUnitTelemetryContext.Create());
            ReadRequest original = BuildReadRequest();
            byte[] payload = WebApiBodyCodec.EncodeBody(original, context, JsonEncoderOptions.Compact);

#pragma warning disable CA2263 // Prefer generic overload when type is known
            IEncodeable decoded = WebApiBodyCodec.DecodeBody(typeof(ReadRequest), payload, context);
#pragma warning restore CA2263 // Prefer generic overload when type is known

            Assert.That(decoded, Is.InstanceOf<ReadRequest>());
            var typed = (ReadRequest)decoded;
            Assert.That(typed.NodesToRead, Has.Count.EqualTo(2));
            Assert.That(typed.MaxAge, Is.EqualTo(250d));
        }

        [Test]
        public async Task DecodeBodyAsyncNonGenericRoundTripsResponse()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(
                NUnitTelemetryContext.Create());

            var original = new ReadResponse
            {
                ResponseHeader = new ResponseHeader { RequestHandle = 99 },
                Results = new ArrayOf<DataValue>(new[]
                {
                    new DataValue(new Variant(42), StatusCodes.Good, DateTime.UtcNow)
                }.AsMemory())
            };
            byte[] payload = WebApiBodyCodec.EncodeBody(original, context, JsonEncoderOptions.Compact);
            using var stream = new MemoryStream(payload);

            IEncodeable decoded = await WebApiBodyCodec
                .DecodeBodyAsync(typeof(ReadResponse), stream, context)
                .ConfigureAwait(false);

            Assert.That(decoded, Is.InstanceOf<ReadResponse>());
            var typed = (ReadResponse)decoded;
            Assert.That(typed.ResponseHeader.RequestHandle, Is.EqualTo(99u));
            Assert.That(typed.Results, Has.Count.EqualTo(1));
            Assert.That(typed.Results[0].WrappedValue.TryGetValue(out int v), Is.True);
            Assert.That(v, Is.EqualTo(42));
        }

        [Test]
        public void DecodeBodyNonGenericRejectsNonEncodeableType()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(
                NUnitTelemetryContext.Create());
            byte[] payload = Encoding.UTF8.GetBytes("{}");

            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => WebApiBodyCodec.DecodeBody(typeof(string), payload, context))!;
            Assert.That(ex.Message, Does.Contain("IEncodeable"));
        }

        [Test]
        public void DecodeBodyNonGenericRejectsNullBodyType()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(
                NUnitTelemetryContext.Create());

            Assert.Throws<ArgumentNullException>(
                () => WebApiBodyCodec.DecodeBody(null!, [], context));
        }
    }
}
