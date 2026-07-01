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

using System.Xml;
using Moq;
using NUnit.Framework;

// The encodeable type registry API is experimental; the schema factory source
// is built on top of it.
#pragma warning disable UA_NETStandard_1

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Tests for resolving data type definitions from the encodeable factory.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    public class EncodeableFactoryDefinitionSourceTests
    {
        [Test]
        public void TryResolveReturnsDefinitionFromFactoryType()
        {
            StructureDefinition definition = CreateDefinition();
            var typeId = new ExpandedNodeId(new NodeId(4001, 1));
            IEncodeableFactory factory = CreateFactory(typeId, "FactoryType", definition);
            var source = new EncodeableFactoryDefinitionSource(factory, new NamespaceTable());

            bool resolved = source.TryResolve(typeId, out UaTypeDescription? description);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.True);
                Assert.That(description!.Definition, Is.SameAs(definition));
                Assert.That(description.Name, Is.EqualTo("FactoryType"));
            });
        }

        [Test]
        public void TryResolveReturnsFalseForUnknownType()
        {
            StructureDefinition definition = CreateDefinition();
            var knownId = new ExpandedNodeId(new NodeId(4001, 1));
            IEncodeableFactory factory = CreateFactory(knownId, "FactoryType", definition);
            var source = new EncodeableFactoryDefinitionSource(factory, new NamespaceTable());

            bool resolved = source.TryResolve(new ExpandedNodeId(new NodeId(9999, 1)), out UaTypeDescription? description);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.False);
                Assert.That(description, Is.Null);
            });
        }

        [Test]
        public void CompositeResolverFallsThroughToFactorySource()
        {
            StructureDefinition definition = CreateDefinition();
            var typeId = new ExpandedNodeId(new NodeId(4002, 1));
            IEncodeableFactory factory = CreateFactory(typeId, "CompositeType", definition);
            var registry = new DataTypeDefinitionRegistry();
            var composite = new CompositeDataTypeDefinitionResolver(
                [registry, new EncodeableFactoryDefinitionSource(factory, new NamespaceTable())]);

            bool resolved = composite.TryResolve(typeId, out UaTypeDescription? description);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.True);
                Assert.That(description!.Name, Is.EqualTo("CompositeType"));
            });
        }

        private static StructureDefinition CreateDefinition()
        {
            return new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields = new[]
                {
                    SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32))
                }
            };
        }

        private static IEncodeableFactory CreateFactory(
            ExpandedNodeId typeId,
            string name,
            DataTypeDefinition definition)
        {
            var typeMock = new Mock<IEncodeableType>();
            typeMock.SetupGet(t => t.XmlName)
                .Returns(new XmlQualifiedName(name, "http://test.org/factory"));
            typeMock.As<IDataTypeDefinitionSource>()
                .Setup(s => s.GetDataTypeDefinition(It.IsAny<NamespaceTable>()))
                .Returns(definition);

            IEncodeableType? encodeableType = typeMock.Object;
            IEnumeratedType? enumeratedType = null;
            var factoryMock = new Mock<IEncodeableFactory>();
            factoryMock.Setup(f => f.TryGetEncodeableType(typeId, out encodeableType)).Returns(true);
            factoryMock.Setup(f => f.TryGetEnumeratedType(
                It.IsAny<ExpandedNodeId>(), out enumeratedType)).Returns(false);
            return factoryMock.Object;
        }
    }
}
