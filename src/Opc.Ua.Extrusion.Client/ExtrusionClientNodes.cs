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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Extrusion.Client
{
    /// <summary>
    /// Resolves model-generated accessors through the existing managed browse infrastructure.
    /// </summary>
    internal static class ExtrusionClientNodes
    {
        /// <summary>
        /// Resolves a namespace-URI-based child, including properties and AddIns.
        /// </summary>
        internal static ValueTask<NodeId> ResolveAsync(
            ObjectTypeClient proxy,
            string namespaceUri,
            string browseName,
            CancellationToken cancellationToken)
        {
            ISession session = RequireSession(proxy);
            return ResolveAsync(session, proxy.ObjectId, namespaceUri, browseName, cancellationToken);
        }

        /// <summary>
        /// Resolves properties of a variable through the same managed browse path.
        /// </summary>
        internal static async ValueTask<NodeId> ResolveAsync(
            ISession session,
            NodeId parent,
            string namespaceUri,
            string browseName,
            CancellationToken cancellationToken)
        {
            int index = session.NamespaceUris.GetIndex(namespaceUri);
            if (index < 0)
            {
                return NodeId.Null;
            }
            var name = new QualifiedName(browseName, (ushort)index);
            NodeId found = NodeId.Null;
            foreach (ReferenceDescription reference in
                await BrowseAsync(session, parent, 0, cancellationToken).ConfigureAwait(false))
            {
                if (reference.BrowseName != name)
                {
                    continue;
                }
                if (!found.IsNull)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadBrowseNameDuplicated, "The server exposes an ambiguous standard child.");
                }
                found = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
            }
            return found;
        }

        /// <summary>
        /// Enumerates typed real placeholder instances, including conforming vendor subtypes.
        /// </summary>
        /// <typeparam name="TProxy">The generated client proxy for each concrete instance.</typeparam>
        internal static async IAsyncEnumerable<TProxy> EnumerateAsync<TProxy>(
            ObjectTypeClient parent,
            ExpandedNodeId expectedType,
            Func<ISession, NodeId, ITelemetryContext, TProxy> create,
            [EnumeratorCancellation] CancellationToken cancellationToken)
            where TProxy : ObjectTypeClient
        {
            ISession session = RequireSession(parent);
            NodeId type = ExpandedNodeId.ToNodeId(expectedType, session.NamespaceUris);
            if (type.IsNull)
            {
                yield break;
            }
            var seen = new HashSet<NodeId>();
            ArrayOf<ReferenceDescription> references = await BrowseAsync(
                session, parent.ObjectId, (uint)NodeClass.Object, cancellationToken).ConfigureAwait(false);
            for (int index = 0; index < references.Count; index++)
            {
                ReferenceDescription reference = references[index];
                NodeId actualType = ExpandedNodeId.ToNodeId(reference.TypeDefinition, session.NamespaceUris);
                NodeId node = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                if (!node.IsNull && !actualType.IsNull && seen.Add(node) &&
                    await session.NodeCache.IsTypeOfAsync(actualType, type, cancellationToken).ConfigureAwait(false))
                {
                    yield return create(session, node, parent.Telemetry);
                }
            }
        }

        /// <summary>
        /// Uses ManagedBrowse so continuation-point lifetime remains owned by the existing client infrastructure.
        /// </summary>
        internal static async ValueTask<ArrayOf<ReferenceDescription>> BrowseAsync(
            ISession session,
            NodeId node,
            uint classes,
            CancellationToken cancellationToken)
        {
            (ArrayOf<ArrayOf<ReferenceDescription>> results, ArrayOf<ServiceResult> errors) =
                await session.ManagedBrowseAsync(null, null, [node], 0, BrowseDirection.Forward,
                    Opc.Ua.ReferenceTypeIds.HierarchicalReferences, true, classes, cancellationToken)
                    .ConfigureAwait(false);
            if (errors.Count != 0 && ServiceResult.IsBad(errors[0]))
            {
                throw new ServiceResultException(errors[0]);
            }
            if (results.Count != 1)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                    "Browse returned an invalid result.");
            }
            return results[0];
        }

        /// <summary>
        /// Requires a real session, rather than using reflection or a service-identity stand-in.
        /// </summary>
        internal static ISession RequireSession(ObjectTypeClient proxy)
        {
            if (proxy == null)
            {
                throw new ArgumentNullException(nameof(proxy));
            }
            return proxy.Session as ISession ??
                throw new ArgumentException("Extrusion workflows require an ISession or ManagedSession.",
                    nameof(proxy));
        }
    }
}
