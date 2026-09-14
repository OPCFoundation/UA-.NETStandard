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
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Regression tests for the NodeId / ExpandedNodeId / NumericRange /
    /// DateTimeUtc / DiagnosticInfo / QualifiedName / XmlElement defects
    /// reported by the read-only bug audit of Opc.Ua.Types.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class IdentifierAuditRegressionTests
    {
        private static readonly ILogger s_logger = new Mock<ILogger>().Object;
        private static readonly string[] s_twoStrings = ["a", "b"];
        private static readonly int[] s_oneTwoThree = [1, 2, 3];
        private static readonly int[] s_fourFive = [4, 5];

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
        }

        [Test]
        public void ServerIndexIsMappedThroughTheServerMappingTable()
        {
            // The parser mapped svr= through NamespaceMappings with an inverted
            // bounds check, so this payload threw IndexOutOfRangeException.
            ServiceMessageContext context = CreateContext();
            var options = new NodeIdParsingOptions
            {
                NamespaceMappings = [0, 1],
                ServerMappings = [0, 0, 0, 0, 0, 7]
            };

            Assert.That(
                ExpandedNodeId.TryParse(context, "svr=5;i=42", options, out ExpandedNodeId value),
                Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(value.ServerIndex, Is.EqualTo(7u));
                Assert.That(value.InnerNodeId.NumericIdentifier, Is.EqualTo(42u));
            });
        }

        [Test]
        public void ServerIndexAboveUInt16RangeIsNotDropped()
        {
            // The index was parsed as a ushort, so anything above 65535 was
            // silently discarded and the node id became server local.
            ServiceMessageContext context = CreateContext();

            Assert.That(
                ExpandedNodeId.TryParse(context, "svr=70000;i=42", null, out ExpandedNodeId value),
                Is.True);
            Assert.That(value.ServerIndex, Is.EqualTo(70000u));
        }

        [Test]
        public void AnUnparsableServerIndexIsReportedInsteadOfDropped()
        {
            // The context taking parse path ignored a failed uint.TryParse, so
            // TryParse reported success with ServerIndex zero and turned a
            // remote node id into a local one undetectably. The sibling ns=
            // case in NodeId already rejects the same shape.
            ServiceMessageContext context = CreateContext();

            Assert.Multiple(() =>
            {
                Assert.That(
                    ExpandedNodeId.TryParse(
                        context,
                        "svr=abc;i=42",
                        null,
                        out ExpandedNodeId _,
                        out NodeIdParseError text),
                    Is.False);
                Assert.That(text, Is.EqualTo(NodeIdParseError.InvalidServerIndex));

                Assert.That(
                    ExpandedNodeId.TryParse(
                        context,
                        "svr=99999999999;i=42",
                        null,
                        out ExpandedNodeId _,
                        out NodeIdParseError overflow),
                    Is.False);
                Assert.That(overflow, Is.EqualTo(NodeIdParseError.InvalidServerIndex));

                // a well formed index still parses.
                Assert.That(
                    ExpandedNodeId.TryParse(context, "svr=7;i=42", null, out ExpandedNodeId good),
                    Is.True);
                Assert.That(good.ServerIndex, Is.EqualTo(7u));
            });
        }

        [Test]
        public void ValidateRejectsAnEmptyDimensionInsteadOfThrowing()
        {
            // ",1:2" produced a null leading sub range whose Begin is -1, and
            // the NumericRange constructor then threw out of Validate.
            Assert.Multiple(() =>
            {
                ServiceResult result = NumericRange.Validate(",1:2", out NumericRange range);
                Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadIndexRangeInvalid));
                Assert.That(range.IsNull, Is.True);

                ServiceResult middle = NumericRange.Validate("1:2,,3:4", out NumericRange other);
                Assert.That(middle.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadIndexRangeInvalid));
                Assert.That(other.IsNull, Is.True);
            });
        }

        [Test]
        public void NodeIdCompareToIsAntisymmetricForNull()
        {
            NodeId nullId = NodeId.Null;
            var numericId = new NodeId(42u);

            Assert.Multiple(() =>
            {
                Assert.That(nullId.CompareTo(numericId), Is.LessThan(0));
                Assert.That(numericId.CompareTo(nullId), Is.GreaterThan(0));
            });
        }

        [Test]
        public void EmptyOpaqueIdentifierHashesLikeNull()
        {
            // The two argument constructor cached ByteString.Empty.GetHashCode()
            // while the one argument constructor and NodeId.Null cached zero.
            var withNamespace = new NodeId(ByteString.Empty, 0);
            var withoutNamespace = new NodeId(ByteString.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(withNamespace.IsNull, Is.True);
                Assert.That(withNamespace.GetHashCode(), Is.EqualTo(withoutNamespace.GetHashCode()));
                Assert.That(withNamespace.GetHashCode(), Is.EqualTo(NodeId.Null.GetHashCode()));
            });
        }

        [Test]
        public void MultiDimensionalNumericRangeRoundTripsThroughItsText()
        {
            // ToString() only wrote the first dimension, so a persisted index
            // range restored the wrong slice.
            ServiceResult result = NumericRange.Validate("1:2,3:4", out NumericRange range);
            Assert.That(ServiceResult.IsGood(result), Is.True);

            string text = range.ToString();

            Assert.Multiple(() =>
            {
                Assert.That(text, Is.EqualTo("1:2,3:4"));
                ServiceResult roundTrip = NumericRange.Validate(text, out NumericRange parsed);
                Assert.That(ServiceResult.IsGood(roundTrip), Is.True);
                Assert.That(parsed.SubRanges, Is.Not.Null);
                Assert.That(parsed.SubRanges, Has.Length.EqualTo(2));
            });
        }

        [Test]
        public void ResolvedNamespaceUriBecomesRelativeEvenForNamespaceZero()
        {
            // A nsu= that resolved to namespace 0 stayed absolute, unlike the
            // equivalent ns=0 form parsed by NodeId.Parse.
            ServiceMessageContext context = CreateContext();
            string namespaceZeroUri = context.NamespaceUris.GetString(0);

            Assert.That(
                ExpandedNodeId.TryParse(
                    context,
                    "nsu=" + namespaceZeroUri + ";i=42",
                    null,
                    out ExpandedNodeId value),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(value.IsAbsolute, Is.False);
                Assert.That(value.NamespaceUri, Is.Null);
                Assert.That(value.InnerNodeId, Is.EqualTo(new NodeId(42u)));
            });
        }

        [Test]
        public void DateTimeUtcFromStringTreatsTextAsUtc()
        {
            // From() used ParseExact without AssumeUniversal, so the value was
            // shifted by the machine's local offset.
            DateTimeUtc value = DateTimeUtc.From("2023-06-15 12:00:00");

            Assert.That(
                value.ToDateTime(),
                Is.EqualTo(new DateTime(2023, 6, 15, 12, 0, 0, DateTimeKind.Utc)));
        }

        [Test]
        public void MaxValueArithmeticSaturatesInsteadOfWrapping()
        {
            // Value reports long.MaxValue for the sentinel, so plain addition
            // overflowed into a negative file time that was clamped to MinValue.
            Assert.Multiple(() =>
            {
                Assert.That(
                    DateTimeUtc.MaxValue.Add(TimeSpan.FromSeconds(1)),
                    Is.EqualTo(DateTimeUtc.MaxValue));
                Assert.That(
                    DateTimeUtc.MaxValue.AddMilliseconds(1000),
                    Is.EqualTo(DateTimeUtc.MaxValue));
                Assert.That(
                    DateTimeUtc.MinValue.Subtract(TimeSpan.FromSeconds(1)),
                    Is.EqualTo(DateTimeUtc.MinValue));

                // Negating long.MinValue is not representable and yields itself
                // unchecked, so subtracting the most negative offset - which is
                // mathematically a very large addition - saturated to MinValue.
                Assert.That(
                    DateTimeUtc.MinValue.Subtract(TimeSpan.MinValue),
                    Is.EqualTo(DateTimeUtc.MaxValue));
                Assert.That(
                    DateTimeUtc.MinValue.SubtractMilliseconds(-1e30),
                    Is.EqualTo(DateTimeUtc.MaxValue));

                // A double to long cast saturates on .NET 5 and later but gives
                // long.MinValue for NaN and out of range values on .NET
                // Framework, so the conversion is clamped explicitly.
                Assert.That(
                    DateTimeUtc.MinValue.AddMilliseconds(double.NaN),
                    Is.EqualTo(DateTimeUtc.MinValue));
                Assert.That(
                    DateTimeUtc.MinValue.AddMilliseconds(1e30),
                    Is.EqualTo(DateTimeUtc.MaxValue));
            });
        }

        [Test]
        public void OperationLevelDiagnosticsCanCarryAdditionalInfo()
        {
            // UserPermissionAdditionalInfo (bit 31) was shifted down with the
            // operation bits, so the test for it at bit 31 never matched.
            var result = new ServiceResult(
                "http://test.org/ns",
                StatusCodes.Bad,
                new LocalizedText("en", "Error"),
                "debug info",
                (ServiceResult)null);

            var stringTable = new StringTable();
            const DiagnosticsMasks mask =
                DiagnosticsMasks.OperationAdditionalInfo |
                DiagnosticsMasks.UserPermissionAdditionalInfo;

            var di = new DiagnosticInfo(result, mask, false, stringTable, s_logger);

            Assert.That(di.AdditionalInfo, Is.EqualTo("debug info"));
        }

        [Test]
        public void QualifiedNameFormatGuardsNamesContainingAColon()
        {
            // Format() omitted the "0:" guard that ToString() writes, so the
            // text parsed back into namespace 1.
            ServiceMessageContext context = CreateContext();
            var name = new QualifiedName("1:Tag", 0);

            string text = name.Format(context);

            Assert.Multiple(() =>
            {
                Assert.That(text, Is.EqualTo("0:1:Tag"));
                Assert.That(QualifiedName.Parse(text), Is.EqualTo(name));
            });
        }

        [Test]
        public void AbsoluteExpandedNodeIdWithNullInnerIdRoundTrips()
        {
            // The formatter wrote "nsu=<uri>;i=" with no identifier at all.
            var value = new ExpandedNodeId(NodeId.Null, "http://test.org/ns", 0);

            string text = value.Format(null);

            Assert.Multiple(() =>
            {
                Assert.That(text, Is.EqualTo("nsu=http://test.org/ns;i=0"));
                Assert.That(ExpandedNodeId.TryParse(text, out ExpandedNodeId parsed), Is.True);
                Assert.That(parsed.NamespaceUri, Is.EqualTo("http://test.org/ns"));
                Assert.That(parsed.InnerNodeId.IsNull, Is.True);
            });
        }

        [Test]
        public void ExpandedNodeIdEqualsAcceptsABoxedNodeId()
        {
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(nodeId);

            Assert.Multiple(() =>
            {
                Assert.That(expanded, Is.EqualTo((object)nodeId));
                Assert.That(expanded.GetHashCode(), Is.EqualTo(nodeId.GetHashCode()));
            });
        }

        [Test]
        public void ExpandedNodeIdCompareToForeignTypeIsNotEqual()
        {
            // A null ExpandedNodeId reported 0 ("equal") for any foreign type.
            ExpandedNodeId nullId = ExpandedNodeId.Null;

            Assert.That(nullId.CompareTo("not an id"), Is.Not.Zero);
        }

        [Test]
        public void NullRangeLeavesTypedArraysUntouched()
        {
            // SliceArrayOf returned Good for a null range, but the caller then
            // fell into the "not two dimensional" branch and nulled the value.
            NumericRange range = NumericRange.Null;

            ArrayOf<string> strings = s_twoStrings.ToArrayOf();
            ArrayOf<ByteString> byteStrings = new[]
            {
                new ByteString(new byte[] { 1 }),
                new ByteString(new byte[] { 2 })
            }.ToArrayOf();

            ArrayOf<int> ints = s_oneTwoThree.ToArrayOf();

            StatusCode stringStatus = range.ApplyRange(ref strings);
            StatusCode byteStringStatus = range.ApplyRange(ref byteStrings);

            // The generic overloads tested the dimension count first, so a null
            // range (Dimensions == 0) was rejected before SliceArrayOf could
            // report Good like every sibling overload does.
            StatusCode intStatus = range.ApplyRange(ref ints);
            StatusCode intUpdateStatus = range.UpdateRange(ref ints, s_fourFive.ToArrayOf());

            Assert.Multiple(() =>
            {
                Assert.That(stringStatus, Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(strings.ToArray(), Is.EqualTo(s_twoStrings));
                Assert.That(byteStringStatus, Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(byteStrings.Count, Is.EqualTo(2));
                Assert.That(intStatus, Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(intUpdateStatus, Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(ints.ToArray(), Is.EqualTo(s_oneTwoThree));
            });
        }

        [Test]
        public void UnparsableNamespaceIndexIsRejected()
        {
            // An out of range or non numeric ns= was silently ignored, landing
            // the node id in namespace zero.
            ServiceMessageContext context = CreateContext();

            Assert.Multiple(() =>
            {
                Assert.That(NodeId.TryParse(context, "ns=70000;i=42", out _), Is.False);
                Assert.That(NodeId.TryParse(context, "ns=abc;i=42", out _), Is.False);
            });
        }

        [Test]
        public void MalformedXmlElementsAreNotAllEqual()
        {
            // Both sides failed to parse, so DeepEquals(null, null) reported
            // equality while the hashes differed.
            var first = XmlElement.From("<not well formed");
            var second = XmlElement.From("<also not well formed");

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.EqualTo(second));
                Assert.That(first, Is.EqualTo(XmlElement.From("<not well formed")));
            });
        }
    }
}
