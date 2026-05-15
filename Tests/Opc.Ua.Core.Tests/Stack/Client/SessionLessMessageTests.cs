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

using System;
using System.IO;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests
{
    [TestFixture]
    [Category("SessionLess")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class SessionLessMessageTests
    {
        private ServiceMessageContext m_context;

        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_context = ServiceMessageContext.Create(telemetry);
        }

        [Test]
        public void DecodeAsJsonThrowsOnNullBuffer()
        {
            Assert.That(
                () => SessionLessMessage.DecodeAsJson(null, m_context),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void DecodeAsJsonThrowsOnNullContext()
        {
            Assert.That(
                () => SessionLessMessage.DecodeAsJson([], null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void EncodeAsJsonThrowsOnNullMessage()
        {
            using var stream = new MemoryStream();
            Assert.That(
                () => SessionLessMessage.EncodeAsJson(null, stream, m_context, true),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void EncodeAsJsonThrowsOnNullContext()
        {
            using var stream = new MemoryStream();
            Assert.That(
                () => SessionLessMessage.EncodeAsJson(new ReadRequest(), stream, null, true),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void EncodeAndDecodeAsJsonRoundTrip()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow
                },
                MaxAge = 1000.0,
                TimestampsToReturn = TimestampsToReturn.Both
            };

            using var stream = new MemoryStream();
            SessionLessMessage.EncodeAsJson(request, stream, m_context, true);

            byte[] buffer = stream.ToArray();
            Assert.That(buffer, Is.Not.Empty);

            IEncodeable decoded = SessionLessMessage.DecodeAsJson(buffer, m_context);
            Assert.That(decoded, Is.Not.Null);
        }

        [Test]
        public void EncodeAsJsonLeaveOpenFalseDisposesStream()
        {
            var request = new ReadRequest();
            var stream = new MemoryStream();

            SessionLessMessage.EncodeAsJson(request, stream, m_context, false);

            Assert.That(() => stream.Position, Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void EncodeAsJsonLeaveOpenTrueResetsPosition()
        {
            var request = new ReadRequest();
            using var stream = new MemoryStream();
            SessionLessMessage.EncodeAsJson(request, stream, m_context, true);
            Assert.That(stream.Position, Is.Zero);
        }

        [Test]
        public void EncodeAsJsonThrowsWhenMaxMessageSizeExceeded()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var smallContext = ServiceMessageContext.Create(telemetry);
            smallContext.MaxMessageSize = 1;
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader()
            };
            var stream = new MemoryStream();
            Assert.That(
                () => SessionLessMessage.EncodeAsJson(request, stream, smallContext, true),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void DecodeAsBinaryThrowsOnNullBuffer()
        {
            Assert.That(
                () => SessionLessMessage.DecodeAsBinary(null, m_context),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void DecodeAsBinaryThrowsOnNullContext()
        {
            Assert.That(
                () => SessionLessMessage.DecodeAsBinary([], null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void EncodeAsBinaryThrowsOnNullContext()
        {
            using var stream = new MemoryStream();
            Assert.That(
                () => SessionLessMessage.EncodeAsBinary(new ReadRequest(), stream, null, true),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void EncodeAsBinaryThrowsOnNullMessage()
        {
            using var stream = new MemoryStream();
            Assert.That(
                () => SessionLessMessage.EncodeAsBinary(null, stream, m_context, true),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void EncodeAndDecodeAsBinaryRoundTrip()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow
                },
                MaxAge = 500.0,
                TimestampsToReturn = TimestampsToReturn.Server
            };

            using var stream = new MemoryStream();
            SessionLessMessage.EncodeAsBinary(request, stream, m_context, true);

            byte[] buffer = stream.ToArray();
            Assert.That(buffer, Is.Not.Empty);

            IEncodeable decoded = SessionLessMessage.DecodeAsBinary(buffer, m_context);
            Assert.That(decoded, Is.Not.Null);
        }

        [Test]
        public void DecodeAsBinaryThrowsOnInvalidTypeId()
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, m_context, true))
            {
                encoder.WriteNodeId(null, new NodeId(999999));
            }

            byte[] buffer = stream.ToArray();
            Assert.That(
                () => SessionLessMessage.DecodeAsBinary(buffer, m_context),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void EncodeAsBinaryThrowsWhenMaxMessageSizeExceeded()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var smallContext = ServiceMessageContext.Create(telemetry);
            smallContext.MaxMessageSize = 1;
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader()
            };
            using var stream = new MemoryStream();
            Assert.That(
                () => SessionLessMessage.EncodeAsBinary(request, stream, smallContext, true),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void EncodeAsBinaryLeaveOpenTrueKeepsStreamOpen()
        {
            var request = new ReadRequest();
            using var stream = new MemoryStream();
            SessionLessMessage.EncodeAsBinary(request, stream, m_context, true);
            Assert.That(stream.CanRead, Is.True);
            Assert.That(stream.Length, Is.GreaterThan(0));
        }

        [Test]
        public void EncodeAsBinaryLeaveOpenFalseDisposesStream()
        {
            var request = new ReadRequest();
            var stream = new MemoryStream();

            SessionLessMessage.EncodeAsBinary(request, stream, m_context, false);

            Assert.That(() => stream.Position, Throws.TypeOf<ObjectDisposedException>());
        }
    }
}
