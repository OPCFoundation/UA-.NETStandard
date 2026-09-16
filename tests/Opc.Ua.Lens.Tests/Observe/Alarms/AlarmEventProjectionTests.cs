/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;
using UaLens.Plugins.Alarms;

namespace UaLens.Tests.Observe;

[TestFixture]
public sealed class AlarmEventProjectionTests
{
    [Test]
    public void SelectsActualConditionObjectIdRatherThanSourceNode()
    {
        var projection = new AlarmEventProjection();
        SimpleAttributeOperand identity = projection.Filter.SelectClauses[projection.ConditionIdIndex];

        Assert.That(identity.TypeDefinitionId, Is.EqualTo(ObjectTypeIds.ConditionType));
        Assert.That(identity.AttributeId, Is.EqualTo(Attributes.NodeId));
        Assert.That(identity.BrowsePath.IsEmpty, Is.True);

        AlarmUpdate update = projection.Decode(AlarmTestData.Fields(projection, ObjectTypeIds.AlarmConditionType), 11);

        Assert.That(update.Kind, Is.EqualTo(AlarmUpdateKind.Condition));
        Assert.That(update.Condition, Is.Not.Null);
        Assert.That(update.Condition!.Key.ConditionId, Is.EqualTo(new NodeId(500u, 2)));
        Assert.That(update.Condition.SourceNode, Is.EqualTo(new NodeId(100u, 2)));
        Assert.That(update.Condition.Key.ConditionId, Is.Not.EqualTo(update.Condition.SourceNode));
        Assert.That(update.Condition.Key.BranchId.IsNull, Is.True);
        Assert.That(update.Condition.EventId, Is.EqualTo(ByteString.From([1, 2, 3])));
        Assert.That(update.Condition.Active, Is.True);
        Assert.That(update.Condition.Acknowledged, Is.False);
        Assert.That(update.Condition.Severity, Is.EqualTo(700));
        Assert.That(update.PartitionId, Is.EqualTo(11));
    }

    [Test]
    public void FilterIncludesConditionsAndAllRefreshControlTypes()
    {
        var projection = new AlarmEventProjection();
        var types = new HashSet<NodeId>();
        foreach (ContentFilterElement element in projection.Filter.WhereClause.Elements)
        {
            if (element.FilterOperator != FilterOperator.OfType)
            {
                continue;
            }
            Assert.That(element.FilterOperands[0].TryGetValue(out LiteralOperand? literal), Is.True);
            Assert.That(literal!.Value.TryGetValue(out NodeId typeId), Is.True);
            types.Add(typeId);
        }

        Assert.That(types, Is.EquivalentTo(new[]
        {
            ObjectTypeIds.ConditionType,
            ObjectTypeIds.RefreshStartEventType,
            ObjectTypeIds.RefreshEndEventType,
            ObjectTypeIds.RefreshRequiredEventType,
            ObjectTypeIds.EventQueueOverflowEventType
        }));
        Assert.That(projection.Filter.WhereClause.Elements[0].FilterOperator, Is.EqualTo(FilterOperator.Or));
        Assert.That(projection.Filter.SelectClauses, Has.Count.LessThan(128));
    }

    [TestCase(ObjectTypes.RefreshStartEventType, (int)AlarmUpdateKind.RefreshStart)]
    [TestCase(ObjectTypes.RefreshEndEventType, (int)AlarmUpdateKind.RefreshEnd)]
    [TestCase(ObjectTypes.RefreshRequiredEventType, (int)AlarmUpdateKind.RefreshRequired)]
    [TestCase(ObjectTypes.EventQueueOverflowEventType, (int)AlarmUpdateKind.Loss)]
    public void PreservesControlEventsWithoutRequiringConditionId(uint eventType, int expectedKind)
    {
        var projection = new AlarmEventProjection();
        Variant[] fields = AlarmTestData.Fields(projection, new NodeId(eventType)).ToArray() ?? [];
        fields[projection.ConditionIdIndex] = Variant.Null;

        AlarmUpdate update = projection.Decode(fields, 22);

        Assert.That(update.Kind, Is.EqualTo((AlarmUpdateKind)expectedKind));
        Assert.That(update.Condition, Is.Null);
        Assert.That(update.PartitionId, Is.EqualTo(22));
    }

    [Test]
    public void MissingConditionIdDoesNotUseSourceNodeAsFallback()
    {
        var projection = new AlarmEventProjection();
        Variant[] fields = AlarmTestData.Fields(projection, ObjectTypeIds.AlarmConditionType).ToArray() ?? [];
        fields[projection.ConditionIdIndex] = Variant.Null;

        AlarmUpdate update = projection.Decode(fields, 11);

        Assert.That(update.Kind, Is.EqualTo(AlarmUpdateKind.Loss));
        Assert.That(update.Condition, Is.Null);
        Assert.That(update.Detail, Does.Contain("no ObjectId was guessed"));
    }

