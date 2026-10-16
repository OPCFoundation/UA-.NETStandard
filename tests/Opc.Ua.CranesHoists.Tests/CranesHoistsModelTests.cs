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
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

namespace Opc.Ua.CranesHoists.Tests
{
    /// <summary>
    /// Verifies generated declarations and instance factories against the released model.
    /// </summary>
    [TestFixture]
    [Category("CranesHoists")]
    public sealed class CranesHoistsModelTests
    {
        /// <summary>
        /// The generated tables preserve every published ObjectType's symbolic and numeric identity.
        /// </summary>
        [TestCase("BrakeType", 1556u)]
        [TestCase("CraneAxisType", 1369u)]
        [TestCase("CraneMotionDeviceType", 1392u)]
        [TestCase("CraneMotionDeviceSystemType", 2112u)]
        [TestCase("CraneMotorType", 1262u)]
        [TestCase("ProtectiveFunctionType", 1462u)]
        public void GeneratedTypeIdsMatchPublishedDeclarations(string name, uint identifier)
        {
            Assert.Multiple(() =>
            {
                Assert.That(Namespaces.CranesHoists, Is.EqualTo(CranesHoistsModel.NamespaceUri));
                Assert.That(ObjectTypes.TryGetValue(name, out uint numeric), Is.True);
                Assert.That(numeric, Is.EqualTo(identifier));
                Assert.That(ObjectTypeIds.TryGetValue(name, out ExpandedNodeId expanded), Is.True);
                Assert.That(expanded, Is.EqualTo(new ExpandedNodeId(identifier, CranesHoistsModel.NamespaceUri)));
                Assert.That(ObjectTypeIds.GetBrowseName(expanded), Is.EqualTo(name));
                Assert.That(ObjectTypes.Identifiers.Count(), Is.EqualTo(6));
            });
        }

        /// <summary>
        /// All published identifiers, classes and qualified browse names survive model loading.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void GeneratedLoaderPreservesEveryPublishedNumericIdentifier(bool reverseNamespaces)
        {
            SystemContext context = CranesHoistsTestModel.CreateContext(reverseNamespaces);
            var roots = new NodeStateCollection();
            Assert.That(roots.AddOpcUaCranesHoists(context), Is.SameAs(roots));
            Dictionary<NodeId, NodeState> nodes = CranesHoistsTestModel.IndexNodes(context, roots);
            XElement[] declarations = CranesHoistsTestModel.ReadPublishedModel().Root!.Elements()
                .Where(element => element.Attribute("NodeId") != null).ToArray();
            NodeId[] expectedIds = declarations.Select(node => CranesHoistsTestModel.Resolve(
                CranesHoistsTestModel.PublishedNumericId(node), context)).ToArray();
            NodeId[] numericIds = nodes.Keys.Where(static id => id.TryGetValue(out uint _)).ToArray();
            Assert.That(numericIds, Is.EquivalentTo(expectedIds));
            Assert.That(numericIds, Has.Length.EqualTo(89));

            foreach (XElement declaration in declarations)
            {
                uint identifier = CranesHoistsTestModel.PublishedNumericId(declaration);
                NodeState node = nodes[CranesHoistsTestModel.Resolve(identifier, context)];
                Assert.Multiple(() =>
                {
                    Assert.That(
                        node.NodeClass.ToString(),
                        Is.EqualTo(declaration.Name.LocalName.Substring(2)),
                        $"Node class of {identifier}");
                    Assert.That(
                        node.BrowseName,
                        Is.EqualTo(PublishedBrowseName(declaration.Attribute("BrowseName")!.Value, context)),
                        $"Browse name of {identifier}");
                });
            }
        }

