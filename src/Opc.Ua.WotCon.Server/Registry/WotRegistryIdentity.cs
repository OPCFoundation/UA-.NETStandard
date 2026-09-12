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
using System.IO;
using System.Text.Json;

namespace Opc.Ua.WotCon.Server.Registry
{
    internal static class WotRegistryIdentity
    {
        public static bool IsAbsoluteUri(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            foreach (char c in value!)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\')
                {
                    return false;
                }
            }
            return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.IsWellFormedOriginalString();
        }

        public static void RequireUri(string? value)
        {
            if (!IsAbsoluteUri(value))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument, "Source authority must be an unambiguous absolute URI.");
            }
        }

        public static bool IsIdentifier(string? value)
        {
            if (string.IsNullOrEmpty(value) || value!.Length > 128 || value[0] is '.' or '-')
            {
                return false;
            }
            foreach (char c in value)
            {
                if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or
                    (>= '0' and <= '9') or '_' or '.' or '-'))
                {
                    return false;
                }
            }
            return true;
        }

        public static string? ReadSourceId(JsonElement root)
        {
            string? sourceId = null;
            int count = 0;
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (property.Name is not ("id" or "@id"))
                {
                    continue;
                }
                if (++count > 1 || property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                {
                    throw new FormatException("The document has ambiguous or non-string source authority.");
                }
                sourceId = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            }
            return sourceId;
        }

        public static void ValidateSnapshot(WotRegistrySnapshot snapshot)
        {
            var groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var catalogues = new HashSet<(WoTDocumentKindEnum, string)>();
            foreach (KeyValuePair<string, WotResourceGroup> entry in snapshot.Groups)
            {
                WotResourceGroup group = entry.Value;
                if (!string.Equals(entry.Key, group.GroupId, StringComparison.Ordinal) ||
                    !groupIds.Add(group.GroupId) ||
                    !WotDocumentKinds.IsDocument(group.Kind))
                {
                    throw new InvalidDataException(
                        "The registry has duplicate group assignments or invalid ownership.");
                }
                bool authoritative = group.CatalogUri is not null;
                if (authoritative &&
                    (!IsAbsoluteUri(group.CatalogUri) ||
                        !catalogues.Add((group.Kind, group.CatalogUri!)) ||
                        !string.Equals(group.Name, group.CatalogUri, StringComparison.Ordinal)))
                {
                    throw new InvalidDataException("The registry catalogue authority map is invalid or contradictory.");
                }
                var resourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var sources = new HashSet<string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, WotResource> resourceEntry in group.Resources)
                {
                    WotResource resource = resourceEntry.Value;
                    if (!string.Equals(resourceEntry.Key, resource.ResourceId, StringComparison.Ordinal) ||
                        !string.Equals(resource.GroupId, group.GroupId, StringComparison.Ordinal) ||
                        resource.Kind != group.Kind ||
                        !resourceIds.Add(resource.ResourceId))
                    {
                        throw new InvalidDataException("The registry has duplicate resource assignments or ownership.");
                    }
                    if ((authoritative && !IsAbsoluteUri(resource.SourceId)) ||
                        (resource.SourceId is not null && !sources.Add(resource.SourceId)))
                    {
                        throw new InvalidDataException("The registry resource authority map is missing or duplicated.");
                    }
                    var versionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (WotResourceVersion version in resource.Versions)
                    {
                        if (!versionIds.Add(version.VersionId) ||
                            (version.DocumentId is not null &&
                                resource.SourceId is not null &&
                                !string.Equals(version.DocumentId, resource.SourceId, StringComparison.Ordinal)))
                        {
                            throw new InvalidDataException(
                                "The registry Version authority is duplicate or conflicting.");
                        }
                    }
                    if ((authoritative || resource.Versions.Length > 0) &&
                        resource.FindVersion(resource.DefaultVersionId) is null)
                    {
                        throw new InvalidDataException("A registry Resource is missing its exact default Version.");
                    }
                }
            }
        }
    }
}
