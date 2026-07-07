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
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;

#nullable enable

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Gap-coverage tests for <see cref="HistorianEventFilterTarget"/> paths
    /// not exercised by <see cref="HistorianEventFilterTargetTests"/>:
    /// null typeDefinitionId, null TypeTree fallback, empty browse-path
    /// attribute resolution, and multi-segment browse paths.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianEventFilterTargetGapsTests
    {
        private static readonly DateTime BaseTime =
            new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // ─── IsTypeOf ───────────────────────────────────────────────────────

        [Test]
        public void IsTypeOfReturnsTrueWhenTypeDefinitionIdIsNull()
        {
            var record = MakeRecord(ObjectTypeIds.BaseEventType);
            var target = new HistorianEventFilterTarget(record);

            // NodeId.Null branch (line 67-69): always returns true.
            Assert.That(target.IsTypeOf(null!, NodeId.Null), Is.True);
        }

        [Test]
        public void IsTypeOfReturnsFalseWhenContextTypeTreeIsNullAndNotExactMatch()
        {
            // AuditEventType does NOT equal BaseEventType, and the context
            // either has no TypeTree or has TypeTree == null, so the
            // degraded path (lines 80-92) fires and returns false.
            var record = MakeRecord(ObjectTypeIds.AuditEventType);
            var target = new HistorianEventFilterTarget(record);

            // Build a context whose TypeTree is null so the fallback fires.
            var mockCtx = new Mock<IFilterContext>();
            mockCtx.Setup(c => c.TypeTree).Returns((ITypeTable)null!);

            // May or may not emit the one-shot warning depending on process
            // order; what matters is the return value.
            bool result = target.IsTypeOf(mockCtx.Object, ObjectTypeIds.BaseEventType);
            Assert.That(result, Is.False);
        }

        // ─── GetAttributeValue – empty browse path ──────────────────────────

        [Test]
        public void GetAttributeValueReturnsEventTypeForEmptyPathAndNodeIdAttribute()
        {
            NodeId eventType = ObjectTypeIds.AuditEventType;
            var record = MakeRecord(eventType);
            var target = new HistorianEventFilterTarget(record);

            // relativePath.Count == 0 && attributeId == Attributes.NodeId
            // → returns new Variant(m_record.EventType)  (lines 107-112)
            Variant result = target.GetAttributeValue(
                null!, NodeId.Null, [], Attributes.NodeId, NumericRange.Null);

            Assert.That(result.TryGetValue(out NodeId resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(eventType));
        }

        [Test]
        public void GetAttributeValueReturnsDefaultForEmptyPathAndNonNodeIdAttribute()
        {
            var record = MakeRecord(ObjectTypeIds.BaseEventType);
            var target = new HistorianEventFilterTarget(record);

            // relativePath.Count == 0 && attributeId != NodeId → default (line 113)
            Variant result = target.GetAttributeValue(
                null!, NodeId.Null, [], Attributes.Value, NumericRange.Null);

            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        // ─── GetAttributeValue – multi-segment browse path ──────────────────

        [Test]
        public void GetAttributeValueResolvesMultiSegmentBrowsePathKey()
        {
            // Multi-segment path triggers the StringBuilder branch (lines 126-135).
            var record = new HistorianEventRecord(
                ByteString.Empty,
                ObjectTypeIds.BaseEventType,
                (DateTimeUtc)BaseTime,
                new Dictionary<string, Variant>(StringComparer.Ordinal)
                {
                    ["Root/Child"] = new Variant("found-it")
                });

            var target = new HistorianEventFilterTarget(record);
            ArrayOf<QualifiedName> path = new QualifiedName[] { new("Root"), new("Child") };

            Variant result = target.GetAttributeValue(
                null!, NodeId.Null, path, Attributes.Value, NumericRange.Null);

            Assert.That(result.TryGetValue(out string? val), Is.True);
            Assert.That(val, Is.EqualTo("found-it"));
        }

        [Test]
        public void GetAttributeValueReturnsDefaultForUnresolvedMultiSegmentPath()
        {
            var record = MakeRecord(ObjectTypeIds.BaseEventType);
            var target = new HistorianEventFilterTarget(record);
            ArrayOf<QualifiedName> path = new QualifiedName[] { new("A"), new("B"), new("C") };

            Variant result = target.GetAttributeValue(
                null!, NodeId.Null, path, Attributes.Value, NumericRange.Null);

            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private static HistorianEventRecord MakeRecord(NodeId eventType)
        {
            return new HistorianEventRecord(
                ByteString.Empty,
                eventType,
                (DateTimeUtc)BaseTime,
                new Dictionary<string, Variant>(StringComparer.Ordinal));
        }
    }
}
