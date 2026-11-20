/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Encoders.Tests
{
    /// <summary>
    /// <see cref="EventFieldList"/> is generated as a value type. Its encodings must stay
    /// byte for byte what the class produced; the expected values were captured with the
    /// class version.
    /// </summary>
    [TestFixture]
    [Category("Encoder")]
    [SetCulture("en-us")]
    [Parallelizable]
    public class EventNotificationWireFormatTests
    {
        [Test]
        public void BinaryEncodingIsUnchanged()
        {
            byte[] encoded = BinaryEncoder.EncodeMessage(CreateMessage(), CreateContext());
            Assert.That(CoreUtils.ToHexString(encoded), Is.EqualTo(kExpectedBinary));
        }

        [Test]
        public void VerboseJsonEncodingIsUnchanged()
        {
            Assert.That(EncodeJson(JsonEncoderOptions.Verbose), Is.EqualTo(kExpectedVerboseJson));
        }

        [Test]
        public void CompactJsonEncodingIsUnchanged()
        {
            Assert.That(EncodeJson(JsonEncoderOptions.Compact), Is.EqualTo(kExpectedCompactJson));
        }

        [Test]
        public void XmlEncodingIsUnchanged()
        {
            Assert.That(
                EncodeXml().Replace("\r\n", "\n", StringComparison.Ordinal),
                Is.EqualTo(kExpectedXml.Replace("\r\n", "\n", StringComparison.Ordinal)));
        }

        [Test]
        public void BinaryRoundTripKeepsEveryEvent()
        {
            IServiceMessageContext context = CreateContext();
            NotificationMessage message = CreateMessage();
            byte[] encoded = BinaryEncoder.EncodeMessage(message, context);

            NotificationMessage decoded = BinaryDecoder.DecodeMessage<NotificationMessage>(encoded, context);

            AssertSameEvents(decoded, message);
        }

        [Test]
        public void JsonRoundTripKeepsEveryEvent([Values] bool compact)
        {
            ServiceMessageContext context = CreateContext();
            NotificationMessage message = CreateMessage();
            string json = EncodeJson(compact ? JsonEncoderOptions.Compact : JsonEncoderOptions.Verbose);

            // the decoder must fill each value type event in place, not a copy of it.
            using var decoder = new JsonDecoder(json, context);
            NotificationMessage decoded = decoder.ReadEncodeable<NotificationMessage>("Message");

            AssertSameEvents(decoded, message);
        }

        /// <summary>
        /// An all-default value is a message like any other, not a missing one.
        /// </summary>
        [Test]
        public void DefaultValueIsEncodedAsMessage()
        {
            ServiceMessageContext context = CreateContext();
            EventFieldList empty = default;
            ExpandedNodeId typeId = empty.TypeId;

            using var binary = new BinaryEncoder(context);
            binary.EncodeMessage(empty);
            EventFieldList decoded = BinaryDecoder.DecodeMessage<EventFieldList>(
                binary.CloseAndReturnBuffer()!,
                context);
            using var binaryWithType = new BinaryEncoder(context);
            using var json = new JsonEncoder(context, JsonEncoderOptions.Verbose);
            using var jsonWithType = new JsonEncoder(context, JsonEncoderOptions.Verbose);
            using var xml = new XmlEncoder(context);
            using var xmlWithType = new XmlEncoder(context);

            Assert.Multiple(() =>
            {
                Assert.That(decoded.ClientHandle, Is.Zero);
                Assert.That(decoded.EventFields.Count, Is.Zero);
                Assert.That(() => binaryWithType.EncodeMessage(empty, typeId), Throws.Nothing);
                Assert.That(() => json.EncodeMessage(empty), Throws.Nothing);
                Assert.That(() => jsonWithType.EncodeMessage(empty, typeId), Throws.Nothing);
                Assert.That(() => xml.EncodeMessage(empty), Throws.Nothing);
                Assert.That(() => xmlWithType.EncodeMessage(empty, typeId), Throws.Nothing);
            });
        }

        private static void AssertSameEvents(NotificationMessage decoded, NotificationMessage message)
        {
            Assert.That(decoded.NotificationData[0].TryGetValue(out EventNotificationList? events), Is.True);
            Assert.That(message.NotificationData[0].TryGetValue(out EventNotificationList? original), Is.True);
            Assert.That(events!.Events.Count, Is.EqualTo(original!.Events.Count));
            for (int ii = 0; ii < original.Events.Count; ii++)
            {
                Assert.That(events.Events[ii].ClientHandle, Is.EqualTo(original.Events[ii].ClientHandle));
                Assert.That(events.Events[ii].EventFields, Is.EqualTo(original.Events[ii].EventFields));
            }
        }

        private static string EncodeJson(JsonEncoderOptions options)
        {
            using var encoder = new JsonEncoder(CreateContext(), options);
            encoder.WriteEncodeable("Message", CreateMessage());
            return encoder.CloseAndReturnText();
        }

        private static string EncodeXml()
        {
            using var encoder = new XmlEncoder(CreateContext());
            encoder.PushNamespace(Namespaces.OpcUaXsd);
            encoder.WriteEncodeable("Message", CreateMessage());
            encoder.PopNamespace();
            return encoder.CloseAndReturnText()!;
        }

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.Create(NUnitTelemetryContext.Create());
        }

        private static NotificationMessage CreateMessage()
        {
            var timestamp = new DateTimeUtc(new DateTime(2026, 11, 14, 9, 45, 30, DateTimeKind.Utc));
            var notification = new EventNotificationList
            {
                Events = new EventFieldList[]
                {
                    new()
                    {
                        ClientHandle = 17,
                        EventFields =
                        [
                            new Variant(new byte[] { 1, 2, 3, 4 }),
                            new Variant(new NodeId(2041)),
                            new Variant(timestamp),
                            new Variant(new LocalizedText("en-US", "Level high")),
                            new Variant((ushort)500)
                        ]
                    },
                    // all fields at their default: written as an object, never as null.
                    new(),
                    new()
                    {
                        ClientHandle = 99,
                        EventFields = [Variant.Null, new Variant("text"), new Variant(StatusCodes.BadNodeIdUnknown)]
                    }
                }
            };
            return new NotificationMessage
            {
                SequenceNumber = 7,
                PublishTime = timestamp,
                NotificationData = [new ExtensionObject(notification)]
            };
        }

        private const string kExpectedBinary =
            "0100250307000000005990E21C74DD010100000001009403015E000000030000001100000005000000830400000001020304110100F9070D005990E21C74DD01150305000000656E2D55530A0000004C6576656C206869676805F40100000000000000006300000003000000000C04000000746578741300003480";

        private const string kExpectedVerboseJson =
            """
            {"Message":{"SequenceNumber":7,"PublishTime":"2026-11-14T09:45:30Z","NotificationData":[{"UaTypeId":"i=914","Events":[{"ClientHandle":17,"EventFields":[{"UaType":3,"Value":[1,2,3,4]},{"UaType":17,"Value":"i=2041"},{"UaType":13,"Value":"2026-11-14T09:45:30Z"},{"UaType":21,"Value":{"Text":"Level high","Locale":"en-US"}},{"UaType":5,"Value":500}]},{"ClientHandle":0,"EventFields":[]},{"ClientHandle":99,"EventFields":[null,{"UaType":12,"Value":"text"},{"UaType":19,"Value":{"Code":2150891520,"Symbol":"BadNodeIdUnknown"}}]}]}]}}
            """;

        private const string kExpectedCompactJson =
            """
            {"Message":{"SequenceNumber":7,"PublishTime":"2026-11-14T09:45:30Z","NotificationData":[{"UaTypeId":"i=914","Events":[{"ClientHandle":17,"EventFields":[{"UaType":3,"Value":[1,2,3,4]},{"UaType":17,"Value":"i=2041"},{"UaType":13,"Value":"2026-11-14T09:45:30Z"},{"UaType":21,"Value":{"Text":"Level high","Locale":"en-US"}},{"UaType":5,"Value":500}]},{"EventFields":[]},{"ClientHandle":99,"EventFields":[null,{"UaType":12,"Value":"text"},{"UaType":19,"Value":{"Code":2150891520}}]}]}]}}
            """;

        private const string kExpectedXml =
            """
            <Message xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
              <SequenceNumber>7</SequenceNumber>
              <PublishTime>2026-11-14T09:45:30Z</PublishTime>
              <NotificationData>
                <ExtensionObject>
                  <TypeId>
                    <Identifier>i=915</Identifier>
                  </TypeId>
                  <Body>
                    <EventNotificationList>
                      <Events>
                        <EventFieldList>
                          <ClientHandle>17</ClientHandle>
                          <EventFields>
                            <Variant>
                              <Value>
                                <ListOfByte>
                                  <Byte>1</Byte>
                                  <Byte>2</Byte>
                                  <Byte>3</Byte>
                                  <Byte>4</Byte>
                                </ListOfByte>
                              </Value>
                            </Variant>
                            <Variant>
                              <Value>
                                <NodeId>
                                  <Identifier>i=2041</Identifier>
                                </NodeId>
                              </Value>
                            </Variant>
                            <Variant>
                              <Value>
                                <DateTime>2026-11-14T09:45:30Z</DateTime>
                              </Value>
                            </Variant>
                            <Variant>
                              <Value>
                                <LocalizedText>
                                  <Locale>en-US</Locale>
                                  <Text>Level high</Text>
                                </LocalizedText>
                              </Value>
                            </Variant>
                            <Variant>
                              <Value>
                                <UInt16>500</UInt16>
                              </Value>
                            </Variant>
                          </EventFields>
                        </EventFieldList>
                        <EventFieldList>
                          <ClientHandle>0</ClientHandle>
                          <EventFields />
                        </EventFieldList>
                        <EventFieldList>
                          <ClientHandle>99</ClientHandle>
                          <EventFields>
                            <Variant>
                              <Value xsi:nil="true" />
                            </Variant>
                            <Variant>
                              <Value>
                                <String>text</String>
                              </Value>
                            </Variant>
                            <Variant>
                              <Value>
                                <StatusCode>
                                  <Code>2150891520</Code>
                                </StatusCode>
                              </Value>
                            </Variant>
                          </EventFields>
                        </EventFieldList>
                      </Events>
                    </EventNotificationList>
                  </Body>
                </ExtensionObject>
              </NotificationData>
            </Message>
            """;
    }
}
