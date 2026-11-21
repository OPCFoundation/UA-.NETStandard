// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// Regression tests for the WebApi body-size hardening. The REST
    /// codec must enforce <see cref="IServiceMessageContext.MaxMessageSize"/>
    /// during the body read — buffering the full body before the size
    /// check lets an oversized or chunked / no-Content-Length body
    /// exhaust memory before the quota kicks in. CWE-770.
    /// </summary>
    [TestFixture]
    [Category("WebApiSecurity")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class WebApiBodyCodecBoundedReadTests
    {
        [Test]
        public async Task DecodeBodyAsyncRejectsOversizedBody()
        {
            // 4 KB payload, MaxMessageSize 512 → must throw BadRequestTooLarge
            // BEFORE the in-buffer DecodeBody size check (which uses
            // BadEncodingLimitsExceeded). Pinning this distinction proves the
            // bounded read short-circuits the allocation.
            byte[] payload = Encoding.UTF8.GetBytes(new string('a', 4096));
            using var stream = new MemoryStream(payload);

            var context = ServiceMessageContext.CreateEmpty(new TelemetryStub());
            context.MaxMessageSize = 512;

            ServiceResultException ex = (await Assert.ThrowsAsync<ServiceResultException>(async () => _ = await WebApiBodyCodec
                    .DecodeBodyAsync<ReadRequest>(stream, context, ct: CancellationToken.None)
                    .ConfigureAwait(false)))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadRequestTooLarge),
                "WebApiBodyCodec.DecodeBodyAsync must short-circuit via the bounded " +
                "read (BadRequestTooLarge), not allocate the full payload and then " +
                "fail at decode-time (BadEncodingLimitsExceeded).");
        }

        [Test]
        public async Task DecodeBodyAsyncAcceptsBodyWithinLimitAsync()
        {
            // Empty JSON object decodes as a ReadRequest with default fields.
            byte[] payload = Encoding.UTF8.GetBytes("{}");
            using var stream = new MemoryStream(payload);

            var context = ServiceMessageContext.CreateEmpty(new TelemetryStub());
            context.MaxMessageSize = 4096;
            context.Factory.Builder.AddEncodeableTypes(typeof(ReadRequest).Assembly).Commit();

            ReadRequest decoded = await WebApiBodyCodec
                .DecodeBodyAsync<ReadRequest>(stream, context, ct: CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(decoded, Is.Not.Null);
        }

        [Test]
        public async Task DecodeBodyAsyncWithoutMaxLimitAcceptsAnyBodyAsync()
        {
            byte[] payload = Encoding.UTF8.GetBytes("{}");
            using var stream = new MemoryStream(payload);

            var context = ServiceMessageContext.CreateEmpty(new TelemetryStub());
            context.MaxMessageSize = 0;  // Disable cap.
            context.Factory.Builder.AddEncodeableTypes(typeof(ReadRequest).Assembly).Commit();

            ReadRequest decoded = await WebApiBodyCodec
                .DecodeBodyAsync<ReadRequest>(stream, context, ct: CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(decoded, Is.Not.Null,
                "MaxMessageSize <= 0 disables the cap (preserves historical behaviour).");
        }

        private sealed class TelemetryStub : TelemetryContextBase
        {
            public TelemetryStub()
                : base(NullLoggerFactory.Instance)
            {
            }
        }
    }
}
