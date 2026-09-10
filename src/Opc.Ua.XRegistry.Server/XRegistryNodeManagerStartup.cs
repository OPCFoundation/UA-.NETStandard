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
