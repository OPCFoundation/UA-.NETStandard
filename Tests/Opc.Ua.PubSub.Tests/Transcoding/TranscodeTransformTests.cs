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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transcoding;
using static Opc.Ua.PubSub.Tests.Transcoding.TranscodingTestUtilities;
using UadpDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Transcoding
{
    /// <summary>
    /// Unit tests for the built-in <see cref="IPubSubMessageTransform"/>
    /// implementations.
    /// </summary>
    [TestFixture]
    public class TranscodeTransformTests
    {
        private static UadpNetworkMessageV2 SampleMessage()
        {
            return NewUadpMessage(
                PublisherId.FromByte(1),
                writerGroupId: 10,
                dataSetWriterId: 100,
                Field("a", new Variant(1)),
                Field("b", new Variant(2)));
        }

        private static async Task<PubSubNetworkMessage?> ApplyAsync(IPubSubMessageTransform transform)
        {
            return await transform.TransformAsync(SampleMessage(), NewContext())
                .ConfigureAwait(false);
        }

        [Test]
        public async Task IdRemapTransform_RewritesPublisherAndWriterGroupAndWriterId()
        {
            var transform = new IdRemapTransform(
                PublisherId.FromUInt16(42),
                writerGroupId: 99,
                dataSetWriterIds: new Dictionary<ushort, ushort> { [100] = 200 });

            PubSubNetworkMessage result = (await ApplyAsync(transform).ConfigureAwait(false))!;

            Assert.That(result.PublisherId, Is.EqualTo(PublisherId.FromUInt16(42)));
            Assert.That(result.WriterGroupId, Is.EqualTo((ushort)99));
            Assert.That(result.DataSetMessages[0].DataSetWriterId, Is.EqualTo((ushort)200));
        }

        [Test]
        public async Task IdRemapTransform_NullPublisher_PreservesSource()
        {
            var transform = new IdRemapTransform(writerGroupId: 5);

            PubSubNetworkMessage result = (await ApplyAsync(transform).ConfigureAwait(false))!;

            Assert.That(result.PublisherId, Is.EqualTo(PublisherId.FromByte(1)));
            Assert.That(result.WriterGroupId, Is.EqualTo((ushort)5));
        }

        [Test]
        public async Task IdRemapTransform_RewritesDataSetClassId()
        {
            var classId = new Uuid(System.Guid.NewGuid());
            var transform = new IdRemapTransform(dataSetClassId: classId);

            PubSubNetworkMessage result = (await ApplyAsync(transform).ConfigureAwait(false))!;

            Assert.That(((UadpNetworkMessageV2)result).DataSetClassId, Is.EqualTo(classId));
        }

        [Test]
        public async Task FieldEncodingTransform_SetsFieldAndMessageEncoding()
        {
            var transform = new FieldEncodingTransform(PubSubFieldEncoding.RawData);

            PubSubNetworkMessage result = (await ApplyAsync(transform).ConfigureAwait(false))!;

            var dsm = (UadpDataSetMessageV2)result.DataSetMessages[0];
            Assert.That(dsm.FieldEncoding, Is.EqualTo(PubSubFieldEncoding.RawData));
            Assert.That(dsm.Fields[0].Encoding, Is.EqualTo(PubSubFieldEncoding.RawData));
        }

        [Test]
        public async Task FieldProjectionTransform_IncludeKeepsAndReorders()
        {
            var transform = new FieldProjectionTransform(["b", "a"]);

            PubSubNetworkMessage result = (await ApplyAsync(transform).ConfigureAwait(false))!;

            ArrayOf<DataSetField> fields = result.DataSetMessages[0].Fields;
            Assert.That(fields.Count, Is.EqualTo(2));
            Assert.That(fields[0].Name, Is.EqualTo("b"));
            Assert.That(fields[1].Name, Is.EqualTo("a"));
        }

        [Test]
        public async Task FieldProjectionTransform_ExcludeDropsNamedFields()
        {
            var transform = new FieldProjectionTransform(["a"], exclude: true);

            PubSubNetworkMessage result = (await ApplyAsync(transform).ConfigureAwait(false))!;

            ArrayOf<DataSetField> fields = result.DataSetMessages[0].Fields;
            Assert.That(fields.Count, Is.EqualTo(1));
            Assert.That(fields[0].Name, Is.EqualTo("b"));
        }

        [Test]
        public async Task FieldRenameTransform_RenamesMappedFields()
        {
            var transform = new FieldRenameTransform(
                new Dictionary<string, string> { ["a"] = "alpha" });

            PubSubNetworkMessage result = (await ApplyAsync(transform).ConfigureAwait(false))!;

            ArrayOf<DataSetField> fields = result.DataSetMessages[0].Fields;
            Assert.That(fields[0].Name, Is.EqualTo("alpha"));
            Assert.That(fields[1].Name, Is.EqualTo("b"));
        }

        [Test]
        public async Task ValueTransform_ReplacesSelectedFieldValues()
        {
            var transform = new ValueTransform(
                (name, value) => name == "a" ? new Variant(100) : value);

            PubSubNetworkMessage result = (await ApplyAsync(transform).ConfigureAwait(false))!;

            ArrayOf<DataSetField> fields = result.DataSetMessages[0].Fields;
            Assert.That(fields[0].Value, Is.EqualTo(new Variant(100)));
            Assert.That(fields[1].Value, Is.EqualTo(new Variant(2)));
        }

        [Test]
        public async Task MessageTypeTransform_DropsKeepAliveAndFiltersWholeMessage()
        {
            var keepAlive = new UadpNetworkMessageV2
            {
                PublisherId = PublisherId.FromByte(1),
                DataSetMessages =
                [
                    new UadpDataSetMessageV2
                    {
                        DataSetWriterId = 1,
                        MessageType = PubSubDataSetMessageType.KeepAlive
                    }
                ]
            };
            var transform = new MessageTypeTransform(
                keep: type => type != PubSubDataSetMessageType.KeepAlive);

            PubSubNetworkMessage? result = await transform
                .TransformAsync(keepAlive, NewContext())
                .ConfigureAwait(false);

            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task MessageTypeTransform_ForceTypeRelabelsKeptMessages()
        {
            var transform = new MessageTypeTransform(
                forceType: PubSubDataSetMessageType.DeltaFrame);

            PubSubNetworkMessage result = (await ApplyAsync(transform).ConfigureAwait(false))!;

            Assert.That(result.DataSetMessages[0].MessageType,
                Is.EqualTo(PubSubDataSetMessageType.DeltaFrame));
        }

        [Test]
        public async Task MetaDataTransform_RewritesMetaDataAndPassesThroughDataMessages()
        {
            var withMeta = SampleMessage() with
            {
                MetaData = new DataSetMetaDataType { Name = "old" }
            };
            var transform = new MetaDataTransform(
                meta => new DataSetMetaDataType { Name = "new" });

            PubSubNetworkMessage rewritten = (await transform
                .TransformAsync(withMeta, NewContext())
                .ConfigureAwait(false))!;
            PubSubNetworkMessage passthrough = (await transform
                .TransformAsync(SampleMessage(), NewContext())
                .ConfigureAwait(false))!;

            Assert.That(rewritten.MetaData!.Name, Is.EqualTo("new"));
            Assert.That(passthrough.MetaData, Is.Null);
        }

        [Test]
        public async Task DelegateMessageTransform_SyncDropReturnsNull()
        {
            IPubSubMessageTransform transform = DelegateMessageTransform.FromSync(_ => null);

            PubSubNetworkMessage? result = await ApplyAsync(transform).ConfigureAwait(false);

            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task DelegateMessageTransform_AsyncTransformsMessage()
        {
            var transform = new DelegateMessageTransform(
                (message, _, _) => new ValueTask<PubSubNetworkMessage?>(
                    message with { WriterGroupId = 777 }));

            PubSubNetworkMessage result = (await ApplyAsync(transform).ConfigureAwait(false))!;

            Assert.That(result.WriterGroupId, Is.EqualTo((ushort)777));
        }
    }
}
