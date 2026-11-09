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
using Opc.Ua.Client;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Resolves effective remote parents without trusting a caller's substitute connection plan.
    /// </summary>
    internal static class McpPubSubRemoteParents
    {
        /// <summary>
        /// Reads the actual connection endpoint/profile and rejects incompatible component plans.
        /// Missing or unreadable model properties never fall back to caller-supplied values.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static async ValueTask<PubSubConnectionDataType> ConnectionAsync(
            ISession session, NodeId target, PubSubConnectionInput plan, bool group, CancellationToken ct)
        {
            NodeId connection = group
                ? await ParentConnectionAsync(session, target, ct).ConfigureAwait(false) : target;
            _ = await McpModelAccess.RequireTypeAsync(
                session, connection, ObjectTypeIds.PubSubConnectionType, ct).ConfigureAwait(false);
            string profile = await TextAsync(
                session, connection, BrowseNames.TransportProfileUri, false, ct).ConfigureAwait(false);
            NodeId address = await McpModelAccess.ChildAsync(
                session, connection, new QualifiedName(BrowseNames.Address), ct).ConfigureAwait(false);
            if (address.IsNull)
            {
                throw Refused();
            }
            string endpoint = await TextAsync(session, address, BrowseNames.Url, false, ct).ConfigureAwait(false);
            string networkInterface = await TextAsync(
                session, address, BrowseNames.NetworkInterface, true, ct).ConfigureAwait(false);
            bool sameEndpoint = plan.Transport == PubSubTransportKind.Ethernet
                ? string.Equals(endpoint, plan.Endpoint, StringComparison.Ordinal)
                : Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? actual) &&
                    actual.Equals(new Uri(plan.Endpoint, UriKind.Absolute));
            if (!sameEndpoint ||
                profile != PubSubInputValidation.Profile(plan) ||
                networkInterface != (plan.NetworkInterface ?? string.Empty))
            {
                throw Refused();
            }
            return new PubSubConnectionDataType
            {
                Enabled = true,
                TransportProfileUri = profile,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = endpoint,
                    NetworkInterface = networkInterface
                })
            };
        }

        /// <summary>
        /// Reads the existing writer group's aggregate/fallback destination before adding another writer.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static async ValueTask<ExtensionObject> WriterTransportAsync(
            ISession session, NodeId group, CancellationToken ct)
        {
            NodeId transport = await McpModelAccess.ChildAsync(
                session, group, new QualifiedName(BrowseNames.TransportSettings), ct).ConfigureAwait(false);
            if (transport.IsNull)
            {
                throw Refused();
            }
            string queue = await TextAsync(session, transport, BrowseNames.QueueName, false, ct).ConfigureAwait(false);
            return new ExtensionObject(new BrokerWriterGroupTransportDataType { QueueName = queue });
        }

        /// <summary>
        /// Requires one unambiguous local connection parent rather than choosing a caller-preferred ancestor.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static async ValueTask<NodeId> ParentConnectionAsync(
            ISession session, NodeId group, CancellationToken ct)
        {
            NodeId parent = NodeId.Null;
            await foreach (ReferenceDescription reference in session.EnumerateReferencesAsync(
                group, ReferenceTypeIds.HasComponent, BrowseDirection.Inverse,
                nodeClassMask: (uint)NodeClass.Object, ct: ct).ConfigureAwait(false))
            {
                if (!parent.IsNull || reference.IsForward || reference.NodeId.ServerIndex != 0)
                {
                    throw Refused();
                }
                parent = McpModelAccess.ModelNode(session, reference.NodeId);
            }
            return parent.IsNull ? throw Refused() : parent;
        }

        /// <summary>
        /// Requires a readable standard string property instead of treating bad quality as an absent constraint.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static async ValueTask<string> TextAsync(
            ISession session, NodeId parent, string name, bool allowEmpty, CancellationToken ct)
        {
            (NodeId node, DataValue value) = await McpModelAccess.PropertyAsync(
                session, parent, new QualifiedName(name), ct).ConfigureAwait(false);
            if (node.IsNull ||
                !StatusCode.IsGood(value.StatusCode) ||
                !value.WrappedValue.TryGetValue(out string? text) ||
                text is null ||
                (!allowEmpty && string.IsNullOrWhiteSpace(text)))
            {
                throw Refused();
            }
            return text;
        }

        /// <summary>
        /// Rejects incomplete or conflicting effective-parent information without exposing endpoint details.
        /// </summary>
        private static ServiceResultException Refused()
        {
            return new ServiceResultException(StatusCodes.BadUserAccessDenied,
                "The actual PubSub parent connection and transport settings must be readable, unambiguous " +
                "and compatible with the component plan before the mutation can be authorized.");
        }
    }
}
