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
using System.IO;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Core.Tests.Types.Encoders;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// Build custom types with a DataTypeDefinition.
    /// </summary>
    [TestFixture, Category("ComplexTypes")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class MockResolverTests : ComplexTypesCommon
    {
        public IServiceMessageContext EncoderContext;
        public Dictionary<StructureType, (ExpandedNodeId, Type)> TypeDictionary;

        public readonly string[] DefaultEncodings = new string[] { BrowseNames.DefaultBinary, BrowseNames.DefaultJson, BrowseNames.DefaultXml };

        #region Test Setup
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
        #endregion

        #region Test Methods
        /// <summary>
        /// Test the functionality to create a custom complex type.
        /// </summary>
        [Theory]
        public async Task CreateMockTypeAsync(EncodingType encodingType)
        {
            var mockResolver = new MockResolver();

            var nameSpaceIndex = mockResolver.NamespaceUris.GetIndexOrAppend(Namespaces.MockResolverUrl);
            uint nodeId = 100;

            var structure = new StructureDefinition() {
                BaseDataType = DataTypeIds.Structure
            };
            var field = new StructureField() {
                Name = "Make",
                Description = new LocalizedText("The make"),
                DataType = DataTypeIds.String,
                ValueRank = ValueRanks.Scalar,
                ArrayDimensions = Array.Empty<UInt32>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);
            field = new StructureField() {
                Name = "Model",
                Description = new LocalizedText("The model"),
                DataType = DataTypeIds.String,
                ValueRank = ValueRanks.Scalar,
                ArrayDimensions = Array.Empty<UInt32>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);
            field = new StructureField() {
                Name = "Engine",
                Description = new LocalizedText("The engine"),
                DataType = DataTypeIds.String,
                ValueRank = ValueRanks.Scalar,
                ArrayDimensions = Array.Empty<UInt32>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);
            field = new StructureField() {
                Name = "NoOfPassengers",
                Description = new LocalizedText("The number of passengers"),
                DataType = DataTypeIds.UInt32,
                ValueRank = ValueRanks.Scalar,
                ArrayDimensions = Array.Empty<UInt32>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);

            var dataTypeNode = new DataTypeNode() {
                NodeId = new NodeId(nodeId++, nameSpaceIndex),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName("CarType", nameSpaceIndex),
                DisplayName = new LocalizedText("CarType"),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(structure)
            };

            foreach (var encodingName in DefaultEncodings)
            {
                // binary encoding
                var description = new ReferenceDescription() {
                    NodeId = new NodeId(nodeId++, nameSpaceIndex),
                    ReferenceTypeId = new NodeId(nodeId++, nameSpaceIndex),
                    BrowseName = encodingName,
                    DisplayName = new LocalizedText("MockType_" + encodingName),
                    IsForward = true,
                    NodeClass = NodeClass.Object
                };
                var encoding = new Node(description);

                // add reference to encoding
                var reference = new ReferenceNode() {
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
            var carType = await cts.LoadType(dataTypeNode.NodeId, false, true).ConfigureAwait(false);
            Assert.NotNull(carType);

            BaseComplexType car = (BaseComplexType)Activator.CreateInstance(carType);

            TestContext.Out.WriteLine(car.ToString());

            car["Make"] = "Toyota";
            car["Model"] = "Land Cruiser";
            car["Engine"] = "Diesel";
            car["NoOfPassengers"] = (UInt32)5;

            TestContext.Out.WriteLine(car.ToString());

            var encoderStream = new MemoryStream();
            ServiceMessageContext encoderContext = new ServiceMessageContext {
                Factory = mockResolver.Factory,
                NamespaceUris = mockResolver.NamespaceUris,
            };
            IEncoder encoder = CreateEncoder(EncodingType.Json, encoderContext, encoderStream, carType);
            encoder.WriteEncodeable("Car", car, carType);
            Dispose(encoder);
            var buffer = encoderStream.ToArray();
            _ = PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));

            // test encoder/decoder
            EncodeDecodeComplexType(encoderContext, encodingType, StructureType.Structure, nodeId, car);

        }

        /// <summary>
        /// Test the functionality to create a custom complex type.
        /// </summary>
        [Theory]
        public async Task CreateMockArrayTypeAsync(EncodingType encodingType)
        {
            var mockResolver = new MockResolver();

            // only enumerable types in the encodeable factory are stored as Enum in a structure.
            AddEncodeableType(mockResolver.Factory, mockResolver.NamespaceUris, DataTypeIds.NamingRuleType, typeof(NamingRuleType));

            var nameSpaceIndex = mockResolver.NamespaceUris.GetIndexOrAppend("http://opcfoundation.org/MockResolver");
            uint nodeId = 100;

            var structure = new StructureDefinition() {
                BaseDataType = DataTypeIds.Structure
            };
            var field = new StructureField() {
                Name = "ArrayOfInteger",
                Description = new LocalizedText("Array of Integer"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.OneDimension,
                ArrayDimensions = Array.Empty<UInt32>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);

            field = new StructureField() {
                Name = "Array2DOfInteger",
                Description = new LocalizedText("2D Array of Integer"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.TwoDimensions,
                ArrayDimensions = Array.Empty<UInt32>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);

            field = new StructureField() {
                Name = "Array3DOfInteger",
                Description = new LocalizedText("3D Array of Integer"),
                DataType = DataTypeIds.Int32,
                ValueRank = 3,
                ArrayDimensions = Array.Empty<UInt32>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);

            field = new StructureField() {
                Name = "ArrayOfNamingRuleType",
                Description = new LocalizedText("Array of NamingRuleType"),
                DataType = DataTypeIds.NamingRuleType,
                ValueRank = ValueRanks.OneDimension,
                ArrayDimensions = Array.Empty<UInt32>(),
                MaxStringLength = 0,
                IsOptional = false
            };
            structure.Fields.Add(field);

            var dataTypeNode = new DataTypeNode() {
                NodeId = new NodeId(nodeId++, nameSpaceIndex),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName("ArrayTypes", nameSpaceIndex),
                DisplayName = new LocalizedText("ArrayTypes"),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(structure)
            };

            foreach (var encodingName in DefaultEncodings)
            {
                // encoding
                var description = new ReferenceDescription() {
                    NodeId = new NodeId(nodeId++, nameSpaceIndex),
                    ReferenceTypeId = new NodeId(nodeId++, nameSpaceIndex),
                    BrowseName = encodingName,
                    DisplayName = new LocalizedText("MockType_" + encodingName),
                    IsForward = true,
                    NodeClass = NodeClass.Object
                };
                var encoding = new Node(description);

                // add reference to encoding
                var reference = new ReferenceNode() {
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
            var arraysTypes = await cts.LoadType(dataTypeNode.NodeId, false, true).ConfigureAwait(false);
            Assert.NotNull(arraysTypes);

            BaseComplexType arrays = (BaseComplexType)Activator.CreateInstance(arraysTypes);

            TestContext.Out.WriteLine(arrays.ToString());

            arrays["ArrayOfInteger"] = new Int32[] { 1, 4, 8, 12, 22 };
            arrays["Array2DOfInteger"] = new Int32[,] {
                { 11, 12, 13, 14, 15 }, { 21, 22, 23, 24, 25 }, { 31, 32, 33, 34, 35 } };
            arrays["Array3DOfInteger"] = new Int32[,,] {
                { { 11, 12, 13, 14, 15 }, { 21, 22, 23, 24, 25 }, { 31, 32, 33, 34, 35 } },
                { { 41, 42, 43, 44, 45 }, { 51, 52, 53, 54, 55 }, { 61, 62, 63, 64, 65 } } };
            arrays["ArrayOfNamingRuleType"] = new NamingRuleType[] { NamingRuleType.Mandatory, NamingRuleType.Optional, NamingRuleType.Constraint };
            //arrays["ArrayOfNamingRuleType"] = new Int32[] { 0,2,1 };

            TestContext.Out.WriteLine(arrays.ToString());

            var encoderStream = new MemoryStream();
            ServiceMessageContext encoderContext = new ServiceMessageContext {
                Factory = mockResolver.Factory,
                NamespaceUris = mockResolver.NamespaceUris,
            };

            IEncoder encoder = CreateEncoder(EncodingType.Json, encoderContext, encoderStream, arraysTypes);
            encoder.WriteEncodeable("Arrays", arrays, arraysTypes);
            Dispose(encoder);
            var buffer = encoderStream.ToArray();
            _ = PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));

            // test encoder/decoder
            EncodeDecodeComplexType(encoderContext, encodingType, StructureType.Structure, dataTypeNode.NodeId, arrays);
        }
        #endregion

        #region Private Methods
        protected void AddEncodeableType(IEncodeableFactory factory, NamespaceTable namespaceUris, ExpandedNodeId typeId, Type enumType)
        {
            if (NodeId.IsNull(typeId) || enumType == null)
            {
                return;
            }
            var internalNodeId = NormalizeExpandedNodeId(typeId, namespaceUris);
            TestContext.Out.WriteLine("Adding Type {0} as: {1}", enumType.FullName, internalNodeId);
            factory.AddEncodeableType(internalNodeId, enumType);
        }

        private ExpandedNodeId NormalizeExpandedNodeId(ExpandedNodeId expandedNodeId, NamespaceTable namespaceUris)
        {
            var nodeId = ExpandedNodeId.ToNodeId(expandedNodeId, namespaceUris);
            return NodeId.ToExpandedNodeId(nodeId, namespaceUris);
        }
        #endregion Private Methods
    }
}
