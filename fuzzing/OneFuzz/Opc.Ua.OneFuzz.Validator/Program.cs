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
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace Opc.Ua.OneFuzz
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            if (args.Length == 0)
            {
                await Console.Error.WriteLineAsync(
                    "Usage: validate --drop DIR [--manifest FILE] [--config FILE] [--areas ID,ID] " +
                    "[--timeout-seconds 120] [--results DIR]; discover --assembly DLL; " +
                    "configure --drop DIR --profile FILE; check-manifest --manifest FILE [--areas ID,ID]")
                    .ConfigureAwait(false);
                return 2;
            }

            Dictionary<string, string> options = ParseOptions(args);
            switch (args[0])
            {
                case "discover":
                    RequireOptions(options, ["assembly"]);
                    await DiscoverAsync(Path.GetFullPath(options["assembly"])).ConfigureAwait(false);
                    return 0;
                case "validate":
                    RequireOptions(options, ["drop", "manifest", "config", "areas", "timeout-seconds", "results"]);
                    await ValidateAsync(options).ConfigureAwait(false);
                    return 0;
                case "check-manifest":
                    RequireOptions(options, ["manifest", "areas"]);
                    List<TargetArea> manifestAreas = TargetManifest.Read(
                        options["manifest"], options.GetValueOrDefault("areas"));
                    await Console.Out.WriteLineAsync(
                        $"Manifest declares {manifestAreas.Sum(static area => area.Targets.Count)} " +
                        "continuous targets.")
                        .ConfigureAwait(false);
                    return 0;
                case "configure":
                    RequireOptions(options, ["drop", "manifest", "areas", "profile"]);
                    string configRoot = Path.GetFullPath(options["drop"]);
                    string configManifest = Path.GetFullPath(
                        options.GetValueOrDefault("manifest") ?? Path.Combine(configRoot, "fuzz-targets.json"));
                    ContractPaths.Resolve(configRoot, ContractPaths.Relative(configRoot, configManifest));
                    List<TargetArea> configAreas = TargetManifest.Read(
                        configManifest, options.GetValueOrDefault("areas"));
                    Dictionary<string, HashSet<string>> configClosures = InspectDrop(configRoot, configAreas);
                    await ServiceConfiguration.CreateAsync(
                        configRoot, configManifest, options["profile"], configAreas, configClosures)
                        .ConfigureAwait(false);
                    return 0;
                case "_replay":
                    RequireOptions(options, ["drop", "manifest", "areas", "target", "result"]);
                    string root = Path.GetFullPath(options["drop"]);
                    List<TargetArea> areas = TargetManifest.Read(
                        options["manifest"], options.GetValueOrDefault("areas"));
                    (TargetArea Area, TargetSpec Target) selected = areas
                        .SelectMany(area => area.Targets.Select(target => (Area: area, Target: target)))
                        .Single(pair => pair.Target.Id == options["target"]);
                    await ReplayProcess.ExecuteAsync(
                        root, selected.Area, selected.Target, options["result"]).ConfigureAwait(false);
                    return 0;
                default:
                    throw new ArgumentException($"Unknown command: {args[0]}");
            }
        }

        private static async Task ValidateAsync(Dictionary<string, string> options)
        {
            string root = Path.GetFullPath(options["drop"]);
            string manifest = Path.GetFullPath(
                options.GetValueOrDefault("manifest") ?? Path.Combine(root, "fuzz-targets.json"));
            ContractPaths.Resolve(root, ContractPaths.Relative(root, manifest));
            List<TargetArea> areas = TargetManifest.Read(manifest, options.GetValueOrDefault("areas"));
            int timeout = int.Parse(
                options.GetValueOrDefault("timeout-seconds", "120"), CultureInfo.InvariantCulture);
            if (timeout is < 1 or > 86400)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options), "The process watchdog must be 1-86400 seconds.");
            }

            string results = Path.GetFullPath(options.GetValueOrDefault("results") ??
                Path.Combine(Path.GetTempPath(), "opcua-onefuzz-validation-" + Guid.NewGuid().ToString("N")));
            if (Directory.Exists(results) || File.Exists(results))
            {
                throw new IOException($"Use a new results directory, not an existing path: {results}");
            }

            Directory.CreateDirectory(results);
            Dictionary<string, HashSet<string>> closures = InspectDrop(root, areas);

            string? configuration = options.GetValueOrDefault("config");
            if (configuration is null && File.Exists(Path.Combine(root, "OneFuzzConfig.json")))
            {
                configuration = Path.Combine(root, "OneFuzzConfig.json");
            }

            if (configuration is not null)
            {
                configuration = Path.GetFullPath(configuration);
                ContractPaths.Resolve(root, ContractPaths.Relative(root, configuration));
                ServiceConfiguration.Validate(root, configuration, areas, closures);
            }

            var replays = new List<(TargetArea Area, TargetSpec Target, int Inputs)>();
            foreach (TargetArea area in areas)
            {
                foreach (TargetSpec target in area.Targets)
                {
                    int count = await ReplayProcess.RunAsync(
                        root, manifest, area, target, results, timeout).ConfigureAwait(false);
                    replays.Add((area, target, count));
                    await Console.Out.WriteLineAsync(
                        $"{target.Id}: {count} published-callback inputs completed.").ConfigureAwait(false);
                }
            }

            string report = Path.Combine(results, "validation.json");
            await WriteReportAsync(root, manifest, configuration, report, closures, replays).ConfigureAwait(false);
            await Console.Out.WriteLineAsync(
                $"Validated {replays.Count} callbacks; zero skips. Local evidence: {report}").ConfigureAwait(false);
        }

        private static Dictionary<string, HashSet<string>> InspectDrop(string root, List<TargetArea> areas)
        {
            var closures = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (TargetArea area in areas)
            {
                using var assembly = new CallbackAssembly(ContractPaths.Resolve(root, area.Assembly));
                List<CallbackKey> discovered = assembly.Discover();
                var expected = area.Targets
                    .Select(target => new CallbackKey(area.Assembly, area.Type, target.Method))
                    .ToHashSet();
                if (discovered.Count != expected.Count || !expected.SetEquals(discovered))
                {
                    throw new InvalidDataException(
                        $"Published callback inventory differs from area {area.Id}. " +
                        $"Missing: {string.Join(", ", expected.Except(discovered))}. " +
                        $"Extra: {string.Join(", ", discovered.Except(expected))}.");
                }

                closures.Add(area.Id, assembly.ValidateClosure());
                foreach (TargetSpec target in area.Targets)
                {
                    ContractPaths.CorpusFiles(root, target);
                    foreach (string dictionary in target.Dictionaries)
                    {
                        if (new FileInfo(ContractPaths.Resolve(root, dictionary)).Length == 0)
                        {
                            throw new InvalidDataException($"Required dictionary is empty: {dictionary}");
                        }
                    }
                }
            }

            return closures;
        }

        private static async Task DiscoverAsync(string path)
        {
            using var assembly = new CallbackAssembly(path);
            Stream output = Console.OpenStandardOutput();
            await using var outputDisposal = output.ConfigureAwait(false);
            using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteString("assembly", Path.GetFileName(path));
            writer.WriteStartArray("callbacks");
            foreach (CallbackKey callback in assembly.Discover())
            {
                writer.WriteStartObject();
                writer.WriteString("type", callback.Type);
                writer.WriteString("method", callback.Method);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            await writer.FlushAsync().ConfigureAwait(false);
        }

        private static async Task WriteReportAsync(
            string root,
            string manifest,
            string? configuration,
            string report,
            Dictionary<string, HashSet<string>> closures,
            List<(TargetArea Area, TargetSpec Target, int Inputs)> replays)
        {
            var stream = new FileStream(report, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using var streamDisposal = stream.ConfigureAwait(false);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("runtime", RuntimeInformation.FrameworkDescription);
            writer.WriteString("operatingSystem", RuntimeInformation.OSDescription);
            writer.WriteString("architecture", RuntimeInformation.ProcessArchitecture.ToString());
            writer.WriteBoolean("serviceCanary", false);
            writer.WriteString("manifestSha256", await ReplayProcess.HashFileAsync(manifest).ConfigureAwait(false));
            if (configuration is not null)
            {
                writer.WriteString("configSha256",
                    await ReplayProcess.HashFileAsync(configuration).ConfigureAwait(false));
            }

            writer.WriteNumber("skipped", 0);
            writer.WriteStartArray("targets");
            foreach ((TargetArea area, TargetSpec target, int inputs) in replays)
            {
                writer.WriteStartObject();
                writer.WriteString("id", target.Id);
                writer.WriteString("assembly", area.Assembly);
                writer.WriteString("type", area.Type);
                writer.WriteString("method", target.Method);
                writer.WriteNumber("inputs", inputs);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartObject("publishedFiles");
            foreach (string file in closures.Values.SelectMany(static files => files)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                writer.WriteString(file, await ReplayProcess.HashFileAsync(
                    ContractPaths.Resolve(root, file)).ConfigureAwait(false));
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
            await writer.FlushAsync().ConfigureAwait(false);
        }

        private static Dictionary<string, string> ParseOptions(string[] args)
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 1; index < args.Length; index += 2)
            {
                if (!args[index].StartsWith("--", StringComparison.Ordinal) ||
                    index + 1 >= args.Length || !options.TryAdd(args[index][2..], args[index + 1]))
                {
                    throw new ArgumentException("Options require distinct --name value pairs.");
                }
            }

            return options;
        }

        private static void RequireOptions(Dictionary<string, string> options, string[] allowed)
        {
            if (options.Keys.Except(allowed, StringComparer.Ordinal).Any())
            {
                throw new ArgumentException("An unknown command option was supplied.");
            }
        }
    }
}
