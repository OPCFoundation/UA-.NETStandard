// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
using Opc.Ua.Server.Tests.NodeManager;
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
        /// Keeps explicit permissions separate from namespace defaults after either configuration path.
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
                            if (explicitNode)
                            {
                                Assert.That(node.RolePermissions.Count, Is.EqualTo(1), node.BrowseName.ToString());
                                Assert.Multiple(() =>
                                {
                                    Assert.That(node.RolePermissions[0].RoleId,
                                        Is.EqualTo(ObjectIds.WellKnownRole_Observer));
                                    Assert.That(node.RolePermissions[0].Permissions,
                                        Is.EqualTo((uint)(PermissionType.Browse | PermissionType.Read)));
                                });
                            }
                            else
                            {
                                Assert.That(node.RolePermissions.IsEmpty, Is.True,
                                    $"Namespace permissions must remain inherited by {node.BrowseName}.");
                            }
                        }

                        var children = new List<BaseInstanceState>();
                        node.GetChildren(serverInternal.DefaultSystemContext, children);
                        foreach (BaseInstanceState child in children)
                        {
                            pending.Push(child);
                        }
                    }

                    if (explicitPermissions || restricted)
                    {
                        SessionSystemContext context = CreateContext(
                            UserTokenType.UserName, MessageSecurityMode.SignAndEncrypt,
                            ObjectIds.WellKnownRole_SecurityAdmin);
                        (ServiceResult result, DataValue value) =
                            await AsyncCustomNodeManager.ReadConfigurationPermissionAttributeAsync(
                                serverInternal, harness.Node, ToServerContext(serverInternal, context),
                                Attributes.RolePermissions,
                                default, default, new DataValue(), CancellationToken.None).ConfigureAwait(false);
                        Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(value.WrappedValue.TryGetStructure(
                            out ArrayOf<RolePermissionType> advertised), Is.True);
                        RolePermissionType stored = explicitPermissions
                            ? harness.Node.RolePermissions[0]
                            : metadata.DefaultRolePermissions.Value[0];
                        RolePermissionType advertisedRole = advertised.ToList().Single(
                            role => role.RoleId == stored.RoleId);
                        Assert.That(advertisedRole, Is.Not.SameAs(stored));
                        uint storedPermissions = stored.Permissions;
                        advertisedRole.Permissions = 0;
                        Assert.That(stored.Permissions, Is.EqualTo(storedPermissions));
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

        /// <summary>
        /// Revoking an inherited role removes its access after either startup path.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NamespacePermissionRevocationAppliesAfterConfigurationAsync(bool asyncConfiguration)
        {
            IServerInternal serverInternal = m_server.CurrentInstance;
            NamespaceMetadataState? metadata = await serverInternal.ConfigurationNodeManager
                .GetNamespaceMetadataStateAsync(0).ConfigureAwait(false);
            Assert.That(metadata?.DefaultRolePermissions, Is.Not.Null);
            Assert.That(metadata?.DefaultUserRolePermissions, Is.Not.Null);
            ArrayOf<RolePermissionType> originalRoles = metadata!.DefaultRolePermissions!.Value;
            ArrayOf<RolePermissionType> originalUserRoles = metadata.DefaultUserRolePermissions!.Value;
            try
            {
                metadata.DefaultRolePermissions.Value =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_Observer,
                        Permissions = (uint)PermissionType.Browse
                    }
                ];
                metadata.DefaultUserRolePermissions.Value = ArrayOf<RolePermissionType>.Empty;
                await metadata.ClearChangeMasksAsync(serverInternal.DefaultSystemContext, true)
                    .ConfigureAwait(false);
                Harness harness = await CreateHarnessAsync(
                    new ServerConfigurationOptions(),
                    asyncConfiguration: asyncConfiguration,
                    beforeConfiguration: root =>
                    {
                        root.RolePermissions = ArrayOf<RolePermissionType>.Empty;
                        root.UserRolePermissions = ArrayOf<RolePermissionType>.Empty;
                        root.GetCertificates!.RolePermissions = ArrayOf<RolePermissionType>.Empty;
                        root.GetCertificates.UserRolePermissions = ArrayOf<RolePermissionType>.Empty;
                    }).ConfigureAwait(false);
                using (harness.Manager)
                {
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Observer,
                        PermissionType.Browse, StatusCodes.Good).ConfigureAwait(false);
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Operator,
                        PermissionType.Browse, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
                    metadata.DefaultRolePermissions.Value =
                    [
                        new RolePermissionType
                        {
                            RoleId = ObjectIds.WellKnownRole_Operator,
                            Permissions = (uint)PermissionType.Browse
                        }
                    ];
                    await metadata.ClearChangeMasksAsync(serverInternal.DefaultSystemContext, true)
                        .ConfigureAwait(false);
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Observer,
                        PermissionType.Browse, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Operator,
                        PermissionType.Browse, StatusCodes.Good).ConfigureAwait(false);
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_SecurityAdmin,
                        PermissionType.Browse, StatusCodes.Good).ConfigureAwait(false);
                    await AssertPermissionAsync(harness, harness.Node.GetCertificates!,
                        ObjectIds.WellKnownRole_SecurityAdmin, PermissionType.Call, StatusCodes.Good)
                        .ConfigureAwait(false);
                    await AssertPermissionAsync(harness, harness.Node.GetCertificates!,
                        ObjectIds.WellKnownRole_Operator, PermissionType.Call, StatusCodes.BadUserAccessDenied)
                        .ConfigureAwait(false);

                    ArrayOf<RolePermissionType> explicitRoles =
                    [
                        new RolePermissionType
                        {
                            RoleId = ObjectIds.WellKnownRole_Observer,
                            Permissions = (uint)PermissionType.Browse
                        }
                    ];
                    harness.Node.RolePermissions = explicitRoles;
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Observer,
                        PermissionType.Browse, StatusCodes.Good).ConfigureAwait(false);
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Operator,
                        PermissionType.Browse, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
                    Assert.That(harness.Node.RolePermissions, Is.EqualTo(explicitRoles));

                    harness.Node.RolePermissions = ArrayOf<RolePermissionType>.Empty;
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Observer,
                        PermissionType.Browse, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Operator,
                        PermissionType.Browse, StatusCodes.Good).ConfigureAwait(false);

                    harness.Node.RolePermissions = default;
                    harness.Node.UserRolePermissions = default;
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Observer,
                        PermissionType.Browse, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Operator,
                        PermissionType.Browse, StatusCodes.Good).ConfigureAwait(false);
                    Assert.That(harness.Node.RolePermissions.IsNull, Is.True);
                    harness.Node.RolePermissions = ArrayOf<RolePermissionType>.Empty;
                    harness.Node.UserRolePermissions = ArrayOf<RolePermissionType>.Empty;

                    metadata.DefaultRolePermissions.Value = explicitRoles;
                    await metadata.ClearChangeMasksAsync(serverInternal.DefaultSystemContext, true)
                        .ConfigureAwait(false);
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Observer,
                        PermissionType.Browse, StatusCodes.Good).ConfigureAwait(false);
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Operator,
                        PermissionType.Browse, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
                    Assert.That(harness.Node.RolePermissions.IsEmpty, Is.True);

                    metadata.DefaultRolePermissions.Value = ArrayOf<RolePermissionType>.Empty;
                    await metadata.ClearChangeMasksAsync(serverInternal.DefaultSystemContext, true)
                        .ConfigureAwait(false);
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Observer,
                        PermissionType.Read, StatusCodes.Good).ConfigureAwait(false);
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_SecurityAdmin,
                        PermissionType.Read, StatusCodes.Good).ConfigureAwait(false);
                }
            }
            finally
            {
                metadata.DefaultRolePermissions.Value = originalRoles;
                metadata.DefaultUserRolePermissions.Value = originalUserRoles;
                await metadata.ClearChangeMasksAsync(serverInternal.DefaultSystemContext, true)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads explicit policies independently of failed defaults while authorization retains those failures.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExplicitPermissionAttributePreservesDefaultFailureIsolationAsync(bool asyncConfiguration)
        {
            IServerInternal server = m_server.CurrentInstance;
            NamespaceMetadataState? metadata = await server.ConfigurationNodeManager
                .GetNamespaceMetadataStateAsync(0).ConfigureAwait(false);
            Assert.That(metadata?.DefaultRolePermissions, Is.Not.Null);
            BaseVariableState property = metadata!.DefaultRolePermissions!;
            NodeValueSimpleEventHandler? original = property.OnSimpleReadValue;
            try
            {
                Harness harness = await CreateHarnessAsync(
                    new ServerConfigurationOptions(),
                    asyncConfiguration: asyncConfiguration,
                    beforeConfiguration: root =>
                    {
                        root.RolePermissions =
                        [
                            new RolePermissionType
                            {
                                RoleId = ObjectIds.WellKnownRole_Observer,
                                Permissions = (uint)PermissionType.Browse
                            }
                        ];
                    }).ConfigureAwait(false);
                using (harness.Manager)
                {
                    property.OnSimpleReadValue =
                        (ISystemContext _, NodeState _, ref Variant _) => StatusCodes.BadInternalError;
                    NamespaceDefaultPermissionReader.Invalidate();
                    SessionSystemContext context = CreateContext(
                        UserTokenType.UserName, MessageSecurityMode.SignAndEncrypt,
                        ObjectIds.WellKnownRole_Observer);
                    (ServiceResult result, DataValue value) =
                        await AsyncCustomNodeManager.ReadConfigurationPermissionAttributeAsync(
                            server, harness.Node, ToServerContext(server, context), Attributes.RolePermissions,
                            default, default, new DataValue(), CancellationToken.None).ConfigureAwait(false);
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(value.WrappedValue.TryGetStructure(
                        out ArrayOf<RolePermissionType> permissions), Is.True);
                    Assert.That(permissions.ToList().Any(permission =>
                        permission.RoleId == ObjectIds.WellKnownRole_Observer), Is.True);
                    object? handle = await harness.Manager.GetManagerHandleAsync(harness.Node.NodeId)
                        .ConfigureAwait(false);
                    Assert.That(handle, Is.Not.Null);
                    (bool handled, ServiceResult? authorization) =
                        await harness.Manager.TryValidatePermissionsAsync(
                            (OperationContext)context.OperationContext!, handle, PermissionType.Browse,
                            null, true, null, false).ConfigureAwait(false);
                    Assert.That(handled, Is.True);
                    Assert.That(authorization?.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
                    harness.Node.RolePermissions = ArrayOf<RolePermissionType>.Empty;
                    (result, _) = await AsyncCustomNodeManager.ReadConfigurationPermissionAttributeAsync(
                        server, harness.Node, ToServerContext(server, context), Attributes.RolePermissions,
                        default, default, new DataValue(), CancellationToken.None).ConfigureAwait(false);
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));

                    ServiceResultException? configurationError = await Assert.ThrowsAsync<ServiceResultException>(
                        async () =>
                    {
                        if (asyncConfiguration)
                        {
                            await harness.Manager.CreateServerConfigurationAsync(
                                server.DefaultSystemContext, m_fixture.Config).ConfigureAwait(false);
                        }
                        else
                        {
                            harness.Manager.CreateServerConfiguration(server.DefaultSystemContext, m_fixture.Config);
                        }
                    }).ConfigureAwait(false);
                    Assert.That(configurationError?.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
                }
            }
            finally
            {
                property.OnSimpleReadValue = original;
                NamespaceDefaultPermissionReader.Invalidate();
            }
        }

        /// <summary>
        /// Evaluates permission callbacks per caller and rejects returned errors and exceptions.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public async Task ConfigurationPermissionCallbacksRemainDynamicAsync(
            bool asyncConfiguration,
            bool namespaceCallback,
            bool userPermissions)
        {
            IServerInternal serverInternal = m_server.CurrentInstance;
            NamespaceMetadataState? metadata = await serverInternal.ConfigurationNodeManager
                .GetNamespaceMetadataStateAsync(0).ConfigureAwait(false);
            Assert.That(metadata?.DefaultRolePermissions, Is.Not.Null);
            Assert.That(metadata?.DefaultUserRolePermissions, Is.Not.Null);
            ArrayOf<RolePermissionType> originalRoles = metadata!.DefaultRolePermissions!.Value;
            ArrayOf<RolePermissionType> originalUserRoles = metadata.DefaultUserRolePermissions!.Value;
            BaseVariableState property = userPermissions
                ? metadata.DefaultUserRolePermissions! : metadata.DefaultRolePermissions!;
            NodeValueSimpleEventHandler? originalCallback = property.OnSimpleReadValue;
            NodeId allowedRole = ObjectIds.WellKnownRole_Observer;
            StatusCode callbackStatus = StatusCodes.Good;
            bool throwCallback = false;
            bool checkCaller = false;
            int callbackCount = 0;
            try
            {
                metadata.DefaultRolePermissions.Value = ArrayOf<RolePermissionType>.Empty;
                metadata.DefaultUserRolePermissions.Value = ArrayOf<RolePermissionType>.Empty;
                if (namespaceCallback)
                {
                    property.OnSimpleReadValue = (ISystemContext context, NodeState _, ref Variant value) =>
                    {
                        ServiceResult result = ReadPermissions(context, out ArrayOf<RolePermissionType> permissions);
                        value = Variant.FromStructure(permissions);
                        return result;
                    };
                }
                await metadata.ClearChangeMasksAsync(serverInternal.DefaultSystemContext, true)
                    .ConfigureAwait(false);
                Harness harness = await CreateHarnessAsync(
                    new ServerConfigurationOptions(),
                    asyncConfiguration: asyncConfiguration,
                    beforeConfiguration: root =>
                    {
                        root.RolePermissions = ArrayOf<RolePermissionType>.Empty;
                        root.UserRolePermissions = ArrayOf<RolePermissionType>.Empty;
                        if (!namespaceCallback)
                        {
                            NodeAttributeEventHandler<ArrayOf<RolePermissionType>> callback =
                                (ISystemContext context, NodeState _, ref ArrayOf<RolePermissionType> value) =>
                                    ReadPermissions(context, out value);
                            if (userPermissions)
                            {
                                root.OnReadUserRolePermissions = callback;
                            }
                            else
                            {
                                root.OnReadRolePermissions = callback;
                            }
                        }
                    }).ConfigureAwait(false);
                using (harness.Manager)
                {
                    checkCaller = true;
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Observer,
                        PermissionType.Browse, StatusCodes.Good).ConfigureAwait(false);
                    allowedRole = ObjectIds.WellKnownRole_Operator;
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Observer,
                        PermissionType.Browse, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Operator,
                        PermissionType.Browse, StatusCodes.Good).ConfigureAwait(false);
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_SecurityAdmin,
                        PermissionType.Browse | PermissionType.Call,
                        userPermissions ? StatusCodes.BadUserAccessDenied : StatusCodes.Good).ConfigureAwait(false);

                    callbackStatus = StatusCodes.BadUserAccessDenied;
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_SecurityAdmin,
                        PermissionType.Browse, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
                    callbackStatus = StatusCodes.BadAttributeIdInvalid;
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_SecurityAdmin,
                        PermissionType.Browse, StatusCodes.BadAttributeIdInvalid).ConfigureAwait(false);
                    throwCallback = true;
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_SecurityAdmin,
                        PermissionType.Browse, StatusCodes.BadUnexpectedError).ConfigureAwait(false);

                    throwCallback = false;
                    callbackStatus = StatusCodes.Good;
                    allowedRole = ObjectIds.WellKnownRole_Observer;
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Observer,
                        PermissionType.Browse, StatusCodes.Good).ConfigureAwait(false);
                    Assert.That(callbackCount, Is.GreaterThan(0));
                    Assert.That(harness.Node.RolePermissions.IsEmpty, Is.True);
                    Assert.That(harness.Node.UserRolePermissions.IsEmpty, Is.True);
                }
            }
            finally
            {
                property.OnSimpleReadValue = originalCallback;
                metadata.DefaultRolePermissions.Value = originalRoles;
                metadata.DefaultUserRolePermissions.Value = originalUserRoles;
                await metadata.ClearChangeMasksAsync(serverInternal.DefaultSystemContext, true)
                    .ConfigureAwait(false);
            }

            ServiceResult ReadPermissions(ISystemContext context, out ArrayOf<RolePermissionType> permissions)
            {
                callbackCount++;
                if (checkCaller)
                {
                    Assert.That(context, Is.InstanceOf<ServerSystemContext>());
                    Assert.That(((ServerSystemContext)context).OperationContext?.UserIdentity, Is.Not.Null);
                }
                if (throwCallback)
                {
                    throw new InvalidOperationException("Permission callback failed.");
                }
                permissions =
                [
                    new RolePermissionType
                    {
                        RoleId = allowedRole,
                        Permissions = (uint)PermissionType.Browse
                    }
                ];
                return callbackStatus;
            }
        }

        /// <summary>
        /// Retires advertised grants and cached authorization without replacing a newer binding's callback.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConfigurationPermissionGrantLifetimeMatchesBindingAsync(bool asyncConfiguration)
        {
            NodeAttributeEventHandler<ArrayOf<RolePermissionType>> original =
                static (ISystemContext _, NodeState _, ref ArrayOf<RolePermissionType> _) => ServiceResult.Good;
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions(),
                asyncConfiguration: asyncConfiguration,
                beforeConfiguration: root =>
                {
                    root.RolePermissions =
                    [
                        new RolePermissionType
                        {
                            RoleId = ObjectIds.WellKnownRole_Observer,
                            Permissions = (uint)PermissionType.Browse
                        }
                    ];
                    root.UserRolePermissions =
                    [
                        new RolePermissionType
                        {
                            RoleId = ObjectIds.WellKnownRole_Observer,
                            Permissions = (uint)PermissionType.Browse
                        },
                        new RolePermissionType
                        {
                            RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                            Permissions = (uint)PermissionType.Browse
                        }
                    ];
                    root.OnReadRolePermissions = original;
                }).ConfigureAwait(false);
            using (harness.Manager)
            {
                var cache = new Dictionary<NodeId, Variant[]>();
                await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_SecurityAdmin,
                    PermissionType.Browse, StatusCodes.Good, cache).ConfigureAwait(false);
                Assert.That(cache.ContainsKey(harness.Node.NodeId), Is.False);

                harness.Manager.RevokeSecurityAdminCallPermissions();
                Assert.That(harness.Node.OnReadRolePermissions, Is.SameAs(original));
                await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_SecurityAdmin,
                    PermissionType.Browse, StatusCodes.BadUserAccessDenied, cache).ConfigureAwait(false);
                await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Observer,
                    PermissionType.Browse, StatusCodes.Good, cache).ConfigureAwait(false);

                AsyncCustomNodeManager.RegisterSecurityAdminCallPermissions(harness.Manager, harness.Node);
                await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_SecurityAdmin,
                    PermissionType.Browse, StatusCodes.Good, cache).ConfigureAwait(false);

                Harness replacement = await CreateHarnessAsync(new ServerConfigurationOptions()).ConfigureAwait(false);
                using (replacement.Manager)
                {
                    AsyncCustomNodeManager.RegisterSecurityAdminCallPermissions(replacement.Manager, harness.Node);
                    Assert.That(replacement.Manager.ReplacePredefinedNode(harness.Node.NodeId, harness.Node), Is.True);
                    await AssertPermissionAsync(replacement, harness.Node, ObjectIds.WellKnownRole_SecurityAdmin,
                        PermissionType.Browse, StatusCodes.Good, cache).ConfigureAwait(false);
                    NodeAttributeEventHandler<ArrayOf<RolePermissionType>>? current =
                        harness.Node.OnReadRolePermissions;
                    harness.Manager.Dispose();
                    Assert.That(harness.Node.OnReadRolePermissions, Is.SameAs(current));
                    await AssertPermissionAsync(replacement, harness.Node, ObjectIds.WellKnownRole_SecurityAdmin,
                        PermissionType.Browse, StatusCodes.Good, cache).ConfigureAwait(false);
                    SessionSystemContext context = CreateContext(
                        UserTokenType.UserName, MessageSecurityMode.SignAndEncrypt,
                        ObjectIds.WellKnownRole_SecurityAdmin);
                    (ServiceResult result, DataValue value) =
                        await AsyncCustomNodeManager.ReadConfigurationPermissionAttributeAsync(
                            m_server.CurrentInstance, harness.Node, ToServerContext(m_server.CurrentInstance, context),
                            Attributes.RolePermissions,
                            default, default, new DataValue(), CancellationToken.None).ConfigureAwait(false);
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(value.WrappedValue.TryGetStructure(
                        out ArrayOf<RolePermissionType> permissions), Is.True);
                    Assert.That(permissions.ToList().Any(permission =>
                        permission.RoleId == ObjectIds.WellKnownRole_SecurityAdmin &&
                        (permission.Permissions & (uint)PermissionType.Browse) != 0), Is.True);

                    replacement.Manager.Dispose();
                    Assert.That(harness.Node.OnReadRolePermissions, Is.SameAs(original));
                    (result, value) = await AsyncCustomNodeManager.ReadConfigurationPermissionAttributeAsync(
                        m_server.CurrentInstance, harness.Node, ToServerContext(m_server.CurrentInstance, context),
                        Attributes.RolePermissions,
                        default, default, new DataValue(), CancellationToken.None).ConfigureAwait(false);
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(value.WrappedValue.TryGetStructure(out permissions), Is.True);
                    Assert.That(permissions.ToList().Any(permission =>
                        permission.RoleId == ObjectIds.WellKnownRole_SecurityAdmin), Is.False);
                }
            }
        }

        /// <summary>
        /// Does not apply a cached grant to a replacement node that reuses a configuration NodeId.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConfigurationPermissionCacheDoesNotGrantReplacementNodeAsync(bool asyncConfiguration)
        {
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions(),
                asyncConfiguration: asyncConfiguration,
                beforeConfiguration: root =>
                {
                    root.RolePermissions =
                    [
                        new RolePermissionType
                        {
                            RoleId = ObjectIds.WellKnownRole_Observer,
                            Permissions = (uint)PermissionType.Browse
                        }
                    ];
                    root.UserRolePermissions = ArrayOf<RolePermissionType>.Empty;
                }).ConfigureAwait(false);
            using (harness.Manager)
            {
                var cache = new Dictionary<NodeId, Variant[]>();
                await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_SecurityAdmin,
                    PermissionType.Browse, StatusCodes.Good, cache).ConfigureAwait(false);
                Assert.That(cache.ContainsKey(harness.Node.NodeId), Is.False);
                var replacement = new BaseObjectState(null)
                {
                    NodeId = harness.Node.NodeId,
                    BrowseName = harness.Node.BrowseName,
                    RolePermissions = harness.Node.RolePermissions,
                    UserRolePermissions = harness.Node.UserRolePermissions
                };
                Assert.That(harness.Manager.ReplacePredefinedNode(replacement.NodeId, replacement), Is.True);
                await AssertPermissionAsync(harness, replacement, ObjectIds.WellKnownRole_SecurityAdmin,
                    PermissionType.Browse, StatusCodes.BadUserAccessDenied, cache).ConfigureAwait(false);
                await AssertPermissionAsync(harness, replacement, ObjectIds.WellKnownRole_Observer,
                    PermissionType.Browse, StatusCodes.Good, cache).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Rejects a read whose callback retires its grant before the permission decision completes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReentrantGrantRetirementRejectsPermissionReadAsync(bool dispose)
        {
            ConfigurationNodeManager? owner = null;
            bool retireDuringRead = false;
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions(),
                beforeConfiguration: root =>
                {
                    root.RolePermissions =
                    [
                        new RolePermissionType
                        {
                            RoleId = ObjectIds.WellKnownRole_Observer,
                            Permissions = (uint)PermissionType.Browse
                        }
                    ];
                    root.OnReadRolePermissions =
                        (ISystemContext _, NodeState _, ref ArrayOf<RolePermissionType> _) =>
                        {
                            if (retireDuringRead)
                            {
                                retireDuringRead = false;
                                if (dispose)
                                {
                                    owner!.Dispose();
                                }
                                else
                                {
                                    owner!.RevokeSecurityAdminCallPermissions();
                                }
                            }
                            return ServiceResult.Good;
                        };
                }).ConfigureAwait(false);
            using (harness.Manager)
            {
                owner = harness.Manager;
                NodeAttributeEventHandler<ArrayOf<RolePermissionType>> retained =
                    harness.Node.OnReadRolePermissions!;
                retireDuringRead = true;
                SessionSystemContext context = CreateContext(
                    UserTokenType.UserName, MessageSecurityMode.SignAndEncrypt,
                    ObjectIds.WellKnownRole_SecurityAdmin);
                (ServiceResult result, DataValue value) =
                    await AsyncCustomNodeManager.ReadConfigurationPermissionAttributeAsync(
                        m_server.CurrentInstance, harness.Node, ToServerContext(m_server.CurrentInstance, context),
                        Attributes.RolePermissions,
                        default, default, new DataValue(), CancellationToken.None).ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(value.WrappedValue.IsNull, Is.True);
                ArrayOf<RolePermissionType> permissions = harness.Node.RolePermissions;
                result = retained(context, harness.Node, ref permissions);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(permissions.ToList().Any(permission =>
                    permission.RoleId == ObjectIds.WellKnownRole_SecurityAdmin), Is.False);
            }
        }

        /// <summary>
        /// Rejects changed relevant defaults but accepts invalidation caused by another namespace.
        /// </summary>
        [TestCase(true, false, false)]
        [TestCase(false, false, false)]
        [TestCase(true, true, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, true)]
        public async Task NamespacePolicyInvalidationDuringPermissionReadIsScopedAsync(
            bool relevantPolicy,
            bool attributeRead,
            bool inPlace)
        {
            IServerInternal server = m_server.CurrentInstance;
            NamespaceMetadataState? metadata = await server.ConfigurationNodeManager
                .GetNamespaceMetadataStateAsync(0).ConfigureAwait(false);
            Assert.That(metadata?.DefaultRolePermissions, Is.Not.Null);
            ArrayOf<RolePermissionType> original = metadata!.DefaultRolePermissions!.Value;
            const string unrelatedUri = "urn:permission-invalidation-unrelated";
            server.NamespaceUris.GetIndexOrAppend(unrelatedUri);
            NamespaceMetadataState unrelated = await server.ConfigurationNodeManager
                .CreateNamespaceMetadataStateAsync(unrelatedUri).ConfigureAwait(false)
                ?? throw new AssertionException("The unrelated namespace metadata state was not created.");
            unrelated.AddDefaultRolePermissions(server.DefaultSystemContext);
            ArrayOf<RolePermissionType> originalUnrelated = unrelated.DefaultRolePermissions!.Value;
            bool changePolicy = false;
            try
            {
                metadata.DefaultRolePermissions.Value =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_Observer,
                        Permissions = (uint)PermissionType.Browse
                    }
                ];
                NamespaceDefaultPermissionReader.Invalidate();
                Harness harness = await CreateHarnessAsync(
                    new ServerConfigurationOptions(),
                    beforeConfiguration: root =>
                    {
                        root.RolePermissions = ArrayOf<RolePermissionType>.Empty;
                        root.UserRolePermissions = ArrayOf<RolePermissionType>.Empty;
                        root.OnReadRolePermissions =
                            (ISystemContext _, NodeState _, ref ArrayOf<RolePermissionType> _) =>
                            {
                                if (changePolicy)
                                {
                                    changePolicy = false;
                                    NamespaceMetadataState changed = relevantPolicy ? metadata : unrelated;
                                    if (inPlace)
                                    {
                                        changed.DefaultRolePermissions!.Value[0].RoleId =
                                            ObjectIds.WellKnownRole_Operator;
                                    }
                                    else
                                    {
                                        changed.DefaultRolePermissions!.Value =
                                        [
                                            new RolePermissionType
                                            {
                                                RoleId = ObjectIds.WellKnownRole_Operator,
                                                Permissions = (uint)PermissionType.Browse
                                            }
                                        ];
                                    }
                                    NamespaceDefaultPermissionReader.Invalidate();
                                }
                                return ServiceResult.Good;
                            };
                    }).ConfigureAwait(false);
                using (harness.Manager)
                {
                    changePolicy = true;
                    SessionSystemContext context = CreateContext(
                        UserTokenType.UserName, MessageSecurityMode.SignAndEncrypt,
                        ObjectIds.WellKnownRole_Observer);
                    object? handle = await harness.Manager.GetManagerHandleAsync(harness.Node.NodeId)
                        .ConfigureAwait(false);
                    Assert.That(handle, Is.Not.Null);
                    if (attributeRead)
                    {
                        (ServiceResult result, DataValue value) =
                            await AsyncCustomNodeManager.ReadConfigurationPermissionAttributeAsync(
                                server, harness.Node, ToServerContext(server, context), Attributes.RolePermissions,
                                default, default, new DataValue(), CancellationToken.None).ConfigureAwait(false);
                        Assert.That(result.StatusCode,
                            Is.EqualTo(relevantPolicy ? StatusCodes.BadInvalidState : StatusCodes.Good));
                        if (relevantPolicy)
                        {
                            Assert.That(value.WrappedValue.IsNull, Is.True);
                        }
                        else
                        {
                            Assert.That(value.WrappedValue.TryGetStructure(
                                out ArrayOf<RolePermissionType> permissions), Is.True);
                            Assert.That(permissions.ToList().Any(permission =>
                                permission.RoleId == ObjectIds.WellKnownRole_Observer), Is.True);
                        }
                    }
                    else if (relevantPolicy)
                    {
                        ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                        {
                            await harness.Manager.TryValidatePermissionsAsync(
                                (OperationContext)context.OperationContext!, handle, PermissionType.Browse,
                                null, true, null, false).ConfigureAwait(false);
                        }).ConfigureAwait(false);
                        Assert.That(error?.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                    }
                    else
                    {
                        (bool handled, ServiceResult? result) = await harness.Manager.TryValidatePermissionsAsync(
                            (OperationContext)context.OperationContext!, handle, PermissionType.Browse,
                            null, true, null, false).ConfigureAwait(false);
                        Assert.That(handled, Is.True);
                        Assert.That(result?.StatusCode, Is.EqualTo(StatusCodes.Good));
                    }
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Observer,
                        PermissionType.Browse,
                        relevantPolicy ? StatusCodes.BadUserAccessDenied : StatusCodes.Good).ConfigureAwait(false);
                    await AssertPermissionAsync(harness, harness.Node, ObjectIds.WellKnownRole_Operator,
                        PermissionType.Browse,
                        relevantPolicy ? StatusCodes.Good : StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
                }
            }
            finally
            {
                metadata.DefaultRolePermissions.Value = original;
                unrelated.DefaultRolePermissions!.Value = originalUnrelated;
                NamespaceDefaultPermissionReader.Invalidate();
            }
        }

        /// <summary>
        /// Resolves async-only metadata before a grant callback rather than blocking or granting unrestricted access.
        /// </summary>
        [Test]
        public async Task ConfigurationGrantAwaitsAsyncOnlyNamespaceMetadataAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                server.SetupGet(value => value.Telemetry).Returns(s_telemetry);
                var configuration = new Mock<IConfigurationNodeManager>();
                var metadataReady = new TaskCompletionSource<NamespaceMetadataState?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                configuration.Setup(value => value.GetNamespaceMetadataStateAsync(
                    It.IsAny<ushort>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<NamespaceMetadataState?>(metadataReady.Task));
                Mock.Get(server.Object.NodeManager).SetupGet(value => value.ConfigurationNodeManager)
                    .Returns(configuration.Object);
                await using var owner = new PermissionTestNodeManager(server.Object);
                var node = new BaseObjectState(null)
                {
                    NodeId = new NodeId(900, owner.NamespaceIndex),
                    BrowseName = new QualifiedName("PermissionNode", owner.NamespaceIndex),
                    RolePermissions = default
                };
                AsyncCustomNodeManager.RegisterSecurityAdminCallPermissions(owner, node);
                var metadata = new NamespaceMetadataState(null);
                metadata.AddDefaultRolePermissions(server.Object.DefaultSystemContext);
                metadata.DefaultRolePermissions!.Value =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_Observer,
                        Permissions = (uint)PermissionType.Browse
                    }
                ];
                SessionSystemContext context = CreateContext(
                    UserTokenType.UserName, MessageSecurityMode.SignAndEncrypt,
                    ObjectIds.WellKnownRole_SecurityAdmin);
                Variant syncValue = default;
                DateTimeUtc timestamp = default;
                ServiceResult syncResult = node.ReadAttribute(
                    ToServerContext(server.Object, context), Attributes.RolePermissions, ref syncValue, ref timestamp);
                Assert.That(syncResult.StatusCode, Is.EqualTo(StatusCodes.BadWouldBlock));
                Assert.That(syncValue.IsNull, Is.True);

                Task<(ServiceResult Result, DataValue Value)> pending =
                    AsyncCustomNodeManager.ReadConfigurationPermissionAttributeAsync(
                        server.Object, node, ToServerContext(server.Object, context), Attributes.RolePermissions,
                        default, default,
                        new DataValue(), CancellationToken.None).AsTask();
                Assert.That(pending.IsCompleted, Is.False);
                metadataReady.SetResult(metadata);
                (ServiceResult result, DataValue value) = await pending.ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(value.WrappedValue.TryGetStructure(out ArrayOf<RolePermissionType> permissions), Is.True);
                Assert.That(permissions.Count, Is.EqualTo(2));
                Assert.That(permissions[0].RoleId, Is.EqualTo(ObjectIds.WellKnownRole_Observer));
                Assert.That(permissions[1].RoleId, Is.EqualTo(ObjectIds.WellKnownRole_SecurityAdmin));
                Assert.That(node.RolePermissions.IsNull, Is.True);
            }
        }

        /// <summary>
        /// Owns only the permission grant under test, without starting another server.
        /// </summary>
        private sealed class PermissionTestNodeManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Initializes the isolated manager against deterministic server services.
            /// </summary>
            public PermissionTestNodeManager(IServerInternal server)
                : base(server, "urn:opcfoundation:tests:configuration-permissions")
            {
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
                byte[] expected = s_initialConfig.ToArray();
                updated.Span.CopyTo(expected);

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
                    Assert.That(provider.LastApplied.ToArray(), Is.EqualTo(expected),
                        "a positional write changes the prefix without truncating the existing file");
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

                // A scheduled update retains writer exclusion until the apply finishes.
                OpenMethodStateResult reopen = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                Assert.That(reopen.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
                Assert.That(reopen.FileHandle, Is.Zero);

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

        /// <summary>
        /// Read and write use the smaller file or server ByteString limit on a seeded read-write handle.
        /// </summary>
        [TestCase(2u, 3)]
        [TestCase(3u, 2)]
        public async Task ConfigurationFileReadWriteSeedsContentAndHonorsEffectiveChunkLimitAsync(
            uint fileLimit, int serverLimit)
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                file.MaxByteStringLength ??=
                    new PropertyState<uint>.Implementation<VariantBuilder>(file)
                    {
                        BrowseName = new QualifiedName(Opc.Ua.BrowseNames.MaxByteStringLength)
                    };
                file.MaxByteStringLength.Value = fileLimit;
                SessionSystemContext sessionContext = CreateAdminContextForSession(new NodeId(8101, 1));
                ServerSystemContext ctx = harness.Manager.SystemContext.Copy(
                    (OperationContext)sessionContext.OperationContext!);
                ServiceMessageContext messageContext = (ServiceMessageContext)ctx.Server.MessageContext;
                int previousLimit = messageContext.MaxByteStringLength;
                try
                {
                    messageContext.MaxByteStringLength = serverLimit;
                    OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                        .ConfigureAwait(false);
                    Assert.That(open.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                    var existing = new List<byte>();
                    while (true)
                    {
                        ReadMethodStateResult read = await file.Read!.OnCallAsync!(
                            ctx, file.Read, file.NodeId, open.FileHandle, int.MaxValue, CancellationToken.None)
                            .ConfigureAwait(false);
                        Assert.That(read.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(read.Data.Length, Is.LessThanOrEqualTo(2));
                        if (read.Data.IsEmpty)
                        {
                            break;
                        }
                        existing.AddRange(read.Data.ToArray());
                        Assert.That(existing, Has.Count.LessThanOrEqualTo(s_initialConfig.Length));
                    }
                    Assert.That(existing.ToArray(), Is.EqualTo(s_initialConfig.ToArray()));
                    GetPositionMethodStateResult position = await file.GetPosition!.OnCallAsync!(
                        ctx, file.GetPosition, file.NodeId, open.FileHandle, CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(position.Position, Is.EqualTo((ulong)s_initialConfig.Length));
                    SetPositionMethodStateResult rewind = await file.SetPosition!.OnCallAsync!(
                        ctx, file.SetPosition, file.NodeId, open.FileHandle, 0, CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(rewind.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                    WriteMethodStateResult tooLarge = await file.Write!.OnCallAsync!(
                        ctx, file.Write, file.NodeId, open.FileHandle, ByteString.From(new byte[] { 1, 2, 3 }),
                        CancellationToken.None).ConfigureAwait(false);
                    Assert.That(tooLarge.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                    position = await file.GetPosition.OnCallAsync!(
                        ctx, file.GetPosition, file.NodeId, open.FileHandle, CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(position.Position, Is.Zero);
                    byte[] replacement = s_initialConfig.Span.ToArray();
                    for (int offset = 0; offset < replacement.Length; offset += 2)
                    {
                        replacement[offset] = 0x55;
                        WriteMethodStateResult write = await file.Write.OnCallAsync!(
                            ctx, file.Write, file.NodeId, open.FileHandle,
                            ByteString.From(replacement.AsSpan(offset, Math.Min(2, replacement.Length - offset))),
                            CancellationToken.None).ConfigureAwait(false);
                        Assert.That(write.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                    }
                    ConfigurationFileCloseAndUpdateMethodStateResult update = await CloseAndUpdateAsync(
                        file, ctx, open.FileHandle, 1, 0, 0).ConfigureAwait(false);
                    Assert.That(update.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(provider.CurrentConfiguration, Is.EqualTo(ByteString.From(replacement.AsSpan())));
                }
                finally
                {
                    messageContext.MaxByteStringLength = previousLimit;
                }
            }
        }

        /// <summary>
        /// Reading the entire configuration does not reduce write capacity, but extension beyond the file cap fails.
        /// </summary>
        [Test]
        public async Task ConfigurationFileFullReadThenFullReplacementPreservesSizeLimitAsync()
        {
            var provider = new FakeConfigurationFileProvider(ByteString.From(new byte[s_maxConfigurationFileSize]));
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(8102, 1));
                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                Assert.That(open.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                long total = 0;
                while (true)
                {
                    ReadMethodStateResult read = await file.Read!.OnCallAsync!(
                        ctx, file.Read, file.NodeId, open.FileHandle, 4096, CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(read.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                    if (read.Data.IsEmpty)
                    {
                        break;
                    }
                    total += read.Data.Length;
                    Assert.That(total, Is.LessThanOrEqualTo(s_maxConfigurationFileSize));
                }
                Assert.That(total, Is.EqualTo(s_maxConfigurationFileSize));
                SetPositionMethodStateResult rewind = await file.SetPosition!.OnCallAsync!(
                    ctx, file.SetPosition, file.NodeId, open.FileHandle, 0, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(rewind.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                byte[] buffer = new byte[4096];
                buffer.AsSpan().Fill(0x55);
                ByteString chunk = ByteString.From(buffer.AsSpan());
                for (long written = 0; written < s_maxConfigurationFileSize; written += chunk.Length)
                {
                    WriteMethodStateResult write = await file.Write!.OnCallAsync!(
                        ctx, file.Write, file.NodeId, open.FileHandle, chunk, CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(write.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                }
                WriteMethodStateResult overflow = await file.Write!.OnCallAsync!(
                    ctx, file.Write, file.NodeId, open.FileHandle, ByteString.From(new byte[] { 0x66 }),
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(overflow.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                ConfigurationFileCloseAndUpdateMethodStateResult update = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, 1, 0, 0).ConfigureAwait(false);
                Assert.That(update.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(provider.CurrentConfiguration.Length, Is.EqualTo(s_maxConfigurationFileSize));
                Assert.That(provider.CurrentConfiguration.Span.ToArray(), Is.All.EqualTo(0x55));
            }
        }

        /// <summary>
        /// A provider configuration exceeding the configured file cap is rejected in either supported mode.
        /// </summary>
        [TestCase(OpenFileMode.Read)]
        [TestCase(OpenFileMode.Read | OpenFileMode.Write)]
        public async Task ConfigurationFileOversizedProviderContentIsRejectedAsync(OpenFileMode mode)
        {
            var provider = new FakeConfigurationFileProvider(
                ByteString.From(new byte[s_maxConfigurationFileSize + 1]));
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(8103, 1));
                OpenMethodStateResult open = await OpenAsync(harness.Node.ConfigurationFile!, ctx, mode)
                    .ConfigureAwait(false);
                Assert.That(open.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(open.FileHandle, Is.Zero);
                Assert.That(harness.Node.ConfigurationFile!.OpenCount!.Value, Is.Zero);
                Assert.That(provider.ApplyCount, Is.Zero);
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
        public async Task ConfigurationFileReadOneByteOverMaxRequestReturnsBoundedContentAsync()
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

                ReadMethodStateResult read = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, (int)s_maxConfigurationFileSize + 1,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(read.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.Data, Is.EqualTo(s_initialConfig));
            }
        }

        /// <summary>
        /// Nonpositive read requests do not advance the open file's cursor.
        /// </summary>
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public async Task ConfigurationFileReadRejectsNonpositiveLengthAsync(int length)
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(8103, 1));
                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read).ConfigureAwait(false);
                Assert.That(open.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                ReadMethodStateResult rejected = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, length, CancellationToken.None).ConfigureAwait(false);
                Assert.That(rejected.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(rejected.Data.IsNull, Is.True);
                GetPositionMethodStateResult position = await file.GetPosition!.OnCallAsync!(
                    ctx, file.GetPosition, file.NodeId, open.FileHandle, CancellationToken.None).ConfigureAwait(false);
                Assert.That(position.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(position.Position, Is.Zero);
                ReadMethodStateResult valid = await file.Read.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, 1, CancellationToken.None).ConfigureAwait(false);
                Assert.That(valid.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(valid.Data, Is.EqualTo(ByteString.From(s_initialConfig.Span[..1])));
                CloseMethodStateResult close = await file.Close!.OnCallAsync!(
                    ctx, file.Close, file.NodeId, open.FileHandle, CancellationToken.None).ConfigureAwait(false);
                Assert.That(close.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            }
        }

        [Test]
        public async Task ConfigurationFileReadWithIntMaxValueLengthReturnsBoundedContentAsync()
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

                ReadMethodStateResult read = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, int.MaxValue,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(read.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.Data, Is.EqualTo(s_initialConfig));
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
        public async Task ConfigurationFileReadAtSizeLimitReturnsEmptyEofAndCanRewindAsync()
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

                file.MaxByteStringLength ??=
                    new PropertyState<uint>.Implementation<VariantBuilder>(file)
                    {
                        BrowseName = new QualifiedName(Opc.Ua.BrowseNames.MaxByteStringLength)
                    };
                file.MaxByteStringLength.Value = (uint)s_maxConfigurationFileSize;
                ReadMethodStateResult first = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, (int)s_maxConfigurationFileSize - 5,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(first.ServiceResult), Is.True);
                long total = first.Data.Length;
                while (total < s_maxConfigurationFileSize - 5)
                {
                    ReadMethodStateResult chunk = await file.Read.OnCallAsync!(
                        ctx, file.Read, file.NodeId, open.FileHandle,
                        (int)(s_maxConfigurationFileSize - 5 - total), CancellationToken.None).ConfigureAwait(false);
                    Assert.That(chunk.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(chunk.Data.Length, Is.Positive);
                    total += chunk.Data.Length;
                }

                ReadMethodStateResult second = await file.Read.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, 6, CancellationToken.None).ConfigureAwait(false);
                Assert.That(second.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(second.Data.Length, Is.EqualTo(5));

                ReadMethodStateResult third = await file.Read.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, 5, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(third.ServiceResult), Is.True);
                Assert.That(third.Data.IsEmpty, Is.True);
                SetPositionMethodStateResult rewind = await file.SetPosition!.OnCallAsync!(
                    ctx, file.SetPosition, file.NodeId, open.FileHandle, 0, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(rewind.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                ReadMethodStateResult reread = await file.Read.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, 5, CancellationToken.None).ConfigureAwait(false);
                Assert.That(reread.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(reread.Data.Length, Is.EqualTo(5));
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

                // Opening another reader rearms the timer but retains both handles.
                OpenMethodStateResult openB = await OpenAsync(file, sessionB, OpenFileMode.Read)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(openB.ServiceResult), Is.True);
                Assert.That(t1.IsDisposed, Is.True);

                // A superseded timer callback must not close either reader.
                t1.Fire();

                ReadMethodStateResult readA = await file.Read!.OnCallAsync!(
                    sessionA, file.Read, file.NodeId, openA.FileHandle, 4, CancellationToken.None)
                    .ConfigureAwait(false);
                ReadMethodStateResult readB = await file.Read!.OnCallAsync!(
                    sessionB, file.Read, file.NodeId, openB.FileHandle, 4, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(file.OpenCount!.Value, Is.EqualTo((ushort)2));
                    Assert.That(ServiceResult.IsGood(readA.ServiceResult), Is.True);
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
                Assert.That(read.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
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

        [Test]
        public async Task ConfigurationFileSupportsMultipleReadersAndExclusiveWriterAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext first = CreateAdminContextForSession(new NodeId(8101, 1));
                SessionSystemContext second = CreateAdminContextForSession(new NodeId(8102, 1));
                SessionSystemContext third = CreateAdminContextForSession(new NodeId(8103, 1));

                OpenMethodStateResult firstRead = await OpenAsync(file, first, OpenFileMode.Read).ConfigureAwait(false);
                OpenMethodStateResult secondRead = await OpenAsync(file, second, OpenFileMode.Read).ConfigureAwait(false);
                Assert.That(file.OpenCount!.Value, Is.EqualTo((ushort)2));

                OpenMethodStateResult rejectedWriter = await OpenAsync(
                    file, third, OpenFileMode.Read | OpenFileMode.Write).ConfigureAwait(false);
                Assert.That(rejectedWriter.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));

                CloseMethodStateResult firstClose = await file.Close!.OnCallAsync!(
                    first, file.Close, file.NodeId, firstRead.FileHandle, CancellationToken.None).ConfigureAwait(false);
                Assert.That(firstClose.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                rejectedWriter = await OpenAsync(file, third, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                Assert.That(rejectedWriter.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));

                CloseMethodStateResult secondClose = await file.Close.OnCallAsync!(
                    second, file.Close, file.NodeId, secondRead.FileHandle, CancellationToken.None).ConfigureAwait(false);
                Assert.That(secondClose.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                OpenMethodStateResult writer = await OpenAsync(
                    file, third, OpenFileMode.Read | OpenFileMode.Write).ConfigureAwait(false);
                Assert.That(writer.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                OpenMethodStateResult rejectedReader = await OpenAsync(file, first, OpenFileMode.Read)
                    .ConfigureAwait(false);
                Assert.That(rejectedReader.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotReadable));

                CloseMethodStateResult writerClose = await file.Close.OnCallAsync!(
                    third, file.Close, file.NodeId, writer.FileHandle, CancellationToken.None).ConfigureAwait(false);
                Assert.That(writerClose.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(file.OpenCount!.Value, Is.Zero);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False);
            }
        }

        [Test]
        public async Task ConfigurationFilePendingProviderReadKeepsWriterAdmissionAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig)
            {
                ReadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
                ReadRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext context = CreateAdminContextForSession(new NodeId(8110, 1));
                Task<OpenMethodStateResult> pending = OpenAsync(
                    file, context, OpenFileMode.Read | OpenFileMode.Write).AsTask();
                OpenMethodStateResult opened;
                try
                {
                    await provider.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        OpenMethodStateResult rejected = await OpenAsync(
                            file, context, OpenFileMode.Read | OpenFileMode.Write).ConfigureAwait(false);
                        Assert.That(rejected.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
                        Assert.That(rejected.FileHandle, Is.Zero);
                    }
                    OpenMethodStateResult rejectedReader = await OpenAsync(
                        file, context, OpenFileMode.Read).ConfigureAwait(false);
                    Assert.That(rejectedReader.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotReadable));
                    Assert.That(rejectedReader.FileHandle, Is.Zero);
                    Assert.That(provider.ReadCount, Is.EqualTo(1));
                    Assert.That(file.OpenCount!.Value, Is.Zero);
                    Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.True);
                }
                finally
                {
                    provider.ReadRelease.TrySetResult(true);
                    opened = await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }

                Assert.That(opened.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                CloseMethodStateResult close = await file.Close!.OnCallAsync!(
                    context, file.Close, file.NodeId, opened.FileHandle, CancellationToken.None).ConfigureAwait(false);
                Assert.That(close.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False);
            }
        }

        /// <summary>
        /// A pending reader rejects a writer before another provider read starts.
        /// </summary>
        [Test]
        public async Task ConfigurationFilePendingReaderRejectsWriterBeforeProviderReadAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig)
            {
                ReadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
                ReadRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext context = CreateAdminContextForSession(new NodeId(8117, 1));
                Task<OpenMethodStateResult> pending = OpenAsync(file, context, OpenFileMode.Read).AsTask();
                OpenMethodStateResult opened;
                try
                {
                    await provider.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    OpenMethodStateResult rejectedWriter = await OpenAsync(
                        file, context, OpenFileMode.Read | OpenFileMode.Write).ConfigureAwait(false);
                    Assert.That(rejectedWriter.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
                    Assert.That(rejectedWriter.FileHandle, Is.Zero);
                    Assert.That(provider.ReadCount, Is.EqualTo(1));
                    Assert.That(file.OpenCount!.Value, Is.Zero);
                    Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False);
                }
                finally
                {
                    provider.ReadRelease.TrySetResult(true);
                    opened = await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }

                Assert.That(opened.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                CloseMethodStateResult close = await file.Close!.OnCallAsync!(
                    context, file.Close, file.NodeId, opened.FileHandle, CancellationToken.None).ConfigureAwait(false);
                Assert.That(close.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(file.OpenCount!.Value, Is.Zero);
            }
        }

        /// <summary>
        /// Cancellation prevents publication even when the provider returns normally, releasing admission.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConfigurationFileCancelledProviderReadReleasesAdmissionAsync(bool writer)
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            using (var cancellation = new CancellationTokenSource())
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext context = CreateAdminContextForSession(new NodeId(8116, 1));
                provider.ReadStarted = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                provider.ReadRelease = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                provider.IgnoreReadCancellation = true;
                Task<OpenMethodStateResult> pending = file.Open!.OnCallAsync!(
                    context, file.Open, file.NodeId,
                    (byte)(writer ? OpenFileMode.Read | OpenFileMode.Write : OpenFileMode.Read),
                    cancellation.Token).AsTask();
                try
                {
                    await provider.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    cancellation.Cancel();
                }
                finally
                {
                    provider.ReadRelease.TrySetResult(true);
                }
                OperationCanceledException? cancelled = await Assert.ThrowsAsync<OperationCanceledException>(
                    async () => await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
                Assert.That(cancelled!.CancellationToken, Is.EqualTo(cancellation.Token));
                Assert.That(file.OpenCount!.Value, Is.Zero);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False);
                OpenMethodStateResult next = await OpenAsync(
                    file, context, OpenFileMode.Read | OpenFileMode.Write).ConfigureAwait(false);
                Assert.That(next.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                CloseMethodStateResult closed = await file.Close!.OnCallAsync!(
                    context, file.Close, file.NodeId, next.FileHandle, CancellationToken.None).ConfigureAwait(false);
                Assert.That(closed.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(file.OpenCount!.Value, Is.Zero);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False);
            }
        }

        [Test]
        public async Task ConfigurationFileWriteReservationBlocksTransactionsThroughApplyAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig)
            {
                ApplyStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
                ApplyRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext owner = CreateAdminContextForSession(new NodeId(8111, 1));
                NodeId otherSessionId = new(8112, 1);
                OpenMethodStateResult open = await OpenAsync(
                    file, owner, OpenFileMode.Read | OpenFileMode.Write).ConfigureAwait(false);
                await file.Write!.OnCallAsync!(
                    owner, file.Write, file.NodeId, open.FileHandle, ByteString.From([1, 2, 3, 4]),
                    CancellationToken.None).ConfigureAwait(false);

                Task<ConfigurationFileCloseAndUpdateMethodStateResult> update = CloseAndUpdateAsync(
                    file, owner, open.FileHandle, versionToUpdate: 1).AsTask();
                try
                {
                    await provider.ApplyStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    ServiceResultException? rejected = Assert.Throws<ServiceResultException>(() =>
                        harness.Coordinator.Stage(otherSessionId, new PushConfigurationOperation
                        {
                            CommitAsync = _ => Task.CompletedTask,
                            RollbackAsync = _ => Task.CompletedTask
                        }));
                    Assert.That(rejected!.StatusCode, Is.EqualTo(StatusCodes.BadTransactionPending));
                    Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.True);
                }
                finally
                {
                    provider.ApplyRelease.TrySetResult(true);
                }

                ConfigurationFileCloseAndUpdateMethodStateResult result = await update.ConfigureAwait(false);
                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False);
            }
        }

        [Test]
        public async Task ConfigurationFileSessionCloseRejectsPendingHandlePublicationAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig)
            {
                ReadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
                ReadRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                using var handler = new ApplicationConfigurationFile(
                    file, provider, _ => { }, _ => { }, s_telemetry,
                    harness.Coordinator, TimeProvider.System, 0);
                NodeId sessionId = new(8112, 1);
                SessionSystemContext context = CreateAdminContextForSession(sessionId);
                Task<OpenMethodStateResult> pending = OpenAsync(
                    file, context, OpenFileMode.Read | OpenFileMode.Write).AsTask();
                OpenMethodStateResult opened;
                try
                {
                    await provider.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    handler.NotifySessionClosing(sessionId);
                }
                finally
                {
                    provider.ReadRelease.TrySetResult(true);
                    opened = await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }

                Assert.That(opened.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
                Assert.That(opened.FileHandle, Is.Zero);
                Assert.That(file.OpenCount!.Value, Is.Zero);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False);

                SessionSystemContext next = CreateAdminContextForSession(new NodeId(8114, 1));
                OpenMethodStateResult nextWriter = await OpenAsync(
                    file, next, OpenFileMode.Read | OpenFileMode.Write).ConfigureAwait(false);
                Assert.That(nextWriter.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                CloseMethodStateResult close = await file.Close!.OnCallAsync!(
                    next, file.Close, file.NodeId, nextWriter.FileHandle, CancellationToken.None).ConfigureAwait(false);
                Assert.That(close.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            }
        }

        [Test]
        public async Task ConfigurationFileShutdownDrainsPendingOpenAndRejectsPublicationAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig)
            {
                ReadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
                ReadRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                using var handler = new ApplicationConfigurationFile(
                    file, provider, _ => { }, _ => { }, s_telemetry,
                    harness.Coordinator, TimeProvider.System, 0);
                SessionSystemContext context = CreateAdminContextForSession(new NodeId(8111, 1));
                Task<OpenMethodStateResult> pending = OpenAsync(
                    file, context, OpenFileMode.Read | OpenFileMode.Write).AsTask();
                Task? stop = null;
                OpenMethodStateResult opened;
                try
                {
                    await provider.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    stop = handler.StopAsync().AsTask();
                    Assert.That(stop.IsCompleted, Is.False);
                    int readsBefore = provider.ReadCount;
                    OpenMethodStateResult rejected = await OpenAsync(file, context, OpenFileMode.Read)
                        .ConfigureAwait(false);
                    Assert.That(rejected.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
                    Assert.That(rejected.FileHandle, Is.Zero);
                    Assert.That(provider.ReadCount, Is.EqualTo(readsBefore));
                }
                finally
                {
                    provider.ReadRelease.TrySetResult(true);
                    opened = await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    if (stop != null)
                    {
                        await stop.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    }
                }

                Assert.That(opened.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
                Assert.That(opened.FileHandle, Is.Zero);
                Assert.That(file.OpenCount!.Value, Is.Zero);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False);
            }
        }

        [Test]
        public async Task ConfigurationFileShutdownWaitsForActiveUpdateAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig)
            {
                ApplyStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
                ApplyRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                using var handler = new ApplicationConfigurationFile(
                    file, provider, _ => { }, _ => { }, s_telemetry,
                    harness.Coordinator, TimeProvider.System, 0);
                SessionSystemContext context = CreateAdminContextForSession(new NodeId(8115, 1));
                OpenMethodStateResult open = await OpenAsync(
                    file, context, OpenFileMode.Read | OpenFileMode.Write).ConfigureAwait(false);
                await file.Write!.OnCallAsync!(
                    context, file.Write, file.NodeId, open.FileHandle, ByteString.From([1, 2, 3, 4]),
                    CancellationToken.None).ConfigureAwait(false);

                Task<ConfigurationFileCloseAndUpdateMethodStateResult> update = CloseAndUpdateAsync(
                    file, context, open.FileHandle, versionToUpdate: 1).AsTask();
                Task? stop = null;
                ConfigurationFileCloseAndUpdateMethodStateResult result;
                try
                {
                    await provider.ApplyStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    stop = handler.StopAsync().AsTask();
                    Assert.That(stop.IsCompleted, Is.False);
                }
                finally
                {
                    provider.ApplyRelease.TrySetResult(true);
                    result = await update.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    if (stop != null)
                    {
                        await stop.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    }
                }

                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False);
            }
        }

        [Test]
        public async Task ConfigurationFileShutdownRejectsNewUpdateAndConfirmationAsync()
        {
            bool authorized = true;
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                using var handler = new ApplicationConfigurationFile(
                    file, provider, _ => { }, _ =>
                    {
                        if (!authorized)
                        {
                            throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                        }
                    }, s_telemetry,
                    harness.Coordinator, TimeProvider.System, 0);
                SessionSystemContext context = CreateAdminContextForSession(new NodeId(8116, 1));
                OpenMethodStateResult open = await OpenAsync(
                    file, context, OpenFileMode.Read | OpenFileMode.Write).ConfigureAwait(false);
                Assert.That(open.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                await handler.StopAsync().ConfigureAwait(false);

                ConfigurationFileCloseAndUpdateMethodStateResult update = await CloseAndUpdateAsync(
                    file, context, open.FileHandle, 1).ConfigureAwait(false);
                Assert.That(update.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
                ConfigurationFileConfirmUpdateMethodStateResult confirm = await file.ConfirmUpdate!.OnCallAsync!(
                    context, file.ConfirmUpdate, file.NodeId, Uuid.NewUuid(), CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(confirm.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));

                authorized = false;
                ServiceResultException? deniedUpdate = await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await CloseAndUpdateAsync(file, context, open.FileHandle, 1).ConfigureAwait(false));
                Assert.That(deniedUpdate!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                ServiceResultException? deniedConfirm = await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await file.ConfirmUpdate!.OnCallAsync!(
                        context, file.ConfirmUpdate, file.NodeId, Uuid.NewUuid(), CancellationToken.None)
                        .ConfigureAwait(false));
                Assert.That(deniedConfirm!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(provider.ValidateCount, Is.Zero);
                Assert.That(provider.ApplyCount, Is.Zero);
                Assert.That(provider.ConfirmCount, Is.Zero);
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

        /// <summary>
        /// Checks optimized, cached and metadata permission paths with a fresh caller context.
        /// </summary>
        private async Task AssertPermissionAsync(
            Harness harness,
            NodeState node,
            NodeId role,
            PermissionType permission,
            StatusCode expectedStatus,
            Dictionary<NodeId, Variant[]>? sharedCache = null)
        {
            object? handle = await harness.Manager.GetManagerHandleAsync(node.NodeId).ConfigureAwait(false);
            Assert.That(handle, Is.Not.Null);
            for (int path = 0; path < 5; path++)
            {
                SessionSystemContext context = CreateContext(
                    UserTokenType.UserName, MessageSecurityMode.SignAndEncrypt, role);
                var operation = (OperationContext)context.OperationContext!;
                ServiceResult? result;
                try
                {
                    if (path < 2)
                    {
                        (bool handled, ServiceResult? verdict) = await harness.Manager.TryValidatePermissionsAsync(
                            operation,
                            handle,
                            permission,
                            path == 0 ? null : sharedCache ?? new Dictionary<NodeId, Variant[]>(),
                            permissionsOnly: path == 0,
                            logger: null,
                            validateAccessRestrictions: false).ConfigureAwait(false);
                        Assert.That(handled, Is.True);
                        result = verdict;
                    }
                    else if (path < 4)
                    {
                        NodeMetadata? permissions = path == 2
                            ? await harness.Manager.GetPermissionMetadataAsync(
                                operation, handle, BrowseResultMask.NodeClass, sharedCache!, permissionsOnly: true)
                                .ConfigureAwait(false)
                            : await harness.Manager.GetNodeMetadataAsync(operation, handle, BrowseResultMask.NodeClass)
                                .ConfigureAwait(false);
                        Assert.That(permissions, Is.Not.Null);
                        result = MasterNodeManager.ValidateRolePermissions(operation, permissions!, permission);
                    }
                    else
                    {
                        IServerInternal server = m_server.CurrentInstance;
                        (ServiceResult rolesResult, DataValue roles) =
                            await AsyncCustomNodeManager.ReadConfigurationPermissionAttributeAsync(
                                server, node, ToServerContext(server, context), Attributes.RolePermissions,
                                default, default, new DataValue(),
                                CancellationToken.None).ConfigureAwait(false);
                        (ServiceResult userRolesResult, DataValue userRoles) =
                            await AsyncCustomNodeManager.ReadConfigurationPermissionAttributeAsync(
                                server, node, ToServerContext(server, context), Attributes.UserRolePermissions,
                                default, default,
                                new DataValue(), CancellationToken.None).ConfigureAwait(false);
                        bool absentRoles = rolesResult.StatusCode == StatusCodes.BadAttributeIdInvalid &&
                            node.RolePermissions.IsNull && node.OnReadRolePermissions == null;
                        bool absentUserRoles = userRolesResult.StatusCode == StatusCodes.BadAttributeIdInvalid &&
                            node.UserRolePermissions.IsNull && node.OnReadUserRolePermissions == null;
                        if (!ServiceResult.IsGood(rolesResult) && !absentRoles)
                        {
                            result = rolesResult;
                        }
                        else if (!ServiceResult.IsGood(userRolesResult) && !absentUserRoles)
                        {
                            result = userRolesResult;
                        }
                        else
                        {
                            NamespaceDefaultPermissions defaults = await NamespaceDefaultPermissionReader.ReadAsync(
                                server, ToServerContext(server, context), node.NodeId.NamespaceIndex)
                                .ConfigureAwait(false);
                            Assert.That(defaults.Status, Is.EqualTo(StatusCodes.Good));
                            var advertised = new NodeMetadata(node, node.NodeId)
                            {
                                DefaultRolePermissions = defaults.DefaultRolePermissions,
                                DefaultUserRolePermissions = defaults.DefaultUserRolePermissions
                            };
                            if (roles.WrappedValue.TryGetStructure(out ArrayOf<RolePermissionType> rolePermissions))
                            {
                                advertised.RolePermissions = rolePermissions;
                            }
                            if (userRoles.WrappedValue.TryGetStructure(out ArrayOf<RolePermissionType> userPermissions))
                            {
                                advertised.UserRolePermissions = userPermissions;
                            }
                            result = MasterNodeManager.ValidateRolePermissions(operation, advertised, permission);
                        }
                    }
                }
                catch (ServiceResultException exception)
                {
                    result = new ServiceResult(exception);
                }
                Assert.That(result?.StatusCode, Is.EqualTo(expectedStatus),
                    $"Permission path {path} on {node.BrowseName}");
            }
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

        /// <summary>
        /// Supplies server services while preserving the permission test's caller and operation cache.
        /// </summary>
        private static ServerSystemContext ToServerContext(IServerInternal server, SessionSystemContext context)
        {
            return new ServerSystemContext(server, (OperationContext)context.OperationContext!);
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
            /// Gets or sets a signal raised when configuration application starts.
            /// </summary>
            public TaskCompletionSource<bool>? ApplyStarted { get; set; }

            /// <summary>
            /// Gets or sets an optional signal that releases configuration application.
            /// </summary>
            public TaskCompletionSource<bool>? ApplyRelease { get; set; }

            /// <summary>
            /// Gets or sets the signal raised when a provider read starts.
            /// </summary>
            public TaskCompletionSource<bool>? ReadStarted { get; set; }

            /// <summary>
            /// Gets or sets the signal releasing a provider read.
            /// </summary>
            public TaskCompletionSource<bool>? ReadRelease { get; set; }

            /// <summary>
            /// Gets or sets whether a provider read returns without observing cancellation.
            /// </summary>
            public bool IgnoreReadCancellation { get; set; }

            /// <summary>
            /// Gets the number of provider reads.
            /// </summary>
            public int ReadCount { get; private set; }

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
            public async ValueTask<ByteString> ReadConfigurationAsync(CancellationToken cancellationToken = default)
            {
                ReadCount++;
                ReadStarted?.TrySetResult(true);
                if (ReadRelease != null)
                {
                    await ReadRelease.Task.WaitAsync(
                        IgnoreReadCancellation ? CancellationToken.None : cancellationToken).ConfigureAwait(false);
                }
                return m_store.Content;
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
            public async ValueTask ApplyConfigurationAsync(
                ByteString configuration,
                CancellationToken cancellationToken = default)
            {
                ApplyStarted?.TrySetResult(true);
                if (ApplyRelease != null)
                {
                    await ApplyRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                if (ApplyShouldThrow)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "apply failed");
                }
                m_store.ApplyCount++;
                m_store.LastApplied = configuration;
                m_store.Content = configuration;
                CurrentVersion++;
                m_store.LastUpdateTime = DateTime.UtcNow;
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
