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
using NUnit.Framework;

namespace Opc.Ua.Fuzzing.Tests
{
    [TestFixture]
    [Category("Fuzzing")]
    [NonParallelizable]
    public sealed class FuzzMethodsTests
    {
        [SetUp]
        public void SetUp()
        {
            FuzzableCode.Reset();
        }

        [TestCase(typeof(FuzzMethods.AflFuzzStream),
            nameof(FuzzableCode.StreamTarget), nameof(FuzzableCode.ThrowingStreamTarget))]
        [TestCase(typeof(FuzzMethods.AflFuzzString),
            nameof(FuzzableCode.StringTarget), nameof(FuzzableCode.ThrowingStringTarget))]
        [TestCase(typeof(FuzzMethods.LibFuzzSpan),
            nameof(FuzzableCode.SpanTarget), nameof(FuzzableCode.ThrowingSpanTarget),
            nameof(FuzzableCode.HangingSpanTarget))]
        public void DiscoveryReturnsOnlySupportedStaticVoidMethods(
            Type delegateType,
            params string[] targets)
        {
            List<Delegate> methods = FuzzMethods.FindFuzzMethods(delegateType);

            Assert.That(methods.Select(method => method.Method.Name),
                Is.EquivalentTo(targets));
            Assert.That(methods, Is.All.TypeOf(delegateType));
            Assert.That(methods.Select(method => method.Target), Is.All.Null);
            Assert.That(FuzzableCode.Invocations, Is.Empty);
            Assert.That(FuzzableCode.FuzzInfoCalls, Is.Zero);
        }

        [TestCase(typeof(Action<Stream>))]
        [TestCase(typeof(Action<string>))]
        [TestCase(typeof(Program.LibFuzzSpan))]
        [TestCase(typeof(string))]
        public void DiscoveryRejectsUnsupportedDelegateTypes(Type delegateType)
        {
            Assert.That(FuzzMethods.FindFuzzMethods(delegateType), Is.Empty);
            Assert.That(FuzzableCode.Invocations, Is.Empty);
        }

        [TestCase(nameof(FuzzableCode.StreamTarget), typeof(FuzzMethods.AflFuzzStream))]
        [TestCase(nameof(FuzzableCode.StringTarget), typeof(FuzzMethods.AflFuzzString))]
        [TestCase(nameof(FuzzableCode.SpanTarget), typeof(FuzzMethods.LibFuzzSpan))]
        [TestCase(nameof(FuzzableCode.ThrowingStreamTarget), typeof(FuzzMethods.AflFuzzStream))]
        [TestCase(nameof(FuzzableCode.ThrowingStringTarget), typeof(FuzzMethods.AflFuzzString))]
        [TestCase(nameof(FuzzableCode.ThrowingSpanTarget), typeof(FuzzMethods.LibFuzzSpan))]
        [TestCase(nameof(FuzzableCode.HangingSpanTarget), typeof(FuzzMethods.LibFuzzSpan))]
        public void NamedDiscoveryBindsTheExactDelegate(string target, Type delegateType)
        {
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            Delegate method = FuzzMethods.FindFuzzMethod(error, target);

            Assert.That(method, Is.TypeOf(delegateType));
            Assert.That(method.Method.Name, Is.EqualTo(target));
            Assert.That(method.Target, Is.Null);
            Assert.That(error.ToString(), Is.Empty);
            Assert.That(FuzzableCode.Invocations, Is.Empty);
        }

        [TestCaseSource(typeof(FuzzableCode), nameof(FuzzableCode.InvalidTargetNames))]
        [TestCase("MissingTarget")]
        [TestCase("spantarget")]
        [TestCase("")]
        public void NamedDiscoveryRejectsInvalidOrMissingTargets(string target)
        {
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            Delegate method = FuzzMethods.FindFuzzMethod(error, target);

            Assert.That(method, Is.Null);
            Assert.That(error.ToString(), Does.Contain("The fuzzing function " + target));
            Assert.That(FuzzableCode.Invocations, Is.Empty);
            Assert.That(FuzzableCode.FuzzInfoCalls, Is.Zero);
        }

        [TestCase(nameof(FuzzableCode.StreamTarget))]
        [TestCase(nameof(FuzzableCode.StringTarget))]
        [TestCase(nameof(FuzzableCode.SpanTarget))]
        [TestCase(nameof(FuzzableCode.HangingSpanTarget))]
        public void NamedDelegateReplaysOnlyTheRequestedTarget(string target)
        {
            byte[] input = [0x41, 0x00, 0xc3, 0xa9];
            Delegate method = FuzzMethods.FindFuzzMethod(TextWriter.Null, target);

            FuzzMethods.Replay(method, input);

            Assert.That(FuzzableCode.Invocations.Select(call => call.Target), Is.EqualTo(new[] { target }));
            Assert.That(FuzzableCode.Invocations[0].Input, Is.EqualTo(input));
            Assert.That(FuzzableCode.FuzzInfoCalls, Is.Zero);
        }

