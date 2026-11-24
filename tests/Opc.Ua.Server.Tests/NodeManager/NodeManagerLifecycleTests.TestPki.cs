// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Certificate stores of the lifecycle tests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A server fixture creates an RSA certificate and up to four ECC certificates when
    /// its store is empty, and a client fixture creates its own set. The lifecycle tests
    /// run in parallel, so they cannot share one store that the first server fills.
    /// Instead every test copies a template that is created once per test process and
    /// holds the certificates of every server type the tests start (the subject only
    /// depends on the type name) and of the default client. A seeded store behaves like
    /// a freshly created one; each test still owns its copy, so trust-list changes,
    /// rejected certificates and deletions stay private.
    /// </para>
    /// <para>
    /// Servers of one type therefore share key pairs within a test process. No lifecycle
    /// test compares the identity of two servers.
    /// </para>
    /// </remarks>
    public sealed partial class NodeManagerLifecycleTests
    {
        private static readonly string s_pkiTemplateRoot = Path.Combine(
            Path.GetTempPath(), "nl-pki-" + Guid.NewGuid().ToString("N"));

        private static readonly SemaphoreSlim s_pkiTemplateLock = new(1, 1);
        private static bool s_pkiTemplateCreated;

        /// <summary>
        /// Creates a new store root for the current test, seeded from the template.
        /// </summary>
        private static async Task<string> CreateSeededPkiRootAsync()
        {
            string pkiRoot = Path.Combine(Path.GetTempPath(), "nl-" + Guid.NewGuid().ToString("N"));
            await SeedPkiAsync(pkiRoot).ConfigureAwait(false);
            return pkiRoot;
        }

        /// <summary>
        /// Copies the template certificates into <paramref name="pkiRoot"/> and returns it.
        /// Files the store already holds are kept.
        /// </summary>
        /// <param name="pkiRoot">The store root a server or client fixture is about to use.</param>
        private static async Task<string> SeedPkiAsync(string pkiRoot)
        {
            await EnsurePkiTemplateAsync().ConfigureAwait(false);
            CopyDirectory(s_pkiTemplateRoot, pkiRoot);
            return pkiRoot;
        }

        /// <summary>
        /// Deletes a test's store root, ignoring files a stopped server still holds open.
        /// </summary>
        private static void DeletePkiRoot(string pkiRoot)
        {
            if (string.IsNullOrEmpty(pkiRoot) || !Directory.Exists(pkiRoot))
            {
                return;
            }
            try
            {
                Directory.Delete(pkiRoot, recursive: true);
            }
            catch (IOException)
            {
                // A temporary folder left behind is harmless.
            }
            catch (UnauthorizedAccessException)
            {
                // A temporary folder left behind is harmless.
            }
        }

        /// <summary>
        /// Deletes the template when the test process finishes.
        /// </summary>
        internal static void DeletePkiTemplate()
        {
            DeletePkiRoot(s_pkiTemplateRoot);
        }

        private static async Task EnsurePkiTemplateAsync()
        {
            if (Volatile.Read(ref s_pkiTemplateCreated))
            {
                return;
            }
            await s_pkiTemplateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (s_pkiTemplateCreated)
                {
                    return;
                }
                // Every server type a lifecycle test starts, so that a test that hosts its
                // own server on its seeded store never creates certificates either.
                await CreateServerCertificatesAsync<LifecycleTestServer>().ConfigureAwait(false);
                await CreateServerCertificatesAsync<StandardServer>().ConfigureAwait(false);
                await CreateServerCertificatesAsync<ShutdownStageTestServer>().ConfigureAwait(false);
                await CreateServerCertificatesAsync<RetainedWorkerTestServer>().ConfigureAwait(false);
                var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
                try
                {
                    await client.LoadClientConfigurationAsync(s_pkiTemplateRoot).ConfigureAwait(false);
                }
                finally
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                }
                Volatile.Write(ref s_pkiTemplateCreated, true);
            }
            finally
            {
                s_pkiTemplateLock.Release();
            }
        }

        private static async Task CreateServerCertificatesAsync<T>()
            where T : ServerBase
        {
            // The factory is never called: only the configuration is loaded.
            var fixture = new ServerFixture<T>(_ => throw new NotSupportedException())
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            try
            {
                await fixture.LoadConfigurationAsync(s_pkiTemplateRoot).ConfigureAwait(false);
                bool created = await fixture.Application
                    .CheckApplicationInstanceCertificatesAsync(true, CertificateFactory.DefaultLifeTime)
                    .ConfigureAwait(false);
                if (!created)
                {
                    throw new InvalidOperationException(
                        $"No application certificate for the server '{typeof(T).Name}'.");
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                string target = Path.Combine(destination, Path.GetFileName(file));
                if (!File.Exists(target))
                {
                    File.Copy(file, target);
                }
            }
            foreach (string directory in Directory.GetDirectories(source))
            {
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
            }
        }

        /// <summary>
        /// Marks a test that hosts its own server, or none, so the fixture does not start
        /// the shared per-test server and session for it.
        /// </summary>
        [AttributeUsage(AttributeTargets.Method)]
        private sealed class NoFixtureServerAttribute : Attribute
        {
        }
    }

    /// <summary>
    /// Removes the lifecycle certificate template after the last NodeManager test ran.
    /// </summary>
    [SetUpFixture]
    public sealed class NodeManagerLifecycleTestPkiCleanup
    {
        /// <summary>
        /// Deletes the certificate template.
        /// </summary>
        [OneTimeTearDown]
        public void DeleteCertificateTemplate()
        {
            NodeManagerLifecycleTests.DeletePkiTemplate();
        }
    }
}
