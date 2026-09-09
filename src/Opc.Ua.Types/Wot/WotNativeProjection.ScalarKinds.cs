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
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The scalar-kind contract of the <c>uav:nodes</c> record grammar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The projection is a schema-complete record of a NodeSet, and every
    /// member of it has exactly one JSON type. A reader that answers "absent"
    /// for a member that is present but of the wrong type turns a corrupt
    /// record into a quietly different NodeSet: <c>"nodeId": 42</c> becomes a
    /// Node with no NodeId, <c>"isAbstract": "true"</c> becomes a concrete
    /// type, and nothing in the restored model says either happened.
    /// </para>
    /// <para>
    /// So the kinds are checked before anything is read, once, against the
    /// grammar below, and every mismatch is reported at its exact RFC 6901
    /// pointer. A member this table does not name is not constrained here -
    /// the grammar is deliberately a list of what the projection writes rather
    /// than a closed world, so a record from a later revision is rejected by
    /// the reader that needs the member rather than by this pass.
    /// </para>
    /// </remarks>
    internal static partial class WotNativeProjection
    {
        /// <summary>
        /// Reports every member of the projection whose JSON type is not the
        /// one the record grammar gives it.
        /// </summary>
        /// <param name="projection">The <c>uav:nodes</c> object.</param>
        /// <param name="options">The bounds this walk observes.</param>
        /// <param name="diagnostics">The diagnostics sink.</param>
        /// <returns><c>true</c> when every named member has its declared type.</returns>
        internal static bool ValidateScalarKinds(
            JsonElement projection,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            int before = CountErrors(diagnostics);
            Walk(projection, "/uav:nodes", 0, options, diagnostics);
            return CountErrors(diagnostics) == before;
        }

        private static void Walk(
            JsonElement element,
            string pointer,
            int depth,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            if (depth > options.MaxJsonDepth)
            {
                // The parser already bounds depth; this only stops the walk
                // from being the thing that overruns when it does not.
                return;
            }
            if (element.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Walk(
                        item,
                        pointer + "/" + index.ToString(CultureInfo.InvariantCulture),
                        depth + 1,
                        options,
                        diagnostics);
                    index++;
                }
                return;
            }
            if (element.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            foreach (JsonProperty member in element.EnumerateObject())
            {
                string memberPointer = pointer + "/" + EscapePointerToken(member.Name);
                if (s_scalarKinds.TryGetValue(member.Name, out WotProjectionMemberKind kind))
                {
                    Check(member.Value, member.Name, kind, memberPointer, diagnostics);
                }
                Walk(member.Value, memberPointer, depth + 1, options, diagnostics);
            }
        }

        private static void Check(
            JsonElement value,
            string name,
            WotProjectionMemberKind kind,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            if (Matches(value, kind))
            {
                return;
            }
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.NativeProjectionInvalid,
                $"The uav:nodes member '{name}' is {Describe(value.ValueKind)} where the " +
                $"record grammar declares {Describe(kind)}. The projection is a " +
                "schema-complete record, so a member of the wrong type is a corrupt record " +
                "rather than an absent value.",
                WotLocation.FromPointer(pointer)));
        }

        private static bool Matches(JsonElement value, WotProjectionMemberKind kind)
        {
            WotProjectionMemberKind actual = Of(value.ValueKind);
            if (actual == WotProjectionMemberKind.None)
            {
                // The writer never emits a JSON null, so a null member is a
                // corrupt record rather than an absent one, and saying so here
                // keeps a reader from answering "absent" for a member that is
                // present and wrong.
                return false;
            }
            return (kind & actual) != WotProjectionMemberKind.None;
        }

        private static WotProjectionMemberKind Of(JsonValueKind kind)
        {
            return kind switch
            {
                JsonValueKind.String => WotProjectionMemberKind.String,
                JsonValueKind.Number => WotProjectionMemberKind.Number,
                JsonValueKind.True or JsonValueKind.False => WotProjectionMemberKind.Boolean,
                JsonValueKind.Array => WotProjectionMemberKind.Array,
                JsonValueKind.Object => WotProjectionMemberKind.Object,
                _ => WotProjectionMemberKind.None
            };
        }

        private static string Describe(WotProjectionMemberKind kind)
        {
            var names = new List<string>(2);
            if ((kind & WotProjectionMemberKind.String) != WotProjectionMemberKind.None)
            {
                names.Add("a string");
            }
            if ((kind & WotProjectionMemberKind.Number) != WotProjectionMemberKind.None)
            {
                names.Add("a number");
            }
            if ((kind & WotProjectionMemberKind.Boolean) != WotProjectionMemberKind.None)
            {
                names.Add("a boolean");
            }
            if ((kind & WotProjectionMemberKind.Array) != WotProjectionMemberKind.None)
            {
                names.Add("an array");
            }
            if ((kind & WotProjectionMemberKind.Object) != WotProjectionMemberKind.None)
            {
                names.Add("an object");
            }

            // Every entry of the grammar table names at least one JSON type, so
            // the list is never empty here; joining one name yields that name.
            return string.Join(" or ", names);
        }

        private static string Describe(JsonValueKind kind)
        {
            WotProjectionMemberKind mapped = Of(kind);
            return mapped == WotProjectionMemberKind.None ? "null" : Describe(mapped);
        }

        private static string EscapePointerToken(string token)
        {
            return token
                .Replace("~", "~0", StringComparison.Ordinal)
                .Replace("/", "~1", StringComparison.Ordinal);
        }

        /// <summary>
        /// The JSON types each member the projection writes may carry, read
        /// off the writer that produces them.
        /// </summary>
        /// <remarks>
        /// Two members are deliberately polymorphic: <c>value</c> is the
        /// aliased NodeId of an alias, the text of a LocalizedText, and the
        /// ordinal of an enumeration field, so it is a string or a number and
        /// never a structure; and <c>name</c> names a field, a definition and a
        /// model in three different records but is a string in all of them.
        /// Listing the alternatives is what keeps the check exact where the
        /// grammar really is one type and honest where it is not.
        /// </remarks>
        private static readonly Dictionary<string, WotProjectionMemberKind> s_scalarKinds =
            new(StringComparer.Ordinal)
            {
                ["@type"] = WotProjectionMemberKind.String,
                ["profileVersion"] = WotProjectionMemberKind.String,
                ["nodeId"] = WotProjectionMemberKind.String,
                ["browseName"] = WotProjectionMemberKind.String,
                ["symbolicName"] = WotProjectionMemberKind.String,
                ["parentNodeId"] = WotProjectionMemberKind.String,
                ["nodeClass"] = WotProjectionMemberKind.String,
                ["dataType"] = WotProjectionMemberKind.String,
                ["methodDeclarationId"] = WotProjectionMemberKind.String,
                ["typeDefinition"] = WotProjectionMemberKind.String,
                ["modellingRule"] = WotProjectionMemberKind.String,
                ["superType"] = WotProjectionMemberKind.String,
                ["referenceType"] = WotProjectionMemberKind.String,
                ["target"] = WotProjectionMemberKind.String,
                ["alias"] = WotProjectionMemberKind.String,
                ["modelUri"] = WotProjectionMemberKind.String,
                ["modelVersion"] = WotProjectionMemberKind.String,
                ["version"] = WotProjectionMemberKind.String,
                ["locale"] = WotProjectionMemberKind.String,
                ["name"] = WotProjectionMemberKind.String,
                ["baseType"] = WotProjectionMemberKind.String,
                ["arrayDimensions"] = WotProjectionMemberKind.String,
                ["lastModified"] = WotProjectionMemberKind.String,
                ["publicationDate"] = WotProjectionMemberKind.String,
                ["documentation"] = WotProjectionMemberKind.String,
                ["releaseStatus"] = WotProjectionMemberKind.String,
                ["purpose"] = WotProjectionMemberKind.String,
                ["roleId"] = WotProjectionMemberKind.String,
                ["kind"] = WotProjectionMemberKind.String,
                ["xmlSchemaUri"] = WotProjectionMemberKind.String,

                ["valueRank"] = WotProjectionMemberKind.Number,
                ["accessLevel"] = WotProjectionMemberKind.Number,
                ["userAccessLevel"] = WotProjectionMemberKind.Number,
                ["minimumSamplingInterval"] = WotProjectionMemberKind.Number,
                ["eventNotifier"] = WotProjectionMemberKind.Number,
                ["writeMask"] = WotProjectionMemberKind.Number,
                ["userWriteMask"] = WotProjectionMemberKind.Number,
                ["accessRestrictions"] = WotProjectionMemberKind.Number,
                ["maxStringLength"] = WotProjectionMemberKind.Number,
                ["permissions"] = WotProjectionMemberKind.Number,

                ["isAbstract"] = WotProjectionMemberKind.Boolean,
                ["isForward"] = WotProjectionMemberKind.Boolean,
                ["symmetric"] = WotProjectionMemberKind.Boolean,
                ["historizing"] = WotProjectionMemberKind.Boolean,
                ["executable"] = WotProjectionMemberKind.Boolean,
                ["userExecutable"] = WotProjectionMemberKind.Boolean,
                ["isOptionSet"] = WotProjectionMemberKind.Boolean,
                ["isUnion"] = WotProjectionMemberKind.Boolean,
                ["isOptional"] = WotProjectionMemberKind.Boolean,
                ["allowSubTypes"] = WotProjectionMemberKind.Boolean,
                ["containsNoLoops"] = WotProjectionMemberKind.Boolean,
                ["designToolOnly"] = WotProjectionMemberKind.Boolean,
                ["hasNoPermissions"] = WotProjectionMemberKind.Boolean,

                ["nodes"] = WotProjectionMemberKind.Array,
                ["namespaceUris"] = WotProjectionMemberKind.Array,
                ["serverUris"] = WotProjectionMemberKind.Array,
                ["models"] = WotProjectionMemberKind.Array,
                ["aliases"] = WotProjectionMemberKind.Array,
                ["references"] = WotProjectionMemberKind.Array,
                ["displayName"] = WotProjectionMemberKind.Array,
                ["description"] = WotProjectionMemberKind.Array,
                ["inverseName"] = WotProjectionMemberKind.Array,
                ["text"] = WotProjectionMemberKind.Array,
                ["rolePermissions"] = WotProjectionMemberKind.Array,
                ["fields"] = WotProjectionMemberKind.Array,
                ["requiredModels"] = WotProjectionMemberKind.Array,
                ["translations"] = WotProjectionMemberKind.Array,
                ["items"] = WotProjectionMemberKind.Array,
                ["argumentDescriptions"] = WotProjectionMemberKind.Array,

                ["definition"] = WotProjectionMemberKind.Object,

                // Polymorphic, and only across scalars.
                ["value"] = WotProjectionMemberKind.String | WotProjectionMemberKind.Number
            };
    }

    /// <summary>
    /// The JSON types a <c>uav:nodes</c> member may carry.
    /// </summary>
    /// <remarks>
    /// A set rather than one value, because two members of the grammar really
    /// are polymorphic and a table that pretended otherwise would reject a
    /// record the projection itself writes.
    /// </remarks>
    [Flags]
    internal enum WotProjectionMemberKind
    {
        /// <summary>No type; the member is not constrained here.</summary>
        None = 0,

        /// <summary>A JSON string.</summary>
        String = 1,

        /// <summary>A JSON number.</summary>
        Number = 2,

        /// <summary>A JSON boolean.</summary>
        Boolean = 4,

        /// <summary>A JSON array.</summary>
        Array = 8,

        /// <summary>A JSON object.</summary>
        Object = 16
    }
}
