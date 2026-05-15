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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test <see cref="MasterNodeManager"/>
    /// </summary>
    [TestFixture]
    [Category("MasterNodeManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class MasterNodeManagerTests
    {
        [Test]
        public void ValidateRolePermissions_NullNodeMetadata_ReturnsGood()
        {
            ServiceResult result = MasterNodeManager.ValidateRolePermissions(null, null, PermissionType.Read);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ValidateRolePermissions_PermissionNone_ReturnsGood()
        {
            var nodeMetadata = new NodeMetadata(null, new NodeId(1));
            ServiceResult result = MasterNodeManager.ValidateRolePermissions(null, nodeMetadata, PermissionType.None);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ValidateRolePermissions_NoRestrictions_ReturnsGood()
        {
            var nodeMetadata = new NodeMetadata(null, new NodeId(1));
            ServiceResult result = MasterNodeManager.ValidateRolePermissions(null, nodeMetadata, PermissionType.Read);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ValidateRolePermissions_NoGrantedRoles_ReturnsBadUserAccessDenied()
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(x => x.GrantedRoleIds).Returns(new ArrayOf<NodeId>());
            var context = new OperationContext(new RequestHeader(), null, RequestType.Read, null, identity.Object);

            var nodeMetadata = new NodeMetadata(null, new NodeId(1))
            {
                RolePermissions = [
                    new RolePermissionType { RoleId = new NodeId(1), Permissions = (uint)PermissionType.Read }
                ]
            };

            var loggerMock = new Mock<ILogger>();
            ServiceResult result = MasterNodeManager.ValidateRolePermissions(context, nodeMetadata, PermissionType.Read, loggerMock.Object);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Current user has no granted role.")),
                    It.IsAny<Exception>(),
                    (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()),
                Times.Once);
        }

        [Test]
        public void ValidateRolePermissions_DoesNotHaveRequestedPermission_ReturnsBadUserAccessDenied()
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(x => x.GrantedRoleIds).Returns([new NodeId(2)]);
            var context = new OperationContext(new RequestHeader(), null, RequestType.Read, null, identity.Object);

            var nodeMetadata = new NodeMetadata(null, new NodeId(1))
            {
                RolePermissions = [
                    new RolePermissionType { RoleId = new NodeId(2), Permissions = (uint)PermissionType.Browse }
                ]
            };

            var loggerMock = new Mock<ILogger>();
            ServiceResult result = MasterNodeManager.ValidateRolePermissions(context, nodeMetadata, PermissionType.Read, loggerMock.Object);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Role permissions validation failed for node")),
                    It.IsAny<Exception>(),
                    (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()),
                Times.Once);
        }

        [Test]
        public void ValidateRolePermissions_HasRequestedPermission_ReturnsGood()
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(x => x.GrantedRoleIds).Returns([new NodeId(2)]);
            var context = new OperationContext(new RequestHeader(), null, RequestType.Read, null, identity.Object);

            var nodeMetadata = new NodeMetadata(null, new NodeId(1))
            {
                RolePermissions = [
                    new RolePermissionType { RoleId = new NodeId(2), Permissions = (uint)PermissionType.Read }
                ]
            };

            var loggerMock = new Mock<ILogger>();
            ServiceResult result = MasterNodeManager.ValidateRolePermissions(context, nodeMetadata, PermissionType.Read, loggerMock.Object);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ValidateRolePermissions_DefaultPermissions_ReturnsGood()
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(x => x.GrantedRoleIds).Returns([new NodeId(2)]);
            var context = new OperationContext(new RequestHeader(), null, RequestType.Read, null, identity.Object);

            var nodeMetadata = new NodeMetadata(null, new NodeId(1))
            {
                DefaultRolePermissions = [
                    new RolePermissionType { RoleId = new NodeId(2), Permissions = (uint)PermissionType.Read }
                ]
            };

            var loggerMock = new Mock<ILogger>();
            ServiceResult result = MasterNodeManager.ValidateRolePermissions(context, nodeMetadata, PermissionType.Read, loggerMock.Object);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ValidateRolePermissions_DefaultUserRolePermissions_ReturnsGood()
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(x => x.GrantedRoleIds).Returns([new NodeId(2)]);
            var context = new OperationContext(new RequestHeader(), null, RequestType.Read, null, identity.Object);

            var nodeMetadata = new NodeMetadata(null, new NodeId(1))
            {
                DefaultUserRolePermissions = [
                    new RolePermissionType { RoleId = new NodeId(2), Permissions = (uint)PermissionType.Read }
                ]
            };

            var loggerMock = new Mock<ILogger>();
            ServiceResult result = MasterNodeManager.ValidateRolePermissions(context, nodeMetadata, PermissionType.Read, loggerMock.Object);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ValidateRolePermissions_UserRolePermissionsAndRolePermissionsIntersect_ReturnsGood()
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(x => x.GrantedRoleIds).Returns([new NodeId(2)]);
            var context = new OperationContext(new RequestHeader(), null, RequestType.Read, null, identity.Object);

            var nodeMetadata = new NodeMetadata(null, new NodeId(1))
            {
                UserRolePermissions = [
                    new RolePermissionType { RoleId = new NodeId(2), Permissions = (uint)PermissionType.Read | (uint)PermissionType.Browse }
                ],
                RolePermissions = [
                    new RolePermissionType { RoleId = new NodeId(2), Permissions = (uint)PermissionType.Read }
                ]
            };

            var loggerMock = new Mock<ILogger>();
            ServiceResult result = MasterNodeManager.ValidateRolePermissions(context, nodeMetadata, PermissionType.Read, loggerMock.Object);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Test for registering a namespace manager for a namespace
        /// not contained in the server's namespace table
        /// </summary>
        [Test]
        public async Task RegisterNamespaceManagerNewNamespaceAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));

            try
            {
                //-- Arrange
                const string ns = "http://test.org/UA/Data/";

                var nodeManager = new Mock<INodeManager>();
                nodeManager.Setup(x => x.NamespaceUris).Returns([]);

                //-- Act
                StandardServer server = await fixture.StartAsync()
                    .ConfigureAwait(false);
                using var sut = new MasterNodeManager(
                    server.CurrentInstance,
                    fixture.Config,
                    null,
                    nodeManager.Object);
                sut.RegisterNamespaceManager(ns, nodeManager.Object);

                //-- Assert
                Assert.Contains(ns, server.CurrentInstance.NamespaceUris.ToArray());
                IAsyncNodeManager[] registeredManagers = [.. sut.NamespaceManagers[
                    server.CurrentInstance.NamespaceUris.GetIndex(ns)
                ]];
                Assert.That(registeredManagers, Has.Length.EqualTo(1));
                Assert.Contains(nodeManager.Object, registeredManagers.Select(m => m.SyncNodeManager).ToList());
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Test for registering a namespace manager for a namespace
        /// contained in the server's namespace table
        /// </summary>
        [Test]
        public async Task RegisterNamespaceManagerExistingNamespaceAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));

            try
            {
                //-- Arrange
                const string ns = "http://test.org/UA/Data/";
                var namespaceUris = new List<string> { ns };

                var originalNodeManager = new Mock<INodeManager>();
                originalNodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                var newNodeManager = new Mock<INodeManager>();
                newNodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                //-- Act
                StandardServer server = await fixture.StartAsync()
                    .ConfigureAwait(false);
                using var sut = new MasterNodeManager(
                    server.CurrentInstance,
                    fixture.Config,
                    null,
                    originalNodeManager.Object);
                sut.RegisterNamespaceManager(ns, newNodeManager.Object);

                //-- Assert
                Assert.Contains(ns, server.CurrentInstance.NamespaceUris.ToArray());
                IAsyncNodeManager[] registeredManagers = [.. sut.NamespaceManagers[
                    server.CurrentInstance.NamespaceUris.GetIndex(ns)
                ]];
                Assert.That(registeredManagers, Has.Length.EqualTo(2));
                Assert.Contains(originalNodeManager.Object, registeredManagers.Select(m => m.SyncNodeManager).ToList());
                Assert.Contains(newNodeManager.Object, registeredManagers.Select(m => m.SyncNodeManager).ToList());
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Test for unregistering a namespace manager which had previously
        /// been registered
        /// </summary>
        [Test]
        [TestCase(3, 0)]
        [TestCase(3, 1)]
        [TestCase(3, 2)]
        public async Task UnregisterNamespaceManagerInCollectionAsync(
            int totalManagers,
            int indexToRemove)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));

            try
            {
                //-- Arrange
                const string ns = "http://test.org/UA/Data/";
                var namespaceUris = new List<string> { ns };

                var additionalManagers = new INodeManager[totalManagers];
                for (int ii = 0; ii < totalManagers; ii++)
                {
                    var nodeManager = new Mock<INodeManager>();
                    nodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                    additionalManagers[ii] = nodeManager.Object;
                }

                INodeManager nodeManagerToRemove = additionalManagers[indexToRemove];

                //-- Act
                StandardServer server = await fixture.StartAsync()
                    .ConfigureAwait(false);
                using var sut = new MasterNodeManager(
                    server.CurrentInstance,
                    fixture.Config,
                    null,
                    additionalManagers);
                bool result = sut.UnregisterNamespaceManager(ns, nodeManagerToRemove);

                //-- Assert
                Assert.That(result, Is.True);
                Assert.Contains(ns, server.CurrentInstance.NamespaceUris.ToArray());
                IAsyncNodeManager[] registeredManagers = [.. sut.NamespaceManagers[
                    server.CurrentInstance.NamespaceUris.GetIndex(ns)
                ]];
                Assert.That(registeredManagers, Has.Length.EqualTo(totalManagers - 1));
                Assert.That(registeredManagers.Select(m => m.SyncNodeManager).ToList(), Has.No.Member(nodeManagerToRemove));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Test for unregistering a namespace manager which had not
        /// previously been registered
        /// </summary>
        [Test]
        public async Task UnregisterNamespaceManagerNotInCollectionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));

            try
            {
                //-- Arrange
                const string ns = "http://test.org/UA/Data/";
                var namespaceUris = new List<string> { ns };

                var firstNodeManager = new Mock<INodeManager>();
                firstNodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                var secondNodeManager = new Mock<INodeManager>();
                secondNodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                var thirdNodeManager = new Mock<INodeManager>();
                thirdNodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                //-- Act
                StandardServer server = await fixture.StartAsync()
                    .ConfigureAwait(false);
                using var sut = new MasterNodeManager(
                    server.CurrentInstance,
                    fixture.Config,
                    null,
                    firstNodeManager.Object,
                    // Do not add the secondNodeManager to additionalManagers
                    thirdNodeManager.Object);
                bool result = sut.UnregisterNamespaceManager(ns, secondNodeManager.Object);

                //-- Assert
                Assert.That(result, Is.False);
                Assert.Contains(ns, server.CurrentInstance.NamespaceUris.ToArray());
                IAsyncNodeManager[] registeredManagers = [.. sut.NamespaceManagers[
                    server.CurrentInstance.NamespaceUris.GetIndex(ns)
                ]];
                Assert.That(registeredManagers, Has.Length.EqualTo(2));
                Assert.Contains(firstNodeManager.Object, registeredManagers.Select(m => m.SyncNodeManager).ToList());
                Assert.Contains(thirdNodeManager.Object, registeredManagers.Select(m => m.SyncNodeManager).ToList());
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Test for unregistering a namespace manager which had not
        /// previously been registered and is for a namespace
        /// which is unknown by the server
        /// </summary>
        [Test]
        public async Task UnregisterNamespaceManagerUnknownNamespaceAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));

            try
            {
                //-- Arrange
                const string originalNs = "http://test.org/UA/Data/";

                var originalNodeManager = new Mock<INodeManager>();
                originalNodeManager.Setup(x => x.NamespaceUris).Returns([originalNs]);

                const string newNs = "http://test.org/UA/Data/Instance";
                var newNodeManager = new Mock<INodeManager>();
                newNodeManager.Setup(x => x.NamespaceUris).Returns([originalNs, newNs]);

                //-- Act
                StandardServer server = await fixture.StartAsync()
                    .ConfigureAwait(false);
                using var sut = new MasterNodeManager(
                    server.CurrentInstance,
                    fixture.Config,
                    null,
                    originalNodeManager.Object);
                bool result = sut.UnregisterNamespaceManager(newNs, newNodeManager.Object);

                //-- Assert
                Assert.That(result, Is.False);
                Assert
                    .That(server.CurrentInstance.NamespaceUris.ToArray(), Has.No.Member(newNs));

                Assert.Contains(originalNs, server.CurrentInstance.NamespaceUris.ToArray());
                IAsyncNodeManager[] registeredManagers = [.. sut.NamespaceManagers[
                    server.CurrentInstance.NamespaceUris.GetIndex(originalNs)
                ]];
                Assert.That(registeredManagers, Has.Length.EqualTo(1));
                Assert.Contains(originalNodeManager.Object, registeredManagers.Select(m => m.SyncNodeManager).ToList());
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }
    }
}
