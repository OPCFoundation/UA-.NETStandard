/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.WotCon.Client
{
    /// <summary>
    /// High-level client for the OPC 10100-1 WoT Connectivity
    /// management surface. Composes (does <strong>not</strong> inherit)
    /// the source-generated <see cref="WoTAssetConnectionManagementTypeClient"/>
    /// proxy so callers can rely on the typed method wrappers while
    /// getting a few ergonomic conveniences (e.g. asset enumeration via
    /// Browse, opening an asset by NodeId).
    /// </summary>
    public sealed class WotConnectivityClient
    {
        /// <summary>
        /// Creates a new client rooted at <paramref name="managementObjectId"/>.
        /// </summary>
        /// <param name="session">An open OPC UA session.</param>
        /// <param name="managementObjectId">NodeId of the
        /// <c>WoTAssetConnectionManagement</c> object on the server.</param>
        /// <param name="telemetry">Telemetry context used for diagnostics.</param>
        public WotConnectivityClient(
            ISession session,
            NodeId managementObjectId,
            ITelemetryContext telemetry)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (managementObjectId.IsNull)
            {
                throw new ArgumentException("Management object NodeId is required.", nameof(managementObjectId));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            Session = session;
            Telemetry = telemetry;
            ManagementObjectId = managementObjectId;
            Proxy = new WoTAssetConnectionManagementTypeClient(session, managementObjectId, telemetry);
        }

        /// <summary>
        /// Creates a client rooted at the WoT Connectivity entry point
        /// (Spec §6.2 — the <c>WoTAssetConnectionManagement</c> object
        /// under <c>Objects</c>) of the connected server.
        /// </summary>
        /// <remarks>
        /// The NodeId of the entry point is server-specific; this
        /// helper resolves the standard <c>BrowseName</c>
        /// (<c>WoTAssetConnectionManagement</c>) by translating from
        /// the <c>Objects</c> folder.
        /// </remarks>
        public static async ValueTask<WotConnectivityClient> ForServerAsync(
            ISession session,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            ushort ns = session.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
            BrowsePath path = new()
            {
                StartingNode = Ua.ObjectIds.ObjectsFolder,
                RelativePath = new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            ReferenceTypeId = Ua.ReferenceTypeIds.Organizes,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName("WoTAssetConnectionManagement", ns)
                        }
                    ]
                }
            };
            ArrayOf<BrowsePath> paths = new[] { path }.ToArrayOf();
            TranslateBrowsePathsToNodeIdsResponse response = await session
                .TranslateBrowsePathsToNodeIdsAsync(null, paths, ct)
                .ConfigureAwait(false);
            if (response.Results.Count == 0 ||
                response.Results[0].Targets.Count == 0 ||
                StatusCode.IsBad(response.Results[0].StatusCode))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown,
                    "WoTAssetConnectionManagement entry point not found on the connected server.");
            }
            var managementId = ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId,
                session.NamespaceUris);
            return new WotConnectivityClient(session, managementId, telemetry);
        }

        /// <summary>The OPC UA session.</summary>
        public ISession Session { get; }

        /// <summary>Telemetry context.</summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>The management object NodeId.</summary>
        public NodeId ManagementObjectId { get; }

        /// <summary>The underlying generated proxy.</summary>
        public WoTAssetConnectionManagementTypeClient Proxy { get; }

        /// <summary>
        /// Calls <c>CreateAsset</c> and returns a wrapper around the
        /// newly created asset.
        /// </summary>
        public async ValueTask<WotAssetClient> CreateAssetAsync(
            string assetName,
            CancellationToken ct = default)
        {
            NodeId assetId = await Proxy.CreateAssetAsync(assetName, ct).ConfigureAwait(false);
            return await OpenAssetAsync(assetId, ct).ConfigureAwait(false);
        }

        /// <summary>Calls <c>DeleteAsset</c>.</summary>
        public ValueTask DeleteAssetAsync(NodeId assetId, CancellationToken ct = default)
            => Proxy.DeleteAssetAsync(assetId, ct);

        /// <summary>Calls <c>DiscoverAssets</c>.</summary>
        public async ValueTask<IReadOnlyList<string>> DiscoverAssetsAsync(
            CancellationToken ct = default)
        {
            ArrayOf<string> endpoints = await Proxy.DiscoverAssetsAsync(ct).ConfigureAwait(false);
            string[] copy = new string[endpoints.Count];
            for (int i = 0; i < endpoints.Count; i++)
            {
                copy[i] = endpoints[i];
            }
            return copy;
        }

        /// <summary>Calls <c>CreateAssetForEndpoint</c>.</summary>
        public async ValueTask<WotAssetClient> CreateAssetForEndpointAsync(
            string assetName,
            string assetEndpoint,
            CancellationToken ct = default)
        {
            NodeId assetId = await Proxy
                .CreateAssetForEndpointAsync(assetName, assetEndpoint, ct)
                .ConfigureAwait(false);
            return await OpenAssetAsync(assetId, ct).ConfigureAwait(false);
        }

        /// <summary>Calls <c>ConnectionTest</c>.</summary>
        public ValueTask<(bool Success, string Status)> ConnectionTestAsync(
            string assetEndpoint,
            CancellationToken ct = default)
            => Proxy.ConnectionTestAsync(assetEndpoint, ct);

        /// <summary>
        /// Builds a <see cref="WotAssetClient"/> wrapper for an existing
        /// asset NodeId. Browses for the asset's <c>WoTFile</c> child to
        /// populate the file proxy.
        /// </summary>
        public async ValueTask<WotAssetClient> OpenAssetAsync(
            NodeId assetId,
            CancellationToken ct = default)
        {
            if (assetId.IsNull)
            {
                throw new ArgumentException("Asset NodeId is required.", nameof(assetId));
            }

            ushort wotConNs = Session.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
            ArrayOf<BrowsePath> paths = new[]
            {
                new BrowsePath
                {
                    StartingNode = assetId,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("WoTFile", wotConNs)
                            }
                        ]
                    }
                }
            }.ToArrayOf();
            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(null, paths, ct)
                .ConfigureAwait(false);
            BrowsePathResult result = response.Results[0];
            if (StatusCode.IsBad(result.StatusCode) || result.Targets.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNoMatch,
                    "WoTFile child not found below the asset object.");
            }
            var fileId = ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, Session.NamespaceUris);

            string assetName = await ReadDisplayNameAsync(assetId, ct).ConfigureAwait(false);
            WoTAssetFileTypeClient file = new(Session, fileId, Telemetry);
            return new WotAssetClient(Session, assetId, assetName, file, Telemetry);
        }

        /// <summary>
        /// Enumerates all currently registered asset entries below the
        /// management object using <c>Browse</c>.
        /// </summary>
        public async IAsyncEnumerable<WotAssetEntry> EnumerateAssetsAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            (_, _, ArrayOf<ReferenceDescription> references) = await Session.BrowseAsync(
                requestHeader: null,
                view: null,
                ManagementObjectId,
                maxResultsToReturn: 0,
                BrowseDirection.Forward,
                Ua.ReferenceTypeIds.Organizes,
                includeSubtypes: true,
                (uint)NodeClass.Object,
                ct).ConfigureAwait(false);
            var snapshot = new ReferenceDescription[references.Count];
            for (int i = 0; i < references.Count; i++)
            {
                snapshot[i] = references[i];
            }
            foreach (ReferenceDescription reference in snapshot)
            {
                ct.ThrowIfCancellationRequested();
                var targetId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                yield return new WotAssetEntry(targetId, reference.DisplayName.Text ?? string.Empty);
            }
        }

        private async ValueTask<string> ReadDisplayNameAsync(NodeId nodeId, CancellationToken ct)
        {
            ArrayOf<ReadValueId> nodesToRead = new[]
            {
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.DisplayName }
            }.ToArrayOf();
            ReadResponse response = await Session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: nodesToRead,
                ct: ct).ConfigureAwait(false);
            if (response.Results.Count == 0 || StatusCode.IsBad(response.Results[0].StatusCode))
            {
                return string.Empty;
            }
            response.Results[0].WrappedValue.TryGetValue(out LocalizedText displayName);
            return displayName.IsNull ? string.Empty : displayName.Text ?? string.Empty;
        }
    }

    /// <summary>
    /// A single asset entry exposed by
    /// <see cref="WotConnectivityClient.EnumerateAssetsAsync"/>.
    /// </summary>
    /// <param name="AssetId">The asset NodeId.</param>
    /// <param name="Name">The asset display name (matches the
    /// <c>BrowseName</c> minus the namespace prefix).</param>
    public sealed record WotAssetEntry(NodeId AssetId, string Name);
}
