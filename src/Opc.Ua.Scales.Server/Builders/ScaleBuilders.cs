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
using Opc.Ua.Di;
using Opc.Ua.Machinery;
using Opc.Ua.Scales.Server.Runtime;

namespace Opc.Ua.Scales.Server.Builders
{
    /// <summary>
    /// Build-time helpers shared by the scale, module and system builders.
    /// </summary>
    internal static class ScaleNodes
    {
        public static ScaleDeviceState CreateScale(
            ISystemContext context,
            ScaleKind kind,
            NodeState parent,
            QualifiedName browseName)
        {
            return kind switch
            {
                ScaleKind.Simple => context.CreateInstanceOfSimpleScaleType(parent, browseName),
                ScaleKind.Laboratory => context.CreateInstanceOfLaboratoryScaleType(parent, browseName),
                ScaleKind.Hopper => context.CreateInstanceOfHopperScaleType(parent, browseName),
                ScaleKind.WeighingModule => context.CreateInstanceOfWeighingModuleType(parent, browseName),
                ScaleKind.AutomaticFilling => context.CreateInstanceOfAutomaticFillingScaleType(parent, browseName),
                ScaleKind.Catchweigher => context.CreateInstanceOfCatchweigherType(parent, browseName),
                ScaleKind.Checkweigher => context.CreateInstanceOfCheckweigherType(parent, browseName),
                ScaleKind.AutomaticWeightPriceLabeler =>
                    context.CreateInstanceOfAutomaticWeightPriceLabelerType(parent, browseName),
                ScaleKind.Continuous => context.CreateInstanceOfContinuousScaleType(parent, browseName),
                ScaleKind.LossInWeight => context.CreateInstanceOfLossInWeightScaleType(parent, browseName),
                ScaleKind.PieceCounting => context.CreateInstanceOfPieceCountingScaleType(parent, browseName),
                ScaleKind.Recipe => context.CreateInstanceOfRecipeScaleType(parent, browseName),
                ScaleKind.TotalizingHopper => context.CreateInstanceOfTotalizingHopperScaleType(parent, browseName),
                ScaleKind.Vehicle => context.CreateInstanceOfVehicleScaleType(parent, browseName),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }

        /// <summary>
        /// Writes an identification record into an OPC 40001-1
        /// identification object.
        /// </summary>
        public static void WriteIdentification(
            ISystemContext context,
            MachineryItemIdentificationState target,
            ScaleIdentification identification,
            bool requireProductInstanceUri)
        {
            if (identification.Manufacturer.IsNullOrEmpty || string.IsNullOrEmpty(identification.SerialNumber))
            {
                throw new ArgumentException(
                    "The identification needs a Manufacturer and a SerialNumber (OPC 40001-1).",
                    nameof(identification));
            }
            if (requireProductInstanceUri && string.IsNullOrEmpty(identification.ProductInstanceUri))
            {
                throw new ArgumentException(
                    "A scale or scale system identification needs a ProductInstanceUri (OPC 40001-1 MachineIdentificationType).",
                    nameof(identification));
            }
            ScaleValues.Set(context, target.Manufacturer, identification.Manufacturer);
            ScaleValues.Set(context, target.SerialNumber, identification.SerialNumber!);
            if (identification.ProductInstanceUri != null)
            {
                target.AddProductInstanceUri(context);
                ScaleValues.Set(context, target.ProductInstanceUri, identification.ProductInstanceUri);
            }
            if (identification.ManufacturerUri != null)
            {
                target.AddManufacturerUri(context);
                ScaleValues.Set(context, target.ManufacturerUri, identification.ManufacturerUri);
            }
            if (!identification.Model.IsNullOrEmpty)
            {
                target.AddModel(context);
                ScaleValues.Set(context, target.Model, identification.Model);
            }
            if (identification.ProductCode != null)
            {
                target.AddProductCode(context);
                ScaleValues.Set(context, target.ProductCode, identification.ProductCode);
            }
            if (identification.HardwareRevision != null)
            {
                target.AddHardwareRevision(context);
                ScaleValues.Set(context, target.HardwareRevision, identification.HardwareRevision);
            }
            if (identification.SoftwareRevision != null)
            {
                target.AddSoftwareRevision(context);
                ScaleValues.Set(context, target.SoftwareRevision, identification.SoftwareRevision);
            }
            if (identification.DeviceClass != null)
            {
                target.AddDeviceClass(context);
                ScaleValues.Set(context, target.DeviceClass, identification.DeviceClass);
            }
            if (identification.AssetId != null)
            {
                target.AddAssetId(context);
                ScaleValues.Set(context, target.AssetId, identification.AssetId);
            }
            if (!identification.ComponentName.IsNullOrEmpty)
            {
                target.AddComponentName(context);
                ScaleValues.Set(context, target.ComponentName, identification.ComponentName);
            }
            if (identification.YearOfConstruction.HasValue)
            {
                target.AddYearOfConstruction(context);
                ScaleValues.Set(context, target.YearOfConstruction, identification.YearOfConstruction.Value);
            }
            if (identification.MonthOfConstruction.HasValue)
            {
                target.AddMonthOfConstruction(context);
                ScaleValues.Set(context, target.MonthOfConstruction, identification.MonthOfConstruction.Value);
            }
            if (identification.InitialOperationDate.HasValue)
            {
                target.AddInitialOperationDate(context);
                ScaleValues.Set(
                    context,
                    target.InitialOperationDate,
                    (DateTimeUtc)identification.InitialOperationDate.Value);
            }
            if (identification.Location != null && target is MachineIdentificationState machine)
            {
                machine.AddLocation(context);
                ScaleValues.Set(context, machine.Location, identification.Location);
            }
        }

        /// <summary>
        /// Mirrors the identification onto the DI nameplate of the component
        /// itself, which a DI client reads without knowing OPC 40001-1.
        /// </summary>
        public static void MirrorNameplate(ISystemContext context, ComponentState component, ScaleIdentification identification)
        {
            component.AddManufacturer(context);
            ScaleValues.Set(context, component.Manufacturer, identification.Manufacturer);
            component.AddSerialNumber(context);
            ScaleValues.Set(context, component.SerialNumber, identification.SerialNumber ?? string.Empty);
            if (identification.ProductInstanceUri != null)
            {
                component.AddProductInstanceUri(context);
                ScaleValues.Set(context, component.ProductInstanceUri, identification.ProductInstanceUri);
            }
            if (!identification.Model.IsNullOrEmpty)
            {
                component.AddModel(context);
                ScaleValues.Set(context, component.Model, identification.Model);
            }
            if (identification.HardwareRevision != null)
            {
                component.AddHardwareRevision(context);
                ScaleValues.Set(context, component.HardwareRevision, identification.HardwareRevision);
            }
            if (identification.SoftwareRevision != null)
            {
                component.AddSoftwareRevision(context);
                ScaleValues.Set(context, component.SoftwareRevision, identification.SoftwareRevision);
            }
        }

        /// <summary>
        /// References the OPC 40001-1 state add-ins from the
        /// <c>MachineryBuildingBlocks</c> folder, as OPC 40200 Tables 12, 16,
        /// 18 and 21 require in addition to the component's own HasAddIn.
        /// </summary>
        public static void WireBuildingBlocks(ISystemContext context, FolderState? folder, params NodeState?[] addIns)
        {
            if (folder == null)
            {
                return;
            }
            NodeId hasAddIn = Opc.Ua.Types.ReferenceTypeIds.HasAddIn;
            var targets = new HashSet<NodeId>(addIns.Where(a => a != null).Select(a => a!.NodeId));

            // The instance declaration carries HasAddIn references to the
            // add-ins of the type; a reference that does not resolve to one of
            // this instance's add-ins is left over from the declaration.
            var existing = new List<IReference>();
            folder.GetReferences(context, existing, hasAddIn, false);
            foreach (IReference reference in existing)
            {
                if (!targets.Contains((NodeId)reference.TargetId))
                {
                    folder.RemoveReference(hasAddIn, false, reference.TargetId);
                }
            }
            foreach (NodeState? addIn in addIns)
            {
                if (addIn == null)
                {
                    continue;
                }
                if (!folder.ReferenceExists(hasAddIn, false, addIn.NodeId))
                {
                    folder.AddReference(hasAddIn, false, addIn.NodeId);
                }
                if (!addIn.ReferenceExists(hasAddIn, true, folder.NodeId))
                {
                    addIn.AddReference(hasAddIn, true, folder.NodeId);
                }
            }
        }

        /// <summary>
        /// Lists a type in a DI <c>SupportedTypes</c> folder; the model ships
        /// the folders empty, so the server has to fill them (OPC 40200 §6.3).
        /// </summary>
        public static void AddSupportedType(ConfigurableObjectState? subDevices, NodeId typeId)
        {
            FolderState? folder = subDevices?.SupportedTypes;
            if (folder == null || folder.ReferenceExists(Opc.Ua.Types.ReferenceTypeIds.Organizes, false, typeId))
            {
                return;
            }
            folder.AddReference(Opc.Ua.Types.ReferenceTypeIds.Organizes, false, typeId);
        }
    }

