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
using Apache.Arrow.Ipc;
using NUnit.Framework;
using Opc.Ua;

#pragma warning disable UA_NETStandard_Arrow // experimental encoder surface under test

namespace Opc.Ua.Core.Tests
{
    /// <summary>
    /// Coverage guard: <see cref="ArrowSchemaCanonicalForm"/> must handle every Arrow type the
    /// <see cref="ArrowEncoder"/> emits for the OPC UA type model. Each case encodes a
    /// representative value, reads back the emitted Arrow schema, and canonicalises it — asserting
    /// no <see cref="NotSupportedException"/> (unmapped type), a non-empty canonical string and a
    /// deterministic SchemaId. This exercises primitives, fixed_size_binary (Guid), struct
    /// (NodeId / DataValue / ExtensionObject), list (arrays) and dense union (Variant).
    /// </summary>
    [TestFixture]
    public sealed class ArrowSchemaCanonicalFormCoverageTests
    {
        private static IServiceMessageContext Context => ServiceMessageContext.CreateEmpty(null!);

        [Test]
        public void CanonicalFormCoversEveryEncoderEmittedArrowType()
        {
            var cases = new (string Name, Action<ArrowEncoder> Write)[]
            {
                ("int32", e => e.WriteInt32(null, 7)),
                ("string", e => e.WriteString(null, "abc")),
                ("guid", e => e.WriteGuid(null, new Uuid(Guid.NewGuid()))),
                ("nodeId", e => e.WriteNodeId(null, new NodeId(123u, 2))),
                ("stringArray", e => e.WriteStringArray(null, new ArrayOf<string>(["a", "b"]))),
                ("int32Array", e => e.WriteInt32Array(null, new ArrayOf<int>([1, 2, 3]))),
                ("variantScalar", e => e.WriteVariant(null, new Variant(123))),
                ("variantArray", e => e.WriteVariant(null, new Variant(new ArrayOf<int>([1, 2])))),
                ("extensionObject", e => e.WriteExtensionObject(null, new ExtensionObject(new NodeId(9u, 3)))),
                ("dataValue", e => e.WriteDataValue(null, new DataValue(new Variant(5)))),
            };

            Assert.Multiple(() =>
            {
                foreach ((string name, Action<ArrowEncoder> write) in cases)
                {
                    Apache.Arrow.Schema schema = EncodeSchema(write);

                    string canonical = null;
                    Assert.DoesNotThrow(
                        () => canonical = ArrowSchemaCanonicalForm.Compute(schema),
                        $"canonical form threw for {name}");
                    Assert.That(canonical, Is.Not.Null.And.StartsWith("arrow-schema-v1"), name);

                    byte[] a = ArrowSchemaCanonicalForm.ComputeSchemaId(schema, 8);
                    byte[] b = ArrowSchemaCanonicalForm.ComputeSchemaId(schema, 8);
                    Assert.That(a, Is.EqualTo(b), $"SchemaId not deterministic for {name}");
                    Assert.That(a, Has.Length.EqualTo(8), name);
                }
            });
        }

        private static Apache.Arrow.Schema EncodeSchema(Action<ArrowEncoder> write)
        {
            using var stream = new MemoryStream();
            using (var encoder = new ArrowEncoder(stream, Context, leaveOpen: true))
            {
                write(encoder);
                encoder.Close();
            }

            stream.Position = 0;
            using var reader = new ArrowStreamReader(stream);
            using Apache.Arrow.RecordBatch batch = reader.ReadNextRecordBatch()
                ?? throw new InvalidOperationException("no record batch");
            return batch.Schema;
        }
    }
}
#endif
