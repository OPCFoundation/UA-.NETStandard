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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Pumps;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Builders;
using Opc.Ua.Pumps.Server.Hosting;
using static PumpsSample.PumpDatasheet;
using BrowseNames = Opc.Ua.Pumps.BrowseNames;
using Range = Opc.Ua.Range;

namespace PumpsSample
{
    /// <summary>
    /// Creates the two sample pumps and keeps the handles the simulation
    /// writes through.
    /// </summary>
    /// <remarks>
    /// Every group and value below is optional in OPC 40223, and the builder
    /// materialises only what is set here. A client browsing the server finds
    /// these values and nothing else of the ~3000-node <c>PumpType</c>
    /// surface.
    /// </remarks>
    internal sealed class PumpFleet
    {
        /// <summary>
        /// The pumps, once <see cref="ConfigureAsync"/> ran.
        /// </summary>
        public IReadOnlyList<PumpUnit> Pumps => Volatile.Read(ref m_units);

        public async ValueTask ConfigureAsync(IPumpsSetupContext context)
        {
            PumpsNodeManager manager = context.Manager;
            PumpUnit duty = await CreatePumpAsync(
                manager,
                DutyPump,
                serialNumber: "CW80-0042",
                redundantPump: StandbyPump,
                context.CancellationToken).ConfigureAwait(false);
            PumpUnit standby = await CreatePumpAsync(
                manager,
                StandbyPump,
                serialNumber: "CW80-0043",
                redundantPump: DutyPump,
                context.CancellationToken).ConfigureAwait(false);

            Volatile.Write(ref m_units, [duty, standby]);
        }