        /// <summary>
        /// Referenced models load once in their own namespaces even when namespace indices are reversed.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void DependencyModelsLoadWithReorderedNamespaces(bool reverseNamespaces)
        {
            SystemContext context = CranesHoistsTestModel.CreateContext(reverseNamespaces);
            string[] before = context.NamespaceUris.ToArray();
            NodeStateCollection roots = CranesHoistsTestModel.LoadDependencies(context);
            roots.AddOpcUaCranesHoists(context);
            Dictionary<NodeId, NodeState> nodes = CranesHoistsTestModel.IndexNodes(context, roots);

            Assert.Multiple(() =>
            {
                Assert.That(context.NamespaceUris.ToArray().Take(before.Length), Is.EqualTo(before));
                Assert.That(context.NamespaceUris.GetIndex(CranesHoistsModel.NamespaceUri), Is.Not.EqualTo(4));
                Assert.That(
                    context.NamespaceUris.GetIndex("http://opcfoundation.org/UA/Robotics/CranesHoists/"),
                    Is.EqualTo(-1));
                Assert.That(nodes.ContainsKey(ExpandedNodeId.ToNodeId(
                    Di.ObjectTypeIds.ComponentType, context.NamespaceUris)), Is.True);
                Assert.That(nodes.ContainsKey(ExpandedNodeId.ToNodeId(
                    Machinery.ObjectTypeIds.MachineIdentificationType, context.NamespaceUris)), Is.True);
                Assert.That(nodes.ContainsKey(ExpandedNodeId.ToNodeId(
                    Robotics.ObjectTypeIds.MotionDeviceSystemType, context.NamespaceUris)), Is.True);
                Assert.That(nodes.ContainsKey(ExpandedNodeId.ToNodeId(
                    Robotics.ObjectTypeIds.AxisType, context.NamespaceUris)), Is.True);
                Assert.That(
                    context.NamespaceUris.GetIndex(Opc.Ua.RobotIntent.Namespaces.RobotIntent),
                    Is.EqualTo(-1));
            });

            AssertSupertype(nodes, context, ObjectTypeIds.CraneMotionDeviceSystemType,
                Robotics.ObjectTypeIds.MotionDeviceSystemType);
            AssertSupertype(nodes, context, ObjectTypeIds.CraneMotionDeviceType,
                Robotics.ObjectTypeIds.MotionDeviceType);
            AssertSupertype(nodes, context, ObjectTypeIds.CraneAxisType, Robotics.ObjectTypeIds.AxisType);
            AssertSupertype(nodes, context, ObjectTypeIds.CraneMotorType, Robotics.ObjectTypeIds.MotorType);
            AssertSupertype(nodes, context, ObjectTypeIds.BrakeType, Di.ObjectTypeIds.ComponentType);
        }

        /// <summary>
        /// Concrete generated states are assignable to the existing dependency types, not regenerated equivalents.
        /// </summary>
        [Test]
        public void GeneratedStatesReuseDependencyTypes()
        {
            SystemContext context = CranesHoistsTestModel.CreateContext();
            Robotics.MotionDeviceSystemState system = context.CreateInstanceOfCraneMotionDeviceSystemType();
            Robotics.MotionDeviceState device = context.CreateInstanceOfCraneMotionDeviceType();
            Robotics.AxisState axis = context.CreateInstanceOfCraneAxisType();
            Robotics.MotorState motor = context.CreateInstanceOfCraneMotorType();
            Di.ComponentState brake = context.CreateInstanceOfBrakeType();
            BaseObjectState protection = context.CreateInstanceOfProtectiveFunctionType();

            Assert.Multiple(() =>
            {
                Assert.That(system, Is.TypeOf<CraneMotionDeviceSystemState>());
                Assert.That(device, Is.TypeOf<CraneMotionDeviceState>());
                Assert.That(axis, Is.TypeOf<CraneAxisState>());
                Assert.That(motor, Is.TypeOf<CraneMotorState>());
                Assert.That(brake, Is.TypeOf<BrakeState>());
                Assert.That(protection, Is.TypeOf<ProtectiveFunctionState>());
                Assert.That(
                    typeof(CraneMotionDeviceSystemState).BaseType,
                    Is.EqualTo(typeof(Robotics.MotionDeviceSystemState)));
                Assert.That(typeof(CraneMotionDeviceState).BaseType, Is.EqualTo(typeof(Robotics.MotionDeviceState)));
                Assert.That(typeof(CraneAxisState).BaseType, Is.EqualTo(typeof(Robotics.AxisState)));
                Assert.That(typeof(CraneMotorState).BaseType, Is.EqualTo(typeof(Robotics.MotorState)));
                Assert.That(typeof(BrakeState).BaseType, Is.EqualTo(typeof(Di.ComponentState)));
                Assert.That(typeof(ProtectiveFunctionState).BaseType, Is.EqualTo(typeof(BaseObjectState)));
                Assert.That(typeof(CraneAxisState).Assembly, Is.Not.SameAs(typeof(Robotics.AxisState).Assembly));
                Assert.That(typeof(BrakeState).Assembly, Is.Not.SameAs(typeof(Di.ComponentState).Assembly));
            });
        }

