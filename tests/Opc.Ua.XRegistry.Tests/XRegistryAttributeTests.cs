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
    /// Verifies the model's <c>AttributesType</c> Methods, which the server binds on the
    /// <c>Labels</c> Object of the registry, of every group and of every resource.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryAttributeTests
    {
        [Test]
        public void TheRegistryExposesTheAttributeMethods()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            RegistryState registry = Registry(nm);

            Assert.Multiple(() =>
            {
                Assert.That(registry.Labels, Is.Not.Null);
                Assert.That(registry.Labels!.AddAttribute, Is.Not.Null);
                Assert.That(registry.Labels!.RemoveAttribute, Is.Not.Null);
            });
        }

        [Test]
        public async Task GroupsAndResourcesExposeTheAttributeMethodsAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();

            CreateGroupMethodStateResult group = await nm
                .OnCreateGroupAsync(nm.SystemContext, null!, NodeId.Null, "labelled", CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult resource = await nm
                .OnCreateResourceAsync(
                    nm.SystemContext, null!, group.GroupNodeId, "r1", "1", false, CancellationToken.None)
                .ConfigureAwait(false);

            var groupState = (GroupState?)nm.Find(group.GroupNodeId);
            var resourceState = (ResourceState?)nm.Find(resource.ResourceNodeId);
            Assert.Multiple(() =>
            {
                Assert.That(groupState!.Labels!.AddAttribute, Is.Not.Null);
                Assert.That(groupState.Labels!.RemoveAttribute, Is.Not.Null);
                Assert.That(resourceState!.Labels!.AddAttribute, Is.Not.Null);
                Assert.That(resourceState.Labels!.RemoveAttribute, Is.Not.Null);
                Assert.That(resourceState.MetaLabels!.AddAttribute, Is.Not.Null);
                Assert.That(resourceState.MetaLabels!.RemoveAttribute, Is.Not.Null);
                Assert.That(resourceState.MetaCreatedAt, Is.Not.Null);
                Assert.That(resourceState.MetaModifiedAt, Is.Not.Null);
            });
        }

        [Test]
        public async Task AddAttributePublishesTheLabelAndBumpsTheEpochAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            RegistryState registry = Registry(nm);
            uint epoch = registry.Epoch!.Value;

            AddAttributeMethodStateResult result = await AddAsync(nm, registry, "owner", "plant-1", epoch)
                .ConfigureAwait(false);

            PropertyState<string>? label = FindLabel(nm, registry, "owner");
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(label, Is.Not.Null);
                Assert.That(label!.Value, Is.EqualTo("plant-1"));
                Assert.That(registry.Epoch!.Value, Is.EqualTo(epoch + 1),
                    "A label change mutates the node, so the epoch has to advance.");
                Assert.That(nm.Find(label.NodeId), Is.Not.Null,
                    "The label is addressable, not just an in-memory child.");
            });
        }

        [Test]
        public async Task AddAttributeReplacesAnExistingLabelAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            RegistryState registry = Registry(nm);

            await AddAsync(nm, registry, "owner", "plant-1", registry.Epoch!.Value).ConfigureAwait(false);
            AddAttributeMethodStateResult second =
                await AddAsync(nm, registry, "owner", "plant-2", registry.Epoch!.Value).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(second.ServiceResult), Is.True);
                Assert.That(FindLabel(nm, registry, "owner")!.Value, Is.EqualTo("plant-2"));
            });
        }

        [Test]
        public async Task AddAttributeRejectsAnEmptyKeyAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            RegistryState registry = Registry(nm);

            AddAttributeMethodStateResult result =
                await AddAsync(nm, registry, string.Empty, "v", registry.Epoch!.Value).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task AddAttributeRejectsAStaleEpochAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            RegistryState registry = Registry(nm);

            AddAttributeMethodStateResult result =
                await AddAsync(nm, registry, "owner", "v", registry.Epoch!.Value + 5).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(FindLabel(nm, registry, "owner"), Is.Null);
            });
        }

        [Test]
        public async Task RemoveAttributeDropsTheLabelAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            RegistryState registry = Registry(nm);
            await AddAsync(nm, registry, "owner", "plant-1", registry.Epoch!.Value).ConfigureAwait(false);
            NodeId labelNodeId = FindLabel(nm, registry, "owner")!.NodeId;

            RemoveAttributeMethodStateResult result =
                await RemoveAsync(nm, registry, "owner", registry.Epoch!.Value).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(FindLabel(nm, registry, "owner"), Is.Null);
                Assert.That(nm.Find(labelNodeId), Is.Null, "The label is unpublished from the address space.");
            });
        }

        [Test]
        public async Task RemoveAttributeReportsNotFoundForAnUnknownKeyAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            RegistryState registry = Registry(nm);

            RemoveAttributeMethodStateResult result =
                await RemoveAsync(nm, registry, "missing", registry.Epoch!.Value).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task RemoveAttributeRejectsAStaleEpochAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            RegistryState registry = Registry(nm);
            await AddAsync(nm, registry, "owner", "plant-1", registry.Epoch!.Value).ConfigureAwait(false);

            RemoveAttributeMethodStateResult result =
                await RemoveAsync(nm, registry, "owner", registry.Epoch!.Value + 3).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(FindLabel(nm, registry, "owner"), Is.Not.Null);
            });
        }

        [Test]
        public async Task AddAttributeWithEpochZeroForcesTheChangeAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            RegistryState registry = Registry(nm);

            // Epoch starts at 1 and only ever increments, so a naive equality check would make 0
            // permanently unusable. The model defines 0 as "do not check".
            AddAttributeMethodStateResult result =
                await AddAsync(nm, registry, "owner", "plant-1", 0).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(FindLabel(nm, registry, "owner")!.Value, Is.EqualTo("plant-1"));
            });
        }

        [Test]
        public async Task RemoveAttributeWithEpochZeroForcesTheChangeAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            RegistryState registry = Registry(nm);
            await AddAsync(nm, registry, "owner", "plant-1", registry.Epoch!.Value).ConfigureAwait(false);

            RemoveAttributeMethodStateResult result =
                await RemoveAsync(nm, registry, "owner", 0).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(FindLabel(nm, registry, "owner"), Is.Null);
            });
        }

        private static ValueTask<AddAttributeMethodStateResult> AddAsync(
            XRegistryRegistrationNodeManager nm,
            RegistryState registry,
            string key,
            string value,
            uint expectedEpoch)
        {
            return nm.OnAddAttributeAsync(registry.Labels!, registry.Epoch, key, value, expectedEpoch);
        }

        private static ValueTask<RemoveAttributeMethodStateResult> RemoveAsync(
            XRegistryRegistrationNodeManager nm,
            RegistryState registry,
            string key,
            uint expectedEpoch)
        {
            return nm.OnRemoveAttributeAsync(registry.Labels!, registry.Epoch, key, expectedEpoch);
        }

        private static PropertyState<string>? FindLabel(
            XRegistryRegistrationNodeManager nm,
            RegistryState registry,
            string key)
        {
            return registry.Labels!.FindChild(nm.SystemContext, new QualifiedName(key, NamespaceIndex(nm)))
                as PropertyState<string>;
        }

        private static RegistryState Registry(XRegistryRegistrationNodeManager nm)
        {
            return (RegistryState)nm.Find(
                new NodeId(XRegistryWellKnown.RegistryObject, NamespaceIndex(nm)))!;
        }

        private static ushort NamespaceIndex(XRegistryRegistrationNodeManager nm)
        {
            return (ushort)nm.SystemContext.NamespaceUris.GetIndex(
                XRegistryWellKnown.XRegistryNamespaceUri);
        }

        private static XRegistryRegistrationNodeManager CreateAddressSpace()
        {
            var options = new XRegistryServerOptions();
            Mock<IServerInternal> server =
                XRegistryServerTestHarness.CreateServer(options.RegistryNamespaceUri);
            var nm = new XRegistryRegistrationNodeManager(server.Object, null!, options);
            nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());
            return nm;
        }
    }
}
