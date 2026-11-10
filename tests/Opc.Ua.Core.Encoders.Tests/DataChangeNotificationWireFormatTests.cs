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

using System;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Encoders.Tests
{
    /// <summary>
    /// <see cref="MonitoredItemNotification"/> is generated as a value type. Its
    /// encodings must stay byte for byte what the class produced; the expected
    /// values were captured with the class version.
    /// </summary>
    [TestFixture]
    [Category("Encoder")]
    [SetCulture("en-us")]
    [Parallelizable]
    public class DataChangeNotificationWireFormatTests
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
            using var encoder = new XmlEncoder(CreateContext());
            encoder.PushNamespace(Namespaces.OpcUaXsd);
            encoder.WriteEncodeable("Message", CreateMessage());
            encoder.PopNamespace();
            Assert.That(
                encoder.CloseAndReturnText()!.Replace("\r\n", "\n", StringComparison.Ordinal),
                Is.EqualTo(kExpectedXml.Replace("\r\n", "\n", StringComparison.Ordinal)));
        }

        [Test]
        public void BinaryRoundTripKeepsEveryItem()
        {
            IServiceMessageContext context = CreateContext();
            NotificationMessage message = CreateMessage();
            byte[] encoded = BinaryEncoder.EncodeMessage(message, context);

            NotificationMessage decoded = BinaryDecoder.DecodeMessage<NotificationMessage>(encoded, context);

            Assert.That(decoded.NotificationData[0].TryGetValue(out DataChangeNotification? notification), Is.True);
            Assert.That(message.NotificationData[0].TryGetValue(out DataChangeNotification? original), Is.True);
            Assert.That(notification!.MonitoredItems, Is.EqualTo(original!.MonitoredItems));
            Assert.That(notification.DiagnosticInfos.Count, Is.EqualTo(3));
        }

        [Test]
        public void JsonRoundTripKeepsEveryItem([Values] bool compact)
        {
            ServiceMessageContext context = CreateContext();
            NotificationMessage message = CreateMessage();
            string json = EncodeJson(compact ? JsonEncoderOptions.Compact : JsonEncoderOptions.Verbose);

            // the decoder must fill each value type item in place, not a copy of it.
            using var decoder = new JsonDecoder(json, context);
            NotificationMessage decoded = decoder.ReadEncodeable<NotificationMessage>("Message");

            Assert.That(decoded.NotificationData[0].TryGetValue(out DataChangeNotification? notification), Is.True);
            Assert.That(message.NotificationData[0].TryGetValue(out DataChangeNotification? original), Is.True);
            Assert.That(notification!.MonitoredItems, Is.EqualTo(original!.MonitoredItems));
            Assert.That(notification.DiagnosticInfos.Count, Is.EqualTo(3));
        }

        /// <summary>
        /// An all-default value is a message like any other, not a missing one.
        /// </summary>
        [Test]
        public void DefaultValueIsEncodedAsMessage()
        {
            ServiceMessageContext context = CreateContext();
            MonitoredItemNotification empty = default;
            ExpandedNodeId typeId = empty.TypeId;

            using var binary = new BinaryEncoder(context);
            binary.EncodeMessage(empty);
            MonitoredItemNotification decoded = BinaryDecoder.DecodeMessage<MonitoredItemNotification>(
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
                Assert.That(() => binaryWithType.EncodeMessage(empty, typeId), Throws.Nothing);
                Assert.That(() => json.EncodeMessage(empty), Throws.Nothing);
                Assert.That(() => jsonWithType.EncodeMessage(empty, typeId), Throws.Nothing);
                Assert.That(() => xml.EncodeMessage(empty), Throws.Nothing);
                Assert.That(() => xmlWithType.EncodeMessage(empty, typeId), Throws.Nothing);
            });
        }

        private static string EncodeJson(JsonEncoderOptions options)
        {
            using var encoder = new JsonEncoder(CreateContext(), options);
            encoder.WriteEncodeable("Message", CreateMessage());
            return encoder.CloseAndReturnText();
        }

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.Create(NUnitTelemetryContext.Create());
        }

        private static NotificationMessage CreateMessage()
        {
            var timestamp = new DateTimeUtc(new DateTime(2026, 10, 21, 8, 30, 15, DateTimeKind.Utc));
            var notification = new DataChangeNotification
            {
                MonitoredItems = new MonitoredItemNotification[]
                {
                    new()
                    {
                        ClientHandle = 17,
                        Value = new DataValue(new Variant(42.5), StatusCodes.Good, timestamp, timestamp)
                    },
                    // all fields at their default: written as an object, never as null.
                    new(),
                    new()
                    {
                        ClientHandle = 99,
                        Value = new DataValue(new Variant("text"), StatusCodes.BadNodeIdUnknown)
                    }
                },
                DiagnosticInfos = new DiagnosticInfo[]
                {
                    null!,
                    new() { SymbolicId = 1, AdditionalInfo = "info" },
                    null!
                }
            };
            return new NotificationMessage
            {
                SequenceNumber = 5,
                PublishTime = timestamp,
                NotificationData = [new ExtensionObject(notification)]
            };
        }

        private const string kExpectedBinary =
            "0100250305000000800580653661DD010100000001002B03014C00000003000000110000000D0B0000000000404540800580653661DD01800580653661DD01000000000063000000030C0400000074657874000034800300000000110100000004000000696E666F00";

        private const string kExpectedVerboseJson =
            """
            {"Message":{"SequenceNumber":5,"PublishTime":"2026-10-21T08:30:15Z","NotificationData":[{"UaTypeId":"i=809","MonitoredItems":[{"ClientHandle":17,"Value":{"UaType":11,"Value":42.5,"SourceTimestamp":"2026-10-21T08:30:15Z","ServerTimestamp":"2026-10-21T08:30:15Z"}},{"ClientHandle":0,"Value":{}},{"ClientHandle":99,"Value":{"UaType":12,"Value":"text","Status":{"Code":2150891520,"Symbol":"BadNodeIdUnknown"}}}],"DiagnosticInfos":[null,{"SymbolicId":1,"AdditionalInfo":"info"},null]}]}}
            """;

        private const string kExpectedCompactJson =
            """
            {"Message":{"SequenceNumber":5,"PublishTime":"2026-10-21T08:30:15Z","NotificationData":[{"UaTypeId":"i=809","MonitoredItems":[{"ClientHandle":17,"Value":{"UaType":11,"Value":42.5,"SourceTimestamp":"2026-10-21T08:30:15Z","ServerTimestamp":"2026-10-21T08:30:15Z"}},{"Value":{}},{"ClientHandle":99,"Value":{"UaType":12,"Value":"text","Status":{"Code":2150891520}}}],"DiagnosticInfos":[null,{"SymbolicId":1,"AdditionalInfo":"info"},null]}]}}
            """;

        private const string kExpectedXml =
            """
            <Message xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
              <SequenceNumber>5</SequenceNumber>
              <PublishTime>2026-10-21T08:30:15Z</PublishTime>
              <NotificationData>
                <ExtensionObject>
                  <TypeId>
                    <Identifier>i=810</Identifier>
                  </TypeId>
                  <Body>
                    <DataChangeNotification>
                      <MonitoredItems>
                        <MonitoredItemNotification>
                          <ClientHandle>17</ClientHandle>
                          <Value>
                            <Value>
                              <Value>
                                <Double>42.5</Double>
                              </Value>
                            </Value>
                            <StatusCode>
                              <Code>0</Code>
                            </StatusCode>
                            <SourceTimestamp>2026-10-21T08:30:15Z</SourceTimestamp>
                            <SourcePicoseconds>0</SourcePicoseconds>
                            <ServerTimestamp>2026-10-21T08:30:15Z</ServerTimestamp>
                            <ServerPicoseconds>0</ServerPicoseconds>
                          </Value>
                        </MonitoredItemNotification>
                        <MonitoredItemNotification>
                          <ClientHandle>0</ClientHandle>
                          <Value>
                            <Value>
                              <Value xsi:nil="true" />
                            </Value>
                            <StatusCode>
                              <Code>0</Code>
                            </StatusCode>
                            <SourceTimestamp>0001-01-01T00:00:00Z</SourceTimestamp>
                            <SourcePicoseconds>0</SourcePicoseconds>
                            <ServerTimestamp>0001-01-01T00:00:00Z</ServerTimestamp>
                            <ServerPicoseconds>0</ServerPicoseconds>
                          </Value>
                        </MonitoredItemNotification>
                        <MonitoredItemNotification>
                          <ClientHandle>99</ClientHandle>
                          <Value>
                            <Value>
                              <Value>
                                <String>text</String>
                              </Value>
                            </Value>
                            <StatusCode>
                              <Code>2150891520</Code>
                            </StatusCode>
                            <SourceTimestamp>0001-01-01T00:00:00Z</SourceTimestamp>
                            <SourcePicoseconds>0</SourcePicoseconds>
                            <ServerTimestamp>0001-01-01T00:00:00Z</ServerTimestamp>
                            <ServerPicoseconds>0</ServerPicoseconds>
                          </Value>
                        </MonitoredItemNotification>
                      </MonitoredItems>
                      <DiagnosticInfos>
                        <DiagnosticInfo xsi:nil="true" />
                        <DiagnosticInfo>
                          <SymbolicId>1</SymbolicId>
                          <NamespaceUri>-1</NamespaceUri>
                          <Locale>-1</Locale>
                          <LocalizedText>-1</LocalizedText>
                          <AdditionalInfo>info</AdditionalInfo>
                          <InnerStatusCode>
                            <Code>0</Code>
                          </InnerStatusCode>
                        </DiagnosticInfo>
                        <DiagnosticInfo xsi:nil="true" />
                      </DiagnosticInfos>
                    </DataChangeNotification>
                  </Body>
                </ExtensionObject>
              </NotificationData>
            </Message>
            """;
    }
}
