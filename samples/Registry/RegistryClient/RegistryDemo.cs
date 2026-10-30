using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.EndpointRegistry;
using Opc.Ua.EndpointRegistry.Client;
using Opc.Ua.Identity;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.SchemaRegistry.Client;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Client;
using NativeEndpoint = Opc.Ua.EndpointRegistry.EndpointDataType;

namespace Opc.Ua.Registry.Samples
{
    /// <summary>Observable results of the native demonstration, without parsing documents or connecting a broker.</summary>
    public sealed record RegistryDemoResult
    {
        /// <summary>Gets the persisted Message Group epoch.</summary>
        public uint Epoch { get; init; }
        /// <summary>Gets the advertised Endpoint protocol.</summary>
        public string Protocol { get; init; } = string.Empty;
        /// <summary>Gets the native Message MQTT topic.</summary>
        public string Topic { get; init; } = string.Empty;
        /// <summary>Gets the authored schema reference.</summary>
        public string SchemaReference { get; init; } = string.Empty;
        /// <summary>Gets the oversized String read through a snapshot.</summary>
        public string Description { get; init; } = string.Empty;
        /// <summary>Gets the persisted typed field update read through the same pinned snapshot.</summary>
        public string GroupName { get; init; } = string.Empty;
        /// <summary>Gets the inherited content type resolved from a local base Message.</summary>
        public string ResolvedContentType { get; init; } = string.Empty;
        /// <summary>Gets the optional exact schema fingerprint.</summary>
        public ByteString SchemaFingerprint { get; init; }
        /// <summary>Gets the number of bounded native String responses.</summary>
        public int SnapshotParts { get; init; }
    }

    /// <summary>Runnable native workflows shared by the CLI and real TCP smoke tests.</summary>
    public static class RegistryDemo
    {
        /// <summary>Gets a String too large for one response, including a Unicode supplementary scalar.</summary>
        public static string LargeDescription { get; } = new string('x', 48 * 1024) + "🌡";

        /// <summary>Gets the application-authored schema namespace source identity.</summary>
        public const string SchemaNamespace = "urn:registry-sample:schema-namespace";
        /// <summary>Gets the application-authored schema subject.</summary>
        public const string SchemaName = "Temperature";
        /// <summary>Gets the sample schema origin, distinct from a network locator.</summary>
        public const string SchemaOrigin = "urn:registry-sample:schemas";
        /// <summary>Gets the explicitly authored exact Version URI, never derived by stripping a logical URI.</summary>
        public const string SchemaEntityUri = "urn:registry-sample:temperature:v1";
        /// <summary>Gets the explicitly authored logical Resource URI.</summary>
        public const string SchemaResourceUri = "urn:registry-sample:temperature";
        /// <summary>Gets the logical Resource Xid derived from configured source identities.</summary>
        public static string SchemaResourceXid { get; } = "/schemagroups/" +
            XRegistryIdentifier.FromSourceIdentity(SchemaNamespace) + "/schemas/" +
            XRegistryIdentifier.FromSourceIdentity(SchemaName + "/jsonschema");

