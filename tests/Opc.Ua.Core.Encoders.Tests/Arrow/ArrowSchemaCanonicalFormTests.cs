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
using System.Collections.Generic;
using Apache.Arrow;
using Apache.Arrow.Types;
using NUnit.Framework;
using Opc.Ua;
using ArrowSchema = Apache.Arrow.Schema;

#pragma warning disable UA_NETStandard_Arrow // experimental encoder surface under test

namespace Opc.Ua.Core.Tests
{
    /// <summary>
    /// Proves a <b>portable</b> canonical Arrow SchemaId. The Part 6 Arrow encoding defines the
    /// canonical form as the serialized Arrow Schema IPC bytes, but that is not byte-identical
    /// across Arrow implementations (pyarrow 24.0.0 serializes a one-field <c>value:int32</c>
    /// schema to 192 bytes; Apache.Arrow 18.1.0 serializes the same schema to 184 different
    /// bytes), so a SchemaId over the raw IPC bytes is not stable across a pyarrow registry and a
    /// .NET one — breaking Schema Registry cross-registry de-duplication (§4.3). The
    /// implementation-independent canonical form in <see cref="ArrowSchemaCanonicalForm"/>
    /// reproduces, byte-for-byte, the canonical string and SHA-256[:8] SchemaId computed by the
    /// equivalent Python routine over pyarrow schemas — the expected values below were produced by
    /// that Python routine.
    /// </summary>
    [TestFixture]
    public sealed class ArrowSchemaCanonicalFormTests
    {
        /// <summary>
        /// Each representative schema's canonical string and portable SchemaId must equal the
        /// value produced by the equivalent Python canonicaliser over the same pyarrow schema.
        /// </summary>
        [Test]
        public void ComputeMatchesPortableReferenceCanonicalFormAndSchemaId()
        {
            var cases = new (ArrowSchema Schema, string Canonical, string SchemaIdHex)[]
            {
                (
                    ValueSchema(Int32Type.Default),
                    "arrow-schema-v1\nM:\"opcua-arrow\"=\"1\"\nF:\"value\":i32:1:",
                    "40a367d545f8cc25"),
                (
                    ValueSchema(StringType.Default),
                    "arrow-schema-v1\nM:\"opcua-arrow\"=\"1\"\nF:\"value\":str:1:",
                    "cb792cebb093cfc5"),
                (
                    ValueSchema(new StructType(new List<Field>
                    {
                        new Field("a", Int32Type.Default, nullable: true, null),
                        new Field("b", StringType.Default, nullable: false, null),
                    })),
                    "arrow-schema-v1\nM:\"opcua-arrow\"=\"1\"\nF:\"value\":struct<\"a\":i32:1,\"b\":str:0>:1:",
                    "ce01c410bb288d70"),
                (
                    ValueSchema(new ListType(new Field("item", Int64Type.Default, nullable: true, null))),
                    "arrow-schema-v1\nM:\"opcua-arrow\"=\"1\"\nF:\"value\":list<i64:1>:1:",
                    "b9ade40ccb355c00"),
                (
                    ValueSchema(new FixedSizeBinaryType(8)),
                    "arrow-schema-v1\nM:\"opcua-arrow\"=\"1\"\nF:\"value\":fsb8:1:",
                    "c528c3d84cb95766"),
                (
                    ValueSchema(new UnionType(
                        new List<Field>
                        {
                            new Field("null", NullType.Default, nullable: true, null),
                            new Field("Int32", Int32Type.Default, nullable: true, null),
                            new Field("bin", BinaryType.Default, nullable: true, null),
                        },
                        s_unionTypeIds,
                        UnionMode.Dense)),
                    "arrow-schema-v1\nM:\"opcua-arrow\"=\"1\"\nF:\"value\":union<dense;0=\"null\":null:1,1=\"Int32\":i32:1,2=\"bin\":bin:1>:1:",
                    "0e27f7d8eed05f55"),
            };

            Assert.Multiple(() =>
            {
                foreach ((ArrowSchema schema, string expectedCanonical, string expectedSchemaId) in cases)
                {
                    string canonical = ArrowSchemaCanonicalForm.Compute(schema);
                    Assert.That(canonical, Is.EqualTo(expectedCanonical),
                        $"Canonical form mismatch for SchemaId {expectedSchemaId}");

                    byte[] schemaId = ArrowSchemaCanonicalForm.ComputeSchemaId(schema, 8);
                    string schemaIdHex = CoreUtils.ToHexString(schemaId).ToLowerInvariant();
                    Assert.That(schemaIdHex, Is.EqualTo(expectedSchemaId),
                        $"SchemaId mismatch for canonical form: {expectedCanonical}");
                }
            });
        }

        private static ArrowSchema ValueSchema(IArrowType type)
        {
            return new ArrowSchema.Builder()
                .Metadata("opcua-arrow", "1")
                .Field(new Field("value", type, nullable: true, null))
                .Build();
        }

        private static readonly int[] s_unionTypeIds = [0, 1, 2];
    }
}
#endif
