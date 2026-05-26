/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
 * ======================================================================*/
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
        public void ConditionIdReturnsSourceNodeForConditionRecord()
        {
            var sourceNode = new NodeId(42u);
            var record = new ConditionTypeRecord { SourceNode = sourceNode };

            Assert.That(record.ConditionId, Is.EqualTo(sourceNode));
            Assert.That(record.ConditionId.IsNull, Is.False);
        }

        [Test]
        public void ConditionIdIsNullWhenSourceNodeIsNull()
        {
            var record = new ConditionTypeRecord { SourceNode = NodeId.Null };

            Assert.That(record.ConditionId.IsNull, Is.True);
        }
    }
}
