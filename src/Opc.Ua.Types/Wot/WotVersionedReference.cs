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
 *
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
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// An additive resolver contract for stores that retain multiple document versions.
    /// </summary>
    public interface IWotVersionedThingResolver : IWotThingResolver
    {
        /// <summary>
        /// Resolves an authored reference without discarding its minimum-version constraint.
        /// </summary>
        ValueTask<WotResolverResult> ResolveThingAsync(
            WotReferenceRequest reference,
            WotResolutionContext context,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Extracts authored floors and dispatches version-aware document acquisition.
    /// </summary>
    public static class WotVersionedReference
    {
        /// <summary>
        /// Gets a link or tm:ref floor, including the implicit TD type-link floor.
        /// </summary>
        public static string? GetRequiredVersion(WotDocument document, JsonElement carryingNode, string kind)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            // A reference that names one stored Version by its registry path is a pin, and a
            // pin bypasses floor derivation (xRegistry WoT 5.4.2 step 2): neither its own
            // floor nor the document's version.model is read, so neither can fail it.
            if (IsVersionPin(ReadString(carryingNode, "href") ?? ReadString(carryingNode, "tm:ref")))
            {
                return null;
            }
            string? floor = ReadFloor(carryingNode);
            if (kind == "type" && document.Kind == WotDocumentKind.ThingDescription)
            {
                JsonElement[] links = document.Links.Where(link => ReadString(link, "rel") == "type").ToArray();
                string? model = document.RootElement.TryGetProperty("version", out JsonElement version)
                    ? ReadString(version, "model") : null;
                if (model is not null && links.Length > 1 &&
                    links.Any(link => !link.TryGetProperty("tm:minVersion", out _)))
                {
                    throw new WotVersionException("ambiguous-floor");
                }
                foreach (JsonElement link in links)
                {
                    string? explicitFloor = ReadFloor(link);
                    if (explicitFloor is not null)
                    {
                        WotVersionResolver.ValidateFloor(explicitFloor);
                    }
                }
                if (model is not null)
                {
                    WotVersionResolver.ValidateFloor(model);
                    if (floor is null || WotSemanticVersion.Parse(model).CompareTo(WotSemanticVersion.Parse(floor)) > 0)
                    {
                        floor = model;
                    }
                }
            }
            if (floor is not null)
            {
                WotVersionResolver.ValidateFloor(floor);
            }
            return floor;
        }

        /// <summary>
        /// Resolves through the additive contract, or verifies a legacy provider's selected document.
        /// </summary>
        public static async ValueTask<WotResolverResult> ResolveAsync(
            IWotThingResolver resolver,
            WotReferenceRequest reference,
            WotResolutionContext context,
            CancellationToken cancellationToken)
        {
            if (resolver is null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }
            if (reference is null)
            {
                throw new ArgumentNullException(nameof(reference));
            }
            if (resolver is IWotVersionedThingResolver versioned)
            {
                return await versioned.ResolveThingAsync(reference, context, cancellationToken).ConfigureAwait(false);
            }
            WotResolverResult result = await resolver.ResolveThingAsync(
                reference.Target, context, cancellationToken).ConfigureAwait(false);
            if (result.Found && reference.MinimumVersion is not null)
            {
                using WotDocument selected = WotDocument.Parse(result.Content);
                ValidateSelectedVersion(selected, reference.MinimumVersion);
            }
            return result;
        }

        /// <summary>
        /// Verifies a selected document when a provider has no version-selection interface.
        /// </summary>
        internal static void ValidateSelectedVersion(WotDocument selected, string? requiredVersion)
        {
            if (requiredVersion is null)
            {
                return;
            }
            WotVersionResolver.ValidateFloor(requiredVersion);
            string? model = selected.RootElement.TryGetProperty("version", out JsonElement version)
                ? ReadString(version, "model") : null;
            if (!WotSemanticVersion.TryParse(model, out WotSemanticVersion? parsed) ||
                parsed!.CompareTo(WotSemanticVersion.Parse(requiredVersion)) < 0)
            {
                throw new WotVersionException("unsatisfied");
            }
        }

        /// <summary>
        /// Gets whether a reference names one stored Version by its registry path, that is,
        /// whether its path ends in <c>/versions/</c> followed by a single segment.
        /// </summary>
        internal static bool IsVersionPin(string? reference)
        {
            if (string.IsNullOrEmpty(reference))
            {
                return false;
            }
            int fragment = reference!.IndexOf('#', StringComparison.Ordinal);
            string path = fragment < 0 ? reference : reference[..fragment];
            int marker = path.LastIndexOf("/versions/", StringComparison.Ordinal);
            if (marker < 0)
            {
                return false;
            }
            string versionId = path[(marker + "/versions/".Length)..];
            return versionId.Length > 0 && versionId.IndexOf('/', StringComparison.Ordinal) < 0;
        }

        private static string? ReadFloor(JsonElement node)
        {
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("tm:minVersion", out JsonElement floor))
            {
                return null;
            }
            return floor.ValueKind == JsonValueKind.String ? floor.GetString() :
                throw new WotVersionException("invalid-version");
        }

        private static string? ReadString(JsonElement node, string name) =>
            node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
