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
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Opc.Ua.AMB.Client
{
    /// <summary>
    /// Client for the OPC 10000-110 Asset Management Basics of a server:
    /// discovers the manageable assets, reads what they publish about their
    /// identification, health, maintenance, documentation, location and
    /// structure, and follows their alarms and maintenance activities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client reads what a server publishes rather than assuming a
    /// layout: identification is looked up on the asset and in its
    /// <c>2:Identification</c> group, conditions are found in the
    /// <c>2:DeviceHealthAlarms</c> folder, and the fields of the AMB
    /// interfaces are selected through the Device Integration alarm types,
    /// because a server filters events on type definitions, which interfaces
    /// are not.
    /// </para>
    /// <para>
    /// Observing events needs an <see cref="IStreamingSubscription"/>; when
    /// none is passed, the default one of a <see cref="ManagedSession"/> is
    /// used.
    /// </para>
    /// </remarks>
    public sealed partial class AmbClient
    {
        /// <summary>
        /// Creates the client.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        public AmbClient(ISession session, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            AmbEncodeableRegistration.Register(session);
        }

        /// <summary>
        /// Gets the session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets whether the server publishes the AMB namespace.
        /// </summary>
        public bool IsSupported => Session.NamespaceUris.GetIndex(Namespaces.AMB) >= 0;

        /// <summary>
        /// Gets the index of a namespace in the session's table.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotSupported"/> when the server does not
        /// publish it.
        /// </exception>
        internal ushort NamespaceIndexOf(string namespaceUri)
        {
            int index = Session.NamespaceUris.GetIndex(namespaceUri);
            if (index < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The server does not publish the namespace '{0}'.",
                    namespaceUri);
            }
            return (ushort)index;
        }

        /// <summary>
        /// Resolves an identifier of a model against the session.
        /// </summary>
        internal NodeId ToNodeId(ExpandedNodeId nodeId)
        {
            return ExpandedNodeId.ToNodeId(nodeId, Session.NamespaceUris);
        }

        /// <summary>
        /// Gets a browse name in the AMB namespace.
        /// </summary>
        internal QualifiedName Amb(string name)
        {
            return new QualifiedName(name, NamespaceIndexOf(Namespaces.AMB));
        }

        /// <summary>
        /// Gets a browse name in the Device Integration namespace.
        /// </summary>
        internal QualifiedName Di(string name)
        {
            return new QualifiedName(name, NamespaceIndexOf(Opc.Ua.Di.Namespaces.OpcUaDi));
        }

        /// <summary>
        /// Resolves relative paths along hierarchical references.
        /// </summary>
        /// <param name="start">The node the paths start at.</param>
        /// <param name="paths">The browse names of each path.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The targets; <see cref="NodeId.Null"/> where a path does not resolve.</returns>
        internal async ValueTask<NodeId[]> ResolveAsync(
            NodeId start,
            IReadOnlyList<QualifiedName[]> paths,
            CancellationToken cancellationToken)
        {
            var results = new NodeId[paths.Count];
            if (start.IsNull || paths.Count == 0)
            {
                return results;
            }

            var browsePaths = new BrowsePath[paths.Count];
            for (int ii = 0; ii < paths.Count; ii++)
            {
                var elements = new RelativePathElement[paths[ii].Length];
                for (int jj = 0; jj < elements.Length; jj++)
                {
                    elements[jj] = new RelativePathElement
                    {
                        ReferenceTypeId = Ua.ReferenceTypeIds.HierarchicalReferences,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = paths[ii][jj]
                    };
                }
                browsePaths[ii] = new BrowsePath
                {
                    StartingNode = start,
                    RelativePath = new RelativePath { Elements = elements.ToArrayOf() }
                };
            }

            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(null, browsePaths.ToArrayOf(), cancellationToken)
                .ConfigureAwait(false);
            for (int ii = 0; ii < response.Results.Count && ii < results.Length; ii++)
            {
                BrowsePathResult result = response.Results[ii];
                results[ii] = StatusCode.IsGood(result.StatusCode) && result.Targets.Count > 0
                    ? ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, Session.NamespaceUris)
                    : NodeId.Null;
            }
            return results;
        }

        /// <summary>
        /// Resolves one child along hierarchical references.
        /// </summary>
        internal async ValueTask<NodeId> ResolveAsync(
            NodeId start,
            CancellationToken cancellationToken,
            params QualifiedName[] path)
        {
            NodeId[] resolved = await ResolveAsync(start, [path], cancellationToken).ConfigureAwait(false);
            return resolved[0];
        }

        /// <summary>
        /// Reads an attribute of several nodes; a null NodeId yields a
        /// <see cref="StatusCodes.BadNodeIdUnknown"/> value without a request.
        /// </summary>
        internal async ValueTask<DataValue[]> ReadAsync(
            IReadOnlyList<NodeId> nodes,
            uint attributeId,
            CancellationToken cancellationToken)
        {
            var values = new DataValue[nodes.Count];
            var requests = new List<ReadValueId>();
            var positions = new List<int>();
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                if (nodes[ii].IsNull)
                {
                    values[ii] = DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown);
                    continue;
                }
                requests.Add(new ReadValueId { NodeId = nodes[ii], AttributeId = attributeId });
                positions.Add(ii);
            }
            if (requests.Count == 0)
            {
                return values;
            }

            ReadResponse response = await Session
                .ReadAsync(null, 0, TimestampsToReturn.Source, requests.ToArrayOf(), cancellationToken)
                .ConfigureAwait(false);
            for (int ii = 0; ii < positions.Count; ii++)
            {
                values[positions[ii]] = ii < response.Results.Count
                    ? response.Results[ii]
                    : DataValue.FromStatusCode(StatusCodes.BadUnexpectedError);
            }
            return values;
        }

        /// <summary>
        /// Reads the values of children of a node, by path.
        /// </summary>
        internal async ValueTask<Variant[]> ReadChildValuesAsync(
            NodeId parent,
            IReadOnlyList<QualifiedName[]> paths,
            CancellationToken cancellationToken)
        {
            NodeId[] nodes = await ResolveAsync(parent, paths, cancellationToken).ConfigureAwait(false);
            DataValue[] values = await ReadAsync(nodes, Attributes.Value, cancellationToken).ConfigureAwait(false);
            var result = new Variant[values.Length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                result[ii] = StatusCode.IsGood(values[ii].StatusCode) ? values[ii].WrappedValue : Variant.Null;
            }
            return result;
        }

        /// <summary>
        /// Browses the references of a node, following continuation points.
        /// </summary>
        internal async ValueTask<List<ReferenceDescription>> BrowseAsync(
            NodeId node,
            NodeId referenceTypeId,
            BrowseDirection direction,
            CancellationToken cancellationToken,
            NodeClass nodeClassMask = NodeClass.Unspecified)
        {
            var references = new List<ReferenceDescription>();
            if (node.IsNull)
            {
                return references;
            }

            var browser = new Browser(Session, new BrowserOptions
            {
                BrowseDirection = direction,
                ReferenceTypeId = referenceTypeId,
                IncludeSubtypes = true,
                NodeClassMask = (int)nodeClassMask,
                ResultMask = (uint)BrowseResultMask.All
            });
            ResultSet<ArrayOf<ReferenceDescription>> result = await browser
                .BrowseAsync(new[] { node }.ToArrayOf(), cancellationToken)
                .ConfigureAwait(false);
            if (result.Results.Count == 0 ||
                (result.Errors.Count > 0 && StatusCode.IsBad(result.Errors[0].StatusCode)))
            {
                return references;
            }
            foreach (ReferenceDescription reference in result.Results[0])
            {
                references.Add(reference);
            }
            return references;
        }

        /// <summary>
        /// Gets the streaming subscription to observe events with.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidState"/> when none is passed and
        /// the session is not a <see cref="ManagedSession"/>.
        /// </exception>
        internal IStreamingSubscription StreamingOf(IStreamingSubscription? streaming)
        {
            if (streaming != null)
            {
                return streaming;
            }
            if (Session is ManagedSession managed)
            {
                return managed.DefaultStreaming;
            }
            throw ServiceResultException.Create(
                StatusCodes.BadInvalidState,
                "Observing events without an IStreamingSubscription requires a ManagedSession.");
        }

        /// <summary>
        /// Gets a string value, treating an empty one as not set.
        /// </summary>
        internal static string? StringOf(Variant value)
        {
            return value.TryGetValue(out string? text) && !string.IsNullOrEmpty(text) ? text : null;
        }

        /// <summary>
        /// Gets a localized text, treating an empty one as not set.
        /// </summary>
        internal static LocalizedText TextOf(Variant value)
        {
            return value.TryGetValue(out LocalizedText text) && !text.IsNullOrEmpty ? text : default;
        }
    }
}
