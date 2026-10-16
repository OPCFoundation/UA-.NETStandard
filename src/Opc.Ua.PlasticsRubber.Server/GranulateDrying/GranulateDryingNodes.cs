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
using Opc.Ua.Machinery;
using Opc.Ua.PlasticsRubber;
using Opc.Ua.Di.Server;
using Opc.Ua.Server;

namespace Opc.Ua.GranulateDrying.Server
{
    /// <summary>
    /// Selects compiled declarations without reproducing the companion type system.
    /// </summary>
    internal static class GranulateDryingNodes
    {
        /// <summary>
        /// Instantiates a complete selected device graph using generated typed factories.
        /// </summary>
        internal static GranulateDryingDeviceState Create(
            DiNodeManager manager,
            NodeState parent,
            QualifiedName browseName,
            GranulateDryingDeviceOptions options)
        {
            ServerSystemContext context = manager.SystemContext;
            GranulateDryingDeviceState device = context.CreateInstanceOfGranulateDryingDeviceType(parent, browseName);
            device.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.Organizes;
            device.EventNotifier = EventNotifiers.SubscribeToEvents;
            ConfigureDevice(context, device, options.Features);

            BinsState bins = device.Dryer!.Bins!;
            ushort gdd = context.NamespaceUris.GetIndexOrAppend(Namespaces.GranulateDrying);
            for (int ii = 0; ii < options.Bins.Count; ii++)
            {
                BinState bin = bins.AddBin_Nr_Placeholder(context, new QualifiedName(Numbered("Bin", ii + 1), gdd));
                ConfigureBin(context, bin, options.Bins[ii].Features);
                Materialize(manager, bin, options.Bins[ii].OptionalMembers);
            }
            if (options.DesiccantBeds.Count != 0)
            {
                device.Dryer.AddDesiccantBeds(context);
                DesiccantBedsState beds = device.Dryer.DesiccantBeds!;
                for (int ii = 0; ii < options.DesiccantBeds.Count; ii++)
                {
                    DesiccantBedState bed = beds.AddDesiccantBed_Nr_Placeholder(
                        context, new QualifiedName(Numbered("DesiccantBed", ii + 1), gdd));
                    ConfigureBed(context, bed, options.DesiccantBeds[ii].Features);
                    Materialize(manager, bed, options.DesiccantBeds[ii].OptionalMembers);
                }
            }
            if (options.Maintenance.Count != 0)
            {
                device.Operation!.AddMaintenanceInformation(context);
                MaintenanceInformationState information = device.Operation.MaintenanceInformation!;
                information.NodeVersion!.Value = "1";
                for (int ii = 0; ii < options.Maintenance.Count; ii++)
                {
                    GranulateDryingMaintenanceOptions description = options.Maintenance[ii];
                    GDDMaintenanceState maintenance = information.AddMaintenance_Nr_Placeholder(
                        context, new QualifiedName(Numbered("Maintenance", ii + 1), gdd));
                    maintenance.Id!.Value = description.Id;
                    maintenance.DisplayName = description.DisplayName;
                    maintenance.Description = description.Description;
                    if (!description.Instructions.IsNull)
                    {
                        maintenance.AddMaintenanceInstructions(context);
                    }
                    Materialize(manager, maintenance, description.OptionalMembers);
                }
            }
            Materialize(manager, device, options.OptionalMembers);
            SetIdentification(context, device, options);
            SetUnits(context, device);
            SetMetadata(manager, device, options.AnalogMetadata);
            AssignStableIds(context, device, options.ProductInstanceUri, browseName.NamespaceIndex);
            return device;
        }

        /// <summary>
        /// Resolves a present standard member path without creating missing optional nodes.
        /// </summary>
        internal static BaseInstanceState? Find(ISystemContext context, NodeState root, string path)
        {
            NodeState node = root;
            foreach (string name in Split(path))
            {
                BaseInstanceState? child = FindChild(context, node, name);
                if (child == null)
                {
                    return null;
                }
                node = child;
            }
            return node as BaseInstanceState;
        }

