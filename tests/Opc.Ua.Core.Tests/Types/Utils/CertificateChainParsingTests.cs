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

#nullable enable

using System;
using System.Buffers;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    /// <summary>
    /// Regressions for certificate-chain parsing and ownership on both parser paths.
    /// Counter deltas are observed without resets, finalizers, or concurrent fixtures.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [NonParallelizable]
    public sealed class CertificateChainParsingTests
    {
        [OneTimeSetUp]
        public void CreateRuntimeChain()
        {
            using Certificate root = CreateCertificate(kRootSubject, 0x11, null, isCa: true);
            using Certificate intermediate = CreateCertificate(
                kIntermediateSubject, 0x22, root, isCa: true);
            using Certificate leaf = CreateCertificate(kLeafSubject, 0x33, intermediate, isCa: false);
            m_certificates = [leaf.RawData, intermediate.RawData, root.RawData];
        }

        [TestCase(false, 1)]
        [TestCase(false, 3)]
        [TestCase(true, 1)]
        [TestCase(true, 3)]
        public void ParseCertificateChainBlobKeepsOrderedCertificatesAliveUntilCollectionDisposal(
            bool useAsnParser,
            int certificateCount)
        {
            byte[] blob = [.. m_certificates.Take(certificateCount).SelectMany(certificate => certificate)];
            long createdBefore = Certificate.InstancesCreated;
            long disposedBefore = Certificate.InstancesDisposed;

            using (CertificateCollection chain = Utils.ParseCertificateChainBlob(
                blob, telemetry: null, useAsnParser: useAsnParser))
            {
                Assert.That(chain, Has.Count.EqualTo(certificateCount));
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(Certificate.InstancesCreated - createdBefore, Is.EqualTo(certificateCount));
                    Assert.That(Certificate.InstancesDisposed - disposedBefore, Is.Zero);
                    for (int index = 0; index < certificateCount; index++)
                    {
                        // The parser has already disposed its temporary handle at this point.
                        Assert.That(chain[index].RawData, Is.EqualTo(m_certificates[index]));
                        Assert.That(chain[index].Subject, Is.EqualTo(s_subjects[index]));
                        Assert.That(chain[index].Issuer, Is.EqualTo(s_issuers[index]));
                        Assert.That(chain[index].NotBefore.ToUniversalTime(), Is.EqualTo(s_notBefore));
                        Assert.That(chain[index].NotAfter.ToUniversalTime(), Is.EqualTo(s_notAfter));
                        Assert.That(chain[index].HashAlgorithmName, Is.EqualTo(HashAlgorithmName.SHA256));
                        Assert.That(chain[index].HasPrivateKey, Is.False);
                    }
                }
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Certificate.InstancesCreated - createdBefore, Is.EqualTo(certificateCount));
                Assert.That(Certificate.InstancesDisposed - disposedBefore, Is.EqualTo(certificateCount));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ParseCertificateChainBlobAcceptsEmptyInputWithoutCreatingCertificates(bool useAsnParser)
        {
            long createdBefore = Certificate.InstancesCreated;
            long disposedBefore = Certificate.InstancesDisposed;

            using (CertificateCollection chain = Utils.ParseCertificateChainBlob(
                ReadOnlyMemory<byte>.Empty, telemetry: null, useAsnParser: useAsnParser))
            {
                Assert.That(chain, Is.Empty);
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Certificate.InstancesCreated - createdBefore, Is.Zero);
                Assert.That(Certificate.InstancesDisposed - disposedBefore, Is.Zero);
            }
        }

        [TestCase(false, 1, false)]
        [TestCase(false, 1, true)]
        [TestCase(false, 2, false)]
        [TestCase(false, 2, true)]
        [TestCase(true, 1, false)]
        [TestCase(true, 1, true)]
        [TestCase(true, 2, false)]
        [TestCase(true, 2, true)]
        public void ParseCertificateChainBlobDisposesValidPrefixWhenSuffixIsInvalid(
            bool useAsnParser,
            int validCertificateCount,
            bool truncateCertificate)
        {
            byte[] suffix = truncateCertificate
                ? m_certificates[validCertificateCount]
                    .AsSpan(0, m_certificates[validCertificateCount].Length - 1)
                    .ToArray()
                : [0x00, 0xFF, 0x00];
            byte[] blob =
            [
                .. m_certificates.Take(validCertificateCount).SelectMany(certificate => certificate),
                .. suffix
            ];
            long createdBefore = Certificate.InstancesCreated;
            long disposedBefore = Certificate.InstancesDisposed;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    () =>
                    {
                        using CertificateCollection chain = Utils.ParseCertificateChainBlob(
                            blob, telemetry: null, useAsnParser: useAsnParser);
                    },
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo((StatusCode)StatusCodes.BadCertificateInvalid));

                long createdDelta = Certificate.InstancesCreated - createdBefore;
                long disposedDelta = Certificate.InstancesDisposed - disposedBefore;
                TestContext.Out.WriteLine($"Parsed prefix: {createdDelta} created, {disposedDelta} disposed.");
                Assert.That(createdDelta, Is.EqualTo(validCertificateCount),
                    "The valid prefix must reach the loader before the invalid suffix.");
                Assert.That(disposedDelta, Is.EqualTo(createdDelta),
                    "Every prefix certificate must be disposed before the parsing exception escapes.");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ParseCertificateChainBlobPropagatesMemoryManagerProgrammingError(bool useAsnParser)
        {
            using var memory = new ThrowingMemoryManager();
            long createdBefore = Certificate.InstancesCreated;
            long disposedBefore = Certificate.InstancesDisposed;
            InvalidOperationException? caught = Assert.Throws<InvalidOperationException>(() =>
            {
                using CertificateCollection chain = Utils.ParseCertificateChainBlob(
                    memory.Input, telemetry: null, useAsnParser: useAsnParser);
            });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(caught, Is.SameAs(memory.Failure),
                    "A programmer exception must not be reclassified as malformed certificate data.");
                Assert.That(memory.SpanAccessCount, Is.EqualTo(1));
                Assert.That(Certificate.InstancesCreated - createdBefore, Is.Zero);
                Assert.That(Certificate.InstancesDisposed - disposedBefore, Is.Zero);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ParseCertificateChainBlobDisposesPrefixWhenMemoryAccessThrows(bool useAsnParser)
        {
            byte[] blob = [.. m_certificates.SelectMany(certificate => certificate)];
            using var memory = new ThrowingMemoryManager(blob);
            long createdBefore = Certificate.InstancesCreated;
            long disposedBefore = Certificate.InstancesDisposed;

            InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() =>
            {
                using CertificateCollection chain = Utils.ParseCertificateChainBlob(
                    memory.Input, telemetry: null, useAsnParser: useAsnParser);
            });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exception, Is.SameAs(memory.Failure));
                Assert.That(Certificate.InstancesCreated - createdBefore, Is.EqualTo(1));
                Assert.That(Certificate.InstancesDisposed - disposedBefore, Is.EqualTo(1));
                Assert.That(memory.SpanAccessCount, Is.GreaterThan(1));
            }
        }

        private static Certificate CreateCertificate(
            string subject,
            byte serialNumber,
            Certificate? issuer,
            bool isCa)
        {
            ICertificateBuilder builder = CertificateBuilder
                .Create(subject)
                .SetNotBefore(s_notBefore)
                .SetNotAfter(s_notAfter)
                .SetHashAlgorithm(HashAlgorithmName.SHA256);
            if (isCa)
            {
                builder.SetCAConstraint();
            }
            if (issuer != null)
            {
                builder.SetIssuer(issuer);
            }
            return builder
                .SetSerialNumber([serialNumber])
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private sealed class ThrowingMemoryManager : MemoryManager<byte>
        {
            public ThrowingMemoryManager(byte[]? input = null)
            {
                m_input = input;
                m_createdBefore = Certificate.InstancesCreated;
            }

            // Unlike MemoryManager.Memory, CreateMemory does not call GetSpan to obtain a length.
            public ReadOnlyMemory<byte> Input => CreateMemory(m_input?.Length ?? 1);

            public InvalidOperationException Failure { get; } =
                new("Injected failure from MemoryManager.GetSpan.");

            public int SpanAccessCount { get; private set; }

            public override Span<byte> GetSpan()
            {
                SpanAccessCount++;
                if (m_input != null && Certificate.InstancesCreated == m_createdBefore)
                {
                    return m_input;
                }
                throw Failure;
            }

            public override MemoryHandle Pin(int elementIndex = 0)
            {
                throw new NotSupportedException();
            }

            public override void Unpin()
            {
            }

            protected override void Dispose(bool disposing)
            {
            }

            private readonly byte[]? m_input;
            private readonly long m_createdBefore;
        }

        private const string kRootSubject = "CN=Chain Parsing Root";
        private const string kIntermediateSubject = "CN=Chain Parsing Intermediate";
        private const string kLeafSubject = "CN=Chain Parsing Leaf";
        private static readonly string[] s_subjects = [kLeafSubject, kIntermediateSubject, kRootSubject];
        private static readonly string[] s_issuers = [kIntermediateSubject, kRootSubject, kRootSubject];
        private static readonly DateTime s_notBefore = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime s_notAfter = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private byte[][] m_certificates = [];
    }
}
