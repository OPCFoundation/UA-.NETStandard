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

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Server.Historian;

#nullable enable

namespace Opc.Ua.Server.Tests.Historian
{
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianEventFilterTargetTests
    {
        [Test]
        public void GetAttributeValueResolvesBrowseNameField()
        {
            var record = new HistorianEventRecord(
                ByteString.Empty,
                ObjectTypeIds.BaseEventType,
                new System.DateTime(2025, 1, 1, 0, 0, 0, System.DateTimeKind.Utc),
                new Dictionary<string, Variant>(System.StringComparer.Ordinal)
                {
                    [BrowseNames.Severity] = new Variant((ushort)500),
                });

            var target = new HistorianEventFilterTarget(record);
            ArrayOf<QualifiedName> path = new QualifiedName[] { new(BrowseNames.Severity) };
            Variant value = target.GetAttributeValue(null!, NodeId.Null, path, Attributes.Value, NumericRange.Null);
            Assert.That(value.TryGetValue(out ushort severity), Is.True);
            Assert.That(severity, Is.EqualTo(500));
        }

        [Test]
        public void GetAttributeValueReturnsEmptyForUnknownField()
        {
            var record = new HistorianEventRecord(
                ByteString.Empty,
                ObjectTypeIds.BaseEventType,
                new System.DateTime(2025, 1, 1, 0, 0, 0, System.DateTimeKind.Utc),
                new Dictionary<string, Variant>(System.StringComparer.Ordinal));

            var target = new HistorianEventFilterTarget(record);
            ArrayOf<QualifiedName> path = new QualifiedName[] { new("DoesNotExist") };
            Variant value = target.GetAttributeValue(null!, NodeId.Null, path, Attributes.Value, NumericRange.Null);
            Assert.That(value, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void IsTypeOfReturnsTrueWhenExactMatch()
        {
            var record = new HistorianEventRecord(
                ByteString.Empty,
                ObjectTypeIds.BaseEventType,
                new System.DateTime(2025, 1, 1, 0, 0, 0, System.DateTimeKind.Utc),
                new Dictionary<string, Variant>(System.StringComparer.Ordinal));

            var target = new HistorianEventFilterTarget(record);
            Assert.That(target.IsTypeOf(null!, ObjectTypeIds.BaseEventType), Is.True);
        }

        [Test]
        public void IsTypeOfResolvesSubtypeViaTypeTree()
        {
            // Record's declared type is AuditEventType, which derives from BaseEventType.
            var record = new HistorianEventRecord(
                ByteString.Empty,
                ObjectTypeIds.AuditEventType,
                new System.DateTime(2025, 1, 1, 0, 0, 0, System.DateTimeKind.Utc),
                new Dictionary<string, Variant>(System.StringComparer.Ordinal));

            // Build a TypeTable with the AuditEventType -> BaseEventType subtype
            // relationship pre-registered (mirrors what a real server's TypeTable
            // looks like after standard-type bootstrapping).
            var ns = new NamespaceTable();
            var typeTree = new TypeTable(ns);
            typeTree.AddSubtype(ObjectTypeIds.BaseEventType, NodeId.Null);
            typeTree.AddSubtype(ObjectTypeIds.AuditEventType, ObjectTypeIds.BaseEventType);
            typeTree.AddSubtype(ObjectTypeIds.SystemEventType, ObjectTypeIds.BaseEventType);

            var filterCtx = new FilterContext(ns, typeTree, (ITelemetryContext)null!);
            var target = new HistorianEventFilterTarget(record);

            Assert.That(target.IsTypeOf(filterCtx, ObjectTypeIds.BaseEventType), Is.True,
                "AuditEventType-typed record must match a BaseEventType filter via TypeTree.");
            Assert.That(target.IsTypeOf(filterCtx, ObjectTypeIds.AuditEventType), Is.True,
                "Exact match always wins.");
            Assert.That(target.IsTypeOf(filterCtx, ObjectTypeIds.SystemEventType), Is.False,
                "Records typed under a different hierarchy do not match.");
        }
    }
}
