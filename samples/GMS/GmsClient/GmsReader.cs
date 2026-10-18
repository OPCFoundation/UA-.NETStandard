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
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.MachineTool;

namespace GmsSample.Client
{
    /// <summary>
    /// Reads actual native GMS instances through the bounded typed facade, never a raw service engine.
    /// </summary>
    internal static class GmsReader
    {
        /// <summary>
        /// Discovers, selects and reads at most two systems without conflating optional absence with Good data.
        /// </summary>
        /// <exception cref="InvalidOperationException">The selection is missing, ambiguous or too large.</exception>
        internal static async Task RunAsync(
            GmsClient client, GmsClientArguments options, CancellationToken cancellationToken)
        {
            ArrayOf<GmsDeviceEntry> discovered = await client.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"DISCOVERED {discovered.Count} native GMS systems (including recognized subtypes).");
            var selected = new List<GmsDeviceEntry>();
            foreach (GmsDeviceEntry entry in discovered)
            {
                Console.WriteLine($"DEVICE {entry.BrowseName} node={entry.NodeId} type={entry.TypeDefinitionId}");
                if (options.SystemName == null ||
                    string.Equals(options.SystemName, entry.BrowseName.Name, StringComparison.Ordinal))
                {
                    selected.Add(entry);
                }
            }
            if (selected.Count is < 1 or > 2 || (options.SystemName != null && selected.Count != 1))
            {
                throw new InvalidOperationException(
                    "Select one unambiguous --system, or discover at most two systems for this bounded sample.");
            }
            Console.WriteLine(options.AcknowledgeResult == null
                ? "PASSIVE: no result or condition acknowledgement, correction write or equipment command."
                : "EXPLICIT_CONSENT: only the selected system/result will be acknowledged once; no implicit retry.");
            foreach (GmsDeviceEntry entry in selected)
            {
                await ReadSystemAsync(client, entry, cancellationToken).ConfigureAwait(false);
                await GmsResultReader.RunAsync(client, entry, options, cancellationToken).ConfigureAwait(false);
                if (options.Seconds > 0)
                {
                    await GmsObservation.RunAsync(client, entry, options, cancellationToken).ConfigureAwait(false);
                }
            }
            Console.WriteLine("READ_BOUNDS systems<=2 elements<=64 references<=4096 rawPayloadBytes<=131072");
        }

        /// <summary>
        /// Prints complete raw quality and timestamps, explicitly labeling an absent optional sample.
        /// </summary>
        internal static void PrintValue(string name, in DataValue value)
        {
            if (value.IsNull)
            {
                Console.WriteLine($"  {name}: absent, not a Good or empty measurement.");
                return;
            }
            Console.WriteLine(FormattableString.Invariant(
                $"  {name}: raw={value.WrappedValue} status={value.StatusCode}"));
            Console.WriteLine(FormattableString.Invariant(
                $"  source={value.SourceTimestamp:O} server={value.ServerTimestamp:O}"));
        }

        /// <summary>
        /// Preserves resolved variable identity independently of acquisition status.
        /// </summary>
        internal static void PrintReading(string name, in GmsVariableReading reading)
        {
            Console.WriteLine($"  VARIABLE {name} node={reading.NodeId} present={reading.IsPresent}");
            PrintValue(name, reading.Value);
        }

