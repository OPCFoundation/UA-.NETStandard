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
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Executes the CI action's native search-path step without loading OpenUSD.
    /// </summary>
    [TestFixture]
    public sealed class LinuxVisionNativePathTests
    {
        /// <summary>
        /// Exports both package layouts without discarding inherited paths or existing CI variables.
        /// </summary>
        [TestCase(null, "Release", "net10.0")]
        [TestCase("", "Debug", "net8.0")]
        [TestCase("/opt/native", "Release", "net9.0")]
        [TestCase("/opt/first:/opt/native with spaces", "Debug", "net10.0")]
        public async Task ActionExportsNativeDirectoriesAndPreservesInheritedPathsAsync(
            string? inheritedPath,
            string configuration,
            string framework)
        {
            string source = await File.ReadAllTextAsync(FindActionPath()).ConfigureAwait(false);
            string step = FindStep(source.ReplaceLineEndings("\n"));
            Assert.Multiple(() =>
            {
                Assert.That(step, Does.Contain("runner.os == 'Linux'"));
                Assert.That(step, Does.Contain("contains(inputs.projects, 'Opc.Ua.Vision.Tests')"));
                Assert.That(step, Does.Not.Contain("Opc.Ua.Lens"));
                Assert.That(step, Does.Contain("shell: pwsh"));
                Assert.That(step, Does.Contain("CI_NATIVE_CONFIGURATION: ${{ inputs.configuration }}"));
                Assert.That(step, Does.Contain("CI_NATIVE_FRAMEWORK: ${{ inputs.framework }}"));
            });

            string[] lines = step.Split('\n');
            int run = Array.IndexOf(lines, "    run: |");
            Assert.That(run, Is.GreaterThanOrEqualTo(0), "The action must execute its search-path script.");
            string script = string.Join("\n", lines.Skip(run + 1)
                .TakeWhile(line => line.Length == 0 || line.StartsWith("      ", StringComparison.Ordinal))
                .Select(line => line.Length == 0 ? line : line[6..]));
            string environmentFile = Path.Combine(Path.GetTempPath(), $"vision-native-env-{Guid.NewGuid():N}.txt");
            try
            {
                await File.WriteAllTextAsync(environmentFile, "EXISTING=value\n").ConfigureAwait(false);
                using var process = new Process();
                process.StartInfo.FileName = "pwsh";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                PowerShellScriptOutput.ConfigureDeterministicOutput(process.StartInfo);
                process.StartInfo.ArgumentList.Add("-NoProfile");
                process.StartInfo.ArgumentList.Add("-NonInteractive");
                process.StartInfo.ArgumentList.Add("-Command");
                process.StartInfo.ArgumentList.Add(script);
                process.StartInfo.Environment["GITHUB_ENV"] = environmentFile;
                process.StartInfo.Environment["GITHUB_WORKSPACE"] = "/work/checkout with spaces";
                process.StartInfo.Environment["CI_NATIVE_CONFIGURATION"] = configuration;
                process.StartInfo.Environment["CI_NATIVE_FRAMEWORK"] = framework;
                if (inheritedPath is null)
                {
                    process.StartInfo.Environment.Remove("LD_LIBRARY_PATH");
                }
                else
                {
                    process.StartInfo.Environment["LD_LIBRARY_PATH"] = inheritedPath;
                }

                Assert.That(process.Start(), Is.True);
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                string diagnostics = await output.ConfigureAwait(false) + await error.ConfigureAwait(false);
                Assert.That(process.ExitCode, Is.Zero, diagnostics);

                string directory = $"/work/checkout with spaces/tests/Opc.Ua.Vision.Tests/bin/{configuration}/{framework}";
                string expected = $"LD_LIBRARY_PATH={directory}:{directory}/runtimes/linux-x64/native";
                if (!string.IsNullOrEmpty(inheritedPath))
                {
                    expected += ":" + inheritedPath;
                }
                string[] actual = await File.ReadAllLinesAsync(environmentFile).ConfigureAwait(false);
                Assert.That(actual, Is.EqualTo(new[] { "EXISTING=value", expected }));
            }
            finally
            {
                File.Delete(environmentFile);
            }
        }

        private static string FindStep(string source)
        {
            const string marker = "  - name: Configure Linux Vision native library search path\n";
            int start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0),
                "Linux Vision hosts need the packaged transitive libraries on their loader search path.");
            int end = source.IndexOf("\n  - name:", start + marker.Length, StringComparison.Ordinal);
            return end < 0 ? source[start..] : source[start..end];
        }

        private static string FindActionPath()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (current is not null)
            {
                string path = Path.Combine(current, ".github", "actions", "run-dotnet-tests", "action.yml");
                if (File.Exists(path))
                {
                    return path;
                }
                current = Directory.GetParent(current)?.FullName;
            }
            throw new InvalidOperationException("Could not find the repository's run-dotnet-tests action.");
        }
    }
}
#endif
