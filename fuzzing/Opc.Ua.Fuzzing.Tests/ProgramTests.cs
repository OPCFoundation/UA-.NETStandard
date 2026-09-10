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
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Fuzzing.Tests
{
    [TestFixture]
    [Category("Fuzzing")]
    [NonParallelizable]
    public sealed class ProgramTests : IDisposable
    {
        [SetUp]
        public void SetUp()
        {
            FuzzableCode.Reset();
            m_inputs = new TestInputDirectory();
            m_directory = m_inputs.DirectoryPath;
        }

        [TearDown]
        public void Dispose()
        {
            m_inputs?.Dispose();
        }

        [Test]
        public void ListEmitsOnlySupportedTargetNamesWithoutInvokingThem()
        {
            int exitCode = Run("--list");

            Assert.That(exitCode, Is.Zero);
            Assert.That(m_standardOutput.Split([Environment.NewLine], StringSplitOptions.None),
                Is.EquivalentTo(new[]
                {
                    nameof(FuzzableCode.StreamTarget),
                    nameof(FuzzableCode.StringTarget),
                    nameof(FuzzableCode.SpanTarget),
                    nameof(FuzzableCode.ThrowingStreamTarget),
                    nameof(FuzzableCode.ThrowingStringTarget),
                    nameof(FuzzableCode.ThrowingSpanTarget),
                    nameof(FuzzableCode.HangingSpanTarget),
                    string.Empty
                }));
            Assert.That(m_standardError, Is.Empty);
            Assert.That(FuzzableCode.Invocations, Is.Empty);
            Assert.That(FuzzableCode.FuzzInfoCalls, Is.Zero);
        }

        [Test]
        public void HelpReturnsSuccessAndWritesUsageWithoutInvokingTargets()
        {
            int exitCode = Run("--help");

            Assert.That(exitCode, Is.Zero);
            Assert.That(m_standardOutput, Is.Empty);
            Assert.That(m_standardError, Does.Contain("Usage:").And.Contain("--list").And.Contain("--replay"));
            Assert.That(m_standardError, Does.Contain(nameof(FuzzableCode.StreamTarget))
                .And.Contain(nameof(FuzzableCode.StringTarget)).And.Contain(nameof(FuzzableCode.SpanTarget)));
            Assert.That(FuzzableCode.Invocations, Is.Empty);
            Assert.That(FuzzableCode.FuzzInfoCalls, Is.Zero);
        }

        [TestCaseSource(nameof(InvalidArguments))]
        public void InvalidArgumentsReturnUsageErrorWithoutInvokingTargets(string[] args)
        {
            int exitCode = Run(args);

            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(m_standardError, Does.Contain("Usage:"));
            Assert.That(m_standardOutput, Is.Empty);
            Assert.That(FuzzableCode.Invocations, Is.Empty);
            Assert.That(FuzzableCode.FuzzInfoCalls, Is.Zero);
        }

        [TestCaseSource(typeof(FuzzableCode), nameof(FuzzableCode.InvalidTargetNames))]
        [TestCase("MissingTarget")]
        [TestCase("spantarget")]
        [TestCase("")]
        public void ReplayRejectsInvalidTargetsBeforeReadingTheInput(string target)
        {
            string missingFile = Path.Combine(m_directory, "missing-input");

            int exitCode = Run("--replay", target, missingFile);

            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(m_standardError, Does.Contain("The fuzzing function " + target));
            Assert.That(m_standardOutput, Is.Empty);
            Assert.That(FuzzableCode.Invocations, Is.Empty);
            Assert.That(FuzzableCode.FuzzInfoCalls, Is.Zero);
        }

        [TestCase(nameof(FuzzableCode.StreamTarget))]
        [TestCase(nameof(FuzzableCode.StringTarget))]
        [TestCase(nameof(FuzzableCode.SpanTarget))]
        public async Task ReplayFileInvokesOnlyTheExactRequestedTargetAsync(string target)
        {
            byte[] input = [0x41, 0x00, 0xc3, 0xa9];
            string file = await WriteInputAsync("input with spaces", input).ConfigureAwait(false);

            int exitCode = Run("--replay", target, file);

            Assert.That(exitCode, Is.Zero);
            Assert.That(FuzzableCode.Invocations.Select(call => call.Target), Is.EqualTo(new[] { target }));
            Assert.That(FuzzableCode.Invocations[0].Input, Is.EqualTo(input));
            Assert.That(FuzzableCode.FuzzInfoCalls, Is.Zero);
            Assert.That(m_standardOutput, Is.Empty);
            Assert.That(m_standardError, Is.Empty);
        }

        [TestCase(nameof(FuzzableCode.StreamTarget))]
        [TestCase(nameof(FuzzableCode.StringTarget))]
        [TestCase(nameof(FuzzableCode.SpanTarget))]
        public async Task ReplayEmptyFileStillInvokesTheRequestedTargetAsync(string target)
        {
            string file = await WriteInputAsync("empty", []).ConfigureAwait(false);

            int exitCode = Run("--replay", target, file);

            Assert.That(exitCode, Is.Zero);
            Assert.That(FuzzableCode.Invocations.Select(call => call.Target), Is.EqualTo(new[] { target }));
            Assert.That(FuzzableCode.Invocations[0].Input, Is.Empty);
            Assert.That(FuzzableCode.FuzzInfoCalls, Is.Zero);
            Assert.That(m_standardOutput, Is.Empty);
            Assert.That(m_standardError, Is.Empty);
        }

        [TestCase(nameof(FuzzableCode.StreamTarget))]
        [TestCase(nameof(FuzzableCode.StringTarget))]
        [TestCase(nameof(FuzzableCode.SpanTarget))]
        public async Task ReplayCorpusRecursesInOrdinalOrderAndUsesOnlyTheRequestedTargetAsync(string target)
        {
            _ = await WriteInputAsync("z-last.bin", [0x7a]).ConfigureAwait(false);
            _ = await WriteInputAsync(Path.Combine("middle corpus", "seed without extension"), [0x6d])
                .ConfigureAwait(false);
            _ = await WriteInputAsync("a-second.bin", [0x61]).ConfigureAwait(false);
            _ = await WriteInputAsync("Z-first.bin", [0x5a]).ConfigureAwait(false);

            int exitCode = Run("--replay", target, m_directory);

            Assert.That(exitCode, Is.Zero);
            Assert.That(FuzzableCode.Invocations.Select(call => call.Target),
                Is.EqualTo(new[] { target, target, target, target }));
            Assert.That(FuzzableCode.Invocations.Select(call => call.Input),
                Is.EqualTo(new[]
                {
                    new byte[] { 0x5a },
                    new byte[] { 0x61 },
                    new byte[] { 0x6d },
                    new byte[] { 0x7a }
                }));
            Assert.That(FuzzableCode.FuzzInfoCalls, Is.Zero);
            Assert.That(m_standardOutput, Is.Empty);
            Assert.That(m_standardError, Is.Empty);
        }

        [TestCase(nameof(FuzzableCode.StreamTarget))]
        [TestCase(nameof(FuzzableCode.StringTarget))]
        [TestCase(nameof(FuzzableCode.SpanTarget))]
        public void ReplayMissingFileThrowsWithoutInvokingTargets(string target)
        {
            string missingFile = Path.Combine(m_directory, "missing-input");

            FileNotFoundException exception =
                Assert.Throws<FileNotFoundException>(() => Run("--replay", target, missingFile));

            Assert.That(exception.FileName, Is.EqualTo(missingFile));
            Assert.That(FuzzableCode.Invocations, Is.Empty);
            Assert.That(FuzzableCode.FuzzInfoCalls, Is.Zero);
        }

        [Test]
        public void ReplayMissingParentDirectoryThrowsWithoutInvokingTargets()
        {
            string missingFile = Path.Combine(m_directory, "missing-corpus", "missing-input");

            Assert.That(() => Run("--replay", nameof(FuzzableCode.SpanTarget), missingFile),
                Throws.TypeOf<DirectoryNotFoundException>());

            Assert.That(FuzzableCode.Invocations, Is.Empty);
            Assert.That(FuzzableCode.FuzzInfoCalls, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReplayEmptyCorpusThrowsEvenWhenItContainsAnEmptySubdirectory(bool hasSubdirectory)
        {
            if (hasSubdirectory)
            {
                Directory.CreateDirectory(Path.Combine(m_directory, "empty-subdirectory"));
            }

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => Run("--replay", nameof(FuzzableCode.SpanTarget), m_directory));

            Assert.That(exception.Message, Does.Contain("Replay corpus is empty:").And.Contain(m_directory));
            Assert.That(FuzzableCode.Invocations, Is.Empty);
            Assert.That(FuzzableCode.FuzzInfoCalls, Is.Zero);
        }

        [TestCase(nameof(FuzzableCode.ThrowingStreamTarget))]
        [TestCase(nameof(FuzzableCode.ThrowingStringTarget))]
        [TestCase(nameof(FuzzableCode.ThrowingSpanTarget))]
        public async Task ReplayDoesNotSwallowOrWrapTheTargetExceptionAsync(string target)
        {
            byte[] input = [0x41, 0x00, 0xc3, 0xa9];
            string file = await WriteInputAsync("failing-input", input).ConfigureAwait(false);

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() => Run("--replay", target, file));

            Assert.That(exception, Is.SameAs(FuzzableCode.InjectedFailure));
            Assert.That(FuzzableCode.Invocations.Select(call => call.Target), Is.EqualTo(new[] { target }));
            Assert.That(FuzzableCode.Invocations[0].Input, Is.EqualTo(input));
            Assert.That(FuzzableCode.FuzzInfoCalls, Is.Zero);
        }

        [TestCase(nameof(FuzzableCode.ThrowingStreamTarget))]
        [TestCase(nameof(FuzzableCode.ThrowingStringTarget))]
        [TestCase(nameof(FuzzableCode.ThrowingSpanTarget))]
        public async Task ReplayCorpusStopsAtTheFirstThrownTargetExceptionAsync(string target)
        {
            _ = await WriteInputAsync("z-unreached", [0x7a]).ConfigureAwait(false);
            _ = await WriteInputAsync("a-failing", [0x61]).ConfigureAwait(false);

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() => Run("--replay", target, m_directory));

            Assert.That(exception, Is.SameAs(FuzzableCode.InjectedFailure));
            Assert.That(FuzzableCode.Invocations.Select(call => call.Target), Is.EqualTo(new[] { target }));
            Assert.That(FuzzableCode.Invocations[0].Input, Is.EqualTo(new byte[] { 0x61 }));
            Assert.That(FuzzableCode.FuzzInfoCalls, Is.Zero);
        }

        private static IEnumerable<TestCaseData> InvalidArguments()
        {
            yield return ArgumentsCase("MissingArgumentsReturnUsageError");
            yield return ArgumentsCase("UnknownOptionReturnsUsageError", "--unknown");
            yield return ArgumentsCase("UnknownTargetReturnsUsageError", "MissingTarget");
            yield return ArgumentsCase("EmptyTargetReturnsUsageError", string.Empty);
            yield return ArgumentsCase("TargetNamesAreCaseSensitive", "spantarget");
            yield return ArgumentsCase("ReplayWithoutTargetReturnsUsageError", "--replay");
            yield return ArgumentsCase("ReplayWithoutInputReturnsUsageError",
                "--replay", nameof(FuzzableCode.SpanTarget));
            yield return ArgumentsCase("ReplayWithExtraArgumentsReturnsUsageError",
                "--replay", nameof(FuzzableCode.SpanTarget), "missing-input", "extra");
            yield return ArgumentsCase("ListWithExtraArgumentsReturnsUsageError", "--list", "extra");
            yield return ArgumentsCase("HelpWithExtraArgumentsReturnsUsageError", "--help", "extra");
            yield return ArgumentsCase("TargetWithExtraArgumentsReturnsUsageError",
                nameof(FuzzableCode.SpanTarget), "extra");
            yield return ArgumentsCase("MultipleModesReturnUsageError", "--list", "--help");
            yield return ArgumentsCase("InvalidSingleTargetReturnsUsageError",
                nameof(FuzzableCode.ReturningSpanTarget));
        }

        private static TestCaseData ArgumentsCase(string name, params string[] args)
        {
            return new TestCaseData(new[] { args }).SetName(name);
        }

        private Task<string> WriteInputAsync(string name, byte[] input)
        {
            return m_inputs.WriteAsync(name, input);
        }

        private int Run(params string[] args)
        {
            TextWriter previousOutput = Console.Out;
            TextWriter previousError = Console.Error;
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            try
            {
                Console.SetOut(output);
                Console.SetError(error);
                return Program.Main(args);
            }
            finally
            {
                Console.SetOut(previousOutput);
                Console.SetError(previousError);
                m_standardOutput = output.ToString();
                m_standardError = error.ToString();
            }
        }

        private string m_directory;
        private TestInputDirectory m_inputs;
        private string m_standardOutput;
        private string m_standardError;
    }
}
