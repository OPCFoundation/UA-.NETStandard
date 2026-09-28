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
using Opc.Ua.ECM;
using Opc.Ua.Machinery.Energy;
using EcmBrowseNames = Opc.Ua.ECM.BrowseNames;
using EnergyBrowseNames = Opc.Ua.Machinery.Energy.BrowseNames;
using EnergyObjectTypes = Opc.Ua.Machinery.Energy.ObjectTypes;
using EnergyReferenceTypes = Opc.Ua.Machinery.Energy.ReferenceTypes;

namespace Opc.Ua.Machinery.Server.Builders
{
    /// <summary>
    /// The resources OPC 40001-4 names. Each one is the browse name of a
    /// folder below <c>Monitoring/Consumption</c> that groups the metering
    /// points measuring it.
    /// </summary>
    /// <remarks>
    /// OPC 40001-4 §9.1 calls these "Well-Known <c>FolderType</c> BrowseNames":
    /// they identify the resource by name, not by a reference to the
    /// same-named Object the model carries — that Object exists only to
    /// publish the name.
    /// </remarks>
    public enum MachineryEnergyCarrier
    {
        /// <summary>Electrical energy.</summary>
        Electricity,

        /// <summary>Compressed air.</summary>
        CompressedAir,

        /// <summary>Cooling lubricant.</summary>
        CoolingLubricant,

        /// <summary>Natural gas.</summary>
        NaturalGas,

        /// <summary>Saturated steam.</summary>
        SteamSaturated,

        /// <summary>Superheated steam.</summary>
        SteamSuperheated,

        /// <summary>Chilled water.</summary>
        ChilledWater,

        /// <summary>Hot water.</summary>
        HotWater,

        /// <summary>High-temperature hot water.</summary>
        HotHotWater,

        /// <summary>Crude oil.</summary>
        CrudeOil,

        /// <summary>Fuel oil no. 2.</summary>
        FuelOil2,

        /// <summary>Fuel oil no. 5.</summary>
        FuelOil5,

        /// <summary>Fuel oil no. 6.</summary>
        FuelOil6,

        /// <summary>Diesel oil.</summary>
        DieselOil,

        /// <summary>Gasoline.</summary>
        Gasoline,

        /// <summary>Propane.</summary>
        Propane,

        /// <summary>Biogas.</summary>
        Biogas,

        /// <summary>Hydraulic oil.</summary>
        HydraulicOil
    }

    /// <summary>
    /// Assembles the OPC 40001-4 energy information of a machinery item below
    /// its <c>Monitoring/Consumption</c> folder.
    /// </summary>
    /// <remarks>
    /// OPC 40001-4 is a extension of the OPC 40001-1 <c>Monitoring</c>
    /// building block rather than a block of its own: the resources are
    /// folders under <c>Consumption</c>, each holding one or more OPC 34100
    /// <c>EnergyMeasurementType</c> metering points. That is what the
    /// "Machinery Energy Base Structure" conformance unit asks for, and it is
    /// why nothing here hangs off the machine directly.
    /// </remarks>
    public interface IMachineryEnergyBuilder
    {
        /// <summary>
        /// Gets the <c>Consumption</c> folder the resources are added to.
        /// </summary>
        FolderState State { get; }

        /// <summary>
        /// Adds a resource folder and its mandatory <c>Main</c> metering
        /// point.
        /// </summary>
        /// <remarks>
        /// <c>Main</c> is created unconditionally: OPC 40001-4's "Main
        /// grouping" conformance unit requires one for <em>each</em> resource
        /// folder, so a resource without it would make the server
        /// non-conformant for every other resource too.
        /// </remarks>
        /// <param name="carrier">The resource the folder groups.</param>
        /// <param name="configure">Configures the resource.</param>
        IMachineryEnergyBuilder AddResource(
            MachineryEnergyCarrier carrier,
            Action<IMachineryEnergyResourceBuilder>? configure = null);
    }

    /// <summary>
    /// Assembles the metering points of one OPC 40001-4 resource.
    /// </summary>
    public interface IMachineryEnergyResourceBuilder
    {
        /// <summary>
        /// Gets the resource folder being configured.
        /// </summary>
        FolderState State { get; }

        /// <summary>
        /// Gets the resource this folder groups.
        /// </summary>
        MachineryEnergyCarrier Carrier { get; }