        /// <summary>
        /// Reads monitoring, equipment, calibration and defined native production metadata.
        /// </summary>
        private static async Task ReadSystemAsync(
            GmsClient client, GmsDeviceEntry entry, CancellationToken cancellationToken)
        {
            GmsSystemReadSnapshot snapshot = await client.ReadSnapshotAsync(entry.NodeId, cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine($"SNAPSHOT {entry.BrowseName} root={snapshot.NodeId}");
            PrintReading("Manufacturer", snapshot.Identification.Manufacturer);
            PrintReading("SerialNumber", snapshot.Identification.SerialNumber);
            PrintReading("ProductInstanceUri", snapshot.Identification.ProductInstanceUri);
            PrintReading("SubDeviceClass", snapshot.Identification.SubDeviceClass);
            PrintReading("Workspace", snapshot.Identification.Workspace);
            PrintValue("OperationMode", snapshot.Inherited.OperationMode);
            PrintValue("FeedOverride", snapshot.Inherited.FeedOverride);
            PrintValue("ActiveProgramName", snapshot.Inherited.ActiveProgramName);
            PrintValue("ActiveProgramState", snapshot.Inherited.ActiveProgramState);
            if (snapshot.Loading is { } loading)
            {
                PrintReading("LoadStatus", loading.LoadStatus);
                PrintReading("IsInLoadingPosition", loading.IsInLoadingPosition);
            }
            else
            {
                Console.WriteLine("OPTIONAL LoadingMonitoring absent.");
            }
            if (snapshot.ToolMonitoring is { } tools)
            {
                PrintReading("ActiveTool", tools.ActiveTool);
            }
            else
            {
                Console.WriteLine("OPTIONAL ToolMonitoring absent.");
            }
            GmsEquipmentReadSnapshot equipment = await client.ReadEquipmentAsync(entry.NodeId, cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine(
                $"EQUIPMENT sensors={equipment.Sensors.Count} accessories={equipment.Accessories.Count} " +
                $"additionalSensors={equipment.AdditionalSensors.Count}");
            foreach (GmsSensorReadSnapshot sensor in equipment.Sensors)
            {
                PrintSensor(sensor);
            }
            foreach (GmsAdditionalSensorReadSnapshot sensor in equipment.AdditionalSensors)
            {
                Console.WriteLine($"ENVIRONMENTAL_SENSOR {sensor.BrowseName} node={sensor.NodeId}");
                PrintValue("Measured input (not probe qualification)", sensor.Value);
                PrintReading("EngineeringUnits", sensor.EngineeringUnits);
                PrintReading("Class", sensor.Class);
            }
            foreach (GmsAccessoryReadSnapshot accessory in equipment.Accessories)
            {
                Console.WriteLine($"ACCESSORY {accessory.BrowseName} type={accessory.TypeDefinitionId}");
                PrintReading("SerialNumber", accessory.Identification.SerialNumber);
                PrintReading("IsIntegrated", accessory.IsIntegrated);
                PrintReading("NumberOfAxes", accessory.NumberOfAxes);
                PrintReading("IsAvailable", accessory.IsAvailable);
            }
            GmsCalibrationReadSnapshot? calibration = await client.ReadCalibrationAsync(entry.NodeId, cancellationToken)
                .ConfigureAwait(false);
            if (calibration == null)
            {
                Console.WriteLine("OPTIONAL Calibration absent.");
            }
            else
            {
                Console.WriteLine($"CALIBRATION node={calibration.NodeId}; observations, not a calibration command.");
                PrintReading("Calibrated", calibration.Calibrated);
                PrintReading("DateOfCalibration", calibration.DateOfCalibration);
                PrintReading("PredictedTime", calibration.PredictedTime);
                PrintReading("CalibrationCertificate (reference only)", calibration.CalibrationCertificate);
            }
            GmsProductionReadSnapshot production = await client.ReadProductionAsync(entry.NodeId, cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine($"PRODUCTION_OBSERVATIONS jobs={production.Jobs.Count}; no job-start verbs.");
            foreach (GmsJobReadSnapshot job in production.Jobs)
            {
                Console.WriteLine($"JOB node={job.Inherited.NodeId} type={job.Inherited.TypeDefinitionId}");
                PrintReading("BatchIdentifier", job.BatchIdentifier);
                PrintReading("Duration", job.Duration);
                PrintReading("RemainingTime", job.RemainingTime);
                PrintReading("MeasurementReason", job.MeasurementReason);
                foreach (GmsPartReadSnapshot part in job.Parts)
                {
                    Console.WriteLine($"PART node={part.Inherited.NodeId} type={part.Inherited.TypeDefinitionId}");
                    PrintReading("PartDescription", part.PartDescription);
                }
            }
            Console.WriteLine(
                "CONFORMANCE: undefined upstream GMS_ProductionStatisticsType is not invented; " +
                "no complete Production facet claim (released NodeSet normative under Annex A.1).");
        }

        /// <summary>
        /// Keeps qualification countdown values distinct from environmental SensorWarningAlarmType conditions.
        /// </summary>
        private static void PrintSensor(GmsSensorReadSnapshot sensor)
        {
            Console.WriteLine($"PRIMARY_SENSOR {sensor.Inherited.BrowseName} node={sensor.Inherited.NodeId}");
            PrintReading("Class", sensor.Class);
            PrintReading("IsQualifiedStatus", sensor.IsQualifiedStatus);
            foreach (MachineToolToolLifeReadSnapshot life in sensor.Inherited.Life)
            {
                Console.WriteLine($"  TOOL_LIFE {life.BrowseName} node={life.NodeId}");
                PrintValue("remaining life", life.Value);
                PrintValue("IsCountingUp", life.IsCountingUp);
                PrintValue("EngineeringUnits", life.EngineeringUnits);
            }
            foreach (GmsSensorReadSnapshot child in sensor.Sensors)
            {
                PrintSensor(child);
            }
        }
    }
}
