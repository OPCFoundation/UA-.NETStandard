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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Materializes a local Condition instance separately from its EventType
    /// declaration. The instance and its standard children belong to the
    /// importing node-manager generation.
    /// </summary>
    public interface IWotProjectionConditionFactory
    {
        /// <summary>
        /// Creates and registers the Condition instance for an event declaration.
        /// </summary>
        ValueTask<ConditionState> CreateAsync(
            INodeManagerBuilder builder,
            BaseObjectState notifier,
            WotProjectedAffordance declaration,
            NodeId eventTypeId,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Uses the base stack's Condition state factories and node registration,
    /// without a dependency on a companion model or generated sample types.
    /// </summary>
    public sealed class WotProjectionConditionFactory : IWotProjectionConditionFactory
    {
        /// <inheritdoc/>
        public async ValueTask<ConditionState> CreateAsync(
            INodeManagerBuilder builder,
            BaseObjectState notifier,
            WotProjectedAffordance declaration,
            NodeId eventTypeId,
            CancellationToken cancellationToken = default)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (notifier is null)
            {
                throw new ArgumentNullException(nameof(notifier));
            }
            if (declaration is null)
            {
                throw new ArgumentNullException(nameof(declaration));
            }
            if (builder.NodeManager is not AsyncCustomNodeManager manager)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "Condition materialization requires a registering async node manager.");
            }

            ISystemContext context = builder.Context;
            context.RequireNodeIdFactory();
            NodeId nodeId = new(declaration.NodeId + "#Condition", eventTypeId.NamespaceIndex);
            if (manager.FindPredefinedNode<NodeState>(nodeId) is not null)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdExists);
            }
            NodeId baseTypeId = string.IsNullOrEmpty(declaration.ConditionTypeId)
                ? builder.Node<BaseObjectTypeState>(eventTypeId).Node.SuperTypeId
                : ExpandedNodeId.Parse(declaration.ConditionTypeId!, context.NamespaceUris);
            if (baseTypeId != Ua.ObjectTypeIds.ConditionType &&
                !context.TypeTable.IsTypeOf(baseTypeId, Ua.ObjectTypeIds.ConditionType))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeDefinitionInvalid, "The event declaration does not derive from ConditionType.");
            }
            ConditionState condition = context.TypeTable.IsTypeOf(baseTypeId, Ua.ObjectTypeIds.LimitAlarmType)
                ? new LimitAlarmState(notifier)
                : context.TypeTable.IsTypeOf(baseTypeId, Ua.ObjectTypeIds.AlarmConditionType)
                    ? new AlarmConditionState(notifier)
                    : context.TypeTable.IsTypeOf(baseTypeId, Ua.ObjectTypeIds.AcknowledgeableConditionType)
                        ? new AcknowledgeableConditionState(notifier)
                        : new ConditionState(notifier);
            condition.Create(
                context, nodeId,
                new QualifiedName(declaration.Name, eventTypeId.NamespaceIndex),
                new LocalizedText(declaration.Name), assignNodeIds: false);
            if (condition is AcknowledgeableConditionState acknowledgeable)
            {
                acknowledgeable.ConfirmedState ??= new TwoStateVariableState(condition);
                acknowledgeable.ConfirmedState.Create(
                    context, NodeId.Null, QualifiedName.From(Ua.BrowseNames.ConfirmedState),
                    new LocalizedText(Ua.BrowseNames.ConfirmedState), assignNodeIds: false);
                acknowledgeable.Confirm ??= new AddCommentMethodState(condition);
                acknowledgeable.Confirm.Create(
                    context, NodeId.Null, QualifiedName.From(Ua.BrowseNames.Confirm),
                    new LocalizedText(Ua.BrowseNames.Confirm), assignNodeIds: false);
                acknowledgeable.Confirm.Executable = true;
                acknowledgeable.Confirm.UserExecutable = true;
                acknowledgeable.Confirm.CreateOrReplaceInputArguments(context, null).Value =
                [
                    new Argument
                    {
                        Name = Ua.BrowseNames.EventId,
                        DataType = Ua.DataTypeIds.ByteString,
                        ValueRank = ValueRanks.Scalar
                    },
                    new Argument
                    {
                        Name = Ua.BrowseNames.Comment,
                        DataType = Ua.DataTypeIds.LocalizedText,
                        ValueRank = ValueRanks.Scalar
                    }
                ];
            }
            context.AssignInstanceChildNodeIds(condition);
            condition.TypeDefinitionId = eventTypeId;
            condition.ReferenceTypeId = Ua.ReferenceTypeIds.HasCondition;
            condition.AutoReportStateChanges = false;
            notifier.AddChild(condition);
            await manager.AddPredefinedNodeAsync(condition, cancellationToken).ConfigureAwait(false);
            return condition;
        }
    }
}
