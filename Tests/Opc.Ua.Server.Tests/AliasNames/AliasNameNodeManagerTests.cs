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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Server.Tests.AliasNames
{
    /// <summary>
    /// Coverage tests for <see cref="AliasNameNodeManager"/> built around a
    /// mocked <see cref="IServerInternal"/> following the pattern used by
    /// <c>AsyncCustomNodeManagerTests</c>.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class AliasNameNodeManagerTests
    {
        private const string c_namespaceUri = "http://example.org/AliasNames/";

        private Mock<IServerInternal> m_mockServer;
        private NamespaceTable m_namespaceTable;
        private ApplicationConfiguration m_configuration;
        private InMemoryAliasNameStore m_store;

        [SetUp]
        public void SetUp()
        {
            m_mockServer = new Mock<IServerInternal>();
            m_namespaceTable = new NamespaceTable();
            m_namespaceTable.Append(c_namespaceUri);

            var stringTable = new StringTable();
            var typeTable = new TypeTable(m_namespaceTable);

            ITelemetryContext telemetry = Ua.Tests.NUnitTelemetryContext.Create();

            m_mockServer.Setup(s => s.NamespaceUris).Returns(m_namespaceTable);
            m_mockServer.Setup(s => s.ServerUris).Returns(stringTable);
            m_mockServer.Setup(s => s.TypeTree).Returns(typeTable);
            m_mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            m_mockServer.Setup(s => s.Telemetry).Returns(telemetry);
            m_mockServer.Setup(s => s.DefaultSystemContext)
                .Returns(new ServerSystemContext(m_mockServer.Object));

            m_configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200
                }
            };

            // No additional type-tree subtype seeding is needed because
            // the tests pass NodeId.Null / References as the reference
            // type filter — InMemoryAliasNameStore short-circuits in that
            // case and never invokes ITypeTable.IsTypeOf.

            var categoryId = new NodeId("MyCategory", 1);
            var descriptor = new AliasNameCategoryDescriptor(
                categoryId,
                new QualifiedName("MyCategory", 1),
                AliasNameCapabilities.All);
            m_store = new InMemoryAliasNameStore([descriptor]);
        }

        [TearDown]
        public void TearDown()
        {
            m_store?.Dispose();
        }

        private AliasNameNodeManager CreateManager(
            AliasNameNodeManagerOptions options = null)
        {
            return new AliasNameNodeManager(
                m_mockServer.Object,
                m_configuration,
                m_store,
                options ??
                new AliasNameNodeManagerOptions
                {
                    NamespaceUri = c_namespaceUri,
                    RegisterWithServerRegistry = false
                });
        }

        [Test]
        public async Task CreateAddressSpaceBuildsCategoryWithAllOptionalChildrenAsync()
        {
            using AliasNameNodeManager manager = CreateManager();
            var externalReferences =
                new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);

            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(
                    new NodeId("MyCategory", 1));
            Assert.That(category, Is.Not.Null);
            Assert.That(category.FindAlias, Is.Not.Null);
            Assert.That(category.FindAliasVerbose, Is.Not.Null);
            Assert.That(category.AddAliasesToCategory, Is.Not.Null);
            Assert.That(category.DeleteAliasesFromCategory, Is.Not.Null);
            Assert.That(category.LastChange, Is.Not.Null);

            Assert.That(externalReferences.TryGetValue(
                ObjectIds.Aliases, out IList<IReference> refs), Is.True);
            bool hasOrganizes = false;
            foreach (IReference r in refs)
            {
                if (r.ReferenceTypeId.Equals(ReferenceTypeIds.Organizes) &&
                    r.TargetId.Equals(category.NodeId))
                {
                    hasOrganizes = true;
                    break;
                }
            }
            Assert.That(hasOrganizes, Is.True,
                "Expected an Organizes external reference from Aliases to category.");
        }

        [Test]
        public async Task OmittingCapabilitiesSkipsOptionalChildrenAsync()
        {
            m_store.Dispose();
            var minimal = new AliasNameCategoryDescriptor(
                new NodeId("Minimal", 1),
                new QualifiedName("Minimal", 1),
                AliasNameCapabilities.None);
            m_store = new InMemoryAliasNameStore([minimal]);

            using AliasNameNodeManager manager = CreateManager();
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(
                    new NodeId("Minimal", 1));
            Assert.That(category, Is.Not.Null);
            Assert.That(category.FindAlias, Is.Not.Null,
                "Mandatory FindAlias must always be present.");
            Assert.That(category.FindAliasVerbose, Is.Null);
            Assert.That(category.AddAliasesToCategory, Is.Null);
            Assert.That(category.DeleteAliasesFromCategory, Is.Null);
            Assert.That(category.LastChange, Is.Null);
        }

        [Test]
        public async Task FindAliasHandlerReturnsStoreAliasesAsync()
        {
            using AliasNameNodeManager manager = CreateManager();
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            var categoryId = new NodeId("MyCategory", 1);
            await m_store.AddAliasesAsync(categoryId,
                [
                    new AliasAddRequest("Alpha",
                        new ExpandedNodeId("Tgt", 1),
                        null,
                        ReferenceTypeIds.AliasFor)
                ],
                CancellationToken.None).ConfigureAwait(false);

            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(categoryId);
            var input = new ArrayOf<Variant>(
                new[]
                {
                    new Variant("%"),
                    new Variant(NodeId.Null)
                });
            var argumentErrors = new List<ServiceResult>();
            var output = new List<Variant>();
            ServiceResult result = await category.FindAlias!.CallAsync(
                manager.SystemContext,
                categoryId,
                input,
                argumentErrors,
                output,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(result, Is.Null.Or.EqualTo(ServiceResult.Good));
            Assert.That(output.Count, Is.EqualTo(1));
            Assert.That(output[0].TryGetStructure(
                out ArrayOf<AliasNameDataType> aliases), Is.True);
            Assert.That(aliases.Count, Is.EqualTo(1));
            Assert.That(aliases[0].AliasName.Name, Is.EqualTo("Alpha"));
        }

        [Test]
        public async Task LastChangeReflectsStoreUpdatesAsync()
        {
            using AliasNameNodeManager manager = CreateManager();
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            var categoryId = new NodeId("MyCategory", 1);
            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(categoryId);

            uint initial = category.LastChange!.Value;
            await m_store.AddAliasesAsync(categoryId,
                [
                    new AliasAddRequest("LC1",
                        new ExpandedNodeId("Tgt", 1),
                        null,
                        ReferenceTypeIds.AliasFor)
                ],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(category.LastChange.Value, Is.Not.EqualTo(initial));
        }

        private static readonly string[] s_singleX = ["X"];
        private static readonly ExpandedNodeId[] s_singleT = [new("T", 1)];
        private static readonly string[] s_singleEmpty = [""];

        [Test]
        public async Task AddAliasesAnonymousIsRejectedWithBadUserAccessDeniedAsync()
        {
            using AliasNameNodeManager manager = CreateManager(
                new AliasNameNodeManagerOptions
                {
                    NamespaceUri = c_namespaceUri,
                    RegisterWithServerRegistry = false,
                    RequireSecurityAdminForMutations = true
                });
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            var categoryId = new NodeId("MyCategory", 1);
            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(categoryId);

            var input = new ArrayOf<Variant>(
                new[]
                {
                    new Variant(s_singleX.ToArrayOf()),
                    new Variant(s_singleT.ToArrayOf()),
                    new Variant(s_singleEmpty.ToArrayOf()),
                    new Variant(ReferenceTypeIds.AliasFor)
                });
            var argumentErrors = new List<ServiceResult>();
            var output = new List<Variant>
            {
                new(System.Array.Empty<StatusCode>().ToArrayOf())
            };

            ServiceResult result = await category.AddAliasesToCategory!.CallAsync(
                manager.SystemContext,
                categoryId,
                input,
                argumentErrors,
                output,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        // --------------------------------------------------------------
        // Security matrix for HasSecureAdminAccess.
        // The auth boundary depends on (a) the channel's SecurityMode
        // and (b) whether the user identity carries the SecurityAdmin
        // role. The matrix below exercises every non-trivial combination
        // and asserts the user-facing service result on AddAliases
        // and DeleteAliases.
        // --------------------------------------------------------------

        private ServerSystemContext BuildSecuritySystemContext(
            MessageSecurityMode securityMode,
            bool grantSecurityAdmin)
        {
            var endpoint = new EndpointDescription { SecurityMode = securityMode };
            var secureChannelContext = new SecureChannelContext(
                "test", endpoint, RequestEncoding.Binary,
                clientChannelCertificate: null,
                serverChannelCertificate: null,
                channelThumbprint: null);

            var baseIdentity = new UserIdentity("admin", []);
            IUserIdentity identity = grantSecurityAdmin
                ? new RoleBasedIdentity(
                    baseIdentity,
                    [Role.SecurityAdmin],
                    m_mockServer.Object.NamespaceUris)
                : baseIdentity;

            // The identity must be carried by the OperationContext —
            // SessionSystemContext.UserIdentity prefers
            // OperationContext.UserIdentity when the OperationContext
            // implements ISessionOperationContext (which the server-side
            // OperationContext class does), so setting it on the
            // SystemContext directly is silently ignored.
            var opContext = new OperationContext(
                new RequestHeader(),
                secureChannelContext,
                RequestType.Call,
                RequestLifetime.None,
                identity);

            return new ServerSystemContext(m_mockServer.Object, opContext);
        }

        private async Task<ServiceResult> InvokeAddAliasesAsync(
            AliasNameNodeManager manager,
            AliasNameCategoryState category,
            NodeId categoryId,
            ISystemContext context)
        {
            var input = new ArrayOf<Variant>(
                new[]
                {
                    new Variant(s_singleX.ToArrayOf()),
                    new Variant(s_singleT.ToArrayOf()),
                    new Variant(s_singleEmpty.ToArrayOf()),
                    new Variant(ReferenceTypeIds.AliasFor)
                });
            var argumentErrors = new List<ServiceResult>();
            var output = new List<Variant>
            {
                new(System.Array.Empty<StatusCode>().ToArrayOf())
            };
            return await category.AddAliasesToCategory!.CallAsync(
                context, categoryId, input, argumentErrors, output,
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<ServiceResult> InvokeDeleteAliasesAsync(
            AliasNameNodeManager manager,
            AliasNameCategoryState category,
            NodeId categoryId,
            ISystemContext context)
        {
            var input = new ArrayOf<Variant>(
                new[]
                {
                    new Variant(s_singleX.ToArrayOf()),
                    new Variant(s_singleT.ToArrayOf())
                });
            var argumentErrors = new List<ServiceResult>();
            var output = new List<Variant>
            {
                new(System.Array.Empty<StatusCode>().ToArrayOf())
            };
            return await category.DeleteAliasesFromCategory!.CallAsync(
                context, categoryId, input, argumentErrors, output,
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<(AliasNameNodeManager Manager,
                            AliasNameCategoryState Category,
                            NodeId CategoryId)>
            CreateAdminCheckedManagerAsync()
        {
            AliasNameNodeManager manager = CreateManager(
                new AliasNameNodeManagerOptions
                {
                    NamespaceUri = c_namespaceUri,
                    RegisterWithServerRegistry = false,
                    RequireSecurityAdminForMutations = true,
                });
            await manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            var categoryId = new NodeId("MyCategory", 1);
            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(categoryId);
            return (manager, category, categoryId);
        }

        [Test]
        public async Task AddAliasesAdminOnSignAndEncryptIsAcceptedAsync()
        {
            (AliasNameNodeManager manager, AliasNameCategoryState category, NodeId categoryId) =
                await CreateAdminCheckedManagerAsync().ConfigureAwait(false);
            using (manager)
            {
                ServerSystemContext ctx = BuildSecuritySystemContext(
                    MessageSecurityMode.SignAndEncrypt, grantSecurityAdmin: true);
                ServiceResult result = await InvokeAddAliasesAsync(
                    manager, category, categoryId, ctx).ConfigureAwait(false);

                // The auth predicate must pass; the call reaches the
                // store and returns Good at the service level.
                Assert.That(result, Is.Null.Or.EqualTo(ServiceResult.Good),
                    "Admin on SignAndEncrypt must satisfy HasSecureAdminAccess.");
            }
        }

        [Test]
        public async Task AddAliasesAdminOnSignOnlyIsRejectedAsync()
        {
            (AliasNameNodeManager manager, AliasNameCategoryState category, NodeId categoryId) =
                await CreateAdminCheckedManagerAsync().ConfigureAwait(false);
            using (manager)
            {
                ServerSystemContext ctx = BuildSecuritySystemContext(
                    MessageSecurityMode.Sign, grantSecurityAdmin: true);
                ServiceResult result = await InvokeAddAliasesAsync(
                    manager, category, categoryId, ctx).ConfigureAwait(false);

                Assert.That(result, Is.Not.Null);
                Assert.That(result.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied),
                    "Admin role on Sign-only must NOT be sufficient — admin mutations require SignAndEncrypt.");
            }
        }

        [Test]
        public async Task AddAliasesAdminOnNoneSecurityIsRejectedAsync()
        {
            (AliasNameNodeManager manager, AliasNameCategoryState category, NodeId categoryId) =
                await CreateAdminCheckedManagerAsync().ConfigureAwait(false);
            using (manager)
            {
                ServerSystemContext ctx = BuildSecuritySystemContext(
                    MessageSecurityMode.None, grantSecurityAdmin: true);
                ServiceResult result = await InvokeAddAliasesAsync(
                    manager, category, categoryId, ctx).ConfigureAwait(false);

                Assert.That(result.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
        }

        [Test]
        public async Task AddAliasesNonAdminOnSignAndEncryptIsRejectedAsync()
        {
            (AliasNameNodeManager manager, AliasNameCategoryState category, NodeId categoryId) =
                await CreateAdminCheckedManagerAsync().ConfigureAwait(false);
            using (manager)
            {
                ServerSystemContext ctx = BuildSecuritySystemContext(
                    MessageSecurityMode.SignAndEncrypt, grantSecurityAdmin: false);
                ServiceResult result = await InvokeAddAliasesAsync(
                    manager, category, categoryId, ctx).ConfigureAwait(false);

                Assert.That(result, Is.Not.Null);
                Assert.That(result.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied),
                    "A SignAndEncrypt channel alone is not enough; the user must also have SecurityAdmin.");
            }
        }

        [Test]
        public async Task DeleteAliasesAnonymousIsRejectedWithBadUserAccessDeniedAsync()
        {
            (AliasNameNodeManager manager, AliasNameCategoryState category, NodeId categoryId) =
                await CreateAdminCheckedManagerAsync().ConfigureAwait(false);
            using (manager)
            {
                // Default manager.SystemContext has no SessionSystemContext shape —
                // mirror the existing Add-side anonymous-rejection test.
                ServiceResult result = await InvokeDeleteAliasesAsync(
                    manager, category, categoryId, manager.SystemContext)
                    .ConfigureAwait(false);

                Assert.That(result, Is.Not.Null);
                Assert.That(result.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
        }

        [Test]
        public async Task DeleteAliasesAdminOnSignAndEncryptIsAcceptedAsync()
        {
            (AliasNameNodeManager manager, AliasNameCategoryState category, NodeId categoryId) =
                await CreateAdminCheckedManagerAsync().ConfigureAwait(false);
            using (manager)
            {
                ServerSystemContext ctx = BuildSecuritySystemContext(
                    MessageSecurityMode.SignAndEncrypt, grantSecurityAdmin: true);
                ServiceResult result = await InvokeDeleteAliasesAsync(
                    manager, category, categoryId, ctx).ConfigureAwait(false);

                // The auth predicate passes; the store returns Good
                // at the service level even though the per-row delete
                // would fail with BadNotFound (the test fixture's store
                // is empty) — the service result itself is Good.
                Assert.That(result, Is.Null.Or.EqualTo(ServiceResult.Good),
                    "Admin on SignAndEncrypt must satisfy the Delete-side auth predicate.");
            }
        }

        [Test]
        public async Task DeleteAliasesAdminOnSignOnlyIsRejectedAsync()
        {
            (AliasNameNodeManager manager, AliasNameCategoryState category, NodeId categoryId) =
                await CreateAdminCheckedManagerAsync().ConfigureAwait(false);
            using (manager)
            {
                ServerSystemContext ctx = BuildSecuritySystemContext(
                    MessageSecurityMode.Sign, grantSecurityAdmin: true);
                ServiceResult result = await InvokeDeleteAliasesAsync(
                    manager, category, categoryId, ctx).ConfigureAwait(false);

                Assert.That(result.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
        }
    }
}
