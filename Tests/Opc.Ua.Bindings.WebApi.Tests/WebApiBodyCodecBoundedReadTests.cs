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

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Opc.Ua.Bindings.WebApi.Tests
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
        public void DecodeBodyAsyncRejectsOversizedBody()
        {
            // 4 KB payload, MaxMessageSize 512 → must throw BadRequestTooLarge
            // BEFORE the in-buffer DecodeBody size check (which uses
            // BadEncodingLimitsExceeded). Pinning this distinction proves the
            // bounded read short-circuits the allocation.
            byte[] payload = Encoding.UTF8.GetBytes(new string('a', 4096));
            using var stream = new MemoryStream(payload);

            var context = ServiceMessageContext.CreateEmpty(new TelemetryStub());
            context.MaxMessageSize = 512;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                _ = await WebApiBodyCodec
                    .DecodeBodyAsync<ReadRequest>(stream, context, ct: CancellationToken.None)
                    .ConfigureAwait(false);
            })!;

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
