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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// Client for OPC 30500-1 Laboratory and Analytical Device Standard (LADS)
    /// servers: discovers LADS devices, reads snapshots of devices, functional
    /// units, functions, programs, results and maintenance tasks, observes
    /// function values, drives the LADS state machines and downloads result files.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Devices are found below the Device Integration <c>DeviceSet</c> and the
    /// Machinery <c>Machines</c> folder; a device both folders reference is
    /// returned once. Every type check is subtype aware, so vendor subtypes of
    /// the LADS device, functional unit and function types are recognized.
    /// </para>
    /// <para>
    /// Methods are invoked through the generated ObjectType proxies of the
    /// LADS model. Observing values needs an <see cref="IStreamingSubscription"/>;
    /// when none is passed, the default one of a <see cref="ManagedSession"/> is used.
    /// </para>
    /// <para>
    /// Use <see cref="GetDeviceAsync(string, CancellationToken)"/> to get a
    /// <see cref="LadsDevice"/> handle for a fluent flow:
    /// </para>
    /// <code>
    /// LadsDevice device = await client.GetDeviceAsync("Reader", ct);
    /// LadsFunctionalUnit unit = await device.GetFunctionalUnitAsync("Unit1", ct);
    /// string runId = await unit.StartProgramAsync(LadsProgramRequest.For("Measure"), ct);
    /// </code>
    /// </remarks>
    public sealed partial class LadsClient
    {
        /// <summary>
        /// Creates the client with the default options.
        /// </summary>
        /// <param name="session">The session, borrowed and never disposed by the client.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        public LadsClient(ISession session, ITelemetryContext telemetry)
            : this(session, telemetry, null)
        {
        }

        /// <summary>
        /// Creates the client.
        /// </summary>
        /// <param name="session">The session, borrowed and never disposed by the client.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="options">The options; <see cref="LadsClientOptions.Default"/> when <c>null</c>.</param>
        /// <exception cref="ArgumentNullException">The session or telemetry is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">The options or the session are invalid.</exception>
        public LadsClient(ISession session, ITelemetryContext telemetry, LadsClientOptions? options)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            Options = options ?? LadsClientOptions.Default;
            Options.Validate();
            IServiceMessageContext messageContext = session.MessageContext ??
                throw new ArgumentException("The session must provide a message context.", nameof(session));
            // Two clients created at once on one session must not both register the types.
            lock (s_registrationLock)
            {
                if (!messageContext.Factory.ContainsEncodeableType(DataTypeIds.KeyValueType))
                {
                    messageContext.Factory.Builder.AddLadsTypeSystem().Commit();
                }
            }
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
        /// Gets the options.
        /// </summary>
        public LadsClientOptions Options { get; }

        /// <summary>
        /// Gets whether the server publishes the LADS namespace.
        /// </summary>
        public bool IsSupported => Session.NamespaceUris.GetIndex(Namespaces.LADS) >= 0;

        /// <summary>
        /// Gets the index of a namespace in the session's table.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotSupported"/> when the server does not publish it.
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
        /// Gets a browse name in the LADS namespace.
        /// </summary>
        internal QualifiedName Lads(string name)
        {
            return new QualifiedName(name, NamespaceIndexOf(Namespaces.LADS));
        }

        /// <summary>
        /// Gets a browse name in the Device Integration namespace.
        /// </summary>
        internal QualifiedName Di(string name)
        {
            return new QualifiedName(name, NamespaceIndexOf(Opc.Ua.Di.Namespaces.OpcUaDi));
        }

        /// <summary>
        /// Gets a browse name in the Asset Management Basics namespace.
        /// </summary>
        internal QualifiedName Amb(string name)
        {
            return new QualifiedName(name, NamespaceIndexOf(Opc.Ua.AMB.Namespaces.AMB));
        }

        /// <summary>
        /// Gets a browse name in the Machinery namespace.
        /// </summary>
        internal QualifiedName Machinery(string name)
        {
            return new QualifiedName(name, NamespaceIndexOf(Opc.Ua.Machinery.Namespaces.Machinery));
        }

        /// <summary>
        /// Gets a browse name in a namespace, or <c>null</c> when the server does not publish it.
        /// </summary>
        internal QualifiedName? TryName(string namespaceUri, string name)
        {
            int index = Session.NamespaceUris.GetIndex(namespaceUri);
            return index < 0 ? null : new QualifiedName(name, (ushort)index);
        }

        /// <summary>
        /// Converts an identifier to a NodeId of the session.
        /// </summary>
        internal NodeId ToNodeId(ExpandedNodeId nodeId)
        {
            return ExpandedNodeId.ToNodeId(nodeId, Session.NamespaceUris);
        }

        /// <summary>
        /// Browses the forward hierarchical references of a node with continuation
        /// points, within the limits of the options. A node the server does not
        /// know yields no references.
        /// </summary>
        internal ValueTask<List<ReferenceDescription>> BrowseAsync(
            NodeId node,
            NodeClass nodeClassMask,
            CancellationToken cancellationToken)
        {
            return BrowseAsync(node, Ua.ReferenceTypeIds.HierarchicalReferences, nodeClassMask, cancellationToken);
        }

        /// <summary>
        /// Browses the forward references of a reference type (and its subtypes)
        /// of a node with continuation points, within the limits of the options.
        /// A node the server does not know yields no references.
        /// </summary>
        internal async ValueTask<List<ReferenceDescription>> BrowseAsync(
            NodeId node,
            NodeId referenceTypeId,
            NodeClass nodeClassMask,
            CancellationToken cancellationToken)
        {
            var references = new List<ReferenceDescription>();
            if (node.IsNull)
            {
                return references;
            }
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                (uint)Math.Min(Options.MaximumBrowseReferences, 1000),
                [
                    new BrowseDescription
                    {
                        NodeId = node,
                        ReferenceTypeId = referenceTypeId,
                        BrowseDirection = BrowseDirection.Forward,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)nodeClassMask,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ],
                cancellationToken).ConfigureAwait(false);
            ValidateResponse(response.ResponseHeader, response.Results.Count, 1);
            BrowseResult result = response.Results[0];
            ByteString owned = default;
            try
            {
                for (int page = 1; ; page++)
                {
                    if (result is null)
                    {
                        throw new ServiceResultException(StatusCodes.BadUnexpectedError, "A browse result is missing.");
                    }
                    owned = result.ContinuationPoint;
                    if (StatusCode.IsBad(result.StatusCode))
                    {
                        if (result.StatusCode == StatusCodes.BadNodeIdUnknown)
                        {
                            return references;
                        }
                        throw new ServiceResultException(result.StatusCode);
                    }
                    foreach (ReferenceDescription reference in result.References)
                    {
                        if (reference is not null)
                        {
                            references.Add(reference);
                        }
                    }
                    if (references.Count > Options.MaximumBrowseReferences)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTooManyOperations, "The LADS browse reference limit was exceeded.");
                    }
                    if (!HasContinuation(owned))
                    {
                        return references;
                    }
                    if (page >= Options.MaximumBrowsePages)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTooManyOperations, "The LADS browse page limit was exceeded.");
                    }
                    BrowseNextResponse next = await Session.BrowseNextAsync(
                        null, false, [owned], cancellationToken).ConfigureAwait(false);
                    owned = default;
                    ValidateResponse(next.ResponseHeader, next.Results.Count, 1);
                    result = next.Results[0];
                }
            }
            catch (Exception) when (HasContinuation(owned))
            {
                await ReleaseAsync(owned).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Resolves relative paths along hierarchical references.
        /// </summary>
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
            ValidateResponse(response.ResponseHeader, response.Results.Count, results.Length);
            for (int ii = 0; ii < results.Length; ii++)
            {
                BrowsePathResult result = response.Results[ii];
                results[ii] = result is not null && StatusCode.IsGood(result.StatusCode) && result.Targets.Count > 0
                    ? ToNodeId(result.Targets[0].TargetId)
                    : NodeId.Null;
            }
            return results;
        }

        /// <summary>
        /// Resolves one relative path along hierarchical references.
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
        /// Reads the values of several nodes; a null NodeId yields a
        /// <see cref="StatusCodes.BadNodeIdUnknown"/> value without a request.
        /// </summary>
        internal ValueTask<DataValue[]> ReadValuesAsync(
            IReadOnlyList<NodeId> nodes,
            CancellationToken cancellationToken)
        {
            return ReadAttributesAsync(nodes, Attributes.Value, cancellationToken);
        }

        /// <summary>
        /// Reads an attribute of several nodes; a null NodeId yields a
        /// <see cref="StatusCodes.BadNodeIdUnknown"/> value without a request.
        /// </summary>
        internal async ValueTask<DataValue[]> ReadAttributesAsync(
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
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                requests.ToArrayOf(),
                cancellationToken).ConfigureAwait(false);
            ValidateResponse(response.ResponseHeader, response.Results.Count, requests.Count);
            for (int ii = 0; ii < positions.Count; ii++)
            {
                values[positions[ii]] = response.Results[ii];
            }
            return values;
        }

        /// <summary>
        /// Resolves relative paths and reads the values of the targets.
        /// </summary>
        /// <returns>The values; a bad status where a path does not resolve.</returns>
        internal async ValueTask<DataValue[]> ReadPathsAsync(
            NodeId start,
            IReadOnlyList<QualifiedName[]> paths,
            CancellationToken cancellationToken)
        {
            NodeId[] nodes = await ResolveAsync(start, paths, cancellationToken).ConfigureAwait(false);
            return await ReadValuesAsync(nodes, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Tests whether a type is the expected type or one of its subtypes.
        /// </summary>
        internal async ValueTask<bool> IsTypeOfAsync(
            ExpandedNodeId typeDefinition,
            ExpandedNodeId expected,
            CancellationToken cancellationToken)
        {
            NodeId type = ToNodeId(typeDefinition);
            NodeId super = ToNodeId(expected);
            if (type.IsNull || super.IsNull)
            {
                return false;
            }
            if (type == super)
            {
                return true;
            }
            var key = new TypeKey(type, super);
            if (m_typeCache.TryGetValue(key, out bool known))
            {
                return known;
            }
            bool result = await Session.NodeCache.IsTypeOfAsync(type, super, cancellationToken)
                .ConfigureAwait(false);
            m_typeCache[key] = result;
            return result;
        }

        /// <summary>
        /// Classifies a function by its type definition, returning the most
        /// specific standard LADS function type.
        /// </summary>
        internal async ValueTask<LadsFunctionKind> ClassifyFunctionAsync(
            ExpandedNodeId typeDefinition,
            CancellationToken cancellationToken)
        {
            NodeId type = ToNodeId(typeDefinition);
            foreach (LadsFunctionKind kind in LadsFunctionKinds.MostSpecificFirst)
            {
                if (type == ToNodeId(LadsFunctionKinds.TypeIdOf(kind)))
                {
                    return kind;
                }
            }
            ArrayOf<LadsFunctionKind> kinds = LadsFunctionKinds.MostSpecificFirst;
            for (int ii = 0; ii < kinds.Count; ii++)
            {
                LadsFunctionKind kind = kinds[ii];
                if (await IsTypeOfAsync(typeDefinition, LadsFunctionKinds.TypeIdOf(kind), cancellationToken)
                    .ConfigureAwait(false))
                {
                    return kind;
                }
            }
            return LadsFunctionKind.Unknown;
        }

        /// <summary>
        /// Gets the streaming subscription to observe values with.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidState"/> when none is passed and
        /// the session is not a <see cref="ManagedSession"/>.
        /// </exception>
        internal IStreamingSubscription StreamingOf(IStreamingSubscription? streaming)
        {
            if (streaming is not null)
            {
                return streaming;
            }
            if (Session is ManagedSession managed)
            {
                return managed.DefaultStreaming;
            }
            throw ServiceResultException.Create(
                StatusCodes.BadInvalidState,
                "Observing values without an IStreamingSubscription requires a ManagedSession.");
        }

        /// <summary>
        /// Gets a text value from a String or LocalizedText, or <c>null</c>
        /// when the value is bad, absent or empty.
        /// </summary>
        internal static string? AsString(DataValue value)
        {
            if (StatusCode.IsBad(value.StatusCode))
            {
                return null;
            }
            if (value.WrappedValue.TryGetValue(out string? text) && !string.IsNullOrEmpty(text))
            {
                return text;
            }
            if (value.WrappedValue.TryGetValue(out LocalizedText localized) &&
                !localized.IsNull &&
                !string.IsNullOrEmpty(localized.Text))
            {
                return localized.Text;
            }
            return null;
        }

        /// <summary>
        /// Gets a duration in milliseconds as a time span.
        /// </summary>
        internal static TimeSpan? AsDuration(DataValue value)
        {
            if (StatusCode.IsBad(value.StatusCode) ||
                !value.WrappedValue.TryGetValue(out double milliseconds) ||
                double.IsNaN(milliseconds) ||
                double.IsInfinity(milliseconds) ||
                Math.Abs(milliseconds) > TimeSpan.MaxValue.TotalMilliseconds)
            {
                return null;
            }
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        /// <summary>
        /// Gets an unsigned 32-bit value.
        /// </summary>
        internal static uint? AsUInt32(DataValue value)
        {
            return StatusCode.IsGood(value.StatusCode) && value.WrappedValue.TryGetValue(out uint result)
                ? result
                : null;
        }

        /// <summary>
        /// Gets an unsigned 64-bit value, widening a 32-bit one.
        /// </summary>
        internal static ulong? AsUInt64(DataValue value)
        {
            if (StatusCode.IsBad(value.StatusCode))
            {
                return null;
            }
            if (value.WrappedValue.TryGetValue(out ulong result))
            {
                return result;
            }
            return value.WrappedValue.TryGetValue(out uint narrow) ? narrow : null;
        }

        /// <summary>
        /// Gets a date, or a null date.
        /// </summary>
        internal static DateTimeUtc AsDate(DataValue value)
        {
            return StatusCode.IsGood(value.StatusCode) && value.WrappedValue.TryGetValue(out DateTimeUtc result)
                ? result
                : default;
        }

        /// <summary>
        /// Gets a NodeId, or a null NodeId.
        /// </summary>
        internal static NodeId AsNodeId(DataValue value)
        {
            return StatusCode.IsGood(value.StatusCode) && value.WrappedValue.TryGetValue(out NodeId result)
                ? result
                : NodeId.Null;
        }

        /// <summary>
        /// Gets the value of a good data value, or a null variant.
        /// </summary>
        internal static Variant AsVariant(DataValue value)
        {
            return StatusCode.IsBad(value.StatusCode) ? Variant.Null : value.WrappedValue;
        }

        /// <summary>
        /// Gets the text of a localized text, or the name.
        /// </summary>
        internal static string DisplayNameOf(ReferenceDescription reference)
        {
            if (!reference.DisplayName.IsNull && !string.IsNullOrEmpty(reference.DisplayName.Text))
            {
                return reference.DisplayName.Text;
            }
            return reference.BrowseName.Name ?? string.Empty;
        }

        /// <summary>
        /// Validates the header and the result count of a response.
        /// </summary>
        /// <exception cref="ServiceResultException">The response is bad or malformed.</exception>
        internal static void ValidateResponse(ResponseHeader? header, int count, int expected)
        {
            if (header is not null && StatusCode.IsBad(header.ServiceResult))
            {
                throw new ServiceResultException(header.ServiceResult);
            }
            if (count != expected)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "The server returned {0} results for {1} operations.",
                    count,
                    expected);
            }
        }

        private static bool HasContinuation(ByteString token)
        {
            return !token.IsNull && !token.IsEmpty;
        }

        private async ValueTask ReleaseAsync(ByteString continuationPoint)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await Session.BrowseNextAsync(null, true, [continuationPoint], timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best effort: the server releases continuation points with the session.
            }
        }

        private readonly record struct TypeKey(NodeId Type, NodeId Super);

        private static readonly Lock s_registrationLock = new();
        private readonly ConcurrentDictionary<TypeKey, bool> m_typeCache = new();
    }
}
