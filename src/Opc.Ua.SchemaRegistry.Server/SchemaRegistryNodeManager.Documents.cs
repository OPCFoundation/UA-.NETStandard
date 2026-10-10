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
            if (view > 1 || kind != "metadata" && target is not (null or "" or "/"))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                    "Shared schema registry documents select the root.");
            }
            RegistryObjectValueDataType value;
            string recordType;
            SchemaRegistryStore.Generation catalog = m_store.CaptureCatalog();
            ArrayOf<SchemaRegistryStore.SchemaEntry> entries = [.. catalog.Entries.Values];
            uint registryEpoch = checked((uint)Math.Max(1ul, catalog.Revision));
            uint epoch = registryEpoch;
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
                        Member("schemagroups", SchemaGroups(caller, catalog))
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
                        Member("pagination", new RegistryBooleanValueDataType { Kind = 1, Value = false }),
                        Member("mutable", new RegistryArrayValueDataType { Kind = 4, Items = [Text("entities")] }),
                        Member("flags", new RegistryArrayValueDataType { Kind = 4, Items = [] }),
                        Member("specversions", new RegistryArrayValueDataType { Kind = 4, Items = [Text("1.0-rc4")] }),
                        Member("shortself", new RegistryBooleanValueDataType { Kind = 1, Value = false }),
                        Member("stickyversions", new RegistryBooleanValueDataType { Kind = 1, Value = true }),
                        Member("enforcecompatibility", new RegistryBooleanValueDataType
                        {
                            Kind = 1, Value = m_options.VerifyCompatibility is not null
                        }),
                        Member("apis", new RegistryArrayValueDataType { Kind = 4, Items = [] }),
                        Member("schemas", new RegistryArrayValueDataType { Kind = 4, Items = formats.ToArray() })
                    ]
                };
                recordType = nameof(RegistryCapabilitiesDocumentDataType);
            }
            else
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The document kind is unsupported.");
            }
            var visibleReferences = new List<SchemaReferenceDataType>();
            if (kind == "metadata")
            {
                foreach (SchemaRegistryStore.SchemaEntry entry in entries)
                {
                    if (Included(value, entry.Reference.Entity.Xid!) &&
                        (target is null or "" or "/" || entry.Reference.Entity.Xid == target ||
                            entry.Reference.Entity.Xid!.StartsWith(target + "/", StringComparison.Ordinal)))
                    {
                        visibleReferences.Add((SchemaReferenceDataType)entry.Reference.Clone());
                    }
                    foreach (SchemaRegistrationDataType draft in catalog.Drafts.Values)
                    {
                        SchemaReferenceDataType reference = m_store.RegistrationReference(draft);
                        if (Included(value, reference.Entity.Xid!) && (target is null or "" or "/" ||
                            reference.Entity.Xid == target || reference.Entity.Xid!.StartsWith(target + "/", StringComparison.Ordinal)))
                        {
                            visibleReferences.Add(reference);
                        }
                    }
                }
            }
            if (kind == "metadata" && target is not (null or "" or "/"))
            {
                string[] segments = target[0] == '/' ? target.Substring(1).Split('/') : [];
                if (segments.Length is not (2 or 4 or 6) || segments[0] != "schemagroups" ||
                    segments.Length >= 4 && segments[2] != "schemas" ||
                    segments.Length == 6 && segments[4] != "versions")
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
                RegistryValueDataType selected = value;
                foreach (string segment in segments)
                {
                    if (selected is not RegistryObjectValueDataType map)
                    {
                        throw new ServiceResultException(StatusCodes.BadNotFound);
                    }
                    selected = FindMember(map, segment) ?? throw new ServiceResultException(StatusCodes.BadNotFound);
                }
                value = (RegistryObjectValueDataType)selected;
                if (segments.Length == 4 && catalog.Defaults.TryGetValue(target,
                    out SchemaRegistryStore.DefaultSelection? defaultSelection))
                {
                    SchemaRegistryStore.SchemaEntry defaultEntry = catalog.Entries[defaultSelection.VersionXid];
                    if (!IsVisible(caller, defaultEntry.Reference))
                    {
                        throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                    }
                    epoch = defaultEntry.Epoch;
                }
                else
                {
                    epoch = segments.Length == 6 &&
                        catalog.Entries.TryGetValue(target, out SchemaRegistryStore.SchemaEntry? entry)
                            ? entry.Epoch : catalog.EntityEpochs[target];
                }
            }
            IEncodeable native = view == 0 ? value : m_documentMapper.Project(value, recordType);
            return new RegistrySnapshotSource(native, epoch, registryEpoch)
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
            ISystemContext caller, SchemaRegistryStore.Generation catalog)
        {
            var groups = new Dictionary<string, RegistryObjectValueDataType>(StringComparer.Ordinal);
            var namespaceUris = new Dictionary<string, string>(catalog.Groups, StringComparer.Ordinal);
            foreach (SchemaRegistryStore.SchemaEntry entry in catalog.Entries.Values)
            {
                if (!IsVisible(caller, entry.Reference))
                {
                    continue;
                }
                string[] path = entry.Reference.Entity.Xid!.Split('/');
                string groupId = path[2];
                namespaceUris[groupId] = entry.Registration?.NamespaceUri ?? m_options.NamespaceUris[groupId];
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
                            Member("name", Text(entry.Registration?.SchemaName ?? m_options.SchemaNames[
                                "/schemagroups/" + groupId + "/schemas/" + path[4]])),
                            Member("xid", Text("/schemagroups/" + groupId + "/schemas/" + path[4])),
                            Member("epoch", Number(catalog.EntityEpochs[
                                "/schemagroups/" + groupId + "/schemas/" + path[4]])),
                            Member("versions", new RegistryObjectValueDataType { Kind = 5, Members = [] })
                        ]
                    };
                    schemas.Members = [.. schemas.Members, Member(path[4], resource)];
                }
                var versions = (RegistryObjectValueDataType)FindMember(resource, "versions")!;
                var labels = new List<RegistryMemberDataType>
                {
                    Member("opcua.schemafingerprint", Text(entry.Reference.SchemaId.ToHexString().ToLowerInvariant())),
                    Member("opcua.schemafingerprint.alg", Text(entry.Reference.SchemaIdAlg!))
                };
                if (entry.Metadata.ModelVersion is { } modelVersion)
                {
                    labels.Add(Member("opcua.modelversion", Text(modelVersion)));
                }
                if (entry.Metadata.DataTypeEncoding is { } encoding)
                {
                    labels.Add(Member("opcua.datatypeencoding", Text(encoding)));
                }
                if (entry.Metadata.ConfigurationVersion is { } configuration)
                {
                    labels.Add(Member("opcua.configurationversion", Text(
                        configuration.MajorVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." +
                        configuration.MinorVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))));
                }
                bool isDefault = catalog.Defaults.TryGetValue("/schemagroups/" + groupId + "/schemas/" + path[4],
                    out SchemaRegistryStore.DefaultSelection? chosen) && chosen.VersionXid == entry.Reference.Entity.Xid;
                if (isDefault)
                {
                    var delegated = new List<RegistryMemberDataType>();
                    foreach (RegistryMemberDataType member in resource.Members)
                    {
                        if (member.Name != "epoch")
                        {
                            delegated.Add(member);
                        }
                    }
                    delegated.Add(Member("epoch", Number(entry.Epoch)));
                    delegated.Add(Member("defaultversionid", Text(path[6])));
                    delegated.Add(Member("self", Text(chosen!.EntityUri)));
                    resource.Members = delegated.ToArray();
                }
                if (entry.Metadata.Compatibility is { } compatibility && FindMember(resource, "compatibility") is null)
                {
                    resource.Members = [.. resource.Members, Member("compatibility", Text(compatibility))];
                }
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
                            Member("xid", Text(entry.Reference.Entity.Xid!)),
                            Member("ancestor", Text(entry.Metadata.Ancestor)),
                            Member("contenttype", Text(entry.ContentType)),
                            Member("isdefault", new RegistryBooleanValueDataType { Kind = 1, Value = isDefault }),
                            Member("labels", new RegistryObjectValueDataType { Kind = 5, Members = labels.ToArray() })
                        ]
                    })
                ];
            }
            var members = new List<RegistryMemberDataType>();
            foreach (KeyValuePair<string, SchemaRegistrationDataType> item in catalog.Drafts)
            {
                SchemaReferenceDataType reference = m_store.RegistrationReference(item.Value);
                if (!IsVisible(caller, reference))
                {
                    continue;
                }
                string[] parts = item.Key.Split('/');
                namespaceUris[parts[2]] = item.Value.NamespaceUri!;
                if (!groups.TryGetValue(parts[2], out RegistryObjectValueDataType? schemas))
                {
                    schemas = new RegistryObjectValueDataType { Kind = 5, Members = [] };
                    groups.Add(parts[2], schemas);
                }
                string resourceXid = "/schemagroups/" + parts[2] + "/schemas/" + parts[4];
                var resource = FindMember(schemas, parts[4]) as RegistryObjectValueDataType;
                if (resource is null)
                {
                    resource = new RegistryObjectValueDataType
                    {
                        Kind = 5,
                        Members =
                        [
                            Member("schemaid", Text(parts[4])), Member("format", Text(item.Value.Format!)),
                            Member("name", Text(item.Value.SchemaName!)), Member("xid", Text(resourceXid)),
                            Member("epoch", Number(catalog.EntityEpochs[resourceXid])),
                            Member("versions", new RegistryObjectValueDataType { Kind = 5, Members = [] })
                        ]
                    };
                    schemas.Members = [.. schemas.Members, Member(parts[4], resource)];
                }
                var versions = (RegistryObjectValueDataType)FindMember(resource, "versions")!;
                versions.Members =
                [
                    .. versions.Members,
                    Member(parts[6], new RegistryObjectValueDataType
                    {
                        Kind = 5, Members =
                        [
                            Member("versionid", Text(parts[6])), Member("xid", Text(item.Key)),
                            Member("self", Text(item.Value.EntityUri!)), Member("epoch", Number(catalog.EntityEpochs[item.Key])),
                            Member("documentavailable", new RegistryBooleanValueDataType { Kind = 1, Value = false })
                        ]
                    })
                ];
            }
            foreach (KeyValuePair<string, string> group in namespaceUris)
            {
                members.Add(Member(group.Key, new RegistryObjectValueDataType
                {
                    Kind = 5,
                    Members =
                    [
                        Member("schemagroupid", Text(group.Key)),
                        Member("name", Text(group.Value)),
                        Member("xid", Text("/schemagroups/" + group.Key)),
                        Member("epoch", Number(catalog.EntityEpochs["/schemagroups/" + group.Key])),
                        Member("labels", new RegistryObjectValueDataType
                        {
                            Kind = 5, Members = [Member("opcua.namespaceuri", Text(group.Value))]
                        }),
                        Member("schemas", groups.TryGetValue(group.Key, out RegistryObjectValueDataType? schemas)
                            ? schemas : new RegistryObjectValueDataType { Kind = 5, Members = [] })
                    ]
                }));
            }
            return new RegistryObjectValueDataType { Kind = 5, Members = members.ToArray() };
        }

        private static RegistryMemberDataType Member(string name, RegistryValueDataType value) =>
            new() { Name = name, Value = value };

        private static RegistryValueDataType? FindMember(RegistryObjectValueDataType value, string name)
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

        private static RegistryStringValueDataType Text(string value) => new() { Kind = 2, Value = value };

        private static RegistryNumberValueDataType Number(uint value) =>
            (RegistryNumberValueDataType)RegistryValues.Parse(System.Text.Encoding.UTF8.GetBytes(
                value.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        private readonly IServiceMessageContext m_documentContext;
        private readonly RegistryRecordMapper m_documentMapper;
        private readonly ArrayOf<ISchemaFormatProvider> m_formats;
    }
}
