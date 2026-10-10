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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Native;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    [TestFixture]
    public sealed class XRegistryNativeSpoolTests
    {
        [Test]
        public async Task SpillingPreservesSeekedContentAndReturnsEveryReservationOnCloseAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), "xregistry-spool-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = new XRegistryBridgeNativeOptions
                {
                    SpoolDirectory = root,
                    MemoryBufferThreshold = 4,
                    MaxSpoolBytes = 10
                };
                var budget = new XRegistryFileBudget(options);
                using (var first = new XRegistryNativeSpool(options, budget))
                {
                    await first.WriteAsync(ByteString.From("1234"u8), CancellationToken.None).ConfigureAwait(false);
                    Assert.That(first.IsSpooled, Is.False);
                    await first.WriteAsync(ByteString.From("56"u8), CancellationToken.None).ConfigureAwait(false);
                    Assert.That(first.IsSpooled, Is.True);
                    first.Position = 1;
                    await first.WriteAsync(ByteString.From("x"u8), CancellationToken.None).ConfigureAwait(false);
                    ByteString contents = await first.MaterializeAsync(CancellationToken.None).ConfigureAwait(false);
                    Assert.That(Encoding.UTF8.GetString(contents.ToArray()), Is.EqualTo("1x3456"));
                    using var rejected = new XRegistryNativeSpool(options, budget);
                    ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await rejected.WriteAsync(ByteString.From("12345"u8), CancellationToken.None).ConfigureAwait(
                            false));
                    Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadOutOfMemory));
                    Assert.That(Directory.GetFiles(root, "*.spool"), Has.Length.EqualTo(1));
                }
                Assert.That(Directory.GetFiles(root, "*.spool"), Is.Empty);
                using var successor = new XRegistryNativeSpool(options, budget);
                await successor.WriteAsync(ByteString.From("1234567890"u8), CancellationToken.None).ConfigureAwait(
                    false);
                Assert.That(successor.Length, Is.EqualTo(10));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public async Task FailedSpoolWritesCannotBeCommittedAsPartialDocumentsAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), "xregistry-spool-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = new XRegistryBridgeNativeOptions
                {
                    SpoolDirectory = root,
                    MemoryBufferThreshold = 0
                };
                using var spool = new XRegistryNativeSpool(options, new XRegistryFileBudget(options));
                using var canceled = new CancellationTokenSource();
                await canceled.CancelAsync().ConfigureAwait(false);
                await Assert.ThatAsync(async () =>
                    await spool.WriteAsync(ByteString.From("not-published"u8), canceled.Token).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await spool.MaterializeAsync(CancellationToken.None).ConfigureAwait(false));
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(Directory.GetFiles(root, "*.spool"), Is.Empty);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }
}