    /// <summary>
    /// The default <see cref="IScaleBuilder"/>.
    /// </summary>
    internal sealed class ScaleBuilder : IScaleBuilder
    {
        public ScaleBuilder(ScaleRuntimeServices services, ScaleDeviceState scale, ScaleKind kind)
        {
            m_services = services;
            Scale = scale;
            Kind = kind;
        }

        public ScaleDeviceState Scale { get; }

        public ScaleKind Kind { get; }

        private ISystemContext Context => m_services.Context;

        public IScaleBuilder WithIdentification(ScaleIdentification identification)
        {
            if (identification == null)
            {
                throw new ArgumentNullException(nameof(identification));
            }
            if (Scale.Identification is not MachineryItemIdentificationState target)
            {
                throw new InvalidOperationException("The scale has no Machinery identification add-in.");
            }
            ScaleNodes.WriteIdentification(Context, target, identification, requireProductInstanceUri: true);
            ScaleNodes.MirrorNameplate(Context, Scale, identification);
            m_identified = true;
            return this;
        }

        public IScaleBuilder WithWeighingRange(WeighingRangeDefinition range)
        {
            m_ranges.Add((range ?? throw new ArgumentNullException(nameof(range))).Validate());
            return this;
        }

        public IScaleBuilder WithUnit(EUInformation unit)
        {
            m_unit = unit ?? throw new ArgumentNullException(nameof(unit));
            return this;
        }

