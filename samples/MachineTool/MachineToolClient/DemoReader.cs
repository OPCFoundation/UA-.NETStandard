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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.WMTP;
using TypedClient = Opc.Ua.MachineTool.Client.MachineToolClient;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace MachineToolSample.Client
{
    /// <summary>
    /// Demonstrates generated proxies and bounded typed workflows without reimplementing OPC UA services.
    /// </summary>
    internal static class DemoReader
    {
        /// <summary>
        /// Discovers actual identities, reads selected instances, then performs at most one bounded observation.
        /// </summary>
        internal static async Task RunAsync(TypedClient client, ClientOptions options, CancellationToken ct)
        {
            ArrayOf<MachineToolDeviceEntry> devices = await client.DiscoverAsync(options.MaximumDevices, ct)
                .ConfigureAwait(false);
            Console.WriteLine($"DISCOVERED count={devices.Count} models={client.AvailableParts}");
            var selected = new List<MachineToolDeviceEntry>();
            for (int index = 0; index < devices.Count; index++)
            {
                MachineToolDeviceEntry entry = devices[index];
                Console.WriteLine($"DEVICE name={entry.BrowseName} id={entry.NodeId} kind={entry.Kind}");
                if (options.Target == null || entry.BrowseName.Name == options.Target)
                {
                    selected.Add(entry);
                }
            }
            if (options.Target != null && selected.Count != 1)
            {
                throw new ArgumentException("The explicit target must identify exactly one discovered instance.");
            }
            var variables = new List<NodeId>();
            foreach (MachineToolDeviceEntry entry in selected)
            {
                if (entry.Kind == MachineToolDeviceKind.MachineTool)
                {
                    await ReadMachineAsync(client, entry, options, ct).ConfigureAwait(false);
                }
                else
                {
                    await ReadPeripheralAsync(client, entry, options, variables, ct).ConfigureAwait(false);
                }
            }
            if (options.Seconds > 0 && selected.Count > 0)
            {
                var settings = new MachineToolObservationOptions
                {
                    Window = TimeSpan.FromSeconds(options.Seconds),
                    MaximumNotifications = options.Count
                };
                MachineToolDeviceEntry? machine = selected.Find(
                    entry => entry.Kind == MachineToolDeviceKind.MachineTool);
                MachineToolDataObservation? observation = machine != null
                    ? await client.ObserveDataAsync(machine.NodeId, settings, ct).ConfigureAwait(false)
                    : variables.Count != 0
                        ? await client.ObserveValuesAsync(variables.ToArrayOf(), settings, ct).ConfigureAwait(false)
                        : null;
                if (observation != null)
                {
                    Console.WriteLine($"OBSERVED count={observation.Changes.Count} limit={observation.LimitReached} " +
                        $"dropped={observation.DroppedNotifications}");
                    for (int index = 0; index < observation.Changes.Count; index++)
                    {
                        Print(client, observation.Changes[index].NodeId.ToString(), observation.Changes[index].Value);
                    }
                }
                else
                {
                    Console.WriteLine(
                        "OBSERVATION_ABSENT: selected peripheral has no configured measurement variables.");
                }
            }
        }

        private static async Task ReadMachineAsync(
            TypedClient client, MachineToolDeviceEntry entry, ClientOptions options, CancellationToken ct)
        {
            if (options.Command is not ("none" or "job-store-start"))
            {
                throw new ArgumentException("This command requires a wireless peripheral, not a machine.");
            }
            MachineToolReadSnapshot snapshot = await client.ReadSnapshotAsync(entry.NodeId, ct).ConfigureAwait(false);
            Print(client, "OperationMode", snapshot.OperationMode);
            Print(client, "ActiveProgram", snapshot.ActiveProgramName);
            MachineToolCapabilityReadSnapshot capabilities = await client.GetCapabilitiesAsync(entry.NodeId, ct)
                .ConfigureAwait(false);
            Console.WriteLine($"CAPABILITIES declared={capabilities.DeclarationAvailable}");
            MachineToolEquipmentReadSnapshot equipment = await client.ReadEquipmentAsync(
                entry.NodeId, 32, 128, ct).ConfigureAwait(false);
            MachineToolPrognosisReadSnapshot prognoses = await client.ReadPrognosesAsync(entry.NodeId, 32, ct)
                .ConfigureAwait(false);
            MachineToolProductionReadSnapshot production = await client.ReadProductionAsync(entry.NodeId,
                new MachineToolProductionReadOptions { MaximumElements = 128, MaximumReferences = 1024 }, ct)
                .ConfigureAwait(false);
            Console.WriteLine($"MACHINE tools={equipment.Tools.Count} prognoses={prognoses.Entries.Count} " +
                $"jobs={production.Jobs.Count}");
            if (options.ReadFiles)
            {
                FileDirectoryTypeClient root =
                    await client.GetWorkMastersAsync(entry.NodeId, ct).ConfigureAwait(false) ??
                    throw new ServiceResultException(StatusCodes.BadNotSupported, "WorkMasters is not configured.");
                var files = new FileSystemClient(client.Session, root.ObjectId,
                    new FileSystemClientOptions { MaxBufferedReadSize = 4096 });
                string path = FormattableString.Invariant($"{root.ObjectId.NamespaceIndex}:{DemoContract.ProgramName}");
                byte[] contents = await files.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                if (Encoding.UTF8.GetString(contents) != DemoContract.ProgramContents)
                {
                    throw new InvalidOperationException("The remote program is not the documented simulation content.");
                }
                Console.WriteLine($"FILE_COPIED bytes={contents.Length}; read-only simulation data, never executed.");
            }
            if (options.Command == "job-store-start")
            {
                Opc.Ua.Machinery.Jobs.JobManagementTypeClient management =
                    await client.GetJobManagementAsync(entry.NodeId, ct).ConfigureAwait(false) ??
                    throw new ServiceResultException(StatusCodes.BadNotSupported, "Machinery Jobs is not configured.");
                V2.ISA95JobOrderReceiverObjectTypeClient receiver =
                    await management.GetJobOrderControlAsync(client.Telemetry, ct).ConfigureAwait(false) ??
                    throw new ServiceResultException(StatusCodes.BadConfigurationError, "JobOrderControl is missing.");
                ulong result = await receiver.StoreAndStartAsync(new V2.ISA95JobOrderDataType
                {
                    EncodingMask = (uint)V2.ISA95JobOrderDataTypeFields.WorkMasterID,
                    JobOrderID = DemoContract.JobOrderId,
                    WorkMasterID = [new V2.ISA95WorkMasterDataType { ID = DemoContract.WorkMasterId }]
                }, [new LocalizedText("Explicitly consented simulated order.")], ct).ConfigureAwait(false);
                if (result != 1)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        FormattableString.Invariant($"ISA95 request refused with ReturnStatus {result}."));
                }
                Console.WriteLine("JOB_ACCEPTED: StoreAndStart accepted; this does not assert CNC execution.");
            }
        }

        private static async Task ReadPeripheralAsync(
            TypedClient owner, MachineToolDeviceEntry entry, ClientOptions options, List<NodeId> variables,
            CancellationToken ct)
        {
            if (options.ReadFiles || options.Command == "job-store-start")
            {
                throw new ArgumentException("The selected workflow requires a machine, not a wireless peripheral.");
            }
            WmtpPeripheralClient client = owner.GetWirelessPeripheral(entry.NodeId);
            WmtpPeripheralSnapshot snapshot = await client.ReadSnapshotAsync(ct).ConfigureAwait(false);
            Print(owner, "SerialNumber", snapshot.SerialNumber);
            Print(owner, "Battery", snapshot.Battery);
            Print(owner, "CalibrationMode", snapshot.CalibrationMode);
            ArrayOf<WmtpMeasurementReading> sensors = await client.ReadMeasurementsAsync(32, ct).ConfigureAwait(false);
            for (int index = 0; index < sensors.Count; index++)
            {
                WmtpMeasurementReading sensor = sensors[index];
                Print(owner, sensor.BrowseName.ToString(), sensor.Sample);
                variables.Add(sensor.ValueNodeId);
                if (variables.Count > 256)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "The complete selected measurement inventory exceeds the observation bound.");
                }
            }
            if (await client.Proxy.GetWMTPWorkCycleDataAsync(owner.Telemetry, ct).ConfigureAwait(false) != null)
            {
                await ReadRecordsAsync(await client.GetWorkRecordsAsync(ct).ConfigureAwait(false), "work", ct)
                    .ConfigureAwait(false);
            }
            if (await client.Proxy.GetWMTPServiceCycleDataAsync(owner.Telemetry, ct).ConfigureAwait(false) != null)
            {
                await ReadRecordsAsync(await client.GetServiceRecordsAsync(ct).ConfigureAwait(false), "service", ct)
                    .ConfigureAwait(false);
            }
            await RequestAsync(client, sensors, options, ct).ConfigureAwait(false);
        }

        private static async Task ReadRecordsAsync(WmtpRecordClient records, string dataset, CancellationToken ct)
        {
            var all = new WmtpRecordQuery();
            uint before = await records.CountAsync(all, ct).ConfigureAwait(false);
            ArrayOf<WMTPOutputDataType> copy = await records.ReadAsync(all, 16, ct).ConfigureAwait(false);
            uint after = await records.CountAsync(all, ct).ConfigureAwait(false);
            if (copy.Count != before || after != before)
            {
                throw new InvalidOperationException("The sample dataset changed during its nonconsuming report.");
            }
            Console.WriteLine($"RECORDS dataset={dataset} count={copy.Count} retained={after}");
        }

        private static async Task RequestAsync(
            WmtpPeripheralClient client, ArrayOf<WmtpMeasurementReading> sensors,
            ClientOptions options, CancellationToken ct)
        {
            switch (options.Command)
            {
                case "none":
                    return;
                case "configure":
                    await ConfigureAsync(client, sensors, options, ct).ConfigureAwait(false);
                    break;
                case "calibrate-on":
                case "calibrate-off":
                    await client.SetCalibrationAsync(options.Command == "calibrate-on", ct).ConfigureAwait(false);
                    break;
                case "set-time":
                    await client.SetDeviceTimeAsync(TimeProvider.System.GetUtcNow(),
                        new TimeZoneDataType { Offset = 0, DaylightSavingInOffset = false }, ct).ConfigureAwait(false);
                    break;
                case "delete-work":
                    WmtpRecordClient deleting = await client.GetWorkRecordsAsync(ct).ConfigureAwait(false);
                    await deleting.DeleteAsync(new WmtpRecordQuery
                    {
                        Selection = WmtpRecordSelection.Index,
                        FromIndex = (uint)options.DeleteIndex!.Value,
                        ToIndex = (uint)options.DeleteIndex.Value
                    }, ct).ConfigureAwait(false);
                    break;
                case "abort-work":
                    WmtpRecordClient aborting = await client.GetWorkRecordsAsync(ct).ConfigureAwait(false);
                    await aborting.AbortAsync(ct).ConfigureAwait(false);
                    break;
                case "firmware":
                case "license":
                    byte[] bytes = new byte[options.TransferBytes!.Value];
                    bytes.AsSpan().Fill((byte)'D');
                    await client.TransferAsync(options.Command == "firmware"
                        ? WmtpTransferKind.Firmware : WmtpTransferKind.License, ByteString.From(bytes),
                        cancellationToken: ct)
                        .ConfigureAwait(false);
                    Console.WriteLine("TRANSFER_ACCEPTED: provider received demonstration bytes; nothing installed.");
                    break;
                default:
                    throw new ArgumentException("Unsupported wireless command.");
            }
            Console.WriteLine($"REQUEST_ACCEPTED command={options.Command} target={options.Target}");
        }

        private static async Task ConfigureAsync(
            WmtpPeripheralClient client, ArrayOf<WmtpMeasurementReading> sensors,
            ClientOptions options, CancellationToken ct)
        {
            NodeId selected = default;
            for (int index = 0; index < sensors.Count; index++)
            {
                if (sensors[index].BrowseName.Name == options.Sensor)
                {
                    if (!selected.IsNull)
                    {
                        throw new ArgumentException("The sensor name is ambiguous.");
                    }
                    selected = sensors[index].NodeId;
                }
            }
            if (selected.IsNull)
            {
                throw new ArgumentException("The selected sensor is not present.");
            }
            WMTPMeasurementTypeClient proxy = client.GetMeasurementProxy(selected);
            await proxy.SetTriggerSettingsAsync(options.TriggerMilliseconds!.Value, ct).ConfigureAwait(false);
            if (options.ProveRefusal)
            {
                bool refused = false;
                try
                {
                    await proxy.SetTriggerSettingsAsync(options.TriggerMilliseconds.Value + 1, ct)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException error) when (error.StatusCode == StatusCodes.BadResourceUnavailable)
                {
                    refused = true;
                }
                if (!refused)
                {
                    throw new InvalidOperationException("The second simulated configuration was not refused.");
                }
                ArrayOf<WmtpMeasurementReading> after =
                    await client.ReadMeasurementsAsync(32, ct).ConfigureAwait(false);
                bool retained = false;
                for (int index = 0; index < after.Count; index++)
                {
                    if (after[index].NodeId == selected)
                    {
                        if (!after[index].TriggerSettings.WrappedValue.TryGetValue(out uint trigger) ||
                            trigger != options.TriggerMilliseconds.Value)
                        {
                            throw new InvalidOperationException("Refusal changed the previously accepted trigger.");
                        }
                        retained = true;
                    }
                }
                if (!retained)
                {
                    throw new InvalidOperationException("The configured sensor disappeared before readback.");
                }
                Console.WriteLine("REFUSAL_CONFIRMED: second request rejected; accepted trigger retained.");
            }
        }

        private static void Print(TypedClient client, string label, in DataValue value)
        {
            if (value.IsNull)
            {
                Console.WriteLine($"{label}: ABSENT");
                return;
            }
            using var encoder = new JsonEncoder(client.Session.MessageContext);
            encoder.WriteDataValue("DataValue", value);
            Console.WriteLine($"{label}: {encoder.CloseAndReturnText()}");
        }
    }
}
