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
using Opc.Ua.PlasticsRubber;

namespace Opc.Ua.GranulateDrying.Client
{
    /// <summary>
    /// Namespace-safe drying-device discovery and generated typed operations on an existing session.
    /// The caller or ManagedSession factory owns connection lifetime.
    /// </summary>
    public sealed partial class GranulateDryingClient
    {
        /// <summary>
        /// Creates a client over a connected session, including ManagedSession.
        /// </summary>
        public GranulateDryingClient(ISession session, ITelemetryContext telemetry)
        {
            m_context = new GranulateDryingClientContext(session, telemetry);
        }

        /// <summary>
        /// Gets the caller-owned session.
        /// </summary>
        public ISession Session => m_context.Session;

        /// <summary>
        /// Gets whether the server advertises the GDD model namespace.
        /// </summary>
        public bool IsSupported => Session.NamespaceUris.GetIndex(Namespaces.GranulateDrying) >= 0;

        /// <summary>
        /// Opens the full selected interface for one device instance.
        /// </summary>
        public GranulateDryingDeviceClient Device(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException("A device instance NodeId is required.", nameof(nodeId));
            }
            return new GranulateDryingDeviceClient(m_context, nodeId);
        }

        /// <summary>
        /// Opens typed common-parameter values on a generated child proxy obtained from a GDD bin or bed.
        /// This does not introduce a different control protocol.
        /// </summary>
        public GranulateDryingParameterClient Parameter(MonitoredParameterTypeClient proxy)
        {
            if (proxy == null)
            {
                throw new ArgumentNullException(nameof(proxy));
            }
            if (!ReferenceEquals(proxy.Session, Session))
            {
                throw new ArgumentException("The proxy must use this client's session.", nameof(proxy));
            }
            return new GranulateDryingParameterClient(m_context, proxy);
        }

        /// <summary>
        /// Opens all common energy values on a generated child proxy.
        /// </summary>
        public GranulateDryingEnergyClient Energy(EnergyTypeClient proxy)
        {
            if (proxy == null)
            {
                throw new ArgumentNullException(nameof(proxy));
            }
            if (!ReferenceEquals(proxy.Session, Session))
            {
                throw new ArgumentException("The proxy must use this client's session.", nameof(proxy));
            }
            return new GranulateDryingEnergyClient(m_context, proxy);
        }

        /// <summary>
        /// Opens typed common configuration values beside its generated clock and page methods.
        /// </summary>
        public GranulateDryingConfigurationClient Configuration(MachineConfigurationTypeClient proxy)
        {
            if (proxy == null)
            {
                throw new ArgumentNullException(nameof(proxy));
            }
            if (!ReferenceEquals(proxy.Session, Session))
            {
                throw new ArgumentException("The proxy must use this client's session.", nameof(proxy));
            }
            return new GranulateDryingConfigurationClient(m_context, proxy);
        }

