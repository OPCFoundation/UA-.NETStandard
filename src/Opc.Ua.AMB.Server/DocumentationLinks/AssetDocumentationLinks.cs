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
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Configuration;
using Opc.Ua.Server;

namespace Opc.Ua.AMB.Server.DocumentationLinks
{
    /// <summary>
    /// The <c>DocumentationLinks</c> AddIn of an asset (OPC 10000-110 §10.5):
    /// the declared links, the editable ones with what clients wrote, and the
    /// links clients added through <c>AddLink</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The AddIn lives in the node manager that owns the asset, referenced
    /// from it with <c>0:HasAddIn</c>. What clients write and add is kept in
    /// the <see cref="IAssetConfigurationStore"/> under the asset's
    /// <c>ProductInstanceUri</c>, and a link a client added comes back after
    /// a restart with the same NodeId.
    /// </para>
    /// <para>
    /// Changing links is authorized by
    /// <see cref="AmbServerOptions.AuthorizeLinkEdit"/>; by default every
    /// authenticated user may, an anonymous one may not. The
    /// <c>UserAccessLevel</c> of an editable link and the
    /// <c>UserExecutable</c> of <c>AddLink</c> and <c>RemoveLink</c> tell a
    /// user who may not that he cannot.
    /// </para>
    /// <para>
    /// An editable link without a value is published as null and, like a
    /// link a client cleared, does not count for "AMB DocumentationLinks
    /// Base".
    /// </para>
    /// <para>
    /// A link a client added carries the
    /// <see cref="DocumentationLinkProperties.UserLink"/> Property, so other
    /// clients see which links <c>RemoveLink</c> accepts; OPC 10000-110
    /// itself has no way to tell.
    /// </para>
    /// </remarks>
    internal sealed class AssetDocumentationLinks : IDocumentationLinks, IDisposable
    {
        private AssetDocumentationLinks(
            AssetHandle handle,
            DocumentationLinksState addIn,
            AmbServerOptions options,
            IAssetConfigurationStore store,
            string productInstanceUri,
            bool userLinks,
            ushort typeNamespaceIndex,
            Func<CancellationToken, ValueTask> changedAsync,
            ILogger logger)
        {
            m_handle = handle;
            m_typeNamespaceIndex = typeNamespaceIndex;
            m_addIn = addIn;
            m_options = options;
            m_store = store;
            m_productInstanceUri = productInstanceUri;
            AllowsUserLinks = userLinks;
            m_changedAsync = changedAsync;
            m_logger = logger;
        }

        /// <inheritdoc/>
        public NodeId NodeId => m_addIn.NodeId;

        /// <inheritdoc/>
        public bool AllowsUserLinks { get; }

        /// <inheritdoc/>
        public ArrayOf<DocumentationLink> Links
        {
            get
            {
                lock (m_lock)
                {
                    var links = new DocumentationLink[m_links.Count];
                    for (int ii = 0; ii < links.Length; ii++)
                    {
                        LinkEntry entry = m_links[ii];
                        links[ii] = new DocumentationLink(
                            entry.Variable.NodeId,
                            entry.Variable.BrowseName,
                            entry.Variable.Value ?? string.Empty,
                            entry.IsEditable,
                            entry.Record != null);
                    }
                    return links.ToArrayOf();
                }
            }
        }

        /// <summary>
        /// Freezes the AddIn when the asset is unregistered: the links stay,
        /// but clients can no longer change them.
        /// </summary>
        public void Dispose()
        {
            lock (m_lock)
            {
                foreach (LinkEntry entry in m_links)
                {
                    entry.Variable.OnSimpleWriteValueAsync = null;
                    entry.Variable.AccessLevel = AccessLevels.CurrentRead;
                    entry.Variable.UserAccessLevel = AccessLevels.CurrentRead;
                }
            }
            m_addIn.AddLink?.OnCallAsync = null;
            m_addIn.RemoveLink?.OnCallAsync = null;
            m_mutation.Dispose();
        }

