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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.Tests;
using PubSubJsonDataSetMessage = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
using PubSubJsonEncoder = Opc.Ua.PubSub.Encoding.Json.JsonEncoder;
using PubSubJsonMetaDataMessage = Opc.Ua.PubSub.Encoding.Json.JsonMetaDataMessage;
using PubSubJsonNetworkMessage = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
using PubSubUadpNetworkMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Encoding.Json
{
    /// <summary>
    /// Pins JSON sender diagnostics independently of carrier selection and failed encode attempts.
    /// </summary>
    [TestFixture]
    public sealed class JsonEncoderDiagnosticsTests
    {
        [Test]
        public async Task SuccessfulDataMetadataAndEmptyMessagesHaveExactCounters()
        {
            var diagnostics = new PubSubDiagnostics(PubSubDiagnosticsLevel.High);
            var context = new PubSubNetworkMessageContext(
                ServiceMessageContext.Create(NUnitTelemetryContext.Create()),
                new DataSetMetaDataRegistry(), diagnostics, TimeProvider.System);
            var encoder = new PubSubJsonEncoder();
            await encoder.EncodeAsync(new PubSubJsonNetworkMessage
            {
                DataSetMessages =
                [
                    new PubSubJsonDataSetMessage { DataSetWriterId = 41 },
                    new PubSubJsonDataSetMessage { DataSetWriterId = 42 }
                ]
            }, context);
            Assert.Multiple(() =>
            {
                Assert.That(diagnostics.Read(PubSubDiagnosticsCounterKind.SentNetworkMessages), Is.EqualTo(1));
                Assert.That(diagnostics.Read(PubSubDiagnosticsCounterKind.SentDataSetMessages), Is.EqualTo(2));
            });

            await encoder.EncodeAsync(new PubSubJsonMetaDataMessage
            {
                DataSetWriterId = 41,
                MetaDataPayload = new DataSetMetaDataType { Name = "metadata" }
            }, context);
            await encoder.EncodeAsync(new PubSubJsonNetworkMessage(), context);
            Assert.Multiple(() =>
            {
                Assert.That(diagnostics.Read(PubSubDiagnosticsCounterKind.SentNetworkMessages), Is.EqualTo(3));
                Assert.That(diagnostics.Read(PubSubDiagnosticsCounterKind.SentDataSetMessages), Is.EqualTo(2));
            });

            Assert.That(() => encoder.EncodeAsync(new PubSubUadpNetworkMessage(), context).AsTask(),
                Throws.ArgumentException);
            Assert.That(() => encoder.EncodeAsync(
                new PubSubJsonNetworkMessage { SingleMessageMode = true }, context).AsTask(),
                Throws.ArgumentException);
            Assert.Multiple(() =>
            {
                Assert.That(diagnostics.Read(PubSubDiagnosticsCounterKind.SentNetworkMessages), Is.EqualTo(3));
                Assert.That(diagnostics.Read(PubSubDiagnosticsCounterKind.SentDataSetMessages), Is.EqualTo(2));
            });
        }
    }
}
