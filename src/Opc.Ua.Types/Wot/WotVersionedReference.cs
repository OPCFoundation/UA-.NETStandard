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
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="WotVersionException"></exception>
        public static string? GetRequiredVersion(WotDocument document, JsonElement carryingNode, string kind)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            if (TryReadTerm(document, carryingNode, "uav:publicationMember", out _))
            {
                throw new WotVersionException("not-supported");
            }
            string expectedFloorTerm = IsTdReference(kind) ? "td:minVersion" : "tm:minVersion";
            string wrongFloorTerm = expectedFloorTerm == "td:minVersion" ? "tm:minVersion" : "td:minVersion";
            if (TryReadTerm(document, carryingNode, "uav:compatibilityLine", out _))
            {
                throw new WotVersionException("invalid-line");
            }
            if (TryReadTerm(document, carryingNode, wrongFloorTerm, out _))
            {
                throw new WotVersionException("invalid-floor");
            }
            string? floor = ReadRequiredFloor(document, carryingNode, expectedFloorTerm);
            if (floor is not null)
            {
                WotVersionResolver.ValidateFloor(floor);
            }
            if (IsVersionPin(ReadString(carryingNode, "href") ?? ReadString(carryingNode, "tm:ref")))
            {
                return null;
            }
            if (kind == "type" && document.Kind == WotDocumentKind.ThingDescription)
            {
                JsonElement[] links = [.. document.Links.Where(link => ReadString(link, "rel") == "type")];
                string? model = document.RootElement.TryGetProperty("version", out JsonElement version)
                    ? ReadString(version, "model") : null;
                if (model is not null &&
                    links.Length > 1 &&
                    links.Any(link => !TryReadTerm(document, link, "tm:minVersion", out _) &&
                        !TryReadTerm(document, link, "uav:minVersion", out _)))
                {
                    throw new WotVersionException("ambiguous-floor");
                }
                foreach (JsonElement link in links)
                {
                    if (TryReadTerm(document, link, "td:minVersion", out _))
                    {
                        throw new WotVersionException("invalid-floor");
                    }
                    string? explicitFloor = ReadRequiredFloor(document, link, "tm:minVersion");
                    if (explicitFloor is not null)
                    {
                        WotVersionResolver.ValidateFloor(explicitFloor);
                    }
                }
                if (model is not null)
                {
                    WotVersionResolver.ValidateFloor(model);
                    if (floor is null ||
                        WotVersionResolver.ParseFloor(model).CompareTo(WotVersionResolver.ParseFloor(floor)) > 0)
                    {
                        floor = model;
                    }
                }
            }
            return floor;
        }

        private static bool IsTdReference(string kind)
        {
            return kind is "td-extends" or "td-subthing" or "td-ref" or
                "td:extends" or "td:subthing" or "td:ref";
        }

        /// <summary>
        /// Resolves through the additive contract, or verifies a legacy provider's selected document.
        /// </summary>
        public static ValueTask<WotResolverResult> ResolveAsync(
            IWotThingResolver resolver,
            WotReferenceRequest reference,
            WotResolutionContext context,
            CancellationToken cancellationToken)
        {
            return ResolveAsync(resolver, reference, context, null, cancellationToken);
        }

        /// <summary>
        /// Uses the containing document for local references and the provider for external references.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static async ValueTask<WotResolverResult> ResolveAsync(
            IWotThingResolver resolver,
            WotReferenceRequest reference,
            WotResolutionContext context,
            WotDocument? containingDocument,
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
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.Local ||
                reference.Target.StartsWith('#') ||
                (containingDocument is not null && IsDocumentLocalReference(containingDocument, reference.Target)))
            {
                if (containingDocument is null)
                {
                    throw new ArgumentException(
                        "A local reference requires its containing document.", nameof(containingDocument));
                }
                ValidateSelectedVersion(containingDocument, reference.MinimumVersion, reference.Kind);
                return WotResolverResult.FromBytes(containingDocument.Utf8Json.ToArray());
            }
            if (resolver is IWotVersionedThingResolver versioned)
            {
                return await versioned.ResolveThingAsync(reference, context, cancellationToken).ConfigureAwait(false);
            }
            WotResolverResult result = await resolver.ResolveThingAsync(
                reference.Target, context, cancellationToken).ConfigureAwait(false);
            if (result.Found && reference.MinimumVersion is not null)
            {
                using var selected = WotDocument.Parse(result.Content);
                ValidateSelectedVersion(selected, reference.MinimumVersion, reference.Kind);
            }
            return result;
        }

        /// <summary>
        /// Verifies a selected document when a provider has no version-selection interface.
        /// </summary>
        internal static void ValidateSelectedVersion(
            WotDocument selected, string? requiredVersion, string kind = "ref")
        {
            if (requiredVersion is null)
            {
                return;
            }
            string? authored = selected.RootElement.TryGetProperty("version", out JsonElement version)
                ? ReadString(version, IsTdReference(kind) ? "instance" : "model") : null;
            WotVersionResolver.ValidateDocumentVersion(authored, requiredVersion, kind);
        }

        /// <summary>
        /// Gets whether a fragment addresses the containing document by relative or absolute identity.
        /// </summary>
        internal static bool IsDocumentLocalReference(WotDocument document, string reference, string? origin = null)
        {
            int fragment = reference.IndexOf('#', StringComparison.Ordinal);
            return fragment >= 0 &&
                (fragment == 0 ||
                    string.Equals(reference[..fragment],
                        document.Id ?? ReadString(document.RootElement, "@id"), StringComparison.Ordinal) ||
                    (origin is not null && string.Equals(reference[..fragment], origin, StringComparison.Ordinal)));
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

        private static string? ReadRequiredFloor(WotDocument document, JsonElement node, string legacyTerm)
        {
            bool legacy = TryReadTerm(document, node, legacyTerm, out JsonElement legacyFloor);
            bool common = TryReadTerm(document, node, "uav:minVersion", out JsonElement commonFloor);
            if (legacy && common)
            {
                throw new WotVersionException("invalid-floor");
            }
            if (!legacy && !common)
            {
                return null;
            }
            JsonElement floor = common ? commonFloor : legacyFloor;
            return floor.ValueKind == JsonValueKind.String ? floor.GetString() :
                throw new WotVersionException("invalid-version");
        }

        /// <summary>
        /// Reads a dependency term in the carrying node's active JSON-LD context.
        /// </summary>
        /// <exception cref="WotVersionException"></exception>
        private static bool TryReadTerm(
            WotDocument document, JsonElement node, string term, out JsonElement value)
        {
            value = default;
            if (node.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            string identity = term.StartsWith("uav:", StringComparison.Ordinal)
                ? "http://opcfoundation.org/UA/WoT-Binding/" + term[4..]
                : "https://www.w3.org/2019/wot/" + term[..2] + "#" + term[3..];
            bool found = false;
            foreach (JsonProperty property in node.EnumerateObject())
            {
                if (property.Name != term &&
                    (!WotProjectionResolver.TryExpandSemanticIdentity(
                        property.Name, document, node, string.Empty, true, out string expanded) ||
                        !string.Equals(expanded, identity, StringComparison.Ordinal)))
                {
                    continue;
                }
                if (found)
                {
                    throw new WotVersionException("invalid-floor");
                }
                found = true;
                value = property.Value;
            }
            return found;
        }

        private static string? ReadString(JsonElement node, string name)
        {
            return node.ValueKind == JsonValueKind.Object &&
                node.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
    }
}
