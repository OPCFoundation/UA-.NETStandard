/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.Text.Json;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Encoders;
using Opc.Ua.XRegistry.Bridge.Native;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    [TestFixture]
    public sealed class XRegistryNativeStructureDiscriminatorTests
    {
        [Test]
        public void ActualNativeDiscriminatorOverridesDefaultBeforeConditionalFieldValidation()
        {
            var structure = new Structure(new XmlQualifiedName("Sensor", "urn:fixture:default-discriminator"),
                new ExpandedNodeId("Sensor", "urn:fixture:default-discriminator"),
                new ExpandedNodeId("Sensor.Binary", "urn:fixture:default-discriminator"),
                new ExpandedNodeId("Sensor.Xml", "urn:fixture:default-discriminator"),
                new StructureDefinition
                {
                    StructureType = StructureType.Structure,
                    Fields =
                    [
                        new StructureField
                        {
                            Name = "reading", DataType = Ua.DataTypeIds.Double, ValueRank = ValueRanks.Scalar
                        },
                        new StructureField
                        {
                            Name = "kind", DataType = Ua.DataTypeIds.String, ValueRank = ValueRanks.Scalar
                        }
                    ]
                }, new Dictionary<string, BuiltInType>
                {
                    ["reading"] = BuiltInType.Double,
                    ["kind"] = BuiltInType.String
                });
            structure["reading"] = Variant.From(0.125);
            structure["kind"] = Variant.From("sensor");
            var mapping = new XRegistryNativeAttributeMapping("/groups", XRegistryNativeAttributeScope.Group,
                ["payload"], [new("urn:fixture:default-discriminator", "Sensor")])
            {
                StructureType = structure,
                StructureTypeId = structure.TypeId
            };
            using JsonDocument definition = JsonDocument.Parse("""
                {"type":"object","attributes":{"kind":{"type":"string","required":true,"default":"text","ifvalues":{
                "text":{"siblingattributes":{"reading":{"type":"string"}}},
                "sensor":{"siblingattributes":{"reading":{"type":"decimal"}}}}}}}
                """);
            JsonElement decoded = XRegistryNativeAttributeCodec.Decode(
                Variant.From(new ExtensionObject(structure)), definition.RootElement, mapping);
            Assert.Multiple(() =>
            {
                Assert.That(decoded.GetProperty("kind").GetString(), Is.EqualTo("sensor"));
                Assert.That(decoded.GetProperty("reading").GetDouble(), Is.EqualTo(0.125));
            });
        }
    }
}
