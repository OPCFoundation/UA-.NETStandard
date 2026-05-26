/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
 * ======================================================================*/
using NUnit.Framework;
using Opc.Ua.Client.Alarms;

namespace Opc.Ua.Client.Tests.Alarms
{
    [TestFixture, Category("Alarms"), Parallelizable]
    public class AlarmEventFilterBuilderTests
    {
        [Test]
        public void BuildIncludesAllStandardSelectClauses()
        {
            EventFilter filter = new AlarmEventFilterBuilder()
                .ForAlarms()
                .Build();

            Assert.That(filter, Is.Not.Null);
            Assert.That(filter.SelectClauses.Count, Is.EqualTo(AlarmEventDecoder.StandardFields.Length));
        }

        [Test]
        public void BuildWithoutOfTypeHasEmptyWhereClause()
        {
            EventFilter filter = new AlarmEventFilterBuilder().Build();

            Assert.That(filter, Is.Not.Null);
            Assert.That(filter.WhereClause, Is.Not.Null);
            Assert.That(filter.WhereClause.Elements.Count, Is.Zero);
        }

        [Test]
        public void ForAlarmsSetsAlarmConditionTypeFilter()
        {
            EventFilter filter = new AlarmEventFilterBuilder()
                .ForAlarms()
                .Build();

            Assert.That(filter.WhereClause.Elements.Count, Is.EqualTo(1));
            Assert.That(filter.WhereClause.Elements[0].FilterOperator,
                Is.EqualTo(FilterOperator.OfType));
        }

        [Test]
        public void OfTypeWithNullNodeIdStillProducesWhereClauseEntry()
        {
            // The builder stores any non-null NodeId reference (including
            // the NodeId.Null sentinel). The Build()-time `m_eventType !=
            // null` check is a reference-null check that NodeId.Null
            // passes, so a where clause element is produced with the
            // NodeId.Null operand.
            EventFilter filter = new AlarmEventFilterBuilder()
                .OfType(NodeId.Null)
                .Build();

            Assert.That(filter.WhereClause.Elements, Has.Count.EqualTo(1));
            NodeId operandValue = GetOfTypeOperandNodeId(filter);
            Assert.That(operandValue.IsNull, Is.True);
        }

        [Test]
        public void ForConditionsSetsConditionTypeInWhereClause()
        {
            EventFilter filter = new AlarmEventFilterBuilder()
                .ForConditions()
                .Build();

            NodeId operandValue = GetOfTypeOperandNodeId(filter);
            Assert.That(operandValue, Is.EqualTo(ObjectTypeIds.ConditionType));
        }

        [Test]
        public void ForDialogsSetsDialogConditionTypeInWhereClause()
        {
            EventFilter filter = new AlarmEventFilterBuilder()
                .ForDialogs()
                .Build();

            NodeId operandValue = GetOfTypeOperandNodeId(filter);
            Assert.That(operandValue, Is.EqualTo(ObjectTypeIds.DialogConditionType));
        }

        [Test]
        public void OfTypeCalledMultipleTimesLastValueWins()
        {
            EventFilter filter = new AlarmEventFilterBuilder()
                .OfType(ObjectTypeIds.ConditionType)
                .OfType(ObjectTypeIds.DialogConditionType)
                .OfType(ObjectTypeIds.AlarmConditionType)
                .Build();

            Assert.That(filter.WhereClause.Elements.Count, Is.EqualTo(1));
            NodeId operandValue = GetOfTypeOperandNodeId(filter);
            Assert.That(operandValue, Is.EqualTo(ObjectTypeIds.AlarmConditionType));
        }

        [Test]
        public void EverySelectClauseTargetsValueAttributeOnBaseEventType()
        {
            EventFilter filter = new AlarmEventFilterBuilder().Build();

            Assert.That(filter.SelectClauses.Count,
                Is.EqualTo(AlarmEventDecoder.StandardFields.Length));
            for (int i = 0; i < filter.SelectClauses.Count; i++)
            {
                SimpleAttributeOperand clause = filter.SelectClauses[i];
                Assert.That(clause.AttributeId, Is.EqualTo(Attributes.Value),
                    $"clause[{i}] AttributeId");
                Assert.That(clause.TypeDefinitionId, Is.EqualTo(ObjectTypeIds.BaseEventType),
                    $"clause[{i}] TypeDefinitionId");
            }
        }

        [Test]
        public void SelectClauseBrowsePathsMatchAlarmEventDecoderStandardFields()
        {
            EventFilter filter = new AlarmEventFilterBuilder().Build();

            for (int i = 0; i < AlarmEventDecoder.StandardFields.Length; i++)
            {
                QualifiedName[] expected = AlarmEventDecoder.StandardFields[i];
                SimpleAttributeOperand clause = filter.SelectClauses[i];

                Assert.That(clause.BrowsePath.Count, Is.EqualTo(expected.Length),
                    $"clause[{i}] BrowsePath length");
                for (int j = 0; j < expected.Length; j++)
                {
                    Assert.That(clause.BrowsePath[j], Is.EqualTo(expected[j]),
                        $"clause[{i}].BrowsePath[{j}]");
                }
            }
        }

        private static NodeId GetOfTypeOperandNodeId(EventFilter filter)
        {
            Assert.That(filter.WhereClause.Elements.Count, Is.EqualTo(1));
            ContentFilterElement element = filter.WhereClause.Elements[0];
            Assert.That(element.FilterOperator, Is.EqualTo(FilterOperator.OfType));
            Assert.That(element.FilterOperands.Count, Is.EqualTo(1));
            ExtensionObject operandObject = element.FilterOperands[0];
            Assert.That(operandObject.TryGetValue(out LiteralOperand? literal), Is.True);
            Assert.That(literal, Is.Not.Null);
            Assert.That(literal!.Value.TryGetValue(out NodeId nodeId), Is.True);
            return nodeId;
        }
    }
}
