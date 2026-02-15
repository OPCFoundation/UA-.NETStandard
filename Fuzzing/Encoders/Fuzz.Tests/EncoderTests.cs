/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Fuzzing
{
    [TestFixture]
    [Category("Fuzzing")]
    public class EncoderTests
    {
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
        public static readonly string[] TestcaseEncoderSuffixes = [".Binary", ".Json", ".Xml"];

        [DatapointSource]
        public static readonly FuzzTargetFunction[] FuzzableFunctions =
        [
            .. typeof(FuzzableCode)
                .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Where(f => f.GetParameters().Length == 1)
                .Select(f => new FuzzTargetFunction(f))
        ];

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
            // note: too many crash files can take forever to create
            // all permutations with nunit, so just run all in one batch
            int exceptions = 0;
            foreach (TestcaseAsset messageEncoder in CrashAssets)
            {
                try
                {
                    TestContext.Out.WriteLine(messageEncoder);
                    FuzzTarget(fuzzableCode, messageEncoder.Testcase);
                }
                catch (Exception ex)
                {
                    TestContext.Error.WriteLine($"Failed: {messageEncoder}\n{ex}");
                    exceptions++;
                }
            }
            // Assert.That(exceptions, Is.EqualTo(0));
        }

        [Theory]
        [CancelAfter(1000)]
        public void FuzzTimeoutAssets(
            FuzzTargetFunction fuzzableCode,
            [ValueSource(nameof(TimeoutAssets))] TestcaseAsset messageEncoder)
        {
            FuzzTarget(fuzzableCode, messageEncoder.Testcase);
        }

        [Theory]
        [CancelAfter(1000)]
        public void FuzzSlowAssets(
            FuzzTargetFunction fuzzableCode,
            [ValueSource(nameof(SlowAssets))] TestcaseAsset messageEncoder)
        {
            FuzzTarget(fuzzableCode, messageEncoder.Testcase);
        }

        public delegate void LibFuzzTemplate(ReadOnlySpan<byte> span);

        private static void FuzzTarget(FuzzTargetFunction fuzzableCode, byte[] blob)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            FuzzableCode.MessageContext = new ServiceMessageContext(telemetry);

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
        }
    }

    /// <summary>
    /// A Testcase as test asset.
    /// </summary>
    public class TestcaseAsset : IAsset, IFormattable
    {
        public string Path { get; private set; }
        public byte[] Testcase { get; private set; }

        public void Initialize(byte[] blob, string path)
        {
            Path = path;
            Testcase = blob;
        }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            string file = System.IO.Path.GetFileName(Path);
            return $"{file}";
        }
    }

    /// <summary>
    /// A Testcase as test asset.
    /// </summary>
    public class FuzzTargetFunction : IFormattable
    {
        public FuzzTargetFunction(MethodInfo methodInfo)
        {
            MethodInfo = methodInfo;
        }

        public MethodInfo MethodInfo { get; }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            string name = MethodInfo.Name;
            return $"{name}";
        }
    }
}
