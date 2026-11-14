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

#if NET10_0_OR_GREATER
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
        /// <summary>
        /// Artifact qualification installs the exact repository SDK through the shared action,
        /// plus only the .NET 10 runtime that the net10.0 artifact jobs execute on.
        /// </summary>
        [Test]
        public async Task ArtifactJobsInstallTheRepositorySdkAsync()
        {
            string path = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "ualens-artifacts.yml");
            string source = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            string[] steps = [.. source.ReplaceLineEndings("\n")
                .Split("\n    - ", StringSplitOptions.None)
                .Where(step => step.StartsWith(RepositorySdkAction, StringComparison.Ordinal))];

            Assert.That(steps, Is.Not.Empty);
            foreach (string step in steps)
            {
                string[] lines = step.Split('\n');
                // The shared action always builds with the global.json SDK. The only extra
                // install allowed is the 10.0 SDK whose runtime the net10.0 artifact jobs need;
                // it never selects the SDK because global.json pins it.
                Assert.That(
                    lines.Where(line => line.Contains("dotnet-version:", StringComparison.Ordinal)),
                    Is.All.EqualTo("        dotnet-version: 10.0.x"));
            }
        }

        /// <summary>
        /// Runner images preinstall newer SDKs that the global.json roll-forward policy would
        /// select, so every workflow installs .NET through the action that isolates the SDK.
        /// </summary>
        [Test]
        public async Task WorkflowsInstallDotnetOnlyThroughTheRepositorySdkActionAsync()
        {
            string github = Path.Combine(FindRepositoryRoot(), ".github");
            string sharedAction = Path.Combine(github, "actions", "setup-repository-dotnet", "action.yml");
            // The release job checks out the candidate commit, which can predate the shared
            // action, and only pushes packages that were already built.
            string release = Path.Combine(github, "workflows", "release.yml");
            string[] files = [.. Directory.GetFiles(Path.Combine(github, "workflows"), "*.yml")
                .Concat(Directory.GetFiles(Path.Combine(github, "actions"), "action.yml", SearchOption.AllDirectories))
                .Where(file => file != sharedAction && file != release)];

            int uses = 0;
            foreach (string file in files)
            {
                string source = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                Assert.That(source, Does.Not.Contain("actions/setup-dotnet@"), file);
                uses += source.Split(RepositorySdkAction, StringSplitOptions.None).Length - 1;
            }
            Assert.That(uses, Is.GreaterThan(0));
        }

        /// <summary>
        /// The SDK selection installs extra SDKs first, the global.json SDK last, into the
        /// job-private directory.
        /// </summary>
        [Test]
        public async Task RepositorySdkSelectionPinsTheGlobalJsonSdkLastAsync()
        {
            string directory = CreateSdkSelectionDirectory();
            try
            {
                string install = Path.Combine(directory, "dotnet");
                ScriptResult result = await RunSdkSelectionAsync(
                    directory, "8.0.x\n10.0.403\n 9.0.x \n8.0.x\n", install).ConfigureAwait(false);

                Assert.That(result.ExitCode, Is.Zero, result.Output);
                string[] environment = await File.ReadAllLinesAsync(
                    Path.Combine(directory, "env")).ConfigureAwait(false);
                Assert.That(environment, Is.EqualTo(new[] { "DOTNET_INSTALL_DIR=" + install }));
                string[] output = await File.ReadAllLinesAsync(
                    Path.Combine(directory, "output")).ConfigureAwait(false);
                Assert.That(output, Has.Length.EqualTo(6));
                Assert.That(output[0], Is.EqualTo("sdk=10.0.403"));
                Assert.That(output[1], Does.StartWith("versions<<"));
                Assert.That(string.Join(',', output[2..5]), Is.EqualTo("8.0.x,9.0.x,10.0.403"));
                Assert.That(output[5], Is.EqualTo(output[1]["versions<<".Length..]));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        /// <summary>
        /// The SDK selection rejects a malformed extra version before anything is installed.
        /// </summary>
        [TestCase("latest")]
        [TestCase("10")]
        [TestCase("8.0.x; rm -rf /")]
        public async Task RepositorySdkSelectionRejectsInvalidVersionsAsync(string version)
        {
            string directory = CreateSdkSelectionDirectory();
            try
            {
                ScriptResult result = await RunSdkSelectionAsync(
                    directory, version, Path.Combine(directory, "dotnet")).ConfigureAwait(false);

                Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
                Assert.That(result.Output, Does.Contain("::error::"));
                Assert.That(File.Exists(Path.Combine(directory, "env")), Is.False);
                Assert.That(File.Exists(Path.Combine(directory, "output")), Is.False);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        /// <summary>
        /// Every artifact job that builds a project has the history needed by Git versioning.
        /// </summary>
        [Test]
        public async Task ArtifactJobsFetchVersionHistoryAsync()
        {
            string path = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "ualens-artifacts.yml");
            string source = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            string[] steps = [.. source.ReplaceLineEndings("\n")
                .Split("\n    - ", StringSplitOptions.None)
                .Where(step => step.StartsWith("uses: actions/checkout@", StringComparison.Ordinal))];

            Assert.That(steps, Has.Length.EqualTo(5));
            foreach (string step in steps)
            {
                Assert.That(step.Split('\n'), Does.Contain("        fetch-depth: 0"),
                    "The installed-tool job also compiles the NUnit artifact project.");
            }
        }

        /// <summary>
        /// SDK changes requalify the published-package artifact workflow.
        /// </summary>
        [TestCase("ualens-artifacts.yml", 2)]
        public async Task ArtifactTriggersIncludeTheRepositorySdkAsync(string workflow, int expectedTriggers)
        {
            string path = Path.Combine(FindRepositoryRoot(), ".github", "workflows", workflow);
            string[] lines = await File.ReadAllLinesAsync(path).ConfigureAwait(false);

            Assert.That(lines.Count(line => line == "    - 'global.json'"), Is.EqualTo(expectedTriggers));
        }

        /// <summary>
        /// Artifact qualification uses NUnit and cannot opt into an unpublished dependency overlay.
        /// </summary>
        [Test]
        public async Task ArtifactWorkflowUsesNUnitWithoutCandidateOverridesAsync()
        {
            string root = FindRepositoryRoot();
            string source = await File.ReadAllTextAsync(
                Path.Combine(root, ".github", "workflows", "ualens-artifacts.yml")).ConfigureAwait(false);

            Assert.That(source, Does.Contain("tests/Opc.Ua.Lens.Artifacts.Tests/Opc.Ua.Lens.Artifacts.Tests.csproj"));
            Assert.That(source, Does.Contain("RunConfiguration.TreatNoTestsAsError=true"));
            Assert.That(source, Does.Not.Contain("candidate_manifest"));
            Assert.That(source, Does.Not.Contain("python"));
            Assert.That(source, Does.Not.Contain("lldb"));
            Assert.That(source, Does.Not.Contain("test-lens-desktop.ps1"));
            Assert.That(source.Split("FullyQualifiedName~PublishedArtifactTests", StringSplitOptions.None),
                Has.Length.EqualTo(3), "Both the native executable and installed shim must be checked.");
        }

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

        /// <summary>
        /// Native dependencies use each Linux test project's own output directory, without changing the parent.
        /// </summary>
        [TestCase(true, "test", null)]
        [TestCase(true, "test", "/inherited native")]
        [TestCase(false, "test", "/inherited native")]
        [TestCase(true, "build", "/inherited native")]
        public async Task GitHubBatchScopesNativePathsToTheLinuxTestProcessAsync(
            bool linux, string verb, string? inherited)
        {
            string directory = Path.Combine(Path.GetTempPath(), "native output with spaces");
            string environment = inherited is null ? "$null" : QuotePowerShell(inherited);
            string setup = $$"""
                $env:LD_LIBRARY_PATH = {{environment}}
                function Get-Command { [pscustomobject]@{ Source = 'fixture-xvfb-run' } }
                """;
            string invocation = $$"""
                $startInfo = New-DotnetStartInfo `
                    -arguments @({{QuotePowerShell(verb)}}, {{QuotePowerShell(LensProjectFile)}}) `
                    -linux ${{linux.ToString().ToLowerInvariant()}} `
                    -nativeLibraryDirectory {{QuotePowerShell(directory)}}
                @{
                    child = $startInfo.Environment['LD_LIBRARY_PATH']
                    parent = $env:LD_LIBRARY_PATH
                } | ConvertTo-Json -Compress
                """;

            ScriptResult result = await RunGitHubFunctionAsync(setup, invocation).ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.Zero, result.Output);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            string? expected = inherited;
            if (linux && verb == "test")
            {
                expected = directory + ":" + Path.Combine(directory, "runtimes", "linux-x64", "native");
                if (inherited is not null)
                {
                    expected += ":" + inherited;
                }
            }
            Assert.That(document.RootElement.GetProperty("child").GetString(), Is.EqualTo(expected));
            Assert.That(document.RootElement.GetProperty("parent").GetString(), Is.EqualTo(inherited));
        }

        private static string CreateSdkSelectionDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "sdk-selection-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "global.json"),
                """{"sdk":{"version":"10.0.403","rollForward":"latestMajor"}}""");
            return directory;
        }

        private static Task<ScriptResult> RunSdkSelectionAsync(
            string directory,
            string additionalSdks,
            string installDirectory)
        {
            string root = FindRepositoryRoot();
            string script = Path.Combine(root, ".github", "scripts", "select-repository-sdk.ps1");
            string command = $$"""
                $env:GITHUB_ENV = {{QuotePowerShell(Path.Combine(directory, "env"))}}
                $env:GITHUB_OUTPUT = {{QuotePowerShell(Path.Combine(directory, "output"))}}
                & {{QuotePowerShell(script)}} `
                    -AdditionalSdks {{QuotePowerShell(additionalSdks)}} `
                    -GlobalJsonPath {{QuotePowerShell(Path.Combine(directory, "global.json"))}} `
                    -InstallDirectory {{QuotePowerShell(installDirectory)}}
                exit $LASTEXITCODE
                """;
            return RunPowerShellAsync(root, command);
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

        private const string RepositorySdkAction = "uses: ./.github/actions/setup-repository-dotnet";

        private sealed record ScriptResult(int ExitCode, string Output);
    }
}
#endif
