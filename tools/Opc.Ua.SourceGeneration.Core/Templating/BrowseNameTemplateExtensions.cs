// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging;
using Opc.Ua.SourceGeneration;
using Opc.Ua.SourceGeneration.Templating;

namespace Opc.Ua
{
    /// <summary>
    /// Helpers for adding BrowseName replacements that need to be safe
    /// to interpolate into both C# identifier contexts (where the value
    /// is constrained upstream to a valid identifier) and C# string-
    /// literal contexts (where defence-in-depth escaping protects the
    /// consuming build from ill-formed design XML).
    /// </summary>
    internal static class BrowseNameTemplateExtensions
    {
        /// <summary>
        /// Reserved <c>EventId</c> value that the host source generator
        /// (Opc.Ua.SourceGeneration / Opc.Ua.SourceGeneration.Stack)
        /// maps to the <c>MODELGEN020</c> / <c>STACKGEN020</c>
        /// <c>UASG_BROWSENAME_UNSAFE</c> diagnostic descriptor.
        /// </summary>
        public const int BrowseNameUnsafeEventId = 20;

        /// <summary>
        /// Adds two replacements for a BrowseName: the raw value under
        /// <paramref name="rawToken"/> (suitable for identifier
        /// contexts) and the C#-string-literal-escaped value under
        /// <paramref name="literalToken"/> (suitable for
        /// <c>"…"</c> contexts in the generated code).
        /// Logs a single <c>BrowseNameUnsafe</c> warning when the value
        /// required escaping so an offending name surfaces in the
        /// consuming build's diagnostics.
        /// </summary>
        /// <param name="template">The template to populate.</param>
        /// <param name="rawToken">
        /// Token name for the raw value, e.g. <see cref="Tokens.BrowseName"/>.
        /// </param>
        /// <param name="literalToken">
        /// Token name for the literal-escaped value, e.g.
        /// <see cref="Tokens.BrowseNameLiteral"/>.
        /// </param>
        /// <param name="value">Raw BrowseName value from the design model.</param>
        /// <param name="logger">
        /// Optional logger to receive the <c>UASG_BROWSENAME_UNSAFE</c>
        /// warning. When <c>null</c> the escape still happens silently.
        /// </param>
        public static void AddBrowseNameReplacement(
            this Template template,
            string rawToken,
            string literalToken,
            string? value,
            ILogger? logger = null)
        {
            string escaped = StringLiteralEscaper.AsCSharpStringLiteralContent(
                value, out bool modified);
            template.AddReplacement(rawToken, value ?? string.Empty);
            template.AddReplacement(literalToken, escaped);
            if (modified)
            {
                logger?.LogWarning(
                    new EventId(BrowseNameUnsafeEventId, "BrowseNameUnsafe"),
                    "BrowseName '{Name}' contained characters that required " +
                    "escaping when emitted into a C# string literal.",
                    value);
            }
        }
    }
}
