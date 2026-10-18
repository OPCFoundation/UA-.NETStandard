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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Di;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Exercises DI tool reads and commands through the generated clients on a scripted UA session.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class DiCoverageTests
    {
        /// <summary>
        /// Lock inspection preserves all four independent variables without invoking any method.
        /// </summary>
        [Test]
        public async Task LockReadPreservesValuesAndDoesNotChangeOwnershipAsync()
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            harness.Values["Locked"] = new DataValue(Variant.From(false));
            harness.Values["LockingClient"] = new DataValue(Variant.From("client-1"));
            harness.Values["LockingUser"] = new DataValue(Variant.From("operator"));
            harness.Values["RemainingLockTime"] = new DataValue(Variant.From(12.5));

            CallToolResult result = await new DiReadTools(harness.Manager)
                .ReadLockAsync("ns=2;s=lock", "selected").ConfigureAwait(false);

            JsonElement json = Success(result);
            Assert.That(json.GetProperty("locked").GetProperty("dataValue").GetProperty("Value").GetBoolean(),
                Is.False);
            Assert.That(json.GetProperty("lockingClient").GetProperty("dataValue").GetProperty("Value").GetString(),
                Is.EqualTo("client-1"));
            Assert.That(json.GetProperty("lockingUser").GetProperty("dataValue").GetProperty("Value").GetString(),
                Is.EqualTo("operator"));
            Assert.That(json.GetProperty("remainingLockTime").GetProperty("dataValue").GetProperty("Value").GetDouble(),
                Is.EqualTo(12.5));
            Assert.That(harness.Calls, Is.Empty);
        }

        /// <summary>
        /// Software inspection reports version fields and does not imply support for absent state machines.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task SoftwareReadPreservesVersionFacetsAndAbsenceAsync(bool hasLoading)
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            foreach (string name in new[] { "PrepareForUpdate", "Installation", "Confirmation", "PowerCycle" })
            {
                harness.Missing.Add(name);
            }
            if (!hasLoading)
            {
                harness.Missing.Add("Loading");
            }
            harness.Values["UpdateStatus"] = new DataValue(Variant.From(0u));
            harness.Values["VendorErrorCode"] = new DataValue(Variant.From(17u));
            harness.Values["Manufacturer"] = new DataValue(Variant.From(new LocalizedText("en", "Acme")));
            harness.Values["ManufacturerUri"] = new DataValue(Variant.From("urn:acme"));
            harness.Values["SoftwareRevision"] = new DataValue(Variant.From("2.1"));
            harness.Values["PatchIdentifiers"] = new DataValue(Variant.From(["patch-1", "patch-2"]));
            harness.Values["ReleaseDate"] = new DataValue(Variant.From(
                new DateTimeUtc(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc))));
            harness.Values["ChangeLogReference"] = new DataValue(Variant.From("urn:acme:changes"));
            harness.Values["Hash"] = new DataValue(Variant.From(new ByteString(new byte[] { 1, 2, 3 })));

            CallToolResult result = await new DiSoftwareUpdateTools(harness.Manager, harness.Transfers)
                .ReadSoftwareUpdateAsync("ns=2;s=software", "selected").ConfigureAwait(false);

            JsonElement json = Success(result);
            Assert.That(json.GetProperty("loading").GetProperty("supported").GetBoolean(), Is.EqualTo(hasLoading));
            Assert.That(json.GetProperty("updateStatus").GetProperty("dataValue").GetProperty("Value").GetUInt32(),
                Is.Zero);
            Assert.That(json.GetProperty("vendorErrorCode").GetProperty("dataValue").GetProperty("Value").GetUInt32(),
                Is.EqualTo(17));
            Assert.That(json.GetProperty("installation").GetProperty("supported").GetBoolean(), Is.False);
            Assert.That(json.GetProperty("prepareForUpdate").GetProperty("supported").GetBoolean(), Is.False);
            Assert.That(json.GetProperty("confirmation").GetProperty("supported").GetBoolean(), Is.False);
            Assert.That(json.GetProperty("powerCycle").GetProperty("supported").GetBoolean(), Is.False);
            if (hasLoading)
            {
                foreach (string versionName in new[] { "currentVersion", "pendingVersion", "fallbackVersion" })
                {
                    JsonElement version = json.GetProperty("loading").GetProperty(versionName);
                    Assert.That(version.GetProperty("supported").GetBoolean(), Is.True);
                    Assert.That(version.GetProperty("softwareRevision").GetProperty("dataValue")
                        .GetProperty("Value").GetString(), Is.EqualTo("2.1"));
                    Assert.That(version.GetProperty("patchIdentifiers").GetProperty("dataValue")
                        .GetProperty("Value")[1].GetString(), Is.EqualTo("patch-2"));
                }
            }
            Assert.That(harness.Calls, Is.Empty);
        }

        /// <summary>
        /// The exact selected property, localization and write status reach the caller.
        /// </summary>
        [TestCase(DiWritableProperty.AssetId, false)]
        [TestCase(DiWritableProperty.ComponentName, false)]
        [TestCase(DiWritableProperty.AssetId, true)]
        public async Task PropertyWritesPreserveTargetTypeAndStatusAsync(DiWritableProperty property, bool denied)
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            StatusCode status = denied ? StatusCodes.BadUserAccessDenied : StatusCodes.Good;
            harness.WriteStatus = status;
            CallToolResult result = await new DiCommandTools(harness.Manager).WritePropertyAsync(
                "ns=2;s=device", property, "updated", property == DiWritableProperty.ComponentName ? "fr" : null,
                "selected").ConfigureAwait(false);

            Assert.That(result.IsError, Is.EqualTo(StatusCode.IsBad(status)));
            Assert.That(harness.Writes, Has.Count.EqualTo(1));
            WriteValue write = harness.Writes[0];
            Assert.That(write.NodeId, Is.EqualTo(new NodeId("device/" + property, 2)));
            Assert.That(write.AttributeId, Is.EqualTo(Attributes.Value));
            if (property == DiWritableProperty.AssetId)
            {
                Assert.That(write.Value.WrappedValue.TryGetValue(out string value), Is.True);
                Assert.That(value, Is.EqualTo("updated"));
            }
            else
            {
                Assert.That(write.Value.WrappedValue.TryGetValue(out LocalizedText value), Is.True);
                Assert.That(value.Text, Is.EqualTo("updated"));
                Assert.That(value.Locale, Is.EqualTo("fr"));
            }
            Assert.That(result.StructuredContent!.Value.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(status.Code));
            Assert.That(harness.Calls, Is.Empty);
        }

        /// <summary>
        /// An absent writable property fails before any write is submitted.
        /// </summary>
        [Test]
        public async Task MissingPropertyPreventsWritesAsync()
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            harness.Missing.Add("AssetId");
            CallToolResult result = await new DiCommandTools(harness.Manager).WritePropertyAsync(
                "ns=2;s=device", DiWritableProperty.AssetId, "new-id", sessionName: "selected").ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(result.StructuredContent!.Value.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.BadNotFound.Code));
            Assert.That(harness.Writes, Is.Empty);
        }

        /// <summary>
        /// A property read preserves its value, or distinctly reports missing/bad quality.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task PropertyReadDistinguishesValueMissingAndBadQualityAsync(bool missing, bool badQuality)
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            StatusCode status = badQuality ? StatusCodes.BadNoData : StatusCodes.Good;
            harness.Values["HardwareRevision"] = new DataValue(Variant.From("HW-0"), status);
            if (missing)
            {
                harness.Missing.Add("HardwareRevision");
            }
            CallToolResult result = await new DiReadTools(harness.Manager).ReadPropertyAsync(
                "ns=2;s=device", DiDeviceProperty.HardwareRevision, "selected").ConfigureAwait(false);

            Assert.That(result.IsError, Is.EqualTo(StatusCode.IsBad(status)));
            JsonElement json = result.StructuredContent!.Value;
            if (StatusCode.IsBad(status))
            {
                Assert.That(json.GetProperty("statusCodeValue").GetUInt32(), Is.EqualTo(status.Code));
            }
            else
            {
                Assert.That(json.GetProperty("supported").GetBoolean(), Is.EqualTo(!missing));
                if (!missing)
                {
                    Assert.That(json.GetProperty("dataValue").GetProperty("Value").GetString(), Is.EqualTo("HW-0"));
                }
            }
        }

        /// <summary>
        /// Both transfer directions preserve the generated method and initialization refusal.
        /// </summary>
        [TestCase(true, 0)]
        [TestCase(false, 0)]
        [TestCase(true, 19)]
        [TestCase(false, -5)]
        public async Task TransfersUseOnlyRequestedDirectionAsync(bool toDevice, int initializationStatus)
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            harness.OutputArguments = [Variant.From(41), Variant.From(initializationStatus)];
            var commands = new DiCommandTools(harness.Manager);

            CallToolResult result = toDevice
                ? await commands.TransferToDeviceAsync("ns=2;s=transfer", "selected").ConfigureAwait(false)
                : await commands.TransferFromDeviceAsync("ns=2;s=transfer", "selected").ConfigureAwait(false);

            Assert.That(harness.Calls, Has.Count.EqualTo(1));
            Assert.That(harness.Calls[0].ObjectId, Is.EqualTo(new NodeId("transfer", 2)));
            uint method = toDevice ? Di.Methods.TransferServicesType_TransferToDevice
                : Di.Methods.TransferServicesType_TransferFromDevice;
            Assert.That(harness.Calls[0].MethodId,
                Is.EqualTo(NodeId.Create(method, Di.Namespaces.OpcUaDi, harness.Session.Object.NamespaceUris)));
            Assert.That(result.IsError, Is.EqualTo(initializationStatus != 0));
            if (initializationStatus == 0)
            {
                Assert.That(result.StructuredContent!.Value.GetProperty("transferId").GetInt32(), Is.EqualTo(41));
                Assert.That(result.StructuredContent.Value.GetProperty("nextSequenceNumber").GetInt32(), Is.Zero);
            }
            else
            {
                Assert.That(result.StructuredContent!.Value.GetProperty("statusCodeValue").GetUInt32(),
                    Is.EqualTo(StatusCodes.BadInvalidArgument.Code));
            }
        }

        /// <summary>
        /// Parameter result polling submits one bounded request and preserves the returned sequence.
        /// </summary>
        [Test]
        public async Task FetchTransferResultsPreservesRequestAndContinuationAsync()
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            harness.OutputArguments =
            [
                Variant.FromStructure(new TransferResultDataDataType
                {
                    SequenceNumber = 9,
                    EndOfResults = false,
                    ParameterDefs =
                    [
                        new ParameterResultDataType
                        {
                            NodePath = [new QualifiedName("Gain", 2)],
                            StatusCode = StatusCodes.BadOutOfRange
                        }
                    ]
                })
            ];

            CallToolResult result = await new DiCommandTools(harness.Manager).FetchTransferResultsAsync(
                "ns=2;s=transfer", 41, 9, 3, true, "selected").ConfigureAwait(false);

            JsonElement json = Success(result);
            Assert.That(json.GetProperty("nextSequenceNumber").GetInt32(), Is.EqualTo(10));
            Assert.That(json.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            Assert.That(json.GetProperty("transferId").GetInt32(), Is.EqualTo(41));
            Assert.That(harness.Calls, Has.Count.EqualTo(1));
            ArrayOf<Variant> args = harness.Calls[0].InputArguments;
            Assert.That(args.Count, Is.EqualTo(4));
            Assert.That(args[0].TryGetValue(out int transfer), Is.True);
            Assert.That(args[1].TryGetValue(out int sequence), Is.True);
            Assert.That(args[2].TryGetValue(out int limit), Is.True);
            Assert.That(args[3].TryGetValue(out bool omitGood), Is.True);
            Assert.That(transfer, Is.EqualTo(41));
            Assert.That(sequence, Is.EqualTo(9));
            Assert.That(limit, Is.EqualTo(3));
            Assert.That(omitGood, Is.True);
        }

        /// <summary>
        /// Package installation retains metadata and invokes only the explicit installation method.
        /// </summary>
        [Test]
        public async Task InstallPackagePreservesAllArgumentsAsync()
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            var request = new DiInstallPackageRequest
            {
                ManufacturerUri = "urn:vendor",
                SoftwareRevision = "2.3",
                PatchIdentifiers = ["patch"],
                HashBase64 = "AQID"
            };

            CallToolResult result = await new DiSoftwareUpdateTools(harness.Manager, harness.Transfers)
                .InstallSoftwarePackageAsync("ns=2;s=software", request, "selected").ConfigureAwait(false);

            Assert.That(Success(result).GetProperty("methodCompleted").GetBoolean(), Is.True);
            Assert.That(harness.Calls, Has.Count.EqualTo(1));
            CallMethodRequest call = harness.Calls[0];
            Assert.That(call.ObjectId, Is.EqualTo(new NodeId("software/Installation", 2)));
            Assert.That(call.InputArguments.Count, Is.EqualTo(4));
            Assert.That(call.InputArguments[0].TryGetValue(out string manufacturer), Is.True);
            Assert.That(call.InputArguments[1].TryGetValue(out string revision), Is.True);
            Assert.That(call.InputArguments[2].TryGetValue(out ArrayOf<string> patches), Is.True);
            Assert.That(call.InputArguments[3].TryGetValue(out ByteString hash), Is.True);
            Assert.That(manufacturer, Is.EqualTo("urn:vendor"));
            Assert.That(revision, Is.EqualTo("2.3"));
            Assert.That(patches[0], Is.EqualTo("patch"));
            Assert.That(hash.ToBase64(), Is.EqualTo("AQID"));
        }

        /// <summary>
        /// File installation submits the requested UA file identities without opening a host file.
        /// </summary>
        [Test]
        public async Task InstallFilesPreservesServerFileIdentitiesAsync()
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            CallToolResult result = await new DiSoftwareUpdateTools(harness.Manager, harness.Transfers)
                .InstallFilesAsync("ns=2;s=software",
                    new DiInstallFilesRequest { FileNodeIds = ["ns=2;s=file-a", "ns=2;s=file-b"] }, "selected")
                .ConfigureAwait(false);

            Assert.That(Success(result).GetProperty("operation").GetString(), Is.EqualTo("InstallFiles"));
            Assert.That(harness.Calls, Has.Count.EqualTo(1));
            Assert.That(harness.Calls[0].InputArguments[0].TryGetValue(out ArrayOf<NodeId> files), Is.True);
            Assert.That(files.Count, Is.EqualTo(2));
            Assert.That(files[0], Is.EqualTo(new NodeId("file-a", 2)));
            Assert.That(files[1], Is.EqualTo(new NodeId("file-b", 2)));
        }

        /// <summary>
        /// Verifies the error envelope before inspecting a successful payload.
        /// </summary>
        private static JsonElement Success(CallToolResult result)
        {
            Assert.That(result.IsError, Is.False, result.Content.OfType<TextContentBlock>().Single().Text);
            Assert.That(result.StructuredContent.HasValue, Is.True);
            return result.StructuredContent!.Value;
        }

        /// <summary>
        /// A real generated client runs against deterministic UA reads, child paths and method responses.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            private Harness(ServiceProvider services)
            {
                m_services = services;
                Manager = services.GetRequiredService<OpcUaSessionManager>();
                Transfers = services.GetRequiredService<McpFileTransfers>();
                var context = ServiceMessageContext.Create(Manager.Telemetry);
                context.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi);
                context.NamespaceUris.GetIndexOrAppend("urn:di-coverage");
                Session = new Mock<ISession>(MockBehavior.Strict);
                Session.SetupGet(value => value.MessageContext).Returns(context);
                Session.SetupGet(value => value.NamespaceUris).Returns(context.NamespaceUris);
                Session.SetupGet(value => value.Connected).Returns(true);
                Session.SetupGet(value => value.SessionName).Returns("selected");
                Session.SetupGet(value => value.SessionId).Returns(new NodeId("session", 2));
                Session.SetupGet(value => value.ConfiguredEndpoint).Returns(new ConfiguredEndpoint(
                    null, new EndpointDescription { EndpointUrl = "opc.tcp://localhost:4840" },
                    EndpointConfiguration.Create()));
                Session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken _) =>
                        new TranslateBrowsePathsToNodeIdsResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = paths.ConvertAll(path =>
                            {
                                string id = path.StartingNode.TryGetValue(out string text) ? text : "root";
                                foreach (RelativePathElement element in path.RelativePath.Elements)
                                {
                                    string name = element.TargetName.Name!;
                                    if (Missing.Contains(name))
                                    {
                                        return new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch };
                                    }
                                    id += "/" + name;
                                }
                                return new BrowsePathResult
                                {
                                    StatusCode = StatusCodes.Good,
                                    Targets =
                                    [
                                        new BrowsePathTarget
                                        {
                                            TargetId = new NodeId(id, 2),
                                            RemainingPathIndex = uint.MaxValue
                                        }
                                    ]
                                };
                            })
                        });
                Session.Setup(value => value.ReadAsync(It.IsAny<RequestHeader?>(), It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(), It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((RequestHeader? _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> nodes,
                        CancellationToken _) => new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = nodes.ConvertAll(Read)
                        });
                Session.Setup(value => value.WriteAsync(It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(),
                    It.IsAny<CancellationToken>()))
                    .ReturnsAsync((RequestHeader? _, ArrayOf<WriteValue> writes, CancellationToken _) =>
                    {
                        foreach (WriteValue write in writes)
                        {
                            Writes.Add(write);
                        }
                        return new WriteResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = writes.ConvertAll(_ => WriteStatus)
                        };
                    });
                Session.Setup(value => value.CallAsync(It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((RequestHeader? _, ArrayOf<CallMethodRequest> calls, CancellationToken _) =>
                    {
                        foreach (CallMethodRequest call in calls)
                        {
                            Calls.Add(call);
                        }
                        return new CallResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = calls.ConvertAll(_ => new CallMethodResult
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments = OutputArguments
                            })
                        };
                    });
            }

            public Mock<ISession> Session { get; }

            public OpcUaSessionManager Manager { get; }

            public McpFileTransfers Transfers { get; }

            public Dictionary<string, DataValue> Values { get; } = new(StringComparer.Ordinal);

            public HashSet<string> Missing { get; } = new(StringComparer.Ordinal);

            public List<CallMethodRequest> Calls { get; } = [];

            public List<WriteValue> Writes { get; } = [];

            public StatusCode WriteStatus { get; set; } = StatusCodes.Good;

            public ArrayOf<Variant> OutputArguments { get; set; } = [];

            public static async Task<Harness> CreateAsync()
            {
                var services = new ServiceCollection();
                services.AddOpcUaMcpCore(new OpcUaMcpOptions());
                var harness = new Harness(services.BuildServiceProvider());
                await harness.Manager.RegisterExistingSessionAsync("selected", harness.Session.Object, "Anonymous")
                    .ConfigureAwait(false);
                return harness;
            }

            public ValueTask DisposeAsync()
            {
                return m_services.DisposeAsync();
            }

            private DataValue Read(ReadValueId item)
            {
                Variant value = item.AttributeId switch
                {
                    Attributes.NodeId => Variant.From(item.NodeId),
                    Attributes.NodeClass => Variant.From((int)NodeClass.Object),
                    Attributes.BrowseName => Variant.From(new QualifiedName("Device", 2)),
                    Attributes.DisplayName => Variant.From(new LocalizedText("Device")),
                    Attributes.Description => Variant.From(new LocalizedText("Description")),
                    Attributes.WriteMask or Attributes.UserWriteMask => Variant.From(0u),
                    Attributes.EventNotifier => Variant.From((byte)0),
                    _ => Variant.Null
                };
                if (item.AttributeId != Attributes.Value)
                {
                    return new DataValue(value);
                }
                if (!item.NodeId.TryGetValue(out string id) ||
                    !Values.TryGetValue(id[(id.LastIndexOf('/') + 1)..], out DataValue result))
                {
                    throw new InvalidOperationException("Unexpected value read: " + item.NodeId);
                }
                return result;
            }

            private readonly ServiceProvider m_services;
        }
    }
}
#endif
