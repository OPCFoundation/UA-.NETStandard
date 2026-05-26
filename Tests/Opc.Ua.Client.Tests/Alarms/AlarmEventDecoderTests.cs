/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
 * ======================================================================*/
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Client.Alarms;

namespace Opc.Ua.Client.Tests.Alarms
{
    [TestFixture, Category("Alarms"), Parallelizable]
    public class AlarmEventDecoderTests
    {
        [Test]
        public void DecodeReturnsNullForEmptyFields()
        {
            ConditionTypeRecord? result = AlarmEventDecoder.Decode(System.Array.Empty<Variant>());
            Assert.That(result, Is.Null);
        }

        [Test]
        public void DecodeReturnsConditionRecordForBaseConditionFields()
        {
            var fields = new List<Variant>
            {
                Variant.From(new ByteString(new byte[] { 1, 2, 3 })),
                Variant.From((NodeId)ObjectTypeIds.ConditionType),
                Variant.From((NodeId)new NodeId(42)),
                Variant.From("Source"),
                Variant.From(new System.DateTime(2024, 1, 1, 12, 0, 0, System.DateTimeKind.Utc)),
                Variant.From(new System.DateTime(2024, 1, 1, 12, 0, 1, System.DateTimeKind.Utc)),
                Variant.From(new LocalizedText("en", "Test")),
                Variant.From((ushort)500),
                Variant.From("MyCondition"),
                default(Variant),
                Variant.From(true),
                Variant.From(true),
                Variant.From(new StatusCode(0)),
                Variant.From(new LocalizedText("en", "ok")),
                Variant.From("user1")
            };

            ConditionTypeRecord? result = AlarmEventDecoder.Decode(fields);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.ConditionName, Is.EqualTo("MyCondition"));
            Assert.That(result.Severity, Is.EqualTo((ushort)500));
            Assert.That(result.Retain, Is.True);
            Assert.That(result.EnabledStateId, Is.True);
            Assert.That(result.SourceName, Is.EqualTo("Source"));
            Assert.That(result.GetType(), Is.EqualTo(typeof(ConditionTypeRecord)));
        }

        [Test]
        public void DecodeReturnsAlarmRecordWhenActiveStateIsPresent()
        {
            var fields = new List<Variant>();
            for (int i = 0; i < 15; i++)
            {
                fields.Add(default(Variant));
            }
            fields.Add(Variant.From(false));
            fields.Add(default(Variant));
            fields.Add(Variant.From(true));

            ConditionTypeRecord? result = AlarmEventDecoder.Decode(fields);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<AlarmConditionTypeRecord>());
            Assert.That(((AlarmConditionTypeRecord)result!).ActiveStateId, Is.True);
        }

        [Test]
        public void DecodeReturnsAcknowledgeableConditionRecordWhenOnlyAckPresent()
        {
            var fields = new List<Variant>();
            for (int i = 0; i < 15; i++)
            {
                fields.Add(default(Variant));
            }
            fields.Add(Variant.From(false));
            fields.Add(Variant.From(true));

            ConditionTypeRecord? result = AlarmEventDecoder.Decode(fields);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<AcknowledgeableConditionTypeRecord>());
            Assert.That(result, Is.Not.InstanceOf<AlarmConditionTypeRecord>());
        }
    }
}
