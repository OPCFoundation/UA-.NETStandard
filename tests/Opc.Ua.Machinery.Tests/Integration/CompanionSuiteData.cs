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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.CranesHoists;
using Opc.Ua.CranesHoists.Server;
using Opc.Ua.GMS;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Robotics;
using Opc.Ua.Vision.MachineVision;
using Opc.Ua.Vision.Server;
using ResultDataType = Opc.Ua.Machinery.Result.ResultDataType;
using ResultMetaDataType = Opc.Ua.Machinery.Result.ResultMetaDataType;

namespace Opc.Ua.Machinery.Tests.Integration
{
    /// <summary>
    /// Explicit, different acquisitions for identically named slots in independent companion instances.
    /// </summary>
    internal static class CompanionSuiteData
    {
        /// <summary>
        /// Gets an acquisition time independent of server and client receipt time.
        /// </summary>
        internal static DateTimeUtc Acquired { get; } = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Builds a real retained production and tool inventory, with no controller commands.
        /// </summary>
        internal static MachineToolSnapshot Machine(int number)
        {
            var sample = new MachineToolSampleInfo(Acquired, StatusCodes.Good);
            var program = new MachineToolProgram
            {
                Name = "CELL" + number + ".NC",
                JobIdentifier = "Order" + number,
                Phase = MachineToolProductionPhase.Running,
                Sample = sample
            };
            return new MachineToolSnapshot
            {
                Operation = new MachineToolOperation
                {
                    Mode = MachineOperationMode.Automatic,
                    FeedOverride = 80 + number,
                    ItemState = MachineToolItemState.Executing,
                    MachineryMode = MachineToolMachineryMode.Processing,
                    Sample = sample
                },
                ActiveProgram = program,
                Production = new MachineToolProductionObservation
                {
                    Jobs =
                    [
                        new MachineToolProductionJob
                        {
                            Identifier = program.JobIdentifier,
                            Phase = MachineToolProductionPhase.Running,
                            RunsPlanned = 1,
                            RunsPlannedIsValid = true,
                            Programs = [program],
                            Sample = sample
                        }
                    ]
                },
                Equipment = new MachineToolEquipmentObservation
                {
                    Tools =
                    [
                        new MachineToolTool
                        {
                            Key = "Tool",
                            Identifier = "Cutter" + number,
                            Name = "Independent cutter " + number,
                            Control = new MachineToolToolControl((uint)number, ToolManagement.NumberBased,
                                false, ToolLocked.CapabilityUnavailable),
                            Sample = sample
                        }
                    ]
                }
            };
        }

        /// <summary>
        /// Supplies native sensors and their own active-tool references and qualification observations.
        /// </summary>
        internal static GmsSystemSnapshot Gms(int number)
        {
            var sample = new MachineToolSampleInfo(Acquired, StatusCodes.Good);
            return new GmsSystemSnapshot
            {
                MachineTool = Machine(number) with { Equipment = null },
                Jobs =
                [
                    new GmsJobObservation
                    {
                        Key = "Order" + number,
                        BatchIdentifier = Value(Variant.From("InspectionBatch" + number))
                    }
                ],
                Sensors =
                [
                    new GmsSensor
                    {
                        Tool = new MachineToolTool
                        {
                            Key = "Probe",
                            Identifier = "Probe" + number,
                            Control = new MachineToolToolControl((uint)number, ToolManagement.NumberBased,
                                false, ToolLocked.CapabilityUnavailable),
                            Sample = sample
                        },
                        Class = Value(Variant.From((ushort)3)),
                        IsQualifiedStatus = Value(Variant.From(ToolIsQualifiedStatus.Qualified)),
                        Qualified = new GmsQualifiedLife { RemainingHours = Value(Variant.From(10.0 + number)) }
                    }
                ],
                ToolMonitoring = new GmsToolMonitoringObservation { ActiveSensorKeys = ["Probe"], Sample = sample }
            };
        }

        /// <summary>
        /// Supplies an instance-specific result using the existing Machinery envelope and file protocol.
        /// </summary>
        internal static GmsResult Result(int number)
        {
            return new GmsResult(Value(Variant.FromStructure(new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType { ResultId = "Result" + number },
                ResultContent = [Variant.From(100.0 + number)]
            })), usedTools: ["Probe" + number], fileContent: ByteString.From((byte)number, 7, 9));
        }

        /// <summary>
        /// Configures the shared identification writer with an actual per-instance nameplate.
        /// </summary>
        internal static void Identify(MachineryIdentificationData identity, string name)
        {
            identity.Manufacturer = new LocalizedText("Composition simulation");
            identity.SerialNumber = name;
            identity.ProductInstanceUri = "urn:opcua:composition:" + name;
            identity.Model = new LocalizedText("Simulated workcell");
            identity.ProductCode = "No hardware";
        }

