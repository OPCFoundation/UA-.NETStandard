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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Replays required good seeds and optional regression inputs against each fuzz target
    /// and records the observed target/input executions.
    /// </summary>
    [TestFixture]
    [Category("Fuzzing")]
    public abstract class FuzzTargetTestsBase
    {
        /// <summary>
        /// Required good-seed inputs loaded recursively from Testcases; a missing or empty inventory is an error.
        /// </summary>
        public static readonly TestcaseAsset[] GoodTestcases =
        [
            .. AssetCollection<TestcaseAsset>.CreateFromFiles(
                TestUtils.EnumerateTestAssets("Testcases", "*", requireNonEmpty: true))
        ];

        /// <summary>
        /// Optional crash regression inputs loaded recursively from Assets for robustness and fidelity replay.
        /// </summary>
        public static readonly TestcaseAsset[] CrashAssets =
        [
            .. AssetCollection<TestcaseAsset>.CreateFromFiles(
                TestUtils.EnumerateTestAssets("Assets", "crash*.*"))
        ];

        /// <summary>
        /// Optional timeout regression inputs loaded recursively from matching files in Assets.
        /// </summary>
        public static readonly TestcaseAsset[] TimeoutAssets =
        [
            .. AssetCollection<TestcaseAsset>.CreateFromFiles(
                TestUtils.EnumerateTestAssets("Assets", "timeout*.*"))
        ];

        /// <summary>
        /// Optional slow-input regression assets loaded recursively from matching files in Assets.
        /// </summary>
        public static readonly TestcaseAsset[] SlowAssets =
        [
            .. AssetCollection<TestcaseAsset>.CreateFromFiles(
                TestUtils.EnumerateTestAssets("Assets", "slow*.*"))
        ];

        /// <summary>
        /// Distinct, sorted encoder suffixes discovered below Testcases and in sibling Testcases.* directories.
        /// </summary>
        [DatapointSource]
        public static readonly string[] TestcaseEncoderSuffixes =
            TestUtils.DiscoverTestcaseEncoderSuffixes("Testcases");

        protected abstract Type FuzzableCodeType { get; }

        /// <summary>
        /// Represents a fuzz target that consumes its input through a read-only byte span.
        /// </summary>
        /// <param name="span">Input bytes to pass directly to the target.</param>
        public delegate void LibFuzzTemplate(ReadOnlySpan<byte> span);

        /// <summary>
        /// Captures target signatures and input identities, digests, and categories before replay
        /// so the evidence describes a fixed input and target scope.
        /// </summary>
        [OneTimeSetUp]
        public void FreezeReplayScope()
        {
            foreach (FuzzTargetFunction target in CreateFuzzTargetFunctions(FuzzableCodeType))
            {
                m_targets.Add(Hash(Encoding.UTF8.GetBytes(target.MethodInfo.ToString())));
            }
            foreach ((TestcaseAsset[] assets, string category) in new[]
            {
                (GoodTestcases, "good"), (CrashAssets, "crash"),
                (TimeoutAssets, "timeout"), (SlowAssets, "slow")
            })
            {
                foreach (TestcaseAsset asset in assets)
                {
                    m_inputs.Add(GetInputId(asset.Path), (Hash(asset.Testcase), category));
                }
            }
        }

        /// <summary>
        /// Writes the frozen target/input inventory and successful file-backed executions to replay XML
        /// and attaches the evidence file to the NUnit result.
        /// </summary>
        /// <remarks>
        /// Uses OPCUA_ASSURANCE_REPLAY_PATH when set; otherwise writes an assembly-named replay file
        /// in the NUnit work directory. Paths and target signatures are represented by their hashes.
        /// </remarks>
        [OneTimeTearDown]
        public void WriteObservedReplay()
        {
            string path = Environment.GetEnvironmentVariable("OPCUA_ASSURANCE_REPLAY_PATH");
            if (string.IsNullOrEmpty(path))
            {
                path = Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    FuzzableCodeType.Assembly.GetName().Name + ".replay.xml");
            }
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true }))
            {
                writer.WriteStartElement("replay");
                writer.WriteAttributeString("schemaVersion", "1");
                writer.WriteStartElement("targets");
                foreach (string target in m_targets.OrderBy(value => value, StringComparer.Ordinal))
                {
                    writer.WriteStartElement("target");
                    writer.WriteAttributeString("id", target);
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                writer.WriteStartElement("inputs");
                foreach (KeyValuePair<string, (string Digest, string Category)> input in
                    m_inputs.OrderBy(value => value.Key, StringComparer.Ordinal))
                {
                    writer.WriteStartElement("input");
                    writer.WriteAttributeString("id", input.Key);
                    writer.WriteAttributeString("digest", input.Value.Digest);
                    writer.WriteAttributeString("category", input.Value.Category);
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                writer.WriteStartElement("executions");
                foreach (string execution in m_executions.OrderBy(value => value, StringComparer.Ordinal))
                {
                    string[] identity = execution.Split('|');
                    writer.WriteStartElement("execution");
                    writer.WriteAttributeString("target", identity[0]);
                    writer.WriteAttributeString("input", identity[1]);
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
            TestContext.AddTestAttachment(path, "Sanitized public replay execution mappings");
        }

        /// <summary>
        /// Replays one required good seed against the selected target and records a successful execution.
        /// </summary>
        /// <param name="fuzzableCode">Target selected for this replay.</param>
        /// <param name="messageEncoder">Good-seed bytes and their source path.</param>
        [Theory]
        public void FuzzGoodTestcases(
            FuzzTargetFunction fuzzableCode,
            [ValueSource(nameof(GoodTestcases))] TestcaseAsset messageEncoder)
        {
            FuzzTarget(fuzzableCode, messageEncoder.Testcase, messageEncoder.Path);
        }

        /// <summary>
        /// Exercises the selected target with empty input without adding a file-backed replay execution.
        /// </summary>
        /// <param name="fuzzableCode">Target to invoke with an empty byte array.</param>
        [Theory]
        public void FuzzEmptyByteArray(FuzzTargetFunction fuzzableCode)
        {
            FuzzTarget(fuzzableCode, []);
        }

        /// <summary>
        /// Enforces robustness for all known crash inputs and fidelity for curated regressions.
        /// Only successful file-backed replays are recorded in the execution evidence.
        /// </summary>
        /// <param name="fuzzableCode">Target to exercise with every available crash asset.</param>
        [Theory]
        public async Task FuzzCrashAssets(FuzzTargetFunction fuzzableCode)
        {
            var failures = new List<string>();
            var fidelityFindings = new List<string>();
            FuzzReplayDiagnostics diagnostics = CreatePrivateDiagnostics();
            foreach (TestcaseAsset messageEncoder in CrashAssets)
            {
                try
                {
                    FuzzTarget(fuzzableCode, messageEncoder.Testcase, messageEncoder.Path);
                }
                catch (Exception ex)
                {
                    if (IsFidelityFinding(ex) && !IsCuratedAsset(messageEncoder))
                    {
                        fidelityFindings.Add(ex.GetType().Name);
                    }
                    else
                    {
                        failures.Add(ex.GetType().Name);
                    }

                    bool retained = await diagnostics.TryWriteAsync(
                        messageEncoder.Testcase,
                        $"Target: {fuzzableCode.MethodInfo.Name}{Environment.NewLine}" +
                        $"Input: {messageEncoder.Path}{Environment.NewLine}{ex}").ConfigureAwait(false);
                    TestContext.Error.WriteLine(retained
                        ? "Replay finding recorded in private runner-local diagnostics; raw input is not attached."
                        : "Replay finding exceeds private diagnostic retention bounds; raw input is not attached.");
                }
            }

            if (fidelityFindings.Count > 0)
            {
                // Differential targets additionally assert that a decoded value re-encodes to
                // an equivalent representation. That is a property of well formed values, and
                // an externally supplied crash corpus is a set of arbitrary mutated blobs
                // collected for other targets, so it cannot be expected to satisfy it. Report
                // the findings and keep gating those inputs on robustness only. Curated assets
                // in the tree stay strict, and continuous fuzzing still treats a fidelity
                // mismatch as a crash, so new regressions are still caught.
                TestContext.Error.WriteLine(
                    $"{fidelityFindings.Count} external crash assets reported encoding " +
                    $"fidelity findings under target '{fuzzableCode.MethodInfo.Name}'." +
                    Environment.NewLine +
                    string.Join(Environment.NewLine, fidelityFindings));
            }

            // Every corpus gates robustness; curated regressions also gate fidelity.
            // Advisory external fidelity findings never earn successful replay credit.
            Assert.That(
                failures,
                Is.Empty,
                "One or more crash assets reproduced under target " +
                $"'{fuzzableCode.MethodInfo.Name}'. Each surfaced bug must be " +
                "fixed (in the decoder/encoder/parser/whitelist) before the " +
                "corresponding asset is left in place. Failures:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, failures));
        }

        /// <summary>
        /// A fidelity finding means the stack stayed healthy but re-encoded a decoded value
        /// differently. It is reported rather than enforced for externally supplied inputs.
        /// <para>
        /// Matched by name because the shared exception source is linked into every fuzz
        /// target and test assembly, so the runtime types are not reference equal.
        /// </para>
        /// </summary>
        private static bool IsFidelityFinding(Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (string.Equals(
                    current.GetType().FullName,
                    "Opc.Ua.Fuzzing.EncodingFidelityException",
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Curated assets live under Assets/Repo in the tree and are always enforced strictly.
        /// Everything else in the Assets folder is overlaid by the pipeline before the build.
        /// </summary>
        private static bool IsCuratedAsset(TestcaseAsset asset)
        {
            string path = asset.Path;
            return path != null &&
                path.Replace('\\', '/')
                    .Contains("/Assets/Repo/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Replays a previous timeout input in a watchdog-controlled process and records successful execution.
        /// </summary>
        /// <param name="fuzzableCode">Target selected for the timeout regression.</param>
        /// <param name="messageEncoder">Timeout regression bytes and their source path.</param>
        [Theory]
        public async Task FuzzTimeoutAssetsAsync(
            FuzzTargetFunction fuzzableCode,
            [ValueSource(nameof(TimeoutAssets))] TestcaseAsset messageEncoder)
        {
            await ReplayWithWatchdogAsync(fuzzableCode, messageEncoder).ConfigureAwait(false);
        }

        /// <summary>
        /// Replays a previously slow input in a watchdog-controlled process and records successful execution.
        /// </summary>
        /// <param name="fuzzableCode">Target selected for the slow-input regression.</param>
        /// <param name="messageEncoder">Slow-input regression bytes and their source path.</param>
        [Theory]
        public async Task FuzzSlowAssetsAsync(
            FuzzTargetFunction fuzzableCode,
            [ValueSource(nameof(SlowAssets))] TestcaseAsset messageEncoder)
        {
            await ReplayWithWatchdogAsync(fuzzableCode, messageEncoder).ConfigureAwait(false);
        }

        /// <summary>
        /// Requires a nonempty corpus and at least one supported fuzz target.
        /// </summary>
        [Test]
        public void RequiredCorpusAndTargetsArePresent()
        {
            Assert.That(GoodTestcases, Is.Not.Empty, "Required fuzz corpus was not copied to the test output.");
            Assert.That(CreateFuzzTargetFunctions(FuzzableCodeType), Is.Not.Empty);
        }

        protected static FuzzTargetFunction[] CreateFuzzTargetFunctions(Type fuzzableCodeType)
        {
            return
            [
                .. fuzzableCodeType
                    .GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .Where(f => f.ReturnType == typeof(void) &&
                        !f.ContainsGenericParameters &&
                        f.GetParameters().Length == 1 &&
                        (f.GetParameters()[0].ParameterType == typeof(Stream) ||
                            f.GetParameters()[0].ParameterType == typeof(string) ||
                            f.GetParameters()[0].ParameterType == typeof(ReadOnlySpan<byte>)))
                    .Select(f => new FuzzTargetFunction(f))
            ];
        }

        protected virtual void OnFuzzTargetSetup(ITelemetryContext telemetry)
        {
        }

        private async Task ReplayWithWatchdogAsync(FuzzTargetFunction target, TestcaseAsset input)
        {
            string file = Path.Combine(Path.GetTempPath(), $"opcua-fuzz-{Guid.NewGuid():N}.bin");
            try
            {
                using (var stream = new FileStream(
                    file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
#if NETFRAMEWORK
                    await stream.WriteAsync(input.Testcase, 0, input.Testcase.Length).ConfigureAwait(false);
#else
                    await stream.WriteAsync(input.Testcase.AsMemory()).ConfigureAwait(false);
#endif
                }
#if NETFRAMEWORK
                string arguments = string.Empty;
#else
                string arguments = $"\"{FuzzableCodeType.Assembly.Location}\" ";
#endif
                ProcessStartInfo startInfo = CreateReplayStartInfo(
                    $"{arguments}--fuzz-replay {target.MethodInfo.Name} \"{file}\"");
                (int exitCode, bool timedOut, string standardOutput, string standardError) =
                    await FuzzProcessWatchdog.RunAsync(startInfo, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                bool retained = false;
                if (timedOut || exitCode != 0)
                {
                    retained = await CreatePrivateDiagnostics().TryWriteAsync(
                        input.Testcase,
                        $"Target: {target.MethodInfo.Name}{Environment.NewLine}" +
                        $"Input: {input.Path}{Environment.NewLine}" +
                        $"Timed out: {timedOut}; exit code: {exitCode}{Environment.NewLine}" +
                        standardOutput +
                        Environment.NewLine +
                        standardError).ConfigureAwait(false);
                }
                string diagnosticStatus = retained
                    ? "Raw diagnostics were retained privately on the runner."
                    : "No raw diagnostics were retained.";
                Assert.That(timedOut, Is.False,
                    $"Replay exceeded the process budget: {target.MethodInfo.Name}. {diagnosticStatus}");
                Assert.That(exitCode, Is.Zero, $"Replay failed with exit code {exitCode}. {diagnosticStatus}");
                RecordReplay(target, input.Path);
            }
            finally
            {
                File.Delete(file);
            }
        }

        private static ProcessStartInfo CreateReplayStartInfo(string arguments)
        {
#if NETFRAMEWORK
            string executable = typeof(FuzzTargetTestsBase).Assembly.Location;
#else
            string executable = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
#endif
            return new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
        }

        /// <summary>
        /// Requires malformed child-process replay requests to fail instead of returning success without execution.
        /// </summary>
        /// <param name="replayArguments">The invalid replay request sent to the child.</param>
        [TestCase("MissingFuzzTarget \"no-such-input\"")]
        [TestCase("OnlyOneArgument")]
        public async Task MalformedReplayRequestFailsInsteadOfReportingSuccessAsync(string replayArguments)
        {
            // The timeout/slow regressions assert a zero exit, so a replay child that
            // cannot run the requested input must fail rather than fall through to the
            // benchmark host and report a pass for an input that was never replayed.
#if NETFRAMEWORK
            string prefix = string.Empty;
#else
            string prefix = $"\"{typeof(FuzzTargetTestsBase).Assembly.Location}\" ";
#endif
            ProcessStartInfo startInfo = CreateReplayStartInfo(
                $"{prefix}--fuzz-replay {replayArguments}");

            (int exitCode, bool timedOut, _, _) =
                await FuzzProcessWatchdog.RunAsync(startInfo, TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            Assert.That(timedOut, Is.False);
            Assert.That(exitCode, Is.Not.Zero);
        }

        private void FuzzTarget(FuzzTargetFunction fuzzableCode, byte[] blob, string path = null)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            OnFuzzTargetSetup(telemetry);

            ParameterInfo[] parameters = fuzzableCode.MethodInfo.GetParameters();
            if (parameters.Length != 1)
            {
                throw new InvalidOperationException(
                    "Fuzzable function must have exactly one parameter.");
            }
            if (parameters[0].ParameterType == typeof(string))
            {
                string text = Encoding.UTF8.GetString(blob);
                _ = fuzzableCode.MethodInfo.Invoke(null, [text]);
            }
            else if (typeof(Stream).IsAssignableFrom(parameters[0].ParameterType))
            {
                using var stream = new MemoryStream(blob);
                _ = fuzzableCode.MethodInfo.Invoke(null, [stream]);
            }
            else if (parameters[0].ParameterType == typeof(ReadOnlySpan<byte>))
            {
                var span = new ReadOnlySpan<byte>(blob);
#if NET8_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
                LibFuzzTemplate fuzzFunction = fuzzableCode.MethodInfo
                    .CreateDelegate<LibFuzzTemplate>();
#else
                var fuzzFunction = (LibFuzzTemplate)fuzzableCode.MethodInfo
                    .CreateDelegate(typeof(LibFuzzTemplate));
#endif
                fuzzFunction(span);
            }
            else
            {
                throw new InvalidOperationException("Unsupported fuzz target signature.");
            }
            if (path != null)
            {
                RecordReplay(fuzzableCode, path);
            }
        }

        private void RecordReplay(FuzzTargetFunction target, string path)
        {
            string targetId = Hash(Encoding.UTF8.GetBytes(target.MethodInfo.ToString()));
            lock (m_evidenceLock)
            {
                m_executions.Add(targetId + "|" + GetInputId(path));
            }
        }

        private static FuzzReplayDiagnostics CreatePrivateDiagnostics()
        {
            return new FuzzReplayDiagnostics(Path.Combine(
                Path.GetTempPath(), "opcua-fuzz-private", Guid.NewGuid().ToString("N")));
        }

        private static string GetInputId(string path)
        {
            string root = Path.GetFullPath(TestContext.CurrentContext.TestDirectory)
                .TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Replay input is outside the test output directory.");
            }
            return Hash(Encoding.UTF8.GetBytes(fullPath[root.Length..].Replace('\\', '/')));
        }

        private static string Hash(byte[] value)
        {
#if NET9_0_OR_GREATER
            return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(value));
#elif NET6_0_OR_GREATER
            return "sha256:" + Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
#else
            using var sha = SHA256.Create();
            return "sha256:" + BitConverter.ToString(sha.ComputeHash(value))
                .Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
#endif
        }

        private readonly HashSet<string> m_targets = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string Digest, string Category)> m_inputs = new(StringComparer.Ordinal);
        private readonly HashSet<string> m_executions = new(StringComparer.Ordinal);
        private readonly Lock m_evidenceLock = new();
    }
}
