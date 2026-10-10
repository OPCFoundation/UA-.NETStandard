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
using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Encoding.Tests
{
    /// <summary>
    /// Verifies that <see cref="AvroSchemaBuilder"/> emits a real, self-contained Avro schema so
    /// the SchemaId is the CRC-64-AVRO fingerprint of the Avro Parsing Canonical Form (§6.3)
    /// rather than a hash of an arbitrary document.
    /// </summary>
    [TestFixture]
    public sealed class AvroSchemaBuilderTests
    {
        private static AvroSchemaField Field(
            string name,
            BuiltInType type,
            int rank = ValueRanks.Scalar,
            PubSubFieldEncoding encoding = PubSubFieldEncoding.RawData)
        {
            return new AvroSchemaField(name, type, rank, encoding);
        }

        [Test]
        public void EmittedSchemaIsParseableAndYieldsAParsingCanonicalForm()
        {
            string schema = AvroSchemaBuilder.Build(
                "Boiler",
                new[]
                {
                    Field("Temperature", BuiltInType.Double),
                    Field("Name", BuiltInType.String),
                    Field("Enabled", BuiltInType.Boolean)
                });

            // Parses as JSON, and - the part that actually matters - as an Avro schema. The
            // Parsing Canonical Form is only defined for a real schema, so this is what separates
            // a genuine SchemaId from a hash of arbitrary bytes.
            using JsonDocument document = JsonDocument.Parse(schema);
            Assert.That(document.RootElement.GetProperty("type").GetString(), Is.EqualTo("record"));

            string canonical = AvroParsingCanonicalForm.Compute(schema);
            Assert.That(canonical, Does.StartWith("{\"name\":\"org.opcfoundation.ua.avro.Boiler\""));
        }

        [Test]
        public void SchemaIdIsTheRabinFingerprintOfTheParsingCanonicalForm()
        {
            string schema = AvroSchemaBuilder.Build(
                "Simple",
                new[] { Field("Value", BuiltInType.Int32) });

            ByteString computed = SchemaCache.ComputeSchemaId(
                ByteString.From(System.Text.Encoding.UTF8.GetBytes(schema)),
                SchemaCache.AvroFormat);

            // Re-derive independently from the canonical form rather than calling the same helper
            // twice, so the assertion checks the definition and not the implementation against
            // itself.
            string canonical = AvroParsingCanonicalForm.Compute(schema);
            ulong fingerprint = SchemaId.RabinCrc64Avro(
                System.Text.Encoding.UTF8.GetBytes(canonical));
            byte[] expected = new byte[8];
            for (int i = 0; i < expected.Length; i++)
            {
                expected[i] = (byte)(fingerprint >> (8 * i));
            }

            Assert.That(computed.Span.ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public void ANonSchemaDocumentIsRejectedRatherThanFingerprinted()
        {
            // A silent fallback to hashing raw bytes previously made a non-schema document produce
            // a well-formed-looking SchemaId that no other implementation could reproduce.
            ByteString notASchema = ByteString.From(new byte[] { 0x00, 0x01, 0x02, 0x03 });
            Assert.That(
                () => SchemaCache.ComputeSchemaId(notASchema, SchemaCache.AvroFormat),
                Throws.InstanceOf<Exception>());
        }

        [Test]
        public void FieldOrderIsPreservedAndChangesTheSchemaId()
        {
            string ordered = AvroSchemaBuilder.Build(
                "Ordered",
                new[] { Field("A", BuiltInType.Int32), Field("B", BuiltInType.String) });
            string swapped = AvroSchemaBuilder.Build(
                "Ordered",
                new[] { Field("B", BuiltInType.String), Field("A", BuiltInType.Int32) });

            // §6.2 step 3: the generator never sorts fields, so declaration order is part of the
            // identity of the schema.
            Assert.That(ordered, Is.Not.EqualTo(swapped));
        }

        [Test]
        public void GenerationIsDeterministic()
        {
            IReadOnlyList<AvroSchemaField> fields = new[]
            {
                Field("Id", BuiltInType.NodeId),
                Field("When", BuiltInType.DateTime),
                Field("Tags", BuiltInType.String, ValueRanks.OneDimension)
            };

            Assert.That(
                AvroSchemaBuilder.Build("Repeat", fields),
                Is.EqualTo(AvroSchemaBuilder.Build("Repeat", fields)));
        }

        [Test]
        public void RepeatedNamedTypesAreReferencedSoTheSchemaStaysSelfContained()
        {
            string schema = AvroSchemaBuilder.Build(
                "TwoNodeIds",
                new[] { Field("First", BuiltInType.NodeId), Field("Second", BuiltInType.NodeId) });

            // Avro rejects a redefinition of a named type, so the second occurrence must be a
            // reference. If it were inlined twice the document would not parse at all.
            Assert.DoesNotThrow(() => AvroParsingCanonicalForm.Compute(schema));
            int definitions = CountOccurrences(schema, "\"name\":\"NodeId\"");
            Assert.That(definitions, Is.EqualTo(1), "NodeId should be defined once and then referenced");
        }

        [Test]
        public void ArraysAndMatricesUseTheDeclaredShapes()
        {
            string array = AvroSchemaBuilder.Build(
                "Arr",
                new[] { Field("Values", BuiltInType.Int32, ValueRanks.OneDimension) });
            Assert.That(array, Does.Contain("\"type\":\"array\""));

            string matrix = AvroSchemaBuilder.Build(
                "Mat",
                new[] { Field("Grid", BuiltInType.Double, 2) });
            Assert.That(matrix, Does.Contain("dimensions"));
            Assert.That(matrix, Does.Contain("values"));
            Assert.DoesNotThrow(() => AvroParsingCanonicalForm.Compute(matrix));
        }

        [Test]
        public void IllegalAvroNamesAreSanitized()
        {
            string schema = AvroSchemaBuilder.Build(
                "1Bad Name",
                new[] { Field("has-dash", BuiltInType.Int32) });

            Assert.That(schema, Does.Contain("\"name\":\"T_1Bad_Name\""));
            Assert.That(schema, Does.Contain("\"name\":\"has_dash\""));
            Assert.DoesNotThrow(() => AvroParsingCanonicalForm.Compute(schema));
        }

        [Test]
        public void VariantUnionGrowsAppendOnlyAcrossALineage()
        {
            var lineage = new AvroSchemaLineage();
            AvroSchemaField boolean = Field("V", BuiltInType.Boolean, ValueRanks.Scalar, PubSubFieldEncoding.Variant);
            AvroSchemaField int32 = Field("V", BuiltInType.Int32, ValueRanks.Scalar, PubSubFieldEncoding.Variant);

            IReadOnlyList<AvroSchemaField> first = lineage.Accumulate("k", new[] { boolean });
            string firstSchema = AvroSchemaBuilder.Build("Grow", new[] { boolean }, first);

            IReadOnlyList<AvroSchemaField> grown = lineage.Accumulate("k", new[] { int32 });
            string grownSchema = AvroSchemaBuilder.Build("Grow", new[] { int32 }, grown);

            Assert.That(grownSchema, Is.Not.EqualTo(firstSchema), "a new body type grows the schema");

            // Append-only is the property that matters: the Boolean branch must survive at its
            // original position, otherwise every previously written value silently changes meaning.
            int booleanIndex = grownSchema.IndexOf("VariantBooleanScalar", StringComparison.Ordinal);
            int int32Index = grownSchema.IndexOf("VariantInt32Scalar", StringComparison.Ordinal);
            Assert.That(booleanIndex, Is.GreaterThan(-1), "the original branch is retained");
            Assert.That(int32Index, Is.GreaterThan(booleanIndex), "the new branch is appended after it");
            Assert.DoesNotThrow(() => AvroParsingCanonicalForm.Compute(grownSchema));
        }

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            int index = text.IndexOf(value, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
            }
            return count;
        }
    }
}
