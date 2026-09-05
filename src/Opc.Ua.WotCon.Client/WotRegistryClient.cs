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
using Opc.Ua.Encoders;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Client;

namespace Opc.Ua.WotCon.Client
{
    /// <summary>
    /// High-level client for the WoT Connectivity 1.1 registry surface
    /// (the well-known <c>WoTRegistry</c> object hosted by the connected
    /// server, see the <c>xRegistry</c>/<c>WoTRegistryType</c> model).
    /// <para>
    /// As the WoT registry model subtypes the xRegistry base model, this is a
    /// domain client in the sense of <see cref="XRegistryClient"/>: it inherits
    /// the base registry lifecycle and adds WoT-specific group/resource
    /// resolution, a typed <c>Refresh</c> result and a dependency-ordered
    /// bulk-load workflow. It composes (does <strong>not</strong> inherit) the
    /// source-generated <see cref="WoTRegistryTypeClient"/> proxy, so the typed
    /// proxy is reused directly rather than re-resolved per call.
    /// </para>
    /// </summary>
    public sealed class WotRegistryClient : XRegistryClient
    {
        /// <summary>
        /// The well-known reserved group id that always holds Thing
        /// Description resources.
        /// </summary>
        public const string ThingDescriptionsGroupId = "thingdescriptions";

        /// <summary>
        /// The well-known reserved group id that always holds Thing Model
        /// resources.
        /// </summary>
        public const string ThingModelsGroupId = "thingmodels";

        /// <summary>
        /// Creates a new client rooted at <paramref name="registryObjectId"/>.
        /// </summary>
        /// <param name="session">An open OPC UA session.</param>
        /// <param name="registryObjectId">NodeId of the <c>WoTRegistry</c>
        /// object on the server.</param>
        /// <param name="telemetry">Telemetry context used for diagnostics.</param>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="registryObjectId"/> is empty.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="telemetry"/> is null.</exception>
        public WotRegistryClient(
            ISession session,
            NodeId registryObjectId,
            ITelemetryContext telemetry)
            : base(
                session,
                EnsureRegistryNamespace(session),
                ValidateRegistryObjectId(registryObjectId),
                telemetry)
        {
            Proxy = new WoTRegistryTypeClient(session, registryObjectId, telemetry);
        }

        /// <summary>
        /// Registers the WoT Connectivity namespace on the session so the base class can resolve
        /// its index. A client may legitimately be constructed against a session that has not
        /// fetched the server namespace table yet, so this appends rather than failing — matching
        /// what <see cref="ForServerAsync"/> does.
        /// </summary>
        private static string EnsureRegistryNamespace(ISession session)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            session.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
            return Namespaces.WotCon;
        }

        /// <summary>
        /// Rejects a null registry root. The WoT registry root is server-specific and discovered by
        /// Browse, so unlike the base model there is no well-known identifier to fall back to.
        /// </summary>
        private static NodeId ValidateRegistryObjectId(NodeId registryObjectId)
        {
            if (registryObjectId.IsNull)
            {
                throw new ArgumentException(
                    "Registry object NodeId is required.",
                    nameof(registryObjectId));
            }
            return registryObjectId;
        }

        /// <summary>
        /// Creates a client rooted at the WoT Connectivity 1.1 registry
        /// entry point (the well-known <c>WoTRegistry</c> object, a
        /// <c>HasComponent</c> child of the <c>Server</c> object) of the
        /// connected server.
        /// </summary>
        /// <remarks>
        /// The NodeId of the entry point is server-specific; this helper
        /// resolves the standard <c>BrowseName</c> (<c>WoTRegistry</c>) by
        /// translating from the <c>Server</c> object.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="telemetry"/> is null.</exception>
        /// <exception cref="ServiceResultException">
        /// The WoTRegistry entry point was not found on the connected server.
        /// </exception>
        public static async ValueTask<WotRegistryClient> ForServerAsync(
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
            NodeId registryId = await WotConBrowsePathResolver.ResolveChildAsync(
                session,
                Ua.ObjectIds.Server,
                Ua.ReferenceTypeIds.HasComponent,
                ns,
                "WoTRegistry",
                StatusCodes.BadNodeIdUnknown,
                "WoTRegistry entry point not found on the connected server.",
                ct).ConfigureAwait(false);
            return new WotRegistryClient(session, registryId, telemetry);
        }