        public IScaleBuilder WithAllowedEngineeringUnits(params EUInformation[] units)
        {
            if (units == null || units.Length == 0)
            {
                throw new ArgumentException("At least one unit is required.", nameof(units));
            }
            Scale.AddAllowedEngineeringUnits(Context);
            Scale.AllowedEngineeringUnits!.Value = units.ToArrayOf();
            return this;
        }

        public IScaleBuilder WithMinimalWeight(double minimalWeight)
        {
            Scale.AddMinimalWeight(Context);
            ScaleValues.Set(Context, Scale.MinimalWeight, Variant.From(minimalWeight));
            m_minimalWeightSet = true;
            return this;
        }

        public IScaleBuilder WithLegalForTrade(bool legalForTrade = true)
        {
            m_legalForTrade = legalForTrade;
            return this;
        }

        public IScaleBuilder WithWeightDetails()
        {
            AddWeightDetails(Scale.CurrentWeight);
            m_weightDetails = true;
            return this;
        }

        public IScaleBuilder WithZeroAndTare(bool presetTare = true)
        {
            Scale.AddSetZero(Context);
            Scale.AddSetTare(Context);
            Scale.AddClearTare(Context);
            if (presetTare)
            {
                Scale.AddSetPresetTare(Context);
            }
            return this;
        }

        public IScaleBuilder WithRegisterWeight()
        {
            Scale.AddRegisterWeight(Context);
            Scale.AddRegisteredWeight(Context);
            return this;
        }

        public IScaleBuilder WithProductionPreset(Action<IProductionPresetBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            Scale.AddProductionPreset(Context);
            m_preset ??= new ProductionPresetBuilder(
                m_services,
                Scale.ProductionPreset!,
                ScalesModel.ProductTypeOf(Kind));
            configure(m_preset);
            return this;
        }

        public IScaleBuilder WithProductionOutput()
        {
            Scale.AddProductionOutput(Context);
            AddStatisticMembers(Context, Scale.ProductionOutput!);
            return this;
        }

        public IScaleBuilder WithPackMLState()
        {
            Scale.AddState(Context);
            AddPackMLMethods(Context, Scale.State!);
            return this;
        }

        public IScaleBuilder WithMachineryBuildingBlocks()
        {
            Scale.AddMachineryItemState(Context);
            Scale.AddMachineryOperationMode(Context);
            Scale.AddMachineryBuildingBlocks(Context);
            ScaleNodes.WireBuildingBlocks(
                Context,
                Scale.MachineryBuildingBlocks,
                Scale.MachineryItemState,
                Scale.MachineryOperationMode);
            return this;
        }

        public IScaleBuilder WithProcessState(string processStateId, LocalizedText processStateMessage)
        {
            Scale.AddProcessStateId(Context);
            Scale.AddProcessStateMessage(Context);
            ScaleValues.Set(Context, Scale.ProcessStateId, processStateId);
            ScaleValues.Set(Context, Scale.ProcessStateMessage, processStateMessage);
            return this;
        }

        public IScaleBuilder WithTypeFeatures()
        {
            ISystemContext context = Context;
            switch (Scale)
            {
                case LaboratoryScaleState laboratory:
                    laboratory.AddCalibrationNeeded(context);
                    laboratory.AddCalibrationRunning(context);
                    laboratory.AddLevelingRunning(context);
                    laboratory.AddIonisatorRunning(context);
                    laboratory.AddDraftShieldLeftClosed(context);
                    laboratory.AddDraftShieldRightClosed(context);
                    laboratory.AddDraftShieldTopClosed(context);
                    laboratory.AddOpenDraftShields(context);
                    laboratory.AddCloseDraftShields(context);
                    laboratory.AddStartLeveling(context);
                    laboratory.AddStartCalibration(context);
                    laboratory.AddStartIonisator(context);
                    laboratory.AddStopIonisator(context);
                    InitFlags(
                        laboratory.CalibrationNeeded,
                        laboratory.CalibrationRunning,
                        laboratory.LevelingRunning,
                        laboratory.IonisatorRunning,
                        laboratory.DraftShieldLeftClosed,
                        laboratory.DraftShieldRightClosed,
                        laboratory.DraftShieldTopClosed);
                    break;
                case HopperScaleState hopper:
                    hopper.AddLevelMax(context);
                    hopper.AddLevelMin(context);
                    break;
                case AutomaticFillingScaleState filling:
                    filling.AddDeviation(context);
                    filling.AddToleranceState(context);
                    break;
                case CheckweigherState checkweigher:
                    checkweigher.AddTU1Percent(context);
                    break;
                case ContinuousScaleState continuous:
                    continuous.AddMasterTotalizer(context);
                    continuous.MasterTotalizer?.AddResetTotalizer(context);
                    continuous.AddControlMagnitude(context);
                    continuous.AddLoad(context);
                    continuous.AddMaxFlowRate(context);
                    continuous.AddMinFlowRate(context);
                    continuous.AddRateControlMode(context);
                    continuous.AddSpeed(context);
                    continuous.AddTargetFlowRate(context);
                    if (continuous.RateControlMode != null)
                    {
                        continuous.RateControlMode.Value = RateControlMode.Gravimetric_0;
                    }
                    if (continuous is LossInWeightScaleState lossInWeight)
                    {
                        lossInWeight.AddBinWeight(context);
                    }
                    break;
                case PieceCountingScaleState pieceCounting:
                    pieceCounting.AddReferenceOptimisationRange(context);
                    pieceCounting.AddStartReference(context);
                    break;
                case RecipeScaleState recipe:
                    recipe.AddRecipes(context);
                    recipe.Recipes?.AddAddRecipe(context);
                    recipe.Recipes?.AddRemoveRecipe(context);
                    recipe.AddStartRecipe(context);
                    recipe.AddStopRecipe(context);
                    recipe.AddContinueRecipe(context);
                    recipe.AddSkipCurrentRecipeElement(context);
                    recipe.AddAbortRecipe(context);
                    recipe.AddSupportedTargetValues(context);
                    recipe.AddSupportedThresholdValues(context);
                    break;
                case VehicleScaleState vehicle:
                    vehicle.AddInboundWeighing(context);
                    vehicle.AddOutboundWeighing(context);
                    vehicle.AddOnePassWeighing(context);
                    break;
            }
            return this;
        }

