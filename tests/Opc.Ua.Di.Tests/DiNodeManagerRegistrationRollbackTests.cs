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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// A device creation through <see cref="DiNodeManager"/> that fails while
    /// the device is registered leaves nothing behind. The device is attached
    /// to its parent before it is registered, so a failure that left it there
    /// kept its name taken: a retry was rejected with BadBrowseNameDuplicated.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("DeviceBuilder")]
    public sealed class DiNodeManagerRegistrationRollbackTests
    {
        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new StandardServer(t))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            StandardServer server = await m_fixture.StartAsync().ConfigureAwait(false);
            m_manager = new RollbackProbeNodeManager(server.CurrentInstance, m_fixture.Config);
            await m_manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                .ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            m_manager?.Dispose();
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [TearDown]
        public void ResetProbe()
        {
            m_manager.Registering = null;
            m_manager.FailRemoval = false;
        }

        [Test]
        public async Task ACancelledDeviceCreationIsDetachedSoTheNameCanBeReusedAsync()
        {
            NodeState deviceSet = DeviceSet();
            QualifiedName name = Name("Cancelled");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await m_manager.CreateDeviceAsync(name, cancellationToken: cancelled.Token)
                    .ConfigureAwait(false));

            Assert.That(
                deviceSet.FindChild(m_manager.SystemContext, name),
                Is.Null,
                "the DeviceSet drops the device");
            await AssertTheNameCanBeUsedAgainAsync(deviceSet, name).ConfigureAwait(false);
        }

        [Test]
        public async Task ADeviceWhoseChildFailsToCreateIsDetachedSoTheNameCanBeReusedAsync()
        {
            NodeState deviceSet = DeviceSet();
            QualifiedName name = Name("BrokenChild");
            DeviceState? failed = null;
            bool attached = false;

            InvalidOperationException? error = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await CreateDeviceWithExtensionAsync(
                    name,
                    device => new FailingChildState(device, () =>
                    {
                        failed = device;
                        attached = ReferenceEquals(deviceSet.FindChild(m_manager.SystemContext, name), device);
                    })).ConfigureAwait(false));

            Assert.That(failed, Is.Not.Null, "the child's create lifecycle ran");
            Assert.Multiple(() =>
            {
                Assert.That(error!.Message, Is.EqualTo("Registration failed."), "the failure is rethrown");
                Assert.That(attached, Is.True, "the device was attached when it failed");
                Assert.That(
                    deviceSet.FindChild(m_manager.SystemContext, name),
                    Is.Null,
                    "the DeviceSet drops the device");
                Assert.That(m_manager.FindPredefinedNode(failed!.NodeId), Is.Null, "no device node remains");
            });

            await AssertTheNameCanBeUsedAgainAsync(deviceSet, name).ConfigureAwait(false);
        }

        [Test]
        public async Task ADeviceThatFailsOnceRegisteredIsDeletedSoTheNameCanBeReusedAsync()
        {
            NodeState deviceSet = DeviceSet();
            QualifiedName name = Name("Broken");
            BaseObjectState? extension = null;
            DeviceState? failed = null;
            NodeId manufacturer = NodeId.Null;
            bool attached = false;
            bool registered = false;
            m_manager.Registering = node =>
            {
                if (!ReferenceEquals(node, extension))
                {
                    return;
                }
                failed = (DeviceState)extension!.Parent!;
                manufacturer = failed.Manufacturer!.NodeId;
                attached = ReferenceEquals(deviceSet.FindChild(m_manager.SystemContext, name), failed);
                registered = ReferenceEquals(m_manager.FindPredefinedNode(failed.NodeId), failed) &&
                    m_manager.FindPredefinedNode(manufacturer) != null;
                throw new InvalidOperationException("Registration failed.");
            };

            InvalidOperationException? error = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await CreateDeviceWithExtensionAsync(
                    name,
                    device => extension = new BaseObjectState(device)).ConfigureAwait(false));

            Assert.That(failed, Is.Not.Null, "the registration reached the extension");
            Assert.Multiple(() =>
            {
                Assert.That(error!.Message, Is.EqualTo("Registration failed."), "the failure is rethrown");
                Assert.That(attached && registered, Is.True, "the device was registered when it failed");
                Assert.That(
                    deviceSet.FindChild(m_manager.SystemContext, name),
                    Is.Null,
                    "the DeviceSet drops the device");
                Assert.That(m_manager.FindPredefinedNode(failed!.NodeId), Is.Null, "the device node is deleted");
                Assert.That(
                    m_manager.FindPredefinedNode(manufacturer),
                    Is.Null,
                    "the device's children are deleted");
            });

            await AssertTheNameCanBeUsedAgainAsync(deviceSet, name).ConfigureAwait(false);
        }

        [Test]
        public void AFailingRollbackDoesNotReplaceTheOriginalFailure()
        {
            NodeState deviceSet = DeviceSet();
            QualifiedName name = Name("BrokenTwice");
            BaseObjectState? extension = null;
            bool registered = false;
            m_manager.Registering = node =>
            {
                if (ReferenceEquals(node, extension))
                {
                    registered = m_manager.FindPredefinedNode(((BaseInstanceState)node).Parent!.NodeId) != null;
                    throw new InvalidOperationException("Registration failed.");
                }
            };
            m_manager.FailRemoval = true;

            InvalidOperationException? error = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await CreateDeviceWithExtensionAsync(
                    name,
                    device => extension = new BaseObjectState(device)).ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(registered, Is.True, "the device was registered when it failed");
                Assert.That(error!.Message, Is.EqualTo("Registration failed."), "the original failure is rethrown");
                Assert.That(
                    deviceSet.FindChild(m_manager.SystemContext, name),
                    Is.Null,
                    "the DeviceSet drops the device even when deleting it fails");
            });
        }

        [Test]
        public void AFailingDetachDoesNotReplaceTheOriginalFailure()
        {
            // The parent is supplied by the caller and RemoveChild is virtual.
            // A detach that threw during the rollback escaped and replaced the
            // exception that failed the creation.
            using var logs = new RecordingLoggerProvider(LogLevel.Error);
            m_manager.Server.Telemetry.LoggerFactory.AddProvider(logs);
            var parent = new DetachRefusingState(Name("DetachRefusingParent"));
            BaseObjectState? extension = null;
            m_manager.Registering = node =>
            {
                if (ReferenceEquals(node, extension))
                {
                    throw new InvalidOperationException("Registration failed.");
                }
            };

            InvalidOperationException? error = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await CreateDeviceWithExtensionAsync(
                    Name("DetachRefused"),
                    device => extension = new BaseObjectState(device),
                    parent).ConfigureAwait(false));

            RecordedLogRecord[] detachFailures =
                [.. logs.Records.Where(r => r.Exception?.Message == DetachRefusingState.Refusal)];
            Assert.Multiple(() =>
            {
                Assert.That(extension, Is.Not.Null, "the registration reached the extension");
                Assert.That(error!.Message, Is.EqualTo("Registration failed."), "the original failure surfaces");
                Assert.That(
                    detachFailures,
                    Has.Length.EqualTo(2),
                    "the delete and the detach both fail on the parent, and both are logged");
                Assert.That(
                    detachFailures.Select(r => r.LogLevel),
                    Is.All.EqualTo(LogLevel.Error),
                    "the cleanup failure is an error");
            });
        }

        /// <summary>
        /// Creates a device of <c>DeviceType</c> through the factory overload
        /// with one more child, which the device factory adds last.
        /// </summary>
        private ValueTask<IDeviceBuilder<DeviceState>> CreateDeviceWithExtensionAsync(
            QualifiedName name,
            Func<DeviceState, BaseObjectState> createExtension,
            NodeState? parent = null)
        {
            return m_manager.CreateDeviceAsync(
                name,
                NodeId.Create(
                    Opc.Ua.Di.ObjectTypes.DeviceType,
                    DiNodeManager.DiNamespaceUri,
                    m_manager.Server.NamespaceUris),
                parent =>
                {
                    DeviceState device = m_manager.SystemContext.CreateInstanceOfDeviceType(parent, name);
                    BaseObjectState extension = createExtension(device);
                    extension.BrowseName = new QualifiedName("Extension", name.NamespaceIndex);
                    extension.DisplayName = new LocalizedText("Extension");
                    extension.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
                    extension.TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType;
                    device.AddChild(extension);
                    return device;
                },
                parent);
        }

        private QualifiedName Name(string name)
        {
            return new QualifiedName(name, m_manager.DiNamespaceIndex);
        }

        private NodeState DeviceSet()
        {
            return m_manager.FindPredefinedNode(NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet,
                DiNodeManager.DiNamespaceUri,
                m_manager.Server.NamespaceUris))!;
        }

        private async Task AssertTheNameCanBeUsedAgainAsync(NodeState deviceSet, QualifiedName name)
        {
            IDeviceBuilder<DeviceState> retry = await m_manager.CreateDeviceAsync(name).ConfigureAwait(false);

            Assert.That(
                deviceSet.FindChild(m_manager.SystemContext, name),
                Is.SameAs(retry.Device),
                "the name can be used again");
        }

        private ServerFixture<StandardServer> m_fixture = null!;
        private RollbackProbeNodeManager m_manager = null!;

        /// <summary>
        /// A DI manager subclass that lets a test fail the registration of a
        /// chosen node, and fail the cleanup that follows.
        /// </summary>
        private sealed class RollbackProbeNodeManager : DiNodeManager
        {
            public RollbackProbeNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
                : base(server, configuration)
            {
            }

            /// <summary>
            /// Called for every node before it is indexed; may throw to fail
            /// the registration at that node.
            /// </summary>
            public Action<NodeState>? Registering { get; set; }

            /// <summary>
            /// Makes the removal of registered nodes fail.
            /// </summary>
            public bool FailRemoval { get; set; }

            protected override ValueTask<NodeState> AddBehaviourToPredefinedNodeAsync(
                ISystemContext context,
                NodeState predefinedNode,
                CancellationToken cancellationToken = default)
            {
                Registering?.Invoke(predefinedNode);
                return base.AddBehaviourToPredefinedNodeAsync(context, predefinedNode, cancellationToken);
            }

            protected override ValueTask RemovePredefinedNodeAsync(
                ISystemContext context,
                NodeState node,
                List<LocalReference> referencesToRemove,
                CancellationToken cancellationToken = default)
            {
                if (FailRemoval)
                {
                    throw new InvalidOperationException("Removal failed.");
                }
                return base.RemovePredefinedNodeAsync(context, node, referencesToRemove, cancellationToken);
            }
        }

        /// <summary>
        /// A device child whose create lifecycle fails, the way a node added
        /// by a device factory can fail while the device is registered.
        /// </summary>
        private sealed class FailingChildState : BaseObjectState
        {
            public FailingChildState(NodeState parent, Action creating)
                : base(parent)
            {
                m_creating = creating;
            }

            protected override void OnAfterCreate(
                ISystemContext context,
                NodeState node,
                CancellationToken ct = default)
            {
                m_creating();
                throw new InvalidOperationException("Registration failed.");
            }

            private readonly Action m_creating;
        }

        /// <summary>
        /// A parent whose detach fails, the way an arbitrary caller supplied
        /// parent overriding <see cref="NodeState.RemoveChild"/> can fail.
        /// </summary>
        private sealed class DetachRefusingState : BaseObjectState
        {
            public const string Refusal = "Detach refused.";

            public DetachRefusingState(QualifiedName browseName)
                : base(null)
            {
                NodeId = new NodeId("DetachRefusingParent", browseName.NamespaceIndex);
                BrowseName = browseName;
                DisplayName = new LocalizedText("DetachRefusingParent");
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType;
            }

            public override void RemoveChild(BaseInstanceState child)
            {
                throw new NotSupportedException(Refusal);
            }
        }
    }
}
