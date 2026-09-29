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
using System.IO;
using System.Text;
using NUnit.Framework;
using Opc.Ua.SchemaRegistry.Formats;

namespace Opc.Ua.SchemaRegistry.Tests
{
    [TestFixture]
    [Category("SchemaRegistry")]
    public sealed class ArrowSchemaFormatTests
    {
        [Test]
        public void GoldenSchemaUsesTheExactStoredBytesForItsFingerprint()
        {
            ByteString document = ByteString.FromHexString(
                "FFFFFFFF800000001000000000000A000C000600050008000A000000000104000C000000080008000000040008000000" +
                "040000000100000014000000100014000800000007000C00000010001000000000000002100000001C00000004000000" +
                "00000000020000006964000008000C00080007000800000000000001400000000000000000000000");
            var provider = new ArrowSchemaFormatProvider();
            var schema = (ArrowIpcSchemaContentDataType)provider.Parse(document.Span);
            Assert.That(provider.ComputeSchemaId(document.Span), Is.EqualTo(ByteString.FromHexString("9972E47DBCA6850A")));
            Assert.That(schema.MetadataVersion, Is.EqualTo(4));
            Assert.That(schema.Fields.Count, Is.EqualTo(1));
            ArrowIpcFieldDataType field = schema.Fields[0];
            Assert.That(field.Name, Is.EqualTo("id"));
            Assert.That(field.Nullable, Is.False);
            Assert.That(field.Type.Code, Is.EqualTo(2));
            Assert.That(field.Type.BitWidth, Is.EqualTo(64));
            Assert.That(field.Type.IsSigned, Is.True);
        }

        [TestCase("arrow-metadata.ipc")]
        [TestCase("arrow-families.ipc")]
        public void IpcMetadataRoundTripsWithoutDependingOnExtensionRegistration(string fixture)
        {
            byte[] document = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Assets", fixture));
            var provider = new ArrowSchemaFormatProvider();
            var schema = (ArrowIpcSchemaContentDataType)provider.Parse(document);
            ByteString encoded = provider.Serialize(schema);
            SchemaContentDataType restored = provider.Parse(encoded.Span);
            Assert.That(schema.IsEqual(restored), Is.True);
            if (fixture == "arrow-metadata.ipc")
            {
                Assert.That(schema.Metadata.Count, Is.EqualTo(3));
                Assert.That(schema.Metadata[0].Key, Is.EqualTo(ByteString.From(Encoding.UTF8.GetBytes("k"))));
                Assert.That(schema.Metadata[1].Key, Is.EqualTo(schema.Metadata[0].Key));
                Assert.That(schema.Metadata[0].Value, Is.EqualTo(ByteString.From(Encoding.UTF8.GetBytes("v1"))));
                Assert.That(schema.Metadata[1].Value, Is.EqualTo(ByteString.From(Encoding.UTF8.GetBytes("v2"))));
                Assert.That(schema.Metadata[2].Value, Is.EqualTo(ByteString.FromHexString("FFFE")));
                Assert.That(schema.Fields[2].Name, Is.EqualTo("dup"));
                Assert.That(schema.Fields[3].Name, Is.EqualTo("dup"));
                Assert.Throws<ArgumentException>(() => provider.Select(schema, "dup"));
            }
            else
            {
                Assert.That(schema.Fields.Count, Is.EqualTo(46));
                Assert.That(schema.Fields[43].Metadata.Count, Is.GreaterThanOrEqualTo(2));
            }
        }

