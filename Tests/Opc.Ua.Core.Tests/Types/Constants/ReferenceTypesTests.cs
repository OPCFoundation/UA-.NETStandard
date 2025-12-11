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
    /// Tests for the ReferenceTypes class helper methods.
    /// </summary>
    [TestFixture]
    [Category("ReferenceTypes")]
    [Parallelizable]
    public class ReferenceTypesTests
    {
        /// <summary>
        /// Test GetBrowseName for standard reference types.
        /// </summary>
        [Test]
        public void GetBrowseName_StandardReferenceTypes_ReturnsValidNames()
        {
            // Test a few standard reference type IDs
            uint[] referenceTypeIds = [
                ReferenceTypes.References,
                ReferenceTypes.HierarchicalReferences,
                ReferenceTypes.NonHierarchicalReferences,
                ReferenceTypes.HasChild,
                ReferenceTypes.Organizes,
                ReferenceTypes.HasEventSource,
                ReferenceTypes.HasModellingRule,
                ReferenceTypes.HasEncoding,
                ReferenceTypes.HasDescription,
                ReferenceTypes.HasTypeDefinition,
                ReferenceTypes.GeneratesEvent,
                ReferenceTypes.Aggregates,
                ReferenceTypes.HasSubtype,
                ReferenceTypes.HasProperty,
                ReferenceTypes.HasComponent
            ];

            foreach (uint id in referenceTypeIds)
            {
                string browseName = ReferenceTypes.GetBrowseName(id);
                Assert.IsNotNull(browseName);
                Assert.IsNotEmpty(browseName);
            }
        }

        /// <summary>
        /// Test GetBrowseName for the References reference type.
        /// </summary>
        [Test]
        public void GetBrowseName_ReferencesReferenceType_ReturnsReferences()
        {
            string browseName = ReferenceTypes.GetBrowseName(ReferenceTypes.References);
            Assert.AreEqual("References", browseName);
        }

        /// <summary>
        /// Test GetBrowseName for invalid reference type ID returns empty string.
        /// </summary>
        [Test]
        public void GetBrowseName_InvalidReferenceTypeId_ReturnsEmptyString()
        {
            string browseName = ReferenceTypes.GetBrowseName(99999);
            Assert.AreEqual(string.Empty, browseName);
        }

        /// <summary>
        /// Test GetIdentifier for standard reference type names.
        /// </summary>
        [Test]
        public void GetIdentifier_StandardReferenceTypes_ReturnsValidIds()
        {
            string[] referenceTypeNames = [
                "References", "HierarchicalReferences", "NonHierarchicalReferences",
                "HasChild", "Organizes", "HasEventSource", "HasModellingRule",
                "HasEncoding", "HasDescription", "HasTypeDefinition",
                "GeneratesEvent", "Aggregates", "HasSubtype",
                "HasProperty", "HasComponent"
            ];

            foreach (string name in referenceTypeNames)
            {
                uint id = ReferenceTypes.GetIdentifier(name);
                Assert.AreNotEqual(0, id);
            }
        }

        /// <summary>
        /// Test GetIdentifier for the References reference type name.
        /// </summary>
        [Test]
        public void GetIdentifier_ReferencesName_ReturnsReferencesId()
        {
            uint id = ReferenceTypes.GetIdentifier("References");
            Assert.AreEqual(ReferenceTypes.References, id);
        }

        /// <summary>
        /// Test GetIdentifier for invalid name returns 0.
        /// </summary>
        [Test]
        public void GetIdentifier_InvalidName_ReturnsZero()
        {
            uint id = ReferenceTypes.GetIdentifier("InvalidReferenceTypeName");
            Assert.AreEqual(0, id);
        }

        /// <summary>
        /// Test that GetBrowseName and GetIdentifier are inverse operations.
        /// </summary>
        [Test]
        public void GetBrowseName_GetIdentifier_AreInverseOperations()
        {
            uint[] referenceTypeIds = [
                ReferenceTypes.References, ReferenceTypes.HasChild,
                ReferenceTypes.HasTypeDefinition, ReferenceTypes.HasProperty,
                ReferenceTypes.Organizes
            ];

            foreach (uint id in referenceTypeIds)
            {
                string browseName = ReferenceTypes.GetBrowseName(id);
                uint retrievedId = ReferenceTypes.GetIdentifier(browseName);
                Assert.AreEqual(id, retrievedId);
            }
        }
    }
}
