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
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Fuzzing
{
    [TestFixture]
    [Category("Fuzzing")]
    public abstract class FuzzTargetTestsBase
    {
        private const int kMaxEmittedReproducers = 60;
        private const int kMaxEmittedReproducerBytes = 4096;

        public static readonly TestcaseAsset[] GoodTestcases =
        [
            .. AssetCollection<TestcaseAsset>.CreateFromFiles(
                TestUtils.EnumerateTestAssets("Testcases", "*.*"))
        ];

        public static readonly TestcaseAsset[] CrashAssets =
        [
            .. AssetCollection<TestcaseAsset>.CreateFromFiles(
                TestUtils.EnumerateTestAssets("Assets", "crash*.*"))
        ];

        public static readonly TestcaseAsset[] TimeoutAssets =
        [
            .. AssetCollection<TestcaseAsset>.CreateFromFiles(
                TestUtils.EnumerateTestAssets("Assets", "timeout*.*"))
        ];

        public static readonly TestcaseAsset[] SlowAssets =
        [
            .. AssetCollection<TestcaseAsset>.CreateFromFiles(
                TestUtils.EnumerateTestAssets("Assets", "slow*.*"))
        ];

        [DatapointSource]
        public static readonly string[] TestcaseEncoderSuffixes =
            TestUtils.DiscoverTestcaseEncoderSuffixes("Testcases");

        protected abstract Type FuzzableCodeType { get; }

        public delegate void LibFuzzTemplate(ReadOnlySpan<byte> span);

        [Theory]
        public void FuzzGoodTestcases(
            FuzzTargetFunction fuzzableCode,
            [ValueSource(nameof(GoodTestcases))] TestcaseAsset messageEncoder)
        {
            FuzzTarget(fuzzableCode, messageEncoder.Testcase);
        }

        [Theory]
        public void FuzzEmptyByteArray(FuzzTargetFunction fuzzableCode)
        {
            FuzzTarget(fuzzableCode, []);
        }

        [Theory]
        public void FuzzCrashAssets(FuzzTargetFunction fuzzableCode)
        {
            var failures = new List<string>();
            int reproducers = 0;
            foreach (TestcaseAsset messageEncoder in CrashAssets)
            {
                try
                {
                    TestContext.Out.WriteLine(messageEncoder);
                    FuzzTarget(fuzzableCode, messageEncoder.Testcase);
                }
                catch (Exception ex)
                {
                    failures.Add(
                        $"asset={messageEncoder} -> {ex.GetType().Name}: {ex.Message}");
                    TestContext.Error.WriteLine($"Failed: {messageEncoder}\n{ex}");

                    // Crash corpora are frequently supplied by the pipeline rather than the
                    // tree, so a failure there is otherwise impossible to reproduce locally.
                    // Emit the reproducer inline, bounded so a systemic failure cannot bury
                    // the log. A passing run emits nothing.
                    if (reproducers++ < kMaxEmittedReproducers &&
                        messageEncoder.Testcase.Length <= kMaxEmittedReproducerBytes)
                    {
                        TestContext.Error.WriteLine(
                            $"REPRODUCER {messageEncoder} " +
                            Convert.ToBase64String(messageEncoder.Testcase));
                    }
                }
            }

            // A crash asset under Assets/crash*.* is by definition an input that
            // already produced an unhandled exception in a prior libfuzzer run.
            // The contract is: once the regression has been fixed in the fuzz
            // target (or the matching producer/decoder), replaying the asset
            // through the target must NOT throw any more. The asset's continued
            // existence in the tree therefore acts as a permanent regression
            // gate. See https://github.com/OPCFoundation/UA-.NETStandard/issues/3546
            // for the historical context that flipped this from
            // log-and-swallow to assert-and-fail.
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

        [Theory]
        public async Task FuzzTimeoutAssetsAsync(
            FuzzTargetFunction fuzzableCode,
            [ValueSource(nameof(TimeoutAssets))] TestcaseAsset messageEncoder)
        {
            await ReplayWithWatchdogAsync(fuzzableCode, messageEncoder.Testcase).ConfigureAwait(false);
        }

        [Theory]
        public async Task FuzzSlowAssetsAsync(
            FuzzTargetFunction fuzzableCode,
            [ValueSource(nameof(SlowAssets))] TestcaseAsset messageEncoder)
        {
            await ReplayWithWatchdogAsync(fuzzableCode, messageEncoder.Testcase).ConfigureAwait(false);
        }

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

        private async Task ReplayWithWatchdogAsync(FuzzTargetFunction target, byte[] data)
        {
            string file = Path.Combine(Path.GetTempPath(), $"opcua-fuzz-{Guid.NewGuid():N}.bin");
            File.WriteAllBytes(file, data);
            try
            {
                string assembly = FuzzableCodeType.Assembly.Location;
#if NETFRAMEWORK
                string executable = assembly;
                string arguments = string.Empty;
#else
                string executable = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
                string arguments = $"\"{assembly}\" ";
#endif
                ProcessStartInfo startInfo = CreateReplayStartInfo(
                    $"{arguments}--fuzz-replay {target.MethodInfo.Name} \"{file}\"");
                (int exitCode, bool timedOut, string standardOutput, string standardError) =
                    await FuzzProcessWatchdog.RunAsync(startInfo, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Assert.That(timedOut, Is.False, $"Replay exceeded the process budget: {target.MethodInfo.Name}");
                Assert.That(exitCode, Is.Zero, standardOutput + Environment.NewLine + standardError);
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

        private void FuzzTarget(FuzzTargetFunction fuzzableCode, byte[] blob)
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
        }
    }
}
