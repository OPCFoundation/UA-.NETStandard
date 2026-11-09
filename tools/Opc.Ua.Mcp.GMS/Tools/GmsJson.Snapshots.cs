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

using System.Text.Json.Nodes;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.MachineTool;

namespace Opc.Ua.Mcp.Tools
{
    internal sealed partial class GmsJson
    {
        /// <summary>
        /// Encodes native geometry, loading and tool-monitoring snapshots without inventing actuation.
        /// </summary>
        internal JsonObject System(GmsSystemReadSnapshot value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["monitoring"] = Readings(
                    ("operationMode", value.Inherited.OperationMode), ("feedOverride", value.Inherited.FeedOverride),
                    ("isWarmUp", value.Inherited.IsWarmUp), ("activeProgramName", value.Inherited.ActiveProgramName),
                    ("activeProgramState", value.Inherited.ActiveProgramState)),
                ["identificationNodeId"] = value.Identification.NodeId.ToString(),
                ["identification"] = Variables(
                    ("manufacturer", value.Identification.Manufacturer),
                    ("serialNumber", value.Identification.SerialNumber),
                    ("productInstanceUri", value.Identification.ProductInstanceUri),
                    ("subDeviceClass", value.Identification.SubDeviceClass),
                    ("workspace", value.Identification.Workspace)),
                ["loading"] = value.Loading == null ? null : new JsonObject
                {
                    ["nodeId"] = value.Loading.NodeId.ToString(),
                    ["loadStatus"] = Variable(value.Loading.LoadStatus),
                    ["isInLoadingPosition"] = Variable(value.Loading.IsInLoadingPosition)
                },
                ["toolMonitoring"] = value.ToolMonitoring == null ? null : new JsonObject
                {
                    ["nodeId"] = value.ToolMonitoring.NodeId.ToString(),
                    ["activeTool"] = Variable(value.ToolMonitoring.ActiveTool)
                }
            };
        }

        /// <summary>
        /// Encodes native sensors, accessories and additional sensors using independent raw readings.
        /// </summary>
        internal JsonObject Equipment(GmsEquipmentReadSnapshot value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["toolsNodeId"] = value.Inherited.ToolsNodeId.IsNull ? null : value.Inherited.ToolsNodeId.ToString(),
                ["nodeVersion"] = Reading(value.Inherited.NodeVersion),
                ["sensors"] = Array(value.Sensors, Sensor),
                ["accessories"] = Array(value.Accessories, accessory => new JsonObject
                {
                    ["nodeId"] = accessory.NodeId.ToString(),
                    ["typeDefinitionId"] = accessory.TypeDefinitionId.ToString(),
                    ["browseName"] = accessory.BrowseName.ToString(),
                    ["kind"] = accessory.Kind.ToString(),
                    ["identificationNodeId"] = accessory.Identification.NodeId.ToString(),
                    ["identification"] = Variables(("manufacturer", accessory.Identification.Manufacturer),
                        ("serialNumber", accessory.Identification.SerialNumber)),
                    ["fields"] = Variables(("isIntegrated", accessory.IsIntegrated),
                        ("numberOfAxes", accessory.NumberOfAxes), ("isAvailable", accessory.IsAvailable))
                }),
                ["additionalSensors"] = Array(value.AdditionalSensors, sensor => new JsonObject
                {
                    ["nodeId"] = sensor.NodeId.ToString(),
                    ["typeDefinitionId"] = sensor.TypeDefinitionId.ToString(),
                    ["browseName"] = sensor.BrowseName.ToString(),
                    ["reading"] = Reading(sensor.Value),
                    ["fields"] = Variables(("identifier", sensor.Identifier), ("name", sensor.Name),
                        ("class", sensor.Class), ("engineeringUnits", sensor.EngineeringUnits))
                })
            };
        }

        /// <summary>
        /// Preserves the native forecast and certificate references without fetching external resources.
        /// </summary>
        internal JsonObject Calibration(GmsCalibrationReadSnapshot? value)
        {
            return new JsonObject
            {
                ["present"] = value != null,
                ["calibration"] = value == null ? null : new JsonObject
                {
                    ["nodeId"] = value.NodeId.ToString(),
                    ["typeDefinitionId"] = value.TypeDefinitionId.ToString(),
                    ["fields"] = Variables(
                        ("predictedTime", value.PredictedTime), ("calibrated", value.Calibrated),
                        ("dateOfCalibration", value.DateOfCalibration),
                        ("calibrationInterval", value.CalibrationInterval),
                        ("calibrationPreptime", value.CalibrationPreptime),
                        ("calibrationCertificate", value.CalibrationCertificate))
                }
            };
        }

        /// <summary>
        /// Retains all native sensor fields and the inherited qualified-life countdown, including negative values.
        /// </summary>
        private JsonObject Sensor(GmsSensorReadSnapshot value)
        {
            MachineToolToolReadSnapshot inherited = value.Inherited;
            return new JsonObject
            {
                ["nodeId"] = inherited.NodeId.ToString(),
                ["typeDefinitionId"] = inherited.TypeDefinitionId.ToString(),
                ["browseName"] = inherited.BrowseName.ToString(),
                ["kind"] = inherited.Kind.ToString(),
                ["inherited"] = Readings(
                    ("identifier", inherited.Identifier), ("name", inherited.Name),
                    ("locationName", inherited.LocationName), ("placeNumber", inherited.PlaceNumber),
                    ("controlIdentifier1", inherited.ControlIdentifier1),
                    ("controlIdentifier2", inherited.ControlIdentifier2),
                    ("controlIdentifierInterpretation", inherited.ControlIdentifierInterpretation),
                    ("locked", inherited.Locked), ("reasonForLocking", inherited.ReasonForLocking),
                    ("lastUsage", inherited.LastUsage), ("plannedForOperating", inherited.PlannedForOperating)),
                ["locationNodeId"] = inherited.LocationNodeId.IsNull ? null : inherited.LocationNodeId.ToString(),
                ["toolLifeNodeId"] = inherited.ToolLifeNodeId.IsNull ? null : inherited.ToolLifeNodeId.ToString(),
                ["life"] = Array(inherited.Life, life => new JsonObject
                {
                    ["nodeId"] = life.NodeId.ToString(),
                    ["typeDefinitionId"] = life.TypeDefinitionId.ToString(),
                    ["browseName"] = life.BrowseName.ToString(),
                    ["dataType"] = life.DataType.ToString(),
                    ["fields"] = Readings(
                        ("value", life.Value), ("engineeringUnits", life.EngineeringUnits),
                        ("indication", life.Indication),
                        ("isCountingUp", life.IsCountingUp), ("startValue", life.StartValue),
                        ("warningValue", life.WarningValue), ("limitValue", life.LimitValue))
                }),
                ["native"] = Variables(
                    ("class", value.Class), ("capabilities", value.Capabilities), ("alignment", value.Alignment),
                    ("axes", value.Axes), ("isQualifiedStatus", value.IsQualifiedStatus),
                    ("absoluteProbe", value.AbsoluteProbe),
                    ("engineeringUnit", value.EngineeringUnit), ("measuringRange", value.MeasuringRange),
                    ("workingRange", value.WorkingRange), ("resolution", value.Resolution)),
                ["sensors"] = Array(value.Sensors, Sensor)
            };
        }
    }
}
