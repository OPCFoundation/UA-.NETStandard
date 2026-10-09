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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Di;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Machinery.Server.StateMachines;

namespace MachinerySample
{
    /// <summary>
    /// Builds the sample machine. Every OPC 40001 part appears exactly once, so
    /// the file doubles as a worked example of the fluent builder surface.
    /// </summary>
    internal sealed class PressConfigurator : IMachineryConfigurator
    {
        public PressConfigurator(PressSimulation simulation)
        {
            m_simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        }

        public async ValueTask ConfigureAsync(
            IMachineryBuildContext context,
            CancellationToken cancellationToken)
        {
            IMachineHandle<BaseObjectState> press = await context
                .AddMachine(new QualifiedName("HydraPress-1"))

                // OPC 40001-1: the identification add-in. Manufacturer and
                // SerialNumber are mandatory on every machinery item, and a
                // machine adds ProductInstanceUri on top.
                .WithIdentification(id =>
                {
                    id.Manufacturer = new LocalizedText(PressDatasheet.Manufacturer);
                    id.ManufacturerUri = PressDatasheet.ManufacturerUri;
                    id.Model = new LocalizedText(PressDatasheet.Model);
                    id.ProductCode = PressDatasheet.ProductCode;
                    id.SerialNumber = PressDatasheet.SerialNumber;
                    id.ProductInstanceUri = PressDatasheet.ProductInstanceUri;
                    id.DeviceClass = PressDatasheet.DeviceClass;
                    id.HardwareRevision = PressDatasheet.HardwareRevision;
                    id.SoftwareRevision = PressDatasheet.SoftwareRevision;
                    id.Location = PressDatasheet.Location;
                    id.YearOfConstruction = PressDatasheet.YearOfConstruction;
                    id.MonthOfConstruction = PressDatasheet.MonthOfConstruction;
                })

                // OPC 40001-1: the monitoring add-in. Neither state machine
                // declares a cause method, so both are driven from the
                // simulation below and never by a client.
                .WithMonitoring(monitoring => monitoring
                    .WithMachineryItemState(MachineryItemStateValue.NotExecuting)
                    .WithOperationMode(MachineryOperationModeValue.None)
                    .WithHealth(health => health
                        .WithDeviceHealth(DeviceHealthEnumeration.NORMAL))
                    .WithProcess())

                .WithComponents(components => components
                    .AddComponent(
                        new QualifiedName("HydraulicUnit"),
                        component => component
                            .WithIdentification(id =>
                            {
                                id.Manufacturer =
                                    new LocalizedText(PressDatasheet.Manufacturer);
                                id.SerialNumber = "HU-2024-0114";
                                id.Model = new LocalizedText("HydraPack 250");
                                id.DeviceRevision = "B";
                            })
                            .WithLifetimeCounters(counters => counters.AddLifetimeVariable(
                                new QualifiedName("SealLife"),
                                PressDatasheet.SealLifetimeCycles,
                                PressDatasheet.SealRemainingCycles,
                                new[] { PressDatasheet.SealWarningCycles }.ToArrayOf()))))

                .WithMachineryEquipment(equipment => equipment.AddEquipment(
                    new QualifiedName("UpperDie"),
                    "urn:acme-machine-tools.example:equipment:die",
                    die => die
                        .WithDescription(new LocalizedText("Upper forming die, 300 x 200 mm"))
                        .WithIdentification(id =>
                        {
                            id.Manufacturer = new LocalizedText("Acme Tooling");
                            id.SerialNumber = "DIE-77120";
                        })

                        // EquipmentLife comes from IMachineryEquipmentType, an
                        // interface, so the placeholder factory never creates
                        // it; the builder does.
                        .WithEquipmentLife(
                            remaining: PressDatasheet.DieRemainingCycles,
                            startValue: PressDatasheet.DieLifetimeCycles,
                            warningValues:
                                new[] { PressDatasheet.DieWarningCycles }.ToArrayOf())))

                // The Notifications add-in is where a machine puts its own
                // events. The publisher is what the simulation reports the
                // stroke event on.
                .WithNotifications(notifications =>
                    notifications.Bind(out m_notifications))

                .WithOperationCounters(counters => counters
                    .WithPowerOnDuration(PressDatasheet.PowerOnDurationHours)
                    .WithOperationDuration(PressDatasheet.OperationDurationHours))

                // OPC 40001-2: a process value over the PADIM analog-signal
                // model, with the limits and the alarms the part adds.
                .WithProcessValue(
                    new QualifiedName("HydraulicOilTemperature"),
                    processValue => processValue
                        .WithEngineeringUnits(
                            DegreeCelsius(),
                            new Opc.Ua.Range
                            {
                                Low = PressDatasheet.OilTemperatureRangeLow,
                                High = PressDatasheet.OilTemperatureRangeHigh
                            })
                        .WithLimits(
                            lowLow: PressDatasheet.OilTemperatureLowLowLimit,
                            low: PressDatasheet.OilTemperatureLowLimit,
                            high: PressDatasheet.OilTemperatureHighLimit,
                            highHigh: PressDatasheet.OilTemperatureHighHighLimit)
                        .WithSetpoint(PressDatasheet.OilTemperatureSetpoint)
                        .WithDeviationLimits(
                            lowLow: -20,
                            low: -10,
                            high: 10,
                            highHigh: 20,
                            autoAdjustment: false,
                            sensitivity: 5)
                        .WithPercentageValue()
                        .WithStatus()

                        // Both alarms take their limits from the two calls
                        // above and follow every SetValueAsync from here on.
                        .WithLimitAlarm()
                        .WithDeviationAlarm()

                        // OPC 40001-2 requires every instance publishing the
                        // method to report the event, which the builder does;
                        // the handler only says how it went.
                        .WithZeroPointAdjustment((_, _) =>
                            new ValueTask<StatusCode>(StatusCodes.Good))
                        .WithValue(PressDatasheet.OilTemperatureSetpoint)
                        .Bind(out m_oilTemperature))

                // OPC 40001-3: job management. The eleven job verbs come from
                // the ISA-95 Job Control V2 model underneath, bound to the
                // in-memory provider this sample registers.
                .WithJobManagement(jobs => jobs
                    .WithJobOrderReceiver(m_simulation.JobProvider)
                    .WithJobResponseProvider(m_simulation.JobProvider)
                    .WithJobOrderCatalog(m_simulation.JobProvider))

                // OPC 40001-4: the compressed-air supply, tied to the
                // well-known carrier with the Energy model's Contains
                // reference.
                // OPC 40001-4: the resources sit below Monitoring/Consumption,
                // each with a Main metering point of the OPC 34100
                // EnergyMeasurementType. WithEnergy materialises the monitoring
                // add-in and the Consumption folder if they are not there yet.
                .WithEnergy(energy => energy
                    .AddResource(MachineryEnergyCarrier.CompressedAir, air =>
                    {
                        air.Main
                            .WithApplicationTag("HydraPress-1/CompressedAir")
                            .WithNonElectricalEnergy(
                                importHighPrecision: 0,
                                exportHighPrecision: 0)
                            .WithVolumeFlow(
                                PressDatasheet.CompressedAirVolume,
                                PressDatasheet.CompressedAirVolumeFlowRate)
                            .WithBaseFlow(
                                PressDatasheet.CompressedAirPressurePascal,
                                PressDatasheet.CompressedAirTemperatureKelvin);

                        // A sub-meter whose consumption is part of Main's, which
                        // is exactly what the Contains reference states.
                        air.AddMeteringPoint(
                            new QualifiedName("ClampingCylinders"),
                            point => point
                                .WithApplicationTag("HydraPress-1/CompressedAir/Clamping")
                                .WithNonElectricalEnergy(
                                    importHighPrecision: 0,
                                    exportHighPrecision: 0));
                    })

                    // Electricity is the one resource OPC 40001-4 does not
                    // describe with interfaces of its own — it defers to
                    // OPC 34100's energy profiles, so the metering point takes
                    // one of those.
                    .AddResource(MachineryEnergyCarrier.Electricity, electricity =>
                        electricity.Main
                            .WithApplicationTag("HydraPress-1/Electricity")
                            .WithInterface(NodeId.Create(
                                Opc.Ua.ECM.ObjectTypes.IEnergyProfileE1Type,
                                Opc.Ua.ECM.Namespaces.ECM,
                                context.Context.NamespaceUris))
                            .AddMeasurementValue(
                                new QualifiedName(Opc.Ua.ECM.BrowseNames.AcActivePowerTotal),
                                Variant.From(PressDatasheet.ElectricalActivePowerTotal),
                                Watt())))

                // OPC 40001-101: results, including the GenerateFileForRead
                // download path.
                .WithResultManagement(results => results
                    .WithInMemoryStore(capacity: 32)
                    .WithResultsFolder(publishedResults: 4)
                    .WithFileTransfer())

                .BuildAsync(cancellationToken)
                .ConfigureAwait(false);

            m_simulation.Attach(press, m_oilTemperature!, m_notifications!);
        }

        private static EUInformation Watt()
        {
            // UNECE Recommendation 20 code for watt.
            return new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = 5720150,
                DisplayName = new LocalizedText("W"),
                Description = new LocalizedText("watt")
            };
        }

        private static EUInformation DegreeCelsius()
        {
            // UNECE Recommendation 20 code for degree Celsius.
            return new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = 4408652,
                DisplayName = new LocalizedText("°C"),
                Description = new LocalizedText("degree Celsius")
            };
        }

        private readonly PressSimulation m_simulation;
        private IProcessValueHandle? m_oilTemperature;
        private IMachineryNotificationPublisher? m_notifications;
    }
}
