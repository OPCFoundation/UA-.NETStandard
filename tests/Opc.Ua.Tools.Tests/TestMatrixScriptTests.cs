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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class TestMatrixScriptTests
    {
        [TestCase(true, "test", LensProjectFile, true)]
        [TestCase(true, "test", "tests/Opc.Ua.Lens.Desktop.Tests/Opc.Ua.Lens.Desktop.Tests.csproj", true)]
        [TestCase(true, "test", "tests/Opc.Ua.Lens.Tests/Opc.Ua.Lens.Tests.csproj", false)]
        [TestCase(false, "test", LensProjectFile, false)]
        [TestCase(true, "build", LensProjectFile, false)]
        [TestCase(true, "test", "tests/Opc.Ua.Core.Tests/Opc.Ua.Core.Tests.csproj", false)]
        [TestCase(true, "test", "tests/Other/Opc.Ua.Lens.Tests.csproj.other", false)]
        public async Task GitHubBatchWrapsOnlyLinuxDesktopTestsAndPreservesArgumentsAsync(
            bool linux, string verb, string project, bool wrapped)
        {
            string discovery = wrapped
                ? """
                    function Get-Command
                    {
                        param([string] $Name, [string] $CommandType, [string] $ErrorAction)
                        if ($Name -ne 'xvfb-run' -or $CommandType -ne 'Application')
                        {
                            throw 'Unexpected command discovery.'
                        }
                        [pscustomobject]@{ Source = 'fixture-xvfb-run' }
                    }
                    """
                : "function Get-Command { throw 'Non-desktop invocations must not require Xvfb.' }";
            string[] arguments =
            [
                verb, project, "--filter", "TestCategory!=LongRunning&TestCategory!=Stress",
                "--results-directory", "results with spaces", "--collect:XPlat Code Coverage"
            ];
            string invocation = $$"""
                $startInfo = New-DotnetStartInfo `
                    -arguments @({{string.Join(", ", arguments.Select(QuotePowerShell))}}) `
                    -linux ${{linux.ToString().ToLowerInvariant()}}
                @{
                    fileName = $startInfo.FileName
                    arguments = @($startInfo.ArgumentList)
                    useShell = $startInfo.UseShellExecute
                } | ConvertTo-Json -Compress
                """;

            ScriptResult result = await RunGitHubFunctionAsync(discovery, invocation).ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.Zero, result.Output);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.That(document.RootElement.GetProperty("fileName").GetString(),
                Is.EqualTo(wrapped ? "fixture-xvfb-run" : "dotnet"));
            Assert.That(document.RootElement.GetProperty("useShell").GetBoolean(), Is.False);
            string[] expected = wrapped ? ["-a", "dotnet", .. arguments] : arguments;
            Assert.That(document.RootElement.GetProperty("arguments").EnumerateArray()
                .Select(static argument => argument.GetString()), Is.EqualTo(expected));
        }

        [Test]
        public async Task GitHubBatchRejectsMissingVirtualDisplayWithoutFallbackAsync()
        {
            ScriptResult result = await RunGitHubFunctionAsync(
                "function Get-Command { }",
                $"New-DotnetStartInfo -arguments @('test', {QuotePowerShell(LensProjectFile)}) -linux $true")
                .ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
            Assert.That(PowerShellScriptOutput.Normalize(result.Output),
                Does.Contain("UaLens desktop tests require xvfb-run on the Linux agent."));
        }

        [TestCase(1)]
        [TestCase(2)]
        public async Task GitHubBatchSelectsFirstVirtualDisplayExecutableAsync(int matches)
        {
            string discovery = $$"""
                function Get-Command
                {
                    param([string] $Name, [string] $CommandType, [string] $ErrorAction)
                    if ($Name -ne 'xvfb-run' -or $CommandType -ne 'Application')
                    {
                        throw 'Unexpected command discovery.'
                    }
                    for ($index = 0; $index -lt {{matches}}; $index++)
                    {
                        [pscustomobject]@{ Source = "fixture-$index-xvfb-run" }
                    }
                }
                """;
            string invocation = $$"""
                $startInfo = New-DotnetStartInfo -arguments @('test', {{QuotePowerShell(LensProjectFile)}}) -linux $true
                @{
                    fileName = $startInfo.FileName
                    arguments = @($startInfo.ArgumentList)
                } | ConvertTo-Json -Compress
                """;

            ScriptResult result = await RunGitHubFunctionAsync(discovery, invocation).ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.Zero, result.Output);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.That(document.RootElement.GetProperty("fileName").GetString(), Is.EqualTo("fixture-0-xvfb-run"));
            string[] expectedArguments = ["-a", "dotnet", "test", LensProjectFile];
            Assert.That(document.RootElement.GetProperty("arguments").EnumerateArray()
                .Select(static argument => argument.GetString()), Is.EqualTo(expectedArguments));
        }

        private static Task<ScriptResult> RunGitHubFunctionAsync(
            string discovery,
            string invocation,
            string functionName = "New-DotnetStartInfo")
        {
            string root = FindRepositoryRoot();
            string executor = Path.Combine(root, ".github", "scripts", "run-dotnet-tests.ps1");
            string command = $$"""
                $ErrorActionPreference = 'Stop'
                $ast = [System.Management.Automation.Language.Parser]::ParseFile(
                    {{QuotePowerShell(executor)}}, [ref]$null, [ref]$null)
                $definition = $ast.Find({
                    param($node)
                    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                        $node.Name -eq {{QuotePowerShell(functionName)}}
                }, $true)
                if ($null -eq $definition) { throw 'The batch runner function was not found.' }
                . ([scriptblock]::Create($definition.Extent.Text))
                {{discovery}}
                {{invocation}}
                """;
            return RunPowerShellAsync(root, command);
        }

        private static async Task<ScriptResult> RunPowerShellAsync(string workingDirectory, string command)
        {
            using var process = new Process();
            process.StartInfo.FileName = "pwsh";
            process.StartInfo.WorkingDirectory = workingDirectory;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.ArgumentList.Add("-NoLogo");
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-NonInteractive");
            process.StartInfo.ArgumentList.Add("-Command");
            process.StartInfo.ArgumentList.Add(command);
            process.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true";
            process.StartInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "true";
            process.StartInfo.Environment["DOTNET_NOLOGO"] = "true";

            Assert.That(process.Start(), Is.True);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> standardError = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                string output = await standardOutput.ConfigureAwait(false);
                string error = await standardError.ConfigureAwait(false);
                return new ScriptResult(process.ExitCode, output + Environment.NewLine + error);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await process.WaitForExitAsync(cleanupTimeout.Token).ConfigureAwait(false);
                }
            }
        }

        private static string FindRepositoryRoot()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, ".github", "scripts", "run-dotnet-tests.ps1")))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName;
            }
            throw new InvalidOperationException("Could not find the GitHub test runner.");
        }

        private static string QuotePowerShell(string value)
        {
            return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        }

        private const string LensProjectFile =
            "tests/Opc.Ua.Lens.Workflow.Tests/Opc.Ua.Lens.Workflow.Tests.csproj";

        private sealed record ScriptResult(int ExitCode, string Output);
    }
}
#endif
