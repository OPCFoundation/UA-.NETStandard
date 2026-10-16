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
using Opc.Ua.Di;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.ProcessValues;

namespace Opc.Ua.MachineTool.WMTP
{
    /// <summary>
    /// Materializes the released nested declarations using their generated factories, without copying wire metadata.
    /// This staging helper neither publishes nodes nor implements equipment behavior.
    /// </summary>
    public sealed class WmtpDeviceNodes
    {
        /// <summary>
        /// Adds explicitly selected nodes to a newly generated, unpublished peripheral.
        /// </summary>
        public WmtpDeviceNodes(
            ISystemContext context, WirelessMachineToolPeripheralState state, WmtpFeatures features)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if ((features & ~WmtpFeatures.All) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(features));
            }
            FolderState information = state.DeviceInformation ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError, "DeviceInformation is mandatory.");
            Identification = information.FindChild(context, new QualifiedName(
                Opc.Ua.Di.BrowseNames.Identification,
                context.NamespaceUris.GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi))) as
                    MachineryComponentIdentificationState ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "The component nameplate is missing.");
            if ((features & (WmtpFeatures.Calibration | WmtpFeatures.DeviceTime |
                WmtpFeatures.Firmware | WmtpFeatures.License)) != 0)
            {
                state.AddDeviceConfiguration(context);
                FolderState configuration = state.DeviceConfiguration!;
                if ((features & WmtpFeatures.Calibration) != 0)
                {
                    CalibrationMode = Attach(context, configuration,
                        context.CreateWirelessMachineToolPeripheralType_DeviceConfiguration_CalibrationMode(
                            configuration, true));
                    SwitchCalibrationMode = Attach(context, configuration,
                        context.CreateWirelessMachineToolPeripheralType_DeviceConfiguration_SwitchCalibrationMode(
                            configuration, true));
                }
                if ((features & WmtpFeatures.DeviceTime) != 0)
                {
                    SetDeviceTime = Attach(context, configuration,
                        context.CreateWirelessMachineToolPeripheralType_DeviceConfiguration_SetDeviceTime(
                            configuration, true));
                }
                if ((features & WmtpFeatures.Firmware) != 0)
                {
                    UpdateFirmware = Attach(context, configuration,
                        context.CreateWirelessMachineToolPeripheralType_DeviceConfiguration_UpdateFirmware(
                            configuration, true));
                }
                if ((features & WmtpFeatures.License) != 0)
                {
                    SendLicenseData = Attach(context, configuration,
                        context.CreateWirelessMachineToolPeripheralType_DeviceConfiguration_SendLicenseData(
                            configuration, true));
                }
            }
            if ((features & WmtpFeatures.Battery) != 0)
            {
                Battery = Attach(context, information,
                    context.CreateWirelessMachineToolPeripheralType_DeviceInformation_BatteryLevel(information, true));
            }
            if ((features & WmtpFeatures.License) != 0)
            {
                state.AddLifetimeCounters(context);
                LicenseInformation = Attach(context, information,
                    context.CreateWirelessMachineToolPeripheralType_DeviceInformation_LicenseInformation(
                        information, true));
                LicenseValidity = Attach(context, state.LifetimeCounters!,
                    context.CreateWirelessMachineToolPeripheralType_LifetimeCounters_LicenseValidity(
                        state.LifetimeCounters!, true));
            }
            if ((features & (WmtpFeatures.ServiceInformation | WmtpFeatures.AdvancedServiceInformation)) != 0)
            {
                NextServiceDate = Attach(context, information,
                    context.CreateWirelessMachineToolPeripheralType_DeviceInformation_NextServiceDate(
                        information, true));
                LastService = Attach(context, information,
                    context.CreateWirelessMachineToolPeripheralType_DeviceInformation_LastService(information, true));
                LifeCycleStatus = Attach(context, information,
                    context.CreateWirelessMachineToolPeripheralType_DeviceInformation_LifeCycleStatus(
                        information, true));
            }
            if ((features & WmtpFeatures.AdvancedServiceInformation) != 0)
            {
                state.AddLifetimeCounters(context).AddOperationCounters(context);
                RemainingLifetime = Attach(context, state.LifetimeCounters!,
                    context.CreateWirelessMachineToolPeripheralType_LifetimeCounters_RemainingLifetimeCounter(
                        state.LifetimeCounters!, true));
                RemainingCycles = Attach(context, state.LifetimeCounters!,
                    context.CreateWirelessMachineToolPeripheralType_LifetimeCounters_RemainingCycleCounter(
                        state.LifetimeCounters!, true));
                NextService = Attach(context, state.LifetimeCounters!,
                    context.CreateWirelessMachineToolPeripheralType_LifetimeCounters_NextServiceCounter(
                        state.LifetimeCounters!, true));
                state.OperationCounters!.AddOperationDuration(context);
                OperationSinceService = Attach(context, state.OperationCounters,
                    context.CreateWirelessMachineToolPeripheralType_OperationCounters_OperationSinceLastServiceCounter(
                        state.OperationCounters, true));
            }
            if (state.LifetimeCounters != null)
            {
                AddBuildingBlock(state, state.LifetimeCounters);
            }
            if (state.OperationCounters != null)
            {
                AddBuildingBlock(state, state.OperationCounters);
            }
        }

        /// <summary>
        /// Gets the existing DI-qualified nested component identity.
        /// </summary>
        public MachineryComponentIdentificationState Identification { get; }

        /// <summary>
        /// Gets the selected calibration state.
        /// </summary>
        public PropertyState<bool>? CalibrationMode { get; }

        /// <summary>
        /// Gets the selected generated calibration method.
        /// </summary>
        public SwitchCalibrationModeMethodState? SwitchCalibrationMode { get; }

        /// <summary>
        /// Gets the selected generated time method.
        /// </summary>
        public SetDeviceTimeMethodState? SetDeviceTime { get; }

        /// <summary>
        /// Gets the selected generated firmware method.
        /// </summary>
        public UpdateFirmwareMethodState? UpdateFirmware { get; }

        /// <summary>
        /// Gets the selected generated license-transfer method.
        /// </summary>
        public SendLicenseDataMethodState? SendLicenseData { get; }

        /// <summary>
        /// Gets the selected battery ProcessValue.
        /// </summary>
        public ProcessValueState? Battery { get; }

        /// <summary>
        /// Gets the selected license description.
        /// </summary>
        public PropertyState<string>? LicenseInformation { get; }

        /// <summary>
        /// Gets the selected license lifetime.
        /// </summary>
        public LifetimeVariableState? LicenseValidity { get; }

        /// <summary>
        /// Gets the selected service schedule.
        /// </summary>
        public PropertyState<DateTimeUtc>? NextServiceDate { get; }

        /// <summary>
        /// Gets the selected last-service date.
        /// </summary>
        public PropertyState<DateTimeUtc>? LastService { get; }

        /// <summary>
        /// Gets the selected generated lifecycle discrete variable.
        /// </summary>
        public MultiStateValueDiscreteState<uint>? LifeCycleStatus { get; }

        /// <summary>
        /// Gets the selected remaining lifetime.
        /// </summary>
        public LifetimeVariableState? RemainingLifetime { get; }

        /// <summary>
        /// Gets the selected remaining work cycles.
        /// </summary>
        public LifetimeVariableState? RemainingCycles { get; }

        /// <summary>
        /// Gets the selected service countdown.
        /// </summary>
        public LifetimeVariableState? NextService { get; }

        /// <summary>
        /// Gets the selected operation duration since service.
        /// </summary>
        public BaseDataVariableState<double>? OperationSinceService { get; }

        /// <summary>
        /// Adds the generated WMTP status properties and their exact table 50 references.
        /// </summary>
        public static ArrayOf<PropertyState<double>> AddLimitCounters(
            ISystemContext context, WirelessMachineToolPeripheralState peripheral, WMTPMeasurementState measurement)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (peripheral == null)
            {
                throw new ArgumentNullException(nameof(peripheral));
            }
            if (measurement == null)
            {
                throw new ArgumentNullException(nameof(measurement));
            }
            measurement.AddLimitCounters(context);
            MultiStateValueDiscreteState<ushort> status = context.CreateWMTPMeasurementType_Status(measurement, true);
            AssignIdentity(context, measurement, status);
            measurement.CreateOrReplaceStatus(context, status);
            PropertyState<double> high = Attach(context, measurement.Status!,
                context.CreateWMTPMeasurementType_Status_OperationAboveHigh(measurement.Status!, true));
            PropertyState<double> highHigh = Attach(context, measurement.Status!,
                context.CreateWMTPMeasurementType_Status_OperationAboveHighHigh(measurement.Status!, true));
            measurement.LimitCounters!.AddReference(Opc.Ua.ReferenceTypeIds.HasProperty, false, high.NodeId);
            high.AddReference(Opc.Ua.ReferenceTypeIds.HasProperty, true, measurement.LimitCounters.NodeId);
            measurement.LimitCounters.AddReference(Opc.Ua.ReferenceTypeIds.HasProperty, false, highHigh.NodeId);
            highHigh.AddReference(Opc.Ua.ReferenceTypeIds.HasProperty, true, measurement.LimitCounters.NodeId);
            AddBuildingBlock(peripheral, measurement.LimitCounters);
            return [high, highHigh];
        }

        /// <summary>
        /// Adds the released discovery reference, not a second owning parent.
        /// </summary>
        private static void AddBuildingBlock(WirelessMachineToolPeripheralState state, BaseObjectState block)
        {
            if (!state.MachineryBuildingBlocks!.ReferenceExists(Opc.Ua.ReferenceTypeIds.HasAddIn, false, block.NodeId))
            {
                state.MachineryBuildingBlocks.AddReference(Opc.Ua.ReferenceTypeIds.HasAddIn, false, block.NodeId);
            }
            if (!block.ReferenceExists(Opc.Ua.ReferenceTypeIds.HasAddIn, true, state.MachineryBuildingBlocks.NodeId))
            {
                block.AddReference(Opc.Ua.ReferenceTypeIds.HasAddIn, true, state.MachineryBuildingBlocks.NodeId);
            }
        }

        /// <summary>
        /// Allocates application identities for a generated nested declaration and its required descendants.
        /// </summary>
        /// <typeparam name="TState">The actual generated nested state.</typeparam>
        private static TState Attach<TState>(ISystemContext context, NodeState parent, TState node)
            where TState : BaseInstanceState
        {
            AssignIdentity(context, parent, node);
            parent.AddChild(node);
            return node;
        }

        /// <summary>
        /// Allocates fresh identities even when an inherited setter cannot recognize a companion-declared template ID.
        /// </summary>
        private static void AssignIdentity(ISystemContext context, NodeState parent, BaseInstanceState node)
        {
            NodeId previous = NodeInstanceExtensions.AssignInstanceNodeId(context, node);
            NodeInstanceExtensions.AssignInstanceChildNodeIds(context, node, previous, parent);
        }
    }
}