        public IScaleBuilder WithRecipeFiles()
        {
            if (Scale is not RecipeScaleState)
            {
                throw new InvalidOperationException("Only recipe scales have recipe files.");
            }
            m_recipeFiles = true;
            return this;
        }

        public IScaleBuilder AddTotalizer(string name)
        {
            if (Scale is not ContinuousScaleState continuous)
            {
                throw new InvalidOperationException("Only continuous scales have totalizers.");
            }
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A totalizer name is required.", nameof(name));
            }
            TotalizerState totalizer = Context.CreateInstanceOfTotalizerType(continuous, m_services.InstanceName(name));
            totalizer.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            totalizer.DisplayName = new LocalizedText(name);
            totalizer.AddResetTotalizer(Context);
            continuous.AddChild(totalizer);
            return this;
        }

        public IScaleBuilder AddFeederModule(string name, Action<IScaleModuleBuilder>? configure = null)
        {
            ConfigurableObjectState subDevices = EnsureSubDevices();
            FeederModuleState feeder = Context.CreateInstanceOfFeederModuleType(subDevices, m_services.InstanceName(name));
            ScaleNodes.AddSupportedType(subDevices, m_services.ScalesId(ObjectTypes.FeederModuleType));
            m_modules.Add(AddModule(subDevices, feeder, name, configure));
            return this;
        }

        public IScaleBuilder AddPrinterModule(string name, Action<IScaleModuleBuilder>? configure = null)
        {
            ConfigurableObjectState subDevices = EnsureSubDevices();
            PrinterModuleState printer = Context.CreateInstanceOfPrinterModuleType(subDevices, m_services.InstanceName(name));
            ScaleNodes.AddSupportedType(subDevices, m_services.ScalesId(ObjectTypes.PrinterModuleType));
            m_modules.Add(AddModule(subDevices, printer, name, configure));
            return this;
        }

        public IScaleBuilder AddWeighingModule(string name, Action<IScaleBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            ConfigurableObjectState subDevices = EnsureSubDevices();
            ScaleDeviceState module = ScaleNodes.CreateScale(
                Context,
                ScaleKind.WeighingModule,
                subDevices,
                m_services.InstanceName(name));
            module.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            module.DisplayName = new LocalizedText(name);
            ScaleNodes.AddSupportedType(subDevices, m_services.ScalesId(ObjectTypes.WeighingModuleType));
            var builder = new ScaleBuilder(m_services, module, ScaleKind.WeighingModule);
            configure(builder);
            builder.Finish();
            subDevices.AddChild(module);
            m_weighingModules.Add(builder);
            return this;
        }

