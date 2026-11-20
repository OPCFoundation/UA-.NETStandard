// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    /// <summary>
    /// Unit tests for the hand-written
    /// <see cref="ConditionTypeRecord.ConditionId"/> extension on the
    /// source-generated condition record.
    /// </summary>
    [TestFixture]
    [Category("Core")]
    [Category("Alarms")]
    [Parallelizable]
    public sealed class EventRecordExtensionsTests
    {
        [Test]
        public void ConditionIdReturnsConditionNodeIdForConditionRecord()
        {
            var sourceNode = new NodeId(42u);
            var conditionNode = new NodeId(99u);
            var record = new ConditionTypeRecord
            {
                SourceNode = sourceNode,
                ConditionId = conditionNode
            };

            Assert.That(record.ConditionId, Is.EqualTo(conditionNode));
            Assert.That(record.ConditionId.IsNull, Is.False);
            Assert.That(record.SourceNode, Is.EqualTo(sourceNode));
        }

        [Test]
        public void ConditionIdIsNullWhenConditionNodeIdIsNull()
        {
            var record = new ConditionTypeRecord
            {
                SourceNode = new NodeId(42u),
                ConditionId = NodeId.Null
            };

            Assert.That(record.ConditionId.IsNull, Is.True);
        }

        [Test]
        public void ConditionEventFilterSelectsConditionNodeIdAttribute()
        {
            EventFilter filter = ConditionTypeRecord.EventFilters.Build();

            Assert.That(
                filter.SelectClauses.ToArray()!.Any(static clause =>
                    clause.TypeDefinitionId == ObjectTypeIds.ConditionType &&
                    clause.AttributeId == Attributes.NodeId &&
                    clause.BrowsePath.IsEmpty),
                Is.True);
        }

        [Test]
        public void ConditionDecoderReadsConditionNodeIdAttribute()
        {
            QualifiedName[][] fields = ConditionTypeRecord.Decoder.StandardFields;
            Variant[] values = [.. Enumerable.Repeat(default(Variant), fields.Length)];
            int conditionIdIndex = fields
                .Select((path, index) => (path, index))
                .Single(value => value.path.Length == 0)
                .index;
            NodeId conditionId = new(99u);
            values[conditionIdIndex] = conditionId;

            ConditionTypeRecord record = ConditionTypeRecord.Decoder.Decode(values)!;

            Assert.That(record.ConditionId, Is.EqualTo(conditionId));
        }
    }
}
