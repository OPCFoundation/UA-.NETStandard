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
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.MachineTool.WMTP;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Tests generated model identity, mandatory structure and cross-model inheritance.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    public sealed class ModelGenerationTests
    {
        /// <summary>
        /// Both independent wire models are generated into one assembly, never into duplicated dependency types.
        /// </summary>
        [Test]
        public void GeneratedTypesRetainDistinctNamespacesInOneAssembly()
        {
            Assert.Multiple(() =>
            {
                Assert.That(typeof(MachineToolState).Namespace, Is.EqualTo("Opc.Ua.MachineTool"));
                Assert.That(typeof(WirelessMachineToolPeripheralState).Namespace,
                    Is.EqualTo("Opc.Ua.MachineTool.WMTP"));
                Assert.That(typeof(WMTPOutputDataType).Assembly, Is.SameAs(typeof(MachineToolState).Assembly));
                Assert.That(typeof(MachineToolIdentificationState).BaseType,
                    Is.EqualTo(typeof(Machinery.MachineIdentificationState)));
                Assert.That(typeof(WMTPMeasurementState).BaseType,
                    Is.EqualTo(typeof(Machinery.ProcessValues.ProcessValueState)));
                Assert.That(
                    typeof(WirelessMachineToolPeripheralState).IsSubclassOf(typeof(MachineToolState)), Is.False);
                Assert.That(Namespaces.MachineTool, Is.EqualTo(MachineToolModels.MachineToolNamespaceUri));
                Assert.That(WMTP.Namespaces.WMTP, Is.EqualTo(MachineToolModels.WmtpNamespaceUri));
                Assert.That(ObjectTypes.MachineToolType, Is.EqualTo(13));
                Assert.That(WMTP.ObjectTypes.WirelessMachineToolPeripheralType, Is.EqualTo(1003));
                Assert.That(WMTP.ObjectTypes.WMTPMeasurementType, Is.EqualTo(1005));
                Assert.That(WMTP.VariableTypes.WMTPOutputType, Is.EqualTo(2001));
                Assert.That(WMTP.DataTypes.WMTPOutputDataType, Is.EqualTo(3003));
            });
        }

        /// <summary>
        /// Every MachineTool enum is registered with its released numeric values and symbolic names.
        /// </summary>
        [Test]
        public void MachineToolEnumFactoriesPreservePublishedValues()
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext(MachineToolParts.MachineTool);
            XElement root = XDocument.Load(Path.Combine(
                TestContext.CurrentContext.TestDirectory, "Model", "MachineTool",
                "Opc.Ua.MachineTool.NodeSet2.xml")).Root!;
            foreach (XElement dataType in root.Elements(s_ns + "UADataType"))
            {
                uint identifier = uint.Parse(
                    ((string)dataType.Attribute("NodeId")!)[7..], CultureInfo.InvariantCulture);
                Assert.That(context.Factory.TryGetEnumeratedType(
                    new ExpandedNodeId(identifier, Namespaces.MachineTool), out IEnumeratedType? type),
                    Is.True, $"Missing enum {identifier}.");
                foreach (XElement field in dataType.Element(s_ns + "Definition")!.Elements(s_ns + "Field"))
                {
                    int number = int.Parse((string)field.Attribute("Value")!, CultureInfo.InvariantCulture);
                    string name = (string)field.Attribute("Name")!;
                    Assert.That(type!.TryGetSymbol(number, out string? symbol), Is.True);
                    Assert.That(symbol, Is.EqualTo(name));
                    Assert.That(type.TryGetValue(name, out int value), Is.True);
                    Assert.That(value, Is.EqualTo(number));
                }
                Assert.That(type!.TryGetSymbol(-1, out _), Is.False);
                Assert.That(type.TryGetValue("NotAReleasedValue", out _), Is.False);
            }
        }

        /// <summary>
        /// The generated model loader preserves every released NodeId, node class and browse-name namespace.
        /// </summary>
        [TestCase(MachineToolParts.MachineTool, "MachineTool", "Opc.Ua.MachineTool.NodeSet2.xml")]
        [TestCase(MachineToolParts.Wmtp, "WMTP", "Opc.Ua.WMTP.Nodeset2.xml")]
        public void GeneratedLoadersPreserveDeclaredIdentitiesAndBrowseNamespaces(
            MachineToolParts parts,
            string family,
            string file)
        {
            SystemContext context = ModelTestContext.CreateSystemContext(parts);
            var roots = new NodeStateCollection();
            roots.AddMachineToolTypeSystem(context, parts);
            Dictionary<NodeId, NodeState> nodes = ModelTestContext.IndexNodes(context, roots);
            XElement root = XDocument.Load(
                Path.Combine(TestContext.CurrentContext.TestDirectory, "Model", family, file)).Root!;
            string[] namespaceUris =
            [
                Opc.Ua.Namespaces.OpcUa,
                .. root.Element(s_ns + "NamespaceUris")!.Elements().Select(uri => uri.Value)
            ];
            var missing = new List<string>();

            foreach (XElement declaration in root.Elements().Where(node => node.Attribute("NodeId") != null))
            {
                string originalId = (string)declaration.Attribute("NodeId")!;
                var nodeId = NodeId.Create(
                    uint.Parse(originalId[7..], CultureInfo.InvariantCulture),
                    namespaceUris[1],
                    context.NamespaceUris);
                if (!nodes.TryGetValue(nodeId, out NodeState? node))
                {
                    missing.Add($"{originalId} {(string?)declaration.Attribute("BrowseName")}");
                    continue;
                }
                Assert.That(node.NodeClass.ToString(), Is.EqualTo(declaration.Name.LocalName[2..]),
                    $"NodeClass of {originalId}.");
                string browseName = (string)declaration.Attribute("BrowseName")!;
                int colon = browseName.AsSpan().IndexOf(':');
                int namespaceIndex = colon > 0
                    ? int.Parse(browseName[..colon], CultureInfo.InvariantCulture)
                    : 0;
                Assert.That(context.NamespaceUris.GetString(node.BrowseName.NamespaceIndex),
                    Is.EqualTo(namespaceUris[namespaceIndex]), $"BrowseName namespace of {originalId}.");
            }
            Assert.That(missing, Is.Empty, $"Every released {family} declaration must be materialized.");
        }

        /// <summary>
        /// MachineTool retains the mandatory legacy Production graph alongside the newer Jobs dependency.
        /// </summary>
        [Test]
        public void MachineToolFactoryRetainsMandatoryDeprecatedProduction()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.MachineTool);
            MachineToolState machine = context.CreateInstanceOfMachineToolType();
            var production = RequireChild<ProductionState>(
                context, machine, "Production", Namespaces.MachineTool);
            var active = RequireChild<ProductionActiveProgramState>(
                context, production, "ActiveProgram", Namespaces.MachineTool);
            var state = RequireChild<ProductionProgramStateMachineState>(
                context, active, "State", Namespaces.MachineTool);
            var number = RequireChild<BaseVariableState>(context, active, "NumberInList", Opc.Ua.Namespaces.OpcUa);
            RequireChild<BaseVariableState>(context, active, "Name", Namespaces.MachineTool);
            RequireChild<BaseVariableState>(context, state, "CurrentState", Opc.Ua.Namespaces.OpcUa);

            var roots = new NodeStateCollection();
            roots.AddMachineToolTypeSystem(context);
            Dictionary<NodeId, NodeState> nodes = ModelTestContext.IndexNodes(context, roots);
            NodeState productionType = nodes[ExpandedNodeId.ToNodeId(ObjectTypeIds.ProductionType,
                context.NamespaceUris)];
            Assert.Multiple(() =>
            {
                Assert.That(machine.TypeDefinitionId,
                    Is.EqualTo(NodeId.Create(13, Namespaces.MachineTool, context.NamespaceUris)));
                Assert.That(production.TypeDefinitionId,
                    Is.EqualTo(ExpandedNodeId.ToNodeId(ObjectTypeIds.ProductionType, context.NamespaceUris)));
                Assert.That(active.TypeDefinitionId,
                    Is.EqualTo(ExpandedNodeId.ToNodeId(ObjectTypeIds.ProductionActiveProgramType,
                        context.NamespaceUris)));
                Assert.That(number.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.UInt16));
                Assert.That(HasReference(context, productionType, ReferenceTypeIds.IsDeprecated,
                    NodeId.Create(5077, Namespaces.MachineTool, context.NamespaceUris)), Is.True);
                Assert.That(nodes.ContainsKey(ExpandedNodeId.ToNodeId(
                    Machinery.Jobs.ObjectTypeIds.JobManagementType, context.NamespaceUris)), Is.True);
            });
        }

        /// <summary>
        /// MachineTool identification inherits DI-qualified members from the existing Machinery model.
        /// </summary>
        [Test]
        public void MachineToolIdentificationPreservesInheritedDiNamespaces()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.MachineTool);
            MachineToolState machine = context.CreateInstanceOfMachineToolType();
            var identification = RequireChild<MachineToolIdentificationState>(
                context, machine, "Identification", Di.Namespaces.OpcUaDi);
            var manufacturer = RequireChild<BaseVariableState>(
                context, identification, "Manufacturer", Di.Namespaces.OpcUaDi);
            var serial = RequireChild<BaseVariableState>(
                context, identification, "SerialNumber", Di.Namespaces.OpcUaDi);
            Assert.Multiple(() =>
            {
                Assert.That(identification, Is.InstanceOf<Machinery.MachineIdentificationState>());
                Assert.That(identification.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasAddIn));
                Assert.That(manufacturer.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.LocalizedText));
                Assert.That(serial.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.String));
                Assert.That(manufacturer.BrowseName.NamespaceIndex,
                    Is.Not.EqualTo(machine.TypeDefinitionId.NamespaceIndex));
            });
        }

        /// <summary>
        /// A WMTP-only factory creates the required peripheral layout without fabricating a MachineTool.
        /// </summary>
        [Test]
        public void WirelessPeripheralFactoryPreservesMandatoryLayoutAndOptionalAbsence()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.Wmtp);
            WirelessMachineToolPeripheralState peripheral = context.CreateInstanceOfWirelessMachineToolPeripheralType();
            var information = RequireChild<BaseObjectState>(
                context, peripheral, "DeviceInformation", WMTP.Namespaces.WMTP);
            var identification = RequireChild<Machinery.MachineryComponentIdentificationState>(
                context, information, "Identification", Di.Namespaces.OpcUaDi);
            RequireChild<BaseVariableState>(context, identification, "Manufacturer", Di.Namespaces.OpcUaDi);
            RequireChild<BaseVariableState>(context, identification, "SerialNumber", Di.Namespaces.OpcUaDi);
            RequireChild<BaseObjectState>(context, peripheral, "Measurements", WMTP.Namespaces.WMTP);
            var blocks = RequireChild<BaseObjectState>(
                context, peripheral, "MachineryBuildingBlocks", Machinery.Namespaces.Machinery);

            Assert.Multiple(() =>
            {
                Assert.That(peripheral.TypeDefinitionId, Is.EqualTo(NodeId.Create(
                    1003, WMTP.Namespaces.WMTP, context.NamespaceUris)));
                Assert.That(context.NamespaceUris.GetIndex(Namespaces.MachineTool), Is.EqualTo(-1));
                Assert.That(identification.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasAddIn));
                Assert.That(HasReference(context, blocks, ReferenceTypeIds.HasAddIn, identification.NodeId), Is.True);
                foreach (string optional in s_optionalPeripheralMembers)
                {
                    Assert.That(peripheral.FindChild(context, new QualifiedName(
                        optional, context.NamespaceUris.GetIndexOrAppend(WMTP.Namespaces.WMTP))), Is.Null);
                }
            });
        }

        /// <summary>
        /// WMTP measurements remain ProcessValues with a PADIM AnalogSignal and core-qualified unit properties.
        /// </summary>
        [Test]
        public void WmtpMeasurementRetainsProcessValueAndPadimIdentity()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.Wmtp);
            WMTPMeasurementState measurement = context.CreateInstanceOfWMTPMeasurementType();
            var signal = RequireChild<BaseVariableState>(
                context, measurement, "AnalogSignal", PADIM.Namespaces.PADIM);
            var units = RequireChild<BaseVariableState>(
                context, signal, "EngineeringUnits", Opc.Ua.Namespaces.OpcUa);
            RequireChild<BaseVariableState>(context, signal, "EURange", Opc.Ua.Namespaces.OpcUa);
            Assert.Multiple(() =>
            {
                Assert.That(measurement, Is.InstanceOf<Machinery.ProcessValues.ProcessValueState>());
                Assert.That(measurement.TypeDefinitionId, Is.EqualTo(NodeId.Create(
                    1005, WMTP.Namespaces.WMTP, context.NamespaceUris)));
                Assert.That(signal.TypeDefinitionId, Is.EqualTo(ExpandedNodeId.ToNodeId(
                    PADIM.VariableTypeIds.AnalogSignalVariableType, context.NamespaceUris)));
                Assert.That(units.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.EUInformation));
            });
        }

        /// <summary>
        /// Method argument types/ranks, enum metadata and inherited optional method namespaces are retained.
        /// </summary>
        [Test]
        public void WmtpMeasurementPreservesEnumsAndLocalAndInheritedMethodContracts()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.Wmtp);
            WMTPMeasurementState measurement = context.CreateInstanceOfWMTPMeasurementType();
            var trigger = RequireChild<MethodState>(
                context, measurement, "SetTriggerSettings", WMTP.Namespaces.WMTP);
            var delta = RequireChild<MethodState>(
                context, measurement, "SetDeltaCondition", WMTP.Namespaces.WMTP);
            var kind = RequireChild<MultiStateValueDiscreteState<uint>>(
                context, measurement, "TypeOfMeasurement", WMTP.Namespaces.WMTP);
            var sample = RequireChild<MultiStateValueDiscreteState<uint>>(
                context, measurement, "TypeOfSample", WMTP.Namespaces.WMTP);
            Assert.That(kind.EnumValues!.Value.IsNull, Is.False);
            Assert.That(sample.EnumValues!.Value.IsNull, Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(trigger.InputArguments!.Value, Has.Count.EqualTo(1));
                Assert.That(trigger.InputArguments.Value[0].Name, Is.EqualTo("TriggerDuration"));
                Assert.That(trigger.InputArguments.Value[0].DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Duration));
                Assert.That(trigger.InputArguments.Value[0].ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(delta.InputArguments!.Value[0].Name, Is.EqualTo("DeltaValue"));
                Assert.That(delta.InputArguments.Value[0].DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Number));
                Assert.That(kind.EnumValues.Value.ToArray()!.Select(value => value.Value),
                    Is.EqualTo(new long[] { 0, 1, 2, 3, 4, 5, 6, 7 }));
                Assert.That(kind.EnumValues.Value.ToArray()!.Select(value => value.DisplayName.Text),
                    Is.EqualTo(s_measurementNames));
                Assert.That(sample.EnumValues.Value.ToArray()!.Select(value => value.Value),
                    Is.EqualTo(new long[] { 0, 1, 2, 3, 4, 5, 6 }));
            });

            var roots = new NodeStateCollection();
            roots.AddMachineToolTypeSystem(context, MachineToolParts.Wmtp);
            Dictionary<NodeId, NodeState> nodes = ModelTestContext.IndexNodes(context, roots);
            var measurementType = (BaseObjectTypeState)nodes[NodeId.Create(
                1005, WMTP.Namespaces.WMTP, context.NamespaceUris)];
            Assert.That(measurementType.SuperTypeId, Is.EqualTo(ExpandedNodeId.ToNodeId(
                Machinery.ProcessValues.ObjectTypeIds.ProcessValueType, context.NamespaceUris)));
            var zeroPoint = RequireChild<MethodState>(
                context, nodes[measurementType.SuperTypeId], "ZeroPointAdjustment", PADIM.Namespaces.PADIM);
            Assert.That(zeroPoint.ModellingRuleId, Is.EqualTo(new NodeId(Opc.Ua.Objects.ModellingRule_Optional)));
        }

        /// <summary>
        /// The distinct work/service-cycle report ValueRanks are preserved rather than silently repaired.
        /// </summary>
        [Test]
        public void CycleDataFactoriesPreserveReleasedReportSignatures()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.Wmtp);
            WMTPWorkCycleDataState work = context.CreateInstanceOfWMTPWorkCycleDataType();
            WMTPServiceCycleDataState service = context.CreateInstanceOfWMTPServiceCycleDataType();
            var workReport = RequireChild<MethodState>(
                context, work, "CombinedReportAll", WMTP.Namespaces.WMTP);
            var serviceReport = RequireChild<MethodState>(
                context, service, "CombinedReportAll", WMTP.Namespaces.WMTP);
            RequireChild<MethodState>(context, work, "AbortOperation", WMTP.Namespaces.WMTP);
            RequireChild<MethodState>(context, service, "AbortOperation", WMTP.Namespaces.WMTP);
            var count = RequireChild<MethodState>(
                context, work, "ReportNumberOfStoredRecords", WMTP.Namespaces.WMTP);
            Assert.Multiple(() =>
            {
                Assert.That(workReport.OutputArguments!.Value[0].DataType, Is.EqualTo(NodeId.Create(
                    3003, WMTP.Namespaces.WMTP, context.NamespaceUris)));
                Assert.That(workReport.OutputArguments.Value[0].ValueRank, Is.EqualTo(ValueRanks.Any));
                Assert.That(serviceReport.OutputArguments!.Value[0].DataType,
                    Is.EqualTo(workReport.OutputArguments.Value[0].DataType));
                Assert.That(serviceReport.OutputArguments.Value[0].ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                Assert.That(count.OutputArguments!.Value[0].DataType, Is.EqualTo(Opc.Ua.DataTypeIds.UInt32));
            });
        }

        /// <summary>
        /// Device configuration keeps the explicit time-zone structure and the calibration-mode wire type.
        /// </summary>
        [Test]
        public void PeripheralConfigurationPreservesTimeZoneAndCalibrationContracts()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.Wmtp);
            var roots = new NodeStateCollection();
            roots.AddMachineToolTypeSystem(context, MachineToolParts.Wmtp);
            Dictionary<NodeId, NodeState> nodes = ModelTestContext.IndexNodes(context, roots);
            NodeState configuration = nodes[NodeId.Create(5001, WMTP.Namespaces.WMTP, context.NamespaceUris)];
            var time = RequireChild<SetDeviceTimeMethodState>(
                context, configuration, "SetDeviceTime", WMTP.Namespaces.WMTP);
            var calibration = RequireChild<SwitchCalibrationModeMethodState>(
                context, configuration, "SwitchCalibrationMode", WMTP.Namespaces.WMTP);
            Assert.Multiple(() =>
            {
                Assert.That(time.InputArguments!.Value, Has.Count.EqualTo(2));
                Assert.That(time.InputArguments.Value[0].Name, Is.EqualTo("DateTime"));
                Assert.That(time.InputArguments.Value[0].DataType, Is.EqualTo(Opc.Ua.DataTypeIds.DateTime));
                Assert.That(time.InputArguments.Value[1].Name, Is.EqualTo("TimeZoneOffset"));
                Assert.That(time.InputArguments.Value[1].DataType, Is.EqualTo(Opc.Ua.DataTypeIds.TimeZoneDataType));
                Assert.That(time.InputArguments.Value[1].ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(calibration.InputArguments!.Value, Has.Count.EqualTo(1));
                Assert.That(calibration.InputArguments.Value[0].Name, Is.EqualTo("TargetMode"));
                Assert.That(calibration.InputArguments.Value[0].DataType, Is.EqualTo(Opc.Ua.DataTypeIds.UInt16));
            });
        }

        /// <summary>
        /// Firmware and licence transfer declarations retain their published Byte-array signatures.
        /// </summary>
        [TestCase(7002u, "FirmwareData")]
        [TestCase(7003u, "LicenseData")]
        public void PeripheralTransferContractsPreserveByteArrayArguments(uint identifier, string argumentName)
        {
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.Wmtp);
            var roots = new NodeStateCollection();
            roots.AddMachineToolTypeSystem(context, MachineToolParts.Wmtp);
            NodeState node = ModelTestContext.IndexNodes(context, roots)[NodeId.Create(
                identifier, WMTP.Namespaces.WMTP, context.NamespaceUris)];
            Assert.That(node, Is.InstanceOf<MethodState>());
            var method = (MethodState)node;
            Assert.Multiple(() =>
            {
                Assert.That(method.InputArguments!.Value, Has.Count.EqualTo(1));
                Assert.That(method.InputArguments.Value[0].Name, Is.EqualTo(argumentName));
                Assert.That(method.InputArguments.Value[0].DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Byte));
                Assert.That(method.InputArguments.Value[0].ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            });
        }

        /// <summary>
        /// The output VariableType retains its structured value and mandatory field-component types.
        /// </summary>
        [Test]
        public void WmtpOutputVariableFactoryMaterializesTypedFields()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.Wmtp);
            WMTPOutputState output = context.CreateInstanceOfWMTPOutputType();
            var units = RequireChild<BaseVariableState>(
                context, output, "EngineeringUnits", WMTP.Namespaces.WMTP);
            var timestamp = RequireChild<BaseVariableState>(
                context, output, "Timestamp", WMTP.Namespaces.WMTP);
            var value = RequireChild<BaseVariableState>(context, output, "ActualValue", WMTP.Namespaces.WMTP);
            Assert.Multiple(() =>
            {
                Assert.That(output.TypeDefinitionId, Is.EqualTo(NodeId.Create(
                    2001, WMTP.Namespaces.WMTP, context.NamespaceUris)));
                Assert.That(output.DataType, Is.EqualTo(NodeId.Create(
                    3003, WMTP.Namespaces.WMTP, context.NamespaceUris)));
                Assert.That(output.ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(units.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.EUInformation));
                Assert.That(timestamp.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.DateTime));
                Assert.That(value.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Double));
            });
        }

        /// <summary>
        /// High/high-high duration counters are the same declarations referenced from Status and LimitCounters.
        /// </summary>
        [Test]
        public void WmtpLimitCounterReferencesRetainTheirDeclaredDirectionAndTargets()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.Wmtp);
            var roots = new NodeStateCollection();
            roots.AddMachineToolTypeSystem(context, MachineToolParts.Wmtp);
            Dictionary<NodeId, NodeState> nodes = ModelTestContext.IndexNodes(context, roots);
            NodeState limits = nodes[NodeId.Create(5019, WMTP.Namespaces.WMTP, context.NamespaceUris)];
            NodeState status = nodes[NodeId.Create(6220, WMTP.Namespaces.WMTP, context.NamespaceUris)];
            Assert.That(context.NamespaceUris.GetString(status.BrowseName.NamespaceIndex),
                Is.EqualTo(Machinery.ProcessValues.Namespaces.MachineryProcessValues));
            foreach (uint identifier in new uint[] { 6223, 6224 })
            {
                NodeState counter = nodes[NodeId.Create(identifier, WMTP.Namespaces.WMTP, context.NamespaceUris)];
                Assert.That(counter, Is.InstanceOf<BaseVariableState>());
                Assert.That(((BaseVariableState)counter).DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Duration));
                Assert.That(HasReference(context, limits, ReferenceTypeIds.HasProperty, counter.NodeId), Is.True);
                Assert.That(((BaseVariableState)counter).Parent, Is.SameAs(status));
            }
        }

        private static T RequireChild<T>(
            SystemContext context,
            NodeState parent,
            string browseName,
            string namespaceUri)
            where T : BaseInstanceState
        {
            var name = new QualifiedName(browseName, context.NamespaceUris.GetIndexOrAppend(namespaceUri));
            BaseInstanceState? child = parent.FindChild(context, name);
            Assert.That(child, Is.InstanceOf<T>(), $"{parent.BrowseName}/{name}");
            Assert.That(child!.BrowseName, Is.EqualTo(name));
            return (T)child;
        }

        private static bool HasReference(SystemContext context, NodeState source, NodeId type, NodeId target)
        {
            var references = new List<IReference>();
            source.GetReferences(context, references, type, false);
            return references.Any(reference => ExpandedNodeId.ToNodeId(
                reference.TargetId, context.NamespaceUris) == target);
        }

        private static readonly XNamespace s_ns = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
        private static readonly string[] s_optionalPeripheralMembers =
            ["DeviceConfiguration", "WMTPWorkCycleData", "WMTPServiceCycleData"];
        private static readonly string[] s_measurementNames =
            ["temperature", "rotation speed", "humidity", "acceleration", "force", "torque", "position", "dimension"];
    }
}
