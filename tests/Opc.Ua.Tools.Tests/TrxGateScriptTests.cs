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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class TrxGateScriptTests
    {
        [TestCase("Completed", false)]
        [TestCase("Completed", true)]
        [TestCase("Passed", false)]
        [TestCase("Passed", true)]
        public async Task CompletedReportRemainsAuthoritativeForSucceededWithIssuesAsync(
            string outcome,
            bool prefixedNamespace)
        {
            using var fixture = new ResultsFixture();
            XDocument document = CreateTrx(outcome);
            if (prefixedNamespace)
            {
                document.Root!.SetAttributeValue(XNamespace.Xmlns + "trx", s_namespace.NamespaceName);
            }
            fixture.Write(document);

            ScriptResult result = await fixture.RunAsync("SucceededWithIssues").ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.Zero, result.Output);
            Assert.That(result.Output, Does.Contain("total=2, passed=2"));
        }

        [TestCase(0)]
        [TestCase(1)]
        public async Task CompletedRunAllowsWarningsAndSkippedResultsAsync(int skippedCounter)
        {
            using var fixture = new ResultsFixture();
            XDocument document = CreateTrx(passed: 2, skipped: 1);
            Counters(document).SetAttributeValue("notExecuted", skippedCounter);
            AddRunInfo(document, "Warning");
            fixture.Write(document);

            ScriptResult result = await fixture.RunAsync("SucceededWithIssues").ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.Zero, result.Output);
            Assert.That(result.Output, Does.Contain("total=3, passed=2"));
        }

        [TestCase("Failed")]
        [TestCase("Error")]
        [TestCase("Aborted")]
        [TestCase("Timeout")]
        [TestCase("InProgress")]
        [TestCase("Pending")]
        [TestCase("NotExecuted")]
        [TestCase("NotRunnable")]
        [TestCase("Disconnected")]
        [TestCase("Unknown")]
        [TestCase("")]
        public async Task AllGreenPartialRunRejectsNonCompletedSummaryAsync(string outcome)
        {
            using var fixture = new ResultsFixture();
            fixture.Write(CreateTrx(outcome, passed: 264));

            await AssertRejectedAsync(fixture, "run outcome").ConfigureAwait(false);
        }

        [TestCase("Error")]
        [TestCase("Failed")]
        [TestCase("Aborted")]
        [TestCase("InProgress")]
        [TestCase("Pending")]
        [TestCase("")]
        public async Task RunLevelFailureRejectsGreenCountersAsync(string outcome)
        {
            using var fixture = new ResultsFixture();
            XDocument document = CreateTrx();
            AddRunInfo(document, outcome);
            fixture.Write(document);

            await AssertRejectedAsync(fixture, "RunInfo").ConfigureAwait(false);
        }

        [Test]
        public async Task CrashAfterPassingTestsIsNotACompletedGreenRunAsync()
        {
            using var fixture = new ResultsFixture();
            XDocument document = CreateTrx("Failed", passed: 264);
            AddRunInfo(document, "Error");
            fixture.Write(document);

            await AssertRejectedAsync(fixture, "run outcome").ConfigureAwait(false);
        }

        [Test]
        public async Task BlameTerminationWithAllRecordedTestsPassedIsRejectedAsync()
        {
            using var fixture = new ResultsFixture();
            XDocument document = CreateTrx("Failed", passed: 264);
            AddRunInfo(document, "Error");
            Summary(document)
                .Element(s_namespace + "RunInfos")!
                .Element(s_namespace + "RunInfo")!
                .Element(s_namespace + "Text")!
                .Value = "The active test run was aborted. Reason: " +
                    "The blame collector terminated the host during process exit.";
            fixture.Write(document);

            await AssertRejectedAsync(fixture, "run outcome").ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MissingOrDuplicateSummaryRejectsGreenCountersAsync(bool duplicate)
        {
            using var fixture = new ResultsFixture();
            XDocument document = CreateTrx();
            if (duplicate)
            {
                document.Root!.Add(new XElement(Summary(document)));
            }
            else
            {
                document.Root!.Add(new XElement(Counters(document)));
                Summary(document).Remove();
            }
            fixture.Write(document);

            await AssertRejectedAsync(fixture, "ResultSummary").ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MissingOrDuplicateCountersFailAsync(bool duplicate)
        {
            using var fixture = new ResultsFixture();
            XDocument document = CreateTrx();
            if (duplicate)
            {
                Summary(document).Add(new XElement(Counters(document)));
            }
            else
            {
                Counters(document).Remove();
            }
            fixture.Write(document);

            await AssertRejectedAsync(fixture, "Counters").ConfigureAwait(false);
        }

        [TestCase("total")]
        [TestCase("executed")]
        [TestCase("passed")]
        [TestCase("failed")]
        [TestCase("aborted")]
        [TestCase("pending")]
        public async Task MissingCounterAttributeFailsClosedAsync(string counter)
        {
            using var fixture = new ResultsFixture();
            XDocument document = CreateTrx();
            Counters(document).SetAttributeValue(counter, null);
            fixture.Write(document);

            await AssertRejectedAsync(fixture, $"counter '{counter}'").ConfigureAwait(false);
        }

        [TestCase("")]
        [TestCase("-1")]
        [TestCase("not-a-number")]
        [TestCase("1.5")]
        [TestCase("9223372036854775808")]
        public async Task MalformedCounterAttributeFailsClosedAsync(string value)
        {
            using var fixture = new ResultsFixture();
            XDocument document = CreateTrx();
            Counters(document).SetAttributeValue("failed", value);
            fixture.Write(document);

            await AssertRejectedAsync(fixture, "counter 'failed'").ConfigureAwait(false);
        }

        [TestCase("failed")]
        [TestCase("error")]
        [TestCase("timeout")]
        [TestCase("aborted")]
        [TestCase("passedButRunAborted")]
        [TestCase("inconclusive")]
        [TestCase("notRunnable")]
        [TestCase("disconnected")]
        [TestCase("warning")]
        [TestCase("completed")]
        [TestCase("inProgress")]
        [TestCase("pending")]
        public async Task NonPassingOrUnfinishedCounterRejectsRunAsync(string counter)
        {
            using var fixture = new ResultsFixture();
            XDocument document = CreateTrx();
            Counters(document).SetAttributeValue(counter, 1);
            fixture.Write(document);

            await AssertRejectedAsync(fixture, counter).ConfigureAwait(false);
        }

        [TestCase("total", 3)]
        [TestCase("executed", 1)]
        [TestCase("passed", 1)]
        [TestCase("notExecuted", 1)]
        public async Task CountersMustMatchRecordedResultsAsync(string counter, int value)
        {
            using var fixture = new ResultsFixture();
            XDocument document = CreateTrx();
            Counters(document).SetAttributeValue(counter, value);
            fixture.Write(document);

            await AssertRejectedAsync(fixture, "recorded results").ConfigureAwait(false);
        }

        [TestCase("Failed")]
        [TestCase("Aborted")]
        [TestCase("InProgress")]
        [TestCase("Pending")]
        [TestCase("Unknown")]
        [TestCase("")]
        public async Task NonPassingRecordedResultCannotHideBehindGreenCountersAsync(string outcome)
        {
            using var fixture = new ResultsFixture();
            XDocument document = CreateTrx();
            document.Root!.Element(s_namespace + "Results")!.Elements().First().SetAttributeValue("outcome", outcome);
            fixture.Write(document);

            await AssertRejectedAsync(fixture, "test outcome").ConfigureAwait(false);
        }

        [Test]
        public async Task MissingRecordedResultsFailAsync()
        {
            using var fixture = new ResultsFixture();
            XDocument document = CreateTrx();
            document.Root!.Element(s_namespace + "Results")!.Remove();
            fixture.Write(document);

            await AssertRejectedAsync(fixture, "recorded results").ConfigureAwait(false);
        }

        [TestCase("aborted", false)]
        [TestCase("aborted", true)]
        [TestCase("missing-counters", false)]
        [TestCase("missing-counters", true)]
        [TestCase("malformed", false)]
        [TestCase("malformed", true)]
        [TestCase("empty", false)]
        [TestCase("empty", true)]
        public async Task MultipleFilesCannotMaskAnInvalidRunAsync(string failure, bool invalidFirst)
        {
            using var fixture = new ResultsFixture();
            string invalidName = invalidFirst ? "a-invalid.trx" : "z-invalid.trx";
            fixture.Write(CreateTrx(), "m-valid.trx");
            XDocument invalid = CreateTrx();
            switch (failure)
            {
                case "aborted":
                    invalid = CreateTrx("Failed", passed: 0);
                    AddRunInfo(invalid, "Error");
                    break;
                case "missing-counters":
                    Counters(invalid).Remove();
                    break;
                case "malformed":
                    fixture.WriteText("<TestRun>", invalidName);
                    break;
                case "empty":
                    invalid = CreateTrx(passed: 0);
                    break;
            }
            if (failure != "malformed")
            {
                fixture.Write(invalid, invalidName);
            }

            await AssertRejectedAsync(fixture, invalidName).ConfigureAwait(false);
        }

        [Test]
        public async Task MultipleCompletedFilesAggregateTheirResultsAsync()
        {
            using var fixture = new ResultsFixture();
            fixture.Write(CreateTrx(passed: 2), "first.trx");
            fixture.Write(CreateTrx(passed: 3), "second.trx");

            ScriptResult result = await fixture.RunAsync("SucceededWithIssues").ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.Zero, result.Output);
            Assert.That(result.Output, Does.Contain("total=5, passed=5"));
        }

        [TestCase("Succeeded", 0)]
        [TestCase("SucceededWithIssues", 1)]
        [TestCase("Failed", 1)]
        [TestCase("Canceled", 1)]
        [TestCase("Unknown", 1)]
        [TestCase("", 1)]
        public async Task NoTrxPreservesMtpJobStatusFallbackAsync(string jobStatus, int expectedExitCode)
        {
            using var fixture = new ResultsFixture();

            ScriptResult result = await fixture.RunAsync(jobStatus).ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(expectedExitCode), result.Output);
            Assert.That(result.Output, Does.Contain("No VSTest TRX"));
            Assert.That(result.Output, Does.Contain($"Agent.JobStatus={jobStatus}"));
        }

        [Test]
        public async Task MissingDirectoryDoesNotPretendMtpSuccessAsync()
        {
            using var fixture = new ResultsFixture();
            Directory.Delete(fixture.DirectoryPath);

            await AssertRejectedAsync(fixture, "Could not evaluate").ConfigureAwait(false);
        }

        private static async Task AssertRejectedAsync(ResultsFixture fixture, string reason)
        {
            ScriptResult result = await fixture.RunAsync("Succeeded").ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
            Assert.That(result.Output, Does.Contain("##vso[task.logissue type=error]"), result.Output);
            Assert.That(result.Output, Does.Contain(reason), result.Output);
        }

        private static XDocument CreateTrx(string outcome = "Completed", int passed = 2, int skipped = 0)
        {
            var results = new XElement(s_namespace + "Results");
            for (int i = 0; i < passed + skipped; i++)
            {
                results.Add(new XElement(
                    s_namespace + "UnitTestResult",
                    new XAttribute("testName", $"Test{i}"),
                    new XAttribute("outcome", i < passed ? "Passed" : "NotExecuted")));
            }

            return new XDocument(new XElement(
                s_namespace + "TestRun",
                new XElement(s_namespace + "Times",
                    new XAttribute("start", "2026-09-18T22:59:36Z"),
                    new XAttribute("finish", "2026-09-18T22:59:53Z")),
                results,
                new XElement(
                    s_namespace + "ResultSummary",
                    new XAttribute("outcome", outcome),
                    new XElement(s_namespace + "Counters",
                        new XAttribute("total", passed + skipped),
                        new XAttribute("executed", passed),
                        new XAttribute("passed", passed),
                        new XAttribute("failed", 0),
                        new XAttribute("error", 0),
                        new XAttribute("timeout", 0),
                        new XAttribute("aborted", 0),
                        new XAttribute("inconclusive", 0),
                        new XAttribute("passedButRunAborted", 0),
                        new XAttribute("notRunnable", 0),
                        new XAttribute("notExecuted", 0),
                        new XAttribute("disconnected", 0),
                        new XAttribute("warning", 0),
                        new XAttribute("completed", 0),
                        new XAttribute("inProgress", 0),
                        new XAttribute("pending", 0)))));
        }

        private static XElement Summary(XDocument document)
        {
            return document.Root!.Element(s_namespace + "ResultSummary")!;
        }

        private static XElement Counters(XDocument document)
        {
            return Summary(document).Element(s_namespace + "Counters")!;
        }

        private static void AddRunInfo(XDocument document, string outcome)
        {
            Summary(document).Add(new XElement(s_namespace + "RunInfos",
                new XElement(s_namespace + "RunInfo",
                    new XAttribute("outcome", outcome),
                    new XElement(s_namespace + "Text", "Test host completion diagnostic."))));
        }

        private static string FindScript()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                string script = Path.Combine(current, ".azurepipelines", "evaluate-test-results.ps1");
                if (File.Exists(script))
                {
                    return script;
                }
                current = Directory.GetParent(current)?.FullName;
            }
            throw new InvalidOperationException("Could not find the TRX gate script.");
        }

        private static readonly XNamespace s_namespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

        private sealed record ScriptResult(int ExitCode, string Output);

        private sealed class ResultsFixture : IDisposable
        {
            public ResultsFixture()
            {
                DirectoryPath = Path.Combine(
                    TestContext.CurrentContext.WorkDirectory, "trx-gate-fixtures", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(DirectoryPath);
            }

            public string DirectoryPath { get; }

            public void Write(XDocument document, string name = "results.trx")
            {
                document.Save(Path.Combine(DirectoryPath, name));
            }

            public void WriteText(string text, string name)
            {
                File.WriteAllText(Path.Combine(DirectoryPath, name), text);
            }

            public async Task<ScriptResult> RunAsync(string jobStatus)
            {
                using var process = new Process();
                process.StartInfo.FileName = OperatingSystem.IsWindows()
                    ? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "WindowsPowerShell", "v1.0", "powershell.exe")
                    : "pwsh";
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(FindScript())!;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.Environment["AGENT_JOBSTATUS"] = jobStatus;
                foreach (string argument in new[]
                {
                    "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", FindScript(),
                    "-ResultsDirectory", DirectoryPath
                })
                {
                    process.StartInfo.ArgumentList.Add(argument);
                }

                Assert.That(process.Start(), Is.True);
                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> standardError = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    await process.WaitForExitAsync().ConfigureAwait(false);
                    throw;
                }
                string output = await standardOutput.ConfigureAwait(false);
                string error = await standardError.ConfigureAwait(false);
                return new ScriptResult(process.ExitCode, output + error);
            }

            public void Dispose()
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }
        }
    }
}
#endif
