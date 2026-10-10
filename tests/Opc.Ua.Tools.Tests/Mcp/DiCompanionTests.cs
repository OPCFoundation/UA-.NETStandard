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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Server;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Focused DI catalogue, command dispatch, refusal, continuation and request-shape regression tests.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class DiCompanionTests
    {
        /// <summary>
        /// Both profile forms expose only the reviewed DI catalogue and shared connection tools.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DiProfileContainsTwentyFourExplicitlyAnnotatedTools(bool composed)
        {
            var services = new ServiceCollection();
            IMcpServerBuilder builder = services.AddMcpServer();
            if (composed)
            {
                builder.WithOpcUaDiTools(new McpToolProfileSet(McpToolProfile.Di));
            }
            else
            {
                builder.WithOpcUaDiTools(McpToolProfile.Di);
            }
            await using ServiceProvider provider = services.BuildServiceProvider();
            McpServerTool[] all = provider.GetServices<McpServerTool>().ToArray();
            McpServerTool[] tools = all
                .Where(tool => tool.ProtocolTool.Name.StartsWith("di_", StringComparison.Ordinal)).ToArray();

            Assert.That(tools.Select(tool => tool.ProtocolTool.Name), Is.EquivalentTo(kToolNames));
            Assert.That(all.Count(tool => tool.ProtocolTool.Name == "Connect"), Is.EqualTo(1));
            Assert.That(all.Select(tool => tool.ProtocolTool.Name).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(all.Length));
            foreach (McpServerTool tool in tools)
            {
                bool readOnly = kReadOnlyTools.Contains(tool.ProtocolTool.Name, StringComparer.Ordinal);
                Assert.That(tool.ProtocolTool.Annotations?.ReadOnlyHint, Is.EqualTo(readOnly), tool.ProtocolTool.Name);
                Assert.That(tool.ProtocolTool.Annotations?.DestructiveHint,
                    Is.EqualTo(!readOnly), tool.ProtocolTool.Name);
                JsonElement properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
                Assert.That(properties.TryGetProperty("sessionName", out _), Is.True, tool.ProtocolTool.Name);
                Assert.That(properties.TryGetProperty("sessionManager", out _), Is.False, tool.ProtocolTool.Name);
                Assert.That(properties.TryGetProperty("fileTransfers", out _), Is.False, tool.ProtocolTool.Name);
            }
        }

        /// <summary>
        /// Non-DI profiles do not receive DI operations or connection registrations from this package.
        /// </summary>
        [TestCase(McpToolProfile.Core)]
        [TestCase(McpToolProfile.Scales)]
        public async Task OtherProfilesContributeNoDiTools(McpToolProfile profile)
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaDiTools(profile);
            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetServices<McpServerTool>(), Is.Empty);
        }

        /// <summary>
        /// Registration is idempotent and preserves an embedding host's transfer policy instance.
        /// </summary>
        [Test]
        public async Task RegistrationPreservesHostServices()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore(new OpcUaMcpOptions());
            var transfers = new McpFileTransfers(new OpcUaMcpOptions { MaxTransferBytes = 1024 });
            services.AddSingleton(transfers);
            services.AddOpcUaMcpDi().AddOpcUaMcpDi();
            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetRequiredService<McpFileTransfers>(), Is.SameAs(transfers));
            Assert.That(provider.GetServices<DiReadTools>().Count(), Is.EqualTo(1));
            Assert.That(provider.GetServices<DiCommandTools>().Count(), Is.EqualTo(1));
            Assert.That(provider.GetServices<DiSoftwareUpdateTools>().Count(), Is.EqualTo(1));
        }

        /// <summary>
        /// All constructors and extension points reject missing dependencies.
        /// </summary>
        [Test]
        public async Task ConstructorsAndExtensionsRejectNullDependencies()
        {
            await using ServiceProvider provider = CreateProvider();
            OpcUaSessionManager manager = provider.GetRequiredService<OpcUaSessionManager>();

            Assert.That(() => new DiReadTools(null!), Throws.ArgumentNullException);
            Assert.That(() => new DiCommandTools(null!), Throws.ArgumentNullException);
            Assert.That(() => new DiSoftwareUpdateTools(null!, new McpFileTransfers(new OpcUaMcpOptions())),
                Throws.ArgumentNullException);
            Assert.That(() => new DiSoftwareUpdateTools(manager, null!), Throws.ArgumentNullException);
            Assert.That(() => ((IServiceCollection)null!).AddOpcUaMcpDi(), Throws.ArgumentNullException);
            Assert.That(() => ((IMcpServerBuilder)null!).WithOpcUaDiTools(), Throws.ArgumentNullException);
            Assert.That(() => DiMcpFilters.AddInstallSchemas(null!), Throws.ArgumentNullException);
        }

        /// <summary>
        /// Every explicit lock operation calls only its own generated method and preserves raw refusals.
        /// </summary>
        [TestCase("InitLock", 0)]
        [TestCase("InitLock", 1)]
        [TestCase("InitLock", 2)]
        [TestCase("RenewLock", 0)]
        [TestCase("RenewLock", 2)]
        [TestCase("ExitLock", 0)]
        [TestCase("ExitLock", 2)]
        [TestCase("BreakLock", 0)]
        [TestCase("BreakLock", 1)]
        [TestCase("BreakLock", -12345)]
        public async Task LockCommandsPreserveRawStatusAndNeverChainCalls(string operation, int status)
        {
            await using ServiceProvider provider = CreateProvider();
            OpcUaSessionManager manager = provider.GetRequiredService<OpcUaSessionManager>();
            Mock<ISession> session = CreateSession(manager.Telemetry);
            var calls = new List<CallMethodRequest>();
            SetupCall(session, calls, [Variant.From(status)]);
            await manager.RegisterExistingSessionAsync("selected", session.Object, "Anonymous").ConfigureAwait(false);
            var tools = new DiCommandTools(manager);

            CallToolResult result = operation switch
            {
                "InitLock" => await tools.AcquireLockAsync("ns=2;s=lock", "test-context", "selected")
                    .ConfigureAwait(false),
                "RenewLock" => await tools.RenewLockAsync("ns=2;s=lock", "selected").ConfigureAwait(false),
                "ExitLock" => await tools.ReleaseLockAsync("ns=2;s=lock", "selected").ConfigureAwait(false),
                _ => await tools.BreakLockAsync("ns=2;s=lock", "selected").ConfigureAwait(false)
            };

            JsonObject json = ReadResult(result);
            Assert.That(result.IsError, Is.EqualTo(status != 0));
            Assert.That(json["returnStatus"]!.GetValue<int>(), Is.EqualTo(status));
            Assert.That(json["succeeded"]!.GetValue<bool>(), Is.EqualTo(status == 0));
            Assert.That(json["operation"]!.GetValue<string>(), Is.EqualTo(operation));
            Assert.That(calls, Has.Count.EqualTo(1));
            Assert.That(calls[0].ObjectId, Is.EqualTo(new NodeId("lock", 2)));
            uint expectedMethod = operation switch
            {
                "InitLock" => Di.Methods.LockingServicesType_InitLock,
                "RenewLock" => Di.Methods.LockingServicesType_RenewLock,
                "ExitLock" => Di.Methods.LockingServicesType_ExitLock,
                _ => Di.Methods.LockingServicesType_BreakLock
            };
            Assert.That(calls[0].MethodId,
                Is.EqualTo(NodeId.Create(expectedMethod, Di.Namespaces.OpcUaDi, session.Object.NamespaceUris)));
            Assert.That(calls[0].InputArguments.Count, Is.EqualTo(operation == "InitLock" ? 1 : 0));
            if (operation == "InitLock")
            {
                Assert.That(calls[0].InputArguments[0].TryGetValue(out string context), Is.True);
                Assert.That(context, Is.EqualTo("test-context"));
            }
        }

        /// <summary>
        /// Re-registering a session cannot leave a cached DI proxy bound to the previous connection.
        /// </summary>
        [Test]
        public async Task CommandsUseTheCurrentNamedSessionAfterReplacement()
        {
            await using ServiceProvider provider = CreateProvider();
            OpcUaSessionManager manager = provider.GetRequiredService<OpcUaSessionManager>();
            Mock<ISession> first = CreateSession(manager.Telemetry);
            Mock<ISession> replacement = CreateSession(manager.Telemetry);
            var firstCalls = new List<CallMethodRequest>();
            var replacementCalls = new List<CallMethodRequest>();
            SetupCall(first, firstCalls, [Variant.From(1)]);
            SetupCall(replacement, replacementCalls, [Variant.From(0)]);
            var tools = new DiCommandTools(manager);
            await manager.RegisterExistingSessionAsync("selected", first.Object, "Anonymous").ConfigureAwait(false);
            CallToolResult refused = await tools.RenewLockAsync("ns=2;s=lock", "selected").ConfigureAwait(false);
            await manager.RegisterExistingSessionAsync("selected", replacement.Object, "Anonymous")
                .ConfigureAwait(false);

            CallToolResult renewed = await tools.RenewLockAsync("ns=2;s=lock", "selected").ConfigureAwait(false);

            Assert.That(ReadResult(refused)["returnStatus"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(ReadResult(renewed)["returnStatus"]!.GetValue<int>(), Is.Zero);
            Assert.That(firstCalls, Has.Count.EqualTo(1));
            Assert.That(replacementCalls, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Two real sessions reach a DI server and preserve its ownership refusals without stealing the lock.
        /// </summary>
        [Test]
        [Category("Integration")]
        [NonParallelizable]
        public async Task LiveDiLockRefusalsPreserveOwnershipAndActualChildId()
        {
            string identity = Guid.NewGuid().ToString("N");
            string root = Path.GetFullPath(Path.Combine("TestResults", nameof(DiCompanionTests), identity));
            string endpoint = $"opc.tcp://localhost:{AvailablePort()}/di-mcp";
            string owner = "di-owner-" + identity;
            string other = "di-other-" + identity;
            var ready = new TaskCompletionSource<(NodeId Device, NodeId Lock)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ILockService, DefaultLockService>();
            services.AddOpcUa().AddServer<StandardServer>(options =>
            {
                options.ApplicationName = "DiMcpTests";
                options.ApplicationUri = "urn:localhost:di-mcp-tests:" + identity;
                options.ProductUri = "urn:localhost:di-mcp-tests:product";
                options.PkiRoot = Path.Combine(root, "pki");
                options.AutoAcceptUntrustedCertificates = true;
                options.IncludeUnsecurePolicyNone = true;
                options.EndpointUrls.Clear();
                options.EndpointUrls.Add(endpoint);
            })
                .AddOpcUaDi()
                .ConfigureDevicesFor<DiNodeManager>(async context =>
                {
                    var device = await context.CreateDeviceAsync(
                        new QualifiedName("LockingDevice", context.Manager.InstanceNamespaceIndex))
                        .ConfigureAwait(false);
                    LockingServicesState locking = context.Manager.SystemContext.CreateInstanceOfLockingServicesType(
                        device.Device, new QualifiedName("Lock", context.Manager.InstanceNamespaceIndex));
                    locking.BrowseName = new QualifiedName("Lock", context.Manager.DiNamespaceIndex);
                    locking.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                    var lockService = (DefaultLockService)context.GetRequiredService<ILockService>();
                    locking.BindToLockService(device.Device.NodeId, lockService);
                    device.Device.Lock = locking;
                    await context.Manager.AddPredefinedNodeAsync(locking, context.CancellationToken)
                        .ConfigureAwait(false);
                    ready.SetResult((device.Device.NodeId, locking.NodeId));
                });
            await using ServiceProvider serverServices = services.BuildServiceProvider();
            IHostedService serverHost = serverServices.GetServices<IHostedService>().Single();
            OpcUaSessionManager sessions = McpTestEnvironment.SessionManager;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await serverHost.StartAsync(timeout.Token).ConfigureAwait(false);
                if (serverHost is BackgroundService { ExecuteTask: { } execution })
                {
                    Task completed = await Task.WhenAny(ready.Task, execution).WaitAsync(timeout.Token)
                        .ConfigureAwait(false);
                    await completed.ConfigureAwait(false);
                    if (completed == execution && !ready.Task.IsCompleted)
                    {
                        throw new InvalidOperationException("The DI server stopped before configuring the device.");
                    }
                }
                (NodeId deviceId, NodeId lockId) = await ready.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await sessions.ConnectAsync(owner, endpoint, "None", "None", "Anonymous",
                    null, null, true, timeout.Token).ConfigureAwait(false);
                await sessions.ConnectAsync(other, endpoint, "None", "None", "Anonymous",
                    null, null, true, timeout.Token).ConfigureAwait(false);
                var reads = new DiReadTools(sessions);
                var commands = new DiCommandTools(sessions);

                CallToolResult device = await reads.ReadDeviceAsync(
                    deviceId.ToString(), sessionName: owner, ct: timeout.Token).ConfigureAwait(false);
                Assert.That(device.IsError, Is.False);
                Assert.That(ReadResult(device)["lockNodeId"]!.GetValue<string>(), Is.EqualTo(lockId.ToString()));

                CallToolResult acquired = await commands.AcquireLockAsync(
                    lockId.ToString(), "owner-context", owner, timeout.Token).ConfigureAwait(false);
                Assert.That(acquired.IsError, Is.False);
                Assert.That(ReadResult(acquired)["returnStatus"]!.GetValue<int>(), Is.EqualTo(LockStatus.Ok));

                CallToolResult refused = await commands.AcquireLockAsync(
                    lockId.ToString(), "other-context", other, timeout.Token).ConfigureAwait(false);
                Assert.That(refused.IsError, Is.True);
                Assert.That(ReadResult(refused)["returnStatus"]!.GetValue<int>(),
                    Is.EqualTo(LockStatus.AlreadyLocked));

                CallToolResult wrongOwner = await commands.ReleaseLockAsync(
                    lockId.ToString(), other, timeout.Token).ConfigureAwait(false);
                Assert.That(wrongOwner.IsError, Is.True);
                Assert.That(ReadResult(wrongOwner)["returnStatus"]!.GetValue<int>(),
                    Is.EqualTo(LockStatus.WrongClient));
                ILockService locks = serverServices.GetRequiredService<ILockService>();
                Assert.That(locks.GetState(deviceId).Locked, Is.True);
                Assert.That(locks.GetState(deviceId).LockingClient, Is.EqualTo("owner-context"));

                CallToolResult released = await commands.ReleaseLockAsync(
                    lockId.ToString(), owner, timeout.Token).ConfigureAwait(false);
                Assert.That(released.IsError, Is.False);
                Assert.That(ReadResult(released)["returnStatus"]!.GetValue<int>(), Is.EqualTo(LockStatus.Ok));
                Assert.That(locks.GetState(deviceId).Locked, Is.False);
            }
            finally
            {
                await sessions.DisconnectAsync(owner).ConfigureAwait(false);
                await sessions.DisconnectAsync(other).ConfigureAwait(false);
                await serverHost.StopAsync(CancellationToken.None).ConfigureAwait(false);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        /// <summary>
        /// Missing optional software state machines are errors rather than successful empty observations.
        /// </summary>
        [Test]
        public async Task MissingSoftwareFacetIsNotAnEmptyObservation()
        {
            await using ServiceProvider provider = CreateProvider();
            OpcUaSessionManager manager = provider.GetRequiredService<OpcUaSessionManager>();
            Mock<ISession> session = CreateSession(manager.Telemetry);
            SetupChild(session, NodeId.Null);
            await manager.RegisterExistingSessionAsync("selected", session.Object, "Anonymous").ConfigureAwait(false);
            var tools = new DiSoftwareUpdateTools(manager, provider.GetRequiredService<McpFileTransfers>());

            CallToolResult result = await tools.ObserveSoftwareUpdateAsync(
                "ns=2;s=update", DiSoftwareStateMachine.Installation, sessionName: "selected").ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(ReadResult(result)["statusCodeValue"]!.GetValue<uint>(),
                Is.EqualTo(StatusCodes.BadNotSupported));
            session.Verify(s => s.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Disabling the host transfer root prevents any remote upload side effect.
        /// </summary>
        [Test]
        public async Task DisabledUploadPolicyNeverCallsTheServer()
        {
            await using ServiceProvider provider = CreateProvider();
            OpcUaSessionManager manager = provider.GetRequiredService<OpcUaSessionManager>();
            Mock<ISession> session = CreateSession(manager.Telemetry);
            await manager.RegisterExistingSessionAsync("selected", session.Object, "Anonymous").ConfigureAwait(false);
            var tools = new DiSoftwareUpdateTools(manager, provider.GetRequiredService<McpFileTransfers>());

            CallToolResult result = await tools.UploadSoftwarePackageAsync(
                "ns=2;s=update", "package.bin", sessionName: "selected").ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(ReadResult(result)["message"]!.GetValue<string>(), Does.Contain("disabled"));
            session.Verify(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            session.Verify(s => s.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// A software command invokes exactly its selected cause method and never implicitly locks or confirms.
        /// </summary>
        [TestCase("Prepare", Di.Methods.PrepareForUpdateStateMachineType_Prepare)]
        [TestCase("AbortPrepare", Di.Methods.PrepareForUpdateStateMachineType_Abort)]
        [TestCase("Uninstall", Di.Methods.InstallationStateMachineType_Uninstall)]
        [TestCase("ResumeInstallation", Di.Methods.InstallationStateMachineType_Resume)]
        [TestCase("Confirm", Di.Methods.ConfirmationStateMachineType_Confirm)]
        public async Task SoftwareCommandsInvokeOnlyTheRequestedCause(string operation, uint methodId)
        {
            await using ServiceProvider provider = CreateProvider();
            OpcUaSessionManager manager = provider.GetRequiredService<OpcUaSessionManager>();
            Mock<ISession> session = CreateSession(manager.Telemetry);
            SetupChild(session, new NodeId("state-machine", 2));
            var calls = new List<CallMethodRequest>();
            SetupCall(session, calls, []);
            await manager.RegisterExistingSessionAsync("selected", session.Object, "Anonymous").ConfigureAwait(false);
            var tools = new DiSoftwareUpdateTools(manager, provider.GetRequiredService<McpFileTransfers>());

            CallToolResult result = operation switch
            {
                "Prepare" => await tools.PrepareAsync("ns=2;s=update", "selected").ConfigureAwait(false),
                "AbortPrepare" => await tools.AbortPrepareAsync("ns=2;s=update", "selected").ConfigureAwait(false),
                "Uninstall" => await tools.UninstallAsync("ns=2;s=update", "selected").ConfigureAwait(false),
                "ResumeInstallation" => await tools.ResumeInstallationAsync("ns=2;s=update", "selected")
                    .ConfigureAwait(false),
                _ => await tools.ConfirmAsync("ns=2;s=update", "selected").ConfigureAwait(false)
            };

            Assert.That(result.IsError, Is.False);
            Assert.That(ReadResult(result)["methodCompleted"]!.GetValue<bool>(), Is.True);
            Assert.That(calls, Has.Count.EqualTo(1));
            Assert.That(calls[0].ObjectId, Is.EqualTo(new NodeId("state-machine", 2)));
            Assert.That(calls[0].MethodId,
                Is.EqualTo(NodeId.Create(methodId, Di.Namespaces.OpcUaDi, session.Object.NamespaceUris)));
            Assert.That(calls[0].InputArguments.IsEmpty, Is.True);
        }

        /// <summary>
        /// Bounded transfer chunks preserve explicit sequence continuation and completion.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task TransferChunksPreserveContinuationAndCompletion(bool complete)
        {
            await using ServiceProvider provider = CreateProvider();
            IServiceMessageContext context = ServiceMessageContext.Create(
                provider.GetRequiredService<OpcUaSessionManager>().Telemetry);
            var chunk = new TransferResultDataDataType
            {
                SequenceNumber = 7,
                EndOfResults = complete,
                ParameterDefs =
                [
                    new ParameterResultDataType
                    {
                        NodePath = [new QualifiedName("ParameterSet", 2), new QualifiedName("Gain", 2)],
                        StatusCode = StatusCodes.BadOutOfRange
                    }
                ]
            };

            JsonObject result = DiJson.TransferChunk(new ExtensionObject(chunk), 7, 1, context);

            Assert.That(result["sequenceNumber"]!.GetValue<int>(), Is.EqualTo(7));
            Assert.That(result["complete"]!.GetValue<bool>(), Is.EqualTo(complete));
            Assert.That(result["items"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(result["items"]![0]!.ToJsonString(), Does.Contain("Gain"));
            if (complete)
            {
                Assert.That(result["nextSequenceNumber"], Is.Null);
            }
            else
            {
                Assert.That(result["nextSequenceNumber"]!.GetValue<int>(), Is.EqualTo(8));
            }
        }

        /// <summary>
        /// Transfer model errors remain tool errors with their actual signed integer status.
        /// </summary>
        [TestCase(17)]
        [TestCase(-71)]
        public async Task TransferRefusalsPreserveRawStatus(int status)
        {
            await using ServiceProvider provider = CreateProvider();
            IServiceMessageContext context = ServiceMessageContext.Create(
                provider.GetRequiredService<OpcUaSessionManager>().Telemetry);
            var error = new TransferResultErrorDataType { Status = status };

            CallToolResult result = McpCompanionTools.Result(
                DiJson.TransferChunk(new ExtensionObject(error), 0, 100, context));

            Assert.That(result.IsError, Is.True);
            Assert.That(ReadResult(result)["returnStatus"]!.GetValue<int>(), Is.EqualTo(status));
        }

        /// <summary>
        /// Malformed payloads, oversized chunks and unusable continuation numbers are rejected explicitly.
        /// </summary>
        [Test]
        public async Task InvalidTransferChunksNeverBecomeEmptySuccess()
        {
            await using ServiceProvider provider = CreateProvider();
            IServiceMessageContext context = ServiceMessageContext.Create(
                provider.GetRequiredService<OpcUaSessionManager>().Telemetry);
            var oversized = new TransferResultDataDataType
            {
                ParameterDefs = [new ParameterResultDataType(), new ParameterResultDataType()]
            };
            var sequence = new TransferResultDataDataType { SequenceNumber = int.MaxValue, EndOfResults = false };

            Assert.That(() => DiJson.TransferChunk(ExtensionObject.Null, 0, 100, context),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(() => DiJson.TransferChunk(new ExtensionObject(oversized), 0, 1, context),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(() => DiJson.TransferChunk(new ExtensionObject(sequence), 0, 1, context),
                Throws.TypeOf<ServiceResultException>());
        }

        /// <summary>
        /// Writable properties keep their intended UA type, localization and explicit empty value.
        /// </summary>
        [Test]
        public void PropertyWritesPreserveStringAndLocalizedTextTypes()
        {
            Variant asset = DiJson.PropertyWriteValue(DiWritableProperty.AssetId, string.Empty, null);
            Variant component = DiJson.PropertyWriteValue(DiWritableProperty.ComponentName, "Pompe", "fr");

            Assert.That(asset.TryGetValue(out string assetId), Is.True);
            Assert.That(assetId, Is.EqualTo(string.Empty));
            Assert.That(component.TryGetValue(out LocalizedText name), Is.True);
            Assert.That(name.Text, Is.EqualTo("Pompe"));
            Assert.That(name.Locale, Is.EqualTo("fr"));
            Assert.That(() => DiJson.PropertyWriteValue(DiWritableProperty.AssetId, "asset", "en"),
                Throws.ArgumentException);
            Assert.That(() => DiJson.PropertyWriteValue((DiWritableProperty)999, "asset", null),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Strong installation DTOs accept real JSON arrays and explicitly decode a base64 hash.
        /// </summary>
        [Test]
        public void InstallationRequestsDeserializeStringArraysAndBase64()
        {
            DiInstallPackageRequest package = JsonSerializer.Deserialize<DiInstallPackageRequest>(
                """
                {"manufacturerUri":"urn:vendor","softwareRevision":"2.1",
                 "patchIdentifiers":["p1","p2"],"hashBase64":"AQIDBA=="}
                """)!;
            DiInstallFilesRequest files = JsonSerializer.Deserialize<DiInstallFilesRequest>(
                """{"fileNodeIds":["ns=2;s=file-a","ns=2;s=file-b"]}""")!;

            Assert.That(package.PatchIdentifiers.Count, Is.EqualTo(2));
            Assert.That(package.PatchIdentifiers[0], Is.EqualTo("p1"));
            Assert.That(package.PatchIdentifiers[1], Is.EqualTo("p2"));
            Assert.That(DiJson.PackageHash(package).ToBase64(), Is.EqualTo("AQIDBA=="));
            Assert.That(DiJson.FileNodeIds(files).ToArray(),
                Is.EqualTo(new[] { new NodeId("file-a", 2), new NodeId("file-b", 2) }));
        }

        /// <summary>
        /// Empty file lists, null patch arrays, invalid base64 and null NodeIds cannot start installations.
        /// </summary>
        [Test]
        public void InvalidInstallationInputsAreRejected()
        {
            Assert.That(() => DiJson.FileNodeIds(new DiInstallFilesRequest { FileNodeIds = [] }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => DiJson.FileNodeIds(new DiInstallFilesRequest { FileNodeIds = ["i=0"] }),
                Throws.ArgumentException);
            Assert.That(() => DiJson.PackageHash(new DiInstallPackageRequest
            {
                ManufacturerUri = "urn:vendor",
                SoftwareRevision = "2",
                PatchIdentifiers = ArrayOf<string>.Null,
                HashBase64 = string.Empty
            }), Throws.ArgumentException);
            Assert.That(() => DiJson.PackageHash(new DiInstallPackageRequest
            {
                ManufacturerUri = "urn:vendor",
                SoftwareRevision = "2",
                HashBase64 = "not-base64"
            }), Throws.TypeOf<FormatException>());
        }

        /// <summary>
        /// The list-tools filter declares the exact bounded array fields and leaves other tools unchanged.
        /// </summary>
        [Test]
        public async Task InstallationSchemasDeclareBoundedStringArrays()
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaDiTools(McpToolProfile.Di);
            await using ServiceProvider provider = services.BuildServiceProvider();
            Tool[] tools = provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool).ToArray();
            Tool unrelated = tools.Single(tool => tool.Name == "di_acquire_lock");
            string original = unrelated.InputSchema.GetRawText();
            var response = new ListToolsResult { Tools = tools };
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> filter =
                DiMcpFilters.AddInstallSchemas((_, _) => ValueTask.FromResult(response));

            ListToolsResult result = await filter(null!, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.SameAs(response));
            AssertArraySchema(tools, "di_install_software_package", "patchIdentifiers", 0);
            AssertArraySchema(tools, "di_install_files", "fileNodeIds", 1);
            Assert.That(unrelated.InputSchema.GetRawText(), Is.EqualTo(original));
        }

        /// <summary>
        /// State snapshots retain localization, null transitions, status and nested state identities.
        /// </summary>
        [Test]
        public void StateSnapshotsPreserveQualityAndMissingFacets()
        {
            var timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var child = new FiniteStateSnapshot(
                new NodeId("submachine", 2), new LocalizedText("en", "Idle"), new NodeId("idle", 2),
                LocalizedText.Null, NodeId.Null, timestamp, StatusCodes.Good);
            var state = new FiniteStateSnapshot(
                new NodeId("installation", 2), new LocalizedText("de", "Wartet"), new NodeId("waiting", 2),
                LocalizedText.Null, NodeId.Null, timestamp, StatusCodes.Uncertain)
            {
                SubMachine = child
            };

            JsonObject result = DiJson.State(state);

            Assert.That(result["currentState"]!["text"]!.GetValue<string>(), Is.EqualTo("Wartet"));
            Assert.That(result["currentState"]!["locale"]!.GetValue<string>(), Is.EqualTo("de"));
            Assert.That(result["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(result["timestamp"]!.GetValue<string>(), Is.EqualTo("2026-01-02T03:04:05.0000000Z"));
            Assert.That(result["lastTransitionId"], Is.Null);
            Assert.That(result["subMachine"]!["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=2;s=submachine"));
            Assert.That(DiJson.State(null)["supported"]!.GetValue<bool>(), Is.False);
        }

        /// <summary>
        /// Builds only in-memory services; no connection, file operation or application initialization is needed.
        /// </summary>
        private static ServiceProvider CreateProvider()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore(new OpcUaMcpOptions()).AddOpcUaMcpDi();
            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Selects an ephemeral loopback endpoint instead of sharing a fixed test port.
        /// </summary>
        private static string AvailablePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Creates an externally owned session with the DI namespace and no network transport.
        /// </summary>
        private static Mock<ISession> CreateSession(ITelemetryContext telemetry)
        {
            var session = new Mock<ISession>(MockBehavior.Strict);
            var namespaces = new NamespaceTable();
            namespaces.GetIndexOrAppend(Di.Namespaces.OpcUaDi);
            namespaces.GetIndexOrAppend("urn:di-test");
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris = namespaces;
            session.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            session.SetupGet(value => value.MessageContext).Returns(context);
            session.SetupGet(value => value.Connected).Returns(true);
            session.SetupGet(value => value.SessionName).Returns("di-test");
            session.SetupGet(value => value.SessionId).Returns(new NodeId("session", 2));
            session.SetupGet(value => value.ConfiguredEndpoint).Returns(new ConfiguredEndpoint(
                null, new EndpointDescription { EndpointUrl = "opc.tcp://localhost:4840" },
                EndpointConfiguration.Create()));
            return session;
        }

        /// <summary>
        /// Records every method request and returns a typed, successful OPC UA service response.
        /// </summary>
        private static void SetupCall(
            Mock<ISession> session,
            List<CallMethodRequest> calls,
            ArrayOf<Variant> output)
        {
            session.Setup(value => value.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, ArrayOf<CallMethodRequest> requests, CancellationToken _) =>
                {
                    foreach (CallMethodRequest request in requests)
                    {
                        calls.Add(request);
                    }
                    return new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = output }]
                    };
                });
        }

        /// <summary>
        /// Resolves a generated child accessor or reports an absent optional child.
        /// </summary>
        private static void SetupChild(Mock<ISession> session, NodeId nodeId)
        {
            session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken _) =>
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = paths.ConvertAll(_ => new BrowsePathResult
                        {
                            StatusCode = nodeId.IsNull ? StatusCodes.BadNoMatch : StatusCodes.Good,
                            Targets = nodeId.IsNull
                                ? []
                                : [new BrowsePathTarget { TargetId = nodeId, RemainingPathIndex = uint.MaxValue }]
                        })
                    });
        }

        /// <summary>
        /// Checks matching structured and textual result envelopes before returning their JSON payload.
        /// </summary>
        private static JsonObject ReadResult(CallToolResult result)
        {
            string text = result.Content.OfType<TextContentBlock>().Single().Text;
            Assert.That(result.StructuredContent.HasValue, Is.True);
            Assert.That(JsonNode.DeepEquals(
                JsonNode.Parse(text), JsonNode.Parse(result.StructuredContent!.Value.GetRawText())), Is.True);
            return JsonNode.Parse(text)!.AsObject();
        }

        /// <summary>
        /// Checks the concrete JSON schema consumed by the bounded string-array converter.
        /// </summary>
        private static void AssertArraySchema(Tool[] tools, string toolName, string fieldName, int minimum)
        {
            JsonElement field = tools.Single(tool => tool.Name == toolName).InputSchema
                .GetProperty("properties").GetProperty("input").GetProperty("properties").GetProperty(fieldName);
            Assert.That(field.GetProperty("type").GetString(), Is.EqualTo("array"));
            Assert.That(field.GetProperty("items").GetProperty("type").GetString(), Is.EqualTo("string"));
            Assert.That(field.GetProperty("minItems").GetInt32(), Is.EqualTo(minimum));
            Assert.That(field.GetProperty("maxItems").GetInt32(), Is.EqualTo(500));
        }

        /// <summary>
        /// The complete reviewed DI tool catalogue.
        /// </summary>
        private static readonly string[] kToolNames =
        [
            "di_discover_devices", "di_browse_topology", "di_read_device", "di_read_property",
            "di_read_functional_group", "di_read_lock", "di_write_property", "di_acquire_lock",
            "di_renew_lock", "di_release_lock", "di_break_lock", "di_transfer_to_device",
            "di_transfer_from_device", "di_fetch_transfer_results", "di_read_software_update",
            "di_observe_software_update", "di_prepare_software_update", "di_abort_prepare",
            "di_upload_software_package", "di_install_software_package", "di_install_files",
            "di_uninstall_software", "di_resume_installation", "di_confirm_software_update"
        ];

        /// <summary>
        /// The operations that never invoke a device mutation.
        /// </summary>
        private static readonly string[] kReadOnlyTools =
        [
            "di_discover_devices", "di_browse_topology", "di_read_device", "di_read_property",
            "di_read_functional_group", "di_read_lock", "di_fetch_transfer_results",
            "di_read_software_update", "di_observe_software_update"
        ];
    }
}
#endif
