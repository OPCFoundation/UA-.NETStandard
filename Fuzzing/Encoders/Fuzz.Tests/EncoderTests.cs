/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
    public partial class EncoderTests
    {
        #region DataPointSources
        public static readonly TestcaseAsset[] GoodTestcases = new AssetCollection<TestcaseAsset>(TestUtils.EnumerateTestAssets("Testcases", "*.*")).ToArray();
        public static readonly TestcaseAsset[] CrashAssets = new AssetCollection<TestcaseAsset>(TestUtils.EnumerateTestAssets("Assets", "crash*.*")).ToArray();
        public static readonly TestcaseAsset[] TimeoutAssets = new AssetCollection<TestcaseAsset>(TestUtils.EnumerateTestAssets("Assets", "timeout*.*")).ToArray();
        public static readonly TestcaseAsset[] SlowAssets = new AssetCollection<TestcaseAsset>(TestUtils.EnumerateTestAssets("Assets", "slow*.*")).ToArray();

        [DatapointSource]
        public static readonly string[] TestcaseEncoderSuffixes = new string[] { ".Binary", ".Json", ".Xml" };

        [DatapointSource]
        public static readonly FuzzTargetFunction[] FuzzableFunctions = typeof(FuzzableCode).GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Where(f => f.GetParameters().Length == 1)
            .Select(f => new FuzzTargetFunction(f)).ToArray();
        #endregion

        [Test]
        public void FuzzGoodTestcases(
            [ValueSource(nameof(FuzzableFunctions))] FuzzTargetFunction fuzzableCode,
            [ValueSource(nameof(GoodTestcases))] TestcaseAsset messageEncoder)
        {
            FuzzTarget(fuzzableCode, messageEncoder.Testcase);
        }

        [Test]
        public void FuzzEmptyByteArray(
            [ValueSource(nameof(FuzzableFunctions))] FuzzTargetFunction fuzzableCode)
        {
            FuzzTarget(fuzzableCode, Array.Empty<byte>());
        }

        [Test]
        public void FuzzCrashAssets(
            [ValueSource(nameof(FuzzableFunctions))] FuzzTargetFunction fuzzableCode)
        {
            // note: too many crash files can take forever to create
            // all permutations with nunit, so just run all in one batch
            foreach (var messageEncoder in CrashAssets)
            {
                FuzzTarget(fuzzableCode, messageEncoder.Testcase);
            }
        }

        [Test]
        [CancelAfter(1000)]
        public void FuzzTimeoutAssets(
            [ValueSource(nameof(FuzzableFunctions))] FuzzTargetFunction fuzzableCode,
            [ValueSource(nameof(TimeoutAssets))] TestcaseAsset messageEncoder)
        {
            FuzzTarget(fuzzableCode, messageEncoder.Testcase);
        }

        [Test]
        [CancelAfter(1000)]
        public void FuzzSlowAssets(
            [ValueSource(nameof(FuzzableFunctions))] FuzzTargetFunction fuzzableCode,
            [ValueSource(nameof(SlowAssets))] TestcaseAsset messageEncoder)
        {
            FuzzTarget(fuzzableCode, messageEncoder.Testcase);
        }

        delegate void LibFuzzTemplate(ReadOnlySpan<byte> span);

        private void FuzzTarget(FuzzTargetFunction fuzzableCode, byte[] blob)
        {
            var parameters = fuzzableCode.MethodInfo.GetParameters();
            if (parameters.Length != 1)
            {
                throw new InvalidOperationException("Fuzzable function must have exactly one parameter.");
            }
            if (parameters[0].ParameterType == typeof(string))
            {
                string text = Encoding.UTF8.GetString(blob);
                _ = fuzzableCode.MethodInfo.Invoke(null, new object[] { text });
            }
            else if (typeof(Stream).IsAssignableFrom(parameters[0].ParameterType))
            {
                using (var stream = new MemoryStream(blob))
                {
                    _ = fuzzableCode.MethodInfo.Invoke(null, new object[] { stream });
                }
            }
            else if (parameters[0].ParameterType == typeof(ReadOnlySpan<byte>))
            {
                var span = new ReadOnlySpan<byte>(blob);
                LibFuzzTemplate fuzzFunction = (LibFuzzTemplate)fuzzableCode.MethodInfo.CreateDelegate(typeof(LibFuzzTemplate));
                fuzzFunction(span);
            }
        }
    }
}