        private static async ValueTask<PumpUnit> CreatePumpAsync(
            PumpsNodeManager manager,
            string name,
            string serialNumber,
            string redundantPump,
            CancellationToken cancellationToken)
        {
            // CreatePumpAsync places the pump below the DI DeviceSet and, by
            // default, organizes it into the Machinery Machines folder too.
            IPumpBuilder pump = await manager.CreatePumpAsync(
                new QualifiedName(name, manager.InstanceNamespaceIndex),
                cancellationToken).ConfigureAwait(false);

            // The nameplate spans three namespaces (DI, Machinery, Pumps);
            // WithNameplate puts every field where OPC 40223 declares it.
            pump.WithNameplate(new PumpNameplate
            {
                NodeId = NodeId.Null,
                Manufacturer = new LocalizedText("en", Manufacturer),
                ManufacturerUri = ManufacturerUri,
                Model = new LocalizedText("en", Model),
                ProductCode = "CW-80-11",
                SerialNumber = serialNumber,
                HardwareRevision = "B",
                SoftwareRevision = "2.4.1",
                DeviceClass = "Centrifugal pump",
                AssetId = name,
                ComponentName = new LocalizedText("en", "Cooling water pump"),
                Location = Location,
                YearOfConstruction = 2024,
                MonthOfConstruction = 3,
                ArticleNumber = "4711-CW80",
                TypeOfProduct = "Single-stage volute casing pump",
                CountryOfOrigin = "DE"
            });

            // Configuration: what the pump is built for (Design), how it was
            // installed (Implementation) and what the plant asks of it
            // (SystemRequirements). Static, so set once.
            pump.Design
                .SetAnalog(
                    BrowseNames.MaximumAllowableWorkingPressure,
                    MaximumAllowableWorkingPressure,
                    Units.Pascal)
                .SetAnalog(
                    BrowseNames.MaximumAllowableContinuousSpeed,
                    MaximumAllowableContinuousSpeed,
                    Units.RevolutionsPerMinute)
                .Set(BrowseNames.ClockwiseRotation, Variant.From(true));
            pump.Implementation
                .SetAnalog(BrowseNames.RatedFlow, RatedMassFlow, Units.KilogramPerSecond)
                .SetAnalog(BrowseNames.RatedSpeed, RatedSpeed, Units.RevolutionsPerMinute)
                .SetAnalog(BrowseNames.PumpBestEfficiency, BestEfficiency, Units.Percent);
            pump.SystemRequirements
                .Set(BrowseNames.Fluid, Variant.From("Cooling water"))
                .SetAnalog(BrowseNames.MaximumFlow, MaximumFlow, Units.KilogramPerSecond)
                .SetAnalog(BrowseNames.MinimumFlow, MinimumFlow, Units.KilogramPerSecond);

            // Measurements: the live values. Units and instrument ranges are
            // published once; the simulation only updates the readings.
            IPumpGroupBuilder measurements = pump.Measurements
                .SetAnalog(BrowseNames.MassFlow, 0, Units.KilogramPerSecond,
                    new Range { Low = 0, High = MaximumFlow })
                .SetAnalog(BrowseNames.DifferentialPressure, 0, Units.Pascal,
                    new Range { Low = 0, High = MaximumAllowableWorkingPressure })
                .SetAnalog(BrowseNames.Speed, 0, Units.RevolutionsPerMinute,
                    new Range { Low = 0, High = MaximumAllowableContinuousSpeed })
                .SetAnalog(BrowseNames.PumpPowerInput, 0, Units.Kilowatt)
                .SetAnalog(BrowseNames.PumpEfficiency, 0, Units.Percent,
                    new Range { Low = 0, High = 100 })
                .SetAnalog(BrowseNames.BearingTemperature, 25, Units.DegreeCelsius)
                .Set(BrowseNames.NumberOfStarts, Variant.From(0u));

            // Vibration measurements are the OptionalPlaceholder <Vibration> of
            // MeasurementsType (OPC 40223 §7.32): one named instance of
            // VibrationMeasurementType per measuring point.
            IPumpGroupBuilder vibration = measurements.AddVibration("DriveEndBearing")
                .SetAnalog(BrowseNames.OverallVibrationVelocityRMS, 0, Units.MillimetrePerSecond,
                    new Range { Low = 0, High = 11.2 })
                .Set(BrowseNames.ReferenceStandardForVibrationMeasurement, Variant.From("ISO 10816-3"));

            // Signals are DiscreteInputObjectType objects; SetDiscrete writes
            // their DiscreteInputValue one level down.
            IPumpGroupBuilder signals = pump.Signals
                .SetDiscrete(BrowseNames.PumpOperation, false)
                .SetDiscrete(BrowseNames.StandBy, true);

            // Supervision: the Events group. Only the signals this pump can
            // actually detect are published.
            IPumpGroupBuilder processFluid = pump.Supervision(BrowseNames.SupervisionProcessFluid)
                .SetDiscrete(BrowseNames.Cavitation, false)
                .SetDiscrete(BrowseNames.Dry, false);
            IPumpGroupBuilder mechanics = pump.Supervision(BrowseNames.SupervisionMechanics)
                .SetDiscrete(BrowseNames.ExcessVibration, false)
                .SetDiscrete(BrowseNames.BearingFault, false);
            pump.Supervision(BrowseNames.SupervisionPumpOperation)
                .SetDiscrete(BrowseNames.MotorOverheat, false);

            // MultiPump describes this pump's role in the duty/standby pair.
            IPumpGroupBuilder multiPump = pump.MultiPump
                .Set(BrowseNames.PumpRole, Variant.From(PumpRoleEnum.Slave))
                .Set(
                    BrowseNames.MultiPumpOperationMode,
                    Variant.From(MultiPumpOperationModeEnum.RedundancyOperation))
                .Set(
                    BrowseNames.DistributionType,
                    Variant.From(DistributionTypeEnum.ConcerningTimeDistribution))
                .Set(BrowseNames.ExchangeMode, Variant.From(ExchangeModeEnum.ManufacturerSpecific))
                .Set(BrowseNames.NumberOfPumps, Variant.From(2u))
                .Set(BrowseNames.MaximumNumberOfPumpsInOperation, Variant.From(1u))
                .Set(BrowseNames.RedundantPumpIDs, Variant.From(redundantPump));

            // Maintenance: state and counters in GeneralMaintenance, failure
            // reporting in BreakdownMaintenance.
            IPumpGroupBuilder general = pump.MaintenanceCategory(BrowseNames.GeneralMaintenance)
                .Set(BrowseNames.StateOfTheItem, Variant.From(StateOfTheItemEnum.StandByState))
                .Set(BrowseNames.MaintenanceLevel, Variant.From(MaintenanceLevelEnum.Level1))
                .SetAnalog(BrowseNames.OperatingTime, InitialOperatingHours, Units.Hour);
            pump.MaintenanceCategory(BrowseNames.BreakdownMaintenance)
                .Set(BrowseNames.Failure, Variant.From(false))
                .Set(BrowseNames.NumberOfFailures, Variant.From(0));

            // Ports are placeholders in PortsGroupType; AddPort names each one.
            pump.AddPort(PumpPortKind.InletConnection, "Suction")
                .Set(BrowseNames.Direction, Variant.From(PortDirectionEnum.In))
                .Set(BrowseNames.Category, Variant.From("DN100 PN16 flange"));
            pump.AddPort(PumpPortKind.OutletConnection, "Discharge")
                .Set(BrowseNames.Direction, Variant.From(PortDirectionEnum.Out))
                .Set(BrowseNames.Category, Variant.From("DN80 PN16 flange"));
            IPumpGroupBuilder motor = pump.AddPort(PumpPortKind.Drive, "Motor")
                .Nested(BrowseNames.Measurements)
                .SetAnalog(BrowseNames.MotorCurrent, 0, Units.Ampere)
                .SetAnalog(BrowseNames.MotorTemperature, 25, Units.DegreeCelsius);

            // Documentation: the links only. Serving the files themselves
            // would mean implementing the FileType methods.
            pump.Documentation
                .Set(BrowseNames.OperationManualLink, Variant.From(OperationManual))
                .Set(BrowseNames.TechnicalDataLink, Variant.From(TechnicalData));

            return new PumpUnit(
                name,
                measurements,
                vibration,
                signals,
                processFluid,
                mechanics,
                multiPump,
                general,
                motor);
        }