        public IScaleBuilder With<TState>(Action<ISystemContext, TState> configure)
            where TState : ScaleDeviceState
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            if (Scale is not TState typed)
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "The scale is a {0}, not a {1}.",
                        Scale.GetType().Name,
                        typeof(TState).Name));
            }
            configure(Context, typed);
            return this;
        }

        /// <summary>
        /// Completes the mandatory members and checks the configuration.
        /// Called once, before the scale is registered.
        /// </summary>
        public void Finish()
        {
            if (!m_identified)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Scale '{0}' has no identification; call WithIdentification (OPC 40200 §7.4.2).",
                    Scale.BrowseName.Name ?? string.Empty);
            }
            if (m_ranges.Count == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Scale '{0}' has no weighing range; call WithWeighingRange (OPC 40200 §7.4.2).",
                    Scale.BrowseName.Name ?? string.Empty);
            }
            m_ranges.Sort((a, b) => a.High.CompareTo(b.High));
            for (int ii = 0; ii < m_ranges.Count; ii++)
            {
                AddWeighingRangeNode(ii + 1, m_ranges[ii]);
            }

            var euRange = new Range { Low = m_ranges.Min(r => r.Low), High = m_ranges[m_ranges.Count - 1].High };
            ConfigureWeightItem(Scale.CurrentWeight, euRange);
            if (Scale.RegisteredWeight != null)
            {
                if (m_weightDetails)
                {
                    AddWeightDetails(Scale.RegisteredWeight);
                }
                ConfigureWeightItem(Scale.RegisteredWeight, euRange);
            }
            if (m_minimalWeightSet)
            {
                ScaleValues.SetUnits(Context, Scale.MinimalWeight, m_unit);
            }

            // §7.4.3: a scale whose methods take units publishes the units it
            // accepts.
            bool takesUnits = Scale.SetPresetTare != null ||
                (Scale as PieceCountingScaleState)?.SetReferencePieceWeight != null;
            if (takesUnits && Scale.AllowedEngineeringUnits == null)
            {
                Scale.AddAllowedEngineeringUnits(Context);
                Scale.AllowedEngineeringUnits!.Value = new[] { m_unit }.ToArrayOf();
            }

            m_preset?.Finish();
            foreach (ScaleModuleBuilder module in m_modules)
            {
                module.Finish();
            }
        }

        /// <summary>
        /// Creates the runtime once the scale is registered.
        /// </summary>
        public ScaleHandle CreateHandle()
        {
            var handle = new ScaleHandle(
                m_services,
                Scale,
                Kind,
                new ScaleConfiguration(
                    m_ranges,
                    m_unit,
                    m_legalForTrade,
                    m_preset?.Lockable ?? false,
                    m_preset?.SelectedProducts ?? [],
                    m_recipeFiles));
            foreach (ScaleModuleBuilder module in m_modules)
            {
                handle.AddModule(new ScaleModuleHandle(m_services, module.Module, handle));
            }
            foreach (ScaleBuilder weighingModule in m_weighingModules)
            {
                handle.AddWeighingModule(weighingModule.CreateHandle());
            }
            return handle;
        }

        private void AddWeighingRangeNode(int index, WeighingRangeDefinition range)
        {
            string name = "WeighingRange" + index.ToString(CultureInfo.InvariantCulture);
            WeighingRangeElementState element = Context.CreateInstanceOfWeighingRangeElementType(
                Scale,
                m_services.InstanceName(name));
            element.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            element.DisplayName = new LocalizedText(name);
            EUInformation unit = range.EngineeringUnits ?? m_unit;
            ScaleValues.Set(Context, element.ActualScaleInterval, Variant.From(range.ActualScaleInterval));
            ScaleValues.Set(Context, element.VerificationScaleInterval, Variant.From(range.VerificationScaleInterval));
            ScaleValues.SetUnits(Context, element.ActualScaleInterval, unit);
            ScaleValues.SetUnits(Context, element.VerificationScaleInterval, unit);
            if (element.Range != null)
            {
                element.Range.Value = new Range { Low = range.Low, High = range.High };
                ScaleValues.Touch(Context, element.Range);
                ScaleValues.SetUnits(Context, element.Range, unit);
            }
            Scale.AddChild(element);
        }

        private void ConfigureWeightItem(WeightItemState? item, Range euRange)
        {
            if (item == null)
            {
                return;
            }
            if (item.EngineeringUnits != null)
            {
                item.EngineeringUnits.Value = m_unit;
            }
            if (item.EURange != null)
            {
                item.EURange.Value = euRange;
            }
            if (item.Overload != null)
            {
                item.Overload.Value = false;
            }
            if (item.Underload != null)
            {
                item.Underload.Value = false;
            }
            if (item.TareMode != null)
            {
                item.TareMode.Value = TareMode.None_0;
            }
        }

        private void AddWeightDetails(WeightItemState? item)
        {
            if (item == null)
            {
                return;
            }
            item.AddGross(Context);
            item.AddNet(Context);
            item.AddTare(Context);
            item.AddInsideZero(Context);
            item.AddCenterOfZero(Context);
            item.AddGrossNegative(Context);
            item.AddWeightStable(Context);
            item.AddCurrentRangeId(Context);
            item.AddWeightId(Context);
            item.AddLegalForTrade(Context);
            item.AddHighResolutionValue(Context);
            item.AddPrintableValue(Context);
        }

        private void InitFlags(params PropertyState<bool>?[] flags)
        {
            foreach (PropertyState<bool>? flag in flags)
            {
                ScaleValues.Set(Context, flag, false);
            }
        }

        private ConfigurableObjectState EnsureSubDevices()
        {
            Scale.AddSubDevices(Context);
            return Scale.SubDevices!;
        }

        private ScaleModuleBuilder AddModule(
            ConfigurableObjectState subDevices,
            ComponentState module,
            string name,
            Action<IScaleModuleBuilder>? configure)
        {
            module.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            module.DisplayName = new LocalizedText(name);
            var builder = new ScaleModuleBuilder(m_services, module);
            configure?.Invoke(builder);
            subDevices.AddChild(module);
            return builder;
        }

        internal static void AddStatisticMembers(ISystemContext context, StatisticState statistic)
        {
            statistic.AddStartTime(context);
            statistic.AddResetCondition(context);
            statistic.AddTotalPackages(context);
            statistic.AddTotalPackagesWeighed(context);
            statistic.AddLastItem(context);
            foreach (StatisticCounterState? counter in new[] { statistic.TotalPackages, statistic.TotalPackagesWeighed })
            {
                if (counter == null)
                {
                    continue;
                }
                counter.AddSumWeight(context);
                counter.AddMinValue(context);
                counter.AddMaxValue(context);
                counter.AddMeanValue(context);
                counter.AddStandardDeviation(context);
                ScaleValues.Set(context, counter.ItemCount, Variant.From(0UL));
                ScaleValues.Set(context, counter.Weighed, counter == statistic.TotalPackagesWeighed);
            }
        }

        internal static void AddPackMLMethods(ISystemContext context, PackML.PackMLBaseStateMachineState state)
        {
            state.AddAbort(context);
            state.AddClear(context);
            if (state.MachineState is { } machine)
            {
                machine.AddStop(context);
                machine.AddReset(context);
                if (machine.ExecuteState is { } execute)
                {
                    execute.AddStart(context);
                    execute.AddHold(context);
                    execute.AddUnhold(context);
                    execute.AddSuspend(context);
                    execute.AddUnsuspend(context);
                    execute.AddToComplete(context);
                    execute.AddReset(context);
                }
            }
        }

        private readonly ScaleRuntimeServices m_services;
        private readonly List<WeighingRangeDefinition> m_ranges = [];
        private readonly List<ScaleModuleBuilder> m_modules = [];
        private readonly List<ScaleBuilder> m_weighingModules = [];
        private ProductionPresetBuilder? m_preset;
        private EUInformation m_unit = ScaleUnits.Kilogram;
        private bool m_legalForTrade;
        private bool m_identified;
        private bool m_weightDetails;
        private bool m_minimalWeightSet;
        private bool m_recipeFiles;
    }

    /// <summary>
    /// The default <see cref="IProductionPresetBuilder"/>.
    /// </summary>
    internal sealed class ProductionPresetBuilder : IProductionPresetBuilder
    {
        public ProductionPresetBuilder(ScaleRuntimeServices services, ProductionPresetState preset, uint productTypeId)
        {
            m_services = services;
            m_preset = preset;
            m_productTypeId = productTypeId;
            preset.AddProducts(services.Context);
        }

        public bool Lockable { get; private set; }

        public List<string> SelectedProducts { get; } = [];

        public IProductionPresetBuilder AllowSelection()
        {
            ISystemContext context = m_services.Context;
            m_preset.AddSelectProduct(context);
            m_preset.AddDeselectProduct(context);
            m_preset.AddSwitchProduct(context);
            m_preset.AddCurrentProducts(context);
            m_preset.CurrentProducts!.Value = default;
            return this;
        }

        public IProductionPresetBuilder AllowManagement()
        {
            m_preset.AddAddProduct(m_services.Context);
            m_preset.AddRemoveProduct(m_services.Context);
            return this;
        }

        public IProductionPresetBuilder WithLocking()
        {
            Lockable = true;
            return this;
        }

        public IProductionPresetBuilder AddProduct(
            string productId,
            LocalizedText productName,
            Action<ISystemContext, ProductState>? configure = null)
        {
            if (string.IsNullOrEmpty(productId))
            {
                throw new ArgumentException("A product id is required.", nameof(productId));
            }
            if (m_products.Any(p => p.Id == productId))
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "Product '{0}' is added twice.", productId),
                    nameof(productId));
            }
            m_products.Add((productId, productName, configure));
            return this;
        }

        public IProductionPresetBuilder Select(string productId)
        {
            SelectedProducts.Add(productId ?? throw new ArgumentNullException(nameof(productId)));
            return this;
        }

        public void Finish()
        {
            if (m_products.Count == 0)
            {
                // Products/<Product> is a MandatoryPlaceholder (§7.7.2).
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "A production preset needs at least one product; call AddProduct (OPC 40200 §7.7.2).");
            }
            ISystemContext context = m_services.Context;
            bool selectable = m_preset.SelectProduct != null || m_preset.CurrentProducts != null;
            foreach ((string id, LocalizedText name, Action<ISystemContext, ProductState>? configure) in m_products)
            {
                ProductState product = ScaleProductionPreset.CreateBuiltInProduct(
                    context,
                    m_productTypeId,
                    m_preset.Products!,
                    m_services.InstanceName(id))
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Product type {0} cannot be instantiated.",
                        m_productTypeId);
                product.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
                product.DisplayName = new LocalizedText(id);
                ScaleProductionPreset.InitializeProduct(context, product, id, name, selectable, Lockable);
                configure?.Invoke(context, product);
                m_preset.Products!.AddChild(product);
            }
            foreach (string selected in SelectedProducts)
            {
                if (m_products.All(p => p.Id != selected))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Product '{0}' is selected but was never added.",
                        selected);
                }
            }
        }

        private readonly ScaleRuntimeServices m_services;
        private readonly ProductionPresetState m_preset;
        private readonly uint m_productTypeId;
        private readonly List<(string Id, LocalizedText Name, Action<ISystemContext, ProductState>? Configure)> m_products = [];
    }

    /// <summary>
    /// The default <see cref="IScaleModuleBuilder"/>.
    /// </summary>
    internal sealed class ScaleModuleBuilder : IScaleModuleBuilder
    {
        public ScaleModuleBuilder(ScaleRuntimeServices services, ComponentState module)
        {
            m_services = services;
            Module = module;
        }

        public ComponentState Module { get; }

        private ISystemContext Context => m_services.Context;

        public IScaleModuleBuilder WithIdentification(ScaleIdentification identification)
        {
            if (identification == null)
            {
                throw new ArgumentNullException(nameof(identification));
            }
            if (Module.Identification is not MachineryItemIdentificationState target)
            {
                throw new InvalidOperationException("The module has no Machinery identification.");
            }
            // The model types the identification with the abstract
            // MachineryItemIdentificationType; an instance needs a concrete
            // type definition (OPC 40001-1 recommends the component one).
            target.TypeDefinitionId = new NodeId(
                Opc.Ua.Machinery.ObjectTypes.MachineryComponentIdentificationType,
                m_services.Namespaces.Machinery);
            ScaleNodes.WriteIdentification(Context, target, identification, requireProductInstanceUri: false);
            ScaleNodes.MirrorNameplate(Context, Module, identification);
            m_identified = true;
            return this;
        }

        public IScaleModuleBuilder WithMachineryBuildingBlocks()
        {
            switch (Module)
            {
                case FeederModuleState feeder:
                    feeder.AddMachineryItemState(Context);
                    feeder.AddMachineryOperationMode(Context);
                    feeder.AddMachineryBuildingBlocks(Context);
                    ScaleNodes.WireBuildingBlocks(
                        Context,
                        feeder.MachineryBuildingBlocks,
                        feeder.MachineryItemState,
                        feeder.MachineryOperationMode);
                    break;
                case PrinterModuleState printer:
                    printer.AddMachineryItemState(Context);
                    printer.AddMachineryOperationMode(Context);
                    printer.AddMachineryBuildingBlocks(Context);
                    ScaleNodes.WireBuildingBlocks(
                        Context,
                        printer.MachineryBuildingBlocks,
                        printer.MachineryItemState,
                        printer.MachineryOperationMode);
                    break;
            }
            return this;
        }

        public IScaleModuleBuilder WithFeederSpeed(double minimum, double maximum, EUInformation unit, double initial)
        {
            if (Module is not FeederModuleState feeder)
            {
                throw new InvalidOperationException("Only a feeder module has a feeder speed.");
            }
            if (!(minimum <= initial && initial <= maximum))
            {
                throw new ArgumentOutOfRangeException(nameof(initial), initial, "The initial speed must be within the limits.");
            }
            feeder.AddMinimalFeederSpeed(Context);
            feeder.AddMaximumFeederSpeed(Context);
            feeder.AddFeederSpeed(Context);
            feeder.AddFeederRunning(Context);
            feeder.AddFeederLoad(Context);
            feeder.AddSetFeederSpeed(Context);
            ScaleValues.Set(Context, feeder.MinimalFeederSpeed, Variant.From(minimum));
            ScaleValues.Set(Context, feeder.MaximumFeederSpeed, Variant.From(maximum));
            ScaleValues.SetUnits(Context, feeder.MinimalFeederSpeed, unit);
            ScaleValues.SetUnits(Context, feeder.MaximumFeederSpeed, unit);
            ScaleValues.Set(Context, feeder.FeederSpeed, Variant.From(initial));
            ScaleValues.SetUnits(Context, feeder.FeederSpeed, unit);
            ScaleValues.Set(Context, feeder.FeederRunning, false);
            return this;
        }

        public IScaleModuleBuilder WithLabel(string labelTypeId, double labelLength, double labelWidth, EUInformation lengthUnit)
        {
            if (Module is not PrinterModuleState printer)
            {
                throw new InvalidOperationException("Only a printer module has labels.");
            }
            printer.AddLabelTypeId(Context);
            printer.AddLabelLength(Context);
            printer.AddLabelWidth(Context);
            printer.AddLabelStock(Context);
            printer.AddPrintMediaStock(Context);
            ScaleValues.Set(Context, printer.LabelTypeId, Variant.From(labelTypeId));
            ScaleValues.Set(Context, printer.LabelLength, Variant.From(labelLength));
            ScaleValues.Set(Context, printer.LabelWidth, Variant.From(labelWidth));
            ScaleValues.SetUnits(Context, printer.LabelLength, lengthUnit);
            ScaleValues.SetUnits(Context, printer.LabelWidth, lengthUnit);
            ScaleValues.Set(Context, printer.LabelStock, Variant.From(100.0));
            ScaleValues.Set(Context, printer.PrintMediaStock, Variant.From(100.0));
            ScaleValues.SetUnits(Context, printer.LabelStock, ScaleUnits.Percent);
            ScaleValues.SetUnits(Context, printer.PrintMediaStock, ScaleUnits.Percent);
            return this;
        }

        public IScaleModuleBuilder With(Action<ISystemContext, ComponentState> configure)
        {
            (configure ?? throw new ArgumentNullException(nameof(configure)))(Context, Module);
            return this;
        }

        public void Finish()
        {
            if (!m_identified)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Module '{0}' has no identification; call WithIdentification (OPC 40200 §7.1, §7.2).",
                    Module.BrowseName.Name ?? string.Empty);
            }
        }

        private readonly ScaleRuntimeServices m_services;
        private bool m_identified;
    }

    /// <summary>
    /// The default <see cref="IScaleSystemBuilder"/>.
    /// </summary>
    internal sealed class ScaleSystemBuilder : IScaleSystemBuilder
    {
        public ScaleSystemBuilder(ScaleRuntimeServices services, ScaleSystemState system)
        {
            m_services = services;
            System = system;
        }

        public ScaleSystemState System { get; }

        private ISystemContext Context => m_services.Context;

        public IScaleSystemBuilder WithIdentification(ScaleIdentification identification)
        {
            if (identification == null)
            {
                throw new ArgumentNullException(nameof(identification));
            }
            if (System.Identification is not MachineryItemIdentificationState target)
            {
                throw new InvalidOperationException("The scale system has no Machinery identification add-in.");
            }
            ScaleNodes.WriteIdentification(Context, target, identification, requireProductInstanceUri: true);
            ScaleNodes.MirrorNameplate(Context, System, identification);
            m_identified = true;
            return this;
        }

        public IScaleSystemBuilder WithProcessState(string? processStateId, LocalizedText processStateMessage)
        {
            if (processStateId != null)
            {
                System.AddProcessStateId(Context);
                ScaleValues.Set(Context, System.ProcessStateId, processStateId);
            }
            ScaleValues.Set(Context, System.ProcessStateMessage, processStateMessage);
            m_processState = true;
            return this;
        }

        public IScaleSystemBuilder WithProductionPreset(Action<IProductionPresetBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            System.AddProductionPreset(Context);
            m_preset ??= new ProductionPresetBuilder(m_services, System.ProductionPreset!, ObjectTypes.SimpleProductType);
            configure(m_preset);
            return this;
        }

        public IScaleSystemBuilder WithProductionOutput()
        {
            System.AddProductionOutput(Context);
            ScaleBuilder.AddStatisticMembers(Context, System.ProductionOutput!);
            System.AddResetGlobalStatistics(Context);
            return this;
        }

        public IScaleSystemBuilder WithPackMLState()
        {
            System.AddSystemState(Context);
            ScaleBuilder.AddPackMLMethods(Context, System.SystemState!);
            return this;
        }

        public IScaleSystemBuilder WithMachineryBuildingBlocks()
        {
            System.AddMachineryItemState(Context);
            System.AddMachineryOperationMode(Context);
            System.AddMachineryBuildingBlocks(Context);
            ScaleNodes.WireBuildingBlocks(
                Context,
                System.MachineryBuildingBlocks,
                System.MachineryItemState,
                System.MachineryOperationMode);
            return this;
        }

        public IScaleSystemBuilder AddScale(string name, ScaleKind kind, Action<IScaleBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            System.AddSubDevices(Context);
            ConfigurableObjectState subDevices = System.SubDevices!;
            ScaleDeviceState scale = ScaleNodes.CreateScale(Context, kind, subDevices, m_services.InstanceName(name));
            scale.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            scale.DisplayName = new LocalizedText(name);
            ScaleNodes.AddSupportedType(subDevices, m_services.ScalesId(ScalesModel.ObjectTypeOf(kind)));
            var builder = new ScaleBuilder(m_services, scale, kind);
            configure(builder);
            builder.Finish();
            subDevices.AddChild(scale);
            m_scales.Add(builder);
            return this;
        }

        public void Finish()
        {
            if (!m_identified)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Scale system '{0}' has no identification; call WithIdentification (OPC 40200 §7.3.2).",
                    System.BrowseName.Name ?? string.Empty);
            }
            if (!m_processState)
            {
                // ProcessStateMessage is mandatory on ScaleSystemType.
                ScaleValues.Set(Context, System.ProcessStateMessage, new LocalizedText(string.Empty));
            }
            m_preset?.Finish();
        }

        public ScaleSystemHandle CreateHandle()
        {
            var handle = new ScaleSystemHandle(m_services, System);
            if (handle.ProductionPreset != null && m_preset != null)
            {
                handle.ProductionPreset.Lockable = m_preset.Lockable;
                foreach (string productId in m_preset.SelectedProducts)
                {
                    handle.ProductionPreset.Select(productId);
                }
            }
            foreach (ScaleBuilder scale in m_scales)
            {
                handle.AddScale(scale.CreateHandle());
            }
            return handle;
        }

        private readonly ScaleRuntimeServices m_services;
        private readonly List<ScaleBuilder> m_scales = [];
        private ProductionPresetBuilder? m_preset;
        private bool m_identified;
        private bool m_processState;
    }
}
