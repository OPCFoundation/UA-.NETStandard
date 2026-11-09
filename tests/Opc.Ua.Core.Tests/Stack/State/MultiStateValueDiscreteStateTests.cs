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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests for the handwritten MultiStateValueDiscreteState write validation which
    /// rejects numeric writes that do not match an EnumValues entry and refreshes the
    /// ValueAsText property on a successful scalar write.
    /// </summary>
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public partial class MultiStateValueDiscreteStateTests
    {
        // Deliberately sparse / non-zero-based to exercise the EnumValues lookup.
        private static readonly (long Value, string Name)[] s_states =
            [(1, "one"), (2, "two"), (4, "four"), (8, "eight")];

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
        public void WriteMatchingSparseValueSucceeds()
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);

            ServiceResult result = WriteValue(node, Variant.From((uint)4));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(node.WrappedValue.TryGetValue(out uint written), Is.True);
            Assert.That(written, Is.EqualTo(4u));
        }

        [Test]
        public void WriteMatchingValueUpdatesValueAsText()
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);

            ServiceResult result = WriteValue(node, Variant.From((uint)8));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(node.ValueAsText, Is.Not.Null);
            Assert.That(node.ValueAsText.WrappedValue.TryGetValue(out LocalizedText text), Is.True);
            Assert.That(text.Text, Is.EqualTo("eight"));
        }

        [Test]
        public void WriteValueBetweenGapsReturnsBadOutOfRange()
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);

            ServiceResult result = WriteValue(node, Variant.From((uint)3));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public void WriteValueBetweenGapsLeavesValueAsTextUnchanged()
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);

            _ = WriteValue(node, Variant.From((uint)3));

            Assert.That(node.ValueAsText, Is.Not.Null);
            Assert.That(node.ValueAsText.WrappedValue.TryGetValue(out LocalizedText text), Is.True);
            Assert.That(text.Text, Is.EqualTo("initial"));
        }

        [Test]
        public void WriteWithEmptyEnumValuesRejectsEveryValue()
        {
            MultiStateValueDiscreteState node = CreateNode([]);

            ServiceResult result = WriteValue(node, Variant.From((uint)1));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public void WriteWithoutEnumValuesChildFallsThroughToBase()
        {
            MultiStateValueDiscreteState node = CreateNode(null!);

            ServiceResult result = WriteValue(node, Variant.From((uint)99));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(node.WrappedValue.TryGetValue(out uint written), Is.True);
            Assert.That(written, Is.EqualTo(99u));
        }

        [Test]
        public void WriteMatchingButNotWritableReturnsBadNotWritable()
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);
            node.AccessLevel = AccessLevels.CurrentRead;
            node.UserAccessLevel = AccessLevels.CurrentRead;

            ServiceResult result = WriteValue(node, Variant.From((uint)4));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotWritable));
        }

        /// <summary>
        /// Updates dependent enum text after an asynchronous handler commits an ordinary cache write.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AsyncEnumWriteUpdatesValueAsTextAfterOrdinaryCommit(bool fullHandler)
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);
            var accepted = new AttributeWriteResult(ServiceResult.Good);
            if (fullHandler)
            {
                node.OnWriteValueAsync = (_, _, _, _, _) => new ValueTask<AttributeWriteResult>(accepted);
            }
            else
            {
                node.OnSimpleWriteValueAsync = (_, _, _, _) => new ValueTask<AttributeWriteResult>(accepted);
            }

            ServiceResult result = await node.WriteAttributeAsync(
                m_context, Attributes.Value, NumericRange.Null, new DataValue(Variant.From(4u)))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(node.WrappedValue, Is.EqualTo(Variant.From(4u)));
                Assert.That(node.ValueAsText!.Value.Text, Is.EqualTo("four"));
            });
        }

        /// <summary>
        /// Preserves a handler-owned value and text publication without applying the requested write again.
        /// </summary>
        [Test]
        public async Task AsyncEnumSuppressedWritePreservesHandlerOwnedPublication()
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);
            node.OnWriteValueAsync = (_, _, _, _, _) =>
            {
                node.Value = Variant.From(8u);
                node.ValueAsText!.Value = LocalizedText.From("eight");
                return new ValueTask<AttributeWriteResult>(
                    new AttributeWriteResult(ServiceResult.Good) { SuppressCacheUpdate = true });
            };

            ServiceResult result = await node.WriteAttributeAsync(
                m_context, Attributes.Value, NumericRange.Null, new DataValue(Variant.From(4u)))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(node.WrappedValue, Is.EqualTo(Variant.From(8u)));
                Assert.That(node.ValueAsText!.Value.Text, Is.EqualTo("eight"));
            });
        }

        /// <summary>
        /// Dispatches non-Value attributes without invoking the asynchronous Value handler.
        /// </summary>
        [Test]
        public async Task AsyncNonValueWriteDoesNotInvokeValueHandler()
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);
            int calls = 0;
            node.WriteMask = AttributeWriteMask.DisplayName;
            node.OnWriteValueAsync = (_, _, _, _, _) =>
            {
                calls++;
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
            };

            ServiceResult result = await node.WriteAttributeAsync(
                m_context,
                Attributes.DisplayName,
                NumericRange.Null,
                new DataValue(Variant.From(LocalizedText.From("updated")))).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(node.DisplayName.Text, Is.EqualTo("updated"));
                Assert.That(calls, Is.Zero);
            });
        }

        /// <summary>
        /// The late simple hook replaces its predecessor while native enum validation and text publication remain active.
        /// </summary>
        [TestCase(1u, "one", true)]
        [TestCase(2u, "two", true)]
        [TestCase(4u, "four", true)]
        [TestCase(8u, "eight", true)]
        [TestCase(0u, "", false)]
        [TestCase(3u, "", false)]
        [TestCase(9u, "", false)]
        public async Task SimpleAsyncEnumWriteValidatesSparseValuesBeforeApplicationHookAsync(
            uint value,
            string expectedText,
            bool valid)
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 1);
            var requestedTimestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 2);
            node.AccessLevel |= AccessLevels.TimestampWrite;
            node.UserAccessLevel |= AccessLevels.TimestampWrite;
            node.StatusCode = StatusCodes.Uncertain;
            node.Timestamp = timestamp;

            int displacedCalls = 0;
            node.OnSimpleWriteValueAsync = (_, _, _, _) =>
            {
                displacedCalls++;
                return new ValueTask<AttributeWriteResult>(
                    new AttributeWriteResult(StatusCodes.BadUnexpectedError));
            };

            int applicationCalls = 0;
            NodeValueSimpleWriteEventHandlerAsync lateHook = (context, state, requestedValue, _) =>
            {
                applicationCalls++;
                Assert.That(context, Is.SameAs(m_context));
                Assert.That(state, Is.SameAs(node));
                Assert.That(requestedValue, Is.EqualTo(Variant.From(value)));
                return new ValueTask<AttributeWriteResult>(
                    new AttributeWriteResult(StatusCodes.GoodClamped));
            };
            node.OnSimpleWriteValueAsync = lateHook;
            await node.ClearChangeMasksAsync(m_context, false).ConfigureAwait(false);

            int changeNotifications = 0;
            NodeStateChangeMasks notifiedChangeMasks = NodeStateChangeMasks.None;
            node.OnStateChangedAsync = (_, changedNode, changeMasks, _) =>
            {
                if (ReferenceEquals(changedNode, node))
                {
                    changeNotifications++;
                    notifiedChangeMasks = changeMasks;
                }
                return default;
            };

            ServiceResult result = await node.WriteAttributeAsync(
                m_context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(Variant.From(value), StatusCodes.Good, requestedTimestamp)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(node.OnSimpleWriteValueAsync, Is.SameAs(lateHook));
                Assert.That(displacedCalls, Is.Zero);
                Assert.That(applicationCalls, Is.EqualTo(valid ? 1 : 0));
                Assert.That(result.StatusCode,
                    Is.EqualTo(valid ? StatusCodes.GoodClamped : StatusCodes.BadOutOfRange));
                if (valid)
                {
                    Assert.That(node.WrappedValue, Is.EqualTo(Variant.From(value)));
                    Assert.That(node.ValueAsText!.Value.Text, Is.EqualTo(expectedText));
                    Assert.That(node.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(node.Timestamp, Is.EqualTo(requestedTimestamp));
                    Assert.That(changeNotifications, Is.EqualTo(1));
                    Assert.That(notifiedChangeMasks & NodeStateChangeMasks.Value,
                        Is.EqualTo(NodeStateChangeMasks.Value));
                    Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
                }
                else
                {
                    Assert.That(changeNotifications, Is.Zero);
                    Assert.That(node.WrappedValue, Is.EqualTo(Variant.From(0u)));
                    Assert.That(node.ValueAsText!.Value.Text, Is.EqualTo("initial"));
                    Assert.That(node.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
                    Assert.That(node.Timestamp, Is.EqualTo(timestamp));
                    Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
                }
            });
        }

        /// <summary>
        /// A bad result from the replacement simple hook does not commit the enum or dependent text.
        /// </summary>
        [Test]
        public async Task LateSimpleAsyncEnumBadResultLeavesValueAndTextUnchangedAsync()
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 1);
            node.AccessLevel |= AccessLevels.TimestampWrite;
            node.UserAccessLevel |= AccessLevels.TimestampWrite;
            node.StatusCode = StatusCodes.Uncertain;
            node.Timestamp = timestamp;

            int displacedCalls = 0;
            node.OnSimpleWriteValueAsync = (_, _, _, _) =>
            {
                displacedCalls++;
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(StatusCodes.Good));
            };

            int applicationCalls = 0;
            NodeValueSimpleWriteEventHandlerAsync lateHook = (_, _, _, _) =>
            {
                applicationCalls++;
                return new ValueTask<AttributeWriteResult>(
                    new AttributeWriteResult(StatusCodes.BadInvalidState));
            };
            node.OnSimpleWriteValueAsync = lateHook;
            await node.ClearChangeMasksAsync(m_context, false).ConfigureAwait(false);

            ServiceResult result = await node.WriteAttributeAsync(
                m_context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(Variant.From(4u), StatusCodes.Good, new DateTimeUtc(2026, 1, 1, 0, 0, 2)))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(node.OnSimpleWriteValueAsync, Is.SameAs(lateHook));
                Assert.That(displacedCalls, Is.Zero);
                Assert.That(applicationCalls, Is.EqualTo(1));
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(node.WrappedValue, Is.EqualTo(Variant.From(0u)));
                Assert.That(node.ValueAsText!.Value.Text, Is.EqualTo("initial"));
                Assert.That(node.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
                Assert.That(node.Timestamp, Is.EqualTo(timestamp));
                Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            });
        }

        private MultiStateValueDiscreteState CreateNode((long Value, string Name)[] states)
        {
            var node = new MultiStateValueDiscreteState(null)
            {
                NodeId = new NodeId(1000),
                BrowseName = new QualifiedName("States"),
                DataType = DataTypeIds.UInt32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                Value = Variant.From((uint)0)
            };

            if (states != null)
            {
                var values = new EnumValueType[states.Length];
                for (int ii = 0; ii < states.Length; ii++)
                {
                    LocalizedText text = LocalizedText.From(states[ii].Name);
                    values[ii] = new EnumValueType
                    {
                        Value = states[ii].Value,
                        DisplayName = text,
                        Description = text
                    };
                }

                node.EnumValues = PropertyState<ArrayOf<EnumValueType>>
                    .With<StructureBuilder<EnumValueType>>(node, values.ToArrayOf());
                node.EnumValues.NodeId = new NodeId(1001);
                node.EnumValues.BrowseName = new QualifiedName(BrowseNames.EnumValues);
                node.EnumValues.DataType = DataTypeIds.EnumValueType;
                node.EnumValues.ValueRank = ValueRanks.OneDimension;
            }

            node.ValueAsText = PropertyState<LocalizedText>.With<VariantBuilder>(
                node, LocalizedText.From("initial"));
            node.ValueAsText.NodeId = new NodeId(1002);
            node.ValueAsText.BrowseName = new QualifiedName(BrowseNames.ValueAsText);
            node.ValueAsText.DataType = DataTypeIds.LocalizedText;
            node.ValueAsText.ValueRank = ValueRanks.Scalar;

            return node;
        }

        private ServiceResult WriteValue(MultiStateValueDiscreteState node, Variant value)
        {
            return node.WriteAttribute(
                m_context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(value));
        }
    }
}
