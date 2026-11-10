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
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests the Optional OPC 10000-12 §7.10.3 <c>ServerConfigurationType</c>
    /// surface bound by <see cref="ConfigurationNodeManager"/>: identity
    /// Properties, <c>HasSecureElement</c>/<c>InApplicationSetup</c>, the
    /// <c>ResetToServerDefaults</c> Method security/transaction rules, and the
    /// <c>ConfigurationFile</c> read/update flow.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [NonParallelizable]
    [Parallelizable(ParallelScope.None)]
    public class ServerConfigurationSurfaceTests
    {
        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();
        private static readonly ByteString s_initialConfig = ByteString.From([0x10, 0x20, 0x30, 0x40]);

        /// <summary>
        /// The effective maximum configuration file size enforced by
        /// <see cref="ApplicationConfigurationFile"/>, fetched via reflection
        /// from the private <c>kMaxConfigurationFileSize</c> constant so the
        /// boundary tests stay correct even if the limit is retuned.
        /// </summary>
        private static readonly long s_maxConfigurationFileSize = GetMaxConfigurationFileSize();

        private ServerFixture<StandardServer> m_fixture = null!;
        private StandardServer m_server = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task IdentityPropertiesAlwaysExposedAndValuedAsync()
        {
            Harness harness = await CreateHarnessAsync(new ServerConfigurationOptions()).ConfigureAwait(false);
            using (harness.Manager)
            {
                ServerConfigurationState node = harness.Node;
                Assert.Multiple(() =>
                {
                    Assert.That(node.ApplicationUri, Is.Not.Null);
                    Assert.That(node.ProductUri, Is.Not.Null);
                    Assert.That(node.ApplicationType, Is.Not.Null);
                    Assert.That(node.ApplicationNames, Is.Not.Null);
                });

                Assert.That(node.ApplicationUri!.Value, Is.EqualTo(m_fixture.Config.ApplicationUri));
                Assert.That(node.ApplicationType!.Value, Is.EqualTo(m_fixture.Config.ApplicationType));
                Assert.That(node.ApplicationNames!.Value.Count, Is.EqualTo(1));
                Assert.That(node.ApplicationNames.Value[0].Text, Is.EqualTo(m_fixture.Config.ApplicationName));
            }
        }

        /// <summary>
        /// Preserves namespace permissions while granting SecurityAdmin access only on restricted nodes.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public async Task NamespacePermissionsPreservedAfterConfigurationAsync(
            bool asyncConfiguration,
            bool restricted,
            bool explicitPermissions)
        {
            IServerInternal serverInternal = m_server.CurrentInstance;
            NamespaceMetadataState? metadata = await serverInternal.ConfigurationNodeManager
                .GetNamespaceMetadataStateAsync(0).ConfigureAwait(false);
            Assert.That(metadata?.DefaultRolePermissions, Is.Not.Null);
            ArrayOf<RolePermissionType> original = metadata!.DefaultRolePermissions!.Value;
            try
            {
                metadata.DefaultRolePermissions.Value = restricted
                    ? [new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_Anonymous,
                        Permissions = (uint)PermissionType.Browse
                    }]
                    : ArrayOf<RolePermissionType>.Empty;
                await metadata.ClearChangeMasksAsync(serverInternal.DefaultSystemContext, true)
                    .ConfigureAwait(false);
                Harness harness = await CreateHarnessAsync(
                    new ServerConfigurationOptions(),
                    asyncConfiguration: asyncConfiguration,
                    beforeConfiguration: root =>
                    {
                        var pending = new Stack<NodeState>();
                        pending.Push(root);
                        while (pending.Count != 0)
                        {
                            NodeState node = pending.Pop();
                            if (node is MethodState or BaseObjectState)
                            {
                                // The standard nodeset has explicit policies; isolate inheritance deliberately.
                                node.RolePermissions = ArrayOf<RolePermissionType>.Empty;
                            }

                            var children = new List<BaseInstanceState>();
                            node.GetChildren(serverInternal.DefaultSystemContext, children);
                            foreach (BaseInstanceState child in children)
                            {
                                pending.Push(child);
                            }
                        }

                        if (explicitPermissions)
                        {
                            root.RolePermissions = [new RolePermissionType
                            {
                                RoleId = ObjectIds.WellKnownRole_Observer,
                                Permissions = (uint)(PermissionType.Browse | PermissionType.Read)
                            }];
                        }
                    }).ConfigureAwait(false);
                using (harness.Manager)
                {
                    var pending = new Stack<NodeState>();
                    pending.Push(harness.Node);
                    while (pending.Count != 0)
                    {
                        NodeState node = pending.Pop();
                        if (node is MethodState or BaseObjectState)
                        {
                            bool explicitNode = explicitPermissions && ReferenceEquals(node, harness.Node);
                            // Dynamic alarm nodes use their own namespace, not namespace zero's defaults.
                            bool namespaceRestricted = restricted && node.NodeId.NamespaceIndex == 0;
                            if (namespaceRestricted || explicitNode)
                            {
                                Assert.That(node.RolePermissions.Count, Is.EqualTo(2), node.BrowseName.ToString());
                                Assert.Multiple(() =>
                                {
                                    Assert.That(node.RolePermissions[0].RoleId,
                                        Is.EqualTo(explicitNode
                                            ? ObjectIds.WellKnownRole_Observer
                                            : ObjectIds.WellKnownRole_Anonymous));
                                    Assert.That(node.RolePermissions[0].Permissions,
                                        Is.EqualTo((uint)(explicitNode
                                            ? PermissionType.Browse | PermissionType.Read
                                            : PermissionType.Browse)));
                                    Assert.That(node.RolePermissions[1].RoleId,
                                        Is.EqualTo(ObjectIds.WellKnownRole_SecurityAdmin));
                                    Assert.That(node.RolePermissions[1].Permissions,
                                        Is.EqualTo((uint)(PermissionType.Browse | PermissionType.Call)));
                                });
                            }
                            else
                            {
                                Assert.That(node.RolePermissions.IsEmpty, Is.True,
                                    $"Unrestricted namespace permissions must remain inherited by {node.BrowseName}.");
                            }
                        }

                        var children = new List<BaseInstanceState>();
                        node.GetChildren(serverInternal.DefaultSystemContext, children);
                        foreach (BaseInstanceState child in children)
                        {
                            pending.Push(child);
                        }
                    }
                }
            }
            finally
            {
                metadata.DefaultRolePermissions.Value = original;
                await metadata.ClearChangeMasksAsync(serverInternal.DefaultSystemContext, true)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task OptionalMembersSuppressedWhenNotConfiguredAsync()
        {
            Harness harness = await CreateHarnessAsync(new ServerConfigurationOptions()).ConfigureAwait(false);
            using (harness.Manager)
            {
                ServerConfigurationState node = harness.Node;
                Assert.Multiple(() =>
                {
                    Assert.That(node.HasSecureElement, Is.Null);
                    Assert.That(node.InApplicationSetup, Is.Null);
                    Assert.That(node.ResetToServerDefaults, Is.Null);
                    Assert.That(node.ConfigurationFile, Is.Null);
                });
            }
        }

        [Test]
        public async Task HasSecureElementExposedWithValueWhenConfiguredAsync()
        {
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { HasSecureElement = true }).ConfigureAwait(false);
            using (harness.Manager)
            {
                Assert.That(harness.Node.HasSecureElement, Is.Not.Null);
                Assert.That(harness.Node.HasSecureElement!.Value, Is.True);
            }
        }

        [Test]
        public async Task InApplicationSetupExposedWithValueWhenConfiguredAsync()
        {
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { InApplicationSetup = false }).ConfigureAwait(false);
            using (harness.Manager)
            {
                Assert.That(harness.Node.InApplicationSetup, Is.Not.Null);
                Assert.That(harness.Node.InApplicationSetup!.Value, Is.False);
            }
        }

        /// <summary>
        /// Host ownership decisions complete before any standard push mutation or transaction state changes.
        /// </summary>
        [TestCase("csr")]
        [TestCase("update")]
        [TestCase("selfSigned")]
        [TestCase("delete")]
        [TestCase("apply")]
        [TestCase("cancel")]
        [TestCase("reset")]
        public async Task PushMutationAwaitsHostAuthorizationBeforeConfigurationEffects(string operation)
        {
            var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var policy = new Mock<IPushConfigurationAuthorization>(MockBehavior.Strict);
            SessionSystemContext context = CreateAdminContextForSession(new NodeId(901, 1));
            policy.Setup(value => value.AuthorizeAsync(context, It.IsAny<CancellationToken>()))
                .Returns(async (ISystemContext _, CancellationToken ct) =>
                {
                    await decision.Task.ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                });
            var reset = new Mock<IServerConfigurationResetProvider>(MockBehavior.Strict);
            Harness harness = await CreateHarnessAsync(new ServerConfigurationOptions
            {
                PushAuthorization = policy.Object,
                ResetProvider = reset.Object
            }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ServerConfigurationState node = harness.Node;
                Task pending = InvokeAsync();
                Assert.That(pending.IsCompleted, Is.False);
                Assert.That(harness.Coordinator.GetSnapshot().IsActive, Is.False);
                var failure = new ServiceResultException(StatusCodes.BadUserAccessDenied);
                decision.SetException(failure);

                ServiceResultException? actual = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await pending.ConfigureAwait(false));

                Assert.That(actual, Is.SameAs(failure));
                Assert.That(harness.Coordinator.GetSnapshot().IsActive, Is.False);
                policy.Verify(value => value.AuthorizeAsync(context, It.IsAny<CancellationToken>()), Times.Once);
                reset.VerifyNoOtherCalls();

                async Task InvokeAsync()
                {
                    switch (operation)
                    {
                        case "csr":
                            await node.CreateSigningRequest!.OnCallAsync!(
                                context, node.CreateSigningRequest, node.NodeId, NodeId.Null,
                                ObjectTypeIds.RsaSha256ApplicationCertificateType, string.Empty, true,
                                ByteString.Empty, CancellationToken.None).ConfigureAwait(false);
                            break;
                        case "update":
                            await node.UpdateCertificate!.OnCallAsync!(
                                context, node.UpdateCertificate, node.NodeId, NodeId.Null,
                                ObjectTypeIds.RsaSha256ApplicationCertificateType, ByteString.Empty,
                                [], string.Empty, ByteString.Empty, CancellationToken.None).ConfigureAwait(false);
                            break;
                        case "selfSigned":
                            await node.CreateSelfSignedCertificate!.OnCallAsync!(
                                context, node.CreateSelfSignedCertificate, node.NodeId, NodeId.Null,
                                ObjectTypeIds.RsaSha256ApplicationCertificateType, "CN=NotCreated", [], [],
                                1, 2048, CancellationToken.None).ConfigureAwait(false);
                            break;
                        case "delete":
                            await node.DeleteCertificate!.OnCallAsync!(
                                context, node.DeleteCertificate, node.NodeId, NodeId.Null,
                                ObjectTypeIds.RsaSha256ApplicationCertificateType, CancellationToken.None)
                                .ConfigureAwait(false);
                            break;
                        default:
                            MethodState method = operation switch
                            {
                                "apply" => node.ApplyChanges!,
                                "cancel" => node.CancelChanges!,
                                _ => node.ResetToServerDefaults!
                            };
                            await method.OnCallMethod2Async!(
                                context, method, node.NodeId, [], [], CancellationToken.None).ConfigureAwait(false);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Host policy cannot authorize an anonymous caller before the standard SecurityAdmin check.
        /// </summary>
        [Test]
        public async Task PushMutationChecksStandardRoleBeforeHostPolicy()
        {
            var policy = new Mock<IPushConfigurationAuthorization>(MockBehavior.Strict);
            Harness harness = await CreateHarnessAsync(new ServerConfigurationOptions
            {
                PushAuthorization = policy.Object
            }).ConfigureAwait(false);
            using (harness.Manager)
            {
                SessionSystemContext context = CreateContext(
                    UserTokenType.Anonymous, MessageSecurityMode.SignAndEncrypt);

                ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await harness.Node.CreateSigningRequest!.OnCallAsync!(
                        context, harness.Node.CreateSigningRequest, harness.Node.NodeId, NodeId.Null,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType, string.Empty, true,
                        ByteString.Empty, CancellationToken.None).ConfigureAwait(false));

                Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                policy.VerifyNoOtherCalls();
                Assert.That(harness.Coordinator.GetSnapshot().IsActive, Is.False);
            }
        }

        /// <summary>
        /// The standard ApplyChanges handler executes host completion inside the actual compensating transaction.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ApplyChangesRecordsHostCompletionWithinStoreTransaction(bool rejectCompletion)
        {
            var sessionId = new NodeId(902, 1);
            SessionSystemContext context = CreateAdminContextForSession(sessionId);
            var policy = new Mock<IPushConfigurationAuthorization>(MockBehavior.Strict);
            policy.Setup(value => value.AuthorizeAsync(context, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            Mock<IPushConfigurationCompletion> completion = policy.As<IPushConfigurationCompletion>();
            int stored = 0;
            completion.Setup(value => value.CompleteAsync(context, It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Assert.That(stored, Is.EqualTo(1));
                    if (rejectCompletion)
                    {
                        throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                    }
                    return default;
                });
            Harness harness = await CreateHarnessAsync(new ServerConfigurationOptions
            {
                PushAuthorization = policy.Object
            }).ConfigureAwait(false);
            using (harness.Manager)
            {
                harness.Coordinator.Stage(sessionId, new PushConfigurationOperation
                {
                    CommitAsync = _ =>
                    {
                        stored++;
                        return Task.CompletedTask;
                    },
                    RollbackAsync = _ =>
                    {
                        stored--;
                        return Task.CompletedTask;
                    }
                });
                ServiceResult result = await harness.Node.ApplyChanges!.OnCallMethod2Async!(
                    context, harness.Node.ApplyChanges, harness.Node.NodeId, [], [], CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(result.StatusCode,
                    Is.EqualTo(rejectCompletion ? StatusCodes.BadResourceUnavailable : StatusCodes.Good));
                Assert.That(stored, Is.EqualTo(rejectCompletion ? 0 : 1));
                Assert.That(harness.Coordinator.IsTransactionActive, Is.False);
                completion.Verify(value => value.CompleteAsync(context, It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        [Test]
        public async Task ResetToServerDefaultsExposedOnlyWhenProviderConfiguredAsync()
        {
            Harness withProvider = await CreateHarnessAsync(
                new ServerConfigurationOptions { ResetProvider = new FakeResetProvider() }).ConfigureAwait(false);
            using (withProvider.Manager)
            {
                Assert.That(withProvider.Node.ResetToServerDefaults, Is.Not.Null);
                Assert.That(withProvider.Node.ResetToServerDefaults!.OnCallMethod2Async, Is.Not.Null);
            }

            Harness without = await CreateHarnessAsync(new ServerConfigurationOptions()).ConfigureAwait(false);
            using (without.Manager)
            {
                Assert.That(without.Node.ResetToServerDefaults, Is.Null);
            }
        }

        [Test]
        public async Task ConfigurationFileExposedWithChildrenWhenProviderConfiguredAsync()
        {
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = new FakeConfigurationFileProvider(s_initialConfig) })
                .ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState? file = harness.Node.ConfigurationFile;
                Assert.That(file, Is.Not.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(file!.Open, Is.Not.Null);
                    Assert.That(file.Read, Is.Not.Null);
                    Assert.That(file.Write, Is.Not.Null);
                    Assert.That(file.Close, Is.Not.Null);
                    Assert.That(file.CloseAndUpdate, Is.Not.Null);
                    Assert.That(file.ConfirmUpdate, Is.Not.Null);
                    Assert.That(file.Open!.OnCallAsync, Is.Not.Null);
                    Assert.That(file.CloseAndUpdate!.OnCallAsync, Is.Not.Null);
                });
            }
        }

        [Test]
        public async Task ConfigurationFilePropertiesSeededFromProviderAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig) { CurrentVersion = 7 };
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions
                {
                    ConfigurationFileProvider = provider,
                    ConfigurationFileActivityTimeout = 12345.0
                }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                Assert.Multiple(() =>
                {
                    Assert.That(file.CurrentVersion!.Value, Is.EqualTo(7u));
                    Assert.That(file.ActivityTimeout!.Value, Is.EqualTo(12345.0));
                    Assert.That(file.SupportedDataType!.Value, Is.EqualTo(DataTypeIds.ApplicationConfigurationDataType));
                    Assert.That(file.Writable!.Value, Is.True);
                });
            }
        }

        [Test]
        public async Task ConfigurationFileReadReturnsProviderContentAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(1, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);

                ReadMethodStateResult read = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, 1024, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(read.ServiceResult), Is.True);
                Assert.That(read.Data.ToArray(), Is.EqualTo(s_initialConfig.ToArray()));

                CloseMethodStateResult close = await file.Close!.OnCallAsync!(
                    ctx, file.Close, file.NodeId, open.FileHandle, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(close.ServiceResult), Is.True);
                Assert.That(provider.ApplyCount, Is.Zero);
            }
        }

        [Test]
        public async Task ConfigurationFileWriteThenCloseAndUpdateAppliesAtomicallyAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(2, 1));
                var updated = ByteString.From([0xAA, 0xBB, 0xCC]);

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.True,
                    "opening the ConfigurationFile for writing must block new transactions/ApplyChanges");

                WriteMethodStateResult write = await file.Write!.OnCallAsync!(
                    ctx, file.Write, file.NodeId, open.FileHandle, updated, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(write.ServiceResult), Is.True);

                ConfigurationFileCloseAndUpdateMethodStateResult result = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, versionToUpdate: 1).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                    Assert.That(provider.ApplyCount, Is.EqualTo(1));
                    Assert.That(provider.LastApplied.ToArray(), Is.EqualTo(updated.ToArray()));
                    Assert.That(result.NewVersion, Is.EqualTo(2u));
                    Assert.That(result.UpdateId.Guid, Is.EqualTo(Guid.Empty));
                    Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False,
                        "committing the update must clear the write-open flag");
                });
            }
        }

        [Test]
        public async Task ConfigurationFileUpdatedAuditEventSourceIsConfigurationOwnerAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                var events = new List<ConfigurationUpdatedAuditEventState>();
                file.OnReportEvent += (_, _, e) =>
                {
                    if (e is ConfigurationUpdatedAuditEventState audit)
                    {
                        events.Add(audit);
                    }
                };
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(22, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                await file.Write!.OnCallAsync!(ctx, file.Write, file.NodeId, open.FileHandle,
                    ByteString.From([0x01]), CancellationToken.None).ConfigureAwait(false);
                ConfigurationFileCloseAndUpdateMethodStateResult result = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, versionToUpdate: 1).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);

                // §7.8.5.8: SourceNode/SourceName identify the configuration
                // owner (the parent of the ConfigurationFile Object).
                Assert.That(events, Has.Count.EqualTo(1));
                Assert.Multiple(() =>
                {
                    Assert.That(events[0].SourceNode!.Value, Is.EqualTo(harness.Node.NodeId));
                    Assert.That(events[0].SourceName!.Value, Is.EqualTo(harness.Node.BrowseName.Name));
                    Assert.That(events[0].OldVersion!.Value, Is.EqualTo(1u));
                    Assert.That(events[0].NewVersion!.Value, Is.EqualTo(2u));
                });
            }
        }

        [Test]
        public async Task ConfigurationFileCloseAndUpdateWithoutTargetsReturnsBadInvalidArgumentAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(21, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                await file.Write!.OnCallAsync!(ctx, file.Write, file.NodeId, open.FileHandle,
                    ByteString.From([0x01]), CancellationToken.None).ConfigureAwait(false);

                // §7.8.5.2: "There must be at least one target."
                ConfigurationFileCloseAndUpdateMethodStateResult result = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, versionToUpdate: 1, withoutTargets: true).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                    Assert.That(provider.ApplyCount, Is.Zero);
                });
            }
        }

        [Test]
        public async Task ConfigurationFileTargetProviderReceivesTargetsAsync()
        {
            var provider = new FakeTargetConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(23, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                await file.Write!.OnCallAsync!(ctx, file.Write, file.NodeId, open.FileHandle,
                    ByteString.From([0x05]), CancellationToken.None).ConfigureAwait(false);

                // §7.8.5.2 Targets: "Contents of the file which are not
                // referenced by a target are ignored" - the provider needs them.
                ConfigurationFileCloseAndUpdateMethodStateResult result = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, versionToUpdate: 1, restartDelayTime: 5000).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                    Assert.That(provider.ValidatedTargets.Count, Is.EqualTo(1));
                    Assert.That(provider.ValidatedTargets[0].Path, Is.EqualTo("ApplicationIdentity"));
                    Assert.That(provider.AppliedTargets.Count, Is.EqualTo(1));
                    Assert.That(provider.ApplyCount, Is.EqualTo(1),
                        "an update that does not interrupt Sessions is applied immediately");
                    Assert.That(result.NewVersion, Is.EqualTo(2u));
                });
            }
        }

        [Test]
        public async Task ConfigurationFileTargetProviderBadTargetReturnsUncertainWithoutApplyAsync()
        {
            var provider = new FakeTargetConfigurationFileProvider(s_initialConfig)
            {
                TargetResults = [StatusCodes.BadInvalidArgument]
            };
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(24, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                await file.Write!.OnCallAsync!(ctx, file.Write, file.NodeId, open.FileHandle,
                    ByteString.From([0x06]), CancellationToken.None).ConfigureAwait(false);

                ConfigurationFileCloseAndUpdateMethodStateResult result = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, versionToUpdate: 1).ConfigureAwait(false);

                // §7.8.5.2: "If any element is not Good then no changes are
                // applied and the Method return code is Uncertain."
                Assert.Multiple(() =>
                {
                    Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
                    Assert.That(result.UpdateResults.Count, Is.EqualTo(1));
                    Assert.That(result.UpdateResults[0], Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
                    Assert.That(result.NewVersion, Is.Zero);
                    Assert.That(provider.ApplyCount, Is.Zero);
                    Assert.That(file.OpenCount!.Value, Is.Zero);
                });
            }
        }

        [Test]
        public async Task ConfigurationFileSessionInterruptingUpdateAppliedAfterRestartDelayAsync()
        {
            var timeProvider = new ControllableTimeProvider();
            var provider = new FakeTargetConfigurationFileProvider(s_initialConfig)
            {
                InterruptsSessions = true
            };
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions
                {
                    ConfigurationFileProvider = provider,
                    ConfigurationFileActivityTimeout = 0
                },
                timeProvider).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                var events = new List<ConfigurationUpdatedAuditEventState>();
                file.OnReportEvent += (_, _, e) =>
                {
                    if (e is ConfigurationUpdatedAuditEventState audit)
                    {
                        lock (events)
                        {
                            events.Add(audit);
                        }
                    }
                };
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(25, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                await file.Write!.OnCallAsync!(ctx, file.Write, file.NodeId, open.FileHandle,
                    ByteString.From([0x07]), CancellationToken.None).ConfigureAwait(false);

                // §7.8.5.2 RestartDelayTime: "How long the Server should wait
                // before applying the configuration changes if applying the
                // configuration changes will interrupt active Sessions."
                ConfigurationFileCloseAndUpdateMethodStateResult result = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, versionToUpdate: 1, restartDelayTime: 5000).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                    Assert.That(result.NewVersion, Is.EqualTo(2u), "the planned version is returned");
                    Assert.That(provider.ApplyCount, Is.Zero, "the apply waits for the RestartDelayTime");
                    Assert.That(file.OpenCount!.Value, Is.Zero);
                });

                // A second update while the first is still scheduled is rejected.
                OpenMethodStateResult reopen = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                ConfigurationFileCloseAndUpdateMethodStateResult second = await CloseAndUpdateAsync(
                    file, ctx, reopen.FileHandle, versionToUpdate: 1, restartDelayTime: 5000).ConfigureAwait(false);
                Assert.That(second.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));

                // The deferred apply starts on a background task; wait until it
                // armed its RestartDelayTime timer.
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                ControllableTimer[] delayTimers = [];
                while (delayTimers.Length == 0 && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(10).ConfigureAwait(false);
                    delayTimers = [.. timeProvider.Timers.Where(
                        t => !string.Equals(t.State?.GetType().Name, "ActivityTimerState", StringComparison.Ordinal))];
                }

                Assert.That(delayTimers, Has.Length.EqualTo(1));
                Assert.That(provider.ApplyCount, Is.Zero, "the apply waits for the RestartDelayTime");
                delayTimers[0].Fire();

                // The audit event is the last step of the deferred apply.
                int eventCount = 0;
                while (eventCount == 0 && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(10).ConfigureAwait(false);
                    lock (events)
                    {
                        eventCount = events.Count;
                    }
                }

                Assert.Multiple(() =>
                {
                    Assert.That(eventCount, Is.EqualTo(1));
                    Assert.That(events[0].NewVersion!.Value, Is.EqualTo(2u));
                    Assert.That(provider.ApplyCount, Is.EqualTo(1));
                    Assert.That(provider.AppliedTargets.Count, Is.EqualTo(1));
                    Assert.That(file.CurrentVersion!.Value, Is.EqualTo(2u));
                });
            }
        }

        [Test]
        public async Task ConfigurationFileCloseDiscardsChangesWithoutApplyingAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(3, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                await file.Write!.OnCallAsync!(ctx, file.Write, file.NodeId, open.FileHandle,
                    ByteString.From([0x01]), CancellationToken.None).ConfigureAwait(false);

                CloseMethodStateResult close = await file.Close!.OnCallAsync!(
                    ctx, file.Close, file.NodeId, open.FileHandle, CancellationToken.None).ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(close.ServiceResult), Is.True);
                Assert.That(provider.ApplyCount, Is.Zero);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False);
            }
        }

        [Test]
        public async Task ConfigurationFileCloseAndUpdateWithInvalidConfigRollsBackAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig) { ValidateShouldThrow = true };
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(4, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                await file.Write!.OnCallAsync!(ctx, file.Write, file.NodeId, open.FileHandle,
                    ByteString.From([0x99]), CancellationToken.None).ConfigureAwait(false);

                ConfigurationFileCloseAndUpdateMethodStateResult result = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, versionToUpdate: 1).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                    Assert.That(provider.ApplyCount, Is.Zero, "no change may be applied when validation fails");
                    Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False);
                });
            }
        }

        /// <summary>
        /// Deferred recovery reacquires deployment admission after its delay and never mutates a revoked deployment.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ConfigurationFileDeferredRecoveryChecksDeploymentAfterTimerAsync(bool revert, bool revoked)
        {
            var time = new ControllableTimeProvider();
            var provider = new Mock<IRecoverableApplicationConfigurationFileProvider>();
            provider.SetupGet(value => value.CurrentVersion).Returns(1u);
            Uuid updateId = Uuid.NewUuid();
            DateTime later = DateTime.UtcNow.AddMinutes(1);
            provider.Setup(value => value.GetPendingUpdateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PendingApplicationConfigurationUpdate(
                    updateId, later, revert ? later : null, revert));
            var operations = new Mock<IApplicationConfigurationPushOperations>(MockBehavior.Strict);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            operations.Setup(value => value.ExecuteAsync(
                It.IsAny<Func<ValueTask>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<ValueTask> effect, CancellationToken _) => ExecuteAsync(effect));
            Harness harness = await CreateHarnessAsync(new ServerConfigurationOptions
            {
                ConfigurationFileProvider = new FakeConfigurationFileProvider(s_initialConfig)
            }).ConfigureAwait(false);
            using (harness.Manager)
            using (var file = new ApplicationConfigurationFile(
                harness.Node.ConfigurationFile!, provider.Object, _ => { }, _ => { }, s_telemetry,
                harness.Coordinator, time, 0, operations.Object))
            {
                try
                {
                    await file.RecoverPendingUpdateAsync(harness.Manager.SystemContext).ConfigureAwait(false);
                    DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                    while (time.Timers.Count == 0 && DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(10).ConfigureAwait(false);
                    }
                    Assert.That(time.Timers, Has.Count.EqualTo(1));
                    operations.Verify(value => value.ExecuteAsync(
                        It.IsAny<Func<ValueTask>>(), It.IsAny<CancellationToken>()), Times.Never);
                    time.Timers[0].Fire();
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                finally
                {
                    await file.StopAsync().ConfigureAwait(false);
                }

                operations.Verify(value => value.ExecuteAsync(
                    It.IsAny<Func<ValueTask>>(), It.IsAny<CancellationToken>()), Times.Once);
                provider.Verify(value => value.ApplyPendingUpdateAsync(updateId, It.IsAny<CancellationToken>()),
                    !revert && !revoked ? Times.Once() : Times.Never());
                provider.Verify(value => value.RevertPendingUpdateAsync(updateId, It.IsAny<CancellationToken>()),
                    revert && !revoked ? Times.Once() : Times.Never());
            }

            async ValueTask ExecuteAsync(Func<ValueTask> effect)
            {
                try
                {
                    if (revoked)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState);
                    }
                    await effect().ConfigureAwait(false);
                }
                finally
                {
                    entered.TrySetResult(true);
                }
            }
        }

        /// <summary>
        /// Shutdown waits for a provider effect already admitted before releasing its owner.
        /// </summary>
        [Test]
        public async Task ConfigurationFileShutdownDrainsAdmittedRecoveryEffectAsync()
        {
            var time = new ControllableTimeProvider();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var operations = new Mock<IApplicationConfigurationPushOperations>(MockBehavior.Strict);
            operations.Setup(value => value.ExecuteAsync(
                It.IsAny<Func<ValueTask>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<ValueTask> effect, CancellationToken _) => effect());
            var provider = new Mock<IRecoverableApplicationConfigurationFileProvider>();
            provider.SetupGet(value => value.CurrentVersion).Returns(1u);
            Uuid updateId = Uuid.NewUuid();
            provider.Setup(value => value.GetPendingUpdateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PendingApplicationConfigurationUpdate(
                    updateId, DateTime.UtcNow.AddMinutes(1), null, false));
            provider.Setup(value => value.ApplyPendingUpdateAsync(updateId, It.IsAny<CancellationToken>()))
                .Returns(() => ApplyAsync());
            Harness harness = await CreateHarnessAsync(new ServerConfigurationOptions
            {
                ConfigurationFileProvider = new FakeConfigurationFileProvider(s_initialConfig)
            }).ConfigureAwait(false);
            using (harness.Manager)
            using (var file = new ApplicationConfigurationFile(
                harness.Node.ConfigurationFile!, provider.Object, _ => { }, _ => { }, s_telemetry,
                harness.Coordinator, time, 0, operations.Object))
            {
                Task? stopping = null;
                try
                {
                    await file.RecoverPendingUpdateAsync(harness.Manager.SystemContext).ConfigureAwait(false);
                    DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                    while (time.Timers.Count == 0 && DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(10).ConfigureAwait(false);
                    }
                    Assert.That(time.Timers, Has.Count.EqualTo(1));
                    time.Timers[0].Fire();
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    stopping = file.StopAsync().AsTask();
                    Assert.That(stopping.IsCompleted, Is.False);
                }
                finally
                {
                    release.TrySetResult(true);
                    await (stopping ?? file.StopAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(10))
                        .ConfigureAwait(false);
                }
                provider.Verify(value => value.ApplyPendingUpdateAsync(
                    updateId, It.IsAny<CancellationToken>()), Times.Once);
            }

            async ValueTask ApplyAsync()
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConfigurationFileApplyFailureLeavesNoPartialUpdateAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig) { ApplyShouldThrow = true };
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(5, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                await file.Write!.OnCallAsync!(ctx, file.Write, file.NodeId, open.FileHandle,
                    ByteString.From([0x77]), CancellationToken.None).ConfigureAwait(false);

                ConfigurationFileCloseAndUpdateMethodStateResult result = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, versionToUpdate: 1).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    Assert.That(result.NewVersion, Is.Zero);
                    Assert.That(provider.CurrentVersion, Is.EqualTo(1u), "the active version must be unchanged");
                });
            }
        }

        [Test]
        public async Task RecoverableConfigurationFileUnexpectedApplyFailureClearsPendingUpdateAsync()
        {
            var store = new FakeConfigurationFileStore(s_initialConfig);
            var provider = new FakeConfigurationFileProvider(store)
            {
                RequiresConfirmation = true,
                ApplyShouldThrowUnexpected = true
            };
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(6, 1));
                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                await file.Write!.OnCallAsync!(ctx, file.Write, file.NodeId, open.FileHandle,
                    ByteString.From([0x78]), CancellationToken.None).ConfigureAwait(false);

                ConfigurationFileCloseAndUpdateMethodStateResult result = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, versionToUpdate: 1, revertAfterTime: 60_000)
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
                    Assert.That(provider.CurrentConfiguration, Is.EqualTo(s_initialConfig));
                    Assert.That(provider.CurrentVersion, Is.EqualTo(1u));
                    Assert.That(provider.RevertCount, Is.EqualTo(1));
                });
                Assert.That(await provider.GetPendingUpdateAsync().ConfigureAwait(false), Is.Null);
            }
        }

        [Test]
        public async Task ConfigurationFileCloseAndUpdateWrongVersionReturnsBadInvalidStateAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(6, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                ConfigurationFileCloseAndUpdateMethodStateResult result = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, versionToUpdate: 999).ConfigureAwait(false);

                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(provider.ApplyCount, Is.Zero);
            }
        }

        [Test]
        public async Task ConfigurationFileOpenUnsupportedModeReturnsBadInvalidArgumentAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(7, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Write | OpenFileMode.EraseExisting)
                    .ConfigureAwait(false);
                Assert.That(open.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            }
        }

        [Test]
        public async Task ConfigurationFileReadWithWrongHandleReturnsBadInvalidArgumentAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(8, 1));

                await OpenAsync(file, ctx, OpenFileMode.Read).ConfigureAwait(false);
                ReadMethodStateResult read = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, 987654u, 16, CancellationToken.None).ConfigureAwait(false);
                Assert.That(read.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            }
        }

        [Test]
        public async Task ConfigurationFileReadAtExactMaxBoundaryReturnsGoodAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(101, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);

                // Requesting exactly the maximum is allowed: length == remaining
                // must not be rejected.
                ReadMethodStateResult read = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, (int)s_maxConfigurationFileSize,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(read.ServiceResult), Is.True);
                Assert.That(read.Data.ToArray(), Is.EqualTo(s_initialConfig.ToArray()));
            }
        }

        [Test]
        public async Task ConfigurationFileReadOneByteOverMaxBoundaryReturnsBadEncodingLimitsExceededAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(102, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);

                // One byte past the boundary must be rejected before any
                // buffer is allocated.
                ReadMethodStateResult read = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, (int)s_maxConfigurationFileSize + 1,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(read.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(read.Data.IsNull, Is.True);
            }
        }

        [Test]
        public async Task ConfigurationFileReadWithIntMaxValueLengthReturnsBadEncodingLimitsExceededAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(103, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);

                // int.MaxValue exceeds Array.MaxLength for byte[] and would
                // throw OutOfMemoryException if allocated before the size
                // check. The fix must reject it up-front with
                // BadEncodingLimitsExceeded instead of allocating and
                // truncating.
                ReadMethodStateResult read = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, int.MaxValue,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(read.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(read.Data.IsNull, Is.True);
            }
        }

        [Test]
        public async Task ConfigurationFileReadCumulativeWithinRemainingSucceedsAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(104, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);

                // Normal remaining-length behavior: a first, small Read
                // consumes part of the quota; a second Read that fits inside
                // what is left must still succeed and return the rest of the
                // content.
                ReadMethodStateResult first = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, 2, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(first.ServiceResult), Is.True);
                Assert.That(first.Data.ToArray(), Is.EqualTo(new byte[] { 0x10, 0x20 }));

                ReadMethodStateResult second = await file.Read.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, (int)s_maxConfigurationFileSize - 2,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(second.ServiceResult), Is.True);
                Assert.That(second.Data.ToArray(), Is.EqualTo(new byte[] { 0x30, 0x40 }));
            }
        }

        [Test]
        public async Task ConfigurationFileReadCumulativeExceedingRemainingReturnsBadEncodingLimitsExceededAsync()
        {
            var content = ByteString.From(new byte[s_maxConfigurationFileSize]);
            var provider = new FakeConfigurationFileProvider(content);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(105, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);

                // Consume all but the last 5 bytes of the quota with real
                // data, then request 6 more: the cumulative total (already
                // processed + requested) exceeds the maximum even though
                // neither call individually looks oversized.
                ReadMethodStateResult first = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, (int)s_maxConfigurationFileSize - 5,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(first.ServiceResult), Is.True);
                Assert.That(first.Data.Length, Is.EqualTo(s_maxConfigurationFileSize - 5));

                ReadMethodStateResult second = await file.Read.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, 6, CancellationToken.None).ConfigureAwait(false);
                Assert.That(second.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(second.Data.IsNull, Is.True);

                // The boundary itself (exactly the remaining 5 bytes) must
                // still succeed.
                ReadMethodStateResult third = await file.Read.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, 5, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(third.ServiceResult), Is.True);
                Assert.That(third.Data.Length, Is.EqualTo(5));
            }
        }

        [Test]
        public async Task ConfigurationFileConfirmUpdateFlowAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig) { RequiresConfirmation = true };
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(9, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                await file.Write!.OnCallAsync!(ctx, file.Write, file.NodeId, open.FileHandle,
                    ByteString.From([0x42]), CancellationToken.None).ConfigureAwait(false);

                ConfigurationFileCloseAndUpdateMethodStateResult update = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, versionToUpdate: 1, revertAfterTime: 0, restartDelayTime: 0)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(update.ServiceResult), Is.True);
                Assert.That(update.UpdateId.Guid, Is.Not.EqualTo(Guid.Empty),
                    "a confirmation-required update returns a non-empty UpdateId");

                ConfigurationFileConfirmUpdateMethodStateResult wrong = await file.ConfirmUpdate!.OnCallAsync!(
                    ctx, file.ConfirmUpdate, file.NodeId, Uuid.NewUuid(), CancellationToken.None).ConfigureAwait(false);
                Assert.That(wrong.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));

                ConfigurationFileConfirmUpdateMethodStateResult confirm = await file.ConfirmUpdate.OnCallAsync!(
                    ctx, file.ConfirmUpdate, file.NodeId, update.UpdateId, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(confirm.ServiceResult), Is.True);
                Assert.That(provider.ConfirmCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ConfigurationFileConfirmationWithoutRecoveryIsRejectedBeforeValidationAsync()
        {
            var provider = new FakeNonRecoverableConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(901, 1));
                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                await file.Write!.OnCallAsync!(ctx, file.Write, file.NodeId, open.FileHandle,
                    ByteString.From([0x42]), CancellationToken.None).ConfigureAwait(false);

                ConfigurationFileCloseAndUpdateMethodStateResult result = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, versionToUpdate: 1, revertAfterTime: 1000, restartDelayTime: 0)
                    .ConfigureAwait(false);

                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(provider.ValidateCount, Is.Zero);
                Assert.That(provider.ApplyCount, Is.Zero);
                Assert.That(provider.CurrentConfiguration, Is.EqualTo(s_initialConfig));
            }
        }

        [Test]
        public async Task ConfigurationFilePendingUpdateIsRecoveredAndConfirmedAfterRestartAsync()
        {
            var store = new FakeConfigurationFileStore(s_initialConfig);
            var provider = new FakeConfigurationFileProvider(store) { RequiresConfirmation = true };
            ConfigurationFileCloseAndUpdateMethodStateResult update;
            {
                Harness first = await CreateHarnessAsync(
                    new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
                using (first.Manager)
                {
                    ApplicationConfigurationFileState file = first.Node.ConfigurationFile!;
                    SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(902, 1));
                    OpenMethodStateResult open = await OpenAsync(
                        file, ctx, OpenFileMode.Read | OpenFileMode.Write).ConfigureAwait(false);
                    await file.Write!.OnCallAsync!(ctx, file.Write, file.NodeId, open.FileHandle,
                        ByteString.From([0x42]), CancellationToken.None).ConfigureAwait(false);
                    update = await CloseAndUpdateAsync(
                        file, ctx, open.FileHandle, 1, 60_000, 0).ConfigureAwait(false);
                }
            }

            Assert.That(ServiceResult.IsGood(update.ServiceResult), Is.True);
            Assert.That(provider.ApplyCount, Is.EqualTo(1));

            var restartedProvider = new FakeConfigurationFileProvider(store) { RequiresConfirmation = true };
            Harness restarted = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = restartedProvider }).ConfigureAwait(false);
            using (restarted.Manager)
            {
                ApplicationConfigurationFileState file = restarted.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(903, 1));
                ConfigurationFileConfirmUpdateMethodStateResult confirm = await file.ConfirmUpdate!.OnCallAsync!(
                    ctx,
                    file.ConfirmUpdate,
                    file.NodeId,
                    update.UpdateId,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(confirm.ServiceResult), Is.True);
                Assert.That(restartedProvider.ApplyCount, Is.EqualTo(1));
                Assert.That(restartedProvider.ConfirmCount, Is.EqualTo(1));
                Assert.That(await restartedProvider.GetPendingUpdateAsync().ConfigureAwait(false), Is.Null);
            }
        }

        [Test]
        public async Task ExpiredConfigurationFileUpdateIsRevertedBeforeServingAfterRestartAsync()
        {
            var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var store = new FakeConfigurationFileStore(s_initialConfig);
            var provider = new FakeConfigurationFileProvider(store) { RequiresConfirmation = true };
            {
                Harness first = await CreateHarnessAsync(
                    new ServerConfigurationOptions { ConfigurationFileProvider = provider },
                    timeProvider).ConfigureAwait(false);
                using (first.Manager)
                {
                    ApplicationConfigurationFileState file = first.Node.ConfigurationFile!;
                    SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(904, 1));
                    OpenMethodStateResult open = await OpenAsync(
                        file, ctx, OpenFileMode.Read | OpenFileMode.Write).ConfigureAwait(false);
                    await file.Write!.OnCallAsync!(ctx, file.Write, file.NodeId, open.FileHandle,
                        ByteString.From([0x42]), CancellationToken.None).ConfigureAwait(false);
                    ConfigurationFileCloseAndUpdateMethodStateResult update = await CloseAndUpdateAsync(
                        file, ctx, open.FileHandle, 1, 1000, 0).ConfigureAwait(false);
                    Assert.That(ServiceResult.IsGood(update.ServiceResult), Is.True);
                }
            }

            timeProvider.Advance(TimeSpan.FromMilliseconds(1001));
            var restartedProvider = new FakeConfigurationFileProvider(store) { RequiresConfirmation = true };
            Harness restarted = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = restartedProvider },
                timeProvider).ConfigureAwait(false);
            using (restarted.Manager)
            {
                Assert.That(restartedProvider.RevertCount, Is.EqualTo(1));
                Assert.That(restartedProvider.CurrentConfiguration, Is.EqualTo(s_initialConfig));
                Assert.That(await restartedProvider.GetPendingUpdateAsync().ConfigureAwait(false), Is.Null);
            }
        }

        [Test]
        public async Task ConfigurationFileUpdateNonAdminThrowsBadUserAccessDeniedAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext anonymous = CreateContext(UserTokenType.Anonymous, MessageSecurityMode.SignAndEncrypt);

                ServiceResultException ex = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await OpenAsync(file, anonymous, OpenFileMode.Read | OpenFileMode.Write).ConfigureAwait(false)))!;
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
        }

        [Test]
        public async Task ConfigurationFileOpenWriteWhileOtherSessionTransactionActiveReturnsBadTransactionPendingAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;

                var otherSession = new NodeId(4242, 1);
                harness.Coordinator.Stage(otherSession, new PushConfigurationOperation
                {
                    CommitAsync = _ => Task.CompletedTask
                });

                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(1, 1));
                ServiceResultException ex = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write).ConfigureAwait(false)))!;
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTransactionPending));

                harness.Coordinator.CancelChanges(otherSession);
            }
        }

        [Test]
        public async Task ResetToServerDefaultsNonAdminThrowsBadUserAccessDeniedAsync()
        {
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ResetProvider = new FakeResetProvider() }).ConfigureAwait(false);
            using (harness.Manager)
            {
                MethodState reset = harness.Node.ResetToServerDefaults!;
                SessionSystemContext ctx = CreateContext(UserTokenType.Anonymous, MessageSecurityMode.SignAndEncrypt);

                ServiceResultException ex = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await reset.OnCallMethod2Async!(ctx, reset, harness.Node.NodeId, ArrayOf<Variant>.Empty,
                        new List<Variant>(), CancellationToken.None).ConfigureAwait(false)))!;
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
        }

        [Test]
        public async Task ResetToServerDefaultsUnauthenticatedChannelThrowsBadSecurityModeInsufficientAsync()
        {
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ResetProvider = new FakeResetProvider() }).ConfigureAwait(false);
            using (harness.Manager)
            {
                MethodState reset = harness.Node.ResetToServerDefaults!;
                SessionSystemContext ctx = CreateContext(
                    UserTokenType.UserName, MessageSecurityMode.None, ObjectIds.WellKnownRole_SecurityAdmin);

                ServiceResultException ex = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await reset.OnCallMethod2Async!(ctx, reset, harness.Node.NodeId, ArrayOf<Variant>.Empty,
                        new List<Variant>(), CancellationToken.None).ConfigureAwait(false)))!;
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            }
        }

        [Test]
        public async Task ResetToServerDefaultsWhileOtherSessionTransactionActiveReturnsBadTransactionPendingAsync()
        {
            var resetProvider = new FakeResetProvider();
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ResetProvider = resetProvider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                var otherSession = new NodeId(7777, 1);
                harness.Coordinator.Stage(otherSession, new PushConfigurationOperation
                {
                    CommitAsync = _ => Task.CompletedTask
                });

                MethodState reset = harness.Node.ResetToServerDefaults!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(1, 1));

                ServiceResult result = await reset.OnCallMethod2Async!(
                    ctx, reset, harness.Node.NodeId, ArrayOf<Variant>.Empty, new List<Variant>(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTransactionPending));
                Assert.That(resetProvider.InvocationCount, Is.Zero);

                harness.Coordinator.CancelChanges(otherSession);
            }
        }

        [Test]
        public async Task ConfigurationFileStaleInactivityCallbackDoesNotCloseRefreshedHandleAsync()
        {
            var timeProvider = new ControllableTimeProvider();
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions
                {
                    ConfigurationFileProvider = provider,
                    ConfigurationFileActivityTimeout = 1000.0
                },
                timeProvider).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(2001, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.True);

                ControllableTimer[] armed = ActivityTimers(timeProvider);
                Assert.That(armed, Has.Length.EqualTo(1), "Open must arm exactly one inactivity timer");
                ControllableTimer t1 = armed[0];

                // Activity supersedes T1 and arms T2.
                GetPositionMethodStateResult pos = await file.GetPosition!.OnCallAsync!(
                    ctx, file.GetPosition, file.NodeId, open.FileHandle, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(pos.ServiceResult), Is.True);
                Assert.That(t1.IsDisposed, Is.True, "activity must dispose the superseded timer");

                // A callback still queued by the superseded T1 must not touch the
                // live handle refreshed by the activity above.
                t1.Fire();

                Assert.Multiple(() =>
                {
                    Assert.That(file.OpenCount!.Value, Is.EqualTo((ushort)1),
                        "a stale timer callback must not close the refreshed handle");
                    Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.True,
                        "a stale timer callback must not clear the write-open flag");
                });

                // The current timer callback still closes the (now inactive) handle.
                ControllableTimer t2 = ActivityTimers(timeProvider)[^1];
                t2.Fire();

                Assert.Multiple(() =>
                {
                    Assert.That(file.OpenCount!.Value, Is.Zero,
                        "the current timer must close the inactive handle");
                    Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False,
                        "closing on inactivity must clear the write-open flag");
                });
            }
        }

        [Test]
        public async Task ConfigurationFileStaleInactivityCallbackDoesNotCloseOtherSessionHandleAsync()
        {
            var timeProvider = new ControllableTimeProvider();
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions
                {
                    ConfigurationFileProvider = provider,
                    ConfigurationFileActivityTimeout = 1000.0
                },
                timeProvider).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext sessionA = CreateAdminContextForSession(new NodeId(3001, 1));
                SessionSystemContext sessionB = CreateAdminContextForSession(new NodeId(3002, 1));

                OpenMethodStateResult openA = await OpenAsync(file, sessionA, OpenFileMode.Read)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(openA.ServiceResult), Is.True);
                ControllableTimer t1 = ActivityTimers(timeProvider)[^1];

                // Session B opens; last-open-wins evicts A's handle and arms a new
                // timer for B.
                OpenMethodStateResult openB = await OpenAsync(file, sessionB, OpenFileMode.Read)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(openB.ServiceResult), Is.True);
                Assert.That(t1.IsDisposed, Is.True);

                // Session A's stale timer callback must not close Session B's handle.
                t1.Fire();

                ReadMethodStateResult readB = await file.Read!.OnCallAsync!(
                    sessionB, file.Read, file.NodeId, openB.FileHandle, 4, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(file.OpenCount!.Value, Is.EqualTo((ushort)1),
                        "Session A's stale callback must not close Session B's handle");
                    Assert.That(ServiceResult.IsGood(readB.ServiceResult), Is.True,
                        "Session B's handle must stay usable after Session A's stale callback");
                });
            }
        }

        [Test]
        public async Task ConfigurationFileCurrentInactivityCallbackClosesHandleAsync()
        {
            var timeProvider = new ControllableTimeProvider();
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions
                {
                    ConfigurationFileProvider = provider,
                    ConfigurationFileActivityTimeout = 1000.0
                },
                timeProvider).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(4001, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.True);

                ControllableTimer current = ActivityTimers(timeProvider)[^1];
                current.Fire();

                Assert.Multiple(() =>
                {
                    Assert.That(file.OpenCount!.Value, Is.Zero,
                        "the current inactivity timer must close the open handle");
                    Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False,
                        "inactivity close must clear the coordinator write-open flag");
                });

                // The handle is gone: a Read with the old handle now fails.
                ReadMethodStateResult read = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, 4, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(read.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            }
        }

        [Test]
        public async Task ConfigurationFileCloseInvalidatesQueuedInactivityCallbackAsync()
        {
            var timeProvider = new ControllableTimeProvider();
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions
                {
                    ConfigurationFileProvider = provider,
                    ConfigurationFileActivityTimeout = 1000.0
                },
                timeProvider).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(5001, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                ControllableTimer t1 = ActivityTimers(timeProvider)[^1];

                CloseMethodStateResult close = await file.Close!.OnCallAsync!(
                    ctx, file.Close, file.NodeId, open.FileHandle, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(close.ServiceResult), Is.True);
                Assert.That(t1.IsDisposed, Is.True);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False);

                // A callback queued before Close must be an inert no-op.
                Assert.That(() => t1.Fire(), Throws.Nothing);
                Assert.That(file.OpenCount!.Value, Is.Zero);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False);

                // Reopen: the superseded callback must not close the new handle.
                OpenMethodStateResult reopen = await OpenAsync(file, ctx, OpenFileMode.Read)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(reopen.ServiceResult), Is.True);

                t1.Fire();
                Assert.That(file.OpenCount!.Value, Is.EqualTo((ushort)1),
                    "a callback queued before Close must not close a later reopened handle");
            }
        }

        [Test]
        public async Task ConfigurationFileDisposeInvalidatesQueuedInactivityCallbackAsync()
        {
            var timeProvider = new ControllableTimeProvider();
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions
                {
                    ConfigurationFileProvider = provider,
                    ConfigurationFileActivityTimeout = 1000.0
                },
                timeProvider).ConfigureAwait(false);

            ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
            SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(6001, 1));

            OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);
            ControllableTimer t1 = ActivityTimers(timeProvider)[^1];

            // Disposing the manager disposes the ApplicationConfigurationFile.
            harness.Manager.Dispose();
            Assert.That(t1.IsDisposed, Is.True);

            // A callback queued before Dispose must be a harmless no-op.
            Assert.That(() => t1.Fire(), Throws.Nothing);
        }

        [Test]
        public async Task ConfigurationFileRepeatedActivityDoesNotLeakTimersOrChangeOpenCountAsync()
        {
            var timeProvider = new ControllableTimeProvider();
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions
                {
                    ConfigurationFileProvider = provider,
                    ConfigurationFileActivityTimeout = 1000.0
                },
                timeProvider).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(7001, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read).ConfigureAwait(false);
                Assert.That(file.OpenCount!.Value, Is.EqualTo((ushort)1));

                for (int i = 0; i < 5; i++)
                {
                    GetPositionMethodStateResult pos = await file.GetPosition!.OnCallAsync!(
                        ctx, file.GetPosition, file.NodeId, open.FileHandle, CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(ServiceResult.IsGood(pos.ServiceResult), Is.True);
                }

                ControllableTimer[] activity = ActivityTimers(timeProvider);
                int undisposed = activity.Count(t => !t.IsDisposed);

                Assert.Multiple(() =>
                {
                    Assert.That(activity, Has.Length.EqualTo(6),
                        "each Open/activity arms exactly one timer (1 Open + 5 activities)");
                    Assert.That(undisposed, Is.EqualTo(1),
                        "only the most recently armed timer may remain live - no timer leak");
                    Assert.That(file.OpenCount!.Value, Is.EqualTo((ushort)1),
                        "repeated activity must not change OpenCount");
                });
            }
        }

        private static ControllableTimer[] ActivityTimers(ControllableTimeProvider timeProvider)
        {
            // The ApplicationConfigurationFile arms its inactivity timer with an
            // ActivityTimerState; filter on it so any unrelated node-manager
            // timer created through the same provider is ignored.
            return [.. timeProvider.Timers.Where(
                t => string.Equals(t.State?.GetType().Name, "ActivityTimerState", StringComparison.Ordinal))];
        }

        private static ValueTask<OpenMethodStateResult> OpenAsync(
            ApplicationConfigurationFileState file, ISystemContext ctx, OpenFileMode mode)
        {
            return file.Open!.OnCallAsync!(ctx, file.Open, file.NodeId, (byte)mode, CancellationToken.None);
        }

        private static long GetMaxConfigurationFileSize()
        {
            FieldInfo? field = typeof(ApplicationConfigurationFile).GetField(
                "kMaxConfigurationFileSize", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "kMaxConfigurationFileSize field not found via reflection");
            return (long)field!.GetRawConstantValue()!;
        }

        private static ValueTask<ConfigurationFileCloseAndUpdateMethodStateResult> CloseAndUpdateAsync(
            ApplicationConfigurationFileState file,
            ISystemContext ctx,
            uint fileHandle,
            uint versionToUpdate,
            double revertAfterTime = 0,
            double restartDelayTime = 0,
            bool withoutTargets = false)
        {
            // §7.8.5.2: "There must be at least one target."
            ArrayOf<ConfigurationUpdateTargetType> targets = withoutTargets
                ? ArrayOf<ConfigurationUpdateTargetType>.Empty
                : [new ConfigurationUpdateTargetType
                {
                    Path = "ApplicationIdentity",
                    UpdateType = ConfigurationUpdateType.InsertOrReplace
                }];
            return file.CloseAndUpdate!.OnCallAsync!(
                ctx,
                file.CloseAndUpdate,
                file.NodeId,
                fileHandle,
                versionToUpdate,
                targets,
                revertAfterTime,
                restartDelayTime,
                CancellationToken.None);
        }

        private async Task<Harness> CreateHarnessAsync(
            ServerConfigurationOptions options,
            TimeProvider? timeProvider = null,
            bool asyncConfiguration = false,
            Action<ServerConfigurationState>? beforeConfiguration = null)
        {
            IServerInternal serverInternal = m_server.CurrentInstance;
            var coordinator = new PushConfigurationTransactionCoordinator(serverInternal.Telemetry);
            var manager = new ConfigurationNodeManager(
                serverInternal,
                m_fixture.Config,
                serverInternal.Telemetry.CreateLogger<ConfigurationNodeManager>(),
                timeProvider: timeProvider,
                coordinator,
                pendingKeyStore: null,
                keyGenerator: null,
                trustListEffectHandler: null,
                serverConfigurationOptions: options);

            IDictionary<NodeId, IList<IReference>> externalReferences =
                new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalReferences, CancellationToken.None).ConfigureAwait(false);
            var node = manager.FindPredefinedNode<ServerConfigurationState>(ObjectIds.ServerConfiguration);
            Assert.That(node, Is.Not.Null, "ServerConfiguration node not materialised");
            beforeConfiguration?.Invoke(node);
            if (asyncConfiguration)
            {
                await manager.CreateServerConfigurationAsync(serverInternal.DefaultSystemContext, m_fixture.Config)
                    .ConfigureAwait(false);
            }
            else
            {
                manager.CreateServerConfiguration(serverInternal.DefaultSystemContext, m_fixture.Config);
            }
            await manager.RecoverConfigurationFileUpdateAsync(CancellationToken.None).ConfigureAwait(false);

            return new Harness(manager, node, coordinator);
        }

        private static SessionSystemContext CreateContext(
            UserTokenType tokenType,
            MessageSecurityMode securityMode,
            params NodeId[] grantedRoles)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(tokenType);
            identity.Setup(i => i.DisplayName).Returns(tokenType.ToString());
            identity.Setup(i => i.GrantedRoleIds).Returns(ArrayOf.Wrapped(grantedRoles));
            var endpoint = new EndpointDescription { SecurityMode = securityMode };
            var channelContext = new SecureChannelContext("test", endpoint, RequestEncoding.Binary);
            var operationContext = new OperationContext(
                new RequestHeader(),
                channelContext,
                RequestType.Call,
                RequestLifetime.None,
                identity.Object);
            return new SessionSystemContext(operationContext, s_telemetry)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
        }

        private static SessionSystemContext CreateAdminContextForSession(NodeId sessionId)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns(nameof(UserTokenType.UserName));
            identity.Setup(i => i.GrantedRoleIds).Returns(ArrayOf.Wrapped(ObjectIds.WellKnownRole_SecurityAdmin));

            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(sessionId);
            session.Setup(s => s.EffectiveIdentity).Returns(identity.Object);
            session.Setup(s => s.PreferredLocales).Returns([]);

            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
            var channelContext = new SecureChannelContext("test", endpoint, RequestEncoding.Binary);
            var operationContext = new OperationContext(
                new RequestHeader(),
                channelContext,
                RequestType.Call,
                RequestLifetime.None,
                session.Object);
            return new SessionSystemContext(operationContext, s_telemetry)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
        }

        private sealed class Harness(
            ConfigurationNodeManager manager,
            ServerConfigurationState node,
            PushConfigurationTransactionCoordinator coordinator)
        {
            public ConfigurationNodeManager Manager { get; } = manager;
            public ServerConfigurationState Node { get; } = node;
            public PushConfigurationTransactionCoordinator Coordinator { get; } = coordinator;
        }

        /// <summary>
        /// Implements the ConfigurationFile provider against retained test storage.
        /// </summary>
        private sealed class FakeConfigurationFileProvider : IRecoverableApplicationConfigurationFileProvider
        {
            /// <summary>
            /// The retained storage used by this provider instance.
            /// </summary>
            private readonly FakeConfigurationFileStore m_store;

            /// <summary>
            /// Initializes the provider with a new in-memory storage instance.
            /// </summary>
            /// <param name="initial">The initial configuration content.</param>
            public FakeConfigurationFileProvider(ByteString initial)
                : this(new FakeConfigurationFileStore(initial))
            {
            }

            /// <summary>
            /// Initializes the provider with an existing storage instance.
            /// </summary>
            /// <param name="store">The storage retained across provider instances.</param>
            public FakeConfigurationFileProvider(FakeConfigurationFileStore store)
            {
                m_store = store ?? throw new ArgumentNullException(nameof(store));
            }

            /// <summary>
            /// Gets or sets the active configuration version.
            /// </summary>
            public uint CurrentVersion
            {
                get => m_store.CurrentVersion;
                set => m_store.CurrentVersion = value;
            }

            /// <summary>
            /// Gets the last active configuration update time.
            /// </summary>
            public DateTime LastUpdateTime => m_store.LastUpdateTime;

            /// <summary>
            /// Gets or sets whether updates require confirmation.
            /// </summary>
            public bool RequiresConfirmation { get; set; }

            /// <summary>
            /// Gets or sets whether validation should fail in this test.
            /// </summary>
            public bool ValidateShouldThrow { get; set; }

            /// <summary>
            /// Gets or sets whether application should fail in this test.
            /// </summary>
            public bool ApplyShouldThrow { get; set; }

            /// <summary>
            /// Gets or sets whether application should throw an unexpected error in this test.
            /// </summary>
            public bool ApplyShouldThrowUnexpected { get; set; }

            /// <summary>
            /// Gets the number of updates applied by this fake provider.
            /// </summary>
            public int ApplyCount => m_store.ApplyCount;

            /// <summary>
            /// Gets the number of updates confirmed by this fake provider.
            /// </summary>
            public int ConfirmCount => m_store.ConfirmCount;

            /// <summary>
            /// Gets the number of updates reverted by this fake provider.
            /// </summary>
            public int RevertCount => m_store.RevertCount;

            /// <summary>
            /// Gets the most recently applied configuration.
            /// </summary>
            public ByteString LastApplied => m_store.LastApplied;

            /// <summary>
            /// Gets the current configuration content.
            /// </summary>
            public ByteString CurrentConfiguration => m_store.Content;

            /// <summary>
            /// Reads the current configuration content.
            /// </summary>
            public ValueTask<ByteString> ReadConfigurationAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask<ByteString>(m_store.Content);
            }

            /// <summary>
            /// Validates configuration content and records the validation call.
            /// </summary>
            public ValueTask ValidateConfigurationAsync(ByteString configuration, CancellationToken cancellationToken = default)
            {
                ValidateCount++;
                if (ValidateShouldThrow)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument, "invalid configuration");
                }
                return default;
            }

            /// <summary>
            /// Applies configuration content without confirmation.
            /// </summary>
            public ValueTask ApplyConfigurationAsync(ByteString configuration, CancellationToken cancellationToken = default)
            {
                if (ApplyShouldThrow)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "apply failed");
                }
                m_store.ApplyCount++;
                m_store.LastApplied = configuration;
                m_store.Content = configuration;
                CurrentVersion++;
                m_store.LastUpdateTime = DateTime.UtcNow;
                return default;
            }

            /// <summary>
            /// Records confirmation of the latest update.
            /// </summary>
            public ValueTask ConfirmUpdateAsync(CancellationToken cancellationToken = default)
            {
                m_store.ConfirmCount++;
                return default;
            }

            /// <summary>
            /// Records reversion of the latest update.
            /// </summary>
            public ValueTask RevertUpdateAsync(CancellationToken cancellationToken = default)
            {
                m_store.RevertCount++;
                return default;
            }

            /// <summary>
            /// Gets the number of validation calls.
            /// </summary>
            public int ValidateCount { get; private set; }

            /// <summary>
            /// Retains a pending update and its rollback state.
            /// </summary>
            public ValueTask BeginUpdateAsync(
                Uuid updateId,
                ByteString configuration,
                ArrayOf<ConfigurationUpdateTargetType> targets,
                DateTime applyAtUtc,
                DateTime? revertAtUtc,
                CancellationToken cancellationToken = default)
            {
                if (m_store.PendingUpdate is PendingApplicationConfigurationUpdate existing)
                {
                    if (existing.UpdateId.Guid == updateId.Guid)
                    {
                        return default;
                    }
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "update already pending");
                }

                m_store.PreviousContent = m_store.Content;
                m_store.PreviousVersion = CurrentVersion;
                m_store.PreviousUpdateTime = LastUpdateTime;
                m_store.PendingContent = configuration;
                m_store.PendingUpdate = new PendingApplicationConfigurationUpdate(
                    updateId, applyAtUtc, revertAtUtc, IsApplied: false);
                return default;
            }

            /// <summary>
            /// Returns the pending update retained by the shared test store.
            /// </summary>
            public ValueTask<PendingApplicationConfigurationUpdate?> GetPendingUpdateAsync(
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<PendingApplicationConfigurationUpdate?>(m_store.PendingUpdate);
            }

            /// <summary>
            /// Applies the staged update once and records its active state.
            /// </summary>
            public ValueTask ApplyPendingUpdateAsync(
                Uuid updateId,
                CancellationToken cancellationToken = default)
            {
                PendingApplicationConfigurationUpdate pending = RequirePending(updateId);
                if (pending.IsApplied)
                {
                    return default;
                }
                if (ApplyShouldThrow)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "apply failed");
                }
                if (ApplyShouldThrowUnexpected)
                {
                    throw new InvalidOperationException("unexpected apply failure");
                }

                m_store.ApplyCount++;
                m_store.LastApplied = m_store.PendingContent;
                m_store.Content = m_store.PendingContent;
                CurrentVersion++;
                m_store.LastUpdateTime = DateTime.UtcNow;
                m_store.PendingUpdate = pending with { IsApplied = true };
                return default;
            }

            /// <summary>
            /// Confirms and clears the staged update.
            /// </summary>
            public ValueTask ConfirmPendingUpdateAsync(
                Uuid updateId,
                CancellationToken cancellationToken = default)
            {
                PendingApplicationConfigurationUpdate pending = RequirePending(updateId);
                if (!pending.IsApplied)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "update not applied");
                }
                m_store.ConfirmCount++;
                m_store.PendingUpdate = null;
                return default;
            }

            /// <summary>
            /// Restores the previous configuration and clears the staged update.
            /// </summary>
            public ValueTask RevertPendingUpdateAsync(
                Uuid updateId,
                CancellationToken cancellationToken = default)
            {
                PendingApplicationConfigurationUpdate pending = RequirePending(updateId);
                if (pending.IsApplied)
                {
                    m_store.Content = m_store.PreviousContent;
                    CurrentVersion = m_store.PreviousVersion;
                    m_store.LastUpdateTime = m_store.PreviousUpdateTime;
                }
                m_store.RevertCount++;
                m_store.PendingUpdate = null;
                return default;
            }

            /// <summary>
            /// Gets the pending update when its identifier matches the request.
            /// </summary>
            private PendingApplicationConfigurationUpdate RequirePending(Uuid updateId)
            {
                if (m_store.PendingUpdate is not PendingApplicationConfigurationUpdate pending ||
                    pending.UpdateId.Guid != updateId.Guid)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument, "update id mismatch");
                }
                return pending;
            }
        }

        /// <summary>
        /// Retains fake provider state so tests can create a new provider after a restart.
        /// </summary>
        private sealed class FakeConfigurationFileStore
        {
            /// <summary>
            /// Initializes the store with its initial active configuration.
            /// </summary>
            /// <param name="initial">The initial configuration content.</param>
            public FakeConfigurationFileStore(ByteString initial)
            {
                Content = initial;
                PreviousContent = initial;
                LastUpdateTime = DateTime.UtcNow;
                PreviousUpdateTime = LastUpdateTime;
            }

            /// <summary>
            /// Gets or sets the active configuration content.
            /// </summary>
            public ByteString Content { get; set; }

            /// <summary>
            /// Gets or sets the configuration content to restore on reversion.
            /// </summary>
            public ByteString PreviousContent { get; set; }

            /// <summary>
            /// Gets or sets the version to restore on reversion.
            /// </summary>
            public uint PreviousVersion { get; set; }

            /// <summary>
            /// Gets or sets the last active configuration update time.
            /// </summary>
            public DateTime LastUpdateTime { get; set; }

            /// <summary>
            /// Gets or sets the last update time to restore on reversion.
            /// </summary>
            public DateTime PreviousUpdateTime { get; set; }

            /// <summary>
            /// Gets or sets the configuration content awaiting application.
            /// </summary>
            public ByteString PendingContent { get; set; }

            /// <summary>
            /// Gets or sets the durable update awaiting confirmation or reversion.
            /// </summary>
            public PendingApplicationConfigurationUpdate? PendingUpdate { get; set; }

            /// <summary>
            /// Gets or sets the active configuration version.
            /// </summary>
            public uint CurrentVersion { get; set; } = 1;

            /// <summary>
            /// Gets or sets the number of applied updates.
            /// </summary>
            public int ApplyCount { get; set; }

            /// <summary>
            /// Gets or sets the number of confirmed updates.
            /// </summary>
            public int ConfirmCount { get; set; }

            /// <summary>
            /// Gets or sets the number of reverted updates.
            /// </summary>
            public int RevertCount { get; set; }

            /// <summary>
            /// Gets or sets the most recently applied configuration.
            /// </summary>
            public ByteString LastApplied { get; set; }
        }

        /// <summary>
        /// Models a confirmation-enabled provider without restart recovery support.
        /// </summary>
        private sealed class FakeNonRecoverableConfigurationFileProvider : IApplicationConfigurationFileProvider
        {
            /// <summary>
            /// Initializes the fake provider with the supplied configuration.
            /// </summary>
            /// <param name="initial">The initial configuration content.</param>
            public FakeNonRecoverableConfigurationFileProvider(ByteString initial)
            {
                CurrentConfiguration = initial;
            }

            /// <summary>
            /// Gets the active configuration version.
            /// </summary>
            public uint CurrentVersion => 1;

            /// <summary>
            /// Gets the last update time.
            /// </summary>
            public DateTime LastUpdateTime => DateTime.UtcNow;

            /// <summary>
            /// Gets whether updates require confirmation.
            /// </summary>
            public bool RequiresConfirmation => true;

            /// <summary>
            /// Gets the number of validation calls.
            /// </summary>
            public int ValidateCount { get; private set; }

            /// <summary>
            /// Gets the number of application calls.
            /// </summary>
            public int ApplyCount { get; private set; }

            /// <summary>
            /// Gets the current configuration content.
            /// </summary>
            public ByteString CurrentConfiguration { get; private set; }

            /// <summary>
            /// Reads the current configuration content.
            /// </summary>
            public ValueTask<ByteString> ReadConfigurationAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask<ByteString>(CurrentConfiguration);
            }

            /// <summary>
            /// Records validation of configuration content.
            /// </summary>
            public ValueTask ValidateConfigurationAsync(
                ByteString configuration,
                CancellationToken cancellationToken = default)
            {
                ValidateCount++;
                return default;
            }

            /// <summary>
            /// Applies configuration content.
            /// </summary>
            public ValueTask ApplyConfigurationAsync(
                ByteString configuration,
                CancellationToken cancellationToken = default)
            {
                ApplyCount++;
                CurrentConfiguration = configuration;
                return default;
            }

            /// <summary>
            /// Completes the provider's confirmation operation.
            /// </summary>
            public ValueTask ConfirmUpdateAsync(CancellationToken cancellationToken = default)
            {
                return default;
            }

            /// <summary>
            /// Completes the provider's reversion operation.
            /// </summary>
            public ValueTask RevertUpdateAsync(CancellationToken cancellationToken = default)
            {
                return default;
            }
        }

        private sealed class FakeTargetConfigurationFileProvider : IApplicationConfigurationFileTargetProvider
        {
            private ByteString m_content;

            public FakeTargetConfigurationFileProvider(ByteString initial)
            {
                m_content = initial;
                CurrentVersion = 1;
                LastUpdateTime = DateTime.UtcNow;
            }

            public uint CurrentVersion { get; private set; }
            public DateTime LastUpdateTime { get; private set; }
            public bool RequiresConfirmation => false;
            public bool InterruptsSessions { get; set; }
            public ArrayOf<StatusCode> TargetResults { get; set; }
            public int ApplyCount { get; private set; }
            public ArrayOf<ConfigurationUpdateTargetType> ValidatedTargets { get; private set; }
            public ArrayOf<ConfigurationUpdateTargetType> AppliedTargets { get; private set; }

            public ValueTask<ByteString> ReadConfigurationAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask<ByteString>(m_content);
            }

            public ValueTask ValidateConfigurationAsync(ByteString configuration, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("the target-aware overload must be used");
            }

            public ValueTask<ApplicationConfigurationUpdatePlan> ValidateConfigurationAsync(
                ByteString configuration,
                ArrayOf<ConfigurationUpdateTargetType> targets,
                CancellationToken cancellationToken = default)
            {
                ValidatedTargets = targets;
                return new ValueTask<ApplicationConfigurationUpdatePlan>(new ApplicationConfigurationUpdatePlan
                {
                    TargetResults = TargetResults,
                    InterruptsSessions = InterruptsSessions,
                    NewVersion = CurrentVersion + 1
                });
            }

            public ValueTask ApplyConfigurationAsync(ByteString configuration, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("the target-aware overload must be used");
            }

            public ValueTask ApplyConfigurationAsync(
                ByteString configuration,
                ArrayOf<ConfigurationUpdateTargetType> targets,
                CancellationToken cancellationToken = default)
            {
                AppliedTargets = targets;
                m_content = configuration;
                CurrentVersion++;
                LastUpdateTime = DateTime.UtcNow;
                ApplyCount++;
                return default;
            }

            public ValueTask ConfirmUpdateAsync(CancellationToken cancellationToken = default)
            {
                return default;
            }

            public ValueTask RevertUpdateAsync(CancellationToken cancellationToken = default)
            {
                return default;
            }
        }

        private sealed class FakeResetProvider : IServerConfigurationResetProvider
        {
            public int InvocationCount { get; private set; }

            public ValueTask ResetToServerDefaultsAsync(CancellationToken cancellationToken = default)
            {
                InvocationCount++;
                return default;
            }
        }

        /// <summary>
        /// A <see cref="TimeProvider"/> whose timers never fire on their own:
        /// every created timer is recorded and only fires when a test invokes
        /// <see cref="ControllableTimer.Fire"/>. This makes inactivity-timer
        /// behaviour fully deterministic and lets a test deliberately fire a
        /// superseded or disposed timer to simulate a callback that was already
        /// queued when the timer was replaced.
        /// </summary>
        private sealed class ControllableTimeProvider : TimeProvider
        {
            private readonly List<ControllableTimer> m_timers = [];
            private readonly Lock m_sync = new();

            public IReadOnlyList<ControllableTimer> Timers
            {
                get
                {
                    lock (m_sync)
                    {
                        return [.. m_timers];
                    }
                }
            }

            public override ITimer CreateTimer(
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                var timer = new ControllableTimer(callback, state);
                lock (m_sync)
                {
                    m_timers.Add(timer);
                }
                return timer;
            }
        }

        /// <summary>
        /// A test <see cref="ITimer"/> that captures its callback and state and
        /// fires only when a test calls <see cref="Fire"/>, deliberately even
        /// after <see cref="Dispose"/>, so a queued callback from a superseded
        /// timer can be simulated.
        /// </summary>
        private sealed class ControllableTimer : ITimer
        {
            private readonly TimerCallback m_callback;

            public ControllableTimer(TimerCallback callback, object? state)
            {
                m_callback = callback;
                State = state;
            }

            public object? State { get; }

            public bool IsDisposed { get; private set; }

            public void Fire()
            {
                m_callback(State);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                return !IsDisposed;
            }

            public void Dispose()
            {
                IsDisposed = true;
            }

            public ValueTask DisposeAsync()
            {
                IsDisposed = true;
                return default;
            }
        }
    }
}
