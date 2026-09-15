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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed partial class TestMatrixScriptTests
    {
        [TestCase("net48", "Legacy.Tests.csproj,Mixed.Tests.csproj")]
        [TestCase("net10.0", "Modern.Tests.csproj,Mixed.Tests.csproj,Pinned.Tests.csproj")]
        public async Task TestFrameworkFiltersSingleAndMultiTargetProjectsAsync(
            string framework,
            string expectedFiles)
        {
            using TestProjects projects = await TestProjects.CreateAsync().ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Legacy.Tests.csproj",
                "<TargetFramework>net48</TargetFramework>").ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Mixed.Tests.csproj",
                "<TargetFrameworks>net48;net8.0;net10.0</TargetFrameworks>").ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Modern.Tests.csproj",
                "<TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>").ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Pinned.Tests.csproj",
                "<TargetFramework>net10.0</TargetFramework>").ConfigureAwait(false);

            ScriptResult result = await projects.RunAsync(
                framework,
                extraArguments:
                    "-Files 'Legacy.Tests.csproj,Mixed.Tests.csproj,Modern.Tests.csproj,Pinned.Tests.csproj'")
                .ConfigureAwait(false);

            Assert.That(GetMatrixFiles(result), Is.EquivalentTo(expectedFiles.Split(',')));
            Assert.That(
                Directory.EnumerateDirectories(projects.RootPath, "obj", SearchOption.AllDirectories),
                Is.Empty,
                "Discovery must evaluate properties without building or restoring a project.");
        }

        [TestCase("net472", "net472")]
        [TestCase("netstandard2.0", "net48")]
        [TestCase("netstandard2.1", "net8.0")]
        [TestCase("net10.0", "net10.0")]
        public async Task CustomTestTargetUsesEvaluatedRuntimeFrameworkAsync(
            string customTestTarget,
            string framework)
        {
            using TestProjects projects = await TestProjects.CreateAsync().ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Shared.Tests.csproj",
                "<TargetFrameworks>$(TestsTargetFrameworks)</TargetFrameworks>").ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Unrelated.Tests.csproj",
                "<TargetFramework>net9.0</TargetFramework>").ConfigureAwait(false);

            ScriptResult result = await projects.RunAsync(framework, customTestTarget).ConfigureAwait(false);

            Assert.That(GetMatrixFiles(result), Is.EqualTo(s_sharedProjectFiles));
        }

        [TestCase("", "net10.0")]
        [TestCase("netstandard2.1", "net8.0")]
        public async Task ExplicitCustomTestTargetOverridesInheritedEnvironmentAsync(
            string customTestTarget,
            string framework)
        {
            using TestProjects projects = await TestProjects.CreateAsync().ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Conditional.Tests.csproj",
                "<TargetFrameworks>$(TestsTargetFrameworks)</TargetFrameworks>").ConfigureAwait(false);

            ScriptResult result = await projects.RunAsync(
                framework,
                customTestTarget,
                inheritedCustomTestTarget: "net472").ConfigureAwait(false);

            Assert.That(GetMatrixFiles(result), Is.EqualTo(s_conditionalProjectFiles));
        }

        [TestCase("", "net48", "Legacy.Tests.csproj")]
        [TestCase("net48", "net48", "Legacy.Tests.csproj")]
        [TestCase("net472", "net472", "Legacy.Tests.csproj")]
        [TestCase("netstandard2.0", "net48", "Legacy.Tests.csproj")]
        [TestCase("netstandard2.1", "net8.0", "Legacy.Tests.csproj")]
        [TestCase("net10.0", "net10.0", "Legacy.Tests.csproj,Restricted.Tests.csproj")]
        public async Task LegacyRestrictedProjectsAreExcludedButRealLegacyTestsRemainAsync(
            string customTestTarget,
            string framework,
            string expectedFiles)
        {
            using TestProjects projects = await TestProjects.CreateAsync().ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Legacy.Tests.csproj",
                """
                <TargetFrameworks>$(TestsTargetFrameworks)</TargetFrameworks>
                <IsTestProject>true</IsTestProject>
                """).ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Restricted.Tests.csproj",
                """
                <TargetFrameworks Condition="'$(CustomTestTarget)' == ''">net8.0;net9.0;net10.0</TargetFrameworks>
                <TargetFrameworks Condition="'$(CustomTestTarget)' != ''">$(CustomTestTarget)</TargetFrameworks>
                <RestrictForLegacyTfm>true</RestrictForLegacyTfm>
                <IsTestProject>true</IsTestProject>
                """).ConfigureAwait(false);

            ScriptResult result = await projects.RunAsync(framework, customTestTarget).ConfigureAwait(false);

            Assert.That(GetMatrixFiles(result), Is.EquivalentTo(expectedFiles.Split(',')));
        }

        [TestCase("false")]
        [TestCase("False")]
        public async Task ExplicitFalseIsTestProjectIsExcludedButUnsetIsEligibleAsync(string isTestProject)
        {
            using TestProjects projects = await TestProjects.CreateAsync().ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Unrestored.Tests.csproj",
                "<TargetFramework>net48</TargetFramework>").ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Disabled.Tests.csproj",
                $"""
                <TargetFramework>net48</TargetFramework>
                <IsTestProject>{isTestProject}</IsTestProject>
                """).ConfigureAwait(false);

            ScriptResult result = await projects.RunAsync("net48").ConfigureAwait(false);

            Assert.That(GetMatrixFiles(result), Is.EqualTo(s_unrestoredProjectFiles));
        }

        [TestCase("net48", "Always.Tests.csproj")]
        [TestCase("net10.0", "Always.Tests.csproj,Conditional.Tests.csproj")]
        public async Task FrameworkConditionalIsTestProjectUsesTheSelectedTargetAsync(
            string framework,
            string expectedFiles)
        {
            using TestProjects projects = await TestProjects.CreateAsync().ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Always.Tests.csproj",
                "<TargetFrameworks>net48;net10.0</TargetFrameworks>").ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Conditional.Tests.csproj",
                """
                <TargetFrameworks>net48;net10.0</TargetFrameworks>
                <IsTestProject>false</IsTestProject>
                <IsTestProject Condition="'$(TargetFramework)' == 'net10.0'">true</IsTestProject>
                """).ConfigureAwait(false);

            ScriptResult result = await projects.RunAsync(framework).ConfigureAwait(false);

            Assert.That(GetMatrixFiles(result), Is.EquivalentTo(expectedFiles.Split(',')));
        }

        [TestCase(null, false)]
        [TestCase(null, true)]
        [TestCase("net48", false)]
        [TestCase("net48", true)]
        public async Task EmptyMatrixFailsUnlessExplicitlyAllowedAsync(string? framework, bool allowEmpty)
        {
            using TestProjects projects = await TestProjects.CreateAsync().ConfigureAwait(false);
            if (framework is not null)
            {
                await projects.WriteProjectAsync(
                    "Modern.Tests.csproj",
                    "<TargetFramework>net10.0</TargetFramework>").ConfigureAwait(false);
            }

            ScriptResult result = await projects.RunAsync(framework, allowEmpty: allowEmpty).ConfigureAwait(false);

            if (allowEmpty)
            {
                Assert.That(GetMatrixFiles(result), Is.Empty);
                Assert.That(result.Output, Does.Not.Contain("##vso[task.logissue type=error]"));
            }
            else
            {
                AssertFailureWithoutMatrix(result, "The job matrix is empty");
                if (framework is not null)
                {
                    Assert.That(result.Output, Does.Contain("no eligible test project targets 'net48'"));
                }
            }
        }

        [Test]
        public async Task MissingExplicitProjectFailsEvenWhenEmptyIsAllowedAsync()
        {
            using TestProjects projects = await TestProjects.CreateAsync().ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Present.Tests.csproj",
                "<TargetFramework>net48</TargetFramework>").ConfigureAwait(false);

            ScriptResult result = await projects.RunAsync(
                "net48",
                allowEmpty: true,
                extraArguments: "-Files 'Present.Tests.csproj,Missing.Tests.csproj'").ConfigureAwait(false);

            AssertFailureWithoutMatrix(result, "Test project file not found:");
            Assert.That(result.Output, Does.Contain("Missing.Tests.csproj"));
        }

        [TestCase("<Project>")]
        [TestCase("""<Project><Import Project="Missing.props" /></Project>""")]
        public async Task FailedMsBuildEvaluationFailsEvenWhenEmptyIsAllowedAsync(string projectContents)
        {
            using TestProjects projects = await TestProjects.CreateAsync().ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(projects.RootPath, "Broken.Tests.csproj"),
                projectContents).ConfigureAwait(false);

            ScriptResult result = await projects.RunAsync("net48", allowEmpty: true).ConfigureAwait(false);

            AssertFailureWithoutMatrix(result, "MSBuild evaluation failed");
            Assert.That(result.Output, Does.Contain("Broken.Tests.csproj"));
        }

        [Test]
        public async Task MissingFrameworkPropertiesFailEvenWhenEmptyIsAllowedAsync()
        {
            using TestProjects projects = await TestProjects.CreateAsync().ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Untargeted.Tests.csproj",
                "<IsTestProject>true</IsTestProject>").ConfigureAwait(false);

            ScriptResult result = await projects.RunAsync("net48", allowEmpty: true).ConfigureAwait(false);

            AssertFailureWithoutMatrix(result, "does not declare TargetFramework or TargetFrameworks");
            Assert.That(result.Output, Does.Contain("Untargeted.Tests.csproj"));
        }

        [TestCase("not JSON")]
        [TestCase("null")]
        [TestCase("""{"Properties":{"TargetFramework":"net48"}}""")]
        [TestCase("""{"Properties":{"TargetFramework":"net48","TargetFrameworks":"","IsTestProject":false}}""")]
        [TestCase("""{"Properties":{"TargetFramework":"net48","TargetFrameworks":"","IsTestProject":"invalid"}}""")]
        public async Task InvalidMsBuildPropertyOutputFailsWithoutPublishingMatrixAsync(string msBuildOutput)
        {
            using TestProjects projects = await TestProjects.CreateAsync().ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Subject.Tests.csproj",
                "<TargetFramework>net48</TargetFramework>").ConfigureAwait(false);

            ScriptResult result = await projects.RunAsync(
                "net48",
                allowEmpty: true,
                msBuildOutput: msBuildOutput).ConfigureAwait(false);

            AssertFailureWithoutMatrix(result, "MSBuild property evaluation");
            Assert.That(result.Output, Does.Contain("Subject.Tests.csproj"));
        }

        [Test]
        public async Task DiscoveryHonorsFilenameAndExclusionsBeforeEvaluationAsync()
        {
            using TestProjects projects = await TestProjects.CreateAsync().ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Included.Tests.csproj",
                "<TargetFramework>net48</TargetFramework>").ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(projects.RootPath, "Excluded.Tests.csproj"),
                "<Project>").ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(projects.RootPath, "NotATest.csproj"),
                "<Project>").ConfigureAwait(false);

            ScriptResult result = await projects.RunAsync(
                "net48",
                extraArguments: "-ExcludeFileName 'Other*.csproj,Excluded*.csproj'").ConfigureAwait(false);

            Assert.That(GetMatrixFiles(result), Is.EqualTo(s_includedProjectFiles));
        }

        [Test]
        public async Task BuildMatrixTfmsKeepUnfilteredAgentAndConfigurationFanoutAsync()
        {
            using TestProjects projects = await TestProjects.CreateAsync().ConfigureAwait(false);
            await projects.WriteProjectAsync(
                "Pinned.Tests.csproj",
                """
                <TargetFramework>net10.0</TargetFramework>
                <IsTestProject>false</IsTestProject>
                """).ConfigureAwait(false);

            ScriptResult result = await projects.RunAsync(
                null,
                extraArguments:
                    "-Files 'Pinned.Tests.csproj' -Tfms 'net48,net10.0' -Configurations 'Debug,Release' " +
                    "-AgentTable @{ windows = 'windows-2025-vs2026'; linux = 'ubuntu-24.04' }",
                msBuildOutput: "MSBuild must not run for build-matrix discovery.").ConfigureAwait(false);

            using JsonDocument matrix = ReadMatrix(result);
            string[] combinations = [.. matrix.RootElement.EnumerateObject().Select(static entry =>
            {
                JsonElement value = entry.Value;
                return $"{value.GetProperty("agent").GetString()}|" +
                    $"{value.GetProperty("configuration").GetString()}|" +
                    $"{value.GetProperty("targetTfm").GetString()}|" +
                    $"{value.GetProperty("poolImage").GetString()}";
            })];

            Assert.Multiple(() =>
            {
                Assert.That(combinations, Is.EquivalentTo(s_buildMatrixCombinations));
                Assert.That(GetMatrixFiles(result), Is.All.EqualTo("Pinned.Tests.csproj"));
            });
        }

        [Test]
        public async Task CustomTestTargetRequiresTestFrameworkAsync()
        {
            using TestProjects projects = await TestProjects.CreateAsync().ConfigureAwait(false);

            ScriptResult result = await projects.RunAsync(
                null,
                customTestTarget: "net48",
                allowEmpty: true).ConfigureAwait(false);

            AssertFailureWithoutMatrix(result, "CustomTestTarget requires TestFramework");
        }

        [TestCase("")]
        [TestCase(" ")]
        public async Task EmptyTestFrameworkIsRejectedAsync(string framework)
        {
            using TestProjects projects = await TestProjects.CreateAsync().ConfigureAwait(false);

            ScriptResult result = await projects.RunAsync(framework, allowEmpty: true).ConfigureAwait(false);

            AssertFailureWithoutMatrix(result, "TestFramework must name a test runtime framework.");
        }

        [Test]
        public async Task TestPreparationPassesEligibilityParametersAfterInstallingSdkAsync()
        {
            string template = await ReadTestTemplateAsync().ConfigureAwait(false);
            int preparationStart = template.IndexOf("- job: testprep", StringComparison.Ordinal);
            int testStart = template.IndexOf("- job: testall", StringComparison.Ordinal);
            Assert.That(preparationStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(testStart, Is.GreaterThan(preparationStart));
            string preparation = template[preparationStart..testStart];
            int sdkInstallation = preparation.IndexOf("task: UseDotNet@2", StringComparison.Ordinal);
            int matrixStep = preparation.IndexOf("task: PowerShell@2", StringComparison.Ordinal);

            Assert.Multiple(() =>
            {
                Assert.That(sdkInstallation, Is.GreaterThanOrEqualTo(0));
                Assert.That(matrixStep, Is.GreaterThan(sdkInstallation));
                Assert.That(preparation, Does.Contain("version: '10.0.x'"));
                Assert.That(preparation, Does.Contain("-TestFramework \"${{ parameters.framework }}\""));
                Assert.That(preparation, Does.Contain("-CustomTestTarget \"${{ parameters.customtestarget }}\""));
            });
        }

        [Test]
        public async Task LinuxLensUsesAnExclusiveXvfbRunnerWithSharedArgumentsAsync()
        {
            string template = await ReadTestTemplateAsync().ConfigureAwait(false);
            string normalStep = GetTemplateStep(template, "testWithoutXvfb");
            string xvfbStep = GetTemplateStep(template, "testWithXvfb");

            Assert.Multiple(() =>
            {
                Assert.That(normalStep, Does.StartWith("  - task: DotNetCoreCLI@2\n"));
                Assert.That(xvfbStep, Does.StartWith("  - task: PowerShell@2\n"));
                Assert.That(
                    GetTemplateBlock(normalStep, "    condition: >-", "      ", " "),
                    Is.EqualTo(
                        "and(succeeded(), not(and(eq(variables['Agent.OS'], 'Linux'), " +
                        $"eq(variables['file'], '{LensProjectFile}'))))"));
                Assert.That(
                    GetTemplateBlock(xvfbStep, "    condition: >-", "      ", " "),
                    Is.EqualTo(
                        "and(succeeded(), eq(variables['Agent.OS'], 'Linux'), " +
                        $"eq(variables['file'], '{LensProjectFile}'))"));
                Assert.That(normalStep, Does.Contain("arguments: '${{ variables.TestArguments }}'"));
                Assert.That(normalStep, Does.Contain("projects: $(file)"));
                Assert.That(normalStep, Does.Contain("publishTestResults: false"));
                Assert.That(xvfbStep, Does.Contain("pwsh: true"));
                Assert.That(
                    xvfbStep,
                    Does.Contain("xvfb-run -a dotnet test \"$(file)\" ${{ variables.TestArguments }}"));
                Assert.That(xvfbStep, Does.Not.Contain("apt-get"));
                Assert.That(xvfbStep, Does.Not.Contain("apt install"));
                Assert.That(xvfbStep, Does.Not.Contain("$env:DISPLAY"));
                Assert.That(normalStep, Does.Contain("timeoutInMinutes: 90"));
                Assert.That(xvfbStep, Does.Contain("timeoutInMinutes: 90"));
                Assert.That(normalStep, Does.Contain("continueOnError: true"));
                Assert.That(xvfbStep, Does.Contain("continueOnError: true"));
                Assert.That(
                    template.IndexOf("  - task: PublishTestResults@2", StringComparison.Ordinal),
                    Is.GreaterThan(template.IndexOf("    name: testWithXvfb", StringComparison.Ordinal)));
            });
        }

        [TestCase(0, false, true)]
        [TestCase(23, true, false)]
        public async Task LinuxLensWrapperPassesSharedArgumentsAndPropagatesExitCodeAsync(
            int exitCode,
            bool useCustomTarget,
            bool includeOptionalArguments)
        {
            using TestProjects projects = await TestProjects.CreateAsync().ConfigureAwait(false);
            string template = await ReadTestTemplateAsync().ConfigureAwait(false);
            string resultsPath = Path.Combine(projects.RootPath, "results with spaces");
            string script = ResolveXvfbScript(template, resultsPath, useCustomTarget, includeOptionalArguments);
            string command = $$"""
                function Get-Command
                {
                    param([string] $Name, [string] $CommandType, [string] $ErrorAction)
                    if ($Name -ne 'xvfb-run' -or $CommandType -ne 'Application')
                    {
                        throw 'Unexpected command discovery.'
                    }
                    'xvfb-run'
                }
                function xvfb-run
                {
                    ConvertTo-Json -InputObject @($args) -Compress
                    $global:LASTEXITCODE = {{exitCode.ToString(CultureInfo.InvariantCulture)}}
                }
                function dotnet
                {
                    throw 'Tests must run through xvfb-run.'
                }
                {{script}}
                """;

            ScriptResult result = await RunPowerShellAsync(projects.RootPath, command).ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(exitCode), result.Output);
            var expectedArguments = new List<string> { "-a", "dotnet", "test", LensProjectFile, "--no-restore" };
            if (useCustomTarget)
            {
                expectedArguments.Add("/p:CustomTestTarget=net10.0");
            }
            else
            {
                expectedArguments.AddRange(["--framework", "net10.0"]);
            }
            expectedArguments.AddRange(["--configuration", "Release"]);
            if (includeOptionalArguments)
            {
                expectedArguments.AddRange(
                [
                    "/p:CollectCoverage=true",
                    "--collect",
                    "XPlat Code Coverage",
                    "--settings",
                    "./tests/coverlet.runsettings.xml",
                    "--filter",
                    "TestCategory!=LongRunning&TestCategory!=Stress"
                ]);
            }
            expectedArguments.AddRange(
            [
                "--blame-hang-timeout",
                "10m",
                "--blame-hang-dump-type",
                "mini",
                "--blame-crash",
                "--blame-crash-dump-type",
                "mini",
                "--logger",
                "trx",
                "--results-directory",
                resultsPath
            ]);
            using JsonDocument invocation = JsonDocument.Parse(result.Output);
            Assert.That(
                invocation.RootElement.EnumerateArray().Select(static argument => argument.GetString()),
                Is.EqualTo(expectedArguments));
        }

        [Test]
        public async Task MissingXvfbFailsWithoutStartingTestsAsync()
        {
            using TestProjects projects = await TestProjects.CreateAsync().ConfigureAwait(false);
            string template = await ReadTestTemplateAsync().ConfigureAwait(false);
            string script = ResolveXvfbScript(template, projects.RootPath, false, true);
            string command = $$"""
                function Get-Command
                {
                    param([string] $Name, [string] $CommandType, [string] $ErrorAction)
                }
                function xvfb-run
                {
                    throw 'Unexpected xvfb-run invocation.'
                }
                function dotnet
                {
                    throw 'Unexpected dotnet invocation.'
                }
                {{script}}
                """;

            ScriptResult result = await RunPowerShellAsync(projects.RootPath, command).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
                Assert.That(
                    result.Output,
                    Does.Contain("UaLens desktop tests require xvfb-run on the Linux agent."));
                Assert.That(result.Output, Does.Not.Contain("Unexpected xvfb-run invocation."));
                Assert.That(result.Output, Does.Not.Contain("Unexpected dotnet invocation."));
            });
        }

        private static async Task<string> ReadTestTemplateAsync()
        {
            string template = await File.ReadAllTextAsync(
                Path.Combine(FindRepositoryRoot(), ".azurepipelines", "test.yml")).ConfigureAwait(false);
            return template.ReplaceLineEndings("\n");
        }

        private static string GetTemplateStep(string template, string name)
        {
            int nameStart = template.IndexOf($"    name: {name}\n", StringComparison.Ordinal);
            Assert.That(nameStart, Is.GreaterThanOrEqualTo(0), name);
            int start = template.LastIndexOf("  - task:", nameStart, StringComparison.Ordinal);
            int end = template.IndexOf("  - task:", nameStart, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), name);
            Assert.That(end, Is.GreaterThan(start), name);
            return template[start..end];
        }

        private static string GetTemplateBlock(string template, string header, string indent, string separator)
        {
            string[] lines = template.Split('\n');
            int start = Array.IndexOf(lines, header);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), header);
            string[] block = [.. lines.Skip(start + 1)
                .TakeWhile(line => line.StartsWith(indent, StringComparison.Ordinal))
                .Select(line => line[indent.Length..])];
            Assert.That(block, Is.Not.Empty, header);
            return string.Join(separator, block);
        }

        private static string ResolveXvfbScript(
            string template,
            string resultsPath,
            bool useCustomTarget,
            bool includeOptionalArguments)
        {
            string arguments = GetTemplateBlock(template, "    TestArguments: >-", "      ", " ")
                .Replace(
                    "${{ variables.DotCliCommandline }}",
                    useCustomTarget ? "/p:CustomTestTarget=net10.0" : "--framework net10.0",
                    StringComparison.Ordinal)
                .Replace("${{ parameters.configuration }}", "Release", StringComparison.Ordinal)
                .Replace(
                    "${{ variables.CoverageArgs }}",
                    includeOptionalArguments ?
                        "/p:CollectCoverage=true --collect \"XPlat Code Coverage\" " +
                        "--settings ./tests/coverlet.runsettings.xml" :
                        string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    "${{ variables.TestFilterArgs }}",
                    includeOptionalArguments ?
                        "--filter \"TestCategory!=LongRunning&TestCategory!=Stress\"" :
                        string.Empty,
                    StringComparison.Ordinal)
                .Replace("${{ parameters.hangtimeout }}", "10m", StringComparison.Ordinal);
            string step = GetTemplateStep(template, "testWithXvfb");
            string script = GetTemplateBlock(step, "      script: |", "        ", Environment.NewLine)
                .Replace("${{ variables.TestArguments }}", arguments, StringComparison.Ordinal)
                .Replace("$(file)", LensProjectFile, StringComparison.Ordinal)
                .Replace("$(Agent.TempDirectory)", resultsPath, StringComparison.Ordinal);
            Assert.That(script, Does.Not.Contain("${{"));
            return script;
        }

        private static async Task<ScriptResult> RunPowerShellAsync(
            string workingDirectory,
            string command,
            string? inheritedCustomTestTarget = null)
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
            foreach (string key in process.StartInfo.Environment.Keys.Where(static key =>
                string.Equals(key, "CustomTestTarget", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                process.StartInfo.Environment.Remove(key);
            }
            if (inheritedCustomTestTarget is not null)
            {
                process.StartInfo.Environment["CustomTestTarget"] = inheritedCustomTestTarget;
            }

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

        private static void AssertFailureWithoutMatrix(ScriptResult result, string message)
        {
            // PowerShell decorates wrapped errors with ANSI styling and continuation pipes.
            string diagnostic = string.Join(
                " ",
                AnsiControlSequences().Replace(result.Output, string.Empty)
                    .Split('\n')
                    .Select(static line => line.Trim().Trim('|').Trim())
                    .Where(static line => line.Length > 0));
            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(diagnostic, Does.Contain(message), result.Output);
                Assert.That(result.Output, Does.Not.Contain(MatrixVariablePrefix));
            });
        }

        [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")]
        private static partial Regex AnsiControlSequences();

        private static string[] GetMatrixFiles(ScriptResult result)
        {
            using JsonDocument matrix = ReadMatrix(result);
            return [.. matrix.RootElement.EnumerateObject().Select(static entry =>
                entry.Value.GetProperty("file").GetString() ??
                    throw new InvalidOperationException("A matrix entry has no file."))];
        }

        private static JsonDocument ReadMatrix(ScriptResult result)
        {
            Assert.That(result.ExitCode, Is.Zero, result.Output);
            string[] matrixLines = [.. result.Output.Split('\n').Where(static line =>
                line.StartsWith(MatrixVariablePrefix, StringComparison.Ordinal))];
            Assert.That(matrixLines, Has.Length.EqualTo(1), result.Output);
            return JsonDocument.Parse(matrixLines[0][MatrixVariablePrefix.Length..]);
        }

        private static string FindRepositoryRoot()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, ".azurepipelines", "get-matrix.ps1")))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName;
            }

            throw new InvalidOperationException("Could not find the repository root.");
        }

        private static string QuotePowerShell(string value)
        {
            return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        }

        private const string MatrixVariablePrefix = "##vso[task.setVariable variable=jobMatrix;isOutput=true]";
        private const string LensProjectFile = "tests/Opc.Ua.Lens.Tests/Opc.Ua.Lens.Tests.csproj";
        private static readonly string[] s_sharedProjectFiles = ["Shared.Tests.csproj"];
        private static readonly string[] s_conditionalProjectFiles = ["Conditional.Tests.csproj"];
        private static readonly string[] s_unrestoredProjectFiles = ["Unrestored.Tests.csproj"];
        private static readonly string[] s_includedProjectFiles = ["Included.Tests.csproj"];
        private static readonly string[] s_buildMatrixCombinations =
        [
            "windows|Debug|net48|windows-2025-vs2026",
            "windows|Debug|net10.0|windows-2025-vs2026",
            "windows|Release|net48|windows-2025-vs2026",
            "windows|Release|net10.0|windows-2025-vs2026",
            "linux|Debug|net48|ubuntu-24.04",
            "linux|Debug|net10.0|ubuntu-24.04",
            "linux|Release|net48|ubuntu-24.04",
            "linux|Release|net10.0|ubuntu-24.04"
        ];

        private sealed record ScriptResult(int ExitCode, string Output);

        private sealed class TestProjects : IDisposable
        {
            private TestProjects(string rootPath)
            {
                RootPath = rootPath;
            }

            public string RootPath { get; }

            public static async Task<TestProjects> CreateAsync()
            {
                string rootPath = Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "test matrix fixtures",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(rootPath);
                string repositoryRoot = FindRepositoryRoot();
                string propertiesPath = SecurityElement.Escape(Path.Combine(repositoryRoot, "targets.props"));
                string targetsPath = SecurityElement.Escape(Path.Combine(repositoryRoot, "Directory.Build.targets"));
                await File.WriteAllTextAsync(
                    Path.Combine(rootPath, "Directory.Build.props"),
                    $"""
                    <Project>
                      <Import Project="{propertiesPath}" />
                    </Project>
                    """).ConfigureAwait(false);
                await File.WriteAllTextAsync(
                    Path.Combine(rootPath, "Directory.Build.targets"),
                    $"""
                    <Project>
                      <Import Project="{targetsPath}" />
                      <Target Name="RejectMatrixBuildOrRestore" BeforeTargets="Build;Restore">
                        <Error Text="Matrix discovery must not build or restore a project." />
                      </Target>
                    </Project>
                    """).ConfigureAwait(false);
                return new TestProjects(rootPath);
            }

            public Task WriteProjectAsync(string name, string properties)
            {
                return File.WriteAllTextAsync(
                    Path.Combine(RootPath, name),
                    $$"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        {{properties}}
                      </PropertyGroup>
                    </Project>
                    """);
            }

            public Task<ScriptResult> RunAsync(
                string? testFramework,
                string customTestTarget = "",
                bool allowEmpty = false,
                string extraArguments = "",
                string? inheritedCustomTestTarget = null,
                string? msBuildOutput = null)
            {
                var command = new StringBuilder("$ErrorActionPreference = 'Stop'; ");
                if (msBuildOutput is not null)
                {
                    command.Append("function dotnet { ")
                        .Append(QuotePowerShell(msBuildOutput))
                        .Append("; $global:LASTEXITCODE = 0 }; ");
                }
                command.Append("& ")
                    .Append(QuotePowerShell(Path.Combine(FindRepositoryRoot(), ".azurepipelines", "get-matrix.ps1")))
                    .Append(" -BuildRoot ")
                    .Append(QuotePowerShell(RootPath))
                    .Append(" -FileName '*.Tests.csproj' -CustomTestTarget ")
                    .Append(QuotePowerShell(customTestTarget));
                if (testFramework is not null)
                {
                    command.Append(" -TestFramework ").Append(QuotePowerShell(testFramework));
                }
                if (allowEmpty)
                {
                    command.Append(" -AllowEmpty");
                }
                command.Append(' ').Append(extraArguments);

                return RunPowerShellAsync(RootPath, command.ToString(), inheritedCustomTestTarget);
            }

            public void Dispose()
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
#endif
