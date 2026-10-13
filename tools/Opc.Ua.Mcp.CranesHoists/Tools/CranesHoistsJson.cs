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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Opc.Ua.CranesHoists;
using Opc.Ua.CranesHoists.Client;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Converts typed snapshots using the stack's UA encoder and a closed, AOT-safe JSON envelope.
    /// </summary>
    internal static class CranesHoistsJson
    {
        /// <summary>
        /// Preserves service refusal codes as MCP tool errors while propagating caller cancellation.
        /// </summary>
        internal static async Task<CallToolResult> ExecuteAsync(
            Func<Task<CallToolResult>> operation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (ServiceResultException error)
            {
                return Error(error.StatusCode, error.Message);
            }
            catch (ArgumentOutOfRangeException error)
            {
                return Error(StatusCodes.BadOutOfRange, error.Message);
            }
            catch (ArgumentException error)
            {
                return Error(StatusCodes.BadInvalidArgument, error.Message);
            }
            catch (InvalidOperationException error)
            {
                return Error(StatusCodes.BadInvalidState, error.Message);
            }
        }

        /// <summary>
        /// Enforces the exact serialized byte limit without returning truncated or partial success.
        /// </summary>
        internal static CallToolResult Result(JsonObject value, int maximumResponseBytes, bool isError = false)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, CranesHoistsJsonContext.Default.JsonObject);
            if (bytes.Length > maximumResponseBytes)
            {
                throw ServiceResultException.Create(StatusCodes.BadEncodingLimitsExceeded,
                    "The JSON response exceeds maximumResponseBytes; " +
                    "narrow the request or increase its bounded budget.");
            }
            return new CallToolResult
            {
                IsError = isError,
                Content = [new TextContentBlock { Text = Encoding.UTF8.GetString(bytes) }]
            };
        }

        /// <summary>
        /// Preserves actual instance and subtype identities, rather than replacing them with standard base NodeIds.
        /// </summary>
        internal static JsonObject Entry(CraneEntry entry)
        {
            return new JsonObject
            {
                ["nodeId"] = entry.NodeId.ToString(),
                ["browseName"] = entry.BrowseName.ToString(),
                ["displayName"] = entry.DisplayName.Text,
                ["displayLocale"] = entry.DisplayName.Locale,
                ["typeDefinition"] = entry.TypeDefinition.ToString(),
                ["kind"] = entry.Kind.ToString()
            };
        }

        /// <summary>
        /// Reports selected capability absence separately from the original status of each present reading.
        /// </summary>
        internal static JsonObject Element(CranesHoistsClient client, CraneElementSnapshot snapshot)
        {
            var signals = new JsonArray();
            foreach (CraneSignalValue signal in snapshot.Signals)
            {
                signals.Add((JsonNode)new JsonObject
                {
                    ["signal"] = signal.Signal.ToString(),
                    ["nodeId"] = signal.NodeId.IsNull ? null : signal.NodeId.ToString(),
                    ["present"] = !signal.NodeId.IsNull,
                    ["writable"] = signal.Writable,
                    ["configurationRequest"] = signal.Signal is CraneSignal.SpeedLimitGlobalEnabled or
                        CraneSignal.SpeedLimitDirMinusSetpoint or CraneSignal.SpeedLimitDirPlusSetpoint or
                        CraneSignal.SpeedLimitEnabledDirMinus or CraneSignal.SpeedLimitEnabledDirPlus,
                    ["reading"] = signal.NodeId.IsNull ? null : Reading(client, signal.Value)
                });
            }
            var relationships = new JsonArray();
            foreach (CraneRelationship relationship in snapshot.Relationships)
            {
                relationships.Add((JsonNode)new JsonObject
                {
                    ["source"] = relationship.Source.ToString(),
                    ["referenceType"] = relationship.ReferenceType.ToString(),
                    ["target"] = relationship.Target.ToString(),
                    ["isForward"] = true
                });
            }
            var functions = new JsonArray();
            foreach (EnumValueType function in snapshot.AxisFunctionValues)
            {
                functions.Add((JsonNode)new JsonObject
                {
                    ["value"] = function.Value,
                    ["displayName"] = function.DisplayName.Text,
                    ["displayLocale"] = function.DisplayName.Locale,
                    ["description"] = function.Description.Text,
                    ["descriptionLocale"] = function.Description.Locale
                });
            }
            return new JsonObject
            {
                ["entry"] = Entry(snapshot.Entry),
                ["signals"] = signals,
                ["relationships"] = relationships,
                ["axisFunctionValues"] = functions,
                ["identification"] = snapshot.Identification == null
                    ? null : Identification(client, snapshot.Identification)
            };
        }

        /// <summary>
        /// Retains standard event identity, qualified selected-field order and explicit completion/overflow state.
        /// </summary>
        internal static JsonObject Observation(CranesHoistsClient client, CraneObservation observation)
        {
            var values = new JsonArray();
            foreach (CraneObservedValue value in observation.Values)
            {
                values.Add((JsonNode)new JsonObject
                {
                    ["nodeId"] = value.NodeId.ToString(),
                    ["reading"] = Reading(client, value.Value)
                });
            }
            var events = new JsonArray();
            foreach (CraneObservedEvent value in observation.Events)
            {
                using var encoder = new JsonEncoder(client.Session.MessageContext);
                encoder.WriteVariantArray("Fields", value.Fields);
                events.Add((JsonNode)new JsonObject
                {
                    ["eventType"] = value.EventType.ToString(),
                    ["eventIdBase64"] = Convert.ToBase64String(value.EventId.Span),
                    ["fields"] = JsonNode.Parse(encoder.CloseAndReturnText())
                });
            }
            var paths = new JsonArray();
            foreach (ArrayOf<QualifiedName> path in observation.FieldPaths)
            {
                var names = new JsonArray();
                foreach (QualifiedName name in path)
                {
                    names.Add((JsonNode?)name.ToString());
                }
                paths.Add((JsonNode)names);
            }
            return new JsonObject
            {
                ["completionReason"] = observation.LimitReached ? "LimitReached" : "WindowElapsed",
                ["limitReached"] = observation.LimitReached,
                ["droppedNotifications"] = observation.DroppedNotifications,
                ["values"] = values,
                ["events"] = events,
                ["fieldPaths"] = paths
            };
        }

        /// <summary>
        /// Uses the native encoder so wire types, structured values, quality and both timestamps survive JSON.
        /// </summary>
        private static JsonNode? Reading(CranesHoistsClient client, in DataValue value)
        {
            if (value.IsNull)
            {
                return null;
            }
            using var encoder = new JsonEncoder(client.Session.MessageContext);
            encoder.WriteDataValue("DataValue", value);
            return JsonNode.Parse(encoder.CloseAndReturnText());
        }

        /// <summary>
        /// Keeps each original nameplate field and its status instead of flattening it to a string.
        /// </summary>
        private static JsonObject Identification(CranesHoistsClient client, CraneIdentificationSnapshot identification)
        {
            return new JsonObject
            {
                ["nodeId"] = identification.NodeId.ToString(),
                ["manufacturer"] = Reading(client, identification.Manufacturer),
                ["serialNumber"] = Reading(client, identification.SerialNumber),
                ["productInstanceUri"] = Reading(client, identification.ProductInstanceUri),
                ["model"] = Reading(client, identification.Model),
                ["productCode"] = Reading(client, identification.ProductCode)
            };
        }

        /// <summary>
        /// Bounds diagnostic text too, without dropping the original UA status code or reporting success.
        /// </summary>
        private static CallToolResult Error(StatusCode code, string message)
        {
            return Result(new JsonObject
            {
                ["error"] = true,
                ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(code),
                ["message"] = message.Length <= 128 ? message : message[..128]
            }, 1024, isError: true);
        }
    }

    /// <summary>
    /// Generates metadata for the closed MCP JSON envelope without reflection-based serialization.
    /// </summary>
    [JsonSerializable(typeof(JsonObject))]
    internal sealed partial class CranesHoistsJsonContext : JsonSerializerContext
    {
    }
}
