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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.OneFuzz
{
    internal static class ReplayProcess
    {
        internal static async Task<int> RunAsync(
            string root,
            string manifest,
            TargetArea area,
            TargetSpec target,
            string results,
            int timeoutSeconds)
        {
            List<string> inputs = ContractPaths.CorpusFiles(root, target);
            string resultPath = Path.Combine(results, target.Id + ".json");
            string stdoutPath = Path.Combine(results, target.Id + ".stdout.log");
            string stderrPath = Path.Combine(results, target.Id + ".stderr.log");
            string? processPath = Environment.ProcessPath;
            string host = Path.GetFileNameWithoutExtension(processPath) == "dotnet" ? processPath! : "dotnet";
            var start = new ProcessStartInfo(host)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = results
            };
            foreach (string argument in new[]
            {
                "exec",
                "--runtimeconfig", Path.Combine(root, Path.ChangeExtension(area.Assembly, ".runtimeconfig.json")),
                Assembly.GetExecutingAssembly().Location,
                "_replay",
                "--drop", root,
                "--manifest", manifest,
                "--areas", area.Id,
                "--target", target.Id,
                "--result", resultPath
            })
            {
                start.ArgumentList.Add(argument);
            }

            start.Environment.Remove("DOTNET_ADDITIONAL_DEPS");
            start.Environment.Remove("DOTNET_SHARED_STORE");
            start.Environment.Remove("DOTNET_STARTUP_HOOKS");
            using var process = new Process { StartInfo = start };
            var stdout = new FileStream(stdoutPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await using var stdoutDisposal = stdout.ConfigureAwait(false);
            var stderr = new FileStream(stderrPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await using var stderrDisposal = stderr.ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start replay for {target.Id}.");
            }

            try
            {
                await Task.WhenAll(
                    process.StandardOutput.BaseStream.CopyToAsync(stdout, timeout.Token),
                    process.StandardError.BaseStream.CopyToAsync(stderr, timeout.Token),
                    process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }

                throw new TimeoutException(
                    $"Hard process watchdog terminated {target.Id} after " +
                    $"{timeoutSeconds.ToString(CultureInfo.InvariantCulture)} seconds. " +
                    $"Logs: {stdoutPath}, {stderrPath}");
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidDataException(
                    $"Published callback {target.Id} failed with exit {process.ExitCode}. Log: {stderrPath}");
            }

            if (!File.Exists(resultPath))
            {
                throw new InvalidDataException(
                    $"Replay exited without a completion receipt for {target.Id}; zero skips are required.");
            }

            using JsonDocument result = JsonDocument.Parse(
                await File.ReadAllBytesAsync(resultPath).ConfigureAwait(false));
            JsonElement receipt = result.RootElement;
            JsonElement completed = receipt.GetProperty("inputs");
            if (JsonContract.String(receipt, "target") != target.Id ||
                JsonContract.String(receipt, "type") != area.Type ||
                JsonContract.String(receipt, "method") != target.Method ||
                completed.GetArrayLength() != inputs.Count)
            {
                throw new InvalidDataException($"Incomplete or mismatched callback receipt: {target.Id}");
            }

            for (int i = 0; i < inputs.Count; i++)
            {
                string hash = await HashFileAsync(inputs[i]).ConfigureAwait(false);
                if (JsonContract.String(completed[i], "path") != ContractPaths.Relative(root, inputs[i]) ||
                    JsonContract.String(completed[i], "sha256") != hash)
                {
                    throw new InvalidDataException(
                        $"Replay did not complete the exact input inventory for {target.Id}.");
                }
            }

            return inputs.Count;
        }

        internal static async Task ExecuteAsync(
            string root,
            TargetArea area,
            TargetSpec target,
            string resultPath)
        {
            List<string> files = ContractPaths.CorpusFiles(root, target);
            using var assembly = new CallbackAssembly(ContractPaths.Resolve(root, area.Assembly));
            SpanCallback callback = assembly.GetCallback(area.Type, target.Method);
            var completed = new List<(string Path, string Hash)>();
            foreach (string file in files)
            {
                string relative = ContractPaths.Relative(root, file);
                await Console.Out.WriteLineAsync($"{target.Id}: {relative}").ConfigureAwait(false);
                await Console.Out.FlushAsync().ConfigureAwait(false);
                byte[] bytes = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
                string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                callback(bytes);
                completed.Add((relative, hash));
            }

            var output = new FileStream(resultPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using var outputDisposal = output.ConfigureAwait(false);
            using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteString("target", target.Id);
            writer.WriteString("type", area.Type);
            writer.WriteString("method", target.Method);
            writer.WriteStartArray("inputs");
            foreach ((string path, string hash) in completed)
            {
                writer.WriteStartObject();
                writer.WriteString("path", path);
                writer.WriteString("sha256", hash);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            await writer.FlushAsync().ConfigureAwait(false);
        }

        internal static async Task<string> HashFileAsync(string path)
        {
            FileStream stream = File.OpenRead(path);
            await using var streamDisposal = stream.ConfigureAwait(false);
            byte[] hash = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
            return Convert.ToHexStringLower(hash);
        }
    }
}
