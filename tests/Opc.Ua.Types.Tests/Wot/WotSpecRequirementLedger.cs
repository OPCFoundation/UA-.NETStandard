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
 *
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

#nullable enable

namespace Opc.Ua.SpecTraceability
{
    /// <summary>
    /// One WoT specification requirement whose proof is an OPC UA
    /// implementation, and the stack tests that provide it.
    /// </summary>
    internal sealed record WotSpecRequirement(
        string SpecId,
        string Specification,
        string Clause,
        string Applicability,
        string StatementHash,
        string Assembly,
        IReadOnlyList<string> Tests,
        string? Gap);

    /// <summary>
    /// One record of the pinned statement-digest inventory: the identity of a
    /// specification requirement, decomposed, and the digest of its normalized
    /// statement as the specification's own ledger published it.
    /// </summary>
    internal sealed record WotSpecStatement(
        string SpecId,
        string Specification,
        string Clause,
        int Ordinal,
        string Applicability,
        IReadOnlyList<string> Keywords,
        IReadOnlyList<string> Evidence,
        int StatementLength,
        string StatementHash);

    /// <summary>
    /// One upstream requirement ledger the inventory was read out of, pinned by
    /// both the identity git stores it under and the digest of its bytes.
    /// </summary>
    internal sealed record WotSpecInventorySource(
        string Path,
        string Specification,
        string Blob,
        string Sha256,
        int RequirementCount);

    /// <summary>
    /// The header of the pinned statement-digest inventory.
    /// </summary>
    internal sealed record WotSpecInventoryHeader(
        string Commit,
        string Repository,
        string Tree,
        int SchemaVersion,
        int StatementCount,
        IReadOnlyList<WotSpecInventorySource> Ledgers);

    /// <summary>
    /// Reads the checked-in stack-side evidence ledger and resolves its
    /// mappings against the assembly that is asking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The specification's own ledgers mark these requirements
    /// <c>pendingStackTests</c>, because a specification cannot name a test in
    /// a repository it does not build. This file is the other half: it names
    /// them, and resolving each by reflection is what stops the naming from
    /// rotting the first time a test is renamed.
    /// </para>
    /// <para>
    /// The ledger spans three test assemblies, and no assembly can see the
    /// others. So each embeds the same file and checks only the mappings that
    /// name it, while the full-set invariants - the count, the uniqueness, the
    /// pinned source commit - are checked once.
    /// </para>
    /// </remarks>
    internal static class WotSpecRequirementLedger
    {
        /// <summary>
        /// The embedded ledger file name.
        /// </summary>
        public const string FileName = "wot-spec-requirements.json";

        /// <summary>
        /// The embedded statement-digest inventory file name.
        /// </summary>
        public const string InventoryFileName = "wot-spec-statements.json";

        /// <summary>
        /// Reads every requirement the ledger records.
        /// </summary>
        public static List<WotSpecRequirement> Load(Assembly assembly)
        {
            using JsonDocument document = JsonDocument.Parse(Read(assembly));
            var requirements = new List<WotSpecRequirement>();
            foreach (JsonElement entry in
                document.RootElement.GetProperty("requirements").EnumerateArray())
            {
                requirements.Add(new WotSpecRequirement(
                    entry.GetProperty("specId").GetString()!,
                    entry.GetProperty("specification").GetString()!,
                    entry.GetProperty("clause").GetString()!,
                    entry.GetProperty("applicability").GetString()!,
                    entry.GetProperty("statementHash").GetString()!,
                    entry.GetProperty("assembly").GetString()!,
                    [.. entry.GetProperty("tests")
                        .EnumerateArray()
                        .Select(e => e.GetString()!)],
                    entry.TryGetProperty("gap", out JsonElement gap)
                        ? gap.GetString()
                        : null));
            }
            return requirements;
        }

        /// <summary>
        /// Reads the header the whole-ledger invariants are checked against.
        /// </summary>
        public static (string Commit, string Repository, string Revision, int PendingCount)
            ReadHeader(Assembly assembly)
        {
            using JsonDocument document = JsonDocument.Parse(Read(assembly));
            JsonElement root = document.RootElement;
            JsonElement pinned = root.GetProperty("pinnedTo");
            return (
                pinned.GetProperty("commit").GetString()!,
                pinned.GetProperty("repository").GetString()!,
                pinned.GetProperty("bindingRevision").GetString()!,
                root.GetProperty("pendingStackTestCount").GetInt32());
        }

        /// <summary>
        /// Reads the upstream requirement ledgers the stack ledger says its
        /// digests came from.
        /// </summary>
        public static List<string> ReadLedgerPaths(Assembly assembly)
        {
            using JsonDocument document = JsonDocument.Parse(Read(assembly));
            return [.. document.RootElement
                .GetProperty("pinnedTo")
                .GetProperty("ledgers")
                .EnumerateArray()
                .Select(e => e.GetString()!)];
        }

        /// <summary>
        /// Reads the digest the ledger pins the statement inventory by, and the
        /// file name it expects to find it under.
        /// </summary>
        public static (string Path, string Sha256) ReadInventoryPin(Assembly assembly)
        {
            using JsonDocument document = JsonDocument.Parse(Read(assembly));
            JsonElement pin = document.RootElement
                .GetProperty("pinnedTo")
                .GetProperty("statementInventory");
            return (
                pin.GetProperty("path").GetString()!,
                pin.GetProperty("sha256").GetString()!);
        }

