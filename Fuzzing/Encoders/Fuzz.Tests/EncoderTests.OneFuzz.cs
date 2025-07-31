// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Opc.Ua.Fuzzing
{
    [TestFixture]
    [Category("Fuzzing")]
    public partial class EncoderTests
    {
        #region DataPointSources
        [DatapointSource]
        public static readonly FuzzTargetFunction[] OneFuzzFunctions = typeof(OneFuzz.OpcUa.Encoders.FuzzTargets).GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Where(f => f.GetParameters().Length == 1)
            .Select(f => new FuzzTargetFunction(f)).ToArray();
        #endregion

        [Test]
        public void OneFuzzGoodTestcases(
            [ValueSource(nameof(OneFuzzFunctions))] FuzzTargetFunction fuzzableCode,
            [ValueSource(nameof(GoodTestcases))] TestcaseAsset messageEncoder)
        {
            FuzzTarget(fuzzableCode, messageEncoder.Testcase);
        }

        [Test]
        public void OneFuzzEmptyByteArray(
            [ValueSource(nameof(OneFuzzFunctions))] FuzzTargetFunction fuzzableCode)
        {
            FuzzTarget(fuzzableCode, Array.Empty<byte>());
        }

        [Test]
        public void OneFuzzCrashAssets(
            [ValueSource(nameof(OneFuzzFunctions))] FuzzTargetFunction fuzzableCode)
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
        public void OneFuzzTimeoutAssets(
            [ValueSource(nameof(OneFuzzFunctions))] FuzzTargetFunction fuzzableCode,
            [ValueSource(nameof(TimeoutAssets))] TestcaseAsset messageEncoder)
        {
            FuzzTarget(fuzzableCode, messageEncoder.Testcase);
        }

        [Test]
        [CancelAfter(1000)]
        public void OneFuzzSlowAssets(
            [ValueSource(nameof(OneFuzzFunctions))] FuzzTargetFunction fuzzableCode,
            [ValueSource(nameof(SlowAssets))] TestcaseAsset messageEncoder)
        {
            FuzzTarget(fuzzableCode, messageEncoder.Testcase);
        }
    }
}
