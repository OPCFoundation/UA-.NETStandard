/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Projects bounded metadata without serializing document bodies, credentials, or runtime objects.
    /// </summary>
    internal static partial class WotDocumentJson
    {
        /// <summary>
        /// Creates a bounded diagnostic page, retaining the original total and error count.
        /// </summary>
        internal static JsonObject Diagnostics(IEnumerable<WotDiagnostic> diagnostics)
        {
            var items = new JsonArray();
            int total = 0;
            int errors = 0;
            foreach (WotDiagnostic diagnostic in diagnostics)
            {
                total++;
                if (diagnostic.Severity == WotDiagnosticSeverity.Error)
                {
                    errors++;
                }
                if (items.Count < MaxItems)
                {
                    items.Add(new JsonObject
                    {
                        ["severity"] = diagnostic.Severity.ToString(),
                        ["code"] = diagnostic.Code.ToString(),
                        ["message"] = Text(diagnostic.Message),
                        ["jsonPointer"] = Text(diagnostic.Location?.JsonPointer),
                        ["nodeId"] = Text(diagnostic.Location?.NodeId),
                        ["reference"] = Reference(diagnostic.Location?.Reference)
                    });
                }
            }
            return new JsonObject
            {
                ["items"] = items,
                ["total"] = total,
                ["errors"] = errors,
                ["truncated"] = total > items.Count
            };
        }

        /// <summary>
        /// Projects binding diagnostics using the same error and size conventions as conversion diagnostics.
        /// </summary>
        internal static JsonObject BindingDiagnostics(IEnumerable<WotBindingDiagnostic> diagnostics)
        {
            var items = new JsonArray();
            int total = 0;
            int errors = 0;
            foreach (WotBindingDiagnostic diagnostic in diagnostics)
            {
                total++;
                if (diagnostic.IsError)
                {
                    errors++;
                }
                if (items.Count < MaxItems)
                {
                    items.Add(new JsonObject
                    {
                        ["severity"] = diagnostic.Severity.ToString(),
                        ["code"] = diagnostic.Code.ToString(),
                        ["message"] = Text(diagnostic.Message),
                        ["jsonPointer"] = Text(diagnostic.JsonPointer),
                        ["term"] = Text(diagnostic.Term)
                    });
                }
            }
            return new JsonObject
            {
                ["items"] = items,
                ["total"] = total,
                ["errors"] = errors,
                ["truncated"] = total > items.Count
            };
        }

        /// <summary>
        /// Bounds metadata strings and removes URL query, fragment and user information.
        /// </summary>
        internal static string? Text(string? value)
        {
            if (value is null)
            {
                return null;
            }
            string bounded = value.Length > MaxText ? value[..MaxText] : value;
            string safe = AddressPattern().Replace(bounded, static match =>
                WotDependencyHttp.SafeReference(match.Value));
            return value.Length > MaxText ? safe + "\u2026" : safe;
        }

        /// <summary>
        /// Redacts and bounds an address without returning an inline document or secret-bearing URI suffix.
        /// </summary>
        internal static string? Reference(string? value)
        {
            return value is null ? null : Text(WotDependencyHttp.SafeReference(value));
        }

        /// <summary>
        /// Matches URI text in converter and planner messages using AOT-safe generated regex code.
        /// </summary>
        [GeneratedRegex("""[a-zA-Z][a-zA-Z0-9+.-]*://[^\s"'<>]+""", RegexOptions.CultureInvariant)]
        private static partial Regex AddressPattern();

        /// <summary>The maximum returned metadata entries per section.</summary>
        internal const int MaxItems = 100;

        /// <summary>The maximum returned characters in one untrusted metadata value.</summary>
        private const int MaxText = 1024;
    }
}
