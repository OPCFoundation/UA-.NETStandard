// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Checks official projection metadata and plan shape without resolving sources
    /// or claiming ordinary TD/TM or native compatibility validation.
    /// </summary>
    internal static class WotProjectionAdmission
    {
        /// <summary>
        /// Gets the admission error for a projection document, or null when it is admissible or not a projection.
        /// </summary>
        /// <param name="document">The WoT document.</param>
        /// <param name="kind">The wo t document kind enum.</param>
        /// <param name="format">The stored format.</param>
        /// <param name="contentType">The content type.</param>
        public static string? GetError(
            WotDocument document,
            WoTDocumentKindEnum kind,
            string format,
            string contentType)
        {
            bool projection = WotProjection.IsProjection(document);
            bool hasProjectionManifest = document.TryGetUav("projects", out _);
            if (!projection && !hasProjectionManifest)
            {
                return null;
            }

            if (!projection)
            {
                return "A projection manifest requires the uav:projection role in @type.";
            }

            string expectedFormat = kind switch
            {
                WoTDocumentKindEnum.ThingDescription => "WoT-TD/1.1",
                WoTDocumentKindEnum.ThingModel => "WoT-TM/1.1",
                _ => string.Empty
            };
            string expectedContentType = kind switch
            {
                WoTDocumentKindEnum.ThingDescription => "application/td+json",
                WoTDocumentKindEnum.ThingModel => "application/tm+json",
                _ => string.Empty
            };
            if (!string.Equals(format, expectedFormat, StringComparison.Ordinal) ||
                !string.Equals(contentType, expectedContentType, StringComparison.Ordinal))
            {
                return $"A projection plan is stored as its ordinary TD/TM format and media type: " +
                    $"'{expectedFormat}' and '{expectedContentType}'.";
            }

            var diagnostics = new List<WotDiagnostic>();
            var parsed = WotProjection.Parse(document, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error))
            {
                return string.Join("; ", diagnostics);
            }
            WotDocumentKind expected = kind switch
            {
                WoTDocumentKindEnum.ThingDescription => WotDocumentKind.ThingDescription,
                WoTDocumentKindEnum.ThingModel => WotDocumentKind.ThingModel,
                _ => WotDocumentKind.Unknown
            };
            if (parsed is null || expected == WotDocumentKind.Unknown || parsed.ResultKind != expected)
            {
                return "The projection result kind must match the resource's stored TD/TM kind.";
            }
            return null;
        }
    }
}
