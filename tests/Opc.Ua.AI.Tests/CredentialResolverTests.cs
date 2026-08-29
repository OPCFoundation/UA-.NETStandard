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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.AI.Inference;

namespace Opc.Ua.AI.Tests
{
    /// <summary>
    /// Covers the credential resolvers, which are the only things in the stack
    /// that ever hold a secret value.
    /// </summary>
    /// <remarks>
    /// Clause 9.2 forbids a Server from exposing credential material through any
    /// Attribute, and argues it from the fact that an address space is browsable,
    /// subscribable and historisable - a secret placed there is not exposed once,
    /// it is published, distributed and archived. These resolvers are where the
    /// name a deployment publishes turns into the value it stands for, so their
    /// refusals matter more than their successes.
    /// </remarks>
    [TestFixture]
    public sealed class CredentialResolverTests
    {
        [Test]
        public async Task FileResolverReadsTheKeyMountedAtTheDirectory()
        {
            using var mount = new TempMount();
            mount.Write("api-key", "s3cret");

            var resolver = new FileCredentialResolver(mount.Path);
            string? value = await resolver.ResolveAsync("api-key", CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(value, Is.EqualTo("s3cret"));
        }

        [Test]
        public async Task FileResolverTrimsTheTrailingNewlineAMountLeaves()
        {
            using var mount = new TempMount();
            mount.Write("api-key", "s3cret\n");

            var resolver = new FileCredentialResolver(mount.Path);
            string? value = await resolver.ResolveAsync("api-key", CancellationToken.None)
                .ConfigureAwait(false);

            // Presenting the newline in a header fails in a way that looks like a
            // wrong key rather than a stray byte, which is a bad afternoon.
            Assert.That(value, Is.EqualTo("s3cret"));
        }

        [Test]
        public async Task FileResolverReturnsNullForAKeyTheMountDoesNotCarry()
        {
            using var mount = new TempMount();

            var resolver = new FileCredentialResolver(mount.Path);
            string? value = await resolver.ResolveAsync("absent", CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(value, Is.Null);
        }

        [Test]
        public void FileResolverRefusesAReferenceThatCouldEscapeTheMount()
        {
            using var mount = new TempMount();
            var resolver = new FileCredentialResolver(mount.Path);

            // A reference names a key, not a path. One containing a separator is
            // configuration this Server controls, so it is a mistake worth
            // surfacing rather than input worth repairing.
            Assert.Multiple(() =>
            {
                Assert.That(
                    async () => await resolver.ResolveAsync("../etc/passwd", CancellationToken.None)
                        .ConfigureAwait(false),
                    Throws.ArgumentException);
                Assert.That(
                    async () => await resolver.ResolveAsync("sub/key", CancellationToken.None)
                        .ConfigureAwait(false),
                    Throws.ArgumentException);
                Assert.That(
                    async () => await resolver.ResolveAsync("..\\key", CancellationToken.None)
                        .ConfigureAwait(false),
                    Throws.ArgumentException);
            });
        }

        [Test]
        public async Task NullResolverNeverProducesAValue()
        {
            string? value = await NullCredentialResolver.Instance
                .ResolveAsync("anything", CancellationToken.None).ConfigureAwait(false);

            Assert.That(value, Is.Null);
        }

        [Test]
        public async Task WorkloadIdentityReadsTheTokenTheAudiencePointsAt()
        {
            using var mount = new TempMount();
            string path = mount.Write("token", "projected-token\n");

            // Workload identity has no secret to name, so the configured audience
            // is the token to read. Every platform projects it as a file; reading
            // it directly is what keeps this free of a cloud SDK.
            var resolver = new WorkloadIdentityCredentialResolver(path);
            string? value = await resolver.ResolveAsync(string.Empty, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(value, Is.EqualTo("projected-token"));
        }

        [Test]
        public async Task WorkloadIdentityFallsBackToTheReferenceWhenNoAudienceIsConfigured()
        {
            using var mount = new TempMount();
            string path = mount.Write("token", "from-reference");

            var resolver = new WorkloadIdentityCredentialResolver();
            string? value = await resolver.ResolveAsync(path, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(value, Is.EqualTo("from-reference"));
        }

        [Test]
        public async Task WorkloadIdentityReturnsNullWhenNoTokenIsProjected()
        {
            using var mount = new TempMount();

            var resolver = new WorkloadIdentityCredentialResolver(
                Path.Combine(mount.Path, "never-written"));
            string? value = await resolver.ResolveAsync(string.Empty, CancellationToken.None)
                .ConfigureAwait(false);

            // A Server whose platform projected nothing sends no Authorization
            // header, which is a clearer failure than an empty one.
            Assert.That(value, Is.Null);
        }

        [Test]
        public async Task WorkloadIdentityUsesASuppliedAcquisitionDelegate()
        {
            string? seen = null;
            var resolver = new WorkloadIdentityCredentialResolver(
                (scope, _) =>
                {
                    seen = scope;
                    return new ValueTask<string?>("delegated");
                },
                "the-scope");

            string? value = await resolver.ResolveAsync("ignored", CancellationToken.None)
                .ConfigureAwait(false);

            // This is the seam a host uses to bring its own identity SDK without
            // this assembly depending on one, and the seam a test uses to exercise
            // the path without a platform.
            Assert.Multiple(() =>
            {
                Assert.That(value, Is.EqualTo("delegated"));
                Assert.That(seen, Is.EqualTo("the-scope"));
            });
        }

        [Test]
        public void WorkloadIdentityRefusesANullAcquisitionDelegate()
        {
            Assert.That(
                () => new WorkloadIdentityCredentialResolver(null!, "scope"),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task WorkloadIdentityReadsTheTokenAGoogleExternalAccountConfigNames()
        {
            using var mount = new TempMount();
            string token = mount.Write("gcp-token", "google-projected-token\n");
            string config = mount.Write(
                "external-account.json",
                "{\"type\":\"external_account\",\"credential_source\":{\"file\":\"" +
                token.Replace("\\", "\\\\", StringComparison.Ordinal) +
                "\"}}");

            // Google is the one platform that does not name the token in a variable
            // of its own: it names a configuration, and the configuration names the
            // token. A variable invented to look like the Azure and AWS ones would
            // never match anything, so this pins the mechanism that actually exists.
            using var env = new TempEnvironment("GOOGLE_APPLICATION_CREDENTIALS", config);
            var resolver = new WorkloadIdentityCredentialResolver();
            string? value = await resolver.ResolveAsync(string.Empty, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(value, Is.EqualTo("google-projected-token"));
        }

        [Test]
        public async Task WorkloadIdentityIgnoresAMalformedGoogleExternalAccountConfig()
        {
            using var mount = new TempMount();
            string config = mount.Write("external-account.json", "{ not json at all");

            using var env = new TempEnvironment("GOOGLE_APPLICATION_CREDENTIALS", config);
            var resolver = new WorkloadIdentityCredentialResolver();
            string? value = await resolver.ResolveAsync(string.Empty, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(value, Is.Null);
        }

        [Test]
        public async Task WorkloadIdentityReadsTheTokenTheAwsVariableNames()
        {
            using var mount = new TempMount();
            string token = mount.Write("aws-token", "aws-projected-token");

            // AWS_WEB_IDENTITY_TOKEN_FILE is the AWS SDK's own variable, which EKS
            // populates for a service account bound to an IAM role.
            using var env = new TempEnvironment("AWS_WEB_IDENTITY_TOKEN_FILE", token);
            var resolver = new WorkloadIdentityCredentialResolver();
            string? value = await resolver.ResolveAsync(string.Empty, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(value, Is.EqualTo("aws-projected-token"));
        }

        [Test]
        public void FileResolverRefusesANullDirectory()
        {
            Assert.That(
                () => new FileCredentialResolver(null!),
                Throws.ArgumentNullException);
        }

        private sealed class TempEnvironment : IDisposable
        {
            public TempEnvironment(string name, string? value)
            {
                m_name = name;
                m_previous = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }

            public void Dispose()
            {
                Environment.SetEnvironmentVariable(m_name, m_previous);
            }

            private readonly string m_name;
            private readonly string? m_previous;
        }

        private sealed class TempMount : IDisposable
        {
            public TempMount()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "opcua-ai-cred-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public string Write(string name, string content)
            {
                string full = System.IO.Path.Combine(Path, name);
                File.WriteAllText(full, content);
                return full;
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Path, recursive: true);
                }
                catch (IOException)
                {
                    // A leftover temp directory is not worth failing a test over.
                }
            }
        }
    }
}
