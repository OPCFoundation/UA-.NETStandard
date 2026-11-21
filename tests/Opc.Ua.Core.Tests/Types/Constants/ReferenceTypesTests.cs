// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;

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
                Assert.That(browseName, Is.Not.Null);
                Assert.That(browseName, Is.Not.Empty);
            }
        }

        /// <summary>
        /// Test GetBrowseName for the References reference type.
        /// </summary>
        [Test]
        public void GetBrowseName_ReferencesReferenceType_ReturnsReferences()
        {
            string browseName = ReferenceTypes.GetBrowseName(ReferenceTypes.References);
            Assert.That(browseName, Is.EqualTo("References"));
        }

        /// <summary>
        /// Test GetBrowseName for invalid reference type ID returns empty string.
        /// </summary>
        [Test]
        public void GetBrowseName_InvalidReferenceTypeId_ReturnsEmptyString()
        {
            string browseName = ReferenceTypes.GetBrowseName(99999);
            Assert.That(browseName, Is.Empty);
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
                Assert.That(id, Is.Not.Zero);
            }
        }

        /// <summary>
        /// Test GetIdentifier for the References reference type name.
        /// </summary>
        [Test]
        public void GetIdentifier_ReferencesName_ReturnsReferencesId()
        {
            uint id = ReferenceTypes.GetIdentifier("References");
            Assert.That(id, Is.EqualTo(ReferenceTypes.References));
        }

        /// <summary>
        /// Test GetIdentifier for invalid name returns 0.
        /// </summary>
        [Test]
        public void GetIdentifier_InvalidName_ReturnsZero()
        {
            uint id = ReferenceTypes.GetIdentifier("InvalidReferenceTypeName");
            Assert.That(id, Is.Zero);
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
                Assert.That(retrievedId, Is.EqualTo(id));
            }
        }
    }
}
