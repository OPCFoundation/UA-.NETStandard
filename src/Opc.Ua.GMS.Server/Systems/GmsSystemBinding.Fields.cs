// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.GMS.Server
{
    internal sealed partial class GmsSystemBinding
    {
        /// <summary>
        /// Selects complete generated declarations, including enum labels, nested metadata and original wire types.
        /// Bare NodeState.CreateChild constructs only a shell and is intentionally not used for native declarations.
        /// </summary>
        private NodeState AddNativeField(NodeState parent, string name)
        {
            ISystemContext context = m_context.Context;
            return (parent, name) switch
            {
                (SensorState node, BrowseNames.Alignment) => node.AddAlignment(context),
                (SensorState node, BrowseNames.Axes) => node.AddAxes(context),
                (SensorState node, BrowseNames.Capabilities) => node.AddCapabilities(context),
                (SensorState node, BrowseNames.IsQualifiedStatus) => node.AddIsQualifiedStatus(context),
                (SensorState node, BrowseNames.AbsoluteProbe) => node.AddAbsoluteProbe(context),
                (SensorState node, BrowseNames.EngineeringUnit) => node.AddEngineeringUnit(context),
                (SensorState node, BrowseNames.MeasuringRange) => node.AddMeasuringRange(context),
                (SensorState node, BrowseNames.WorkingRange) => node.AddWorkingRange(context),
                (SensorState node, BrowseNames.Resolution) => node.AddResolution(context),
                (MultiSensorState node, BrowseNames.Alignment) => node.AddAlignment(context),
                (MultiSensorState node, BrowseNames.Axes) => node.AddAxes(context),
                (GMSIdentificationState node, BrowseNames.SubDeviceClass) => node.AddSubDeviceClass(context),
                (GMSIdentificationState node, BrowseNames.Workspace) => node.AddWorkspace(context),
                (LoadingMonitoringState node, BrowseNames.IsInLoadingPosition) => node.AddIsInLoadingPosition(context),
                (ToolMonitoringState node, BrowseNames.ActiveTool) => node.AddActiveTool(context),
                (AdditionalSensorState node, BrowseNames.Class) => node.AddClass(context),
                (AdditionalSensorState node, BrowseNames.Name) => node.AddName(context),
                (GMSJobState node, BrowseNames.BatchIdentifier) => node.AddBatchIdentifier(context),
                (GMSJobState node, BrowseNames.Duration) => node.AddDuration(context),
                (GMSJobState node, BrowseNames.RemainingTime) => node.AddRemainingTime(context),
                (GMSJobState node, BrowseNames.MeasurementReason) => node.AddMeasurementReason(context),
                (GMSPartState node, BrowseNames.PartDescription) => node.AddPartDescription(context),
                (GMSPartState node, BrowseNames.PartAmendmentStatus) => node.AddPartAmendmentStatus(context),
                (GMSPartState node, BrowseNames.ProductionNumber) => node.AddProductionNumber(context),
                (GMSPartState node, BrowseNames.NestIdentifier) => node.AddNestIdentifier(context),
                (GMSPartState node, BrowseNames.Operator) => node.AddOperator(context),
                (GMSPartState node, BrowseNames.PartCarrierIdentifier) => node.AddPartCarrierIdentifier(context),
                (GMSPartState node, BrowseNames.ProcessingMachineIdentifier) =>
                    node.AddProcessingMachineIdentifier(context),
                (GMSPartState node, BrowseNames.ProcessParameter) => node.AddProcessParameter(context),
                (CalibrationPrognosisState node, BrowseNames.CalibrationInterval) =>
                    node.AddCalibrationInterval(context),
                (CalibrationPrognosisState node, BrowseNames.CalibrationPreptime) =>
                    node.AddCalibrationPreptime(context),
                (CalibrationPrognosisState node, BrowseNames.CalibrationCertificate) =>
                    node.AddCalibrationCertificate(context),
                _ => throw Invalid($"The selected native type has no optional '{name}' declaration.")
            };
        }
    }
}
