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
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.MachineVision;

namespace Opc.Ua.Mcp.Tools
{
    public sealed partial class VisionMachineTools
    {
        /// <summary>
        /// Discovers namespace support and actual optional interfaces without executing methods.
        /// </summary>
        [McpServerTool(Name = "vision_machine_capabilities")]
        [Description("Reports independent Part 1/Part 2 namespace support. With systemNodeId, also resolves actual " +
            "control, management, transfer, safety and step interfaces and their instance method executability. " +
            "An absent interface has available=false; a loaded namespace alone does not imply interface support.")]
        public static async Task<JsonElement> CapabilitiesAsync(
            VisionClientAccessor accessor,
            [Description("Optional Part 1 system NodeId for interface inspection; omit for namespace support only.")]
            string? systemNodeId = null,
            [Description("Connected OPC UA session name; omit when exactly one session is available.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            NodeId node = string.IsNullOrWhiteSpace(systemNodeId)
                ? NodeId.Null : OpcUaJsonHelper.ParseNodeId(systemNodeId);
            VisionClient client = accessor.CreateClient(sessionName);
            MachineVisionCapabilities capabilities = await accessor.CreateMachineVisionClient(sessionName)
                .ReadCapabilitiesAsync(node, ct).ConfigureAwait(false);
            var interfaces = new JsonArray();
            foreach (MachineVisionInterfaceCapability entry in capabilities.Interfaces)
            {
                var methods = new JsonArray();
                foreach (MachineVisionMethodCapability method in entry.Methods)
                {
                    AddNode(methods, new JsonObject
                    {
                        ["nodeId"] = method.NodeId.ToString(),
                        ["browseName"] = method.BrowseName.ToString(),
                        ["executable"] = Reading(client, method.Executable),
                        ["userExecutable"] = Reading(client, method.UserExecutable)
                    });
                }
                AddNode(interfaces, new JsonObject
                {
                    ["path"] = entry.Path,
                    ["nodeId"] = entry.NodeId.IsNull ? null : entry.NodeId.ToString(),
                    ["available"] = !entry.NodeId.IsNull,
                    ["methods"] = methods
                });
            }
            return Element(new JsonObject
            {
                ["supportsControl"] = capabilities.SupportsControl,
                ["supportsAssets"] = capabilities.SupportsAssets,
                ["interfaces"] = interfaces
            });
        }

        /// <summary>
        /// Reads one bounded page of a selected relationship.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentException"></exception>
        [McpServerTool(Name = "vision_machine_relationships")]
        [Description("Reads one-hop SameEntity, OpticalPath, DataTransmission, or Interface relationships. " +
            "direction is Forward, Inverse, or Both. Preserves reference direction, namespaces, and vendor types. " +
            "Use nextIndex when isComplete=false; pages are live views, not a snapshot across topology edits.")]
        public static async Task<JsonElement> RelationshipsAsync(
            VisionClientAccessor accessor,
            [Description("Source system, asset or component NodeId whose one-hop relationships should be read.")]
            string nodeId,
            [Description("SameEntity, OpticalPath, DataTransmission, or Interface; default SameEntity.")]
            string relationship = "SameEntity",
            [Description("Reference direction: Forward, Inverse, or Both; default Both.")]
            string direction = "Both",
            [Description("Maximum references returned in this page, from 1 to 100; default 50.")]
            [Range(1, 100)] uint maximumResults = 50,
            [Description("Zero-based reference offset, from 0 to 10000; use nextIndex to request another page.")]
            [Range(0, 10000)] uint startIndex = 0,
            [Description("Connected OPC UA session name; omit when exactly one session is available.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            ValidatePage(maximumResults);
            if (startIndex > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            }
            MachineVisionRelationship kind = relationship switch
            {
                "SameEntity" => MachineVisionRelationship.SameEntity,
                "OpticalPath" => MachineVisionRelationship.OpticalPath,
                "DataTransmission" => MachineVisionRelationship.DataTransmission,
                "Interface" => MachineVisionRelationship.Interface,
                _ => throw new ArgumentException("Unknown relationship.", nameof(relationship))
            };
            BrowseDirection browseDirection = direction switch
            {
                "Forward" => BrowseDirection.Forward,
                "Inverse" => BrowseDirection.Inverse,
                "Both" => BrowseDirection.Both,
                _ => throw new ArgumentException("Unknown browse direction.", nameof(direction))
            };
            MachineVisionRelationshipPage page = await accessor.CreateMachineVisionClient(sessionName)
                .ReadRelationshipsAsync(OpcUaJsonHelper.ParseNodeId(nodeId), kind, browseDirection,
                    maximumResults, startIndex, ct).ConfigureAwait(false);
            var references = new JsonArray();
            foreach (ReferenceDescription reference in page.References)
            {
                AddNode(references, new JsonObject
                {
                    ["nodeId"] = reference.NodeId.ToString(),
                    ["browseName"] = reference.BrowseName.ToString(),
                    ["displayName"] = reference.DisplayName.Text,
                    ["referenceTypeId"] = reference.ReferenceTypeId.ToString(),
                    ["isForward"] = reference.IsForward,
                    ["typeDefinition"] = reference.TypeDefinition.ToString(),
                    ["nodeClass"] = reference.NodeClass.ToString()
                });
            }
            return Element(new JsonObject
            {
                ["references"] = references,
                ["nextIndex"] = page.NextIndex,
                ["isComplete"] = page.IsComplete
            });
        }

        /// <summary>
        /// Collects a bounded event window using a subscription owned only by this call.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        [McpServerTool(Name = "vision_machine_observe")]
        [Description("Observes Events or Conditions for 5 seconds/50 notifications by default, at most " +
            "60 seconds/100 notifications. refreshConditions requests retained conditions in Conditions mode. " +
            "Returns exact eventIdBase64/conditionNodeId selectors and namespace-qualified fields. " +
            "refreshComplete=false is not a complete retained-condition snapshot. " +
            "Always cleans up its subscription; " +
            "never acknowledges conditions, starts acquisition, or leaves a monitoring handle.")]
        public static async Task<JsonElement> ObserveAsync(
            VisionClientAccessor accessor,
            [Description("Event-notifier NodeId to monitor, such as i=2253 for the Server object.")]
            string notifierNodeId,
            [Description("Events or Conditions; Conditions selects condition fields needed for acknowledgement.")]
            string mode = "Events",
            [Description("Request a retained-condition refresh; only valid in Conditions mode, default false.")]
            bool refreshConditions = false,
            [Description("Observation window in seconds, from 1 to 60; default 5.")]
            [Range(1, 60)] int durationSeconds = 5,
            [Description("Maximum notifications to retain, from 1 to 100; default 50.")]
            [Range(1, 100)] int maximumNotifications = 50,
            [Description("Connected OPC UA session name; omit when exactly one session is available.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            var options = new MachineVisionObservationOptions
            {
                Mode = mode switch
                {
                    "Events" => MachineVisionObservationMode.Events,
                    "Conditions" => MachineVisionObservationMode.Conditions,
                    _ => throw new ArgumentException("Unknown observation mode.", nameof(mode))
                },
                RefreshConditions = refreshConditions,
                Window = TimeSpan.FromSeconds(durationSeconds),
                MaximumNotifications = maximumNotifications
            };
            options.Validate();
            VisionClient client = accessor.CreateClient(sessionName);
            MachineVisionObservation observation = await accessor.CreateMachineVisionClient(sessionName)
                .ObserveAsync(OpcUaJsonHelper.ParseNodeId(notifierNodeId), options, ct).ConfigureAwait(false);
            var paths = new JsonArray();
            foreach (ArrayOf<QualifiedName> path in observation.FieldPaths)
            {
                var names = new JsonArray();
                foreach (QualifiedName name in path)
                {
                    AddNode(names, JsonValue.Create(name.ToString()));
                }
                AddNode(paths, names);
            }
            var events = new JsonArray();
            foreach (MachineVisionObservedEvent notification in observation.Events)
            {
                using var encoder = new JsonEncoder(client.Session.MessageContext);
                encoder.WriteVariantArray("Fields", notification.Fields);
                AddNode(events, new JsonObject
                {
                    ["eventType"] = notification.EventType.ToString(),
                    ["eventIdBase64"] = Convert.ToBase64String(notification.EventId.Span),
                    ["conditionNodeId"] = notification.ConditionId.IsNull ? null : notification.ConditionId.ToString(),
                    ["data"] = JsonNode.Parse(encoder.CloseAndReturnText())
                });
            }
            return Element(new JsonObject
            {
                ["completionReason"] = observation.LimitReached ? "LimitReached" : "WindowElapsed",
                ["refreshComplete"] = observation.RefreshComplete,
                ["droppedNotifications"] = observation.DroppedNotifications,
                ["fieldPaths"] = paths,
                ["events"] = events
            });
        }

        /// <summary>
        /// Registers and transfers a bounded recipe, waiting for commit but not preparing it.
        /// </summary>
        [McpServerTool(Name = "vision_machine_upload_recipe")]
        [Description("Registers recipe metadata and uploads at most 1 MiB of decoded base64 content when required. " +
            "Waits for server commit completion. Does not prepare the recipe, change mode, or start a job. " +
            "Use vision_machine_file for larger uploads. identifierJson preserves complete UA identifier data.")]
        public static async Task<JsonElement> UploadRecipeAsync(
            VisionClientAccessor accessor,
            [Description("Part 1 system NodeId returned by vision_machine_list, such as ns=2;s=VisionSystem.")]
            string systemNodeId,
            [Description("Complete base64 recipe content, at most 1048576 decoded bytes; may be empty.")]
            string dataBase64,
            [Description("External recipe ID to register, unless supplied in identifierJson; not a NodeId.")]
            string externalId = "",
            [Description("Optional product identifier to associate with the recipe; default empty identifier.")]
            string productId = "",
            [Description("External recipe version used with externalId; empty leaves the version unspecified.")]
            string version = "",
            [Description("Optional UA JSON recipe identifier with Id, Version, Hash, HashAlgorithm and Description.")]
            string? identifierJson = null,
            [Description("Connected OPC UA session name; omit when exactly one session is available.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            ValidateInputJson(identifierJson);
            using var content = new MemoryStream(DecodeContent(dataBase64), writable: false);
            VisionClient client = accessor.CreateClient(sessionName);
            RecipeIdExternalDataType identifier =
                Identifier<RecipeIdExternalDataType>(client, externalId, version, identifierJson);
            RecipeIdInternalDataType id = await System(client, systemNodeId).UploadRecipeAsync(
                identifier, new ProductIdDataType { Id = productId }, content, ct).ConfigureAwait(false);
            return Response(client, ("InternalId", Variant.FromStructure(id)), ("Error", Variant.From(0)));
        }

        /// <summary>
        /// Registers and transfers a bounded configuration without activating it.
        /// </summary>
        [McpServerTool(Name = "vision_machine_upload_configuration")]
        [Description("Registers and uploads a configuration with at most 1 MiB of decoded base64 content. " +
            "Waits for asynchronous server commit completion; activation remains a separate explicit command. " +
            "Use vision_machine_file for larger chunked content. identifierJson preserves version/hash metadata.")]
        public static async Task<JsonElement> UploadConfigurationAsync(
            VisionClientAccessor accessor,
            [Description("Part 1 system NodeId returned by vision_machine_list, such as ns=2;s=VisionSystem.")]
            string systemNodeId,
            [Description("Complete base64 configuration content, at most 1048576 decoded bytes; may be empty.")]
            string dataBase64,
            [Description("External configuration ID to register, unless supplied in identifierJson; not a NodeId.")]
            string externalId = "",
            [Description("Configuration version used with externalId; empty leaves the version unspecified.")]
            string version = "",
            [Description("UA JSON configuration identifier with Id, Version, Hash, HashAlgorithm and Description.")]
            string? identifierJson = null,
            [Description("Connected OPC UA session name; omit when exactly one session is available.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            ValidateInputJson(identifierJson);
            using var content = new MemoryStream(DecodeContent(dataBase64), writable: false);
            VisionClient client = accessor.CreateClient(sessionName);
            ConfigurationIdDataType identifier =
                Identifier<ConfigurationIdDataType>(client, externalId, version, identifierJson);
            ConfigurationIdDataType id = await System(client, systemNodeId).UploadConfigurationAsync(
                identifier, content, ct).ConfigureAwait(false);
            return Response(client, ("InternalId", Variant.FromStructure(id)), ("Error", Variant.From(0)));
        }

        /// <summary>
        /// Downloads an entire bounded result and releases its file and metadata handles.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [McpServerTool(Name = "vision_machine_download_result")]
        [Description("Downloads complete result file bytes and releases file/metadata handles " +
            "on success or failure. " +
            "maximumBytes is 1-1048576; exceeding it fails rather than returning truncated content. " +
            "Use the chunked file tool for larger results. Returns Data as an OPC UA ByteString.")]
        public static async Task<JsonElement> DownloadResultAsync(
            VisionClientAccessor accessor,
            [Description("Part 1 system NodeId returned by vision_machine_list, such as ns=2;s=VisionSystem.")]
            string systemNodeId,
            [Description("Result identifier returned by List/Get in vision_machine_results; not the result's NodeId.")]
            string resultId,
            [Description("Maximum complete result bytes (1-1048576); larger results fail without truncation.")]
            [Range(1, 1048576)] int maximumBytes = 1048576,
            [Description("Connected OPC UA session name; omit when exactly one session is available.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            if (maximumBytes is < 1 or > 1048576)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }
            using var content = new BoundedResultStream(maximumBytes);
            VisionClient client = accessor.CreateClient(sessionName);
            await System(client, systemNodeId).DownloadResultAsync(
                new ResultIdDataType { Id = resultId }, content, ct).ConfigureAwait(false);
            return Response(client, ("Data", Variant.From(ByteString.From(content.ToArray()))),
                ("Length", Variant.From(content.Length)));
        }

        private static JsonObject StatusDetails(VisionClient client, MachineVisionStatusSnapshot snapshot)
        {
            var values = new JsonArray();
            foreach (MachineVisionStatusValue value in snapshot.Values)
            {
                AddNode(values, new JsonObject
                {
                    ["path"] = value.Path,
                    ["nodeId"] = value.NodeId.IsNull ? null : value.NodeId.ToString(),
                    ["reading"] = Reading(client, value.Value)
                });
            }
            var steps = new JsonArray();
            foreach (MachineVisionStepSnapshot step in snapshot.Steps)
            {
                AddNode(steps, new JsonObject
                {
                    ["path"] = step.Path,
                    ["nodeId"] = step.NodeId.ToString(),
                    ["state"] = State(step.State)
                });
            }
            return new JsonObject { ["values"] = values, ["steps"] = steps };
        }

        private static JsonNode? Reading(VisionClient client, in DataValue value)
        {
            using var encoder = new JsonEncoder(client.Session.MessageContext);
            encoder.WriteDataValue("DataValue", value);
            return JsonNode.Parse(encoder.CloseAndReturnText());
        }

        private static byte[] DecodeContent(string dataBase64)
        {
            ArgumentNullException.ThrowIfNull(dataBase64);
            if (dataBase64.Length > 1398104)
            {
                throw new ArgumentOutOfRangeException(nameof(dataBase64), "Use file chunks for content over 1 MiB.");
            }
            byte[] bytes = Convert.FromBase64String(dataBase64);
            if (bytes.Length > 1048576)
            {
                throw new ArgumentOutOfRangeException(nameof(dataBase64), "Use file chunks for content over 1 MiB.");
            }
            return bytes;
        }

        /// <summary>
        /// Enforces the content limit before a download can expand its destination buffer.
        /// </summary>
        private sealed class BoundedResultStream(int maximumBytes) : MemoryStream
        {
            public override void Write(byte[] buffer, int offset, int count)
            {
                CheckLength(count);
                base.Write(buffer, offset, count);
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                CheckLength(buffer.Length);
                base.Write(buffer);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                CheckLength(count);
                return base.WriteAsync(buffer, offset, count, cancellationToken);
            }

            public override ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                CheckLength(buffer.Length);
                return base.WriteAsync(buffer, cancellationToken);
            }

            private void CheckLength(int count)
            {
                if (count > maximumBytes - Position)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "The result exceeds maximumBytes; use the chunked file tool.");
                }
            }
        }
    }
}