        /// <summary>
        /// Discovers standard devices and vendor subtypes under Machines or an explicit collection root.
        /// Cycles are de-duplicated; exceeding the traversal bound is an error, not silent truncation.
        /// </summary>
        public async ValueTask<ArrayOf<GranulateDryingDeviceEntry>> DiscoverDevicesAsync(
            NodeId root = default,
            int maximumNodes = 10000,
            CancellationToken cancellationToken = default)
        {
            if (maximumNodes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumNodes));
            }
            if (!IsSupported)
            {
                return [];
            }
            NodeId type = ExpandedNodeId.ToNodeId(ObjectTypeIds.GranulateDryingDeviceType, Session.NamespaceUris);
            NodeId start = root.IsNull
                ? ExpandedNodeId.ToNodeId(Machinery.ObjectIds.Machines, Session.NamespaceUris) : root;
            if (start.IsNull)
            {
                return [];
            }
            var pending = new Queue<NodeId>();
            var seen = new HashSet<NodeId> { start };
            var devices = new List<GranulateDryingDeviceEntry>();
            pending.Enqueue(start);
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ArrayOf<ReferenceDescription> references = await m_context.BrowseAsync(
                    pending.Dequeue(), cancellationToken).ConfigureAwait(false);
                for (int index = 0; index < references.Count; index++)
                {
                    ReferenceDescription reference = references[index];
                    NodeId node = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                    if (node.IsNull || !seen.Add(node))
                    {
                        continue;
                    }
                    if (seen.Count > maximumNodes)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                    }
                    NodeId actual = ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris);
                    if (!actual.IsNull &&
                        await Session.NodeCache.IsTypeOfAsync(actual, type, cancellationToken).ConfigureAwait(false))
                    {
                        devices.Add(new GranulateDryingDeviceEntry(
                            node, reference.BrowseName, reference.DisplayName, actual));
                    }
                    else
                    {
                        pending.Enqueue(node);
                    }
                }
            }
            return devices.ToArrayOf();
        }

        /// <summary>
        /// The shared typed-session context.
        /// </summary>
        private readonly GranulateDryingClientContext m_context;
    }

    /// <summary>
    /// A discovered real device instance, retaining its actual (possibly derived) ObjectType.
    /// </summary>
    /// <param name="NodeId">The instance identifier.</param>
    /// <param name="BrowseName">The qualified browse name.</param>
    /// <param name="DisplayName">The display name.</param>
    /// <param name="TypeDefinition">The actual ObjectType.</param>
    public sealed record GranulateDryingDeviceEntry(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText DisplayName,
        NodeId TypeDefinition);

    /// <summary>
    /// Shared browse and variable-path operations; standard methods remain in generated proxies.
    /// </summary>
    internal sealed class GranulateDryingClientContext
    {
        /// <summary>
        /// Registers compiled codecs without reflection-based type discovery.
        /// </summary>
        internal GranulateDryingClientContext(ISession session, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            session.MessageContext.Factory.Builder.AddOpcUaPlasticsRubber().AddOpcUaGranulateDrying().Commit();
        }

        /// <summary>
        /// Gets the connected session.
        /// </summary>
        internal ISession Session { get; }

        /// <summary>
        /// Gets diagnostics for generated proxies.
        /// </summary>
        internal ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Creates a typed variable with URI-qualified standard path segments.
        /// </summary>
        /// <typeparam name="T">The declared variable value type.</typeparam>
        internal GranulateDryingVariable<T> Variable<T>(
            NodeId owner,
            GranulateDryingCodec<T> codec,
            bool writable,
            params GranulateDryingPathElement[] path)
        {
            return new GranulateDryingVariable<T>(this, owner, path.ToArrayOf(), codec, writable);
        }

        /// <summary>
        /// Resolves a standard variable or placeholder path, including properties and add-ins.
        /// </summary>
        internal async ValueTask<NodeId> ResolveAsync(
            NodeId owner,
            ArrayOf<GranulateDryingPathElement> path,
            CancellationToken cancellationToken)
        {
            var elements = new List<RelativePathElement>(path.Count);
            foreach (GranulateDryingPathElement part in path)
            {
                int ns = Session.NamespaceUris.GetIndex(part.NamespaceUri);
                if (ns < 0)
                {
                    return NodeId.Null;
                }
                elements.Add(new RelativePathElement
                {
                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    TargetName = new QualifiedName(part.Name, (ushort)ns)
                });
            }
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(null,
                [new BrowsePath { StartingNode = owner,
                    RelativePath = new RelativePath { Elements = elements.ToArrayOf() } }],
                cancellationToken).ConfigureAwait(false);
            if (response.Results.Count != 1)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError);
            }
            BrowsePathResult result = response.Results[0];
            if (result.StatusCode is var code && (code == StatusCodes.BadNoMatch || code == StatusCodes.BadNotFound))
            {
                return NodeId.Null;
            }
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode);
            }
            if (result.Targets.Count != 1 || result.Targets[0].RemainingPathIndex != uint.MaxValue)
            {
                throw new ServiceResultException(StatusCodes.BadBrowseNameInvalid);
            }
            return ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, Session.NamespaceUris);
        }

        /// <summary>
        /// Browses all object references using the stack's continuation-point-aware implementation.
        /// </summary>
        internal async ValueTask<ArrayOf<ReferenceDescription>> BrowseAsync(
            NodeId node,
            CancellationToken cancellationToken)
        {
            (ArrayOf<ArrayOf<ReferenceDescription>> results, ArrayOf<ServiceResult> errors) =
                await Session.ManagedBrowseAsync(null, null, [node], 0, BrowseDirection.Forward,
                    Opc.Ua.ReferenceTypeIds.HierarchicalReferences, true, (uint)NodeClass.Object, cancellationToken)
                    .ConfigureAwait(false);
            if (errors.Count != 1 || results.Count != 1)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError);
            }
            if (ServiceResult.IsBad(errors[0]))
            {
                throw new ServiceResultException(errors[0]);
            }
            return results[0];
        }

        /// <summary>
        /// Creates a GDD-qualified path segment.
        /// </summary>
        internal static GranulateDryingPathElement Gdd(string name) => new(Namespaces.GranulateDrying, name);

        /// <summary>
        /// Creates a common-model-qualified path segment.
        /// </summary>
        internal static GranulateDryingPathElement Common(string name) =>
            new(PlasticsRubber.Namespaces.PlasticsRubber, name);
    }
}
