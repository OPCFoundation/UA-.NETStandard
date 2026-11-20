// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.AMB
{
    /// <summary>
    /// The metadata Properties the AMB server of this SDK adds to the links
    /// of a <c>DocumentationLinks</c> AddIn, as OPC 10000-110 §10.5.1 lets
    /// vendors do.
    /// </summary>
    /// <remarks>
    /// OPC 10000-110 defines no way to tell a link of the manufacturer from
    /// one a user added through <c>AddLink</c>, although only the latter can
    /// be removed with <c>RemoveLink</c> (§10.5.4). The names are not AMB
    /// names: the server qualifies them with its namespace of server-specific
    /// types, which an application may configure, so a client matches the
    /// name alone.
    /// </remarks>
    public static class DocumentationLinkProperties
    {
        /// <summary>
        /// The <c>Boolean</c> Property, <see langword="true"/>, a link a user
        /// added through <c>AddLink</c> carries, and only such a link. It is
        /// the link <c>RemoveLink</c> accepts.
        /// </summary>
        public const string UserLink = "UserLink";
    }
}
