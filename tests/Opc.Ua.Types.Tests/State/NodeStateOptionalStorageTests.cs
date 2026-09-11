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

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Covers optional storage publication, lifetime, and unchanged attribute semantics.
    /// </summary>
    [TestFixture]
    [Category("NodeStateOptionalStorage")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeStateOptionalStorageTests
    {
        [Test]
        public void DefaultReadsAndWritesLeaveBagsAbsent()
        {
            var node = new BaseObjectState(null);
            AssertDefaults(node);
            ResetMetadata(node);
            node.RolePermissions = default;
            node.UserRolePermissions = default;
            node.AccessRestrictions = null;
            AssertDefaults(node);
            Assert.That(s_metadataField.GetValue(node), Is.Null);
            Assert.That(s_securityField.GetValue(node), Is.Null);
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void MetadataMembersIndependentlyAllocateAndRetainStorage(int member)
        {
            var node = new BaseObjectState(null);
            SetMetadataMember(node, member);
            object? bag = s_metadataField.GetValue(node);
            Assert.That(bag, Is.Not.Null);
            Assert.That(node.Extensions, member == 0 ? Is.SameAs(s_extensions) : Is.Null);
            Assert.That(node.Categories, member == 1 ? Is.SameAs(s_categories) : Is.Null);
            Assert.That(node.Specification, Is.EqualTo(member == 2 ? string.Empty : null));
            Assert.That(node.NodeSetDocumentation, Is.EqualTo(member == 3 ? string.Empty : null));
            Assert.That(node.ReleaseStatus, Is.EqualTo(member == 4
                ? Export.ReleaseStatus.Draft : Export.ReleaseStatus.Released));
            Assert.That(node.DesignToolOnly, Is.EqualTo(member == 5));

            ResetMetadata(node);
            AssertDefaults(node);
            Assert.That(s_metadataField.GetValue(node), Is.SameAs(bag));
            Assert.That(s_securityField.GetValue(node), Is.Null);
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        [Test]
        public void MetadataCloneSharesPayloadsButNotStorage()
        {
            var source = new BaseObjectState(null);
            for (int member = 0; member < 6; member++)
            {
                SetMetadataMember(source, member);
            }
            source.Extensions = [XmlElement.From(new System.Xml.XmlDocument().CreateElement("extension"))];
            source.Categories = ["Category"];
            source.Specification = "specification";
            source.NodeSetDocumentation = "documentation";
            source.ReleaseStatus = Export.ReleaseStatus.Deprecated;
            var copy = (BaseObjectState)source.Clone();
            Assert.That(copy.DeepEquals(source), Is.True);
            Assert.That(copy.Extensions, Is.SameAs(source.Extensions));
            Assert.That(copy.Categories, Is.SameAs(source.Categories));
            Assert.That(s_metadataField.GetValue(copy), Is.Not.SameAs(s_metadataField.GetValue(source)));
            copy.Categories![0] = "shared";
            Assert.That(source.Categories[0], Is.EqualTo("shared"));
            ResetMetadata(copy);
            Assert.That(source.DesignToolOnly, Is.True);
            Assert.That(source.Extensions, Has.Length.EqualTo(1));
            Assert.That(source.Specification, Is.EqualTo("specification"));
            Assert.That(source.NodeSetDocumentation, Is.EqualTo("documentation"));
            Assert.That(source.ReleaseStatus, Is.EqualTo(Export.ReleaseStatus.Deprecated));
            Assert.That(copy.DeepEquals(source), Is.False);
            ResetMetadata(source);
            Assert.That(copy.DeepEquals(source), Is.True);
        }

        [TestCase(Attributes.RolePermissions)]
        [TestCase(Attributes.UserRolePermissions)]
        [TestCase(Attributes.AccessRestrictions)]
        public async Task SecurityMembersPreservePresenceAndSetterMasksAsync(uint attribute)
        {
            var context = new SystemContext(NUnitTelemetryContext.Create());
            var node = new BaseObjectState(null);
            NodeStateChangeMasks expected = attribute == Attributes.AccessRestrictions
                ? NodeStateChangeMasks.NonValue
                : NodeStateChangeMasks.NonValue | NodeStateChangeMasks.RolePermissions;

            SetSecurityMember(node, attribute, false);
            object? bag = s_securityField.GetValue(node);
            Assert.That(bag, Is.Not.Null);
            Assert.That(node.RolePermissions.IsNull, Is.EqualTo(attribute != Attributes.RolePermissions));
            Assert.That(node.UserRolePermissions.IsNull, Is.EqualTo(attribute != Attributes.UserRolePermissions));
            Assert.That(node.RolePermissions.Count, Is.Zero);
            Assert.That(node.UserRolePermissions.Count, Is.Zero);
            Assert.That(node.AccessRestrictions, Is.EqualTo(attribute == Attributes.AccessRestrictions
                ? AccessRestrictionType.None : (AccessRestrictionType?)null));
            Assert.That(node.ChangeMasks, Is.EqualTo(expected));

            await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            SetSecurityMember(node, attribute, false);
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));

            SetSecurityMember(node, attribute, true);
            Assert.That(node.ChangeMasks, Is.EqualTo(expected));
            Assert.That(node.RolePermissions, Is.EqualTo(attribute == Attributes.RolePermissions
                ? s_permissions : default));
            Assert.That(node.UserRolePermissions, Is.EqualTo(attribute == Attributes.UserRolePermissions
                ? s_permissions : default));
            Assert.That(node.AccessRestrictions, Is.EqualTo(attribute == Attributes.AccessRestrictions
                ? AccessRestrictionType.SigningRequired : (AccessRestrictionType?)null));

            await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            node.RolePermissions = default;
            node.UserRolePermissions = default;
            node.AccessRestrictions = null;
            AssertDefaults(node);
            Assert.That(node.ChangeMasks, Is.EqualTo(expected));
            Assert.That(s_securityField.GetValue(node), Is.SameAs(bag));
            Assert.That(s_metadataField.GetValue(node), Is.Null);
            await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            node.RolePermissions = default;
            node.UserRolePermissions = default;
            node.AccessRestrictions = null;
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(s_securityField.GetValue(node), Is.SameAs(bag));
        }

        [Test]
        public async Task ConcurrentFirstWritesToDifferentMembersPersistAsync()
        {
            for (int iteration = 0; iteration < 50; iteration++)
            {
                var node = new BaseObjectState(null);
                await RunConcurrentWritesAsync(
                    () => node.RolePermissions = s_permissions,
                    () => node.UserRolePermissions = s_replacementPermissions,
                    () => node.AccessRestrictions = AccessRestrictionType.EncryptionRequired,
                    () => node.Extensions = s_extensions,
                    () => node.Categories = s_categories,
                    () => node.Specification = "spec",
                    () => node.NodeSetDocumentation = "docs",
                    () => node.ReleaseStatus = Export.ReleaseStatus.Draft,
                    () => node.DesignToolOnly = true).ConfigureAwait(false);
                Assert.That(node.RolePermissions, Is.EqualTo(s_permissions));
                Assert.That(node.UserRolePermissions, Is.EqualTo(s_replacementPermissions));
                Assert.That(node.AccessRestrictions, Is.EqualTo(AccessRestrictionType.EncryptionRequired));
                Assert.That(node.Extensions, Is.SameAs(s_extensions));
                Assert.That(node.Categories, Is.SameAs(s_categories));
                Assert.That(node.Specification, Is.EqualTo("spec"));
                Assert.That(node.NodeSetDocumentation, Is.EqualTo("docs"));
                Assert.That(node.ReleaseStatus, Is.EqualTo(Export.ReleaseStatus.Draft));
                Assert.That(node.DesignToolOnly, Is.True);

                object? security = s_securityField.GetValue(node);
                object? metadata = s_metadataField.GetValue(node);
                Assert.That(security, Is.Not.Null);
                Assert.That(metadata, Is.Not.Null);
                await RunConcurrentWritesAsync(
                    () => node.RolePermissions = default,
                    () => node.UserRolePermissions = [],
                    () => node.AccessRestrictions = null,
                    () => node.Specification = null,
                    () => node.NodeSetDocumentation = "retained").ConfigureAwait(false);
                Assert.That(node.RolePermissions.IsNull, Is.True);
                Assert.That(node.UserRolePermissions.IsNull, Is.False);
                Assert.That(node.UserRolePermissions.Count, Is.Zero);
                Assert.That(node.AccessRestrictions, Is.Null);
                Assert.That(node.Specification, Is.Null);
                Assert.That(node.NodeSetDocumentation, Is.EqualTo("retained"));
                Assert.That(node.Extensions, Is.SameAs(s_extensions));
                Assert.That(node.Categories, Is.SameAs(s_categories));
                Assert.That(node.ReleaseStatus, Is.EqualTo(Export.ReleaseStatus.Draft));
                Assert.That(node.DesignToolOnly, Is.True);
                Assert.That(s_securityField.GetValue(node), Is.SameAs(security));
                Assert.That(s_metadataField.GetValue(node), Is.SameAs(metadata));
            }
        }

        [Test]
        public void SecurityClonePreservesEqualityAndIndependentBags()
        {
            var source = new BaseObjectState(null)
            {
                RolePermissions = s_permissions,
                UserRolePermissions = []
            };
            var copy = (BaseObjectState)source.Clone();
            Assert.That(copy.DeepEquals(source), Is.True);
            Assert.That(copy.DeepGetHashCode(), Is.EqualTo(source.DeepGetHashCode()));
            Assert.That(copy.RolePermissions[0], Is.SameAs(source.RolePermissions[0]));
            Assert.That(copy.UserRolePermissions.IsNull, Is.False);
            Assert.That(s_securityField.GetValue(copy), Is.Not.SameAs(s_securityField.GetValue(source)));
            copy.RolePermissions = default;
            copy.UserRolePermissions = s_permissions;
            copy.AccessRestrictions = AccessRestrictionType.None;
            Assert.That(source.RolePermissions, Is.EqualTo(s_permissions));
            Assert.That(source.UserRolePermissions.Count, Is.Zero);
            Assert.That(source.AccessRestrictions, Is.Null);
            Assert.That(copy.DeepEquals(source), Is.False);
        }

        [Test]
        public void CloneCopiesAccessRestrictions()
        {
            var source = new BaseObjectState(null) { AccessRestrictions = AccessRestrictionType.SigningRequired };
            var copy = (BaseObjectState)source.Clone();
            // CopyTo used to drop AccessRestrictions (and UserWriteMask) entirely.
            Assert.That(copy.AccessRestrictions, Is.EqualTo(AccessRestrictionType.SigningRequired));
            Assert.That(s_securityField.GetValue(copy), Is.Not.Null);
            Assert.That(copy.DeepEquals(source), Is.True);
            // The copy has its own storage.
            copy.AccessRestrictions = AccessRestrictionType.EncryptionRequired;
            Assert.That(source.AccessRestrictions, Is.EqualTo(AccessRestrictionType.SigningRequired));
        }

        [TestCase(Attributes.RolePermissions)]
        [TestCase(Attributes.UserRolePermissions)]
        [TestCase(Attributes.AccessRestrictions)]
        public void AttributeReadsPreserveAbsentAndPresentValues(uint attribute)
        {
            var context = new SystemContext(NUnitTelemetryContext.Create());
            var node = new BaseObjectState(null);
            var value = new DataValue();
            ServiceResult result = node.ReadAttribute(context, attribute, default, default, ref value);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(value.WrappedValue.IsNull, Is.True);
            Assert.That(s_securityField.GetValue(node), Is.Null);

            SetSecurityMember(node, attribute, false);
            result = node.ReadAttribute(context, attribute, default, default, ref value);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            if (attribute == Attributes.AccessRestrictions)
            {
                Assert.That(value.WrappedValue.TryGetValue(out ushort restriction), Is.True);
                Assert.That(restriction, Is.Zero);
            }
            else
            {
                Assert.That(value.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> permissions), Is.True);
                Assert.That(permissions.IsNull, Is.False);
                Assert.That(permissions.Count, Is.Zero);
            }
        }

        [TestCase(Attributes.RolePermissions)]
        [TestCase(Attributes.UserRolePermissions)]
        [TestCase(Attributes.AccessRestrictions)]
        public void ReadCallbacksReturnLocalValuesWithoutAllocatingStorage(uint attribute)
        {
            var context = new SystemContext(NUnitTelemetryContext.Create());
            var node = new BaseObjectState(null);
            int calls = 0;
            node.OnReadRolePermissions = ReadPermissions;
            node.OnReadUserRolePermissions = ReadPermissions;
            node.OnReadAccessRestrictions = (_, _, ref restriction) =>
            {
                calls++;
                restriction = AccessRestrictionType.EncryptionRequired;
                return ServiceResult.Good;
            };
            var value = new DataValue();
            ServiceResult result = node.ReadAttribute(context, attribute, default, default, ref value);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(calls, Is.EqualTo(1));
            if (attribute == Attributes.AccessRestrictions)
            {
                Assert.That(value.WrappedValue.TryGetValue(out ushort restriction), Is.True);
                Assert.That(restriction, Is.EqualTo((ushort)AccessRestrictionType.EncryptionRequired));
            }
            else
            {
                Assert.That(value.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> permissions), Is.True);
                Assert.That(permissions.Count, Is.EqualTo(1));
                Assert.That(permissions[0].TryGetValue(out RolePermissionType? permission), Is.True);
                Assert.That(permission, Is.EqualTo(s_permissions[0]));
            }
            AssertDefaults(node);
            Assert.That(s_securityField.GetValue(node), Is.Null);
            Assert.That(s_metadataField.GetValue(node), Is.Null);

            ServiceResult ReadPermissions(ISystemContext _, NodeState __, ref ArrayOf<RolePermissionType> permissions)
            {
                calls++;
                permissions = s_permissions;
                return ServiceResult.Good;
            }
        }

        [TestCase(Attributes.RolePermissions)]
        [TestCase(Attributes.UserRolePermissions)]
        [TestCase(Attributes.AccessRestrictions)]
        public void ReadCallbackErrorsLeaveStoredValuesUnchanged(uint attribute)
        {
            var context = new SystemContext(NUnitTelemetryContext.Create());
            var node = new BaseObjectState(null);
            SetSecurityMember(node, attribute, true);
            node.OnReadRolePermissions = (_, _, ref permissions) =>
            {
                permissions = default;
                return StatusCodes.BadUserAccessDenied;
            };
            node.OnReadUserRolePermissions = node.OnReadRolePermissions;
            node.OnReadAccessRestrictions = (_, _, ref restriction) =>
            {
                restriction = null;
                return StatusCodes.BadUserAccessDenied;
            };
            var value = new DataValue();
            ServiceResult result = node.ReadAttribute(context, attribute, default, default, ref value);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(node.RolePermissions, Is.EqualTo(attribute == Attributes.RolePermissions
                ? s_permissions : default));
            Assert.That(node.UserRolePermissions, Is.EqualTo(attribute == Attributes.UserRolePermissions
                ? s_permissions : default));
            Assert.That(node.AccessRestrictions, Is.EqualTo(attribute == Attributes.AccessRestrictions
                ? AccessRestrictionType.SigningRequired : (AccessRestrictionType?)null));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RolePermissionWritesPreserveCallbackResultsAndUnconditionalMasksAsync(bool reset)
        {
            var context = new SystemContext(NUnitTelemetryContext.Create());
            var node = new BaseObjectState(null) { WriteMask = AttributeWriteMask.RolePermissions };
            int calls = 0;
            node.OnWriteRolePermissions = (_, _, ref permissions) =>
            {
                Assert.That(permissions, Is.EqualTo(s_permissions));
                calls++;
                permissions = reset ? default : s_replacementPermissions;
                return ServiceResult.Good;
            };
            var value = new DataValue(Variant.FromStructure(s_permissions));
            for (int iteration = 0; iteration < 2; iteration++)
            {
                await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
                ServiceResult result = node.WriteAttribute(context, Attributes.RolePermissions, default, value);
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(node.RolePermissions, Is.EqualTo(reset ? default : s_replacementPermissions));
                Assert.That(node.ChangeMasks,
                    Is.EqualTo(NodeStateChangeMasks.NonValue | NodeStateChangeMasks.RolePermissions));
            }
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(s_securityField.GetValue(node), reset ? Is.Null : Is.Not.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RestrictionWritesPreserveCallbackResultsWithoutSettingMasksAsync(bool reset)
        {
            var context = new SystemContext(NUnitTelemetryContext.Create());
            var node = new BaseObjectState(null) { WriteMask = AttributeWriteMask.AccessRestrictions };
            int calls = 0;
            node.OnWriteAccessRestrictions = (_, _, ref restriction) =>
            {
                Assert.That(restriction, Is.EqualTo(AccessRestrictionType.SigningRequired));
                calls++;
                restriction = reset ? null : AccessRestrictionType.EncryptionRequired;
                return ServiceResult.Good;
            };
            await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            ServiceResult result = node.WriteAttribute(context, Attributes.AccessRestrictions, default,
                new DataValue((ushort)AccessRestrictionType.SigningRequired));
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(node.AccessRestrictions, Is.EqualTo(reset
                ? (AccessRestrictionType?)null : AccessRestrictionType.EncryptionRequired));
            // A successful write raises the change mask so that monitored
            // items on the attribute are notified.
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.NonValue));
            Assert.That(s_securityField.GetValue(node), reset ? Is.Null : Is.Not.Null);
        }

        [TestCase(Attributes.RolePermissions, false)]
        [TestCase(Attributes.RolePermissions, true)]
        [TestCase(Attributes.AccessRestrictions, false)]
        [TestCase(Attributes.AccessRestrictions, true)]
        public async Task RejectedWriteCallbacksPreserveStorageAndMasksAsync(uint attribute, bool throws)
        {
            var context = new SystemContext(NUnitTelemetryContext.Create());
            var node = new BaseObjectState(null)
            {
                WriteMask = AttributeWriteMask.RolePermissions | AttributeWriteMask.AccessRestrictions,
                OnWriteRolePermissions = (_, _, ref permissions) =>
                    {
                        permissions = s_replacementPermissions;
                        return Reject();
                    },
                OnWriteAccessRestrictions = (_, _, ref restriction) =>
                    {
                        restriction = AccessRestrictionType.EncryptionRequired;
                        return Reject();
                    }
            };
            await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            Variant input = attribute == Attributes.RolePermissions
                ? Variant.FromStructure(s_permissions) : new Variant((ushort)1);
            ServiceResult result = node.WriteAttribute(context, attribute, default, new DataValue(input));
            Assert.That(result.StatusCode, Is.EqualTo(throws
                ? StatusCodes.BadUnexpectedError : StatusCodes.BadUserAccessDenied));
            AssertDefaults(node);
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(s_securityField.GetValue(node), Is.Null);

            ServiceResult Reject()
            {
                if (throws)
                {
                    throw new InvalidOperationException("Callback failure");
                }
                return StatusCodes.BadUserAccessDenied;
            }
        }

        [TestCase(Attributes.RolePermissions)]
        [TestCase(Attributes.AccessRestrictions)]
        public void WriteValidationPreservesAbsentStorage(uint attribute)
        {
            var context = new SystemContext(NUnitTelemetryContext.Create());
            var node = new BaseObjectState(null);
            Variant input = attribute == Attributes.RolePermissions
                ? Variant.FromStructure(s_permissions) : new Variant((ushort)1);
            ServiceResult result = node.WriteAttribute(context, attribute, default, new DataValue(input));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            result = node.WriteAttribute(context, attribute, default, new DataValue("invalid"));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(s_securityField.GetValue(node), Is.Null);
        }

        [Test]
        public void UserRolePermissionsRemainUnwritable()
        {
            var context = new SystemContext(NUnitTelemetryContext.Create());
            var node = new BaseObjectState(null) { WriteMask = AttributeWriteMask.RolePermissions };
            bool invoked = false;
            node.OnWriteUserRolePermissions = (_, _, ref permissions) =>
            {
                invoked = true;
                return ServiceResult.Good;
            };
            ServiceResult result = node.WriteAttribute(context, Attributes.UserRolePermissions, default,
                new DataValue(Variant.FromStructure(s_permissions)));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadAttributeIdInvalid));
            Assert.That(invoked, Is.False);
            Assert.That(node.UserRolePermissions.IsNull, Is.True);
            Assert.That(s_securityField.GetValue(node), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExistingNodeFormatsContinueOmittingOptionalStorage(bool xml)
        {
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId(1000),
                BrowseName = QualifiedName.From("OptionalNode"),
                DisplayName = LocalizedText.From("OptionalNode"),
                SymbolicName = "OptionalNode"
            };
            NodeState.AttributesToSave attributes = node.GetAttributesToSave(context);
            node.RolePermissions = s_permissions;
            node.UserRolePermissions = s_permissions;
            node.AccessRestrictions = AccessRestrictionType.SigningRequired;
            node.Specification = "spec";
            Assert.That(node.GetAttributesToSave(context), Is.EqualTo(attributes));
            using var stream = new MemoryStream();
            if (xml)
            {
                node.SaveAsXml(context, stream);
            }
            else
            {
                node.SaveAsBinary(context, stream);
            }
            using var input = new MemoryStream(stream.ToArray());
            var loaded = new BaseObjectState(null);
            if (xml)
            {
                loaded.LoadFromXml(context, input);
            }
            else
            {
                loaded.LoadAsBinary(context, input);
            }
            Assert.That(loaded.NodeId, Is.EqualTo(node.NodeId));
            Assert.That(loaded.BrowseName, Is.EqualTo(node.BrowseName));
            AssertDefaults(loaded);
            Assert.That(s_securityField.GetValue(loaded), Is.Null);
            Assert.That(s_metadataField.GetValue(loaded), Is.Null);
        }

        private static void AssertDefaults(NodeState node)
        {
            Assert.That(node.Extensions, Is.Null);
            Assert.That(node.Categories, Is.Null);
            Assert.That(node.Specification, Is.Null);
            Assert.That(node.NodeSetDocumentation, Is.Null);
            Assert.That(node.ReleaseStatus, Is.EqualTo(Export.ReleaseStatus.Released));
            Assert.That(node.DesignToolOnly, Is.False);
            Assert.That(node.RolePermissions.IsNull, Is.True);
            Assert.That(node.UserRolePermissions.IsNull, Is.True);
            Assert.That(node.AccessRestrictions, Is.Null);
        }

        private static void ResetMetadata(NodeState node)
        {
            node.Extensions = null;
            node.Categories = null;
            node.Specification = null;
            node.NodeSetDocumentation = null;
            node.ReleaseStatus = Export.ReleaseStatus.Released;
            node.DesignToolOnly = false;
        }

        private static void SetMetadataMember(NodeState node, int member)
        {
            switch (member)
            {
                case 0:
                    node.Extensions = s_extensions;
                    break;
                case 1:
                    node.Categories = s_categories;
                    break;
                case 2:
                    node.Specification = string.Empty;
                    break;
                case 3:
                    node.NodeSetDocumentation = string.Empty;
                    break;
                case 4:
                    node.ReleaseStatus = Export.ReleaseStatus.Draft;
                    break;
                case 5:
                    node.DesignToolOnly = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(member));
            }
        }

        private static void SetSecurityMember(NodeState node, uint attribute, bool populated)
        {
            switch (attribute)
            {
                case Attributes.RolePermissions:
                    node.RolePermissions = populated ? s_permissions : [];
                    break;
                case Attributes.UserRolePermissions:
                    node.UserRolePermissions = populated ? s_permissions : [];
                    break;
                case Attributes.AccessRestrictions:
                    node.AccessRestrictions = populated
                        ? AccessRestrictionType.SigningRequired : AccessRestrictionType.None;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(attribute));
            }
        }

        private static async Task RunConcurrentWritesAsync(params Action[] writes)
        {
            using var barrier = new Barrier(writes.Length);
            Task[] workers = [.. writes.Select(write => Task.Factory.StartNew(() =>
            {
                if (!barrier.SignalAndWait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("Concurrent writers did not reach the gate.");
                }
                write();
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))];
            await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        }

        private static readonly FieldInfo s_metadataField = typeof(NodeState).GetField(
            "m_designMetadata", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(NodeState).FullName, "m_designMetadata");

        private static readonly FieldInfo s_securityField = typeof(NodeState).GetField(
            "m_securityData", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(NodeState).FullName, "m_securityData");

        private static readonly XmlElement[] s_extensions = [];
        private static readonly string[] s_categories = [];

        private static readonly ArrayOf<RolePermissionType> s_permissions =
        [
            new RolePermissionType { RoleId = new NodeId(1), Permissions = (uint)PermissionType.Read }
        ];

        private static readonly ArrayOf<RolePermissionType> s_replacementPermissions =
        [
            new RolePermissionType { RoleId = new NodeId(2), Permissions = (uint)PermissionType.Browse }
        ];
    }
}
