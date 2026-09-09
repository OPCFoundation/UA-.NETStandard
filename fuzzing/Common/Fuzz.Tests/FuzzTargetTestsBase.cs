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
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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
        /// Optional crash regression inputs loaded recursively from Assets whose replay must no longer throw.
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
                path = System.IO.Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    FuzzableCodeType.Assembly.GetName().Name + ".replay.xml");
            }
            path = System.IO.Path.GetFullPath(path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            using (XmlWriter writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true }))
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
                foreach (var input in m_inputs.OrderBy(value => value.Key, StringComparer.Ordinal))
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
        /// Replays all known crash regressions against the selected target and fails if any input still throws.
        /// Successful file-backed replays are recorded in the execution evidence.
        /// </summary>
        /// <param name="fuzzableCode">Target to exercise with every available crash asset.</param>
        [Theory]
        public void FuzzCrashAssets(FuzzTargetFunction fuzzableCode)
        {
            var failures = new List<string>();
            foreach (TestcaseAsset messageEncoder in CrashAssets)
            {
                try
                {
                    FuzzTarget(fuzzableCode, messageEncoder.Testcase, messageEncoder.Path);
                }
                catch (Exception ex)
                {
                    failures.Add(ex.GetType().Name);
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

        /// <summary>
        /// Replays a previous timeout input with NUnit cancellation requested after one second.
        /// </summary>
        /// <param name="fuzzableCode">Target selected for the timeout regression.</param>
        /// <param name="messageEncoder">Timeout regression bytes and their source path.</param>
        [Theory]
        [CancelAfter(1000)]
        public void FuzzTimeoutAssets(
            FuzzTargetFunction fuzzableCode,
            [ValueSource(nameof(TimeoutAssets))] TestcaseAsset messageEncoder)
        {
            FuzzTarget(fuzzableCode, messageEncoder.Testcase, messageEncoder.Path);
        }

        /// <summary>
        /// Replays a previously slow input with NUnit cancellation requested after one second.
        /// </summary>
        /// <param name="fuzzableCode">Target selected for the slow-input regression.</param>
        /// <param name="messageEncoder">Slow-input regression bytes and their source path.</param>
        [Theory]
        [CancelAfter(1000)]
        public void FuzzSlowAssets(
            FuzzTargetFunction fuzzableCode,
            [ValueSource(nameof(SlowAssets))] TestcaseAsset messageEncoder)
        {
            FuzzTarget(fuzzableCode, messageEncoder.Testcase, messageEncoder.Path);
        }

        protected static FuzzTargetFunction[] CreateFuzzTargetFunctions(Type fuzzableCodeType)
        {
            return
            [
                .. fuzzableCodeType
                    .GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .Where(f => f.GetParameters().Length == 1)
                    .Select(f => new FuzzTargetFunction(f))
            ];
        }

        protected virtual void OnFuzzTargetSetup(ITelemetryContext telemetry)
        {
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
                throw new InvalidOperationException("Unsupported fuzz target parameter type.");
            }
            if (path != null)
            {
                string target = Hash(Encoding.UTF8.GetBytes(fuzzableCode.MethodInfo.ToString()));
                lock (m_evidenceLock)
                {
                    m_executions.Add(target + "|" + GetInputId(path));
                }
            }
        }

        private static string GetInputId(string path)
        {
            string root = System.IO.Path.GetFullPath(TestContext.CurrentContext.TestDirectory)
                .TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            string fullPath = System.IO.Path.GetFullPath(path);
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