        /// <summary>
        /// Creates the native Robotics topology, including load and protection, without writable motion semantics.
        /// </summary>
        internal static CraneSystemBuilder Crane(
            CranesHoistsBuildContext context, string name, ICraneDataProvider provider)
        {
            return context.AddCraneSystem(name)
                .WithProvider(provider)
                .WithIdentification(identity => Identify(identity, name))
                .AddController("Controller", controller =>
                {
                    controller.WithIdentification(identity =>
                    {
                        identity.Manufacturer = new LocalizedText("Composition simulation");
                        identity.Model = new LocalizedText("Observer");
                        identity.ProductCode = "Observer";
                        identity.SerialNumber = name + "-controller";
                    }).WithCurrentUser(user => user.WithName("Observer").WithLevel("Read only"));
                    controller.AddSoftware("Acquisition", software => software.WithIdentification(identity =>
                    {
                        identity.Manufacturer = new LocalizedText("Composition simulation");
                        identity.Model = new LocalizedText("Acquisition");
                        identity.SoftwareRevision = "1";
                    }));
                    controller.AddTaskControl("Main");
                })
                .AddSafetyState("Safety")
                .AddDevice("Hoist", CraneMotionDeviceCategoryEnum.HOIST, device =>
                {
                    device.WithIdentification(identity => Identify(identity, name + "-hoist"));
                    device.AddAxis("Lift", axis => axis
                        .WithPositionCapability(true, AxisMotionProfileEnumeration.LINEAR)
                        .WithActualLoad()
                        .AddMotor("Motor"));
                })
                .AddProtectiveFunction("Overload", ["Lift"], protection => protection
                    .WithStateMonitoring()
                    .WithDescription(ProtectiveFunctionEnumeration.FORCE_LIMITER, "Observed overload", true)
                    .WithIdentification(identity => Identify(identity, name + "-protection")));
        }

        /// <summary>
        /// Supplies every selected crane signal; local names deliberately repeat in both crane systems.
        /// </summary>
        internal static ArrayOf<CraneSample> CraneSamples(string name, int number)
        {
            return
            [
                Signal(name, CraneSignal.OperationalMode,
                    Variant.From((int)CraneOperationalModeEnumeration.MANUAL)),
                Signal(name, CraneSignal.SystemOn, Variant.From(true)),
                Signal("Hoist", CraneSignal.SpeedOverride, Variant.From(75.0 + number)),
                Signal("Lift", CraneSignal.ActualPosition, Variant.From(1.0 + number)),
                Signal("Lift", CraneSignal.LoadMass, Variant.From(100.0 + number)),
                Signal("Motor", CraneSignal.MotorTemperature, Variant.From(30.0 + number)),
                Signal("Safety", CraneSignal.EmergencyStop, Variant.From(false)),
                Signal("Safety", CraneSignal.ProtectiveStop, Variant.From(false)),
                Signal("Safety", CraneSignal.SafetyOperationalMode,
                    Variant.From((int)OperationalModeEnumeration.AUTOMATIC)),
                Signal("Overload", CraneSignal.Enabled, Variant.From(true)),
                Signal("Overload", CraneSignal.Active, Variant.From(number == 2))
            ];
        }

        /// <summary>
        /// Defines a bounded measurement whose acquisition is independent of its record dataset.
        /// </summary>
        internal static WmtpMeasurementDefinition Measurement()
        {
            return new WmtpMeasurementDefinition
            {
                Name = "Temperature",
                Kind = WmtpMeasurementKind.Temperature,
                EngineeringUnits = new EUInformation
                {
                    NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                    UnitId = 4408652,
                    DisplayName = new LocalizedText("°C")
                },
                EURange = new Range { Low = 0, High = 100 },
                InstrumentRange = new Range { Low = -40, High = 150 }
            };
        }

        /// <summary>
        /// Supplies a nonconsuming record-store acquisition with instance-specific numeric contents.
        /// </summary>
        internal static WMTPOutputDataType Record(int number)
        {
            return new WMTPOutputDataType
            {
                Index = (uint)number,
                ActualValue = 200.0 + number,
                Timestamp = Acquired,
                TypeOfMeasurement = (uint)WmtpMeasurementKind.Temperature,
                EngineeringUnits = Measurement().EngineeringUnits,
                EURange = new Range { Low = 0, High = 300 },
                InstrumentRange = new Range { Low = -40, High = 400 },
                SignalTag = "Temperature",
                Definition = "Independent acquired record"
            };
        }

        /// <summary>
        /// Creates an explicit simulation-only provider; every command is observable by the test.
        /// </summary>
        internal static Mock<IMachineVisionOperationsProvider> Vision(int number)
        {
            var provider = new Mock<IMachineVisionOperationsProvider>(MockBehavior.Strict);
            provider.Setup(value => value.ControlAsync(It.IsAny<MachineVisionControlAction>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<MachineVisionOperationResult>(MachineVisionOperationResult.Good));
            provider.Setup(value => value.StartJobAsync(It.IsAny<MachineVisionJob>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<MachineVisionOperationResult>(MachineVisionOperationResult.Good));
            provider.Setup(value => value.ExecuteAsync(It.IsAny<MachineVisionJob>(), It.IsAny<CancellationToken>()))
                .Returns((MachineVisionJob _, CancellationToken ct) => AcquisitionAsync(number, ct));
            return provider;
        }

        /// <summary>
        /// Preserves actual sample quality and acquisition time.
        /// </summary>
        internal static DataValue Value(Variant value)
        {
            return new DataValue(value, StatusCodes.Good, Acquired);
        }

        /// <summary>
        /// Creates one addressed provider sample.
        /// </summary>
        private static CraneSample Signal(string name, CraneSignal signal, Variant value)
        {
            return new CraneSample(new CraneSignalAddress(name, signal), Value(value));
        }

        /// <summary>
        /// Returns one finite simulated result; no hardware or host file is involved.
        /// </summary>
        private static async IAsyncEnumerable<MachineVisionAcquisition> AcquisitionAsync(
            int number, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new MachineVisionAcquisition(true, new Opc.Ua.Vision.MachineVision.ResultDataType
            {
                ResultId = new ResultIdDataType { Id = "Inspection" + number },
                ResultState = number,
                ResultContent = [Variant.From(number)],
                EncodingMask = (uint)ResultDataTypeFields.ResultContent
            });
        }
    }
}
