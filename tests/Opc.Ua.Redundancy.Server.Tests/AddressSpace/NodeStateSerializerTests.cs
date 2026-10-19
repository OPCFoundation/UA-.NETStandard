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
using System.Buffers.Binary;
using System.IO;
using NUnit.Framework;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="NodeStateSerializer"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class NodeStateSerializerTests
    {
        private const ushort NamespaceIndex = 1;
        private SystemContext m_context = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:serializer");
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public void ObjectRoundTripsAsBaseObjectState()
        {
            var original = new BaseObjectState(null)
            {
                NodeId = new NodeId("obj", NamespaceIndex),
                BrowseName = new QualifiedName("Obj", NamespaceIndex),
                DisplayName = new LocalizedText("Obj")
            };

            ByteString payload = NodeStateSerializer.Serialize(m_context, original);
            NodeState restored = NodeStateSerializer.Deserialize(m_context, payload);

            Assert.That(restored, Is.InstanceOf<BaseObjectState>());
            Assert.That(restored.NodeClass, Is.EqualTo(NodeClass.Object));
            Assert.That(restored.BrowseName, Is.EqualTo(original.BrowseName));
        }

        [Test]
        public void VariableRoundTripsWithValue()
        {
            var original = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("var", NamespaceIndex),
                BrowseName = new QualifiedName("Var", NamespaceIndex),
                DisplayName = new LocalizedText("Var"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                Value = new Variant(2.5)
            };

            ByteString payload = NodeStateSerializer.Serialize(m_context, original);
            NodeState restored = NodeStateSerializer.Deserialize(m_context, payload);

            Assert.That(restored, Is.InstanceOf<BaseDataVariableState>());
            Assert.That(restored.NodeClass, Is.EqualTo(NodeClass.Variable));
            var variable = (BaseDataVariableState)restored;
            Assert.That(variable.Value, Is.EqualTo(new Variant(2.5)));
            Assert.That(variable.DataType, Is.EqualTo(DataTypeIds.Double));
        }

        [Test]
        public void MethodRoundTripsAsMethodState()
        {
            var original = new MethodState(null)
            {
                NodeId = new NodeId("m", NamespaceIndex),
                BrowseName = new QualifiedName("DoIt", NamespaceIndex),
                DisplayName = new LocalizedText("DoIt"),
                Executable = true,
                UserExecutable = true
            };

            ByteString payload = NodeStateSerializer.Serialize(m_context, original);
            NodeState restored = NodeStateSerializer.Deserialize(m_context, payload);

            Assert.That(restored, Is.InstanceOf<MethodState>());
            Assert.That(((MethodState)restored).Executable, Is.True);
        }

        [Test]
        public void DeserializeTooShortPayloadThrows()
        {
            var tooShort = ByteString.From(new byte[] { 1, 2 });

            Assert.That(
                () => NodeStateSerializer.Deserialize(m_context, tooShort),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void SerializeThrowsOnNullContext()
        {
            var node = new BaseObjectState(null);

            Assert.That(
                () => NodeStateSerializer.Serialize(null!, node),
                Throws.ArgumentNullException);
        }

        [Test]
        public void SerializeThrowsOnNullNode()
        {
            Assert.That(
                () => NodeStateSerializer.Serialize(m_context, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void DeserializeThrowsOnNullContext()
        {
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId("obj", NamespaceIndex),
                BrowseName = new QualifiedName("Obj", NamespaceIndex)
            };
            ByteString payload = NodeStateSerializer.Serialize(m_context, node);

            Assert.That(
                () => NodeStateSerializer.Deserialize(null!, payload),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ViewRoundTripsAsViewState()
        {
            var original = new ViewState
            {
                NodeId = new NodeId("view", NamespaceIndex),
                BrowseName = new QualifiedName("View", NamespaceIndex),
                DisplayName = new LocalizedText("View"),
                ContainsNoLoops = true
            };

            NodeState restored = RoundTrip(original);

            Assert.That(restored, Is.InstanceOf<ViewState>());
            Assert.That(restored.NodeClass, Is.EqualTo(NodeClass.View));
            Assert.That(((ViewState)restored).ContainsNoLoops, Is.True);
        }

        [Test]
        public void ObjectTypeRoundTripsAsBaseObjectTypeState()
        {
            var original = new BaseObjectTypeState
            {
                NodeId = new NodeId("otype", NamespaceIndex),
                BrowseName = new QualifiedName("OType", NamespaceIndex),
                DisplayName = new LocalizedText("OType"),
                IsAbstract = true
            };

            NodeState restored = RoundTrip(original);

            Assert.That(restored, Is.InstanceOf<BaseObjectTypeState>());
            Assert.That(restored.NodeClass, Is.EqualTo(NodeClass.ObjectType));
            Assert.That(((BaseObjectTypeState)restored).IsAbstract, Is.True);
        }

        [Test]
        public void VariableTypeRoundTripsAsBaseDataVariableTypeState()
        {
            var original = new BaseDataVariableTypeState
            {
                NodeId = new NodeId("vtype", NamespaceIndex),
                BrowseName = new QualifiedName("VType", NamespaceIndex),
                DisplayName = new LocalizedText("VType"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };

            NodeState restored = RoundTrip(original);

            Assert.That(restored, Is.InstanceOf<BaseDataVariableTypeState>());
            Assert.That(restored.NodeClass, Is.EqualTo(NodeClass.VariableType));
            Assert.That(((BaseDataVariableTypeState)restored).DataType, Is.EqualTo(DataTypeIds.Int32));
        }

        [Test]
        public void ReferenceTypeRoundTripsAsReferenceTypeState()
        {
            var original = new ReferenceTypeState
            {
                NodeId = new NodeId("rtype", NamespaceIndex),
                BrowseName = new QualifiedName("RType", NamespaceIndex),
                DisplayName = new LocalizedText("RType"),
                InverseName = new LocalizedText("RTypeInverse"),
                Symmetric = false
            };

            NodeState restored = RoundTrip(original);

            Assert.That(restored, Is.InstanceOf<ReferenceTypeState>());
            Assert.That(restored.NodeClass, Is.EqualTo(NodeClass.ReferenceType));
            Assert.That(((ReferenceTypeState)restored).InverseName, Is.EqualTo(original.InverseName));
        }

        [Test]
        public void DataTypeRoundTripsAsDataTypeState()
        {
            var original = new DataTypeState
            {
                NodeId = new NodeId("dtype", NamespaceIndex),
                BrowseName = new QualifiedName("DType", NamespaceIndex),
                DisplayName = new LocalizedText("DType"),
                IsAbstract = true
            };

            NodeState restored = RoundTrip(original);

            Assert.That(restored, Is.InstanceOf<DataTypeState>());
            Assert.That(restored.NodeClass, Is.EqualTo(NodeClass.DataType));
            Assert.That(((DataTypeState)restored).IsAbstract, Is.True);
        }

        /// <summary>
        /// Rejects an unsupported root class inside an otherwise valid replication envelope.
        /// </summary>
        [Test]
        public void DeserializeUnknownNodeClassThrows()
        {
            var source = new BaseObjectState(null)
            {
                NodeId = new NodeId("unknown-class", NamespaceIndex),
                BrowseName = new QualifiedName("UnknownClass", NamespaceIndex)
            };
            byte[] bytes = NodeStateSerializer.Serialize(m_context, source).ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), 3);
            var payload = new ByteString(bytes);

            Assert.That(
                () => NodeStateSerializer.Deserialize(m_context, payload),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNodeClassInvalid));
        }

        /// <summary>
        /// Preserves explicit and inherited policy states independently on every descendant.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public void SecurityF07PolicyRoundTripPreservesNullAndEmptyPermissions(bool explicitEmpty)
        {
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("policy-root", NamespaceIndex),
                BrowseName = new QualifiedName("Root", NamespaceIndex),
                RolePermissions = explicitEmpty ? [] : default,
                UserRolePermissions = explicitEmpty ? [] : default,
                AccessRestrictions = explicitEmpty ? AccessRestrictionType.None : null
            };
            var child = new BaseObjectState(root)
            {
                NodeId = new NodeId("policy-child", NamespaceIndex),
                BrowseName = new QualifiedName("Child", NamespaceIndex),
                RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                        Permissions = (uint)(PermissionType.Read | PermissionType.Write)
                    }
                ],
                AccessRestrictions = AccessRestrictionType.EncryptionRequired | AccessRestrictionType.SessionRequired
            };
            var grandchild = new BaseDataVariableState(child)
            {
                NodeId = new NodeId("policy-grandchild", NamespaceIndex),
                BrowseName = new QualifiedName("Grandchild", NamespaceIndex),
                RolePermissions = child.RolePermissions,
                UserRolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                        Permissions = (uint)PermissionType.Read
                    }
                ],
                AccessRestrictions = AccessRestrictionType.SigningRequired,
                Value = new Variant(42)
            };
            child.AddChild(grandchild);
            root.AddChild(child);

            NodeState restored = RoundTrip(root);
            BaseInstanceState? restoredChild = restored.FindChild(m_context, child.BrowseName);
            Assert.That(restoredChild, Is.Not.Null);
            BaseInstanceState? restoredGrandchild = restoredChild!.FindChild(m_context, grandchild.BrowseName);
            Assert.That(restoredGrandchild, Is.Not.Null);
            Assert.That(restored.RolePermissions.IsNull, Is.EqualTo(!explicitEmpty));
            Assert.That(restored.UserRolePermissions.IsNull, Is.EqualTo(!explicitEmpty));
            Assert.That(restored.AccessRestrictions, Is.EqualTo(root.AccessRestrictions));
            Assert.That(restoredChild.RolePermissions, Is.EqualTo(child.RolePermissions));
            Assert.That(restoredChild.AccessRestrictions, Is.EqualTo(child.AccessRestrictions));
            Assert.That(restoredGrandchild!.RolePermissions, Is.EqualTo(grandchild.RolePermissions));
            Assert.That(restoredGrandchild.UserRolePermissions, Is.EqualTo(grandchild.UserRolePermissions));
            Assert.That(restoredGrandchild.AccessRestrictions, Is.EqualTo(grandchild.AccessRestrictions));
        }

        /// <summary>
        /// Custom role identifiers are resolved by URI rather than by the sender's namespace index.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public void SecurityF07RolePolicyUsesNamespaceUris()
        {
            SystemContext sourceContext = CreatePolicyContext("urn:test:serializer", "urn:test:roles");
            SystemContext peerContext = CreatePolicyContext("urn:test:roles", "urn:test:serializer");
            var source = new BaseObjectState(null)
            {
                NodeId = new NodeId("custom-role", 1),
                BrowseName = new QualifiedName("CustomRole", 1),
                RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = new NodeId("RestrictedRole", 2),
                        Permissions = (uint)PermissionType.Read
                    }
                ]
            };

            ByteString payload = NodeStateSerializer.Serialize(sourceContext, source);
            NodeState restored = NodeStateSerializer.Deserialize(peerContext, payload);

            Assert.That(restored.NodeId, Is.EqualTo(new NodeId("custom-role", 2)));
            Assert.That(restored.RolePermissions, Has.Count.EqualTo(1));
            Assert.That(restored.RolePermissions[0].RoleId, Is.EqualTo(new NodeId("RestrictedRole", 1)));
            Assert.That(restored.RolePermissions[0].Permissions, Is.EqualTo((uint)PermissionType.Read));
        }

        /// <summary>
        /// A peer lacking the namespace of a required role cannot publish a policy-less replacement.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public void SecurityF07MissingRoleNamespaceFailsClosed()
        {
            SystemContext sourceContext = CreatePolicyContext("urn:test:serializer", "urn:test:roles");
            var source = new BaseObjectState(null)
            {
                NodeId = new NodeId("unknown-role", NamespaceIndex),
                BrowseName = new QualifiedName("UnknownRole", NamespaceIndex),
                RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = new NodeId("RestrictedRole", 2),
                        Permissions = (uint)PermissionType.Read
                    }
                ]
            };
            ByteString payload = NodeStateSerializer.Serialize(sourceContext, source);

            Assert.That(
                () => NodeStateSerializer.Deserialize(m_context, payload),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadDecodingError));
        }

        /// <summary>
        /// The old binary representation cannot prove that a node was intentionally unrestricted.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public void SecurityF07LegacyPayloadCannotBeMistakenForUnrestrictedPolicy()
        {
            var source = new BaseObjectState(null)
            {
                NodeId = new NodeId("legacy", NamespaceIndex),
                BrowseName = new QualifiedName("Legacy", NamespaceIndex),
                AccessRestrictions = AccessRestrictionType.EncryptionRequired
            };
            using var stream = new MemoryStream();
            byte[] header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, (int)source.NodeClass);
            stream.Write(header, 0, header.Length);
            source.SaveAsBinary(m_context, stream);
            var payload = new ByteString(stream.ToArray());

            Assert.That(
                () => NodeStateSerializer.Deserialize(m_context, payload),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadDecodingError));
        }

        /// <summary>
        /// Missing policy bytes and unsupported versions are rejected instead of defaulting to unrestricted access.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public void SecurityF07IncompletePolicyFailsClosed(bool unknownVersion)
        {
            var source = new BaseObjectState(null)
            {
                NodeId = new NodeId("invalid-policy", NamespaceIndex),
                BrowseName = new QualifiedName("InvalidPolicy", NamespaceIndex),
                AccessRestrictions = AccessRestrictionType.EncryptionRequired
            };
            byte[] bytes = NodeStateSerializer.Serialize(m_context, source).ToArray();
            if (unknownVersion)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), uint.MaxValue);
            }
            else
            {
                Array.Resize(ref bytes, bytes.Length - 1);
            }
            var payload = new ByteString(bytes);

            Assert.That(
                () => NodeStateSerializer.Deserialize(m_context, payload),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadDecodingError));
        }

        /// <summary>
        /// A complete node body cannot substitute for a missing descendant policy entry.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public void SecurityF07DescendantPolicyCannotBeOmitted()
        {
            var source = new BaseObjectState(null)
            {
                NodeId = new NodeId("missing-child-policy", NamespaceIndex),
                BrowseName = new QualifiedName("Root", NamespaceIndex)
            };
            source.AddChild(new BaseObjectState(source)
            {
                NodeId = new NodeId("restricted-child", NamespaceIndex),
                BrowseName = new QualifiedName("Child", NamespaceIndex),
                AccessRestrictions = AccessRestrictionType.EncryptionRequired
            });
            byte[] bytes = NodeStateSerializer.Serialize(m_context, source).ToArray();
            using (var decoder = new BinaryDecoder(bytes, m_context.AsMessageContext()))
            {
                _ = decoder.ReadUInt32(null);
                _ = decoder.ReadUInt32(null);
                _ = decoder.ReadInt32(null);
                _ = decoder.ReadByteString(null);
                _ = decoder.ReadExpandedNodeId(null);
                _ = decoder.ReadString(null);
                _ = decoder.ReadString(null);
                _ = decoder.ReadInt32(null);
                _ = decoder.ReadInt32(null);
                _ = decoder.ReadInt32(null);
                _ = decoder.ReadByte(null);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(decoder.Position, 4), 0);
            }
            var payload = new ByteString(bytes);

            Assert.That(
                () => NodeStateSerializer.Deserialize(m_context, payload),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadDecodingError));
        }

        /// <summary>
        /// Unsupported access restrictions are not silently downgraded to the restrictions this replica understands.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public void SecurityF07UnsupportedAccessRestrictionFailsClosed()
        {
            var source = new BaseObjectState(null)
            {
                NodeId = new NodeId("unsupported-restriction", NamespaceIndex),
                BrowseName = new QualifiedName("UnsupportedRestriction", NamespaceIndex),
                AccessRestrictions = (AccessRestrictionType)0x8000
            };

            Assert.That(
                () => NodeStateSerializer.Serialize(m_context, source),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadEncodingError));
        }

        /// <summary>
        /// Invalid role entries cannot disappear into an apparently unrestricted replica policy.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public void SecurityF07InvalidRolePolicyFailsClosed(bool missingNamespace)
        {
            var source = new BaseObjectState(null)
            {
                NodeId = new NodeId("invalid-role", NamespaceIndex),
                BrowseName = new QualifiedName("InvalidRole", NamespaceIndex),
                RolePermissions =
                [
                    missingNamespace
                        ? new RolePermissionType { RoleId = new NodeId("role", 99) }
                        : null!
                ]
            };

            Assert.That(
                () => NodeStateSerializer.Serialize(m_context, source),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadEncodingError));
        }

        /// <summary>
        /// A caller-dependent policy cannot be replaced by the callback's possibly empty backing attribute.
        /// </summary>
        [TestCase(Attributes.RolePermissions)]
        [TestCase(Attributes.UserRolePermissions)]
        [TestCase(Attributes.AccessRestrictions)]
        [Category("SecurityRegression")]
        public void SecurityF07CallbackPolicyFailsClosed(uint attributeId)
        {
            var source = new BaseObjectState(null)
            {
                NodeId = new NodeId("callback-policy", NamespaceIndex),
                BrowseName = new QualifiedName("CallbackPolicy", NamespaceIndex)
            };
            if (attributeId == Attributes.RolePermissions)
            {
                source.OnReadRolePermissions = ReadRestrictedRoles;
            }
            else if (attributeId == Attributes.UserRolePermissions)
            {
                source.OnReadUserRolePermissions = ReadRestrictedRoles;
            }
            else
            {
                source.OnReadAccessRestrictions = ReadRestrictedAccess;
            }

            Assert.That(
                () => NodeStateSerializer.Serialize(m_context, source),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadEncodingError));
        }

        /// <summary>
        /// Supplies a role policy whose callback cannot be reconstructed on another replica.
        /// </summary>
        private static ServiceResult ReadRestrictedRoles(
            ISystemContext context,
            NodeState node,
            ref ArrayOf<RolePermissionType> permissions)
        {
            permissions =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                    Permissions = (uint)PermissionType.Read
                }
            ];
            return ServiceResult.Good;
        }

        /// <summary>
        /// Supplies an access policy whose callback cannot be reconstructed on another replica.
        /// </summary>
        private static ServiceResult ReadRestrictedAccess(
            ISystemContext context,
            NodeState node,
            ref AccessRestrictionType? restrictions)
        {
            restrictions = AccessRestrictionType.EncryptionRequired;
            return ServiceResult.Good;
        }

        /// <summary>
        /// Creates an independent replica context with a chosen namespace ordering.
        /// </summary>
        private static SystemContext CreatePolicyContext(params string[] namespaceUris)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.CreateEmpty(telemetry);
            foreach (string namespaceUri in namespaceUris)
            {
                context.NamespaceUris.GetIndexOrAppend(namespaceUri);
            }
            return new SystemContext(telemetry)
            {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                EncodeableFactory = context.Factory
            };
        }

        private NodeState RoundTrip(NodeState original)
        {
            ByteString payload = NodeStateSerializer.Serialize(m_context, original);
            return NodeStateSerializer.Deserialize(m_context, payload);
        }
    }
}
