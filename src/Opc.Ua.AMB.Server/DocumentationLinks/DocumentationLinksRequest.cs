// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Opc.Ua.AMB.Server.DocumentationLinks
{
    /// <summary>
    /// A link the application declared while registering the asset.
    /// </summary>
    /// <param name="Name">The browse name, in the namespace of the asset.</param>
    /// <param name="Uri">The link, or the default of an editable one.</param>
    /// <param name="Description">The description.</param>
    /// <param name="Editable">Whether clients can write the link.</param>
    internal sealed record DeclaredLink(string Name, string? Uri, LocalizedText Description, bool Editable);

    /// <summary>
    /// What an application asked for about the <c>DocumentationLinks</c>
    /// AddIn of an asset.
    /// </summary>
    internal sealed class DocumentationLinksRequest : IDocumentationLinksBuilder
    {
        /// <summary>
        /// Gets the links the application declared.
        /// </summary>
        public List<DeclaredLink> Links { get; } = [];

        /// <summary>
        /// Gets whether clients can add and remove links.
        /// </summary>
        public bool UserLinks { get; private set; }

        /// <inheritdoc/>
        public IDocumentationLinksBuilder Add(string name, string uri, LocalizedText description = default)
        {
            if (string.IsNullOrEmpty(uri))
            {
                throw new ArgumentException("A documentation link needs a URI.", nameof(uri));
            }
            Declare(new DeclaredLink(name, uri, description, Editable: false));
            return this;
        }

        /// <inheritdoc/>
        public IDocumentationLinksBuilder AddEditable(
            string name,
            string? defaultUri = null,
            LocalizedText description = default)
        {
            Declare(new DeclaredLink(name, defaultUri, description, Editable: true));
            return this;
        }

        /// <inheritdoc/>
        public IDocumentationLinksBuilder AllowUserLinks()
        {
            UserLinks = true;
            return this;
        }

        private void Declare(DeclaredLink link)
        {
            if (string.IsNullOrEmpty(link.Name))
            {
                throw new ArgumentException("A documentation link needs a name.", nameof(link));
            }
            foreach (DeclaredLink existing in Links)
            {
                if (string.Equals(existing.Name, link.Name, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"The asset already has a documentation link named '{link.Name}'.",
                        nameof(link));
                }
            }
            Links.Add(link);
        }
    }

    /// <summary>
    /// A link a client added through <c>AddLink</c>, as it is persisted.
    /// </summary>
    internal sealed class UserLinkRecord
    {
        /// <summary>Gets or sets the string identifier of the link variable.</summary>
        public string Identifier { get; set; } = string.Empty;

        /// <summary>Gets or sets the namespace of the link variable's NodeId.</summary>
        public string NodeNamespaceUri { get; set; } = string.Empty;

        /// <summary>Gets or sets the name of the browse name.</summary>
        public string BrowseName { get; set; } = string.Empty;

        /// <summary>Gets or sets the namespace of the browse name.</summary>
        public string BrowseNameNamespaceUri { get; set; } = string.Empty;

        /// <summary>Gets or sets the link.</summary>
        public string Uri { get; set; } = string.Empty;

        /// <summary>Gets or sets the text of the display name.</summary>
        public string? DisplayName { get; set; }

        /// <summary>Gets or sets the locale of the display name.</summary>
        public string? DisplayNameLocale { get; set; }

        /// <summary>Gets or sets the text of the description.</summary>
        public string? Description { get; set; }

        /// <summary>Gets or sets the locale of the description.</summary>
        public string? DescriptionLocale { get; set; }
    }

    /// <summary>
    /// The source-generated JSON contract of the persisted user links.
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(List<UserLinkRecord>))]
    internal sealed partial class UserLinkJsonContext : JsonSerializerContext;
}