        /// <summary>
        /// Reads the pinned statement-digest inventory.
        /// </summary>
        public static List<WotSpecStatement> LoadStatements(Assembly assembly)
        {
            using JsonDocument document = JsonDocument.Parse(ReadInventory(assembly));
            var statements = new List<WotSpecStatement>();
            foreach (JsonElement entry in
                document.RootElement.GetProperty("statements").EnumerateArray())
            {
                statements.Add(new WotSpecStatement(
                    entry.GetProperty("specId").GetString()!,
                    entry.GetProperty("specification").GetString()!,
                    entry.GetProperty("clause").GetString()!,
                    entry.GetProperty("ordinal").GetInt32(),
                    entry.GetProperty("applicability").GetString()!,
                    [.. entry.GetProperty("keywords").EnumerateArray()
                        .Select(e => e.GetString()!)],
                    [.. entry.GetProperty("evidence").EnumerateArray()
                        .Select(e => e.GetString()!)],
                    entry.GetProperty("statementLength").GetInt32(),
                    entry.GetProperty("statementHash").GetString()!));
            }
            return statements;
        }

        /// <summary>
        /// Reads the inventory's own header: the upstream it was read from, the
        /// sources it was read out of, and the number of statements it claims to
        /// hold.
        /// </summary>
        public static WotSpecInventoryHeader ReadInventoryHeader(Assembly assembly)
        {
            using JsonDocument document = JsonDocument.Parse(ReadInventory(assembly));
            JsonElement root = document.RootElement;
            JsonElement pinned = root.GetProperty("pinnedTo");
            var sources = new List<WotSpecInventorySource>();
            foreach (JsonElement source in pinned.GetProperty("ledgers").EnumerateArray())
            {
                sources.Add(new WotSpecInventorySource(
                    source.GetProperty("path").GetString()!,
                    source.GetProperty("specification").GetString()!,
                    source.GetProperty("blob").GetString()!,
                    source.GetProperty("sha256").GetString()!,
                    source.GetProperty("requirementCount").GetInt32()));
            }
            return new WotSpecInventoryHeader(
                pinned.GetProperty("commit").GetString()!,
                pinned.GetProperty("repository").GetString()!,
                pinned.GetProperty("tree").GetString()!,
                root.GetProperty("schemaVersion").GetInt32(),
                root.GetProperty("statementCount").GetInt32(),
                sources);
        }

        /// <summary>
        /// Computes the SHA-256 of the inventory's actual bytes, which is what
        /// the ledger's pin is compared against.
        /// </summary>
        /// <remarks>
        /// The digest is over the bytes as embedded, so it answers "is this the
        /// file the ledger was pinned to" rather than "does a re-serialization
        /// of it look similar".
        /// </remarks>
        public static string ComputeInventoryDigest(Assembly assembly)
        {
            byte[] bytes = ReadInventory(assembly);
#if NET6_0_OR_GREATER
            byte[] digest = System.Security.Cryptography.SHA256.HashData(bytes);
#else
            using var algorithm = System.Security.Cryptography.SHA256.Create();
            byte[] digest = algorithm.ComputeHash(bytes);
#endif
            var builder = new System.Text.StringBuilder(digest.Length * 2);
            foreach (byte value in digest)
            {
                builder.Append(value.ToString(
                    "x2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        /// <summary>
        /// Resolves one named test in the given assembly and says what stopped
        /// it, so a failure names the cause rather than only the symptom.
        /// </summary>
        /// <returns>
        /// <c>"runs"</c> when the name is a fixture or test method NUnit will
        /// execute, and a description of the fault otherwise.
        /// </returns>
        public static string DescribeResolution(Assembly assembly, string name)
        {
            Type? type = assembly.GetType(name, throwOnError: false);
            if (type is not null)
            {
                if (!HasAttribute(type, "TestFixtureAttribute"))
                {
                    return "is a type that is not a TestFixture";
                }
                return IsExcluded(type) ? "is excluded from a normal run" : "runs";
            }

            int lastDot = name.LastIndexOf('.');
            if (lastDot <= 0)
            {
                return "is neither a fixture nor a method";
            }
            Type? owner = assembly.GetType(name.Substring(0, lastDot), throwOnError: false);
            if (owner is null)
            {
                return "names a type that does not exist";
            }
            MethodInfo? method = owner.GetMethod(
                name.Substring(lastDot + 1),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            if (method is null)
            {
                return "names a method that does not exist";
            }
            if (!HasAttribute(method, "TestAttribute") &&
                !HasAttribute(method, "TestCaseAttribute") &&
                !HasAttribute(method, "TestCaseSourceAttribute"))
            {
                return "names a method that is not a test";
            }
            return IsExcluded(method) || IsExcluded(owner)
                ? "is excluded from a normal run"
                : "runs";
        }

        private static bool HasAttribute(MemberInfo member, string attributeName)
        {
            return member.GetCustomAttributes(inherit: false)
                .Any(a => string.Equals(
                    a.GetType().Name, attributeName, StringComparison.Ordinal));
        }

        private static bool IsExcluded(MemberInfo member)
        {
            return member.GetCustomAttributes(inherit: false)
                .Any(a => a.GetType().Name is "ExplicitAttribute" or "IgnoreAttribute");
        }

        private static byte[] Read(Assembly assembly)
        {
            return Read(assembly, FileName);
        }

        private static byte[] ReadInventory(Assembly assembly)
        {
            return Read(assembly, InventoryFileName);
        }

        private static byte[] Read(Assembly assembly, string fileName)
        {
            string resource = assembly
                .GetManifestResourceNames()
                .Single(n => n.EndsWith(fileName, StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("The ledger is not embedded.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}
