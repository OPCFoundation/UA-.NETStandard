// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class CoverageGateScriptTests
    {
        /// <summary>
        /// Large positive hit counts remain covered while duplicate classes preserve line and branch maxima.
        /// </summary>
        [TestCase("2147483657", false)]
        [TestCase("2147483657", true)]
        [TestCase("9223372036854775817", false)]
        [TestCase("9223372036854775817", true)]
        public async Task LargeHitCountsPreserveCoverageAndClassMergeOrderAsync(string hits, bool largeFirst)
        {
            ScriptResult result = await RunHitCountGateAsync(hits, largeFirst).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero, result.Output);
                Assert.That(result.Output, Does.Contain("100.00% (1/1 lines"));
                Assert.That(result.Output, Does.Contain("75.00%"));
                Assert.That(result.Output, Does.Contain("Coverage gate passed."));
            }
        }

        /// <summary>
        /// Invalid hit counts are rejected as input errors even when the synthetic project floor is zero.
        /// </summary>
        [TestCase("-1")]
        [TestCase("")]
        [TestCase("not-a-count")]
        public async Task InvalidHitCountsCannotPassAZeroFloorGateAsync(string hits)
        {
            ScriptResult result = await RunHitCountGateAsync(hits, largeFirst: false).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(result.Output, Does.Contain("Invalid non-negative coverage hit count"));
                Assert.That(result.Output, Does.Not.Contain("Coverage gate passed."));
            }
        }

        /// <summary>
        /// Zero remains a valid hit count without making its source line covered.
        /// </summary>
        [Test]
        public async Task ZeroHitCountsRemainValidAndUncoveredAsync()
        {
            ScriptResult result = await RunHitCountGateAsync("0", largeFirst: false).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero, result.Output);
                Assert.That(result.Output, Does.Contain("0.00% (0/1 lines"));
                Assert.That(result.Output, Does.Contain("Coverage gate passed."));
            }
        }

        /// <summary>
        /// Presence copies preserve raw reports, source paths and branch evidence without overflowing hit counts.
        /// </summary>
        [TestCase("separate")]
        [TestCase("empty")]
        [TestCase("location-relative")]
        public async Task NormalizationPreservesRawFragmentsAndBranchEvidenceAsync(string output)
        {
            NormalizationResult result = await RunNormalizationAsync(
                ["0", "1", "2147483640", "9223372036854775817"], output).ConfigureAwait(false);

            Assert.That(result.Script.ExitCode, Is.Zero, result.Script.Output);
            Assert.That(result.Normalized, Is.Not.Null);
            var normalized = XDocument.Parse(result.Normalized!);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.RawHashAfter, Is.EqualTo(result.RawHashBefore));
                Assert.That(
                    normalized.Descendants("line").Select(line => (string?)line.Attribute("hits")),
                    Is.EqualTo(s_expectedPresenceHits));
                Assert.That(
                    normalized.Descendants("line").Select(line => (string?)line.Attribute("condition-coverage")),
                    Is.All.EqualTo("75% (3/4)"));
                Assert.That(normalized.Descendants("source").Single().Value, Is.EqualTo("synthetic-source"));
                Assert.That((string?)normalized.Root!.Attribute("branch-rate"), Is.EqualTo("0.75"));
            }
        }

        /// <summary>
        /// Invalid raw counts must be rejected before they can become a successful merged coverage report.
        /// </summary>
        [TestCase("-1")]
        [TestCase("")]
        [TestCase("not-a-count")]
        public async Task NormalizationRejectsInvalidCountsWithoutChangingRawInputAsync(string hits)
        {
            NormalizationResult result = await RunNormalizationAsync([hits]).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Script.ExitCode, Is.Not.Zero, result.Script.Output);
                Assert.That(result.Script.Output, Does.Contain("Invalid non-negative coverage hit count"));
                Assert.That(result.RawHashAfter, Is.EqualTo(result.RawHashBefore));
                Assert.That(result.Normalized, Is.Null);
            }
        }

        /// <summary>
        /// Output storage cannot overwrite, contain or be rediscovered within the raw artifact directory.
        /// </summary>
        [TestCase("same")]
        [TestCase("nested")]
        [TestCase("ancestor")]
        public async Task NormalizationRejectsOverlappingDirectoriesAsync(string output)
        {
            NormalizationResult result = await RunNormalizationAsync(["1"], output).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Script.ExitCode, Is.Not.Zero, result.Script.Output);
                Assert.That(result.Script.Output, Does.Contain("must not overlap"));
                Assert.That(result.RawHashAfter, Is.EqualTo(result.RawHashBefore));
            }
        }

        /// <summary>
        /// Existing output is not removed or mixed with a later fragment set.
        /// </summary>
        [Test]
        public async Task NormalizationRejectsExistingOutputWithoutRemovingItAsync()
        {
            NormalizationResult result = await RunNormalizationAsync(["1"], "nonempty").ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Script.ExitCode, Is.Not.Zero, result.Script.Output);
                Assert.That(result.Script.Output, Does.Contain("must be empty"));
                Assert.That(result.Sentinel, Is.EqualTo("keep"));
                Assert.That(result.RawHashAfter, Is.EqualTo(result.RawHashBefore));
            }
        }

        /// <summary>
        /// Missing raw fragments cannot be treated as a successful normalization.
        /// </summary>
        [Test]
        public async Task NormalizationRejectsMissingFragmentsAsync()
        {
            NormalizationResult result = await RunNormalizationAsync(["1"], writeReport: false).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Script.ExitCode, Is.Not.Zero, result.Script.Output);
                Assert.That(result.Script.Output, Does.Contain("No Cobertura fragments were found"));
                Assert.That(result.Normalized, Is.Null);
            }
        }

        /// <summary>
        /// A file with the expected suffix must still contain Cobertura data.
        /// </summary>
        [Test]
        public async Task NormalizationRejectsUnrelatedXmlWithoutChangingItAsync()
        {
            NormalizationResult result = await RunNormalizationAsync(
                ["1"], validCoverage: false).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Script.ExitCode, Is.Not.Zero, result.Script.Output);
                Assert.That(result.Script.Output, Does.Contain("Not a Cobertura fragment"));
                Assert.That(result.RawHashAfter, Is.EqualTo(result.RawHashBefore));
                Assert.That(result.Normalized, Is.Null);
            }
        }

        /// <summary>
        /// Same-basename partial files stay distinct while class names and original evidence remain recoverable.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task PartialTypeFileIdentitiesPreserveRawPathsAndBranchEvidenceAsync(int sourceDepth)
        {
            IdentityResult result = await RunClassIdentityAsync("restore", sourceDepth).ConfigureAwait(false);
            Assert.That(result.Prepared.ExitCode, Is.Zero, result.Prepared.Output);
            Assert.That(result.Restored.ExitCode, Is.Zero, result.Restored.Output);
            XElement[] classes = [.. XDocument.Parse(result.PreparedReport).Descendants("class")];
            XElement[] restored = [.. XDocument.Parse(result.RestoredReport).Descendants("class")];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.RawHashesAfter, Is.EqualTo(result.RawHashesBefore));
                Assert.That((string?)classes[0].Attribute("name"),
                    Is.EqualTo((string?)classes[1].Attribute("name")));
                Assert.That((string?)classes[0].Attribute("name"),
                    Is.Not.EqualTo((string?)classes[^1].Attribute("name")));
                Assert.That(restored.Select(value => (string?)value.Attribute("name")),
                    Is.All.EqualTo("Synthetic.Subject"));
                Assert.That(restored.Select(value => (string?)value.Attribute("filename")),
                    Is.EqualTo(classes.Select(value => (string?)value.Attribute("filename"))));
                Assert.That(restored.Select(value => value.Element("lines")!.ToString()),
                    Is.EqualTo(classes.Select(value => value.Element("lines")!.ToString())));
                Assert.That(restored[0].Element("lines")!.Element("line")!.Attribute("hits")!.Value,
                    Is.EqualTo("1"));
                Assert.That(restored[^1].Element("lines")!.Element("line")!.Attribute("hits")!.Value,
                    Is.EqualTo("0"));
                Assert.That((string?)restored[^1].Element("lines")!.Element("line")!
                    .Attribute("condition-coverage"), Is.EqualTo("50% (1/2)"));
                Assert.That(result.Html, Does.Not.Contain("__UaCoverageFile_"));
                Assert.That(result.Html, Does.Contain("Synthetic.Subject"));
            }
        }

        /// <summary>
        /// Types without a same-basename partial-file collision do not acquire temporary identities.
        /// </summary>
        [Test]
        public async Task PartialTypeIdentityLeavesDistinctBasenamesUnchangedAsync()
        {
            IdentityResult result = await RunClassIdentityAsync("no-collision", sourceDepth: 0)
                .ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Prepared.ExitCode, Is.Zero, result.Prepared.Output);
                Assert.That(result.Restored.ExitCode, Is.Zero, result.Restored.Output);
                Assert.That(result.PreparedReport, Does.Not.Contain("__UaCoverageFile_"));
                Assert.That(XDocument.Parse(result.RestoredReport).ToString(),
                    Is.EqualTo(XDocument.Parse(result.PreparedReport).ToString()));
                Assert.That(result.RawHashesAfter, Is.EqualTo(result.RawHashesBefore));
            }
        }

        /// <summary>
        /// Repeated staging and unknown merged identities fail without changing raw collector data.
        /// </summary>
        [TestCase("repeat")]
        [TestCase("unknown")]
        [TestCase("missing-manifest")]
        public async Task PartialTypeIdentityRejectsUnclassifiedOrRepeatedPreparationAsync(string operation)
        {
            IdentityResult result = await RunClassIdentityAsync(operation, sourceDepth: 0).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Prepared.ExitCode, Is.Zero, result.Prepared.Output);
                Assert.That(result.Restored.ExitCode, Is.Not.Zero, result.Restored.Output);
                Assert.That(result.RawHashesAfter, Is.EqualTo(result.RawHashesBefore));
                Assert.That(result.RestoredReport, Is.EqualTo(result.PreparedReport),
                    "A rejected operation must not partially restore or rewrite the merged report.");
            }
        }

        /// <summary>
        /// Both coverage jobs must actually merge the copied presence reports with an explicit tool version.
        /// </summary>
        [TestCase("buildandtest.yml")]
        [TestCase("nightly.yml")]
        public async Task NormalizationFeedsThePinnedWorkflowMergerAsync(string workflow)
        {
            string text = await File.ReadAllTextAsync(
                Path.Combine(FindRepositoryRoot(), ".github", "workflows", workflow)).ConfigureAwait(false);
            int start = text.IndexOf("    - name: Merge coverage report", StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            int end = text.IndexOf("\n    - name:", start + 1, StringComparison.Ordinal);
            string step = end < 0 ? text[start..] : text[start..end];
            int normalize = step.IndexOf("./.github/scripts/normalize-coverage.ps1", StringComparison.Ordinal);
            int merge = step.IndexOf("./tools/reportgenerator", StringComparison.Ordinal);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(normalize, Is.GreaterThanOrEqualTo(0));
                Assert.That(merge, Is.GreaterThan(normalize));
                Assert.That(step, Does.Contain("-OutputDirectory normalized-coverage"));
                Assert.That(step, Does.Contain("-reports:normalized-coverage/**/*.cobertura.xml"));
                Assert.That(step, Does.Contain("./.github/scripts/prepare-coverage-class-identities.ps1"));
                Assert.That(step, Does.Contain("-RestoreReport CodeCoverage/Cobertura.xml"));
                Assert.That(step, Does.Not.Contain("-reports:coverage-artifacts/"));
                Assert.That(step, Does.Contain("dotnet-reportgenerator-globaltool --version "));
                Assert.That(step, Does.Contain("installation failed with exit code"));
            }
        }

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
            await File.WriteAllTextAsync(
                Path.Combine(repository.RootPath, "README.md"),
                "documentation change" + Environment.NewLine).ConfigureAwait(false);
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
                Assert.That(
                    result.Output,
                    Does.Not.Contain("Rule 'WoT test rule'"),
                    "A rule with nothing in scope states nothing about the patch."));
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
                "public interface IContract" +
                Environment.NewLine +
                "{" +
                Environment.NewLine +
                "    int Value { get; }" +
                Environment.NewLine +
                "}");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteNonCoverableWotFile(
                "IContract.cs",
                "public interface IContract" +
                Environment.NewLine +
                "{" +
                Environment.NewLine +
                "    int Value { get; }" +
                Environment.NewLine +
                Environment.NewLine +
                "    int Other { get; }" +
                Environment.NewLine +
                "}");
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
                "public enum Kind" +
                Environment.NewLine +
                "{" +
                Environment.NewLine +
                "    None = 0" +
                Environment.NewLine +
                "}");
            await repository.CommitAllAsync("base").ConfigureAwait(false);
            await repository.CreateAndCheckoutBranchAsync("feature").ConfigureAwait(false);
            repository.WriteWotSourceFile("2");
            repository.WriteNonCoverableWotFile(
                "Kind.cs",
                "public enum Kind" +
                Environment.NewLine +
                "{" +
                Environment.NewLine +
                "    None = 0," +
                Environment.NewLine +
                "    Some = 1" +
                Environment.NewLine +
                "}");
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

        /// <summary>
        /// Runs synthetic count controls without initializing a Git fixture or consulting changed-line coverage.
        /// </summary>
        private static async Task<ScriptResult> RunHitCountGateAsync(string hits, bool largeFirst)
        {
            string directory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "coverage-hit-count-fixtures",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string high = $$"""
                    <class name="High" filename="src/CoverageSubject/Subject.cs">
                      <lines><line number="1" hits="{{SecurityElement.Escape(hits)}}" branch="true"
                        condition-coverage="75% (3/4)" /></lines>
                    </class>
                    """;
                const string low = """
                    <class name="Low" filename="src/CoverageSubject/Subject.cs">
                      <lines><line number="1" hits="0" branch="true" condition-coverage="25% (1/4)" /></lines>
                    </class>
                    """;
                string path = Path.Combine(directory, "synthetic-counts.cobertura.xml");
                await File.WriteAllTextAsync(path, $$"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <coverage line-rate="1" branch-rate="0.75">
                      <sources><source>.</source></sources>
                      <packages><package name="SyntheticCounts"><classes>
                        {{(largeFirst ? high + low : low + high)}}
                      </classes></package></packages>
                    </coverage>
                    """).ConfigureAwait(false);
                return await RunCoverageGateAsync(
                    directory, path, FindRepositoryRoot(), projectOnly: true).ConfigureAwait(false);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static async Task<ScriptResult> RunCoverageGateAsync(
            string workingDirectory,
            string coberturaPath,
            string repoRoot,
            string? pathRules = null,
            bool projectOnly = false)
        {
            string scriptPath = Path.Combine(FindRepositoryRoot(), ".azurepipelines", "check-coverage.ps1");
            string thresholdsPath = Path.Combine(workingDirectory, "coverage-thresholds.json");
            await File.WriteAllTextAsync(
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
                """).ConfigureAwait(false);

            var arguments = new List<string>
            {
                "-CoberturaPath", coberturaPath,
                "-ThresholdsPath", thresholdsPath,
                "-RepoRoot", repoRoot
            };
            if (!projectOnly)
            {
                arguments.AddRange(["-BaseRef", "master", "-SkipFetch"]);
            }
            return await RunScriptAsync(workingDirectory, scriptPath, [.. arguments]).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs one isolated normalization case using ordinary directories, never a Git fixture.
        /// </summary>
        private static async Task<NormalizationResult> RunNormalizationAsync(
            string[] hits,
            string output = "separate",
            bool writeReport = true,
            bool validCoverage = true)
        {
            string directory = Path.Combine(Path.GetTempPath(), "coverage-normalization", Guid.NewGuid().ToString("N"));
            string inputDirectory = Path.Combine(directory, "input");
            string outputDirectory = output switch
            {
                "same" => inputDirectory,
                "nested" => Path.Combine(inputDirectory, "output"),
                "ancestor" => directory,
                "location-relative" => Path.Combine(directory, "caller", "output"),
                _ => Path.Combine(directory, "output")
            };
            Directory.CreateDirectory(Path.Combine(inputDirectory, "shard"));
            try
            {
                if (output is "empty" or "nonempty")
                {
                    Directory.CreateDirectory(outputDirectory);
                }
                string sentinel = Path.Combine(outputDirectory, "keep.txt");
                if (output == "nonempty")
                {
                    await File.WriteAllTextAsync(sentinel, "keep").ConfigureAwait(false);
                }
                string path = Path.Combine(inputDirectory, "shard", "coverage.cobertura.xml");
                string? original = null;
                if (writeReport)
                {
                    string lines = string.Concat(hits.Select((value, index) =>
                        $"<line number=\"{index + 1}\" hits=\"{SecurityElement.Escape(value)}\" " +
                        "branch=\"true\" condition-coverage=\"75% (3/4)\" />"));
                    original = $$"""
                        <coverage line-rate="0.75" branch-rate="0.75">
                          <sources><source>synthetic-source</source></sources>
                          <packages><package name="SyntheticCounts"><classes>
                            <class name="Subject" filename="SyntheticSubject.txt">
                              <methods><method name="M"><lines>{{lines}}</lines></method></methods>
                              <lines>{{lines}}</lines>
                            </class>
                          </classes></package></packages>
                        </coverage>
                        """;
                    if (!validCoverage)
                    {
                        original = original.Replace("<coverage ", "<other ", StringComparison.Ordinal)
                            .Replace("</coverage>", "</other>", StringComparison.Ordinal);
                    }
                    await File.WriteAllTextAsync(path, original).ConfigureAwait(false);
                }
                string? originalHash = File.Exists(path)
                    ? Convert.ToHexString(SHA256.HashData(
                        await File.ReadAllBytesAsync(path).ConfigureAwait(false)))
                    : null;
                string scriptPath = Path.Combine(
                    FindRepositoryRoot(), ".github", "scripts", "normalize-coverage.ps1");
                string[] arguments = ["-InputDirectory", inputDirectory, "-OutputDirectory", outputDirectory];
                if (output == "location-relative")
                {
                    string callerDirectory = Path.Combine(directory, "caller");
                    Directory.CreateDirectory(callerDirectory);
                    string wrapper = Path.Combine(callerDirectory, "run.ps1");
                    await File.WriteAllTextAsync(wrapper, """
                        param([string] $Script, [string] $InputRoot)
                        Set-Location $PSScriptRoot
                        & $Script -InputDirectory $InputRoot -OutputDirectory output
                        """).ConfigureAwait(false);
                    arguments = [scriptPath, inputDirectory];
                    scriptPath = wrapper;
                }
                ScriptResult result = await RunScriptAsync(
                    directory, scriptPath, arguments).ConfigureAwait(false);
                string normalizedPath = Path.Combine(outputDirectory, "shard", "coverage.cobertura.xml");
                return new NormalizationResult(
                    result,
                    originalHash,
                    File.Exists(path)
                        ? Convert.ToHexString(SHA256.HashData(
                            await File.ReadAllBytesAsync(path).ConfigureAwait(false)))
                        : null,
                    File.Exists(normalizedPath)
                        ? await File.ReadAllTextAsync(normalizedPath).ConfigureAwait(false)
                        : null,
                    File.Exists(sentinel) ? await File.ReadAllTextAsync(sentinel).ConfigureAwait(false) : null);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        /// <summary>
        /// Runs a repository PowerShell script without shell argument interpolation.
        /// </summary>
        private static async Task<ScriptResult> RunScriptAsync(
            string workingDirectory,
            string scriptPath,
            params string[] arguments)
        {
            using var process = new Process();
            process.StartInfo.FileName = "pwsh";
            process.StartInfo.WorkingDirectory = workingDirectory;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(scriptPath);
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
            return new ScriptResult(process.ExitCode, output + error);
        }

        /// <summary>
        /// Exercises temporary class identities using copies, including a shard containing only one partial file.
        /// </summary>
        private static async Task<IdentityResult> RunClassIdentityAsync(string operation, int sourceDepth)
        {
            string directory = Path.Combine(Path.GetTempPath(), "coverage-identities", Guid.NewGuid().ToString("N"));
            string raw = Path.Combine(directory, "raw");
            string copies = Path.Combine(directory, "copies");
            Directory.CreateDirectory(raw);
            Directory.CreateDirectory(copies);
            try
            {
                static XElement MakeClass(string path, int hits) => new("class",
                    new XAttribute("name", "Synthetic.Subject"), new XAttribute("filename", path),
                    new XElement("lines", new XElement("line",
                        new XAttribute("number", 7), new XAttribute("hits", hits),
                        new XAttribute("branch", "true"), new XAttribute("condition-coverage", "50% (1/2)"))));

                static XDocument MakeReport(string root, params XElement[] classes) => new(new XElement("coverage",
                    new XElement("sources", new XElement("source", root)),
                    new XElement("packages", new XElement("package", new XAttribute("name", "SyntheticCounts"),
                        new XElement("classes", classes)))));

                string prefix = sourceDepth switch
                {
                    0 => "src/SyntheticCounts/",
                    1 => "SyntheticCounts/",
                    _ => string.Empty
                };
                string sourceRoot = sourceDepth switch
                {
                    0 => "/other/repository/",
                    1 => "/other/repository/src/",
                    _ => "/other/repository/src/SyntheticCounts/"
                };
                XDocument[] reports =
                [
                    MakeReport("/agent/repository/",
                        MakeClass("src/SyntheticCounts/A/Shared.cs", 1),
                        MakeClass("src/SyntheticCounts/A/Shared.cs", 0)),
                    MakeReport(sourceRoot,
                        MakeClass(prefix + "A/Shared.cs", 0),
                        MakeClass(prefix + "B/Shared.cs", 0))
                ];
                string generatedSuffix = sourceDepth == 1 ? ".<>c" : "/<ReadAsync>d__0";
                reports[0].Descendants("class").Last().SetAttributeValue(
                    "name", "Synthetic.Subject" + generatedSuffix);
                if (operation == "no-collision")
                {
                    reports[1].Descendants("class").Last().SetAttributeValue(
                        "filename", "src/SyntheticCounts/B/Different.cs");
                }
                var before = new List<string>();
                var paths = new List<string>();
                for (int index = 0; index < reports.Length; index++)
                {
                    string path = Path.Combine(raw, index + ".cobertura.xml");
                    await File.WriteAllTextAsync(path, reports[index].ToString()).ConfigureAwait(false);
                    before.Add(Convert.ToHexString(SHA256.HashData(
                        await File.ReadAllBytesAsync(path).ConfigureAwait(false))));
                    File.Copy(path, Path.Combine(copies, Path.GetFileName(path)));
                    paths.Add(path);
                }
                string script = Path.Combine(
                    FindRepositoryRoot(), ".github", "scripts", "prepare-coverage-class-identities.ps1");
                ScriptResult prepared = await RunScriptAsync(
                    directory, script, "-InputDirectory", copies).ConfigureAwait(false);
                Assert.That(prepared.ExitCode, Is.Zero, prepared.Output);
                var classes = new List<XElement>();
                foreach (string path in Directory.GetFiles(copies, "*.cobertura.xml").Order())
                {
                    var copy = XDocument.Parse(
                        await File.ReadAllTextAsync(path).ConfigureAwait(false));
                    XDocument original = reports[int.Parse(Path.GetFileNameWithoutExtension(
                        Path.GetFileNameWithoutExtension(path)), CultureInfo.InvariantCulture)];
                    Assert.That(copy.Root!.Element("sources")!.ToString(),
                        Is.EqualTo(original.Root!.Element("sources")!.ToString()));
                    if (Path.GetFileName(path) == "0.cobertura.xml" && operation != "no-collision")
                    {
                        string parent = copy.Descendants("class").First().Attribute("name")!.Value;
                        Assert.That(copy.Descendants("class").Last().Attribute("name")!.Value,
                            Is.EqualTo(parent + generatedSuffix),
                            "Generated state machines must retain the pinned parser's parent-type grouping.");
                    }
                    classes.AddRange(copy.Descendants("class"));
                }
                foreach (XElement value in classes)
                {
                    string name = value.Attribute("name")!.Value.Split('/')[0];
                    int generated = name.IndexOf(".<", StringComparison.Ordinal);
                    value.SetAttributeValue("name", generated < 0 ? name : name[..generated]);
                }
                if (operation == "unknown")
                {
                    classes[^1].SetAttributeValue("name", "Unknown__UaCoverageFile_0000");
                }
                string merged = Path.Combine(directory, "merged.xml");
                await File.WriteAllTextAsync(merged, MakeReport("unchanged", [.. classes]).ToString())
                    .ConfigureAwait(false);
                string html = Path.Combine(directory, "summary.html");
                await File.WriteAllTextAsync(html, string.Join(" ",
                    classes.Select(value => System.Net.WebUtility.HtmlEncode((string?)value.Attribute("name")))))
                    .ConfigureAwait(false);
                string preparedReport = await File.ReadAllTextAsync(merged).ConfigureAwait(false);
                if (operation == "missing-manifest")
                {
                    File.Delete(Path.Combine(copies, "coverage-class-identities.json"));
                }
                ScriptResult restored = operation == "repeat"
                    ? await RunScriptAsync(directory, script, "-InputDirectory", copies).ConfigureAwait(false)
                    : await RunScriptAsync(directory, script,
                        "-InputDirectory", copies, "-RestoreReport", merged).ConfigureAwait(false);
                var after = new List<string>();
                foreach (string path in paths)
                {
                    after.Add(Convert.ToHexString(SHA256.HashData(
                        await File.ReadAllBytesAsync(path).ConfigureAwait(false))));
                }
                return new IdentityResult(prepared, restored, [.. before], [.. after], preparedReport,
                    await File.ReadAllTextAsync(merged).ConfigureAwait(false),
                    await File.ReadAllTextAsync(html).ConfigureAwait(false));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static string FindRepositoryRoot()
        {
            string? configured = Environment.GetEnvironmentVariable("OPCUA_TEST_REPOSITORY_ROOT");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                string root = Path.GetFullPath(configured);
                if (!File.Exists(Path.Combine(root, ".azurepipelines", "check-coverage.ps1")))
                {
                    throw new InvalidOperationException("The configured repository root has no coverage gate.");
                }

                return root;
            }

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

        /// <summary>Expected method and class line-presence counts in the synthetic normalization fixture.</summary>
        private static readonly string[] s_expectedPresenceHits = ["0", "1", "1", "1", "0", "1", "1", "1"];

        private sealed record ScriptResult(int ExitCode, string Output);

        /// <summary>
        /// Records the raw hashes and reversible class/file identity evidence from a bounded script invocation.
        /// </summary>
        private sealed record IdentityResult(
            ScriptResult Prepared,
            ScriptResult Restored,
            string[] RawHashesBefore,
            string[] RawHashesAfter,
            string PreparedReport,
            string RestoredReport,
            string Html);

        /// <summary>
        /// Captures the script outcome and immutable raw/report evidence before fixture cleanup.
        /// </summary>
        private sealed record NormalizationResult(
            ScriptResult Script,
            string? RawHashBefore,
            string? RawHashAfter,
            string? Normalized,
            string? Sentinel);

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