        /// <summary>
        /// Gets the <c>Main</c> metering point of the resource.
        /// </summary>
        IMachineryMeteringPointBuilder Main { get; }

        /// <summary>
        /// Adds a further metering point below the resource.
        /// </summary>
        /// <param name="browseName">The browse name of the metering point.</param>
        /// <param name="configure">Configures the metering point.</param>
        /// <param name="containedInMain">
        /// When <see langword="true"/> (the default), <c>Main</c> gets a
        /// <c>Contains</c> reference to the new point, which is how
        /// OPC 40001-4 says that its measurements are part of the main
        /// reading. Pass <see langword="false"/> for an independent meter.
        /// </param>
        IMachineryEnergyResourceBuilder AddMeteringPoint(
            QualifiedName browseName,
            Action<IMachineryMeteringPointBuilder>? configure = null,
            bool containedInMain = true);
    }

    /// <summary>
    /// Assembles one OPC 34100 <c>EnergyMeasurementType</c> metering point.
    /// </summary>
    public interface IMachineryMeteringPointBuilder
    {
        /// <summary>
        /// Gets the metering point being configured.
        /// </summary>
        EnergyMeasurementState State { get; }

        /// <summary>
        /// Writes the mandatory <c>ApplicationTag</c> — the name the operator
        /// knows the metering point by.
        /// </summary>
        /// <param name="applicationTag">The tag.</param>
        IMachineryMeteringPointBuilder WithApplicationTag(string applicationTag);

        /// <summary>
        /// Writes the optional <c>StartTime</c>, the instant the accumulating
        /// measurements were last reset.
        /// </summary>
        /// <param name="startTime">The start time.</param>
        IMachineryMeteringPointBuilder WithStartTime(DateTime startTime);

        /// <summary>
        /// Implements OPC 40001-4's <c>INonElectricalEnergyType</c> and writes
        /// the two mandatory high-precision energy readings it declares.
        /// </summary>
        /// <param name="importHighPrecision">Consumed energy, in watt hours.</param>
        /// <param name="exportHighPrecision">Produced energy, in watt hours.</param>
        IMachineryMeteringPointBuilder WithNonElectricalEnergy(
            double importHighPrecision,
            double exportHighPrecision);

        /// <summary>
        /// Implements <c>IBaseFlowType</c> and writes its two readings.
        /// </summary>
        /// <param name="pressure">Pressure, in pascal.</param>
        /// <param name="temperature">Temperature, in kelvin.</param>
        IMachineryMeteringPointBuilder WithBaseFlow(float pressure, float temperature);

        /// <summary>
        /// Implements <c>IVolumeFlowType</c> and writes its two readings.
        /// </summary>
        IMachineryMeteringPointBuilder WithVolumeFlow(float volume, float volumeFlowRate);

        /// <summary>
        /// Implements <c>IMassFlowType</c> and writes its two readings.
        /// </summary>
        IMachineryMeteringPointBuilder WithMassFlow(float mass, float massFlowRate);

        /// <summary>
        /// Implements a further OPC UA interface on the metering point — the
        /// OPC 34100 energy profiles for electricity in particular, which
        /// OPC 40001-4 deliberately leaves to that specification instead of
        /// declaring interfaces of its own.
        /// </summary>
        /// <param name="interfaceTypeId">The interface's ObjectType NodeId.</param>
        IMachineryMeteringPointBuilder WithInterface(NodeId interfaceTypeId);

        /// <summary>
        /// Adds an OPC 34100 measurement value the interfaces above do not
        /// cover — the electrical readings in particular, which OPC 40001-4
        /// leaves to OPC 34100's own interfaces.
        /// </summary>
        /// <param name="browseName">
        /// The browse name. A name without a namespace is placed in the
        /// OPC 34100 namespace, where the predefined measurement names live.
        /// </param>
        /// <param name="value">The reading.</param>
        /// <param name="engineeringUnits">Optional engineering unit.</param>
        /// <param name="measurementId">
        /// Optional OPC 34100 <c>MeasurementID</c>, the semantic identifier of
        /// the reading.
        /// </param>
        IMachineryMeteringPointBuilder AddMeasurementValue(
            QualifiedName browseName,
            Variant value,
            EUInformation? engineeringUnits = null,
            ushort? measurementId = null);
    }

