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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.MachineVision;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Released OPC 40100 control, management, asset, and file tools in the Vision catalog.
    /// Perception tools have independent names and contracts.
    /// </summary>
    [McpServerToolType]
    public sealed partial class VisionMachineTools
    {
        /// <summary>
        /// Discovers released control systems or system assets.
        /// </summary>
        [McpServerTool(Name = "vision_machine_list")]
        [Description("Discovers released OPC 40100-1 systems, or OPC 40100-2 assets when assets=true. " +
            "Works without the Perception namespace. Does not change device state.")]
        public static async Task<JsonElement> ListAsync(
            VisionClientAccessor accessor,
            bool assets = false,
            string? rootNodeId = null,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            MachineVisionClient client = accessor.CreateClient(sessionName).MachineVision();
            NodeId root = string.IsNullOrWhiteSpace(rootNodeId) ? NodeId.Null : OpcUaJsonHelper.ParseNodeId(rootNodeId);
            var entries = new JsonArray();
            await foreach (VisionNodeEntry entry in (assets
                ? client.EnumerateAssetsAsync(root, cancellationToken: ct)
                : client.EnumerateSystemsAsync(root, cancellationToken: ct)).ConfigureAwait(false))
            {
                AddNode(entries, new JsonObject
                {
                    ["nodeId"] = entry.NodeId.ToString(),
                    ["browseName"] = entry.BrowseName.ToString(),
                    ["displayName"] = entry.DisplayName.Text,
                    ["typeDefinition"] = entry.TypeDefinitionId.ToString()
                });
            }
            return Element(entries);
        }

        /// <summary>
        /// Reads the two released control state machines.
        /// </summary>
        public static Task<JsonElement> ReadStateAsync(
            VisionClientAccessor accessor,
            string systemNodeId,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return ReadStateWithDetailsAsync(accessor, systemNodeId, false, sessionName, ct);
        }

        /// <summary>
        /// Reads the existing state response with optional diagnostics, safety, configuration, and step details.
        /// </summary>
        [McpServerTool(Name = "vision_machine_read_state")]
        [Description("Reads control and automatic-mode states without changing device state. " +
            "includeDetails adds optional diagnostics, SystemState, active configuration, safety, and step states " +
            "with their actual quality/status. The default response is unchanged.")]
        public static async Task<JsonElement> ReadStateWithDetailsAsync(
            VisionClientAccessor accessor,
            string systemNodeId,
            bool includeDetails = false,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionClient client = accessor.CreateClient(sessionName);
            MachineVisionSystemClient system = System(client, systemNodeId);
            VisionStateMachineTypeClient control = await system.ControlAsync(ct).ConfigureAwait(false);
            VisionAutomaticModeStateMachineTypeClient automatic = await system.AutomaticAsync(ct).ConfigureAwait(false);
            FiniteStateSnapshot top = await control.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false);
            FiniteStateSnapshot mode = await automatic.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false);
            var response = new JsonObject
            {
                ["control"] = State(top),
                ["automatic"] = State(mode)
            };
            if (includeDetails)
            {
                MachineVisionStatusSnapshot details = await accessor.CreateMachineVisionClient(sessionName)
                    .ReadStatusAsync(OpcUaJsonHelper.ParseNodeId(systemNodeId), ct).ConfigureAwait(false);
                response["details"] = StatusDetails(client, details);
            }
            return Element(response);
        }

        /// <summary>
        /// Executes one explicit state-machine command.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        [McpServerTool(Name = "vision_machine_command")]
        [Description("Explicit OPC 40100-1 command: SelectAutomatic, Halt, Reset, Stop, Abort, StartSingleJob, " +
            "StartContinuous, EnableSimulation, DisableSimulation, ConfirmAll, or Sync. " +
            "Sync uses a step-model NodeId as systemNodeId. " +
            "Returns the domain Error separately from service failures. " +
            "Never retries or changes mode implicitly.")]
        public static async Task<JsonElement> CommandAsync(
            VisionClientAccessor accessor,
            string systemNodeId,
            string command,
            string? recipeId = null,
            string? productId = null,
            string? measurementId = null,
            string? partId = null,
            int cause = 0,
            string causeDescription = "",
            [Description("Optional UA JSON object containing a Parameters Variant array and optionally an " +
                "ExternalRecipeId (Id, Version, Hash, HashAlgorithm, Description), as written by JsonEncoder.")]
            string? parametersJson = null,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            ValidateInputJson(parametersJson, "Parameters");
            VisionClient client = accessor.CreateClient(sessionName);
            if (command == "Sync")
            {
                var step = new VisionStepModelStateMachineTypeClient(
                    client.Session, OpcUaJsonHelper.ParseNodeId(systemNodeId), client.Telemetry);
                return Response(client, ("Error", Variant.From(await step.SyncAsync(cause, causeDescription, ct)
                    .ConfigureAwait(false))));
            }
            MachineVisionSystemClient system = System(client, systemNodeId);
            VisionStateMachineTypeClient control = await system.ControlAsync(ct).ConfigureAwait(false);
            if (command == "ConfirmAll")
            {
                await control.ConfirmAllAsync(new LocalizedText(causeDescription), ct).ConfigureAwait(false);
                return Response(client, ("Error", Variant.From(0)));
            }
            if (command is "StartSingleJob" or "StartContinuous")
            {
                VisionAutomaticModeStateMachineTypeClient mode = await system.AutomaticAsync(ct).ConfigureAwait(false);
                var measurement = new MeasIdDataType { Id = measurementId ?? string.Empty };
                var part = new PartIdDataType { Id = partId ?? string.Empty };
                var product = new ProductIdDataType { Id = productId ?? string.Empty };
                using JsonDecoder? parameters = parametersJson == null
                    ? null : new JsonDecoder(parametersJson, client.Session.MessageContext);
                RecipeIdExternalDataType recipe =
                    parameters?.ReadEncodeable<RecipeIdExternalDataType>("ExternalRecipeId") ?? new();
                if (!string.IsNullOrEmpty(recipeId))
                {
                    if (!string.IsNullOrEmpty(recipe.Id) && recipe.Id != recipeId)
                    {
                        throw new ArgumentException("recipeId and ExternalRecipeId disagree.", nameof(recipeId));
                    }
                    recipe.Id = recipeId;
                }
                ArrayOf<Variant> values = default;
                if (parameters is { } decoder)
                {
                    values = decoder.ReadVariantArray("Parameters");
                }
                (JobIdDataType jobId, int error) result = command == "StartSingleJob"
                    ? await mode.StartSingleJobAsync(measurement, part, recipe, product, values, ct)
                        .ConfigureAwait(false)
                    : await mode.StartContinuousAsync(measurement, part, recipe, product, values, ct)
                        .ConfigureAwait(false);
                return Response(client, ("JobId", Variant.FromStructure(result.jobId)),
                    ("Error", Variant.From(result.error)));
            }
            int error;
            if (command == "SelectAutomatic")
            {
                error = await control.SelectModeAutomaticAsync(ct).ConfigureAwait(false);
            }
            else if (command == "Halt")
            {
                error = await control.HaltAsync(cause, causeDescription, ct).ConfigureAwait(false);
            }
            else if (command == "Reset")
            {
                error = await control.ResetAsync(cause, causeDescription, ct).ConfigureAwait(false);
            }
            else
            {
                VisionAutomaticModeStateMachineTypeClient mode = await system.AutomaticAsync(ct).ConfigureAwait(false);
                error = command switch
                {
                    "Stop" => await mode.StopAsync(cause, causeDescription, ct).ConfigureAwait(false),
                    "Abort" => await mode.AbortAsync(cause, causeDescription, ct).ConfigureAwait(false),
                    "EnableSimulation" => await mode.SimulationModeAsync(true, cause, causeDescription, ct)
                        .ConfigureAwait(false),
                    "DisableSimulation" => await mode.SimulationModeAsync(false, cause, causeDescription, ct)
                        .ConfigureAwait(false),
                    _ => throw new ArgumentException("Unknown Machine Vision command.", nameof(command))
                };
            }
            return Response(client, ("Error", Variant.From(error)));
        }

        /// <summary>
        /// Reads a bounded, namespace-qualified asset or management subtree.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [McpServerTool(Name = "vision_machine_read_asset")]
        [Description("Reads OPC 40100-2 identification, inventory, maintenance, health, " +
            "and component-specific values. " +
            "Can be rooted at an individual item or group. isComplete=false explicitly reports a depth/value bound. " +
            "Returned NodeIds can be monitored with standard OPC UA subscriptions.")]
        public static async Task<JsonElement> ReadAssetAsync(
            VisionClientAccessor accessor,
            string assetNodeId,
            [Range(0, 32)] int maximumDepth = 5,
            [Range(1, 1024)] int maximumValues = 128,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            if (maximumValues is <= 0 or > 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumValues));
            }
            VisionClient client = accessor.CreateClient(sessionName);
            MachineVisionAssetSnapshot snapshot = await client.MachineVision().ReadAssetAsync(
                OpcUaJsonHelper.ParseNodeId(assetNodeId), maximumDepth, maximumValues, ct).ConfigureAwait(false);
            var values = new JsonArray();
            foreach (MachineVisionAssetValue value in snapshot.Values)
            {
                var path = new JsonArray();
                foreach (QualifiedName element in value.BrowsePath)
                {
                    AddNode(path, JsonValue.Create(element.ToString()));
                }
                using var encoder = new JsonEncoder(client.Session.MessageContext);
                encoder.WriteDataValue("DataValue", value.Value);
                AddNode(values, new JsonObject
                {
                    ["nodeId"] = value.NodeId.ToString(),
                    ["path"] = path,
                    ["typeDefinition"] = value.TypeDefinition.ToString(),
                    ["reading"] = JsonNode.Parse(encoder.CloseAndReturnText())
                });
            }
            return Element(new JsonObject { ["isComplete"] = snapshot.IsComplete, ["values"] = values });
        }

        /// <summary>
        /// Executes one recipe/product metadata operation.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentException"></exception>
        [McpServerTool(Name = "vision_machine_recipes")]
        [Description("OPC 40100-1 recipe/product operation: List, Add, Prepare, Unprepare, Remove, PrepareProduct, " +
            "UnprepareProduct, UnlinkProduct, or Release. List is bounded to 1-100 records. " +
            "Add only registers metadata; transferRequired=true requires the file tool before preparation. " +
            "Error is the domain result, not a service status.")]
        public static async Task<JsonElement> RecipesAsync(
            VisionClientAccessor accessor,
            string systemNodeId,
            string operation,
            string externalId = "",
            string internalId = "",
            string productId = "",
            string version = "",
            [Range(1, 100)] uint maximumResults = 50,
            uint startIndex = 0,
            uint handle = 0,
            [Range(0, 2)] int preparedState = 2,
            [Description("Optional UA JSON recipe identifier including " +
                "Id, Version, Hash, HashAlgorithm, and Description.")]
            string? identifierJson = null,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            ValidatePage(maximumResults);
            ValidateInputJson(identifierJson);
            if (preparedState is < 0 or > 2)
            {
                throw new ArgumentOutOfRangeException(nameof(preparedState));
            }
            VisionClient client = accessor.CreateClient(sessionName);
            RecipeManagementTypeClient recipes = await System(client, systemNodeId).RecipesAsync(ct)
                .ConfigureAwait(false);
            RecipeIdExternalDataType external = Identifier<RecipeIdExternalDataType>(
                client, externalId, version, identifierJson);
            var internalIdentifier = new RecipeIdInternalDataType { Id = internalId };
            var product = new ProductIdDataType { Id = productId };
            switch (operation)
            {
                case "List":
                    (bool isComplete, uint resultCount, uint recipeHandle,
                        ArrayOf<RecipeIdInternalDataType> recipeList, int error) = await recipes
                        .GetRecipeListFilteredAsync(external, product, (TriStateBooleanDataType)preparedState,
                            maximumResults, startIndex, -1, ct)
                        .ConfigureAwait(false);
                    return Response(client, ("IsComplete", Variant.From(isComplete)),
                        ("ResultCount", Variant.From(resultCount)), ("Handle", Variant.From(recipeHandle)),
                        ("Recipes", Variant.FromStructure(recipeList)), ("Error", Variant.From(error)));
                case "Add":
                    (RecipeIdInternalDataType internalId, NodeId recipe, NodeId product,
                        bool transferRequired, int error) added =
                        await recipes.AddRecipeAsync(external, product, ct).ConfigureAwait(false);
                    return Response(client, ("InternalId", Variant.FromStructure(added.internalId)),
                        ("RecipeNodeId", Variant.From(added.recipe)), ("ProductNodeId", Variant.From(added.product)),
                        ("TransferRequired", Variant.From(added.transferRequired)),
                        ("Error", Variant.From(added.error)));
                case "Prepare":
                    (RecipeIdInternalDataType internalIdOut, bool isCompleted, int error) prepared =
                        await recipes.PrepareRecipeAsync(external, internalIdentifier, ct).ConfigureAwait(false);
                    return Response(client, ("InternalId", Variant.FromStructure(prepared.internalIdOut)),
                        ("IsCompleted", Variant.From(prepared.isCompleted)), ("Error", Variant.From(prepared.error)));
                case "Unprepare":
                    (RecipeIdInternalDataType internalIdOut, int error) unprepared =
                        await recipes.UnprepareRecipeAsync(external, internalIdentifier, ct).ConfigureAwait(false);
                    return Response(client, ("InternalId", Variant.FromStructure(unprepared.internalIdOut)),
                        ("Error", Variant.From(unprepared.error)));
                case "PrepareProduct":
                    (RecipeIdInternalDataType internalId, int error) prepareProduct =
                        await recipes.PrepareProductAsync(product, ct).ConfigureAwait(false);
                    return Response(client, ("InternalId", Variant.FromStructure(prepareProduct.internalId)),
                        ("Error", Variant.From(prepareProduct.error)));
                case "UnprepareProduct":
                    (RecipeIdInternalDataType internalId, int error) unprepareProduct =
                        await recipes.UnprepareProductAsync(product, ct).ConfigureAwait(false);
                    return Response(client, ("InternalId", Variant.FromStructure(unprepareProduct.internalId)),
                        ("Error", Variant.From(unprepareProduct.error)));
                case "Remove":
                    return Response(client, ("Error",
                        Variant.From(await recipes.RemoveRecipeAsync(external, ct).ConfigureAwait(false))));
                case "UnlinkProduct":
                    return Response(client, ("Error", Variant.From(
                        await recipes.UnlinkProductAsync(internalIdentifier, product, ct).ConfigureAwait(false))));
                case "Release":
                    return Response(client, ("Error",
                        Variant.From(await recipes.ReleaseRecipeHandleAsync(handle, ct).ConfigureAwait(false))));
                default:
                    throw new ArgumentException("Unknown recipe operation.", nameof(operation));
            }
        }

        /// <summary>
        /// Executes one configuration metadata operation.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        [McpServerTool(Name = "vision_machine_configurations")]
        [Description("OPC 40100-1 configuration operation: List, Get, Add, Activate, Remove, or Release. " +
            "Add registers metadata only; use the file tool when transferRequired is true. " +
            "Activation is never implicit.")]
        public static async Task<JsonElement> ConfigurationsAsync(
            VisionClientAccessor accessor,
            string systemNodeId,
            string operation,
            string id = "",
            string version = "",
            [Range(1, 100)] uint maximumResults = 50,
            uint startIndex = 0,
            uint handle = 0,
            [Description("Optional UA JSON configuration identifier including " +
                "Id, Version, Hash, HashAlgorithm, and Description.")]
            string? identifierJson = null,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            ValidatePage(maximumResults);
            ValidateInputJson(identifierJson);
            VisionClient client = accessor.CreateClient(sessionName);
            ConfigurationManagementTypeClient configurations = await System(client, systemNodeId)
                .ConfigurationsAsync(ct).ConfigureAwait(false);
            ConfigurationIdDataType identifier =
                Identifier<ConfigurationIdDataType>(client, id, version, identifierJson);
            switch (operation)
            {
                case "List":
                    (bool isComplete, uint resultCount, uint configurationHandle,
                        ArrayOf<ConfigurationDataType> configurationList, int error)
                        = await configurations.GetConfigurationListAsync(maximumResults, startIndex, -1, ct)
                        .ConfigureAwait(false);
                    return Response(client, ("IsComplete", Variant.From(isComplete)),
                        ("ResultCount", Variant.From(resultCount)),
                        ("Handle", Variant.From(configurationHandle)),
                        ("Configurations", Variant.FromStructure(configurationList)),
                        ("Error", Variant.From(error)));
                case "Get":
                    (uint configurationHandle, ConfigurationDataType configuration, int error) found =
                        await configurations.GetConfigurationByIdAsync(identifier, -1, ct).ConfigureAwait(false);
                    return Response(client, ("Handle", Variant.From(found.configurationHandle)),
                        ("Configuration", Variant.FromStructure(found.configuration)),
                        ("Error", Variant.From(found.error)));
                case "Add":
                    (ConfigurationIdDataType internalId, NodeId configuration, bool transferRequired, int error) added =
                        await configurations.AddConfigurationAsync(identifier, ct).ConfigureAwait(false);
                    return Response(client, ("InternalId", Variant.FromStructure(added.internalId)),
                        ("ConfigurationNodeId", Variant.From(added.configuration)),
                        ("TransferRequired", Variant.From(added.transferRequired)),
                        ("Error", Variant.From(added.error)));
                case "Activate":
                    return Response(client, ("Error", Variant.From(
                        await configurations.ActivateConfigurationAsync(identifier, ct).ConfigureAwait(false))));
                case "Remove":
                    return Response(client, ("Error", Variant.From(
                        await configurations.RemoveConfigurationAsync(identifier, ct).ConfigureAwait(false))));
                case "Release":
                    return Response(client, ("Error", Variant.From(
                        await configurations.ReleaseConfigurationHandleAsync(handle, ct).ConfigureAwait(false))));
                default:
                    throw new ArgumentException("Unknown configuration operation.", nameof(operation));
            }
        }

        /// <summary>
        /// Reads or releases result metadata.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        [McpServerTool(Name = "vision_machine_results")]
        [Description("OPC 40100-1 result operation: Get, Components, List, or Release. " +
            "Lists are paged and support all identity filters through filtersJson. " +
            "Returns complete typed OPC UA metadata/content and the handle to release.")]
        public static async Task<JsonElement> ResultsAsync(
            VisionClientAccessor accessor,
            string systemNodeId,
            string operation,
            string resultId = "",
            string jobId = "",
            int resultState = 0,
            [Range(1, 100)] uint maximumResults = 50,
            uint startIndex = 0,
            uint handle = 0,
            [Description("Optional UA JSON object with MeasId, PartId, ExternalRecipeId, InternalRecipeId, " +
                "ExternalConfigurationId, InternalConfigurationId, ProductId, and JobId structures. " +
                "Binary identifiers retain Version, Hash, HashAlgorithm, and Description.")]
            string? filtersJson = null,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            ValidatePage(maximumResults);
            ValidateInputJson(filtersJson);
            VisionClient client = accessor.CreateClient(sessionName);
            ResultManagementTypeClient results = await System(client, systemNodeId).ResultsAsync(ct)
                .ConfigureAwait(false);
            if (operation == "Release")
            {
                return Response(client, ("Error", Variant.From(await results.ReleaseResultHandleAsync(handle, ct)
                    .ConfigureAwait(false))));
            }
            if (operation == "Get")
            {
                (uint resultHandle, ResultDataType result, int error) =
                    await results.GetResultByIdAsync(new ResultIdDataType { Id = resultId }, -1, ct)
                        .ConfigureAwait(false);
                return Response(client, ("Handle", Variant.From(resultHandle)),
                    ("Result", Variant.FromStructure(result)), ("Error", Variant.From(error)));
            }
            if (operation == "Components")
            {
                (bool hasTransferableDataOnFile, uint resultHandle, bool isPartial, bool isSimulated,
                    int resultState, MeasIdDataType measId, PartIdDataType partId,
                    RecipeIdExternalDataType externalRecipeId, RecipeIdInternalDataType internalRecipeId,
                    ProductIdDataType productId, ConfigurationIdDataType externalConfigurationId,
                    ConfigurationIdDataType internalConfigurationId, JobIdDataType jobId, DateTimeUtc creationTime,
                    ProcessingTimesDataType processingTimes, ArrayOf<Variant> resultContent, int error) found =
                    await results.GetResultComponentsByIdAsync(new ResultIdDataType { Id = resultId }, -1, ct)
                        .ConfigureAwait(false);
                return Response(client, ("Handle", Variant.From(found.resultHandle)),
                    ("HasTransferableDataOnFile", Variant.From(found.hasTransferableDataOnFile)),
                    ("IsPartial", Variant.From(found.isPartial)), ("IsSimulated", Variant.From(found.isSimulated)),
                    ("ResultState", Variant.From(found.resultState)),
                    ("MeasId", Variant.FromStructure(found.measId)), ("PartId", Variant.FromStructure(found.partId)),
                    ("ExternalRecipeId", Variant.FromStructure(found.externalRecipeId)),
                    ("InternalRecipeId", Variant.FromStructure(found.internalRecipeId)),
                    ("ExternalConfigurationId", Variant.FromStructure(found.externalConfigurationId)),
                    ("InternalConfigurationId", Variant.FromStructure(found.internalConfigurationId)),
                    ("ProductId", Variant.FromStructure(found.productId)),
                    ("JobId", Variant.FromStructure(found.jobId)),
                    ("CreationTime", Variant.From(found.creationTime)),
                    ("ProcessingTimes", Variant.FromStructure(found.processingTimes)),
                    ("ResultContent", Variant.From(found.resultContent)), ("Error", Variant.From(found.error)));
            }
            if (operation != "List")
            {
                throw new ArgumentException("Unknown result operation.", nameof(operation));
            }
            using JsonDecoder? filters = filtersJson == null
                ? null : new JsonDecoder(filtersJson, client.Session.MessageContext);
            JobIdDataType job = filters?.ReadEncodeable<JobIdDataType>("JobId") ?? new JobIdDataType();
            if (!string.IsNullOrEmpty(jobId))
            {
                if (!string.IsNullOrEmpty(job.Id) && job.Id != jobId)
                {
                    throw new ArgumentException("The jobId and filtersJson JobId disagree.", nameof(jobId));
                }
                job.Id = jobId;
            }
            (bool isComplete, uint resultCount, uint resultHandle, ArrayOf<ResultDataType> resultList, int error) list =
                await results.GetResultListFilteredAsync(resultState,
                    filters?.ReadEncodeable<MeasIdDataType>("MeasId") ?? new MeasIdDataType(),
                    filters?.ReadEncodeable<PartIdDataType>("PartId") ?? new PartIdDataType(),
                    filters?.ReadEncodeable<RecipeIdExternalDataType>("ExternalRecipeId") ??
                    new RecipeIdExternalDataType(),
                    filters?.ReadEncodeable<RecipeIdInternalDataType>("InternalRecipeId") ??
                    new RecipeIdInternalDataType(),
                    filters?.ReadEncodeable<ConfigurationIdDataType>("ExternalConfigurationId") ??
                    new ConfigurationIdDataType(),
                    filters?.ReadEncodeable<ConfigurationIdDataType>("InternalConfigurationId") ??
                    new ConfigurationIdDataType(),
                    filters?.ReadEncodeable<ProductIdDataType>("ProductId") ?? new ProductIdDataType(),
                    job, maximumResults, startIndex, -1, ct)
                .ConfigureAwait(false);
            return Response(client, ("IsComplete", Variant.From(list.isComplete)),
                ("ResultCount", Variant.From(list.resultCount)), ("Handle", Variant.From(list.resultHandle)),
                ("Results", Variant.FromStructure(list.resultList)), ("Error", Variant.From(list.error)));
        }

        /// <summary>
        /// Reports safety information without acting as a safety controller.
        /// </summary>
        [McpServerTool(Name = "vision_machine_report_safety")]
        [Description("Reports OPC 40100-1 safety information and raises its event. " +
            "This does not implement a safety controller " +
            "and must not replace a certified safety circuit.")]
        public static async Task<JsonElement> ReportSafetyAsync(
            VisionClientAccessor accessor, string systemNodeId, bool triggered, string information,
            string? sessionName = null, CancellationToken ct = default)
        {
            VisionClient client = accessor.CreateClient(sessionName);
            SafetyStateManagementTypeClient safety = await System(client, systemNodeId).SafetyAsync(ct)
                .ConfigureAwait(false);
            return Response(client, ("Error",
                Variant.From(await safety.ReportSafetyStateAsync(triggered, information, ct).ConfigureAwait(false))));
        }

        internal static MachineVisionSystemClient System(VisionClient client, string id)
        {
            return client.MachineVision().System(OpcUaJsonHelper.ParseNodeId(id));
        }

        internal static JsonElement Response(VisionClient client, params (string Name, Variant Value)[] fields)
        {
            using var encoder = new JsonEncoder(client.Session.MessageContext);
            foreach ((string name, Variant value) in fields)
            {
                encoder.WriteVariant(name, value);
            }
            return ParseResponse(encoder.CloseAndReturnText());
        }

        private static JsonElement Element(JsonNode value)
        {
            return ParseResponse(value.ToJsonString());
        }

        private static void AddNode(JsonArray array, JsonNode? value)
        {
            array.Add(value);
        }

        private static JsonElement ParseResponse(string json)
        {
            if (Encoding.UTF8.GetByteCount(json) > 2 * 1024 * 1024)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "The MCP response is too large; use a smaller page, a narrower asset root, or file chunks.");
            }
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        private static JsonObject State(FiniteStateSnapshot state)
        {
            return new JsonObject
            {
                ["state"] = state.CurrentState.Text,
                ["stateId"] = state.CurrentStateId.ToString(),
                ["lastTransition"] = state.LastTransition.Text,
                ["status"] = state.Status.ToString()
            };
        }

        private static void ValidatePage(uint maximum)
        {
            if (maximum is 0 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum), "MCP pages contain between 1 and 100 items.");
            }
        }

        private static T Identifier<T>(VisionClient client, string id, string version, string? json)
            where T : BinaryIdBaseDataType, new()
        {
            using JsonDecoder? decoder = json == null ? null : new JsonDecoder(json, client.Session.MessageContext);
            var identifier = new T();
            if (decoder != null)
            {
                identifier.Decode(decoder);
            }
            if (!string.IsNullOrEmpty(id))
            {
                if (!string.IsNullOrEmpty(identifier.Id) && identifier.Id != id)
                {
                    throw new ArgumentException(
                        "The identifier's Id disagrees with the supplied selector.", nameof(id));
                }
                identifier.Id = id;
            }
            if (!string.IsNullOrEmpty(version))
            {
                if (!string.IsNullOrEmpty(identifier.Version) && identifier.Version != version)
                {
                    throw new ArgumentException(
                        "The identifier's Version disagrees with the supplied selector.", nameof(version));
                }
                identifier.Version = version;
                identifier.EncodingMask |= (uint)BinaryIdBaseDataTypeFields.Version;
            }
            return identifier;
        }

        private static void ValidateInputJson(string? json, string? arrayProperty = null)
        {
            if (json == null)
            {
                return;
            }
            if (Encoding.UTF8.GetByteCount(json) > 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(json), "A structured MCP input cannot exceed 1 MiB.");
            }
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("A UA JSON object is required.", nameof(json));
            }
            if (arrayProperty != null &&
                (!document.RootElement.TryGetProperty(arrayProperty, out JsonElement array) ||
                    array.ValueKind != JsonValueKind.Array))
            {
                throw new ArgumentException("The UA JSON object must contain a Parameters array.", nameof(json));
            }
        }
    }
}
