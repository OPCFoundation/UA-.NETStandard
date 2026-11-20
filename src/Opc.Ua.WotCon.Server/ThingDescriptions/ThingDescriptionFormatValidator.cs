// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Text.Json;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Server.ThingDescriptions
{
    /// <summary>
    /// The <c>Wot-Con 1.02</c> format validation of <i>OPC UA — WoT
    /// Connectivity</i> §14, held in one place so that every path which
    /// materializes nodes from an untrusted Thing Description applies the
    /// same rule.
    /// </summary>
    /// <remarks>
    /// Two paths reach it. A document uploaded through the <c>WoTFile</c>
    /// flow arrives as bytes and is checked as JSON. A Thing Description
    /// auto-generated from a caller-chosen endpoint by
    /// <c>CreateAssetForEndpoint</c> arrives already deserialized: §11
    /// requires it to be treated as untrusted input all the same, because
    /// the endpoint it was built from is a caller-chosen outbound target
    /// and the provider that built it is pluggable.
    /// <para>
    /// Deserializing into the <see cref="ThingDescription"/> shape is not
    /// itself the check: every member of that type is optional, so an
    /// empty object deserializes happily. The rule is that the document
    /// must identify itself. This surface keys an asset on <c>name</c>,
    /// and W3C WoT TD 1.1 §5.3.1 makes <c>title</c> mandatory, so either
    /// one identifies it.
    /// </para>
    /// </remarks>
    internal static class ThingDescriptionFormatValidator
    {
        /// <summary>
        /// Returns <c>true</c> when the deserialized document carries an
        /// identifying member and is not an unresolved projection plan.
        /// </summary>
        /// <param name="thingDescription">
        /// The document to check. A <c>null</c> reference never identifies
        /// itself and is rejected.
        /// </param>
        public static bool HasIdentifyingMember(ThingDescription? thingDescription)
        {
            return thingDescription != null &&
                (!thingDescription.Type.HasValue || !WotProjection.IsProjectionType(thingDescription.Type.Value)) &&
                (!string.IsNullOrEmpty(thingDescription.Name) ||
                    !string.IsNullOrEmpty(thingDescription.Title));
        }

        /// <summary>
        /// Returns <c>true</c> when the parsed JSON document carries an
        /// identifying member and is not an unresolved projection plan.
        /// </summary>
        /// <param name="root">
        /// The root element of the parsed document.
        /// </param>
        public static bool HasIdentifyingMember(JsonElement root)
        {
            return root.ValueKind == JsonValueKind.Object &&
                (!root.TryGetProperty("@type", out JsonElement type) || !WotProjection.IsProjectionType(type)) &&
                (IsNonEmptyString(root, "name") || IsNonEmptyString(root, "title"));
        }

        private static bool IsNonEmptyString(JsonElement root, string member)
        {
            return root.TryGetProperty(member, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(value.GetString());
        }
    }
}
