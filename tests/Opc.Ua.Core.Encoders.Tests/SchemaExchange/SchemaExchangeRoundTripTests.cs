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

using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua;

namespace Opc.Ua.Core.Tests
{
    /// <summary>
    /// Tests schema-exchange descriptor round-trips.
    /// </summary>
    [TestFixture]
    public sealed class SchemaExchangeRoundTripTests
    {
        /// <summary>
        /// Verifies the Avro schema announcement field order and nullable epoch branch.
        /// </summary>
        [Test]
        public void AvroSchemaAnnouncement_RoundTrips()
        {
            string schemaJson = "{\"type\":\"record\",\"name\":\"T\",\"fields\":[]}";
            AvroSchemaAnnouncement expected = new(
                AvroSchemaAnnouncement.ComputeSchemaId(schemaJson),
                schemaJson,
                42);

            AvroSchemaAnnouncement actual = AvroSchemaAnnouncement.Decode(expected.Encode());

            Assert.That(actual.SchemaId, Is.EqualTo(expected.SchemaId));
            Assert.That(actual.SchemaJson, Is.EqualTo(expected.SchemaJson));
            Assert.That(actual.SchemaEpoch, Is.EqualTo(expected.SchemaEpoch));
        }

        /// <summary>
        /// Verifies the Avro schema request field order and array item bytes.
        /// </summary>
        [Test]
        public void AvroSchemaRequest_RoundTrips()
        {
            AvroSchemaRequest expected = new("subscriber", CreateIds());

            AvroSchemaRequest actual = AvroSchemaRequest.Decode(expected.Encode());

            Assert.That(actual.RequesterId, Is.EqualTo(expected.RequesterId));
            Assert.That(actual.SchemaIds, Is.EqualTo(expected.SchemaIds));
        }

        /// <summary>
        /// Verifies the JSON schema announcement envelope round-trip.
        /// </summary>
        [Test]
        public void JsonSchemaAnnouncementRoundTrips()
        {
            string schemaJson = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"type\":\"object\"}";
            JsonSchemaAnnouncement expected = new(
                JsonSchemaAnnouncement.ComputeSchemaId(schemaJson),
                schemaJson,
                7);

            JsonSchemaAnnouncement actual = JsonSchemaAnnouncement.Decode(expected.Encode());

            Assert.That(actual.SchemaId, Is.EqualTo(expected.SchemaId));
            Assert.That(actual.SchemaJson, Is.EqualTo(expected.SchemaJson));
            Assert.That(actual.SchemaEpoch, Is.EqualTo(expected.SchemaEpoch));
        }

        /// <summary>
        /// Verifies the JSON schema announcement accepts a null epoch in the envelope.
        /// </summary>
        [Test]
        public void JsonSchemaAnnouncementRoundTripsWithNullEpoch()
        {
            string schemaJson = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"type\":\"object\"}";
            JsonSchemaAnnouncement expected = new(
                JsonSchemaAnnouncement.ComputeSchemaId(schemaJson),
                schemaJson,
                null);

            JsonSchemaAnnouncement actual = JsonSchemaAnnouncement.Decode(expected.Encode());

            Assert.That(actual.SchemaEpoch, Is.Null);
        }

        /// <summary>
        /// Verifies the JSON schema announcement validates null arguments and required fields.
        /// </summary>
        [Test]
        public void JsonSchemaAnnouncementRejectsInvalidInputs()
        {
            string schemaJson = "{\"type\":\"object\"}";
            JsonSchemaAnnouncement nullSchemaId = new(default, schemaJson, null);
            JsonSchemaAnnouncement nullSchemaJson = new(
                JsonSchemaAnnouncement.ComputeSchemaId(schemaJson),
                null!,
                null);

            Assert.Multiple(() =>
            {
                Assert.That(() => nullSchemaId.Encode(), Throws.InvalidOperationException);
                Assert.That(() => nullSchemaJson.Encode(), Throws.InvalidOperationException);
                Assert.That(() => nullSchemaId.Encode(null!), Throws.ArgumentNullException);
                Assert.That(() => JsonSchemaAnnouncement.Decode((System.IO.Stream)null!), Throws.ArgumentNullException);
                Assert.That(
                    () => JsonSchemaAnnouncement.Decode(global::System.ReadOnlyMemory<byte>.Empty),
                    Throws.InstanceOf<JsonException>());
                Assert.That(
                    () => JsonSchemaAnnouncement.Decode(Encoding.UTF8.GetBytes("{\"schemaId\":\"not-base64\"}")),
                    Throws.Exception);
                Assert.That(() => JsonSchemaAnnouncement.ComputeSchemaId(null!), Throws.ArgumentNullException);
            });
        }

