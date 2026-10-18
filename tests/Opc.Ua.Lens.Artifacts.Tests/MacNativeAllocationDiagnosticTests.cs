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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Lens.Artifacts.Tests
{
    /// <summary>
    /// Captures allocation evidence in one authorized native process, never as rendering acceptance.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Explicit("One authorized CI4822 allocation diagnostic; removed after evidence retention.")]
    public sealed class MacNativeAllocationDiagnosticTests
    {
        /// <summary>
        /// Runs one bounded LLDB target with zombie and allocator tracking against immutable source.
        /// </summary>
        [Test]
        public async Task CaptureAllocationOnceAsync()
        {
            Assert.That(OperatingSystem.IsMacOS(), Is.True);
            Assert.That(Environment.GetEnvironmentVariable("GITHUB_REPOSITORY"),
                Is.EqualTo("OPCFoundation/UA-.NETStandard"));
            Assert.That(Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT"), Is.EqualTo("1"));
            Assert.That(Environment.GetEnvironmentVariable("CI4822_ALLOCATION_AUTHORIZATION"),
                Is.EqualTo("mac-allocation-once-20261018"));
            string head = Required("CI4822_ALLOCATION_HEAD");
            Assert.That(head.Length == 40 && head.All(Uri.IsHexDigit), Is.True);
            string source = Required("CI4822_ALLOCATION_SOURCE_DIRECTORY");
            Assert.That(await GitAsync(source, "rev-parse", "HEAD").ConfigureAwait(false),
                Is.EqualTo(kSource));
            Assert.That(await GitAsync(source, "rev-parse", "HEAD^{tree}").ConfigureAwait(false),
                Is.EqualTo("3ce786d4c4155b884438e58122a5078a2faa2942"));
            Assert.That(await GitAsync(source, "diff", "--exit-code").ConfigureAwait(false), Is.Empty);
            Assert.That(await GitAsync(TestContext.CurrentContext.TestDirectory,
                "rev-parse", head + "^").ConfigureAwait(false), Is.EqualTo(kSource));

            string executable = Path.GetFullPath(Required("UALENS_ARTIFACT_EXECUTABLE"));
            string logs = Path.GetFullPath(Required("UALENS_ARTIFACT_LOG_DIRECTORY"));
            Directory.CreateDirectory(logs);
            string home = Path.Combine(Path.GetTempPath(), "ci4822-allocation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(home);
            try
            {
                string rawOutput = Path.Combine(home, "target-stdout.txt");
                string rawErrors = Path.Combine(home, "target-stderr.txt");
                string script = Path.Combine(home, "allocation.py");
                await File.WriteAllTextAsync(script, kLldbScript).ConfigureAwait(false);
                ProcessStartInfo start = ArtifactProbe.CreateStartInfo(
                    executable, home, home, true, false, false, false);
                string[] arguments = [.. start.ArgumentList];
                start.FileName = "/usr/bin/lldb";
                start.Environment["CI4822_RAW_ERRORS"] = rawErrors;
                start.ArgumentList.Clear();
                foreach (string argument in new[]
                {
                    "--batch",
                    "-o", "settings set target.disable-aslr false",
                    "-o", "settings set target.env-vars NSZombieEnabled=YES NSDeallocateZombies=NO " +
                        "MallocStackLogging=1 MallocStackLoggingNoCompact=1 MallocScribble=1",
                    "-o", $"settings set target.output-path \"{rawOutput}\"",
                    "-o", $"settings set target.error-path \"{rawErrors}\"",
                    "-o", $"command script import \"{script}\"",
                    "-o", "breakpoint set --name malloc_error_break",
                    "-o", "run",
                    "-o", "ci4822-allocation",
                    "-k", "ci4822-allocation",
                    "--", executable
                }.Concat(arguments))
                {
                    start.ArgumentList.Add(argument);
                }

                string executableHash;
                using (FileStream executableStream = File.OpenRead(executable))
                {
                    executableHash = Convert.ToHexString(
                        await SHA256.HashDataAsync(executableStream).ConfigureAwait(false));
                }
                using (var marker = new FileStream(
                    Path.Combine(logs, "execution-started.txt"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await marker.WriteAsync(Encoding.UTF8.GetBytes(
                        $"definition={head}\nsource={kSource}\nsha256={executableHash}\n")).ConfigureAwait(false);
                }
                DateTimeOffset started = DateTimeOffset.UtcNow;
                ArtifactProcessResult result = await ArtifactProbe.RunProcessAsync(
                    start, TimeSpan.FromSeconds(120), TestContext.CurrentContext.CancellationToken)
                    .ConfigureAwait(false);
                string output = Path.Combine(logs, "allocation.txt");
                string errors = Path.Combine(logs, "debugger-stderr.txt");
                string provenance = Path.Combine(logs, "provenance.txt");
                await File.WriteAllTextAsync(output, Redact(result.StandardOutput, home, source)).ConfigureAwait(false);
                await File.WriteAllTextAsync(errors, Redact(result.StandardError, home, source)).ConfigureAwait(false);
                string signal = File.Exists(rawErrors)
                    ? await File.ReadAllTextAsync(rawErrors).ConfigureAwait(false) : string.Empty;
                string signals = string.Join('\n', signal.Split('\n').Where(line =>
                    line.Contains("message sent to deallocated instance", StringComparison.Ordinal) ||
                    line.Contains("malloc: *** error", StringComparison.Ordinal) ||
                    line.Contains("MallocStackLogging", StringComparison.Ordinal) ||
                    line.Contains("UALENS_NATIVE_VIEWPORT_SMOKE_PASS", StringComparison.Ordinal)));
                string signalPath = Path.Combine(logs, "native-signals.txt");
                await File.WriteAllTextAsync(signalPath, Redact(signals, home, source)).ConfigureAwait(false);
                await File.WriteAllTextAsync(provenance,
                    $"diagnostic-only\nsource={kSource}\ndefinition={head}\n" +
                    $"run={Required("GITHUB_RUN_ID")}\nattempt=1\nsha256={executableHash}\n" +
                    $"started={started:O}\ncompleted={DateTimeOffset.UtcNow:O}\ndebuggerPid={result.ProcessId}\n" +
                    $"debuggerExit={result.ExitCode}\ntimedOut={result.TimedOut}\n" +
                    "NSZombieEnabled=YES\nNSDeallocateZombies=NO\nMallocStackLogging=1\n" +
                    "MallocStackLoggingNoCompact=1\nMallocScribble=1\n").ConfigureAwait(false);
                foreach (string attachment in new[] { output, errors, provenance, signalPath })
                {
                    TestContext.AddTestAttachment(attachment);
                }
                Assert.That(result.TimedOut, Is.False, "The single diagnostic timed out; it must not be retried.");
                Assert.That(result.StandardOutput, Does.Contain("CI4822_ALLOCATION_CAPTURE"),
                    "No allocation capture was produced; do not retry without fresh authorization.");
                Assert.That(await GitAsync(source, "rev-parse", "HEAD").ConfigureAwait(false),
                    Is.EqualTo(kSource));
                Assert.That(await GitAsync(source, "diff", "--exit-code").ConfigureAwait(false), Is.Empty);
            }
            finally
            {
                Directory.Delete(home, recursive: true);
            }
        }

        /// <summary>
        /// Reads source identity without running any target application.
        /// </summary>
        private static async Task<string> GitAsync(string directory, params string[] arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
            ArtifactProcessResult result = await ArtifactProbe.RunProcessAsync(start, TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            return result.StandardOutput.Trim();
        }

        /// <summary>
        /// Removes local directory identities from exported debugger evidence.
        /// </summary>
        private static string Redact(string text, string home, string source)
        {
            return text.Replace(home, "<DIAGNOSTIC_HOME>", StringComparison.Ordinal)
                .Replace(source, "<SOURCE>", StringComparison.Ordinal)
                .Replace(Required("GITHUB_WORKSPACE"), "<WORKSPACE>", StringComparison.Ordinal);
        }

        /// <summary>
        /// Requires explicit authorization and artifact locations.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static string Required(string name)
        {
            return Environment.GetEnvironmentVariable(name) ??
                throw new InvalidOperationException($"Missing diagnostic input: {name}");
        }

        /// <summary>The published application source, excluding uncommitted review fixes.</summary>
        private const string kSource = "f31ba46963821a411c3c6754b9bd11aeac0dcc93";

        /// <summary>Reads stacks and allocation histories without executing expressions in the target.</summary>
        private const string kLldbScript = """
            import json
            import lldb
            import os
            import re
            import subprocess

            captured = False

            def capture(debugger, command, result, internal_dict):
                global captured
                if captured:
                    return
                captured = True
                target = debugger.GetSelectedTarget()
                process = target.GetProcess()
                report = {
                    "marker": "CI4822_ALLOCATION_CAPTURE",
                    "pid": process.GetProcessID(),
                    "state": process.GetState(),
                    "triple": target.GetTriple(),
                    "histories": [],
                }
                for query in ["process status", "thread backtrace all -c 64", "image list"]:
                    response = lldb.SBCommandReturnObject()
                    debugger.GetCommandInterpreter().HandleCommand(query, response)
                    report[query] = {
                        "success": response.Succeeded(),
                        "output": response.GetOutput() or "",
                        "error": response.GetError() or "",
                    }
                candidates = []
                errors_path = os.environ.get("CI4822_RAW_ERRORS")
                if errors_path and os.path.isfile(errors_path):
                    with open(errors_path, encoding="utf-8", errors="replace") as stream:
                        errors = stream.read(262144)
                    candidates.extend(int(value, 16) for value in
                        re.findall(r"deallocated instance (0x[0-9a-fA-F]+)", errors))
                if process.GetState() in [lldb.eStateStopped, lldb.eStateCrashed]:
                    thread = process.GetSelectedThread()
                    frame = thread.GetFrameAtIndex(0)
                    registers = {}
                    for name in ["pc", "x0", "x1", "x19", "x20", "x21"]:
                        value = frame.FindRegister(name)
                        if value.IsValid():
                            number = value.GetValueAsUnsigned()
                            registers[name] = hex(number)
                            if name in ["x0", "x19", "x20"] and number > 65536:
                                candidates.append(number)
                    report["registers"] = registers
                    for address in list(dict.fromkeys(candidates))[:4]:
                        entry = {"address": hex(address)}
                        try:
                            history = subprocess.run(
                                ["/usr/bin/malloc_history", str(process.GetProcessID()), hex(address)],
                                capture_output=True, text=True, errors="replace", timeout=8, check=False)
                            entry["exit_code"] = history.returncode
                            entry["stdout"] = history.stdout[:65536]
                            entry["stderr"] = history.stderr[:65536]
                            entry["truncated"] = len(history.stdout) > 65536 or len(history.stderr) > 65536
                        except subprocess.TimeoutExpired:
                            entry["error"] = "malloc_history exceeded its eight-second bound"
                        report["histories"].append(entry)
                result.PutCString(json.dumps(report, indent=2))

            def __lldb_init_module(debugger, internal_dict):
                debugger.HandleCommand("command script add -f allocation.capture ci4822-allocation")
            """;
    }
}
