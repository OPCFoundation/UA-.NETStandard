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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Scales;
using ReferenceTypeIds = Opc.Ua.ReferenceTypeIds;
using Scales = Opc.Ua.Scales;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed partial class WeighingCompanionProvider
    {
        /// <inheritdoc/>
        public async ValueTask<CompanionTaskInput> PrepareInputAsync(
            CompanionContext context, CompanionTarget target, string operationId, ArrayOf<CompanionValue> inputs,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            cancellationToken = lifetime.Token;
            var stamp = new SessionStamp(context);
            ScaleEntry scale = await RequireScaleAsync(context, target, cancellationToken).ConfigureAwait(false);
            Command command = FindCommand(scale, operationId);
            Payload payload = ParseInput(command, inputs);
            WeighingSimulationGrant grant = await AuthorizeSimulationAsync(
                context, target, operationId, cancellationToken).ConfigureAwait(false);
            CommandEvidence evidence = await ReadEvidenceAsync(
                context, scale, command, payload, cancellationToken).ConfigureAwait(false);
            stamp.RequireMatch(context);
            cancellationToken.ThrowIfCancellationRequested();
            RequireUnexpiredGrant(grant, m_timeProvider);
            return new PreparedTask(target, command, payload, evidence, stamp, grant);
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionOperationResult> ExecutePreparedAsync(
            CompanionContext context, CompanionTarget target, string operationId, CompanionTaskInput input,
            IProgress<CompanionTaskProgress>? progress, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            cancellationToken = lifetime.Token;
            if (input is not PreparedTask prepared || prepared.Target != target || prepared.Command.Id != operationId)
            {
                throw new ArgumentException("Prepare this exact typed weighing request first.", nameof(input));
            }
            prepared.RequireUnused();
            prepared.Stamp.RequireMatch(context);
            RequireUnexpiredGrant(prepared.Grant, m_timeProvider);
            ScaleEntry scale = await RequireScaleAsync(context, target, cancellationToken).ConfigureAwait(false);
            Command command = FindCommand(scale, operationId);
            CommandEvidence fresh = await ReadEvidenceAsync(
                context, scale, command, prepared.Payload, cancellationToken).ConfigureAwait(false);
            if (!prepared.Evidence.Matches(fresh))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "Weighing state, type, selection, units or method metadata changed. Prepare again.");
            }
            WeighingSimulationGrant grant = await AuthorizeSimulationAsync(
                context, target, operationId, cancellationToken).ConfigureAwait(false);
            if (grant != prepared.Grant)
            {
                throw new UnauthorizedAccessException("The verified owned-simulation run or grant changed.");
            }
            prepared.Stamp.RequireMatch(context);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new CompanionTaskProgress("Dispatching the reviewed typed weighing method once."));
            using var guarded = new PreparedCompanionMethodClient(
                context, prepared.Evidence.Binding.Owner, prepared.Command.DeclarationId,
                prepared.Evidence.Binding.Method, prepared.Command.ReturnsNode,
                () =>
                {
                    prepared.Stamp.RequireMatch(context);
                    RequireUnexpiredGrant(prepared.Grant, m_timeProvider);
                },
                prepared.Consume);
            NodeId created = await DispatchAsync(context, guarded, prepared, cancellationToken).ConfigureAwait(false);
            var values = new FieldBuffer(context.MaxFields);
            values.Add(new CompanionValue("Method result", Variant.From(guarded.ResultStatus)));
            if (command.ReturnsNode)
            {
                values.Add(new CompanionValue("Created node", Variant.From(created)));
            }
            if (operationId == "register-weight")
            {
                try
                {
                    await InspectWeightAsync(context, target.NodeId, Scales.BrowseNames.RegisteredWeight,
                        values, cancellationToken).ConfigureAwait(false);
                }
                catch (ServiceResultException exception)
                {
                    throw new ServiceResultException(exception.StatusCode,
                        "RegisterWeight was accepted, but readback failed. Inspect again; do not repeat the command.");
                }
            }
            prepared.Stamp.RequireMatch(context);
            cancellationToken.ThrowIfCancellationRequested();
            return new CompanionOperationResult(
                "The server accepted the single reviewed method call. Acceptance is not observed completion. " +
                "Refresh to observe state; this preparation cannot be replayed, including after an unknown outcome.",
                values.ToArray());
        }

        private static async ValueTask<CommandEvidence> ReadEvidenceAsync(
            CompanionContext context, ScaleEntry scale, Command command, Payload payload,
            CancellationToken cancellationToken)
        {
            MethodBinding binding = await ReadBindingAsync(context, scale.NodeId, command, cancellationToken)
                .ConfigureAwait(false);
            var values = new List<StateValue>();
            ArrayOf<Field> process = await ReadFieldsAsync(context, scale.NodeId,
                [new(Scales.BrowseNames.ProcessStateId, BuiltInType.String)], cancellationToken).ConfigureAwait(false);
            if (!process[0].IsAbsent)
            {
                RequireScalar(process[0], BuiltInType.String);
                RequireText(ReadString(process[0]), 256, "Process state", allowEmpty: true);
            }
            AddEvidence(context, values, "Process state", process[0]);
            UnitValue? unit = null;
            NodeId subject = default;
            if (command.Family is CommandFamily.Weight or CommandFamily.Pieces or CommandFamily.Vehicle)
            {
                unit = await ReadWeightEvidenceAsync(
                    context, scale.NodeId, command, payload, values, cancellationToken).ConfigureAwait(false);
            }
            if (command.Family is CommandFamily.Product or CommandFamily.RecipeManagement or CommandFamily.RecipeRun)
            {
                subject = await ReadSelectionEvidenceAsync(
                    context, scale, command, payload, values, cancellationToken).ConfigureAwait(false);
            }
            return new CommandEvidence(scale.TypeDefinition, binding, subject, unit, [.. values]);
        }

        private static async ValueTask<UnitValue> ReadWeightEvidenceAsync(
            CompanionContext context, NodeId scale, Command command, Payload payload, List<StateValue> values,
            CancellationToken cancellationToken)
        {
            NodeId weightNode = await ResolveAsync(
                context, scale, Scales.BrowseNames.CurrentWeight, cancellationToken).ConfigureAwait(false);
            if (weightNode.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNoData,
                    "No current weight is exposed; no weighing command can be validated.");
            }
            ArrayOf<Field> fields = await ReadFieldsAsync(context, weightNode, s_weightFields, cancellationToken)
                .ConfigureAwait(false);
            RequireGood(fields[0].Status, "Current weight");
            if (!fields[0].Value.TryGetValue<WeightType>(
                out WeightType? weight, context.Session.MessageContext) ||
                weight is null)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Current weight is not a WeightType.");
            }
            if (!double.IsFinite(weight.Gross) || !double.IsFinite(weight.Net) || !double.IsFinite(weight.Tare))
            {
                throw new ServiceResultException(
                    StatusCodes.BadOutOfRange, "A non-finite weight cannot authorize a command.");
            }
            if (ReadBoolean(fields[1]) || ReadBoolean(fields[2]) || !ReadBoolean(fields[4]))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "The current weight must be stable and neither overloaded nor underloaded.");
            }
            if (command.Id == "set-zero" && !ReadBoolean(fields[5]))
            {
                throw new ServiceResultException(StatusCodes.BadOutOfRange, "The scale reports it is outside zero.");
            }
            if (command.Id == "set-tare" && weight.Gross < 0)
            {
                throw new ServiceResultException(StatusCodes.BadOutOfRange, "A negative gross weight cannot be tared.");
            }
            if (!fields[3].IsAbsent)
            {
                RequireScalar(fields[3], BuiltInType.Int32);
                if (!fields[3].Value.TryGetValue(out int mode) || !Enum.IsDefined((TareMode)mode))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Invalid tare mode.");
                }
            }
            if (!fields[5].IsAbsent)
            {
                RequireScalar(fields[5], BuiltInType.Boolean);
            }
            if (!fields[6].IsAbsent)
            {
                RequireScalar(fields[6], BuiltInType.UInt16);
            }
            if (!fields[7].IsAbsent)
            {
                RequireText(ReadString(fields[7]), 256, "Weight ID", allowEmpty: true);
            }
            RequireGood(fields[8].Status, "Current engineering units");
            if (!fields[8].Value.TryGetValue<EUInformation>(
                out EUInformation? unit, context.Session.MessageContext) ||
                unit is null)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Current engineering units are invalid.");
            }
            RequireUnit(unit);
            Opc.Ua.Range? range = null;
            if (!fields[9].IsAbsent)
            {
                RequireGood(fields[9].Status, "Current range");
                if (!fields[9].Value.TryGetValue<Opc.Ua.Range>(out range, context.Session.MessageContext) ||
                    range is null ||
                    !double.IsFinite(range.Low) ||
                    !double.IsFinite(range.High) ||
                    range.Low > range.High)
                {
                    throw new ServiceResultException(StatusCodes.BadOutOfRange, "The server's range is invalid.");
                }
            }
            if (command.Id is "preset-tare" or "reference-piece-weight")
            {
                if (payload.UnitId != unit.UnitId || payload.UnitNamespace != unit.NamespaceUri)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                        "Use the current weight's exact engineering unit. Lens does not convert weights.");
                }
                double number = command.Id == "preset-tare" ? payload.Number : payload.Count;
                if (range is null || number > range.High)
                {
                    throw new ServiceResultException(StatusCodes.BadOutOfRange,
                        "A current server range is required and the reviewed value must not exceed it.");
                }
                ArrayOf<Field> allowed = await ReadFieldsAsync(context, scale,
                    [new(Scales.BrowseNames.AllowedEngineeringUnits, BuiltInType.ExtensionObject, IsArray: true)],
                    cancellationToken).ConfigureAwait(false);
                if (!allowed[0].IsAbsent)
                {
                    RequireGood(allowed[0].Status, "Allowed engineering units");
                    if (!allowed[0].Value.TryGetValue(out ArrayOf<ExtensionObject> units) || units.IsNull)
                    {
                        throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Invalid allowed-units array.");
                    }
                    CellCompanionSupport.CheckCount(units.Count, kMaxItems, "Allowed engineering units");
                    bool found = units.Count == 0;
                    foreach (ExtensionObject item in units)
                    {
                        if (!Variant.From(item).TryGetValue<EUInformation>(
                            out EUInformation? candidate, context.Session.MessageContext) ||
                            candidate is null)
                        {
                            throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Invalid allowed unit.");
                        }
                        RequireUnit(candidate);
                        found |= candidate.UnitId == unit.UnitId && candidate.NamespaceUri == unit.NamespaceUri;
                    }
                    if (!found)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                            "The current unit is not in the server's allowed-unit list.");
                    }
                }
                AddEvidence(context, values, "Allowed units", allowed[0]);
            }
            foreach (Field field in fields)
            {
                AddEvidence(context, values, "Weight / " + field.Specification.Name, field);
            }
            return new UnitValue(
                unit.NamespaceUri ?? throw new ServiceResultException(StatusCodes.BadInvalidArgument),
                unit.UnitId, unit.DisplayName, unit.Description);
        }

        private static async ValueTask<NodeId> ReadSelectionEvidenceAsync(
            CompanionContext context, ScaleEntry scale, Command command, Payload payload,
            List<StateValue> values, CancellationToken cancellationToken)
        {
            bool product = command.Family == CommandFamily.Product;
            NodeId group = await ResolveAsync(context, scale.NodeId,
                product ? Scales.BrowseNames.ProductionPreset : Scales.BrowseNames.Recipes,
                cancellationToken).ConfigureAwait(false);
            if (group.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The selected collection is absent.");
            }
            await RequireObjectTypeAsync(context, group, product
                ? Scales.ObjectTypeIds.ProductionPresetType : Scales.ObjectTypeIds.RecipeManagementType,
                cancellationToken).ConfigureAwait(false);
            ArrayOf<string> current = default;
            if (product)
            {
                ArrayOf<Field> selected = await ReadFieldsAsync(context, group,
                    [new(Scales.BrowseNames.CurrentProducts, BuiltInType.String, IsArray: true)],
                    cancellationToken).ConfigureAwait(false);
                if (!selected[0].IsAbsent || command.Id != "add-product")
                {
                    RequireGood(selected[0].Status, "Current products");
                    if (!selected[0].Value.TryGetValue(out current) || current.IsNull)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTypeMismatch, "Invalid current-products array.");
                    }
                    CellCompanionSupport.CheckCount(current.Count, kMaxItems, "Current products");
                    foreach (string id in current)
                    {
                        RequireText(id, 128, "Current product ID");
                    }
                }
                AddEvidence(context, values, "Current products", selected[0]);
                group = await ResolveAsync(context, group, Scales.BrowseNames.Products, cancellationToken)
                    .ConfigureAwait(false);
                if (group.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported, "The product collection is absent.");
                }
            }
            NodeId selectedNode = default;
            bool processing = false;
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<NodeId>();
            var budget = new IndustrialBrowseBudget(context);
            await foreach (ReferenceDescription reference in IndustrialCompanionAccess.BrowseAsync(
                context, group, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences,
                NodeClass.Object, budget, cancellationToken).ConfigureAwait(false))
            {
                NodeId node = RequireLocal(context, reference.NodeId);
                if (!visited.Add(node))
                {
                    continue;
                }
                CellCompanionSupport.CheckCount(visited.Count, kMaxItems, "Weighing collection");
                string? kind = await IndustrialCompanionAccess.ClassifyAsync(context, reference.TypeDefinition,
                    [new(product ? Scales.ObjectTypeIds.ProductType : Scales.ObjectTypeIds.RecipeType, "entry")],
                    budget, cancellationToken).ConfigureAwait(false);
                if (kind is null)
                {
                    continue;
                }
                ArrayOf<Field> fields = await ReadFieldsAsync(context, node,
                    product
                        ? [new(Scales.BrowseNames.ProductId, BuiltInType.String),
                            new(Scales.BrowseNames.ProductMode, BuiltInType.Boolean)]
                        : [new(Scales.BrowseNames.RecipeId, BuiltInType.String)],
                    cancellationToken).ConfigureAwait(false);
                string identifier = ReadString(fields[0]);
                RequireText(identifier, 128, "Weighing collection ID");
                if (!identifiers.Add(identifier))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "Duplicate weighing collection IDs.");
                }
                for (int index = 0; index < fields.Count; index++)
                {
                    if (index > 0 && !fields[index].IsAbsent)
                    {
                        RequireScalar(fields[index], BuiltInType.Boolean);
                    }
                    AddEvidence(context, values, "Entry / " + identifier + " / " + fields[index].Specification.Name,
                        fields[index]);
                }
                values.Add(new StateValue("Entry type / " + identifier, node, StatusCodes.Good,
                    Variant.From(RequireLocal(context, reference.TypeDefinition))));
                if (identifier == payload.Identifier)
                {
                    selectedNode = node;
                    processing = product && !fields[1].IsAbsent && ReadBoolean(fields[1]);
                }
            }
            bool adding = command.Id is "add-product" or "add-recipe";
            if (adding != selectedNode.IsNull)
            {
                throw new ServiceResultException(adding ? StatusCodes.BadNodeIdExists : StatusCodes.BadNotFound,
                    adding ? "The reviewed identifier already exists." : "The reviewed identifier no longer exists.");
            }
            if (product && !adding)
            {
                bool selected = false;
                foreach (string id in current)
                {
                    selected |= id == payload.Identifier;
                }
                if ((command.Id == "select-product" && (selected || processing)) ||
                    (command.Id == "deselect-product" && !selected) ||
                    (command.Id == "remove-product" && (selected || processing)))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "The product selection or processing state does not permit this command.");
                }
            }
            return selectedNode;
        }

        private static void RequireScalar(Field field, BuiltInType type)
        {
            RequireGood(field.Status, field.Specification.Name);
            if (!field.Value.TypeInfo.IsScalar || field.Value.TypeInfo.BuiltInType != type)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    field.Specification.Name + " has the wrong type.");
            }
        }

        private static string ReadString(Field field)
        {
            RequireScalar(field, BuiltInType.String);
            return field.Value.TryGetValue(out string? value) && value is not null
                ? value
                : throw new ServiceResultException(StatusCodes.BadTypeMismatch, "A non-null string is required.");
        }

        private static bool ReadBoolean(Field field)
        {
            RequireScalar(field, BuiltInType.Boolean);
            return field.Value.TryGetValue(out bool value) && value;
        }

        private static void RequireUnit(EUInformation unit)
        {
            RequireText(unit.NamespaceUri, 2048, "Engineering-unit namespace");
            RequireText(unit.DisplayName.Text, 256, "Engineering-unit name", allowEmpty: true);
            RequireText(unit.Description.Text, 256, "Engineering-unit description", allowEmpty: true);
            if (unit.UnitId < 0 ||
                !Uri.TryCreate(unit.NamespaceUri, UriKind.Absolute, out Uri? uri) ||
                !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Invalid engineering-unit identity.");
            }
        }

        private static async ValueTask<NodeId> DispatchAsync(
            CompanionContext context, PreparedCompanionMethodClient session, PreparedTask task,
            CancellationToken cancellationToken)
        {
            NodeId owner = task.Evidence.Binding.Owner;
            var scale = new ScaleDeviceTypeClient(session, owner, context.Telemetry);
            var preset = new ProductionPresetTypeClient(session, owner, context.Telemetry);
            var recipes = new RecipeManagementTypeClient(session, owner, context.Telemetry);
            var recipeScale = new RecipeScaleTypeClient(session, owner, context.Telemetry);
            var pieces = new PieceCountingScaleTypeClient(session, owner, context.Telemetry);
            var vehicle = new VehicleScaleTypeClient(session, owner, context.Telemetry);
            Payload payload = task.Payload;
            switch (task.Command.Id)
            {
                case "set-zero":
                    await scale.SetZeroAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "set-tare":
                    await scale.SetTareAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "clear-tare":
                    await scale.ClearTareAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "preset-tare":
                    await scale.SetPresetTareAsync(payload.Number, task.Evidence.Unit!.ToValue(), cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case "register-weight":
                    await scale.RegisterWeightAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "select-product":
                    await preset.SelectProductAsync(payload.Identifier, cancellationToken).ConfigureAwait(false);
                    break;
                case "deselect-product":
                    await preset.DeselectProductAsync(payload.Identifier, cancellationToken).ConfigureAwait(false);
                    break;
                case "switch-product":
                    await preset.SwitchProductAsync(payload.Identifier, cancellationToken).ConfigureAwait(false);
                    break;
                case "remove-product":
                    await preset.RemoveProductAsync(payload.Identifier, cancellationToken).ConfigureAwait(false);
                    break;
                case "add-product":
                    return await preset.AddProductAsync(
                        payload.Name, payload.Identifier, NodeId.Null, cancellationToken).ConfigureAwait(false);
                case "add-recipe":
                    return await recipes.AddRecipeAsync(
                        payload.Identifier, payload.RecipeName, cancellationToken).ConfigureAwait(false);
                case "remove-recipe":
                    await recipes.RemoveRecipeAsync(payload.Identifier, cancellationToken).ConfigureAwait(false);
                    break;
                case "start-recipe":
                    await recipeScale.StartRecipeAsync(task.Evidence.Subject, cancellationToken).ConfigureAwait(false);
                    break;
                case "stop-recipe":
                    await recipeScale.StopRecipeAsync(task.Evidence.Subject, cancellationToken).ConfigureAwait(false);
                    break;
                case "continue-recipe":
                    await recipeScale.ContinueRecipeAsync(task.Evidence.Subject, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case "abort-recipe":
                    await recipeScale.AbortRecipeAsync(task.Evidence.Subject, cancellationToken).ConfigureAwait(false);
                    break;
                case "skip-recipe-element":
                    await recipeScale.SkipCurrentRecipeElementAsync(task.Evidence.Subject, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case "reference-piece-weight":
                    await pieces.SetReferencePieceWeightAsync(
                        payload.Count, task.Evidence.Unit!.ToValue(), cancellationToken).ConfigureAwait(false);
                    break;
                case "reference-piece-count":
                    await pieces.SetNumberOfReferencePiecesAsync(payload.Count, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case "start-reference":
                    await pieces.StartReferenceAsync(payload.Count, cancellationToken).ConfigureAwait(false);
                    break;
                case "inbound-weighing":
                    await vehicle.InboundWeighingAsync(payload.Identifier, cancellationToken).ConfigureAwait(false);
                    break;
                case "outbound-weighing":
                    await vehicle.OutboundWeighingAsync(payload.Identifier, cancellationToken).ConfigureAwait(false);
                    break;
                case "one-pass-weighing":
                    await vehicle.OnePassWeighingAsync(payload.Identifier, cancellationToken).ConfigureAwait(false);
                    break;
                case "reset-statistics":
                    await new ScaleSystemTypeClient(session, owner, context.Telemetry)
                        .ResetGlobalStatisticsAsync(cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            return NodeId.Null;
        }

        private sealed class PreparedTask(
            CompanionTarget target, Command command, Payload payload, CommandEvidence evidence,
            SessionStamp stamp, WeighingSimulationGrant grant) : CompanionTaskInput
        {
            public override string Review =>
                Command.MethodName +
                " on " +
                Target.NodeId +
                ". " +
                Payload.Review +
                " Verified owned simulation only. No physical calibration or metrology certification. " +
                "Owned-run grant expires at " +
                Grant.ExpiresAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture) +
                ". " +
                "State, units and permissions are rechecked; ambiguous outcomes are never replayed.";

            public CompanionTarget Target { get; } = target;

            public Command Command { get; } = command;

            public Payload Payload { get; } = payload;

            public CommandEvidence Evidence { get; } = evidence;

            public SessionStamp Stamp { get; } = stamp;

            public WeighingSimulationGrant Grant { get; } = grant;

            public void RequireUnused()
            {
                if (Volatile.Read(ref m_consumed) != 0)
                {
                    throw new InvalidOperationException("This weighing preparation was consumed. Do not replay it.");
                }
            }

            public void Consume()
            {
                if (Interlocked.Exchange(ref m_consumed, 1) != 0)
                {
                    throw new InvalidOperationException("This weighing preparation was consumed. Do not replay it.");
                }
            }

            private int m_consumed;
        }
    }
}
