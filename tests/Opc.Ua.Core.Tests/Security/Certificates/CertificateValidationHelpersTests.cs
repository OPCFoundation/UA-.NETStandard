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
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests TLS validation certificate collection construction.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class CertificateValidationHelpersTests
    {
        [Test]
        public void ChainWithSeveralElementsCopiesEveryElementInOrder()
        {
            using TestCertificateChain source = TestCertificateChain.Create();

            using CertificateCollection validation = CertificateValidationHelpers
                .BuildValidationCertificateCollection(source.LeafX509, source.Chain);

            Assert.That(validation, Has.Count.EqualTo(source.Chain.ChainElements.Count));
            for (int ii = 0; ii < source.Chain.ChainElements.Count; ii++)
            {
                Assert.That(
                    validation[ii].Thumbprint,
                    Is.EqualTo(source.Chain.ChainElements[ii].Certificate.Thumbprint));
            }
        }

        [Test]
        public void NullChainUsesLeafOnlyFallback()
        {
            using TestCertificateChain source = TestCertificateChain.Create();

            using CertificateCollection validation = CertificateValidationHelpers
                .BuildValidationCertificateCollection(source.LeafX509, chain: null);

            Assert.That(validation, Has.Count.EqualTo(1));
            Assert.That(validation[0].Thumbprint, Is.EqualTo(source.LeafX509.Thumbprint));
        }

        [Test]
        public void EmptyChainUsesLeafOnlyFallback()
        {
            using TestCertificateChain source = TestCertificateChain.Create();
            using var emptyChain = new X509Chain();

            Assert.That(emptyChain.ChainElements, Is.Empty);

            using CertificateCollection validation = CertificateValidationHelpers
                .BuildValidationCertificateCollection(source.LeafX509, emptyChain);

            Assert.That(validation, Has.Count.EqualTo(1));
            Assert.That(validation[0].Thumbprint, Is.EqualTo(source.LeafX509.Thumbprint));
        }

        [Test]
        public void EmptyChainCanPreserveEmptyCollection()
        {
            using TestCertificateChain source = TestCertificateChain.Create();
            using var emptyChain = new X509Chain();

            Assert.That(emptyChain.ChainElements, Is.Empty);

            using CertificateCollection validation = CertificateValidationHelpers
                .BuildValidationCertificateCollection(
                    source.LeafX509,
                    emptyChain,
                    CertificateValidationHelpers.EmptyChainHandling.PreserveEmptyChain);

            Assert.That(validation, Is.Empty);
        }

        [Test]
        public void ReturnedCollectionSurvivesSourceChainDisposal()
        {
            using CertificateCollection validation = BuildCollectionFromDisposedSource(
                out string leafThumbprint);

            Assert.That(validation, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(validation[0].Thumbprint, Is.EqualTo(leafThumbprint));

            using X509Certificate2 copy = validation[0].AsX509Certificate2();
            Assert.That(copy.Thumbprint, Is.EqualTo(leafThumbprint));
        }

        [Test]
        public void ConstructionFailureDisposesAlreadyBuiltCertificates()
        {
            using TestCertificateChain source = TestCertificateChain.Create();
            long createdBefore = Certificate.InstancesCreated;
            long disposedBefore = Certificate.InstancesDisposed;
            int calls = 0;

            InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(
                () => CertificateValidationHelpers.BuildValidationCertificateCollection(
                    source.LeafX509,
                    source.Chain,
                    certificate =>
                    {
                        calls++;
                        if (calls == 2)
                        {
                            throw new InvalidOperationException("Simulated certificate factory failure.");
                        }

                        return Certificate.FromRawData(certificate.GetRawCertData());
                    }));

            Assert.That(exception, Is.Not.Null);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(Certificate.InstancesCreated - createdBefore, Is.EqualTo(1));
            Assert.That(
                Certificate.InstancesDisposed - disposedBefore,
                Is.EqualTo(Certificate.InstancesCreated - createdBefore));
        }

        private static CertificateCollection BuildCollectionFromDisposedSource(
            out string leafThumbprint)
        {
            using TestCertificateChain source = TestCertificateChain.Create();
            leafThumbprint = source.Chain.ChainElements[0].Certificate.Thumbprint;
            return CertificateValidationHelpers.BuildValidationCertificateCollection(
                source.LeafX509,
                source.Chain);
        }

        private static Certificate CreateCertificateAuthority(string commonName)
        {
            return CertificateBuilder
                .Create($"CN={commonName}")
                .SetCAConstraint(-1)
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private static Certificate CreateIssuedCertificate(
            string commonName,
            Certificate issuer)
        {
            return CertificateBuilder
                .Create($"CN={commonName}")
                .SetIssuer(issuer)
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private sealed class TestCertificateChain : IDisposable
        {
            private TestCertificateChain(
                Certificate root,
                Certificate intermediate,
                Certificate leaf,
                X509Certificate2 rootX509,
                X509Certificate2 intermediateX509,
                X509Certificate2 leafX509,
                X509Chain chain)
            {
                Root = root;
                Intermediate = intermediate;
                Leaf = leaf;
                RootX509 = rootX509;
                IntermediateX509 = intermediateX509;
                LeafX509 = leafX509;
                Chain = chain;
            }

            public Certificate Root { get; }

            public Certificate Intermediate { get; }

            public Certificate Leaf { get; }

            public X509Certificate2 RootX509 { get; }

            public X509Certificate2 IntermediateX509 { get; }

            public X509Certificate2 LeafX509 { get; }

            public X509Chain Chain { get; }

            public static TestCertificateChain Create()
            {
                Certificate? root = CreateCertificateAuthority("TLS Validation Helper Root");
                Certificate? intermediate = null;
                Certificate? leaf = null;
                X509Certificate2? rootX509 = null;
                X509Certificate2? intermediateX509 = null;
                X509Certificate2? leafX509 = null;
                X509Chain? chain = null;
                try
                {
                    intermediate = CertificateBuilder
                        .Create("CN=TLS Validation Helper Intermediate")
                        .SetCAConstraint(0)
                        .SetIssuer(root)
                        .SetRSAKeySize(2048)
                        .CreateForRSA();
                    leaf = CreateIssuedCertificate("TLS Validation Helper Leaf", intermediate);
                    rootX509 = root.AsX509Certificate2();
                    intermediateX509 = intermediate.AsX509Certificate2();
                    leafX509 = leaf.AsX509Certificate2();
                    chain = new X509Chain();
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.ChainPolicy.VerificationFlags =
                        X509VerificationFlags.AllowUnknownCertificateAuthority;
                    chain.ChainPolicy.ExtraStore.Add(intermediateX509);
                    chain.ChainPolicy.ExtraStore.Add(rootX509);
                    _ = chain.Build(leafX509);

                    if (chain.ChainElements.Count != 3)
                    {
                        throw new InvalidOperationException("Failed to build the test certificate chain.");
                    }

                    var result = new TestCertificateChain(
                        root!,
                        intermediate!,
                        leaf!,
                        rootX509!,
                        intermediateX509!,
                        leafX509!,
                        chain!);
                    root = null!;
                    intermediate = null;
                    leaf = null;
                    rootX509 = null;
                    intermediateX509 = null;
                    leafX509 = null;
                    chain = null;
                    return result;
                }
                finally
                {
                    chain?.Dispose();
                    leafX509?.Dispose();
                    intermediateX509?.Dispose();
                    rootX509?.Dispose();
                    leaf?.Dispose();
                    intermediate?.Dispose();
                    root?.Dispose();
                }
            }

            public void Dispose()
            {
                Chain.Dispose();
                LeafX509.Dispose();
                IntermediateX509.Dispose();
                RootX509.Dispose();
                Leaf.Dispose();
                Intermediate.Dispose();
                Root.Dispose();
            }
        }
    }
}
