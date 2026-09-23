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

namespace Opc.Ua.Types.Tests.Utils
{
    [TestFixture]
    [Parallelizable]
    public sealed class StringTablePrivateViewTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task PrivateMappingIsExecutionLocalAndPreservesConcurrentLiveChanges(bool namespaces)
        {
            StringTable table = CreateTable(namespaces);
            ArrayOf<string> before = table.GetSnapshot(out long version);
            var inspectLive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task live = Task.Run(async () =>
            {
                await inspectLive.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Assert.That(table.ToArrayOf(), Is.EqualTo(before));
                Assert.That(table.Version, Is.EqualTo(version));
                table.Append("urn:live");
            });

            using (table.UsePrivateCopy())
            {
                try
                {
                    table.Append("urn:private");
                    Assert.That(table.GetIndex("urn:private"), Is.EqualTo(2));
                    Assert.That(table.Version, Is.EqualTo(version + 1));
                }
                finally
                {
                    inspectLive.TrySetResult(true);
                }
                await live.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Assert.That(table.GetIndex("urn:live"), Is.EqualTo(-1));
                Assert.That(table.GetString(2), Is.EqualTo("urn:private"));
                Assert.That(table.Count, Is.EqualTo(3));
            }

            Assert.That(table.ToArray(), Is.EqualTo(new[] { Namespaces.OpcUa, "urn:base", "urn:live" }));
            Assert.That(table.GetIndex("urn:private"), Is.EqualTo(-1));
            Assert.That(table.Version, Is.EqualTo(version + 1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PrivateMappingSupportsTheCompleteTableApi(bool namespaces)
        {
            StringTable table = CreateTable(namespaces);
            ArrayOf<string> before = table.GetSnapshot(out long version);

            using (table.UsePrivateCopy())
            {
                table.Update([Namespaces.OpcUa, "urn:updated"]);
                Assert.That(table.Append("urn:appended"), Is.EqualTo(2));
                Assert.That(table.GetIndexOrAppend("urn:appended"), Is.EqualTo(2));
                Assert.That(table.GetIndexOrAppend("urn:inserted"), Is.EqualTo(3));
                Assert.That(table.GetIndex("urn:base"), Is.EqualTo(-1));
                Assert.That(table.GetIndex(string.Empty), Is.EqualTo(-1));
                Assert.That(table.GetString(3), Is.EqualTo("urn:inserted"));
                Assert.That(table.GetString(4), Is.Null);
                Assert.That(table.Count, Is.EqualTo(4));
                Assert.That(table.ToArray(),
                    Is.EqualTo(new[] { Namespaces.OpcUa, "urn:updated", "urn:appended", "urn:inserted" }));
                Assert.That(table.GetSnapshot(out long privateVersion), Is.EqualTo(table.ToArrayOf()));
                Assert.That(privateVersion, Is.EqualTo(version + 3));
                var source = new StringTable([Namespaces.OpcUa, "urn:mapped"]);
                Assert.That(table.CreateMapping(source, false), Is.EqualTo(new ushort[] { 0, ushort.MaxValue }));
                Assert.That(table.CreateMapping(source, true), Is.EqualTo(new ushort[] { 0, 4 }));
                Assert.That(table.GetString(4), Is.EqualTo("urn:mapped"));
                Assert.That(table.Version, Is.EqualTo(version + 4));
            }

            Assert.That(table.ToArrayOf(), Is.EqualTo(before));
            Assert.That(table.Version, Is.EqualTo(version));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NestedPrivateMappingsRestoreTheirParentAcrossAwait(bool namespaces)
        {
            StringTable table = CreateTable(namespaces);
            ArrayOf<string> before = table.GetSnapshot(out long version);

            using (table.UsePrivateCopy())
            {
                table.Append("urn:outer");
                using (IDisposable inner = table.UsePrivateCopy())
                {
                    table.Append("urn:inner");
                    await Task.Yield();
                    Assert.That(table.GetString(2), Is.EqualTo("urn:outer"));
                    Assert.That(table.GetString(3), Is.EqualTo("urn:inner"));
                    inner.Dispose();
                    inner.Dispose();
                    Assert.That(table.GetIndex("urn:inner"), Is.EqualTo(-1));
                    Assert.That(table.GetString(2), Is.EqualTo("urn:outer"));
                    Assert.That(table.Version, Is.EqualTo(version + 1));
                }
            }

            Assert.That(table.ToArrayOf(), Is.EqualTo(before));
            Assert.That(table.Version, Is.EqualTo(version));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CopyConstructorCopiesThePrivateMappingWithoutSharingItsStorage(bool namespaces)
        {
            StringTable table = CreateTable(namespaces);
            StringTable copy;
            using (table.UsePrivateCopy())
            {
                table.Append("urn:private");
                copy = table is NamespaceTable namespaceTable
                    ? new NamespaceTable(namespaceTable)
                    : new StringTable(table);
                table.Append("urn:later");
                Assert.That(copy.Count, Is.EqualTo(3));
            }

            Assert.That(copy.GetString(2), Is.EqualTo("urn:private"));
            Assert.That(copy.GetIndex("urn:later"), Is.EqualTo(-1));
            Assert.That(table.Count, Is.EqualTo(2));
        }

        [Test]
        public void InvalidPrivateNamespaceUpdateStillValidatesTheCandidateAndLeavesTheLiveMappingIntact()
        {
            var table = new NamespaceTable([Namespaces.OpcUa, "urn:base"]);
            ArrayOf<string> before = table.GetSnapshot(out long version);
            using (table.UsePrivateCopy())
            {
                Assert.That(() => table.Update(["urn:not-the-opc-ua-namespace"]),
                    Throws.TypeOf<ArgumentException>());
            }
            Assert.That(table.ToArrayOf(), Is.EqualTo(before));
            Assert.That(table.Version, Is.EqualTo(version));
        }

        private static StringTable CreateTable(bool namespaces)
        {
            return namespaces
                ? new NamespaceTable([Namespaces.OpcUa, "urn:base"])
                : new StringTable([Namespaces.OpcUa, "urn:base"]);
        }
    }
}
