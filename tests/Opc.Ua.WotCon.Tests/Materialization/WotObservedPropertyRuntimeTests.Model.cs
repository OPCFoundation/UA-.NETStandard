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

using System;
using System.Collections.Generic;
using System.Xml;
using Opc.Ua.Export;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotObservedPropertyRuntimeTests
    {
        private static void RegisterObservedRange(IEncodeableFactory factory)
        {
            var type = new ObservedRangeType();
            var instance = new ObservedRange();
            factory.Builder.AddEncodeableType(instance.TypeId, type)
                .AddEncodeableType(instance.BinaryEncodingId, type)
                .AddEncodeableType(instance.XmlEncodingId, type).Commit();
        }

        private static UANode[] CreateObservedRangeNodes()
        {
            return
            [
                new UADataType
                {
                    NodeId = "ns=1;i=9102",
                    BrowseName = "1:ObservedRange",
                    Definition = new Export.DataTypeDefinition
                    {
                        Name = "ObservedRange",
                        BaseType = "i=22",
                        Field =
                        [
                            new DataTypeField { Name = "Low", DataType = "i=11", ValueRank = ValueRanks.Scalar },
                            new DataTypeField { Name = "High", DataType = "i=11", ValueRank = ValueRanks.Scalar }
                        ]
                    },
                    References =
                    [
                        new Reference { ReferenceType = "i=45", IsForward = false, Value = "i=22" },
                        new Reference { ReferenceType = "i=38", Value = "ns=1;i=9103" },
                        new Reference { ReferenceType = "i=38", Value = "ns=1;i=9104" }
                    ]
                },
                new UAObject
                {
                    NodeId = "ns=1;i=9103",
                    BrowseName = "Default Binary",
                    References = [new Reference { ReferenceType = "i=40", Value = "i=76" }]
                },
                new UAObject
                {
                    NodeId = "ns=1;i=9104",
                    BrowseName = "Default XML",
                    References = [new Reference { ReferenceType = "i=40", Value = "i=76" }]
                }
            ];
        }

        private sealed class ObservedRange : IEncodeable, IStructure
        {
            public double Low { get; set; }

            public double High { get; set; }

            public ExpandedNodeId TypeId => new(9102, "urn:wot-observe");

            public ExpandedNodeId BinaryEncodingId => new(9103, "urn:wot-observe");

            public ExpandedNodeId XmlEncodingId => new(9104, "urn:wot-observe");

            public Variant this[int index]
            {
                get => this[FieldName(index)];
                set => this[FieldName(index)] = value;
            }

            public Variant this[string name]
            {
                get => name switch
                {
                    nameof(Low) => new Variant(Low),
                    nameof(High) => new Variant(High),
                    _ => throw new ArgumentOutOfRangeException(nameof(name))
                };
                set
                {
                    if (!value.TryGetValue(out double number))
                    {
                        throw new ServiceResultException(StatusCodes.BadTypeMismatch);
                    }
                    switch (name)
                    {
                        case nameof(Low):
                            Low = number;
                            break;
                        case nameof(High):
                            High = number;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(name));
                    }
                }
            }

            public void Encode(IEncoder encoder)
            {
                encoder.PushNamespace("urn:wot-observe");
                encoder.WriteDouble(nameof(Low), Low);
                encoder.WriteDouble(nameof(High), High);
                encoder.PopNamespace();
            }

            public void Decode(IDecoder decoder)
            {
                decoder.PushNamespace("urn:wot-observe");
                Low = decoder.ReadDouble(nameof(Low));
                High = decoder.ReadDouble(nameof(High));
                decoder.PopNamespace();
            }

            public bool IsEqual(IEncodeable? encodeable)
            {
                return encodeable is ObservedRange range && Low.Equals(range.Low) && High.Equals(range.High);
            }

            IReadOnlyList<IStructureField> IStructure.GetFields()
            {
                return s_fields;
            }

            object ICloneable.Clone()
            {
                return new ObservedRange { Low = Low, High = High };
            }

            private static string FieldName(int index)
            {
                return index switch
                {
                    0 => nameof(Low),
                    1 => nameof(High),
                    _ => throw new ArgumentOutOfRangeException(nameof(index))
                };
            }

            private static readonly IStructureField[] s_fields =
            [
                new ObservedField(nameof(Low)),
                new ObservedField(nameof(High))
            ];
        }

        private sealed class ObservedField(string name) : IStructureField
        {
            public TypeInfo TypeInfo => new(BuiltInType.Double, ValueRanks.Scalar);

            public string Name { get; } = name;

            public bool IsOptional => false;
        }

        private sealed class ObservedRangeType : EncodeableType<ObservedRange>
        {
            public override XmlQualifiedName XmlName => new("ObservedRange", "urn:wot-observe");

            public override IEncodeable CreateInstance()
            {
                return new ObservedRange();
            }

            public override DataTypeDefinition GetDataTypeDefinition(NamespaceTable namespaceUris)
            {
                return new StructureDefinition
                {
                    BaseDataType = Ua.DataTypeIds.Structure,
                    StructureType = StructureType.Structure,
                    Fields =
                    [
                        new StructureField
                        {
                            Name = "Low", DataType = Ua.DataTypeIds.Double, ValueRank = ValueRanks.Scalar
                        },
                        new StructureField
                        {
                            Name = "High", DataType = Ua.DataTypeIds.Double, ValueRank = ValueRanks.Scalar
                        }
                    ]
                };
            }
        }
    }
}
