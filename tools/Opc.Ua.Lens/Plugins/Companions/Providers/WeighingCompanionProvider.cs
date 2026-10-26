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
using Opc.Ua.Scales.Client;
using ReferenceTypeIds = Opc.Ua.ReferenceTypeIds;
using Scales = Opc.Ua.Scales;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Bounded OPC 40200 inspection and typed, single-use operations on a verified
    /// owned simulation. The primary session, monitoring and exports remain host-owned.
    /// </summary>
    internal sealed partial class WeighingCompanionProvider : IPreparedCompanionProvider
    {
        /// <summary>
        /// Creates the provider. Without a host-owned simulation policy, every mutation is denied.
        /// The injected clock enforces the fixed expiry of each owned-run grant.
        /// </summary>
        public WeighingCompanionProvider(
            IWeighingSimulationPolicy? simulationPolicy = null, TimeProvider? timeProvider = null)
        {
            m_simulationPolicy = simulationPolicy;
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public CompanionDescriptor Descriptor { get; } = new(
            kProviderId, "Weighing", Scales.Namespaces.Scales,
            "OPC 40200 Weighing Technology. Server-produced measurements; no legal-for-trade or " +
            "physical calibration claim. Commands require a verified owned simulation.");

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
            CompanionContext context, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            cancellationToken = lifetime.Token;
            var stamp = new SessionStamp(context);
            var client = new ScalesClient(context.Session, context.Telemetry);
            var budget = new IndustrialBrowseBudget(context);
            var pending = new Queue<NodeId>();
            var visited = new HashSet<NodeId>();
            var targets = new List<CompanionTarget>();
            foreach (NodeId root in new[] { client.Topology.DeviceSetId, client.MachinesFolderId })
            {
                if (!root.IsNull && visited.Add(root))
                {
                    pending.Enqueue(root);
                }
            }
            while (pending.TryDequeue(out NodeId parent))
            {
                await foreach (ReferenceDescription reference in IndustrialCompanionAccess.BrowseAsync(
                    context, parent, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences,
                    NodeClass.Object, budget, cancellationToken).ConfigureAwait(false))
                {
                    NodeId node = IndustrialCompanionAccess.LocalId(context, reference.NodeId);
                    if (node.IsNull || reference.NodeClass != NodeClass.Object || !visited.Add(node))
                    {
                        continue;
                    }
                    CellCompanionSupport.CheckCount(visited.Count, budget.MaxNodes, "Weighing discovery nodes");
                    ScaleEntry? scale = await ClassifyAsync(
                        context, client, reference, budget, cancellationToken).ConfigureAwait(false);
                    if (scale is null)
                    {
                        pending.Enqueue(node);
                        continue;
                    }
                    CellCompanionSupport.CheckCount(targets.Count + 1, context.MaxTargets, "Weighing targets");
                    string name = scale.DisplayName.Text ?? scale.BrowseName.Name ?? KindName(scale);
                    RequireText(name, 256, "Scale display name");
                    targets.Add(new CompanionTarget(kProviderId, node, name, KindName(scale)));
                    NodeId children = await ResolveAsync(
                        context, node, Scales.BrowseNames.SubDevices, cancellationToken).ConfigureAwait(false);
                    if (!children.IsNull && visited.Add(children))
                    {
                        pending.Enqueue(children);
                    }
                }
            }
            stamp.RequireMatch(context);
            cancellationToken.ThrowIfCancellationRequested();
            return [.. targets];
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionInspection> InspectAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            cancellationToken = lifetime.Token;
            var stamp = new SessionStamp(context);
            ScaleEntry scale = await RequireScaleAsync(context, target, cancellationToken).ConfigureAwait(false);
            var values = new FieldBuffer(context.MaxFields);
            values.Add(new CompanionValue("Kind", Variant.From(KindName(scale))));
            if (scale.IsScaleSystem)
            {
                values.Add(Absent("CurrentWeight", "A scale system has no weight of its own; select a SubDevice."));
            }
            else
            {
                await InspectWeightAsync(context, target.NodeId, Scales.BrowseNames.CurrentWeight, values,
                    cancellationToken).ConfigureAwait(false);
                await InspectWeightAsync(context, target.NodeId, Scales.BrowseNames.RegisteredWeight, values,
                    cancellationToken).ConfigureAwait(false);
            }
            List<CompanionOperation> operations = [.. s_sections];
            for (int index = 0; index < s_commands.Count; index++)
            {
                Command command = s_commands[index];
                if (!command.Applies(scale))
                {
                    continue;
                }
                MethodAvailability availability = await ReadAvailabilityAsync(
                    context, target.NodeId, command, cancellationToken).ConfigureAwait(false);
                values.Add(new CompanionValue("Command / " + command.MethodName, Variant.From(availability.Reason))
                {
                    Quality = availability.Status
                });
                if (availability.CanExecute)
                {
                    operations.Add(command.Operation);
                }
            }
            stamp.RequireMatch(context);
            cancellationToken.ThrowIfCancellationRequested();
            return new CompanionInspection(values.ToArray(), [.. operations],
                "Weights and limits are reported by the server, not calculated by Lens. " +
                "Monitor, history, export and notifications require an explicit action. " +
                "Mutations additionally require deployment authorization, confirmation " +
                "and a verified owned simulation. " +
                "PackML state presentation belongs to the shared PackML tool.");
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionOperationResult> ExecuteAsync(
            CompanionContext context, CompanionTarget target, string operationId, string? input,
            CancellationToken cancellationToken)
        {
            CellCompanionSupport.RequireNoInput(input);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSection(operationId))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "Only the listed read sections run directly. A weighing command requires typed preparation.");
            }
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            cancellationToken = lifetime.Token;
            var stamp = new SessionStamp(context);
            ScaleEntry scale = await RequireScaleAsync(context, target, cancellationToken).ConfigureAwait(false);
            var values = new FieldBuffer(context.MaxFields);
            switch (operationId)
            {
                case "weights":
                    if (scale.IsScaleSystem)
                    {
                        values.Add(Absent("CurrentWeight", "Select a scale in this system's SubDevices."));
                    }
                    else
                    {
                        await InspectWeightAsync(context, target.NodeId, Scales.BrowseNames.CurrentWeight,
                            values, cancellationToken).ConfigureAwait(false);
                        await InspectWeightAsync(context, target.NodeId, Scales.BrowseNames.RegisteredWeight,
                            values, cancellationToken).ConfigureAwait(false);
                    }
                    break;
                case "ranges":
                    await InspectRangesAsync(context, target.NodeId, values, cancellationToken).ConfigureAwait(false);
                    break;
                case "notifications":
                    await InspectNotificationsAsync(context, target.NodeId, values, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                default:
                    await InspectSectionAsync(context, scale, operationId, values, cancellationToken)
                        .ConfigureAwait(false);
                    break;
            }
            stamp.RequireMatch(context);
            cancellationToken.ThrowIfCancellationRequested();
            return new CompanionOperationResult(
                operationId == "notifications"
                    ? "Notification source and OPC 40200 filter metadata only. Open the shared Alarms tool " +
                        "to observe events; no subscription or condition command was started."
                    : "Read the selected weighing section only. Missing, bad and uncertain data remain explicit.",
                values.ToArray());
        }

        private static CancellationTokenSource Begin(CompanionContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.Session.Connected || context.Session.SessionId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadSessionClosed, "The weighing session is disconnected.");
            }
            return CellCompanionSupport.BeginOperation(context, Scales.Namespaces.Scales, cancellationToken);
        }

        private static async ValueTask<ScaleEntry?> ClassifyAsync(
            CompanionContext context, ScalesClient client, ReferenceDescription reference,
            IndustrialBrowseBudget budget, CancellationToken cancellationToken)
        {
            string? kind = await IndustrialCompanionAccess.ClassifyAsync(
                context, reference.TypeDefinition, s_scaleTypes, budget, cancellationToken).ConfigureAwait(false);
            if (kind is null)
            {
                return null;
            }
            NodeId type = IndustrialCompanionAccess.LocalId(context, reference.TypeDefinition);
            ScaleKind? typedKind = kind == kSystemKind ? null :
                await client.GetKindAsync(type, cancellationToken).ConfigureAwait(false);
            if (kind != kSystemKind && typedKind?.ToString() != kind)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "The live type ancestry differs from the Scales client's type metadata. Refresh the session.");
            }
            return new ScaleEntry(IndustrialCompanionAccess.LocalId(context, reference.NodeId),
                reference.BrowseName, reference.DisplayName, type, typedKind, kind == kSystemKind);
        }

        private static async ValueTask<ScaleEntry> RequireScaleAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            CellCompanionSupport.ValidateTarget(context, target, kProviderId);
            ArrayOf<DataValue> attributes = await ReadAsync(context,
                [new ReadValueId { NodeId = target.NodeId, AttributeId = Attributes.NodeClass }],
                cancellationToken).ConfigureAwait(false);
            RequireGood(attributes[0].StatusCode, "Scale node class");
            if (!attributes[0].WrappedValue.TryGetValue(out int nodeClass) || nodeClass != (int)NodeClass.Object)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The selected scale is not an Object.");
            }
            NodeId type = await ReadTypeAsync(context, target.NodeId, cancellationToken).ConfigureAwait(false);
            ScaleEntry? scale = await ClassifyAsync(context, new ScalesClient(context.Session, context.Telemetry),
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(target.NodeId),
                    TypeDefinition = new ExpandedNodeId(type),
                    DisplayName = new LocalizedText(target.DisplayName)
                }, new IndustrialBrowseBudget(context), cancellationToken).ConfigureAwait(false);
            if (scale is null || KindName(scale) != target.TypeName)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "The selected weighing type changed. Discover and prepare again.");
            }
            return scale;
        }

        private static async ValueTask<NodeId> ReadTypeAsync(
            CompanionContext context, NodeId node, CancellationToken cancellationToken)
        {
            NodeId result = default;
            await foreach (ReferenceDescription reference in IndustrialCompanionAccess.BrowseAsync(
                context, node, BrowseDirection.Forward, ReferenceTypeIds.HasTypeDefinition,
                NodeClass.ObjectType, new IndustrialBrowseBudget(context), cancellationToken).ConfigureAwait(false))
            {
                if (!result.IsNull || reference.NodeClass != NodeClass.ObjectType)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Ambiguous weighing object type.");
                }
                result = IndustrialCompanionAccess.LocalId(context, reference.NodeId);
            }
            if (result.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "No local weighing object type.");
            }
            return result;
        }

        private static string KindName(ScaleEntry scale)
        {
            return scale.IsScaleSystem ? kSystemKind : scale.Kind?.ToString() ??
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "No concrete OPC 40200 scale kind.");
        }

        private static bool IsSection(string id)
        {
            foreach (CompanionOperation section in s_sections)
            {
                if (section.Id == id)
                {
                    return true;
                }
            }
            return false;
        }

        private static ArrayOf<IndustrialCompanionType> ScaleTypes()
        {
            var types = new List<IndustrialCompanionType>
            {
                new(Scales.ObjectTypeIds.ScaleSystemType, kSystemKind)
            };
            foreach (ScaleKind kind in ScalesModel.AllKinds)
            {
                types.Add(new IndustrialCompanionType(
                    new ExpandedNodeId(ScalesModel.ObjectTypeOf(kind), Scales.Namespaces.Scales), kind.ToString()));
            }
            return [.. types];
        }

        private readonly IWeighingSimulationPolicy? m_simulationPolicy;
        private readonly TimeProvider m_timeProvider;
        private const string kProviderId = "weighing";
        private const string kSystemKind = "Scale system";
        private const int kMaxItems = 64;
        private static readonly ArrayOf<IndustrialCompanionType> s_scaleTypes = ScaleTypes();

        private static readonly ArrayOf<CompanionOperation> s_sections =
        [
            new("weights", "Read current and registered weights", CompanionOperationSafety.ReadOnly),
            new("identification", "Read identification", CompanionOperationSafety.ReadOnly),
            new("ranges", "Read units and weighing ranges", CompanionOperationSafety.ReadOnly),
            new("products", "Read products and selection", CompanionOperationSafety.ReadOnly),
            new("statistics", "Read server statistics", CompanionOperationSafety.ReadOnly),
            new("recipes", "Read recipes and elements", CompanionOperationSafety.ReadOnly),
            new("type-values", "Read kind-specific values", CompanionOperationSafety.ReadOnly),
            new("notifications", "Inspect notification source", CompanionOperationSafety.ReadOnly)
        ];
    }
}