        /// <summary>
        /// Required DI, Machinery, Robotics and crane members coexist on the same typed instances.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void GeneratedFactoriesPreserveInheritedMandatoryDeclarations(bool reverseNamespaces)
        {
            SystemContext context = CranesHoistsTestModel.CreateContext(reverseNamespaces);
            CraneMotionDeviceSystemState system = context.CreateInstanceOfCraneMotionDeviceSystemType();
            CraneMotionDeviceState device = context.CreateInstanceOfCraneMotionDeviceType();
            CraneAxisState axis = context.CreateInstanceOfCraneAxisType();
            CraneMotorState motor = context.CreateInstanceOfCraneMotorType();
            BrakeState brake = context.CreateInstanceOfBrakeType();
            ProtectiveFunctionState protection = context.CreateInstanceOfProtectiveFunctionType();

            Assert.Multiple(() =>
            {
                Assert.That(system.TypeDefinitionId, Is.EqualTo(CranesHoistsTestModel.Resolve(2112, context)));
                Assert.That(device.TypeDefinitionId, Is.EqualTo(CranesHoistsTestModel.Resolve(1392, context)));
                Assert.That(axis.TypeDefinitionId, Is.EqualTo(CranesHoistsTestModel.Resolve(1369, context)));
                Assert.That(motor.TypeDefinitionId, Is.EqualTo(CranesHoistsTestModel.Resolve(1262, context)));
                Assert.That(brake.TypeDefinitionId, Is.EqualTo(CranesHoistsTestModel.Resolve(1556, context)));
                Assert.That(protection.TypeDefinitionId, Is.EqualTo(CranesHoistsTestModel.Resolve(1462, context)));
                Assert.That(system.Identification, Is.TypeOf<Machinery.MachineIdentificationState>());
                Assert.That(device.Identification, Is.TypeOf<Machinery.MachineIdentificationState>());
            });

            AssertChild(system, context, Robotics.Namespaces.Robotics, "Controllers");
            AssertChild(system, context, Robotics.Namespaces.Robotics, "MotionDevices");
            AssertChild(system, context, Robotics.Namespaces.Robotics, "SafetyStates");
            AssertChild(system.Identification!, context, Di.Namespaces.OpcUaDi, "Manufacturer");
            AssertChild(system.Identification!, context, Di.Namespaces.OpcUaDi, "SerialNumber");
            AssertChild(system.Identification!, context, Di.Namespaces.OpcUaDi, "ProductInstanceUri");
            AssertChild(device.Identification!, context, Di.Namespaces.OpcUaDi, "Manufacturer");
            AssertChild(device.Identification!, context, Di.Namespaces.OpcUaDi, "SerialNumber");
            AssertChild(device.Identification!, context, Di.Namespaces.OpcUaDi, "ProductInstanceUri");
            AssertChild(device, context, Robotics.Namespaces.Robotics, "Axes");
            AssertChild(device, context, Robotics.Namespaces.Robotics, "PowerTrains");
            AssertChild(device, context, Robotics.Namespaces.Robotics, "MotionDeviceCategory");
            AssertChild(axis, context, Robotics.Namespaces.Robotics, "MotionProfile");

            foreach (BaseObjectState component in new BaseObjectState[] { device, motor })
            {
                AssertChild(component, context, Di.Namespaces.OpcUaDi, "Manufacturer");
                AssertChild(component, context, Di.Namespaces.OpcUaDi, "Model");
                AssertChild(component, context, Di.Namespaces.OpcUaDi, "ProductCode");
                AssertChild(component, context, Di.Namespaces.OpcUaDi, "SerialNumber");
            }

            BaseInstanceState systemParameters = AssertChild(system, context, Di.Namespaces.OpcUaDi, "ParameterSet");
            BaseInstanceState deviceParameters = AssertChild(device, context, Di.Namespaces.OpcUaDi, "ParameterSet");
            AssertChild(device, context, CranesHoistsModel.NamespaceUri, "CraneMotionDeviceCategory");
            BaseInstanceState axisParameters = AssertChild(axis, context, Di.Namespaces.OpcUaDi, "ParameterSet");
            BaseInstanceState motorParameters = AssertChild(motor, context, Di.Namespaces.OpcUaDi, "ParameterSet");
            BaseInstanceState brakeParameters = AssertChild(brake, context, Di.Namespaces.OpcUaDi, "ParameterSet");
            AssertChild(protection, context, Di.Namespaces.OpcUaDi, "ParameterSet");
            Assert.Multiple(() =>
            {
                AssertChild(systemParameters, context, CranesHoistsModel.NamespaceUri, "SystemOn");
                AssertChild(systemParameters, context, CranesHoistsModel.NamespaceUri, "CraneOperationalMode");
                AssertChild(deviceParameters, context, Robotics.Namespaces.Robotics, "SpeedOverride");
                AssertChild(axisParameters, context, Robotics.Namespaces.Robotics, "ActualPosition");
                AssertChild(axisParameters, context, CranesHoistsModel.NamespaceUri, "ActualPositionCapability");
                AssertChild(motorParameters, context, Robotics.Namespaces.Robotics, "MotorTemperature");
                AssertChild(brakeParameters, context, CranesHoistsModel.NamespaceUri, "BrakeReleased");
            });
        }

