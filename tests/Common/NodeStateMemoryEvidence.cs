/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Tests
{
    /// <summary>
    /// Managed-test-only evidence writer shared by node and server population fixtures.
    /// </summary>
    internal sealed class NodeStateMemoryEvidence : IDisposable
    {
        internal NodeStateMemoryEvidence(
            string output,
            string sourceRoot,
            string? sdk = null,
            string? command = null,
            string? revision = null)
        {
            sourceRoot = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar);
            output = Path.GetFullPath(output);
            if (output.Equals(sourceRoot, StringComparison.OrdinalIgnoreCase) ||
                output.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Evidence must be outside the source tree.", nameof(output));
            }
            if (!File.Exists(Path.Combine(sourceRoot, "UA.slnx")))
            {
                throw new ArgumentException("Expected the source worktree used for this build.", nameof(sourceRoot));
            }
            Directory.CreateDirectory(output);
            m_samples = Create(output, "samples.csv");
            m_occupancy = Create(output, "occupancy.csv");
            m_samples.WriteLine("Metric,Scenario,Sample,Count,Total,PerUnit");
            m_occupancy.WriteLine(
                "Phase,NodeId,ConcreteType,Groups,CallbackSlots,Description,Metadata,Security," +
                "RoleCount,UserRoleCount,Children,DynamicChildren,ExplicitReferences,BrowseReferences," +
                "SynthesizedReferences,Notifiers");
            using StreamWriter definitions = Create(output, "definitions.csv");
            definitions.WriteLine("Key,Value");
            Pair(definitions, "Names",
                "Cached: one global 16-character string. Unique: one X16 string per node.");
            Pair(definitions, "NameSharing",
                "NodeId when string, BrowseName, DisplayName, Description share that string.");
            Pair(definitions, "Metadata",
                "Both name modes share the same XML/list/URI payloads; bag cost is included.");
            Pair(definitions, "Security",
                "Cached: global permission entry/array. Unique: one entry/array per node.");
            Pair(definitions, "Value",
                "Double 42 except Payload cases: cached or newly allocated zero-filled 64 bytes.");
            Pair(definitions, "ReferenceModes",
                "0: cached NodeId converted; 1: new absolute wrapper; 2: preexpanded.");
            Pair(definitions, "Allocation",
                "Warm synchronous work on the calling thread only; includes discarded objects.");
            Pair(definitions, "Live",
                "Full-GC deltas; roots preallocated; shared services/payloads excluded; estimates only.");
            Pair(definitions, "Index",
                "Separate index component holds preexisting nodes; do not add to whole-manager.");
            Pair(definitions, "Occupancy",
                "Per-node rows allow group co-occurrence; no assumed deployment percentages.");
            Pair(definitions, "Browse",
                "Diagnostic browsing touches lazy state after memory measurement, never before.");
            Pair(definitions, "WholeManager",
                "Mock-backed test-manager graph includes context/mocks/index, not a server.");
            Pair(definitions, "Telemetry",
                "Field-free variable subclass invokes protected Initialize(ITelemetryContext); distinct from Create.");
            using StreamWriter identity = Create(output, "identity.csv");
            identity.WriteLine("Key,Value");
            Pair(identity, "Schema", "NodeStateMemory-v1");
            Pair(identity, "Utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            Pair(identity, "Command", Environment.CommandLine);
            Pair(identity, "Runtime", RuntimeInformation.FrameworkDescription);
            Pair(identity, "Architecture", RuntimeInformation.ProcessArchitecture.ToString());
            Pair(identity, "OS", RuntimeInformation.OSDescription);
            Pair(identity, "ServerGC", GCSettings.IsServerGC.ToString());
            Pair(identity, "LatencyMode", GCSettings.LatencyMode.ToString());
            Pair(identity, "TieredCompilation",
                Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? string.Empty);
            Pair(identity, "SourceRoot", Path.GetFullPath(sourceRoot));
            Pair(identity, "SDK", sdk ??
                Environment.GetEnvironmentVariable("NODESTATE_MEMORY_SDK")
                    ?? throw new InvalidOperationException("Set NODESTATE_MEMORY_SDK to dotnet --version."));
            Pair(identity, "BuildCommand", command ??
                Environment.GetEnvironmentVariable("NODESTATE_MEMORY_COMMAND")
                    ?? throw new InvalidOperationException("Set NODESTATE_MEMORY_COMMAND to the fresh-build command."));
            Pair(identity, "SourceRevision", revision ??
                Environment.GetEnvironmentVariable("NODESTATE_MEMORY_REVISION")
                    ?? throw new InvalidOperationException("Set NODESTATE_MEMORY_REVISION to git rev-parse HEAD."));
            using StreamWriter assemblies = Create(output, "assemblies.csv");
            assemblies.WriteLine("Name,Path,SHA256,MVID,TFM");
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .OrderBy(a => a.FullName, StringComparer.Ordinal))
            {
                assemblies.WriteLine(AssemblyRow(assembly));
            }
            using StreamWriter sources = Create(output, "sources.csv");
            sources.WriteLine("Path,SHA256");
            foreach (string path in SourceFiles(sourceRoot).OrderBy(p => p, StringComparer.Ordinal))
            {
                string relative = path[(sourceRoot.TrimEnd(Path.DirectorySeparatorChar).Length + 1)..];
                sources.WriteLine($"{Csv(relative)},{HashFile(path)}");
            }
        }

        public void Dispose()
        {
            m_samples.Dispose();
            m_occupancy.Dispose();
        }

        internal void Sample(string metric, string scenario, int sample, int count, long total)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            m_samples.WriteLine(FormattableString.Invariant(
                $"{Csv(metric)},{Csv(scenario)},{sample},{count},{total},{(double)total / count:F4}"));
            m_samples.Flush();
        }

        internal void Occupancy(string phase, NodeState node, ISystemContext context)
        {
            m_occupancy.WriteLine(OccupancyRow(phase, node, context));
            m_occupancy.Flush();
        }

        internal static string OccupancyRow(string phase, NodeState node, ISystemContext context)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            var references = new List<IReference>();
            node.GetReferences(context, references);
            var notifiers = new List<NodeState.Notifier>();
            node.GetNotifiers(context, notifiers);
            int browseCount = 0;
            int synthesized = 0;
            using (INodeBrowser browser = node.CreateBrowser(
                context, null, default, true, BrowseDirection.Both, default, default, false))
            {
                IReference? reference;
                while ((reference = browser.Next()) is not null)
                {
                    browseCount++;
                    if (!references.Exists(r => r.ReferenceTypeId == reference.ReferenceTypeId &&
                        r.IsInverse == reference.IsInverse &&
                        r.TargetId == reference.TargetId))
                    {
                        synthesized++;
                    }
                }
            }
            var dynamicChildren = (List<BaseInstanceState>?)s_children.GetValue(node);
            (string groups, int slots) = CallbackGroups(node);
            bool metadata = node.Extensions is not null ||
                node.Categories is not null ||
                node.ReleaseStatus != Export.ReleaseStatus.Released ||
                node.Specification is not null ||
                node.NodeSetDocumentation is not null ||
                node.DesignToolOnly;
            bool security = !node.RolePermissions.IsNull ||
                !node.UserRolePermissions.IsNull ||
                node.AccessRestrictions.HasValue;
            return
                $"{Csv(phase)},{Csv(node.NodeId.ToString())},{Csv(node.GetType().FullName ?? string.Empty)}," +
                $"{Csv(groups)},{slots},{!node.Description.IsNull},{metadata},{security}," +
                $"{(node.RolePermissions.IsNull ? -1 : node.RolePermissions.Count)}," +
                $"{(node.UserRolePermissions.IsNull ? -1 : node.UserRolePermissions.Count)}," +
                $"{children.Count},{dynamicChildren?.Count ?? 0},{references.Count},{browseCount}," +
                $"{synthesized},{notifiers.Count}";
        }

        internal static (string Groups, int Slots) CallbackGroups(NodeState node)
        {
            var groups = new SortedSet<string>(StringComparer.Ordinal);
            int slots = 0;
            for (Type? type = node.GetType(); type is not null; type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!typeof(Delegate).IsAssignableFrom(field.FieldType) || field.GetValue(node) is not Delegate)
                    {
                        continue;
                    }
                    slots++;
                    string group = field.Name switch
                    {
                        "StateChanged" or "StateChangedAsync" => "EventSubscription",
                        "OnSimpleReadValue" or "OnSimpleWriteValue" or "OnReadValue" or "OnWriteValue" or
                        "OnReadValueAsync" or "OnWriteValueAsync" or "OnSimpleReadValueAsync" or
                        "OnSimpleWriteValueAsync" => "Value",
                        _ when type == typeof(NodeState) =>
                            field.Name.StartsWith("OnRead", StringComparison.Ordinal) ||
                            field.Name.StartsWith("OnWrite", StringComparison.Ordinal) ? "BaseAttribute" : "Behavior",
                        _ when type == typeof(BaseVariableState) => "VariableAttribute",
                        _ when type == typeof(MethodState) => "Method",
                        _ => "Derived"
                    };
                    groups.Add(group);
                }
            }
            return (string.Join("+", groups), slots);
        }

        internal static string AssemblyRow(Assembly assembly)
        {
            return $"{Csv(assembly.GetName().Name ?? string.Empty)},{Csv(assembly.Location)}," +
                $"{HashFile(assembly.Location)}," +
                $"{assembly.ManifestModule.ModuleVersionId:D}," +
                $"{Csv(assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName ?? string.Empty)}";
        }

        internal static string HashFile(string path)
        {
            using FileStream stream = File.OpenRead(path);
            using var algorithm = SHA256.Create();
#if NET5_0_OR_GREATER
            return Convert.ToHexString(algorithm.ComputeHash(stream));
#else
            return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
#endif
        }

        internal static string Csv(string value)
        {
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        internal static long FullHeap()
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            return GC.GetTotalMemory(forceFullCollection: true);
        }

        internal static IEnumerable<string> SourceFiles(string root)
        {
            foreach (string file in Directory.EnumerateFiles(root))
            {
                string extension = Path.GetExtension(file);
                if (extension is ".cs" or ".csproj" or ".props" or ".targets" or ".json" or ".slnx" or
                    ".xml" or ".csv" or ".resx" or ".config" ||
                    Path.GetFileName(file) == ".editorconfig")
                {
                    yield return file;
                }
            }
            foreach (string directory in Directory.EnumerateDirectories(root))
            {
                string name = Path.GetFileName(directory);
                if (name is "bin" or "obj" or "TestResults" or "BenchmarkDotNet.Artifacts" ||
                    name[0] == '.')
                {
                    continue;
                }
                foreach (string file in SourceFiles(directory))
                {
                    yield return file;
                }
            }
        }

        private static StreamWriter Create(string directory, string name)
        {
            return new StreamWriter(
                new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(false));
        }

        private static void Pair(StreamWriter writer, string key, string value)
        {
            writer.WriteLine($"{Csv(key)},{Csv(value)}");
        }

        private readonly StreamWriter m_samples;
        private readonly StreamWriter m_occupancy;

        private static readonly FieldInfo s_children = typeof(NodeState).GetField(
            "m_children", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Update the diagnostic dynamic-child probe for this node layout.");
    }

    /// <summary>
    /// Checks the shared CSV/source writer in each consuming test assembly and target framework.
    /// </summary>
    [TestFixture]
    [Category("NodeStateOccupancy")]
    public sealed class NodeStateMemoryEvidenceTests
    {
        /// <summary>
        /// Output pins actual input bytes and sample units, and cannot overwrite a baseline.
        /// </summary>
        [Test]
        public void EvidencePreservesSourceIdentityAndSampleUnits()
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string output = root + "-evidence";
            Directory.CreateDirectory(root);
            string solution = Path.Combine(root, "UA.slnx");
            File.WriteAllText(solution, "abc", new UTF8Encoding(false));
            try
            {
                Assert.Throws<ArgumentException>(() =>
                {
                    using var invalid = new NodeStateMemoryEvidence(Path.Combine(root, "output"), root);
                });
                using (var evidence = new NodeStateMemoryEvidence(
                    output, root, "test-sdk", "test-command", "test-revision"))
                {
                    evidence.Sample("Bytes", "TwoNodes", 1, 2, 20);
                    Assert.Throws<ArgumentOutOfRangeException>(() => evidence.Sample("Bytes", "Invalid", 1, 0, 20));
                }
                string samples = File.ReadAllText(Path.Combine(output, "samples.csv"));
                Assert.That(samples, Does.Contain("\"Bytes\",\"TwoNodes\",1,2,20,10.0000"));
                string sources = File.ReadAllText(Path.Combine(output, "sources.csv"));
                Assert.That(sources, Does.Contain(
                    "\"UA.slnx\",BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"));
                string identity = File.ReadAllText(Path.Combine(output, "identity.csv"));
                Assert.That(identity, Does.Contain("\"SDK\",\"test-sdk\""));
                Assert.That(identity, Does.Contain("\"BuildCommand\",\"test-command\""));
                Assert.That(identity, Does.Contain("\"SourceRevision\",\"test-revision\""));
                Assert.Throws<IOException>(() =>
                {
                    using var duplicate = new NodeStateMemoryEvidence(output, root, "test-sdk", "command", "revision");
                });
                Assert.That(File.ReadAllText(Path.Combine(output, "samples.csv")), Is.EqualTo(samples));
            }
            finally
            {
                foreach (string name in s_evidenceNames)
                {
                    File.Delete(Path.Combine(output, name));
                }
                File.Delete(solution);
                Directory.Delete(output);
                Directory.Delete(root);
            }
        }

        private static readonly string[] s_evidenceNames =
            ["samples.csv", "occupancy.csv", "definitions.csv", "identity.csv", "assemblies.csv", "sources.csv"];
    }
}