        /// <summary>
        /// The underlying generated proxy.
        /// </summary>
        public WoTRegistryTypeClient Proxy { get; }

        /// <summary>
        /// Calls the inherited xRegistry <c>CreateGroup</c> Method and
        /// returns a wrapper around the newly created group. The wire
        /// protocol does not carry a "kind" argument; the created group's
        /// actual kind (Thing Description or Thing Model) is discovered
        /// from its reported <c>TypeDefinition</c> after creation, so this
        /// works against any conformant server regardless of its group
        /// naming convention.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="groupId"/> is null or empty.</exception>
        public async ValueTask<WotRegistryGroupClient> CreateGroupAsync(
            string groupId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                throw new ArgumentException("Group id is required.", nameof(groupId));
            }
            NodeId groupNodeId = await Proxy.CreateGroupAsync(groupId, ct).ConfigureAwait(false);
            bool distinct = await UsesDistinctHierarchyAsync(ct).ConfigureAwait(false);
            return await OpenGroupClientAsync(Session, groupNodeId, groupId, Telemetry, ct, distinct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Calls the inherited xRegistry <c>GetOrCreateGroup</c> Method and
        /// returns a wrapper around the resolved group plus whether it was
        /// newly created.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="groupId"/> is null or empty.</exception>
        public async ValueTask<(WotRegistryGroupClient Group, bool Created)> GetOrCreateGroupAsync(
            string groupId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                throw new ArgumentException("Group id is required.", nameof(groupId));
            }
            (NodeId groupNodeId, bool created) = await Proxy
                .GetOrCreateGroupAsync(groupId, ct).ConfigureAwait(false);
            bool distinct = await UsesDistinctHierarchyAsync(ct).ConfigureAwait(false);
            WotRegistryGroupClient group = await OpenGroupClientAsync(
                Session, groupNodeId, groupId, Telemetry, ct, distinct).ConfigureAwait(false);
            return (group, created);
        }

        /// <summary>
        /// Creates the well-known Thing Description group
        /// (<see cref="ThingDescriptionsGroupId"/>).
        /// </summary>
        public ValueTask<WotRegistryGroupClient> CreateThingDescriptionGroupAsync(
            CancellationToken ct = default)
        {
            return CreateGroupAsync(ThingDescriptionsGroupId, ct);
        }

        /// <summary>
        /// Gets or creates the well-known Thing Description group
        /// (<see cref="ThingDescriptionsGroupId"/>).
        /// </summary>
        public ValueTask<(WotRegistryGroupClient Group, bool Created)> GetOrCreateThingDescriptionGroupAsync(
            CancellationToken ct = default)
        {
            return GetOrCreateGroupAsync(ThingDescriptionsGroupId, ct);
        }

        /// <summary>
        /// Creates the well-known Thing Model group
        /// (<see cref="ThingModelsGroupId"/>).
        /// </summary>
        public ValueTask<WotRegistryGroupClient> CreateThingModelGroupAsync(
            CancellationToken ct = default)
        {
            return CreateGroupAsync(ThingModelsGroupId, ct);
        }

        /// <summary>
        /// Gets or creates the well-known Thing Model group
        /// (<see cref="ThingModelsGroupId"/>).
        /// </summary>
        public ValueTask<(WotRegistryGroupClient Group, bool Created)> GetOrCreateThingModelGroupAsync(
            CancellationToken ct = default)
        {
            return GetOrCreateGroupAsync(ThingModelsGroupId, ct);
        }

