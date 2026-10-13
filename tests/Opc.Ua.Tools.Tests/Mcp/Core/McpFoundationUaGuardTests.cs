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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpAuthorization
{
    /// <summary>
    /// Direct guard classification evidence. These tests do not dispatch mutation services;
    /// generic-tool no-dispatch evidence belongs to the separate tool-entry-point tests.
    /// </summary>
    [TestFixture]
    public sealed class McpFoundationUaGuardTests
    {
        [SetUp]
        public void SetUp()
        {
            m_model = new McpAuthorizationModel();
            m_policy = new Mock<IMcpOperationPolicy>(MockBehavior.Strict);
            m_guard = new McpUaOperationGuard(m_policy.Object);
            m_cancellation = new CancellationTokenSource();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                m_model.VerifyNoMutations();
                AssertWireArguments();
                m_model.NodeCache.Verify(cache => cache.IsTypeOfAsync(
                    It.IsAny<ExpandedNodeId>(), It.IsAny<ExpandedNodeId>(),
                    It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                m_cancellation.Dispose();
            }
        }

        [Test]
        public void GuardConstructorRejectsNullPolicy()
        {
            Assert.That(
                () => new McpUaOperationGuard(null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("policy"));
            AssertNoModelAccess();
            m_policy.VerifyNoOtherCalls();
        }

        [TestCaseSource(nameof(CapabilityCases))]
        public async Task AuthorizeTargetSelectsExactCapabilityAsync(
            NodeId standardType,
            string? namespaceUri,
            McpCapability capability,
            bool typedCall,
            bool typedMutation,
            bool genericCall)
        {
            NodeId target = namespaceUri is null
                ? standardType
                : new NodeId(8000u, (ushort)m_model.NamespaceUris.GetIndex(namespaceUri));
            m_model.AddNode(target);
            NodeId instance = m_model.AddNode(new NodeId("typed-instance", 1));
            m_model.AddTypeDefinition(instance, target);
            Allow(capability);

            if (typedMutation || (genericCall && typedCall))
            {
                await AssertStatusAsync(
                    () => m_guard.AuthorizeTargetAsync(
                        m_model.SessionInfo, target, genericCall, m_cancellation.Token),
                    StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
                await AssertStatusAsync(
                    () => m_guard.AuthorizeTargetAsync(
                        m_model.SessionInfo, instance, genericCall, m_cancellation.Token),
                    StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
            }
            else
            {
                await m_guard.AuthorizeTargetAsync(
                    m_model.SessionInfo, target, genericCall, m_cancellation.Token).ConfigureAwait(false);
                await m_guard.AuthorizeTargetAsync(
                    m_model.SessionInfo, instance, genericCall, m_cancellation.Token).ConfigureAwait(false);
            }

            VerifyAuthority(capability, Times.Exactly(2));
            Assert.That(m_model.Browses, Has.Count.EqualTo(1));
            AssertQuery(instance, ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward);
            Assert.That(m_model.Browses[0].Result.References[0].NodeId, Is.EqualTo(new ExpandedNodeId(target)));
            Assert.That(m_model.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
        }

        [TestCase("EmptyMetadata", false)]
        [TestCase("EmptyMetadata", true)]
        [TestCase("UnrelatedType", false)]
        [TestCase("UnrelatedType", true)]
        [TestCase("MisleadingBrowseName", false)]
        [TestCase("MisleadingBrowseName", true)]
        [TestCase("VendorNumericLookalike", false)]
        [TestCase("VendorNumericLookalike", true)]
        [TestCase("VendorProtectedName", false)]
        [TestCase("VendorProtectedName", true)]
        public async Task AuthorizeTargetAllowsOrdinaryModelWithoutAuthorityAsync(string shape, bool genericCall)
        {
            m_model = new McpAuthorizationModel(MessageSecurityMode.None);
            NodeId target = m_model.AddNode(new NodeId("ordinary", 1));
            NodeId type = shape switch
            {
                "UnrelatedType" => ObjectTypeIds.BaseObjectType,
                "MisleadingBrowseName" => new NodeId("ordinary-type", 4),
                "VendorNumericLookalike" => ObjectTypeIds.RoleType.WithNamespaceIndex(4),
                "VendorProtectedName" => new NodeId("SecurityGroupType", 4),
                _ => default
            };
            if (!type.IsNull)
            {
                m_model.AddNode(type);
                m_model.AddTypeDefinition(target, type, new QualifiedName("RoleType", 0));
            }

            await m_guard.AuthorizeTargetAsync(
                m_model.SessionInfo, target, genericCall, m_cancellation.Token).ConfigureAwait(false);

            m_policy.VerifyNoOtherCalls();
            AssertOrdinaryQueries([target]);
            Assert.That(m_model.Browses, Has.Count.EqualTo(type.IsNull ? 3 : 4));
            if (!type.IsNull)
            {
                AssertQuery(type, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse);
                Assert.That(m_model.CacheQueries, Has.Count.EqualTo(19));
                Assert.That(m_model.CacheQueries.Select(query => query.Actual), Is.All.EqualTo(type));
                Assert.That(m_model.CacheQueries.Select(query => query.Matches), Is.All.False);
            }
            Assert.That(m_model.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.None));
        }

        [Test]
        public async Task AuthorizeTargetPreservesPolicyRefusalAsync()
        {
            var refusal = new ServiceResultException(StatusCodes.BadUserAccessDenied, "Host grant refused.");
            m_policy.Setup(policy => policy.RequireSession(McpCapability.IdentityAdministration, "Prod-A"))
                .Throws(refusal);

            ServiceResultException actual = await AssertStatusAsync(
                () => m_guard.AuthorizeTargetAsync(
                    m_model.SessionInfo, ObjectTypeIds.RoleType, false, m_cancellation.Token),
                StatusCodes.BadUserAccessDenied).ConfigureAwait(false);

            Assert.That(actual, Is.SameAs(refusal));
            VerifyAuthority(McpCapability.IdentityAdministration, Times.Once());
            Assert.That(m_model.Browses, Is.Empty);
            m_model.Session.VerifyGet(session => session.ConfiguredEndpoint, Times.Never);
        }

        [TestCase(false, MessageSecurityMode.None)]
        [TestCase(false, MessageSecurityMode.Sign)]
        [TestCase(false, MessageSecurityMode.Invalid)]
        [TestCase(true, MessageSecurityMode.None)]
        [TestCase(true, MessageSecurityMode.Sign)]
        [TestCase(true, MessageSecurityMode.Invalid)]
        public async Task AuthorizeTargetRequiresSecureProtectedMutationAsync(bool gds, MessageSecurityMode mode)
        {
            m_model = new McpAuthorizationModel(mode);
            NodeId target = gds ? new NodeId("directory", 2) : ObjectTypeIds.RoleType;
            McpCapability capability = gds
                ? McpCapability.DirectoryAdministration
                : McpCapability.IdentityAdministration;
            Allow(capability);

            await AssertStatusAsync(
                () => m_guard.AuthorizeTargetAsync(
                    m_model.SessionInfo, target, false, m_cancellation.Token),
                StatusCodes.BadSecurityModeInsufficient).ConfigureAwait(false);

            VerifyAuthority(capability, Times.Once());
            Assert.That(m_model.Browses, Is.Empty);
            m_model.Session.VerifyGet(session => session.ConfiguredEndpoint, Times.Once);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task AuthorizeCallsChecksObjectAndMethodIndependentlyAsync(bool protectedObject)
        {
            NodeId objectId = m_model.AddNode(new NodeId("object", 1));
            NodeId methodId = m_model.AddNode(new NodeId("method", 1));
            if (protectedObject)
            {
                m_model.AddTypeDefinition(objectId, ObjectTypeIds.RoleType);
            }
            else
            {
                m_model.AddOwner(methodId, ObjectTypeIds.RoleType, ReferenceTypeIds.HasComponent);
            }
            Allow(McpCapability.IdentityAdministration);

            await m_guard.AuthorizeCallsAsync(m_model.SessionInfo,
                [new CallMethodRequest { ObjectId = objectId, MethodId = methodId }],
                m_cancellation.Token).ConfigureAwait(false);

            VerifyAuthority(McpCapability.IdentityAdministration, Times.Once());
            AssertQuery(objectId, ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward);
            AssertQuery(methodId, ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward);
            Assert.That(m_model.Browses, Has.Count.EqualTo(protectedObject ? 4 : 6));
            AssertOrdinaryQueries([protectedObject ? methodId : objectId]);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AuthorizeCallsRejectsAnyDeniedRequestInBatchAsync(bool deniedFirst)
        {
            NodeId ordinaryObject = m_model.AddNode(new NodeId("ordinary-object", 1));
            NodeId ordinaryMethod = m_model.AddNode(new NodeId("ordinary-method", 1));
            NodeId deniedObject = m_model.AddNode(new NodeId("denied-object", 1));
            NodeId deniedMethod = m_model.AddNode(new NodeId("denied-method", 1));
            m_model.AddTypeDefinition(deniedObject, ObjectTypeIds.CertificateGroupType);
            var ordinary = new CallMethodRequest { ObjectId = ordinaryObject, MethodId = ordinaryMethod };
            var denied = new CallMethodRequest { ObjectId = deniedObject, MethodId = deniedMethod };
            ArrayOf<CallMethodRequest> requests = deniedFirst ? [denied, ordinary] : [ordinary, denied];
            Deny(McpCapability.CertificateAdministration);

            await AssertStatusAsync(
                () => m_guard.AuthorizeCallsAsync(m_model.SessionInfo, requests, m_cancellation.Token),
                StatusCodes.BadUserAccessDenied).ConfigureAwait(false);

            VerifyAuthority(McpCapability.CertificateAdministration, Times.Once());
            Assert.That(m_model.Browses, Has.Count.EqualTo(deniedFirst ? 1 : 7));
            AssertQuery(deniedObject, ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward);
            Assert.That(m_model.Browses.Any(query => query.Request.NodeId == deniedMethod), Is.False);
            if (!deniedFirst)
            {
                AssertOrdinaryQueries([ordinaryObject, ordinaryMethod]);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(5)]
        public async Task AuthorizeNodesChecksEverySelectedNodeAsync(int count)
        {
            NodeId first = m_model.AddNode(new NodeId("first", 1));
            NodeId middle = m_model.AddNode(new NodeId("middle", 1));
            NodeId last = m_model.AddNode(new NodeId("last", 1));
            ArrayOf<NodeId> nodes = count switch
            {
                0 => [],
                1 => [ObjectTypeIds.RoleType],
                _ => [first, ObjectTypeIds.RoleType, middle, ObjectTypeIds.CertificateGroupType, last]
            };
            if (count > 0)
            {
                Allow(McpCapability.IdentityAdministration);
            }
            if (count > 1)
            {
                Allow(McpCapability.CertificateAdministration);
            }

            await m_guard.AuthorizeNodesAsync(m_model.SessionInfo, nodes, m_cancellation.Token)
                .ConfigureAwait(false);

            m_policy.Verify(policy => policy.RequireSession(McpCapability.IdentityAdministration, "Prod-A"),
                Times.Exactly(count == 0 ? 0 : 1));
            m_policy.Verify(policy => policy.RequireSession(McpCapability.CertificateAdministration, "Prod-A"),
                Times.Exactly(count > 1 ? 1 : 0));
            m_policy.VerifyNoOtherCalls();
            Assert.That(m_model.Browses, Has.Count.EqualTo(count > 1 ? 9 : 0));
            if (count > 1)
            {
                AssertOrdinaryQueries([first, middle, last]);
            }
            else if (count == 0)
            {
                AssertNoModelAccess();
            }
        }

        [TestCase(Attributes.RolePermissions)]
        [TestCase(Attributes.UserRolePermissions)]
        [TestCase(Attributes.AccessRestrictions)]
        [TestCase(Attributes.UserAccessLevel)]
        [TestCase(Attributes.UserExecutable)]
        [TestCase(Attributes.UserWriteMask)]
        public async Task AuthorizeWritesClassifiesEachPermissionAttributeAsync(uint attribute)
        {
            NodeId node = m_model.AddNode(new NodeId("ordinary-permission-target", 1));
            Allow(McpCapability.IdentityAdministration);

            await m_guard.AuthorizeWritesAsync(m_model.SessionInfo,
                [new WriteValue { NodeId = node, AttributeId = attribute }],
                m_cancellation.Token).ConfigureAwait(false);

            VerifyAuthority(McpCapability.IdentityAdministration, Times.Once());
            AssertOrdinaryQueries([node]);
            Assert.That(m_model.Browses, Has.Count.EqualTo(3));
            m_model.Session.VerifyGet(session => session.ConfiguredEndpoint, Times.Once);
        }

        [TestCase(MessageSecurityMode.None)]
        [TestCase(MessageSecurityMode.Sign)]
        [TestCase(MessageSecurityMode.Invalid)]
        public async Task AuthorizePermissionWriteRequiresSecureChannelAsync(MessageSecurityMode mode)
        {
            m_model = new McpAuthorizationModel(mode);
            NodeId node = m_model.AddNode(new NodeId("ordinary-permission-target", 1));
            Allow(McpCapability.IdentityAdministration);

            await AssertStatusAsync(
                () => m_guard.AuthorizeWritesAsync(m_model.SessionInfo,
                    [new WriteValue { NodeId = node, AttributeId = Attributes.RolePermissions }],
                    m_cancellation.Token),
                StatusCodes.BadSecurityModeInsufficient).ConfigureAwait(false);

            VerifyAuthority(McpCapability.IdentityAdministration, Times.Once());
            Assert.That(m_model.Browses, Is.Empty);
            Assert.That(m_model.CacheQueries, Is.Empty);
            m_model.Session.VerifyGet(session => session.ConfiguredEndpoint, Times.Once);
        }

        [TestCase("BothGranted")]
        [TestCase("IdentityDenied")]
        [TestCase("CertificateDenied")]
        public async Task AuthorizeWritesRequiresBothPermissionAndDomainCapabilitiesAsync(string grants)
        {
            NodeId node = m_model.AddNode(new NodeId("certificate-permissions", 1));
            m_model.AddTypeDefinition(node, ObjectTypeIds.CertificateGroupType);
            if (grants == "IdentityDenied")
            {
                Deny(McpCapability.IdentityAdministration);
            }
            else
            {
                Allow(McpCapability.IdentityAdministration);
                if (grants == "CertificateDenied")
                {
                    Deny(McpCapability.CertificateAdministration);
                }
                else
                {
                    Allow(McpCapability.CertificateAdministration);
                }
            }
            ArrayOf<WriteValue> writes = [new WriteValue
            {
                NodeId = node,
                AttributeId = Attributes.RolePermissions
            }];

            if (grants == "BothGranted")
            {
                await m_guard.AuthorizeWritesAsync(m_model.SessionInfo, writes, m_cancellation.Token)
                    .ConfigureAwait(false);
            }
            else
            {
                await AssertStatusAsync(
                    () => m_guard.AuthorizeWritesAsync(m_model.SessionInfo, writes, m_cancellation.Token),
                    StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
            }

            McpCapability[] expected = grants == "IdentityDenied"
                ? [McpCapability.IdentityAdministration]
                : [McpCapability.IdentityAdministration, McpCapability.CertificateAdministration];
            Assert.That(m_policy.Invocations.Select(call => (McpCapability)call.Arguments[0]), Is.EqualTo(expected));
            m_policy.Verify(policy => policy.RequireSession(McpCapability.IdentityAdministration, "Prod-A"),
                Times.Once);
            m_policy.Verify(policy => policy.RequireSession(McpCapability.CertificateAdministration, "Prod-A"),
                Times.Exactly(grants == "IdentityDenied" ? 0 : 1));
            m_policy.VerifyNoOtherCalls();
            Assert.That(m_model.Browses, Has.Count.EqualTo(grants == "IdentityDenied" ? 0 : 1));
            if (grants == "IdentityDenied")
            {
                AssertNoModelAccess();
            }
            else
            {
                AssertQuery(node, ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward);
            }
        }

        [Test]
        public async Task AuthorizeWritesValueOnOrdinaryNodeNeedsNoCapabilityAsync()
        {
            m_model = new McpAuthorizationModel(MessageSecurityMode.None);
            NodeId node = m_model.AddNode(new NodeId("temperature-setpoint", 1));

            await m_guard.AuthorizeWritesAsync(m_model.SessionInfo,
                [new WriteValue
                {
                    NodeId = node,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }],
                m_cancellation.Token).ConfigureAwait(false);

            m_policy.VerifyNoOtherCalls();
            AssertOrdinaryQueries([node]);
            Assert.That(m_model.Browses, Has.Count.EqualTo(3));
            m_model.Session.VerifyGet(session => session.ConfiguredEndpoint, Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AuthorizeWritesRejectsAnyDeniedRequestInBatchAsync(bool deniedFirst)
        {
            NodeId ordinary = m_model.AddNode(new NodeId("ordinary-value", 1));
            NodeId protectedNode = m_model.AddNode(new NodeId("protected-value", 1));
            m_model.AddTypeDefinition(protectedNode, ObjectTypeIds.CertificateGroupType);
            var ordinaryWrite = new WriteValue { NodeId = ordinary, AttributeId = Attributes.Value };
            var protectedWrite = new WriteValue { NodeId = protectedNode, AttributeId = Attributes.Value };
            ArrayOf<WriteValue> writes = deniedFirst
                ? [protectedWrite, ordinaryWrite]
                : [ordinaryWrite, protectedWrite];
            Deny(McpCapability.CertificateAdministration);

            await AssertStatusAsync(
                () => m_guard.AuthorizeWritesAsync(m_model.SessionInfo, writes, m_cancellation.Token),
                StatusCodes.BadUserAccessDenied).ConfigureAwait(false);

            VerifyAuthority(McpCapability.CertificateAdministration, Times.Once());
            AssertQuery(protectedNode, ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward);
            Assert.That(m_model.Browses, Has.Count.EqualTo(deniedFirst ? 1 : 4));
            if (!deniedFirst)
            {
                AssertOrdinaryQueries([ordinary]);
            }
        }

        [Test]
        public async Task AuthorizeWritesChecksEveryGrantedRequestAsync()
        {
            NodeId value = m_model.AddNode(new NodeId("value-target", 1));
            NodeId permission = m_model.AddNode(new NodeId("permission-target", 1));
            NodeId certificate = m_model.AddNode(new NodeId("certificate-target", 1));
            m_model.AddTypeDefinition(certificate, ObjectTypeIds.CertificateGroupType);
            Allow(McpCapability.IdentityAdministration);
            Allow(McpCapability.CertificateAdministration);

            await m_guard.AuthorizeWritesAsync(m_model.SessionInfo,
                [
                    new WriteValue { NodeId = value, AttributeId = Attributes.Value },
                    new WriteValue { NodeId = permission, AttributeId = Attributes.RolePermissions },
                    new WriteValue { NodeId = certificate, AttributeId = Attributes.Value }
                ],
                m_cancellation.Token).ConfigureAwait(false);

            m_policy.Verify(policy => policy.RequireSession(McpCapability.IdentityAdministration, "Prod-A"),
                Times.Once);
            m_policy.Verify(policy => policy.RequireSession(McpCapability.CertificateAdministration, "Prod-A"),
                Times.Once);
            m_policy.VerifyNoOtherCalls();
            AssertOrdinaryQueries([value, permission]);
            AssertQuery(certificate, ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward);
            Assert.That(m_model.Browses, Has.Count.EqualTo(7));
        }

        [TestCase(McpAuthorizationModel.GdsNamespaceUri, 2, 5, McpCapability.DirectoryAdministration)]
        [TestCase(McpAuthorizationModel.GdsNamespaceUri, 2, 7, McpCapability.DirectoryAdministration)]
        [TestCase(McpAuthorizationModel.OnboardingNamespaceUri, 3, 5, McpCapability.Onboarding)]
        [TestCase(McpAuthorizationModel.OnboardingNamespaceUri, 3, 8, McpCapability.Onboarding)]
        public async Task AuthorizeTargetUsesActualNamespaceRemappingAsync(
            string uri, int formerIndex, int actualIndex, McpCapability capability)
        {
            string[] namespaces =
            [
                McpAuthorizationModel.BenignNamespaceUri,
                McpAuthorizationModel.GdsNamespaceUri,
                McpAuthorizationModel.OnboardingNamespaceUri,
                McpAuthorizationModel.VendorNamespaceUri,
                "urn:test:slot-five",
                "urn:test:slot-six",
                "urn:test:slot-seven",
                "urn:test:slot-eight"
            ];
            namespaces[formerIndex - 1] = "urn:test:former-protected-index";
            namespaces[actualIndex - 1] = uri;
            m_model = new McpAuthorizationModel(namespaceUris: namespaces);
            string[] originalNamespaces = m_model.NamespaceUris.ToArray();
            NodeId ordinary = m_model.AddNode(new NodeId(7000u, (ushort)formerIndex));
            NodeId protectedNode = m_model.AddNode(new NodeId(7000u, (ushort)actualIndex));
            Allow(capability);

            await m_guard.AuthorizeTargetAsync(
                m_model.SessionInfo, ordinary, false, m_cancellation.Token).ConfigureAwait(false);
            m_policy.VerifyNoOtherCalls();
            await AssertStatusAsync(
                () => m_guard.AuthorizeTargetAsync(
                    m_model.SessionInfo, protectedNode, false, m_cancellation.Token),
                StatusCodes.BadUserAccessDenied).ConfigureAwait(false);

            VerifyAuthority(capability, Times.Once());
            AssertOrdinaryQueries([ordinary]);
            Assert.That(m_model.Browses, Has.Count.EqualTo(3));
            Assert.That(m_model.NamespaceUris.GetIndex(uri), Is.EqualTo(actualIndex));
            Assert.That(m_model.NamespaceUris.ToArray(), Is.EqualTo(originalNamespaces));
            Assert.That(m_model.Session.Object.NamespaceUris, Is.SameAs(m_model.MessageContext.NamespaceUris));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task AuthorizeTargetResolvesLocalExpandedNamespaceUriAsync(bool typeDefinition)
        {
            NodeId node = m_model.AddNode(new NodeId("target", 1));
            NodeId resolved = m_model.AddNode(new NodeId("resolved-vendor-type", 4));
            var expanded = new ExpandedNodeId(
                new NodeId("resolved-vendor-type", 0), McpAuthorizationModel.VendorNamespaceUri);
            if (typeDefinition)
            {
                m_model.AddTypeDefinition(node, expanded);
                m_model.AddSubtype(resolved, ObjectTypeIds.RoleType);
            }
            else
            {
                m_model.AddOwner(node, expanded, ReferenceTypeIds.HasProperty);
                m_model.AddTypeDefinition(resolved, ObjectTypeIds.RoleType);
            }
            string[] originalNamespaces = m_model.NamespaceUris.ToArray();
            Allow(McpCapability.IdentityAdministration);

            await m_guard.AuthorizeTargetAsync(
                m_model.SessionInfo, node, false, m_cancellation.Token).ConfigureAwait(false);

            VerifyAuthority(McpCapability.IdentityAdministration, Times.Once());
            AssertQuery(resolved,
                typeDefinition ? ReferenceTypeIds.HasSubtype : ReferenceTypeIds.HasTypeDefinition,
                typeDefinition ? BrowseDirection.Inverse : BrowseDirection.Forward);
            Assert.That(m_model.Browses, Has.Count.EqualTo(4));
            if (typeDefinition)
            {
                m_model.NodeCache.Verify(cache => cache.IsTypeOfAsync(
                    resolved, ObjectTypeIds.RoleType, m_cancellation.Token), Times.Once);
            }
            Assert.That(m_model.Browses.Select(query => query.Request.NodeId),
                Does.Not.Contain(new NodeId("resolved-vendor-type", 0)));
            Assert.That(m_model.NamespaceUris.ToArray(), Is.EqualTo(originalNamespaces));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task AuthorizeTargetUsesCachedVendorSubtypeAncestryAsync(bool protectedAncestry)
        {
            NodeId node = m_model.AddNode(new NodeId("vendor-instance", 1));
            NodeId vendorType = m_model.AddNode(new NodeId("vendor-role-subtype", 4));
            m_model.AddTypeDefinition(node, vendorType);
            if (protectedAncestry)
            {
                m_model.AddAncestry(vendorType, ObjectTypeIds.RoleType);
                Allow(McpCapability.IdentityAdministration);
            }
            else
            {
                m_model.AddAncestry(vendorType, ObjectTypeIds.BaseObjectType);
            }

            await m_guard.AuthorizeTargetAsync(
                m_model.SessionInfo, node, true, m_cancellation.Token).ConfigureAwait(false);

            m_model.NodeCache.Verify(cache => cache.IsTypeOfAsync(
                vendorType, ObjectTypeIds.RoleType, m_cancellation.Token), Times.Once);
            Assert.That(m_model.CacheQueries.Select(query => query.Actual), Is.All.EqualTo(vendorType));
            Assert.That(m_model.CacheQueries, Has.Count.EqualTo(protectedAncestry ? 2 : 19));
            Assert.That(m_model.CacheQueries.Count(query => query.Matches), Is.EqualTo(protectedAncestry ? 1 : 0));
            Assert.That(m_model.Browses, Has.Count.EqualTo(protectedAncestry ? 1 : 4));
            if (protectedAncestry)
            {
                VerifyAuthority(McpCapability.IdentityAdministration, Times.Once());
            }
            else
            {
                m_policy.VerifyNoOtherCalls();
                AssertQuery(vendorType, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse);
                AssertOrdinaryQueries([node]);
            }
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public async Task AuthorizeTargetUsesInverseSubtypeAncestryAsync(
            bool throughTypeDefinition, bool gds, bool genericCall)
        {
            NodeId node = m_model.AddNode(new NodeId("subtype-instance", 1));
            NodeId actual = throughTypeDefinition
                ? m_model.AddNode(new NodeId("vendor-subtype", 4))
                : node;
            if (throughTypeDefinition)
            {
                m_model.AddTypeDefinition(node, actual);
            }
            NodeId supertype = gds ? new NodeId("gds-base", 2) : ObjectTypeIds.RoleType;
            m_model.AddSubtype(actual, supertype);
            McpCapability capability = gds
                ? McpCapability.DirectoryAdministration
                : McpCapability.IdentityAdministration;
            Allow(capability);

            if (gds)
            {
                await AssertStatusAsync(
                    () => m_guard.AuthorizeTargetAsync(
                        m_model.SessionInfo, node, genericCall, m_cancellation.Token),
                    StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
            }
            else
            {
                await m_guard.AuthorizeTargetAsync(
                    m_model.SessionInfo, node, genericCall, m_cancellation.Token).ConfigureAwait(false);
            }

            VerifyAuthority(capability, Times.Once());
            AssertQuery(actual, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse);
            Assert.That(m_model.Browses, Has.Count.EqualTo(throughTypeDefinition ? 4 : 3));
            Assert.That(m_model.CacheQueries, Has.Count.EqualTo(throughTypeDefinition ? 19 : 0));
            Assert.That(m_model.CacheQueries.Select(query => query.Matches), Is.All.False);
        }

        [TestCase("HasComponent", true)]
        [TestCase("HasProperty", true)]
        [TestCase("Organizes", true)]
        [TestCase("CustomAggregates", true)]
        [TestCase("Unrelated", false)]
        public async Task AuthorizeTargetFollowsOnlyRealOwnershipAsync(string relation, bool owning)
        {
            NodeId node = m_model.AddNode(new NodeId("owned-method", 1));
            NodeId referenceType = relation switch
            {
                "HasComponent" => ReferenceTypeIds.HasComponent,
                "HasProperty" => ReferenceTypeIds.HasProperty,
                "Organizes" => ReferenceTypeIds.Organizes,
                "CustomAggregates" => new NodeId("vendor-ownership", 4),
                _ => ReferenceTypeIds.HasNotifier
            };
            m_model.AddOwner(node, ObjectTypeIds.RoleType, referenceType);
            if (relation == "CustomAggregates")
            {
                m_model.AddAncestry(referenceType, ReferenceTypeIds.Aggregates);
            }
            if (owning)
            {
                Allow(McpCapability.IdentityAdministration);
            }

            await m_guard.AuthorizeTargetAsync(
                m_model.SessionInfo, node, true, m_cancellation.Token).ConfigureAwait(false);

            if (owning)
            {
                VerifyAuthority(McpCapability.IdentityAdministration, Times.Once());
            }
            else
            {
                m_policy.VerifyNoOtherCalls();
            }
            AssertOrdinaryQueries([node]);
            Assert.That(m_model.Browses, Has.Count.EqualTo(3));
            bool cacheRequired = relation is "CustomAggregates" or "Unrelated";
            m_model.NodeCache.Verify(cache => cache.IsTypeOfAsync(
                referenceType, ReferenceTypeIds.Aggregates, m_cancellation.Token),
                Times.Exactly(cacheRequired ? 1 : 0));
            Assert.That(m_model.CacheQueries, Has.Count.EqualTo(cacheRequired ? 1 : 0));
        }

        [Test]
        public async Task AuthorizeTargetIgnoresNonOwningRemoteReferenceAsync()
        {
            NodeId node = m_model.AddNode(new NodeId("ordinary", 1));
            var remote = new ExpandedNodeId(ObjectTypeIds.RoleType, "urn:test:unadvertised", 1);
            m_model.AddOwner(node, remote, ReferenceTypeIds.HasNotifier);
            string[] originalNamespaces = m_model.NamespaceUris.ToArray();

            await m_guard.AuthorizeTargetAsync(
                m_model.SessionInfo, node, false, m_cancellation.Token).ConfigureAwait(false);

            m_policy.VerifyNoOtherCalls();
            AssertOrdinaryQueries([node]);
            Assert.That(m_model.Browses, Has.Count.EqualTo(3));
            Assert.That(ReturnedReferences(), Is.EqualTo(1));
            m_model.NodeCache.Verify(cache => cache.IsTypeOfAsync(
                ReferenceTypeIds.HasNotifier, ReferenceTypeIds.Aggregates, m_cancellation.Token), Times.Once);
            Assert.That(m_model.NamespaceUris.ToArray(), Is.EqualTo(originalNamespaces));
        }

        [TestCaseSource(nameof(InvalidReferenceCases))]
        public async Task AuthorizeTargetRejectsInvalidModelReferencesAsync(string edge, string invalidKind)
        {
            NodeId node = m_model.AddNode(new NodeId("target", 1));
            ExpandedNodeId invalid = invalidKind switch
            {
                "UnknownNamespace" => new ExpandedNodeId(new NodeId("missing", 0), "urn:test:not-advertised"),
                "Remote" => new ExpandedNodeId(ObjectTypeIds.RoleType, serverIndex: 1),
                _ => default
            };
            int expectedQueries;
            switch (edge)
            {
                case "TypeDefinition":
                    m_model.AddTypeDefinition(node, invalid);
                    expectedQueries = 1;
                    break;
                case "CurrentSupertype":
                    m_model.AddSubtype(node, invalid);
                    expectedQueries = 2;
                    break;
                case "TypeSupertype":
                    NodeId type = m_model.AddNode(new NodeId("vendor-type", 4));
                    m_model.AddTypeDefinition(node, type);
                    m_model.AddSubtype(type, invalid);
                    expectedQueries = 2;
                    break;
                default:
                    m_model.AddOwner(node, invalid, ReferenceTypeIds.HasComponent);
                    expectedQueries = 3;
                    break;
            }
            string[] originalNamespaces = m_model.NamespaceUris.ToArray();

            await AssertStatusAsync(
                () => m_guard.AuthorizeTargetAsync(
                    m_model.SessionInfo, node, false, m_cancellation.Token),
                StatusCodes.BadNotSupported).ConfigureAwait(false);

            m_policy.VerifyNoOtherCalls();
            Assert.That(m_model.Browses, Has.Count.EqualTo(expectedQueries));
            Assert.That(m_model.CacheQueries, Has.Count.EqualTo(edge == "TypeSupertype" ? 19 : 0));
            Assert.That(m_model.NamespaceUris.ToArray(), Is.EqualTo(originalNamespaces));
            Assert.That(m_model.Browses[^1].Result.References[^1].NodeId, Is.EqualTo(invalid));
        }

        [TestCase("Unknown")]
        [TestCase("ExplicitUnknown")]
        [TestCase("TypeDefinition")]
        [TestCase("Subtype")]
        [TestCase("Owner")]
        public async Task AuthorizeTargetPropagatesUnknownAndBadMetadataAsync(string failureAt)
        {
            var node = new NodeId("unreadable", 1);
            if (failureAt is not ("Unknown" or "ExplicitUnknown"))
            {
                m_model.AddNode(node);
            }
            NodeId referenceType = failureAt switch
            {
                "Subtype" => ReferenceTypeIds.HasSubtype,
                "Owner" => ReferenceTypeIds.HierarchicalReferences,
                _ => ReferenceTypeIds.HasTypeDefinition
            };
            BrowseDirection direction = failureAt is "Subtype" or "Owner"
                ? BrowseDirection.Inverse
                : BrowseDirection.Forward;
            if (failureAt != "Unknown")
            {
                m_model.SetBrowseFailure(node, referenceType, direction, StatusCodes.BadNodeIdUnknown);
            }

            await AssertStatusAsync(
                () => m_guard.AuthorizeTargetAsync(
                    m_model.SessionInfo, node, false, m_cancellation.Token),
                StatusCodes.BadNodeIdUnknown).ConfigureAwait(false);

            m_policy.VerifyNoOtherCalls();
            AssertQuery(node, referenceType, direction);
            Assert.That(m_model.Browses, Has.Count.EqualTo(failureAt switch
            {
                "Subtype" => 2,
                "Owner" => 3,
                _ => 1
            }));
            Assert.That(m_model.Browses[^1].Result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown.Code));
            Assert.That(m_model.CacheQueries, Is.Empty);
        }

        [Test]
        public async Task AuthorizeTargetRejectsNullTargetAsync()
        {
            await AssertStatusAsync(
                () => m_guard.AuthorizeTargetAsync(
                    m_model.SessionInfo, default, false, m_cancellation.Token),
                StatusCodes.BadNodeIdInvalid).ConfigureAwait(false);

            AssertNoModelAccess();
            m_policy.VerifyNoOtherCalls();
        }

        [TestCase("Self")]
        [TestCase("TwoNode")]
        [TestCase("Diamond")]
        public async Task AuthorizeTargetSuppressesCyclesAndDuplicateVisitsAsync(string shape)
        {
            NodeId root = m_model.AddNode(new NodeId("root", 1));
            NodeId[] visited;
            int edgeCount;
            if (shape == "Self")
            {
                m_model.AddOwner(root, root, ReferenceTypeIds.HasComponent);
                visited = [root];
                edgeCount = 1;
            }
            else if (shape == "TwoNode")
            {
                NodeId other = m_model.AddNode(new NodeId("other", 1));
                m_model.AddOwner(root, other, ReferenceTypeIds.HasComponent);
                m_model.AddOwner(other, root, ReferenceTypeIds.HasProperty);
                visited = [root, other];
                edgeCount = 2;
            }
            else
            {
                NodeId left = m_model.AddNode(new NodeId("left", 1));
                NodeId right = m_model.AddNode(new NodeId("right", 1));
                NodeId shared = m_model.AddNode(new NodeId("shared", 1));
                m_model.AddOwner(root, left, ReferenceTypeIds.HasComponent);
                m_model.AddOwner(root, left, ReferenceTypeIds.HasComponent);
                m_model.AddOwner(root, right, ReferenceTypeIds.HasComponent);
                m_model.AddOwner(left, shared, ReferenceTypeIds.HasProperty);
                m_model.AddOwner(right, shared, ReferenceTypeIds.Organizes);
                visited = [root, left, right, shared];
                edgeCount = 5;
            }

            await m_guard.AuthorizeTargetAsync(
                m_model.SessionInfo, root, false, m_cancellation.Token).ConfigureAwait(false);

            m_policy.VerifyNoOtherCalls();
            AssertOrdinaryQueries(visited);
            Assert.That(m_model.Browses, Has.Count.EqualTo(3 * visited.Length));
            Assert.That(m_model.Browses.Select(query => query.Request.NodeId).Distinct(),
                Is.EquivalentTo(visited));
            Assert.That(ReturnedReferences(), Is.EqualTo(edgeCount));
            Assert.That(m_model.CacheQueries, Is.Empty);
        }

        [TestCaseSource(nameof(BudgetCases))]
        public async Task AuthorizeTargetEnforcesIndependentGraphBudgetsAsync(
            string budget, int amount, string edgeKind, bool accepted)
        {
            NodeId[] nodes;
            if (budget == "Nodes")
            {
                nodes = AddWideGraph(amount, 10000);
                Assert.That(nodes.Length - 1, Is.LessThan(512), "The wide graph has depth at most one.");
            }
            else if (budget == "Depth")
            {
                nodes = AddDepthGraph(amount, 20000);
                Allow(McpCapability.IdentityAdministration);
                Assert.That(nodes, Has.Length.LessThan(128));
                Assert.That(amount + 1, Is.LessThan(512), "Includes the terminal type-definition reference.");
            }
            else
            {
                nodes = AddReferenceGraph(amount, 30000, edgeKind);
                Assert.That(nodes, Has.Length.LessThanOrEqualTo(2), "Repeated edges have depth at most one.");
            }

            if (accepted)
            {
                await m_guard.AuthorizeTargetAsync(
                    m_model.SessionInfo, nodes[0], false, m_cancellation.Token).ConfigureAwait(false);
            }
            else
            {
                await AssertStatusAsync(
                    () => m_guard.AuthorizeTargetAsync(
                        m_model.SessionInfo, nodes[0], false, m_cancellation.Token),
                    StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
            }

            if (budget == "Nodes")
            {
                int visited = Math.Min(amount, 128);
                AssertOrdinaryQueries(nodes.Take(visited));
                Assert.That(m_model.Browses, Has.Count.EqualTo(3 * visited));
                Assert.That(ReturnedReferences(), Is.EqualTo(amount - 1));
                Assert.That(m_model.CacheQueries, Is.Empty);
                Assert.That(m_model.Browses.Select(query => query.Request.NodeId).Distinct().ToArray(),
                    Has.Length.EqualTo(visited));
            }
            else if (budget == "Depth")
            {
                if (accepted)
                {
                    AssertOrdinaryQueries(nodes.Take(amount));
                    AssertQuery(nodes[^1], ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward);
                    Assert.That(m_model.Browses, Has.Count.EqualTo((3 * amount) + 1));
                    Assert.That(ReturnedReferences(), Is.EqualTo(amount + 1));
                    VerifyAuthority(McpCapability.IdentityAdministration, Times.Once());
                }
                else
                {
                    AssertOrdinaryQueries(nodes.Take(17));
                    Assert.That(m_model.Browses, Has.Count.EqualTo(51));
                    Assert.That(ReturnedReferences(), Is.EqualTo(17));
                    Assert.That(m_model.Browses.Any(query => query.Request.NodeId == nodes[17]), Is.False);
                }
            }
            else
            {
                NodeId[] visited = accepted ? nodes : [nodes[0]];
                AssertOrdinaryQueries(visited);
                Assert.That(m_model.Browses, Has.Count.EqualTo(3 * visited.Length));
                Assert.That(ReturnedReferences(), Is.EqualTo(amount), "Duplicate and rejected edges are supplied.");
                int examined = Math.Min(amount, 512);
                Assert.That(m_model.CacheQueries, Has.Count.EqualTo(edgeKind == "Rejected" ? examined : 0));
                if (edgeKind == "Rejected")
                {
                    m_model.NodeCache.Verify(cache => cache.IsTypeOfAsync(
                        ReferenceTypeIds.HasNotifier, ReferenceTypeIds.Aggregates, m_cancellation.Token),
                        Times.Exactly(examined));
                }
            }
            if (budget != "Depth" || !accepted)
            {
                m_policy.VerifyNoOtherCalls();
            }
        }

        [TestCase(512)]
        [TestCase(513)]
        public async Task AuthorizeTargetCountsReferencesAcrossContinuationPagesAsync(int count)
        {
            NodeId root = m_model.AddNode(new NodeId("paged-root", 1));
            ReferenceDescription[] references = RepeatedReferences(
                count, ObjectTypeIds.RoleType, ReferenceTypeIds.HasNotifier);
            ByteString[] points =
                [.. Enumerable.Range(1, 6).Select(index => new ByteString(new byte[] { (byte)index, 0xA5 }))];
            m_model.SetBrowsePage(root, ReferenceTypeIds.HierarchicalReferences, BrowseDirection.Inverse,
                references[..100], points[0]);
            for (int page = 1; page <= 5; page++)
            {
                int start = page * 100;
                int end = Math.Min(start + 100, count);
                ByteString next = page < 5 || count == 513 ? points[page] : default;
                m_model.SetContinuationPage(points[page - 1], references[start..end], next);
            }
            if (count == 513)
            {
                // The over-budget page still owns a continuation; it must be released, not consumed.
                m_model.SetContinuationPage(points[5], []);
                await AssertStatusAsync(
                    () => m_guard.AuthorizeTargetAsync(
                        m_model.SessionInfo, root, false, m_cancellation.Token),
                    StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
            }
            else
            {
                await m_guard.AuthorizeTargetAsync(
                    m_model.SessionInfo, root, false, m_cancellation.Token).ConfigureAwait(false);
            }

            m_policy.VerifyNoOtherCalls();
            AssertOrdinaryQueries([root]);
            Assert.That(m_model.Browses, Has.Count.EqualTo(3));
            Assert.That(m_model.Browses.Select(query => query.Request.NodeId).Distinct(), Is.EqualTo([root]));
            Assert.That(ReturnedReferences(), Is.EqualTo(count));
            Assert.That(m_model.CacheQueries, Has.Count.EqualTo(512));
            Assert.That(m_model.Continuations.Where(query => !query.Release).Select(query => query.Point),
                Is.EqualTo(points.Take(5)));
            Assert.That(m_model.Continuations.Count(query => query.Release), Is.EqualTo(count == 513 ? 1 : 0));
            m_model.Session.Verify(session => session.BrowseNextAsync(
                null, true,
                It.Is<ArrayOf<ByteString>>(tokens => tokens.Count == 1 && tokens[0] == points[5]),
                CancellationToken.None), Times.Exactly(count == 513 ? 1 : 0));
            if (count == 513)
            {
                Assert.That(m_model.Continuations[^1].Point, Is.EqualTo(points[5]));
                Assert.That(m_model.Continuations[^1].Release, Is.True);
            }
        }

        [TestCase(false, 512)]
        [TestCase(false, 513)]
        [TestCase(true, 512)]
        [TestCase(true, 513)]
        public async Task AuthorizeTargetSharesReferenceBudgetAcrossModelEdgesAsync(
            bool throughTypeDefinition, int count)
        {
            NodeId root = m_model.AddNode(new NodeId("subtype-budget-root", 1));
            NodeId type = throughTypeDefinition
                ? m_model.AddNode(new NodeId("vendor-type", 4))
                : root;
            NodeId supertype = throughTypeDefinition
                ? m_model.AddNode(new NodeId("ordinary-supertype", 4))
                : root;
            int typeReferences = throughTypeDefinition ? 1 : 0;
            if (throughTypeDefinition)
            {
                m_model.AddTypeDefinition(root, type);
            }
            m_model.SetBrowsePage(type, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse,
                RepeatedReferences(count - typeReferences, supertype, ReferenceTypeIds.HasSubtype,
                    NodeClass.ObjectType));

            if (count == 513)
            {
                await AssertStatusAsync(
                    () => m_guard.AuthorizeTargetAsync(
                        m_model.SessionInfo, root, false, m_cancellation.Token),
                    StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
            }
            else
            {
                await m_guard.AuthorizeTargetAsync(
                    m_model.SessionInfo, root, false, m_cancellation.Token).ConfigureAwait(false);
            }

            m_policy.VerifyNoOtherCalls();
            Assert.That(ReturnedReferences(), Is.EqualTo(count));
            Assert.That(m_model.KnownNodes, Has.Count.EqualTo(throughTypeDefinition ? 3 : 1));
            Assert.That(m_model.CacheQueries, Has.Count.EqualTo(throughTypeDefinition ? 19 : 0));
            AssertQuery(type, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse);
            Assert.That(m_model.Browses,
                Has.Count.EqualTo(count == 513 ? 2 : throughTypeDefinition ? 7 : 3));
            if (count == 512)
            {
                AssertOrdinaryQueries(throughTypeDefinition ? [root, supertype] : [root]);
            }
        }

        [TestCase("Nodes")]
        [TestCase("Depth")]
        [TestCase("References")]
        public async Task AuthorizeBatchResetsBudgetsForEachTargetAsync(string budget)
        {
            NodeId[] first;
            NodeId[] second;
            if (budget == "Nodes")
            {
                first = AddWideGraph(128, 10000);
                second = AddWideGraph(128, 20000);
            }
            else if (budget == "Depth")
            {
                first = AddDepthGraph(16, 10000);
                second = AddDepthGraph(16, 20000);
                Allow(McpCapability.IdentityAdministration);
            }
            else
            {
                first = AddReferenceGraph(512, 10000, "Rejected");
                second = AddReferenceGraph(512, 20000, "Rejected");
            }

            await m_guard.AuthorizeNodesAsync(m_model.SessionInfo, [first[0], second[0]], m_cancellation.Token)
                .ConfigureAwait(false);

            if (budget == "Depth")
            {
                VerifyAuthority(McpCapability.IdentityAdministration, Times.Exactly(2));
                AssertOrdinaryQueries(first.Take(16).Concat(second.Take(16)));
                AssertQuery(first[^1], ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward);
                AssertQuery(second[^1], ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward);
                Assert.That(m_model.Browses, Has.Count.EqualTo(98));
                Assert.That(ReturnedReferences(), Is.EqualTo(34));
            }
            else
            {
                m_policy.VerifyNoOtherCalls();
                AssertOrdinaryQueries(first.Concat(second));
                Assert.That(m_model.Browses, Has.Count.EqualTo(budget == "Nodes" ? 768 : 6));
                Assert.That(ReturnedReferences(), Is.EqualTo(budget == "Nodes" ? 254 : 1024));
            }
            Assert.That(m_model.Browses.Select(query => query.Request.NodeId).Distinct(),
                Is.EquivalentTo(first.Concat(second)));
            Assert.That(m_model.CacheQueries, Has.Count.EqualTo(budget switch
            {
                "Depth" => 2,
                "References" => 1024,
                _ => 0
            }));
        }

        [TestCase(true, false)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        [TestCase(false, true)]
        public async Task AuthorizeTargetCannotIgnoreGdsOwnerAfterGrantedRoleOwnerAsync(
            bool roleFirst, bool genericCall)
        {
            NodeId child = m_model.AddNode(new NodeId("shared-owned-method", 1));
            var directory = new NodeId("directory-owner", 2);
            NodeId[] owners = roleFirst
                ? [ObjectTypeIds.RoleType, directory]
                : [directory, ObjectTypeIds.RoleType];
            foreach (NodeId owner in owners)
            {
                m_model.AddOwner(child, owner, ReferenceTypeIds.HasComponent);
            }
            Allow(McpCapability.IdentityAdministration);
            Deny(McpCapability.DirectoryAdministration);

            // G02: a granted first owner must not hide a denied, typed-only second owner.
            await AssertStatusAsync(
                () => m_guard.AuthorizeTargetAsync(
                    m_model.SessionInfo, child, genericCall, m_cancellation.Token),
                StatusCodes.BadUserAccessDenied).ConfigureAwait(false);

            m_policy.Verify(policy => policy.RequireSession(McpCapability.DirectoryAdministration, "Prod-A"),
                Times.Once);
            m_policy.Verify(policy => policy.RequireSession(McpCapability.IdentityAdministration, "Prod-A"),
                Times.AtMostOnce);
            m_policy.VerifyNoOtherCalls();
            AssertOrdinaryQueries([child]);
            Assert.That(m_model.Browses, Has.Count.EqualTo(3));
            Assert.That(m_model.Browses[^1].Result.References.ConvertAll(reference => reference.NodeId).ToArray(),
                Is.EqualTo(owners.Select(owner => new ExpandedNodeId(owner))));
        }

        [TestCase("Target")]
        [TestCase("Calls")]
        [TestCase("Writes")]
        [TestCase("Nodes")]
        public async Task NonEmptyAuthorizationHonorsPreCancellationAsync(string api)
        {
            NodeId node = m_model.AddNode(new NodeId("ordinary", 1));
            await m_cancellation.CancelAsync().ConfigureAwait(false);
            CancellationToken token = m_cancellation.Token;

            await AssertCancellationAsync(() => api switch
            {
                "Target" => m_guard.AuthorizeTargetAsync(m_model.SessionInfo, node, false, token),
                "Calls" => m_guard.AuthorizeCallsAsync(m_model.SessionInfo,
                    [new CallMethodRequest { ObjectId = node, MethodId = node }], token),
                "Writes" => m_guard.AuthorizeWritesAsync(m_model.SessionInfo,
                    [new WriteValue { NodeId = node, AttributeId = Attributes.Value }], token),
                _ => m_guard.AuthorizeNodesAsync(m_model.SessionInfo, [node], token)
            }, token).ConfigureAwait(false);

            AssertNoModelAccess();
            m_policy.VerifyNoOtherCalls();
        }

        [TestCase("Calls", false)]
        [TestCase("Calls", true)]
        [TestCase("Writes", false)]
        [TestCase("Writes", true)]
        [TestCase("Nodes", false)]
        [TestCase("Nodes", true)]
        public async Task EmptyAuthorizationHonorsPreCancellationAsync(string api, bool defaultArray)
        {
            await m_cancellation.CancelAsync().ConfigureAwait(false);
            CancellationToken token = m_cancellation.Token;

            // G03: both explicit empty and default arrays must refuse before authority or model access.
            await AssertCancellationAsync(() => api switch
            {
                "Calls" => m_guard.AuthorizeCallsAsync(
                    m_model.SessionInfo, defaultArray ? default : [], token),
                "Writes" => m_guard.AuthorizeWritesAsync(
                    m_model.SessionInfo, defaultArray ? default : [], token),
                _ => m_guard.AuthorizeNodesAsync(m_model.SessionInfo, defaultArray ? default : [], token)
            }, token).ConfigureAwait(false);

            AssertNoModelAccess();
            m_policy.VerifyNoOtherCalls();
        }

        [TestCase("Calls", false)]
        [TestCase("Calls", true)]
        [TestCase("Writes", false)]
        [TestCase("Writes", true)]
        [TestCase("Nodes", false)]
        [TestCase("Nodes", true)]
        public async Task EmptyAuthorizationWithoutCancellationNeedsNoModelAsync(string api, bool defaultArray)
        {
            CancellationToken token = m_cancellation.Token;
            switch (api)
            {
                case "Calls":
                    await m_guard.AuthorizeCallsAsync(m_model.SessionInfo, defaultArray ? default : [], token)
                        .ConfigureAwait(false);
                    break;
                case "Writes":
                    await m_guard.AuthorizeWritesAsync(m_model.SessionInfo, defaultArray ? default : [], token)
                        .ConfigureAwait(false);
                    break;
                default:
                    await m_guard.AuthorizeNodesAsync(m_model.SessionInfo, defaultArray ? default : [], token)
                        .ConfigureAwait(false);
                    break;
            }

            AssertNoModelAccess();
            m_policy.VerifyNoOtherCalls();
        }

        [TestCase(Attributes.RolePermissions)]
        [TestCase(Attributes.UserRolePermissions)]
        [TestCase(Attributes.AccessRestrictions)]
        [TestCase(Attributes.UserAccessLevel)]
        [TestCase(Attributes.UserExecutable)]
        [TestCase(Attributes.UserWriteMask)]
        public async Task PermissionWritePreCancellationPrecedesAuthorityAsync(uint attribute)
        {
            NodeId node = m_model.AddNode(new NodeId("permission-target", 1));
            Deny(McpCapability.IdentityAdministration);
            await m_cancellation.CancelAsync().ConfigureAwait(false);
            CancellationToken token = m_cancellation.Token;

            // G03: a policy refusal is not an acceptable substitute for observing pre-cancellation.
            await AssertCancellationAsync(
                () => m_guard.AuthorizeWritesAsync(m_model.SessionInfo,
                    [new WriteValue { NodeId = node, AttributeId = attribute }], token),
                token).ConfigureAwait(false);

            m_policy.Verify(policy => policy.RequireSession(McpCapability.IdentityAdministration, "Prod-A"),
                Times.Never);
            m_policy.VerifyNoOtherCalls();
            AssertNoModelAccess();
        }

        [TestCase("Target")]
        [TestCase("Calls")]
        [TestCase("Writes")]
        [TestCase("Nodes")]
        public async Task NullSessionIsRejectedBeforeMetadataAsync(string api)
        {
            NodeId node = m_model.AddNode(new NodeId("ordinary", 1));
            await AssertArgumentNullAsync(() => api switch
            {
                "Target" => m_guard.AuthorizeTargetAsync(null!, node, false, m_cancellation.Token),
                "Calls" => m_guard.AuthorizeCallsAsync(null!, [], m_cancellation.Token),
                "Writes" => m_guard.AuthorizeWritesAsync(null!, [], m_cancellation.Token),
                _ => m_guard.AuthorizeNodesAsync(null!, [], m_cancellation.Token)
            }, "session").ConfigureAwait(false);

            AssertNoModelAccess();
            m_policy.VerifyNoOtherCalls();
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task NullBatchRequestIsRejectedBeforeModelAsync(bool calls)
        {
            await AssertArgumentNullAsync(
                () => calls
                    ? m_guard.AuthorizeCallsAsync(m_model.SessionInfo, [null!], m_cancellation.Token)
                    : m_guard.AuthorizeWritesAsync(m_model.SessionInfo, [null!], m_cancellation.Token),
                "request").ConfigureAwait(false);

            AssertNoModelAccess();
            m_policy.VerifyNoOtherCalls();
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task AuthorizeNodesRejectsNullEntryAfterOrdinaryChecksAsync(bool nullFirst)
        {
            NodeId ordinary = m_model.AddNode(new NodeId("ordinary", 1));
            ArrayOf<NodeId> nodes = nullFirst ? [default, ordinary] : [ordinary, default];

            await AssertStatusAsync(
                () => m_guard.AuthorizeNodesAsync(m_model.SessionInfo, nodes, m_cancellation.Token),
                StatusCodes.BadNodeIdInvalid).ConfigureAwait(false);

            m_policy.VerifyNoOtherCalls();
            Assert.That(m_model.Browses, Has.Count.EqualTo(nullFirst ? 0 : 3));
            if (nullFirst)
            {
                AssertNoModelAccess();
            }
            else
            {
                AssertOrdinaryQueries([ordinary]);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task AuthorizeCallsRejectsNullObjectOrMethodTargetAsync(bool nullObject)
        {
            NodeId ordinary = m_model.AddNode(new NodeId("ordinary", 1));
            var request = new CallMethodRequest
            {
                ObjectId = nullObject ? default : ordinary,
                MethodId = nullObject ? ordinary : default
            };

            await AssertStatusAsync(
                () => m_guard.AuthorizeCallsAsync(m_model.SessionInfo, [request], m_cancellation.Token),
                StatusCodes.BadNodeIdInvalid).ConfigureAwait(false);

            m_policy.VerifyNoOtherCalls();
            Assert.That(m_model.Browses, Has.Count.EqualTo(nullObject ? 0 : 3));
            if (nullObject)
            {
                AssertNoModelAccess();
            }
            else
            {
                AssertOrdinaryQueries([ordinary]);
            }
        }

        [Test]
        public async Task AuthorizeWritesRejectsNullTargetAsync()
        {
            await AssertStatusAsync(
                () => m_guard.AuthorizeWritesAsync(m_model.SessionInfo,
                    [new WriteValue { NodeId = default, AttributeId = Attributes.Value }], m_cancellation.Token),
                StatusCodes.BadNodeIdInvalid).ConfigureAwait(false);

            AssertNoModelAccess();
            m_policy.VerifyNoOtherCalls();
        }

        public static IEnumerable<TestCaseData> CapabilityCases()
        {
            (string Name, NodeId Type, string? Uri, McpCapability Capability, bool Call, bool Mutation)[] rows =
            [
                ("Gds", default, McpAuthorizationModel.GdsNamespaceUri,
                    McpCapability.DirectoryAdministration, true, true),
                ("Onboarding", default, McpAuthorizationModel.OnboardingNamespaceUri,
                    McpCapability.Onboarding, true, true),
                ("RoleSetType", ObjectTypeIds.RoleSetType, null, McpCapability.IdentityAdministration, false, false),
                ("RoleType", ObjectTypeIds.RoleType, null, McpCapability.IdentityAdministration, false, false),
                ("NamespaceMetadataType", ObjectTypeIds.NamespaceMetadataType, null,
                    McpCapability.IdentityAdministration, false, false),
                ("UserManagementType", ObjectTypeIds.UserManagementType, null,
                    McpCapability.IdentityAdministration, false, false),
                ("SecurityGroupType", ObjectTypeIds.SecurityGroupType, null, McpCapability.PubSubSecurity, true, false),
                ("SecurityGroupFolderType", ObjectTypeIds.SecurityGroupFolderType, null,
                    McpCapability.PubSubSecurity, true, false),
                ("PubSubKeyServiceType", ObjectTypeIds.PubSubKeyServiceType, null,
                    McpCapability.PubSubSecurity, true, false),
                ("TrustListType", ObjectTypeIds.TrustListType, null,
                    McpCapability.CertificateAdministration, false, false),
                ("CertificateGroupType", ObjectTypeIds.CertificateGroupType, null,
                    McpCapability.CertificateAdministration, false, false),
                ("CertificateGroupFolderType", ObjectTypeIds.CertificateGroupFolderType, null,
                    McpCapability.CertificateAdministration, false, false),
                ("ServerConfigurationType", ObjectTypeIds.ServerConfigurationType, null,
                    McpCapability.CertificateAdministration, false, false),
                ("PublishSubscribeType", ObjectTypeIds.PublishSubscribeType, null,
                    McpCapability.PubSubConfiguration, true, false),
                ("PubSubConnectionType", ObjectTypeIds.PubSubConnectionType, null,
                    McpCapability.PubSubConfiguration, false, false),
                ("WriterGroupType", ObjectTypeIds.WriterGroupType, null,
                    McpCapability.PubSubConfiguration, false, false),
                ("ReaderGroupType", ObjectTypeIds.ReaderGroupType, null,
                    McpCapability.PubSubConfiguration, false, false),
                ("DataSetWriterType", ObjectTypeIds.DataSetWriterType, null,
                    McpCapability.PubSubConfiguration, false, false),
                ("DataSetReaderType", ObjectTypeIds.DataSetReaderType, null,
                    McpCapability.PubSubConfiguration, false, false),
                ("PubSubConfigurationType", ObjectTypeIds.PubSubConfigurationType, null,
                    McpCapability.PubSubConfiguration, false, false),
                ("DataSetFolderType", ObjectTypeIds.DataSetFolderType, null,
                    McpCapability.PubSubConfiguration, false, false)
            ];
            foreach ((
                string Name, NodeId Type, string? Uri, McpCapability Capability, bool Call, bool Mutation) row in rows)
            {
                foreach (bool genericCall in new[] { false, true })
                {
                    yield return new TestCaseData(
                        row.Type, row.Uri, row.Capability, row.Call, row.Mutation, genericCall)
                        .SetName($"{nameof(AuthorizeTargetSelectsExactCapabilityAsync)}({row.Name},{genericCall})");
                }
            }
        }

        public static IEnumerable<TestCaseData> InvalidReferenceCases()
        {
            foreach (string edge in new[] { "TypeDefinition", "CurrentSupertype", "TypeSupertype", "Owner" })
            {
                foreach (string invalid in new[] { "UnknownNamespace", "Remote", "Null" })
                {
                    yield return new TestCaseData(edge, invalid)
                        .SetName($"{nameof(AuthorizeTargetRejectsInvalidModelReferencesAsync)}({edge},{invalid})");
                }
            }
        }

        public static IEnumerable<TestCaseData> BudgetCases()
        {
            foreach (int count in new[] { 1, 2, 64, 127, 128, 129, 130 })
            {
                yield return new TestCaseData("Nodes", count, "Wide", count <= 128)
                    .SetName($"{nameof(AuthorizeTargetEnforcesIndependentGraphBudgetsAsync)}(Nodes,{count})");
            }
            foreach (int depth in new[] { 0, 1, 8, 15, 16, 17, 18 })
            {
                yield return new TestCaseData("Depth", depth, "Chain", depth <= 16)
                    .SetName($"{nameof(AuthorizeTargetEnforcesIndependentGraphBudgetsAsync)}(Depth,{depth})");
            }
            foreach (string edge in new[] { "Rejected", "Duplicate", "Self" })
            {
                foreach (int references in new[] { 0, 1, 256, 511, 512, 513, 514 })
                {
                    yield return new TestCaseData("References", references, edge, references <= 512)
                        .SetName(
                            nameof(AuthorizeTargetEnforcesIndependentGraphBudgetsAsync) +
                            $"(References,{edge},{references})");
                }
            }
        }

        private void Allow(McpCapability capability)
        {
            m_policy.Setup(policy => policy.RequireSession(capability, "Prod-A")).Returns(m_caller);
        }

        private void Deny(McpCapability capability)
        {
            m_policy.Setup(policy => policy.RequireSession(capability, "Prod-A"))
                .Throws(new ServiceResultException(StatusCodes.BadUserAccessDenied, "Host grant refused."));
        }

        private void VerifyAuthority(McpCapability capability, Times times)
        {
            m_policy.Verify(policy => policy.RequireSession(capability, "Prod-A"), times);
            m_policy.VerifyNoOtherCalls();
        }

        private void AssertNoModelAccess()
        {
            Assert.That(m_model.Browses, Is.Empty);
            Assert.That(m_model.Continuations, Is.Empty);
            Assert.That(m_model.CacheQueries, Is.Empty);
            Assert.That(m_model.Session.Invocations, Is.Empty);
            Assert.That(m_model.NodeCache.Invocations, Is.Empty);
        }

        private void AssertQuery(NodeId node, NodeId referenceType, BrowseDirection direction)
        {
            Assert.That(m_model.Browses.Count(query =>
                query.Request.NodeId == node &&
                query.Request.ReferenceTypeId == referenceType &&
                query.Request.BrowseDirection == direction), Is.EqualTo(1));
        }

        private void AssertOrdinaryQueries(IEnumerable<NodeId> nodes)
        {
            foreach (NodeId node in nodes)
            {
                AssertQuery(node, ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward);
                AssertQuery(node, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse);
                AssertQuery(node, ReferenceTypeIds.HierarchicalReferences, BrowseDirection.Inverse);
            }
        }

        private void AssertWireArguments()
        {
            foreach (McpAuthorizationModel.BrowseQuery query in m_model.Browses)
            {
                Assert.That(query.Header, Is.Null);
                Assert.That(query.View, Is.Null);
                Assert.That(query.Maximum, Is.EqualTo(100u));
                Assert.That(query.Request.IncludeSubtypes, Is.True);
                Assert.That(query.Request.NodeClassMask, Is.Zero);
                Assert.That(query.Request.ResultMask, Is.EqualTo((uint)BrowseResultMask.All));
                Assert.That(query.Token, Is.EqualTo(m_cancellation.Token));
            }
            foreach (McpAuthorizationModel.CacheQuery query in m_model.CacheQueries)
            {
                Assert.That(query.Token, Is.EqualTo(m_cancellation.Token));
            }
            foreach (McpAuthorizationModel.ContinuationQuery query in m_model.Continuations)
            {
                Assert.That(query.Header, Is.Null);
                Assert.That(query.Token,
                    Is.EqualTo(query.Release ? CancellationToken.None : m_cancellation.Token));
            }
        }

        private int ReturnedReferences()
        {
            return m_model.Browses.Sum(query => query.Result.References.Count) +
                m_model.Continuations.Where(query => !query.Release).Sum(query => query.Result.References.Count);
        }

        private NodeId[] AddWideGraph(int count, uint firstId)
        {
            NodeId[] nodes =
                [.. Enumerable.Range(0, count).Select(index => m_model.AddNode(new NodeId(firstId + (uint)index, 1)))];
            for (int index = 1; index < nodes.Length; index++)
            {
                m_model.AddOwner(nodes[0], nodes[index], ReferenceTypeIds.HasComponent);
            }
            return nodes;
        }

        private NodeId[] AddDepthGraph(int depth, uint firstId)
        {
            NodeId[] nodes =
            [
                .. Enumerable.Range(0, depth + 1)
                    .Select(index => m_model.AddNode(new NodeId(firstId + (uint)index, 1)))
            ];
            for (int index = 1; index < nodes.Length; index++)
            {
                m_model.AddOwner(nodes[index - 1], nodes[index], ReferenceTypeIds.HasComponent);
            }
            m_model.AddTypeDefinition(nodes[^1], ObjectTypeIds.RoleType);
            return nodes;
        }

        private NodeId[] AddReferenceGraph(int count, uint firstId, string edgeKind)
        {
            NodeId root = m_model.AddNode(new NodeId(firstId, 1));
            NodeId target = edgeKind switch
            {
                "Rejected" => ObjectTypeIds.RoleType,
                "Duplicate" when count > 0 => m_model.AddNode(new NodeId(firstId + 1, 1)),
                _ => root
            };
            NodeId referenceType = edgeKind == "Rejected"
                ? ReferenceTypeIds.HasNotifier
                : ReferenceTypeIds.HasComponent;
            m_model.SetBrowsePage(root, ReferenceTypeIds.HierarchicalReferences, BrowseDirection.Inverse,
                RepeatedReferences(count, target, referenceType));
            return edgeKind == "Duplicate" && count > 0 ? [root, target] : [root];
        }

        private static ReferenceDescription[] RepeatedReferences(
            int count, NodeId target, NodeId referenceType, NodeClass nodeClass = NodeClass.Object)
        {
            return [.. Enumerable.Range(0, count).Select(_ => new ReferenceDescription
            {
                NodeId = target,
                ReferenceTypeId = referenceType,
                IsForward = false,
                NodeClass = nodeClass
            })];
        }

        private static async Task<ServiceResultException> AssertStatusAsync(Func<ValueTask> action, StatusCode status)
        {
            ServiceResultException? failure = null;
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                failure = exception;
            }
            Assert.That(failure, Is.TypeOf<ServiceResultException>());
            Assert.That(failure!.StatusCode.Code, Is.EqualTo(status.Code));
            return failure!;
        }

        private static async Task AssertCancellationAsync(Func<ValueTask> action, CancellationToken token)
        {
            OperationCanceledException? failure = null;
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                failure = exception;
            }
            Assert.That(failure, Is.TypeOf<OperationCanceledException>());
            Assert.That(failure!.CancellationToken, Is.EqualTo(token));
        }

        private static async Task AssertArgumentNullAsync(Func<ValueTask> action, string parameter)
        {
            ArgumentNullException? failure = null;
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (ArgumentNullException exception)
            {
                failure = exception;
            }
            Assert.That(failure, Is.TypeOf<ArgumentNullException>());
            Assert.That(failure!.ParamName, Is.EqualTo(parameter));
        }

        private McpAuthorizationModel m_model = null!;
        private Mock<IMcpOperationPolicy> m_policy = null!;
        private McpUaOperationGuard m_guard = null!;
        private CancellationTokenSource m_cancellation = null!;
        private readonly McpCallerIdentity m_caller = new("operator-42", "urn:test:trusted-issuer");
    }
}
#endif
