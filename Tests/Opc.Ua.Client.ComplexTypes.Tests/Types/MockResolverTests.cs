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
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Core.Tests.Types.Encoders;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// Main purpose of this test is to verify the
    /// system.emit functionality on a target platform.
    /// </summary>
    [TestFixture, Category("ComplexTypes")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class MockResolverTests : ComplexTypesCommon
    {
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
        [Test]
        public async Task CreateMockTypeAsync()
        {
            var mockResolver = new MockResolver();

            var nameSpaceIndex = mockResolver.NamespaceUris.GetIndexOrAppend("http://opcfoundation.org/MockResolver");
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

            // binary encoding
            var description = new ReferenceDescription() {
                NodeId = new NodeId(nodeId++, nameSpaceIndex),
                ReferenceTypeId = new NodeId(nodeId++, nameSpaceIndex),
                BrowseName = BrowseNames.DefaultBinary,
                DisplayName = new LocalizedText(BrowseNames.DefaultBinary),
                IsForward = true,
                NodeClass = NodeClass.Object
            };
            var encoding = new Node(description);

            // add reference to encoding
            var reference = new ReferenceNode()
            {
                ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                IsInverse = false,
                TargetId = description.NodeId
            };

            mockResolver.DataTypeNodes[encoding.NodeId] = encoding;

            // add type
            dataTypeNode.References.Add(reference);
            mockResolver.DataTypeNodes[dataTypeNode.NodeId] = dataTypeNode;

            var cts = new ComplexTypeSystem(mockResolver);
            var carType = await cts.LoadType(dataTypeNode.NodeId, false, true).ConfigureAwait(false);

            BaseComplexType car = (BaseComplexType)Activator.CreateInstance(carType);

            TestContext.Out.WriteLine(car.ToString());

            car["Make"] = "Toyota";
            car["Model"] = "Land Cruiser";
            car["Engine"] = "Diesel";
            car["NoOfPassengers"] = (UInt32)5;

            TestContext.Out.WriteLine(car.ToString());

            var encoderStream = new MemoryStream();
            ServiceMessageContext encoderContext = new ServiceMessageContext();
            encoderContext.Factory = mockResolver.Factory;
            IEncoder encoder = CreateEncoder(EncodingType.Json, encoderContext, encoderStream, carType);
            encoder.WriteEncodeable("Car", car, carType);
            Dispose(encoder);
            var buffer = encoderStream.ToArray();
            string jsonFormatted = PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));
        }
        #endregion

        #region Private Methods
        #endregion Private Methods
    }
}
