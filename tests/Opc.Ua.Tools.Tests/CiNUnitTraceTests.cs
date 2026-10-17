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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Contract tests for .github/scripts/stage-nunit-trace.ps1, the diagnostics
    /// helper that captures NUnit InternalTrace logs for the net48 server tests.
    /// It must apply to exactly one project and framework, generate only the
    /// minimal settings, stage only fresh logs of that assembly, and report a
    /// missing or oversize capture instead of silently succeeding.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class CiNUnitTraceTests
    {
        /// <summary>
        /// Only Opc.Ua.Server.Tests on net48 without coverage enables the trace.
        /// </summary>
        [TestCase("Opc.Ua.Server.Tests", "net48", "$false", "True")]
        [TestCase("Opc.Ua.Server.Tests", "net10.0", "$false", "False")]
        [TestCase("Opc.Ua.Server.Tests", "net48", "$true", "False")]
        [TestCase("Opc.Ua.Client.Tests", "net48", "$false", "False")]
        [TestCase("opc.ua.server.tests", "net48", "$false", "False")]
        public async Task AppliesOnlyToServerTestsOnNet48Async(
            string project,
            string framework,
            string coverage,
            string expected)
        {
            string output = await RunAsync(
                $"Test-NUnitTraceApplies '{project}' '{framework}' {coverage}").ConfigureAwait(false);
            Assert.That(output.Trim(), Is.EqualTo(expected));
        }

        /// <summary>
        /// The generated settings enable only the Info trace level.
        /// </summary>
        [Test]
        public async Task GeneratedSettingsContainOnlyInfoTraceLevelAsync()
        {
            string output = await RunAsync(
                "$p = New-NUnitTraceSettings (Join-Path ([IO.Path]::GetTempPath()) " +
                "('cit-' + [guid]::NewGuid().ToString('N'))); " +
                "[IO.File]::ReadAllText($p); Remove-Item (Split-Path $p) -Recurse -Force").ConfigureAwait(false);
            Assert.That(
                output.Trim(),
                Is.EqualTo("<RunSettings><NUnit><InternalTraceLevel>Info</InternalTraceLevel></NUnit></RunSettings>"));
        }

        /// <summary>
        /// Only fresh logs of the diagnosed assembly are staged; old, engine and
        /// other-assembly logs are not.
        /// </summary>
        [Test]
        public async Task StagesOnlyFreshMatchingLogsAsync()
        {
            string output = await RunAsync(
                Arrange +
                "$a = Copy-NUnitTraceLogs $o $s.AddSeconds(-1) (Join-Path $r 'd'); " +
                "\"$($a.captureStatus) $(@($a.staged).Count) $(@($a.warnings).Count) " +
                "$((Get-ChildItem (Join-Path $r 'd')).Count)\"; " +
                Cleanup).ConfigureAwait(false);
            Assert.That(output.Trim(), Is.EqualTo("captured 2 0 2"));
        }

        /// <summary>
        /// No fresh log, or an unresolved output directory, is reported as missing
        /// with a warning and creates no destination.
        /// </summary>
        [Test]
        public async Task ReportsMissingCaptureAsync()
        {
            string output = await RunAsync(
                Arrange +
                "$a = Copy-NUnitTraceLogs $o $s.AddHours(1) (Join-Path $r 'd'); " +
                "$b = Copy-NUnitTraceLogs (Join-Path $r 'nope') $s (Join-Path $r 'e'); " +
                "$c = Copy-NUnitTraceLogs '' $s (Join-Path $r 'f'); " +
                "\"$($a.captureStatus) $(@($a.warnings).Count) $($b.captureStatus) $(@($b.warnings).Count) " +
                "$($c.captureStatus) $(@($c.warnings).Count) " +
                "$((Test-Path (Join-Path $r 'd')) -or (Test-Path (Join-Path $r 'e')))\"; " +
                Cleanup).ConfigureAwait(false);
            Assert.That(output.Trim(), Is.EqualTo("missing 1 missing 1 missing 1 False"));
        }

        /// <summary>
        /// A log over the limit is not staged and is never reported as captured:
        /// all oversize is missing, a mix of oversize and staged is partial.
        /// </summary>
        [Test]
        public async Task ReportsOversizeCaptureAsync()
        {
            string output = await RunAsync(
                Arrange +
                "$big = Join-Path $o 'InternalTrace.9.Opc.Ua.Server.Tests.exe.log'; " +
                "[IO.File]::WriteAllText($big, ('y' * 100)); " +
                "$a = Copy-NUnitTraceLogs $o $s.AddSeconds(-1) (Join-Path $r 'd') 50; " +
                "$b = Copy-NUnitTraceLogs $o $s.AddSeconds(-1) (Join-Path $r 'e') 1; " +
                "\"$($a.captureStatus) $(@($a.staged).Count) $(@($a.oversize).Count) " +
                "$($b.captureStatus) $(@($b.staged).Count) $(@($b.oversize).Count)\"; " +
                Cleanup).ConfigureAwait(false);
            Assert.That(output.Trim(), Is.EqualTo("partial 2 1 missing 0 3"));
        }

        /// <summary>
        /// A second staging into the same directory keeps the first copies.
        /// </summary>
        [Test]
        public async Task DoesNotOverwriteOrDeleteStagedLogsAsync()
        {
            string output = await RunAsync(
                Arrange +
                "$d = Join-Path $r 'd'; " +
                "$null = Copy-NUnitTraceLogs $o $s.AddSeconds(-1) $d; " +
                "$null = Copy-NUnitTraceLogs $o $s.AddSeconds(-1) $d; " +
                "\"$((Get-ChildItem $d).Count) $((Get-ChildItem $o).Count)\"; " +
                Cleanup).ConfigureAwait(false);
            Assert.That(output.Trim(), Is.EqualTo("4 5"));
        }

        /// <summary>
        /// The output directory is the parent of an absolute TargetPath; a missing
        /// or relative value yields an empty result so the capture is reported
        /// as missing instead of guessing a location.
        /// </summary>
        [TestCase("absolute", true)]
        [TestCase("relative", false)]
        [TestCase("empty", false)]
        [TestCase("blank", false)]
        public async Task ResolvesOutputDirectoryFromTargetPathAsync(string kind, bool resolves)
        {
            string directory = Path.Combine(Path.GetTempPath(), "cit-target", "bin", "net48");
            string targetPath = kind switch
            {
                "absolute" => Path.Combine(directory, "Opc.Ua.Server.Tests.exe"),
                "relative" => Path.Combine("bin", "Opc.Ua.Server.Tests.exe"),
                "blank" => "   ",
                _ => string.Empty
            };
            string encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(targetPath));
            string output = await RunAsync(
                $"$i = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encoded}')); " +
                "'[' + (Get-TargetPathDirectory $i) + ']'").ConfigureAwait(false);
            Assert.That(output.Trim(), Is.EqualTo("[" + (resolves ? directory : string.Empty) + "]"));
        }

        /// <summary>
        /// A successful evaluation, even with warnings around the document, yields
        /// the directory of TargetPath.
        /// </summary>
        [Test]
        public async Task EvaluationResolvesDirectoryFromJsonDocumentAsync()
        {
            string target = Path.Combine(Path.GetTempPath(), "cit-eval", "bin", "t.exe");
            string json = "{\"Properties\":{\"TargetPath\":" + System.Text.Json.JsonSerializer.Serialize(target) + "}}";
            string output = await RunEvaluationAsync("'warning MSB: x'\n'" + json + "'\n", "1").ConfigureAwait(false);
            Assert.That(output, Is.EqualTo("[" + Path.GetDirectoryName(target) + "]|[]|marker=False|alive=False"));
        }

        /// <summary>
        /// Every way the evaluation can fail leaves the directory empty and names
        /// the reason, including the stderr of a failed process.
        /// </summary>
        [TestCase("[Console]::Error.WriteLine('bad stuff'); exit 3\n", "exited with 3: bad stuff")]
        [TestCase("'nothing'\n", "no property document")]
        [TestCase("'{\"Properties\":{\"TargetPath\":\"bin/t.exe\"}}'\n", "is not an absolute path")]
        public async Task EvaluationFailureNamesReasonAsync(string body, string reason)
        {
            string output = await RunEvaluationAsync(body, "1").ConfigureAwait(false);
            Assert.That(output, Does.StartWith("[]|["));
            Assert.That(output, Does.Contain(reason));
        }

        /// <summary>
        /// An exhausted shared budget starts no process at all.
        /// </summary>
        [Test]
        public async Task EvaluationStartsNothingWhenBudgetIsExhaustedAsync()
        {
            string output = await RunEvaluationAsync("Set-Content $env:CIT_MARKER 'ran'\n", "0").ConfigureAwait(false);
            Assert.That(output, Does.Contain("budget is already exhausted"));
            Assert.That(output, Does.Contain("|marker=False|"));
        }

        /// <summary>
        /// A child that outlives the remaining budget is stopped at the budget and
        /// its process is gone afterwards.
        /// </summary>
        [Test]
        public async Task EvaluationStopsHungChildAtBudgetAsync()
        {
            string output = await RunEvaluationAsync(
                "Set-Content $env:CIT_PIDFILE $PID\nStart-Sleep 300\n",
                "0.1").ConfigureAwait(false);
            Assert.That(output, Does.Contain("did not finish within the remaining shared budget"));
            Assert.That(output, Does.EndWith("|alive=False"));
        }
        private static async Task<string> RunEvaluationAsync(string childBody, string budgetMinutes)
        {
            string directory = Path.Combine(Path.GetTempPath(), "cit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string child = Path.Combine(directory, "child.ps1");
                await File.WriteAllTextAsync(child, childBody).ConfigureAwait(false);
                string runner = Path.Combine(FindRepositoryRoot(), ".github", "scripts", "run-dotnet-tests.ps1");
                string marker = Path.Combine(directory, "marker.txt");
                string pidFile = Path.Combine(directory, "pid.txt");
                string command =
                    $"$env:CIT_MARKER = '{Quote(marker)}'; $env:CIT_PIDFILE = '{Quote(pidFile)}'; " +
                    "$Framework = 'net48'; $CustomTestTarget = 'net48'; $Configuration = 'Debug'; " +
                    $"$ast = [Management.Automation.Language.Parser]::ParseFile(" +
                    $"'{Quote(runner)}', [ref]$null, [ref]$null); " +
                    "$fn = $ast.Find({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] " +
                    "-and $n.Name -eq 'Get-TestAssemblyDirectory' }, $true); Invoke-Expression $fn.Extent.Text; " +
                    "$sw = [Diagnostics.Stopwatch]::StartNew(); " +
                    $"$r = Get-TestAssemblyDirectory 'p.csproj' $sw {budgetMinutes} 'pwsh' " +
                    $"@('-NoProfile', '-File', '{Quote(child)}'); " +
                    "$alive = $false; if (Test-Path $env:CIT_PIDFILE) { " +
                    "$alive = [bool](Get-Process -Id ([int](Get-Content $env:CIT_PIDFILE)) " +
                    "-ErrorAction SilentlyContinue) }; " +
                    "\"[$($r.Directory)]|[$($r.Reason)]|marker=$(Test-Path $env:CIT_MARKER)|alive=$alive\"";
                return (await RunAsync(command).ConfigureAwait(false)).Trim();
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string Quote(string value)
        {
            return value.Replace("'", "''", StringComparison.Ordinal);
        }
        private static async Task<string> RunAsync(string command)
        {
            string root = FindRepositoryRoot();
            string script = Path.Combine(root, ".github", "scripts", "stage-nunit-trace.ps1");
            using var process = new Process();
            process.StartInfo.FileName = "pwsh";
            process.StartInfo.WorkingDirectory = root;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            PowerShellScriptOutput.ConfigureDeterministicOutput(process.StartInfo);
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-Command");
            string quotedScript = script.Replace("'", "''", StringComparison.Ordinal);
            process.StartInfo.ArgumentList.Add(". '" + quotedScript + "'; " + command);

            Assert.That(process.Start(), Is.True);
            try
            {
                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> standardError = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

                string output = await standardOutput.ConfigureAwait(false);
                string error = await standardError.ConfigureAwait(false);
                Assert.That(
                    process.ExitCode,
                    Is.Zero,
                    $"stage-nunit-trace.ps1 failed: {PowerShellScriptOutput.Normalize(output + error)}");
                return output;
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try
                    {
                        await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        TestContext.Error.WriteLine(
                            "Owned pwsh process " + process.Id + " did not exit within 10 seconds of Kill(true).");
                    }
                }
            }
        }

        /// <summary>
        /// Locates the checkout from this source file first, because the build output
        /// may live outside the repository (a redirected artifacts path), then from
        /// the test and work directories.
        /// </summary>
        private static string FindRepositoryRoot([CallerFilePath] string sourceFile = "")
        {
            string?[] starts =
            [
                Path.GetDirectoryName(sourceFile),
                TestContext.CurrentContext.TestDirectory,
                TestContext.CurrentContext.WorkDirectory
            ];
            foreach (string? start in starts)
            {
                string? current = start;
                while (!string.IsNullOrWhiteSpace(current))
                {
                    if (File.Exists(Path.Combine(current, ".github", "scripts", "stage-nunit-trace.ps1")))
                    {
                        return current;
                    }
                    current = Directory.GetParent(current)?.FullName;
                }
            }

            throw new InvalidOperationException("Could not find the repository root.");
        }

        private const string Arrange =
            "$r = Join-Path ([IO.Path]::GetTempPath()) ('cit-' + [guid]::NewGuid().ToString('N')); " +
            "$o = Join-Path $r 'bin'; $null = New-Item -ItemType Directory $o; $s = [datetime]::UtcNow; " +
            "foreach ($n in 'InternalTrace.2.Opc.Ua.Server.Tests.exe.log'," +
            "'InternalTrace.3.Opc.Ua.Server.Tests.dll.log','InternalTrace.4.log'," +
            "'InternalTrace.5.Opc.Ua.Client.Tests.exe.log') { 'x' | Set-Content (Join-Path $o $n) }; " +
            "$old = Join-Path $o 'InternalTrace.1.Opc.Ua.Server.Tests.exe.log'; 'x' | Set-Content $old; " +
            "(Get-Item $old).LastWriteTimeUtc = $s.AddMinutes(-10); ";

        private const string Cleanup = "Remove-Item $r -Recurse -Force";
    }
}
#endif
