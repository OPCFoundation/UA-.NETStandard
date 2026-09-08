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

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// One clause of the WoT Binding and the tests that hold it.
    /// </summary>
    /// <param name="Clause">The clause number.</param>
    /// <param name="Title">What the clause is about.</param>
    /// <param name="Assembly">The test assembly the mapping lives in.</param>
    /// <param name="Tests">The fully qualified fixtures or test methods.</param>
    internal sealed record WotTraceabilityClause(
        string Clause, string Title, string Assembly, IReadOnlyList<string> Tests);

    /// <summary>
    /// Reads the clause-to-test map and resolves a mapping against a test
    /// assembly.
    /// </summary>
    /// <remarks>
    /// The map spans more than one test assembly, and no assembly can see
    /// another's types. Each assembly therefore resolves only the clauses that
    /// name it, which is why the reading and the reflection live here rather
    /// than in one fixture: a second copy of this logic would be the thing that
    /// drifts.
    /// </remarks>
    internal static class WotTraceabilityLedger
    {
        /// <summary>
        /// The embedded ledger file name.
        /// </summary>
        public const string FileName = "wot-binding-traceability.json";

        /// <summary>
        /// The test assemblies a clause may be held in.
        /// </summary>
        public static IReadOnlyList<string> Assemblies { get; } =
        [
            "Opc.Ua.Types.Tests",
            "Opc.Ua.WotCon.Tests",
            "Opc.Ua.WotCon.Bindings.Tests"
        ];

        /// <summary>
        /// Reads every clause from the ledger embedded in an assembly.
        /// </summary>
        /// <param name="assembly">The assembly carrying the ledger.</param>
        /// <returns>The clauses, in file order.</returns>
        public static List<WotTraceabilityClause> Load(Assembly assembly)
        {
            using JsonDocument document = JsonDocument.Parse(Read(assembly));
            var clauses = new List<WotTraceabilityClause>();
            foreach (JsonElement clause in
                document.RootElement.GetProperty("clauses").EnumerateArray())
            {
                clauses.Add(new WotTraceabilityClause(
                    clause.GetProperty("clause").GetString()!,
                    clause.GetProperty("title").GetString()!,
                    clause.TryGetProperty("assembly", out JsonElement owner)
                        ? owner.GetString()!
                        : "Opc.Ua.Types.Tests",
                    [.. clause.GetProperty("tests")
                        .EnumerateArray()
                        .Select(e => e.GetString()!)]));
            }
            return clauses;
        }

        /// <summary>
        /// Reads the raw ledger bytes embedded in an assembly.
        /// </summary>
        /// <param name="assembly">The assembly carrying the ledger.</param>
        /// <returns>The UTF-8 ledger bytes.</returns>
        public static byte[] Read(Assembly assembly)
        {
            string resource = assembly
                .GetManifestResourceNames()
                .Single(n => n.EndsWith(FileName, StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("The ledger is not embedded.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        /// <summary>
        /// Resolves one mapping and says what stopped it, so a failure names
        /// the cause rather than only the symptom.
        /// </summary>
        /// <param name="assembly">The assembly the mapping names.</param>
        /// <param name="name">The fully qualified fixture or test method.</param>
        /// <returns><c>runs</c>, or what is wrong with the mapping.</returns>
        public static string DescribeResolution(Assembly assembly, string name)
        {
            Type? type = assembly.GetType(name, throwOnError: false);
            if (type is not null)
            {
                if (type.GetCustomAttributes(inherit: false)
                    .Any(a => a.GetType().Name is "TestFixtureAttribute"))
                {
                    return IsExcluded(type) ? "is excluded from a normal run" : "runs";
                }
                return "is a type that is not a TestFixture";
            }

            int lastDot = name.LastIndexOf('.');
            if (lastDot <= 0)
            {
                return "is neither a fixture nor a method";
            }
            Type? owner = assembly.GetType(
                name.Substring(0, lastDot), throwOnError: false);
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
            if (!method.GetCustomAttributes(inherit: false)
                .Any(a => a.GetType().Name is "TestAttribute" or "TestCaseAttribute"))
            {
                return "names a method that is not a test";
            }
            return IsExcluded(method) || IsExcluded(owner)
                ? "is excluded from a normal run"
                : "runs";
        }

        private static bool IsExcluded(MemberInfo member)
        {
            return member.GetCustomAttributes(inherit: false)
                .Any(a => a.GetType().Name is "ExplicitAttribute" or "IgnoreAttribute");
        }
    }
}
