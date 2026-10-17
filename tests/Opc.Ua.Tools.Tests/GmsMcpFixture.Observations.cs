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

#if NET10_0
using Opc.Ua.GMS;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Result;
using Opc.Ua.MachineTool;

namespace Opc.Ua.Tools.Tests
{
    internal sealed partial class GmsMcpFixture
    {
        /// <summary>
        /// Creates all native equipment and retained-production observations through public provider contracts.
        /// </summary>
        internal static GmsSystemSnapshot Snapshot()
        {
            var metadata = new MachineToolSampleInfo(Acquired, StatusCodes.Good);
            var program = new MachineToolProgram
            {
                Key = "Program", Name = "Inspect", JobIdentifier = "J-1", Sample = metadata
            };
            var catalog = new GmsCatalogObservation
            {
                Value = Sample(Variant.From((ushort)42)), CatalogName = "Fixture nests", CatalogEntry = "Nest-42",
                EnumValues = [new EnumValueType { Value = 42, DisplayName = new LocalizedText("Nest 42") }],
                ValueAsText = Sample(Variant.From(new LocalizedText("Nest 42")))
            };
            return new GmsSystemSnapshot
            {
                MachineTool = new MachineToolSnapshot
                {
                    Operation = new MachineToolOperation { Sample = metadata, FeedOverride = 75, IsWarmUp = false },
                    ActiveProgram = program,
                    Production = new MachineToolProductionObservation
                    {
                        Jobs =
                        [
                            new MachineToolProductionJob
                            {
                                Key = "Job", Identifier = "J-1", Programs = [program], Sample = metadata,
                                RunsPlanned = 2, RunsPlannedIsValid = true,
                                PartSets =
                                [
                                    new MachineToolProductionPartSet
                                    {
                                        Key = "Set", Name = "Workpieces", PartsPlannedPerRun = 1, Sample = metadata,
                                        Parts =
                                        [
                                            new MachineToolProductionPart
                                            {
                                                Key = "Part", Name = "Workpiece", Identifier = "P-1", Sample = metadata
                                            }
                                        ]
                                    }
                                ]
                            }
                        ],
                        Statistics = new MachineToolProductionStatistics(10, metadata)
                    }
                },
                Identification = new GmsIdentificationObservation
                {
                    SubDeviceClass = Sample(Variant.From("CoordinateMeasuringMachine")),
                    Workspace = Sample(Variant.FromStructure(
                        new CartesianWorkspaceType { Length = 150, Width = 100, Height = 80 }))
                },
                Sensors =
                [
                    new GmsSensor
                    {
                        Tool = new MachineToolTool
                        {
                            Key = "Multi", Kind = MachineToolToolKind.MultiTool, Sample = metadata
                        },
                        Sensors = [Sensor()],
                        Alignment = Sample(Variant.From(ToolAlignmentState.Fixed)),
                        Axes = Sample(Variant.From((ArrayOf<string>)["X", "Y"]))
                    }
                ],
                Loading = new GmsLoadingObservation
                {
                    LoadStatus = Sample(Variant.From(2u)), IsInLoadingPosition = Sample(Variant.From(false))
                },
                ToolMonitoring = new GmsToolMonitoringObservation { ActiveSensorKeys = ["Probe"], Sample = metadata },
                Calibration = new GmsCalibrationObservation
                {
                    Calibrated = Sample(Variant.From(true)),
                    DateOfCalibration = Sample(Variant.From(new DateTimeUtc(2026, 1, 1, 0, 0, 0))),
                    PredictedTime = Sample(Variant.From(new DateTimeUtc(2027, 1, 1, 0, 0, 0))),
                    CalibrationInterval = Sample(Variant.From(31_536_000_000.0)),
                    CalibrationPreptime = Sample(Variant.From(86_400_000.0)),
                    CalibrationCertificate = Sample(Variant.From(
                        (ArrayOf<string>)["urn:test:calibration:certificate-1"]))
                },
                Accessories =
                [
                    new GmsAccessory
                    {
                        Key = "Rotary", Kind = GmsAccessoryKind.RotaryTable,
                        Identification = new GmsItemIdentification(new LocalizedText("Table maker"), "RT-1"),
                        IsIntegrated = Sample(Variant.From(true)), NumberOfAxes = Sample(Variant.From((byte)2))
                    },
                    new GmsAccessory
                    {
                        Key = "Racks", Kind = GmsAccessoryKind.SensorExchangeRack,
                        Identification = new GmsItemIdentification(new LocalizedText("Rack maker"), "SR-1"),
                        IsAvailable = Sample(Variant.From(true))
                    },
                    new GmsAccessory
                    {
                        Key = "Tips", Kind = GmsAccessoryKind.TipExchangeRack,
                        Identification = new GmsItemIdentification(new LocalizedText("Tip maker"), "TR-1"),
                        IsAvailable = Sample(Variant.From(false))
                    }
                ],
                AdditionalSensors =
                [
                    new GmsAdditionalSensor
                    {
                        Key = "Temperature", Identifier = "T-1", Name = "Ambient",
                        Class = Sample(Variant.From((byte)1)),
                        Value = Sample(Variant.From(21.5f), StatusCodes.UncertainSensorNotAccurate),
                        EngineeringUnits = new EUInformation
                        {
                            NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                            UnitId = 4408652, DisplayName = new LocalizedText("°C")
                        }
                    }
                ],
                Jobs =
                [
                    new GmsJobObservation
                    {
                        Key = "Job", BatchIdentifier = Sample(Variant.From("B-2026")),
                        Duration = Sample(Variant.From(1000.0)), RemainingTime = Sample(Variant.From(6000.0)),
                        MeasurementReason = Sample(Variant.From(MeasurementReasonEnum.AuditMeasurement))
                    }
                ],
                Parts =
                [
                    new GmsPartObservation
                    {
                        JobKey = "Job", PartSetKey = "Set", PartKey = "Part",
                        PartDescription = Sample(Variant.From("Inspection workpiece")),
                        PartAmendmentStatus = Sample(Variant.From("A")),
                        ProductionNumber = Sample(Variant.From("PN-1")),
                        NestIdentifier = catalog,
                        Operator = catalog with { Value = Sample(Variant.From(42u)) },
                        PartCarrierIdentifier = catalog with { Value = Sample(Variant.From(42L)) },
                        ProcessingMachineIdentifier = catalog with { Value = Sample(Variant.From(42.0)) },
                        ProcessParameter = catalog with { Value = Sample(Variant.From((ArrayOf<double>)[1, 2.5])) }
                    }
                ]
            };
        }

