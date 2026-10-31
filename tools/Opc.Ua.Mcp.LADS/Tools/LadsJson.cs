/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
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
using System.Globalization;
using System.Text.Json.Nodes;
using Opc.Ua;
using Opc.Ua.LADS.Client;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Explicit JSON projections for LADS client records.
    /// </summary>
    internal static class LadsJson
    {
        /// <summary>
        /// Projects a discovered device.
        /// </summary>
        public static JsonObject DeviceEntry(LadsDevice value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["name"] = value.Name,
                ["displayName"] = value.DisplayName
            };
        }

        /// <summary>
        /// Projects a device snapshot.
        /// </summary>
        public static JsonObject Device(LadsDeviceSnapshot value, IServiceMessageContext context)
        {
            var units = new JsonArray();
            for (int index = 0; index < value.FunctionalUnitNames.Count; index++)
            {
                units.Add(value.FunctionalUnitNames[index]);
            }
            return new JsonObject
            {
                ["nodeId"] = value.DeviceId.ToString(),
                ["displayName"] = value.DisplayName,
                ["identification"] = Identification(value.Identification),
                ["deviceState"] = State(value.DeviceState),
                ["machineryItemState"] = State(value.MachineryItemState),
                ["operationMode"] = State(value.OperationMode),
                ["deviceHealth"] = value.DeviceHealth.IsNull
                    ? null : McpCompanionJson.Variant(value.DeviceHealth, context),
                ["functionalUnitNames"] = units
            };
        }

        /// <summary>
        /// Projects a functional-unit snapshot.
        /// </summary>
        public static JsonObject FunctionalUnit(LadsFunctionalUnitSnapshot value, IServiceMessageContext context)
        {
            var functions = new JsonArray();
            for (int index = 0; index < value.Functions.Count; index++)
            {
                functions.Add(Function(value.Functions[index], context));
            }
            return new JsonObject
            {
                ["nodeId"] = value.FunctionalUnitId.ToString(),
                ["displayName"] = value.DisplayName,
                ["state"] = State(value.State),
                ["runningState"] = State(value.RunningState),
                ["hasProgramManager"] = value.HasProgramManager,
                ["activeProgram"] = ActiveProgram(value.ActiveProgram),
                ["functions"] = functions
            };
        }

        /// <summary>
        /// Projects a function snapshot.
        /// </summary>
        public static JsonObject Function(LadsFunctionSnapshot value, IServiceMessageContext context)
        {
            var values = new JsonArray();
            for (int index = 0; index < value.Values.Count; index++)
            {
                LadsFunctionValue item = value.Values[index];
                values.Add(new JsonObject
                {
                    ["name"] = item.Name,
                    ["nodeId"] = item.NodeId.ToString(),
                    ["value"] = McpCompanionJson.Variant(item.Value.WrappedValue, context),
                    ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(item.Value.StatusCode),
                    ["sourceTimestamp"] = item.Value.SourceTimestamp.IsNull
                        ? null : item.Value.SourceTimestamp.ToDateTime()
                });
            }
            return new JsonObject
            {
                ["nodeId"] = value.FunctionId.ToString(),
                ["displayName"] = value.DisplayName,
                ["kind"] = value.Kind.ToString(),
                ["state"] = value.State,
                ["isEnabled"] = value.IsEnabled,
                ["sensorValue"] = Variant(value.SensorValue, context),
                ["currentValue"] = Variant(value.CurrentValue, context),
                ["targetValue"] = Variant(value.TargetValue, context),
                ["values"] = values
            };
        }

        /// <summary>
        /// Projects a program template.
        /// </summary>
        public static JsonObject Template(LadsProgramTemplate value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["templateId"] = value.Name,
                ["displayName"] = value.DisplayName,
                ["author"] = value.Author,
                ["description"] = value.Description,
                ["deviceTemplateId"] = value.DeviceTemplateId,
                ["supervisoryTemplateId"] = value.SupervisoryTemplateId,
                ["version"] = value.Version,
                ["created"] = value.Created.ToString(CultureInfo.InvariantCulture),
                ["modified"] = value.Modified.ToString(CultureInfo.InvariantCulture)
            };
        }

        /// <summary>
        /// Projects a result and its file references.
        /// </summary>
        public static JsonObject Result(LadsResult value)
        {
            var files = new JsonArray();
            for (int index = 0; index < value.Files.Count; index++)
            {
                files.Add(ResultFile(value.Files[index]));
            }
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["name"] = value.Name,
                ["displayName"] = value.DisplayName,
                ["description"] = value.Description,
                ["applicationUri"] = value.ApplicationUri,
                ["user"] = value.User,
                ["supervisoryJobId"] = value.SupervisoryJobId,
                ["supervisoryTaskId"] = value.SupervisoryTaskId,
                ["deviceProgramRunId"] = value.DeviceProgramRunId,
                ["started"] = value.Started.ToString(CultureInfo.InvariantCulture),
                ["stopped"] = value.Stopped.ToString(CultureInfo.InvariantCulture),
                ["totalRuntime"] = Duration(value.TotalRuntime),
                ["totalPauseTime"] = Duration(value.TotalPauseTime),
                ["estimatedRuntime"] = Duration(value.EstimatedRuntime),
                ["files"] = files
            };
        }

        /// <summary>
        /// Projects a result file without fetching its content.
        /// </summary>
        public static JsonObject ResultFile(LadsResultFile value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["fileId"] = value.CanDownload ? value.FileId.ToString() : null,
                ["name"] = value.Name,
                ["fileName"] = value.FileName,
                ["mimeType"] = value.MimeType,
                ["url"] = value.Url,
                ["canDownload"] = value.CanDownload
            };
        }

        /// <summary>
        /// Projects a maintenance task.
        /// </summary>
        public static JsonObject Maintenance(LadsMaintenanceTask value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["name"] = value.Name,
                ["displayName"] = value.DisplayName,
                ["state"] = State(value.State),
                ["lastExecutionDate"] = value.LastExecutionDate.ToString(CultureInfo.InvariantCulture),
                ["plannedDate"] = value.PlannedDate.ToString(CultureInfo.InvariantCulture),
                ["recurrencePeriod"] = Duration(value.RecurrencePeriod),
                ["estimatedDowntime"] = Duration(value.EstimatedDowntime),
                ["lastOperatingTime"] = Duration(value.LastOperatingTime),
                ["nextOperatingTime"] = Duration(value.NextOperatingTime),
                ["lastOperatingCycles"] = value.LastOperatingCycles,
                ["nextOperatingCycles"] = value.NextOperatingCycles
            };
        }

        /// <summary>
        /// Projects a LADS state snapshot.
        /// </summary>
        public static JsonObject? State(LadsStateSnapshot? value)
        {
            return value is null
                ? null
                : new JsonObject
                {
                    ["stateMachineNodeId"] = value.StateMachineId.ToString(),
                    ["currentState"] = value.CurrentState,
                    ["currentStateId"] = value.CurrentStateId.IsNull ? null : value.CurrentStateId.ToString()
                };
        }

        private static JsonObject Identification(LadsIdentification value)
        {
            return new JsonObject
            {
                ["manufacturer"] = value.Manufacturer,
                ["model"] = value.Model,
                ["serialNumber"] = value.SerialNumber,
                ["softwareRevision"] = value.SoftwareRevision,
                ["hardwareRevision"] = value.HardwareRevision,
                ["deviceRevision"] = value.DeviceRevision,
                ["assetId"] = value.AssetId,
                ["componentName"] = value.ComponentName,
                ["productInstanceUri"] = value.ProductInstanceUri,
                ["manufacturerUri"] = value.ManufacturerUri,
                ["productCode"] = value.ProductCode,
                ["deviceManual"] = value.DeviceManual,
                ["operationalLocation"] = value.OperationalLocation,
                ["hierarchicalLocation"] = value.HierarchicalLocation
            };
        }

        private static JsonObject? ActiveProgram(LadsActiveProgram? value)
        {
            return value is null
                ? null
                : new JsonObject
                {
                    ["nodeId"] = value.NodeId.ToString(),
                    ["templateId"] = value.CurrentProgramTemplateId.IsNull
                        ? null : value.CurrentProgramTemplateId.ToString(),
                    ["templateName"] = value.CurrentProgramTemplate,
                    ["deviceProgramRunId"] = value.DeviceProgramRunId,
                    ["currentStepName"] = value.CurrentStepName,
                    ["currentStepNumber"] = value.CurrentStepNumber,
                    ["estimatedStepNumbers"] = value.EstimatedStepNumbers,
                    ["currentRuntime"] = Duration(value.CurrentRuntime),
                    ["currentPauseTime"] = Duration(value.CurrentPauseTime),
                    ["currentStepRuntime"] = Duration(value.CurrentStepRuntime),
                    ["estimatedRuntime"] = Duration(value.EstimatedRuntime),
                    ["estimatedStepRuntime"] = Duration(value.EstimatedStepRuntime)
                };
        }

        private static JsonNode? Variant(Variant value, IServiceMessageContext context)
        {
            return value.IsNull ? null : McpCompanionJson.Variant(value, context);
        }

        private static string? Duration(TimeSpan? value)
        {
            return value?.ToString("c", CultureInfo.InvariantCulture);
        }
    }
}