        /// <summary>
        /// Optional capabilities are declared but not silently instantiated by the mandatory-only factories.
        /// </summary>
        [Test]
        public void GeneratedFactoriesLeaveOptionalCapabilitiesAbsent()
        {
            SystemContext context = CranesHoistsTestModel.CreateContext();
            CraneMotionDeviceSystemState system = context.CreateInstanceOfCraneMotionDeviceSystemType();
            CraneAxisState axis = context.CreateInstanceOfCraneAxisType();
            CraneMotorState motor = context.CreateInstanceOfCraneMotorType();
            BrakeState brake = context.CreateInstanceOfBrakeType();
            ProtectiveFunctionState protection = context.CreateInstanceOfProtectiveFunctionType();

            Assert.Multiple(() =>
            {
                Assert.That(system.ProtectiveFunctions, Is.Null);
                Assert.That(system.Components, Is.Null);
                Assert.That(FindCraneChild(system.ParameterSet!, context, "ExternalControlRequest"), Is.Null);
                Assert.That(FindCraneChild(system.ParameterSet!, context, "SpeedLimitGlobalEnabled"), Is.Null);
                Assert.That(axis.ActualLoad, Is.Null);
                Assert.That(FindCraneChild(axis.ParameterSet!, context, "SpeedLimitDirPlusSetpoint"), Is.Null);
                Assert.That(FindCraneChild(axis.ParameterSet!, context, "SpeedLimitDirMinusSetpoint"), Is.Null);
                Assert.That(FindCraneChild(axis.ParameterSet!, context, "CraneAxisFunction"), Is.Null);
                Assert.That(FindCraneChild(motor.ParameterSet!, context, "Overheated"), Is.Null);
                Assert.That(brake.Identification, Is.Null);
                Assert.That(FindCraneChild(protection.ParameterSet!, context, "Active"), Is.Null);
                Assert.That(FindCraneChild(protection.ParameterSet!, context, "Enabled"), Is.Null);
            });
        }

        /// <summary>
        /// The released ProtectsAxis reference retains its inverse and non-hierarchical semantics.
        /// </summary>
        [Test]
        public void ProtectsAxisRetainsItsReferenceSemantics()
        {
            SystemContext context = CranesHoistsTestModel.CreateContext(true);
            var roots = new NodeStateCollection();
            roots.AddOpcUaCranesHoists(context);
            NodeState node = CranesHoistsTestModel.IndexNodes(context, roots)[
                CranesHoistsTestModel.Resolve(4001, context)];
            Assert.That(node, Is.TypeOf<ReferenceTypeState>());
            var reference = (ReferenceTypeState)node;
            Assert.Multiple(() =>
            {
                Assert.That(ReferenceTypes.ProtectsAxis, Is.EqualTo(4001));
                Assert.That(ReferenceTypeIds.ProtectsAxis,
                    Is.EqualTo(new ExpandedNodeId(4001, CranesHoistsModel.NamespaceUri)));
                Assert.That(reference.SuperTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.NonHierarchicalReferences));
                Assert.That(reference.Symmetric, Is.False);
                Assert.That(reference.IsAbstract, Is.False);
                Assert.That(reference.InverseName.Text, Is.EqualTo("AxisProtectedBy"));
                Assert.That(reference.BrowseName.Name, Is.EqualTo("ProtectsAxis"));
            });
        }

        /// <summary>
        /// Mandatory placeholders remain declarations with their original types, not fabricated physical devices.
        /// </summary>
        [TestCase(5008u, 1392u, "http://opcfoundation.org/UA/CranesHoists/", 11510u)]
        [TestCase(5025u, 1462u, "http://opcfoundation.org/UA/CranesHoists/", 11510u)]
        [TestCase(5001u, 16601u, "http://opcfoundation.org/UA/Robotics/", 11510u)]
        [TestCase(1373u, 1556u, "http://opcfoundation.org/UA/CranesHoists/", 11508u)]
        public void DeclaredPlaceholdersPreserveRequiredTypesAndRules(
            uint identifier,
            uint typeIdentifier,
            string typeNamespace,
            uint modellingRule)
        {
            SystemContext context = CranesHoistsTestModel.CreateContext();
            var roots = new NodeStateCollection();
            roots.AddOpcUaCranesHoists(context);
            var declaration = (BaseInstanceState)CranesHoistsTestModel.IndexNodes(context, roots)[
                CranesHoistsTestModel.Resolve(identifier, context)];
            Assert.Multiple(() =>
            {
                Assert.That(declaration.TypeDefinitionId,
                    Is.EqualTo(NodeId.Create(typeIdentifier, typeNamespace, context.NamespaceUris)));
                Assert.That(declaration.ModellingRuleId, Is.EqualTo(new NodeId(modellingRule)));
            });
        }

