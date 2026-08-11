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

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests for the handwritten SelectionListState write validation which
    /// rejects values that are not contained in the node's Selections property.
    /// </summary>
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class SelectionListStateTests
    {
        private static readonly string[] s_colors = ["Red", "Green", "Blue"];
        private static readonly Variant[] s_colorVariants =
            [Variant.From("Red"), Variant.From("Green"), Variant.From("Blue")];

        private SystemContext m_context;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory,
                TypeTable = new TypeTable(messageContext.NamespaceUris)
            };
        }

        [Test]
        public void WriteMemberOfStringSelectionsSucceeds()
        {
            SelectionListState node = CreateSelectionList(
                Variant.From(s_colors.ToArrayOf()));

            ServiceResult result = WriteValue(node, "Green");

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(node.WrappedValue.TryGetValue(out string written), Is.True);
            Assert.That(written, Is.EqualTo("Green"));
        }

        [Test]
        public void WriteNonMemberOfStringSelectionsReturnsBadOutOfRange()
        {
            SelectionListState node = CreateSelectionList(
                Variant.From(s_colors.ToArrayOf()));

            ServiceResult result = WriteValue(node, "Purple");

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public void WriteWithoutSelectionsChildFallsThroughToBase()
        {
            var node = new SelectionListState(null)
            {
                NodeId = new NodeId(1000),
                BrowseName = new QualifiedName("Colors"),
                DataType = DataTypeIds.String,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                Value = Variant.From("Red")
            };

            ServiceResult result = WriteValue(node, "AnythingGoes");

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(node.WrappedValue.TryGetValue(out string written), Is.True);
            Assert.That(written, Is.EqualTo("AnythingGoes"));
        }

        [Test]
        public void WriteWithEmptyStringSelectionsAppliesNoRestriction()
        {
            SelectionListState node = CreateSelectionList(
                Variant.From(ArrayOf<string>.Empty));

            ServiceResult result = WriteValue(node, "AnythingGoes");

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void WriteNonStringValueWhenStringSelectionsFallsThroughToBaseTypeCheck()
        {
            SelectionListState node = CreateSelectionList(
                Variant.From(s_colors.ToArrayOf()));

            ServiceResult result = node.WriteAttribute(
                m_context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(Variant.From(42)));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void WriteMemberOfVariantSelectionsSucceeds()
        {
            SelectionListState node = CreateSelectionList(
                Variant.From(s_colorVariants.ToArrayOf()));

            ServiceResult result = WriteValue(node, "Blue");

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void WriteNonMemberOfVariantSelectionsReturnsBadOutOfRange()
        {
            SelectionListState node = CreateSelectionList(
                Variant.From(s_colorVariants.ToArrayOf()));

            ServiceResult result = WriteValue(node, "Purple");

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public void WriteMemberButNotWritableStillReturnsBadNotWritable()
        {
            SelectionListState node = CreateSelectionList(
                Variant.From(s_colors.ToArrayOf()));
            node.AccessLevel = AccessLevels.CurrentRead;
            node.UserAccessLevel = AccessLevels.CurrentRead;

            ServiceResult result = WriteValue(node, "Green");

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotWritable));
        }

        private SelectionListState CreateSelectionList(Variant selectionsValue)
        {
            var node = new SelectionListState(null)
            {
                NodeId = new NodeId(1000),
                BrowseName = new QualifiedName("Colors"),
                DataType = DataTypeIds.String,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                Value = Variant.From("Red")
            };

            var selections = new BaseDataVariableState(node)
            {
                NodeId = new NodeId(1001),
                BrowseName = new QualifiedName(BrowseNames.Selections),
                DataType = DataTypeIds.String,
                ValueRank = ValueRanks.OneDimension,
                Value = selectionsValue
            };

            node.AddChild(selections);
            return node;
        }

        private ServiceResult WriteValue(SelectionListState node, string value)
        {
            return node.WriteAttribute(
                m_context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(Variant.From(value)));
        }
    }
}
