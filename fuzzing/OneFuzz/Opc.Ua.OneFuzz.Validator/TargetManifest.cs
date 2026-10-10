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
using System.Linq;
using System.Text.Json;

namespace Opc.Ua.OneFuzz
{
    internal sealed record TargetArea(
        string Id,
        string Project,
        string Assembly,
        string Type,
        List<TargetSpec> Targets);

    internal sealed record TargetSpec(
        string Id,
        string Method,
        List<string> Corpus,
        List<string> Dictionaries);

    internal readonly record struct CallbackKey(string Dll, string Type, string Method);

    internal static class TargetManifest
    {
        internal static List<TargetArea> Read(string path, string? selectedAreas)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;
            JsonContract.RejectDuplicateProperties(root);
            if (root.GetProperty("schemaVersion").GetInt32() != 1)
            {
                throw new InvalidDataException("The fuzz manifest requires schemaVersion 1.");
            }

            HashSet<string>? selected = selectedAreas is null
                ? null
                : new HashSet<string>(selectedAreas.Split(','), StringComparer.Ordinal);
            if (selected?.Contains(string.Empty) == true)
            {
                throw new InvalidDataException("Area selection must contain nonempty, exact area IDs.");
            }

            var areaIds = new HashSet<string>(StringComparer.Ordinal);
            var targetIds = new HashSet<string>(StringComparer.Ordinal);
            var assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var callbacks = new HashSet<CallbackKey>();
            var areas = new List<TargetArea>();
            foreach (JsonElement area in root.GetProperty("areas").EnumerateArray())
            {
                string id = JsonContract.Identifier(area, "id");
                if (!areaIds.Add(id))
                {
                    throw new InvalidDataException($"Duplicate area ID: {id}");
                }

                if (selected is not null && !selected.Contains(id))
                {
                    continue;
                }

                string assembly = JsonContract.String(area, "assembly");
                if (!assembly.EndsWith(".dll", StringComparison.Ordinal))
                {
                    assembly += ".dll";
                }

                if (assembly.IndexOfAny(['/', '\\', ':']) >= 0 || !assemblies.Add(assembly))
                {
                    throw new InvalidDataException($"Assembly names must be unique DLL filenames: {assembly}");
                }

                string type = JsonContract.String(area, "type");
                var targets = new List<TargetSpec>();
                foreach (JsonElement target in area.GetProperty("targets").EnumerateArray())
                {
                    string targetId = JsonContract.Identifier(target, "id");
                    string method = JsonContract.Identifier(target, "method");
                    if (!targetIds.Add(targetId) || !callbacks.Add(new CallbackKey(assembly, type, method)))
                    {
                        throw new InvalidDataException($"Duplicate target ID or callback: {targetId}");
                    }

                    if (JsonContract.String(target, "classification") != "continuous")
                    {
                        throw new InvalidDataException(
                            $"Only explicitly continuous targets belong in a OneFuzz drop: {targetId}");
                    }

                    targets.Add(new TargetSpec(
                        targetId,
                        method,
                        JsonContract.Strings(target, "corpus", allowEmpty: false),
                        JsonContract.Strings(target, "dictionaries", allowEmpty: true)));
                }

                if (targets.Count == 0)
                {
                    throw new InvalidDataException($"Area {id} declares no targets.");
                }

                areas.Add(new TargetArea(
                    id,
                    JsonContract.String(area, "project"),
                    assembly,
                    type,
                    targets));
            }

            if (areas.Count == 0 || (selected is not null && !selected.IsSubsetOf(areaIds)))
            {
                throw new InvalidDataException("No selected areas, or a requested area is absent from the manifest.");
            }

            return areas;
        }
    }

    internal static class JsonContract
    {
        internal static string String(JsonElement parent, string property)
        {
            JsonElement value = parent.GetProperty(property);
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw new InvalidDataException($"'{property}' must be a nonempty string.");
            }

            return value.GetString()!;
        }

        internal static string Identifier(JsonElement parent, string property)
        {
            string value = String(parent, property);
            if (!char.IsAsciiLetterOrDigit(value[0]) ||
                value.Any(static c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-'))
            {
                throw new InvalidDataException($"'{property}' must be a portable identifier: {value}");
            }

            return value;
        }

        internal static List<string> Strings(JsonElement parent, string property, bool allowEmpty)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement value in parent.GetProperty(property).EnumerateArray())
            {
                string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                if (string.IsNullOrWhiteSpace(text) || !seen.Add(text))
                {
                    throw new InvalidDataException($"'{property}' requires distinct nonempty strings.");
                }

                result.Add(text);
            }

            if (!allowEmpty && result.Count == 0)
            {
                throw new InvalidDataException($"'{property}' must not be empty.");
            }

            return result;
        }

        internal static void RejectDuplicateProperties(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new InvalidDataException($"Duplicate JSON property: {property.Name}");
                    }

                    RejectDuplicateProperties(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    RejectDuplicateProperties(item);
                }
            }
        }
    }
}
