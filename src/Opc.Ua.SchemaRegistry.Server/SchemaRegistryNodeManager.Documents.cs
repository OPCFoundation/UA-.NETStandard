/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.SchemaRegistry.Server
{
    public sealed partial class SchemaRegistryNodeManager
    {
        private void BindDocuments(ISystemContext context, NativeRegistryAccessState access)
        {
            access.AddReadDocument(context);
            access.ReadDocument!.OnCallAsync = (caller, _, _, request, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                ServiceResult allowed = m_authorize(caller, RegistryAccessKind.Read);
                if (ServiceResult.IsBad(allowed))
                {
                    return new ValueTask<ReadDocumentMethodStateResult>(
                        new ReadDocumentMethodStateResult { ServiceResult = allowed });
                }
                RegistryReadResultDataType result;
                try
                {
                    if (request.MaxItems == 0 || request.View > 1)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                    }
                    if (request.ContinuationPoint.Length != 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadContinuationPointInvalid);
                    }
                    RegistrySnapshotSource document = SharedDocument(caller, request.TargetXid, request.DocumentKind,
                        request.View);
                    if (document.Document is RegistryObjectValueDataType generic &&
                        generic.Members.Count > request.MaxItems)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "Use OpenDocument/ReadDocumentPart for the requested page bound.");
                    }
                    result = new RegistryReadResultDataType
                    {
                        StatusCode = StatusCodes.Good,
                        Epoch = document.TargetEpoch,
                        Document = new ExtensionObject(document.Document),
                        ContinuationPoint = ByteString.Empty,
                        Issues = []
                    };
                    RegistryEncodedSize.Measure(result, m_documentContext, m_snapshots.Limits.MaxReadBytes);
                }
                catch (ServiceResultException error)
                {
                    result = new RegistryReadResultDataType
                    {
                        StatusCode = error.StatusCode,
                        Document = ExtensionObject.Null,
                        ContinuationPoint = ByteString.Empty,
                        Issues =
                        [
                            new RegistryDiagnosticDataType
                            {
                                StatusCode = error.StatusCode, Code = "E_NATIVE_INPUT", Path = [], Detail = error.Message
                            }
                        ]
                    };
                }
                return new ValueTask<ReadDocumentMethodStateResult>(
                    new ReadDocumentMethodStateResult { ServiceResult = ServiceResult.Good, Result = result });
            };
        }

        private RegistrySnapshotSource SharedDocument(ISystemContext caller, string? target, string? kind, uint view)
        {
            if (target is not (null or "" or "/") || view > 1)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                    "Shared schema registry documents select the root.");
            }
            RegistryObjectValueDataType value;
            string recordType;
            (ArrayOf<SchemaRegistryStore.SchemaEntry> entries, uint epoch) = m_store.CaptureGeneration();
            if (kind == "metadata")
            {
                value = new RegistryObjectValueDataType
                {
                    Kind = 5,
                    Members =
                    [
                        Member("registryid", Text(m_options.RegistryId)),
                        Member("specversion", Text("1.0-rc4")),
                        Member("epoch", Number(epoch)),
                        Member("schemagroups", SchemaGroups(caller, entries))
                    ]
                };
                recordType = nameof(RegistryMetadataDataType);
            }
            else if (kind == "model")
            {
                value = (RegistryObjectValueDataType)RegistryValues.Parse(
                    """
                    {"groups":{"schemagroups":{"singular":"schemagroup","plural":"schemagroups",
                      "resources":{"schemas":{"singular":"schema","plural":"schemas",
                       "hasdocument":true,"maxversions":0}}}}}
                    """u8);
                recordType = nameof(RegistryModelDocumentDataType);
            }
            else if (kind == "capabilities")
            {
                var formats = new List<RegistryValueDataType>();
                foreach (ISchemaFormatProvider provider in m_formats)
                {
                    formats.Add(Text(provider.Format));
                }
                value = new RegistryObjectValueDataType
                {
                    Kind = 5,
                    Members =
                    [
                        Member("formats", new RegistryArrayValueDataType { Kind = 4, Items = formats.ToArray() }),
                        Member("pagination", new RegistryBooleanValueDataType { Kind = 1, Value = false })
                    ]
                };
                recordType = nameof(RegistryCapabilitiesDocumentDataType);
            }
            else
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The document kind is unsupported.");
            }
            IEncodeable native = view == 0 ? value : m_documentMapper.Project(value, recordType);
            var visibleReferences = new List<SchemaReferenceDataType>();
            if (kind == "metadata")
            {
                foreach (SchemaRegistryStore.SchemaEntry entry in entries)
                {
                    if (Included(value, entry.Reference.Entity.Xid!))
                    {
                        visibleReferences.Add((SchemaReferenceDataType)entry.Reference.Clone());
                    }
                }
            }
            return new RegistrySnapshotSource(native, epoch, epoch)
            {
                Reauthorize = () =>
                {
                    ServiceResult allowed = m_authorize(caller, RegistryAccessKind.Read);
                    if (ServiceResult.IsBad(allowed))
                    {
                        return allowed;
                    }
                    foreach (SchemaReferenceDataType reference in visibleReferences)
                    {
                        if (!IsVisible(caller, reference))
                        {
                            return StatusCodes.BadUserAccessDenied;
                        }
                    }
                    return ServiceResult.Good;
                }
            };
        }

        private static bool Included(RegistryObjectValueDataType root, string xid)
        {
            RegistryValueDataType? selected = root;
            foreach (string segment in xid.Substring(1).Split('/'))
            {
                RegistryValueDataType? child = null;
                if (selected is RegistryObjectValueDataType map)
                {
                    foreach (RegistryMemberDataType member in map.Members)
                    {
                        if (member.Name == segment)
                        {
                            child = member.Value;
                            break;
                        }
                    }
                }
                selected = child;
            }
            return selected is not null;
        }

        private RegistryObjectValueDataType SchemaGroups(
            ISystemContext caller, ArrayOf<SchemaRegistryStore.SchemaEntry> entries)
        {
            var groups = new Dictionary<string, RegistryObjectValueDataType>(StringComparer.Ordinal);
            foreach (SchemaRegistryStore.SchemaEntry entry in entries)
            {
                if (!IsVisible(caller, entry.Reference))
                {
                    continue;
                }
                string[] path = entry.Reference.Entity.Xid!.Split('/');
                string groupId = path[2];
                if (!groups.TryGetValue(groupId, out RegistryObjectValueDataType? schemas))
                {
                    schemas = new RegistryObjectValueDataType { Kind = 5, Members = [] };
                    groups.Add(groupId, schemas);
                }
                RegistryObjectValueDataType? resource = null;
                foreach (RegistryMemberDataType item in schemas.Members)
                {
                    if (item.Name == path[4])
                    {
                        resource = (RegistryObjectValueDataType)item.Value;
                    }
                }
                if (resource is null)
                {
                    resource = new RegistryObjectValueDataType
                    {
                        Kind = 5,
                        Members =
                        [
                            Member("schemaid", Text(path[4])),
                            Member("format", Text(entry.Format)),
                            Member("versions", new RegistryObjectValueDataType { Kind = 5, Members = [] })
                        ]
                    };
                    schemas.Members = [.. schemas.Members, Member(path[4], resource)];
                }
                var versions = (RegistryObjectValueDataType)resource.Members[2].Value;
                versions.Members =
                [
                    .. versions.Members,
                    Member(path[6], new RegistryObjectValueDataType
                    {
                        Kind = 5,
                        Members =
                        [
                            Member("versionid", Text(path[6])),
                            Member("epoch", Number(entry.Epoch)),
                            Member("self", Text(entry.Reference.EntityUri!)),
                            Member("xid", Text(entry.Reference.Entity.Xid!))
                        ]
                    })
                ];
            }
            var members = new List<RegistryMemberDataType>();
            foreach (KeyValuePair<string, RegistryObjectValueDataType> group in groups)
            {
                members.Add(Member(group.Key, new RegistryObjectValueDataType
                {
                    Kind = 5,
                    Members =
                    [
                        Member("schemagroupid", Text(group.Key)),
                        Member("schemas", group.Value)
                    ]
                }));
            }
            return new RegistryObjectValueDataType { Kind = 5, Members = members.ToArray() };
        }

        private static RegistryMemberDataType Member(string name, RegistryValueDataType value) =>
            new() { Name = name, Value = value };

        private static RegistryStringValueDataType Text(string value) => new() { Kind = 2, Value = value };

        private static RegistryNumberValueDataType Number(uint value) =>
            (RegistryNumberValueDataType)RegistryValues.Parse(System.Text.Encoding.UTF8.GetBytes(
                value.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        private readonly IServiceMessageContext m_documentContext;
        private readonly RegistryRecordMapper m_documentMapper;
        private readonly ArrayOf<ISchemaFormatProvider> m_formats;
    }
}
