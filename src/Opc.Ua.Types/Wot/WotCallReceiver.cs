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
using System.Text.Json;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Captures source-Thing Call receiver authority without contacting a Server.
    /// Live execution must additionally validate the receiver and Method relationship.
    /// </summary>
    public static class WotCallReceiver
    {
        /// <summary>
        /// Tries to capture an explicitly identified source Object at one OPC UA endpoint.
        /// Projection envelopes and relocated affordances cannot supply root authority.
        /// </summary>
        /// <param name="document">The original source Thing document.</param>
        /// <param name="affordance">The source action.</param>
        /// <param name="receiver">The portable receiver identity on success.</param>
        /// <param name="endpoint">The unambiguous source endpoint on success.</param>
        /// <param name="error">The reason source authority cannot be captured.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static bool TryCaptureSourceObject(
            WotDocument document,
            JsonElement affordance,
            out string receiver,
            out string endpoint,
            out string error)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            receiver = string.Empty;
            endpoint = string.Empty;
            error = "An omitted Call receiver requires an explicitly identified original source Thing Object.";
            if (document.TryGetSourceCallReceiver(affordance, out receiver, out endpoint))
            {
                error = string.Empty;
                return true;
            }
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.TryGetProperty("uav:scenario", out _) ||
                WotProjection.IsProjection(document) ||
                affordance.ValueKind != JsonValueKind.Object ||
                affordance.TryGetProperty("uav:resolvedFrom", out _) ||
                document.Kind == WotDocumentKind.ThingModel)
            {
                return false;
            }
            foreach (string token in document.TypeTokens)
            {
                if (token is "uav:variable" or "uav:method" or "uav:view" or
                    "uav:objectType" or "uav:variableType" or "uav:eventType" or
                    "uav:dataType" or "uav:referenceType")
                {
                    return false;
                }
            }
            if (!root.TryGetProperty("uav:id", out JsonElement identity) ||
                identity.ValueKind != JsonValueKind.String ||
                !WotPortableIdentity.IsPortableNodeId(identity.GetString()!) ||
                !ExpandedNodeId.TryParse(identity.GetString()!, out ExpandedNodeId parsed) ||
                parsed.IsNull)
            {
                return false;
            }
            var endpoints = new HashSet<string>(StringComparer.Ordinal);
            string? documentBase = ReadString(root, "base");
            if (TryGetEndpoint(documentBase, out string baseEndpoint))
            {
                endpoints.Add(baseEndpoint);
            }
            AddForms(root);
            foreach (string collection in s_collections)
            {
                if (root.TryGetProperty(collection, out JsonElement members) &&
                    members.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty member in members.EnumerateObject())
                    {
                        AddForms(member.Value);
                    }
                }
            }
            error = "The source Thing must identify exactly one OPC UA endpoint for an omitted Call receiver.";
            if (endpoints.Count != 1)
            {
                return false;
            }
            foreach (string value in endpoints)
            {
                endpoint = value;
            }
            receiver = identity.GetString()!;
            error = string.Empty;
            return true;

            void AddForms(JsonElement element)
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("forms", out JsonElement forms) ||
                    forms.ValueKind != JsonValueKind.Array)
                {
                    return;
                }
                foreach (JsonElement form in forms.EnumerateArray())
                {
                    string? href = ReadString(form, "href");
                    if (Uri.TryCreate(documentBase, UriKind.Absolute, out Uri? baseUri) &&
                        Uri.TryCreate(href, UriKind.RelativeOrAbsolute, out Uri? relative) &&
                        Uri.TryCreate(baseUri, relative, out Uri? absolute))
                    {
                        href = absolute.AbsoluteUri;
                    }
                    if (TryGetEndpoint(href, out string address))
                    {
                        endpoints.Add(address);
                    }
                }
            }
        }

        /// <summary>
        /// Extracts an OPC UA service endpoint, excluding the target NodeId query.
        /// </summary>
        /// <param name="address">The endpoint or form href.</param>
        /// <param name="endpoint">The normalized endpoint.</param>
        public static bool TryGetEndpoint(string? address, out string endpoint)
        {
            endpoint = string.Empty;
            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme is not ("opc.tcp" or "opc.https" or "opc.wss") ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                return false;
            }
            string serviceAddress;
            try
            {
                WotPortableIdentity.ReadUriTargetNodeId(uri.AbsoluteUri, out serviceAddress);
            }
            catch (FormatException)
            {
                return false;
            }
            if (!Uri.TryCreate(serviceAddress, UriKind.Absolute, out Uri? service) ||
                !string.IsNullOrEmpty(service.Query))
            {
                return false;
            }
            endpoint = service.AbsolutePath is "" or "/"
                ? service.GetLeftPart(UriPartial.Authority) : service.AbsoluteUri;
            return true;
        }

        /// <summary>
        /// Reads a string without coercing a missing or differently typed JSON term.
        /// </summary>
        private static string? ReadString(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        /// <summary>
        /// The source affordance collections that can declare endpoints.
        /// </summary>
        private static readonly string[] s_collections = ["properties", "actions", "events"];
    }
}
