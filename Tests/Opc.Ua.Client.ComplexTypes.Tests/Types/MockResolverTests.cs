/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Core.Tests.Types.Encoders;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// Build custom types with a DataTypeDefinition.
    /// </summary>
    [TestFixture]
    [Category("ComplexTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class MockResolverTests : ComplexTypesCommon
    {
        public IServiceMessageContext EncoderContext;
        public Dictionary<StructureType, (ExpandedNodeId, Type)> TypeDictionary;

        public readonly string[] DefaultEncodings =
        [
            BrowseNames.DefaultBinary,
            BrowseNames.DefaultJson,
            BrowseNames.DefaultXml
        ];

        public enum TestRanks
        {
            Scalar = ValueRanks.Scalar,
            One = ValueRanks.OneDimension,
            Two = ValueRanks.TwoDimensions,
            Five = 5
        }

        public class TestType : IFormattable
        {
            public TestType(BuiltInType builtInType)
            {
                Name = Enum.GetName(
#if !NET8_0_OR_GREATER
                    typeof(BuiltInType),
#endif
                    builtInType);
                TypeId = new NodeId((uint)builtInType);
            }

            public TestType(string name, NodeId typeId)
            {
                Name = name;
                TypeId = typeId;
            }

            public string Name { get; }
            public NodeId TypeId { get; }

            public string ToString(string format, IFormatProvider formatProvider)
            {
                return Name;
            }
        }

        public class TestTypeCollection : List<TestType>
        {
            public TestTypeCollection()
            {
            }

            public TestTypeCollection(IEnumerable<TestType> collection)
                : base(collection)
            {
            }

            public TestTypeCollection(int capacity)
                : base(capacity)
            {
            }

            public static TestTypeCollection ToTestTypeCollection(TestType[] values)
            {
                return values != null ? [.. values] : [];
            }

            public void Add(BuiltInType builtInType)
            {
                Add(new TestType(builtInType));
            }

            public void Add(string name, NodeId typeId)
            {
                Add(new TestType(name, typeId));
            }
        }

        [DatapointSource]
        public static readonly TestType[] TypeSource = new TestTypeCollection(
#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS
            Enum.GetValues<BuiltInType>()
#else
            Enum.GetValues(typeof(BuiltInType))
                .Cast<BuiltInType>()
#endif
                .Where(b => b is > BuiltInType.Null and <= BuiltInType.DiagnosticInfo)
                .Select(b => new TestType(b)))
        {
            { nameof(DataTypeIds.BuildInfo), DataTypeIds.BuildInfo },
            { nameof(DataTypeIds.Duration), DataTypeIds.Duration },
            { nameof(DataTypeIds.BaseDataType), DataTypeIds.BaseDataType },
            { nameof(DataTypeIds.Structure), DataTypeIds.Structure }
        }.ToArray();

        [OneTimeSetUp]
        protected new void OneTimeSetUp()
        {
            base.OneTimeSetUp();
        }

        [OneTimeTearDown]
        protected new void OneTimeTearDown()
        {
            base.OneTimeTearDown();
        }

        [SetUp]
        protected new void SetUp()
        {
            base.SetUp();
        }

        [TearDown]
        protected new void TearDown()
        {
            base.TearDown();
        }

        /// <summary>
        /// Test the functionality to create a custom complex type.
        /// </summary>
        [Theory]
        public async Task CreateMockTypeAsync(
            [ValueSource(
                nameof(EncodingTypesReversibleCompact))] EncodingTypeGroup encoderTypeGroup,
            MemoryStreamType memoryStreamType)
        {
            var mockResolver = new MockResolver();
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;

            ushort nameSpaceIndex = mockResolver.NamespaceUris
                .GetIndexOrAppend(Namespaces.MockResolverUrl);
            uint nodeId = 100;

            var structure = new StructureDefinition { BaseDataType = DataTypeIds.Structure };
            var field = new StructureField
            {
                Name = "Make",
                Description = new LocalizedText("The make"),
                DataType = DataTypeIds.String,
                ValueRank = ValueRanks.Scalar,
                ArrayDimensions = Array.Empty<uint>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);
            field = new StructureField
            {
                Name = "Model",
                Description = new LocalizedText("The model"),
                DataType = DataTypeIds.String,
                ValueRank = ValueRanks.Scalar,
                ArrayDimensions = Array.Empty<uint>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);
            field = new StructureField
            {
                Name = "Engine",
                Description = new LocalizedText("The engine"),
                DataType = DataTypeIds.String,
                ValueRank = ValueRanks.Scalar,
                ArrayDimensions = Array.Empty<uint>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);
            field = new StructureField
            {
                Name = "NoOfPassengers",
                Description = new LocalizedText("The number of passengers"),
                DataType = DataTypeIds.UInt32,
                ValueRank = ValueRanks.Scalar,
                ArrayDimensions = Array.Empty<uint>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);

            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(nodeId++, nameSpaceIndex),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName("CarType", nameSpaceIndex),
                DisplayName = new LocalizedText("CarType"),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(structure)
            };

            foreach (string encodingName in DefaultEncodings)
            {
                // binary encoding
                var description = new ReferenceDescription
                {
                    NodeId = new NodeId(nodeId++, nameSpaceIndex),
                    ReferenceTypeId = new NodeId(nodeId++, nameSpaceIndex),
                    BrowseName = encodingName,
                    DisplayName = new LocalizedText("MockType_" + encodingName),
                    IsForward = true,
                    NodeClass = NodeClass.Object
                };
                var encoding = new Node(description);

                // add reference to encoding
                var reference = new ReferenceNode
                {
                    ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                    IsInverse = false,
                    TargetId = description.NodeId
                };
                mockResolver.DataTypeNodes[encoding.NodeId] = encoding;
                dataTypeNode.References.Add(reference);
            }

            // add type
            mockResolver.DataTypeNodes[dataTypeNode.NodeId] = dataTypeNode;

            var cts = new ComplexTypeSystem(mockResolver);
            Type carType = await cts.LoadTypeAsync(dataTypeNode.NodeId, false, true)
                .ConfigureAwait(false);
            Assert.NotNull(carType);

            var car = (BaseComplexType)Activator.CreateInstance(carType);

            TestContext.Out.WriteLine(car.ToString());

            car["Make"] = "Toyota";
            car["Model"] = "Land Cruiser";
            car["Engine"] = "Diesel";
            car["NoOfPassengers"] = (uint)5;

            TestContext.Out.WriteLine(car.ToString());

            var encoderContext = new ServiceMessageContext
            {
                Factory = mockResolver.Factory,
                NamespaceUris = mockResolver.NamespaceUris
            };

            byte[] buffer;
            using (MemoryStream encoderStream = CreateEncoderMemoryStream(memoryStreamType))
            {
                using (IEncoder encoder = CreateEncoder(
                    EncodingType.Json,
                    encoderContext,
                    encoderStream,
                    carType))
                {
                    encoder.WriteEncodeable("Car", car, carType);
                }
                buffer = encoderStream.ToArray();
            }

            _ = PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));

            // test encoder/decoder
            EncodeDecodeComplexType(
                encoderContext,
                memoryStreamType,
                encoderType,
                jsonEncodingType,
                StructureType.Structure,
                dataTypeNode.NodeId,
                car);

            // Test extracting type definition

            NodeIdDictionary<DataTypeDefinition> definitions = cts
                .GetDataTypeDefinitionsForDataType(
                    dataTypeNode.NodeId);
            Assert.IsNotEmpty(definitions);
            Assert.AreEqual(1, definitions.Count);
            Assert.AreEqual(structure, definitions[dataTypeNode.NodeId]);
        }

        /// <summary>
        /// Test the functionality to create a custom complex type.
        /// </summary>
        [Theory]
        public async Task CreateMockArrayTypeAsync(
            [ValueSource(
                nameof(EncodingTypesReversibleCompact))] EncodingTypeGroup encoderTypeGroup,
            MemoryStreamType memoryStreamType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            var mockResolver = new MockResolver();

            // only enumerable types in the encodeable factory are stored as Enum in a structure.
            AddEncodeableType(
                mockResolver.Factory,
                mockResolver.NamespaceUris,
                DataTypeIds.NamingRuleType,
                typeof(NamingRuleType));

            ushort nameSpaceIndex = mockResolver.NamespaceUris.GetIndexOrAppend(
                "http://opcfoundation.org/MockResolver");
            uint nodeId = 100;

            var structure = new StructureDefinition { BaseDataType = DataTypeIds.Structure };
            var field = new StructureField
            {
                Name = "ArrayOfInteger",
                Description = new LocalizedText("Array of Integer"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.OneDimension,
                ArrayDimensions = Array.Empty<uint>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);

            field = new StructureField
            {
                Name = "Array2DOfInteger",
                Description = new LocalizedText("2D Array of Integer"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.TwoDimensions,
                ArrayDimensions = Array.Empty<uint>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);

            field = new StructureField
            {
                Name = "Array3DOfInteger",
                Description = new LocalizedText("3D Array of Integer"),
                DataType = DataTypeIds.Int32,
                ValueRank = 3,
                ArrayDimensions = Array.Empty<uint>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);

            field = new StructureField
            {
                Name = "ArrayOfNamingRuleType",
                Description = new LocalizedText("Array of NamingRuleType"),
                DataType = DataTypeIds.NamingRuleType,
                ValueRank = ValueRanks.OneDimension,
                ArrayDimensions = Array.Empty<uint>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);

            field = new StructureField
            {
                Name = "Array2DOfNamingRuleType",
                Description = new LocalizedText("Array 2D of NamingRuleType"),
                DataType = DataTypeIds.NamingRuleType,
                ValueRank = ValueRanks.TwoDimensions,
                ArrayDimensions = Array.Empty<uint>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);

            field = new StructureField
            {
                Name = "Array3DOfNamingRuleType",
                Description = new LocalizedText("Array 3D of NamingRuleType"),
                DataType = DataTypeIds.NamingRuleType,
                ValueRank = 3,
                ArrayDimensions = Array.Empty<uint>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);

            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(nodeId++, nameSpaceIndex),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName("ArrayTypes", nameSpaceIndex),
                DisplayName = new LocalizedText("ArrayTypes"),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(structure)
            };

            foreach (string encodingName in DefaultEncodings)
            {
                // encoding
                var description = new ReferenceDescription
                {
                    NodeId = new NodeId(nodeId++, nameSpaceIndex),
                    ReferenceTypeId = new NodeId(nodeId++, nameSpaceIndex),
                    BrowseName = encodingName,
                    DisplayName = new LocalizedText("MockType_" + encodingName),
                    IsForward = true,
                    NodeClass = NodeClass.Object
                };
                var encoding = new Node(description);

                // add reference to encoding
                var reference = new ReferenceNode
                {
                    ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                    IsInverse = false,
                    TargetId = description.NodeId
                };
                mockResolver.DataTypeNodes[encoding.NodeId] = encoding;
                dataTypeNode.References.Add(reference);
            }

            // add types needed
            mockResolver.DataTypeNodes[dataTypeNode.NodeId] = dataTypeNode;

            var cts = new ComplexTypeSystem(mockResolver);
            Type arraysTypes = await cts.LoadTypeAsync(dataTypeNode.NodeId, false, true)
                .ConfigureAwait(false);
            Assert.NotNull(arraysTypes);

            var arrays = (BaseComplexType)Activator.CreateInstance(arraysTypes);

            TestContext.Out.WriteLine(arrays.ToString());

            arrays["ArrayOfInteger"] = new int[] { 1, 4, 8, 12, 22 };
            arrays["Array2DOfInteger"] = new int[,]
            {
                { 11, 12, 13, 14, 15 },
                { 21, 22, 23, 24, 25 },
                { 31, 32, 33, 34, 35 }
            };
            arrays["Array3DOfInteger"] = new int[,,]
            {
                {
                    { 11, 12, 13, 14, 15 },
                    { 21, 22, 23, 24, 25 },
                    { 31, 32, 33, 34, 35 }
                },
                {
                    { 41, 42, 43, 44, 45 },
                    { 51, 52, 53, 54, 55 },
                    { 61, 62, 63, 64, 65 }
                }
            };
            arrays["ArrayOfNamingRuleType"] = new NamingRuleType[]
            {
                NamingRuleType.Mandatory,
                NamingRuleType.Optional,
                NamingRuleType.Constraint
            };
            // note: an assignement of the Int32[] to an enum type is a supported cast,
            // but the Encode/Decode test would fail because the int/Enum compare different
            // arrays["ArrayOfNamingRuleType"] = new Int32[] { 0,2,1 };
            arrays["Array2DOfNamingRuleType"] = new NamingRuleType[,]
            {
                { NamingRuleType.Mandatory, NamingRuleType.Optional, NamingRuleType.Constraint },
                { NamingRuleType.Optional, NamingRuleType.Mandatory, NamingRuleType.Constraint }
            };
            arrays["Array3DOfNamingRuleType"] = new NamingRuleType[,,]
            {
                {
                    { NamingRuleType.Mandatory, NamingRuleType.Optional, NamingRuleType.Mandatory },
                    { NamingRuleType.Optional, NamingRuleType.Mandatory, NamingRuleType.Mandatory }
                },
                {
                    {
                        NamingRuleType.Mandatory,
                        NamingRuleType.Optional,
                        NamingRuleType.Constraint },
                    { NamingRuleType.Optional, NamingRuleType.Mandatory, NamingRuleType.Constraint }
                }
            };

            TestContext.Out.WriteLine(arrays.ToString());

            var encoderContext = new ServiceMessageContext
            {
                Factory = mockResolver.Factory,
                NamespaceUris = mockResolver.NamespaceUris
            };

            byte[] buffer;
            using (MemoryStream encoderStream = CreateEncoderMemoryStream(memoryStreamType))
            {
                using (IEncoder encoder = CreateEncoder(
                    EncodingType.Json,
                    encoderContext,
                    encoderStream,
                    arraysTypes))
                {
                    encoder.WriteEncodeable("Arrays", arrays, arraysTypes);
                }
                buffer = encoderStream.ToArray();
            }

            _ = PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));

            // test encoder/decoder
            EncodeDecodeComplexType(
                encoderContext,
                memoryStreamType,
                encoderType,
                jsonEncodingType,
                StructureType.Structure,
                dataTypeNode.NodeId,
                arrays);

            // Test extracting type definition

            NodeIdDictionary<DataTypeDefinition> definitions = cts
                .GetDataTypeDefinitionsForDataType(
                    dataTypeNode.NodeId);
            Assert.IsNotEmpty(definitions);
            Assert.AreEqual(1, definitions.Count);
            Assert.AreEqual(structure, definitions[dataTypeNode.NodeId]);
        }

        /// <summary>
        /// Create a complex type with a single scalar or array type, with default and random values.
        /// </summary>
        [Theory]
        public async Task CreateMockSingleTypeAsync(
            [ValueSource(
                nameof(EncodingTypesReversibleCompact))] EncodingTypeGroup encoderTypeGroup,
            MemoryStreamType memoryStreamType,
            TestType typeDescription,
            bool randomValues,
            TestRanks testRank)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            SetRepeatedRandomSeed();

            var mockResolver = new MockResolver();

            // only enumerable types in the encodeable factory are stored as Enum in a structure.
            AddEncodeableType(
                mockResolver.Factory,
                mockResolver.NamespaceUris,
                DataTypeIds.NamingRuleType,
                typeof(NamingRuleType));

            ushort nameSpaceIndex = mockResolver.NamespaceUris.GetIndexOrAppend(
                "http://opcfoundation.org/MockResolver");
            uint nodeId = 100;

            var structure = new StructureDefinition { BaseDataType = DataTypeIds.Structure };

            int valueRank = (int)testRank;
            string typeName = typeDescription.Name;
            string arrayPrefix = Enum.GetName(typeof(TestRanks), valueRank);
            string seperator = valueRank > 0 ? "Of" : string.Empty;
            var field = new StructureField
            {
                Name = arrayPrefix + seperator + typeName,
                Description = new LocalizedText(arrayPrefix + " " + seperator + " " + typeName),
                DataType = typeDescription.TypeId,
                ValueRank = valueRank,
                ArrayDimensions = Array.Empty<uint>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);

            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(nodeId++, nameSpaceIndex),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName(field.Name + "TestType", nameSpaceIndex),
                DisplayName = new LocalizedText(field.Description + " Test Type"),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(structure)
            };

            foreach (string encodingName in DefaultEncodings)
            {
                // encoding
                var description = new ReferenceDescription
                {
                    NodeId = new NodeId(nodeId++, nameSpaceIndex),
                    ReferenceTypeId = new NodeId(nodeId++, nameSpaceIndex),
                    BrowseName = encodingName,
                    DisplayName = new LocalizedText("MockType_" + encodingName),
                    IsForward = true,
                    NodeClass = NodeClass.Object
                };
                var encoding = new Node(description);

                // add reference to encoding
                var reference = new ReferenceNode
                {
                    ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                    IsInverse = false,
                    TargetId = description.NodeId
                };
                mockResolver.DataTypeNodes[encoding.NodeId] = encoding;
                dataTypeNode.References.Add(reference);
            }

            // add types needed
            mockResolver.DataTypeNodes[dataTypeNode.NodeId] = dataTypeNode;

            var cts = new ComplexTypeSystem(mockResolver);
            Type arraysTypes = await cts.LoadTypeAsync(dataTypeNode.NodeId, false, true)
                .ConfigureAwait(false);
            Assert.NotNull(arraysTypes);

            var testType = (BaseComplexType)Activator.CreateInstance(arraysTypes);
            Assert.NotNull(testType);

            TestContext.Out.WriteLine(testType.ToString());

            object value;
            Type valueType = TypeInfo.GetSystemType(field.DataType, mockResolver.Factory);
            BuiltInType builtInType = TypeInfo.GetBuiltInType(field.DataType);
            if (valueRank == ValueRanks.Scalar)
            {
                if (builtInType > 0)
                {
                    if (randomValues)
                    {
                        value = DataGenerator.GetRandom(builtInType);
                    }
                    else
                    {
                        switch (builtInType)
                        {
                            case BuiltInType.DataValue:
                                value = new DataValue();
                                break;
                            case BuiltInType.DiagnosticInfo:
                                value = new DiagnosticInfo();
                                break;
                            default:
                                value = TypeInfo.GetDefaultValue(builtInType);
                                break;
                        }
                    }
                }
                else
                {
                    value = Activator.CreateInstance(valueType);
                }
            }
            else
            {
                int[] dimensions = new int[valueRank];
                if (builtInType > 0)
                {
                    if (randomValues)
                    {
                        for (int ii = 0; ii < dimensions.Length; ii++)
                        {
                            dimensions[ii] = (DataGenerator.GetRandom<int>(false) & 3) + 1;
                        }
                        Array array = TypeInfo.CreateArray(builtInType, dimensions);
                        int[] indices = new int[valueRank];
                        for (int ii = 0; ii < array.Length; ii++)
                        {
                            array.SetValue(DataGenerator.GetRandom(builtInType), indices);
                            Iterate(dimensions, indices);
                        }
                        value = array;
                    }
                    else
                    {
                        value = TypeInfo.CreateArray(builtInType, dimensions);
                    }
                }
                else
                {
                    var array = Array.CreateInstance(valueType, dimensions);

                    if (randomValues)
                    {
                        int[] indices = new int[valueRank];
                        for (int ii = 0; ii < array.Length; ii++)
                        {
                            array.SetValue(GetRandom(field.DataType), indices);
                            Iterate(dimensions, indices);
                        }
                    }

                    value = array;
                }
            }
            testType[field.Name] = value;

            TestContext.Out.WriteLine(testType.ToString());

            var encoderContext = new ServiceMessageContext
            {
                Factory = mockResolver.Factory,
                NamespaceUris = mockResolver.NamespaceUris
            };

            byte[] buffer;
            using (MemoryStream encoderStream = CreateEncoderMemoryStream(memoryStreamType))
            {
                using (IEncoder encoder = CreateEncoder(
                    EncodingType.Json,
                    encoderContext,
                    encoderStream,
                    arraysTypes))
                {
                    encoder.WriteEncodeable("TestType", testType, arraysTypes);
                }
                buffer = encoderStream.ToArray();
            }

            _ = PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));

            // test encoder/decoder
            EncodeDecodeComplexType(
                encoderContext,
                memoryStreamType,
                encoderType,
                jsonEncodingType,
                StructureType.Structure,
                dataTypeNode.NodeId,
                testType);

            // Test extracting type definition

            NodeIdDictionary<DataTypeDefinition> definitions = cts
                .GetDataTypeDefinitionsForDataType(
                    dataTypeNode.NodeId);
            Assert.IsNotEmpty(definitions);
            Assert.AreEqual(1, definitions.Count);
            Assert.AreEqual(structure, definitions[dataTypeNode.NodeId]);
        }

        [Test]
        public void CreateBaseComplexTypeTest()
        {
            _ = new TestDataComplexType
            {
                PropertyInt8 = 1,
                PropertyInt16 = 2,
                PropertyInt32 = 3,
                PropertyInt64 = 4,
                PropertyInt32Array = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12],
                PropertyInt322DArray = new[,]
                {
                    { 1, 2, 3 },
                    { 4, 5, 6 }
                },
                PropertyInt325DArray = new[, , , ,]
                {
                    {
                        {
                            {
                                { 1, 2, 3 },
                                { 4, 5, 6 }
                            },
                            {
                                { 7, 8, 9 },
                                { 10, 11, 12 }
                            }
                        },
                        {
                            {
                                { 111, 112, 113 },
                                { 114, 115, 116 }
                            },
                            {
                                { 117, 118, 119 },
                                { 1110, 1111, 1112 }
                            }
                        },
                        {
                            {
                                { 311, 312, 313 },
                                { 314, 315, 316 }
                            },
                            {
                                { 317, 318, 319 },
                                { 3110, 3111, 3112 }
                            }
                        }
                    },
                    {
                        {
                            {
                                { 71, 72, 73 },
                                { 74, 75, 76 }
                            },
                            {
                                { 77, 78, 79 },
                                { 710, 711, 712 }
                            }
                        },
                        {
                            {
                                { 7111, 7112, 7113 },
                                { 7114, 7115, 7116 }
                            },
                            {
                                { 7117, 7118, 7119 },
                                { 71110, 71111, 71112 }
                            }
                        },
                        {
                            {
                                { 7311, 7312, 7313 },
                                { 7314, 7315, 7316 }
                            },
                            {
                                { 7317, 7318, 7319 },
                                { 73110, 73111, 73112 }
                            }
                        }
                    }
                }
            };
        }

        private object GetRandom(NodeId valueType)
        {
            BuiltInType builtInType = TypeInfo.GetBuiltInType(valueType);
            if (builtInType != BuiltInType.Null)
            {
                return DataGenerator.GetRandom(builtInType);
            }
            if (valueType == DataTypeIds.BuildInfo)
            {
                return new BuildInfo
                {
                    BuildDate = DataGenerator.GetRandomDateTime(),
                    BuildNumber = "1.4." +
                        DataGenerator.GetRandomByte().ToString(CultureInfo.InvariantCulture),
                    ManufacturerName = "OPC Foundation",
                    ProductName = "Complex Type Client",
                    ProductUri = "http://opcfoundation.org/ComplexTypeClient"
                };
            }

            NUnit.Framework.Assert.Fail($"Unexpected ValueType {valueType}");
            return null;
        }

        private static void Iterate(int[] dimensions, int[] indices)
        {
            for (int i = 0; i < dimensions.Length; i++)
            {
                indices[i]++;
                if (indices[i] < dimensions[i])
                {
                    break;
                }
                indices[i] = 0;
            }
        }

        protected void AddEncodeableType(
            IEncodeableFactory factory,
            NamespaceTable namespaceUris,
            ExpandedNodeId typeId,
            Type enumType)
        {
            if (NodeId.IsNull(typeId) || enumType == null)
            {
                return;
            }
            ExpandedNodeId internalNodeId = NormalizeExpandedNodeId(typeId, namespaceUris);
            TestContext.Out.WriteLine("Adding Type {0} as: {1}", enumType.FullName, internalNodeId);
            factory.AddEncodeableType(internalNodeId, enumType);
        }

        private static ExpandedNodeId NormalizeExpandedNodeId(
            ExpandedNodeId expandedNodeId,
            NamespaceTable namespaceUris)
        {
            var nodeId = ExpandedNodeId.ToNodeId(expandedNodeId, namespaceUris);
            return NodeId.ToExpandedNodeId(nodeId, namespaceUris);
        }
    }

    [StructureDefinition(BaseDataType = StructureBaseDataType.Structure)]
    [StructureTypeId(
        ComplexTypeId = "i=10000",
        BinaryEncodingId = "i=10001",
        XmlEncodingId = "i=10002")]
    public class TestDataComplexType : BaseComplexType
    {
        [DataMember(Order = 0)]
        [StructureField(BuiltInType = (int)BuiltInType.SByte)]
        public sbyte PropertyInt8 { get; set; }

        [DataMember(Order = 1)]
        [StructureField(BuiltInType = (int)BuiltInType.Int16)]
        public short PropertyInt16 { get; set; }

        [DataMember(Order = 2)]
        [StructureField(BuiltInType = (int)BuiltInType.Int32)]
        public int PropertyInt32 { get; set; }

        [DataMember(Order = 3)]
        [StructureField(BuiltInType = (int)BuiltInType.Int64)]
        public long PropertyInt64 { get; set; }

        [DataMember(Order = 4)]
        [StructureField(BuiltInType = (int)BuiltInType.Int32, ValueRank = 1, IsOptional = false)]
        public int[] PropertyInt32Array { get; set; }

        [DataMember(Order = 5)]
        [StructureField(BuiltInType = (int)BuiltInType.Int32, ValueRank = 2, IsOptional = false)]
        public int[,] PropertyInt322DArray { get; set; }

        [DataMember(Order = 6)]
        [StructureField(BuiltInType = (int)BuiltInType.Int32, ValueRank = 5, IsOptional = false)]
        public int[,,,,] PropertyInt325DArray { get; set; }
    }
}