        /// <summary>
        /// Returns a present standard member or reports an invalid compiled model/configuration.
        /// </summary>
        /// <typeparam name="T">The expected generated node type.</typeparam>
        internal static T Require<T>(ISystemContext context, NodeState root, string path)
            where T : BaseInstanceState
        {
            return Find(context, root, path) as T ?? throw ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "The selected standard member '{0}' is missing or has an unexpected state type.", path);
        }

        /// <summary>
        /// Expands selected optional paths through the generated child factories.
        /// </summary>
        internal static void Materialize(DiNodeManager manager, NodeState root, ArrayOf<string> paths)
        {
            ServerSystemContext context = manager.SystemContext;
            foreach (string path in paths)
            {
                NodeState node = root;
                foreach (string name in Split(path))
                {
                    BaseInstanceState? child = FindChild(context, node, name);
                    if (child == null)
                    {
                        BaseInstanceState declaration = FindDeclaration(manager, node, name) ??
                            throw ServiceResultException.Create(
                                StatusCodes.BadBrowseNameInvalid,
                                "'{0}' is not an optional child in the compiled standard model.", path);
                        child = node.CreateChild(context, declaration.BrowseName);
                        if (child == null)
                        {
                            child = context.NodeStateFactory.CreateInstance(
                                context, node, declaration.NodeClass, declaration.BrowseName,
                                declaration.ReferenceTypeId, declaration.TypeDefinitionId) as BaseInstanceState ??
                                throw ServiceResultException.Create(
                                    StatusCodes.BadConfigurationError, "No generated factory exists for '{0}'.", path);
                            node.AddChild(child);
                        }
                        child.Create(context, declaration);
                        TrimUnselectedChildren(context, child);
                    }
                    node = child;
                }
            }
        }

        /// <summary>
        /// Finds the exact compiled occurrence rather than manufacturing a blank generic child.
        /// </summary>
        private static BaseInstanceState? FindDeclaration(DiNodeManager manager, NodeState parent, string name)
        {
            NodeId type = (parent as BaseInstanceState)?.TypeDefinitionId ?? NodeId.Null;
            var visited = new HashSet<NodeId>();
            while (!type.IsNull && visited.Add(type))
            {
                NodeState? declaration = manager.FindPredefinedNode(type);
                if (declaration == null)
                {
                    break;
                }
                BaseInstanceState? child = FindChild(manager.SystemContext, declaration, name);
                if (child != null)
                {
                    string? uri = manager.SystemContext.NamespaceUris.GetString(child.BrowseName.NamespaceIndex);
                    if (uri != null && Array.IndexOf(s_modelNamespaces, uri) >= 0)
                    {
                        return child;
                    }
                }
                type = declaration switch
                {
                    BaseObjectTypeState objectType => objectType.SuperTypeId,
                    BaseVariableTypeState variableType => variableType.SuperTypeId,
                    _ => NodeId.Null
                };
            }
            return null;
        }

