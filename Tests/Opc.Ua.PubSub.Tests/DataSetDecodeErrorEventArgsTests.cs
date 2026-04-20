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
using NUnit.Framework;

using PubSubEncoding = Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DataSetDecodeErrorEventArgsTests
    {
        [Test]
        public void ConstructorSetsDecodeErrorReason()
        {
            var args = new DataSetDecodeErrorEventArgs(
                DataSetDecodeErrorReason.NoError,
                null,
                null);

            Assert.That(args.DecodeErrorReason, Is.EqualTo(DataSetDecodeErrorReason.NoError));
        }

        [Test]
        public void ConstructorSetsMetadataMajorVersionReason()
        {
            var args = new DataSetDecodeErrorEventArgs(
                DataSetDecodeErrorReason.MetadataMajorVersion,
                null,
                null);

            Assert.That(
                args.DecodeErrorReason,
                Is.EqualTo(DataSetDecodeErrorReason.MetadataMajorVersion));
        }

        [Test]
        public void ConstructorSetsNetworkMessage()
        {
            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            var args = new DataSetDecodeErrorEventArgs(
                DataSetDecodeErrorReason.NoError,
                networkMessage,
                null);

            Assert.That(args.UaNetworkMessage, Is.Not.Null);
            Assert.That(
                ReferenceEquals(args.UaNetworkMessage, networkMessage), Is.True);
        }

        [Test]
        public void ConstructorSetsDataSetReader()
        {
            var reader = new DataSetReaderDataType
            {
                Enabled = true,
                Name = "TestReader",
                DataSetWriterId = 1
            };

            var args = new DataSetDecodeErrorEventArgs(
                DataSetDecodeErrorReason.NoError,
                null,
                reader);

            Assert.That(args.DataSetReader, Is.SameAs(reader));
            Assert.That(args.DataSetReader.Name, Is.EqualTo("TestReader"));
        }

        [Test]
        public void ConstructorSetsAllProperties()
        {
            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            var reader = new DataSetReaderDataType { Enabled = true, Name = "Reader1" };

            var args = new DataSetDecodeErrorEventArgs(
                DataSetDecodeErrorReason.MetadataMajorVersion,
                networkMessage,
                reader);

            Assert.That(
                args.DecodeErrorReason,
                Is.EqualTo(DataSetDecodeErrorReason.MetadataMajorVersion));
            Assert.That(
                ReferenceEquals(args.UaNetworkMessage, networkMessage), Is.True);
            Assert.That(
                ReferenceEquals(args.DataSetReader, reader), Is.True);
        }

        [Test]
        public void ConstructorWithNullNetworkMessageAndReaderDoesNotThrow()
        {
            DataSetDecodeErrorEventArgs args = null;
            Assert.DoesNotThrow(() =>
            {
                args = new DataSetDecodeErrorEventArgs(
                    DataSetDecodeErrorReason.NoError,
                    null,
                    null);
            });
            Assert.That(args.UaNetworkMessage, Is.Null);
            Assert.That(args.DataSetReader, Is.Null);
        }

        [Test]
        public void DecodeErrorReasonPropertyIsSettable()
        {
            var args = new DataSetDecodeErrorEventArgs(
                DataSetDecodeErrorReason.NoError,
                null,
                null)
            {
                DecodeErrorReason = DataSetDecodeErrorReason.MetadataMajorVersion
            };
            Assert.That(
                args.DecodeErrorReason,
                Is.EqualTo(DataSetDecodeErrorReason.MetadataMajorVersion));
        }

        [Test]
        public void NetworkMessagePropertyIsSettable()
        {
            var args = new DataSetDecodeErrorEventArgs(
                DataSetDecodeErrorReason.NoError,
                null,
                null);

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            args.UaNetworkMessage = networkMessage;
            Assert.That(
                ReferenceEquals(args.UaNetworkMessage, networkMessage), Is.True);
        }

        [Test]
        public void DataSetReaderPropertyIsSettable()
        {
            var args = new DataSetDecodeErrorEventArgs(
                DataSetDecodeErrorReason.NoError,
                null,
                null);

            var reader = new DataSetReaderDataType { Enabled = true, Name = "NewReader" };
            args.DataSetReader = reader;
            Assert.That(args.DataSetReader, Is.SameAs(reader));
            Assert.That(args.DataSetReader.Name, Is.EqualTo("NewReader"));
        }

        [Test]
        public void InheritsFromEventArgs()
        {
            var args = new DataSetDecodeErrorEventArgs(
                DataSetDecodeErrorReason.NoError,
                null,
                null);

            Assert.That(args, Is.InstanceOf<EventArgs>());
        }
    }
}