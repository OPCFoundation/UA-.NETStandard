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
using System.Text.Json;
using Opc.Ua.Types;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// One namespace-URI-qualified step of an OPC UA relative path, without Session-local indexes.
    /// </summary>
    /// <param name="TargetName">The target BrowseName.</param>
    /// <param name="ReferenceKind">The native reference selector.</param>
    /// <param name="ReferenceName">The named reference type, when the selector names one.</param>
    /// <param name="IncludeSubtypes">Whether reference subtypes are included.</param>
    public readonly record struct WotBrowsePathStep(
        WotBrowsePathElement TargetName,
        RelativePathFormatter.ElementType ReferenceKind,
        WotBrowsePathElement ReferenceName,
        bool IncludeSubtypes);

    /// <summary>
    /// Immutable source addressing captured while a form still belongs to its original JSON-LD document.
    /// </summary>
    public sealed class WotBrowsePathTarget
    {
        private WotBrowsePathTarget(
            string path, string anchorId, string jsonPointer, ArrayOf<WotBrowsePathStep> elements)
        {
            Path = path;
            AnchorId = anchorId;
            JsonPointer = jsonPointer;
            Elements = elements;
        }

        /// <summary>
        /// Gets the authored path, including its absolute or relative spelling.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the portable starting NodeId; absolute paths use the AddressSpace Root.
        /// </summary>
        public string AnchorId { get; }

        /// <summary>
        /// Gets the pointer of the carrying browse-path term, which may belong to an enclosing scope.
        /// </summary>
        public string JsonPointer { get; }

        /// <summary>
        /// Gets the ordered portable native path steps.
        /// </summary>
        public ArrayOf<WotBrowsePathStep> Elements { get; }

        /// <summary>
        /// Captures a form's inherited path and anchor using the carrying object's effective context.
        /// Returns null only when no scope declares a path; malformed declarations throw.
        /// </summary>
        /// <param name="document">The original owning document.</param>
        /// <param name="formPointer">The pointer of a form in properties, actions, events, or root forms.</param>
        /// <param name="maxPathLength">The bound applied before parsing the path.</param>
        /// <param name="maxElements">The maximum number of native path steps.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A bound is non-positive.</exception>
        /// <exception cref="ServiceResultException">The path, anchor, context, or form pointer is invalid.</exception>
        public static WotBrowsePathTarget? FromForm(
            WotDocument document,
            string formPointer,
            int maxPathLength = 65536,
            int maxElements = 1024)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            if (formPointer is null)
            {
                throw new ArgumentNullException(nameof(formPointer));
            }
            if (maxPathLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPathLength));
            }
            if (maxElements <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxElements));
            }
            string[] tokens = formPointer.Split('/');
            bool rootForm = tokens.Length == 3 && tokens[0].Length == 0 && tokens[1] == "forms";
            bool affordanceForm = tokens.Length == 5 &&
                tokens[0].Length == 0 &&
                tokens[1] is "properties" or "actions" or "events" &&
                tokens[3] == "forms";
            if ((!rootForm && !affordanceForm) ||
                !document.TryEvaluatePointer(formPointer, out JsonElement form) ||
                form.ValueKind != JsonValueKind.Object)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument, "The pointer must identify a WoT form.");
            }
            WotAnchorScope scope = WotAnchorScope.None;
            string? path = null;
            string? anchorError = null;
            string? identityError = null;
            bool explicitAnchor = false;
            string pathPointer = string.Empty;
            JsonElement carryingNode = default;
            Enter(document.RootElement, string.Empty);
            if (affordanceForm)
            {
                string affordancePointer = "/" + tokens[1] + "/" + tokens[2];
                if (!document.TryEvaluatePointer(affordancePointer, out JsonElement affordance))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument, "The affordance cannot be resolved.");
                }
                Enter(affordance, affordancePointer);
            }
            Enter(form, formPointer);
            if (path is null)
            {
                return null;
            }
            if (path.Length > maxPathLength)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "The browse path exceeds the character limit.");
            }
            string? anchor = path[0] == '/' ? "i=84" : scope.Effective;
            if (path[0] != '/' && (explicitAnchor ? anchorError : identityError) is { } error)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, error);
            }
            if (!WotPortableIdentity.IsPortableNodeId(anchor))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid,
                    "A relative browse path requires a portable enclosing anchor or identity.");
            }
            var namespaces = new NamespaceTable();
            var parsed = RelativePathFormatter.ParsePortable(
                path, namespaces,
                prefix => document.TryGetContextPrefix(prefix, out string uri, carryingNode) ? uri : null,
                maxElements);
            if (parsed.Elements.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadBrowseNameInvalid, "A browse path requires a target name.");
            }
            var elements = new WotBrowsePathStep[parsed.Elements.Count];
            for (int index = 0; index < elements.Length; index++)
            {
                RelativePathFormatter.Element element = parsed.Elements[index];
                if (element.TargetName.IsNull || string.IsNullOrEmpty(element.TargetName.Name))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadBrowseNameInvalid,
                        "Every browse-path step requires a complete target BrowseName.");
                }
                elements[index] = new WotBrowsePathStep(
                    Portable(element.TargetName),
                    element.ElementType,
                    element.ReferenceTypeName.IsNull ? default : Portable(element.ReferenceTypeName),
                    element.IncludeSubtypes);
            }
            return new WotBrowsePathTarget(path, anchor!, pathPointer, elements);

            void Enter(JsonElement node, string pointer)
            {
                scope = scope.Enter(node);
                if (node.TryGetProperty(WotAnchorScope.AnchorTerm, out JsonElement anchorValue))
                {
                    explicitAnchor = true;
                    anchorError = anchorValue.ValueKind == JsonValueKind.String &&
                        WotPortableIdentity.IsPortableNodeId(anchorValue.GetString())
                        ? null
                        : "An explicitly declared browse-path anchor must be a portable NodeId.";
                }
                if (node.TryGetProperty(WotAnchorScope.IdentityTerm, out JsonElement identity))
                {
                    identityError = identity.ValueKind == JsonValueKind.String &&
                        WotPortableIdentity.IsPortableNodeId(identity.GetString())
                        ? null
                        : "An identity used as a browse-path anchor must be a portable NodeId.";
                }
                if (node.TryGetProperty(PathTerm, out JsonElement value))
                {
                    if (value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadBrowseNameInvalid, "A declared browse path must be a non-empty string.");
                    }
                    path = value.GetString();
                    carryingNode = node;
                    pathPointer = pointer + "/" + PathTerm;
                }
            }

            WotBrowsePathElement Portable(QualifiedName name) =>
                new(namespaces.GetString(name.NamespaceIndex), name.Name!);
        }

        /// <summary>The form and affordance browse-path term.</summary>
        public const string PathTerm = "uav:browsePath";
    }
}
