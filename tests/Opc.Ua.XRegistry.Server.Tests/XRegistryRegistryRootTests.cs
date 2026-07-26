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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;

namespace Opc.Ua.XRegistry.Server.Tests
{
    /// <summary>
    /// Verifies the registry root the server materializes from the compiled model, and the group
    /// lifecycle the model declares on <c>RegistryType</c>: <c>CreateGroup</c> is strict and
    /// <c>GetOrCreateGroup</c> is idempotent.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryRegistryRootTests
    {
        /// <summary>
        /// The root has to come from the source-generated type factory so it carries the type's
        /// mandatory children; a bare state would leave the lifecycle Methods unbound.
        /// </summary>
        [Test]
        public void RegistryRootIsPublishedWithItsModelMetadata()
        {
            using XRegistryRegistrationNodeManager nm = CreateNodeManager(new XRegistryServerOptions
            {
                RegistryBrowseName = "MyRegistry",
                RegistryId = "urn:example:registry",
                SpecVersion = "1.2.3"
            });

            nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

            var registry = (RegistryState?)nm.Find(RegistryNodeId(nm));
            Assert.That(registry, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(registry!.BrowseName.Name, Is.EqualTo("MyRegistry"));
                Assert.That(registry.RegistryId, Is.Not.Null);
                Assert.That(registry.RegistryId!.Value, Is.EqualTo("urn:example:registry"));
                Assert.That(registry.SpecVersion!.Value, Is.EqualTo("1.2.3"));
                Assert.That(registry.Epoch!.Value, Is.EqualTo(1u));
                Assert.That(registry.CreateGroup, Is.Not.Null);
                Assert.That(registry.GetOrCreateGroup, Is.Not.Null);
            });
        }

        [Test]
        public async Task CreateGroupPublishesAGroupFromTheModelAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();

            CreateGroupMethodStateResult result = await CreateGroupAsync(nm, "schemas")
                .ConfigureAwait(false);

            var group = (GroupState?)nm.Find(result.GroupNodeId);
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(result.GroupNodeId.IsNull, Is.False);
                Assert.That(group, Is.Not.Null);
                Assert.That(group!.GroupId!.Value, Is.EqualTo("schemas"));
                Assert.That(group.Epoch!.Value, Is.EqualTo(1u));
                Assert.That(group.CreateResource, Is.Not.Null,
                    "The group exposes the model's resource lifecycle.");
                Assert.That(group.GetOrCreateResource, Is.Not.Null);
            });
        }

        [Test]
        public async Task CreateGroupRejectsADuplicateGroupIdAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();

            CreateGroupMethodStateResult first = await CreateGroupAsync(nm, "schemas").ConfigureAwait(false);
            CreateGroupMethodStateResult second = await CreateGroupAsync(nm, "schemas").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(first.ServiceResult), Is.True);
                Assert.That(second.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdExists));
            });
        }

        [Test]
        public async Task CreateGroupRejectsAnEmptyGroupIdAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();

            CreateGroupMethodStateResult result = await CreateGroupAsync(nm, string.Empty)
                .ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task GetOrCreateGroupIsIdempotentAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();

            GetOrCreateGroupMethodStateResult created = await GetOrCreateGroupAsync(nm, "schemas")
                .ConfigureAwait(false);
            GetOrCreateGroupMethodStateResult fetched = await GetOrCreateGroupAsync(nm, "schemas")
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(created.Created, Is.True);
                Assert.That(fetched.Created, Is.False, "The second call returns the existing group.");
                Assert.That(fetched.GroupNodeId, Is.EqualTo(created.GroupNodeId));
            });
        }

        [Test]
        public async Task GetOrCreateGroupRejectsAnEmptyGroupIdAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();

            GetOrCreateGroupMethodStateResult result = await GetOrCreateGroupAsync(nm, string.Empty)
                .ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task CreatedGroupsUseTheDynamicInstanceRangeAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();

            CreateGroupMethodStateResult first = await CreateGroupAsync(nm, "a").ConfigureAwait(false);
            CreateGroupMethodStateResult second = await CreateGroupAsync(nm, "b").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first.GroupNodeId.TryGetValue(out uint firstId), Is.True);
                Assert.That(firstId, Is.GreaterThanOrEqualTo(XRegistryWellKnown.FirstDynamicInstance),
                    "Runtime instances must not fall into the compiled model's identifier range.");
                Assert.That(second.GroupNodeId, Is.Not.EqualTo(first.GroupNodeId));
            });
        }

        private static ValueTask<CreateGroupMethodStateResult> CreateGroupAsync(
            XRegistryRegistrationNodeManager nm,
            string groupId)
        {
            return nm.OnCreateGroupAsync(nm.SystemContext, null!, NodeId.Null, groupId, CancellationToken.None);
        }

        private static ValueTask<GetOrCreateGroupMethodStateResult> GetOrCreateGroupAsync(
            XRegistryRegistrationNodeManager nm,
            string groupId)
        {
            return nm.OnGetOrCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, groupId, CancellationToken.None);
        }

        private static NodeId RegistryNodeId(XRegistryRegistrationNodeManager nm)
        {
            ushort ns = (ushort)nm.SystemContext.NamespaceUris.GetIndex(
                XRegistryWellKnown.XRegistryNamespaceUri);
            return new NodeId(XRegistryWellKnown.RegistryObject, ns);
        }

        private static XRegistryRegistrationNodeManager CreateAddressSpace()
        {
            XRegistryRegistrationNodeManager nm = CreateNodeManager(new XRegistryServerOptions());
            nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());
            return nm;
        }

        private static XRegistryRegistrationNodeManager CreateNodeManager(XRegistryServerOptions options)
        {
            Mock<IServerInternal> server =
                XRegistryServerTestHarness.CreateServer(options.RegistryNamespaceUri);
            return new XRegistryRegistrationNodeManager(server.Object, null!, options);
        }
    }
}
