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
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Registry
{
    [TestFixture]
    public sealed class WotPreparedLifecycleMutationTests
    {
        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                nameof(WotPreparedLifecycleMutationTests), Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task MutationImageUsesTheExistingDecisionAndPreservesDeletedChangeIdentity(bool delete, bool commit)
        {
            using var store = new FileWotRegistryStore(m_root);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotRegistryMutationResult added = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "mutation-source", VersionId = "v1", Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(TestMaterialization.Td("urn:mutation-source"))
            }).ConfigureAwait(false);
            Assert.That(added.Changed, Is.True, added.Message);
            WotResource resource = added.Resource!;
            WotRegistrySnapshot previous = registry.Current;
            WotResourceGroup group = previous.FindGroup(resource.GroupId)!;
            WotRegistrySnapshot desired = previous.WithGroup(group.WithResources(
                delete ? group.Resources.Remove(resource.ResourceId) :
                    group.Resources.SetItem(resource.ResourceId, resource.With(enabled: false)),
                group.Epoch), previous.Generation);
            var mutation = new WotRegistryMutationImage(previous, desired, [resource.Xid]);
            var changes = new List<WotRegistryChangedEventArgs>();
            registry.Changed += (_, change) => changes.Add(change);
            await using (IWotRegistryPublication invocation = await registry.BeginPublicationAsync().ConfigureAwait(false))
            {
                Assert.That(invocation, Is.InstanceOf<IWotRegistryMutationPublication>());
                await using IWotPreparedRegistryPublication prepared =
                    await ((IWotRegistryMutationPublication)invocation).PrepareMutationAsync(
                        mutation, [], previous.RefreshGeneration).ConfigureAwait(false);
                Assert.That(registry.Current, Is.SameAs(previous));
                Assert.That(changes, Is.Empty);
                Assert.That(prepared.IntendedSnapshot.Generation, Is.EqualTo(previous.Generation + 1));
                if (commit)
                {
                    await prepared.DecideAsync().ConfigureAwait(false);
                    Assert.That(registry.Current, Is.SameAs(previous));
                    Assert.That(changes, Is.Empty);
                    prepared.Publish();
                }
            }
            if (commit)
            {
                Assert.That(changes, Has.Count.EqualTo(1));
                Assert.That(changes[0].ProjectionOnly, Is.False);
                Assert.That(changes[0].ChangedResourceXids, Is.EquivalentTo(new[] { resource.Xid }));
                Assert.That(registry.Current.RefreshGeneration, Is.EqualTo(previous.RefreshGeneration));
            }
            else
            {
                Assert.That(registry.Current, Is.SameAs(previous));
                Assert.That(changes, Is.Empty);
            }
            using var reopened = new FileWotRegistryStore(m_root);
            WotRegistrySnapshot durable = await reopened.LoadAsync().ConfigureAwait(false);
            Assert.That(durable.Generation, Is.EqualTo(previous.Generation + (commit ? 1 : 0)));
            WotResource? restored = durable.FindResourceByXid(resource.Xid);
            if (commit && delete)
            {
                Assert.That(restored, Is.Null);
            }
            else
            {
                Assert.That(restored, Is.Not.Null);
                Assert.That(restored!.Enabled, Is.EqualTo(!commit));
            }
        }

        private string m_root = null!;
    }
}
