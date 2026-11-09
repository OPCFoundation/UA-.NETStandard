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
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.Scales;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Explicit projections preserving measurement quality, units and optional fields.
    /// </summary>
    internal static class ScalesJson
    {
        /// <summary>
        /// Projects discovery metadata, retaining vendor type definitions.
        /// </summary>
        internal static JsonObject Entry(ScaleEntry entry)
        {
            return new JsonObject
            {
                ["nodeId"] = entry.NodeId.ToString(),
                ["browseName"] = entry.BrowseName.ToString(),
                ["displayName"] = Text(entry.DisplayName),
                ["typeDefinition"] = entry.TypeDefinition.ToString(),
                ["kind"] = entry.Kind?.ToString(),
                ["isScaleSystem"] = entry.IsScaleSystem
            };
        }

        /// <summary>
        /// Projects every identification field.
        /// </summary>
        internal static JsonObject Identification(ScaleIdentification value)
        {
            return new JsonObject
            {
                ["manufacturer"] = Text(value.Manufacturer),
                ["serialNumber"] = value.SerialNumber,
                ["productInstanceUri"] = value.ProductInstanceUri,
                ["manufacturerUri"] = value.ManufacturerUri,
                ["model"] = Text(value.Model),
                ["productCode"] = value.ProductCode,
                ["hardwareRevision"] = value.HardwareRevision,
                ["softwareRevision"] = value.SoftwareRevision,
                ["deviceClass"] = value.DeviceClass,
                ["assetId"] = value.AssetId,
                ["componentName"] = Text(value.ComponentName),
                ["location"] = value.Location,
                ["yearOfConstruction"] = value.YearOfConstruction,
                ["monthOfConstruction"] = value.MonthOfConstruction,
                ["initialOperationDate"] = value.InitialOperationDate
            };
        }

        /// <summary>
        /// Distinguishes an absent WeightItem from a published but non-finite measurement.
        /// </summary>
        internal static JsonObject Weight(ScaleReading? value, IServiceMessageContext context)
        {
            return new JsonObject
            {
                ["available"] = value != null,
                ["reading"] = value == null ? null : new JsonObject
                {
                    ["gross"] = McpCompanionJson.Number(value.Gross),
                    ["net"] = McpCompanionJson.Number(value.Net),
                    ["tare"] = McpCompanionJson.Number(value.Tare),
                    ["tareMode"] = value.TareMode.ToString(),
                    ["tareModeValue"] = (int)value.TareMode,
                    ["overload"] = value.Overload,
                    ["underload"] = value.Underload,
                    ["stable"] = value.Stable,
                    ["insideZero"] = value.InsideZero,
                    ["currentRangeId"] = value.CurrentRangeId,
                    ["weightId"] = value.WeightId,
                    ["engineeringUnits"] = Units(value.EngineeringUnits, context),
                    ["sourceTimestamp"] = value.Timestamp,
                    ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(value.StatusCode),
                    ["statusCodeValue"] = value.StatusCode.Code
                }
            };
        }

        /// <summary>
        /// Projects an ordered weighing range without losing unusual numeric values.
        /// </summary>
        internal static JsonObject Range(WeighingRangeDefinition value, IServiceMessageContext context)
        {
            return new JsonObject
            {
                ["low"] = McpCompanionJson.Number(value.Low),
                ["high"] = McpCompanionJson.Number(value.High),
                ["actualScaleInterval"] = McpCompanionJson.Number(value.ActualScaleInterval),
                ["verificationScaleInterval"] = McpCompanionJson.Number(value.VerificationScaleInterval),
                ["engineeringUnits"] = Units(value.EngineeringUnits, context)
            };
        }

        /// <summary>
        /// Projects product identity, selection state and the resolved DI service node.
        /// </summary>
        internal static JsonObject Product(ScaleProductInfo value, NodeId lockNodeId)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["productId"] = value.ProductId,
                ["productName"] = Text(value.ProductName),
                ["typeDefinition"] = value.TypeDefinition.ToString(),
                ["processing"] = value.Processing,
                ["lockNodeId"] = lockNodeId.IsNull ? null : lockNodeId.ToString()
            };
        }

        /// <summary>
        /// Projects event and alarm values without interpreting inactive as absent.
        /// </summary>
        internal static JsonObject Notification(ScaleNotificationInfo value)
        {
            var auxiliary = new JsonArray();
            foreach (string parameter in value.AuxParameters)
            {
                auxiliary.Add(parameter);
            }
            return new JsonObject
            {
                ["sourceNode"] = value.SourceNode.ToString(),
                ["sourceName"] = value.SourceName,
                ["time"] = value.Time,
                ["severity"] = value.Severity,
                ["message"] = Text(value.Message),
                ["category"] = value.Category.ToString(),
                ["categoryValue"] = (uint)value.Category,
                ["notificationId"] = value.NotificationId,
                ["definedId"] = value.DefinedId?.ToString(),
                ["vendorNotificationId"] = value.VendorNotificationId,
                ["auxParameters"] = auxiliary,
                ["isAlarm"] = value.IsAlarm,
                ["active"] = value.Active
            };
        }

        /// <summary>
        /// Retains localized text and distinguishes a null value from empty text.
        /// </summary>
        internal static JsonNode? Text(LocalizedText value)
        {
            return value.IsNull ? null : new JsonObject { ["text"] = value.Text, ["locale"] = value.Locale };
        }

        /// <summary>
        /// Encodes an engineering unit using its generated UA encoder.
        /// </summary>
        internal static JsonNode? Units(EUInformation? value, IServiceMessageContext context)
        {
            return value == null ? null : McpCompanionJson.Encode(value, context);
        }
    }
}
