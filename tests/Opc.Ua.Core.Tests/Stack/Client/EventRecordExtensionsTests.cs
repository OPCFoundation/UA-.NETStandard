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
                filter.SelectClauses.ToArray().Any(static clause =>
                    clause.TypeDefinitionId == ObjectTypeIds.ConditionType &&
                    clause.AttributeId == Attributes.NodeId &&
                    clause.BrowsePath.IsEmpty),
                Is.True);
        }

        [Test]
        public void ConditionDecoderReadsConditionNodeIdAttribute()
        {
            QualifiedName[][] fields = ConditionTypeRecord.Decoder.StandardFields;
            var values = Enumerable.Repeat(default(Variant), fields.Length).ToArray();
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
