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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Server;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Hosting;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace MachineToolSample.Server
{
    /// <summary>
    /// Supplies explicit simulation observations while the existing host owns address-space and service lifetimes.
    /// </summary>
    internal sealed class DemoApplication : IServerStartupTask, IAsyncDisposable
    {
        /// <summary>
        /// Borrows host services and keeps equipment/storage isolated per configured instance.
        /// </summary>
        public DemoApplication(
            ServerOptions options, TimeProvider clock, ITelemetryContext telemetry, IWmtpAuthorization authorization)
        {
            m_options = options;
            m_clock = clock;
            m_telemetry = telemetry;
            m_authorization = authorization;
            m_logger = telemetry.CreateLogger<DemoApplication>();
        }

        /// <summary>
        /// Gets completion of actual OPC UA startup, not merely generic-host construction.
        /// </summary>
        public Task Ready => m_ready.Task;

        /// <summary>
        /// Adds only the explicitly selected families to one existing namespace owner.
        /// </summary>
        public async ValueTask ConfigureAsync(IMachineryBuildContext context, CancellationToken cancellationToken)
        {
            for (int index = 1; index <= m_options.Instances; index++)
            {
                if ((m_options.Parts & MachineToolParts.MachineTool) != 0)
                {
                    await AddMachineAsync(context, FormattableString.Invariant($"Machine{index}"), cancellationToken)
                        .ConfigureAwait(false);
                }
                if ((m_options.Parts & MachineToolParts.Wmtp) != 0)
                {
                    await AddPeripheralAsync(
                        context, FormattableString.Invariant($"Peripheral{index}"), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            m_ready.TrySetResult(true);
            return default;
        }

        /// <summary>
        /// Acquires explicit samples and drains every accepted job revision in order until the bounded window ends.
        /// </summary>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            DateTimeOffset end = m_clock.GetUtcNow().AddSeconds(m_options.Seconds);
            while (m_clock.GetUtcNow() < end)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (MachineInstance machine in m_machines)
                {
                    if (machine.Jobs != null)
                    {
                        await machine.Jobs.SetMonitoringAsync(Monitoring(), cancellationToken).ConfigureAwait(false);
                        await SimulateJobAsync(machine, cancellationToken).ConfigureAwait(false);
                        await machine.Jobs.DrainAsync(machine.Runtime.RefreshAsync, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await machine.Provider!.SetSnapshotAsync(Monitoring(), cancellationToken).ConfigureAwait(false);
                        await machine.Runtime.RefreshAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                foreach (WmtpPeripheral peripheral in m_peripherals)
                {
                    try
                    {
                        await peripheral.RefreshAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (ServiceResultException error) when (error.StatusCode == StatusCodes.BadServerTooBusy)
                    {
                        m_logger.AcquisitionDeferred(peripheral.NodeId);
                    }
                }
                await TimeProviderExtensions.Delay(m_clock, TimeSpan.FromMilliseconds(200), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            var failures = new List<Exception>();
            for (int index = m_resources.Count - 1; index >= 0; index--)
            {
                try
                {
                    await m_resources[index].DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
            }
            foreach (string root in m_fileRoots)
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    failures.Add(error);
                }
            }
            if (failures.Count != 0)
            {
                throw new AggregateException("Simulation resources did not all close cleanly.", failures);
            }
        }

        private async ValueTask AddMachineAsync(
            IMachineryBuildContext context, string name, CancellationToken cancellationToken)
        {
            MachineToolBuilder<MachineToolState> builder = context.AddMachineTool(name)
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("OPC Foundation simulation");
                    identity.SerialNumber = name;
                    identity.ProductInstanceUri = "urn:machinetool-sample:" + name;
                });
            InMemoryMachineToolProvider? provider = null;
            MachineToolJobSource? jobs = null;
            if (m_options.Advanced)
            {
                jobs = new MachineToolJobSource(new MachineToolJobSourceOptions
                {
                    Jobs = [new MachineToolJobSourceDescriptor
                    {
                        JobOrderId = DemoContract.JobOrderId, WorkMasterId = DemoContract.WorkMasterId,
                        ProgramName = DemoContract.ProgramName, RunsPlanned = 1,
                        PartSetKey = "DemoParts", PartSetName = "Simulated part set", PartsPlannedPerRun = 1
                    }],
                    MaximumJobOrders = 4,
                    MaximumJobResponses = 4
                }, Monitoring(), m_clock, m_telemetry);
                m_resources.Add(jobs);
                builder.WithProvider(jobs)
                    .WithProduction(new MachineToolProductionOptions
                    {
                        Features = MachineToolFeatures.Production | MachineToolFeatures.DynamicProductionPlan |
                            MachineToolFeatures.ProductionParts
                    })
                    .WithJobManagement(new MachineToolJobManagementOptions
                    {
                        Receiver = new AuthorizedReceiver(jobs, m_options.AllowCommands && !m_options.RefuseCommands),
                        ResponseProvider = jobs,
                        Catalog = jobs
                    });
                string root = Path.Combine(Path.GetTempPath(), "mt-demo-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(root, "WorkMasters"));
                m_fileRoots.Add(root);
                using (var output = new FileStream(Path.Combine(root, "WorkMasters", DemoContract.ProgramName),
                    FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(DemoContract.ProgramContents);
#if NETFRAMEWORK
                    await output.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
#else
                    await output.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
#endif
                }
                builder.WithFileSystem(
                    new PhysicalFileSystemProvider(root, isWritable: false), includeWorkMasters: true);
            }
            else
            {
                provider = new InMemoryMachineToolProvider(Monitoring());
                builder.WithProvider(provider);
            }
            MachineToolRuntime<MachineToolState> runtime = await builder.BuildAsync(cancellationToken)
                .ConfigureAwait(false);
            m_resources.Add(runtime);
            m_machines.Add(new MachineInstance(name, runtime, provider, jobs));
        }

        private async ValueTask AddPeripheralAsync(
            IMachineryBuildContext context, string name, CancellationToken cancellationToken)
        {
            var provider = new DemoPeripheral(m_options, m_clock);
            WmtpPeripheralBuilder builder = context.AddWirelessPeripheral(name)
                .WithProvider(provider).WithAuthorization(m_authorization)
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("OPC Foundation simulation");
                    identity.SerialNumber = name;
                    identity.ProductInstanceUri = "urn:machinetool-sample:" + name;
                })
                .WithByteLimits(1024, 65536);
            if (m_options.Advanced)
            {
                builder.WithFeatures(WmtpFeatures.All);
                foreach (WmtpMeasurementKind kind in DemoPeripheral.MeasurementKinds)
                {
                    builder.AddMeasurement(DemoPeripheral.Definition(kind, advanced: true));
                }
                builder.WithWorkRecords(await CreateRecordsAsync(false, cancellationToken).ConfigureAwait(false),
                    WmtpRecordFeatures.All);
                builder.WithServiceRecords(await CreateRecordsAsync(true, cancellationToken).ConfigureAwait(false),
                    WmtpRecordFeatures.All);
            }
            WmtpPeripheral peripheral = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
            m_resources.Add(peripheral);
            m_peripherals.Add(peripheral);
        }

        private async ValueTask<InMemoryWmtpRecordStore> CreateRecordsAsync(
            bool service, CancellationToken cancellationToken)
        {
            var store = new InMemoryWmtpRecordStore(m_telemetry, capacity: 3, maximumReportBytes: 65536,
                timeProvider: m_clock);
            m_resources.Add(store);
            WmtpMeasurementDefinition definition = DemoPeripheral.Definition(WmtpMeasurementKind.Temperature, true);
            for (uint index = 1; index <= 3; index++)
            {
                await store.AppendAsync(new WMTPOutputDataType
                {
                    EngineeringUnits = definition.EngineeringUnits,
                    ActualValue = 20 + index,
                    TypeOfMeasurement = (uint)definition.Kind,
                    TypeOfSample = (uint)WmtpSampleKind.Instantaneous,
                    InstrumentRange = definition.InstrumentRange,
                    EURange = definition.EURange,
                    ValuePrecision = definition.ValuePrecision,
                    Definition = definition.Definition,
                    SignalTag = service ? "service-temperature" : "work-temperature",
                    Timestamp = m_clock.GetUtcNow().AddSeconds((int)index - 4),
                    Index = index,
                    MeasurementPeriod = 50,
                    InternalUpdateInterval = 25
                }, cancellationToken).ConfigureAwait(false);
            }
            return store;
        }

        private async ValueTask SimulateJobAsync(MachineInstance machine, CancellationToken cancellationToken)
        {
            if (machine.Name != m_options.SimulateJob || m_simulated.Contains(machine.Name))
            {
                return;
            }
            MachineToolJobSource source = machine.Jobs!;
            MachineToolJobSourceCapture capture = await source.CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (capture.JobOrders.Count == 0 || capture.JobOrders[0].State[0].StateNumber != 2)
            {
                return;
            }
            Isa95JobOrderReceiptV2 receipt = await source.TransitionAsync(
                DemoContract.JobOrderId, Isa95JobExecutionTransition.BeginExecution, cancellationToken)
                .ConfigureAwait(false);
            if (receipt.ReturnStatus != Isa95JobReturnStatus.Success)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, receipt.Result.ToString());
            }
            await source.RecordPartsAsync(DemoContract.JobOrderId, new MachineToolJobSourcePartCounts(1, 1,
                new MachineToolSampleInfo(m_clock.GetUtcNow(), StatusCodes.Good)), cancellationToken)
                .ConfigureAwait(false);
            await source.CompleteRunAsync(DemoContract.JobOrderId, cancellationToken).ConfigureAwait(false);
            m_simulated.Add(machine.Name);
            Console.WriteLine($"SIMULATED_JOB_COMPLETED machine={machine.Name}; no CNC execution.");
        }

        private MachineToolSnapshot Monitoring() => new()
        {
            Operation = new MachineToolOperation
            {
                Mode = MachineOperationMode.Automatic,
                Sample = new MachineToolSampleInfo(m_clock.GetUtcNow(), StatusCodes.Good)
            }
        };

        /// <summary>
        /// Keeps one machine's provider and coherent job source separate from all sibling equipment.
        /// </summary>
        private sealed record MachineInstance(
            string Name, MachineToolRuntime<MachineToolState> Runtime,
            InMemoryMachineToolProvider? Provider, MachineToolJobSource? Jobs);

        /// <summary>
        /// Uses the existing ISA95 engine while requiring explicit server-side consent for simulated controls.
        /// </summary>
        private sealed class AuthorizedReceiver(MachineToolJobSource source, bool enabled) : IIsa95JobOrderReceiverV2
        {
            /// <inheritdoc/>
            public ValueTask<Isa95JobOrderReceiptV2> ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2 operation, V2.ISA95JobOrderDataType jobOrder,
                ArrayOf<LocalizedText> comment = default, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return enabled ? source.ReceiveJobOrderAsync(operation, jobOrder, comment, cancellationToken) :
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                        "This sample requires explicit server consent to accept simulated job controls.");
            }
        }

        private readonly ServerOptions m_options;
        private readonly TimeProvider m_clock;
        private readonly ITelemetryContext m_telemetry;
        private readonly IWmtpAuthorization m_authorization;
        private readonly ILogger m_logger;
        private readonly List<MachineInstance> m_machines = [];
        private readonly List<WmtpPeripheral> m_peripherals = [];
        private readonly List<IAsyncDisposable> m_resources = [];
        private readonly List<string> m_fileRoots = [];
        private readonly HashSet<string> m_simulated = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource<bool> m_ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int m_disposed;
    }

    /// <summary>
    /// Makes expected peripheral backpressure explicit without retrying or aborting a caller's device operation.
    /// </summary>
    internal static partial class DemoApplicationLog
    {
        /// <summary>
        /// Retains the previous acquired value and normal stale-age policy until the next acquisition tick.
        /// </summary>
        [LoggerMessage(EventId = MachineToolServerEventIds.DemoApplication, Level = LogLevel.Information,
            Message = "Acquisition deferred for {PeripheralId}: device operation active; retrying next normal tick.")]
        public static partial void AcquisitionDeferred(this ILogger logger, NodeId peripheralId);
    }
}
