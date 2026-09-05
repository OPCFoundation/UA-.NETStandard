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
using Opc.Ua.XRegistry;

namespace Opc.Ua.WotCon.Client
{
    /// <summary>
    /// Wrapper around a single registry group object (a
    /// <c>ThingDescriptionGroupType</c> or <c>ThingModelGroupType</c>
    /// instance below the <c>WoTRegistry</c> object). Composes the
    /// generated <see cref="GroupTypeClient"/> proxy shared by
    /// both group subtypes.
    /// </summary>
    public sealed class WotRegistryGroupClient
    {
        internal WotRegistryGroupClient(
            ISession session,
            NodeId groupNodeId,
            string groupId,
            WoTDocumentKindEnum kind,
            GroupTypeClient proxy,
            ITelemetryContext telemetry,
            bool usesDistinctHierarchy = false)
        {
            Session = session;
            GroupNodeId = groupNodeId;
            GroupId = groupId;
            Kind = kind;
            Proxy = proxy;
            Telemetry = telemetry;
            m_usesDistinctHierarchy = usesDistinctHierarchy;
        }

        /// <summary>
        /// The OPC UA session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Group object NodeId.
        /// </summary>
        public NodeId GroupNodeId { get; }

        /// <summary>
        /// Group id (BrowseName minus namespace prefix).
        /// </summary>
        public string GroupId { get; }

        /// <summary>
        /// Whether this group holds Thing Description or Thing Model
        /// resources.
        /// </summary>
        public WoTDocumentKindEnum Kind { get; }

        /// <summary>
        /// The underlying generated proxy, shared by
        /// <c>ThingDescriptionGroupType</c> and <c>ThingModelGroupType</c>.
        /// </summary>
        public GroupTypeClient Proxy { get; }

        /// <summary>
        /// Telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Calls <c>CreateResource</c> and returns a wrapper around the
        /// newly created resource plus the server-assigned version id.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="resourceId"/> is null or empty.</exception>
        public async ValueTask<(WotRegistryResourceClient Resource, string VersionId)> CreateResourceAsync(
            string resourceId,
            string versionId = "",
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                throw new ArgumentException("Resource id is required.", nameof(resourceId));
            }
            (NodeId resourceNodeId, string versionIdOut, _) = await Proxy
                .CreateResourceAsync(resourceId, versionId ?? string.Empty, requestFileOpen: false, ct)
                .ConfigureAwait(false);
            return (
                OpenResourceClient(
                    resourceNodeId,
                    resourceId,
                    versionIdOut,
                    pendingStructuralVersion: true),
                versionIdOut);
        }

        /// <summary>
        /// Calls <c>GetOrCreateResource</c> and returns a wrapper around
        /// the resolved resource, the server-assigned version id and
        /// whether it was newly created.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="resourceId"/> is null or empty.</exception>
        public async ValueTask<(WotRegistryResourceClient Resource, string VersionId, bool Created)>
            GetOrCreateResourceAsync(
            string resourceId,
            string versionId = "",
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                throw new ArgumentException("Resource id is required.", nameof(resourceId));
            }
            (NodeId resourceNodeId, string versionIdOut, _, bool created) = await Proxy
                .GetOrCreateResourceAsync(resourceId, versionId ?? string.Empty, requestFileOpen: false, ct)
                .ConfigureAwait(false);
            WotRegistryResourceClient resource = OpenResourceClient(
                resourceNodeId,
                resourceId,
                versionIdOut,
                pendingStructuralVersion: created);
            // This read only selects the conditional-fill path. The server
            // rechecks content state atomically before returning a write handle.
            if (!created &&
                await resource.HasContentAsync(ct).ConfigureAwait(false) == false)
            {
                resource.MarkPendingStructuralVersion();
            }
            return (resource, versionIdOut, created);
        }

        /// <summary>
        /// Resolves an existing logical Resource by id to its current default
        /// Version node. Deleting the returned client deletes the logical
        /// Resource and all of its Versions.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="resourceId"/> is null or empty.</exception>
        /// <exception cref="ServiceResultException">The resource was not found in this group.</exception>
        public async ValueTask<WotRegistryResourceClient> OpenResourceAsync(
            string resourceId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                throw new ArgumentException("Resource id is required.", nameof(resourceId));
            }
            ushort ns = Session.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
            NodeId resourceNodeId = await WotConBrowsePathResolver.ResolveLogicalResourceAsync(
                Session,
                GroupNodeId,
                ns,
                resourceId,
                m_usesDistinctHierarchy,
                StatusCodes.BadNoMatch,
                $"Resource '{resourceId}' not found in group '{GroupId}'.",
                ct).ConfigureAwait(false);
            return OpenResourceClient(
                resourceNodeId,
                resourceId,
                versionId: string.Empty,
                pendingStructuralVersion: false);
        }

        /// <summary>
        /// Calls <c>Delete</c> on the group.
        /// </summary>
        public ValueTask DeleteAsync(uint expectedEpoch, CancellationToken ct = default)
        {
            return Proxy.DeleteAsync(expectedEpoch, ct);
        }

        private WotRegistryResourceClient OpenResourceClient(
            NodeId resourceNodeId,
            string resourceId,
            string versionId,
            bool pendingStructuralVersion)
        {
            WoTDocumentTypeClient proxy = Kind == WoTDocumentKindEnum.ThingModel
                ? new ThingModelFileTypeClient(Session, resourceNodeId, Telemetry)
                : new ThingDescriptionFileTypeClient(Session, resourceNodeId, Telemetry);
            return new WotRegistryResourceClient(
                Session,
                resourceNodeId,
                GroupId,
                resourceId,
                versionId,
                Kind,
                Proxy,
                proxy,
                pendingStructuralVersion,
                Telemetry);
        }

        private readonly bool m_usesDistinctHierarchy;
    }
}