    /// <summary>
    /// The browse names of the measurement values OPC 40001-4 declares on its
    /// interfaces.
    /// </summary>
    /// <remarks>
    /// The members live in the Energy model but carry ECM-qualified browse
    /// names, so the generator emits no constant for them in either model's
    /// <c>BrowseNames</c> class. They are spelled out here, and the tests pin
    /// them against the vendored NodeSet.
    /// </remarks>
    internal static class FlowMemberNames
    {
        public const string Pressure = "Pressure";
        public const string Temperature = "Temperature";
        public const string Volume = "Volume";
        public const string VolumeFlowRate = "VolumeFlowRate";
        public const string Mass = "Mass";
        public const string MassFlowRate = "MassFlowRate";
        public const string NeEnergyImportHp = "NeEnergyImportHp";
        public const string NeEnergyExportHp = "NeEnergyExportHp";
    }

    /// <summary>
    /// Implements <see cref="IMachineryEnergyBuilder"/> over the
    /// <c>Monitoring/Consumption</c> folder, adding one OPC 40001-4 resource
    /// folder per <see cref="MachineryEnergyCarrier"/> under its well-known
    /// browse name.
    /// </summary>
    internal sealed class MachineryEnergyBuilder : IMachineryEnergyBuilder
    {
        public MachineryEnergyBuilder(MachineryBuildScope scope, FolderState consumption)
        {
            m_scope = scope;
            State = consumption;
            m_energyNamespaceIndex = MachineryBuilderUtilities.NamespaceIndex(
                scope.Context,
                Opc.Ua.Machinery.Energy.Namespaces.MachineryEnergy);
        }

        public FolderState State { get; }

        public IMachineryEnergyBuilder AddResource(
            MachineryEnergyCarrier carrier,
            Action<IMachineryEnergyResourceBuilder>? configure = null)
        {
            m_scope.EnsureMutable();
            var browseName = new QualifiedName(
                CarrierBrowseName(carrier),
                m_energyNamespaceIndex);
            if (!m_resources.Add(browseName))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated,
                    "The machinery item already publishes the energy resource '{0}'.",
                    browseName);
            }

