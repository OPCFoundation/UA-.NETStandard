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
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Encoders;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// The binary decoder bounds the work of decoding structures whose layout
    /// comes from a DataTypeDefinition by the length of its input. Such
    /// structures can be empty, so a nesting of them can decode from no bytes.
    /// </summary>
    [TestFixture]
    [Category("Encoder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class StructureDecodeWorkTests
    {
        private const string kNs = "urn:test:structure-work";

        [Test]
        public void NestedStructuresWithinTheBudgetDecodeFromEmptyInput()
        {
            ServiceMessageContext context = CreateContext(10, out ExpandedNodeId top);
            using var decoder = new BinaryDecoder(Array.Empty<byte>(), context);

            IEncodeable value = decoder.ReadEncodeable<IEncodeable>(null, top);

            Assert.That(CountInstances((Structure)value), Is.EqualTo((1 << 11) - 1));
        }

        [Test]
        public void NestedStructuresBeyondTheBudgetFailWithEncodingLimitsExceeded()
        {
            // 2^18 - 1 instances, several times the allowance, from zero bytes.
            ServiceMessageContext context = CreateContext(17, out ExpandedNodeId top);
            using var decoder = new BinaryDecoder(Array.Empty<byte>(), context);

            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEncodeable<IEncodeable>(null, top));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void NestedStructuresBeyondTheBudgetFailInsideAnExtensionObject()
        {
            ServiceMessageContext context = CreateContext(17, out _);
            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteNodeId(null, ExpandedNodeId.ToNodeId(EncodingIdOf(17), context.NamespaceUris));
                encoder.WriteByte(null, 1);
                encoder.WriteInt32(null, 0);
                buffer = encoder.CloseAndReturnBuffer()!;
            }
            using var decoder = new BinaryDecoder(buffer, context);

            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => decoder.ReadExtensionObject(null));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void LongArrayOfEmptyStructuresDecodes()
        {
            ServiceMessageContext context = CreateContext(0, out ExpandedNodeId empty);
            const int count = 65535;
            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteInt32(null, count);
                buffer = encoder.CloseAndReturnBuffer()!;
            }
            using var decoder = new BinaryDecoder(buffer, context);

            ArrayOf<IEncodeable> values = decoder.ReadEncodeableArray<IEncodeable>(null, empty);

            Assert.That(values.Count, Is.EqualTo(count));
        }

        [Test]
        public void StructuresBackedByInputDecodeBeyondTheAllowance()
        {
            // An array of structures with an Int32 field: the work grows with
            // the input, so the allowance does not limit it.
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            int ns = context.NamespaceUris.GetIndexOrAppend(kNs);
            var typeId = new NodeId("WithValue", (ushort)ns);
            Structure prototype = CreateStructure(
                context,
                typeId,
                [new StructureField { Name = "Value", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }],
                new Dictionary<string, BuiltInType> { ["Value"] = BuiltInType.Int32 });
            context.Factory.Builder.AddEncodeableType(prototype).Commit();
            int count = (int)BinaryDecoder.kStructureWorkAllowance + 1000;
            context.MaxArrayLength = count;
            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteInt32(null, count);
                for (int i = 0; i < count; i++)
                {
                    encoder.WriteInt32(null, i);
                }
                buffer = encoder.CloseAndReturnBuffer()!;
            }
            using var decoder = new BinaryDecoder(buffer, context);

            ArrayOf<IEncodeable> values = decoder.ReadEncodeableArray<IEncodeable>(
                null,
                NodeId.ToExpandedNodeId(typeId, context.NamespaceUris));

            Assert.That(values.Count, Is.EqualTo(count));
            Assert.That(((Structure)values[count - 1])["Value"], Is.EqualTo(new Variant(count - 1)));
        }

        private static int CountInstances(Structure structure)
        {
            int count = 1;
            foreach (Field field in structure.PropertyList)
            {
                object? value = field.Value.AsBoxedObject();
                if (value is ExtensionObject extension &&
                    extension.TryGetValue(out Structure? body))
                {
                    value = body;
                }
                if (value is Structure nested)
                {
                    count += CountInstances(nested);
                }
            }
            return count;
        }

        /// <summary>
        /// Registers T0 without fields and T1 to Tn, where each level has two
        /// fields of the type one level below.
        /// </summary>
        private static ServiceMessageContext CreateContext(int levels, out ExpandedNodeId top)
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            int ns = context.NamespaceUris.GetIndexOrAppend(kNs);
            IEncodeableFactoryBuilder builder = context.Factory.Builder;
            for (int level = 0; level <= levels; level++)
            {
                var fields = new List<StructureField>();
                if (level > 0)
                {
                    var child = new NodeId("T" + (level - 1), (ushort)ns);
                    fields.Add(new StructureField { Name = "A", DataType = child, ValueRank = ValueRanks.Scalar });
                    fields.Add(new StructureField { Name = "B", DataType = child, ValueRank = ValueRanks.Scalar });
                }
                builder.AddEncodeableType(CreateStructure(
                    context,
                    new NodeId("T" + level, (ushort)ns),
                    fields.ToArrayOf(),
                    []));
            }
            builder.Commit();
            top = new ExpandedNodeId("T" + levels, kNs);
            return context;
        }

        private static ExpandedNodeId EncodingIdOf(int level)
        {
            return new ExpandedNodeId("T" + level + "_Binary", kNs);
        }

        private static Structure CreateStructure(
            ServiceMessageContext context,
            NodeId typeId,
            ArrayOf<StructureField> fields,
            Dictionary<string, BuiltInType> fieldTypes)
        {
            string name = typeId.TryGetValue(out string s) ? s : "T";
            return new Structure(
                new XmlQualifiedName(name, kNs),
                NodeId.ToExpandedNodeId(typeId, context.NamespaceUris),
                new ExpandedNodeId(name + "_Binary", kNs),
                new ExpandedNodeId(name + "_Xml", kNs),
                new StructureDefinition
                {
                    BaseDataType = DataTypeIds.Structure,
                    StructureType = StructureType.Structure,
                    Fields = fields
                },
                fieldTypes);
        }
    }
}
