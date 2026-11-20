/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Tests that attribute-driven (source-annotated) generated encodeable
    /// types always expose a non-null data type definition through their
    /// generated activator, per the IDataTypeDefinitionSource contract.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    [Parallelizable]
    public class GeneratedDataTypeDefinitionTests
    {
        [Test]
        public void StructureActivatorExposesNonNullStructureDefinition()
        {
            DataTypeDefinition definition =
                BrowserOptionsActivator.Instance.GetDataTypeDefinition(new NamespaceTable());

            Assert.That(definition, Is.Not.Null);
            Assert.That(definition, Is.InstanceOf<StructureDefinition>());

            var structure = (StructureDefinition)definition;
            Assert.That(structure.Fields, Has.Count.GreaterThan(0));
        }

        [Test]
        public void SubscriptionOptionsActivatorExposesNonNullDefinition()
        {
            DataTypeDefinition definition =
                SubscriptionOptionsActivator.Instance.GetDataTypeDefinition(new NamespaceTable());

            Assert.That(definition, Is.Not.Null);
            Assert.That(definition, Is.InstanceOf<StructureDefinition>());
        }
    }
}