        /// <summary>
        /// Gets the number of links that have a value.
        /// </summary>
        internal int PublishedLinkCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_links.FindAll(entry => !string.IsNullOrEmpty(entry.Variable.Value)).Count;
                }
            }
        }

        /// <summary>
        /// Gets whether at least one link is editable.
        /// </summary>
        internal bool HasEditableLink
        {
            get
            {
                lock (m_lock)
                {
                    return m_links.Exists(entry => entry.IsEditable);
                }
            }
        }

        /// <summary>
        /// Creates the AddIn of an asset while it is registered.
        /// </summary>
        /// <returns>
        /// The AddIn, or <see langword="null"/> when the application asked
        /// for none.
        /// </returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadConfigurationError"/> when links are to
        /// be persisted for an asset without <c>ProductInstanceUri</c>, or users
        /// are to add links while the store does not survive a restart.
        /// </exception>
        public static async ValueTask<AssetDocumentationLinks?> CreateAsync(
            AmbNodeManager manager,
            AssetHandle handle,
            DocumentationLinksRequest? request,
            AmbServerOptions options,
            IAssetConfigurationStore store,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return null;
            }

            string productInstanceUri = handle.ProductInstanceUri;
            if (productInstanceUri.Length == 0 &&
                (request.UserLinks || request.Links.Exists(link => link.Editable)))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Asset '{0}' has no ProductInstanceUri, which the links clients change are persisted under.",
                    handle.BrowseName);
            }
            if (request.UserLinks && !store.IsPersistent)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Asset '{0}' lets users add links, which OPC 10000-110 §10.5.3 asks to be kept persistently, " +
                    "but the configuration store keeps them in memory only. Use UseFileSystemStores or another " +
                    "persistent IAssetConfigurationStore.",
                    handle.BrowseName);
            }

            ISystemContext context = handle.Context;
            var addIn = new DocumentationLinksState(handle.Asset);
            addIn.Create(
                context,
                NodeId.Null,
                new QualifiedName(BrowseNames.DocumentationLinks, manager.AmbNamespaceIndex),
                new LocalizedText(BrowseNames.DocumentationLinks),
                false);
            addIn.ReferenceTypeId = Ua.ReferenceTypeIds.HasAddIn;
            if (request.UserLinks)
            {
                addIn.AddAddLink(context)
                    .AddRemoveLink(context);
            }
            handle.Asset.AddChild(addIn);
            NodeId previous = context.AssignInstanceNodeId(addIn);
            context.AssignInstanceChildNodeIds(addIn, previous);
            handle.Owner.AddNode(addIn);

            var links = new AssetDocumentationLinks(
                handle,
                addIn,
                options,
                store,
                productInstanceUri,
                request.UserLinks,
                manager.TypeNamespaceIndex,
                manager.OnAssetsChangedAsync,
                logger);
            foreach (DeclaredLink link in request.Links)
            {
                await links.DeclareAsync(link, cancellationToken).ConfigureAwait(false);
            }
            if (request.UserLinks)
            {
                await links.RestoreUserLinksAsync(cancellationToken).ConfigureAwait(false);
                addIn.AddLink!.OnCallAsync = links.OnAddLinkAsync;
                addIn.RemoveLink!.OnCallAsync = links.OnRemoveLinkAsync;
                addIn.AddLink!.OnReadUserExecutable = links.OnReadUserExecutable;
                addIn.RemoveLink!.OnReadUserExecutable = links.OnReadUserExecutable;
            }
            return links;
        }

        private async ValueTask DeclareAsync(DeclaredLink link, CancellationToken cancellationToken)
        {
            string? uri = link.Uri;
            if (link.Editable)
            {
                string? stored = await m_store
                    .GetValueAsync(m_productInstanceUri, StoreNameOf(link.Name), cancellationToken)
                    .ConfigureAwait(false);
                if (stored != null && UnicodeText.Length(stored) > m_options.MaxDocumentationLinkLength)
                {
                    // A limit lowered since the value was stored.
                    stored = UnicodeText.Truncate(stored, m_options.MaxDocumentationLinkLength);
                    m_logger.LinkClamped(link.Name, m_productInstanceUri);
                }
                uri = stored ?? uri;
            }

            BaseDataVariableState<string> variable = CreateVariable(
                new QualifiedName(link.Name, m_handle.Asset.NodeId.NamespaceIndex),
                new LocalizedText(link.Name),
                link.Description,
                uri,
                link.Editable);
            variable.NodeId = m_handle.Owner.New(m_handle.Context, variable);
            var entry = new LinkEntry(variable, link.Editable, StoreNameOf(link.Name), null);
            Register(entry);
        }

        private async ValueTask RestoreUserLinksAsync(CancellationToken cancellationToken)
        {
            string? json = await m_store
                .GetValueAsync(m_productInstanceUri, AssetConfigurationNames.DocumentationLinks, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            List<UserLinkRecord>? records;
            try
            {
                records = JsonSerializer.Deserialize(json, UserLinkJsonContext.Default.ListUserLinkRecord);
            }
            catch (JsonException ex)
            {
                m_logger.UserLinksUnreadable(ex, m_productInstanceUri);
                return;
            }

            NamespaceTable namespaceUris = m_handle.Context.NamespaceUris;
            int restored = 0;
            foreach (UserLinkRecord record in records ?? [])
            {
                int nodeNamespace = namespaceUris.GetIndex(record.NodeNamespaceUri);
                int browseNamespace = namespaceUris.GetIndex(record.BrowseNameNamespaceUri);
                if (nodeNamespace < 0 ||
                    browseNamespace < 0 ||
                    string.IsNullOrEmpty(record.Identifier) ||
                    string.IsNullOrEmpty(record.BrowseName) ||
                    string.IsNullOrEmpty(record.Uri))
                {
                    m_logger.UserLinkSkipped(record.BrowseName, m_productInstanceUri);
                    continue;
                }
                if (restored == m_options.MaxUserLinksPerAsset)
                {
                    // A limit lowered since the links were added.
                    m_logger.UserLinkOverLimit(record.BrowseName, m_productInstanceUri, m_options.MaxUserLinksPerAsset);
                    continue;
                }
                if (Clamp(record))
                {
                    m_logger.LinkClamped(record.BrowseName, m_productInstanceUri);
                }
                if (m_addIn.FindChildWithQualifiedName(
                        m_handle.Context,
                        new QualifiedName(record.BrowseName, (ushort)browseNamespace)) != null)
                {
                    m_logger.UserLinkSkipped(record.BrowseName, m_productInstanceUri);
                    continue;
                }
                restored++;
                BaseDataVariableState<string> variable = CreateVariable(
                    new QualifiedName(record.BrowseName, (ushort)browseNamespace),
                    new LocalizedText(record.DisplayNameLocale, record.DisplayName ?? record.BrowseName),
                    new LocalizedText(record.DescriptionLocale, record.Description),
                    record.Uri,
                    editable: true);
                variable.NodeId = new NodeId(record.Identifier, (ushort)nodeNamespace);
                MarkAsUserLink(variable, record.Identifier);
                Register(new LinkEntry(variable, true, null, record));
            }
        }

        /// <summary>
        /// Cuts the texts of a persisted link to the current limits.
        /// </summary>
        /// <returns><see langword="true"/> when something was cut.</returns>
        private bool Clamp(UserLinkRecord record)
        {
            bool clamped = false;
            string? Cut(string? text, int maxLength)
            {
                if (text != null && UnicodeText.Length(text) > maxLength)
                {
                    clamped = true;
                    return UnicodeText.Truncate(text, maxLength);
                }
                return text;
            }
            record.Uri = Cut(record.Uri, m_options.MaxDocumentationLinkLength)!;
            record.BrowseName = Cut(record.BrowseName, m_options.MaxDocumentationLinkNameLength)!;
            record.DisplayName = Cut(record.DisplayName, m_options.MaxDocumentationLinkNameLength);
            record.Description = Cut(record.Description, m_options.MaxDocumentationLinkDescriptionLength);
            record.DisplayNameLocale = Cut(record.DisplayNameLocale, MaxLocaleLength);
            record.DescriptionLocale = Cut(record.DescriptionLocale, MaxLocaleLength);
            return clamped;
        }

        private BaseDataVariableState<string> CreateVariable(
            QualifiedName browseName,
            LocalizedText displayName,
            LocalizedText description,
            string? uri,
            bool editable)
        {
            BaseDataVariableState<string> variable = BaseDataVariableState<string>.With<VariantBuilder>(m_addIn);
            variable.SymbolicName = browseName.Name ?? string.Empty;
            variable.BrowseName = browseName;
            variable.DisplayName = displayName.IsNullOrEmpty ? new LocalizedText(browseName.Name) : displayName;
            variable.Description = description;
            variable.TypeDefinitionId = Ua.VariableTypeIds.BaseDataVariableType;
            variable.ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent;
            variable.DataType = Ua.DataTypeIds.UriString;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = editable ? AccessLevels.CurrentReadOrWrite : AccessLevels.CurrentRead;
            variable.UserAccessLevel = variable.AccessLevel;
            if (string.IsNullOrEmpty(uri))
            {
                // An editable link nobody has set yet is unknown, not empty.
                variable.WrappedValue = Variant.Null;
            }
            else
            {
                variable.Value = uri;
            }
            variable.Timestamp = DateTimeUtc.Now;
            return variable;
        }

        /// <summary>
        /// Gives a link a user added the
        /// <see cref="DocumentationLinkProperties.UserLink"/> Property, so a
        /// client can tell it from the links of the manufacturer, which
        /// <c>RemoveLink</c> refuses (§10.5.4). Its NodeId derives from the
        /// link's, so it is the same after a restart.
        /// </summary>
        private void MarkAsUserLink(BaseDataVariableState<string> variable, string identifier)
        {
            var marker = PropertyState<bool>.With<VariantBuilder>(variable);
            marker.SymbolicName = DocumentationLinkProperties.UserLink;
            marker.BrowseName = new QualifiedName(DocumentationLinkProperties.UserLink, m_typeNamespaceIndex);
            marker.DisplayName = new LocalizedText(DocumentationLinkProperties.UserLink);
            marker.TypeDefinitionId = VariableTypeIds.PropertyType;
            marker.ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty;
            marker.DataType = Ua.DataTypeIds.Boolean;
            marker.ValueRank = ValueRanks.Scalar;
            marker.AccessLevel = AccessLevels.CurrentRead;
            marker.UserAccessLevel = AccessLevels.CurrentRead;
            marker.Value = true;
            marker.NodeId = new NodeId(
                identifier + "_" + DocumentationLinkProperties.UserLink,
                variable.NodeId.NamespaceIndex);
            variable.AddChild(marker);
        }

        private void Register(LinkEntry entry)
        {
            BaseDataVariableState<string> variable = entry.Variable;
            m_addIn.AddChild(variable);
            if (entry.IsEditable)
            {
                variable.OnSimpleWriteValueAsync = (context, _, value, cancellationToken) =>
                    OnWriteAsync(entry, context, value, cancellationToken);
                variable.OnReadUserAccessLevel = OnReadUserAccessLevel;
            }
            m_handle.Owner.AddNode(variable);
            lock (m_lock)
            {
                m_links.Add(entry);
            }
        }

        private async ValueTask<AttributeWriteResult> OnWriteAsync(
            LinkEntry entry,
            ISystemContext context,
            Variant value,
            CancellationToken cancellationToken)
        {
            if (!Authorize(context))
            {
                return new AttributeWriteResult(StatusCodes.BadUserAccessDenied);
            }
            string uri;
            if (value.IsNull)
            {
                uri = string.Empty;
            }
            else if (!value.TryGetValue(out string text))
            {
                return new AttributeWriteResult(StatusCodes.BadTypeMismatch);
            }
            else
            {
                uri = text;
            }
            if (UnicodeText.Length(uri) > m_options.MaxDocumentationLinkLength)
            {
                return new AttributeWriteResult(ServiceResult.Create(
                    StatusCodes.BadOutOfRange,
                    "A documentation link must not be longer than {0} characters.",
                    m_options.MaxDocumentationLinkLength));
            }

            await m_mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (entry.Record != null)
                {
                    string previous = entry.Record.Uri;
                    entry.Record.Uri = uri;
                    if (!await TrySaveUserLinksAsync(null, null, cancellationToken).ConfigureAwait(false))
                    {
                        entry.Record.Uri = previous;
                        return new AttributeWriteResult(StatusCodes.BadResourceUnavailable);
                    }
                }
                else
                {
                    try
                    {
                        await m_store
                            .SetValueAsync(m_productInstanceUri, entry.StoreName!, uri, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        m_logger.LinkNotPersisted(ex, entry.Variable.BrowseName, m_productInstanceUri);
                        return new AttributeWriteResult(StatusCodes.BadResourceUnavailable);
                    }
                }

                // The new value counts for the conformance units published
                // below; the framework writes the same value again once the
                // handler returns.
                entry.Variable.WrappedValue = value;
                entry.Variable.Timestamp = DateTimeUtc.Now;
                entry.Variable.ClearChangeMasks(context, false);
            }
            finally
            {
                m_mutation.Release();
            }
            m_logger.LinkChanged(entry.Variable.BrowseName, m_productInstanceUri);
            await NotifyChangedAsync(cancellationToken).ConfigureAwait(false);
            return new AttributeWriteResult(ServiceResult.Good);
        }

        /// <summary>
        /// Tells a user who may not change links that the link is read-only
        /// for him.
        /// </summary>
        private ServiceResult OnReadUserAccessLevel(ISystemContext context, NodeState node, ref byte value)
        {
            if (!Authorize(context))
            {
                value = (byte)(value & ~AccessLevels.CurrentWrite);
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Tells a user who may not change links that he cannot call
        /// <c>AddLink</c> and <c>RemoveLink</c>.
        /// </summary>
        private ServiceResult OnReadUserExecutable(ISystemContext context, NodeState node, ref bool value)
        {
            value &= Authorize(context);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Lets the conformance units follow a change of the links; a failure
        /// there does not undo the change.
        /// </summary>
        private async ValueTask NotifyChangedAsync(CancellationToken cancellationToken)
        {
            try
            {
                await m_changedAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger.LinkFollowUpFailed(ex, m_productInstanceUri);
            }
        }

        private async ValueTask<AddLinkMethodStateResult> OnAddLinkAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string linkToExternalSource,
            QualifiedName browseName,
            LocalizedText displayName,
            LocalizedText description,
            CancellationToken cancellationToken)
        {
            if (!Authorize(context))
            {
                return new AddLinkMethodStateResult { ServiceResult = StatusCodes.BadUserAccessDenied };
            }
            if (browseName.IsNull ||
                string.IsNullOrEmpty(browseName.Name) ||
                string.IsNullOrEmpty(linkToExternalSource) ||
                UnicodeText.Length(linkToExternalSource) > m_options.MaxDocumentationLinkLength)
            {
                return new AddLinkMethodStateResult { ServiceResult = StatusCodes.BadInvalidArgument };
            }
            if (TooLong(browseName.Name, m_options.MaxDocumentationLinkNameLength) ||
                TooLong(displayName.Text, m_options.MaxDocumentationLinkNameLength) ||
                TooLong(description.Text, m_options.MaxDocumentationLinkDescriptionLength) ||
                TooLong(displayName.Locale, MaxLocaleLength) ||
                TooLong(description.Locale, MaxLocaleLength))
            {
                // What a user adds is persisted and comes back with every
                // start, so its texts are bounded like the link itself.
                return new AddLinkMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(
                        StatusCodes.BadInvalidArgument,
                        "The browse name or display name is longer than {0}, or the description longer than {1} " +
                        "characters.",
                        m_options.MaxDocumentationLinkNameLength,
                        m_options.MaxDocumentationLinkDescriptionLength)
                };
            }

            NodeId variableId;
            await m_mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (m_lock)
                {
                    if (m_links.FindAll(entry => entry.Record != null).Count >= m_options.MaxUserLinksPerAsset)
                    {
                        return Invalid("The asset has {0} links of its users already.", m_options.MaxUserLinksPerAsset);
                    }
                }
                if (m_addIn.FindChildWithQualifiedName(m_handle.Context, browseName) != null)
                {
                    return Invalid("The DocumentationLinks of the asset have a child named '{0}' already.", browseName);
                }

                NamespaceTable namespaceUris = m_handle.Context.NamespaceUris;
                ushort nodeNamespace = m_addIn.NodeId.NamespaceIndex;
                var record = new UserLinkRecord
                {
                    Identifier = "DocumentationLink_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
                    NodeNamespaceUri = namespaceUris.GetString(nodeNamespace) ?? string.Empty,
                    BrowseName = browseName.Name!,
                    BrowseNameNamespaceUri = namespaceUris.GetString(browseName.NamespaceIndex) ?? string.Empty,
                    Uri = linkToExternalSource,
                    DisplayName = displayName.Text,
                    DisplayNameLocale = displayName.Locale,
                    Description = description.Text,
                    DescriptionLocale = description.Locale
                };
                BaseDataVariableState<string> variable = CreateVariable(
                    browseName,
                    displayName,
                    description,
                    linkToExternalSource,
                    editable: true);
                variable.NodeId = new NodeId(record.Identifier, nodeNamespace);
                MarkAsUserLink(variable, record.Identifier);
                variableId = variable.NodeId;
                var entry = new LinkEntry(variable, true, null, record);

                // Persist first: a link that would not survive a restart is
                // not added (§10.5.3).
                if (!await TrySaveUserLinksAsync(record, null, cancellationToken).ConfigureAwait(false))
                {
                    return new AddLinkMethodStateResult { ServiceResult = StatusCodes.BadResourceUnavailable };
                }
                Register(entry);
                m_logger.UserLinkAdded(browseName, m_productInstanceUri);
            }
            finally
            {
                m_mutation.Release();
            }
            await NotifyChangedAsync(cancellationToken).ConfigureAwait(false);
            return new AddLinkMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                LinkVariable = variableId
            };
        }

        private async ValueTask<RemoveLinkMethodStateResult> OnRemoveLinkAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId variableToBeDeleted,
            CancellationToken cancellationToken)
        {
            if (!Authorize(context))
            {
                return new RemoveLinkMethodStateResult { ServiceResult = StatusCodes.BadUserAccessDenied };
            }

            await m_mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                LinkEntry? entry;
                lock (m_lock)
                {
                    entry = m_links.Find(candidate => candidate.Variable.NodeId == variableToBeDeleted);
                }
                if (entry?.Record == null)
                {
                    // Only what AddLink created can be removed (§10.5.4).
                    return new RemoveLinkMethodStateResult
                    {
                        ServiceResult = ServiceResult.Create(
                            StatusCodes.BadInvalidArgument,
                            "'{0}' is no link a user added to the asset.",
                            variableToBeDeleted)
                    };
                }
                if (!await TrySaveUserLinksAsync(null, entry.Record, cancellationToken).ConfigureAwait(false))
                {
                    return new RemoveLinkMethodStateResult { ServiceResult = StatusCodes.BadResourceUnavailable };
                }
                lock (m_lock)
                {
                    m_links.Remove(entry);
                }

                if (m_handle.Owner is AsyncCustomNodeManager owner)
                {
                    await owner.DeleteNodeAsync(owner.SystemContext, variableToBeDeleted, cancellationToken)
                        .ConfigureAwait(false);
                }
                m_addIn.RemoveChild(entry.Variable);
                m_logger.UserLinkRemoved(entry.Variable.BrowseName, m_productInstanceUri);
            }
            finally
            {
                m_mutation.Release();
            }
            await NotifyChangedAsync(cancellationToken).ConfigureAwait(false);
            return new RemoveLinkMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        /// <summary>
        /// Persists the links users added, with one added or removed.
        /// </summary>
        private async ValueTask<bool> TrySaveUserLinksAsync(
            UserLinkRecord? adding,
            UserLinkRecord? removing,
            CancellationToken cancellationToken)
        {
            var records = new List<UserLinkRecord>();
            lock (m_lock)
            {
                foreach (LinkEntry entry in m_links)
                {
                    if (entry.Record != null && !ReferenceEquals(entry.Record, removing))
                    {
                        records.Add(entry.Record);
                    }
                }
            }
            if (adding != null)
            {
                records.Add(adding);
            }
            try
            {
                string json = JsonSerializer.Serialize(records, UserLinkJsonContext.Default.ListUserLinkRecord);
                await m_store
                    .SetValueAsync(
                        m_productInstanceUri,
                        AssetConfigurationNames.DocumentationLinks,
                        json,
                        cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger.UserLinksNotPersisted(ex, m_productInstanceUri);
                return false;
            }
        }

        private bool Authorize(ISystemContext context)
        {
            IUserIdentity? identity = (context as ISessionSystemContext)?.UserIdentity;
            Func<IUserIdentity?, bool>? authorize = m_options.AuthorizeLinkEdit;
            return authorize != null
                ? authorize(identity)
                : identity != null && identity.TokenType != UserTokenType.Anonymous;
        }

        private static AddLinkMethodStateResult Invalid(string format, object argument)
        {
            return new AddLinkMethodStateResult
            {
                ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidArgument, format, argument)
            };
        }

        private static bool TooLong(string? text, int maxLength)
        {
            return text != null && UnicodeText.Length(text) > maxLength;
        }

        private static string StoreNameOf(string linkName)
        {
            return AssetConfigurationNames.DocumentationLinks + "/" + linkName;
        }

        /// <summary>
        /// A link of the AddIn.
        /// </summary>
        /// <param name="Variable">The link variable.</param>
        /// <param name="IsEditable">Whether clients can write it.</param>
        /// <param name="StoreName">Where a declared editable link is persisted.</param>
        /// <param name="Record">The persisted form of a link a client added.</param>
        private sealed record LinkEntry(
            BaseDataVariableState<string> Variable,
            bool IsEditable,
            string? StoreName,
            UserLinkRecord? Record);

        /// <summary>
        /// The number of characters of a locale id kept at most.
        /// </summary>
        private const int MaxLocaleLength = 64;

        private readonly AssetHandle m_handle;
        private readonly ushort m_typeNamespaceIndex;
        private readonly DocumentationLinksState m_addIn;
        private readonly AmbServerOptions m_options;
        private readonly IAssetConfigurationStore m_store;
        private readonly string m_productInstanceUri;
        private readonly Func<CancellationToken, ValueTask> m_changedAsync;
        private readonly ILogger m_logger;
        private readonly Lock m_lock = new();
        private readonly SemaphoreSlim m_mutation = new(1, 1);
        private readonly List<LinkEntry> m_links = [];
    }

    internal static partial class AssetDocumentationLinksLog
    {
        [LoggerMessage(
            EventId = AmbServerEventIds.DocumentationLinks + 0,
            Level = LogLevel.Information,
            Message = "Documentation link {BrowseName} of asset {ProductInstanceUri} changed.")]
        public static partial void LinkChanged(
            this ILogger logger,
            QualifiedName browseName,
            string productInstanceUri);

        [LoggerMessage(
            EventId = AmbServerEventIds.DocumentationLinks + 1,
            Level = LogLevel.Information,
            Message = "Documentation link {BrowseName} added to asset {ProductInstanceUri}.")]
        public static partial void UserLinkAdded(
            this ILogger logger,
            QualifiedName browseName,
            string productInstanceUri);

        [LoggerMessage(
            EventId = AmbServerEventIds.DocumentationLinks + 2,
            Level = LogLevel.Information,
            Message = "Documentation link {BrowseName} removed from asset {ProductInstanceUri}.")]
        public static partial void UserLinkRemoved(
            this ILogger logger,
            QualifiedName browseName,
            string productInstanceUri);

        [LoggerMessage(
            EventId = AmbServerEventIds.DocumentationLinks + 3,
            Level = LogLevel.Error,
            Message = "Documentation link {BrowseName} of asset {ProductInstanceUri} could not be persisted.")]
        public static partial void LinkNotPersisted(
            this ILogger logger,
            Exception exception,
            QualifiedName browseName,
            string productInstanceUri);

        [LoggerMessage(
            EventId = AmbServerEventIds.DocumentationLinks + 4,
            Level = LogLevel.Error,
            Message = "The documentation links users added to asset {ProductInstanceUri} could not be persisted.")]
        public static partial void UserLinksNotPersisted(
            this ILogger logger,
            Exception exception,
            string productInstanceUri);

        [LoggerMessage(
            EventId = AmbServerEventIds.DocumentationLinks + 5,
            Level = LogLevel.Warning,
            Message = "The persisted documentation links of asset {ProductInstanceUri} are unreadable and ignored.")]
        public static partial void UserLinksUnreadable(
            this ILogger logger,
            Exception exception,
            string productInstanceUri);

        [LoggerMessage(
            EventId = AmbServerEventIds.DocumentationLinks + 6,
            Level = LogLevel.Warning,
            Message = "Documentation link {BrowseName} of asset {ProductInstanceUri} names a namespace the server " +
                "does not publish and is not restored.")]
        public static partial void UserLinkSkipped(this ILogger logger, string browseName, string productInstanceUri);

        [LoggerMessage(
            EventId = AmbServerEventIds.DocumentationLinks + 9,
            Level = LogLevel.Warning,
            Message = "The links of asset {ProductInstanceUri} changed, but publishing the conformance units again " +
                "failed.")]
        public static partial void LinkFollowUpFailed(
            this ILogger logger,
            Exception exception,
            string productInstanceUri);

        [LoggerMessage(
            EventId = AmbServerEventIds.DocumentationLinks + 7,
            Level = LogLevel.Warning,
            Message = "Documentation link {BrowseName} of asset {ProductInstanceUri} is longer than the limits " +
                "allow now and is cut to them.")]
        public static partial void LinkClamped(this ILogger logger, string browseName, string productInstanceUri);

        [LoggerMessage(
            EventId = AmbServerEventIds.DocumentationLinks + 8,
            Level = LogLevel.Warning,
            Message = "Documentation link {BrowseName} of asset {ProductInstanceUri} is not restored: the asset may " +
                "keep {MaxUserLinks} links of its users.")]
        public static partial void UserLinkOverLimit(
            this ILogger logger,
            string browseName,
            string productInstanceUri,
            int maxUserLinks);
    }
}
