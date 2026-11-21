// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Programs
{
    public sealed partial class ProgramClient
    {
        /// <summary>
        /// Discovers Program subtypes by following HasSubtype, not by scanning the server.
        /// The requested root defaults to ProgramStateMachineType and is not included in the result.
        /// </summary>
        public async ValueTask<ArrayOf<ReferenceDescription>> DiscoverTypesAsync(
            NodeId rootTypeId = default,
            CancellationToken ct = default)
        {
            if (rootTypeId.IsNull)
            {
                rootTypeId = ObjectTypeIds.ProgramStateMachineType;
            }
            if (!await IsTypeOfAsync(rootTypeId, ObjectTypeIds.ProgramStateMachineType, new StrongBox<int>(), ct)
                .ConfigureAwait(false))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The root is not a Program type.");
            }
            var pending = new Queue<NodeId>();
            var visited = new HashSet<NodeId> { rootTypeId };
            var found = new List<ReferenceDescription>();
            pending.Enqueue(rootTypeId);
            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                ArrayOf<ReferenceDescription> references = await BrowseAllAsync(
                    pending.Dequeue(), ReferenceTypeIds.HasSubtype, BrowseDirection.Forward, NodeClass.ObjectType, ct)
                    .ConfigureAwait(false);
                foreach (ReferenceDescription reference in references)
                {
                    NodeId id = LocalNodeId(reference.NodeId);
                    if (visited.Add(id))
                    {
                        EnsureWalkBudget(visited.Count, MaxDiscoveryNodes);
                        found.Add(reference);
                        pending.Enqueue(id);
                    }
                }
            }
            return ArrayOf.Wrapped(found.ToArray());
        }

        /// <summary>
        /// Discovers Program invocations beneath an explicitly supplied hierarchy root, including
        /// the root itself when it is a matching invocation. Cycles and duplicate references are
        /// visited once. Type ancestry is resolved through HasSubtype and continuation points.
        /// </summary>
        public async ValueTask<ArrayOf<NodeId>> DiscoverInvocationsAsync(
            NodeId scopeId,
            NodeId typeId = default,
            bool includeSubtypes = true,
            CancellationToken ct = default)
        {
            ValidateNodeId(scopeId, nameof(scopeId));
            if (typeId.IsNull)
            {
                typeId = ObjectTypeIds.ProgramStateMachineType;
            }
            // one budget for every ancestry check of this discovery, so the types
            // the server reports cannot each start a walk with a budget of its own.
            var ancestry = new StrongBox<int>();
            if (!await IsTypeOfAsync(typeId, ObjectTypeIds.ProgramStateMachineType, ancestry, ct)
                .ConfigureAwait(false))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The requested type is not a Program.");
            }
            var pending = new Queue<NodeId>();
            var visited = new HashSet<NodeId> { scopeId };
            var typeMatches = new Dictionary<NodeId, bool>();
            var found = new List<NodeId>();
            pending.Enqueue(scopeId);
            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                NodeId id = pending.Dequeue();
                ArrayOf<ReferenceDescription> types = await BrowseAllAsync(
                    id, ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward, NodeClass.ObjectType, ct)
                    .ConfigureAwait(false);
                for (int index = 0; index < types.Count; index++)
                {
                    ReferenceDescription type = types[index];
                    NodeId actualType = LocalNodeId(type.NodeId);
                    if (!typeMatches.TryGetValue(actualType, out bool matches))
                    {
                        matches = actualType == typeId ||
                            (includeSubtypes &&
                                await IsTypeOfAsync(actualType, typeId, ancestry, ct).ConfigureAwait(false));
                        typeMatches.Add(actualType, matches);
                        EnsureWalkBudget(typeMatches.Count, MaxDiscoveryNodes);
                    }
                    if (matches)
                    {
                        found.Add(id);
                        break;
                    }
                }
                ArrayOf<ReferenceDescription> children = await BrowseAllAsync(
                    id, ReferenceTypeIds.HierarchicalReferences, BrowseDirection.Forward, NodeClass.Object, ct)
                    .ConfigureAwait(false);
                foreach (ReferenceDescription child in children)
                {
                    NodeId childId = LocalNodeId(child.NodeId);
                    if (visited.Add(childId))
                    {
                        EnsureWalkBudget(visited.Count, MaxDiscoveryNodes);
                        pending.Enqueue(childId);
                    }
                }
            }
            return ArrayOf.Wrapped(found.ToArray());
        }

        /// <summary>
        /// Tests type ancestry without requiring a prepopulated NodeCache. The types
        /// followed are counted in <paramref name="followed"/>, which the checks of one
        /// operation share.
        /// </summary>
        private async ValueTask<bool> IsTypeOfAsync(
            NodeId typeId,
            NodeId expected,
            StrongBox<int> followed,
            CancellationToken ct)
        {
            var pending = new Queue<NodeId>();
            var visited = new HashSet<NodeId> { typeId };
            EnsureWalkBudget(++followed.Value, MaxDiscoveryNodes);
            pending.Enqueue(typeId);
            while (pending.Count > 0)
            {
                NodeId current = pending.Dequeue();
                if (current == expected)
                {
                    return true;
                }
                ArrayOf<ReferenceDescription> parents = await BrowseAllAsync(
                    current, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse, NodeClass.ObjectType, ct)
                    .ConfigureAwait(false);
                foreach (ReferenceDescription parent in parents)
                {
                    NodeId parentId = LocalNodeId(parent.NodeId);
                    // a type is marked when it is queued, not when it is followed, so
                    // the queue holds each type once and stays within the budget.
                    if (visited.Add(parentId))
                    {
                        EnsureWalkBudget(++followed.Value, MaxDiscoveryNodes);
                        pending.Enqueue(parentId);
                    }
                }
            }
            return false;
        }
    }
}
