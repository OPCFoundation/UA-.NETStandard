// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using NUnit.Framework;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    /// <summary>
    /// Verifies the HTTP/3 WebTransport Extended CONNECT request field-section contract.
    /// </summary>
    [TestFixture]
    public sealed class Http3ExtendedConnectRequestTests
    {
        /// <summary>
        /// Preserves the exact authority, resource and optional Origin from a valid request.
        /// </summary>
        [Test]
        public void Parse_ValidRequest_PreservesTargetAndOrigin()
        {
            QpackHeaderField[] fields =
            [
                new(":method", "CONNECT"),
                new(":protocol", "webtransport"),
                new(":scheme", "https"),
                new(":authority", "device.example:443"),
                new(":path", "/ua/wt?resource=service"),
                new("origin", "https://client.example"),
                new("capsule-protocol", "?1")
            ];

            Http3ExtendedConnectRequestStatus status =
                Http3ExtendedConnectRequestParser.TryParse(fields, out Http3ExtendedConnectRequest request);

            Assert.That(status, Is.EqualTo(Http3ExtendedConnectRequestStatus.Success));
            Assert.That(request.Authority, Is.EqualTo("device.example:443"));
            Assert.That(request.Path, Is.EqualTo("/ua/wt?resource=service"));
            Assert.That(request.Origin, Is.EqualTo("https://client.example"));
        }

        /// <summary>
        /// Rejects requests that do not use the exact CONNECT, webtransport and HTTPS pseudo-header values.
        /// </summary>
        [TestCase(":method", "GET", (int)Http3ExtendedConnectRequestStatus.InvalidMethod)]
        [TestCase(":protocol", "webtransport-h3", (int)Http3ExtendedConnectRequestStatus.InvalidProtocol)]
        [TestCase(":scheme", "http", (int)Http3ExtendedConnectRequestStatus.InvalidScheme)]
        public void Parse_InvalidRequiredPseudoHeader_IsRejected(
            string name,
            string value,
            int expected)
        {
            QpackHeaderField[] fields = ValidFields();
            int index = name switch
            {
                ":method" => 0,
                ":protocol" => 1,
                _ => 2
            };
            fields[index] = new QpackHeaderField(name, value);

            Assert.That(
                Http3ExtendedConnectRequestParser.TryParse(fields, out _),
                Is.EqualTo((Http3ExtendedConnectRequestStatus)expected));
        }

        /// <summary>
        /// Rejects duplicate, unknown, and incorrectly ordered pseudo-headers.
        /// </summary>
        [Test]
        public void Parse_InvalidPseudoHeaderLayout_IsRejected()
        {
            QpackHeaderField[] duplicate =
            [
                .. ValidFields()[..5],
                new(":authority", "other.example"),
                new("origin", "https://client.example"),
                new("capsule-protocol", "?1")
            ];
            QpackHeaderField[] unknown = [.. ValidFields()[..5], new(":status", "200")];
            QpackHeaderField[] outOfOrder =
            [
                new(":method", "CONNECT"),
                new("origin", "https://client.example"),
                new(":protocol", "webtransport")
            ];

            Assert.That(
                Http3ExtendedConnectRequestParser.TryParse(duplicate, out _),
                Is.EqualTo(Http3ExtendedConnectRequestStatus.DuplicatePseudoHeader));
            Assert.That(
                Http3ExtendedConnectRequestParser.TryParse(unknown, out _),
                Is.EqualTo(Http3ExtendedConnectRequestStatus.UnknownPseudoHeader));
            Assert.That(
                Http3ExtendedConnectRequestParser.TryParse(outOfOrder, out _),
                Is.EqualTo(Http3ExtendedConnectRequestStatus.PseudoHeaderAfterRegularHeader));
        }

        /// <summary>
        /// Rejects absent required pseudo-headers and invalid target paths.
        /// </summary>
        [Test]
        public void Parse_MissingRequiredPseudoHeader_IsRejected()
        {
            QpackHeaderField[] fields = ValidFields();
            fields[4] = new QpackHeaderField(":path", "relative");

            Assert.That(
                Http3ExtendedConnectRequestParser.TryParse(fields, out _),
                Is.EqualTo(Http3ExtendedConnectRequestStatus.MissingRequiredPseudoHeader));
            Assert.That(
                Http3ExtendedConnectRequestParser.TryParse(
                    [.. ValidFields()[..4], .. ValidFields()[5..]], out _),
                Is.EqualTo(Http3ExtendedConnectRequestStatus.MissingRequiredPseudoHeader));
        }

        /// <summary>
        /// Rejects connection-specific headers and duplicate Origin values.
        /// </summary>
        [Test]
        public void Parse_ForbiddenRegularHeaders_AreRejected()
        {
            QpackHeaderField[] connectionHeader = [.. ValidFields(), new("connection", "close")];
            QpackHeaderField[] invalidTe = [.. ValidFields(), new("te", "trailers, gzip")];
            QpackHeaderField[] duplicateOrigin =
                [.. ValidFields(), new("origin", "https://second.example")];

            Assert.That(
                Http3ExtendedConnectRequestParser.TryParse(connectionHeader, out _),
                Is.EqualTo(Http3ExtendedConnectRequestStatus.ConnectionSpecificHeader));
            Assert.That(
                Http3ExtendedConnectRequestParser.TryParse(invalidTe, out _),
                Is.EqualTo(Http3ExtendedConnectRequestStatus.ConnectionSpecificHeader));
            Assert.That(
                Http3ExtendedConnectRequestParser.TryParse(duplicateOrigin, out _),
                Is.EqualTo(Http3ExtendedConnectRequestStatus.DuplicateOrigin));
        }

        /// <summary>
        /// Requires exactly one Capsule Protocol indicator with the registered structured-field value.
        /// </summary>
        [Test]
        public void Parse_CapsuleProtocolIndicator_IsRequiredAndUnique()
        {
            QpackHeaderField[] missing = ValidFields()[..^1];
            QpackHeaderField[] duplicate = [.. ValidFields(), new("capsule-protocol", "?1")];
            QpackHeaderField[] invalid = [.. ValidFields()[..^1], new("capsule-protocol", "?0")];

            Assert.That(
                Http3ExtendedConnectRequestParser.TryParse(missing, out _),
                Is.EqualTo(Http3ExtendedConnectRequestStatus.MissingCapsuleProtocol));
            Assert.That(
                Http3ExtendedConnectRequestParser.TryParse(duplicate, out _),
                Is.EqualTo(Http3ExtendedConnectRequestStatus.DuplicateCapsuleProtocol));
            Assert.That(
                Http3ExtendedConnectRequestParser.TryParse(invalid, out _),
                Is.EqualTo(Http3ExtendedConnectRequestStatus.InvalidCapsuleProtocol));
        }

        private static QpackHeaderField[] ValidFields()
        {
            return
            [
                new(":method", "CONNECT"),
                new(":protocol", "webtransport"),
                new(":scheme", "https"),
                new(":authority", "device.example:443"),
                new(":path", "/ua/wt"),
                new("origin", "https://client.example"),
                new("capsule-protocol", "?1")
            ];
        }
    }
}
