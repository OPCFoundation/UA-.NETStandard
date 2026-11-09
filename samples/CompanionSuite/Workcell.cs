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
using Opc.Ua.CranesHoists;
using Opc.Ua.CranesHoists.Server;
using Opc.Ua.GMS;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Robotics;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Vision.MachineVision;
using Opc.Ua.Vision.Server;

namespace CompanionSuite
{
    /// <summary>
    /// Composes independent simulation adapters on one existing owner, never on duplicated model scaffolds.
    /// </summary>
    internal sealed class Workcell : IServerStartupTask, IAsyncDisposable
    {
        /// <summary>
        /// Receives policy, acquisition time and telemetry through the application's DI container.
        /// </summary>
        public Workcell(SuiteOptions options, TimeProvider clock, ITelemetryContext telemetry)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_acquired = clock.GetUtcNow().UtcDateTime;
        }

        /// <summary>
        /// Completes only after OPC UA startup, not merely generic-host startup.
        /// </summary>
        internal Task Ready => m_ready.Task;

        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (m_rootCount != 6 * m_options.Instances || m_owner == null)
            {
                throw new InvalidOperationException("The workcell did not publish every configured native runtime.");
            }
            m_ready.TrySetResult(true);
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            var errors = new List<Exception>();
            for (int i = m_resources.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (m_resources[i] is CraneSystemHandle { IsDisposed: false } crane)
                    {
                        await crane.RemoveAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        await m_resources[i].DisposeAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }
            m_resources.Clear();
            if (errors.Count != 0)
            {
                throw new AggregateException("One or more workcell resources failed to drain.", errors);
            }
        }

        /// <summary>
        /// Publishes observed production/tooling and independent wireless measurements and record datasets.
        /// </summary>
        internal async ValueTask BuildMachinesAsync(IMachineryBuildContext context, CancellationToken ct)
        {
            CaptureOwner(context.Manager);
            for (int number = 1; number <= m_options.Instances; number++)
            {
                string name = "Mill" + number;
                MachineToolSnapshot observations = Machine(number);
                MachineToolRuntime<MachineToolState> machine = await context.AddMachineTool(name)
                    .WithIdentification(identity => Identify(identity, name))
                    .WithProvider(new InMemoryMachineToolProvider(observations))
                    .WithFeatures(MachineToolFeatures.Production | MachineToolFeatures.Tools |
                        MachineToolFeatures.MachineryState)
                    .BuildAsync(ct).ConfigureAwait(false);
                m_resources.Add(machine);
                m_rootCount++;

                var peripheral = new SimulatedPeripheral(number, m_acquired);
                var records = new InMemoryWmtpRecordStore(m_telemetry, capacity: 4, timeProvider: m_clock);
                m_resources.Add(records);
                await records.AppendAsync(peripheral.Record, ct).ConfigureAwait(false);
                WmtpPeripheral wireless = await context.AddWirelessPeripheral("Peripheral" + number)
                    .WithIdentification(identity => Identify(identity, "Peripheral" + number))
                    .WithProvider(peripheral).WithMeasurementProvider(peripheral).WithAuthorization(peripheral)
                    .WithFeatures(WmtpFeatures.Battery).WithWorkRecords(records)
                    .AddMeasurement(SimulatedPeripheral.Measurement)
                    .BuildAsync(ct).ConfigureAwait(false);
                m_resources.Add(wireless);
                m_rootCount++;
                Console.WriteLine($"MACHINE {name} root={machine.NodeId} peripheral={wireless.NodeId}");
            }
        }

