/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Text.Json;

namespace Opc.Ua.WotCon.Server.ThingDescriptions
{
    /// <summary>
    /// The <c>WOTC-Legacy</c> format validation of <i>OPC UA — WoT
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
        /// identifying member.
        /// </summary>
        /// <param name="thingDescription">
        /// The document to check. A <c>null</c> reference never identifies
        /// itself and is rejected.
        /// </param>
        public static bool HasIdentifyingMember(ThingDescription? thingDescription)
        {
            return thingDescription != null &&
                (!string.IsNullOrEmpty(thingDescription.Name) ||
                    !string.IsNullOrEmpty(thingDescription.Title));
        }

        /// <summary>
        /// Returns <c>true</c> when the parsed JSON document carries an
        /// identifying member.
        /// </summary>
        /// <param name="root">
        /// The root element of the parsed document.
        /// </param>
        public static bool HasIdentifyingMember(JsonElement root)
        {
            return IsNonEmptyString(root, "name") || IsNonEmptyString(root, "title");
        }

        private static bool IsNonEmptyString(JsonElement root, string member)
        {
            return root.TryGetProperty(member, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(value.GetString());
        }
    }
}
