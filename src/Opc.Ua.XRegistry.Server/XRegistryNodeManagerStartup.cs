/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Keeps incomplete startup references private and rolls back a failed model load.
    /// </summary>
    internal static class XRegistryNodeManagerStartup
    {
        public static async ValueTask RunAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            Func<IDictionary<NodeId, IList<IReference>>, CancellationToken, ValueTask> initialize,
            Func<CancellationToken, ValueTask> rollback,
            CancellationToken cancellationToken)
        {
            if (externalReferences is null)
            {
                throw new ArgumentNullException(nameof(externalReferences));
            }
            var staged = new Dictionary<NodeId, IList<IReference>>();
            var published = new List<KeyValuePair<NodeId, IReference>>();
            var addedEntries = new List<NodeId>();
            bool completed = false;
            try
            {
                await initialize(staged, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                foreach (KeyValuePair<NodeId, IList<IReference>> entry in staged)
                {
                    if (!externalReferences.TryGetValue(entry.Key, out IList<IReference>? references))
                    {
                        externalReferences.Add(entry.Key, references = []);
                        addedEntries.Add(entry.Key);
                    }
                    foreach (IReference reference in entry.Value)
                    {
                        if (!references.Any(existing =>
                            existing.ReferenceTypeId == reference.ReferenceTypeId &&
                            existing.IsInverse == reference.IsInverse &&
                            existing.TargetId == reference.TargetId))
                        {
                            references.Add(reference);
                            published.Add(new KeyValuePair<NodeId, IReference>(entry.Key, reference));
                        }
                    }
                }
                completed = true;
            }
            finally
            {
                if (!completed)
                {
                    foreach (KeyValuePair<NodeId, IReference> reference in published)
                    {
                        externalReferences[reference.Key].Remove(reference.Value);
                    }
                    foreach (NodeId key in addedEntries)
                    {
                        if (externalReferences[key].Count == 0)
                        {
                            externalReferences.Remove(key);
                        }
                    }
                    await rollback(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
    }
}
