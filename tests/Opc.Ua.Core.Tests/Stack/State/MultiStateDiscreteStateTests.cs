// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests for the handwritten MultiStateDiscreteState write validation which rejects
    /// numeric writes that fall outside the range of the EnumStrings lookup table.
    /// </summary>
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class MultiStateDiscreteStateTests
    {
        private static readonly string[] s_states = ["open", "closed", "jammed"];

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
        public void WriteInRangeValueSucceeds()
        {
            MultiStateDiscreteState node = CreateNode(s_states);

            ServiceResult result = WriteValue(node, Variant.From((uint)2));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(node.WrappedValue.TryGetValue(out uint written), Is.True);
            Assert.That(written, Is.EqualTo(2u));
        }

        [Test]
        public void WriteValueEqualToLengthReturnsBadOutOfRange()
        {
            MultiStateDiscreteState node = CreateNode(s_states);

            ServiceResult result = WriteValue(node, Variant.From((uint)3));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public void WriteValueAboveRangeReturnsBadOutOfRange()
        {
            MultiStateDiscreteState node = CreateNode(s_states);

            ServiceResult result = WriteValue(node, Variant.From((uint)99));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public void WriteWithEmptyEnumStringsRejectsEveryValue()
        {
            MultiStateDiscreteState node = CreateNode([]);

            ServiceResult result = WriteValue(node, Variant.From((uint)0));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public void WriteWithoutEnumStringsChildFallsThroughToBase()
        {
            MultiStateDiscreteState node = CreateNode(null!);

            ServiceResult result = WriteValue(node, Variant.From((uint)42));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(node.WrappedValue.TryGetValue(out uint written), Is.True);
            Assert.That(written, Is.EqualTo(42u));
        }

        [Test]
        public void WriteMemberButNotWritableReturnsBadNotWritable()
        {
            MultiStateDiscreteState node = CreateNode(s_states);
            node.AccessLevel = AccessLevels.CurrentRead;
            node.UserAccessLevel = AccessLevels.CurrentRead;

            ServiceResult result = WriteValue(node, Variant.From((uint)1));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotWritable));
        }

        private MultiStateDiscreteState CreateNode(string[] enumStrings)
        {
            var node = new MultiStateDiscreteState(null)
            {
                NodeId = new NodeId(1000),
                BrowseName = new QualifiedName("States"),
                DataType = DataTypeIds.UInt32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                Value = Variant.From((uint)0)
            };

            if (enumStrings != null)
            {
                var entries = new LocalizedText[enumStrings.Length];
                for (int ii = 0; ii < enumStrings.Length; ii++)
                {
                    entries[ii] = LocalizedText.From(enumStrings[ii]);
                }

                node.EnumStrings = PropertyState<ArrayOf<LocalizedText>>.With<VariantBuilder>(
                    node, entries.ToArrayOf());
                node.EnumStrings.NodeId = new NodeId(1001);
                node.EnumStrings.BrowseName = new QualifiedName(BrowseNames.EnumStrings);
                node.EnumStrings.DataType = DataTypeIds.LocalizedText;
                node.EnumStrings.ValueRank = ValueRanks.OneDimension;
            }

            return node;
        }

        private ServiceResult WriteValue(MultiStateDiscreteState node, Variant value)
        {
            return node.WriteAttribute(
                m_context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(value));
        }
    }
}
