/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.MigrationAnalyzer.Core.Tests
{
    /// <summary>
    /// Meta-tests that scan every <see cref="OpcUaShimAttribute"/>-marked
    /// member in the shim assembly. These guard the contract that the
    /// analyzer relies on: each shim member must carry both an
    /// <see cref="ObsoleteAttribute"/> and a valid <c>UA00xx</c> rule id.
    /// </summary>
    [TestFixture]
    [Category("Shim")]
    public partial class OpcUaShimAttributeInventoryTests
    {
#if NET7_0_OR_GREATER
        [GeneratedRegex(@"^UA\d{4}$", RegexOptions.CultureInvariant)]
        private static partial Regex RuleIdRegex();
#else
        private static Regex RuleIdRegex() => s_ruleIdRegex;
        private static readonly Regex s_ruleIdRegex =
            new(@"^UA\d{4}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
#endif

        private static IEnumerable<MemberInfo> ShimMembers()
        {
            Assembly shimAssembly = typeof(OpcUaShimAttribute).Assembly;
            foreach (Type type in shimAssembly.GetTypes())
            {
                foreach (MemberInfo member in type.GetMembers(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Static |
                    BindingFlags.Instance |
                    BindingFlags.DeclaredOnly))
                {
                    if (member.GetCustomAttribute<OpcUaShimAttribute>() != null)
                    {
                        yield return member;
                    }
                }
            }
        }

        /// <summary>
        /// Sanity check: the shim assembly exposes at least one
        /// <c>[OpcUaShim]</c>-attributed member.
        /// </summary>
        [Test]
        public Task ShimAssemblyContainsAttributedMembersAsync()
        {
            MemberInfo[] members = ShimMembers().ToArray();
            Assert.That(members, Is.Not.Empty,
                "Expected at least one [OpcUaShim] member in the shim assembly.");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Every <c>[OpcUaShim]</c> member must also carry
        /// <see cref="ObsoleteAttribute"/> so callers see the migration
        /// guidance at the call site.
        /// </summary>
        [Test]
        public Task EveryShimMemberIsAlsoObsoleteAsync()
        {
            var missing = ShimMembers()
                .Where(m => m.GetCustomAttribute<ObsoleteAttribute>() == null)
                .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}")
                .ToArray();

            Assert.That(missing, Is.Empty,
                "These shim members are missing [Obsolete]: " +
                string.Join(", ", missing));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Rule ids must match the <c>UA00xx</c> convention so the analyzer
        /// can correlate the shim with its diagnostic descriptor.
        /// </summary>
        [Test]
        public Task EveryShimRuleIdMatchesUa00xxConventionAsync()
        {
            var malformed = ShimMembers()
                .Select(m => (Member: m, Id: m.GetCustomAttribute<OpcUaShimAttribute>()!.RuleId))
                .Where(t => !RuleIdRegex().IsMatch(t.Id))
                .Select(t => $"{t.Member.DeclaringType?.FullName}.{t.Member.Name}={t.Id}")
                .ToArray();

            Assert.That(malformed, Is.Empty,
                "These shim members have non-UA00xx rule ids: " +
                string.Join(", ", malformed));
            return Task.CompletedTask;
        }
    }
}
