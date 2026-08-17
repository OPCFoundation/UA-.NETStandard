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
using System.Text;
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

        private static async Task<ScriptResult> RunCoverageGateAsync(
            string workingDirectory,
            string coberturaPath,
            string repoRoot)
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

            public string WriteCoberturaReport(string filePath, string sourceRoot, int lineNumber, int hits)
            {
                string reportPath = Path.Combine(RootPath, "coverage.xml");
                string normalizedSourceRoot = NormalizeCoberturaPath(sourceRoot);
                string normalizedFilePath = NormalizeCoberturaPath(filePath);
                File.WriteAllText(
                    reportPath,
                    $$"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <coverage line-rate="1" branch-rate="1" version="1.0">
                      <sources>
                        <source>{{EscapeXml(normalizedSourceRoot)}}</source>
                      </sources>
                      <packages>
                        <package name="CoverageSubject" line-rate="1" branch-rate="1">
                          <classes>
                            <class name="CoverageSubject.Subject"
                                   filename="{{EscapeXml(normalizedFilePath)}}"
                                   line-rate="1"
                                   branch-rate="1">
                              <lines>
                                <line number="{{lineNumber}}" hits="{{hits}}" />
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
