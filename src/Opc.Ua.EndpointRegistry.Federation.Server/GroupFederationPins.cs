/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.EndpointRegistry.Federation.Server
{
    internal sealed class GroupFederationPins
    {
        internal const string Attribute = "x-ua-group-reference";

        internal GroupFederationPins(
            RegistryObjectValueDataType value, GroupFederationSource selection, RegistryRecordMapper mapper)
        {
            Value = value;
            if (value.Members.Count != 13 || Text(value, "schema") != "group-reference-v1")
            {
                throw new ArgumentException("Unsupported durable Group reference format.");
            }
            m_source = new RegistryEntityReferenceDataType
            {
                OriginUri = Text(value, "originuri"),
                ApplicationUri = Text(value, "originapplicationuri"),
                RegistryNode = Node(Text(value, "originregistrynode")),
                Xid = Text(value, "xid"),
                Role = Text(value, "role"),
                Locator = Text(value, "locator"),
                HasNativeTarget = Member(value, "hasnativetarget") is RegistryBooleanValueDataType flag
                    ? flag.Value : throw new ArgumentException("A native target discriminator is required."),
                NativeTarget = Node(Text(value, "nativetarget"))
            };
            var origin = new RegistryOriginKey(m_source);
            m_application = Text(value, "applicationuri");
            m_root = Node(Text(value, "registryroot"));
            if (!selection.Trust.Origin.Equals(origin) || selection.RemoteXid != m_source.Xid ||
                m_source.Role != "Group" || selection.Trust.ApplicationUri != m_application ||
                selection.Trust.RegistryRoot != m_root ||
                !Uri.TryCreate(m_source.Locator, UriKind.Absolute, out _) ||
                m_source.HasNativeTarget != (m_application.Length > 0))
            {
                throw new ArgumentException("Durable Group identity contradicts its configured binding.");
            }
            if (m_source.HasNativeTarget)
            {
                Portable(m_source.NativeTarget);
                Portable(m_root);
                if (m_root == m_source.NativeTarget)
                {
                    throw new ArgumentException("The registry root cannot substitute for a Group.");
                }
            }
            else if (!m_source.NativeTarget.IsNull || !m_root.IsNull)
            {
                throw new ArgumentException("An HTTP Group cannot have a native transport target.");
            }
            Epoch = RegistryMetadataMutation.Epoch(value);
            if (Member(value, "metadata") is not RegistryObjectValueDataType metadata)
            {
                throw new ArgumentException("Complete native source metadata is required.");
            }
            Metadata = mapper.Project(metadata,
                selection.RemoteXid.StartsWith("/endpoints/", StringComparison.Ordinal)
                    ? nameof(EndpointDataType) : nameof(MessageGroupDataType));
            string id = selection.RemoteXid.Split('/')[2];
            if (Text(metadata, Metadata is EndpointDataType ? "endpointid" : "messagegroupid") != id ||
                Member(metadata, "epoch") is not null && RegistryMetadataMutation.Epoch(metadata) != Epoch ||
                Member(metadata, "xid") is not null && Text(metadata, "xid") != selection.RemoteXid)
            {
                throw new ArgumentException("Durable native Group metadata contradicts its identity or epoch.");
            }
            Origin = new RegistryOriginDataType
            {
                OriginUri = origin.OriginUri,
                ServerUri = origin.ApplicationUri,
                RegistryNodeId = origin.RegistryNode
            };
        }

        internal RegistryObjectValueDataType Value { get; }
        internal RegistryOriginDataType Origin { get; }
        internal RegistryRecordDataType Metadata { get; }
        internal uint Epoch { get; }

        internal void Match(FederationGroupSnapshot observed, RegistryRecordMapper mapper)
        {
            RegistryEntityReferenceDataType source = observed.Source;
            if (!new RegistryOriginKey(source).Equals(new RegistryOriginKey(m_source)) ||
                source.Xid != m_source.Xid || source.Role != m_source.Role ||
                source.HasNativeTarget != m_source.HasNativeTarget || source.NativeTarget != m_source.NativeTarget ||
                observed.ApplicationUri != m_application || observed.RegistryRoot != m_root)
            {
                throw new ArgumentException("Group relocation changes a durable identity pin.");
            }
            if (observed.Epoch < Epoch || observed.Epoch == Epoch &&
                !RegistryValues.Identical(Member(Value, "metadata")!, mapper.Restore(observed.Metadata)))
            {
                throw new ArgumentException("The Group observation rolls back or contradicts its committed source epoch.");
            }
        }

        internal static RegistryObjectValueDataType Encode(FederationGroupSnapshot observation, RegistryRecordMapper mapper)
        {
            RegistryEntityReferenceDataType source = observation.Source;
            return new RegistryObjectValueDataType
            {
                Kind = 5,
                Members =
                [
                    Field("schema", "group-reference-v1"),
                    Field("originuri", source.OriginUri ?? string.Empty),
                    Field("originapplicationuri", source.ApplicationUri ?? string.Empty),
                    Field("originregistrynode", Format(source.RegistryNode)),
                    Field("applicationuri", observation.ApplicationUri),
                    Field("registryroot", Format(observation.RegistryRoot)),
                    Field("xid", source.Xid!),
                    Field("role", source.Role!),
                    Field("locator", source.Locator!),
                    Field("nativetarget", Format(source.NativeTarget)),
                    new RegistryMemberDataType
                    {
                        Name = "hasnativetarget",
                        Value = new RegistryBooleanValueDataType { Kind = 1, Value = source.HasNativeTarget }
                    },
                    new RegistryMemberDataType
                    {
                        Name = "epoch",
                        Value = RegistryValues.Parse(System.Text.Encoding.UTF8.GetBytes(
                            observation.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    },
                    new RegistryMemberDataType { Name = "metadata", Value = mapper.Restore(observation.Metadata) }
                ]
            };
        }

        internal static RegistryObjectValueDataType LocalDefinition(
            FederationGroupSnapshot observed, string localXid, RegistryObjectValueDataType pins, RegistryRecordMapper mapper)
        {
            var document = (RegistryObjectValueDataType)mapper.Restore(observed.Metadata);
            StripOwned(document);
            string idName = localXid.StartsWith("/endpoints/", StringComparison.Ordinal) ? "endpointid" : "messagegroupid";
            var members = new List<RegistryMemberDataType>();
            foreach (RegistryMemberDataType member in document.Members)
            {
                if (member.Name != idName && member.Name != Attribute)
                {
                    members.Add(member);
                }
            }
            members.Add(Field(idName, localXid.Split('/')[2]));
            members.Add(new RegistryMemberDataType { Name = Attribute, Value = pins });
            document.Members = members.ToArray();
            return document;
        }

        private static void StripOwned(RegistryObjectValueDataType value)
        {
            var members = new List<RegistryMemberDataType>();
            foreach (RegistryMemberDataType member in value.Members)
            {
                if (member.Name is "epoch" or "self" or "xid" or "createdat" or "modifiedat")
                {
                    continue;
                }
                if (member.Name is "messages" or "versions" && member.Value is RegistryObjectValueDataType children)
                {
                    foreach (RegistryMemberDataType child in children.Members)
                    {
                        if (child.Value is RegistryObjectValueDataType entity)
                        {
                            StripOwned(entity);
                        }
                    }
                }
                members.Add(member);
            }
            value.Members = members.ToArray();
        }

        internal static RegistryValueDataType? Member(RegistryObjectValueDataType value, string name)
        {
            foreach (RegistryMemberDataType member in value.Members)
            {
                if (member.Name == name)
                {
                    return member.Value;
                }
            }
            return null;
        }

        private static string Text(RegistryObjectValueDataType value, string name) =>
            Member(value, name) is RegistryStringValueDataType text && text.Value is not null ? text.Value :
                throw new ArgumentException("A durable Group pin field is missing or has another type: " + name);

        private static RegistryMemberDataType Field(string name, string value) => new()
        {
            Name = name,
            Value = new RegistryStringValueDataType { Kind = 2, Value = value }
        };

        private static string Format(ExpandedNodeId value) => value.IsNull ? string.Empty : value.ToString();
        private static ExpandedNodeId Node(string value) => value.Length == 0 ? ExpandedNodeId.Null : ExpandedNodeId.Parse(value);

        private static void Portable(ExpandedNodeId value)
        {
            if (value.IsNull || value.NamespaceIndex != 0 || value.ServerIndex != 0 ||
                !Uri.TryCreate(value.NamespaceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("Durable Group pins require URI-qualified NodeIds without Session indexes.");
            }
        }

        private readonly RegistryEntityReferenceDataType m_source;
        private readonly string m_application;
        private readonly ExpandedNodeId m_root;
    }
}
