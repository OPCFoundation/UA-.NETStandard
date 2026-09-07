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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Security
{
    /// <summary>
    /// Verifies the fail-closed wiring of
    /// <see cref="PubSubSecurityWrapperResolver"/> through the
    /// dependency-injection extensions and the
    /// <see cref="PubSubApplicationBuilder"/> per
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3">
    /// Part 14 §8.3</see>.
    /// </summary>
    [TestFixture]
    [TestSpec("8.3")]
    public sealed class PubSubSecurityWiringTests
    {
        private const string UdpProfile =
            "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp";

        private const string DemoGroup = "DemoSecurityGroup";

        [Test]
        public void DependencyInjectionRegistersSecurityWrapperResolver()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddOpcUa().AddPubSub();

            using ServiceProvider sp = services.BuildServiceProvider();
            IPubSubSecurityWrapperResolver? resolver = sp.GetService<IPubSubSecurityWrapperResolver>();

            Assert.That(resolver, Is.Not.Null);
        }

        [Test]
        public async Task DependencyInjectionRoutesPubSubCryptoThroughTheRegisteredProviderAsync()
        {
            var provider = new RecordingSymmetricProvider();

            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services
                .AddOpcUa()
                .AddCryptoProvider(builder => builder
                    .For(CryptoPurpose.ChannelSymmetric)
                    .Use(provider))
                .AddPubSub();
            services.AddSingleton<IPubSubSecurityKeyProvider>(CreateKeyProvider(DemoGroup));

            using ServiceProvider sp = services.BuildServiceProvider();
            var resolver = sp.GetRequiredService<IPubSubSecurityWrapperResolver>();

            PubSubSecurityContext? context = resolver.Resolve(
                SecuredConfiguration().Connections[0]);
            Assert.That(context, Is.Not.Null);

            byte[] prefix = [0xB1, 0x10, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00];
            byte[] plaintext =
            [
                0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27,
                0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F
            ];

            await context!.Wrapper
                .WrapAsync(prefix, plaintext, context.WrapOptions)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(provider.EncryptCount, Is.GreaterThan(0),
                    "A container-registered provider must perform the PubSub encryption.");
                Assert.That(provider.SignCount, Is.GreaterThan(0),
                    "A container-registered provider must produce the PubSub signature.");
            });
        }

        [Test]
        public void BuildSecuredConnectionWithoutKeySourceThrows()
        {
            PubSubApplicationBuilder builder = new PubSubApplicationBuilder(
                    NUnitTelemetryContext.Create())
                .WithApplicationId("secured-no-keys")
                .UseConfiguration(SecuredConfiguration())
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory());

            Assert.That(() => builder.Build(),
                Throws.TypeOf<PubSubConfigurationException>());
        }

        [Test]
        public async Task BuildSecuredConnectionWithKeyProviderSucceedsAsync()
        {
            await using IPubSubApplication app = new PubSubApplicationBuilder(
                    NUnitTelemetryContext.Create())
                .WithApplicationId("secured-with-keys")
                .UseConfiguration(SecuredConfiguration())
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory())
                .AddSecurityKeyProvider(CreateKeyProvider(DemoGroup))
                .Build();

            Assert.That(app.Connections, Has.Count.EqualTo(1));
        }

        private static PubSubConfigurationDataType SecuredConfiguration()
        {
            return new PubSubConfigurationDataType
            {
                Connections =
                [
                    new PubSubConnectionDataType
                    {
                        Name = "secured-conn",
                        TransportProfileUri = UdpProfile,
                        PublisherId = new Variant((ushort)7),
                        Address = new ExtensionObject(
                            new NetworkAddressUrlDataType
                            {
                                Url = "opc.udp://224.0.0.22:4840"
                            }),
                        WriterGroups =
                        [
                            new WriterGroupDataType
                            {
                                Name = "wg",
                                WriterGroupId = 1,
                                PublishingInterval = 1000,
                                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                                SecurityGroupId = DemoGroup,
                                SecurityKeyServices =
                                [
                                    new EndpointDescription
                                    {
                                        EndpointUrl = "opc.tcp://localhost:4840"
                                    }
                                ]
                            }
                        ]
                    }
                ],
                PublishedDataSets = []
            };
        }

        private static StaticSecurityKeyProvider CreateKeyProvider(string securityGroupId)
        {
            PubSubAes256CtrPolicy policy = PubSubAes256CtrPolicy.Instance;
            byte[] signing = new byte[policy.SigningKeyLength];
            byte[] encrypting = new byte[policy.EncryptingKeyLength];
            byte[] nonce = new byte[policy.NonceLength];
            for (int i = 0; i < signing.Length; i++)
            {
                signing[i] = (byte)(i + 1);
            }
            for (int i = 0; i < encrypting.Length; i++)
            {
                encrypting[i] = (byte)(i + 100);
            }
            for (int i = 0; i < nonce.Length; i++)
            {
                nonce[i] = (byte)(i + 200);
            }

            var key = new PubSubSecurityKey(
                1U,
                ByteString.Create(signing),
                ByteString.Create(encrypting),
                ByteString.Create(nonce),
                DateTimeUtc.From(DateTime.UtcNow),
                TimeSpan.FromMinutes(60));

            var ring = new PubSubSecurityKeyRing(securityGroupId);
            ring.SetCurrent(key);
            return new StaticSecurityKeyProvider(securityGroupId, ring);
        }

        private sealed class StubTransportFactory : IPubSubTransportFactory
        {
            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                return new StubTransport();
            }
        }

        private sealed class StubTransport : IPubSubTransport
        {
            private bool m_isConnected;

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public PubSubTransportDirection Direction => PubSubTransportDirection.SendReceive;

            public bool IsConnected => m_isConnected;

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                m_isConnected = true;
                return default;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                m_isConnected = false;
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                return default;
            }

            public System.Collections.Generic.IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                CancellationToken cancellationToken = default)
            {
                return TestAsyncEnumerable.Empty<PubSubTransportFrame>();
            }

            public ValueTask DisposeAsync()
            {
                m_isConnected = false;
                return default;
            }
        }

        /// <summary>
        /// Stands in for a provider-backed module: counts the operations that
        /// reach it and defers the arithmetic to the platform.
        /// </summary>
        private sealed class RecordingSymmetricProvider : ICryptoProvider, ISymmetricCryptoProvider
        {
            public int EncryptCount => Volatile.Read(ref m_encryptCount);

            public int SignCount => Volatile.Read(ref m_signCount);

            public string Name => "Recording";

            public CryptoValidationStatus Validation
                => new(CryptoValidationLevel.Uncertified, "test double");

            public ArrayOf<CryptoCapability> Capabilities { get; } =
                new(new[] { new CryptoCapability(CryptoPurpose.ChannelSymmetric) });

            public bool Supports(SymmetricEncryptionAlgorithm algorithm)
            {
                return PlatformSymmetricCryptoProvider.Instance.Supports(algorithm);
            }

            public bool Supports(SymmetricSignatureAlgorithm algorithm)
            {
                return PlatformSymmetricCryptoProvider.Instance.Supports(algorithm);
            }

            public void Encrypt(
                SymmetricEncryptionAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> iv,
                ReadOnlySpan<byte> plaintext,
                Span<byte> ciphertext)
            {
                Interlocked.Increment(ref m_encryptCount);
                PlatformSymmetricCryptoProvider.Instance
                    .Encrypt(algorithm, key, iv, plaintext, ciphertext);
            }

            public void Decrypt(
                SymmetricEncryptionAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> iv,
                ReadOnlySpan<byte> ciphertext,
                Span<byte> plaintext)
            {
                PlatformSymmetricCryptoProvider.Instance
                    .Decrypt(algorithm, key, iv, ciphertext, plaintext);
            }

            public void EncryptAuthenticated(
                SymmetricEncryptionAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> nonce,
                ReadOnlySpan<byte> plaintext,
                Span<byte> ciphertext,
                Span<byte> tag,
                ReadOnlySpan<byte> associatedData)
            {
                PlatformSymmetricCryptoProvider.Instance.EncryptAuthenticated(
                    algorithm, key, nonce, plaintext, ciphertext, tag, associatedData);
            }

            public bool DecryptAuthenticated(
                SymmetricEncryptionAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> nonce,
                ReadOnlySpan<byte> ciphertext,
                ReadOnlySpan<byte> tag,
                Span<byte> plaintext,
                ReadOnlySpan<byte> associatedData)
            {
                return PlatformSymmetricCryptoProvider.Instance.DecryptAuthenticated(
                    algorithm, key, nonce, ciphertext, tag, plaintext, associatedData);
            }

            public int GetSignatureLength(SymmetricSignatureAlgorithm algorithm)
            {
                return PlatformSymmetricCryptoProvider.Instance.GetSignatureLength(algorithm);
            }

            public void Sign(
                SymmetricSignatureAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> data,
                Span<byte> signature)
            {
                Interlocked.Increment(ref m_signCount);
                PlatformSymmetricCryptoProvider.Instance.Sign(algorithm, key, data, signature);
            }

            public bool Verify(
                SymmetricSignatureAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> data,
                ReadOnlySpan<byte> signature)
            {
                return PlatformSymmetricCryptoProvider.Instance
                    .Verify(algorithm, key, data, signature);
            }

            private int m_encryptCount;
            private int m_signCount;
        }
    }
}
