/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

// CA2000: the in-cluster client returned by the factory is disposed in a finally
// block within the same test; there is no cross-test resource leak.
#pragma warning disable CA2000

using System;
using System.IO;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Kubernetes.Tests
{
    /// <summary>
    /// Unit tests for the Kubernetes API client factory that selects between the in-cluster HTTP client and the
    /// not-in-cluster fallback based on the runtime environment.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class KubernetesApiClientFactoryTests
    {
        [Test]
        public void CreateRejectsNullOptions()
        {
            Assert.That(
                () => KubernetesApiClientFactory.Create(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void CreateReturnsNotInClusterClientWhenTokenFileMissing()
        {
            var options = new KubernetesServerOptions
            {
                ApiServerHost = "k8s.test",
                TokenPath = Path.Combine(Path.GetTempPath(), "no-token-" + Guid.NewGuid().ToString("N"))
            };

            IKubernetesApiClient client = KubernetesApiClientFactory.Create(options);

            Assert.That(client.IsInCluster, Is.False);
            Assert.That(client, Is.InstanceOf<NotInClusterKubernetesApiClient>());
        }

        [Test]
        public void CreateReturnsInClusterClientWhenHostAndTokenPresent()
        {
            string tokenPath = WriteTempFile("bearer-token");
            string namespacePath = WriteTempFile("factory-ns");
            var options = new KubernetesServerOptions
            {
                ApiServerHost = "k8s.test",
                ApiServerPort = 6443,
                TokenPath = tokenPath,
                NamespacePath = namespacePath,
                CertificateAuthorityPath = Path.Combine(Path.GetTempPath(), "no-ca-" + Guid.NewGuid().ToString("N"))
            };

            IKubernetesApiClient client = KubernetesApiClientFactory.Create(options);
            try
            {
                Assert.That(client.IsInCluster, Is.True);
                Assert.That(client, Is.InstanceOf<KubernetesHttpApiClient>());
                Assert.That(((KubernetesHttpApiClient)client).DefaultNamespace, Is.EqualTo("factory-ns"));
            }
            finally
            {
                (client as IDisposable)?.Dispose();
                File.Delete(tokenPath);
                File.Delete(namespacePath);
            }
        }

        [Test]
        public void ResolveNamespaceRejectsNullOptions()
        {
            Assert.That(
                () => KubernetesApiClientFactory.ResolveNamespace(null!, Mock.Of<IKubernetesApiClient>()),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ResolveNamespaceRejectsNullClient()
        {
            Assert.That(
                () => KubernetesApiClientFactory.ResolveNamespace(new KubernetesServerOptions(), null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ResolveNamespaceReturnsExplicitNamespace()
        {
            var options = new KubernetesServerOptions { Namespace = "explicit-ns" };

            string resolved = KubernetesApiClientFactory.ResolveNamespace(options, Mock.Of<IKubernetesApiClient>());

            Assert.That(resolved, Is.EqualTo("explicit-ns"));
        }

        [Test]
        public void ResolveNamespaceReadsNamespaceFileWhenNotConfigured()
        {
            string namespacePath = WriteTempFile("  file-ns  ");
            var options = new KubernetesServerOptions
            {
                Namespace = null,
                NamespacePath = namespacePath
            };
            try
            {
                string resolved = KubernetesApiClientFactory.ResolveNamespace(options, Mock.Of<IKubernetesApiClient>());

                Assert.That(resolved, Is.EqualTo("file-ns"));
            }
            finally
            {
                File.Delete(namespacePath);
            }
        }

        [Test]
        public void ResolveNamespaceFallsBackToDefaultWhenNoSource()
        {
            var options = new KubernetesServerOptions
            {
                Namespace = null,
                NamespacePath = Path.Combine(Path.GetTempPath(), "no-ns-" + Guid.NewGuid().ToString("N"))
            };

            string resolved = KubernetesApiClientFactory.ResolveNamespace(options, Mock.Of<IKubernetesApiClient>());

            Assert.That(resolved, Is.EqualTo("default"));
        }

        private static string WriteTempFile(string content)
        {
            string path = Path.Combine(Path.GetTempPath(), "k8s-factory-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(path, content);
            return path;
        }
    }
}
