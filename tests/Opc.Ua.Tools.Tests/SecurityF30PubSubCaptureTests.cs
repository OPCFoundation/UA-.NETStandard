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
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.DependencyInjection;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Pcap;
using Opc.Ua.PubSub.Pcap.KeyLog;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;

namespace Opc.Ua.Tools.Tests.CaptureAuthorization
{
    /// <summary>
    /// Exercises PubSub dissection entry points without the live MCP server setup fixture.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class SecurityF30PubSubCaptureTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_directory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, $"security-f30-{Guid.NewGuid():N}");
            m_allowedRoot = Path.Combine(m_directory, "captures");
            Directory.CreateDirectory(m_allowedRoot);
            var services = new ServiceCollection();
            services.AddSingleton(new OpcUaMcpOptions { PcapBaseFolder = m_allowedRoot });
            services.AddSingleton(new PcapOptions { BaseFolder = m_directory });
            m_registry = new PubSubCaptureRegistry();
            m_manager = new PubSubCaptureSessionManager(m_registry);
            services.AddSingleton(m_manager);
            m_callers = new McpCallerContext();
            services.AddSingleton(m_callers);
            m_services = services.BuildServiceProvider();

            IPubSubSecurityPolicy policy = ResolvePolicy(PubSubSecurityPolicyUri.PubSubAes256Ctr);
            m_material = new PubSubKeyMaterial(
                "security-regression-test-group",
                1234,
                policy.PolicyUri,
                RandomNumberGenerator.GetBytes(policy.SigningKeyLength),
                RandomNumberGenerator.GetBytes(policy.EncryptingKeyLength),
                RandomNumberGenerator.GetBytes(AesCtrNonceLayout.KeyNonceLength));
            m_encryptedFrame = await CreateEncryptedFrameAsync(m_material).ConfigureAwait(false);
            await PubSubCaptureTools.StartCaptureAsync(m_manager, CancellationToken.None).ConfigureAwait(false);
            CaptureFrame(m_registry);
            PubSubCaptureSessionInfo stopped = await PubSubCaptureTools.StopCaptureAsync(
                m_manager, CancellationToken.None).ConfigureAwait(false);
            Assert.That(stopped.FrameCount, Is.EqualTo(1));
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            try
            {
                if (m_manager is not null)
                {
                    await m_manager.ClearKeyMaterialForCallerAsync("trusted-host").ConfigureAwait(false);
                }

                if (m_manager is not null)
                {
                    IPubSubCaptureSource? active = await m_manager.StopAsync().ConfigureAwait(false);
                    if (active is not null)
                    {
                        await active.DisposeAsync().ConfigureAwait(false);
                    }

                    // Starting a fresh capture disposes the tool's static stopped snapshot.
                    await PubSubCaptureTools.StartCaptureAsync(m_manager, CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                if (m_manager is not null)
                {
                    await m_manager.DisposeAsync().ConfigureAwait(false);
                }

                m_material?.Dispose();
                if (m_services is not null)
                {
                    await m_services.DisposeAsync().ConfigureAwait(false);
                }

                if (Directory.Exists(m_directory))
                {
                    Directory.Delete(m_directory, recursive: true);
                }
            }
        }

        [TestCase("absolute")]
        [TestCase("parent-traversal")]
        [TestCase("prefix-sibling")]
        [Category("SecurityRegression")]
        public async Task SecurityF30DissectionCannotUseOutOfRootKeys(string escape)
        {
            string outside = escape == "prefix-sibling"
                ? Path.Combine(m_allowedRoot + "-sibling", "keys.jsonl")
                : Path.Combine(m_directory, "outside", "keys.jsonl");
            await WriteKeyLogAsync(outside).ConfigureAwait(false);
            string supplied = escape == "parent-traversal"
                ? Path.Combine("..", "outside", "keys.jsonl")
                : outside;

            using (FileStream lockedFile = new(outside, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await Assert.ThatAsync(
                    () => PubSubDecodeTools.DissectCaptureAsync(
                        m_services, m_manager, "json", supplied),
                    Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("filePath"),
                    "Root authorization must reject the path before any attempt to open the locked key file.");
            }

            await Assert.ThatAsync(
                () => PubSubDecodeTools.DissectCaptureAsync(m_services, m_manager, "json", supplied),
                Throws.TypeOf<ArgumentException>(),
                "An existing readable key file under the broader PcapOptions root is still outside the MCP root.");
            AssertEncryptedWithoutKeys(await PubSubDecodeTools.DissectCaptureAsync(
                m_services, m_manager, "json").ConfigureAwait(false));

            await WriteKeyLogAsync(Path.Combine(m_allowedRoot, "approved", "keys.jsonl")).ConfigureAwait(false);
            AssertDecrypted(await PubSubDecodeTools.DissectCaptureAsync(
                m_services, m_manager, "json", Path.Combine("approved", "keys.jsonl")).ConfigureAwait(false));
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF30DissectionRejectsMissingOutsideKeyBeforeFileLookup()
        {
            string outside = Path.Combine(m_directory, "not-created", "keys.jsonl");
            await Assert.ThatAsync(
                () => PubSubDecodeTools.DissectCaptureAsync(m_services, m_manager, "json", outside),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("filePath"));
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF30DissectionDecryptsWithHostApprovedKeys(bool absolute)
        {
            string path = Path.Combine(m_allowedRoot, "approved", "keys.jsonl");
            await WriteKeyLogAsync(path).ConfigureAwait(false);
            string supplied = absolute ? path : Path.Combine("approved", "keys.jsonl");

            IList<ContentBlock> result = await PubSubDecodeTools.DissectCaptureAsync(
                m_services, m_manager, "json", supplied).ConfigureAwait(false);

            AssertDecrypted(result);
        }

        [TestCase("load")]
        [TestCase("decode")]
        [Category("SecurityRegression")]
        public async Task SecurityF30SharedKeyLoadersPreserveHostRootAuthorization(string entryPoint)
        {
            string outside = Path.Combine(m_directory, "outside", "keys.jsonl");
            await WriteKeyLogAsync(outside).ConfigureAwait(false);
            string approved = Path.Combine(m_allowedRoot, "approved.jsonl");
            await WriteKeyLogAsync(approved).ConfigureAwait(false);
            PubSubPcapWriteInfo capture = await PubSubCaptureTools.WritePcapAsync(
                m_services, m_manager, "capture.pcap", CancellationToken.None).ConfigureAwait(false);
            Assert.That(capture.FramesWritten, Is.EqualTo(1));

            if (entryPoint == "load")
            {
                await Assert.ThatAsync(
                    () => PubSubDecodeTools.LoadKeyLogAsync(m_services, outside, CancellationToken.None),
                    Throws.TypeOf<ArgumentException>());
                PubSubKeyLogInfo loaded = await PubSubDecodeTools.LoadKeyLogAsync(
                    m_services, "approved.jsonl", CancellationToken.None).ConfigureAwait(false);
                Assert.That(loaded.FilePath, Is.EqualTo(approved));
                Assert.That(loaded.KeyCount, Is.EqualTo(1));
                AssertDecrypted(await PubSubDecodeTools.DissectCaptureAsync(
                    m_services, m_manager, "json").ConfigureAwait(false));
            }
            else
            {
                await Assert.ThatAsync(
                    () => PubSubDecodeTools.DecodePcapAsync(
                        m_services, capture.FilePath, "json", outside),
                    Throws.TypeOf<ArgumentException>());
                AssertDecrypted(await PubSubDecodeTools.DecodePcapAsync(
                    m_services, capture.FilePath, "json", "approved.jsonl").ConfigureAwait(false));
            }
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF30DissectionWithoutKeysDoesNotExposeEncryptedFields()
        {
            AssertEncryptedWithoutKeys(await PubSubDecodeTools.DissectCaptureAsync(
                m_services, m_manager, "json").ConfigureAwait(false));
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR42StoppedCapturesAndImplicitKeysStayWithTheirCallerAndHostAsync()
        {
            var callerA = new McpCallerIdentity("capture-a", "urn:test:issuer");
            var callerB = new McpCallerIdentity("capture-b", "urn:test:issuer");
            await WriteKeyLogAsync(Path.Combine(m_allowedRoot, "caller-a.jsonl")).ConfigureAwait(false);

            using (m_callers.EnterTrustedCaller(callerA))
            {
                await PubSubDecodeTools.LoadKeyLogAsync(
                    m_services, "caller-a.jsonl", CancellationToken.None).ConfigureAwait(false);
                await PubSubCaptureTools.StartCaptureAsync(m_manager, m_callers, CancellationToken.None)
                    .ConfigureAwait(false);
                CaptureFrame(m_registry);
                await PubSubCaptureTools.StopCaptureAsync(m_manager, m_callers, CancellationToken.None)
                    .ConfigureAwait(false);
                AssertDecrypted(await PubSubDecodeTools.DissectCaptureAsync(
                    m_services, m_manager, "json").ConfigureAwait(false));
            }

            using (m_callers.EnterTrustedCaller(callerB))
            {
                await Assert.ThatAsync(
                    () => PubSubDecodeTools.DissectCaptureAsync(m_services, m_manager, "json"),
                    Throws.TypeOf<PcapDiagnosticsException>(),
                    "A second caller must not read another caller's stopped capture or cached keys.");
            }

            var secondRegistry = new PubSubCaptureRegistry();
            await using var secondManager = new PubSubCaptureSessionManager(secondRegistry);
            var secondServices = new ServiceCollection();
            secondServices.AddSingleton(new OpcUaMcpOptions { PcapBaseFolder = m_allowedRoot });
            secondServices.AddSingleton(new PcapOptions { BaseFolder = m_directory });
            secondServices.AddSingleton(new McpCallerContext());
            secondServices.AddSingleton(secondManager);
            await using ServiceProvider secondProvider = secondServices.BuildServiceProvider();
            McpCallerContext secondCallers = secondProvider.GetRequiredService<McpCallerContext>();

            using (secondCallers.EnterTrustedCaller(callerB))
            {
                await Assert.ThatAsync(
                    () => PubSubDecodeTools.DissectCaptureAsync(secondProvider, secondManager, "json"),
                    Throws.TypeOf<PcapDiagnosticsException>(),
                    "A separate service provider must not retrieve a prior host's stopped capture.");

                await PubSubCaptureTools.StartCaptureAsync(secondManager, secondCallers, CancellationToken.None)
                    .ConfigureAwait(false);
                CaptureFrame(secondRegistry);
                await PubSubCaptureTools.StopCaptureAsync(secondManager, secondCallers, CancellationToken.None)
                    .ConfigureAwait(false);
                AssertEncryptedWithoutKeys(await PubSubDecodeTools.DissectCaptureAsync(
                    secondProvider, secondManager, "json").ConfigureAwait(false));
            }

            using (m_callers.EnterTrustedCaller(callerA))
            {
                AssertDecrypted(await PubSubDecodeTools.DissectCaptureAsync(
                    m_services, m_manager, "json").ConfigureAwait(false));
            }
        }

        /// <summary>
        /// Owner checks and active source identity remain paired across capture replacement.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR42ActiveCaptureOwnershipChangesWithItsSourceAsync()
        {
            IPubSubCaptureSource first = await m_manager.StartForCallerAsync("caller-a").ConfigureAwait(false);
            Assert.That(m_manager.GetActiveSourceForCaller("caller-a"), Is.SameAs(first));
            Assert.That(m_manager.GetActiveSourceForCaller("caller-b"), Is.Null);
            await Assert.ThatAsync(
                async () => await m_manager.StopForCallerAsync("caller-b").ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadUserAccessDenied))
                .ConfigureAwait(false);
            Assert.That(m_manager.GetActiveSourceForCaller("caller-a"), Is.SameAs(first));
            IPubSubCaptureSource? stopped = await m_manager.StopForCallerAsync("caller-a").ConfigureAwait(false);
            Assert.That(stopped, Is.SameAs(first));
            await first.DisposeAsync().ConfigureAwait(false);
            Assert.That(m_manager.GetActiveSourceForCaller("caller-a"), Is.Null);

            IPubSubCaptureSource second = await m_manager.StartForCallerAsync("caller-b").ConfigureAwait(false);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(m_manager.GetActiveSourceForCaller("caller-a"), Is.Null);
            Assert.That(m_manager.GetActiveSourceForCaller("caller-b"), Is.SameAs(second));
        }

        /// <summary>
        /// Failed startup removes its observer before a subsequent caller starts another capture.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR42FailedCaptureStartupDetachesObserverAndAllowsRetryAsync()
        {
            var registry = new Mock<IPubSubCaptureRegistry>(MockBehavior.Strict);
            IPubSubCaptureObserver? observer = null;
            bool rejectStartup = true;
            registry.Setup(value => value.SetObserver(It.IsAny<IPubSubCaptureObserver>()))
                .Callback<IPubSubCaptureObserver>(value =>
                {
                    observer = value;
                    if (rejectStartup)
                    {
                        throw new InvalidOperationException("Capture registration failed.");
                    }
                });
            registry.Setup(value => value.TryClearObserver(It.IsAny<IPubSubCaptureObserver>()))
                .Returns<IPubSubCaptureObserver>(value =>
                {
                    if (!ReferenceEquals(value, observer))
                    {
                        return false;
                    }
                    observer = null;
                    return true;
                });
            await using var manager = new PubSubCaptureSessionManager(registry.Object);

            await Assert.ThatAsync(
                () => manager.StartForCallerAsync("caller-a").AsTask(),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Capture registration failed."))
                .ConfigureAwait(false);

            Assert.That(observer, Is.Null);
            Assert.That(manager.ActiveSource, Is.Null);
            Assert.That(manager.GetActiveSourceForCaller("caller-a"), Is.Null);
            registry.Verify(value => value.TryClearObserver(It.IsAny<IPubSubCaptureObserver>()), Times.Once);

            rejectStartup = false;
            IPubSubCaptureSource source = await manager.StartForCallerAsync("caller-b").ConfigureAwait(false);
            Assert.That(observer, Is.SameAs(source));
            Assert.That(manager.GetActiveSourceForCaller("caller-a"), Is.Null);
            Assert.That(manager.GetActiveSourceForCaller("caller-b"), Is.SameAs(source));
        }

        /// <summary>
        /// Disposing supplied or returned keys and clearing another caller cannot change retained material.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR42LoadedKeysAreDefensiveAndClearedOnlyForOwnerAsync()
        {
            using var supplied = new PubSubKeyMaterial("group-a", 7, "policy-a", [1, 2, 3], [4, 5], [6]);
            await m_manager.StoreKeyMaterialForCallerAsync("caller-a", [supplied]).ConfigureAwait(false);
            supplied.Dispose();

            ArrayOf<PubSubKeyMaterial> first = await m_manager.CopyKeyMaterialForCallerAsync("caller-a")
                .ConfigureAwait(false);
            Assert.That(first.Count, Is.EqualTo(1));
            using PubSubKeyMaterial copy = first[0];
            Assert.That(copy.SecurityGroupId, Is.EqualTo("group-a"));
            Assert.That(copy.TokenId, Is.EqualTo(7));
            Assert.That(copy.SecurityPolicyUri, Is.EqualTo("policy-a"));
            Assert.That(copy.SigningKey.ToArray(), Is.EqualTo(s_expectedSigningKey));
            Assert.That(copy.EncryptingKey.ToArray(), Is.EqualTo(s_expectedEncryptingKey));
            Assert.That(copy.KeyNonce.ToArray(), Is.EqualTo(s_expectedKeyNonce));
            copy.Dispose();

            await m_manager.ClearKeyMaterialForCallerAsync("caller-b").ConfigureAwait(false);
            ArrayOf<PubSubKeyMaterial> retained = await m_manager.CopyKeyMaterialForCallerAsync("caller-a")
                .ConfigureAwait(false);
            Assert.That(retained.Count, Is.EqualTo(1));
            using PubSubKeyMaterial retainedCopy = retained[0];
            Assert.That(retainedCopy, Is.Not.SameAs(copy));
            Assert.That(retainedCopy.SigningKey.ToArray(), Is.EqualTo(s_expectedSigningKey));
            Assert.That(retainedCopy.EncryptingKey.ToArray(), Is.EqualTo(s_expectedEncryptingKey));
            Assert.That(retainedCopy.KeyNonce.ToArray(), Is.EqualTo(s_expectedKeyNonce));
            Assert.That((await m_manager.CopyKeyMaterialForCallerAsync("caller-b").ConfigureAwait(false)).Count,
                Is.Zero);

            await m_manager.ClearKeyMaterialForCallerAsync("caller-a").ConfigureAwait(false);
            Assert.That((await m_manager.CopyKeyMaterialForCallerAsync("caller-a").ConfigureAwait(false)).Count,
                Is.Zero);
            Assert.That(retainedCopy.SigningKey.ToArray(), Is.EqualTo(s_expectedSigningKey));
        }

        /// <summary>
        /// Cancellation and a malformed replacement leave the owner's previous keys intact.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR42FailedKeyReplacementRetainsOwnedSnapshotAsync(bool cancelled)
        {
            using var original = new PubSubKeyMaterial("group-a", 7, "policy-a", [1, 2, 3], [4, 5], [6]);
            using var replacement = new PubSubKeyMaterial("group-b", 8, "policy-b", [9], [10], [11]);
            await m_manager.StoreKeyMaterialForCallerAsync("caller-a", [original]).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();

            if (cancelled)
            {
                cancellation.Cancel();
                await Assert.ThatAsync(
                    () => m_manager.StoreKeyMaterialForCallerAsync("caller-a", [replacement], cancellation.Token)
                        .AsTask(),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(
                    () => m_manager.StoreKeyMaterialForCallerAsync("caller-a", [replacement, null!]).AsTask(),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("key"))
                    .ConfigureAwait(false);
            }

            ArrayOf<PubSubKeyMaterial> retained = await m_manager.CopyKeyMaterialForCallerAsync("caller-a")
                .ConfigureAwait(false);
            Assert.That(retained.Count, Is.EqualTo(1));
            using PubSubKeyMaterial copy = retained[0];
            Assert.That(copy.SecurityGroupId, Is.EqualTo("group-a"));
            Assert.That(copy.TokenId, Is.EqualTo(7));
            Assert.That(copy.SecurityPolicyUri, Is.EqualTo("policy-a"));
            Assert.That(copy.SigningKey.ToArray(), Is.EqualTo(s_expectedSigningKey));
            Assert.That(copy.EncryptingKey.ToArray(), Is.EqualTo(s_expectedEncryptingKey));
            Assert.That(copy.KeyNonce.ToArray(), Is.EqualTo(s_expectedKeyNonce));
            Assert.That(replacement.SigningKey[0], Is.EqualTo(9));
        }

        private void CaptureFrame(PubSubCaptureRegistry registry)
        {
            var context = new PubSubCaptureContext(
                PubSubCaptureDirection.Inbound,
                kTransportProfile,
                DateTimeUtc.From(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                "127.0.0.1:4840");
            registry.CurrentObserver!.OnFrameCaptured(in context, m_encryptedFrame);
        }

        private async Task WriteKeyLogAsync(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var writer = new PubSubKeyLogWriter(path);
            await writer.AppendAsync(m_material).ConfigureAwait(false);
        }

        private static void AssertDecrypted(IList<ContentBlock> result)
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0], Is.TypeOf<TextContentBlock>());
            using var document = JsonDocument.Parse(((TextContentBlock)result[0]).Text);
            Assert.That(document.RootElement.GetArrayLength(), Is.EqualTo(1));
            JsonElement frame = document.RootElement[0];
            Assert.That(frame.GetProperty("IsDecoded").GetBoolean(), Is.True);
            Assert.That(frame.GetProperty("SecurityState").GetString(), Is.EqualTo("Encrypted"));
            Assert.That(frame.GetProperty("DiagnosticMessage").GetString(), Is.EqualTo("decrypted"));
            Assert.That(frame.GetProperty("SecurityTokenId").GetUInt32(), Is.EqualTo(1234));
            JsonElement dataSets = frame.GetProperty("DataSets");
            Assert.That(dataSets.GetArrayLength(), Is.EqualTo(1));
            Assert.That(dataSets[0].GetProperty("Fields")[0].GetProperty("Value").GetString(), Is.EqualTo("42"));
        }

        private static void AssertEncryptedWithoutKeys(IList<ContentBlock> result)
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0], Is.TypeOf<TextContentBlock>());
            using var document = JsonDocument.Parse(((TextContentBlock)result[0]).Text);
            Assert.That(document.RootElement.GetArrayLength(), Is.EqualTo(1));
            JsonElement frame = document.RootElement[0];
            Assert.That(frame.GetProperty("IsDecoded").GetBoolean(), Is.False);
            Assert.That(frame.GetProperty("DiagnosticMessage").GetString(), Is.EqualTo("encrypted (key required)"));
            Assert.That(frame.GetProperty("DataSets").GetArrayLength(), Is.Zero);
        }

        private static async Task<byte[]> CreateEncryptedFrameAsync(PubSubKeyMaterial material)
        {
            using PubSubSecurityKeyRing ring = new(material.SecurityGroupId);
            using PubSubSecurityKey key = new(
                material.TokenId,
                ByteString.Create(material.SigningKey.ToArray()),
                ByteString.Create(material.EncryptingKey.ToArray()),
                ByteString.Create(material.KeyNonce.ToArray()),
                DateTimeUtc.From(DateTime.UtcNow),
                TimeSpan.FromMinutes(30));
            ring.SetCurrent(key);
            using RandomNonceProvider nonce = new(PublisherId.FromUInt16(700));
            var wrapper = new UadpSecurityWrapper(
                ResolvePolicy(material.SecurityPolicyUri),
                new StaticSecurityKeyProvider(material.SecurityGroupId, ring),
                nonce,
                new SecurityTokenWindow(),
                new CaptureTelemetry());
            var message = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = PublisherId.FromUInt16(700),
                WriterGroupId = 7,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 100,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [new DataSetField { Value = new Variant(42) }]
                    }
                ]
            };
            var context = new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                TimeProvider.System);
            ReadOnlyMemory<byte> encoded = UadpEncoder.EncodeWithSecurityBoundary(
                message, context, out int payloadOffset);
            ReadOnlyMemory<byte> secured = await wrapper.WrapAsync(
                encoded[..payloadOffset], encoded[payloadOffset..], UadpSecurityWrapOptions.SignAndEncrypt)
                .ConfigureAwait(false);
            return secured.ToArray();
        }

        private static IPubSubSecurityPolicy ResolvePolicy(string policyUri)
        {
            return PubSubSecurityPolicyRegistry.Default.GetByUri(policyUri) ??
                throw new InvalidOperationException($"PubSub security policy '{policyUri}' is not registered.");
        }

        private sealed class CaptureTelemetry : TelemetryContextBase
        {
            public CaptureTelemetry()
                : base(NullLoggerFactory.Instance)
            {
            }
        }

        private const string kTransportProfile = "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp";
        private static readonly byte[] s_expectedSigningKey = [1, 2, 3];
        private static readonly byte[] s_expectedEncryptingKey = [4, 5];
        private static readonly byte[] s_expectedKeyNonce = [6];
        private string m_directory = string.Empty;
        private string m_allowedRoot = string.Empty;
        private ServiceProvider m_services = null!;
        private PubSubCaptureSessionManager m_manager = null!;
        private PubSubCaptureRegistry m_registry = null!;
        private McpCallerContext m_callers = null!;
        private PubSubKeyMaterial m_material = null!;
        private byte[] m_encryptedFrame = [];
    }
}
#endif
