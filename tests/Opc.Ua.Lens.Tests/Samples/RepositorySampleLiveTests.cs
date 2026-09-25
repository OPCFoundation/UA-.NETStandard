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

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Vision.Client;
using UaLens.Connection;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using UaLens.Samples;
using Isa95V2 = Opc.Ua.ISA95.JobControl.V2;
using VisionTypes = Opc.Ua.Vision;

namespace UaLens.Tests.Samples
{
    [TestFixture]
    [NonParallelizable]
    public sealed partial class RepositorySampleLiveTests
    {
        [Test]
        [Explicit("Requires a trusted Release/net10 Vision fixture cell in UALENS_SAMPLE_SOURCE_ROOT.")]
        [Category("RepositorySampleProbe")]
        public async Task PrivateFixtureAcquisitionInferenceAndFeedbackUsePinnedPeerTrust()
        {
            string sourceRoot = Environment.GetEnvironmentVariable("UALENS_SAMPLE_SOURCE_ROOT") ??
                throw new InvalidOperationException("Set UALENS_SAMPLE_SOURCE_ROOT to the trusted built checkout.");
            string root = Path.Combine(Path.GetTempPath(), "UaLens-fixture-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            var samples = new RepositorySampleService(
                new RepositorySampleFiles(Path.Combine(root, "runs"), LocalFileSystem.Instance),
                new RepositorySamplePortAllocator(), new RepositorySampleRuntime(),
                new RepositorySampleDiscoveryProbe(telemetry), TimeProvider.System);
            await using (samples.ConfigureAwait(false))
            {
                try
                {
                    RepositorySampleSnapshot setup = await samples.ConfigureAsync(
                        RepositorySampleId.VisualInspectionCell,
                        new RepositorySampleSource(sourceRoot, RepositorySampleBuildConfiguration.Release,
                            RepositorySampleFramework.Net10, RepositorySampleBuildLayout.CurrentRuntimeDirectory))
                        .ConfigureAwait(false);
                    Assert.That(setup.Phase, Is.EqualTo(RepositorySamplePhase.Configured), setup.Message);
                    RepositorySampleSnapshot ready = await samples.StartAsync(
                        new RepositorySampleRunOptions { RunSeconds = 30 }).ConfigureAwait(false);
                    Assert.That(ready.Phase, Is.EqualTo(RepositorySamplePhase.AdvertisedReady), ready.Message);
                    RepositorySampleEvidence evidence = ready.Evidence ??
                        throw new AssertionException("The owned sample returned no identity evidence.");
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    string clientPki = Path.Combine(root, "client");
                    var backend = new StackConnectionBackend(telemetry,
                        token => AppConfig.BuildAsync(telemetry, clientPki, token));
                    await using (backend.ConfigureAwait(false))
                    {
                        var connection = new ConnectionService(
                            telemetry, null, backend, new ProfileCredentialProvider());
                        await using (connection.ConfigureAwait(false))
                        {
                            ApplicationConfiguration configuration = await connection.GetConfigAsync(deadline.Token)
                                .ConfigureAwait(false);
                            ArrayOf<EndpointDescription> endpoints = await backend.DiscoverAsync(
                                configuration, evidence.Endpoint.AbsoluteUri, deadline.Token).ConfigureAwait(false);
                            EndpointDescription endpoint = endpoints.ToList().Single(candidate =>
                                candidate.EndpointUrl == evidence.Endpoint.AbsoluteUri &&
                                candidate.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                                candidate.SecurityPolicyUri == SecurityPolicies.Basic256Sha256);
                            Assert.That(ByteString.From(SHA256.HashData(endpoint.ServerCertificate.Span)),
                                Is.EqualTo(evidence.CertificateSha256));
                            await TrustOwnedPeersAsync(configuration, endpoint,
                                ready.PrivatePkiRoot ?? throw new AssertionException("The private PKI is absent."),
                                telemetry, deadline.Token).ConfigureAwait(false);
                            UserTokenPolicy policy = endpoint.UserIdentityTokens.ToList()
                                .First(candidate => candidate.TokenType == UserTokenType.Anonymous);
                            var identity = new UserIdentity(new AnonymousIdentityToken())
                            {
                                PolicyId = policy.PolicyId ??
                                    throw new AssertionException("The token policy has no ID.")
                            };
                            await connection.ConnectAsync(new ConnectionOptions
                            {
                                EndpointUrl = endpoint.EndpointUrl ??
                                    throw new AssertionException("The selected endpoint has no URL."),
                                UseSecurity = true,
                                Engine = SubscriptionEngineKind.ChannelV2
                            }, endpoint, identity,
                            (_, error) => throw new AssertionException("Unexpected trust request: " + error.StatusCode),
                            deadline.Token).ConfigureAwait(false);
                            ISession session = connection.CurrentSession ??
                                throw new AssertionException("No secure primary session was created.");
                            Assert.That(session.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                            var context = new CompanionContext(session, telemetry);
                            await VerifyFixtureAiAsync(context, deadline.Token).ConfigureAwait(false);
                            await VerifyFixtureJobControlAsync(context, deadline.Token).ConfigureAwait(false);
                            var provider = new VisionCompanionProvider();
                            ArrayOf<CompanionTarget> targets = await provider.DiscoverAsync(context, deadline.Token)
                                .ConfigureAwait(false);
                            CompanionTarget sensor = targets.ToList()
                                .Single(target => target.TypeName == "VisionSensor");
                            CompanionTarget pipeline = targets.ToList()
                                .Single(target => target.TypeName == "InferencePipeline");
                            CompanionOperationResult media = await provider.ExecuteAsync(
                                context, sensor, "media", null, deadline.Token).ConfigureAwait(false);
                            Assert.That(Value(media, "Endpoint 1 node").TryGetValue(out NodeId clipEndpoint), Is.True);
                            Assert.That(Value(media, "Endpoint 1 kind").TryGetValue(out string? kind), Is.True);
                            Assert.That(kind, Is.EqualTo("ClipEndpoints"));
                            Assert.That(Value(media, "Endpoint 1 state").TryGetValue(out string? state), Is.True);
                            Assert.That(state, Is.EqualTo("Ready"));
                            CompanionOperationResult clip = await RunFixtureTaskAsync(provider, context, sensor,
                                "get-clip",
                                [
                                    new("endpoint", Variant.From(clipEndpoint)),
                                    new("resultId", Variant.From("bracket-ok.png")),
                                    new("timestamp", Variant.From(default(DateTimeUtc))),
                                    new("format", Variant.From((int)VisionTypes.VisionClipFormatEnum.Png)),
                                    new("requestInline", Variant.From(true))
                                ], deadline.Token).ConfigureAwait(false);
                            Assert.That(Value(clip, "Frame width").TryGetValue(out uint width), Is.True);
                            Assert.That(width, Is.EqualTo(800u));
                            Assert.That(Value(clip, "Inline bytes returned").TryGetValue(out int bytes), Is.True);
                            Assert.That(bytes, Is.GreaterThan(0).And.LessThanOrEqualTo(1024 * 1024));
                            CompanionOperationResult inference = await RunFixtureTaskAsync(provider, context, pipeline,
                                "run-inference", [new("timestamp", Variant.From(new DateTimeUtc(2026, 9, 15, 10)))],
                                deadline.Token)
                                .ConfigureAwait(false);
                            Assert.That(Value(inference, "Result kind").TryGetValue(out string? resultKind), Is.True);
                            Assert.That(resultKind, Is.EqualTo("Inspection"));
                            Assert.That(Value(inference, "Characteristics").TryGetValue(
                                out ArrayOf<VisionTypes.VisionCharacteristicDataType> characteristics,
                                session.MessageContext), Is.True);
                            Assert.That(characteristics.Count, Is.GreaterThan(0));
                            NodeId mediaNode = await IndustrialCompanionAccess.ResolveChildAsync(
                                context, sensor.NodeId, VisionTypes.Namespaces.Vision, "Media", false, deadline.Token)
                                .ConfigureAwait(false);
                            VisionTypes.VisionImageReferenceDataType image =
                                await new VisionClient(session, telemetry).Media(mediaNode).ReadLatestClipMetadataAsync(
                                    clipEndpoint, deadline.Token).ConfigureAwait(false)
                                ?? throw new AssertionException("The fixture clip has no published provenance.");
                            string resultId = "ualens-inspection-" + Guid.NewGuid().ToString("N");
                            await RunFixtureTaskAsync(provider, context, pipeline, "submit-image-reference",
                                [
                                    new("purpose",
                                        Variant.From((int)VisionTypes.VisionFeedbackPurposeEnum.Reconciliation)),
                                    new("image", Variant.FromStructure(image)), new("resultId", Variant.From(resultId))
                                ], deadline.Token).ConfigureAwait(false);
                            await RunFixtureTaskAsync(provider, context, pipeline, "submit-inspection",
                                [
                                    new("resultId", Variant.From(resultId)),
                                    new("evaluation", Variant.From((int)VisionTypes.VisionResultEvaluationEnum.Ok)),
                                    new("characteristics", Variant.FromStructure(characteristics))
                                ], deadline.Token).ConfigureAwait(false);
                            NodeId resultNode = await VisionWorkflowResults.FindAsync(
                                context, pipeline.NodeId, resultId, deadline.Token).ConfigureAwait(false);
                            CompanionOperationResult observed = await VisionWorkflowResults.ReadAsync(
                                context, resultNode, resultId, sensor.NodeId, pipeline.NodeId, deadline.Token)
                                .ConfigureAwait(false);
                            Assert.That(Value(observed, "Result ID").TryGetValue(out string? actualId), Is.True);
                            Assert.That(actualId, Is.EqualTo(resultId));
                            Assert.That(Value(observed, "Sensor").TryGetValue(out NodeId actualSensor), Is.True);
                            Assert.That(actualSensor, Is.EqualTo(sensor.NodeId));
                            Assert.That(Value(observed, "Pipeline").TryGetValue(out NodeId actualPipeline), Is.True);
                            Assert.That(actualPipeline, Is.EqualTo(pipeline.NodeId));
                            await connection.DisconnectAsync().ConfigureAwait(false);
                        }
                    }
                    RepositorySampleSnapshot completed = await samples.Completion.WaitAsync(TimeSpan.FromSeconds(90))
                        .ConfigureAwait(false);
                    Assert.That(completed.Phase, Is.EqualTo(RepositorySamplePhase.Stopped), completed.Message);
                    Assert.That(completed.ExitCode, Is.Zero);
                    Assert.That(completed.ForcedTermination, Is.False);
                    Assert.That(completed.OwnsResources, Is.False);
                }
                finally
                {
                    await samples.StopAsync().ConfigureAwait(false);
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [Explicit(
            "Requires UALENS_SAMPLE_SOURCE_ROOT naming a trusted checkout with the Release/net10 samples built.")]
        [Category("RepositorySampleProbe")]
        public async Task BuiltManagedSampleAdvertisesOwnedIdentityAndCleansUp(int sampleId)
        {
            string sourceRoot = Environment.GetEnvironmentVariable("UALENS_SAMPLE_SOURCE_ROOT") ??
                throw new InvalidOperationException("Set UALENS_SAMPLE_SOURCE_ROOT to the trusted built checkout.");
            string runParent = Path.Combine(Path.GetTempPath(), "UaLens-live-sample-" + Guid.NewGuid().ToString("N"));
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            var service = new RepositorySampleService(
                new RepositorySampleFiles(runParent, LocalFileSystem.Instance),
                new RepositorySamplePortAllocator(),
                new RepositorySampleRuntime(),
                new RepositorySampleDiscoveryProbe(telemetry),
                TimeProvider.System);
            await using (service.ConfigureAwait(false))
            {
                try
                {
                    RepositorySampleSnapshot setup = await service.ConfigureAsync(
                        (RepositorySampleId)sampleId,
                        new RepositorySampleSource(sourceRoot, RepositorySampleBuildConfiguration.Release,
                            RepositorySampleFramework.Net10, sampleId == 2
                                ? RepositorySampleBuildLayout.CurrentRuntimeDirectory
                                : RepositorySampleBuildLayout.FrameworkDirectory)).ConfigureAwait(false);
                    Assert.That(setup.Phase, Is.EqualTo(RepositorySamplePhase.Configured), setup.Message);
                    RepositorySampleSnapshot ready = await service.StartAsync(
                        new RepositorySampleRunOptions { RunSeconds = 10 }).ConfigureAwait(false);
                    Assert.That(ready.Phase, Is.EqualTo(RepositorySamplePhase.AdvertisedReady));
                    Assert.That(ready.ProcessId, Is.GreaterThan(0));
                    Assert.That(ready.OwnsResources, Is.True);
                    Assert.That(ready.Evidence, Is.Not.Null);
                    Assert.That(ready.Evidence!.ApplicationName,
                        Is.EqualTo(RepositorySampleCatalog.Get((RepositorySampleId)sampleId).ApplicationName));
                    Assert.That(ready.Evidence.CertificateSha256.Length, Is.EqualTo(32));
                    Assert.That(ready.Evidence.Endpoint.IsLoopback, Is.True);
                    Assert.That(ready.SecureConnectAuthorized, Is.False);

                    RepositorySampleSnapshot completed = await service.Completion.WaitAsync(TimeSpan.FromSeconds(50))
                        .ConfigureAwait(false);

                    Assert.That(completed.Phase, Is.EqualTo(RepositorySamplePhase.Stopped), completed.Message);
                    Assert.That(completed.ExitCode, Is.Zero);
                    Assert.That(completed.ForcedTermination, Is.False);
                    Assert.That(completed.OwnsResources, Is.False);
                    Assert.That(Directory.EnumerateFileSystemEntries(runParent), Is.Empty);
                }
                catch (RepositorySampleException error)
                {
                    RepositorySampleSnapshot snapshot = service.Snapshot;
                    string output = string.Join(
                        Environment.NewLine,
                        snapshot.Output.ToArray()?.Select(line => line.Text)
                            ?? []);
                    throw new AssertionException(
                        $"{snapshot.Phase}/{snapshot.Failure}; exit {snapshot.ExitCode}: {snapshot.Message}\n{output}",
                        error);
                }
                finally
                {
                    await service.StopAsync().ConfigureAwait(false);
                    if (Directory.Exists(runParent) && !Directory.EnumerateFileSystemEntries(runParent).Any())
                    {
                        Directory.Delete(runParent, recursive: false);
                    }
                }
            }
        }

        private static async Task TrustOwnedPeersAsync(
            ApplicationConfiguration configuration, EndpointDescription endpoint, string serverPki,
            ITelemetryContext telemetry, CancellationToken cancellationToken)
        {
            ICertificateManager manager = configuration.CertificateManager ??
                throw new AssertionException("The private client certificate manager is missing.");
            using CertificateEntry entry =
                manager.AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256) ??
                throw new AssertionException("The private application certificate is missing.");
            using var clientPublic = Certificate.FromRawData(entry.Certificate.RawData);
            using ICertificateStore serverPeers = new CertificateStoreIdentifier(
                Path.Combine(serverPki, "trusted"), CertificateStoreType.Directory).OpenStore(telemetry);
            await serverPeers.AddAsync(clientPublic, ct: cancellationToken).ConfigureAwait(false);
            using var serverPublic = Certificate.FromRawData(endpoint.ServerCertificate.Memory);
            using ICertificateStore clientPeers = manager.OpenTrustedStore(TrustListIdentifier.Peers);
            await clientPeers.AddAsync(serverPublic, ct: cancellationToken).ConfigureAwait(false);
            manager.NotifyTrustListChanged(TrustListIdentifier.Peers, trustChanged: true, crlChanged: false);
        }

        private static async Task VerifyFixtureAiAsync(
            CompanionContext context, CancellationToken cancellationToken)
        {
            var provider = new AICompanionProvider();
            ArrayOf<CompanionTarget> targets = await provider.DiscoverAsync(context, cancellationToken)
                .ConfigureAwait(false);
            CompanionTarget deployment = targets.ToList().Single(target => target.TypeName == "Deployment");
            CompanionTarget model = targets.ToList().Single(target => target.TypeName == "Model");
            CompanionOperationResult result = await RunFixtureTaskAsync(provider, context, deployment,
                "invoke-request",
                [
                    new("model", Variant.From(model.NodeId)),
                    new("capability", Variant.From("vision-measurement")),
                    new("payload", Variant.From(ByteString.From("{\"fixture\":\"bracket-ok.png\"}"u8))),
                    new("contentType", Variant.From("application/json")),
                    new("parameters", Variant.FromStructure(ArrayOf<KeyValuePair>.Empty)),
                    new("timeout", Variant.From(5000d))
                ], cancellationToken).ConfigureAwait(false);

            Assert.That(Value(result, "Complete response").TryGetValue(out bool complete), Is.True);
            Assert.That(complete, Is.True);
            Assert.That(Value(result, "Model used").TryGetValue(out NodeId modelUsed), Is.True);
            Assert.That(modelUsed, Is.EqualTo(model.NodeId));
            Assert.That(Value(result, "Response").TryGetValue(out ByteString response), Is.True);
            using var document = JsonDocument.Parse(response.Memory);
            Assert.That(document.RootElement.GetProperty("fixture").GetString(), Is.EqualTo("bracket-ok.png"));
            Assert.That(document.RootElement.GetProperty("confidence").GetDouble(), Is.EqualTo(0.99));
            Assert.That(document.RootElement.GetProperty("measurements").GetArrayLength(), Is.GreaterThan(0));
        }

        private static async Task VerifyFixtureJobControlAsync(
            CompanionContext context, CancellationToken cancellationToken)
        {
            var provider = new Isa95CompanionProvider();
            ArrayOf<CompanionTarget> targets = await provider.DiscoverAsync(context, cancellationToken)
                .ConfigureAwait(false);
            CompanionTarget receiver = targets.ToList().Single(target => target.TypeName == "V2 order receiver");
            const string orderId = "VIS-REWORK-REJECT-001";
            foreach ((string operation, uint expectedState) in new[] { ("pause-job", 4u), ("resume-job", 3u) })
            {
                CompanionOperationResult result = await RunFixtureTaskAsync(provider, context, receiver, operation,
                    [
                        new("jobOrderId", Variant.From(orderId)),
                        new("comment", Variant.From(ArrayOf<LocalizedText>.Empty))
                    ], cancellationToken).ConfigureAwait(false);

                Assert.That(Value(result, "ISA-95 return status").TryGetValue(out ulong status), Is.True);
                Assert.That(status, Is.EqualTo(1UL));
                Assert.That(Value(result, "Job order ID").TryGetValue(out string? actualId), Is.True);
                Assert.That(actualId, Is.EqualTo(orderId));
                Assert.That(Value(result, "Observed job").TryGetValue<Isa95V2.ISA95JobOrderAndStateDataType>(
                    out Isa95V2.ISA95JobOrderAndStateDataType? job, context.Session.MessageContext), Is.True);
                Assert.That(job!.State.Count, Is.GreaterThan(0));
                Assert.That(job.State[0].StateNumber, Is.EqualTo(expectedState));
            }
        }

        private static async Task<CompanionOperationResult> RunFixtureTaskAsync(
            IPreparedCompanionProvider provider, CompanionContext context, CompanionTarget target, string operation,
            ArrayOf<CompanionValue> inputs, CancellationToken cancellationToken)
        {
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                context, target, operation, inputs, cancellationToken).ConfigureAwait(false);
            return await provider.ExecutePreparedAsync(context, target, operation, prepared, null, cancellationToken)
                .ConfigureAwait(false);
        }

        private static Variant Value(CompanionOperationResult result, string name)
        {
            foreach (CompanionValue value in result.Values)
            {
                if (value.Name == name)
                {
                    return value.Value;
                }
            }
            throw new AssertionException("Missing result field: " + name);
        }
    }
}