        /// <summary>
        /// Resolves an already existing group by id, browsing from the
        /// registry object.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="groupId"/> is null or empty.</exception>
        /// <exception cref="ServiceResultException">The group was not found below the registry object.</exception>
        public async ValueTask<WotRegistryGroupClient> OpenGroupAsync(
            string groupId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                throw new ArgumentException("Group id is required.", nameof(groupId));
            }
            ushort ns = Session.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
            NodeId groupNodeId = await WotConBrowsePathResolver.ResolveChildAsync(
                Session,
                RegistryNodeId,
                Ua.ReferenceTypeIds.Organizes,
                ns,
                groupId,
                StatusCodes.BadNoMatch,
                $"Group '{groupId}' not found in the registry.",
                ct).ConfigureAwait(false);
            return await OpenGroupClientAsync(
                Session, groupNodeId, groupId, Telemetry, ct,
                await UsesDistinctHierarchyAsync(ct).ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Calls the generated <c>Refresh</c> Method and returns a typed
        /// result wrapping the summary, per-resource results and the new
        /// generation.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
        public async ValueTask<WotRegistryRefreshResult> RefreshAsync(
            ArrayOf<WoTResourceSelectorDataType> selection,
            WoTRefreshOptionsDataType options,
            uint expectedGeneration = 0,
            string requestId = "",
            CancellationToken ct = default)
        {
            var request = new CallMethodRequest
            {
                ObjectId = RegistryNodeId,
                MethodId = ExpandedNodeId.ToNodeId(
                    MethodIds.WoTRegistryType_Refresh,
                    Session.NamespaceUris),
                InputArguments =
                [
                    Variant.FromStructure(selection),
                    Variant.FromStructure(options),
                    new Variant(expectedGeneration),
                    new Variant(requestId)
                ]
            };
            CallResponse response = await Session
                .CallAsync(new RequestHeader(), [request], ct)
                .ConfigureAwait(false);
            CallMethodResult callResult = GetFirstCallResult(response, "Refresh");
            if (StatusCode.IsBad(callResult.StatusCode))
            {
                throw new ServiceResultException(callResult.StatusCode);
            }
            ArrayOf<Variant> output = callResult.OutputArguments.IsNull
                ? []
                : callResult.OutputArguments;
            if (output.Count < 3)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "Refresh returned unexpected output arguments.");
            }
            bool hasSummary = TryDecodeStructure(
                output[0], out WoTRefreshSummaryDataType? summary);
            bool hasResults = TryDecodeStructureArray(
                output[1], out ArrayOf<WoTResourceLoadResultDataType> results);
            bool hasGeneration = output[2].TryGetValue(out uint newGeneration);
            if (!hasSummary || summary is null || !hasResults || !hasGeneration)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "Refresh returned unexpected output arguments: " +
                    $"{output[0].TypeInfo}, {output[1].TypeInfo}, {output[2].TypeInfo}.");
            }
            return new WotRegistryRefreshResult(summary, results, newGeneration);
        }

        private bool TryDecodeStructure<T>(Variant value, out T? result)
            where T : class, IEncodeable, new()
        {
            result = null;
#pragma warning disable CS8600 // TryGetStructure/TryGetValue annotate failed output as maybe-null.
            if (value.TryGetStructure(Session.MessageContext, out T decoded))
#pragma warning restore CS8600
            {
                result = decoded;
                return true;
            }
#pragma warning disable CS8600 // TryGetValue annotates failed output as maybe-null.
            if (value.TryGetValue(out ExtensionObject extension) &&
                extension.TryGetValue(out decoded, Session.MessageContext))
#pragma warning restore CS8600
            {
                result = decoded;
                return true;
            }
#pragma warning disable CS8600 // TryGetValue annotates failed output as maybe-null.
            if (value.TryGetValue(out extension) &&
                TryDecodeBinaryExtension(extension, out decoded))
#pragma warning restore CS8600
            {
                result = decoded;
                return true;
            }
            return false;
        }

        private bool TryDecodeStructureArray<T>(Variant value, out ArrayOf<T> result)
            where T : class, IEncodeable, new()
        {
            result = [];
            if (value.TryGetStructure(Session.MessageContext, out result))
            {
                return true;
            }
            if (!value.TryGetValue(out ArrayOf<ExtensionObject> extensions))
            {
                return false;
            }
            var decoded = new T[extensions.Count];
            for (int i = 0; i < extensions.Count; i++)
            {
                if (!extensions[i].TryGetValue(out T? item, Session.MessageContext) &&
                    !TryDecodeBinaryExtension(extensions[i], out item) ||
                    item is null)
                {
                    return false;
                }
                decoded[i] = item;
            }
            result = decoded.ToArrayOf();
            return true;
        }

        private bool TryDecodeBinaryExtension<T>(ExtensionObject extension, out T? value)
            where T : class, IEncodeable, new()
        {
            value = null;
            if (!extension.TryGetAsBinary(out ByteString body, Session.MessageContext) || body.IsNull)
            {
                return false;
            }
            try
            {
                using var decoder = new BinaryDecoder(body.Span.ToArray(), Session.MessageContext);
                value = new T();
                value.Decode(decoder);
                return true;
            }
            catch (ServiceResultException)
            {
                value = null;
                return false;
            }
        }

        /// <summary>
        /// Convenience overload of <see cref="RefreshAsync"/> that selects
        /// the whole registry (an empty selection) with default options
        /// when none are supplied.
        /// </summary>
        public ValueTask<WotRegistryRefreshResult> RefreshAllAsync(
            WoTRefreshOptionsDataType? options = null,
            string requestId = "",
            uint expectedGeneration = 0,
            CancellationToken ct = default)
        {
            return RefreshAsync(
                [],
                options ?? new WoTRefreshOptionsDataType(),
                expectedGeneration,
                requestId,
                ct);
        }

        /// <summary>
        /// Loads <paramref name="documents"/> into their target
        /// groups/resources and optionally triggers a <c>Refresh</c> as a
        /// single workflow. Thing Models are get-or-created and uploaded
        /// before Thing Descriptions, preserving the caller's relative
        /// order within each kind, so referenced models are always
        /// materialised before the descriptions that depend on them.
        /// Mutation failures propagate immediately (no document after the
        /// failing one is processed); refresh failures are surfaced in the
        /// returned <see cref="WotRegistryRefreshResult"/> rather than
        /// thrown, since a partial refresh outcome is legitimate
        /// application data.
        /// </summary>
        /// <param name="documents">The documents to load.</param>
        /// <param name="refresh">When <c>true</c> (the default), calls
        /// <see cref="RefreshAllAsync"/> after every document has been
        /// uploaded.</param>
        /// <param name="refreshOptions">Options forwarded to the refresh
        /// call; ignored when <paramref name="refresh"/> is <c>false</c>.</param>
        /// <param name="requestId">Request id forwarded to the refresh
        /// call. When null, a new identifier is generated.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="documents"/> is null.</exception>
        /// <exception cref="ServiceResultException">A document's declared
        /// kind does not match the kind already held by its target group.</exception>
        public async ValueTask<WotRegistryBulkLoadResult> LoadDocumentsAsync(
            ArrayOf<WotRegistryDocument> documents,
            bool refresh = true,
            WoTRefreshOptionsDataType? refreshOptions = null,
            string? requestId = null,
            CancellationToken ct = default)
        {
            if (documents.IsNull)
            {
                throw new ArgumentNullException(nameof(documents));
            }

            WotRegistryDocument[] ordered = OrderThingModelsFirst(documents);
            var outcomes = new WotRegistryDocumentLoadOutcome[ordered.Length];
            var groups = new Dictionary<string, WotRegistryGroupClient>(StringComparer.Ordinal);
            for (int i = 0; i < ordered.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                WotRegistryDocument document = ordered[i];
                if (!groups.TryGetValue(document.GroupId, out WotRegistryGroupClient? group))
                {
                    (group, _) = await GetOrCreateGroupAsync(document.GroupId, ct).ConfigureAwait(false);
                    groups[document.GroupId] = group;
                }
                if (group.Kind != document.Kind)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        $"Registry group '{document.GroupId}' holds {group.Kind} resources; " +
                        $"the document for resource '{document.ResourceId}' declares {document.Kind}.");
                }

                (WotRegistryResourceClient resource, string versionId, bool created) = await group
                    .GetOrCreateResourceAsync(document.ResourceId, document.VersionId, ct)
                    .ConfigureAwait(false);
                NodeId uploadedResourceNodeId = resource.ResourceNodeId;
                string uploadedVersionId = versionId;
                if (string.IsNullOrEmpty(document.VersionId))
                {
                    WotRegistryUploadResult upload = await resource
                        .UploadNewVersionAndGetResultAsync(document.Content, ct: ct)
                        .ConfigureAwait(false);
                    uploadedResourceNodeId = upload.ResourceNodeId;
                    uploadedVersionId = upload.VersionId;
                }
                else
                {
                    await resource.Proxy.UploadAsync(document.Content, ct: ct)
                        .ConfigureAwait(false);
                }

                outcomes[i] = new WotRegistryDocumentLoadOutcome(
                    document, uploadedResourceNodeId, uploadedVersionId, created);
            }

            WotRegistryRefreshResult? refreshResult = null;
            if (refresh)
            {
                refreshResult = await RefreshAllAsync(
                    refreshOptions,
                    requestId ?? Guid.NewGuid().ToString("N"),
                    ct: ct).ConfigureAwait(false);
            }
            return new WotRegistryBulkLoadResult(outcomes.ToArrayOf(), refreshResult);
        }

        /// <summary>
        /// Stably partitions <paramref name="documents"/> so every Thing
        /// Model document precedes every Thing Description document,
        /// preserving the caller's relative order within each kind.
        /// </summary>
        private static WotRegistryDocument[] OrderThingModelsFirst(ArrayOf<WotRegistryDocument> documents)
        {
            var ordered = new WotRegistryDocument[documents.Count];
            int next = 0;
            foreach (WotRegistryDocument document in documents)
            {
                if (document.Kind == WoTDocumentKindEnum.ThingModel)
                {
                    ordered[next++] = document;
                }
            }
            foreach (WotRegistryDocument document in documents)
            {
                if (document.Kind != WoTDocumentKindEnum.ThingModel)
                {
                    ordered[next++] = document;
                }
            }
            return ordered;
        }

        /// <summary>
        /// Builds a <see cref="WotRegistryGroupClient"/> for an existing
        /// group NodeId, discovering its Thing Description / Thing Model
        /// kind from the reported <c>TypeDefinition</c>.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The group's TypeDefinition could not be resolved or is not recognised.
        /// </exception>
        internal static async ValueTask<WotRegistryGroupClient> OpenGroupClientAsync(
            ISession session,
            NodeId groupNodeId,
            string groupId,
            ITelemetryContext telemetry,
            CancellationToken ct,
            bool usesDistinctHierarchy = false)
        {
            WoTDocumentKindEnum kind = await ResolveGroupKindAsync(session, groupNodeId, ct)
                .ConfigureAwait(false);
            GroupTypeClient proxy = kind == WoTDocumentKindEnum.ThingModel
                ? new ThingModelGroupTypeClient(session, groupNodeId, telemetry)
                : new ThingDescriptionGroupTypeClient(session, groupNodeId, telemetry);
            return new WotRegistryGroupClient(
                session, groupNodeId, groupId, kind, proxy, telemetry, usesDistinctHierarchy);
        }

        private static async ValueTask<WoTDocumentKindEnum> ResolveGroupKindAsync(
            ISession session,
            NodeId groupNodeId,
            CancellationToken ct)
        {
            (_, _, ArrayOf<ReferenceDescription> references) = await session.BrowseAsync(
                requestHeader: null,
                view: null,
                groupNodeId,
                maxResultsToReturn: 1,
                BrowseDirection.Forward,
                Ua.ReferenceTypeIds.HasTypeDefinition,
                includeSubtypes: false,
                (uint)NodeClass.ObjectType,
                ct).ConfigureAwait(false);
            if (references.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "Registry group has no reported TypeDefinition.");
            }
            var typeId = ExpandedNodeId.ToNodeId(references[0].NodeId, session.NamespaceUris);
            if (typeId == ExpandedNodeId.ToNodeId(ObjectTypeIds.ThingModelGroupType, session.NamespaceUris))
            {
                return WoTDocumentKindEnum.ThingModel;
            }
            if (typeId == ExpandedNodeId.ToNodeId(ObjectTypeIds.ThingDescriptionGroupType, session.NamespaceUris))
            {
                return WoTDocumentKindEnum.ThingDescription;
            }
            throw new ServiceResultException(
                StatusCodes.BadUnexpectedError,
                "Registry group has an unrecognised TypeDefinition.");
        }

        private static CallMethodResult GetFirstCallResult(CallResponse response, string methodName)
        {
            if (response.Results.IsNull || response.Results.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    $"{methodName} returned no method result.");
            }
            return response.Results[0];
        }
    }

    /// <summary>
    /// An immutable descriptor for a single document to load through
    /// <see cref="WotRegistryClient.LoadDocumentsAsync"/>.
    /// </summary>
    public sealed class WotRegistryDocument
    {
        /// <summary>
        /// Creates a new document descriptor.
        /// </summary>
        /// <param name="kind">Whether the document is a Thing Description
        /// or a Thing Model.</param>
        /// <param name="groupId">Id of the target registry group.</param>
        /// <param name="resourceId">Id of the target resource within the
        /// group.</param>
        /// <param name="content">The document body.</param>
        /// <param name="versionId">Optional caller-supplied version id;
        /// defaults to empty (server-assigned).</param>
        /// <exception cref="ArgumentException"><paramref name="groupId"/>
        /// or <paramref name="resourceId"/> is null or empty, or
        /// <paramref name="content"/> is null.</exception>
        public WotRegistryDocument(
            WoTDocumentKindEnum kind,
            string groupId,
            string resourceId,
            ByteString content,
            string versionId = "")
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                throw new ArgumentException("Group id is required.", nameof(groupId));
            }
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                throw new ArgumentException("Resource id is required.", nameof(resourceId));
            }
            if (content.IsNull)
            {
                throw new ArgumentException("Document content is required.", nameof(content));
            }
            Kind = kind;
            GroupId = groupId;
            ResourceId = resourceId;
            Content = content;
            VersionId = versionId ?? string.Empty;
        }

        /// <summary>
        /// Whether the document is a Thing Description or a Thing Model.
        /// </summary>
        public WoTDocumentKindEnum Kind { get; }

        /// <summary>
        /// Id of the target registry group.
        /// </summary>
        public string GroupId { get; }

        /// <summary>
        /// Id of the target resource within the group.
        /// </summary>
        public string ResourceId { get; }

        /// <summary>
        /// The document body.
        /// </summary>
        public ByteString Content { get; }

        /// <summary>
        /// Caller-supplied version id (may be empty for server-assigned).
        /// </summary>
        public string VersionId { get; }
    }

    /// <summary>
    /// The outcome of loading a single <see cref="WotRegistryDocument"/>
    /// through <see cref="WotRegistryClient.LoadDocumentsAsync"/>.
    /// </summary>
    public sealed class WotRegistryDocumentLoadOutcome
    {
        internal WotRegistryDocumentLoadOutcome(
            WotRegistryDocument document,
            NodeId resourceNodeId,
            string versionId,
            bool created)
        {
            Document = document;
            ResourceNodeId = resourceNodeId;
            VersionId = versionId;
            Created = created;
        }

        /// <summary>
        /// The document descriptor this outcome corresponds to.
        /// </summary>
        public WotRegistryDocument Document { get; }

        /// <summary>
        /// NodeId of the concrete Version the document was uploaded to.
        /// </summary>
        public NodeId ResourceNodeId { get; }

        /// <summary>
        /// The server-assigned version id after the upload.
        /// </summary>
        public string VersionId { get; }

        /// <summary>
        /// Whether the resource was newly created by this load.
        /// </summary>
        public bool Created { get; }
    }

    /// <summary>
    /// The result of <see cref="WotRegistryClient.LoadDocumentsAsync"/>.
    /// </summary>
    public sealed class WotRegistryBulkLoadResult
    {
        internal WotRegistryBulkLoadResult(
            ArrayOf<WotRegistryDocumentLoadOutcome> uploaded,
            WotRegistryRefreshResult? refresh)
        {
            Uploaded = uploaded;
            Refresh = refresh;
        }

        /// <summary>
        /// The per-document upload outcomes, in the order they were
        /// applied (Thing Models before Thing Descriptions).
        /// </summary>
        public ArrayOf<WotRegistryDocumentLoadOutcome> Uploaded { get; }

        /// <summary>
        /// The refresh result, or null when the caller opted out of the
        /// refresh step.
        /// </summary>
        public WotRegistryRefreshResult? Refresh { get; }
    }

    /// <summary>
    /// A typed wrapper around the output of the generated
    /// <see cref="WoTRegistryTypeClient.RefreshAsync"/> call.
    /// </summary>
    public sealed class WotRegistryRefreshResult
    {
        internal WotRegistryRefreshResult(
            WoTRefreshSummaryDataType summary,
            ArrayOf<WoTResourceLoadResultDataType> results,
            uint newGeneration)
        {
            Summary = summary;
            Results = results;
            NewGeneration = newGeneration;
        }

        /// <summary>
        /// The registry-wide refresh summary.
        /// </summary>
        public WoTRefreshSummaryDataType Summary { get; }

        /// <summary>
        /// The per-resource load results.
        /// </summary>
        public ArrayOf<WoTResourceLoadResultDataType> Results { get; }

        /// <summary>
        /// The registry generation committed by this refresh.
        /// </summary>
        public uint NewGeneration { get; }

        /// <summary>
        /// True when the registry-wide outcome, or any per-resource
        /// outcome, is <see cref="WoTOutcomeEnum.Failed"/> or
        /// <see cref="WoTOutcomeEnum.Rejected"/>.
        /// </summary>
        public bool HasFailures
        {
            get
            {
                if (Summary.Outcome is WoTOutcomeEnum.Failed or WoTOutcomeEnum.Rejected)
                {
                    return true;
                }
                foreach (WoTResourceLoadResultDataType result in Results)
                {
                    if (result.Outcome is WoTOutcomeEnum.Failed or WoTOutcomeEnum.Rejected)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Throws a <see cref="ServiceResultException"/> when
        /// <see cref="HasFailures"/> is true.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The refresh reported at least one failed or rejected outcome.
        /// </exception>
        public void EnsureSuccess()
        {
            if (!HasFailures)
            {
                return;
            }
            throw new ServiceResultException(
                StatusCodes.BadUnexpectedError,
                $"Refresh request '{Summary.RequestId}' reported {Summary.Failed} " +
                $"failed and {Summary.Skipped} skipped resource(s) out of {Summary.Total}.");
        }
    }
}
