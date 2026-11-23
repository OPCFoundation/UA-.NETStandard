// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Creates the application certificates of the WoT live tests once per test
    /// process and copies them into every per-test certificate store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A server fixture creates an RSA certificate and up to four ECC
    /// certificates when its store is empty, and a client fixture creates its
    /// own set. Creating them for every server and every client was the largest
    /// fixed cost of a live test. The certificates only depend on the
    /// application name, so a store seeded from one template behaves exactly
    /// like a freshly created store. Every caller still gets its own copy, so
    /// trust-list changes, rejected certificates and deletions stay private to
    /// the test that made them.
    /// </para>
    /// <para>
    /// Stores seeded with the same <c>identity</c> share key pairs. A test that
    /// needs servers which authenticate as different authorities seeds each of
    /// them with a different identity.
    /// </para>
    /// <para>
    /// Seed a store before the first <c>ServerFixture.StartAsync</c> or
    /// <c>ClientFixture.LoadClientConfigurationAsync</c> that uses it. Seeding a
    /// store that already holds certificates does nothing.
    /// </para>
    /// </remarks>
    internal static class WotTestPki
    {
        /// <summary>
        /// The client application name a <see cref="ClientFixture"/> uses by default.
        /// </summary>
        public const string DefaultClientName = "TestClient";

        /// <summary>
        /// Copies the reference server and client certificates of identity 0
        /// into the store at <paramref name="pkiRoot"/>.
        /// </summary>
        /// <param name="pkiRoot">The store root a fixture is about to use.</param>
        /// <param name="clientNames">
        /// The client application names the store must hold certificates for,
        /// in addition to <see cref="DefaultClientName"/>.
        /// </param>
        public static Task SeedAsync(string pkiRoot, params string[] clientNames)
        {
            return SeedAsync(pkiRoot, 0, clientNames);
        }

        /// <summary>
        /// Copies the reference server and client certificates of
        /// <paramref name="identity"/> into the store at <paramref name="pkiRoot"/>.
        /// </summary>
        /// <param name="pkiRoot">The store root a fixture is about to use.</param>
        /// <param name="identity">
        /// Selects the key pairs; stores with different identities hold different keys.
        /// </param>
        /// <param name="clientNames">
        /// The client application names the store must hold certificates for,
        /// in addition to <see cref="DefaultClientName"/>.
        /// </param>
        public static Task SeedAsync(string pkiRoot, int identity, params string[] clientNames)
        {
            return SeedAsync<ReferenceServer>(
                pkiRoot,
                telemetry => new ReferenceServer(telemetry),
                identity,
                clientNames);
        }

        /// <summary>
        /// Copies the certificates of a server of type <typeparamref name="T"/>
        /// and of the named clients into the store at <paramref name="pkiRoot"/>.
        /// </summary>
        /// <typeparam name="T">The server type, which names the server certificate.</typeparam>
        /// <param name="pkiRoot">The store root a fixture is about to use.</param>
        /// <param name="factory">Creates the server; it is never started.</param>
        /// <param name="identity">
        /// Selects the key pairs; stores with different identities hold different keys.
        /// </param>
        /// <param name="clientNames">
        /// The client application names the store must hold certificates for,
        /// in addition to <see cref="DefaultClientName"/>.
        /// </param>
        public static async Task SeedAsync<T>(
            string pkiRoot, Func<ITelemetryContext, T> factory, int identity, params string[] clientNames)
            where T : ServerBase
        {
            if (string.IsNullOrEmpty(pkiRoot))
            {
                throw new ArgumentException("A certificate store root is required.", nameof(pkiRoot));
            }
            if (identity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(identity));
            }
            string own = Path.Combine(pkiRoot, "own");
            if (Directory.Exists(own) && Directory.GetFileSystemEntries(own).Length != 0)
            {
                return;
            }

            string template = Path.Combine(s_templateRoot, identity.ToString(CultureInfo.InvariantCulture));
            await s_lock.WaitAsync().ConfigureAwait(false);
            try
            {
                await EnsureServerAsync(template, typeof(T).Name, factory).ConfigureAwait(false);
                await EnsureClientAsync(template, DefaultClientName).ConfigureAwait(false);
                foreach (string clientName in clientNames)
                {
                    await EnsureClientAsync(template, clientName).ConfigureAwait(false);
                }
                CopyDirectory(template, pkiRoot);
            }
            finally
            {
                s_lock.Release();
            }
        }

        /// <summary>
        /// Deletes the template stores when the test process finishes.
        /// </summary>
        internal static void DeleteTemplate()
        {
            if (Directory.Exists(s_templateRoot))
            {
                try
                {
                    Directory.Delete(s_templateRoot, recursive: true);
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
        }

        private static async Task EnsureServerAsync<T>(
            string template, string name, Func<ITelemetryContext, T> factory)
            where T : ServerBase
        {
            string key = template + "|" + name;
            if (!s_created.Add(key))
            {
                return;
            }
            var fixture = new ServerFixture<T>(factory)
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            try
            {
                await fixture.LoadConfigurationAsync(template).ConfigureAwait(false);
                bool created = await fixture.Application
                    .CheckApplicationInstanceCertificatesAsync(true, CertificateFactory.DefaultLifeTime)
                    .ConfigureAwait(false);
                if (!created)
                {
                    throw new InvalidOperationException($"No application certificate for the server '{name}'.");
                }
            }
            catch
            {
                s_created.Remove(key);
                throw;
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private static async Task EnsureClientAsync(string template, string name)
        {
            string key = template + "|client|" + name;
            if (!s_created.Add(key))
            {
                return;
            }
            var fixture = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            try
            {
                await fixture.LoadClientConfigurationAsync(template, name).ConfigureAwait(false);
            }
            catch
            {
                s_created.Remove(key);
                throw;
            }
            finally
            {
                await fixture.DisposeAsync().ConfigureAwait(false);
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

        private static readonly string s_templateRoot = Path.Combine(
            Path.GetTempPath(), "wot-pki", Guid.NewGuid().ToString("N").Substring(0, 12));

        private static readonly SemaphoreSlim s_lock = new(1, 1);
        private static readonly HashSet<string> s_created = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Removes the shared certificate templates after the last WoT test ran.
    /// </summary>
    [SetUpFixture]
    public sealed class WotTestAssemblySetup
    {
        /// <summary>
        /// Deletes the certificate templates.
        /// </summary>
        [OneTimeTearDown]
        public void DeleteCertificateTemplate()
        {
            WotTestPki.DeleteTemplate();
        }
    }
}