        /// <summary>
        /// Verifies the JSON schema request envelope round-trip.
        /// </summary>
        [Test]
        public void JsonSchemaRequestRoundTrips()
        {
            JsonSchemaRequest expected = new("subscriber", CreateIds());

            JsonSchemaRequest actual = JsonSchemaRequest.Decode(expected.Encode());

            Assert.That(actual.RequesterId, Is.EqualTo(expected.RequesterId));
            Assert.That(actual.SchemaIds, Is.EqualTo(expected.SchemaIds));
        }

        /// <summary>
        /// Verifies the JSON schema request accepts a null requester and an empty id collection.
        /// </summary>
        [Test]
        public void JsonSchemaRequestRoundTripsNullRequesterAndEmptyIds()
        {
            JsonSchemaRequest expected = new(null, []);

            JsonSchemaRequest actual = JsonSchemaRequest.Decode(expected.Encode());

            Assert.Multiple(() =>
            {
                Assert.That(actual.RequesterId, Is.Null);
                Assert.That(actual.SchemaIds, Is.Empty);
            });
        }

        /// <summary>
        /// Verifies the JSON schema request validates null arguments and required identifiers.
        /// </summary>
        [Test]
        public void JsonSchemaRequestRejectsInvalidInputs()
        {
            JsonSchemaRequest nullIds = new("subscriber", null!);
            JsonSchemaRequest nullItem = new("subscriber", [default]);

            Assert.Multiple(() =>
            {
                Assert.That(() => nullIds.Encode(), Throws.InvalidOperationException);
                Assert.That(() => nullItem.Encode(), Throws.InvalidOperationException);
                Assert.That(() => nullIds.Encode(null!), Throws.ArgumentNullException);
                Assert.That(() => JsonSchemaRequest.Decode((System.IO.Stream)null!), Throws.ArgumentNullException);
                Assert.That(
                    () => JsonSchemaRequest.Decode(global::System.ReadOnlyMemory<byte>.Empty),
                    Throws.InstanceOf<JsonException>());
                Assert.That(
                    () => JsonSchemaRequest.Decode(Encoding.UTF8.GetBytes("{\"requesterId\":null,\"schemaIds\":[\"!\"]}")),
                    Throws.Exception);
            });
        }

        /// <summary>
        /// Verifies the Arrow schema announcement descriptor round-trip.
        /// </summary>
        [Test]
        public void ArrowSchemaAnnouncement_RoundTrips()
        {
            ByteString schema = ByteString.From(1, 2, 3, 4);
            ArrowSchemaAnnouncement expected = new(
                ArrowSchemaAnnouncement.ComputeSchemaId(schema),
                schema,
                null);

            ArrowSchemaAnnouncement actual = ArrowSchemaAnnouncement.Decode(expected.Encode());

            Assert.That(actual.SchemaId, Is.EqualTo(expected.SchemaId));
            Assert.That(actual.Schema, Is.EqualTo(expected.Schema));
            Assert.That(actual.SchemaEpoch, Is.Null);
        }

        /// <summary>
        /// Verifies the Arrow schema request descriptor round-trip.
        /// </summary>
        [Test]
        public void ArrowSchemaRequest_RoundTrips()
        {
            ArrowSchemaRequest expected = new(null, CreateIds());

            ArrowSchemaRequest actual = ArrowSchemaRequest.Decode(expected.Encode());

            Assert.That(actual.RequesterId, Is.Null);
            Assert.That(actual.SchemaIds, Is.EqualTo(expected.SchemaIds));
        }

        private static ByteString[] CreateIds()
        {
            return Enumerable.Range(0, 2)
                .Select(i => ByteString.From((byte)i, 2, 3, 4, 5, 6, 7, 8))
                .ToArray();
        }
    }
}