        [TestCase(0)]
        [TestCase(4)]
        public void StreamReplayUsesOnlyTheInputSliceAndDisposesTheReadOnlyStream(int length)
        {
            byte[] buffer = [0x58, 0x00, 0xff, 0xc3, 0xa9, 0x59];
            byte[] expected = length == 0 ? [] : [0x00, 0xff, 0xc3, 0xa9];
            byte[] actual = null;
            Stream observedStream = null;
            int calls = 0;
            FuzzMethods.AflFuzzStream target = stream =>
            {
                calls++;
                observedStream = stream;
                Assert.That(stream.Position, Is.Zero);
                Assert.That(stream.CanRead, Is.True);
                Assert.That(stream.CanWrite, Is.False);
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                actual = copy.ToArray();
            };

            FuzzMethods.Replay(target, buffer.AsSpan(1, length));

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(observedStream.CanRead, Is.False);
            Assert.That(() => observedStream.ReadByte(), Throws.TypeOf<ObjectDisposedException>());
        }

        [TestCase(0)]
        [TestCase(4)]
        public void SpanReplayInvokesTheTargetOnceWithOnlyTheInputSlice(int length)
        {
            byte[] buffer = [0x58, 0x00, 0xff, 0xc3, 0xa9, 0x59];
            byte[] expected = length == 0 ? [] : [0x00, 0xff, 0xc3, 0xa9];
            byte[] actual = null;
            int calls = 0;
            FuzzMethods.LibFuzzSpan target = input =>
            {
                calls++;
                actual = input.ToArray();
            };

            FuzzMethods.Replay(target, buffer.AsSpan(1, length));

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(StringReplayInputs))]
        public void StringReplayDecodesTheUtf8SliceExactlyOnce(byte[] input, string expected)
        {
            byte[] buffer = [0x58, .. input, 0x59];
            string actual = null;
            int calls = 0;
            FuzzMethods.AflFuzzString target = text =>
            {
                calls++;
                actual = text;
            };

            FuzzMethods.Replay(target, buffer.AsSpan(1, input.Length));

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCase(nameof(FuzzableCode.ThrowingStreamTarget))]
        [TestCase(nameof(FuzzableCode.ThrowingStringTarget))]
        [TestCase(nameof(FuzzableCode.ThrowingSpanTarget))]
        public void ReplayPropagatesTheOriginalTargetException(string target)
        {
            byte[] input = [0x41, 0x00, 0xc3, 0xa9];
            Delegate method = FuzzMethods.FindFuzzMethod(TextWriter.Null, target);

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() => FuzzMethods.Replay(method, input));

            Assert.That(exception, Is.SameAs(FuzzableCode.InjectedFailure));
            Assert.That(FuzzableCode.Invocations.Select(call => call.Target), Is.EqualTo(new[] { target }));
            Assert.That(FuzzableCode.Invocations[0].Input, Is.EqualTo(input));
        }

        [Test]
        public void StreamReplayDisposesTheStreamWhenTheTargetThrows()
        {
            Stream observedStream = null;
            FuzzMethods.AflFuzzStream target = stream =>
            {
                observedStream = stream;
                throw FuzzableCode.InjectedFailure;
            };

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() => FuzzMethods.Replay(target, new byte[] { 0x41 }));

            Assert.That(exception, Is.SameAs(FuzzableCode.InjectedFailure));
            Assert.That(observedStream.CanRead, Is.False);
        }

        [Test]
        public void ReplayRejectsAnUnsupportedDelegateEvenWithAMatchingSignature()
        {
            int calls = 0;
            Action<Stream> unsupported = _ => calls++;

            ArgumentException exception =
                Assert.Throws<ArgumentException>(() => FuzzMethods.Replay(unsupported, new byte[] { 0x41 }));

            Assert.That(exception.ParamName, Is.EqualTo("fuzzingMethod"));
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void ReplayRejectsANullDelegate()
        {
            ArgumentException exception =
                Assert.Throws<ArgumentException>(() => FuzzMethods.Replay(null, ReadOnlySpan<byte>.Empty));

            Assert.That(exception.ParamName, Is.EqualTo("fuzzingMethod"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RunningAnUnsupportedDelegateThrowsWithoutStartingAFuzzer(bool outOfProcess)
        {
            int calls = 0;
            Action unsupported = () => calls++;

            ArgumentException exception =
                Assert.Throws<ArgumentException>(() => FuzzMethods.RunFuzzMethod(unsupported, outOfProcess));

            Assert.That(exception.ParamName, Is.EqualTo("fuzzingMethod"));
            Assert.That(calls, Is.Zero);
        }

        private static IEnumerable<TestCaseData> StringReplayInputs()
        {
            yield return new TestCaseData(Array.Empty<byte>(), string.Empty).SetName("StringReplayAcceptsEmptyInput");
            yield return new TestCaseData(
                new byte[] { 0x41, 0x00, 0x0d, 0x0a, 0xc3, 0xa9, 0xe2, 0x82, 0xac, 0xf0, 0x9f, 0x9a, 0x80 },
                "A\0\r\n\u00e9\u20ac\U0001f680").SetName("StringReplayDecodesMultibyteUtf8AndPreservesControls");
            yield return new TestCaseData(
                new byte[] { 0x66, 0x80, 0x6f },
                "f\ufffdo").SetName("StringReplayReplacesMalformedUtf8");
        }
    }
}
