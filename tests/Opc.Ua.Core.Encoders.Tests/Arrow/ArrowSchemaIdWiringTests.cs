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
#if NET8_0_OR_GREATER
using System;
using System.IO;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using NUnit.Framework;
using Opc.Ua;
using ArrowSchema = Apache.Arrow.Schema;

#pragma warning disable UA_NETStandard_Encoders // experimental encoder surface under test

namespace Opc.Ua.Core.Tests
{
    /// <summary>
    /// Proves the Arrow SchemaId wiring (finding 10) is <b>portable</b>: the public "compute a
    /// SchemaId for serialized Arrow schema bytes" surfaces — the pluggable
    /// <see cref="SchemaIdProviders"/> arrow provider and
    /// <see cref="ArrowSchemaAnnouncement.ComputeSchemaId(ByteString)"/> — re-derive the SchemaId
    /// from the schema's logical content (the implementation-independent
    /// <see cref="ArrowSchemaCanonicalForm"/>), not from the raw Arrow IPC bytes. This is what makes
    /// a pyarrow-produced and a .NET-produced IPC of the same logical schema share one SchemaId, so
    /// the Schema Registry can de-duplicate across implementations.
    /// </summary>
    [TestFixture]
    public sealed class ArrowSchemaIdWiringTests
    {
        [Test]
        public void ArrowProviderAndAnnouncementUsePortableCanonicalSchemaId()
        {
            ArrowSchema intSchema = ValueSchema(Int32Type.Default);
            byte[] ipc = SerializeSchemaIpc(intSchema, new[] { (IArrowArray)new Int32Array.Builder().Build() });

            byte[] canonical = ArrowSchemaCanonicalForm.ComputeSchemaId(intSchema, 8);
            byte[] rawIpcHash = SchemaId.Sha256Id(ipc, 8);

            byte[] providerId = SchemaIdProviders.ComputeSchemaId(SchemaIdProviders.ArrowFormat, ipc);
            ByteString announcementId = ArrowSchemaAnnouncement.ComputeSchemaId(ByteString.From(ipc));

            Assert.Multiple(() =>
            {
                // The provider is registered under the canonical algorithm name.
                Assert.That(SchemaIdProviders.AlgorithmFor(SchemaIdProviders.ArrowFormat),
                    Is.EqualTo("SHA-256/ArrowCanonical"));

                // Both public surfaces yield the portable canonical SchemaId ...
                Assert.That(providerId, Is.EqualTo(canonical),
                    "arrow provider must fingerprint the canonical form, not the raw IPC bytes");
                Assert.That(announcementId.Span.ToArray(), Is.EqualTo(canonical),
                    "ArrowSchemaAnnouncement.ComputeSchemaId must fingerprint the canonical form");

                // ... which is NOT the (implementation-dependent) hash of the raw IPC bytes.
                Assert.That(providerId, Is.Not.EqualTo(rawIpcHash),
                    "a raw-IPC-bytes SchemaId would not be portable across Arrow implementations");
            });
        }

        [Test]
        public void ArrowProviderFallsBackToRawHashForNonIpcInput()
        {
            // A non-IPC input (here a JSON descriptor placeholder, as the current PubSub announcement
            // still uses) is not a readable Arrow schema, so the provider falls back to a raw SHA-256.
            byte[] notIpc = System.Text.Encoding.UTF8.GetBytes("{\"format\":\"arrow\"}");

            byte[] providerId = SchemaIdProviders.ComputeSchemaId(SchemaIdProviders.ArrowFormat, notIpc);

            Assert.That(providerId, Is.EqualTo(SchemaId.Sha256Id(notIpc, 8)));
        }

        private static ArrowSchema ValueSchema(IArrowType type)
        {
            return new ArrowSchema.Builder()
                .Metadata("opcua-arrow", "1")
                .Field(new Field("value", type, nullable: true, null))
                .Build();
        }

        private static byte[] SerializeSchemaIpc(ArrowSchema schema, IArrowArray[] columns)
        {
            using var batch = new RecordBatch(schema, columns, columns[0].Length);
            using var stream = new MemoryStream();
            using (var writer = new ArrowStreamWriter(stream, schema, leaveOpen: true))
            {
                writer.WriteStart();
                writer.WriteRecordBatch(batch);
                writer.WriteEnd();
            }

            return stream.ToArray();
        }
    }
}
#endif
