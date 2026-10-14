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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.PackML;
using Opc.Ua.Scales;
using Opc.Ua.Scales.Client;
using ScaleNames = Opc.Ua.Scales.BrowseNames;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Explicit consequential commands for OPC 40200 scales; never acquires DI locks.
    /// </summary>
    [McpServerToolType]
    public sealed class ScalesCommandTools
    {
        /// <summary>
        /// Initializes the explicit command tools.
        /// </summary>
        public ScalesCommandTools(ScalesClientAccessor accessor)
        {
            m_accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        }

        /// <summary>
        /// Sets the scale's zero reference.
        /// </summary>
        [McpServerTool(Name = "scales_set_zero", ReadOnly = false, Destructive = true)]
        [Description("Explicitly change the scale's zero reference (SetZero). Server stability/range rules apply. " +
            "A refusal is an MCP error retaining the raw UA status. Never automatically tares or acquires locks.")]
        public Task<CallToolResult> SetZeroAsync(
            [Description("Scale NodeId.")] string nodeId,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return RunAsync(nodeId, sessionName, (client, target, token) => client.SetZeroAsync(target, token), ct);
        }

        /// <summary>
        /// Measures and sets the scale's tare.
        /// </summary>
        [McpServerTool(Name = "scales_set_tare", ReadOnly = false, Destructive = true)]
        [Description("Explicitly measure and set tare using the current load (SetTare). " +
            "Changes subsequent net weights.")]
        public Task<CallToolResult> SetTareAsync(
            [Description("Scale NodeId.")] string nodeId,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return RunAsync(nodeId, sessionName, (client, target, token) => client.SetTareAsync(target, token), ct);
        }

        /// <summary>
        /// Clears the scale's tare.
        /// </summary>
        [McpServerTool(Name = "scales_clear_tare", ReadOnly = false, Destructive = true)]
        [Description("Explicitly clear measured or preset tare (ClearTare). Changes subsequent net weights.")]
        public Task<CallToolResult> ClearTareAsync(
            [Description("Scale NodeId.")] string nodeId,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return RunAsync(nodeId, sessionName,
                (client, target, token) => client.ClearTareAsync(target, token), ct);
        }

        /// <summary>
        /// Sets a preset tare with an explicit engineering unit.
        /// </summary>
        [McpServerTool(Name = "scales_set_preset_tare", ReadOnly = false, Destructive = true)]
        [Description("Explicitly set a preset tare value and engineering unit. Obtain allowed unit identifiers with " +
            "scales_read AllowedEngineeringUnits. The server performs unit conversion and range validation.")]
        public Task<CallToolResult> SetPresetTareAsync(
            [Description("Scale NodeId.")] string nodeId,
            [Description("Preset tare in engineeringUnits.")] double presetTare,
            [Description("Explicit OPC UA engineering unit.")] ScalesEngineeringUnit engineeringUnits,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return RunAsync(nodeId, sessionName, (client, target, token) =>
                client.SetPresetTareAsync(target, presetTare, Units(engineeringUnits), token), ct);
        }

        /// <summary>
        /// Registers the current weight and reads it back.
        /// </summary>
        [McpServerTool(Name = "scales_register_weight", ReadOnly = false, Destructive = true)]
        [Description("Explicitly register the current weight (RegisterWeight), then read RegisteredWeight. " +
            "This changes the registered record and can update statistics. No automatic retry. " +
            "A successful command without a published RegisteredWeight returns available=false.")]
        public Task<CallToolResult> RegisterWeightAsync(
            [Description("Scale NodeId.")] string nodeId,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ScalesClient client = m_accessor.CreateClient(sessionName);
                ScaleReading? reading = await client.RegisterWeightAsync(
                    ScalesReadTools.ParseRequired(nodeId), token).ConfigureAwait(false);
                JsonObject result = ScalesJson.Weight(reading, client.Session.MessageContext);
                result["completed"] = true;
                return result;
            }, ct);
        }

        /// <summary>
        /// Adds a product to the production preset.
        /// </summary>
        [McpServerTool(Name = "scales_add_product", ReadOnly = false, Destructive = true)]
        [Description("Explicitly create a product in a scale/system's production preset. Returns the new product " +
            "NodeId. No implicit selection or client-side locking; " +
            "a server may assign the new product to the caller. " +
            "Read Products afterwards to obtain the actual DI lockNodeId.")]
        public Task<CallToolResult> AddProductAsync(
            [Description("Scale or scale system NodeId.")] string nodeId,
            [Description("New product name.")] string productName,
            [Description("Unique product identifier.")] string productId,
            [Description("Optional product type NodeId; omitted uses the owner's default type.")]
            string? productTypeNodeId = null,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(productName);
                ArgumentException.ThrowIfNullOrWhiteSpace(productId);
                ScalesClient client = m_accessor.CreateClient(sessionName);
                NodeId created = await client.AddProductAsync(
                    ScalesReadTools.ParseRequired(nodeId),
                    productName,
                    productId,
                    productTypeNodeId == null ? NodeId.Null : ScalesReadTools.ParseRequired(productTypeNodeId),
                    token).ConfigureAwait(false);
                return Created(created);
            }, ct);
        }

        /// <summary>
        /// Removes an explicitly selected product.
        /// </summary>
        [McpServerTool(Name = "scales_remove_product", ReadOnly = false, Destructive = true)]
        [Description("Explicitly remove a product from a production preset. Server lock and in-use rules apply. " +
            "Never automatically acquires or breaks locks; use the product lockNodeId with canonical di_* tools.")]
        public Task<CallToolResult> RemoveProductAsync(
            [Description("Scale or scale system NodeId.")] string nodeId,
            [Description("Product identifier, not its NodeId.")] string productId,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return RunAsync(nodeId, sessionName,
                (client, target, token) => client.RemoveProductAsync(target, ProductId(productId), token), ct);
        }

        /// <summary>
        /// Selects an explicitly identified product.
        /// </summary>
        [McpServerTool(Name = "scales_select_product", ReadOnly = false, Destructive = true)]
        [Description("Adds the identified product to the scale's current processing set, " +
            "leaving other products selected. " +
            "Returns completion status. Use scales_deselect_product to remove a current selection.")]
        public Task<CallToolResult> SelectProductAsync(
            [Description("Scale or scale system NodeId.")] string nodeId,
            [Description("Product identifier, not its NodeId.")] string productId,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return RunAsync(nodeId, sessionName,
                (client, target, token) => client.SelectProductAsync(target, ProductId(productId), token), ct);
        }

        /// <summary>
        /// Deselects an explicitly identified product.
        /// </summary>
        [McpServerTool(Name = "scales_deselect_product", ReadOnly = false, Destructive = true)]
        [Description("Removes a product from active processing without deleting its preset " +
            "or choosing a replacement. " +
            "Returns completion status. Choose scales_remove_product instead to delete the stored preset.")]
        public Task<CallToolResult> DeselectProductAsync(
            [Description("Scale or scale system NodeId.")] string nodeId,
            [Description("Product identifier, not its NodeId.")] string productId,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return RunAsync(nodeId, sessionName,
                (client, target, token) => client.DeselectProductAsync(target, ProductId(productId), token), ct);
        }

        /// <summary>
        /// Switches processing to an explicitly identified product.
        /// </summary>
        [McpServerTool(Name = "scales_switch_product", ReadOnly = false, Destructive = true)]
        [Description("Explicitly switch the production preset to a product. May deselect the current products. " +
            "Server lock rules apply; no automatic acquisition, release or retry.")]
        public Task<CallToolResult> SwitchProductAsync(
            [Description("Scale or scale system NodeId.")] string nodeId,
            [Description("Product identifier, not its NodeId.")] string productId,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return RunAsync(nodeId, sessionName,
                (client, target, token) => client.SwitchProductAsync(target, ProductId(productId), token), ct);
        }

        /// <summary>
        /// Executes one standard PackML command at the correct state-machine level.
        /// </summary>
        [McpServerTool(Name = "scales_packml_command", ReadOnly = false, Destructive = true)]
        [Description("Explicitly execute a standard PackML command on a scale or system. " +
            "May start or stop machinery. " +
            "The existing client resolves the correct state-machine level; " +
            "invalid transitions are server refusals. " +
            "No arbitrary method names, automatic state transitions or retry.")]
        public Task<CallToolResult> PackMLCommandAsync(
            [Description("Scale or scale system NodeId.")] string nodeId,
            [Description("Standard PackML command.")] PackMLCommand command,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return RunAsync(nodeId, sessionName, (client, target, token) =>
            {
                if (!Enum.IsDefined(command))
                {
                    throw new ArgumentOutOfRangeException(nameof(command));
                }
                return client.ExecutePackMLCommandAsync(target, command, token);
            }, ct);
        }

        /// <summary>
        /// Executes an argument-less method from the closed OPC 40200 whitelist.
        /// </summary>
        [McpServerTool(Name = "scales_standard_command", ReadOnly = false, Destructive = true)]
        [Description("Explicit consequential standard scale command: discharge/refill start or stop, " +
            "laboratory leveling/calibration/ionisator, reset totalizer or reset system statistics. " +
            "Only the enumerated OPC 40200 commands are accepted, never arbitrary or vendor method names.")]
        public Task<CallToolResult> StandardCommandAsync(
            [Description("Object that owns the standard method.")] string nodeId,
            [Description("Closed standard no-argument method whitelist.")] ScalesStandardCommand command,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return RunAsync(nodeId, sessionName,
                (client, target, token) => client.InvokeAsync(target, StandardMethod(command), token), ct);
        }

        /// <summary>
        /// Sets the reference piece weight with explicit units.
        /// </summary>
        [McpServerTool(Name = "scales_set_reference_piece_weight", ReadOnly = false, Destructive = true)]
        [Description("Explicitly set a piece-counting scale's reference piece weight. OPC 40200 defines this " +
            "method argument as UInt32; choose suitable allowed engineering units for fractional weights.")]
        public Task<CallToolResult> SetReferencePieceWeightAsync(
            [Description("Piece-counting scale NodeId.")] string nodeId,
            [Description("Reference piece weight, UInt32 in engineeringUnits.")] uint referencePieceWeight,
            [Description("Explicit allowed OPC UA engineering unit.")] ScalesEngineeringUnit engineeringUnits,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return RunAsync(nodeId, sessionName, (client, target, token) =>
                client.SetReferencePieceWeightAsync(target, referencePieceWeight, Units(engineeringUnits), token), ct);
        }

        /// <summary>
        /// Sets the number of reference pieces.
        /// </summary>
        [McpServerTool(Name = "scales_set_number_of_reference_pieces", ReadOnly = false, Destructive = true)]
        [Description("Explicitly set a piece-counting scale's number of reference pieces.")]
        public Task<CallToolResult> SetNumberOfReferencePiecesAsync(
            [Description("Piece-counting scale NodeId.")] string nodeId,
            [Description("Reference piece count.")] uint numberOfReferencePieces,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return RunAsync(nodeId, sessionName, (client, target, token) =>
                client.SetNumberOfReferencePiecesAsync(target, numberOfReferencePieces, token), ct);
        }

        /// <summary>
        /// Starts piece-count reference acquisition.
        /// </summary>
        [McpServerTool(Name = "scales_start_reference", ReadOnly = false, Destructive = true)]
        [Description("Explicitly start piece-count reference acquisition " +
            "for the supplied number of pieces on the scale.")]
        public Task<CallToolResult> StartReferenceAsync(
            [Description("Piece-counting scale NodeId.")] string nodeId,
            [Description("Number of pieces physically on the scale.")] uint numberOfReferencePieces,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return RunAsync(nodeId, sessionName, (client, target, token) =>
                client.StartReferenceAsync(target, numberOfReferencePieces, token), ct);
        }

        /// <summary>
        /// Opens or closes specific laboratory draft shields.
        /// </summary>
        [McpServerTool(Name = "scales_set_draft_shields", ReadOnly = false, Destructive = true)]
        [Description("Explicitly open or close the selected laboratory draft shields. " +
            "This can move physical hardware.")]
        public Task<CallToolResult> SetDraftShieldsAsync(
            [Description("Laboratory scale NodeId.")] string nodeId,
            [Description("Standard draft-shield selection.")] DraftShieldType shield,
            [Description("True closes; false opens.")] bool close,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return RunAsync(nodeId, sessionName, (client, target, token) =>
            {
                if (!Enum.IsDefined(shield))
                {
                    throw new ArgumentOutOfRangeException(nameof(shield));
                }
                return client.SetDraftShieldsAsync(target, shield, close, token);
            }, ct);
        }

        /// <summary>
        /// Performs a standard vehicle weighing operation.
        /// </summary>
        [McpServerTool(Name = "scales_weigh_vehicle", ReadOnly = false, Destructive = true)]
        [Description("Explicitly perform Inbound, Outbound or OnePass vehicle weighing. The enum selects only those " +
            "three standard methods; no arbitrary method passthrough. Can create or update weighing records.")]
        public Task<CallToolResult> WeighVehicleAsync(
            [Description("Vehicle scale NodeId.")] string nodeId,
            [Description("Inbound, Outbound or OnePass.")] ScalesVehicleCommand command,
            [Description("Vehicle identifier.")] string vehicleId,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return RunAsync(nodeId, sessionName, (client, target, token) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(vehicleId);
                string method = command switch
                {
                    ScalesVehicleCommand.Inbound => ScaleNames.InboundWeighing,
                    ScalesVehicleCommand.Outbound => ScaleNames.OutboundWeighing,
                    ScalesVehicleCommand.OnePass => ScaleNames.OnePassWeighing,
                    _ => throw new ArgumentOutOfRangeException(nameof(command))
                };
                return client.WeighVehicleAsync(target, method, vehicleId, token);
            }, ct);
        }

        /// <summary>
        /// Sets a feeder module's target speed.
        /// </summary>
        [McpServerTool(Name = "scales_set_feeder_speed", ReadOnly = false, Destructive = true)]
        [Description("Explicitly change a feeder module's target speed with an engineering unit. " +
            "This can change material flow. The server validates units, limits and operational state.")]
        public Task<CallToolResult> SetFeederSpeedAsync(
            [Description("Feeder module NodeId, not the owning scale.")] string nodeId,
            [Description("Target feeder speed in engineeringUnits.")] float feederSpeed,
            [Description("Explicit speed engineering unit.")] ScalesEngineeringUnit engineeringUnits,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return RunAsync(nodeId, sessionName, (client, target, token) =>
                client.SetFeederSpeedAsync(target, feederSpeed, Units(engineeringUnits), token), ct);
        }

        /// <summary>
        /// Adds a recipe to a recipe scale's management object.
        /// </summary>
        [McpServerTool(Name = "scales_add_recipe", ReadOnly = false, Destructive = true)]
        [Description("Explicitly create a recipe in a recipe scale's Recipes management object. " +
            "Returns its NodeId; does not create elements or begin processing.")]
        public Task<CallToolResult> AddRecipeAsync(
            [Description("Recipe scale NodeId.")] string nodeId,
            [Description("Unique recipe identifier.")] string recipeId,
            [Description("Recipe display name.")] string recipeName,
            [Description("Optional locale for recipeName.")] string? locale = null,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(recipeId);
                ArgumentException.ThrowIfNullOrWhiteSpace(recipeName);
                ScalesClient client = m_accessor.CreateClient(sessionName);
                return Created(await client.AddRecipeAsync(
                    ScalesReadTools.ParseRequired(nodeId),
                    recipeId,
                    new LocalizedText(locale, recipeName),
                    token).ConfigureAwait(false));
            }, ct);
        }

        /// <summary>
        /// Adds a typed recipe element with explicit predecessors.
        /// </summary>
        [McpServerTool(Name = "scales_add_recipe_element", ReadOnly = false, Destructive = true)]
        [Description("Explicitly add a recipe element with its type, name and predecessor NodeIds. " +
            "Use the recipe NodeId as predecessor for a start element. Does not start processing.")]
        public Task<CallToolResult> AddRecipeElementAsync(
            [Description("Recipe NodeId, not the scale.")] string nodeId,
            [Description("Typed element and predecessor request.")] ScalesRecipeElementRequest input,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                ArgumentException.ThrowIfNullOrWhiteSpace(input.ElementName);
                ArgumentOutOfRangeException.ThrowIfLessThan(input.PreviousElements.Count, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(input.PreviousElements.Count, 500);
                var predecessors = new NodeId[input.PreviousElements.Count];
                for (int index = 0; index < predecessors.Length; index++)
                {
                    predecessors[index] = ScalesReadTools.ParseRequired(input.PreviousElements[index]);
                }
                ScalesClient client = m_accessor.CreateClient(sessionName);
                return Created(await client.AddRecipeElementAsync(
                    ScalesReadTools.ParseRequired(nodeId),
                    ScalesReadTools.ParseRequired(input.ElementTypeNodeId),
                    input.ElementName,
                    predecessors,
                    token).ConfigureAwait(false));
            }, ct);
        }

        /// <summary>
        /// Starts, stops, continues, skips or aborts recipe processing.
        /// </summary>
        [McpServerTool(Name = "scales_process_recipe", ReadOnly = false, Destructive = true)]
        [Description("Explicitly Start, Stop, Continue, SkipCurrentElement or Abort a recipe. " +
            "This affects physical production. Only these standard commands are accepted; " +
            "no arbitrary method names, automatic transitions or retry.")]
        public Task<CallToolResult> ProcessRecipeAsync(
            [Description("Recipe scale NodeId.")] string nodeId,
            [Description("Recipe NodeId.")] string recipeNodeId,
            [Description("Closed standard recipe command.")] ScalesRecipeCommand command,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return RunAsync(nodeId, sessionName, (client, target, token) =>
            {
                string method = command switch
                {
                    ScalesRecipeCommand.Start => ScaleNames.StartRecipe,
                    ScalesRecipeCommand.Stop => ScaleNames.StopRecipe,
                    ScalesRecipeCommand.Continue => ScaleNames.ContinueRecipe,
                    ScalesRecipeCommand.SkipCurrentElement => ScaleNames.SkipCurrentRecipeElement,
                    ScalesRecipeCommand.Abort => ScaleNames.AbortRecipe,
                    _ => throw new ArgumentOutOfRangeException(nameof(command))
                };
                return client.ProcessRecipeAsync(
                    target, method, ScalesReadTools.ParseRequired(recipeNodeId), token);
            }, ct);
        }

        /// <summary>
        /// Maps every allowed no-argument command to a generated standard browse name.
        /// </summary>
        internal static string StandardMethod(ScalesStandardCommand command)
        {
            return command switch
            {
                ScalesStandardCommand.DischargeStart => ScaleNames.DischargeStart,
                ScalesStandardCommand.DischargeStop => ScaleNames.DischargeStop,
                ScalesStandardCommand.RefillStart => ScaleNames.RefillStart,
                ScalesStandardCommand.RefillStop => ScaleNames.RefillStop,
                ScalesStandardCommand.StartLeveling => ScaleNames.StartLeveling,
                ScalesStandardCommand.StartCalibration => ScaleNames.StartCalibration,
                ScalesStandardCommand.StartIonisator => ScaleNames.StartIonisator,
                ScalesStandardCommand.StopIonisator => ScaleNames.StopIonisator,
                ScalesStandardCommand.ResetTotalizer => ScaleNames.ResetTotalizer,
                ScalesStandardCommand.ResetGlobalStatistics => ScaleNames.ResetGlobalStatistics,
                _ => throw new ArgumentOutOfRangeException(nameof(command))
            };
        }

        /// <summary>
        /// Runs one explicit command without retries or automatic prerequisite mutations.
        /// </summary>
        private Task<CallToolResult> RunAsync(
            string nodeId,
            string? sessionName,
            Func<ScalesClient, NodeId, CancellationToken, ValueTask> command,
            CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                NodeId target = ScalesReadTools.ParseRequired(nodeId);
                await command(m_accessor.CreateClient(sessionName), target, token).ConfigureAwait(false);
                return new JsonObject { ["completed"] = true, ["nodeId"] = target.ToString() };
            }, ct);
        }

        /// <summary>
        /// Validates and converts a concrete engineering-unit input.
        /// </summary>
        private static EUInformation Units(ScalesEngineeringUnit value)
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentException.ThrowIfNullOrWhiteSpace(value.NamespaceUri);
            return value.ToUnits();
        }

        /// <summary>
        /// Validates a product identifier before any method call.
        /// </summary>
        private static string ProductId(string productId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(productId);
            return productId;
        }

        /// <summary>
        /// Returns a created node, rejecting a malformed successful method response.
        /// </summary>
        private static JsonObject Created(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError, "The method did not return the created NodeId.");
            }
            return new JsonObject { ["completed"] = true, ["nodeId"] = nodeId.ToString() };
        }

        private readonly ScalesClientAccessor m_accessor;
    }
}
