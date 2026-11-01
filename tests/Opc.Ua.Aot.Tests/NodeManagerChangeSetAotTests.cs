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

using System.Text;
using Opc.Ua.Server;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises the slot change-set and committed recovery contract in the native executable.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public sealed class NodeManagerChangeSetAotTests(AotTestFixture fixture)
    {
        /// <summary>
        /// Publishes, replaces, reconciles and physically retires the same stable slot without runtime generation.
        /// </summary>
        [Test]
        public async Task SlotChangeSetPublicationAndRecoveryIsAotSafeAsync()
        {
            string namespaceUri = "urn:aot:lifecycle:" + Guid.NewGuid().ToString("N");
            INodeManagerLifecycle lifecycle = fixture.ServerFixture.Server.NodeManagerLifecycle;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateFactory(namespaceUri))]).ConfigureAwait(false);
            NodeManagerSlot slot = added.Registrations[0].Slot;
            LifecycleResult current = added;
            try
            {
                await using ILifecycleWriter writer = await lifecycle.BeginAsync(added.Revision).ConfigureAwait(false);
                await using (IPreparedChanges aborted = await writer.PrepareAsync(
                    [LifecycleChange.Replace(slot, CreateFactory(namespaceUri))]).ConfigureAwait(false))
                {
                    await Assert.That(aborted.IsCommitted).IsFalse();
                }
                await Assert.That(lifecycle.Revision).IsEqualTo(added.Revision);
                await using IPreparedChanges replacement = await writer.PrepareAsync(
                    [LifecycleChange.Replace(slot, CreateFactory(namespaceUri))]).ConfigureAwait(false);
                replacement.SetMetadata(slot, new ChangeSetReadImage(replacement.Registrations[0].NodeManager));
                int decisions = 0;
                int publications = 0;
                current = await replacement.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }, () =>
                {
                    if (++publications == 1)
                    {
                        throw new InvalidOperationException("AOT committed callback failure.");
                    }
                }).ConfigureAwait(false);
                await Assert.That(replacement.IsCommitted).IsTrue();
                await Assert.That(current.Registrations[0].Slot).IsSameReferenceAs(slot);
                await Assert.That(current.Registrations[0].Generation).IsEqualTo(2);
                await Assert.That(current.Issues.Count).IsEqualTo(1);
                await Assert.That(current.Issues[0].IsRetryable).IsTrue();
                await current.RetryReconciliationAsync().ConfigureAwait(false);
                await Assert.That(current.Issues.Count).IsEqualTo(0);
                await Assert.That(current.CleanupFailure).IsNotNull();
                await Assert.That(decisions).IsEqualTo(1);
                await Assert.That(publications).IsEqualTo(2);
                await Assert.That((added.PublishedSlots.ToArray() ?? [])
                    .Single(value => value.Slot == slot).Generation).IsEqualTo(1);
                await Assert.That(writer.IsCurrent).IsTrue();
            }
            finally
            {
                LifecycleResult removed = await lifecycle.ApplyAsync([LifecycleChange.Remove(slot)])
                    .ConfigureAwait(false);
                await removed.Retired.ConfigureAwait(false);
                await current.Retired.ConfigureAwait(false);
                await Assert.That(slot.TryGetCurrent(out _)).IsFalse();
                await Assert.That(removed.RetirementStatus).IsEqualTo(NodeManagerRetirementStatus.Completed);
            }
        }

        /// <summary>
        /// Constructs a reload-capable runtime NodeSet factory without reflection or dynamic code.
        /// </summary>
        /// <param name="namespaceUri">The test's isolated namespace.</param>
        /// <returns>The candidate factory.</returns>
        private static RuntimeNodeSetNodeManagerFactory CreateFactory(string namespaceUri)
        {
            string xml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">" +
                "<NamespaceUris><Uri>" +
                namespaceUri +
                "</Uri></NamespaceUris>" +
                "<Models><Model ModelUri=\"" +
                namespaceUri +
                "\" /></Models>" +
                "<UAObject NodeId=\"ns=1;i=1\" BrowseName=\"1:Root\">" +
                "<DisplayName>Root</DisplayName></UAObject></UANodeSet>";
            return new RuntimeNodeSetNodeManagerFactory(new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        "Lifecycle AOT",
                        _ => new ValueTask<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(xml))),
                        [namespaceUri])
                ]
            });
        }

        /// <summary>
        /// Carries typed generation metadata through the native prepared unit.
        /// </summary>
        /// <param name="Owner">The exact unpublished candidate.</param>
        private sealed record ChangeSetReadImage(IAsyncNodeManager Owner) : INodeManagerReadImage;
    }
}
