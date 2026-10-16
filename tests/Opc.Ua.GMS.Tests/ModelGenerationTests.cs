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
using Opc.Ua.Machinery.Result;
using Opc.Ua.MachineTool;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Exercises generated factories and inherited mandatory/default-optional structures at public model seams.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    public sealed class ModelGenerationTests
    {
        /// <summary>
        /// GMS uses the existing dependency types rather than regenerating their declarations in its assembly.
        /// </summary>
        [Test]
        public void GeneratedInheritanceUsesDependencyAssemblies()
        {
            Assert.Multiple(() =>
            {
                Assert.That(typeof(GMSState).BaseType, Is.EqualTo(typeof(MachineToolState)));
                Assert.That(typeof(GMSIdentificationState).BaseType,
                    Is.EqualTo(typeof(MachineToolIdentificationState)));
                Assert.That(typeof(GMSEquipmentState).BaseType, Is.EqualTo(typeof(EquipmentState)));
                Assert.That(typeof(GMSMonitoringState).BaseType, Is.EqualTo(typeof(MonitoringState)));
                Assert.That(typeof(GMSJobState).BaseType, Is.EqualTo(typeof(ProductionJobState)));
                Assert.That(typeof(GMSPartState).BaseType, Is.EqualTo(typeof(ProductionPartState)));
                Assert.That(typeof(SensorState).BaseType, Is.EqualTo(typeof(ToolState)));
                Assert.That(typeof(MultiSensorState).BaseType, Is.EqualTo(typeof(MultiToolState)));
                Assert.That(typeof(GMSResultManagementState).BaseType, Is.EqualTo(typeof(ResultManagementState)));
                Assert.That(typeof(GMSResultState).BaseType, Is.EqualTo(typeof(ResultState)));
                Assert.That(typeof(IntermediateResultEventState).BaseType, Is.EqualTo(typeof(ResultReadyEventState)));
                Assert.That(typeof(GMSState).Assembly, Is.Not.SameAs(typeof(MachineToolState).Assembly));
                Assert.That(typeof(GMSResultState).Assembly, Is.Not.SameAs(typeof(ResultState).Assembly));
                Assert.That(Namespaces.GMS, Is.EqualTo("http://opcfoundation.org/UA/GMS/"));
                Assert.That(GmsModel.NamespaceUri, Is.EqualTo(Namespaces.GMS));
                Assert.That(GmsModel.Version, Is.EqualTo("1.0.0"));
                Assert.That(GmsModel.ReferenceEdition, Is.EqualTo("1.0.1"));
                Assert.That(GmsModel.SourceCommit, Is.EqualTo("96a08945425d0295edb3607d2a38f6841ef44046"));
                Assert.That(GmsModel.NormalizedNodeSetSha256,
                    Is.EqualTo("9E510C01A2671B732E5EB27561B06A5BC040BB219412884D0A3AFF3C55B88EE0"));
            });
        }

        /// <summary>
        /// Every published object type has a typed, reflection-free factory at its canonical numeric identity.
        /// </summary>
        [TestCase(1001u, typeof(IntermediateResultEventState))]
        [TestCase(1002u, typeof(GMSState))]
        [TestCase(1003u, typeof(GMSEquipmentState))]
        [TestCase(1004u, typeof(GMSMonitoringState))]
        [TestCase(1005u, typeof(CorrectionState))]
        [TestCase(1006u, typeof(ToolMonitoringState))]
        [TestCase(1007u, typeof(SensorState))]
        [TestCase(1008u, typeof(GMSResultManagementState))]
        [TestCase(1009u, typeof(CharacteristicState))]
        [TestCase(1010u, typeof(SensorWarningAlarmState))]
        [TestCase(1011u, typeof(GMSIdentificationState))]
        [TestCase(1012u, typeof(MultiSensorState))]
        [TestCase(1014u, typeof(LoadingMonitoringState))]
        [TestCase(1015u, typeof(SensorExchangeRackState))]
        [TestCase(1016u, typeof(GMSJobState))]
        [TestCase(1017u, typeof(GMSPartState))]
        [TestCase(1018u, typeof(RotaryTableState))]
        [TestCase(1019u, typeof(CalibrationPrognosisState))]
        [TestCase(1022u, typeof(TipExchangeRackState))]
        public void ObjectFactoriesRetainCanonicalTypes(uint identifier, Type expectedType)
        {
            SystemContext context = ModelTestContext.CreateSystemContext(reverseNamespaces: true);
            var factory = new NodeStateFactory();
            factory.AddGmsTypeSystem();
            var typeId = NodeId.Create(identifier, GmsModel.NamespaceUri, context.NamespaceUris);
            var browseName = new QualifiedName("GmsComponent", 1);
            NodeState? created = factory.CreateInstance(
                context, null, NodeClass.Object, browseName, ReferenceTypeIds.HasComponent, typeId);
            Assert.That(created, Is.TypeOf(expectedType));
            Assert.That(((BaseObjectState)created!).TypeDefinitionId, Is.EqualTo(typeId));
        }

        /// <summary>
        /// The three GMS variable types retain their typed factory and underlying wire data types.
        /// </summary>
        [TestCase(2002u, typeof(AdditionalSensorState), 26u, "http://opcfoundation.org/UA/")]
        [TestCase(2003u, typeof(CatalogState), 26u, "http://opcfoundation.org/UA/")]
        [TestCase(2004u, typeof(GMSResultState), 3008u, "http://opcfoundation.org/UA/Machinery/Result/")]
        public void VariableFactoriesRetainCanonicalTypes(
            uint identifier,
            Type expectedType,
            uint dataTypeId,
            string dataTypeNamespace)
        {
            SystemContext context = ModelTestContext.CreateSystemContext(reverseNamespaces: true);
            var factory = new NodeStateFactory();
            factory.AddGmsTypeSystem();
            var typeId = NodeId.Create(identifier, GmsModel.NamespaceUri, context.NamespaceUris);
            NodeState? created = factory.CreateInstance(
                context, null, NodeClass.Variable, new QualifiedName("Value", 1),
                ReferenceTypeIds.HasComponent, typeId);
            Assert.That(created, Is.TypeOf(expectedType));
            var variable = (BaseVariableState)created!;
            Assert.Multiple(() =>
            {
                Assert.That(variable.TypeDefinitionId, Is.EqualTo(typeId));
                Assert.That(variable.DataType,
                    Is.EqualTo(NodeId.Create(dataTypeId, dataTypeNamespace, context.NamespaceUris)));
            });
        }

        /// <summary>
        /// The default factory retains required inherited production, monitoring, identification and result structure.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void GmsFactoryRetainsMandatoryInheritedStructure(bool reverseNamespaces)
        {
            SystemContext context = ModelTestContext.CreateSystemContext(reverseNamespaces: reverseNamespaces);
            GMSState machine = context.CreateInstanceOfGMSType();
            var identification = ModelTestContext.RequireChild<GMSIdentificationState>(
                context, machine, "Identification", Di.Namespaces.OpcUaDi);
            var equipment = ModelTestContext.RequireChild<GMSEquipmentState>(
                context, machine, "Equipment", MachineTool.Namespaces.MachineTool);
            var monitoring = ModelTestContext.RequireChild<GMSMonitoringState>(
                context, machine, "Monitoring", MachineTool.Namespaces.MachineTool);
            var operation = ModelTestContext.RequireChild<MachineOperationMonitoringState>(
                context, monitoring, "MachineTool", MachineTool.Namespaces.MachineTool);
            var mode = ModelTestContext.RequireChild<BaseVariableState>(
                context, operation, "OperationMode", MachineTool.Namespaces.MachineTool);
            var production = ModelTestContext.RequireChild<ProductionState>(
                context, machine, "Production", MachineTool.Namespaces.MachineTool);
            var active = ModelTestContext.RequireChild<ProductionActiveProgramState>(
                context, production, "ActiveProgram", MachineTool.Namespaces.MachineTool);
            var state = ModelTestContext.RequireChild<ProductionProgramStateMachineState>(
                context, active, "State", MachineTool.Namespaces.MachineTool);
            var current = ModelTestContext.RequireChild<FiniteStateVariableState>(
                context, state, "CurrentState", Opc.Ua.Namespaces.OpcUa);
            var result = ModelTestContext.RequireChild<GMSResultManagementState>(
                context, machine, "ResultManagement", GmsModel.NamespaceUri);
            ModelTestContext.RequireChild<NotificationState>(
                context, machine, "Notification", MachineTool.Namespaces.MachineTool);
            ModelTestContext.RequireChild<PropertyState<LocalizedText>>(
                context, identification, "Manufacturer", Di.Namespaces.OpcUaDi);
            ModelTestContext.RequireChild<PropertyState<string>>(
                context, identification, "ProductInstanceUri", Di.Namespaces.OpcUaDi);
            ModelTestContext.RequireChild<PropertyState<string>>(
                context, identification, "SerialNumber", Di.Namespaces.OpcUaDi);
            ModelTestContext.RequireChild<BaseVariableState>(
                context, active, "Name", MachineTool.Namespaces.MachineTool);
            ModelTestContext.RequireChild<PropertyState<ushort>>(
                context, active, "NumberInList", Opc.Ua.Namespaces.OpcUa);
            ModelTestContext.RequireChild<PropertyState<NodeId>>(context, current, "Id", Opc.Ua.Namespaces.OpcUa);
            ModelTestContext.RequireChild<PropertyState<uint>>(context, current, "Number", Opc.Ua.Namespaces.OpcUa);

            Assert.Multiple(() =>
            {
                Assert.That(machine.TypeDefinitionId,
                    Is.EqualTo(NodeId.Create(1002, GmsModel.NamespaceUri, context.NamespaceUris)));
                Assert.That(identification.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasAddIn));
                Assert.That(mode.DataType, Is.EqualTo(ExpandedNodeId.ToNodeId(
                    MachineTool.DataTypeIds.MachineOperationMode, context.NamespaceUris)));
                Assert.That(result.TypeDefinitionId,
                    Is.EqualTo(NodeId.Create(1008, GmsModel.NamespaceUri, context.NamespaceUris)));
                Assert.That(result.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
                Assert.That(result.ResultTransfer, Is.Null);
                Assert.That(result.GetLatestResult, Is.Null);
                Assert.That(result.CorrectionsFolder, Is.Null);
                Assert.That(identification.Workspace, Is.Null);
                Assert.That(equipment.Tools, Is.Null);
                Assert.That(equipment.Accessories, Is.Null);
                Assert.That(monitoring.LoadingMonitoring, Is.Null);
                Assert.That(monitoring.ToolMonitoring, Is.Null);
            });
        }

        /// <summary>
        /// Sensor factories specialize real tools and preserve mandatory tool-life and classification structures.
        /// </summary>
        [Test]
        public void SensorFactoryPreservesToolInheritanceAndOptionalQualification()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(reverseNamespaces: true);
            SensorState sensor = context.CreateInstanceOfSensorType();
            var life = ModelTestContext.RequireChild<BaseObjectState>(
                context, sensor, "ToolLife", MachineTool.Namespaces.MachineTool);
            var classification = ModelTestContext.RequireChild<BaseVariableState>(
                context, sensor, "Class", GmsModel.NamespaceUri);
            ModelTestContext.RequireChild<BaseDataVariableState<uint>>(
                context, sensor, "ControlIdentifier1", MachineTool.Namespaces.MachineTool);
            ModelTestContext.RequireChild<BaseVariableState>(
                context, sensor, "Locked", MachineTool.Namespaces.MachineTool);
            Assert.Multiple(() =>
            {
                Assert.That(classification.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.UInteger));
                Assert.That(life.FindChild(context, new QualifiedName(
                    "Qualified", context.NamespaceUris.GetIndexOrAppend(GmsModel.NamespaceUri))), Is.Null);
                Assert.That(sensor.Alignment, Is.Null);
                Assert.That(sensor.IsQualifiedStatus, Is.Null);
            });

            sensor.AddAlignment(context).AddIsQualifiedStatus(context).AddAxes(context);
            sensor.Alignment!.Value = ToolAlignmentState.Indexed;
            sensor.IsQualifiedStatus!.Value = ToolIsQualifiedStatus.Imprecise;
            sensor.Axes!.Value = ["A", "B"];
            Assert.Multiple(() =>
            {
                Assert.That(sensor.Alignment.DataType,
                    Is.EqualTo(NodeId.Create(3004, GmsModel.NamespaceUri, context.NamespaceUris)));
                Assert.That(sensor.IsQualifiedStatus.DataType,
                    Is.EqualTo(NodeId.Create(3002, GmsModel.NamespaceUri, context.NamespaceUris)));
                Assert.That(sensor.Axes.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            });
        }

        /// <summary>
        /// A MultiSensor's inherited placeholder creates the specialized sensor with required nested tool members.
        /// </summary>
        [Test]
        public void MultiSensorPlaceholderCreatesSensorRatherThanBaseTool()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(reverseNamespaces: true);
            MultiSensorState multi = context.CreateInstanceOfMultiSensorType();
            var browseName = new QualifiedName("ProbeHead", 1);
            SensorState sensor = multi.AddTool_Placeholder(context, browseName);
            Assert.Multiple(() =>
            {
                Assert.That(multi.FindChild(context, browseName), Is.SameAs(sensor));
                Assert.That(sensor.Parent, Is.SameAs(multi));
                Assert.That(sensor.TypeDefinitionId,
                    Is.EqualTo(NodeId.Create(1007, GmsModel.NamespaceUri, context.NamespaceUris)));
                Assert.That(sensor.Class, Is.Not.Null);
                Assert.That(sensor.ToolLife, Is.Not.Null);
                Assert.That(sensor.ControlIdentifier1, Is.Not.Null);
            });
        }

        /// <summary>
        /// The Qualified declaration retains published defaults; selecting the CU is a runtime responsibility.
        /// </summary>
        [Test]
        public void QualifiedDeclarationPreservesCanonicalDefaultsAndRequiredProperties()
        {
            SystemContext context = ModelTestContext.CreateSystemContext();
            Dictionary<NodeId, NodeState> nodes = ModelTestContext.IndexNodes(
                context, new NodeStateCollection().AddGmsTypeSystem(context));
            var qualified = (ToolLifeState<double>)nodes[NodeId.Create(
                6341, GmsModel.NamespaceUri, context.NamespaceUris)];
            Assert.Multiple(() =>
            {
                Assert.That(qualified.Indication!.Value, Is.EqualTo(ToolLifeIndication.Time));
                Assert.That(qualified.IsCountingUp!.Value, Is.True,
                    "The XML and Table 17 say true; the QualifiedToolLife CU instead requires false at runtime.");
                Assert.That(qualified.EngineeringUnits, Is.Not.Null);
                Assert.That(qualified.TypeDefinitionId, Is.EqualTo(ExpandedNodeId.ToNodeId(
                    MachineTool.VariableTypeIds.ToolLifeType, context.NamespaceUris)));
                Assert.That(qualified.ModellingRuleId, Is.EqualTo(Opc.Ua.ObjectIds.ModellingRule_Optional));
                Assert.That(qualified.Indication.BrowseName.NamespaceIndex,
                    Is.EqualTo(context.NamespaceUris.GetIndex(MachineTool.Namespaces.MachineTool)));
            });
        }

        /// <summary>
        /// GMS monitoring adds optional typed loading and tool monitoring without replacing required operation data.
        /// </summary>
        [Test]
        public void OptionalMonitoringIncludesInheritedMandatoryMembers()
        {
            SystemContext context = ModelTestContext.CreateSystemContext();
            GMSMonitoringState monitoring = context.CreateInstanceOfGMSMonitoringType();
            monitoring.AddLoadingMonitoring(context).AddToolMonitoring(context);
            var load = ModelTestContext.RequireChild<LoadingMonitoringState>(
                context, monitoring, "LoadingMonitoring", GmsModel.NamespaceUri);
            var tool = ModelTestContext.RequireChild<ToolMonitoringState>(
                context, monitoring, "ToolMonitoring", GmsModel.NamespaceUri);
            var status = ModelTestContext.RequireChild<BaseVariableState>(
                context, load, "LoadStatus", GmsModel.NamespaceUri);
            ModelTestContext.RequireChild<BaseVariableState>(
                context, load, "Name", MachineTool.Namespaces.MachineTool);
            ModelTestContext.RequireChild<BaseVariableState>(
                context, tool, "Name", MachineTool.Namespaces.MachineTool);
            Assert.Multiple(() =>
            {
                Assert.That(status.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.UInteger));
                Assert.That(load.IsInLoadingPosition, Is.Null);
                Assert.That(tool.ActiveTool, Is.Null);
            });
            tool.AddActiveTool(context);
            Assert.That(tool.ActiveTool!.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        /// <summary>
        /// GMS jobs and parts retain the older MachineTool production model while adding measurement metadata.
        /// </summary>
        [Test]
        public void ProductionJobsAndPartsRetainInheritedMandatoryStateAndStatistics()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(reverseNamespaces: true);
            GMSJobState job = context.CreateInstanceOfGMSJobType();
            GMSPartState part = context.CreateInstanceOfGMSPartType();
            ModelTestContext.RequireChild<PropertyState<string>>(
                context, job, "Identifier", MachineTool.Namespaces.MachineTool);
            ModelTestContext.RequireChild<BaseVariableState>(
                context, job, "RunsCompleted", MachineTool.Namespaces.MachineTool);
            ModelTestContext.RequireChild<BaseVariableState>(
                context, job, "RunsPlanned", MachineTool.Namespaces.MachineTool);
            ModelTestContext.RequireChild<OrderedListState>(
                context, job, "ProductionPrograms", MachineTool.Namespaces.MachineTool);
            var state = ModelTestContext.RequireChild<ProductionStateMachineState>(
                context, job, "State", MachineTool.Namespaces.MachineTool);
            ModelTestContext.RequireChild<FiniteStateVariableState>(
                context, state, "CurrentState", Opc.Ua.Namespaces.OpcUa);
            ModelTestContext.RequireChild<BaseVariableState>(
                context, part, "Name", MachineTool.Namespaces.MachineTool);
            ModelTestContext.RequireChild<BaseVariableState>(
                context, part, "PartQuality", MachineTool.Namespaces.MachineTool);
            ModelTestContext.RequireChild<BaseVariableState>(
                context, part, "ProcessIrregularity", MachineTool.Namespaces.MachineTool);
            Assert.Multiple(() =>
            {
                Assert.That(job.MeasurementReason, Is.Null);
                Assert.That(part.Operator, Is.Null);
                Assert.That(part.ProcessParameter, Is.Null);
            });

            job.AddMeasurementReason(context).AddRemainingTime(context);
            job.MeasurementReason!.Value = MeasurementReasonEnum.AuditMeasurement;
            part.AddOperator(context).AddProcessParameter(context);
            Assert.Multiple(() =>
            {
                Assert.That(job.MeasurementReason.DataType,
                    Is.EqualTo(NodeId.Create(3009, GmsModel.NamespaceUri, context.NamespaceUris)));
                Assert.That(job.RemainingTime!.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Duration));
                Assert.That(part.Operator!.CatalogName, Is.Not.Null);
                Assert.That(part.Operator.CatalogEntry, Is.Not.Null);
                Assert.That(part.ProcessParameter!.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            });
        }

        /// <summary>
        /// The real inherited statistics type remains available without inventing the undefined GMS statistics type.
        /// </summary>
        [Test]
        public void GmsRetainsOptionalMachineToolProductionStatistics()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(reverseNamespaces: true);
            GMSState machine = context.CreateInstanceOfGMSType();
            ProductionState production = machine.Production!;
            Assert.That(production.Statistics, Is.Null);
            production.AddStatistics(context);
            var statistics = ModelTestContext.RequireChild<ProductionStatisticsState>(
                context, production, "Statistics", MachineTool.Namespaces.MachineTool);
            Assert.That(statistics.PartsProducedInLifetime, Is.Null);
            statistics.AddPartsProducedInLifetime(context);
            statistics.PartsProducedInLifetime!.Value = 42;
            Assert.Multiple(() =>
            {
                Assert.That(statistics.TypeDefinitionId,
                    Is.EqualTo(NodeId.Create(20, MachineTool.Namespaces.MachineTool, context.NamespaceUris)));
                Assert.That(statistics.PartsProducedInLifetime.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.UInt32));
                Assert.That(statistics.PartsProducedInLifetime.Value, Is.EqualTo(42));
                Assert.That(statistics.PartsProducedInLifetime.BrowseName.NamespaceIndex,
                    Is.EqualTo(context.NamespaceUris.GetIndex(MachineTool.Namespaces.MachineTool)));
            });
        }

        /// <summary>
        /// Accessories and prognoses preserve their DI/Machinery identification and required calibration data.
        /// </summary>
        [Test]
        public void AccessoryAndCalibrationFactoriesPreserveMandatoryInheritedMembers()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(reverseNamespaces: true);
            RotaryTableState rotary = context.CreateInstanceOfRotaryTableType();
            SensorExchangeRackState sensorRack = context.CreateInstanceOfSensorExchangeRackType();
            TipExchangeRackState tipRack = context.CreateInstanceOfTipExchangeRackType();
            foreach (BaseObjectState accessory in new BaseObjectState[] { rotary, sensorRack, tipRack })
            {
                var identification = ModelTestContext.RequireChild<Machinery.MachineryItemIdentificationState>(
                    context, accessory, "Identification", Di.Namespaces.OpcUaDi);
                ModelTestContext.RequireChild<PropertyState<LocalizedText>>(
                    context, identification, "Manufacturer", Di.Namespaces.OpcUaDi);
                Assert.That(identification.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasAddIn));
            }
            CalibrationPrognosisState calibration = context.CreateInstanceOfCalibrationPrognosisType();
            Assert.Multiple(() =>
            {
                Assert.That(rotary.IsIntegrated, Is.Not.Null);
                Assert.That(rotary.NumberOfAxes!.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Byte));
                Assert.That(sensorRack.IsAvailable, Is.Not.Null);
                Assert.That(tipRack.IsAvailable, Is.Not.Null);
                Assert.That(calibration.Calibrated!.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Boolean));
                Assert.That(calibration.DateOfCalibration!.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.UtcTime));
                Assert.That(calibration.PredictedTime!.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.UtcTime));
                Assert.That(calibration.CalibrationCertificate, Is.Null);
            });
            calibration.AddCalibrationCertificate(context).AddCalibrationInterval(context);
            Assert.Multiple(() =>
            {
                Assert.That(calibration.CalibrationCertificate!.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                Assert.That(calibration.CalibrationInterval!.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Duration));
            });
        }

        /// <summary>
        /// Specialized result/event/alarm factories keep Machinery Result inheritance and GMS-specific members.
        /// </summary>
        [Test]
        public void ResultEventAndAlarmFactoriesPreserveTheirTypedExtensions()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(reverseNamespaces: true);
            GMSResultState result = context.CreateInstanceOfGMSResultType();
            IntermediateResultEventState intermediate = context.CreateInstanceOfIntermediateResultEventType();
            SensorWarningAlarmState alarm = context.CreateInstanceOfSensorWarningAlarmType();
            Assert.Multiple(() =>
            {
                Assert.That(result.ResultMetaData, Is.Not.Null);
                Assert.That(result.UsedTools, Is.Null);
                Assert.That(result.ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(intermediate.Result, Is.Not.Null);
                Assert.That(intermediate.EventId, Is.Not.Null);
                Assert.That(alarm.ErrorCode!.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.String));
                Assert.That(alarm.ActiveState, Is.Not.Null);
                Assert.That(alarm.InputNode, Is.Not.Null);
            });
            result.AddUsedTools(context);
            result.UsedTools!.Value = ["sensor-17", "sensor-18"];
            var browseName = new QualifiedName("Diameter", 1);
            CharacteristicState characteristic = intermediate.AddCharacteristics_Placeholder(context, browseName);
            characteristic.AddResultValue(context).AddNominal(context).AddUpperToleranceLimit(context);
            Assert.Multiple(() =>
            {
                Assert.That(intermediate.FindChild(context, browseName), Is.SameAs(characteristic));
                Assert.That(result.UsedTools.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                Assert.That(characteristic.ResultValue!.EngineeringUnits, Is.Not.Null);
                Assert.That(characteristic.UpperToleranceLimit!.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Double));
            });
        }

        /// <summary>
        /// Every released GMS enumeration registers exact symbols and rejects unknown values and names.
        /// </summary>
        [Test]
        public void EnumFactoriesPreserveReleasedValuesAndRejectInvalidSymbols()
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext();
            XElement root = XDocument.Load(Path.Combine(
                TestContext.CurrentContext.TestDirectory, "Model", "opc.ua.gms.nodeset2.xml")).Root!;
            XNamespace ns = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
            XElement[] enums =
            [
                .. root.Elements(ns + "UADataType").Where(node =>
                    node.Element(ns + "Definition")!.Elements(ns + "Field")
                        .Any(field => field.Attribute("Value") != null))
            ];
            Assert.That(enums, Has.Length.EqualTo(4));
            foreach (XElement definition in enums)
            {
                uint identifier = uint.Parse(
                    ((string)definition.Attribute("NodeId")!)[7..], CultureInfo.InvariantCulture);
                Assert.That(context.Factory.TryGetEnumeratedType(
                    new ExpandedNodeId(identifier, GmsModel.NamespaceUri), out IEnumeratedType? type), Is.True);
                foreach (XElement field in definition.Element(ns + "Definition")!.Elements(ns + "Field"))
                {
                    int value = (int)field.Attribute("Value")!;
                    string name = (string)field.Attribute("Name")!;
                    Assert.That(type!.TryGetSymbol(value, out string? symbol), Is.True);
                    Assert.That(symbol, Is.EqualTo(name));
                    Assert.That(type.TryGetValue(name, out int actual), Is.True);
                    Assert.That(actual, Is.EqualTo(value));
                }
                Assert.Multiple(() =>
                {
                    Assert.That(type!.TryGetSymbol(-1, out _), Is.False);
                    Assert.That(type.TryGetSymbol(int.MaxValue, out _), Is.False);
                    Assert.That(type.TryGetValue("NotAReleasedValue", out _), Is.False);
                });
            }
        }
    }
}
