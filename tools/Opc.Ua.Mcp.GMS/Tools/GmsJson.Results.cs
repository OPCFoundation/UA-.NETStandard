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
using Opc.Ua.GMS;
using Opc.Ua.GMS.Client;
using Opc.Ua.Machinery.Result;
using Opc.Ua.MachineTool;

namespace Opc.Ua.Mcp.Tools
{
    internal sealed partial class GmsJson
    {
        /// <summary>
        /// Encodes method-returned metadata/content without inventing sample acquisition quality.
        /// </summary>
        internal JsonObject ResultData(ResultDataType value)
        {
            return new JsonObject
            {
                ["resultId"] = value.ResultMetaData?.ResultId,
                ["result"] = Value(Variant.FromStructure(value)),
                ["acquisitionQualityProvided"] = false
            };
        }

        /// <summary>
        /// Preserves actual ring-slot acquisition metadata and optional UsedTools presence.
        /// </summary>
        internal JsonObject Published(ArrayOf<GmsResultVariableValue> values)
        {
            return new JsonObject
            {
                ["present"] = !values.IsNull,
                ["results"] = values.IsNull ? null : Array(values, value => new JsonObject
                {
                    ["nodeId"] = value.NodeId.ToString(),
                    ["reading"] = Reading(value.Value),
                    ["usedToolsPresent"] = !value.UsedTools.IsNull,
                    ["usedTools"] = Reading(value.UsedTools)
                })
            };
        }

        /// <summary>
        /// Reports calculated relative and absolute corrections without treating them as applied offsets.
        /// </summary>
        internal JsonObject Corrections(ArrayOf<GmsCorrectionValues> values)
        {
            return new JsonObject
            {
                ["present"] = !values.IsNull,
                ["actuationRequested"] = false,
                ["corrections"] = values.IsNull ? null : Array(values, value => new JsonObject
                {
                    ["nodeId"] = value.NodeId.ToString(),
                    ["identifier"] = Reading(value.Identifier),
                    ["characteristicIdentifier"] = Reading(value.CharacteristicIdentifier),
                    ["relativeValue"] = Analog(value.RelativeValue),
                    ["absoluteValue"] = Analog(value.AbsoluteValue),
                    ["lowerControlLimit"] = Analog(value.LowerControlLimit),
                    ["upperControlLimit"] = Analog(value.UpperControlLimit),
                    ["description"] = Reading(value.Description),
                    ["programName"] = Reading(value.ProgramName)
                })
            };
        }

        /// <summary>
        /// Preserves native data notifications and their publication order without claiming a complete history.
        /// </summary>
        internal JsonObject Observation(MachineToolDataObservation value)
        {
            return new JsonObject
            {
                ["completionReason"] = value.LimitReached ? "LimitReached" : "WindowElapsed",
                ["limitReached"] = value.LimitReached,
                ["droppedNotifications"] = value.DroppedNotifications,
                ["changes"] = Array(value.Changes, change => new JsonObject
                {
                    ["nodeId"] = change.NodeId.ToString(),
                    ["reading"] = Reading(change.Value),
                    ["publishTime"] = change.PublishTime.ToString("O", CultureInfo.InvariantCulture),
                    ["sequenceNumber"] = change.SequenceNumber
                })
            };
        }

        /// <summary>
        /// Preserves qualified native fields and whether an initial retained refresh actually completed.
        /// </summary>
        internal JsonObject Events(MachineToolEventObservation value)
        {
            return new JsonObject
            {
                ["limitReached"] = value.LimitReached,
                ["droppedNotifications"] = value.DroppedNotifications,
                ["refreshCompleted"] = value.RefreshCompleted,
                ["events"] = Array(value.Events, entry => new JsonObject
                {
                    ["notifierNodeId"] = entry.NotifierId.ToString(),
                    ["eventType"] = entry.Event.EventType.ToString(),
                    ["eventIdBase64"] = Convert.ToBase64String(entry.Event.EventId.Span),
                    ["nativeSensorWarning"] = entry.Event is SensorWarningAlarmTypeRecord,
                    ["sensorErrorCode"] = entry.Event is SensorWarningAlarmTypeRecord native ? native.ErrorCode : null,
                    ["publishTime"] = entry.PublishTime.ToString("O", CultureInfo.InvariantCulture),
                    ["sequenceNumber"] = entry.SequenceNumber,
                    ["fields"] = Array(entry.Fields, field => new JsonObject
                    {
                        ["browsePath"] = Array(field.BrowsePath, name => JsonValue.Create(name.ToString())),
                        ["value"] = Value(field.Value)
                    })
                })
            };
        }

        /// <summary>
        /// Encodes typed intermediate characteristics, tolerances and explicit instance namespace qualification.
        /// </summary>
        internal JsonObject Intermediate(GmsIntermediateResultNotification value)
        {
            IntermediateResultEventTypeRecord record = value.Event;
            return new JsonObject
            {
                ["eventType"] = record.EventType.ToString(),
                ["eventIdBase64"] = Convert.ToBase64String(record.EventId.Span),
                ["sourceNodeId"] = record.SourceNode.ToString(),
                ["time"] = record.Time?.ToString("O", CultureInfo.InvariantCulture),
                ["receiveTime"] = record.ReceiveTime?.ToString("O", CultureInfo.InvariantCulture),
                ["programName"] = record.ProgramName,
                ["result"] = record.Result == null ? null : Value(Variant.FromStructure(record.Result)),
                ["acquisitionQualityProvided"] = false,
                ["characteristics"] = Array(value.Characteristics, characteristic => new JsonObject
                {
                    ["name"] = characteristic.Name.Name,
                    ["namespaceIndex"] = characteristic.Name.NamespaceIndex,
                    ["identifier"] = Value(characteristic.Identifier),
                    ["resultValue"] = Value(characteristic.ResultValue),
                    ["engineeringUnits"] = Value(characteristic.EngineeringUnits),
                    ["upperTolerance"] = Value(characteristic.UpperTolerance),
                    ["upperToleranceUnits"] = Value(characteristic.UpperToleranceUnits),
                    ["upperToleranceForm"] = Value(characteristic.UpperToleranceForm),
                    ["lowerTolerance"] = Value(characteristic.LowerTolerance),
                    ["lowerToleranceUnits"] = Value(characteristic.LowerToleranceUnits),
                    ["lowerToleranceForm"] = Value(characteristic.LowerToleranceForm),
                    ["nominal"] = Value(characteristic.Nominal),
                    ["nominalUnits"] = Value(characteristic.NominalUnits),
                    ["characteristicsClass"] = Value(characteristic.CharacteristicsClass),
                    ["evaluation"] = Value(characteristic.Evaluation),
                    ["isValid"] = Value(characteristic.IsValid),
                    ["formula"] = Value(characteristic.Formula)
                })
            };
        }

        /// <summary>
        /// Retains engineering-unit quality independently from the associated numeric sample.
        /// </summary>
        private JsonObject? Analog(GmsAnalogReading? value)
        {
            return value == null ? null : new JsonObject
            {
                ["reading"] = Reading(value.Value),
                ["engineeringUnits"] = Reading(value.EngineeringUnits)
            };
        }
    }
}
