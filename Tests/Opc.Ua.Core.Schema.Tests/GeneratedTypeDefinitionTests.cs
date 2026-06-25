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
using Opc.Ua.Schema.Json;

// The encodeable type registry API is experimental; the schema factory source
// is built on top of it.
#pragma warning disable UA_NETStandard_1

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Tests that source-generated encodeable types expose their data type
    /// definition through the encodeable factory.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    public class GeneratedTypeDefinitionTests
    {
        [Test]
        public void GeneratedStructureActivatorExposesDefinition()
        {
            DataTypeDefinition? definition =
                ArgumentActivator.Instance.GetDataTypeDefinition(new NamespaceTable());

            Assert.That(definition, Is.InstanceOf<StructureDefinition>());
        }

        [Test]
        public void SchemaIsProducedFromGeneratedDefinition()
        {
            DataTypeDefinition definition =
                ArgumentActivator.Instance.GetDataTypeDefinition(new NamespaceTable())!;
            var typeId = new ExpandedNodeId(DataTypeIds.Argument);
            var registry = new DataTypeDefinitionRegistry();
            registry.Add(new UaTypeDescription(
                typeId,
                new QualifiedName("Argument"),
                definition,
                Namespaces.OpcUa));
            var provider = new DefaultSchemaProvider(registry, [new JsonSchemaGenerator()]);

            bool resolved = provider.TryGetSchema(
                typeId,
                UaSchemaFormat.JsonCompact,
                UaSchemaScope.Type,
                out IUaSchema? schema);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.True);
                Assert.That(schema!.ToSchemaString(), Does.Contain("Argument"));
            });
        }
    }
}
