// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Validates portable native condition values against the existing local condition shape.
    /// </summary>
    internal static class NativeConditionFields
    {
        /// <summary>
        /// Decodes and validates the whole field set before the caller applies any changes.
        /// </summary>
        internal static List<(BaseVariableState Target, BaseVariableState Source)> ReadUpdates(
            ISystemContext context, AcknowledgeableConditionState target, ByteString payload)
        {
            if (NodeStateSerializer.Deserialize(context, payload) is not BaseObjectState source ||
                target.TypeDefinitionId.IsNull || source.TypeDefinitionId != target.TypeDefinitionId)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "The native condition type does not match the bound condition.");
            }

            var updates = new List<(BaseVariableState, BaseVariableState)>();
            Collect(context, target, source, updates, root: true);
            return updates;
        }

        private static void Collect(
            ISystemContext context,
            NodeState target,
            NodeState source,
            List<(BaseVariableState, BaseVariableState)> updates,
            bool root = false)
        {
            var children = new List<BaseInstanceState>();
            var names = new HashSet<QualifiedName>();
            source.GetChildren(context, children);
            foreach (BaseInstanceState field in children)
            {
                if (field.NodeClass is not (NodeClass.Variable or NodeClass.Object) ||
                    (root && (field.BrowseName == BrowseNames.EventId ||
                        field.BrowseName == BrowseNames.BranchId || field.BrowseName == BrowseNames.EventType)))
                {
                    continue;
                }

                BaseInstanceState? live = target.FindChild(context, field.BrowseName);
                if (!names.Add(field.BrowseName) || live == null || live.NodeClass != field.NodeClass)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                        "A native condition field does not match the bound condition.");
                }
                if (live is BaseVariableState variable && field is BaseVariableState saved)
                {
                    if (variable.DataType != saved.DataType || variable.ValueRank != saved.ValueRank)
                    {
                        throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                            "A native condition variable has the wrong type or value rank.");
                    }
                    updates.Add((variable, saved));
                }
                Collect(context, live, field, updates);
            }
        }
    }
}
