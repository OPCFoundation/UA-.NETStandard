/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Verifies how the server assigns a <c>VersionId</c> when the caller leaves it empty: the
    /// identifier has to be one that is not already in use, and the counter that backs it must be
    /// scoped per resource and not grow without bound.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryVersionAssignmentTests
    {
        [Test]
        public async Task AnAssignedVersionSkipsOneTheCallerCreatedExplicitlyAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            NodeId group = await CreateGroupAsync(nm, "schemas").ConfigureAwait(false);

            // Take "1" and "2" explicitly, then let the server assign.
            await CreateAsync(nm, group, "r", "1").ConfigureAwait(false);
            await CreateAsync(nm, group, "r", "2").ConfigureAwait(false);
            CreateResourceMethodStateResult assigned =
                await CreateAsync(nm, group, "r", string.Empty).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(assigned.ServiceResult), Is.True,
                    "An auto-assigned version must not collide with an explicit one.");
                Assert.That(assigned.AssignedVersionId, Is.EqualTo("3"));
            });
        }

        [Test]
        public async Task GetOrCreateWithAnEmptyVersionDoesNotReturnAnUnrelatedVersionAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            NodeId group = await CreateGroupAsync(nm, "schemas").ConfigureAwait(false);
            await CreateAsync(nm, group, "r", "1").ConfigureAwait(false);

            GetOrCreateResourceMethodStateResult result = await nm.OnGetOrCreateResourceAsync(
                nm.SystemContext, null!, group, "r", string.Empty, false, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Created, Is.True,
                    "The pre-existing version 1 must not be silently returned as if it were the " +
                    "one the caller asked the server to assign.");
                Assert.That(result.AssignedVersionId, Is.EqualTo("2"));
            });
        }

        [Test]
        public async Task VersionCountersAreScopedPerGroupAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            NodeId first = await CreateGroupAsync(nm, "a").ConfigureAwait(false);
            NodeId second = await CreateGroupAsync(nm, "b").ConfigureAwait(false);

            CreateResourceMethodStateResult inFirst =
                await CreateAsync(nm, first, "r", string.Empty).ConfigureAwait(false);
            CreateResourceMethodStateResult inSecond =
                await CreateAsync(nm, second, "r", string.Empty).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(inFirst.AssignedVersionId, Is.EqualTo("1"));
                Assert.That(inSecond.AssignedVersionId, Is.EqualTo("1"),
                    "The same resource id in another group starts its own version sequence.");
            });
        }

        [Test]
        public async Task DeletingTheLastVersionResetsTheCounterAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            NodeId group = await CreateGroupAsync(nm, "schemas").ConfigureAwait(false);

            CreateResourceMethodStateResult created =
                await CreateAsync(nm, group, "r", string.Empty).ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;
            await nm.OnDeleteResourceAsync(resource, resource.Epoch!.Value).ConfigureAwait(false);

            CreateResourceMethodStateResult again =
                await CreateAsync(nm, group, "r", string.Empty).ConfigureAwait(false);

            Assert.That(again.AssignedVersionId, Is.EqualTo("1"),
                "The counter is pruned with the last version, so it cannot grow unboundedly " +
                "across a create/delete loop with fresh ids.");
        }

        [Test]
        public async Task AnExistingVersionKeepsTheCounterAdvancingAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            NodeId group = await CreateGroupAsync(nm, "schemas").ConfigureAwait(false);

            CreateResourceMethodStateResult first =
                await CreateAsync(nm, group, "r", string.Empty).ConfigureAwait(false);
            CreateResourceMethodStateResult second =
                await CreateAsync(nm, group, "r", string.Empty).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first.AssignedVersionId, Is.EqualTo("1"));
                Assert.That(second.AssignedVersionId, Is.EqualTo("2"),
                    "The counter is only pruned once the resource has no versions left.");
            });
        }

        private static ValueTask<CreateResourceMethodStateResult> CreateAsync(
            XRegistryRegistrationNodeManager nm,
            NodeId group,
            string resourceId,
            string versionId)
        {
            return nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group, resourceId, versionId, false, CancellationToken.None);
        }

        private static async Task<NodeId> CreateGroupAsync(
            XRegistryRegistrationNodeManager nm,
            string groupId)
        {
            CreateGroupMethodStateResult result = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, groupId, CancellationToken.None)
                .ConfigureAwait(false);
            return result.GroupNodeId;
        }

        private static XRegistryRegistrationNodeManager CreateAddressSpace()
        {
            var options = new XRegistryServerOptions
            {
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider()
            };
            Mock<IServerInternal> server =
                XRegistryServerTestHarness.CreateServer(options.RegistryNamespaceUri);
            var nm = new XRegistryRegistrationNodeManager(server.Object, null!, options);
            nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());
            return nm;
        }
    }
}
