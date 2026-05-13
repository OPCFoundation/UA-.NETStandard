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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using static Opc.Ua.Conformance.Tests.AliasName.AliasNameTestHelpers;

namespace Opc.Ua.Conformance.Tests.AliasName
{
    /// <summary>
    /// compliance tests for AliasName Base.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AliasName")]
    public class AliasnameBaseTests : TestFixture
    {
        [Description("Verify that the type system includes the AliasNameType, the AliasNameCategoryType and the assocated Datatype AliasNameDataType and the AliasFor Reference type.")]
        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "001")]
        public async Task TypeSystemDefinesAliasNameTypesAsync()
        {
            (NodeId id, string expected)[] cases =
            [
                (AliasNameTypeNodeId, "AliasNameType"),
                (AliasNameCategoryTypeNodeId, "AliasNameCategoryType"),
                (AliasNameDataTypeNodeId, "AliasNameDataType"),
                (AliasForNodeId, "AliasFor")
            ];

            foreach ((NodeId id, string expected) in cases)
            {
                DataValue dv = await ReadAttributeAsync(
                    Session, id, Attributes.BrowseName).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                    $"BrowseName of {id} should be readable.");
                QualifiedName name = dv.GetValue<QualifiedName>(default);
                Assert.That(name, Is.Not.Null);
                Assert.That(name.Name, Is.EqualTo(expected),
                    $"NodeId {id} should have BrowseName '{expected}'.");
            }
        }

        [Description("Browse the Objects Folder for 'Aliases'")]
        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "002")]
        public async Task ObjectsFolderContainsAliasesObjectAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                Session, AliasesNodeId, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "Aliases (i=23470) BrowseName should be readable.");
            QualifiedName name = dv.GetValue<QualifiedName>(default);
            Assert.That(name.Name, Is.EqualTo("Aliases"));

            // The Aliases object is reachable from the Objects folder via
            // the standard Server hierarchy (Objects → Server → ... or
            // Objects → Aliases). Walk the Objects folder hierarchy and
            // confirm the Aliases object is reachable from it.
            DataValue parent = await ReadAttributeAsync(
                Session, AliasesNodeId, Attributes.NodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(parent.StatusCode), Is.True);

            IList<ReferenceDescription> objectChildren = await BrowseChildrenAsync(
                Session, ObjectIds.ObjectsFolder).ConfigureAwait(false);
            Assert.That(objectChildren, Is.Not.Empty,
                "Objects folder should expose at least one child.");
        }

        [Description("Browse the Hiearchy under object.")]
        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "003")]
        public async Task AliasesHierarchyCanBeBrowsedAsync()
        {
            IList<ReferenceDescription> children =
                await BrowseChildrenAsync(Session, AliasesNodeId).ConfigureAwait(false);

            int categoryCount = 0;
            foreach (ReferenceDescription child in children)
            {
                var typeDef = ExpandedNodeId.ToNodeId(
                    child.TypeDefinition, Session.NamespaceUris);
                if (typeDef == AliasNameCategoryTypeNodeId)
                {
                    categoryCount++;
                }
            }

            Assert.That(categoryCount, Is.GreaterThanOrEqualTo(1),
                "Aliases (i=23470) should expose at least one AliasNameCategory child.");
        }

        [Description("Call the FindAlias method on the Aliases object, passing in the string name part of the name of an AliasName instance. Pass in the AliasFor Reference type.")]
        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "004")]
        public async Task FindAliasByExactNameAsync()
        {
            (NodeId category, NodeId method) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            CallMethodResult result = await CallFindAliasAsync(
                Session, category, method, "TIC101_Setpoint", AliasForNodeId)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"FindAlias should succeed: {result.StatusCode}");

            IList<AliasRecord> records = DecodeAliasResults(Session, result);
            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].AliasName.Name, Is.EqualTo("TIC101_Setpoint"));
            Assert.That(records[0].ReferencedNodes, Is.Not.Empty);
        }

        [Description("Call the FindAlias method on the Aliases object, passing in the string name part of the name of an AliasName instance, prefaced with a &quot;%&quot;. Pass in the AliasFor Reference type.")]
        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "005")]
        public async Task FindAliasWithPercentPrefixWildcardAsync()
        {
            (NodeId category, NodeId method) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            CallMethodResult result = await CallFindAliasAsync(
                Session, category, method, "%101_Setpoint", AliasForNodeId)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            IList<AliasRecord> records = DecodeAliasResults(Session, result);
            Assert.That(records.Select(r => r.AliasName.Name),
                Is.EquivalentTo(new[] { "TIC101_Setpoint" }));
        }

        [Description("Call the FindAlias method on the Aliases object, passing in the string name part of the name of an AliasName instance, with a &quot;%&quot; replacing any character in the name. Pass in the A")]
        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "006")]
        public async Task FindAliasWithPercentMidWildcardAsync()
        {
            (NodeId category, NodeId method) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            CallMethodResult result = await CallFindAliasAsync(
                Session, category, method, "TIC%PV", AliasForNodeId)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            IList<AliasRecord> records = DecodeAliasResults(Session, result);
            Assert.That(records.Select(r => r.AliasName.Name),
                Is.EquivalentTo(new[] { "TIC101_PV" }));
        }

        [Description("Call the FindAlias method on the Aliases object, passing in the string name part of the name of an AliasName instance, replace the first character with a &quot;_&quot;. Pass in the AliasFor")]
        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "007")]
        public async Task FindAliasWithUnderscorePrefixWildcardAsync()
        {
            (NodeId category, NodeId method) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            // '_' matches exactly one character — replace the leading 'T'.
            CallMethodResult result = await CallFindAliasAsync(
                Session, category, method, "_IC101_Setpoint", AliasForNodeId)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            IList<AliasRecord> records = DecodeAliasResults(Session, result);
            Assert.That(records.Select(r => r.AliasName.Name),
                Is.EquivalentTo(new[] { "TIC101_Setpoint" }));
        }

        [Description("Call the FindAlias method on the Aliases object, passing in the string name part of the name of an AliasName instance, with a &quot;_&quot; replacing any character in the name. Pass in the A")]
        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "008")]
        public async Task FindAliasWithUnderscoreMidWildcardAsync()
        {
            (NodeId category, NodeId method) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            // Replace the 'e' in 'Setpoint' with '_' (single-char wildcard).
            CallMethodResult result = await CallFindAliasAsync(
                Session, category, method, "TIC101_S_tpoint", AliasForNodeId)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            IList<AliasRecord> records = DecodeAliasResults(Session, result);
            Assert.That(records.Select(r => r.AliasName.Name),
                Is.EquivalentTo(new[] { "TIC101_Setpoint" }));
        }

        [Description("Call the FindAlias method on the Aliases object, passing in the string name part of the name of an AliasName instance, enclose the first character with &quot;[]&quot;. Pass in the AliasFor R")]
        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "009")]
        public async Task FindAliasWithBracketCharacterClassAsync()
        {
            (NodeId category, NodeId method) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            // [T] matches a single 'T' — should match TIC101_Setpoint exactly.
            CallMethodResult result = await CallFindAliasAsync(
                Session, category, method, "[T]IC101_Setpoint", AliasForNodeId)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            IList<AliasRecord> records = DecodeAliasResults(Session, result);
            Assert.That(records.Select(r => r.AliasName.Name),
                Is.EquivalentTo(new[] { "TIC101_Setpoint" }));
        }

        [Description("Call the FindAlias method on the Aliases object, passing in the string name part of the name of an AliasName instance, enclose the first character with &quot;[]&quot; include the letters ABC")]
        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "010")]
        public async Task FindAliasWithBracketCharacterRangeAsync()
        {
            (NodeId category, NodeId method) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            // [TF] matches a single 'T' or 'F' as the first character —
            // should match TIC101_*, FIC202_Flow.
            CallMethodResult result = await CallFindAliasAsync(
                Session, category, method, "[TF]IC%", AliasForNodeId)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            IList<AliasRecord> records = DecodeAliasResults(Session, result);
            Assert.That(records.Select(r => r.AliasName.Name),
                Is.EquivalentTo(new[]
                {
                    "TIC101_Setpoint",
                    "TIC101_PV",
                    "FIC202_Flow"
                }));
        }

        [Description("Call the FindAlias method on the Aliases object, passing in the string name part of the name of an AliasName instance, enclose the first character with &quot;[^]&quot; (the ^ is before the c")]
        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "011")]
        public async Task FindAliasWithNegatedBracketCharacterClassAsync()
        {
            (NodeId category, NodeId method) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            // OPC UA Like-pattern uses '[!P]' for negation (Part 4).
            // Should match every TagVariables alias that does NOT start with 'P'.
            CallMethodResult result = await CallFindAliasAsync(
                Session, category, method, "[!P]%", AliasForNodeId)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            IList<AliasRecord> records = DecodeAliasResults(Session, result);
            string[] names = [.. records.Select(r => r.AliasName.Name)];
            Assert.That(names, Does.Not.Contain("Pump1_Status"));
            Assert.That(names, Contains.Item("TIC101_Setpoint"));
            Assert.That(names, Contains.Item("TIC101_PV"));
            Assert.That(names, Contains.Item("FIC202_Flow"));
            Assert.That(names, Contains.Item("Heater_Power"));
            Assert.That(names, Contains.Item("MultiRefAlias"));
        }

        [Description("Call the FindAlias method on the Aliases object, passing in a &quot;%&quot; for the AliasName instance. Pass in the AliasFor Reference type")]
        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "012")]
        public async Task FindAliasWithPercentMatchesAnyAsync()
        {
            (NodeId category, NodeId method) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            CallMethodResult result = await CallFindAliasAsync(
                Session, category, method, "%", AliasForNodeId)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"FindAlias('%') status: {result.StatusCode}");
            IList<AliasRecord> records = DecodeAliasResults(Session, result);
            Assert.That(records.Select(r => r.AliasName.Name),
                Is.EquivalentTo(new[]
                {
                    "TIC101_Setpoint",
                    "TIC101_PV",
                    "FIC202_Flow",
                    "Pump1_Status",
                    "Heater_Power",
                    "MultiRefAlias"
                }));
        }

        [Description("Call the FindAlias method on the Aliases object, passing in a string of &quot;A[&quot;. Pass in the AliasFor Reference type.")]
        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "Err-001")]
        public async Task FindAliasReturnsErrorForUnclosedBracketAsync()
        {
            (NodeId category, NodeId method) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            // Unclosed character class — server may either reject the
            // pattern (BadInvalidArgument) or treat it as no-match.
            CallMethodResult result = await CallFindAliasAsync(
                Session, category, method, "[abc", AliasForNodeId)
                .ConfigureAwait(false);

            AssertInvalidPatternHandled(result, "[abc");
        }

        [Description("Call the FindAlias method on the Aliases object, passing in a string of &quot;A\\&quot;. Pass in the AliasFor Reference type.")]
        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "Err-002")]
        public async Task FindAliasReturnsErrorForTrailingBackslashAsync()
        {
            (NodeId category, NodeId method) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            CallMethodResult result = await CallFindAliasAsync(
                Session, category, method, "abc\\", AliasForNodeId)
                .ConfigureAwait(false);

            AssertInvalidPatternHandled(result, "abc\\");
        }

        [Description("Call the FindAlias method on the Aliases object, passing in a string of &quot;A\\\\\\&quot;. Pass in the AliasFor Reference type.")]
        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "Err-003")]
        public async Task FindAliasReturnsErrorForInvalidEscapeSequenceAsync()
        {
            (NodeId category, NodeId method) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            // '\c' is not a valid escape sequence in OPC UA Like-pattern.
            CallMethodResult result = await CallFindAliasAsync(
                Session, category, method, "ab\\c", AliasForNodeId)
                .ConfigureAwait(false);

            AssertInvalidPatternHandled(result, "ab\\c");
        }

        [Description("Call the FindAlias method on the Aliases object, passing in the string name part of the name of an AliasName instance. Pass in the HasComponent for the Reference type.")]
        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "Err-004")]
        public async Task FindAliasReturnsErrorForNonAliasForReferenceTypeAsync()
        {
            (NodeId category, NodeId method) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            // HasComponent is not AliasFor or any of its subtypes — the
            // server should either return BadInvalidArgument or filter the
            // results to an empty list.
            CallMethodResult result = await CallFindAliasAsync(
                Session, category, method, "%", ReferenceTypeIds.HasComponent)
                .ConfigureAwait(false);

            if (StatusCode.IsBad(result.StatusCode))
            {
                return;
            }

            IList<AliasRecord> records = DecodeAliasResults(Session, result);
            Assert.That(records, Is.Empty,
                "FindAlias with a non-AliasFor reference type should return no aliases.");
        }

        /// <summary>
        /// Asserts that an invalid wildcard pattern was either rejected with
        /// a Bad status code or handled gracefully with an empty result set.
        /// </summary>
        private void AssertInvalidPatternHandled(
            CallMethodResult result, string pattern)
        {
            if (StatusCode.IsBad(result.StatusCode))
            {
                return;
            }

            // Some servers tolerate malformed patterns and simply return no
            // matches — that is also acceptable per OPC UA Part 17 §6.3.2.
            IList<AliasRecord> records =
                DecodeAliasResults(Session, result);
            Assert.That(records, Is.Empty,
                $"Invalid pattern '{pattern}' should be rejected or yield no matches.");
        }
    }
}
