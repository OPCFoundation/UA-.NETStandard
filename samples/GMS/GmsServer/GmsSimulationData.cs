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
using System.Text;
using Opc.Ua;
using Opc.Ua.GMS;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Result;
using Opc.Ua.MachineTool;

namespace GmsSample.Server
{
    /// <summary>
    /// Creates explicit simulation inputs, not readings acquired from physical measuring equipment.
    /// </summary>
    internal static class GmsSimulationData
    {
        /// <summary>
        /// Gets limits sufficient for the complete 120-second simulation without implicit result eviction.
        /// </summary>
        internal static GmsResultLimits ResultLimits => new()
        {
            MaximumResults = 256,
            MaximumMetadataBytes = 128 * 1024,
            MaximumFileBytes = 32 * 1024,
            MaximumStoredBytes = 8 * 1024 * 1024,
            MaximumCharacteristics = 1,
            MaximumCorrections = 1,
            MaximumUsedTools = 1
        };

        /// <summary>
        /// Produces one coherent native/inherited observation with separate per-instance identities.
        /// </summary>
        internal static GmsSystemSnapshot Snapshot(string id, bool advanced, int tick, DateTimeUtc acquired)
        {
            var metadata = new MachineToolSampleInfo(acquired, StatusCodes.Good);
            var program = new MachineToolProgram
            {
                Key = "Inspect",
                Name = "SIMULATION inspection",
                JobIdentifier = advanced ? id + "-job" : string.Empty,
                Sample = metadata
            };
            var inherited = new MachineToolSnapshot
            {
                Operation = new MachineToolOperation
                {
                    FeedOverride = 75 + tick % 5,
                    IsWarmUp = false,
                    Sample = metadata
                },
                ActiveProgram = program
            };
            if (!advanced)
            {
                return new GmsSystemSnapshot { MachineTool = inherited };
            }
            return new GmsSystemSnapshot
            {
                MachineTool = inherited with
                {
                    Production = new MachineToolProductionObservation
                    {
                        Jobs =
                        [
                            new MachineToolProductionJob
                            {
                                Key = "Job", Identifier = id + "-job", Programs = [program], Sample = metadata,
                                RunsPlanned = 1, RunsPlannedIsValid = true,
                                PartSets =
                                [
                                    new MachineToolProductionPartSet
                                    {
                                        Key = "Set", Name = "Simulated workpieces", PartsPlannedPerRun = 1,
                                        Sample = metadata,
                                        Parts =
                                        [
                                            new MachineToolProductionPart
                                            {
                                                Key = "Part", Name = "Simulated bore", Identifier = id + "-part",
                                                Sample = metadata
                                            }
                                        ]
                                    }
                                ]
                            }
                        ],
                        Statistics = new MachineToolProductionStatistics((uint)tick, metadata)
                    }
                },
                Identification = new GmsIdentificationObservation
                {
                    SubDeviceClass = Sample(Variant.From("SimulatedCoordinateMeasuringMachine"), acquired),
                    Workspace = Sample(Variant.FromStructure(
                        new CartesianWorkspaceType { Length = 150, Width = 100, Height = 80 }), acquired)
                },
                Sensors =
                [
                    new GmsSensor
                    {
                        Tool = new MachineToolTool
                        {
                            Key = "Probe", Identifier = id + "-probe", Name = "Simulated touch probe",
                            Control = new MachineToolToolControl(
                                7, ToolManagement.NumberBased, false, ToolLocked.CapabilityUnavailable),
                            Sample = metadata
                        },
                        Class = Sample(Variant.From((ushort)3), acquired),
                        IsQualifiedStatus = Sample(Variant.From(ToolIsQualifiedStatus.Qualified), acquired),
                        Qualified = new GmsQualifiedLife
                        {
                            RemainingHours = Sample(Variant.From(24 - tick / 3600.0), acquired),
                            StartHours = Sample(Variant.From(24.0), acquired),
                            WarningHours = Sample(Variant.From(0.5), acquired),
                            LimitHours = Sample(Variant.From(0.0), acquired)
                        },
                        EngineeringUnit = Sample(Variant.FromStructure(Millimetres()), acquired),
                        Resolution = Sample(Variant.From(0.001), acquired)
                    }
                ],
                Loading = new GmsLoadingObservation
                {
                    LoadStatus = Sample(Variant.From(2u), acquired),
                    IsInLoadingPosition = Sample(Variant.From(false), acquired)
                },
                ToolMonitoring = new GmsToolMonitoringObservation { ActiveSensorKeys = ["Probe"], Sample = metadata },
                Accessories =
                [
                    new GmsAccessory
                    {
                        Key = "Rotary", Kind = GmsAccessoryKind.RotaryTable,
                        Identification = new GmsItemIdentification(
                            new LocalizedText("OPC Foundation simulation"), id + "-rotary"),
                        IsIntegrated = Sample(Variant.From(true), acquired),
                        NumberOfAxes = Sample(Variant.From((byte)1), acquired)
                    }
                ],
                AdditionalSensors =
                [
                    new GmsAdditionalSensor
                    {
                        Key = "Temperature", Identifier = id + "-temperature", Name = "Simulated ambient temperature",
                        Class = Sample(Variant.From((byte)1), acquired),
                        Value = Sample(Variant.From(31.5 + tick % 5 * 0.1), acquired,
                            StatusCodes.UncertainSensorNotAccurate),
                        EngineeringUnits = new EUInformation
                        {
                            NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                            UnitId = 4408652, DisplayName = new LocalizedText("°C")
                        }
                    }
                ],
                SensorWarnings =
                [
                    new GmsSensorWarning
                    {
                        Key = "TemperatureWarning", SensorKey = "Temperature", ErrorCode = "SIM-TEMPERATURE",
                        Message = new LocalizedText("SIMULATION: ambient temperature above the configured limit."),
                        IsActive = true, Severity = 600,
                        HighLimit = Sample(Variant.From(25.0), acquired),
                        Sample = metadata,
                        AllowedOperations = MachineToolConditionOperations.None
                    }
                ],
                Calibration = new GmsCalibrationObservation
                {
                    Calibrated = Sample(Variant.From(true), acquired),
                    DateOfCalibration = Sample(Variant.From(new DateTimeUtc(2026, 1, 1, 0, 0, 0)), acquired),
                    PredictedTime = Sample(Variant.From(new DateTimeUtc(2027, 1, 1, 0, 0, 0)), acquired),
                    CalibrationCertificate = Sample(
                        Variant.From((ArrayOf<string>)["urn:simulation:calibration:no-physical-certificate"]), acquired)
                },
                Jobs =
                [
                    new GmsJobObservation
                    {
                        Key = "Job",
                        BatchIdentifier = Sample(Variant.From(id + "-batch"), acquired),
                        Duration = Sample(Variant.From(tick * 1000.0), acquired),
                        RemainingTime = Sample(Variant.From((120 - tick) * 1000.0), acquired),
                        MeasurementReason = Sample(Variant.From(MeasurementReasonEnum.AuditMeasurement), acquired)
                    }
                ],
                Parts =
                [
                    new GmsPartObservation
                    {
                        JobKey = "Job", PartSetKey = "Set", PartKey = "Part",
                        PartDescription = Sample(Variant.From("SIMULATION: bore inspection"), acquired)
                    }
                ]
            };
        }