        /// <summary>
        /// Keeps mandatory descendants of a selected occurrence while leaving its optional children unselected.
        /// </summary>
        private static void TrimUnselectedChildren(ServerSystemContext context, BaseInstanceState root)
        {
            root.IsPartOfTypeHierarchy = false;
            root.ModellingRuleId = NodeId.Null;
            var children = new List<BaseInstanceState>();
            root.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child.ModellingRuleId == Opc.Ua.ObjectIds.ModellingRule_MandatoryPlaceholder)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Use explicit instance options to populate the mandatory placeholder '{0}'.", child.BrowseName);
                }
                if (child.ModellingRuleId == Opc.Ua.ObjectIds.ModellingRule_Optional ||
                    child.ModellingRuleId == Opc.Ua.ObjectIds.ModellingRule_OptionalPlaceholder)
                {
                    root.RemoveChild(child);
                    continue;
                }
                TrimUnselectedChildren(context, child);
            }
        }

        /// <summary>
        /// Assigns persistent, URI-qualified instance identities before bindings capture their keys.
        /// </summary>
        private static void AssignStableIds(
            ServerSystemContext context, NodeState root, string identity, ushort instanceNamespace)
        {
            var replacements = new Dictionary<NodeId, NodeId>();
            var visited = new HashSet<NodeState>(RefEqualityComparer.Default);
            Assign(root, "GranulateDrying/" + Uri.EscapeDataString(identity));
            root.UpdateReferenceTargets(context, replacements);

            void Assign(NodeState node, string identifier)
            {
                if (!visited.Add(node))
                {
                    return;
                }
                NodeId previous = node.NodeId;
                node.NodeId = new NodeId(identifier, instanceNamespace);
                node.IsPartOfTypeHierarchy = false;
                if (node is BaseInstanceState instance)
                {
                    instance.ModellingRuleId = NodeId.Null;
                }
                if (!previous.IsNull && previous != node.NodeId)
                {
                    replacements[previous] = node.NodeId;
                }
                var children = new List<BaseInstanceState>();
                node.GetChildren(context, children);
                foreach (BaseInstanceState child in children)
                {
                    string uri = context.NamespaceUris.GetString(child.BrowseName.NamespaceIndex) ??
                        throw new ArgumentException("A child BrowseName namespace is not registered.");
                    string name = child.BrowseName.Name ??
                        throw new ArgumentException("A child BrowseName is empty.");
                    Assign(child, identifier + "/" + Uri.EscapeDataString(uri) + ":" + Uri.EscapeDataString(name));
                }
            }
        }

        /// <summary>
        /// Formats a normative placeholder browse name.
        /// </summary>
        internal static string Numbered(string prefix, int number)
        {
            return prefix + "_" + number.ToString("D3", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Visits the generated instance graph once, including its shared identification add-in.
        /// </summary>
        internal static IEnumerable<NodeState> Walk(ISystemContext context, NodeState root)
        {
            var visited = new HashSet<NodeState>(RefEqualityComparer.Default);
            var pending = new Stack<NodeState>();
            pending.Push(root);
            while (pending.Count != 0)
            {
                NodeState node = pending.Pop();
                if (!visited.Add(node))
                {
                    continue;
                }
                yield return node;
                var children = new List<BaseInstanceState>();
                node.GetChildren(context, children);
                foreach (BaseInstanceState child in children)
                {
                    pending.Push(child);
                }
            }
        }

        /// <summary>
        /// Selects the generated top-level optional members.
        /// </summary>
        private static void ConfigureDevice(
            ServerSystemContext context,
            GranulateDryingDeviceState device,
            GranulateDryingDeviceFeatures features)
        {
            OperationState operation = device.Operation!;
            if (features.HasFlag(GranulateDryingDeviceFeatures.DisplayLanguage))
            {
                device.AddDisplayLanguage(context);
            }
            if (features.HasFlag(GranulateDryingDeviceFeatures.Users))
            {
                device.AddUserLogin(context);
                UserLoginState user = device.UserLogin!;
                user.ActualUser!.AddCardUid(context);
                user.ActualUser.AddLanguage(context);
                user.ActualUser.AddUserLevel(context);
                user.ActualUser.AddUserRole(context);
            }
            if (features.HasFlag(GranulateDryingDeviceFeatures.IdentifyDevice))
            {
                operation.AddIdentifyDevice(context);
            }
            if (features.HasFlag(GranulateDryingDeviceFeatures.ResetAllErrors))
            {
                operation.AddResetAllErrors(context);
            }
            if (features.HasFlag(GranulateDryingDeviceFeatures.ResetErrorById))
            {
                operation.AddResetErrorById(context);
            }
            if (features.HasFlag(GranulateDryingDeviceFeatures.ResetEnergyMeasurement))
            {
                operation.AddResetEnergyMeasurement(context);
            }
            if (features.HasFlag(GranulateDryingDeviceFeatures.MainFailureBehaviour))
            {
                operation.AddMainFailureBehaviour(context);
            }
            if (features.HasFlag(GranulateDryingDeviceFeatures.TotalEnergy))
            {
                operation.AddTotalEnergy(context);
                ConfigureEnergy(context, operation.TotalEnergy!);
            }
            if (features.HasFlag(GranulateDryingDeviceFeatures.EnergyConsumptionResettable))
            {
                operation.AddEnergyConsumptionResettable(context);
            }
            if (features.HasFlag(GranulateDryingDeviceFeatures.EnergyConsumptionLimit))
            {
                operation.AddEnergyConsumptionLimit(context);
            }
            if (features.HasFlag(GranulateDryingDeviceFeatures.OperationMode))
            {
                FolderState blocks = device.MachineryBuildingBlocks!;
                MachineryOperationModeStateMachineState mode =
                    context.CreateInstanceOfMachineryOperationModeStateMachineType(blocks,
                        new QualifiedName("MachineryOperationMode",
                            context.NamespaceUris.GetIndexOrAppend(Machinery.Namespaces.Machinery)));
                mode.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasAddIn;
                blocks.AddChild(mode);
            }
            if (features.HasFlag(GranulateDryingDeviceFeatures.ConfigurationPages))
            {
                MachineConfigurationState configuration = device.MachineConfiguration!;
                configuration.AddGetCurrentPage(context);
                configuration.AddGetPage(context);
                configuration.AddPageDirectory(context);
            }
        }

        /// <summary>
        /// Selects the generated bin optional members and complete request/response groups.
        /// </summary>
        private static void ConfigureBin(
            ServerSystemContext context,
            BinState bin,
            GranulateDryingBinFeatures features)
        {
            if (features.HasFlag(GranulateDryingBinFeatures.MaterialIdentification))
            {
                bin.AddMaterialIdentification(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.BatchNumber))
            {
                bin.AddBatchNumber(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.MaterialDryingTime))
            {
                bin.AddMaterialDryingTime(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.RemainingMaterialDryingTime))
            {
                bin.AddRemainingMaterialDryingTime(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.ExpiredDryingTime))
            {
                bin.AddExpiredDryingTime(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.TemperatureReduction))
            {
                bin.AddTemperatureReduction(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.DryingProtection))
            {
                bin.AddActivateDryingProtection(context);
                bin.AddDeactivateDryingProtection(context);
                bin.AddDryingProtectionStatus(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.MaterialAvailableDryer))
            {
                bin.AddMaterialAvailableDryer(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.MaterialAvailableMachine))
            {
                bin.AddMaterialAvailableMachine(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.MaterialFeeder))
            {
                bin.AddActivateMaterialFeederDryer(context);
                bin.AddDeactivateMaterialFeederDryer(context);
                bin.AddMaterialFeederDryerStatus(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.MachineFeeder))
            {
                bin.AddActivateMaterialFeederMachine(context);
                bin.AddDeactivateMaterialFeederMachine(context);
                bin.AddMaterialFeederMachineStatus(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.AllFeeders))
            {
                bin.AddActivateAllFeeders(context);
                bin.AddDeactivateAllFeeders(context);
                bin.AddAllFeedersStatus(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.TemperatureController))
            {
                bin.AddActivateTemperatureController(context);
                bin.AddDeactivateTemperatureController(context);
                bin.AddTemperatureControllerStatus(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.DustRemovalPulses))
            {
                bin.AddDustRemovalPulses(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.DryingLevel))
            {
                bin.AddDryingLevel(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.AirFlowMode))
            {
                bin.AddAirFlowMode(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.IntervalTimeOn))
            {
                bin.AddIntervalTimeOn(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.IntervalTimeOff))
            {
                bin.AddIntervalTimeOff(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.InitialFillingActive))
            {
                bin.AddInitialFillingActive(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.ResetDryingTime))
            {
                bin.AddResetDryingTime(context);
                bin.AddResetDryingTimeResponseStatus(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.AirSource))
            {
                bin.AddAirSource(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.AirFlow))
            {
                bin.AddAirFlow(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.TotalAirConsumption))
            {
                bin.AddTotalAirConsumption(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.DewPoint))
            {
                bin.AddDewPoint(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.DryingProtectionAirVolumeFlowReduction))
            {
                bin.AddActivateDryingProtectionAirVolumeFlowReduction(context);
                bin.AddDeactivateDryingProtectionAirVolumeFlowReduction(context);
                bin.AddDryingProtectionAirVolumeFlowMode(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.DryAirTemperature))
            {
                bin.AddDryAirTemperature(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.AmbientAirTemperature))
            {
                bin.AddAmbientAirTemperature(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.TopTemperature))
            {
                bin.AddTopTemperature(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.ConeTemperature))
            {
                bin.AddConeTemperature(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.CylinderTemperature))
            {
                bin.AddCylinderTemperature(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.LooseningAid))
            {
                bin.AddLooseningAid(context);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.TotalEnergy))
            {
                bin.AddTotalEnergy(context);
                ConfigureEnergy(context, bin.TotalEnergy!);
            }
            if (features.HasFlag(GranulateDryingBinFeatures.EnergyConsumptionResettable))
            {
                bin.AddEnergyConsumptionResettable(context);
            }
        }

        /// <summary>
        /// Selects the generated desiccant-bed optional members.
        /// </summary>
        private static void ConfigureBed(
            ServerSystemContext context,
            DesiccantBedState bed,
            GranulateDryingBedFeatures features)
        {
            if (features.HasFlag(GranulateDryingBedFeatures.DewPoint))
            {
                bed.AddDewPoint(context);
            }
            if (features.HasFlag(GranulateDryingBedFeatures.AirFlow))
            {
                bed.AddAirFlow(context);
            }
            if (features.HasFlag(GranulateDryingBedFeatures.TemperatureOfDesiccantBed))
            {
                bed.AddTemperatureOfDesiccantBed(context);
            }
            if (features.HasFlag(GranulateDryingBedFeatures.TemperatureDesiccantBedOut))
            {
                bed.AddTemperatureDesiccantBedOut(context);
            }
            if (features.HasFlag(GranulateDryingBedFeatures.TemperatureBackToDesiccantBed))
            {
                bed.AddTemperatureBackToDesiccantBed(context);
            }
            if (features.HasFlag(GranulateDryingBedFeatures.TotalEnergy))
            {
                bed.AddTotalEnergy(context);
                ConfigureEnergy(context, bed.TotalEnergy!);
            }
            if (features.HasFlag(GranulateDryingBedFeatures.EnergyConsumptionResettable))
            {
                bed.AddEnergyConsumptionResettable(context);
            }
            if (features.HasFlag(GranulateDryingBedFeatures.Controller))
            {
                bed.AddActivateDesiccantBedController(context);
                bed.AddDeactivateDesiccantBedController(context);
                bed.AddDesiccantBedControllerStatus(context);
            }
        }

        /// <summary>
        /// Makes the useful lifetime-energy and power members explicit; other common members remain selectable.
        /// </summary>
        private static void ConfigureEnergy(ServerSystemContext context, EnergyState energy)
        {
            energy.AddActualPower(context);
            energy.AddPowerConsumption(context);
        }

        /// <summary>
        /// Configures every generated identification add-in with the same physical identity.
        /// </summary>
        private static void SetIdentification(
            ServerSystemContext context,
            GranulateDryingDeviceState device,
            GranulateDryingDeviceOptions options)
        {
            foreach (NodeState node in Walk(context, device))
            {
                if (node is MachineIdentificationState identification)
                {
                    options.ConfigureIdentification?.Invoke(identification, context);
                    identification.CreateOrReplaceManufacturer(context, null).Value = options.Manufacturer;
                    identification.CreateOrReplaceModel(context, null).Value = options.Model;
                    identification.CreateOrReplaceSerialNumber(context, null).Value = options.SerialNumber;
                    identification.CreateOrReplaceProductInstanceUri(context, null).Value = options.ProductInstanceUri;
                    identification.CreateOrReplaceDeviceClass(context, null).Value = "Granulate Drying Device";
                }
            }
        }

        /// <summary>
        /// Sets SI engineering units required by the drying model; no measurement values are synthesized.
        /// </summary>
        private static void SetUnits(ServerSystemContext context, GranulateDryingDeviceState device)
        {
            foreach (NodeState node in Walk(context, device))
            {
                if (node is not BaseAnalogState variable)
                {
                    continue;
                }
                string name = variable.BrowseName.Name ?? string.Empty;
                string parent = variable.Parent?.BrowseName.Name ?? string.Empty;
                EUInformation? unit = name switch
                {
                    "EnergyConsumptionResettable" or "PowerConsumption" =>
                        GranulateDryingUnits.KilowattHour,
                    "ActualPower" => GranulateDryingUnits.Kilowatt,
                    "TotalAirConsumption" => GranulateDryingUnits.CubicMetre,
                    "TemperatureReduction" => GranulateDryingUnits.Kelvin,
                    "DewPoint" => GranulateDryingUnits.Celsius,
                    _ when parent.Contains("Temperature", StringComparison.Ordinal) ||
                        parent == "DewPoint" => GranulateDryingUnits.Celsius,
                    _ when parent == "AirFlow" && (variable.Parent as BaseInstanceState)?.Parent is DesiccantBedState =>
                        GranulateDryingUnits.LitrePerSecond,
                    _ when parent == "AirFlow" => GranulateDryingUnits.CubicMetrePerHour,
                    _ => null
                };
                if (unit != null)
                {
                    variable.AddEngineeringUnits(context);
                    variable.EngineeringUnits!.Value = unit;
                }
                variable.AddEURange(context);
                if (variable.EURange!.WrappedValue.IsNull ||
                    variable.EURange.Value is { Low: 0, High: 0 })
                {
                    variable.EURange.Value = new Range { Low = -double.MaxValue, High = double.MaxValue };
                }
            }
        }

        /// <summary>
        /// Applies exact hardware metadata without modifying the generated declarations or units of other instances.
        /// </summary>
        private static void SetMetadata(
            DiNodeManager manager,
            GranulateDryingDeviceState device,
            ArrayOf<GranulateDryingAnalogMetadata> metadata)
        {
            ServerSystemContext context = manager.SystemContext;
            foreach (GranulateDryingAnalogMetadata item in metadata)
            {
                NodeState target = item.Target.Kind switch
                {
                    GranulateDryingTargetKind.Device when item.Target.Number == 0 => device,
                    GranulateDryingTargetKind.Bin => Require<BinState>(context,
                        device.Dryer!.Bins!, Numbered("Bin", item.Target.Number)),
                    GranulateDryingTargetKind.DesiccantBed => Require<DesiccantBedState>(context,
                        device.Dryer!.DesiccantBeds!, Numbered("DesiccantBed", item.Target.Number)),
                    GranulateDryingTargetKind.Maintenance => Require<GDDMaintenanceState>(context,
                        device.Operation!.MaintenanceInformation!, Numbered("Maintenance", item.Target.Number)),
                    _ => throw new ArgumentException("The metadata target is invalid.", nameof(metadata))
                };
                BaseAnalogState variable = Require<BaseAnalogState>(context, target, item.Member);
                variable.AddEngineeringUnits(context);
                variable.AddEURange(context);
                PropertyState<EUInformation> unit = variable.EngineeringUnits!;
                unit.Value = new EUInformation
                {
                    NamespaceUri = item.EngineeringUnits.NamespaceUri,
                    UnitId = item.EngineeringUnits.UnitId,
                    DisplayName = item.EngineeringUnits.DisplayName,
                    Description = item.EngineeringUnits.Description
                };
                variable.EURange!.Value =
                    new Range { Low = item.EngineeringRange.Low, High = item.EngineeringRange.High };
            }
        }

        /// <summary>
        /// Finds one child by its unambiguous local standard browse name.
        /// </summary>
        private static BaseInstanceState? FindChild(ISystemContext context, NodeState node, string name)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            BaseInstanceState? found = null;
            foreach (BaseInstanceState child in children)
            {
                if (child.BrowseName.Name == name)
                {
                    if (found != null && !ReferenceEquals(found, child))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadBrowseNameDuplicated, "The standard member '{0}' is ambiguous.", name);
                    }
                    found = child;
                }
            }
            return found;
        }

        /// <summary>
        /// Rejects malformed optional or committed-value paths.
        /// </summary>
        private static string[] Split(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A standard member path is required.", nameof(path));
            }
            string[] names = path.Split('/');
            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Contains(':',
                    StringComparison.Ordinal))
                {
                    throw new ArgumentException("Use unqualified, slash-separated standard browse names.",
                        nameof(path));
                }
            }
            return names;
        }

        /// <summary>
        /// The only declaration namespaces accepted for optional member selection.
        /// </summary>
        private static readonly string[] s_modelNamespaces =
        [
            Namespaces.GranulateDrying,
            PlasticsRubber.Namespaces.PlasticsRubber,
            Di.Namespaces.OpcUaDi,
            Machinery.Namespaces.Machinery,
            Opc.Ua.Namespaces.OpcUa
        ];
    }
}
