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
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Guards the Debug package-set invariant that .github/workflows/nuget-publish.yml
    /// depends on: the Release and Debug pack legs of the same commit are aggregated
    /// into a single directory, so every project that packs in both configurations
    /// must produce a distinct package ID. A project satisfies that either by
    /// renaming itself to "&lt;id&gt;.Debug" or by excluding itself from the Debug leg.
    /// Without this, a project silently ships no Debug counterpart. See
    /// docs/ReleaseProcess.md.
    /// </summary>
    [TestFixture]
    public sealed partial class DebugPackageIdConventionTests
    {
        [Test]
        public void EveryPackableProjectRenamesOrExcludesItselfInDebug()
        {
            string repositoryRoot = FindRepositoryRoot();
            List<string> projects = GetPackSolutionProjects(repositoryRoot);
            Assert.That(
                projects,
                Is.Not.Empty,
                "The pack solution must contain projects.");

            var violations = new List<string>();
            int renamed = 0;
            int excluded = 0;

            foreach (string project in projects)
            {
                string text = ReadProjectWithLocalImports(project);
                if (!OptsIntoPacking(text))
                {
                    continue;
                }

                if (HasDebugConditionedElement(text, "PackageId"))
                {
                    renamed++;
                }
                else if (HasDebugConditionedElement(text, "IsPackable"))
                {
                    excluded++;
                }
                else
                {
                    violations.Add(
                        Path.GetRelativePath(repositoryRoot, project).Replace('\\', '/'));
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                "Each of these packable projects would pack under its Release package ID in " +
                "the Debug leg of nuget-publish.yml, colliding with the Release leg during " +
                "aggregation. Add a Debug PackageId override or exclude the project from " +
                "the Debug pack:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal)));

            Assert.Multiple(() => {
                Assert.That(
                    renamed,
                    Is.GreaterThan(0),
                    "The check found no renamed project, so it is not actually observing " +
                    "the repository's packable projects.");
                Assert.That(
                    excluded,
                    Is.GreaterThan(0),
                    "The check found no opted-out project, so the opt-out branch is unproven.");
            });

            TestContext.Out.WriteLine(
                $"{renamed} project(s) rename to .Debug, {excluded} opt out of the Debug pack.");
        }

        /// <summary>
        /// Mirrors .azurepipelines/generate-slnx.ps1: the pack solution is UA.slnx
        /// with the test, fuzzing and solution-item folders removed.
        /// </summary>
        private static List<string> GetPackSolutionProjects(string repositoryRoot)
        {
            string solutionPath = Path.Combine(repositoryRoot, "UA.slnx");
            Assert.That(File.Exists(solutionPath), Is.True, "UA.slnx must exist.");

            string[] excludedPrefixes = ["/tests/", "/fuzzing/", "/Solution Items/"];
            XDocument solution = XDocument.Load(solutionPath);
            var projects = new List<string>();

            foreach (XElement folder in solution.Root?.Elements("Folder") ?? [])
            {
                string name = folder.Attribute("Name")?.Value ?? string.Empty;
                if (excludedPrefixes.Any(prefix =>
                    name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                foreach (XElement project in folder.Elements("Project"))
                {
                    string? path = project.Attribute("Path")?.Value;
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    string full = Path.GetFullPath(Path.Combine(
                        repositoryRoot,
                        path.Replace('/', Path.DirectorySeparatorChar)));
                    if (File.Exists(full))
                    {
                        projects.Add(full);
                    }
                }
            }

            return projects;
        }

        /// <summary>
        /// Returns the project text concatenated with every repository-local file it
        /// imports, transitively. Several projects - the source-generator pack
        /// projects, for example - declare both their packability and their Debug
        /// rename in a shared .targets file, so reading the .csproj alone classifies
        /// them incorrectly.
        /// </summary>
        private static string ReadProjectWithLocalImports(string projectPath)
        {
            var builder = new StringBuilder();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<string>();
            pending.Enqueue(projectPath);

            while (pending.Count > 0)
            {
                string current = pending.Dequeue();
                if (!visited.Add(current) || !File.Exists(current))
                {
                    continue;
                }

                string text = File.ReadAllText(current);
                builder.AppendLine(text);

                string? directory = Path.GetDirectoryName(current);
                if (directory is null)
                {
                    continue;
                }

                foreach (Match import in ImportProjectRegex().Matches(text))
                {
                    // Only repository-local, statically resolvable imports are
                    // followed. An import whose path is computed from a property
                    // other than MSBuildThisFileDirectory cannot be resolved here.
                    string relative = import.Groups["path"].Value.Replace(
                        "$(MSBuildThisFileDirectory)",
                        string.Empty,
                        StringComparison.Ordinal);
                    if (relative.Contains("$(", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    pending.Enqueue(Path.GetFullPath(Path.Combine(
                        directory,
                        relative.Replace('\\', Path.DirectorySeparatorChar))));
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// common.props sets IsPackable=false for the whole repository, so a project
        /// produces a package only when it - or one of its imports - opts back in.
        /// </summary>
        private static bool OptsIntoPacking(string projectText)
        {
            string outsideDebugGroups = DebugPropertyGroupRegex()
                .Replace(projectText, string.Empty);

            return IsPackableElementRegex()
                .Matches(outsideDebugGroups)
                .Any(match => match.Groups["value"].Value.Trim()
                    .Equals("true", StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasDebugConditionedElement(string projectText, string elementName)
        {
            return DebugPropertyGroupRegex()
                .Matches(projectText)
                .Any(group => group.Groups["body"].Value.Contains(
                    $"<{elementName}>",
                    StringComparison.Ordinal));
        }

        [GeneratedRegex(
            @"<PropertyGroup\s+Condition\s*=\s*""'\$\(Configuration\)'\s*==\s*'Debug'""\s*>(?<body>.*?)</PropertyGroup>",
            RegexOptions.Singleline)]
        private static partial Regex DebugPropertyGroupRegex();

        [GeneratedRegex(@"<IsPackable>(?<value>[^<]*)</IsPackable>")]
        private static partial Regex IsPackableElementRegex();

        [GeneratedRegex(@"<Import\s+[^>]*Project\s*=\s*""(?<path>[^""]+)""")]
        private static partial Regex ImportProjectRegex();

        private static string FindRepositoryRoot()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, "version.targets")))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName;
            }

            throw new InvalidOperationException("Could not find the repository root.");
        }
    }
}
#endif
