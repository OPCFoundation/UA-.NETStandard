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
using System.Globalization;
using System.Text.Json.Nodes;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Machinery.ProcessValues;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Explicit projections of Machinery records and semantic result filters.
    /// </summary>
    internal static class MachineryJson
    {
        /// <summary>
        /// Parses a required local node identifier.
        /// </summary>
        public static NodeId Node(string text)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(text);
            NodeId node = OpcUaJsonHelper.ParseNodeId(text);
            return node.IsNull ? throw new ArgumentException("A non-null NodeId is required.", nameof(text)) : node;
        }

        /// <summary>
        /// Projects a discovered instance without guessing its type.
        /// </summary>
        public static JsonObject Entry(MachineEntry value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["browseName"] = value.BrowseName.ToString(),
                ["displayName"] = Text(value.DisplayName),
                ["typeDefinitionId"] = value.TypeDefinitionId.ToString()
            };
        }

        /// <summary>
        /// Projects optional identification values, preserving locale and missing values.
        /// </summary>
        public static JsonObject? Identification(MachineIdentification? value)
        {
            return value is null ? null : new JsonObject
            {
                ["manufacturer"] = Text(value.Manufacturer),
                ["serialNumber"] = value.SerialNumber,
                ["productInstanceUri"] = value.ProductInstanceUri,
                ["model"] = Text(value.Model),
                ["manufacturerUri"] = value.ManufacturerUri,
                ["productCode"] = value.ProductCode,
                ["hardwareRevision"] = value.HardwareRevision,
                ["softwareRevision"] = value.SoftwareRevision,
                ["deviceRevision"] = value.DeviceRevision,
                ["deviceClass"] = value.DeviceClass,
                ["assetId"] = value.AssetId,
                ["componentName"] = Text(value.ComponentName),
                ["location"] = value.Location,
                ["initialOperationDate"] = value.InitialOperationDate?.ToString("O", CultureInfo.InvariantCulture),
                ["yearOfConstruction"] = value.YearOfConstruction,
                ["monthOfConstruction"] = value.MonthOfConstruction
            };
        }

        /// <summary>
        /// Projects operation counters without boxing the open cycle-counter Variant.
        /// </summary>
        public static JsonObject? Counters(MachineryOperationCounters? value, IServiceMessageContext context)
        {
            return value is null ? null : new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["powerOnDuration"] = Number(value.PowerOnDuration),
                ["operationDuration"] = Number(value.OperationDuration),
                ["operationCycleCounter"] = McpCompanionJson.Variant(value.OperationCycleCounter, context)
            };
        }

        /// <summary>
        /// Projects lifetime counters, including non-finite warning values.
        /// </summary>
        public static JsonObject? Lifetime(MachineryLifetimeVariable? value)
        {
            return value is null ? null : new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["browseName"] = value.BrowseName.ToString(),
                ["remaining"] = Number(value.Remaining),
                ["startValue"] = Number(value.StartValue),
                ["limitValue"] = Number(value.LimitValue),
                ["warningValues"] = Array(value.WarningValues, McpCompanionJson.Number)
            };
        }

        /// <summary>
        /// Projects one equipment record and its optional lifetime.
        /// </summary>
        public static JsonObject Equipment(MachineryEquipmentItem value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["browseName"] = value.BrowseName.ToString(),
                ["equipmentTypeId"] = value.MachineryEquipmentTypeId,
                ["description"] = Text(value.Description),
                ["serialNumber"] = value.SerialNumber,
                ["equipmentLife"] = Lifetime(value.EquipmentLife)
            };
        }

        /// <summary>
        /// Projects a finite-state snapshot and its quality.
        /// </summary>
        public static JsonObject? State(FiniteStateSnapshot? value)
        {
            return value is null ? null : new JsonObject
            {
                ["stateMachineId"] = value.StateMachineId.ToString(),
                ["currentState"] = Text(value.CurrentState),
                ["currentStateId"] = value.CurrentStateId.ToString(),
                ["lastTransition"] = Text(value.LastTransition),
                ["lastTransitionId"] = value.LastTransitionId.ToString(),
                ["timestamp"] = value.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(value.Status),
                ["statusCodeValue"] = value.Status.Code,
                ["subMachine"] = State(value.SubMachine)
            };
        }

        /// <summary>
        /// Projects the complete typed process-value record.
        /// </summary>
        public static JsonObject? ProcessValue(MachineryProcessValue? value, IServiceMessageContext context)
        {
            return value is null ? null : new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["signalNodeId"] = value.SignalNodeId.ToString(),
                ["value"] = Number(value.Value),
                ["setpoint"] = Number(value.Setpoint),
                ["status"] = value.Status,
                ["alarmSuppression"] = value.AlarmSuppression,
                ["lowLowLimit"] = Number(value.LowLowLimit),
                ["lowLimit"] = Number(value.LowLimit),
                ["highLimit"] = Number(value.HighLimit),
                ["highHighLimit"] = Number(value.HighHighLimit),
                ["percentageValue"] = Number(value.PercentageValue),
                ["engineeringUnits"] = value.EngineeringUnits is null
                    ? null : McpCompanionJson.Encode(value.EngineeringUnits, context),
                ["euRange"] = value.EuRange is null ? null : McpCompanionJson.Encode(value.EuRange, context)
            };
        }

        /// <summary>
        /// Projects typed energy readings alongside their measurement variable IDs.
        /// </summary>
        public static JsonObject? Meter(MachineryMeteringPoint? value, IServiceMessageContext context)
        {
            return value is null ? null : new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["applicationTag"] = value.ApplicationTag,
                ["measurements"] = Array(value.Measurements, measurement => new JsonObject
                {
                    ["nodeId"] = measurement.NodeId.ToString(),
                    ["browseName"] = measurement.BrowseName.ToString(),
                    ["value"] = McpCompanionJson.Variant(measurement.Value, context)
                })
            };
        }

        /// <summary>
        /// Reports actual endpoint IDs; the unsupported response-receiver role is never fabricated.
        /// </summary>
        public static JsonObject Endpoints(MachineryJobManagementEndpoints value)
        {
            return new JsonObject
            {
                ["jobManagementId"] = value.JobManagementId.ToString(),
                ["jobOrderReceiverId"] = value.JobOrderReceiverId.ToString(),
                ["jobResponseProviderId"] = value.JobResponseProviderId.ToString(),
                ["jobResponseReceiverId"] = NodeId.Null.ToString(),
                ["definesResponseReceiver"] = false
            };
        }

        /// <summary>
        /// Projects predefined order parameters and preserves every raw typed parameter.
        /// </summary>
        public static JsonObject? OrderParameters(
            MachineryJobOrderParameters? value,
            IServiceMessageContext context)
        {
            return value is null ? null : new JsonObject
            {
                ["jobName"] = Array(value.JobName, Text),
                ["orderNumbers"] = Array(value.OrderNumbers, text => JsonValue.Create(text)),
                ["customers"] = Array(value.Customers, text => JsonValue.Create(text)),
                ["customerOrderNumbers"] = Array(value.CustomerOrderNumbers, text => JsonValue.Create(text)),
                ["jobExecutionMode"] = value.JobExecutionMode.HasValue ? (int)value.JobExecutionMode.Value : null,
                ["reasonForStateChange"] = Text(value.ReasonForStateChange),
                ["runsPlanned"] = value.RunsPlanned,
                ["plannedProductionTime"] = Number(value.PlannedProductionTime),
                ["plannedSetupTime"] = Number(value.PlannedSetupTime),
                ["plannedTimePerRun"] = Number(value.PlannedTimePerRun),
                ["plannedQuantityPerRun"] = Number(value.PlannedQuantityPerRun),
                ["plannedOrderQuantity"] = Number(value.PlannedOrderQuantity),
                ["overproduction"] = value.Overproduction,
                ["plannedDuration"] = Number(value.PlannedDuration),
                ["jobAnnotation"] = Array(value.JobAnnotation, Text),
                ["parameters"] = Array(value.Parameters, parameter => McpCompanionJson.Encode(parameter, context))
            };
        }

        /// <summary>
        /// Projects predefined response parameters and their complete original structures.
        /// </summary>
        public static JsonObject ResponseParameters(
            MachineryJobResponseParameters value,
            IServiceMessageContext context)
        {
            return new JsonObject
            {
                ["jobName"] = Array(value.JobName, Text),
                ["orderNumbers"] = Array(value.OrderNumbers, text => JsonValue.Create(text)),
                ["customers"] = Array(value.Customers, text => JsonValue.Create(text)),
                ["customerOrderNumbers"] = Array(value.CustomerOrderNumbers, text => JsonValue.Create(text)),
                ["jobExecutionMode"] = value.JobExecutionMode.HasValue ? (int)value.JobExecutionMode.Value : null,
                ["reasonForStateChange"] = Text(value.ReasonForStateChange),
                ["runsCompleted"] = value.RunsCompleted,
                ["runsStarted"] = value.RunsStarted,
                ["actualQuantityCurrentRun"] = Number(value.ActualQuantityCurrentRun),
                ["actualUnitBusyTime"] = Number(value.ActualUnitBusyTime),
                ["actualUnitSetupTime"] = Number(value.ActualUnitSetupTime),
                ["actualUnitDelayTime"] = Number(value.ActualUnitDelayTime),
                ["actualProductionTime"] = Number(value.ActualProductionTime),
                ["producedQuantity"] = Number(value.ProducedQuantity),
                ["estimatedRemainingTime"] = Number(value.EstimatedRemainingTime),
                ["jobResult"] = value.JobResult.HasValue ? (int)value.JobResult.Value : null,
                ["goodQuantity"] = Number(value.GoodQuantity),
                ["asBuiltBOM"] = Array(value.AsBuiltBOM, item => McpCompanionJson.Encode(item, context)),
                ["outputPerformanceInfo"] = Array(
                    value.OutputPerformanceInfo, item => McpCompanionJson.Encode(item, context)),
                ["parameters"] = Array(value.Parameters, parameter => McpCompanionJson.Encode(parameter, context))
            };
        }

        /// <summary>
        /// Projects the base-event fields without reflection over a record.
        /// </summary>
        public static JsonObject Event(BaseEventTypeRecord value)
        {
            return new JsonObject
            {
                ["eventId"] = value.EventId.IsNull ? null : value.EventId.ToBase64(),
                ["eventType"] = value.EventType.ToString(),
                ["sourceNode"] = value.SourceNode.ToString(),
                ["sourceName"] = value.SourceName,
                ["time"] = value.Time?.ToString("O", CultureInfo.InvariantCulture),
                ["receiveTime"] = value.ReceiveTime?.ToString("O", CultureInfo.InvariantCulture),
                ["message"] = Text(value.Message),
                ["severity"] = value.Severity
            };
        }

        /// <summary>
        /// Adds the structured result payload to the base-event fields.
        /// </summary>
        public static JsonObject ResultEvent(ResultReadyEventTypeRecord value, IServiceMessageContext context)
        {
            JsonObject result = Event(value);
            result["result"] = McpCompanionJson.Encode(value.Result, context);
            return result;
        }

        /// <summary>
        /// Adds the zero-adjustment status code without dropping its numeric value.
        /// </summary>
        public static JsonObject AdjustmentEvent(ZeroPointAdjustmentEventTypeRecord value)
        {
            JsonObject result = Event(value);
            result["statusCode"] = OpcUaJsonHelper.StatusCodeToString(value.ZeroPointAdjustmentResult);
            result["statusCodeValue"] = value.ZeroPointAdjustmentResult.Code;
            return result;
        }

        /// <summary>
        /// Encodes a complete result or just its metadata using the generated UA contract.
        /// </summary>
        public static JsonNode? Result(ResultDataType? value, bool metadataOnly, IServiceMessageContext context)
        {
            if (value is null)
            {
                return null;
            }
            return metadataOnly
                ? McpCompanionJson.Encode(value.ResultMetaData, context)
                : McpCompanionJson.Encode(value, context);
        }

        /// <summary>
        /// Builds the finite supported filter clauses with AND semantics and an ascending sort key.
        /// </summary>
        public static (ContentFilter Filter, ArrayOf<RelativePath> Order) ResultFilter(
            NamespaceTable namespaces,
            string? jobId,
            string? partId,
            DateTime? createdAfter,
            DateTime? createdBefore,
            MachineryResultOrder orderBy)
        {
            if (!Enum.IsDefined(orderBy))
            {
                throw new ArgumentOutOfRangeException(nameof(orderBy));
            }
            createdAfter = TimestampBound(createdAfter, nameof(createdAfter));
            createdBefore = TimestampBound(createdBefore, nameof(createdBefore));
            if (createdAfter.HasValue && createdBefore.HasValue && createdAfter > createdBefore)
            {
                throw new ArgumentException("createdAfter must not be later than createdBefore.", nameof(createdAfter));
            }
            int index = namespaces.GetIndex(Ua.Machinery.Result.Namespaces.MachineryResult);
            if (index < 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The Machinery Result model is absent.");
            }
            ushort ns = (ushort)index;
            var filter = new ContentFilter();
            ContentFilterElement? root = null;
            if (jobId is not null)
            {
                Add("JobId", FilterOperator.Equals, Variant.From(jobId));
            }
            if (partId is not null)
            {
                Add("PartId", FilterOperator.Equals, Variant.From(partId));
            }
            if (createdAfter.HasValue)
            {
                Add("CreationTime", FilterOperator.GreaterThanOrEqual, Variant.From(createdAfter.Value));
            }
            if (createdBefore.HasValue)
            {
                Add("CreationTime", FilterOperator.LessThanOrEqual, Variant.From(createdBefore.Value));
            }
            return (filter, (ArrayOf<RelativePath>)
            [
                new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            ReferenceTypeId = ReferenceTypeIds.HasComponent,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName("ResultMetaData", ns)
                        },
                        new RelativePathElement
                        {
                            ReferenceTypeId = ReferenceTypeIds.HasComponent,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName(orderBy.ToString(), ns)
                        }
                    ]
                }
            ]);

            void Add(string field, FilterOperator operation, Variant literal)
            {
                ContentFilterElement comparison = filter.Push(operation,
                    Variant.FromStructure(new SimpleAttributeOperand(
                        new NodeId(Ua.Machinery.Result.ObjectTypes.ResultReadyEventType, ns),
                        (ArrayOf<QualifiedName>)
                        [
                            new QualifiedName("Result", ns),
                            new QualifiedName("ResultMetaData", ns),
                            new QualifiedName(field, ns)
                        ])),
                    literal);
                root = root is null
                    ? comparison
                    : filter.Push(FilterOperator.And, Variant.FromStructure(root), Variant.FromStructure(comparison));
            }
        }

        /// <summary>
        /// Projects an immutable array without serializer reflection.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        public static JsonArray Array<T>(ArrayOf<T> values, Func<T, JsonNode?> project)
        {
            var array = new JsonArray();
            foreach (T value in values)
            {
                array.Add(project(value));
            }
            return array;
        }

        /// <summary>
        /// Preserves localized text and the UA null sentinel.
        /// </summary>
        private static JsonObject? Text(LocalizedText value)
        {
            return value.IsNull ? null : new JsonObject { ["text"] = value.Text, ["locale"] = value.Locale };
        }

        /// <summary>
        /// Rejects undesignated wall-clock times and compares accepted bounds as UTC instants.
        /// </summary>
        private static DateTime? TimestampBound(DateTime? value, string parameterName)
        {
            if (value.HasValue && value.Value.Kind == DateTimeKind.Unspecified)
            {
                throw new ArgumentException(
                    "A creation-time bound must include a UTC or offset designation.", parameterName);
            }
            return value?.ToUniversalTime();
        }

        /// <summary>
        /// Distinguishes missing measurements from zero and non-finite values.
        /// </summary>
        private static JsonNode? Number(double? value)
        {
            return value.HasValue ? McpCompanionJson.Number(value.Value) : null;
        }
    }
}