        /// <summary>
        /// Speed-limit requests retain writable declarations while reported limits remain read-only.
        /// </summary>
        [TestCase(1485u, (byte)3, 5007u)]
        [TestCase(1486u, (byte)3, 5007u)]
        [TestCase(1950u, (byte)1, 5003u)]
        [TestCase(2031u, (byte)1, 5003u)]
        public void GeneratedLimitDeclarationsDistinguishRequestsFromReportedValues(
            uint identifier,
            byte expectedAccess,
            uint organizingGroup)
        {
            SystemContext context = CranesHoistsTestModel.CreateContext(true);
            var roots = new NodeStateCollection();
            roots.AddOpcUaCranesHoists(context);
            var variable = (BaseVariableState)CranesHoistsTestModel.IndexNodes(context, roots)[
                CranesHoistsTestModel.Resolve(identifier, context)];
            var references = new List<IReference>();
            variable.GetReferences(context, references);
            Assert.Multiple(() =>
            {
                Assert.That(variable.AccessLevel, Is.EqualTo(expectedAccess));
                Assert.That(variable.UserAccessLevel, Is.EqualTo(expectedAccess));
                Assert.That(variable.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Double));
                Assert.That(variable.TypeDefinitionId, Is.EqualTo(new NodeId(17497)));
                Assert.That(variable.ModellingRuleId, Is.EqualTo(Opc.Ua.ObjectIds.ModellingRule_Optional));
                Assert.That(
                    references.Any(reference =>
                        reference.ReferenceTypeId == Opc.Ua.ReferenceTypeIds.Organizes &&
                        reference.IsInverse &&
                        ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris) ==
                            CranesHoistsTestModel.Resolve(organizingGroup, context)),
                    Is.True);
            });
        }

        private static BaseInstanceState AssertChild(
            NodeState parent,
            SystemContext context,
            string namespaceUri,
            string name)
        {
            var browseName = new QualifiedName(name, context.NamespaceUris.GetIndexOrAppend(namespaceUri));
            BaseInstanceState? child = parent.FindChildWithQualifiedName(context, browseName);
            Assert.That(child, Is.Not.Null, $"{parent.BrowseName}/{namespaceUri}:{name}");
            if (child != null)
            {
                Assert.That(child.BrowseName, Is.EqualTo(browseName));
                Assert.That(child.Parent, Is.SameAs(parent));
            }
            return child!;
        }

        private static BaseInstanceState? FindCraneChild(
            NodeState parent,
            SystemContext context,
            string name)
        {
            return parent.FindChildWithQualifiedName(context, new QualifiedName(
                name, context.NamespaceUris.GetIndexOrAppend(CranesHoistsModel.NamespaceUri)));
        }

        private static void AssertSupertype(
            Dictionary<NodeId, NodeState> nodes,
            SystemContext context,
            ExpandedNodeId type,
            ExpandedNodeId baseType)
        {
            NodeState state = nodes[ExpandedNodeId.ToNodeId(type, context.NamespaceUris)];
            Assert.That(state, Is.TypeOf<BaseObjectTypeState>());
            Assert.That(
                ((BaseObjectTypeState)state).SuperTypeId,
                Is.EqualTo(ExpandedNodeId.ToNodeId(baseType, context.NamespaceUris)));
        }

        private static QualifiedName PublishedBrowseName(string value, SystemContext context)
        {
            var browseName = QualifiedName.Parse(value);
            string[] namespaces =
            [
                Opc.Ua.Namespaces.OpcUa,
                Di.Namespaces.OpcUaDi,
                Machinery.Namespaces.Machinery,
                Robotics.Namespaces.Robotics,
                CranesHoistsModel.NamespaceUri
            ];
            return new QualifiedName(browseName.Name,
                context.NamespaceUris.GetIndexOrAppend(namespaces[browseName.NamespaceIndex]));
        }
    }
}