    [Test]
    public void CustomAlarmTypeUsesGeneratedStandardFieldsWithoutATypeCache()
    {
        var projection = new AlarmEventProjection();
        var customType = new NodeId("CustomHighTemperatureAlarm", 3);
        ArrayOf<Variant> fields = AlarmTestData.SetField(
            projection, AlarmTestData.Fields(projection, customType),
            BrowseNames.BranchId, Variant.From(new NodeId(99u, 2)));

        AlarmUpdate update = projection.Decode(fields, 11);

        Assert.That(update.Kind, Is.EqualTo(AlarmUpdateKind.Condition));
        Assert.That(update.Condition!.Kind, Is.EqualTo(AlarmConditionKind.Alarm));
        Assert.That(update.Condition.EventType, Is.EqualTo(customType));
        Assert.That(update.Condition.Key.BranchId, Is.EqualTo(new NodeId(99u, 2)));
        Assert.That(update.Condition.Key.ConditionId, Is.EqualTo(new NodeId(500u, 2)));
        Assert.That(update.Condition.Message, Is.EqualTo("Temperature is high"));
    }

    [Test]
    public void MissingQualityAndSeverityAreNotDisplayedAsGoodAndZero()
    {
        var projection = new AlarmEventProjection();
        ArrayOf<Variant> fields = AlarmTestData.Fields(projection, ObjectTypeIds.AlarmConditionType);
        fields = AlarmTestData.SetField(projection, fields, BrowseNames.Quality, Variant.Null);
        fields = AlarmTestData.SetField(projection, fields, BrowseNames.Severity, Variant.Null);

        AlarmUpdate update = projection.Decode(fields, 11);

        Assert.That(update.Condition!.Quality, Is.EqualTo(StatusCodes.BadNoData));
        Assert.That(update.Condition.Severity, Is.Null);
    }

    [Test]
    public void OversizedIdentifiersAreRejectedAndDisplayTextIsBounded()
    {
        var projection = new AlarmEventProjection();
        ArrayOf<Variant> fields = AlarmTestData.SetField(
            projection, AlarmTestData.Fields(projection, ObjectTypeIds.AlarmConditionType),
            BrowseNames.Message, Variant.From(new LocalizedText(new string('x', AlarmLimits.TextLength * 4))));

        AlarmUpdate bounded = projection.Decode(fields, 11);
        Assert.That(bounded.Condition!.Message, Has.Length.EqualTo(AlarmLimits.TextLength + 1));

        Variant[] oversized = fields.ToArray() ?? [];
        oversized[projection.ConditionIdIndex] = Variant.From(
            new NodeId(new string('x', AlarmLimits.IdentifierLength + 1), 2));
        AlarmUpdate invalid = projection.Decode(oversized, 11);
        Assert.That(invalid.Kind, Is.EqualTo(AlarmUpdateKind.Loss));
        Assert.That(invalid.Condition, Is.Null);
    }

    [Test]
    public void DialogResponsesUseABoundedTypedProjection()
    {
        var projection = new AlarmEventProjection();
        ArrayOf<Variant> fields = AlarmTestData.SetField(
            projection, AlarmTestData.Fields(projection, ObjectTypeIds.DialogConditionType),
            BrowseNames.DialogState, Variant.From(true));
        fields = AlarmTestData.SetField(projection, fields, BrowseNames.ResponseOptionSet,
            Variant.From(ArrayOf.Wrapped(new[] { new LocalizedText("Cancel"), new LocalizedText("Proceed") })));

        AlarmUpdate decoded = projection.Decode(fields, 11);

        Assert.That(decoded.Condition!.Kind, Is.EqualTo(AlarmConditionKind.Dialog));
        Assert.That(decoded.Condition.Responses.ToArray(), Is.EqualTo(s_responseOptions));
        Assert.That(decoded.Condition.DialogActive, Is.True);

        var excessive = new LocalizedText[AlarmLimits.ResponseCount + 1];
        fields = AlarmTestData.SetField(projection, fields, BrowseNames.ResponseOptionSet,
            Variant.From(ArrayOf.Wrapped(excessive)));
        Assert.That(projection.Decode(fields, 11).Condition!.Responses.IsEmpty, Is.True);
    }

    [Test]
    public void MalformedFieldCountCannotBecomeACondition()
    {
        var projection = new AlarmEventProjection();
        Assert.That(projection.Decode([], 11).Kind, Is.EqualTo(AlarmUpdateKind.Loss));
    }

    private static readonly string[] s_responseOptions = ["Cancel", "Proceed"];
}