        /// <summary>
        /// Supplies a primary probe's qualification separately from environmental sensor warnings.
        /// </summary>
        internal static GmsSensor Sensor() => new()
        {
            Tool = new MachineToolTool
            {
                Key = "Probe", Identifier = "probe-7", Name = "Touch probe",
                Control = new MachineToolToolControl(
                    7, ToolManagement.NumberBased, false, ToolLocked.CapabilityUnavailable),
                Sample = new MachineToolSampleInfo(Acquired, StatusCodes.Good)
            },
            Class = Sample(Variant.From((ushort)3)),
            Capabilities = Sample(Variant.From((ArrayOf<ushort>)[1, 4])),
            Alignment = Sample(Variant.From(ToolAlignmentState.Fixed)),
            Axes = Sample(Variant.From((ArrayOf<string>)["X", "Y", "Z"])),
            IsQualifiedStatus = Sample(Variant.From(ToolIsQualifiedStatus.NotQualified)),
            AbsoluteProbe = Sample(Variant.From(true)),
            EngineeringUnit = Sample(Variant.FromStructure(Unit())),
            MeasuringRange = Sample(Variant.From(50.0)),
            WorkingRange = Sample(Variant.From(80.0)),
            Resolution = Sample(Variant.From(0.001)),
            Qualified = new GmsQualifiedLife
            {
                RemainingHours = Sample(Variant.From(-0.25), StatusCodes.UncertainSensorNotAccurate),
                StartHours = Sample(Variant.From(10.0)), WarningHours = Sample(Variant.From(0.5)),
                LimitHours = Sample(Variant.From(0.0))
            }
        };

        /// <summary>
        /// Supplies an environmental additional-sensor warning, never a primary-probe qualification alarm.
        /// </summary>
        private static GmsSensorWarning EnvironmentalWarning() => new()
        {
            Key = "TemperatureWarning", SensorKey = "Temperature", ErrorCode = "ENV42",
            Message = new LocalizedText("Ambient temperature outside measurement range"),
            IsActive = true, Severity = 650, HighLimit = Sample(Variant.From(20.0)),
            Sample = new MachineToolSampleInfo(Acquired, StatusCodes.Good)
        };

        /// <summary>
        /// Creates a complete characteristic with independent absolute tolerances and their limit forms.
        /// </summary>
        private static GmsCharacteristic Characteristic() => new("Bore")
        {
            Identifier = "bore", ResultValue = Analog(10.25), Nominal = Analog(10),
            UpperTolerance = new GmsTolerance(Analog(10.5), ToleranceLimitEnum.LimitValue),
            LowerTolerance = new GmsTolerance(Analog(9.5), ToleranceLimitEnum.NaturalLimit),
            CharacteristicsClass = 4, Evaluation = ResultEvaluationEnum.OK, IsValid = true, Formula = "diameter"
        };

        /// <summary>
        /// Creates a known analog sample and explicit engineering-unit metadata.
        /// </summary>
        private static GmsAnalogValue Analog(double value) => new(
            Sample(Variant.From(value), StatusCodes.UncertainDataSubNormal), Unit());

        /// <summary>
        /// Supplies the released UNECE millimetre unit, not an invented workspace unit.
        /// </summary>
        private static EUInformation Unit() => new()
        {
            NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
            UnitId = 5066068, DisplayName = new LocalizedText("mm"), Description = new LocalizedText("millimetre")
        };

        /// <summary>
        /// Produces a deterministic native value with source quality and time.
        /// </summary>
        private static DataValue Sample(Variant value, StatusCode status = default) => new(value, status, Acquired);
    }
}
#endif
