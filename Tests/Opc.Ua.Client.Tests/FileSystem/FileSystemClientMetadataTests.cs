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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;

namespace Opc.Ua.Client.Tests.FileSystem
{
    /// <summary>
    /// End-to-end mock-based tests for <see cref="UaFileInfo.RefreshAsync"/>
    /// (the seven well-known FileType properties).
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [Parallelizable]
    public class FileSystemClientMetadataTests
    {
        [Test]
        public async Task RefreshAsyncPopulatesMandatoryPropertiesAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            var props = new FileProperties
            {
                Size = 12_345UL,
                Writable = true,
                UserWritable = false,
                OpenCount = (ushort)3
            };
            props.Realize();
            harness.RegisterFile(harness.Root, new QualifiedName("data.bin"), properties: props);
            var client = new FileSystemClient(harness.Session, harness.Root);

            UaFileInfo file = await client.GetFileAsync("/data.bin").ConfigureAwait(false);
            await file.RefreshAsync().ConfigureAwait(false);

            Assert.That(file.Size, Is.EqualTo(12_345UL));
            Assert.That(file.Writable, Is.True);
            Assert.That(file.UserWritable, Is.False);
            Assert.That(file.OpenCount, Is.EqualTo(3));
            Assert.That(file.MimeType, Is.Null);
            Assert.That(file.MaxByteStringLength, Is.Null);
            Assert.That(file.LastModifiedTime, Is.Null);
        }

        [Test]
        public async Task RefreshAsyncPopulatesOptionalPropertiesAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            var expectedModified = new DateTime(2024, 5, 12, 13, 0, 0, DateTimeKind.Utc);
            var props = new FileProperties
            {
                Size = 0UL,
                Writable = true,
                UserWritable = true,
                OpenCount = (ushort)0,
                MimeType = "application/json",
                MaxByteStringLength = 64_000u,
                LastModifiedTime = expectedModified
            };
            props.Realize();
            harness.RegisterFile(harness.Root, new QualifiedName("data.json"), properties: props);
            var client = new FileSystemClient(harness.Session, harness.Root);

            UaFileInfo file = await client.GetFileAsync("/data.json").ConfigureAwait(false);
            await file.RefreshAsync().ConfigureAwait(false);

            Assert.That(file.MimeType, Is.EqualTo("application/json"));
            Assert.That(file.MaxByteStringLength, Is.EqualTo(64_000u));
            Assert.That(file.LastModifiedTime, Is.EqualTo(expectedModified));
        }

        [Test]
        public async Task RefreshAsyncToleratesMissingOptionalPropertiesAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            // No properties realised — the harness will return BadNoMatch
            // for every property lookup.
            harness.RegisterFile(harness.Root, new QualifiedName("data.bin"),
                properties: new FileProperties());
            var client = new FileSystemClient(harness.Session, harness.Root);

            UaFileInfo file = await client.GetFileAsync("/data.bin").ConfigureAwait(false);
            await file.RefreshAsync().ConfigureAwait(false);

            // Defaults for non-resolved mandatory properties.
            Assert.That(file.Size, Is.Zero);
            Assert.That(file.Writable, Is.False);
            Assert.That(file.UserWritable, Is.False);
            Assert.That(file.OpenCount, Is.Zero);
            // Optional properties remain null.
            Assert.That(file.MimeType, Is.Null);
            Assert.That(file.MaxByteStringLength, Is.Null);
            Assert.That(file.LastModifiedTime, Is.Null);
        }
    }
}
