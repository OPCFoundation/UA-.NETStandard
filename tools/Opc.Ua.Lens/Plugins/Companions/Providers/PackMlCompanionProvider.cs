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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.PackML;
using PackTypes = Opc.Ua.PackML.ObjectTypeIds;
using PackMethods = Opc.Ua.PackML.MethodIds;
using PackNames = Opc.Ua.PackML.BrowseNames;
using UaNames = Opc.Ua.BrowseNames;
using static UaLens.Plugins.Companions.CompanionMethodMetadata;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Observes actual PackML state machines without assuming a scale-specific owner.
    /// Accepted commands do not imply equipment-driven completion.
    /// </summary>
    internal sealed partial class PackMlCompanionProvider : IPreparedCompanionProvider
    {
        /// <summary>
        /// Creates an adapter with default-deny mutation policy.
        /// </summary>
        public PackMlCompanionProvider(ICompanionDeploymentPolicy? policy = null, TimeProvider? timeProvider = null)
        {
            m_time = timeProvider ?? TimeProvider.System;
            m_policy = policy ?? new ConfiguredCompanionDeploymentPolicy(timeProvider: m_time);
        }

        /// <inheritdoc/>
        public CompanionDescriptor Descriptor { get; } = new(
            "packml", "PackML", Opc.Ua.PackML.Namespaces.PackML,
            "Published OPC 30050 PackML 1.01. Accepted commands do not prove equipment action has completed.");

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
            CompanionContext context, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime =
                CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
            return await IndustrialCompanionAccess.DiscoverAsync(
                context, Descriptor.Id, s_types, [Opc.Ua.ObjectIds.ObjectsFolder], lifetime.Token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionInspection> InspectAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime =
                CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
            ArrayOf<StateLevel> levels = await ReadLevelsAsync(context, target, lifetime.Token).ConfigureAwait(false);
            var values = new List<CompanionValue>();
            foreach (StateLevel level in levels)
            {
                values.Add(new CompanionValue(level.Kind + " state", Variant.From(level.Label))
                {
                    SourceNodeId = level.ValueNode,
                    Quality = level.Quality,
                    SourceTimestamp = level.Timestamp
                });
                values.Add(new CompanionValue(level.Kind + " state ID", Variant.From(level.StateId))
                {
                    SourceNodeId = level.IdNode,
                    Quality = level.Quality,
                    SourceTimestamp = level.Timestamp
                });
            }
            List<CompanionOperation> operations = [s_snapshot];
            for (int index = 0; index < s_commands.Count; index++)
            {
                Command command = s_commands[index];
                StateLevel? level = levels.ToList().SingleOrDefault(value => value.Kind == command.Level);
                if (level is null || !StatusCode.IsGood(level.Quality))
                {
                    continue;
                }
                string reason;
                StatusCode quality = StatusCodes.Good;
                try
                {
                    _ = await ResolveCommandAsync(context, level, command, lifetime.Token).ConfigureAwait(false);
                    var draft = new CompanionOperationDraft(
                        target, command.Operation, null, context.Session, m_time.GetUtcNow().AddMinutes(5));
                    _ = await AuthorizeAsync(context, draft, lifetime.Token).ConfigureAwait(false);
                    operations.Add(command.Operation);
                    reason = "Available; prepare and confirm before sending.";
                }
                catch (UnauthorizedAccessException)
                {
                    reason = "Requires an explicitly authorized owned simulation.";
                    quality = StatusCodes.BadUserAccessDenied;
                }
                catch (ServiceResultException error)
                {
                    reason = "Unavailable: " + error.StatusCode;
                    quality = error.StatusCode;
                }
                values.Add(new CompanionValue(command.Level + " / " + command.Name, Variant.From(reason))
                {
                    Quality = quality
                });
            }
            CellCompanionSupport.CheckCount(values.Count, context.MaxFields, "PackML inspection");
            return new CompanionInspection([.. values], [.. operations],
                "Read the active PackML hierarchy. No command was sent; acting-state completion remains equipment-owned.");
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionOperationResult> ExecuteAsync(
            CompanionContext context, CompanionTarget target, string operationId, string? input,
            CancellationToken cancellationToken)
        {
            CellCompanionSupport.RequireNoInput(input);
            if (operationId != "snapshot")
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "Prepare the typed command before sending.");
            }
            CompanionInspection result = await InspectAsync(context, target, cancellationToken).ConfigureAwait(false);
            return new CompanionOperationResult(result.Summary, result.Values);
        }

        private async Task<ArrayOf<StateLevel>> ReadLevelsAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            await IndustrialCompanionAccess.RequireTargetAsync(
                context, target, Descriptor.Id, s_types, cancellationToken).ConfigureAwait(false);
            List<StateLevel> levels =
                [await ReadLevelAsync(context, target.NodeId, target.TypeName, cancellationToken).ConfigureAwait(false)];
            StateLevel current = levels[0];
            if (current.Kind == BaseKind && current.Number == PackMLStateNumbers.Cleared)
            {
                NodeId machine = await IndustrialCompanionAccess.ResolveChildAsync(
                    context, current.Owner, Descriptor.ModelUri, PackNames.MachineState, false, cancellationToken)
                    .ConfigureAwait(false);
                await RequireLevelTypeAsync(context, machine, MachineKind, cancellationToken).ConfigureAwait(false);
                current = await ReadLevelAsync(context, machine, MachineKind, cancellationToken).ConfigureAwait(false);
                levels.Add(current);
            }
            if (current.Kind == MachineKind && current.Number == PackMLStateNumbers.Running)
            {
                NodeId execute = await IndustrialCompanionAccess.ResolveChildAsync(
                    context, current.Owner, Descriptor.ModelUri, PackNames.ExecuteState, false, cancellationToken)
                    .ConfigureAwait(false);
                await RequireLevelTypeAsync(context, execute, ExecuteKind, cancellationToken).ConfigureAwait(false);
                levels.Add(await ReadLevelAsync(context, execute, ExecuteKind, cancellationToken).ConfigureAwait(false));
            }
            return [.. levels];
        }

        private Task RequireLevelTypeAsync(
            CompanionContext context, NodeId node, string kind, CancellationToken cancellationToken)
        {
            return IndustrialCompanionAccess.RequireTargetAsync(
                context, new CompanionTarget(Descriptor.Id, node, kind, kind), Descriptor.Id, s_types, cancellationToken)
                .AsTask();
        }

        private static async Task<StateLevel> ReadLevelAsync(
            CompanionContext context, NodeId owner, string kind, CancellationToken cancellationToken)
        {
            NodeId current = await IndustrialCompanionAccess.ResolveChildAsync(
                context, owner, Opc.Ua.Namespaces.OpcUa, UaNames.CurrentState, false, cancellationToken)
                .ConfigureAwait(false);
            NodeId id = await IndustrialCompanionAccess.ResolveChildAsync(
                context, current, Opc.Ua.Namespaces.OpcUa, UaNames.Id, false, cancellationToken).ConfigureAwait(false);
            ReadResponse response = await context.Session.ReadAsync(null, 0, TimestampsToReturn.Both,
            [
                new ReadValueId { NodeId = current, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = id, AttributeId = Attributes.Value }
            ], cancellationToken).ConfigureAwait(false);
            if (response.ResponseHeader is null || response.Results.Count != 2)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "The PackML state read is incomplete.");
            }
            if (!StatusCode.IsGood(response.ResponseHeader.ServiceResult))
            {
                throw new ServiceResultException(response.ResponseHeader.ServiceResult);
            }
            DataValue label = response.Results[0];
            DataValue identity = response.Results[1];
            if (label.StatusCode == StatusCodes.BadStateNotActive &&
                (identity.StatusCode == StatusCodes.BadStateNotActive ||
                    (StatusCode.IsGood(identity.StatusCode) &&
                        identity.WrappedValue.TryGetValue(out NodeId inactiveId) &&
                        inactiveId.IsNull)))
            {
                return new StateLevel(kind, owner, current, id, LocalizedText.Null, NodeId.Null, 0,
                    StatusCodes.BadStateNotActive, null);
            }
            if (!StatusCode.IsGood(label.StatusCode) || !StatusCode.IsGood(identity.StatusCode))
            {
                throw new ServiceResultException(!StatusCode.IsGood(label.StatusCode)
                    ? label.StatusCode : identity.StatusCode, "The PackML state is not reliable for a command.");
            }
            if (!label.WrappedValue.TryGetValue(out LocalizedText text) ||
                text.IsNull ||
                !identity.WrappedValue.TryGetValue(out NodeId state) ||
                state.IsNull ||
                text.Text is { Length: > 4096 })
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "PackML state metadata is malformed.");
            }
            uint number = state.NamespaceIndex == context.Session.NamespaceUris.GetIndex(Opc.Ua.PackML.Namespaces.PackML) &&
                state.TryGetValue(out uint numeric) ? PackMLStateNumbers.FromStateId(numeric) : 0;
            return new StateLevel(kind, owner, current, id, text, state, number, label.StatusCode,
                label.SourceTimestamp.IsNull ? null : label.SourceTimestamp.ToDateTime());
        }

        private static Task<NodeId> ResolveCommandAsync(
            CompanionContext context, StateLevel level, Command command, CancellationToken cancellationToken)
        {
            if (level.Number == 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The active state is not a known PackML state.");
            }
            NodeId parameterType = ExpandedNodeId.ToNodeId(
                Opc.Ua.PackML.DataTypeIds.PackMLDescriptorDataType, context.Session.NamespaceUris);
            return RequireMethodAsync(
                context, level.Owner, Opc.Ua.PackML.Namespaces.PackML, command.Name,
                command.IsStart ? [parameterType] : [], [], cancellationToken,
                command.IsStart ? ValueRanks.OneDimension : ValueRanks.Scalar);
        }

        private readonly ICompanionDeploymentPolicy m_policy;
        private readonly TimeProvider m_time;
        private const string BaseKind = "PackML base";
        private const string MachineKind = "PackML machine";
        private const string ExecuteKind = "PackML execute";

        private static readonly CompanionOperation s_snapshot = new(
            "snapshot", "Read current PackML state", CompanionOperationSafety.ReadOnly);

        private static readonly ArrayOf<IndustrialCompanionType> s_types =
        [
            new(PackTypes.PackMLBaseStateMachineType, BaseKind),
            new(PackTypes.PackMLMachineStateMachineType, MachineKind),
            new(PackTypes.PackMLExecuteStateMachineType, ExecuteKind)
        ];

        private static readonly ArrayOf<Command> s_commands =
        [
            new("abort", PackNames.Abort, BaseKind, PackMethods.PackMLBaseStateMachineType_Abort),
            new("clear", PackNames.Clear, BaseKind, PackMethods.PackMLBaseStateMachineType_Clear),
            new("stop", PackNames.Stop, MachineKind, PackMethods.PackMLMachineStateMachineType_Stop),
            new("reset-machine", PackNames.Reset, MachineKind, PackMethods.PackMLMachineStateMachineType_Reset),
            new("reset-execute", PackNames.Reset, ExecuteKind, PackMethods.PackMLExecuteStateMachineType_Reset),
            new("start", PackNames.Start, ExecuteKind, PackMethods.PackMLExecuteStateMachineType_Start),
            new("hold", PackNames.Hold, ExecuteKind, PackMethods.PackMLExecuteStateMachineType_Hold),
            new("unhold", PackNames.Unhold, ExecuteKind, PackMethods.PackMLExecuteStateMachineType_Unhold),
            new("suspend", PackNames.Suspend, ExecuteKind, PackMethods.PackMLExecuteStateMachineType_Suspend),
            new("unsuspend", PackNames.Unsuspend, ExecuteKind, PackMethods.PackMLExecuteStateMachineType_Unsuspend),
            new("complete", PackNames.ToComplete, ExecuteKind, PackMethods.PackMLExecuteStateMachineType_ToComplete)
        ];

        /// <summary>
        /// State evidence includes the variable identities, not only a display label.
        /// </summary>
        private sealed record StateLevel(
            string Kind, NodeId Owner, NodeId ValueNode, NodeId IdNode, LocalizedText Label,
            NodeId StateId, uint Number, StatusCode Quality, DateTime? Timestamp)
        {
            public bool Matches(StateLevel other) => Kind == other.Kind &&
                Owner == other.Owner &&
                ValueNode == other.ValueNode &&
                IdNode == other.IdNode &&
                StateId == other.StateId &&
                Quality == other.Quality;
        }

        /// <summary>
        /// A standard generated cause and the exact level that owns it.
        /// </summary>
        private sealed class Command
        {
            public Command(string id, string name, string level, ExpandedNodeId declaration)
            {
                Name = name;
                Level = level;
                Declaration = declaration;
                IsStart = id == "start";
                Operation = new CompanionOperation(
                    id, name + " (" + level + ")", CompanionOperationSafety.DeploymentMutation)
                {
                    Inputs = IsStart
                        ? [new CompanionInputDefinition("parameters", "Start parameters", BuiltInType.ExtensionObject,
                            "Up to 32 unique, finite descriptors; an empty array starts without parameters.")
                        {
                            DataTypeId = Opc.Ua.PackML.DataTypeIds.PackMLDescriptorDataType,
                            ValueRank = ValueRanks.OneDimension
                        }]
                        : []
                };
            }

            public string Name { get; }
            public string Level { get; }
            public ExpandedNodeId Declaration { get; }
            public bool IsStart { get; }
            public CompanionOperation Operation { get; }
        }
    }
}
