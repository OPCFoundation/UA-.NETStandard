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

#if NET10_0
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class CoverageGateScriptTests
    {
        [Test]
        public async Task RelativeRepoRootKeepsSourceSubRootInCoveragePathsAsync()
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            string sourceFile = repository.WriteSourceFile("1");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteSourceFile("2");
            await repository.CommitAllAsync("change source").ConfigureAwait(false);
            string reportPath = repository.WriteCoberturaReport(sourceFile, repository.SourceRoot, 5, 1);

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, result.Output);
                Assert.That(result.Output, Does.Contain("Patch:       100.00% (1/1 changed lines covered"));
                Assert.That(result.Output, Does.Not.Contain("No coverable changed lines were found"));
            });
        }

        [Test]
        public async Task ForeignAgentAbsolutePathIsReanchoredToCurrentCheckoutAsync()
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            repository.WriteSourceFile("1");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteSourceFile("2");
            await repository.CommitAllAsync("change source").ConfigureAwait(false);
            string reportPath = repository.WriteCoberturaReportWithRawPaths(
                "/Users/runner/work/fixture/fixture/src/CoverageSubject/Subject.cs",
                "/",
                5,
                1);

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, result.Output);
                Assert.That(result.Output, Does.Contain("Patch:       100.00% (1/1 changed lines covered"));
                Assert.That(result.Output, Does.Not.Contain("Changed C# files were found, but none matched"));
            });
        }

        [Test]
        public async Task PatchGatePassesWhenNoCSharpFilesChangedAsync()
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            string sourceFile = repository.WriteSourceFile("1");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            File.WriteAllText(
                Path.Combine(repository.RootPath, "README.md"),
                "documentation change" + Environment.NewLine);
            await repository.CommitAllAsync("change docs").ConfigureAwait(false);
            string reportPath = repository.WriteCoberturaReport(sourceFile, repository.SourceRoot, 5, 1);

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, result.Output);
                Assert.That(result.Output, Does.Contain("No changed C# files were found; the patch gate passes."));
                Assert.That(result.Output, Does.Not.Contain("ERROR:"));
            });
        }

        [Test]
        public async Task PatchGateFailsWhenChangedFilesDoNotMatchCoverageReportAsync()
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            repository.WriteSourceFile("1");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteSourceFile("2");
            await repository.CommitAllAsync("change source").ConfigureAwait(false);
            string unmatchedFile = Path.Combine(repository.RootPath, "src", "Other", "Other.cs");
            string reportPath = repository.WriteCoberturaReport(unmatchedFile, repository.SourceRoot, 5, 1);

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(
                    result.Output,
                    Does.Contain("Changed C# files were found, but none matched any file in the coverage report."));
                Assert.That(result.Output, Does.Contain("Example changed file: src/CoverageSubject/Subject.cs"));
                Assert.That(result.Output, Does.Contain("Example report file: src/Other/Other.cs"));
            });
        }

        /// <summary>
        /// A path rule is not graduated: it is the rule for the path whatever
        /// the patch size, so one uncovered line in a two-line change fails it.
        /// </summary>
        [Test]
        public async Task PathRuleFailsOnAnUncoveredChangedLineAsync()
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            string sourceFile = repository.WriteWotSourceFile("1");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteWotSourceFile("2");
            await repository.CommitAllAsync("change source").ConfigureAwait(false);
            string reportPath = repository.WriteCoberturaReport(sourceFile, repository.SourceRoot, 5, 0);

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".",
                WithWotPathRule).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(
                    result.Output,
                    Does.Contain("Changed-line coverage 0.00% for 'WoT test rule' is below the required 100.00%"));
                Assert.That(
                    result.Output,
                    Does.Contain("src/Opc.Ua.Types/Wot/Subject.cs: 5"),
                    "The uncovered changed lines are named, because that is the actionable part.");
            });
        }

        [Test]
        public async Task PathRulePassesWhenEveryChangedLineIsCoveredAsync()
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            string sourceFile = repository.WriteWotSourceFile("1");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteWotSourceFile("2");
            await repository.CommitAllAsync("change source").ConfigureAwait(false);
            string reportPath = repository.WriteCoberturaReport(sourceFile, repository.SourceRoot, 5, 1);

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".",
                WithWotPathRule).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, result.Output);
                Assert.That(
                    result.Output,
                    Does.Contain("Rule 'WoT test rule': line 100.00% (1/1 changed lines)"));
            });
        }

        /// <summary>
        /// A line whose branches are only half taken is a covered line and an
        /// unexercised path, which is exactly the bug a protocol mapping hides.
        /// </summary>
        [Test]
        public async Task PathRuleFailsOnAPartiallyCoveredChangedBranchAsync()
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            string sourceFile = repository.WriteWotSourceFile("1");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteWotSourceFile("2");
            await repository.CommitAllAsync("change source").ConfigureAwait(false);
            string reportPath = repository.WriteCoberturaReport(
                sourceFile, repository.SourceRoot, 5, 1, "50% (1/2)");

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".",
                WithWotPathRule).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(
                    result.Output,
                    Does.Contain("Changed-branch coverage 50.00% for 'WoT test rule' is below the required 100.00%"));
                Assert.That(
                    result.Output,
                    Does.Contain("Partially covered changed branches:"));
            });
        }

        /// <summary>
        /// A rule states its own exclusions explicitly, so a path it excludes
        /// is governed by the graduated repository-wide band alone.
        /// </summary>
        [Test]
        public async Task AnExcludedPathIsNotGovernedByTheRuleAsync()
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            string sourceFile = repository.WriteWotSourceFile("1", Path.Combine("Design", "Subject.cs"));
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteWotSourceFile("2", Path.Combine("Design", "Subject.cs"));
            await repository.CommitAllAsync("change source").ConfigureAwait(false);
            string reportPath = repository.WriteCoberturaReport(sourceFile, repository.SourceRoot, 5, 0);

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".",
                WithWotPathRule).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Output,
                    Does.Not.Contain("for 'WoT test rule' is below the required 100.00%"),
                    "The rule excludes the path, so it says nothing about it.");
                Assert.That(
                    result.Output,
                    Does.Contain("Patch:       0.00%"),
                    "The graduated repository-wide band still governs the excluded path.");
            });
        }

        /// <summary>
        /// A change outside every rule's include globs leaves the rule with
        /// nothing to say, which is reported rather than passed silently.
        /// </summary>
        [Test]
        public async Task APathOutsideTheRuleReportsNoScopeAsync()
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            string sourceFile = repository.WriteSourceFile("1");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteSourceFile("2");
            await repository.CommitAllAsync("change source").ConfigureAwait(false);
            string reportPath = repository.WriteCoberturaReport(sourceFile, repository.SourceRoot, 5, 0);

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".",
                WithWotPathRule).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Output,
                    Does.Not.Contain("Rule 'WoT test rule'"),
                    "A rule with nothing in scope states nothing about the patch.");
            });
        }

        /// <summary>
        /// A file the rule governs that the coverage report never mentions is
        /// the vacuous pass this gate exists to stop: the assembly was not
        /// collected, so nothing measured the changed lines and the rule
        /// reported success.
        /// </summary>
        [Test]
        public async Task PathRuleFailsWhenAnInScopeFileIsAbsentFromTheReportAsync()
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            repository.WriteWotSourceFile("1");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteWotSourceFile("2");
            await repository.CommitAllAsync("change source").ConfigureAwait(false);

            // The report names a different file in the same rule's scope, so
            // the assembly is present but the changed file is not.
            string reportPath = repository.WriteCoberturaReport(
                Path.Combine(repository.RootPath, "src", "Opc.Ua.Types", "Wot", "Other.cs"),
                repository.SourceRoot,
                5,
                1);

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".",
                WithWotPathRule).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(
                    result.Output,
                    Does.Contain("exist on disk but are absent from the coverage report"));
                Assert.That(
                    result.Output,
                    Does.Contain("src/Opc.Ua.Types/Wot/Subject.cs"),
                    "The unmatched file is named, because that is the actionable part.");
            });
        }

        /// <summary>
        /// A report that carries no assembly at all is the same fault: the rule
        /// governs a changed file, and nothing measured it.
        /// </summary>
        [Test]
        public async Task PathRuleFailsWhenTheReportCarriesNoAssemblyAsync()
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            repository.WriteWotSourceFile("1");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteWotSourceFile("2");
            await repository.CommitAllAsync("change source").ConfigureAwait(false);
            string reportPath = repository.WriteEmptyCoberturaReport();

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".",
                WithWotPathRule).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(
                    result.Output,
                    Does.Contain("exist on disk but are absent from the coverage report"));
            });
        }

        /// <summary>
        /// One of two changed in-scope files measured is not "the rule passed".
        /// A partial report is the shape a half-collected matrix produces, and
        /// it is exactly as vacuous as an empty one for the file it omits.
        /// </summary>
        [Test]
        public async Task PathRuleFailsWhenOneOfTwoInScopeFilesIsOmittedAsync()
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            string measured = repository.WriteWotSourceFile("1");
            repository.WriteWotSourceFile("1", "Second.cs");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteWotSourceFile("2");
            repository.WriteWotSourceFile("2", "Second.cs");
            await repository.CommitAllAsync("change source").ConfigureAwait(false);
            string reportPath = repository.WriteCoberturaReport(
                measured, repository.SourceRoot, 5, 1);

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".",
                WithWotPathRule).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(
                    result.Output,
                    Does.Contain("src/Opc.Ua.Types/Wot/Second.cs"));
                Assert.That(
                    result.Output,
                    Does.Not.Contain("Rule 'WoT test rule': line 100.00%"),
                    "The measured half must not be reported as the whole.");
            });
        }

        /// <summary>
        /// A file the report knows about whose changed lines are all
        /// non-coverable - a comment, a brace - is legitimate. It is named
        /// rather than failed, so the reader can tell it from a file nothing
        /// measured.
        /// </summary>
        /// <summary>
        /// A file that declares only an interface produces no sequence point,
        /// so no coverage report can ever mention it. Failing the rule for it
        /// would make the rule impossible to satisfy - but the absence is
        /// excused only on evidence: the project it belongs to has to be one
        /// the report does carry.
        /// </summary>
        [TestCase(
            "IContract.cs",
            "public interface IContract\n{\n    int Value { get; }\n\n    void Do(int x = 0);\n}",
            TestName = "InterfaceOnly")]
        [TestCase(
            "Kind.cs",
            "public enum Kind\n{\n    None = 0,\n    Some = 1\n}",
            TestName = "EnumOnly")]
        [TestCase(
            "Terms.cs",
            "public static class Terms\n{\n    public const string Name = \"x\";\n}",
            TestName = "ConstantsOnly")]
        [TestCase(
            "Handler.cs",
            "public delegate void Handler(int value);",
            TestName = "DelegateOnly")]
        public async Task PathRuleExcusesAChangedFileThatProducesNoSequencePointAsync(
            string fileName, string body)
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            repository.WriteWotProjectFile();
            repository.WriteWotSourceFile("1");
            repository.WriteNonCoverableWotFile(
                fileName,
                body.Replace("\n", Environment.NewLine, StringComparison.Ordinal));
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteWotSourceFile("2");
            repository.WriteNonCoverableWotFile(
                fileName,
                (body + "\n").Replace("\n", Environment.NewLine, StringComparison.Ordinal));
            await repository.CommitAllAsync("change both").ConfigureAwait(false);

            // The measured file is in the report; the declaration-only one
            // cannot be, so the assembly evidence has to come from its sibling.
            string reportPath = repository.WriteCoberturaReport(
                Path.Combine(repository.RootPath, "src", "Opc.Ua.Types", "Wot", "Subject.cs"),
                repository.SourceRoot,
                5,
                1);

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".",
                WithWotPathRule).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Output,
                    Does.Not.Contain("exist on disk but are absent from the coverage report"),
                    result.Output);
                Assert.That(
                    result.Output,
                    Does.Contain("produce no sequence point and are correctly absent"),
                    "The excused file is named rather than silently skipped.");
                Assert.That(result.Output, Does.Contain(fileName));
                Assert.That(
                    result.Output,
                    Does.Contain("Rule 'WoT test rule': line 100.00% (1/1 changed lines)"),
                    "The measurable part of the patch is still measured.");
            });
        }

        /// <summary>
        /// A file the report does not mention because its assembly was never
        /// collected is the vacuous pass the rule exists to stop, and it stays
        /// a failure however little executable code it declares.
        /// </summary>
        [Test]
        public async Task PathRuleStillFailsWhenTheAssemblyWasNotCollectedAsync()
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            repository.WriteWotProjectFile();
            repository.WriteNonCoverableWotFile(
                "IContract.cs",
                "public interface IContract" + Environment.NewLine + "{" + Environment.NewLine +
                    "    int Value { get; }" + Environment.NewLine + "}");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteNonCoverableWotFile(
                "IContract.cs",
                "public interface IContract" + Environment.NewLine + "{" + Environment.NewLine +
                    "    int Value { get; }" + Environment.NewLine + Environment.NewLine +
                    "    int Other { get; }" + Environment.NewLine + "}");
            await repository.CommitAllAsync("change contract").ConfigureAwait(false);

            // Nothing of the project is in the report, so nothing proves the
            // assembly was collected at all.
            string reportPath = repository.WriteCoberturaReport(
                Path.Combine(repository.RootPath, "src", "Elsewhere", "Other.cs"),
                repository.SourceRoot,
                5,
                1);

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".",
                WithWotPathRule).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(
                    result.Output,
                    Does.Contain("exist on disk but are absent from the coverage report"));
                Assert.That(
                    result.Output,
                    Does.Contain("src/Opc.Ua.Types/Wot/IContract.cs"),
                    "The failure names the rule and the file it governs.");
                Assert.That(
                    result.Output,
                    Does.Contain("Path rule 'WoT test rule' governs 1 changed file(s)"),
                    "The message states which rule caught it and how many files.");
            });
        }

        /// <summary>
        /// A file whose absence the assembly evidence would excuse is still a
        /// failure when it declares code the compiler emits a sequence point
        /// for: the two proofs are independent, and both are required.
        /// </summary>
        [TestCase(
            "public sealed class Subject\n{\n    public int Value() { return 1; }\n}",
            TestName = "MethodBody")]
        [TestCase(
            "public static class Subject\n{\n    public static readonly string[] Names = [\"x\"];\n}",
            TestName = "FieldInitializer")]
        [TestCase(
            "public sealed class Subject\n{\n    public int Value { get; set; }\n}",
            TestName = "AutoProperty")]
        [TestCase(
            "public readonly record struct Subject(string Name);",
            TestName = "PrimaryConstructor")]
        public async Task PathRuleFailsWhenAnExecutableFileIsAbsentAsync(string body)
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            repository.WriteWotProjectFile();
            repository.WriteWotSourceFile("1");
            repository.WriteNonCoverableWotFile(
                "Absent.cs",
                body.Replace("\n", Environment.NewLine, StringComparison.Ordinal));
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteNonCoverableWotFile(
                "Absent.cs",
                (body + "\n").Replace("\n", Environment.NewLine, StringComparison.Ordinal));
            await repository.CommitAllAsync("change absent").ConfigureAwait(false);

            string reportPath = repository.WriteCoberturaReport(
                Path.Combine(repository.RootPath, "src", "Opc.Ua.Types", "Wot", "Subject.cs"),
                repository.SourceRoot,
                5,
                1);

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".",
                WithWotPathRule).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(
                    result.Output,
                    Does.Contain("exist on disk but are absent from the coverage report"));
                Assert.That(result.Output, Does.Contain("src/Opc.Ua.Types/Wot/Absent.cs"));
            });
        }

        /// <summary>
        /// A patch that changes one file of each kind is measured on the one
        /// that can be measured and excused on the one that cannot, so the rule
        /// stays enforceable without becoming impossible.
        /// </summary>
        [Test]
        public async Task PathRuleMeasuresTheCoverableFileAndExcusesTheOtherAsync()
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            repository.WriteWotProjectFile();
            repository.WriteWotSourceFile("1");
            repository.WriteNonCoverableWotFile(
                "Kind.cs",
                "public enum Kind" + Environment.NewLine + "{" + Environment.NewLine +
                    "    None = 0" + Environment.NewLine + "}");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteWotSourceFile("2");
            repository.WriteNonCoverableWotFile(
                "Kind.cs",
                "public enum Kind" + Environment.NewLine + "{" + Environment.NewLine +
                    "    None = 0," + Environment.NewLine + "    Some = 1" +
                    Environment.NewLine + "}");
            await repository.CommitAllAsync("change both").ConfigureAwait(false);

            // The one file that can be measured is uncovered, so the rule has
            // to fail on the measurement rather than pass on the exception.
            string reportPath = repository.WriteCoberturaReport(
                Path.Combine(repository.RootPath, "src", "Opc.Ua.Types", "Wot", "Subject.cs"),
                repository.SourceRoot,
                5,
                0);

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".",
                WithWotPathRule).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(
                    result.Output,
                    Does.Contain("produce no sequence point and are correctly absent"));
                Assert.That(result.Output, Does.Contain("Kind.cs"));
                Assert.That(
                    result.Output,
                    Does.Contain("Changed-line coverage 0.00%"),
                    "The measurable file is still measured.");
            });
        }

        [Test]
        public async Task PathRuleAcceptsAFileWithNoCoverableChangedLinesAsync()
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            string sourceFile = repository.WriteWotSourceFile("1");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteWotSourceFile("2");
            await repository.CommitAllAsync("change source").ConfigureAwait(false);

            // The report knows the file, but records a coverable line the patch
            // did not touch.
            string reportPath = repository.WriteCoberturaReport(
                sourceFile, repository.SourceRoot, 3, 1);

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".",
                WithWotPathRule).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, result.Output);
                Assert.That(
                    result.Output,
                    Does.Contain("contributed no coverable line"));
                Assert.That(
                    result.Output,
                    Does.Contain("src/Opc.Ua.Types/Wot/Subject.cs"));
                Assert.That(
                    result.Output,
                    Does.Not.Contain("absent from the coverage report"));
            });
        }

        /// <summary>
        /// A rule whose include list is empty or mistyped matches nothing, so
        /// every patch passes it without a file being looked at. That is a
        /// configuration fault rather than a clean patch.
        /// </summary>
        [Test]
        public async Task PathRuleWithNoIncludeGlobsFailsAsConfigurationAsync()
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            repository.WriteWotSourceFile("1");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteWotSourceFile("2");
            await repository.CommitAllAsync("change source").ConfigureAwait(false);
            string reportPath = repository.WriteCoberturaReport(
                Path.Combine(repository.RootPath, "src", "Opc.Ua.Types", "Wot", "Subject.cs"),
                repository.SourceRoot,
                5,
                1);

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".",
                WithEmptyIncludePathRule).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(
                    result.Output,
                    Does.Contain("declares no usable 'include' globs"));
            });
        }

        /// <summary>
        /// A file the patch deleted is correctly absent from the report, so its
        /// absence is not the missing-measurement fault.
        /// </summary>
        [Test]
        public async Task PathRuleAcceptsAFileThePatchDeletedAsync()
        {
            using TestRepository repository = await TestRepository.CreateAsync().ConfigureAwait(false);
            string measured = repository.WriteWotSourceFile("1");
            string deleted = repository.WriteWotSourceFile("1", "Gone.cs");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteWotSourceFile("2");
            File.Delete(deleted);
            await repository.CommitAllAsync("change source").ConfigureAwait(false);
            string reportPath = repository.WriteCoberturaReport(
                measured, repository.SourceRoot, 5, 1);

            ScriptResult result = await RunCoverageGateAsync(
                repository.RootPath,
                reportPath,
                ".",
                WithWotPathRule).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, result.Output);
                Assert.That(
                    result.Output,
                    Does.Not.Contain("absent from the coverage report"));
                Assert.That(
                    result.Output,
                    Does.Contain("Rule 'WoT test rule': line 100.00% (1/1 changed lines)"));
            });
        }

        /// <summary>
        /// The rule the fixture repository runs under: the same shape as the
        /// shipped one, over the fixture's own paths.
        /// </summary>
        private const string WithWotPathRule =
            """
              "pathRules": [
                {
                  "name": "WoT test rule",
                  "include": [ "src/Opc.Ua.Types/Wot/**" ],
                  "exclude": [ "**/Design/**" ],
                  "minimumChangedLineRate": 100.0,
                  "minimumChangedBranchRate": 100.0
                }
              ],
            """;

        /// <summary>
        /// A rule whose include list is empty: it matches nothing, so it can
        /// only ever pass, which is the configuration fault the gate refuses.
        /// </summary>
        private const string WithEmptyIncludePathRule =
            """
              "pathRules": [
                {
                  "name": "WoT test rule",
                  "include": [],
                  "exclude": [ "**/Design/**" ],
                  "minimumChangedLineRate": 100.0,
                  "minimumChangedBranchRate": 100.0
                }
              ],
            """;

        private static async Task<ScriptResult> RunCoverageGateAsync(
            string workingDirectory,
            string coberturaPath,
            string repoRoot,
            string? pathRules = null)
        {
            string scriptPath = Path.Combine(FindRepositoryRoot(), ".azurepipelines", "check-coverage.ps1");
            string thresholdsPath = Path.Combine(workingDirectory, "coverage-thresholds.json");
            File.WriteAllText(
                thresholdsPath,
                """
                {
                  "project": {
                    "minimumLineRate": 0.0,
                    "minimumBranchRate": 0.0,
                    "baselineLineRate": 0.0,
                    "advisoryDeltaTolerance": 1.0
                  },
                  "patch": {
                    "target": 100.0,
                    "threshold": 0.0,
                    "bands": []
                  },
                """ +
                Environment.NewLine +
                (pathRules ?? string.Empty) +
                Environment.NewLine +
                """
                  "ignore": [
                    "tests/**",
                    "samples/**",
                    "**/obj/**",
                    "**/bin/**",
                    "**/*.g.cs"
                  ]
                }
                """);

            using var process = new Process();
            process.StartInfo.FileName = "pwsh";
            process.StartInfo.WorkingDirectory = workingDirectory;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(scriptPath);
            process.StartInfo.ArgumentList.Add("-CoberturaPath");
            process.StartInfo.ArgumentList.Add(coberturaPath);
            process.StartInfo.ArgumentList.Add("-ThresholdsPath");
            process.StartInfo.ArgumentList.Add(thresholdsPath);
            process.StartInfo.ArgumentList.Add("-RepoRoot");
            process.StartInfo.ArgumentList.Add(repoRoot);
            process.StartInfo.ArgumentList.Add("-BaseRef");
            process.StartInfo.ArgumentList.Add("master");
            process.StartInfo.ArgumentList.Add("-SkipFetch");

            Assert.That(process.Start(), Is.True);
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            string output = await standardOutput.ConfigureAwait(false);
            string error = await standardError.ConfigureAwait(false);
            return new ScriptResult(process.ExitCode, output + error);
        }

        private static string FindRepositoryRoot()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, ".azurepipelines", "check-coverage.ps1")))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName;
            }

            throw new InvalidOperationException("Could not find the repository root.");
        }

        private sealed record ScriptResult(int ExitCode, string Output);

        private sealed class TestRepository : IDisposable
        {
            private TestRepository(string rootPath)
            {
                RootPath = rootPath;
            }

            public string RootPath { get; }

            public string SourceRoot => Path.Combine(RootPath, "src");

            public static async Task<TestRepository> CreateAsync()
            {
                string rootPath = Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "coverage-gate-fixtures",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(rootPath);
                var repository = new TestRepository(rootPath);

                await repository.RunGitAsync("init", "-b", "master").ConfigureAwait(false);
                await repository.RunGitAsync(
                    "config",
                    "user.email",
                    "coverage-gate@example.invalid").ConfigureAwait(false);
                await repository.RunGitAsync("config", "user.name", "Coverage Gate").ConfigureAwait(false);
                return repository;
            }

            public string WriteSourceFile(string value)
            {
                string path = Path.Combine(RootPath, "src", "CoverageSubject", "Subject.cs");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(
                    path,
                    $$"""
                    namespace CoverageSubject;

                    public sealed class Subject
                    {
                        public int Value => {{value}};
                    }
                    """);
                return path;
            }

            public string WriteWotSourceFile(string value, string fileName = "Subject.cs")
            {
                string path = Path.Combine(
                    RootPath, "src", "Opc.Ua.Types", "Wot", fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(
                    path,
                    $$"""
                    namespace CoverageSubject;

                    public sealed class Subject
                    {
                        public int Value => {{value}};
                    }
                    """);
                return path;
            }

            /// <summary>
            /// Writes the project file that identifies the assembly the WoT
            /// sources belong to, which is the evidence the gate uses to tell a
            /// file that produces no sequence point from one whose assembly was
            /// never collected.
            /// </summary>
            public string WriteWotProjectFile()
            {
                string path = Path.Combine(
                    RootPath, "src", "Opc.Ua.Types", "Opc.Ua.Types.csproj");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(
                    path,
                    """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFramework>net10.0</TargetFramework>
                      </PropertyGroup>
                    </Project>
                    """);
                return path;
            }

            /// <summary>
            /// Writes a source file that declares only what the compiler emits
            /// no sequence point for, so no coverage report can ever mention it.
            /// </summary>
            public string WriteNonCoverableWotFile(string fileName, string body)
            {
                string path = Path.Combine(
                    RootPath, "src", "Opc.Ua.Types", "Wot", fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(
                    path,
                    $$"""
                    namespace CoverageSubject;

                    {{body}}
                    """);
                return path;
            }

            public string WriteCoberturaReport(
                string filePath,
                string sourceRoot,
                int lineNumber,
                int hits,
                string? conditionCoverage = null)
            {
                return WriteCoberturaReportWithRawPaths(
                    NormalizeCoberturaPath(filePath),
                    NormalizeCoberturaPath(sourceRoot),
                    lineNumber,
                    hits,
                    conditionCoverage);
            }

            /// <summary>
            /// Writes a report that carries no assembly at all, which is what a
            /// leg that collected nothing publishes.
            /// </summary>
            public string WriteEmptyCoberturaReport()
            {
                string reportPath = Path.Combine(RootPath, "coverage.xml");
                File.WriteAllText(
                    reportPath,
                    $$"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <coverage line-rate="1" branch-rate="1" version="1.0">
                      <sources>
                        <source>{{EscapeXml(NormalizeCoberturaPath(SourceRoot))}}</source>
                      </sources>
                      <packages />
                    </coverage>
                    """);
                return reportPath;
            }

            public string WriteCoberturaReportWithRawPaths(
                string filePath,
                string sourceRoot,
                int lineNumber,
                int hits,
                string? conditionCoverage = null)
            {
                string branchAttributes = conditionCoverage is null
                    ? string.Empty
                    : $" branch=\"true\" condition-coverage=\"{EscapeXml(conditionCoverage)}\"";
                string reportPath = Path.Combine(RootPath, "coverage.xml");
                File.WriteAllText(
                    reportPath,
                    $$"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <coverage line-rate="1" branch-rate="1" version="1.0">
                      <sources>
                        <source>{{EscapeXml(sourceRoot)}}</source>
                      </sources>
                      <packages>
                        <package name="CoverageSubject" line-rate="1" branch-rate="1">
                          <classes>
                            <class name="CoverageSubject.Subject"
                                   filename="{{EscapeXml(filePath)}}"
                                   line-rate="1"
                                   branch-rate="1">
                              <lines>
                                <line number="{{lineNumber}}" hits="{{hits}}"{{branchAttributes}} />
                              </lines>
                            </class>
                          </classes>
                        </package>
                      </packages>
                    </coverage>
                    """);
                return reportPath;
            }

            public async Task CreateAndCheckoutBranchAsync(string branch)
            {
                await RunGitAsync("checkout", "-b", branch).ConfigureAwait(false);
            }

            public async Task CommitAllAsync(string message)
            {
                await RunGitAsync("add", ".").ConfigureAwait(false);
                await RunGitAsync("commit", "-m", message).ConfigureAwait(false);
            }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(RootPath))
                    {
                        foreach (string file in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                        }
                        Directory.Delete(RootPath, true);
                    }
                }
                catch (IOException)
                {
                    TestContext.Out.WriteLine("Could not delete test repository '{0}'.", RootPath);
                }
                catch (UnauthorizedAccessException)
                {
                    TestContext.Out.WriteLine("Could not delete test repository '{0}'.", RootPath);
                }
            }

            private static string EscapeXml(string value)
            {
                return SecurityElement.Escape(value) ?? string.Empty;
            }

            private static string NormalizeCoberturaPath(string path)
            {
                return Path.GetFullPath(path).Replace(Path.DirectorySeparatorChar, '/');
            }

            private async Task RunGitAsync(params string[] arguments)
            {
                using var process = new Process();
                process.StartInfo.FileName = "git";
                process.StartInfo.WorkingDirectory = RootPath;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                foreach (string argument in arguments)
                {
                    process.StartInfo.ArgumentList.Add(argument);
                }

                Assert.That(process.Start(), Is.True);
                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> standardError = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                string output = await standardOutput.ConfigureAwait(false);
                string error = await standardError.ConfigureAwait(false);

                Assert.That(
                    process.ExitCode,
                    Is.Zero,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "git {0} failed.{1}{2}",
                        string.Join(' ', arguments),
                        Environment.NewLine,
                        output + error));
            }
        }
    }
}
#endif