        /// <summary>
        /// Creates a unique immutable final or intermediate sample, keeping UA quality independent of validity.
        /// </summary>
        internal static GmsResult Result(string id, bool advanced, int tick, DateTimeUtc acquired, bool intermediate)
        {
            string resultId = FormattableString.Invariant($"{id}-{(intermediate ? "I" : "F")}-{tick:D4}");
            var envelope = new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType { ResultId = resultId },
                ResultContent = [Variant.From(10.25 + tick / 1000.0)]
            };
            ArrayOf<GmsCharacteristic> characteristics = intermediate
                ? [new GmsCharacteristic("Bore")
                {
                    Identifier = id + "-bore",
                    ResultValue = Analog(10.25 + tick / 1000.0, acquired),
                    Nominal = Analog(10.0, acquired),
                    UpperTolerance = new GmsTolerance(Analog(10.5, acquired), ToleranceLimitEnum.LimitValue),
                    LowerTolerance = new GmsTolerance(Analog(9.5, acquired), ToleranceLimitEnum.NaturalLimit),
                    CharacteristicsClass = 4,
                    Evaluation = ResultEvaluationEnum.OK,
                    IsValid = true,
                    Formula = "Simulated diameter"
                }]
                : default;
            ByteString file = advanced && !intermediate
                ? ByteString.From(Encoding.UTF8.GetBytes(
                    $"SIMULATION ONLY: {resultId}; no physical measurement.\r\n" + new string('x', 12 * 1024)))
                : default;
            return new GmsResult(
                Sample(Variant.FromStructure(envelope), acquired, StatusCodes.UncertainDataSubNormal),
                usedTools: advanced ? [id + "-probe"] : default,
                characteristics: characteristics,
                programName: intermediate ? "SIMULATION inspection" : null,
                isIntermediate: intermediate,
                fileContent: file,
                mimeType: "text/plain");
        }

        /// <summary>
        /// Supplies a calculated monitoring value, never a request to change an upstream machine.
        /// </summary>
        internal static GmsCorrection Correction(string id, int tick, DateTimeUtc acquired) => new(
            id + "-correction", id + "-bore", relativeValue: Analog(0.25 + tick / 1000.0, acquired))
        {
            Description = new LocalizedText("SIMULATION: calculated deviation only; do not apply to equipment."),
            ProgramName = "SIMULATION inspection",
            LowerControlLimit = Analog(-0.5, acquired),
            UpperControlLimit = Analog(0.5, acquired)
        };

        /// <summary>
        /// Retains a simulated source timestamp and quality without substituting client receive time.
        /// </summary>
        private static DataValue Sample(Variant value, DateTimeUtc acquired, StatusCode status = default) =>
            new(value, status, acquired);

        /// <summary>
        /// Supplies unmodified analog quality independently of tolerance evaluation.
        /// </summary>
        private static GmsAnalogValue Analog(double value, DateTimeUtc acquired) =>
            new(Sample(Variant.From(value), acquired, StatusCodes.UncertainDataSubNormal), Millimetres());

        /// <summary>
        /// Uses the released UNECE millimetre unit rather than inventing workspace unit fields.
        /// </summary>
        private static EUInformation Millimetres() => new()
        {
            NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
            UnitId = 5066068,
            DisplayName = new LocalizedText("mm"),
            Description = new LocalizedText("millimetre")
        };
    }
}
