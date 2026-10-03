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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AMB.Client
{
    public sealed partial class AmbClient
    {
        /// <summary>
        /// Resolves the <c>DocumentationLinks</c> AddIn of an asset
        /// (OPC 10000-110 §10.5).
        /// </summary>
        /// <param name="asset">The asset.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The AddIn, or <see cref="NodeId.Null"/> when the asset has none.</returns>
        public ValueTask<NodeId> ResolveDocumentationLinksAsync(
            NodeId asset,
            CancellationToken cancellationToken = default)
        {
            return ResolveAsync(asset, cancellationToken, Amb(BrowseNames.DocumentationLinks));
        }

        /// <summary>
        /// Reads the links of the <c>DocumentationLinks</c> AddIn of an asset.
        /// </summary>
        /// <param name="asset">The asset.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The links; empty when the asset has no AddIn.</returns>
        public async ValueTask<ArrayOf<DocumentationLinkRecord>> ReadDocumentationLinksAsync(
            NodeId asset,
            CancellationToken cancellationToken = default)
        {
            NodeId addIn = await ResolveDocumentationLinksAsync(asset, cancellationToken).ConfigureAwait(false);
            List<ReferenceDescription> variables = await BrowseAsync(
                addIn,
                Ua.ReferenceTypeIds.HasComponent,
                BrowseDirection.Forward,
                cancellationToken,
                NodeClass.Variable).ConfigureAwait(false);
            var nodes = new NodeId[variables.Count];
            for (int ii = 0; ii < nodes.Length; ii++)
            {
                nodes[ii] = ToNodeId(variables[ii].NodeId);
            }
            DataValue[] values = await ReadAsync(nodes, Attributes.Value, cancellationToken).ConfigureAwait(false);
            DataValue[] access = await ReadAsync(nodes, Attributes.UserAccessLevel, cancellationToken)
                .ConfigureAwait(false);

            var links = new DocumentationLinkRecord[nodes.Length];
            for (int ii = 0; ii < links.Length; ii++)
            {
                byte userAccessLevel = access[ii].WrappedValue.TryGetValue(out byte level) ? level : (byte)0;
                links[ii] = new DocumentationLinkRecord(
                    nodes[ii],
                    variables[ii].BrowseName,
                    variables[ii].DisplayName,
                    StringOf(values[ii].WrappedValue) ?? string.Empty,
                    (userAccessLevel & AccessLevels.CurrentWrite) != 0);
            }
            return links.ToArrayOf();
        }

        /// <summary>
        /// Adds a link of the user to an asset with <c>AddLink</c>
        /// (§10.5.3); the server keeps it across restarts.
        /// </summary>
        /// <param name="asset">The asset.</param>
        /// <param name="uri">The link.</param>
        /// <param name="browseName">The browse name of the new link variable.</param>
        /// <param name="displayName">Its display name.</param>
        /// <param name="description">Its description.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The new link variable.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotSupported"/> when the asset has no
        /// AddIn, or what the server answered, for example
        /// <see cref="StatusCodes.BadUserAccessDenied"/>.
        /// </exception>
        public async ValueTask<NodeId> AddDocumentationLinkAsync(
            NodeId asset,
            string uri,
            QualifiedName browseName,
            LocalizedText displayName = default,
            LocalizedText description = default,
            CancellationToken cancellationToken = default)
        {
            DocumentationLinksTypeClient addIn = await AddInAsync(asset, cancellationToken).ConfigureAwait(false);
            return await addIn
                .AddLinkAsync(uri, browseName, displayName, description, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Removes a link a user added with <c>RemoveLink</c> (§10.5.4).
        /// </summary>
        /// <param name="asset">The asset.</param>
        /// <param name="link">The link variable.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotSupported"/> when the asset has no
        /// AddIn, or what the server answered.
        /// </exception>
        public async ValueTask RemoveDocumentationLinkAsync(
            NodeId asset,
            NodeId link,
            CancellationToken cancellationToken = default)
        {
            DocumentationLinksTypeClient addIn = await AddInAsync(asset, cancellationToken).ConfigureAwait(false);
            await addIn.RemoveLinkAsync(link, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Changes a link that users may edit.
        /// </summary>
        /// <param name="link">The link variable.</param>
        /// <param name="uri">The new link.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ServiceResultException">The status the server answered the write with.</exception>
        public ValueTask WriteDocumentationLinkAsync(
            NodeId link,
            string uri,
            CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(link, Variant.From(uri), "No link given.", cancellationToken);
        }

        private async ValueTask<DocumentationLinksTypeClient> AddInAsync(
            NodeId asset,
            CancellationToken cancellationToken)
        {
            NodeId addIn = await ResolveDocumentationLinksAsync(asset, cancellationToken).ConfigureAwait(false);
            if (addIn.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "Asset '{0}' has no DocumentationLinks.",
                    asset);
            }
            return new DocumentationLinksTypeClient(Session, addIn, Telemetry);
        }
    }
}
