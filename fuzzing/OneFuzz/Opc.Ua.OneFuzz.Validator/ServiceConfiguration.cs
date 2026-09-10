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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Opc.Ua.OneFuzz
{
    internal static class ServiceConfiguration
    {
        internal static async Task CreateAsync(
            string root,
            string manifest,
            string profilePath,
            List<TargetArea> areas,
            Dictionary<string, HashSet<string>> closures)
        {
            string destination = Path.Combine(root, "OneFuzzConfig.json");
            if (File.Exists(destination))
            {
                throw new IOException("Refusing to replace an existing service configuration.");
            }

            if (File.Exists(Path.Combine(root, "publication.json")))
            {
                throw new IOException(
                    "Cannot configure a completed drop. Rebuild with -OwnershipProfile to preserve its evidence.");
            }

            using JsonDocument document = JsonDocument.Parse(
                await File.ReadAllBytesAsync(profilePath).ConfigureAwait(false));
            JsonElement profile = document.RootElement;
            JsonContract.RejectDuplicateProperties(profile);
            RequireProperties(profile, ["schemaVersion", "worker", "ownership", "targets"]);
            if (profile.GetProperty("schemaVersion").GetInt32() != 1)
            {
                throw new InvalidDataException("The ownership profile requires schemaVersion 1.");
            }

            JsonElement worker = profile.GetProperty("worker");
            RequireProperties(worker, ["os", "framework", "architecture", "instrumentation"]);
            if (JsonContract.String(worker, "os") != "azurelinux3" ||
                JsonContract.String(worker, "framework") != "net10.0" ||
                JsonContract.String(worker, "instrumentation") != "service" ||
                JsonContract.String(worker, "architecture") is not ("x64" or "arm64"))
            {
                throw new InvalidDataException(
                    "The supplied worker contract must specify azurelinux3/net10.0, " +
                    "x64 or arm64, service instrumentation.");
            }

            JsonElement ownership = profile.GetProperty("ownership");
            RequireProperties(ownership, ["JobNotificationEmail", "AdoTemplate", "CodeCoverage", "SdlWorkItemId"]);
            ValidateOwnership(ownership);
            var ownedTargets = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var ownedJobs = new HashSet<(string Project, string Target)>();
            foreach (JsonElement target in profile.GetProperty("targets").EnumerateArray())
            {
                RequireProperties(target, ["id", "ProjectName", "TargetName", "SeedCorpusContainer"]);
                string id = JsonContract.Identifier(target, "id");
                if (!ownedTargets.TryAdd(id, target))
                {
                    throw new InvalidDataException($"Duplicate ownership profile target: {id}");
                }
            }

            if (!ownedTargets.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(
                areas.SelectMany(static area => area.Targets).Select(static target => target.Id)))
            {
                throw new InvalidDataException(
                    "The ownership profile must exactly cover the selected manifest targets.");
            }

            foreach ((string id, JsonElement target) in ownedTargets)
            {
                ValidateJobOwnership(target, ownedJobs, id);
            }

            string temporary = Path.Combine(root, ".OneFuzzConfig-" + Guid.NewGuid().ToString("N") + ".json");
            var createdDictionaries = new List<string>();
            bool completed = false;
            try
            {
                var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await using (output.ConfigureAwait(false))
                {
                    using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
                    writer.WriteStartObject();
                    writer.WriteNumber("ConfigVersion", 3);
                    writer.WriteStartArray("Entries");
                    foreach (TargetArea area in areas)
                    {
                        foreach (TargetSpec target in area.Targets)
                        {
                            JsonElement owner = ownedTargets[target.Id];
                            string? dictionary = await PrepareDictionaryAsync(
                                root, target, createdDictionaries).ConfigureAwait(false);
                            var dependencies = new HashSet<string>(closures[area.Id], StringComparer.Ordinal)
                            {
                                ContractPaths.Relative(root, manifest)
                            };
                            dependencies.UnionWith(ContractPaths.CorpusFiles(root, target)
                                .Select(path => ContractPaths.Relative(root, path)));
                            dependencies.UnionWith(target.Dictionaries.Select(path =>
                                ContractPaths.Relative(root, ContractPaths.Resolve(root, path))));
                            if (dictionary is not null)
                            {
                                dependencies.Add(dictionary);
                            }

                            if (File.Exists(Path.Combine(root, "build.json")))
                            {
                                dependencies.Add("build.json");
                            }

                            WriteEntry(writer, area, target, owner, ownership, dictionary, dependencies);
                        }
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                    await writer.FlushAsync().ConfigureAwait(false);
                }

                Validate(root, temporary, areas, closures);
                File.Move(temporary, destination);
                completed = true;
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }

                if (!completed)
                {
                    foreach (string dictionary in createdDictionaries)
                    {
                        File.Delete(dictionary);
                    }
                }
            }
        }

        internal static void Validate(
            string root,
            string path,
            List<TargetArea> areas,
            Dictionary<string, HashSet<string>> closures)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement config = document.RootElement;
            JsonContract.RejectDuplicateProperties(config);
            if (config.GetProperty("ConfigVersion").GetInt32() != 3)
            {
                throw new InvalidDataException("OneFuzz service configuration requires ConfigVersion 3.");
            }

            var expected = areas
                .SelectMany(area => area.Targets.Select(target => (Area: area, Target: target)))
                .ToDictionary(
                    static pair => new CallbackKey(pair.Area.Assembly, pair.Area.Type, pair.Target.Method));
            var actual = new HashSet<CallbackKey>();
            var jobs = new HashSet<(string Project, string Target)>();
            foreach (JsonElement entry in config.GetProperty("Entries").EnumerateArray())
            {
                JsonElement fuzzer = entry.GetProperty("Fuzzer");
                var key = new CallbackKey(
                    JsonContract.String(fuzzer, "Dll"),
                    JsonContract.String(fuzzer, "Class"),
                    JsonContract.String(fuzzer, "Method"));
                if (entry.GetProperty("Skip").GetBoolean() ||
                    JsonContract.String(fuzzer, "$type") != "libfuzzerDotNet" ||
                    !actual.Add(key) || !expected.TryGetValue(key, out var pair))
                {
                    throw new InvalidDataException($"Skipped, duplicate, or unexpected configured callback: {key}");
                }

                if (fuzzer.EnumerateObject().Count() != 4 ||
                    entry.GetProperty("MinAvailableMemoryMB").GetInt32() != 100 ||
                    entry.GetProperty("FuzzerTimeoutInSeconds").GetInt32() != 120)
                {
                    throw new InvalidDataException(
                        $"The approved managed fuzzer schema or baseline policy changed: {key}");
                }

                ValidateOwnership(entry);
                JsonElement serviceJobs = entry.GetProperty("OneFuzzJobs");
                if (serviceJobs.GetArrayLength() != 1)
                {
                    throw new InvalidDataException($"Exactly one explicitly owned service job is required: {key}");
                }

                JsonElement job = serviceJobs[0];
                ValidateJobOwnership(job, jobs, key.ToString());

                var dependencies = new HashSet<string>(StringComparer.Ordinal);
                foreach (string dependency in JsonContract.Strings(entry, "JobDependencies", allowEmpty: false))
                {
                    string resolved = ContractPaths.Resolve(root, dependency);
                    if (!File.Exists(resolved))
                    {
                        throw new InvalidDataException(
                            $"JobDependencies must list actual files, not directories: {dependency}");
                    }

                    dependencies.Add(ContractPaths.Relative(root, resolved));
                }

                var required = new HashSet<string>(closures[pair.Area.Id], StringComparer.Ordinal);
                required.UnionWith(ContractPaths.CorpusFiles(root, pair.Target)
                    .Select(file => ContractPaths.Relative(root, file)));
                required.UnionWith(pair.Target.Dictionaries.Select(path =>
                    ContractPaths.Relative(root, ContractPaths.Resolve(root, path))));
                string? dictionary = ValidateDictionaryOption(root, job, pair.Target);
                if (dictionary is not null)
                {
                    required.Add(dictionary);
                }

                if (!required.IsSubsetOf(dependencies))
                {
                    throw new InvalidDataException(
                        $"JobDependencies omits published closure or seed files for {pair.Target.Id}: " +
                        string.Join(", ", required.Except(dependencies, StringComparer.Ordinal)));
                }
            }

            if (!actual.SetEquals(expected.Keys))
            {
                throw new InvalidDataException(
                    "Service configuration does not exactly cover the manifest callback set.");
            }
        }

        internal static void ValidateOwnership(JsonElement entry)
        {
            if (!MailAddress.TryCreate(OwnershipString(entry, "JobNotificationEmail"), out _) ||
                entry.GetProperty("SdlWorkItemId").GetInt64() <= 0)
            {
                throw new InvalidDataException(
                    "A valid supplied notification address and SDL work item are required.");
            }

            JsonElement ado = entry.GetProperty("AdoTemplate");
            foreach (string property in new[] { "Org", "Project", "AssignedTo", "AreaPath", "IterationPath", "Type" })
            {
                OwnershipString(ado, property);
            }

            JsonContract.Strings(ado, "UniqueFields", allowEmpty: false);
            if (ado.GetProperty("AdoFields").ValueKind != JsonValueKind.Object ||
                !ado.GetProperty("AdoFields").EnumerateObject().Any())
            {
                throw new InvalidDataException("Explicit AdoFields are required.");
            }

            JsonElement coverage = entry.GetProperty("CodeCoverage");
            OwnershipString(coverage, "Org");
            OwnershipString(coverage, "Project");
            if (!long.TryParse(
                    OwnershipString(coverage, "PipelineId"),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long pipelineId) || pipelineId <= 0)
            {
                throw new InvalidDataException("A positive supplied coverage PipelineId is required.");
            }
        }

        internal static string OwnershipString(JsonElement parent, string property)
        {
            string value = JsonContract.String(parent, property);
            if (value.Contains("$(", StringComparison.Ordinal) ||
                value.Contains("${", StringComparison.Ordinal) ||
                value.Contains('<', StringComparison.Ordinal) ||
                value.Contains('>', StringComparison.Ordinal) ||
                value.Equals("REQUIRED", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Explicit ownership is required; '{property}' contains a placeholder.");
            }

            return value;
        }

        private static void RequireProperties(JsonElement value, string[] expected)
        {
            if (!value.EnumerateObject().Select(static property => property.Name)
                .ToHashSet(StringComparer.Ordinal).SetEquals(expected))
            {
                throw new InvalidDataException("The ownership profile contains missing or unrecognized properties.");
            }
        }

        private static async Task<string?> PrepareDictionaryAsync(
            string root,
            TargetSpec target,
            List<string> created)
        {
            if (target.Dictionaries.Count == 0)
            {
                return null;
            }

            if (target.Dictionaries.Count == 1)
            {
                string source = target.Dictionaries[0];
                DictionaryTokens(ContractPaths.Resolve(root, source));
                return source.Replace('\\', '/');
            }

            var tokens = new HashSet<string>(StringComparer.Ordinal);
            foreach (string source in target.Dictionaries)
            {
                tokens.UnionWith(DictionaryTokens(ContractPaths.Resolve(root, source)));
            }

            string relative = "onefuzz-dictionaries/" + target.Id + ".dict";
            string directory = Path.Combine(root, "onefuzz-dictionaries");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, target.Id + ".dict");
            if (File.Exists(path))
            {
                throw new IOException($"Refusing to replace an existing merged dictionary: {path}");
            }

            var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created.Add(path);
            await using var outputDisposal = output.ConfigureAwait(false);
            byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', tokens.Order(StringComparer.Ordinal)) + "\n");
            await output.WriteAsync(bytes).ConfigureAwait(false);
            return relative;
        }

        private static void ValidateJobOwnership(
            JsonElement job,
            HashSet<(string Project, string Target)> jobs,
            string context)
        {
            string project = OwnershipString(job, "ProjectName");
            string targetName = OwnershipString(job, "TargetName");
            string container = OwnershipString(job, "SeedCorpusContainer");
            if (!jobs.Add((project, targetName)) || !IsContainerName(container))
            {
                throw new InvalidDataException(
                    $"Duplicate service job or invalid explicit corpus container: {context}");
            }
        }

        private static void WriteEntry(
            Utf8JsonWriter writer,
            TargetArea area,
            TargetSpec target,
            JsonElement owner,
            JsonElement ownership,
            string? dictionary,
            HashSet<string> dependencies)
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in ownership.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WriteBoolean("Skip", false);
            writer.WriteStartObject("Fuzzer");
            writer.WriteString("$type", "libfuzzerDotNet");
            writer.WriteString("Dll", area.Assembly);
            writer.WriteString("Class", area.Type);
            writer.WriteString("Method", target.Method);
            writer.WriteEndObject();
            writer.WriteNumber("MinAvailableMemoryMB", 100);
            writer.WriteNumber("FuzzerTimeoutInSeconds", 120);
            writer.WriteStartArray("OneFuzzJobs");
            writer.WriteStartObject();
            foreach (string property in new[] { "ProjectName", "TargetName", "SeedCorpusContainer" })
            {
                writer.WriteString(property, OwnershipString(owner, property));
            }

            if (dictionary is not null)
            {
                writer.WriteStartArray("FuzzingTargetOptions");
                writer.WriteStringValue("-dict={setup_dir}/" + dictionary);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteStartArray("JobDependencies");
            foreach (string file in dependencies.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(file);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static bool IsContainerName(string value)
        {
            return value.Length is >= 3 and <= 63 &&
                char.IsAsciiLetterOrDigit(value[0]) && char.IsAsciiLetterOrDigit(value[^1]) &&
                !value.Contains("--", StringComparison.Ordinal) &&
                value.All(static c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
        }

        private static string? ValidateDictionaryOption(string root, JsonElement job, TargetSpec target)
        {
            List<string> options = job.TryGetProperty("FuzzingTargetOptions", out _)
                ? JsonContract.Strings(job, "FuzzingTargetOptions", allowEmpty: true)
                : [];
            string[] dictionaries = [.. options.Where(static option =>
                option.StartsWith("-dict=", StringComparison.Ordinal))];
            if (target.Dictionaries.Count == 0)
            {
                if (dictionaries.Length != 0)
                {
                    throw new InvalidDataException($"Unexpected dictionary option for {target.Id}.");
                }

                return null;
            }

            const string prefix = "-dict={setup_dir}/";
            if (dictionaries.Length != 1 || !dictionaries[0].StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Exactly one relocatable dictionary option is required for {target.Id}.");
            }

            string relative = dictionaries[0][prefix.Length..];
            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (string source in target.Dictionaries)
            {
                expected.UnionWith(DictionaryTokens(ContractPaths.Resolve(root, source)));
            }

            HashSet<string> actual = DictionaryTokens(ContractPaths.Resolve(root, relative));
            if (!actual.SetEquals(expected))
            {
                throw new InvalidDataException(
                    $"The configured dictionary does not preserve all manifest tokens: {target.Id}");
            }

            return relative;
        }

        private static HashSet<string> DictionaryTokens(string path)
        {
            var tokens = new HashSet<string>(
                File.ReadLines(path)
                    .Select(static line => line.Trim())
                    .Where(static line => line.Length != 0 && !line.StartsWith('#')),
                StringComparer.Ordinal);
            if (tokens.Count == 0)
            {
                throw new InvalidDataException($"A required dictionary has no tokens: {path}");
            }

            return tokens;
        }
    }
}
