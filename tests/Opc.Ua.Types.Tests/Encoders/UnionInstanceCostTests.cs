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
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Encoders;
using Opc.Ua.Tests;
using DynamicUnion = Opc.Ua.Encoders.Union;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// The definition of a Union comes from the peer and can have tens of
    /// thousands of fields, while a value takes four bytes on the wire. A
    /// decoded value must therefore not cost one object per field of the
    /// definition.
    /// </summary>
    [TestFixture]
    [Category("Encoder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class UnionInstanceCostTests
    {
        private const string kNs = "urn:test:union-cost";
        private const int kFields = 20_000;

        [Test]
        public void DecodingAUnionDoesNotAllocatePerFieldOfItsDefinition()
        {
            ServiceMessageContext context = CreateContext(out DynamicUnion type);
            byte[] unselected = Encode(context, 0, 0);
            byte[] selected = Encode(context, kFields, 42);

            // warm up
            Decode(type, context, unselected);
            Decode(type, context, selected);

            const int kInstances = 200;
            var values = new DynamicUnion[2 * kInstances];
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < kInstances; i++)
            {
                values[2 * i] = Decode(type, context, unselected);
                values[(2 * i) + 1] = Decode(type, context, selected);
            }
            long perInstance = (GC.GetAllocatedBytesForCurrentThread() - before) / values.Length;

            // One field of the definition costs more than 60 bytes when it
            // is created, which made a value of this type more than 1 MB.
            Assert.That(perInstance, Is.LessThan(2048));
            Assert.That(values[0].SwitchField, Is.Zero);
            Assert.That(values[1].SwitchField, Is.EqualTo((uint)kFields));
            Assert.That(values[1].Value.GetInt32(), Is.EqualTo(42));
        }

        [Test]
        public void DecodedUnionKeepsItsValueWhenAllFieldsAreCreated()
        {
            ServiceMessageContext context = CreateContext(out DynamicUnion type);
            DynamicUnion value = Decode(type, context, Encode(context, 3, 7));

            Assert.That(value.GetFields(), Has.Count.EqualTo(kFields));
            Assert.That(value.SwitchField, Is.EqualTo(3u));
            Assert.That(value[2].GetInt32(), Is.EqualTo(7));
            Assert.That(value["F2"].GetInt32(), Is.EqualTo(7));
            Assert.That(value[1].IsNull, Is.True);
            Assert.That(value.Value.GetInt32(), Is.EqualTo(7));
        }

        [Test]
        public void ConcurrentReadersOfADecodedUnionSeeItsValue()
        {
            const int kReaders = 4;
            ServiceMessageContext context = CreateContext(out DynamicUnion type, 64);
            byte[] encoded = Encode(context, 3, 7);
            using var start = new Barrier(kReaders);

            for (int round = 0; round < 500; round++)
            {
                DynamicUnion value = Decode(type, context, encoded);
                var lists = new object[kReaders];
                var clones = new DynamicUnion[kReaders];
                Parallel.For(
                    0,
                    kReaders,
                    new ParallelOptions { MaxDegreeOfParallelism = kReaders },
                    reader =>
                    {
                        start.SignalAndWait();
                        if (reader % 2 == 0)
                        {
                            lists[reader] = value.GetFields();
                        }
                        clones[reader] = (DynamicUnion)value.Clone();
                    });

                Assert.That(value[2].GetInt32(), Is.EqualTo(7));
                Assert.That(value.Value.GetInt32(), Is.EqualTo(7));
                Assert.That(lists[2], Is.SameAs(lists[0]));
                foreach (DynamicUnion clone in clones)
                {
                    Assert.That(clone.Value.GetInt32(), Is.EqualTo(7));
                }
            }
        }

        [Test]
        public void UnionRoundTripsCloneAndCompare()
        {
            ServiceMessageContext context = CreateContext(out DynamicUnion type);
            byte[] encoded = Encode(context, 5, 99);
            DynamicUnion value = Decode(type, context, encoded);
            var clone = (DynamicUnion)value.Clone();
            DynamicUnion other = Decode(type, context, Encode(context, 5, 100));

            Assert.That(clone.SwitchField, Is.EqualTo(5u));
            Assert.That(clone.Value.GetInt32(), Is.EqualTo(99));
            Assert.That(clone.IsEqual(value), Is.True);
            Assert.That(other.IsEqual(value), Is.False);
            Assert.That(value.ToString(), Is.EqualTo("{99}"));

            using var encoder = new BinaryEncoder(context);
            clone.Encode(encoder);
            Assert.That(encoder.CloseAndReturnBuffer(), Is.EqualTo(encoded));
        }

        [Test]
        public void SettingASecondFieldKeepsTheFirst()
        {
            CreateContext(out DynamicUnion type);
            var value = (DynamicUnion)type.CreateInstance();

            value[1] = Variant.From(1);
            value[4] = Variant.From(4);

            Assert.That(value.SwitchField, Is.EqualTo(5u));
            Assert.That(value.Value.GetInt32(), Is.EqualTo(4));
            value[1] = Variant.From(11);
            Assert.That(value.SwitchField, Is.EqualTo(2u));
            Assert.That(value.Value.GetInt32(), Is.EqualTo(11));
            Assert.That(
                () => value[kFields] = Variant.From(0),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void UnionWithASwitchFieldBeyondItsFieldsIsRejected()
        {
            ServiceMessageContext context = CreateContext(out DynamicUnion type);
            byte[] encoded = Encode(context, kFields + 1, 0);

            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => Decode(type, context, encoded));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void IndexBeyondTheFieldsIsRejectedOnAValueWithoutFields()
        {
            CreateContext(out DynamicUnion type);
            var value = (DynamicUnion)type.CreateInstance();

            Assert.That(
                () => value[kFields] = Variant.From(0),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(value[kFields].IsNull, Is.True);
            Assert.That(value.SwitchField, Is.Zero);
        }

        [Test]
        public void UnknownIndexOrNameReadsTheSelectedField()
        {
            ServiceMessageContext context = CreateContext(out DynamicUnion type);
            DynamicUnion value = Decode(type, context, Encode(context, 3, 7));
            DynamicUnion unselected = Decode(type, context, Encode(context, 0, 0));

            Assert.That(value[-1].GetInt32(), Is.EqualTo(7));
            Assert.That(value["NoSuchField"].GetInt32(), Is.EqualTo(7));
            Assert.That(unselected[-1].IsNull, Is.True);
            Assert.That(unselected["NoSuchField"].IsNull, Is.True);
            Assert.That(unselected.Value.IsNull, Is.True);
        }

        [Test]
        public void UnionsOfDifferentSelectionOrTypeAreNotEqual()
        {
            ServiceMessageContext context = CreateContext(out DynamicUnion type);
            DynamicUnion value = Decode(type, context, Encode(context, 3, 7));
            DynamicUnion unselected = Decode(type, context, Encode(context, 0, 0));

            Assert.That(value.IsEqual(unselected), Is.False);
            Assert.That(value.IsEqual(null), Is.False);
            Assert.That(unselected.IsEqual(Decode(type, context, Encode(context, 0, 0))), Is.True);
            Assert.That(unselected.ToString(), Does.Contain("Wide"));
            Assert.That(() => value.ToString("x", null), Throws.InstanceOf<FormatException>());
        }

        [Test]
        public void EncodingAUnionWithASwitchFieldBeyondItsFieldsIsRejected()
        {
            ServiceMessageContext context = CreateContext(out _);
            var id = new ExpandedNodeId("One", kNs);
            var fields = new List<StructureField>
            {
                new() { Name = "A", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }
            };
            var value = new DynamicUnion(
                new XmlQualifiedName("One", kNs),
                id,
                id,
                id,
                new StructureDefinition { StructureType = StructureType.Union, Fields = fields.ToArrayOf() },
                new Dictionary<string, BuiltInType> { ["A"] = BuiltInType.Int32 },
                switchField: 2);

            using var encoder = new BinaryEncoder(context);
            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => value.Encode(encoder));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void DefinitionWithTwoFieldsOfOneNameIsRejectedWhenTheTypeIsCreated()
        {
            var fields = new List<StructureField>
            {
                new() { Name = "A", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar },
                new() { Name = "A", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }
            };
            var id = new ExpandedNodeId("Twice", kNs);

            Assert.That(
                () => new DynamicUnion(
                    new XmlQualifiedName("Twice", kNs),
                    id,
                    id,
                    id,
                    new StructureDefinition { StructureType = StructureType.Union, Fields = fields.ToArrayOf() },
                    new Dictionary<string, BuiltInType> { ["A"] = BuiltInType.Int32 }),
                Throws.ArgumentException);
        }

        private static DynamicUnion Decode(DynamicUnion type, ServiceMessageContext context, byte[] encoded)
        {
            var value = (DynamicUnion)type.CreateInstance();
            using var decoder = new BinaryDecoder(encoded, context);
            value.Decode(decoder);
            return value;
        }

        private static byte[] Encode(ServiceMessageContext context, int switchField, int value)
        {
            using var encoder = new BinaryEncoder(context);
            encoder.WriteUInt32(null, (uint)switchField);
            if (switchField != 0)
            {
                encoder.WriteInt32(null, value);
            }
            return encoder.CloseAndReturnBuffer()!;
        }

        private static ServiceMessageContext CreateContext(out DynamicUnion type, int count = kFields)
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            context.NamespaceUris.GetIndexOrAppend(kNs);
            var fields = new List<StructureField>(count);
            var fieldTypes = new Dictionary<string, BuiltInType>(count);
            for (int i = 0; i < count; i++)
            {
                string name = "F" + i;
                fields.Add(new StructureField { Name = name, DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar });
                fieldTypes[name] = BuiltInType.Int32;
            }
            var id = new ExpandedNodeId("Wide", kNs);
            type = new DynamicUnion(
                new XmlQualifiedName("Wide", kNs),
                id,
                id,
                id,
                new StructureDefinition { StructureType = StructureType.Union, Fields = fields.ToArrayOf() },
                fieldTypes);
            return context;
        }
    }
}
