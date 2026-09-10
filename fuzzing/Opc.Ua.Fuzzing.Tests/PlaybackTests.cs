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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Fuzzing.Tests
{
    [TestFixture]
    [Category("Fuzzing")]
    [NonParallelizable]
    public sealed class PlaybackTests : IDisposable
    {
        [SetUp]
        public void SetUp()
        {
            FuzzableCode.Reset();
            m_inputs = new TestInputDirectory();
            m_telemetry = TelemetryExtensions.InternalOnly__TelemetryHook();
        }

        [TestCase(nameof(FuzzableCode.ThrowingStreamTarget))]
        [TestCase(nameof(FuzzableCode.ThrowingStringTarget))]
        [TestCase(nameof(FuzzableCode.ThrowingSpanTarget))]
        public async Task ASingleParserFailureStillThrowsAnAggregateWithItsOriginalCauseAsync(string target)
        {
            byte[] input = [0x41];
            string file = await m_inputs.WriteAsync("single failing input", input).ConfigureAwait(false);
            var injected = new FormatException("Injected parser failure.");
            FuzzableCode.InjectedFailure = injected;

            AggregateException failure = Assert.Throws<AggregateException>(() => Run(file, target));

            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(failure.InnerExceptions[0].Message, Is.EqualTo($"Target {target}, input {file}"));
            Assert.That(failure.InnerExceptions[0].InnerException, Is.SameAs(injected));
            Assert.That(FuzzableCode.Invocations.Select(call => call.Target), Is.EqualTo(new[] { target }));
            Assert.That(FuzzableCode.Invocations[0].Input, Is.EqualTo(input));
            Assert.That(m_standardOutput, Does.Contain(target + ": " + file));
            Assert.That(OutputLines(m_standardError), Is.EqualTo(new[] { injected.Message }));
        }

        [TearDown]
        public void Dispose()
        {
            m_inputs?.Dispose();
        }

        [TestCase(nameof(FuzzableCode.StreamTarget))]
        [TestCase(nameof(FuzzableCode.StringTarget))]
        [TestCase(nameof(FuzzableCode.SpanTarget))]
        public async Task NamedReplayInvokesOnlyTheRequestedAdapterAsync(string target)
        {
            byte[] input = [0x41, 0x00, 0xc3, 0xa9];
            string file = await m_inputs.WriteAsync("input with spaces", input).ConfigureAwait(false);

            Run(file, target);

            Assert.That(FuzzableCode.Invocations.Select(call => call.Target), Is.EqualTo(new[] { target }));
            Assert.That(FuzzableCode.Invocations[0].Input, Is.EqualTo(input));
            Assert.That(FuzzableCode.FuzzInfoCalls, Is.Zero);
            Assert.That(m_standardOutput, Does.Contain(target + ": " + file));
            Assert.That(m_standardError, Is.Empty);
        }

        [TestCase(nameof(FuzzableCode.ThrowingStreamTarget), false)]
        [TestCase(nameof(FuzzableCode.ThrowingStreamTarget), true)]
        [TestCase(nameof(FuzzableCode.ThrowingStringTarget), false)]
        [TestCase(nameof(FuzzableCode.ThrowingStringTarget), true)]
        [TestCase(nameof(FuzzableCode.ThrowingSpanTarget), false)]
        [TestCase(nameof(FuzzableCode.ThrowingSpanTarget), true)]
        public async Task NamedReplayAggregatesAllInputFailuresAndPreservesTheirCausesAsync(
            string target,
            bool stackTrace)
        {
            string last = await m_inputs.WriteAsync("z-last", [0x7a]).ConfigureAwait(false);
            string middle = await m_inputs.WriteAsync(Path.Combine("a-middle corpus", "input"), [0x6d])
                .ConfigureAwait(false);
            string first = await m_inputs.WriteAsync("Z-first", [0x61]).ConfigureAwait(false);
            string[] files = [first, middle, last];

            AggregateException failure =
                Assert.Throws<AggregateException>(() => Run(m_inputs.DirectoryPath, target, stackTrace));

            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(3).And.All.TypeOf<InvalidOperationException>());
            Assert.That(failure.InnerExceptions.Select(exception => exception.Message),
                Is.EqualTo(files.Select(file => $"Target {target}, input {file}")));
            Assert.That(failure.InnerExceptions.Select(exception => exception.InnerException),
                Is.All.SameAs(FuzzableCode.InjectedFailure));
            Assert.That(FuzzableCode.Invocations.Select(call => call.Target),
                Is.EqualTo(new[] { target, target, target }));
            Assert.That(FuzzableCode.Invocations.Select(call => call.Input),
                Is.EqualTo(new[] { new byte[] { 0x61 }, new byte[] { 0x6d }, new byte[] { 0x7a } }));
            Assert.That(OutputLines(m_standardOutput), Has.Length.EqualTo(3));
            foreach (string file in files)
            {
                Assert.That(m_standardOutput, Does.Contain(target + ": " + file));
            }
            if (stackTrace)
            {
                Assert.That(m_standardError, Does.Contain("System.InvalidOperationException: ")
                    .And.Contain(FuzzableCode.InjectedFailure.Message).And.Contain("FuzzableCode." + target));
            }
            else
            {
                Assert.That(OutputLines(m_standardError),
                    Is.EqualTo(Enumerable.Repeat(FuzzableCode.InjectedFailure.Message, 3)));
            }
            Assert.That(FuzzableCode.FuzzInfoCalls, Is.Zero);
        }

        [TestCase(null)]
        [TestCase("")]
        public async Task DefaultReplayCollectsFailuresWithoutSkippingOtherSpanTargetsOrInputsAsync(
            string selectedTarget)
        {
            string last = await m_inputs.WriteAsync("z-last", [0x7a]).ConfigureAwait(false);
            string first = await m_inputs.WriteAsync("A-first", [0x61]).ConfigureAwait(false);
            string[] targets =
            [
                nameof(FuzzableCode.SpanTarget),
                nameof(FuzzableCode.ThrowingSpanTarget),
                nameof(FuzzableCode.HangingSpanTarget)
            ];

            AggregateException failure =
                Assert.Throws<AggregateException>(() => Run(m_inputs.DirectoryPath, selectedTarget));

            Assert.That(failure.InnerExceptions.Select(exception => exception.Message), Is.EqualTo(new[]
            {
                $"Target {nameof(FuzzableCode.ThrowingSpanTarget)}, input {first}",
                $"Target {nameof(FuzzableCode.ThrowingSpanTarget)}, input {last}"
            }));
            Assert.That(failure.InnerExceptions.Select(exception => exception.InnerException),
                Is.All.SameAs(FuzzableCode.InjectedFailure));
            Assert.That(FuzzableCode.Invocations, Has.Count.EqualTo(6));
            Assert.That(FuzzableCode.Invocations.Select(call => call.Target).Distinct(), Is.EquivalentTo(targets));
            foreach (string target in targets)
            {
                Assert.That(FuzzableCode.Invocations.Where(call => call.Target == target).Select(call => call.Input),
                    Is.EqualTo(new[] { new byte[] { 0x61 }, new byte[] { 0x7a } }));
                Assert.That(m_standardOutput, Does.Contain(target + ": " + first).And.Contain(target + ": " + last));
            }
            Assert.That(OutputLines(m_standardOutput), Has.Length.EqualTo(6));
            Assert.That(OutputLines(m_standardError),
                Is.EqualTo(Enumerable.Repeat(FuzzableCode.InjectedFailure.Message, 2)));
        }

        [Test]
        public async Task MatchingGlobReplaysOnlyMatchingFilesInOrdinalOrderAsync()
        {
            string last = await m_inputs.WriteAsync("crash-z", [0x7a]).ConfigureAwait(false);
            _ = await m_inputs.WriteAsync("unrelated.bin", [0x75]).ConfigureAwait(false);
            string first = await m_inputs.WriteAsync("crash-A", [0x61]).ConfigureAwait(false);
            string pattern = Path.Combine(m_inputs.DirectoryPath, "crash-*");

            Run(pattern, nameof(FuzzableCode.SpanTarget));

            Assert.That(FuzzableCode.Invocations.Select(call => call.Target),
                Is.EqualTo(new[] { nameof(FuzzableCode.SpanTarget), nameof(FuzzableCode.SpanTarget) }));
            Assert.That(FuzzableCode.Invocations.Select(call => call.Input),
                Is.EqualTo(new[] { new byte[] { 0x61 }, new byte[] { 0x7a } }));
            Assert.That(m_standardOutput, Does.Contain(nameof(FuzzableCode.SpanTarget) + ": " + first)
                .And.Contain(nameof(FuzzableCode.SpanTarget) + ": " + last).And.Not.Contain("unrelated.bin"));
            Assert.That(m_standardError, Is.Empty);
        }

        [TestCase("MissingTarget")]
        [TestCase(nameof(FuzzableCode.FuzzInfo))]
        public async Task InvalidTargetCannotBeReportedAsSuccessfulReplayAsync(string target)
        {
            string file = await m_inputs.WriteAsync("input", [0x41]).ConfigureAwait(false);

            ArgumentException failure = Assert.Throws<ArgumentException>(() => Run(file, target));

            Assert.That(failure.ParamName, Is.EqualTo("target"));
            Assert.That(failure.Message, Does.Contain("Unknown fuzz target: " + target));
            Assert.That(FuzzableCode.Invocations, Is.Empty);
            Assert.That(m_standardOutput, Is.Empty);
            Assert.That(m_standardError, Does.Contain("The fuzzing function " + target));
        }

        [Test]
        public void MissingFileCannotBeReportedAsSuccessfulReplay()
        {
            string file = Path.Combine(m_inputs.DirectoryPath, "missing-input");

            InvalidOperationException failure =
                Assert.Throws<InvalidOperationException>(() => Run(file, nameof(FuzzableCode.SpanTarget)));

            Assert.That(failure.Message, Is.EqualTo("Replay input contains no files: " + file));
            Assert.That(FuzzableCode.Invocations, Is.Empty);
            Assert.That(m_standardOutput, Is.Empty);
            Assert.That(m_standardError, Is.Empty);
        }

        [Test]
        public async Task UnmatchedGlobFailsEvenWhenTheDirectoryContainsOtherFilesAsync()
        {
            _ = await m_inputs.WriteAsync("unrelated.bin", [0x41]).ConfigureAwait(false);
            string pattern = Path.Combine(m_inputs.DirectoryPath, "crash-*");

            InvalidOperationException failure =
                Assert.Throws<InvalidOperationException>(() => Run(pattern, nameof(FuzzableCode.SpanTarget)));

            Assert.That(failure.Message, Is.EqualTo("Replay input contains no files: " + pattern));
            Assert.That(FuzzableCode.Invocations, Is.Empty);
            Assert.That(m_standardOutput, Is.Empty);
            Assert.That(m_standardError, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EmptyCorpusCannotBeReportedAsSuccessfulReplay(bool nestedDirectory)
        {
            if (nestedDirectory)
            {
                Directory.CreateDirectory(Path.Combine(m_inputs.DirectoryPath, "empty-directory"));
            }

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                () => Run(m_inputs.DirectoryPath, nameof(FuzzableCode.SpanTarget)));

            Assert.That(failure.Message, Is.EqualTo("Replay input contains no files: " + m_inputs.DirectoryPath));
            Assert.That(FuzzableCode.Invocations, Is.Empty);
            Assert.That(m_standardOutput, Is.Empty);
            Assert.That(m_standardError, Is.Empty);
        }

        [Test]
        public void MissingParentDirectoryPropagatesTheIoFailure()
        {
            string file = Path.Combine(m_inputs.DirectoryPath, "missing-directory", "input");

            Assert.That(() => Run(file, nameof(FuzzableCode.SpanTarget)), Throws.TypeOf<DirectoryNotFoundException>());

            Assert.That(FuzzableCode.Invocations, Is.Empty);
            Assert.That(m_standardOutput, Is.Empty);
            Assert.That(m_standardError, Is.Empty);
        }

        [Test]
        public void NullTelemetryIsRejectedBeforeReplay()
        {
            ArgumentNullException failure = Assert.Throws<ArgumentNullException>(
                () => Playback.Run(m_inputs.DirectoryPath, false, null, nameof(FuzzableCode.SpanTarget)));

            Assert.That(failure.ParamName, Is.EqualTo("telemetry"));
            Assert.That(FuzzableCode.Invocations, Is.Empty);
        }

        private static string[] OutputLines(string output)
        {
            return output.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries);
        }

        private void Run(string input, string target = null, bool stackTrace = false)
        {
            TextWriter previousOutput = Console.Out;
            TextWriter previousError = Console.Error;
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            try
            {
                Console.SetOut(output);
                Console.SetError(error);
                if (target == null)
                {
                    Playback.Run(input, stackTrace, m_telemetry);
                }
                else
                {
                    Playback.Run(input, stackTrace, m_telemetry, target);
                }
            }
            finally
            {
                Console.SetOut(previousOutput);
                Console.SetError(previousError);
                m_standardOutput = output.ToString();
                m_standardError = error.ToString();
            }
        }

        private TestInputDirectory m_inputs;
        private ITelemetryContext m_telemetry;
        private string m_standardOutput;
        private string m_standardError;
    }
}