            FolderState resource = MachineryBuilderUtilities.AddFolderChild(
                m_scope.Context,
                State,
                browseName,
                Opc.Ua.ReferenceTypeIds.HasComponent);
            var builder = new MachineryEnergyResourceBuilder(m_scope, resource, carrier);
            m_scope.RecordFacet(MachineryFacet.EnergyBaseStructure);
            m_scope.RecordFacet(MachineryFacet.EnergyMainGrouping);
            configure?.Invoke(builder);
            return this;
        }

        internal static string CarrierBrowseName(MachineryEnergyCarrier carrier)
        {
            return carrier switch
            {
                MachineryEnergyCarrier.Electricity => EnergyBrowseNames.Electricity,
                MachineryEnergyCarrier.CompressedAir => EnergyBrowseNames.CompressedAir,
                MachineryEnergyCarrier.CoolingLubricant => EnergyBrowseNames.CoolingLubricant,
                MachineryEnergyCarrier.NaturalGas => EnergyBrowseNames.NaturalGas,
                MachineryEnergyCarrier.SteamSaturated => EnergyBrowseNames.Steam_Saturated,
                MachineryEnergyCarrier.SteamSuperheated => EnergyBrowseNames.Steam_Superheated,
                MachineryEnergyCarrier.ChilledWater => EnergyBrowseNames.ChilledWater,
                MachineryEnergyCarrier.HotWater => EnergyBrowseNames.HotWater,
                MachineryEnergyCarrier.HotHotWater => EnergyBrowseNames.HotHotWater,
                MachineryEnergyCarrier.CrudeOil => EnergyBrowseNames.CrudeOil,
                MachineryEnergyCarrier.FuelOil2 => EnergyBrowseNames.FuelOil_2,
                MachineryEnergyCarrier.FuelOil5 => EnergyBrowseNames.FuelOil_5,
                MachineryEnergyCarrier.FuelOil6 => EnergyBrowseNames.FuelOil_6,
                MachineryEnergyCarrier.DieselOil => EnergyBrowseNames.DieselOil,
                MachineryEnergyCarrier.Gasoline => EnergyBrowseNames.Gasoline,
                MachineryEnergyCarrier.Propane => EnergyBrowseNames.Propane,
                MachineryEnergyCarrier.Biogas => EnergyBrowseNames.Biogas,
                MachineryEnergyCarrier.HydraulicOil => EnergyBrowseNames.HydraulicOil,
                _ => throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "'{0}' is not an OPC 40001-4 resource.",
                    carrier)
            };
        }

        private readonly MachineryBuildScope m_scope;
        private readonly ushort m_energyNamespaceIndex;
        private readonly HashSet<QualifiedName> m_resources = [];
    }

    /// <summary>
    /// Implements <see cref="IMachineryEnergyResourceBuilder"/> for one
    /// OPC 40001-4 resource folder: creates its mandatory <c>Main</c> metering
    /// point up front and, unless told otherwise, links every further point to
    /// it with the OPC 40001-4 <c>Contains</c> reference.
    /// </summary>
    internal sealed class MachineryEnergyResourceBuilder : IMachineryEnergyResourceBuilder
    {
        public MachineryEnergyResourceBuilder(
            MachineryBuildScope scope,
            FolderState state,
            MachineryEnergyCarrier carrier)
        {
            m_scope = scope;
            State = state;
            Carrier = carrier;
            m_energyNamespaceIndex = MachineryBuilderUtilities.NamespaceIndex(
                scope.Context,
                Opc.Ua.Machinery.Energy.Namespaces.MachineryEnergy);
            m_main = CreateMeteringPoint(
                new QualifiedName(EnergyBrowseNames.Main, m_energyNamespaceIndex));
        }

        public FolderState State { get; }

        public MachineryEnergyCarrier Carrier { get; }

        public IMachineryMeteringPointBuilder Main => m_main;

        public IMachineryEnergyResourceBuilder AddMeteringPoint(
            QualifiedName browseName,
            Action<IMachineryMeteringPointBuilder>? configure = null,
            bool containedInMain = true)
        {
            m_scope.EnsureMutable();
            QualifiedName name = browseName.NamespaceIndex == 0
                ? new QualifiedName(
                    browseName.Name,
                    m_scope.BuildContext.InstanceNamespaceIndex)
                : browseName;
            if (!m_names.Add(name))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated,
                    "The resource '{0}' already publishes a metering point named '{1}'.",
                    State.BrowseName,
                    name);
            }

            MachineryMeteringPointBuilder point = CreateMeteringPoint(name);
            if (containedInMain)
            {
                // OPC 40001-4 §8.1: Contains runs from the main metering point
                // to a point whose measurements are part of it. Both ends have
                // to be EnergyMeasurementType objects, which is why the
                // reference cannot point at the resource folder.
                var containsId = NodeId.Create(
                    EnergyReferenceTypes.Contains,
                    Opc.Ua.Machinery.Energy.Namespaces.MachineryEnergy,
                    m_scope.Context.NamespaceUris);
                m_main.State.AddReference(containsId, false, point.State.NodeId);
                point.State.AddReference(containsId, true, m_main.State.NodeId);
                m_scope.RecordFacet(MachineryFacet.EnergyContains);
            }
            configure?.Invoke(point);
            return this;
        }

        private MachineryMeteringPointBuilder CreateMeteringPoint(QualifiedName browseName)
        {
            EnergyMeasurementState state = MachineryBuilderUtilities.AddComponentChild(
                m_scope.Context,
                State,
                browseName,
                static (ctx, parent, name) =>
                    ctx.CreateInstanceOfEnergyMeasurementType(parent, name));
            return new MachineryMeteringPointBuilder(m_scope, state);
        }

        private readonly MachineryBuildScope m_scope;
        private readonly MachineryMeteringPointBuilder m_main;
        private readonly ushort m_energyNamespaceIndex;
        private readonly HashSet<QualifiedName> m_names = [];
    }

    /// <summary>
    /// Implements <see cref="IMachineryMeteringPointBuilder"/> over one
    /// OPC 34100 <c>EnergyMeasurementType</c> metering point, adding the
    /// OPC 40001-4 interfaces and the <c>EnergyMeasurementValueType</c>
    /// readings they declare, with the units the specification pins.
    /// </summary>
    internal sealed class MachineryMeteringPointBuilder : IMachineryMeteringPointBuilder
    {
        public MachineryMeteringPointBuilder(
            MachineryBuildScope scope,
            EnergyMeasurementState state)
        {
            m_scope = scope;
            State = state;
            m_ecmNamespaceIndex = MachineryBuilderUtilities.NamespaceIndex(
                scope.Context,
                Opc.Ua.ECM.Namespaces.ECM);
            m_energyNamespaceIndex = MachineryBuilderUtilities.NamespaceIndex(
                scope.Context,
                Opc.Ua.Machinery.Energy.Namespaces.MachineryEnergy);
            m_statisticReferenceId = NodeId.Create(
                Opc.Ua.IA.ReferenceTypes.HasStatisticComponent,
                Opc.Ua.IA.Namespaces.IA,
                scope.Context.NamespaceUris);

            // ApplicationTag is mandatory on EnergyMeasurementType, so the
            // factory created it; give it a usable default rather than null.
            State.ApplicationTag!.Value ??= State.BrowseName.Name ?? string.Empty;
        }

        public EnergyMeasurementState State { get; }

        public IMachineryMeteringPointBuilder WithApplicationTag(string applicationTag)
        {
            m_scope.EnsureMutable();
            State.ApplicationTag!.Value = applicationTag ??
                throw new ArgumentNullException(nameof(applicationTag));
            return this;
        }

        public IMachineryMeteringPointBuilder WithStartTime(DateTime startTime)
        {
            m_scope.EnsureMutable();
            State.AddStartTime(
                m_scope.Context,
                v => v.Value = new DateTimeUtc(startTime.ToUniversalTime()));
            return this;
        }

        public IMachineryMeteringPointBuilder WithNonElectricalEnergy(
            double importHighPrecision,
            double exportHighPrecision)
        {
            AddInterface(EnergyObjectTypes.INonElectricalEnergyType);

            // OPC 40001-4 attaches the two energy readings with the IA
            // HasStatisticComponent reference, not HasComponent — they are
            // accumulating statistics, not instantaneous components.
            AddMeasurement(
                FlowMemberNames.NeEnergyImportHp,
                Variant.From(importHighPrecision),
                Opc.Ua.DataTypeIds.Double,
                m_statisticReferenceId,
                WattHour(),
                measurementId: 2002);
            AddMeasurement(
                FlowMemberNames.NeEnergyExportHp,
                Variant.From(exportHighPrecision),
                Opc.Ua.DataTypeIds.Double,
                m_statisticReferenceId,
                WattHour(),
                measurementId: 2005);
            m_scope.RecordFacet(MachineryFacet.EnergyNonElectrical);
            return this;
        }

        public IMachineryMeteringPointBuilder WithBaseFlow(float pressure, float temperature)
        {
            AddInterface(EnergyObjectTypes.IBaseFlowType);
            AddFlowMeasurement(FlowMemberNames.Pressure, pressure, Pascal(), 28683);
            AddFlowMeasurement(FlowMemberNames.Temperature, temperature, Kelvin(), 28684);
            return this;
        }

        public IMachineryMeteringPointBuilder WithVolumeFlow(float volume, float volumeFlowRate)
        {
            AddInterface(EnergyObjectTypes.IVolumeFlowType);
            AddFlowMeasurement(FlowMemberNames.Volume, volume, null, null);
            AddFlowMeasurement(FlowMemberNames.VolumeFlowRate, volumeFlowRate, null, null);
            m_scope.RecordFacet(MachineryFacet.EnergyVolumeFlow);
            return this;
        }

        public IMachineryMeteringPointBuilder WithMassFlow(float mass, float massFlowRate)
        {
            AddInterface(EnergyObjectTypes.IMassFlowType);
            AddFlowMeasurement(FlowMemberNames.Mass, mass, null, null);
            AddFlowMeasurement(FlowMemberNames.MassFlowRate, massFlowRate, null, null);
            m_scope.RecordFacet(MachineryFacet.EnergyMassFlow);
            return this;
        }

        public IMachineryMeteringPointBuilder WithInterface(NodeId interfaceTypeId)
        {
            m_scope.EnsureMutable();
            if (interfaceTypeId.IsNull)
            {
                throw new ArgumentNullException(nameof(interfaceTypeId));
            }
            if (!State.ReferenceExists(
                    Opc.Ua.ReferenceTypeIds.HasInterface,
                    false,
                    interfaceTypeId))
            {
                State.AddReference(
                    Opc.Ua.ReferenceTypeIds.HasInterface,
                    false,
                    interfaceTypeId);
            }
            return this;
        }

        public IMachineryMeteringPointBuilder AddMeasurementValue(
            QualifiedName browseName,
            Variant value,
            EUInformation? engineeringUnits = null,
            ushort? measurementId = null)
        {
            if (browseName.IsNull || string.IsNullOrEmpty(browseName.Name))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "A measurement value must carry a browse name.");
            }
            AddMeasurement(
                browseName.Name!,
                value,
                DataTypeOf(value),
                Opc.Ua.ReferenceTypeIds.HasComponent,
                engineeringUnits,
                measurementId,
                browseName.NamespaceIndex == 0 ? m_ecmNamespaceIndex : browseName.NamespaceIndex);
            return this;
        }

        private void AddFlowMeasurement(
            string browseName,
            float value,
            EUInformation? engineeringUnits,
            ushort? measurementId)
        {
            AddMeasurement(
                browseName,
                Variant.From(value),
                Opc.Ua.DataTypeIds.Float,
                Opc.Ua.ReferenceTypeIds.HasComponent,
                engineeringUnits,
                measurementId);
        }

        private void AddInterface(uint objectTypeId)
        {
            m_scope.EnsureMutable();
            var interfaceId = new NodeId(objectTypeId, m_energyNamespaceIndex);
            if (!State.ReferenceExists(
                    Opc.Ua.ReferenceTypeIds.HasInterface,
                    false,
                    interfaceId))
            {
                State.AddReference(
                    Opc.Ua.ReferenceTypeIds.HasInterface,
                    false,
                    interfaceId);
            }
        }

        private void AddMeasurement(
            string browseName,
            Variant value,
            NodeId dataTypeId,
            NodeId referenceTypeId,
            EUInformation? engineeringUnits,
            ushort? measurementId,
            ushort? namespaceIndex = null)
        {
            m_scope.EnsureMutable();
            var name = new QualifiedName(
                browseName,
                namespaceIndex ?? m_ecmNamespaceIndex);
            EnergyMeasurementValueState? measurement =
                MachineryBuilderUtilities.FindChild<EnergyMeasurementValueState>(
                    m_scope.Context,
                    State,
                    name);
            if (measurement == null)
            {
                measurement = MachineryBuilderUtilities.AddComponentChild(
                    m_scope.Context,
                    State,
                    name,
                    static (ctx, parent, n) =>
                        ctx.CreateInstanceOfEnergyMeasurementValueType(parent, n));
                measurement.ReferenceTypeId = referenceTypeId;
                measurement.DataType = dataTypeId;
                measurement.ValueRank = ValueRanks.Scalar;
            }
            measurement.WrappedValue = value;
            if (engineeringUnits != null)
            {
                measurement.AddEngineeringUnits(
                    m_scope.Context,
                    v => v.Value = engineeringUnits);
            }
            if (measurementId.HasValue)
            {
                measurement.AddMeasurementID(
                    m_scope.Context,
                    v => v.Value = measurementId.Value);
            }
        }

        private static NodeId DataTypeOf(Variant value)
        {
            return value.TypeInfo.BuiltInType switch
            {
                BuiltInType.Double => Opc.Ua.DataTypeIds.Double,
                BuiltInType.Float => Opc.Ua.DataTypeIds.Float,
                BuiltInType.Int32 => Opc.Ua.DataTypeIds.Int32,
                BuiltInType.UInt32 => Opc.Ua.DataTypeIds.UInt32,
                BuiltInType.Int64 => Opc.Ua.DataTypeIds.Int64,
                BuiltInType.UInt64 => Opc.Ua.DataTypeIds.UInt64,
                _ => Opc.Ua.DataTypeIds.Number
            };
        }

        private static EUInformation WattHour()
        {
            return Unece(5720146, "W·h", "watt hour");
        }

        private static EUInformation Pascal()
        {
            return Unece(5259596, "Pa", "pascal");
        }

        private static EUInformation Kelvin()
        {
            return Unece(4932940, "K", "kelvin");
        }

        /// <summary>
        /// Builds the engineering unit OPC 40001-4 pins for a measurement.
        /// The unit identifiers are the UNECE Recommendation 20 codes the
        /// specification's attribute tables carry verbatim.
        /// </summary>
        private static EUInformation Unece(int unitId, string displayName, string description)
        {
            return new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = unitId,
                DisplayName = new LocalizedText(displayName),
                Description = new LocalizedText(description)
            };
        }

        private readonly MachineryBuildScope m_scope;
        private readonly ushort m_ecmNamespaceIndex;
        private readonly ushort m_energyNamespaceIndex;
        private readonly NodeId m_statisticReferenceId;
    }
}