        [Test]
        public void SchemaOnlyFramingRejectsTruncationTrailingBytesAndForgedOffsets()
        {
            byte[] original = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "Assets", "arrow-metadata.ipc"));
            var provider = new ArrowSchemaFormatProvider();
            for (int length = 0; length < original.Length; length++)
            {
                int truncatedLength = length;
                Assert.Throws<ArgumentException>(() => provider.Parse(original.AsSpan(0, truncatedLength)),
                    $"Truncation at {length} must fail.");
            }
            byte[] trailing = new byte[original.Length + 8];
            original.CopyTo(trailing, 0);
            Assert.Throws<ArgumentException>(() => provider.Parse(trailing));
            byte[] corrupt = (byte[])original.Clone();
            for (int index = 8; index < 12; index++)
            {
                corrupt[index] = 0xff;
            }
            Assert.Throws<ArgumentException>(() => provider.Parse(corrupt));
        }

        [Test]
        public void MessageMetadataBigEndianAbsentNamesAndDictionaryIdentityRemainNative()
        {
            byte[] original = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "Assets", "arrow-families.ipc"));
            var provider = new ArrowSchemaFormatProvider();
            var schema = (ArrowIpcSchemaContentDataType)provider.Parse(original);
            schema.Endianness = 1;
            schema.MetadataVersion = 3;
            schema.Features = [1, 2];
            schema.Fields[0].HasName = false;
            schema.Fields[0].Name = string.Empty;
            schema.Fields[36].Dictionary.Id = 123456789012345;
            schema.Fields[36].Dictionary.HasIndexType = false;
            schema.Fields[36].Dictionary.IndexBitWidth = 0;
            schema.Fields[36].Dictionary.IndexIsSigned = false;
            schema.Fields[35].Type.HasTypeIds = false;
            schema.Fields[35].Type.TypeIds = [];
            schema.MessageMetadata = [new ArrowKeyValueDataType
            {
                Key = ByteString.FromHexString("00FF"),
                Value = ByteString.FromHexString("FF0080")
            }];
            SchemaContentDataType restored = provider.Parse(provider.Serialize(schema).Span);
            Assert.That(schema.IsEqual(restored), Is.True);
        }

        [Test]
        public void ExtensionBytesAndInactiveParametersAreNeverSilentlyRewritten()
        {
            var provider = new ArrowSchemaFormatProvider();
            byte[] original = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "Assets", "arrow-metadata.ipc"));
            var schema = (ArrowIpcSchemaContentDataType)provider.Parse(original);
            schema.Fields[4].Metadata =
            [
                new ArrowKeyValueDataType
                {
                    Key = ByteString.From(Encoding.UTF8.GetBytes("ARROW:extension:name")),
                    Value = ByteString.From(Encoding.UTF8.GetBytes("arrow.uuid"))
                },
                new ArrowKeyValueDataType
                {
                    Key = ByteString.From(Encoding.UTF8.GetBytes("ARROW:extension:metadata")),
                    Value = ByteString.FromHexString("FF0080")
                }
            ];
            Assert.That(schema.IsEqual(provider.Parse(provider.Serialize(schema).Span)), Is.True);
            schema.Fields[0].Type.Precision = 9;
            Assert.Throws<ArgumentException>(() => provider.Serialize(schema),
                "An integer type cannot discard a populated inactive decimal precision field.");
        }

        [Test]
        public void MutatedMetadataFailsExplicitlyInsteadOfEscapingBoundsChecks()
        {
            byte[] original = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "Assets", "arrow-metadata.ipc"));
            var provider = new ArrowSchemaFormatProvider();
            for (int index = 8; index < original.Length; index += 7)
            {
                byte[] changed = (byte[])original.Clone();
                changed[index] ^= 0x80;
                try
                {
                    SchemaContentDataType parsed = provider.Parse(changed);
                    Assert.That(parsed.IsEqual(provider.Parse(provider.Serialize(parsed).Span)), Is.True);
                }
                catch (ArgumentException)
                {
                    // Invalid source values are expected; unchecked offsets and runtime failures are not.
                }
                catch (ServiceResultException error) when (
                    error.StatusCode == StatusCodes.BadNotSupported ||
                    error.StatusCode == StatusCodes.BadEncodingLimitsExceeded)
                {
                }
            }
        }
    }
}
