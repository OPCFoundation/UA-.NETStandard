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
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Assets;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Verifies <see cref="WotConnectivityNodeManager.EnforceManagementAccess"/>
    /// rejects address-space callers that violate the configured
    /// <see cref="WotManagementAccessPolicy"/> and accepts those that
    /// satisfy it.
    /// </summary>
    /// <remarks>
    /// The five OPC 10100-1 management methods (CreateAsset,
    /// DeleteAsset, DiscoverAssets, CreateAssetForEndpoint,
    /// ConnectionTest) all invoke the same enforcement entry-point as
    /// their very first action; this fixture therefore exercises the
    /// matrix once per operation name via <see cref="TestCaseAttribute"/>
    /// rather than duplicating the body five times.
    /// </remarks>
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotManagementAccessPolicyTests
    {
        private const string AssetNamespace =
            "http://opcfoundation.org/UA/WoT-Con/Assets/";

        private static readonly string[] s_operations =
        [
            "CreateAsset",
            "DeleteAsset",
            "DiscoverAssets",
            "CreateAssetForEndpoint",
            "ConnectionTest"
        ];

        private string _tempFolder = null!;

        [SetUp]
        public void SetUp()
        {
            _tempFolder = Path.Combine(
                Path.GetTempPath(),
                "wotcon-policy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempFolder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempFolder))
            {
                try
                {
                    Directory.Delete(_tempFolder, recursive: true);
                }
                catch
                { /* swallow */
                }
            }
        }

        [TestCase("CreateAsset")]
        [TestCase("DeleteAsset")]
        [TestCase("DiscoverAssets")]
        [TestCase("CreateAssetForEndpoint")]
        [TestCase("ConnectionTest")]
        [TestCase("Write")]
        [TestCase("CloseAndUpdate")]
        public void AnonymousChannelReturnsBadUserAccessDenied(string operation)
        {
            using var harness = new PolicyHarness(_tempFolder);
            ISystemContext context = harness.BuildSystemContext(
                MessageSecurityMode.SignAndEncrypt,
                identity: new UserIdentity());

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => harness.Manager.EnforceManagementAccess(context, operation))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [TestCase("CreateAsset")]
        [TestCase("DeleteAsset")]
        [TestCase("DiscoverAssets")]
        [TestCase("CreateAssetForEndpoint")]
        [TestCase("ConnectionTest")]
        [TestCase("Write")]
        [TestCase("CloseAndUpdate")]
        public void NoneSecurityModeReturnsBadUserAccessDenied(string operation)
        {
            using var harness = new PolicyHarness(_tempFolder);
            ISystemContext context = harness.BuildSystemContext(
                MessageSecurityMode.None,
                identity: harness.BuildAdminIdentity());

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => harness.Manager.EnforceManagementAccess(context, operation))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [TestCase("CreateAsset")]
        [TestCase("DeleteAsset")]
        [TestCase("DiscoverAssets")]
        [TestCase("CreateAssetForEndpoint")]
        [TestCase("ConnectionTest")]
        [TestCase("Write")]
        [TestCase("CloseAndUpdate")]
        public void SignOnlySecurityModeReturnsBadUserAccessDenied(string operation)
        {
            using var harness = new PolicyHarness(_tempFolder);
            ISystemContext context = harness.BuildSystemContext(
                MessageSecurityMode.Sign,
                identity: harness.BuildAdminIdentity());

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => harness.Manager.EnforceManagementAccess(context, operation))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [TestCase("CreateAsset")]
        [TestCase("DeleteAsset")]
        [TestCase("DiscoverAssets")]
        [TestCase("CreateAssetForEndpoint")]
        [TestCase("ConnectionTest")]
        [TestCase("Write")]
        [TestCase("CloseAndUpdate")]
        public void NonAdminUserReturnsBadUserAccessDenied(string operation)
        {
            using var harness = new PolicyHarness(_tempFolder);
            // Authenticated UserName user but with no role grants.
            IUserIdentity identity = new UserIdentity("alice", []);
            ISystemContext context = harness.BuildSystemContext(
                MessageSecurityMode.SignAndEncrypt, identity);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => harness.Manager.EnforceManagementAccess(context, operation))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [TestCase("CreateAsset")]
        [TestCase("DeleteAsset")]
        [TestCase("DiscoverAssets")]
        [TestCase("CreateAssetForEndpoint")]
        [TestCase("ConnectionTest")]
        [TestCase("Write")]
        [TestCase("CloseAndUpdate")]
        public void SecurityAdminSignAndEncryptSucceeds(string operation)
        {
            using var harness = new PolicyHarness(_tempFolder);
            ISystemContext context = harness.BuildSystemContext(
                MessageSecurityMode.SignAndEncrypt,
                identity: harness.BuildAdminIdentity());

            Assert.DoesNotThrow(
                () => harness.Manager.EnforceManagementAccess(context, operation));
        }

        [Test]
        public void InternalCallWithoutOperationContextIsExempt()
        {
            using var harness = new PolicyHarness(_tempFolder);
            // ServerSystemContext built with no OperationContext: this is
            // how internal startup callers reach the registry. The policy
            // check must skip rather than throw.
            var systemContext = new ServerSystemContext(harness.MockServer.Object);

            foreach (string operation in s_operations)
            {
                Assert.DoesNotThrow(
                    () => harness.Manager.EnforceManagementAccess(systemContext, operation));
            }
        }

        [Test]
        public void CustomRoleAllowsCustomRoleDeniesOthers()
        {
            using var harness = new PolicyHarness(_tempFolder, opts => opts.ManagementAccess = new WotManagementAccessPolicy
            {
                RequiredRoleId = Ua.ObjectIds.WellKnownRole_ConfigureAdmin
            });

            // SecurityAdmin no longer enough.
            ISystemContext withSecurityAdmin = harness.BuildSystemContext(
                MessageSecurityMode.SignAndEncrypt,
                identity: harness.BuildAdminIdentity());
            ServiceResultException denied = Assert.Throws<ServiceResultException>(
                () => harness.Manager.EnforceManagementAccess(
                    withSecurityAdmin, "CreateAsset"))!;
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));

            // ConfigureAdmin now allowed.
            ISystemContext withConfigureAdmin = harness.BuildSystemContext(
                MessageSecurityMode.SignAndEncrypt,
                identity: harness.BuildIdentityWithRole(Role.ConfigureAdmin));
            Assert.DoesNotThrow(
                () => harness.Manager.EnforceManagementAccess(
                    withConfigureAdmin, "CreateAsset"));
        }

        [Test]
        public void LoweredMinimumSecurityModeAcceptsStrongerChannels()
        {
            using var harness = new PolicyHarness(_tempFolder, opts => opts.ManagementAccess = new WotManagementAccessPolicy
            {
                MinimumSecurityMode = MessageSecurityMode.Sign
            });

            // The configured mode itself is accepted.
            Assert.DoesNotThrow(
                () => harness.Manager.EnforceManagementAccess(
                    harness.BuildSystemContext(
                        MessageSecurityMode.Sign,
                        identity: harness.BuildAdminIdentity()),
                    "CreateAsset"));

            // A stronger channel must not be rejected: the policy is a floor, not an exact match.
            Assert.DoesNotThrow(
                () => harness.Manager.EnforceManagementAccess(
                    harness.BuildSystemContext(
                        MessageSecurityMode.SignAndEncrypt,
                        identity: harness.BuildAdminIdentity()),
                    "CreateAsset"));

            // A weaker channel is still denied.
            ServiceResultException denied = Assert.Throws<ServiceResultException>(
                () => harness.Manager.EnforceManagementAccess(
                    harness.BuildSystemContext(
                        MessageSecurityMode.None,
                        identity: harness.BuildAdminIdentity()),
                    "CreateAsset"))!;
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void AllowAnonymousPolicyAcceptsAnonymousWhenSecure()
        {
            using var harness = new PolicyHarness(_tempFolder, opts => opts.ManagementAccess = new WotManagementAccessPolicy
            {
                AllowAnonymous = true,
                // anonymous can't have a role, so widen role to a built-in.
                RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
            });

            // Anonymous identity with the well-known anonymous role granted.
            IUserIdentity anonymous = new RoleBasedIdentity(
                new UserIdentity(),
                [Role.Anonymous],
                harness.MockServer.Object.NamespaceUris);
            ISystemContext context = harness.BuildSystemContext(
                MessageSecurityMode.SignAndEncrypt, anonymous);

            Assert.DoesNotThrow(
                () => harness.Manager.EnforceManagementAccess(context, "CreateAsset"));
        }

        [Test]
        public void UploadPathRefusesAnUnauthorisedWriteAndCloseAndUpdate()
        {
            // WoT Connectivity 1.1-draft3 names the WoTFile Write and
            // CloseAndUpdate operations alongside the management Methods,
            // because they reach the same materializer. Asserting the policy
            // covers those names is not enough on its own - the file manager
            // has to actually consult it - so this drives the handlers.
            using var harness = new PolicyHarness(_tempFolder);
            ISystemContext denied = harness.BuildSystemContext(
                MessageSecurityMode.None,
                identity: new UserIdentity());

            var file = new WoTAssetFileState(null);
            file.Create(
                harness.Manager.SystemContext,
                new NodeId(Guid.NewGuid(), 1),
                new QualifiedName("WoTFile", 1),
                new LocalizedText("WoTFile"),
                true);

            var enforced = new List<string>();
            using var manager = new WotAssetFileManager(
                file,
                maxOpenHandles: 4,
                maxThingDescriptionSize: 4096,
                onCloseAndUpdate: (td, token) =>
                    new ValueTask<ServiceResult>(ServiceResult.Good),
                logger: NullLogger.Instance,
                enforceAccess: (context, operation) =>
                {
                    enforced.Add(operation);
                    harness.Manager.EnforceManagementAccess(context, operation);
                });

            Assert.That(file.Write, Is.Not.Null);
            WriteMethodState writeMethod = file.Write!;
            Assert.That(writeMethod.OnCall, Is.Not.Null);
            ServiceResult write = writeMethod.OnCall!(
                denied,
                writeMethod,
                file.NodeId,
                1u,
                ByteString.From([1, 2, 3]));

            Assert.That(ServiceResult.IsBad(write), Is.True);
            Assert.That(write.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
            Assert.That(enforced, Does.Contain("Write"));

            // CloseAndUpdate reaches the same materializer and carries the same
            // obligation. It enforces access before it validates the handle, so
            // no valid handle is needed to prove the gate is wired.
            Assert.That(file.CloseAndUpdate, Is.Not.Null);
            CloseAndUpdateIWoTAssetTypeWoTFileMethodState closeAndUpdate = file.CloseAndUpdate!;
            Assert.That(closeAndUpdate.OnCall, Is.Not.Null);
            ServiceResult close = closeAndUpdate.OnCall!(
                denied,
                closeAndUpdate,
                file.NodeId,
                1u);

            Assert.That(ServiceResult.IsBad(close), Is.True);
            Assert.That(close.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
            Assert.That(enforced, Does.Contain("CloseAndUpdate"));
        }

        /// <summary>
        /// ----------------------------------------------------------------
        /// Harness — minimal IServerInternal mock just sufficient to
        /// instantiate WotConnectivityNodeManager (no address-space load
        /// needed because EnforceManagementAccess does not touch
        /// m_registry).
        /// ----------------------------------------------------------------
        /// </summary>
        private sealed class PolicyHarness : IDisposable
        {
            public PolicyHarness(
                string thingDescriptionFolder,
                Action<WotConnectivityServerOptions>? configure = null)
            {
                MockServer = new Mock<IServerInternal>();

                var namespaceTable = new NamespaceTable();
                namespaceTable.Append(Namespaces.WotCon);
                namespaceTable.Append(AssetNamespace);
                MockServer.Setup(s => s.NamespaceUris).Returns(namespaceTable);
                MockServer.Setup(s => s.ServerUris).Returns(new StringTable());
                MockServer.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceTable));
                MockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());

                var mockTelemetry = new Mock<ITelemetryContext>();
                MockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

                m_monitoredItemQueueFactory = new MonitoredItemQueueFactory(mockTelemetry.Object);
                MockServer.Setup(s => s.MonitoredItemQueueFactory).Returns(m_monitoredItemQueueFactory);

                var mockMaster = new Mock<IMasterNodeManager>();
                var mockConfig = new Mock<IConfigurationNodeManager>();
                mockMaster.Setup(m => m.ConfigurationNodeManager).Returns(mockConfig.Object);
                MockServer.Setup(s => s.NodeManager).Returns(mockMaster.Object);

                var serverSystemContext = new ServerSystemContext(MockServer.Object);
                MockServer.Setup(s => s.DefaultSystemContext).Returns(serverSystemContext);

                m_options = new WotConnectivityServerOptions
                {
                    AssetNamespaceUri = AssetNamespace,
                    ThingDescriptionStorageFolder = thingDescriptionFolder
                };
                configure?.Invoke(m_options);

                m_configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration()
                };

                Manager = new WotConnectivityNodeManager(
                    MockServer.Object, m_configuration, m_options);
            }

            public Mock<IServerInternal> MockServer { get; }
            public WotConnectivityNodeManager Manager { get; }

            public RoleBasedIdentity BuildAdminIdentity()
            {
                return BuildIdentityWithRole(Role.SecurityAdmin);
            }

            public RoleBasedIdentity BuildIdentityWithRole(Role role)
            {
                return new RoleBasedIdentity(
                    new UserIdentity("admin", []),
                    [role],
                    MockServer.Object.NamespaceUris);
            }

            public ServerSystemContext BuildSystemContext(
                MessageSecurityMode securityMode,
                IUserIdentity identity)
            {
                var endpoint = new EndpointDescription { SecurityMode = securityMode };
                var channel = new SecureChannelContext(
                    "test", endpoint, RequestEncoding.Binary,
                    clientChannelCertificate: null,
                    serverChannelCertificate: null,
                    channelThumbprint: null);
                var op = new OperationContext(
                    new RequestHeader(), channel,
                    RequestType.Call, RequestLifetime.None, identity);
                return new ServerSystemContext(MockServer.Object, op);
            }

            public void Dispose()
            {
                Manager.Dispose();
                m_monitoredItemQueueFactory.Dispose();
            }

            private readonly WotConnectivityServerOptions m_options;
            private readonly ApplicationConfiguration m_configuration;
            private readonly MonitoredItemQueueFactory m_monitoredItemQueueFactory;
        }
    }
}