        /// <summary>
        /// Connects on SignAndEncrypt with a username provider and runs one bounded demonstration.
        /// Certificate auto-accept is an explicit development-only choice.
        /// </summary>
        public static async Task<RegistryDemoResult> ConnectAndRunAsync(
            string endpointUrl, string userName, string password, string pkiDirectory, bool autoAccept = false,
            bool includeSchema = false, bool verifyOnly = false, CancellationToken cancellationToken = default)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Services.AddOpcUa().AddClient(options =>
            {
                options.ApplicationName = "RegistryClient";
                options.ApplicationUri = "urn:localhost:OPCFoundation:RegistryClient";
                options.ProductUri = "uri:opcfoundation.org:RegistryClient";
                options.PkiRoot = pkiDirectory;
                options.AutoAcceptUntrustedCertificates = autoAccept;
                options.MinimumCertificateKeySize = 2048;
                options.RejectSHA1SignedCertificates = true;
                options.Session = new ManagedSessionOptions
                {
                    SessionName = "NativeRegistryDemo",
                    SessionTimeout = TimeSpan.FromSeconds(60),
                    IdentityProvider = new SampleIdentityProvider(userName, password)
                };
            }).AddDiscoveryAndConnect(options =>
            {
                options.DiscoveryUrl = endpointUrl;
                options.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                options.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            });
            builder.Services.AddEndpointRegistryClient().AddSchemaRegistryClient();
            using IHost host = builder.Build();
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Func<CancellationToken, Task<ManagedSession>> connect =
                    host.Services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
                ManagedSession session = await connect(cancellationToken).ConfigureAwait(false);
                await using (session.ConfigureAwait(false))
                {
                    return await RunAsync(session, host.Services, includeSchema, verifyOnly, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>Runs against an application-owned session using DI and directly constructed client modules.</summary>
        public static async Task<RegistryDemoResult> RunAsync(
            ISession session, IServiceProvider services, bool includeSchema = false, bool verifyOnly = false,
            CancellationToken cancellationToken = default)
        {
            if (session.ConfiguredEndpoint.Description.SecurityMode != MessageSecurityMode.SignAndEncrypt)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityModeInsufficient);
            }
            ITelemetryContext telemetry = services.GetRequiredService<ITelemetryContext>();
            EndpointRegistryClient client = await services.GetRequiredService<EndpointRegistryClientFactory>()
                .DiscoverAsync(session, cancellationToken: cancellationToken).ConfigureAwait(false);
            EndpointRegistryClient media = await EndpointRegistryClient.DiscoverAsync(
                session, telemetry, media: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (media.RegistryNodeId == client.RegistryNodeId || media.TypedAccessNodeId == client.TypedAccessNodeId)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "Media and generic roots are not isolated.");
            }
            if (!verifyOnly)
            {
                await EnsureDocumentAsync(client, "/endpoints/sample", Endpoint(), cancellationToken)
                    .ConfigureAwait(false);
                uint groupEpoch = await EnsureDocumentAsync(client, "/messagegroups/sample", Messages(), cancellationToken)
                    .ConfigureAwait(false);
                Check(await client.ApplyChangesAsync(new RegistryChangeRequestDataType
                {
                    TargetXid = "/messagegroups/sample",
                    ExpectedEpoch = groupEpoch,
                    Changes =
                    [
                        new RegistryChangeDataType
                        {
                            Operation = 0,
                            Path = [new RegistryPathElementDataType { Kind = 0, Name = "name" }],
                            Value = new RegistryStringValueDataType { Kind = 2, Value = "Updated reusable Message Group" }
                        }
                    ]
                }, cancellationToken).ConfigureAwait(false));
            }
            RegistryReadResultDataType endpoint = await ReadAsync(client, "/endpoints/sample", cancellationToken)
                .ConfigureAwait(false);
            if (!endpoint.Document.TryGetValue(out NativeEndpoint? typedEndpoint) ||
                typedEndpoint?.ProtocolOptions is not EndpointProtocolOptionsMQTT50DataType)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Expected native MQTT Endpoint options.");
            }
            RegistryReadResultDataType message = await ReadAsync(client,
                "/messagegroups/sample/messages/temperature", cancellationToken).ConfigureAwait(false);
            if (!message.Document.TryGetValue(out MessageDefinitionDataType? typedMessage) ||
                typedMessage?.ProtocolOptions is not MessageDefinitionProtocolOptionsMQTT50DataType mqtt)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Expected native MQTT Message options.");
            }
            RegistrySnapshotClient snapshot = await client.OpenSnapshotAsync(
                new RegistrySnapshotOpenRequestDataType
                {
                    TargetXid = "/messagegroups/sample",
                    DocumentKind = "metadata",
                    View = 1
                }, maxBytes: 8192, cancellationToken).ConfigureAwait(false);
            await using (snapshot.ConfigureAwait(false))
            {
                var text = new StringBuilder();
                int parts = 0;
                await foreach (RegistrySnapshotReadResultDataType part in snapshot.ReadPartsAsync(
                    [new RegistryPathElementDataType { Kind = 0, Name = "Description" }], cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (part.Kind != 1 || !part.Value.TryGetValue(out string chunk))
                    {
                        throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Expected a native String part.");
                    }
                    text.Append(chunk);
                    parts++;
                }
                string groupName = await snapshot.ReadStringAsync(
                    [new RegistryPathElementDataType { Kind = 0, Name = "Name" }], cancellationToken)
                    .ConfigureAwait(false);
                if (text.ToString() != LargeDescription || groupName != "Updated reusable Message Group")
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "The pinned native String or typed update differs from the authored sample.");
                }
                NativeMessageResolutionResultDataType resolved = await client.ResolveMessageAsync(
                    new MessageResolutionRequestDataType { Reference = "/messagegroups/sample/messages/derived" },
                    cancellationToken).ConfigureAwait(false);
                Check(resolved.StatusCode, resolved.Issues);
                if (resolved.Status != "complete" || resolved.Definition is null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "Local base Message resolution is incomplete: " + resolved.Status);
                }
                ByteString schemaId = includeSchema
                    ? await SchemaAsync(session, services, verifyOnly, cancellationToken).ConfigureAwait(false) : default;
                return new RegistryDemoResult
                {
                    Epoch = snapshot.TargetEpoch,
                    Protocol = typedEndpoint.Protocol ?? throw new ServiceResultException(StatusCodes.BadDecodingError),
                    Topic = mqtt.TopicName ?? throw new ServiceResultException(StatusCodes.BadDecodingError),
                    SchemaReference = typedMessage.DataSchemaUri ?? throw new ServiceResultException(StatusCodes.BadDecodingError),
                    Description = text.ToString(),
                    GroupName = groupName,
                    ResolvedContentType = resolved.Definition.DataContentType ??
                        throw new ServiceResultException(StatusCodes.BadDecodingError),
                    SchemaFingerprint = schemaId,
                    SnapshotParts = parts
                };
            }
        }

        private static async Task<RegistryReadResultDataType> ReadAsync(
            EndpointRegistryClient client, string xid, CancellationToken cancellationToken)
        {
            RegistryReadResultDataType result = await client.ReadDocumentAsync(new RegistryReadRequestDataType
            {
                TargetXid = xid,
                DocumentKind = "metadata",
                View = 1,
                MaxItems = 64
            }, cancellationToken).ConfigureAwait(false);
            Check(result.StatusCode, result.Issues);
            return result;
        }

        private static async Task<uint> EnsureDocumentAsync(
            EndpointRegistryClient client, string xid, RegistryRecordDataType definition,
            CancellationToken cancellationToken)
        {
            RegistryReadResultDataType existing = await client.ReadDocumentAsync(new RegistryReadRequestDataType
            {
                TargetXid = xid, DocumentKind = "metadata", View = 1, MaxItems = 64
            }, cancellationToken).ConfigureAwait(false);
            if (existing.StatusCode != StatusCodes.BadNotFound)
            {
                Check(existing.StatusCode, existing.Issues);
                return existing.Epoch;
            }
            RegistryMutationResultDataType created = await client.WriteDocumentAsync(new RegistryWriteRequestDataType
            {
                TargetXid = xid, Definition = client.Canonicalize(definition)
            }, cancellationToken).ConfigureAwait(false);
            Check(created);
            return created.Epoch;
        }

        private static NativeEndpoint Endpoint() => new()
        {
            PresentFields = ["EndpointId", "Usage", "Protocol", "ProtocolOptions"],
            EndpointId = "sample",
            Usage = ["producer"],
            Protocol = "MQTT/5.0",
            ProtocolOptions = new EndpointProtocolOptionsMQTT50DataType
            {
                PresentFields = ["Endpoints", "Topic"],
                Topic = "factory/line1/temperature",
                Endpoints =
                [
                    new EndpointProtocolOptionsMQTT50EndpointsDataTypeItem
                    {
                        PresentFields = ["Uri"], Uri = "mqtts://broker.example.test"
                    }
                ]
            }
        };

        private static MessageGroupDataType Messages() => new()
        {
            PresentFields = ["MessageGroupId", "Description", "Messages"],
            MessageGroupId = "sample",
            Description = LargeDescription,
            Messages = new MessageDefinitionMapDataType
            {
                Entries =
                [
                    new MessageDefinitionMapEntryDataType
                    {
                        Name = "temperature",
                        Value = new MessageDefinitionDataType
                        {
                            PresentFields = ["MessageId", "Protocol", "DataSchemaFormat", "DataSchemaUri", "ProtocolOptions"],
                            MessageId = "temperature",
                            Protocol = "MQTT/5.0",
                            DataSchemaFormat = "JsonSchema/2020-12",
                            DataSchemaUri = "urn:registry-sample:temperature",
                            ProtocolOptions = new MessageDefinitionProtocolOptionsMQTT50DataType
                            {
                                PresentFields = ["TopicName"], TopicName = "factory/line1/temperature"
                            }
                        }
                    },
                    new MessageDefinitionMapEntryDataType
                    {
                        Name = "base",
                        Value = new MessageDefinitionDataType
                        {
                            PresentFields = ["MessageId", "DataContentType"],
                            MessageId = "base", DataContentType = "application/json"
                        }
                    },
                    new MessageDefinitionMapEntryDataType
                    {
                        Name = "derived",
                        Value = new MessageDefinitionDataType
                        {
                            PresentFields = ["MessageId", "BaseMessageUri"],
                            MessageId = "derived", BaseMessageUri = "/messagegroups/sample/messages/base"
                        }
                    }
                ]
            }
        };

        private static async Task<ByteString> SchemaAsync(
            ISession session, IServiceProvider services, bool verifyOnly, CancellationToken cancellationToken)
        {
            SchemaRegistryClient client = services.GetRequiredService<SchemaRegistryClientFactory>().Create(session);
            var reference = new SchemaReferenceDataType
            {
                Entity = new RegistryEntityReferenceDataType
                {
                    OriginUri = SchemaOrigin,
                    Xid = SchemaResourceXid + "/versions/1",
                    Role = "ExactVersion"
                },
                EntityUri = SchemaEntityUri,
                SelectedObjectUri = SchemaEntityUri,
                Format = "JsonSchema/2020-12"
            };
            if (!verifyOnly)
            {
                TypedSchemaReadResultDataType registered = await client.RegisterSchemaAsync(
                    new TypedSchemaRegistrationRequestDataType
                    {
                        Registration = new SchemaRegistrationDataType
                        {
                            NamespaceUri = SchemaNamespace,
                            SchemaName = SchemaName,
                            Format = "JsonSchema/2020-12",
                            VersionId = "1",
                            EntityUri = SchemaEntityUri,
                            ResourceUri = SchemaResourceUri,
                            MakeDefault = true
                        },
                        Content = new JsonSchemaContentDataType
                        {
                            Format = "JsonSchema/2020-12",
                            Root = new JsonSchemaBooleanDataType { PresentFields = ["Value"], Value = false }
                        }
                    }, cancellationToken).ConfigureAwait(false);
                Check(registered.StatusCode, registered.Issues);
                var updateReference = (SchemaReferenceDataType)registered.Document.Reference.Clone();
                // A write's optional fingerprint claims the new content, not the previously read content.
                updateReference.SchemaIdAlg = string.Empty;
                updateReference.SchemaId = default;
                TypedSchemaReadResultDataType written = await client.WriteSchemaAsync(new TypedSchemaWriteRequestDataType
                {
                    Reference = updateReference,
                    ExpectedEpoch = registered.Document.Epoch,
                    Content = new JsonSchemaContentDataType
                    {
                        Format = "JsonSchema/2020-12",
                        Root = new JsonSchemaBooleanDataType { PresentFields = ["Value"], Value = true }
                    }
                }, cancellationToken).ConfigureAwait(false);
                Check(written.StatusCode, written.Issues);
                reference = written.Document.Reference;
            }
            TypedSchemaReadResultDataType read = await client.ReadSchemaAsync(reference, cancellationToken)
                .ConfigureAwait(false);
            Check(read.StatusCode, read.Issues);
            var logical = (SchemaReferenceDataType)read.Document.Reference.Clone();
            logical.Entity.Xid = SchemaResourceXid;
            logical.Entity.Role = "LogicalResource";
            logical.EntityUri = SchemaResourceUri;
            logical.SelectedObjectUri = SchemaResourceUri;
            TypedSchemaReadResultDataType defaultVersion = await client.ReadSchemaAsync(logical, cancellationToken)
                .ConfigureAwait(false);
            Check(defaultVersion.StatusCode, defaultVersion.Issues);
            if (defaultVersion.Document.Reference.Entity.Xid != read.Document.Reference.Entity.Xid ||
                defaultVersion.Document.Reference.SchemaId != read.Document.Reference.SchemaId)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The logical default did not select the exact Version.");
            }
            (ByteString bytes, string format, _) = await client.GetSchemaAsync(
                read.Document.Reference.SchemaId, cancellationToken).ConfigureAwait(false);
            if (bytes.IsNull || format != "JsonSchema/2020-12" ||
                read.Document.Content is not JsonSchemaContentDataType { Root: JsonSchemaBooleanDataType { Value: true } })
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Unexpected exact native schema.");
            }
            RegistrySnapshotClient snapshot = await client.OpenSnapshotAsync(
                new RegistrySnapshotOpenRequestDataType
                {
                    TargetXid = read.Document.Reference.Entity.Xid,
                    DocumentKind = "schema",
                    View = 1,
                    ExpectedEpoch = read.Document.Epoch
                }, cancellationToken: cancellationToken).ConfigureAwait(false);
            await using (snapshot.ConfigureAwait(false))
            {
                ByteString fingerprint = await snapshot.ReadByteStringAsync(
                [
                    new RegistryPathElementDataType { Kind = 0, Name = "Reference" },
                new RegistryPathElementDataType { Kind = 0, Name = "SchemaId" }
                ], cancellationToken).ConfigureAwait(false);
                if (fingerprint != read.Document.Reference.SchemaId)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "Schema snapshot fingerprint differs.");
                }
                return fingerprint;
            }
        }

        private static void Check(RegistryMutationResultDataType result) => Check(result.StatusCode, result.Issues);

        private static void Check(StatusCode status, ArrayOf<RegistryDiagnosticDataType> issues)
        {
            if (StatusCode.IsBad(status))
            {
                var message = new StringBuilder("Native registry operation failed.");
                foreach (RegistryDiagnosticDataType issue in issues)
                {
                    message.Append(' ').Append(issue.Code).Append(": ").Append(issue.Detail);
                }
                throw new ServiceResultException(status, message.ToString());
            }
        }

        private sealed class SampleIdentityProvider(string userName, string password) : IClientIdentityProvider
        {
            public IReadOnlyList<UserTokenType> SupportedTokenTypes { get; } = [UserTokenType.UserName];
            public IReadOnlyList<string> SupportedIssuedTokenProfileUris { get; } = [];
            public DateTime ExpiresAt => DateTime.MaxValue;

            public ValueTask<CanSatisfyResult> CanSatisfyAsync(
                UserTokenPolicy policy, IdentitySelectionContext context, CancellationToken ct = default) =>
                new(policy.TokenType == UserTokenType.UserName
                    ? CanSatisfyResult.Yes : CanSatisfyResult.No("Username authentication is required."));

            public ValueTask<IUserIdentity> GetIdentityAsync(
                UserTokenPolicy policy, IdentitySelectionContext context, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                if (policy.TokenType != UserTokenType.UserName)
                {
                    throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected);
                }
                return new ValueTask<IUserIdentity>(new UserIdentity(userName, Encoding.UTF8.GetBytes(password)));
            }

            public ValueTask InvalidateAsync(CancellationToken ct = default) => default;
        }
    }
}