        /// <summary>
        /// Binds native GMS sensors, qualification and per-instance results through the existing shared services.
        /// </summary>
        internal async ValueTask BuildGmsAsync(IMachineryBuildContext context, CancellationToken ct)
        {
            CaptureOwner(context.Manager);
            for (int number = 1; number <= m_options.Instances; number++)
            {
                string name = "Gms" + number;
                var observation = new GmsSystemSnapshot
                {
                    MachineTool = new MachineToolSnapshot
                    {
                        Operation = new MachineToolOperation
                        {
                            Mode = MachineOperationMode.Automatic,
                            Sample = new MachineToolSampleInfo(m_acquired, StatusCodes.Good)
                        },
                        ActiveProgram = new MachineToolProgram
                        {
                            Name = "Inspect" + number,
                            Sample = new MachineToolSampleInfo(m_acquired, StatusCodes.Good)
                        }
                    },
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
                                Sample = new MachineToolSampleInfo(m_acquired, StatusCodes.Good)
                            },
                            Class = Value(Variant.From((ushort)3)),
                            IsQualifiedStatus = Value(Variant.From(ToolIsQualifiedStatus.Qualified)),
                            Qualified = new GmsQualifiedLife { RemainingHours = Value(Variant.From(10.0 + number)) }
                        }
                    ],
                    ToolMonitoring = new GmsToolMonitoringObservation
                    {
                        ActiveSensorKeys = ["Probe"],
                        Sample = new MachineToolSampleInfo(m_acquired, StatusCodes.Good)
                    }
                };
                var encoding = ServiceMessageContext.Create(m_telemetry);
                encoding.NamespaceUris = context.Context.NamespaceUris;
                encoding.Factory.Builder.AddGmsTypeSystem().Commit();
                GmsHandle gms = await context.AddGms(name).WithProvider(new InMemoryGmsProvider(observation))
                    .WithIdentification(identity => Identify(identity, name))
                    .WithOptions(new GmsSystemOptions { Features = GmsFeatures.Sensors | GmsFeatures.ToolMonitoring })
                    .WithResults(new InMemoryGmsResultStore(encoding), new GmsResultBindingOptions
                    {
                        PublishedResults = 1,
                        EnableFileTransfer = true,
                        IncludeUsedTools = true
                    }).BuildAsync(ct).ConfigureAwait(false);
                m_resources.Add(gms);
                m_rootCount++;
                await gms.Results.PublishAsync(new GmsResult(Value(Variant.FromStructure(
                    new Opc.Ua.Machinery.Result.ResultDataType
                    {
                        ResultMetaData = new Opc.Ua.Machinery.Result.ResultMetaDataType
                        {
                            ResultId = "Result" + number
                        },
                        ResultContent = [Variant.From(100.0 + number)]
                    })), usedTools: ["Probe" + number], fileContent: ByteString.From((byte)number, 7, 9)), ct)
                    .ConfigureAwait(false);
                Console.WriteLine($"GMS {name} root={gms.NodeId} result=Result{number}");
            }
        }

        /// <summary>
        /// Uses the actual typed Robotics root and Machinery binding transaction for native crane observations.
        /// </summary>
        internal async ValueTask BuildCranesAsync(CranesHoistsBuildContext context, CancellationToken ct)
        {
            CaptureOwner(context.Machinery.Manager);
            for (int number = 1; number <= m_options.Instances; number++)
            {
                string name = "Crane" + number;
                var provider = new SimulatedCraneProvider(CraneSamples(name, number));
                m_resources.Add(provider);
                CraneSystemHandle crane = await context.AddCraneSystem(name).WithProvider(provider)
                    .WithIdentification(identity => Identify(identity, name))
                    .AddController("Controller", controller =>
                    {
                        controller.WithIdentification(identity =>
                        {
                            identity.Manufacturer = new LocalizedText("OPC Foundation sample");
                            identity.Model = new LocalizedText("Simulated observer");
                            identity.ProductCode = "No controller";
                            identity.SerialNumber = name + "-controller";
                        }).WithCurrentUser(user => user.WithName("Simulation").WithLevel("Observer"));
                        controller.AddSoftware("Acquisition", software => software.WithIdentification(identity =>
                        {
                            identity.Manufacturer = new LocalizedText("OPC Foundation sample");
                            identity.Model = new LocalizedText("Simulated acquisition");
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
                            .WithActualLoad().AddMotor("Motor"));
                    })
                    .AddProtectiveFunction("Overload", ["Lift"], protection => protection.WithStateMonitoring()
                        .WithDescription(ProtectiveFunctionEnumeration.FORCE_LIMITER, "Simulated overload", true)
                        .WithIdentification(identity => Identify(identity, name + "-protection")))
                    .BuildAsync(ct).ConfigureAwait(false);
                m_resources.Add(crane);
                m_rootCount++;
                Console.WriteLine($"CRANE {name} root={crane.NodeId}; monitoring only, no movement methods.");
            }
        }

        /// <summary>
        /// Composes the real control runtime and native AMCM asset with independent result storage per camera.
        /// </summary>
        internal async ValueTask BuildVisionAsync(IVisionBuildContext context, CancellationToken ct)
        {
            CaptureOwner(context.Manager);
            for (int number = 1; number <= m_options.Instances; number++)
            {
                var provider = new SimulatedInspection(number);
                MachineVisionSystemHandle inspection = await context.AddMachineVisionSystemAsync(
                    "Inspection" + number, provider, new MachineVisionSystemOptions
                    {
                        InitiallyReady = true,
                        InitiallySimulated = true,
                        Features = MachineVisionFeatures.Results | MachineVisionFeatures.Folders |
                            MachineVisionFeatures.FileTransfer
                    }, cancellationToken: ct).ConfigureAwait(false);
                m_resources.Add(inspection);
                m_rootCount++;
                var identity = new MachineryIdentificationData();
                Identify(identity, "Camera" + number);
                MachineVisionAssetBuilder asset = context.AddMachineVisionAsset("Camera" + number)
                    .WithIdentification(identity).RepresentsSameSystem(inspection.State);
                asset.Nodes.WithHealth(health => health.Set("Temperature", Variant.From(20.0 + number)));
                MachineVisionAssetHandle camera = await asset.BuildAsync(ct).ConfigureAwait(false);
                m_resources.Add(camera);
                m_rootCount++;
                await inspection.PublishResultAsync(provider.PublishedResult, ByteString.From((byte)number), ct)
                    .ConfigureAwait(false);
                Console.WriteLine(
                    $"VISION Inspection{number} root={inspection.State.NodeId} asset={camera.State.NodeId}");
            }
        }

        /// <summary>
        /// Creates observed production and tool state rather than translating it into machine commands.
        /// </summary>
        private MachineToolSnapshot Machine(int number)
        {
            var sample = new MachineToolSampleInfo(m_acquired, StatusCodes.Good);
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
                            Identifier = program.JobIdentifier, Phase = MachineToolProductionPhase.Running,
                            RunsPlanned = 1, RunsPlannedIsValid = true, Programs = [program], Sample = sample
                        }
                    ]
                },
                Equipment = new MachineToolEquipmentObservation
                {
                    Tools =
                    [
                        new MachineToolTool
                        {
                            Key = "Tool", Identifier = "Cutter" + number, Name = "Simulated cutter",
                            Control = new MachineToolToolControl((uint)number, ToolManagement.NumberBased,
                                false, ToolLocked.CapabilityUnavailable),
                            Sample = sample
                        }
                    ]
                }
            };
        }

        /// <summary>
        /// Supplies all selected acquisition signals with instance-specific load, position and protection state.
        /// </summary>
        private ArrayOf<CraneSample> CraneSamples(string name, int number)
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
        /// Couples one explicit signal address and acquired value.
        /// </summary>
        private CraneSample Signal(string name, CraneSignal signal, Variant value)
        {
            return new CraneSample(new CraneSignalAddress(name, signal), Value(value));
        }

        /// <summary>
        /// Supplies a real sample timestamp, not a client receipt timestamp.
        /// </summary>
        private DataValue Value(Variant value)
        {
            return new DataValue(value, StatusCodes.Good, m_acquired);
        }

        /// <summary>
        /// Uses the shared identification writer on every native family subtype.
        /// </summary>
        private static void Identify(MachineryIdentificationData identity, string name)
        {
            identity.Manufacturer = new LocalizedText("OPC Foundation sample");
            identity.SerialNumber = name;
            identity.ProductInstanceUri = "urn:opcfoundation:sample:workcell:" + name;
            identity.Model = new LocalizedText("Simulation only");
            identity.ProductCode = "No hardware";
        }

        /// <summary>
        /// Enforces the shared-owner invariant on the actual configurator contexts.
        /// </summary>
        private void CaptureOwner(AsyncCustomNodeManager manager)
        {
            if (m_owner != null && !ReferenceEquals(m_owner, manager))
            {
                throw new InvalidOperationException("Two different namespace owners reached the workcell.");
            }
            m_owner = manager;
        }

        /// <summary>The application instance namespace shared by every configurator.</summary>
        internal const string InstanceUri = "urn:opcfoundation:sample:companion-workcell";

        /// <summary>The finite application policy.</summary>
        private readonly SuiteOptions m_options;

        /// <summary>The supplied receipt and retention clock.</summary>
        private readonly TimeProvider m_clock;

        /// <summary>The shared logging and codec context source.</summary>
        private readonly ITelemetryContext m_telemetry;

        /// <summary>The explicit initial simulated acquisition time.</summary>
        private readonly DateTimeUtc m_acquired;

        /// <summary>The ready signal from the actual UA startup task.</summary>
        private readonly TaskCompletionSource<bool> m_ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The application-owned resources in construction order.</summary>
        private readonly List<IAsyncDisposable> m_resources = [];

        /// <summary>The actual shared manager captured from public build contexts.</summary>
        private AsyncCustomNodeManager? m_owner;

        /// <summary>The number of native runtime roots successfully constructed.</summary>
        private int m_rootCount;

        /// <summary>Whether application resource cleanup has already run.</summary>
        private bool m_disposed;
    }
}
