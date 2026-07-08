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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Security
{
    /// <summary>
    /// Unit tests for the fail-closed
    /// <see cref="PubSubSecurityWrapperResolver"/> that wires the
    /// message-security subsystem into the runtime data path per
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3">
    /// Part 14 §8.3</see>.
    /// </summary>
    [TestFixture]
    [TestSpec("8.3")]
    [Parallelizable(ParallelScope.All)]
    public sealed class PubSubSecurityWrapperResolverTests
    {
        private const string UdpProfile =
            "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp";

        private const string DemoGroup = "DemoSecurityGroup";

        [Test]
        public void ResolveReturnsNullForNoneSecurityMode()
        {
            var resolver = new PubSubSecurityWrapperResolver(
                [CreateKeyProvider(DemoGroup)],
                NUnitTelemetryContext.Create());

            PubSubSecurityContext? context = resolver.Resolve(
                SecuredConnection(MessageSecurityMode.None, DemoGroup));

            Assert.That(context, Is.Null);
        }

        [Test]
        public void ResolveReturnsConfiguredWrapperForSignAndEncrypt()
        {
            var resolver = new PubSubSecurityWrapperResolver(
                [CreateKeyProvider(DemoGroup)],
                NUnitTelemetryContext.Create());

            PubSubSecurityContext? context = resolver.Resolve(
                SecuredConnection(MessageSecurityMode.SignAndEncrypt, DemoGroup));

            Assert.Multiple(() =>
            {
                Assert.That(context, Is.Not.Null);
                Assert.That(context!.Wrapper, Is.Not.Null);
                Assert.That(context.WrapOptions,
                    Is.EqualTo(UadpSecurityWrapOptions.SignAndEncrypt));
            });
        }

        [Test]
        public void ResolveReturnsSignOnlyOptionsForSignMode()
        {
            var resolver = new PubSubSecurityWrapperResolver(
                [CreateKeyProvider(DemoGroup)],
                NUnitTelemetryContext.Create());

            PubSubSecurityContext? context = resolver.Resolve(
                SecuredConnection(MessageSecurityMode.Sign, DemoGroup));

            Assert.Multiple(() =>
            {
                Assert.That(context, Is.Not.Null);
                Assert.That(context!.WrapOptions,
                    Is.EqualTo(UadpSecurityWrapOptions.SignOnly));
            });
        }

        [Test]
        public void ResolveReturnsNullWhenNoKeyProviderForSecurityGroup()
        {
            var resolver = new PubSubSecurityWrapperResolver(
                [CreateKeyProvider("OtherGroup")],
                NUnitTelemetryContext.Create());

            PubSubSecurityContext? context = resolver.Resolve(
                SecuredConnection(MessageSecurityMode.SignAndEncrypt, DemoGroup));

            Assert.That(context, Is.Null);
        }

        [Test]
        public void ResolveReturnsNullWhenNoKeyProvidersRegistered()
        {
            var resolver = new PubSubSecurityWrapperResolver(
                [],
                NUnitTelemetryContext.Create());

            PubSubSecurityContext? context = resolver.Resolve(
                SecuredConnection(MessageSecurityMode.SignAndEncrypt, DemoGroup));

            Assert.That(context, Is.Null);
        }

        [Test]
        public void TryResolveConnectionSecuritySelectsStrictestMode()
        {
            var connection = new PubSubConnectionDataType
            {
                Name = "mixed",
                TransportProfileUri = UdpProfile,
                WriterGroups =
                [
                    new WriterGroupDataType
                    {
                        Name = "wg-sign",
                        SecurityMode = MessageSecurityMode.Sign,
                        SecurityGroupId = "SignGroup"
                    }
                ],
                ReaderGroups =
                [
                    new ReaderGroupDataType
                    {
                        Name = "rg-encrypt",
                        SecurityMode = MessageSecurityMode.SignAndEncrypt,
                        SecurityGroupId = DemoGroup
                    }
                ]
            };

            bool resolved = PubSubSecurityWrapperResolver.TryResolveConnectionSecurity(
                connection,
                out MessageSecurityMode mode,
                out string securityGroupId);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.True);
                Assert.That(mode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                Assert.That(securityGroupId, Is.EqualTo(DemoGroup));
            });
        }

        [Test]
        public void TryResolveConnectionSecurityReturnsFalseWhenAllNone()
        {
            bool resolved = PubSubSecurityWrapperResolver.TryResolveConnectionSecurity(
                SecuredConnection(MessageSecurityMode.None, DemoGroup),
                out MessageSecurityMode mode,
                out _);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.False);
                Assert.That(mode, Is.EqualTo(MessageSecurityMode.None));
            });
        }

        [Test]
        public async Task ResolveProducesCiphertextDifferentFromPlaintextAsync()
        {
            var resolver = new PubSubSecurityWrapperResolver(
                [CreateKeyProvider(DemoGroup)],
                NUnitTelemetryContext.Create());

            PubSubSecurityContext? context = resolver.Resolve(
                SecuredConnection(MessageSecurityMode.SignAndEncrypt, DemoGroup));
            Assert.That(context, Is.Not.Null);

            byte[] prefix = [0xB1, 0x10, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00];
            byte[] plaintext =
            [
                0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
                0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F
            ];

            ReadOnlyMemory<byte> wrapped = await context!.Wrapper
                .WrapAsync(prefix, plaintext, context.WrapOptions)
                .ConfigureAwait(false);

            ReadOnlyMemory<byte> body = wrapped.Slice(prefix.Length);

            Assert.Multiple(() =>
            {
                Assert.That(wrapped.Length, Is.GreaterThan(prefix.Length + plaintext.Length),
                    "Secured frame must carry a security header and signature.");
                Assert.That(ContainsSequence(body.Span, plaintext), Is.False,
                    "Plaintext must not appear verbatim on the wire.");
            });
        }

        private static bool ContainsSequence(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
        {
            if (needle.Length == 0 || haystack.Length < needle.Length)
            {
                return false;
            }
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
                {
                    return true;
                }
            }
            return false;
        }

        private static PubSubConnectionDataType SecuredConnection(
            MessageSecurityMode mode,
            string securityGroupId)
        {
            return new PubSubConnectionDataType
            {
                Name = "secured-conn",
                TransportProfileUri = UdpProfile,
                PublisherId = new Variant((ushort)7),
                WriterGroups =
                [
                    new WriterGroupDataType
                    {
                        Name = "wg",
                        SecurityMode = mode,
                        SecurityGroupId = securityGroupId
                    }
                ]
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
    }
}
