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
using Opc.Ua.Pumps;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Explicit projections of Pumps records, preserving typed UA values without reflection.
    /// </summary>
    internal static class PumpsJson
    {
        /// <summary>
        /// Projects a discovered pump or marking.
        /// </summary>
        internal static JsonObject Entry(PumpEntry entry)
        {
            return new JsonObject
            {
                ["nodeId"] = entry.NodeId.ToString(),
                ["browseName"] = entry.BrowseName.ToString(),
                ["displayName"] = Text(entry.DisplayName),
                ["typeDefinitionId"] = entry.TypeDefinitionId.ToString()
            };
        }

        /// <summary>
        /// Projects a complete nameplate.
        /// </summary>
        internal static JsonNode? Nameplate(PumpNameplate? value, IServiceMessageContext context)
        {
            return value is null ? null : new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["manufacturer"] = Text(value.Manufacturer),
                ["serialNumber"] = value.SerialNumber,
                ["manufacturerUri"] = value.ManufacturerUri,
                ["model"] = Text(value.Model),
                ["productCode"] = value.ProductCode,
                ["hardwareRevision"] = value.HardwareRevision,
                ["softwareRevision"] = value.SoftwareRevision,
                ["deviceClass"] = value.DeviceClass,
                ["productInstanceUri"] = value.ProductInstanceUri,
                ["assetId"] = value.AssetId,
                ["componentName"] = Text(value.ComponentName),
                ["location"] = value.Location,
                ["initialOperationDate"] = value.InitialOperationDate,
                ["yearOfConstruction"] = value.YearOfConstruction,
                ["monthOfConstruction"] = value.MonthOfConstruction,
                ["dayOfConstruction"] = value.DayOfConstruction,
                ["articleNumber"] = value.ArticleNumber,
                ["orderProductCode"] = value.OrderProductCode,
                ["typeOfProduct"] = value.TypeOfProduct,
                ["supplier"] = value.Supplier,
                ["countryOfOrigin"] = value.CountryOfOrigin,
                ["fabricationNumber"] = value.FabricationNumber,
                ["gtinCode"] = value.GTINCode,
                ["nationalStockNumber"] = value.NationalStockNumber,
                ["physicalAddress"] = value.PhysicalAddress is null
                    ? null : McpCompanionJson.Encode(value.PhysicalAddress, context),
                ["markingsFolderId"] = value.MarkingsFolderId.ToString()
            };
        }

        /// <summary>
        /// Projects a value with its type, quality, timestamp, units and range.
        /// </summary>
        internal static JsonObject Value(PumpValue value, IServiceMessageContext context)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["name"] = value.Name,
                ["value"] = McpCompanionJson.Variant(value.Value, context),
                ["statusCode"] = value.StatusCode.Code,
                ["sourceTimestamp"] = value.SourceTimestamp,
                ["engineeringUnits"] = value.EngineeringUnits is null
                    ? null : McpCompanionJson.Encode(value.EngineeringUnits, context),
                ["euRange"] = value.EuRange is null
                    ? null : McpCompanionJson.Encode(value.EuRange, context)
            };
        }

        /// <summary>
        /// Projects all values and nested groups of a typed value set.
        /// </summary>
        internal static JsonObject ValueSet(PumpValueSet values, IServiceMessageContext context)
        {
            var items = new JsonArray();
            foreach (PumpValue value in values)
            {
                items.Add(Value(value, context));
            }
            var groups = new JsonObject();
            foreach (var group in values.Groups)
            {
                groups[group.Key] = ValueSet(group.Value, context);
            }
            return new JsonObject
            {
                ["nodeId"] = values.NodeId.ToString(),
                ["available"] = !values.NodeId.IsNull,
                ["values"] = items,
                ["groups"] = groups
            };
        }

        /// <summary>
        /// Projects the three configuration groups.
        /// </summary>
        internal static JsonNode? Configuration(PumpConfigurationData? value, IServiceMessageContext context)
        {
            return value is null ? null : new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["design"] = ValueSet(value.Design, context),
                ["implementation"] = ValueSet(value.Implementation, context),
                ["systemRequirements"] = ValueSet(value.SystemRequirements, context)
            };
        }

        /// <summary>
        /// Projects operational readings, never providing a write path.
        /// </summary>
        internal static JsonNode? Operational(PumpOperationalData? value, IServiceMessageContext context)
        {
            return value is null ? null : new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["measurements"] = ValueSet(value.Measurements, context),
                ["signals"] = ValueSet(value.Signals, context),
                ["control"] = ValueSet(value.Control, context),
                ["pumpActuation"] = ValueSet(value.PumpActuation, context),
                ["bypassActuation"] = ValueSet(value.BypassActuation, context),
                ["throttleValveActuation"] = ValueSet(value.ThrottleValveActuation, context),
                ["multiPump"] = MultiPump(value.MultiPump)
            };
        }

        /// <summary>
        /// Projects the complete multi-pump record, retaining null versus empty arrays.
        /// </summary>
        internal static JsonNode? MultiPump(MultiPumpConfiguration? value)
        {
            return value is null ? null : new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["pumpRole"] = value.PumpRole?.ToString(),
                ["operationMode"] = value.OperationMode?.ToString(),
                ["distributionType"] = value.DistributionType?.ToString(),
                ["exchangeMode"] = value.ExchangeMode?.ToString(),
                ["exchangeTime"] = value.ExchangeTime,
                ["exchangeTimeDifference"] = Number(value.ExchangeTimeDifference),
                ["numberOfPumps"] = value.NumberOfPumps,
                ["maximumNumberOfPumpsInOperation"] = value.MaximumNumberOfPumpsInOperation,
                ["distributionPriority"] = Strings(value.DistributionPriority),
                ["pumpCollectiveIds"] = Strings(value.PumpCollectiveIDs),
                ["redundantPumpIds"] = Strings(value.RedundantPumpIDs)
            };
        }

        /// <summary>
        /// Projects every supervision flag, including false and bad-quality values.
        /// </summary>
        internal static JsonNode? Supervision(PumpSupervisionStatus? value, IServiceMessageContext context)
        {
            if (value is null)
            {
                return null;
            }
            var groups = new JsonObject();
            foreach (var group in value.Groups)
            {
                groups[group.Key] = ValueSet(group.Value, context);
            }
            var active = new JsonArray();
            foreach (var signal in value.ActiveSignals)
            {
                active.Add(new JsonObject
                {
                    ["group"] = signal.Key,
                    ["signal"] = Value(signal.Value, context)
                });
            }
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["groups"] = groups,
                ["hasActiveSignals"] = value.HasActiveSignals,
                ["activeSignals"] = active
            };
        }

        /// <summary>
        /// Projects all four maintenance groups.
        /// </summary>
        internal static JsonNode? Maintenance(PumpMaintenanceData? value, IServiceMessageContext context)
        {
            if (value is null)
            {
                return null;
            }
            var groups = new JsonObject();
            foreach (var group in value.Groups)
            {
                groups[group.Key] = ValueSet(group.Value, context);
            }
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["groups"] = groups,
                ["stateOfTheItem"] = value.StateOfTheItem?.ToString(),
                ["maintenanceLevel"] = value.MaintenanceLevel?.ToString(),
                ["hasFailure"] = value.HasFailure
            };
        }

        /// <summary>
        /// Projects a typed or vendor-defined port with all its groups.
        /// </summary>
        internal static JsonObject Port(PumpPortDescriptor value, IServiceMessageContext context)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["name"] = value.Name,
                ["typeDefinitionId"] = value.TypeDefinitionId.ToString(),
                ["kind"] = value.Kind.ToString(),
                ["direction"] = value.Direction?.ToString(),
                ["category"] = value.Category,
                ["idCarrier"] = value.IdCarrier,
                ["design"] = ValueSet(value.Design, context),
                ["implementation"] = ValueSet(value.Implementation, context),
                ["measurements"] = ValueSet(value.Measurements, context),
                ["systemRequirements"] = ValueSet(value.SystemRequirements, context)
            };
        }

        /// <summary>
        /// Projects a complete pump snapshot.
        /// </summary>
        internal static JsonObject Snapshot(
            PumpSnapshot value,
            IServiceMessageContext context,
            int offset,
            int maxResults)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["browseName"] = value.BrowseName.ToString(),
                ["displayName"] = Text(value.DisplayName),
                ["typeDefinitionId"] = value.TypeDefinitionId.ToString(),
                ["nameplate"] = Nameplate(value.Nameplate, context),
                ["configuration"] = Configuration(value.Configuration, context),
                ["operational"] = Operational(value.Operational, context),
                ["supervision"] = Supervision(value.Supervision, context),
                ["maintenance"] = Maintenance(value.Maintenance, context),
                ["documentation"] = ValueSet(value.Documentation, context),
                ["ports"] = McpCompanionTools.Page(
                    value.Ports, port => Port(port, context), offset, maxResults),
                ["consistency"] = "live"
            };
        }

        /// <summary>
        /// Projects localized text with its locale.
        /// </summary>
        private static JsonObject? Text(LocalizedText value)
        {
            return value.IsNull ? null : new JsonObject { ["text"] = value.Text, ["locale"] = value.Locale };
        }

        /// <summary>
        /// Preserves non-finite optional measurements.
        /// </summary>
        private static JsonNode? Number(double? value)
        {
            return value.HasValue ? McpCompanionJson.Number(value.Value) : null;
        }

        /// <summary>
        /// Projects a nullable UA array as a JSON array, never its implementation shape.
        /// </summary>
        private static JsonArray? Strings(ArrayOf<string> values)
        {
            if (values.IsNull)
            {
                return null;
            }
            var result = new JsonArray();
            foreach (string value in values)
            {
                result.Add(value);
            }
            return result;
        }
    }
}
