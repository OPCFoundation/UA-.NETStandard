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
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Types.Constants
{
    /// <summary>
    /// Tests for the DataTypes class helper methods.
    /// </summary>
    [TestFixture]
    [Category("DataTypes")]
    [Parallelizable]
    public class DataTypesTests
    {
        /// <summary>
        /// Test GetBrowseName for standard data types.
        /// </summary>
        [Test]
        public void GetBrowseName_StandardDataTypes_ReturnsValidNames()
        {
            // Test a few standard data type IDs
            uint[] dataTypeIds =
            [
                DataTypes.Boolean,
                DataTypes.SByte,
                DataTypes.Byte,
                DataTypes.Int16,
                DataTypes.UInt16,
                DataTypes.Int32,
                DataTypes.UInt32,
                DataTypes.Int64,
                DataTypes.UInt64,
                DataTypes.Float,
                DataTypes.Double,
                DataTypes.String,
                DataTypes.DateTime,
                DataTypes.Guid,
                DataTypes.ByteString,
                DataTypes.XmlElement,
                DataTypes.NodeId,
                DataTypes.ExpandedNodeId,
                DataTypes.StatusCode,
                DataTypes.QualifiedName,
                DataTypes.LocalizedText
            ];

            foreach (uint id in dataTypeIds)
            {
                string browseName = DataTypes.GetBrowseName((int)id);
                Assert.IsNotNull(browseName);
                Assert.IsNotEmpty(browseName);
            }
        }

        /// <summary>
        /// Test GetBrowseName for the Boolean data type.
        /// </summary>
        [Test]
        public void GetBrowseName_BooleanDataType_ReturnsBoolean()
        {
            string browseName = DataTypes.GetBrowseName((int)DataTypes.Boolean);
            Assert.AreEqual("Boolean", browseName);
        }

        /// <summary>
        /// Test GetBrowseName for invalid data type ID returns empty string.
        /// </summary>
        [Test]
        public void GetBrowseName_InvalidDataTypeId_ReturnsEmptyString()
        {
            string browseName = DataTypes.GetBrowseName(-9999);
            Assert.AreEqual(string.Empty, browseName);
        }

        /// <summary>
        /// Test GetIdentifier for standard data type names.
        /// </summary>
        [Test]
        public void GetIdentifier_StandardDataTypes_ReturnsValidIds()
        {
            string[] dataTypeNames =
            [
                "Boolean", "SByte", "Byte", "Int16", "UInt16",
                "Int32", "UInt32", "Int64", "UInt64",
                "Float", "Double", "String", "DateTime",
                "Guid", "ByteString", "XmlElement",
                "NodeId", "ExpandedNodeId", "StatusCode",
                "QualifiedName", "LocalizedText"
            ];

            foreach (string name in dataTypeNames)
            {
                uint id = DataTypes.GetIdentifier(name);
                Assert.AreNotEqual(0, id);
            }
        }

        /// <summary>
        /// Test GetIdentifier for the Boolean data type name.
        /// </summary>
        [Test]
        public void GetIdentifier_BooleanName_ReturnsBooleanId()
        {
            uint id = DataTypes.GetIdentifier("Boolean");
            Assert.AreEqual(DataTypes.Boolean, id);
        }

        /// <summary>
        /// Test GetIdentifier for invalid name returns 0.
        /// </summary>
        [Test]
        public void GetIdentifier_InvalidName_ReturnsZero()
        {
            uint id = DataTypes.GetIdentifier("InvalidDataTypeName");
            Assert.AreEqual(0, id);
        }

        /// <summary>
        /// Test that GetBrowseName and GetIdentifier are inverse operations.
        /// </summary>
        [Test]
        public void GetBrowseName_GetIdentifier_AreInverseOperations()
        {
            uint[] dataTypeIds =
            [
                DataTypes.Boolean,
                DataTypes.Int32,
                DataTypes.String,
                DataTypes.DateTime,
                DataTypes.NodeId
            ];

            foreach (uint id in dataTypeIds)
            {
                string browseName = DataTypes.GetBrowseName((int)id);
                uint retrievedId = DataTypes.GetIdentifier(browseName);
                Assert.AreEqual(id, retrievedId);
            }
        }

        /// <summary>
        /// Test GetDataTypeId for EUInformation type returns specific DataTypeId (i=887) not Structure (i=22).
        /// </summary>
        [Test]
        public void GetDataTypeId_EUInformationType_ReturnsSpecificDataTypeId()
        {
            NodeId dataTypeId = TypeInfo.GetDataTypeId(typeof(EUInformation));

            Assert.IsNotNull(dataTypeId);
            Assert.AreEqual(DataTypes.EUInformation, (uint)dataTypeId.Identifier);
            Assert.AreEqual(0, dataTypeId.NamespaceIndex);
            Assert.AreNotEqual(DataTypes.Structure, (uint)dataTypeId.Identifier,
                "Should return specific EUInformation DataTypeId (i=887), not generic Structure (i=22)");
        }

        /// <summary>
        /// Test GetDataTypeId for EUInformation instance returns specific DataTypeId.
        /// </summary>
        [Test]
        public void GetDataTypeId_EUInformationInstance_ReturnsSpecificDataTypeId()
        {
            var euInfo = new EUInformation("unit", "http://test.org");
            NodeId dataTypeId = DataTypes.GetDataTypeId(euInfo);

            Assert.IsNotNull(dataTypeId);
            Assert.AreEqual(DataTypes.EUInformation, (uint)dataTypeId.Identifier);
            Assert.AreEqual(0, dataTypeId.NamespaceIndex);
        }

        /// <summary>
        /// Test GetDataTypeId for various well-known IEncodeable types returns their specific DataTypeIds.
        /// </summary>
        [Test]
        public void GetDataTypeId_WellKnownEncodeableTypes_ReturnsSpecificDataTypeIds()
        {
            // Test various well-known types that implement IEncodeable

            (Type, uint)[] testCases =
            [
                (typeof(EUInformation), DataTypes.EUInformation),
                (typeof(Range), DataTypes.Range),
                (typeof(Argument), DataTypes.Argument),
                (typeof(EnumValueType), DataTypes.EnumValueType),
                (typeof(TimeZoneDataType), DataTypes.TimeZoneDataType)
            ];

            foreach ((Type type, uint expectedId) in testCases)
            {
                NodeId dataTypeId = TypeInfo.GetDataTypeId(type);

                Assert.IsNotNull(dataTypeId, $"DataTypeId should not be null for {type.Name}");
                Assert.AreEqual(expectedId, (uint)dataTypeId.Identifier,
                    $"DataTypeId for {type.Name} should be i={expectedId}, not i={dataTypeId.Identifier}");
                Assert.AreEqual(0, dataTypeId.NamespaceIndex,
                    $"NamespaceIndex should be 0 for {type.Name}");
            }
        }
    }
}
