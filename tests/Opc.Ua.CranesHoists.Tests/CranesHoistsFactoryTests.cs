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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.CranesHoists.Tests
{
    /// <summary>
    /// Exercises generated registration seams, enum metadata and namespace-aware factory activation.
    /// </summary>
    [TestFixture]
    [Category("CranesHoists")]
    public sealed class CranesHoistsFactoryTests
    {
        /// <summary>
        /// Registration contributes exactly the crane ObjectTypes, leaving dependency ownership with the host.
        /// </summary>
        [Test]
        public void GeneratedLoadersRegisterOnlyTheCraneModel()
        {
            var registered = new Dictionary<ExpandedNodeId, INodeStateFactory>();
            var builder = new Mock<INodeStateFactoryBuilder>(MockBehavior.Strict);
            builder.Setup(value => value.RegisterType(It.IsAny<ExpandedNodeId>(), It.IsAny<INodeStateFactory>()))
                .Callback<ExpandedNodeId, INodeStateFactory>((id, factory) => registered.Add(id, factory))
                .Returns(builder.Object);

            Assert.That(builder.Object.AddOpcUaCranesHoists(), Is.SameAs(builder.Object));
            Assert.That(registered.Keys, Is.EquivalentTo(ObjectTypeIds.Identifiers));
            Assert.That(registered, Has.Count.EqualTo(6));
            builder.Verify(value => value.RegisterType(It.IsAny<ExpandedNodeId>(), It.IsAny<INodeStateFactory>()),
                Times.Exactly(6));
            builder.VerifyNoOtherCalls();

            ServiceMessageContext message = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            IEncodeableFactoryBuilder encodeables = message.Factory.Builder;
            Assert.That(encodeables.AddOpcUaCranesHoists(), Is.SameAs(encodeables));
            encodeables.Commit();
            Assert.That(message.Factory.KnownTypeIds, Is.EquivalentTo(DataTypeIds.Identifiers));
            message.Factory.Builder.AddOpcUaCranesHoists().Commit();
            Assert.That(message.Factory.KnownTypeIds, Is.EquivalentTo(DataTypeIds.Identifiers));
            Assert.That(message.Factory.KnownTypeIds.Count(), Is.EqualTo(4));
        }

        /// <summary>
        /// Every registered ObjectType activates its exact generated state and preserves the supplied parent.
        /// </summary>
        [TestCase(1556u, typeof(BrakeState))]
        [TestCase(1369u, typeof(CraneAxisState))]
        [TestCase(1392u, typeof(CraneMotionDeviceState))]
        [TestCase(2112u, typeof(CraneMotionDeviceSystemState))]
        [TestCase(1262u, typeof(CraneMotorState))]
        [TestCase(1462u, typeof(ProtectiveFunctionState))]
        public void NodeFactoryCreatesEveryCraneObjectType(uint identifier, Type expectedType)
        {
            SystemContext context = CranesHoistsTestModel.CreateContext(true);
            var factory = new NodeStateFactory();
            Assert.That(factory.AddOpcUaCranesHoists(), Is.SameAs(factory));
            var parent = new BaseObjectState(null);
            NodeId typeId = CranesHoistsTestModel.Resolve(identifier, context);
            NodeState? state = factory.CreateInstance(context, parent, NodeClass.Object,
                new QualifiedName("CraneInstance"), Opc.Ua.ReferenceTypeIds.HasComponent, typeId);

            Assert.That(state, Is.TypeOf(expectedType));
            var instance = (BaseObjectState)state!;
            Assert.Multiple(() =>
            {
                Assert.That(instance.Parent, Is.SameAs(parent));
                Assert.That(instance.TypeDefinitionId, Is.EqualTo(typeId));
                Assert.That(instance.BrowseName.NamespaceIndex,
                    Is.EqualTo(context.NamespaceUris.GetIndex(CranesHoistsModel.NamespaceUri)));
                Assert.That(instance.FindChildWithQualifiedName(context, new QualifiedName(
                    "ParameterSet", context.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi))), Is.Not.Null);
            });
        }

        /// <summary>
        /// The same numeric identifier in an unrelated namespace must not activate a crane state.
        /// </summary>
        [Test]
        public void NodeFactoryDoesNotConfuseVendorAndCraneIdentifiers()
        {
            SystemContext context = CranesHoistsTestModel.CreateContext();
            var factory = new NodeStateFactory();
            factory.AddOpcUaCranesHoists();
            var vendorType = new NodeId(ObjectTypes.CraneAxisType,
                context.NamespaceUris.GetIndexOrAppend("urn:craneshoists:tests:vendor"));
            NodeState? state = factory.CreateInstance(context, null, NodeClass.Object,
                new QualifiedName("VendorObject"), Opc.Ua.ReferenceTypeIds.HasComponent, vendorType);
            Assert.That(state, Is.TypeOf<BaseObjectState>());
            Assert.That(ExpandedNodeId.ToNodeId(ObjectTypeIds.CraneAxisType, new NamespaceTable()).IsNull, Is.True);
        }

        /// <summary>
        /// All four enum contracts preserve every published name and value, including zero and upper bounds.
        /// </summary>
        [TestCase(3000u, typeof(CraneOperationalModeEnumeration))]
        [TestCase(3001u, typeof(ExternalControlRequestEnumeration))]
        [TestCase(3002u, typeof(ProtectiveFunctionEnumeration))]
        [TestCase(3003u, typeof(CraneMotionDeviceCategoryEnum))]
        public void GeneratedEnumValuesMatchPublishedDefinitions(uint identifier, Type expectedType)
        {
            SystemContext context = CranesHoistsTestModel.CreateContext(true);
            context.EncodeableFactory.Builder.AddOpcUaCranesHoists().Commit();
            var typeId = new ExpandedNodeId(identifier, CranesHoistsModel.NamespaceUri);
            Assert.That(context.EncodeableFactory.TryGetEnumeratedType(typeId, out IEnumeratedType? enumerated),
                Is.True);
            Assert.That(enumerated!.Type, Is.EqualTo(expectedType));
            Assert.That(enumerated, Is.InstanceOf<IDataTypeDefinitionSource>());
            DataTypeDefinition definition = ((IDataTypeDefinitionSource)enumerated)
                .GetDataTypeDefinition(context.NamespaceUris);
            Assert.That(definition, Is.TypeOf<EnumDefinition>());
            var enumDefinition = (EnumDefinition)definition;

            XNamespace schema = CranesHoistsTestModel.Schema;
            XElement declaration = CranesHoistsTestModel.ReadPublishedModel().Root!
                .Elements(schema + "UADataType")
                .Single(node => node.Attribute("NodeId")!.Value == $"ns=4;i={identifier}");
            (string Name, long Value)[] expected = declaration.Element(schema + "Definition")!
                .Elements(schema + "Field")
                .Select(field => (
                    field.Attribute("Name")!.Value,
                    long.Parse(field.Attribute("Value")!.Value, CultureInfo.InvariantCulture)))
                .ToArray();
            Assert.That(enumDefinition.Fields.IsNull, Is.False);
            Assert.That(
                enumDefinition.Fields.ToArray()!.Select(field => (field.Name, field.Value)),
                Is.EqualTo(expected));
            Assert.That(Enum.GetNames(expectedType), Is.EqualTo(expected.Select(field => field.Name)));

            foreach ((string name, long value) in expected)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(enumerated.TryGetSymbol((int)value, out string? symbol), Is.True);
                    Assert.That(symbol, Is.EqualTo(name));
                    Assert.That(enumerated.TryGetValue(name, out int numeric), Is.True);
                    Assert.That(numeric, Is.EqualTo(value));
                });
            }
            Assert.Multiple(() =>
            {
                Assert.That(enumerated.TryGetSymbol(-1, out _), Is.False);
                Assert.That(enumerated.TryGetSymbol((int)expected[expected.Length - 1].Value + 1, out _), Is.False);
                Assert.That(enumerated.TryGetValue("UNDEFINED_CRANE_ENUM_VALUE", out _), Is.False);
            });

            var roots = new NodeStateCollection();
            roots.AddOpcUaCranesHoists(context);
            Dictionary<NodeId, NodeState> nodes = CranesHoistsTestModel.IndexNodes(context, roots);
            var dataType = (DataTypeState)nodes[CranesHoistsTestModel.Resolve(identifier, context)];
            Assert.That(dataType.SuperTypeId, Is.EqualTo(Opc.Ua.DataTypeIds.Enumeration));
            BaseInstanceState? valuesNode = dataType.FindChildWithQualifiedName(
                context, new QualifiedName(Opc.Ua.BrowseNames.EnumValues));
            Assert.That(valuesNode, Is.InstanceOf<BaseVariableState>());
            Assert.That(
                ((BaseVariableState)valuesNode!).Value.TryGetValue(out ArrayOf<ExtensionObject> values),
                Is.True);
            Assert.That(values.Count, Is.EqualTo(expected.Length));
            for (int index = 0; index < values.Count; index++)
            {
                Assert.That(values[index].TryGetValue(out EnumValueType? value), Is.True);
                Assert.That(value!.Value, Is.EqualTo(expected[index].Value));
                Assert.That(value.DisplayName.Text, Is.EqualTo(expected[index].Name));
            }
        }

        /// <summary>
        /// CraneAxisFunction remains an array-valued UInt16 discrete variable with all nine published choices.
        /// </summary>
        [Test]
        public void AxisFunctionPreservesDiscreteValueMetadata()
        {
            SystemContext context = CranesHoistsTestModel.CreateContext();
            var roots = new NodeStateCollection();
            roots.AddOpcUaCranesHoists(context);
            Dictionary<NodeId, NodeState> nodes = CranesHoistsTestModel.IndexNodes(context, roots);
            var function = (BaseVariableState)nodes[CranesHoistsTestModel.Resolve(2416, context)];
            var enumValues = (BaseVariableState)nodes[CranesHoistsTestModel.Resolve(6016, context)];
            Assert.Multiple(() =>
            {
                Assert.That(function.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.UInt16));
                Assert.That(function.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                Assert.That(function.TypeDefinitionId, Is.EqualTo(Opc.Ua.VariableTypeIds.MultiStateValueDiscreteType));
                Assert.That(enumValues.ModellingRuleId, Is.EqualTo(Opc.Ua.ObjectIds.ModellingRule_Mandatory));
                Assert.That(enumValues.BrowseName.NamespaceIndex, Is.Zero);
                Assert.That(nodes[CranesHoistsTestModel.Resolve(6017, context)].BrowseName,
                    Is.EqualTo(new QualifiedName(Opc.Ua.BrowseNames.ValueAsText)));
                Assert.That(DataTypeIds.Identifiers.Count(), Is.EqualTo(4));
            });
            Assert.That(enumValues.Value.TryGetValue(out ArrayOf<ExtensionObject> values), Is.True);
            string[] expectedNames =
            [
                "RATED_SPEED", "EXTENDED_SPEED", "MICROSPEED", "INCHING", "ANTISWAY",
                "TANDEM_HOIST", "TANDEM_TROLLEY", "TANDEM_CRANE", "PRESET_DESTINATION"
            ];
            Assert.That(values.Count, Is.EqualTo(expectedNames.Length));
            for (int index = 0; index < values.Count; index++)
            {
                Assert.That(values[index].TryGetValue(out EnumValueType? value), Is.True);
                Assert.That(value!.Value, Is.EqualTo(index));
                Assert.That(value.DisplayName.Text, Is.EqualTo(expectedNames[index]));
            }
        }
    }
}
