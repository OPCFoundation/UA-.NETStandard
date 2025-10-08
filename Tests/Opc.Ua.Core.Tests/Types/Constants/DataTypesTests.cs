/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
            int[] dataTypeIds = {
                (int)DataTypes.Boolean,
                (int)DataTypes.SByte,
                (int)DataTypes.Byte,
                (int)DataTypes.Int16,
                (int)DataTypes.UInt16,
                (int)DataTypes.Int32,
                (int)DataTypes.UInt32,
                (int)DataTypes.Int64,
                (int)DataTypes.UInt64,
                (int)DataTypes.Float,
                (int)DataTypes.Double,
                (int)DataTypes.String,
                (int)DataTypes.DateTime,
                (int)DataTypes.Guid,
                (int)DataTypes.ByteString,
                (int)DataTypes.XmlElement,
                (int)DataTypes.NodeId,
                (int)DataTypes.ExpandedNodeId,
                (int)DataTypes.StatusCode,
                (int)DataTypes.QualifiedName,
                (int)DataTypes.LocalizedText
            };

            foreach (int id in dataTypeIds)
            {
                string browseName = DataTypes.GetBrowseName(id);
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
            string[] dataTypeNames = {
                "Boolean", "SByte", "Byte", "Int16", "UInt16",
                "Int32", "UInt32", "Int64", "UInt64",
                "Float", "Double", "String", "DateTime",
                "Guid", "ByteString", "XmlElement",
                "NodeId", "ExpandedNodeId", "StatusCode",
                "QualifiedName", "LocalizedText"
            };

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
            Assert.AreEqual((uint)DataTypes.Boolean, id);
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
            int[] dataTypeIds = {
                (int)DataTypes.Boolean, (int)DataTypes.Int32, (int)DataTypes.String,
                (int)DataTypes.DateTime, (int)DataTypes.NodeId
            };

            foreach (int id in dataTypeIds)
            {
                string browseName = DataTypes.GetBrowseName(id);
                uint retrievedId = DataTypes.GetIdentifier(browseName);
                Assert.AreEqual((uint)id, retrievedId);
            }
        }
    }
}
