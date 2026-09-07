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
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

#nullable enable

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// The <c>uav:nodes</c> record grammar gives every member exactly one JSON
    /// type, and a member of the wrong type is a corrupt record rather than an
    /// absent value.
    /// </summary>
    /// <remarks>
    /// A reader that answered "absent" for a member that is present and wrong
    /// would restore a quietly different NodeSet: a Node with no NodeId, a
    /// concrete type where the record says abstract, a Variable with the
    /// default ValueRank. Nothing in the restored model would say either
    /// happened, and the projection exists precisely so that nothing is lost.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotNativeProjectionScalarKindTests
    {
        [TestCase("\"nodeId\":42", "/uav:nodes/nodes/0/nodeId", "a string",
            TestName = "ANumericNodeIdIsRejected")]
        [TestCase("\"nodeId\":null", "/uav:nodes/nodes/0/nodeId", "a string",
            TestName = "ANullNodeIdIsRejected")]
        [TestCase("\"browseName\":true", "/uav:nodes/nodes/0/browseName", "a string",
            TestName = "ABooleanBrowseNameIsRejected")]
        [TestCase("\"isAbstract\":\"true\"", "/uav:nodes/nodes/0/isAbstract", "a boolean",
            TestName = "ATextualIsAbstractIsRejected")]
        [TestCase("\"isAbstract\":1", "/uav:nodes/nodes/0/isAbstract", "a boolean",
            TestName = "ANumericIsAbstractIsRejected")]
        [TestCase("\"valueRank\":\"-1\"", "/uav:nodes/nodes/0/valueRank", "a number",
            TestName = "ATextualValueRankIsRejected")]
        [TestCase("\"references\":{}", "/uav:nodes/nodes/0/references", "an array",
            TestName = "AnObjectReferenceListIsRejected")]
        [TestCase("\"displayName\":\"Pump\"", "/uav:nodes/nodes/0/displayName", "an array",
            TestName = "AScalarDisplayNameIsRejected")]
        [TestCase("\"definition\":[]", "/uav:nodes/nodes/0/definition", "an object",
            TestName = "AnArrayDefinitionIsRejected")]
        [TestCase("\"accessLevel\":\"1\"", "/uav:nodes/nodes/0/accessLevel", "a number",
            TestName = "ATextualAccessLevelIsRejected")]
        public void AMemberOfTheWrongTypeIsRejectedAtItsPointer(
            string member, string pointer, string expected)
        {
            List<WotDiagnostic> diagnostics = Read(
                "{\"@type\":\"uav:NodeModel\",\"profileVersion\":\"1.0\"," +
                "\"nodes\":[{\"nodeClass\":\"Object\",\"nodeId\":\"i=1\"," +
                member + "}]}",
                out UANodeSet? nodeSet);

            Assert.Multiple(() =>
            {
                Assert.That(
                    nodeSet,
                    Is.Null,
                    "A corrupt record restores nothing; a partially restored NodeSet would " +
                    "be the silent loss the projection exists to prevent.");
                Assert.That(
                    diagnostics.Any(d =>
                        d.Severity == WotDiagnosticSeverity.Error &&
                        d.Code == WotDiagnosticCode.NativeProjectionInvalid &&
                        d.Location?.JsonPointer == pointer &&
                        d.Message.Contains(expected, StringComparison.Ordinal)),
                    Is.True,
                    string.Join(
                        "; ",
                        diagnostics.Select(d => $"{d.Location?.JsonPointer}: {d.Message}")));
            });
        }

        /// <summary>
        /// The pointer is the place the record is wrong, which for a member of
        /// a nested record is the nested pointer and not the node it belongs
        /// to.
        /// </summary>
        [Test]
        public void ANestedMemberIsReportedAtItsOwnPointer()
        {
            List<WotDiagnostic> diagnostics = Read(
                "{\"@type\":\"uav:NodeModel\",\"profileVersion\":\"1.0\"," +
                "\"nodes\":[{\"nodeClass\":\"Object\",\"nodeId\":\"i=1\"," +
                "\"references\":[{\"referenceType\":\"i=47\",\"isForward\":\"yes\"," +
                "\"target\":\"i=2\"}]}]}",
                out _);

            Assert.That(
                diagnostics.Any(d =>
                    d.Location?.JsonPointer ==
                        "/uav:nodes/nodes/0/references/0/isForward"),
                Is.True,
                string.Join(
                    "; ",
                    diagnostics.Select(d => $"{d.Location?.JsonPointer}: {d.Message}")));
        }

        /// <summary>
        /// <c>value</c> is the aliased NodeId of an alias, the text of a
        /// LocalizedText and the ordinal of an enumeration field, so it is a
        /// string or a number - and never a structure.
        /// </summary>
        [TestCase("\"i=47\"", true, TestName = "AStringAliasValueIsAccepted")]
        [TestCase("47", true, TestName = "ANumericValueIsAccepted")]
        [TestCase("{}", false, TestName = "AnObjectValueIsRejected")]
        [TestCase("[]", false, TestName = "AnArrayValueIsRejected")]
        [TestCase("true", false, TestName = "ABooleanValueIsRejected")]
        public void ThePolymorphicValueMemberAdmitsOnlyScalars(
            string value, bool accepted)
        {
            List<WotDiagnostic> diagnostics = Read(
                "{\"@type\":\"uav:NodeModel\",\"profileVersion\":\"1.0\"," +
                "\"aliases\":[{\"alias\":\"HasComponent\",\"value\":" + value + "}]," +
                "\"nodes\":[]}",
                out _);

            Assert.That(
                diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.NativeProjectionInvalid &&
                    d.Location?.JsonPointer == "/uav:nodes/aliases/0/value"),
                Is.EqualTo(!accepted),
                string.Join("; ", diagnostics.Select(d => d.Message)));
        }

        /// <summary>
        /// A member the grammar does not name is not constrained here: the
        /// table is a list of what the projection writes, not a closed world,
        /// so a record from a later revision reaches the reader that needs it.
        /// </summary>
        [Test]
        public void AnUnknownMemberIsNotConstrained()
        {
            List<WotDiagnostic> diagnostics = Read(
                "{\"@type\":\"uav:NodeModel\",\"profileVersion\":\"1.0\"," +
                "\"nodes\":[{\"nodeClass\":\"Object\",\"nodeId\":\"i=1\"," +
                "\"browseName\":\"1:X\"," +
                "\"vendorFuture\":{\"anything\":[1,2,3]}}]}",
                out UANodeSet? nodeSet);

            Assert.Multiple(() =>
            {
                Assert.That(
                    diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                    Is.Empty,
                    string.Join("; ", diagnostics.Select(d => d.Message)));
                Assert.That(nodeSet, Is.Not.Null);
            });
        }

        /// <summary>
        /// Every member the projection itself writes has to survive its own
        /// check, or the grammar table and the writer disagree.
        /// </summary>
        [Test]
        public void TheProjectionTheWriterProducesPassesItsOwnCheck()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                WotTestData.CreateRichNodeSet(),
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Always
                });

            Assert.That(document.TryGetNativeProjection(out JsonElement projection), Is.True);

            var diagnostics = new List<WotDiagnostic>();
            UANodeSet? restored = WotNativeProjection.Read(
                projection, new WotNodeSetConverterOptions(), diagnostics);

            Assert.Multiple(() =>
            {
                Assert.That(
                    diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                    Is.Empty,
                    string.Join("; ", diagnostics.Select(d => d.Message)));
                Assert.That(restored, Is.Not.Null);
            });
        }

        private static List<WotDiagnostic> Read(
            string json, out UANodeSet? nodeSet)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            var diagnostics = new List<WotDiagnostic>();
            nodeSet = WotNativeProjection.Read(
                document.RootElement, new WotNodeSetConverterOptions(), diagnostics);
            return diagnostics;
        }
    }
}
