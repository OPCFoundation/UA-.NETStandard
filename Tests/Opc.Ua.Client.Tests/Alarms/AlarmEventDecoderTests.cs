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

        [Test]
        public void DecodeWithNullFieldsReturnsNull()
        {
            ConditionTypeRecord? result = AlarmEventDecoder.Decode(null!);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void DecodeWithTruncatedFieldArrayReturnsPartialConditionRecord()
        {
            // 5 fields — fewer than the 15 base condition fields. Decoder
            // must not throw and must yield a plain ConditionTypeRecord
            // with missing fields defaulted.
            var fields = new List<Variant>
            {
                Variant.From(new ByteString(new byte[] { 9 })),
                Variant.From((NodeId)ObjectTypeIds.ConditionType),
                Variant.From((NodeId)new NodeId(7)),
                Variant.From("SrcName"),
                default(Variant)
            };

            ConditionTypeRecord? result = AlarmEventDecoder.Decode(fields);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ConditionTypeRecord>());
            Assert.That(result, Is.Not.InstanceOf<AcknowledgeableConditionTypeRecord>());
            Assert.That(result!.SourceName, Is.EqualTo("SrcName"));
            Assert.That(result.Severity, Is.Zero);
            Assert.That(result.ConditionName, Is.Null);
        }

        [Test]
        public void DecodeResolvesExclusiveLimitAlarmSubtype()
        {
            // Need at least 33 fields and a non-null NodeId at index 32
            // (LimitState/CurrentState/Id) plus one alarm-bearing field
            // (e.g. activeStateId at index 17) so the alarm branch fires.
            var fields = new List<Variant>();
            for (int i = 0; i < 33; i++)
            {
                fields.Add(default(Variant));
            }
            // alarm marker
            fields[17] = Variant.From(true);
            // exclusive-limit marker
            fields[32] = Variant.From((NodeId)new NodeId(123u));

            ConditionTypeRecord? result = AlarmEventDecoder.Decode(fields);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ExclusiveLimitAlarmTypeRecord>());
            Assert.That(((AlarmConditionTypeRecord)result!).ActiveStateId, Is.True);
        }

        [Test]
        public void DecodeDetectsDialogConditionWhenDialogStatePopulated()
        {
            var fields = new List<Variant>();
            for (int i = 0; i < 25; i++)
            {
                fields.Add(default(Variant));
            }
            // dialogStateId at index 24
            fields[24] = Variant.From(true);

            ConditionTypeRecord? result = AlarmEventDecoder.Decode(fields);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<DialogConditionTypeRecord>());
            Assert.That(((DialogConditionTypeRecord)result!).DialogStateId, Is.True);
        }

        [Test]
        public void DecodeMapsLimitAlarmFields()
        {
            // Populate base + alarm marker (activeStateId) + limit fields
            // 27..30 so the LimitAlarmTypeRecord branch fires.
            var fields = new List<Variant>();
            for (int i = 0; i < 31; i++)
            {
                fields.Add(default(Variant));
            }
            fields[17] = Variant.From(true); // alarm marker
            fields[27] = Variant.From(100.5);
            fields[28] = Variant.From(80.25);
            fields[29] = Variant.From(20.5);
            fields[30] = Variant.From(0.125);

            ConditionTypeRecord? result = AlarmEventDecoder.Decode(fields);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<LimitAlarmTypeRecord>());
            var limit = (LimitAlarmTypeRecord)result!;
            Assert.That(limit.HighHighLimit, Is.EqualTo(100.5));
            Assert.That(limit.HighLimit, Is.EqualTo(80.25));
            Assert.That(limit.LowLimit, Is.EqualTo(20.5));
            Assert.That(limit.LowLowLimit, Is.EqualTo(0.125));
        }

        [Test]
        public void DecodeMapsNullVariantToTypeNull()
        {
            // 15 fields, every variant null — decoder must fall back to
            // NodeId.Null / null reference for INullable fields and
            // default for primitives.
            var fields = new List<Variant>();
            for (int i = 0; i < 15; i++)
            {
                fields.Add(default(Variant));
            }

            ConditionTypeRecord? result = AlarmEventDecoder.Decode(fields);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ConditionTypeRecord>());
            Assert.That(result, Is.Not.InstanceOf<AcknowledgeableConditionTypeRecord>());
            Assert.That(result!.SourceNode.IsNull, Is.True);
            Assert.That(result.EventType.IsNull, Is.True);
            Assert.That(result.BranchId.IsNull, Is.True);
            Assert.That(result.EventId.IsNull, Is.True);
            Assert.That(result.Severity, Is.Zero);
            Assert.That(result.Retain, Is.False);
        }
    }
}
