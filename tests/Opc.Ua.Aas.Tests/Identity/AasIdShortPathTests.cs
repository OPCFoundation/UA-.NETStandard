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

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Opc.Ua.Aas.Tests.Identity
{
    /// <summary>
    /// Tests the metamodel path convention and the operation-variable role
    /// paths that clause 6.1.3 derives an element's NodeId from.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasIdShortPathTests
    {
        [Test]
        public void ADirectChildOfASubmodelIsItsOwnPath()
        {
            Assert.That(AasIdShortPath.AppendName(string.Empty, "Ordering"), Is.EqualTo("Ordering"));
        }

        [Test]
        public void NamedChildrenAreJoinedWithADot()
        {
            Assert.That(AasIdShortPath.AppendName("Parent", "Child"), Is.EqualTo("Parent.Child"));
        }

        [Test]
        public void AListMemberIsAddressedByIndex()
        {
            Assert.That(AasIdShortPath.AppendIndex("CollectionsInsideAList", 0),
                Is.EqualTo("CollectionsInsideAList[0]"));
        }

        [Test]
        public void TheSpecificationsWorkedElementPathIsReproduced()
        {
            // The ordering-and-nesting fixture of Annex F.4.
            string list = AasIdShortPath.AppendName(string.Empty, "CollectionsInsideAList");
            string member = AasIdShortPath.AppendIndex(list, 0);

            Assert.That(member, Is.EqualTo("CollectionsInsideAList[0]"));
        }

        [TestCase(AasOperationVariableRole.Input, "inputVariables")]
        [TestCase(AasOperationVariableRole.Output, "outputVariables")]
        [TestCase(AasOperationVariableRole.Inoutput, "inoutputVariables")]
        public void RoleNamesAreTheExactMetamodelFieldNames(
            AasOperationVariableRole role,
            string expected)
        {
            Assert.That(AasIdShortPath.NameOf(role), Is.EqualTo(expected));
        }

        [Test]
        public void TheFirstInputValueOfAnOperationMatchesTheSpecificationExample()
        {
            Assert.That(
                AasIdShortPath.AppendOperationVariable(
                    "AnOperation", AasOperationVariableRole.Input, 0),
                Is.EqualTo("AnOperation.inputVariables[0]"));
        }

        [Test]
        public void IndicesRestartAtZeroForEachRole()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    AasIdShortPath.AppendOperationVariable(
                        "Op", AasOperationVariableRole.Input, 0),
                    Is.EqualTo("Op.inputVariables[0]"));
                Assert.That(
                    AasIdShortPath.AppendOperationVariable(
                        "Op", AasOperationVariableRole.Output, 0),
                    Is.EqualTo("Op.outputVariables[0]"));
                Assert.That(
                    AasIdShortPath.AppendOperationVariable(
                        "Op", AasOperationVariableRole.Inoutput, 0),
                    Is.EqualTo("Op.inoutputVariables[0]"));
            });
        }

        [TestCase("inputVariables", AasOperationVariableRole.Input)]
        [TestCase("outputVariables", AasOperationVariableRole.Output)]
        [TestCase("inoutputVariables", AasOperationVariableRole.Inoutput)]
        public void RoleNamesParseBack(string name, AasOperationVariableRole expected)
        {
            Assert.That(AasIdShortPath.TryParseRole(name, out AasOperationVariableRole role), Is.True);
            Assert.That(role, Is.EqualTo(expected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("InputVariables")]
        [TestCase("inputvariables")]
        [TestCase("inputs")]
        public void AnUnknownRoleNameIsRejected(string? name)
        {
            Assert.That(AasIdShortPath.TryParseRole(name, out _), Is.False);
        }

        [Test]
        public void ANamedPathParsesIntoItsSegments()
        {
            Assert.That(AasIdShortPath.TryParse("A.B.C", out IReadOnlyList<AasIdShortPathSegment> segments),
                Is.True);
            Assert.That(segments, Is.EqualTo(new[]
            {
                AasIdShortPathSegment.ForName("A"),
                AasIdShortPathSegment.ForName("B"),
                AasIdShortPathSegment.ForName("C")
            }));
        }

        [Test]
        public void AnIndexedPathParsesIntoItsSegments()
        {
            Assert.That(
                AasIdShortPath.TryParse("List[2].Value", out IReadOnlyList<AasIdShortPathSegment> segments),
                Is.True);
            Assert.That(segments, Is.EqualTo(new[]
            {
                AasIdShortPathSegment.ForName("List"),
                AasIdShortPathSegment.ForIndex(2),
                AasIdShortPathSegment.ForName("Value")
            }));
        }

        [Test]
        public void NestedIndicesParseIntoTheirSegments()
        {
            Assert.That(
                AasIdShortPath.TryParse("L[0][1]", out IReadOnlyList<AasIdShortPathSegment> segments),
                Is.True);
            Assert.That(segments, Is.EqualTo(new[]
            {
                AasIdShortPathSegment.ForName("L"),
                AasIdShortPathSegment.ForIndex(0),
                AasIdShortPathSegment.ForIndex(1)
            }));
        }

        [Test]
        public void AnOperationVariablePathParsesIntoItsSegments()
        {
            Assert.That(
                AasIdShortPath.TryParse(
                    "AnOperation.inputVariables[0]",
                    out IReadOnlyList<AasIdShortPathSegment> segments),
                Is.True);
            Assert.That(segments, Is.EqualTo(new[]
            {
                AasIdShortPathSegment.ForName("AnOperation"),
                AasIdShortPathSegment.ForName("inputVariables"),
                AasIdShortPathSegment.ForIndex(0)
            }));
        }

        [TestCase(null, TestName = "RejectsNull")]
        [TestCase("", TestName = "RejectsEmpty")]
        [TestCase(".A", TestName = "RejectsALeadingDot")]
        [TestCase("A..B", TestName = "RejectsAnEmptySegment")]
        [TestCase("[0]", TestName = "RejectsALeadingIndex")]
        [TestCase("A[", TestName = "RejectsAnUnclosedIndex")]
        [TestCase("A[]", TestName = "RejectsAnEmptyIndex")]
        [TestCase("A[01]", TestName = "RejectsALeadingZeroInAnIndex")]
        [TestCase("A[x]", TestName = "RejectsANonNumericIndex")]
        [TestCase("A[-1]", TestName = "RejectsANegativeIndex")]
        public void AMalformedPathIsRejected(string? path)
        {
            Assert.That(AasIdShortPath.TryParse(path, out _), Is.False);
        }

        [Test]
        public void SegmentsRenderBackToTheirPathForm()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AasIdShortPathSegment.ForName("A").ToString(), Is.EqualTo("A"));
                Assert.That(AasIdShortPathSegment.ForIndex(3).ToString(), Is.EqualTo("[3]"));
                Assert.That(AasIdShortPathSegment.ForName("A").IsIndex, Is.False);
                Assert.That(AasIdShortPathSegment.ForIndex(3).IsIndex, Is.True);
            });
        }

        [Test]
        public void SegmentEqualityDistinguishesNamesFromIndices()
        {
            AasIdShortPathSegment name = AasIdShortPathSegment.ForName("0");
            AasIdShortPathSegment index = AasIdShortPathSegment.ForIndex(0);

            bool equalOperator = name == AasIdShortPathSegment.ForName("0");
            bool notEqualOperator = name != index;

            Assert.Multiple(() =>
            {
                Assert.That(name, Is.Not.EqualTo(index));
                Assert.That(equalOperator, Is.True);
                Assert.That(notEqualOperator, Is.True);
                Assert.That(
                    name.GetHashCode(),
                    Is.EqualTo(AasIdShortPathSegment.ForName("0").GetHashCode()));
            });
        }

        [Test]
        public void PathBuildersRejectNullArguments()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => AasIdShortPath.AppendName(null!, "A"), Throws.ArgumentNullException);
                Assert.That(
                    () => AasIdShortPath.AppendName(string.Empty, null!), Throws.ArgumentNullException);
                Assert.That(
                    () => AasIdShortPath.AppendIndex(null!, 0), Throws.ArgumentNullException);
                Assert.That(
                    () => AasIdShortPath.AppendOperationVariable(
                        null!, AasOperationVariableRole.Input, 0),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public void PathBuildersRejectNegativeIndices()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => AasIdShortPath.AppendIndex("A", -1),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => AasIdShortPath.AppendOperationVariable(
                        "A", AasOperationVariableRole.Input, -1),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void AnElementNodeIdIsBuiltFromTheOwnerAndTheDerivedPath()
        {
            // The two halves compose: this is how clause 6.1.6 step 3 walks a
            // submodel and gives each element its identity.
            const string owner = "https://fabrikam.com/ids/sm/ordering";
            string path = AasIdShortPath.AppendIndex(
                AasIdShortPath.AppendName(string.Empty, "CollectionsInsideAList"), 0);

            string identifier = AasNodeIdEncoding.CreateElementId(owner, path);

            Assert.That(AasNodeIdEncoding.TryParse(identifier, out AasParsedNodeId parsed), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(parsed.Id, Is.EqualTo(owner));
                Assert.That(parsed.IdShortPath, Is.EqualTo("CollectionsInsideAList[0]"));
            });
        }
    }
}
