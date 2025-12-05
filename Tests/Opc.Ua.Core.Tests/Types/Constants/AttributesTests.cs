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

using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Types.Constants
{
    /// <summary>
    /// Tests for the Attributes class helper methods.
    /// </summary>
    [TestFixture]
    [Category("Attributes")]
    [Parallelizable]
    public class AttributesTests
    {
        /// <summary>
        /// Test GetBrowseName for all standard attribute IDs.
        /// </summary>
        [Test]
        public void GetBrowseName_AllStandardAttributes_ReturnsValidNames()
        {
            // Test all standard attribute IDs
            uint[] attributeIds = [
                Attributes.NodeId,
                Attributes.NodeClass,
                Attributes.BrowseName,
                Attributes.DisplayName,
                Attributes.Description,
                Attributes.WriteMask,
                Attributes.UserWriteMask,
                Attributes.IsAbstract,
                Attributes.Symmetric,
                Attributes.InverseName,
                Attributes.ContainsNoLoops,
                Attributes.EventNotifier,
                Attributes.Value,
                Attributes.DataType,
                Attributes.ValueRank,
                Attributes.ArrayDimensions,
                Attributes.AccessLevel,
                Attributes.UserAccessLevel,
                Attributes.MinimumSamplingInterval,
                Attributes.Historizing,
                Attributes.Executable,
                Attributes.UserExecutable,
                Attributes.DataTypeDefinition,
                Attributes.RolePermissions,
                Attributes.UserRolePermissions,
                Attributes.AccessRestrictions,
                Attributes.AccessLevelEx
            ];

            foreach (uint id in attributeIds)
            {
                string browseName = Attributes.GetBrowseName(id);
                Assert.IsNotNull(browseName);
                Assert.IsNotEmpty(browseName);
            }
        }

        /// <summary>
        /// Test GetBrowseName for the Value attribute specifically (from the issue).
        /// </summary>
        [Test]
        public void GetBrowseName_ValueAttribute_ReturnsValue()
        {
            string attributeBrowseName = Attributes.GetBrowseName(Attributes.Value);
            Assert.AreEqual("Value", attributeBrowseName);
        }

        /// <summary>
        /// Test GetBrowseName for invalid attribute ID returns empty string.
        /// </summary>
        [Test]
        public void GetBrowseName_InvalidAttributeId_ReturnsEmptyString()
        {
            string browseName = Attributes.GetBrowseName(9999);
            Assert.AreEqual(string.Empty, browseName);
        }

        /// <summary>
        /// Test GetIdentifier for all standard attribute names.
        /// </summary>
        [Test]
        public void GetIdentifier_AllStandardAttributes_ReturnsValidIds()
        {
            string[] attributeNames = [
                "NodeId", "NodeClass", "BrowseName", "DisplayName", "Description",
                "WriteMask", "UserWriteMask", "IsAbstract", "Symmetric", "InverseName",
                "ContainsNoLoops", "EventNotifier", "Value", "DataType", "ValueRank",
                "ArrayDimensions", "AccessLevel", "UserAccessLevel", "MinimumSamplingInterval",
                "Historizing", "Executable", "UserExecutable", "DataTypeDefinition",
                "RolePermissions", "UserRolePermissions", "AccessRestrictions", "AccessLevelEx"
            ];

            foreach (string name in attributeNames)
            {
                uint id = Attributes.GetIdentifier(name);
                Assert.AreNotEqual(0, id);
            }
        }

        /// <summary>
        /// Test GetIdentifier for the Value attribute name.
        /// </summary>
        [Test]
        public void GetIdentifier_ValueName_ReturnsValueId()
        {
            uint id = Attributes.GetIdentifier("Value");
            Assert.AreEqual(Attributes.Value, id);
        }

        /// <summary>
        /// Test GetIdentifier for invalid name returns 0.
        /// </summary>
        [Test]
        public void GetIdentifier_InvalidName_ReturnsZero()
        {
            uint id = Attributes.GetIdentifier("InvalidAttributeName");
            Assert.AreEqual(0, id);
        }

        /// <summary>
        /// Test that GetBrowseName and GetIdentifier are inverse operations.
        /// </summary>
        [Test]
        public void GetBrowseName_GetIdentifier_AreInverseOperations()
        {
            uint[] attributeIds = [
                Attributes.NodeId, Attributes.Value, Attributes.DisplayName,
                Attributes.Executable, Attributes.AccessLevelEx
            ];

            foreach (uint id in attributeIds)
            {
                string browseName = Attributes.GetBrowseName(id);
                uint retrievedId = Attributes.GetIdentifier(browseName);
                Assert.AreEqual(id, retrievedId);
            }
        }
    }
}
