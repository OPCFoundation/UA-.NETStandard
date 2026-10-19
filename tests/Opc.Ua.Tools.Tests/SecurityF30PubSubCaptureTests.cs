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
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
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
            m_services = services.BuildServiceProvider();
            var registry = new PubSubCaptureRegistry();
            m_manager = new PubSubCaptureSessionManager(registry);
            await File.WriteAllTextAsync(Path.Combine(m_allowedRoot, "empty.jsonl"), string.Empty)
                .ConfigureAwait(false);
            await PubSubDecodeTools.LoadKeyLogAsync(
                m_services, "empty.jsonl", CancellationToken.None).ConfigureAwait(false);

            IPubSubSecurityPolicy policy = ResolvePolicy(PubSubSecurityPolicyUri.PubSubAes256Ctr);
            m_material = new PubSubKeyMaterial(
                "security-regression-test-group",
                1234,
                policy.PolicyUri,
                RandomNumberGenerator.GetBytes(policy.SigningKeyLength),
                RandomNumberGenerator.GetBytes(policy.EncryptingKeyLength),
                RandomNumberGenerator.GetBytes(AesCtrNonceLayout.KeyNonceLength));
            byte[] frame = await CreateEncryptedFrameAsync(m_material).ConfigureAwait(false);
            await PubSubCaptureTools.StartCaptureAsync(m_manager, CancellationToken.None).ConfigureAwait(false);
            var context = new PubSubCaptureContext(
                PubSubCaptureDirection.Inbound,
                kTransportProfile,
                DateTimeUtc.From(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                "127.0.0.1:4840");
            registry.CurrentObserver!.OnFrameCaptured(in context, frame);
            PubSubCaptureSessionInfo stopped = await PubSubCaptureTools.StopCaptureAsync(
                m_manager, CancellationToken.None).ConfigureAwait(false);
            Assert.That(stopped.FrameCount, Is.EqualTo(1));
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            try
            {
                if (m_services is not null)
                {
                    await PubSubDecodeTools.LoadKeyLogAsync(
                        m_services, "empty.jsonl", CancellationToken.None).ConfigureAwait(false);
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
        private string m_directory = string.Empty;
        private string m_allowedRoot = string.Empty;
        private ServiceProvider m_services = null!;
        private PubSubCaptureSessionManager m_manager = null!;
        private PubSubKeyMaterial m_material = null!;
    }
}
#endif
