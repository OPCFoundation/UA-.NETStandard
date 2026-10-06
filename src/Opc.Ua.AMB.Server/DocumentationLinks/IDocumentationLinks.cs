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

namespace Opc.Ua.AMB.Server.DocumentationLinks
{
    /// <summary>
    /// The <c>DocumentationLinks</c> AddIn of a registered asset
    /// (OPC 10000-110 §10.5): links to documentation that is managed outside
    /// the server.
    /// </summary>
    public interface IDocumentationLinks
    {
        /// <summary>
        /// Gets the NodeId of the AddIn.
        /// </summary>
        NodeId NodeId { get; }

        /// <summary>
        /// Gets whether clients can add and remove links through
        /// <c>AddLink</c> and <c>RemoveLink</c>.
        /// </summary>
        bool AllowsUserLinks { get; }

        /// <summary>
        /// Gets the current links: those of the manufacturer first, then the
        /// ones clients added.
        /// </summary>
        ArrayOf<DocumentationLink> Links { get; }
    }

    /// <summary>
    /// A link of the <c>DocumentationLinks</c> AddIn.
    /// </summary>
    /// <param name="NodeId">The NodeId of the link variable.</param>
    /// <param name="BrowseName">The browse name of the link variable.</param>
    /// <param name="Uri">The link.</param>
    /// <param name="IsEditable">Whether clients can write the link.</param>
    /// <param name="IsUserLink">Whether a client added the link through <c>AddLink</c>.</param>
    public sealed record DocumentationLink(
        NodeId NodeId,
        QualifiedName BrowseName,
        string Uri,
        bool IsEditable,
        bool IsUserLink);

    /// <summary>
    /// Configures the <c>DocumentationLinks</c> AddIn of an asset while it is
    /// registered.
    /// </summary>
    public interface IDocumentationLinksBuilder
    {
        /// <summary>
        /// Adds a link the manufacturer provides; clients can read it only.
        /// </summary>
        /// <param name="name">
        /// The browse name of the link variable, in the namespace of the asset.
        /// </param>
        /// <param name="uri">The link, typically a URL.</param>
        /// <param name="description">The description of the link.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="System.ArgumentException">
        /// <paramref name="name"/> or <paramref name="uri"/> is empty, or
        /// <paramref name="name"/> is used already.
        /// </exception>
        IDocumentationLinksBuilder Add(string name, string uri, LocalizedText description = default);

        /// <summary>
        /// Adds a link that clients can change to a link of their own; what
        /// they write is persisted ("AMB DocumentationLinks Edit Base").
        /// </summary>
        /// <param name="name">
        /// The browse name of the link variable, in the namespace of the asset.
        /// </param>
        /// <param name="defaultUri">The link while no client has written one.</param>
        /// <param name="description">The description of the link.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="System.ArgumentException">
        /// <paramref name="name"/> is empty or used already.
        /// </exception>
        IDocumentationLinksBuilder AddEditable(
            string name,
            string? defaultUri = null,
            LocalizedText description = default);

        /// <summary>
        /// Lets clients add and remove links of their own through the
        /// <c>AddLink</c> and <c>RemoveLink</c> methods; the links survive a
        /// restart ("AMB DocumentationLinks Edit Advanced").
        /// </summary>
        /// <returns>The same builder, for chaining.</returns>
        IDocumentationLinksBuilder AllowUserLinks();
    }
}