        private IReadOnlyList<PumpUnit> m_units = [];
    }

    /// <summary>
    /// The groups of one pump the simulation writes.
    /// </summary>
    /// <remarks>
    /// Every write goes through the builder, which stamps the value and
    /// publishes the change, so a client subscribed to any of these values sees
    /// each update. Nothing here has to know how the node manager reports data
    /// changes.
    /// </remarks>
    internal sealed class PumpUnit
    {
        public PumpUnit(
            string name,
            IPumpGroupBuilder measurements,
            IPumpGroupBuilder vibration,
            IPumpGroupBuilder signals,
            IPumpGroupBuilder processFluid,
            IPumpGroupBuilder mechanics,
            IPumpGroupBuilder multiPump,
            IPumpGroupBuilder generalMaintenance,
            IPumpGroupBuilder motor)
        {
            Name = name;
            Measurements = measurements;
            Vibration = vibration;
            Signals = signals;
            ProcessFluid = processFluid;
            Mechanics = mechanics;
            MultiPump = multiPump;
            GeneralMaintenance = generalMaintenance;
            Motor = motor;
        }

        public string Name { get; }
        public IPumpGroupBuilder Measurements { get; }
        public IPumpGroupBuilder Vibration { get; }
        public IPumpGroupBuilder Signals { get; }
        public IPumpGroupBuilder ProcessFluid { get; }
        public IPumpGroupBuilder Mechanics { get; }
        public IPumpGroupBuilder MultiPump { get; }
        public IPumpGroupBuilder GeneralMaintenance { get; }
        public IPumpGroupBuilder Motor { get; }

        /// <summary>
        /// Writes an analog reading, rounded for display.
        /// </summary>
        public void Analog(IPumpGroupBuilder group, string browseName, double value)
        {
            group.SetAnalog(browseName, Math.Round(value, 2));
        }

        /// <summary>
        /// Writes any variable value.
        /// </summary>
        public void Value(IPumpGroupBuilder group, string browseName, Variant value)
        {
            group.Set(browseName, value);
        }

        /// <summary>
        /// Writes a supervision flag or a signal. SetDiscrete handles both
        /// shapes: a TwoStateDiscreteType variable and a discrete object whose
        /// state lives in DiscreteInputValue or DiscreteOutputValue.
        /// </summary>
        public void Discrete(IPumpGroupBuilder group, string browseName, bool value)
        {
            group.SetDiscrete(browseName, value);
        }
    }
}
