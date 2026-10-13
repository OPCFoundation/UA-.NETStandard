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
using Opc.Ua.Di;
using Opc.Ua.Di.Client;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Validates finite DI inputs and explicitly projects client records without reflection.
    /// </summary>
    internal static class DiJson
    {
        /// <summary>
        /// Parses a required local NodeId.
        /// </summary>
        internal static NodeId ParseNodeId(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            NodeId nodeId = OpcUaJsonHelper.ParseNodeId(value);
            return nodeId.IsNull
                ? throw new ArgumentException("A non-null local NodeId is required.", nameof(value))
                : nodeId;
        }

        /// <summary>
        /// Rejects undefined enum values before any server operation.
        /// </summary>
        /// <typeparam name="T">The finite selector type.</typeparam>
        internal static void RequireDefined<T>(T value, string parameterName)
            where T : struct, Enum
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "Unknown DI selector.");
            }
        }

        /// <summary>
        /// Validates the package metadata and explicitly decodes the base64 hash.
        /// </summary>
        internal static ByteString PackageHash(DiInstallPackageRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.ManufacturerUri);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.SoftwareRevision);
            ArgumentNullException.ThrowIfNull(request.HashBase64);
            if (request.PatchIdentifiers.IsNull)
            {
                throw new ArgumentException("Use an empty patchIdentifiers array instead of null.", nameof(request));
            }
            ArgumentOutOfRangeException.ThrowIfGreaterThan(request.PatchIdentifiers.Count, 500);
            foreach (string identifier in request.PatchIdentifiers)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
            }
            return ByteString.FromBase64(request.HashBase64);
        }

        /// <summary>
        /// Validates a bounded list of server file identities before installation.
        /// </summary>
        internal static ArrayOf<NodeId> FileNodeIds(DiInstallFilesRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentOutOfRangeException.ThrowIfLessThan(request.FileNodeIds.Count, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(request.FileNodeIds.Count, 500);
            return request.FileNodeIds.ConvertAll(ParseNodeId);
        }

        /// <summary>
        /// Creates the precisely typed value of an explicit writable nameplate property.
        /// </summary>
        internal static Variant PropertyWriteValue(DiWritableProperty property, string value, string? locale)
        {
            RequireDefined(property, nameof(property));
            ArgumentNullException.ThrowIfNull(value);
            if (property == DiWritableProperty.AssetId && locale is not null)
            {
                throw new ArgumentException("A locale applies only to ComponentName.", nameof(locale));
            }
            return property == DiWritableProperty.AssetId
                ? Variant.From(value)
                : Variant.From(new LocalizedText(locale, value));
        }

        /// <summary>
        /// Projects a discovered device.
        /// </summary>
        internal static JsonObject Device(DeviceEntry value)
        {
            return new JsonObject
            {
                ["deviceNodeId"] = value.DeviceId.ToString(),
                ["displayName"] = value.DisplayName,
                ["deviceClass"] = value.DeviceClass
            };
        }

        /// <summary>
        /// Projects a direct topology child, preserving its browse-name namespace.
        /// </summary>
        internal static JsonObject Topology(TopologyEntry value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["displayName"] = value.DisplayName,
                ["typeDefinitionId"] = Node(value.TypeDefinitionId),
                ["browseName"] = Name(value.BrowseName)
            };
        }

        /// <summary>
        /// Projects a functional-group identity for subsequent reads.
        /// </summary>
        internal static JsonObject Group(FunctionalGroupEntry value)
        {
            return new JsonObject { ["nodeId"] = value.NodeId.ToString(), ["displayName"] = value.DisplayName };
        }

        /// <summary>
        /// Projects the DI client's identification record without substituting empty strings.
        /// </summary>
        internal static JsonObject Identification(DeviceIdentification value)
        {
            return new JsonObject
            {
                ["manufacturer"] = value.Manufacturer,
                ["model"] = value.Model,
                ["serialNumber"] = value.SerialNumber,
                ["hardwareRevision"] = value.HardwareRevision,
                ["softwareRevision"] = value.SoftwareRevision,
                ["deviceRevision"] = value.DeviceRevision,
                ["deviceClass"] = value.DeviceClass,
                ["productInstanceUri"] = value.ProductInstanceUri
            };
        }

        /// <summary>
        /// Preserves a model-level method return status, including unknown vendor refusal codes.
        /// </summary>
        internal static JsonObject MethodStatus(NodeId nodeId, string operation, int returnStatus)
        {
            return new JsonObject
            {
                ["error"] = returnStatus != 0,
                ["succeeded"] = returnStatus == 0,
                ["nodeId"] = nodeId.ToString(),
                ["operation"] = operation,
                ["returnStatus"] = returnStatus
            };
        }

        /// <summary>
        /// Projects a bounded transfer chunk, retaining continuation, diagnostics and model-level refusals.
        /// </summary>
        internal static JsonObject TransferChunk(
            ExtensionObject payload,
            int requestedSequence,
            int maxResults,
            IServiceMessageContext context)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(requestedSequence);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maxResults, 500);
            if (payload.IsNull || !payload.TryGetValue(out IEncodeable? body, context))
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "The DI transfer result is missing.");
            }
            if (body is TransferResultErrorDataType error)
            {
                return new JsonObject
                {
                    ["error"] = true,
                    ["returnStatus"] = error.Status,
                    ["result"] = McpCompanionJson.Encode(error, context)
                };
            }
            if (body is not TransferResultDataDataType data)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Unknown DI transfer result type.");
            }
            if (data.ParameterDefs.Count > maxResults)
            {
                throw new ServiceResultException(
                    StatusCodes.BadResponseTooLarge, "The server exceeded the requested parameter-result limit.");
            }
            if (data.SequenceNumber < requestedSequence ||
                (!data.EndOfResults && data.SequenceNumber == int.MaxValue))
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Invalid DI result sequence number.");
            }
            var items = new JsonArray();
            foreach (ParameterResultDataType parameter in data.ParameterDefs)
            {
                items.Add(McpCompanionJson.Encode(parameter, context));
            }
            return new JsonObject
            {
                ["items"] = items,
                ["sequenceNumber"] = data.SequenceNumber,
                ["nextSequenceNumber"] = data.EndOfResults ? null : data.SequenceNumber + 1,
                ["complete"] = data.EndOfResults
            };
        }

        /// <summary>
        /// Projects a state snapshot and distinguishes a missing optional state machine.
        /// </summary>
        internal static JsonObject State(FiniteStateSnapshot? value)
        {
            return State(value, 0);
        }

        /// <summary>
        /// Projects a nullable OPC UA identity without using Nullable of NodeId.
        /// </summary>
        internal static string? Node(NodeId value)
        {
            return value.IsNull ? null : value.ToString();
        }

        /// <summary>
        /// Identifies an optional facet explicitly instead of reporting an empty successful result.
        /// </summary>
        internal static JsonObject Facet(NodeId nodeId)
        {
            return new JsonObject { ["supported"] = !nodeId.IsNull, ["nodeId"] = Node(nodeId) };
        }

        /// <summary>
        /// Preserves a qualified name as structured data.
        /// </summary>
        private static JsonObject Name(QualifiedName value)
        {
            return new JsonObject { ["name"] = value.Name, ["namespaceIndex"] = value.NamespaceIndex };
        }

        /// <summary>
        /// Preserves localization and null state names.
        /// </summary>
        private static JsonObject? Text(LocalizedText value)
        {
            return value.IsNull ? null : new JsonObject { ["text"] = value.Text, ["locale"] = value.Locale };
        }

        /// <summary>
        /// Bounds nested state projections while preserving all snapshot fields.
        /// </summary>
        private static JsonObject State(FiniteStateSnapshot? value, int depth)
        {
            if (value is null)
            {
                return Facet(NodeId.Null);
            }
            ArgumentOutOfRangeException.ThrowIfGreaterThan(depth, 16);
            return new JsonObject
            {
                ["supported"] = true,
                ["nodeId"] = value.StateMachineId.ToString(),
                ["currentState"] = Text(value.CurrentState),
                ["currentStateId"] = Node(value.CurrentStateId),
                ["lastTransition"] = Text(value.LastTransition),
                ["lastTransitionId"] = Node(value.LastTransitionId),
                ["timestamp"] = value.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(value.Status),
                ["statusCodeValue"] = value.Status.Code,
                ["subMachine"] = value.SubMachine is null ? null : State(value.SubMachine, depth + 1)
            };
        }
    }
}
