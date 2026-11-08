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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
#if NETCOREAPP2_1_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace Opc.Ua.Configuration.Tests
{
    /// <summary>
    /// Tests for the ApplicationInstance configuration, certificate provisioning,
    /// and application-configuration loading.
    /// </summary>
    [TestFixture]
    [Category("ApplicationInstance")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ApplicationInstanceTests
    {
        public const string ApplicationName = "UA Configuration Test";
        public const string ApplicationUri = "urn:localhost:opcfoundation.org:ConfigurationTest";
        public const string ProductUri = "http://opcfoundation.org/UA/ConfigurationTest";

        public const string SubjectName
            = "CN=UA Configuration Test, O=OPC Foundation, C=US, S=Arizona";

        public const string EndpointUrl = "opc.tcp://localhost:51000";

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            // pki directory root for test runs.
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName() + Path.DirectorySeparatorChar;
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            try
            {
                // pki directory root for test runs.
                Directory.Delete(m_pkiRoot, true);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Load a file configuration.
        /// </summary>
        [Test]
        public async Task TestFileConfigAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                Assert.That(applicationInstance, Is.Not.Null);
                string configPath = Utils.GetAbsoluteFilePath(
                    "Opc.Ua.Configuration.Tests.Config.xml",
                    checkCurrentDirectory: true,
                    createAlways: false);
                Assert.That(configPath, Is.Not.Null);
                ApplicationConfiguration applicationConfiguration = await applicationInstance
                    .LoadApplicationConfigurationAsync(configPath, true)
                    .ConfigureAwait(false);
                Assert.That(applicationConfiguration, Is.Not.Null);
                bool certOK = await applicationInstance
                    .CheckApplicationInstanceCertificatesAsync(true)
                    .ConfigureAwait(false);
                Assert.That(certOK, Is.True);
            }
        }

        /// <summary>
        /// Verifies that loaded and directly configured certificate managers remain owned after configuration replacement.
        /// </summary>
        [Test]
        public async Task LoadedAndConfiguredCertificateManagersRemainOwnedAfterConfigurationOverrideAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            var replacementManager = new CertificateManager(telemetry);
            string? configPath = Utils.GetAbsoluteFilePath(
                "Opc.Ua.Configuration.Tests.Config.xml",
                checkCurrentDirectory: true,
                createAlways: false);
            Assert.That(configPath, Is.Not.Null);
            CertificateManager loadedManager;
            SecurityConfiguration securityConfiguration;

            await using (applicationInstance.ConfigureAwait(false))
            {
                ApplicationConfiguration configuration = await applicationInstance
                    .LoadApplicationConfigurationAsync(configPath!, true)
                    .ConfigureAwait(false);
                loadedManager = configuration.CertificateManager as CertificateManager ??
                    throw new InvalidOperationException("Configuration loading did not create a certificate manager.");
                securityConfiguration = configuration.SecurityConfiguration;
                Assert.That(applicationInstance.CertificateManager, Is.SameAs(loadedManager));

                configuration.CertificateManager = replacementManager;
                Assert.That(
                    await applicationInstance.CheckApplicationInstanceCertificatesAsync(true)
                        .ConfigureAwait(false),
                    Is.True);
                Assert.That(configuration.CertificateManager, Is.SameAs(replacementManager));
                configuration.CertificateManager = Mock.Of<ICertificateManager>();
            }

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => loadedManager.LoadApplicationCertificatesAsync(securityConfiguration)).ConfigureAwait(false);
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => replacementManager.LoadApplicationCertificatesAsync(securityConfiguration)).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a validated certificate manager remains owned after configuration replacement.
        /// </summary>
        [Test]
        public async Task CreatedCertificateManagerRemainsOwnedAfterConfigurationOverrideAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            CertificateManager createdManager;
            SecurityConfiguration securityConfiguration;

            await using (applicationInstance.ConfigureAwait(false))
            {
                ArrayOf<CertificateIdentifier> applicationCertificates =
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        SubjectName,
                        CertificateStoreType.Directory,
                        m_pkiRoot);
                ApplicationConfiguration configuration = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsClient()
                    .AddSecurityConfiguration(applicationCertificates, m_pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false);
                createdManager = configuration.CertificateManager as CertificateManager ??
                    throw new InvalidOperationException("Configuration validation did not create a certificate manager.");
                securityConfiguration = configuration.SecurityConfiguration;
                Assert.That(applicationInstance.CertificateManager, Is.SameAs(createdManager));

                configuration.CertificateManager = Mock.Of<ICertificateManager>();
            }

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => createdManager.LoadApplicationCertificatesAsync(securityConfiguration)).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a manager created by validation after configuration assignment is owned by the instance.
        /// </summary>
        [Test]
        public async Task CertificateManagerCreatedAfterConfigurationAssignmentRemainsOwnedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            CertificateManager createdManager;
            SecurityConfiguration securityConfiguration;

            await using (applicationInstance.ConfigureAwait(false))
            {
                ArrayOf<CertificateIdentifier> applicationCertificates =
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        SubjectName,
                        CertificateStoreType.Directory,
                        m_pkiRoot);
                applicationInstance.Build(ApplicationUri, ProductUri)
                    .AsClient()
                    .AddSecurityConfiguration(applicationCertificates, m_pkiRoot);

                ApplicationConfiguration configuration = applicationInstance.ApplicationConfiguration!;
                Assert.That(configuration.CertificateManager, Is.Null);
                await configuration.ValidateAsync(applicationInstance.ApplicationType).ConfigureAwait(false);

                createdManager = configuration.CertificateManager as CertificateManager ??
                    throw new InvalidOperationException("Configuration validation did not create a certificate manager.");
                securityConfiguration = configuration.SecurityConfiguration;
                Assert.That(applicationInstance.CertificateManager, Is.Null);
                Assert.That(
                    await applicationInstance.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false),
                    Is.True);
                Assert.That(applicationInstance.CertificateManager, Is.SameAs(createdManager));
            }

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => createdManager.LoadApplicationCertificatesAsync(securityConfiguration)).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that direct validation after assigning the configuration leaves its manager owned by the instance.
        /// </summary>
        [Test]
        public async Task DirectConfigurationValidationManagerIsDisposedWithApplicationInstanceAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            CertificateManager createdManager;
            SecurityConfiguration securityConfiguration;

            await using (applicationInstance.ConfigureAwait(false))
            {
                applicationInstance.Build(ApplicationUri, ProductUri)
                    .AsClient()
                    .AddSecurityConfiguration(
                        ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                            SubjectName, CertificateStoreType.Directory, m_pkiRoot),
                        m_pkiRoot);

                ApplicationConfiguration configuration = applicationInstance.ApplicationConfiguration!;
                securityConfiguration = configuration.SecurityConfiguration;
                await configuration.ValidateAsync(applicationInstance.ApplicationType).ConfigureAwait(false);
                createdManager = configuration.CertificateManager as CertificateManager ??
                    throw new InvalidOperationException("Configuration validation did not create a certificate manager.");
            }

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => createdManager.LoadApplicationCertificatesAsync(securityConfiguration)).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies ownership of every assignment even when no application operation observes a replacement.
        /// </summary>
        [Test]
        public async Task IntermediateManagersRemainOwnedAfterConfigurationReplacementAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var firstManager = new CertificateManager(telemetry);
            var secondManager = new CertificateManager(telemetry);
            var configuration = new ApplicationConfiguration(telemetry);
            await using (var application = new ApplicationInstance(configuration, telemetry))
            {
                configuration.CertificateManager = firstManager;
                configuration.CertificateManager = secondManager;
                configuration.CertificateManager = Mock.Of<ICertificateManager>();
                application.ApplicationConfiguration = new ApplicationConfiguration(telemetry);
            }

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => firstManager.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => secondManager.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
        }

        /// <summary>
        /// Verifies that validation-created managers are retained when both the manager and configuration change.
        /// </summary>
        [Test]
        public async Task ValidationCreatedManagerRemainsOwnedWithoutCertificateCheckAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var application = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            CertificateManager createdManager;
            SecurityConfiguration securityConfiguration;
            await using (application.ConfigureAwait(false))
            {
                application.Build(ApplicationUri, ProductUri).AsClient().AddSecurityConfiguration(
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        SubjectName, CertificateStoreType.Directory, m_pkiRoot),
                    m_pkiRoot);
                ApplicationConfiguration configuration = application.ApplicationConfiguration!;
                await configuration.ValidateAsync(ApplicationType.Client);
                createdManager = configuration.CertificateManager as CertificateManager ??
                    throw new InvalidOperationException("Validation did not create a certificate manager.");
                securityConfiguration = configuration.SecurityConfiguration;
                configuration.CertificateManager = Mock.Of<ICertificateManager>();
                application.ApplicationConfiguration = new ApplicationConfiguration(telemetry);
            }

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => createdManager.LoadApplicationCertificatesAsync(securityConfiguration));
        }

        /// <summary>
        /// Verifies borrowed provenance across replacement, reassignment, template copying, and repeated disposal.
        /// </summary>
        [Test]
        public async Task BorrowedManagerRemainsBorrowedAcrossConfigurationReplacementAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var borrowedManager = new CertificateManager(telemetry);
            var ownedManager = new CertificateManager(telemetry);
            var configuration = new ApplicationConfiguration(telemetry) { CertificateManager = borrowedManager };
            var application = new ApplicationInstance(configuration, telemetry);
            application.MarkCertificateManagerBorrowed();
            var templateCopy = new ApplicationConfiguration(configuration);
            await using var copyApplication = new ApplicationInstance(templateCopy, telemetry);
            configuration.CertificateManager = ownedManager;
            configuration.CertificateManager = borrowedManager;
            application.ApplicationConfiguration = new ApplicationConfiguration(telemetry)
            {
                CertificateManager = borrowedManager
            };

            await application.DisposeAsync();
            await application.DisposeAsync();
            await copyApplication.DisposeAsync();
            using ICertificateStore store = borrowedManager.OpenCertificateStore(
                m_pkiRoot, CertificateStoreType.Directory, false);
            Assert.That(store, Is.Not.Null);
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => ownedManager.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
        }

        /// <summary>
        /// Preserves the provisioned certificate until the last application releases a shared manager identity.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        [NonParallelizable]
        public async Task SharedConfigurationKeepsCertificateIdentityUntilLastApplicationAsync(
            bool reverseDisposal,
            bool templateCopy)
        {
            long leaksBefore = Certificate.InstancesLeaked;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var first = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            ApplicationConfiguration configuration = await first.Build(ApplicationUri, ProductUri).AsClient()
                .AddSecurityConfiguration(
                    [
                        new CertificateIdentifier
                        {
                            SubjectName = SubjectName,
                            StoreType = CertificateStoreType.Directory,
                            StorePath = m_pkiRoot,
                            CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                        }
                    ],
                    m_pkiRoot)
                .CreateAsync();
            Assert.That(await first.CheckApplicationInstanceCertificatesAsync(true), Is.True);
            CertificateManager manager = first.CertificateManager ??
                throw new InvalidOperationException("Provisioning did not create a certificate manager.");
            ApplicationConfiguration shared = templateCopy
                ? new ApplicationConfiguration(configuration)
                : configuration;
            await using var second = new ApplicationInstance(shared, telemetry);
            string thumbprint;
            using (CertificateEntryCollection certificates = manager.SnapshotApplicationCertificates())
            {
                Assert.That(certificates, Has.Count.EqualTo(1));
                thumbprint = certificates[0].Certificate.Thumbprint;
            }

            ApplicationInstance released = reverseDisposal ? second : first;
            ApplicationInstance remaining = reverseDisposal ? first : second;
            await released.DisposeAsync();
            await released.DisposeAsync();
            Assert.That(remaining.CertificateManager, Is.SameAs(manager));
            await manager.LoadApplicationCertificatesAsync(
                configuration.SecurityConfiguration, configuration.ApplicationUri);
            using (CertificateEntryCollection certificates = manager.SnapshotApplicationCertificates())
            {
                Assert.That(certificates, Has.Count.EqualTo(1));
                Assert.That(certificates[0].Certificate.Thumbprint, Is.EqualTo(thumbprint));
                Assert.That(certificates[0].Certificate.HasPrivateKey, Is.True);
            }

            await remaining.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => manager.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
            using CertificateEntryCollection disposedCertificates = manager.SnapshotApplicationCertificates();
            Assert.That(disposedCertificates, Is.Empty);
            Assert.That(Certificate.InstancesLeaked, Is.EqualTo(leaksBefore));
        }

        /// <summary>
        /// Assignments after both applications attach acquire a reference for each active application.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ManagerAssignedAfterBothApplicationsAttachAsync(bool reverseDisposal)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var configuration = new ApplicationConfiguration(telemetry);
            await using var first = new ApplicationInstance(configuration, telemetry);
            await using var second = new ApplicationInstance(configuration, telemetry);
            var manager = new CertificateManager(telemetry);
            configuration.CertificateManager = manager;
            ApplicationInstance released = reverseDisposal ? second : first;
            ApplicationInstance remaining = reverseDisposal ? first : second;
            await released.DisposeAsync();
            using (ICertificateStore store = manager.OpenCertificateStore(
                m_pkiRoot, CertificateStoreType.Directory, false))
            {
                Assert.That(store, Is.Not.Null);
            }
            await remaining.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => manager.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
        }

        /// <summary>
        /// Overlapping checks share initialization without overwriting a replacement configuration's manager.
        /// </summary>
        [TestCase(false, false, false, false)]
        [TestCase(true, false, false, false)]
        [TestCase(true, true, false, false)]
        [TestCase(false, false, true, false)]
        [TestCase(true, false, true, false)]
        [TestCase(true, true, true, false)]
        [TestCase(false, false, false, true)]
        public async Task ConcurrentCertificateChecksInitializeOneSharedManagerAsync(
            bool replaceManager,
            bool borrowReplacement,
            bool replaceConfiguration,
            bool crossThreadCallback)
        {
            ITelemetryContext actualTelemetry = NUnitTelemetryContext.Create();
            var telemetry = new Mock<ITelemetryContext>();
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(value => value.CreateLogger(It.IsAny<string>()))
                .Returns(actualTelemetry.LoggerFactory.CreateLogger);
            telemetry.SetupGet(value => value.LoggerFactory).Returns(loggerFactory.Object);
            telemetry.SetupGet(value => value.ActivitySource).Returns(actualTelemetry.ActivitySource);
            telemetry.Setup(value => value.GetActivitySource(It.IsAny<System.Reflection.Assembly>()))
                .Returns((System.Reflection.Assembly assembly) => actualTelemetry.GetActivitySource(assembly));
            telemetry.Setup(value => value.CreateMeter()).Returns(actualTelemetry.CreateMeter);
            telemetry.Setup(value => value.CreateMeter(It.IsAny<System.Reflection.Assembly>()))
                .Returns((System.Reflection.Assembly assembly) => actualTelemetry.CreateMeter(assembly));
            await using var external = new CertificateManager(actualTelemetry);
            await using var first = new ApplicationInstance(telemetry.Object)
            {
                ApplicationName = ApplicationName,
                DisableCertificateAutoCreation = true
            };
            first.Build(ApplicationUri, ProductUri).AsClient().AddSecurityConfiguration(
                [
                    new CertificateIdentifier
                    {
                        SubjectName = SubjectName,
                        StoreType = CertificateStoreType.Directory,
                        StorePath = m_pkiRoot,
                        CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                    }
                ],
                m_pkiRoot);
            ApplicationConfiguration configuration = first.ApplicationConfiguration!;
            var replacementConfiguration = new ApplicationConfiguration(actualTelemetry)
            {
                ApplicationName = configuration.ApplicationName,
                ApplicationUri = configuration.ApplicationUri,
                ApplicationType = configuration.ApplicationType,
                SecurityConfiguration = configuration.SecurityConfiguration
            };
            await using var second = new ApplicationInstance(configuration, telemetry.Object)
            {
                DisableCertificateAutoCreation = true
            };
            Task<bool>? overlappingCheck = null;
            Task<ApplicationConfiguration?>? callbackRead = null;
            bool callbackReadCompleted = false;
            int factoryCalls = 0;
            loggerFactory.Setup(value => value.CreateLogger(It.IsAny<string>()))
                .Returns((string category) =>
                {
                    if (category == typeof(CertificateManager).FullName &&
                        Interlocked.Increment(ref factoryCalls) == 1)
                    {
                        if (crossThreadCallback)
                        {
                            callbackRead = Task.Run(() => first.ApplicationConfiguration);
                            callbackReadCompleted = callbackRead.Wait(TimeSpan.FromSeconds(10));
                        }
                        // The constructor callback starts a second check before shared initialization completes.
                        overlappingCheck = second.CheckApplicationInstanceCertificatesAsync(true).AsTask();
                        Assert.That(overlappingCheck.IsCompleted, Is.False);
                        if (replaceConfiguration)
                        {
                            if (replaceManager)
                            {
                                replacementConfiguration.CertificateManager = external;
                            }
                            second.ApplicationConfiguration = replacementConfiguration;
                        }
                        if (replaceManager)
                        {
                            if (!replaceConfiguration)
                            {
                                configuration.CertificateManager = external;
                            }
                            if (borrowReplacement)
                            {
                                second.MarkCertificateManagerBorrowed();
                            }
                        }
                    }
                    return actualTelemetry.LoggerFactory.CreateLogger(category);
                });

            Assert.That(configuration.CertificateManager, Is.Null);
            ServiceResultException? firstFailure = await Assert.ThrowsAsync<ServiceResultException>(
                () => first.CheckApplicationInstanceCertificatesAsync(true).AsTask());
            Assert.That(overlappingCheck, Is.Not.Null);
            ServiceResultException? secondFailure = await Assert.ThrowsAsync<ServiceResultException>(
                () => overlappingCheck!);
            Assert.That(firstFailure!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(secondFailure!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            if (crossThreadCallback)
            {
                Assert.That(callbackRead, Is.Not.Null);
                Assert.That(await callbackRead!.ConfigureAwait(false), Is.SameAs(configuration));
                Assert.That(callbackReadCompleted, Is.True,
                    "The manager factory callback must not hold the application's configuration lock.");
            }
            Assert.That(factoryCalls, Is.EqualTo(1));
            CertificateManager manager = configuration.CertificateManager as CertificateManager ??
                throw new InvalidOperationException("Concurrent checking did not configure a concrete manager.");
            Assert.That(first.CertificateManager, Is.SameAs(manager));
            CertificateManager? replacementConfigurationManager = null;
            if (replaceConfiguration)
            {
                Assert.That(second.ApplicationConfiguration, Is.SameAs(replacementConfiguration));
                Assert.That(second.CertificateManager, Is.SameAs(replaceManager ? external : null));
                Assert.That(replacementConfiguration.CertificateManager, Is.SameAs(replaceManager ? external : null));
                ServiceResultException? replacementFailure = await Assert.ThrowsAsync<ServiceResultException>(
                    () => second.CheckApplicationInstanceCertificatesAsync(true).AsTask());
                Assert.That(replacementFailure!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                replacementConfigurationManager = replacementConfiguration.CertificateManager as CertificateManager ??
                    throw new InvalidOperationException("The replacement configuration did not initialize its manager.");
                Assert.That(replacementConfigurationManager, Is.Not.SameAs(manager));
                Assert.That(second.CertificateManager, Is.SameAs(replacementConfigurationManager));
                Assert.That(factoryCalls, Is.EqualTo(replaceManager ? 1 : 2));
                Assert.That(GetTrackedCertificateManagers(replacementConfiguration),
                    Is.EquivalentTo(new[] { replacementConfigurationManager }));
            }
            else
            {
                Assert.That(second.CertificateManager, Is.SameAs(manager));
            }
            if (replaceManager && !replaceConfiguration)
            {
                Assert.That(manager, Is.SameAs(external));
            }
            // A manager created but never published must not join the leases of the attached applications.
            Assert.That(GetTrackedCertificateManagers(configuration), Is.EquivalentTo(new[] { manager }));
            await first.DisposeAsync();
            using (ICertificateStore store = manager.OpenCertificateStore(
                m_pkiRoot, CertificateStoreType.Directory, false))
            {
                Assert.That(store, Is.Not.Null);
            }
            await second.DisposeAsync();
            if (borrowReplacement && !replaceConfiguration)
            {
                using ICertificateStore store = manager.OpenCertificateStore(
                    m_pkiRoot, CertificateStoreType.Directory, false);
                Assert.That(store, Is.Not.Null);
            }
            else
            {
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    () => manager.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
            }
            if (replacementConfigurationManager != null)
            {
                if (borrowReplacement)
                {
                    using ICertificateStore store = replacementConfigurationManager.OpenCertificateStore(
                        m_pkiRoot, CertificateStoreType.Directory, false);
                    Assert.That(store, Is.Not.Null);
                }
                else
                {
                    await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                        replacementConfigurationManager.LoadApplicationCertificatesAsync(
                            replacementConfiguration.SecurityConfiguration));
                }
            }
        }

        /// <summary>
        /// Reads the managers whose lifetimes follow the configuration leases.
        /// </summary>
        private static CertificateManager[] GetTrackedCertificateManagers(ApplicationConfiguration configuration)
        {
            System.Reflection.FieldInfo field = typeof(ApplicationConfiguration).GetField(
                "m_certificateManagers",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) ??
                throw new InvalidOperationException("The configuration does not track certificate managers.");
            var managers = (HashSet<CertificateManager>)field.GetValue(configuration)!;
            return [.. managers];
        }
        /// <summary>
        /// An application attached after final release does not join initialization from the retired ownership period.
        /// </summary>
        [Test]
        public async Task ReattachDuringValidationInitializationStartsNewOwnershipPeriodAsync()
        {
            ITelemetryContext actualTelemetry = NUnitTelemetryContext.Create();
            var telemetry = new Mock<ITelemetryContext>();
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(value => value.CreateLogger(It.IsAny<string>()))
                .Returns(actualTelemetry.LoggerFactory.CreateLogger);
            telemetry.SetupGet(value => value.LoggerFactory).Returns(loggerFactory.Object);
            telemetry.SetupGet(value => value.ActivitySource).Returns(actualTelemetry.ActivitySource);
            telemetry.Setup(value => value.GetActivitySource(It.IsAny<System.Reflection.Assembly>()))
                .Returns((System.Reflection.Assembly assembly) => actualTelemetry.GetActivitySource(assembly));
            telemetry.Setup(value => value.CreateMeter()).Returns(actualTelemetry.CreateMeter);
            telemetry.Setup(value => value.CreateMeter(It.IsAny<System.Reflection.Assembly>()))
                .Returns((System.Reflection.Assembly assembly) => actualTelemetry.CreateMeter(assembly));
            await using var first = new ApplicationInstance(telemetry.Object)
            {
                ApplicationName = ApplicationName,
                DisableCertificateAutoCreation = true
            };
            first.Build(ApplicationUri, ProductUri).AsClient().AddSecurityConfiguration(
                [
                    new CertificateIdentifier
                    {
                        SubjectName = SubjectName,
                        StoreType = CertificateStoreType.Directory,
                        StorePath = m_pkiRoot,
                        CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                    }
                ],
                m_pkiRoot);
            ApplicationConfiguration configuration = first.ApplicationConfiguration!;
            ApplicationInstance? second = null;
            Task<bool>? reattachedCheck = null;
            int factoryCalls = 0;
            loggerFactory.Setup(value => value.CreateLogger(It.IsAny<string>()))
                .Returns((string category) =>
                {
                    if (category == typeof(CertificateManager).FullName &&
                        Interlocked.Increment(ref factoryCalls) == 1)
                    {
                        // Validation's factory call is paused here while ownership moves to a new period.
                        Task release = first.DisposeAsync().AsTask();
                        Assert.That(release.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                        second = new ApplicationInstance(configuration, telemetry.Object)
                        {
                            DisableCertificateAutoCreation = true
                        };
                        reattachedCheck = second.CheckApplicationInstanceCertificatesAsync(true).AsTask();
                    }
                    return actualTelemetry.LoggerFactory.CreateLogger(category);
                });
            try
            {
                Assert.That(configuration.CertificateManager, Is.Null);
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    () => configuration.ValidateAsync(ApplicationType.Client));
                Assert.That(reattachedCheck, Is.Not.Null);
                ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(
                    () => reattachedCheck!);
                Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(factoryCalls, Is.EqualTo(2));
                CertificateManager manager = configuration.CertificateManager as CertificateManager ??
                    throw new InvalidOperationException("Reattachment did not configure a concrete manager.");
                Assert.That(second!.CertificateManager, Is.SameAs(manager));
                using (ICertificateStore store = manager.OpenCertificateStore(
                    m_pkiRoot, CertificateStoreType.Directory, false))
                {
                    Assert.That(store, Is.Not.Null);
                }
                await second.DisposeAsync();
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    () => manager.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
            }
            finally
            {
                if (second != null)
                {
                    await second.DisposeAsync();
                }
            }
        }
        /// <summary>
        /// The retired period's cleanup keeps a newer in-flight initialization joinable by later callers.
        /// </summary>
        [Test]
        public async Task RetiredInitializationCleanupKeepsNewerInitializationJoinableAsync()
        {
            ITelemetryContext actualTelemetry = NUnitTelemetryContext.Create();
            var telemetry = new Mock<ITelemetryContext>();
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(value => value.CreateLogger(It.IsAny<string>()))
                .Returns(actualTelemetry.LoggerFactory.CreateLogger);
            telemetry.SetupGet(value => value.LoggerFactory).Returns(loggerFactory.Object);
            telemetry.SetupGet(value => value.ActivitySource).Returns(actualTelemetry.ActivitySource);
            telemetry.Setup(value => value.GetActivitySource(It.IsAny<System.Reflection.Assembly>()))
                .Returns((System.Reflection.Assembly assembly) => actualTelemetry.GetActivitySource(assembly));
            telemetry.Setup(value => value.CreateMeter()).Returns(actualTelemetry.CreateMeter);
            telemetry.Setup(value => value.CreateMeter(It.IsAny<System.Reflection.Assembly>()))
                .Returns((System.Reflection.Assembly assembly) => actualTelemetry.CreateMeter(assembly));
            await using var first = new ApplicationInstance(telemetry.Object)
            {
                ApplicationName = ApplicationName,
                DisableCertificateAutoCreation = true
            };
            first.Build(ApplicationUri, ProductUri).AsClient().AddSecurityConfiguration(
                [
                    new CertificateIdentifier
                    {
                        SubjectName = SubjectName,
                        StoreType = CertificateStoreType.Directory,
                        StorePath = m_pkiRoot,
                        CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                    }
                ],
                m_pkiRoot);
            ApplicationConfiguration configuration = first.ApplicationConfiguration!;
            using var newerCreationEntered = new ManualResetEventSlim();
            using var newerCreationReleased = new ManualResetEventSlim();
            ApplicationInstance? second = null;
            ApplicationInstance? third = null;
            Task<bool>? secondCheck = null;
            int factoryCalls = 0;
            loggerFactory.Setup(value => value.CreateLogger(It.IsAny<string>()))
                .Returns((string category) =>
                {
                    if (category == typeof(CertificateManager).FullName)
                    {
                        int call = Interlocked.Increment(ref factoryCalls);
                        if (call == 1)
                        {
                            // The retired period's creation stays paused until the newer creation is in flight.
                            Task release = first.DisposeAsync().AsTask();
                            Assert.That(release.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                            second = new ApplicationInstance(configuration, telemetry.Object)
                            {
                                DisableCertificateAutoCreation = true
                            };
                            ApplicationInstance reattached = second;
                            secondCheck = Task.Run(() =>
                                reattached.CheckApplicationInstanceCertificatesAsync(true).AsTask());
                            Assert.That(newerCreationEntered.Wait(TimeSpan.FromSeconds(30)), Is.True);
                        }
                        else if (call == 2)
                        {
                            newerCreationEntered.Set();
                            Assert.That(newerCreationReleased.Wait(TimeSpan.FromSeconds(30)), Is.True);
                        }
                    }
                    return actualTelemetry.LoggerFactory.CreateLogger(category);
                });
            try
            {
                Assert.That(configuration.CertificateManager, Is.Null);
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    () => Task.Run(() => configuration.ValidateAsync(ApplicationType.Client)));
                Assert.That(secondCheck, Is.Not.Null);
                Assert.That(secondCheck!.IsCompleted, Is.False);
                third = new ApplicationInstance(configuration, telemetry.Object)
                {
                    DisableCertificateAutoCreation = true
                };
                Task<bool> thirdCheck = third.CheckApplicationInstanceCertificatesAsync(true).AsTask();
                Assert.That(thirdCheck.IsCompleted, Is.False);
                Assert.That(factoryCalls, Is.EqualTo(2));
                newerCreationReleased.Set();
                ServiceResultException? secondFailure = await Assert.ThrowsAsync<ServiceResultException>(
                    () => secondCheck!);
                ServiceResultException? thirdFailure = await Assert.ThrowsAsync<ServiceResultException>(
                    () => thirdCheck);
                Assert.That(secondFailure!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(thirdFailure!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(factoryCalls, Is.EqualTo(2));
                CertificateManager manager = configuration.CertificateManager as CertificateManager ??
                    throw new InvalidOperationException("Reattachment did not configure a concrete manager.");
                Assert.That(second!.CertificateManager, Is.SameAs(manager));
                Assert.That(third.CertificateManager, Is.SameAs(manager));
                await second.DisposeAsync();
                using (ICertificateStore store = manager.OpenCertificateStore(
                    m_pkiRoot, CertificateStoreType.Directory, false))
                {
                    Assert.That(store, Is.Not.Null);
                }
                await third.DisposeAsync();
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    () => manager.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
            }
            finally
            {
                newerCreationReleased.Set();
                if (secondCheck != null)
                {
                    try
                    {
                        await secondCheck;
                    }
                    catch (ServiceResultException)
                    {
                        // Expected configuration failure was asserted above when the test reached that point.
                    }
                }
                if (second != null)
                {
                    await second.DisposeAsync();
                }
                if (third != null)
                {
                    await third.DisposeAsync();
                }
            }
        }
        /// <summary>
        /// Unattached validation keeps a manager assigned during factory work and discards its unpublished manager.
        /// </summary>
        [Test]
        public async Task UnattachedValidationKeepsManagerAssignedDuringCreationAsync()
        {
            ITelemetryContext actualTelemetry = NUnitTelemetryContext.Create();
            var telemetry = new Mock<ITelemetryContext>();
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(value => value.CreateLogger(It.IsAny<string>()))
                .Returns(actualTelemetry.LoggerFactory.CreateLogger);
            telemetry.SetupGet(value => value.LoggerFactory).Returns(loggerFactory.Object);
            telemetry.SetupGet(value => value.ActivitySource).Returns(actualTelemetry.ActivitySource);
            telemetry.Setup(value => value.GetActivitySource(It.IsAny<System.Reflection.Assembly>()))
                .Returns((System.Reflection.Assembly assembly) => actualTelemetry.GetActivitySource(assembly));
            telemetry.Setup(value => value.CreateMeter()).Returns(actualTelemetry.CreateMeter);
            telemetry.Setup(value => value.CreateMeter(It.IsAny<System.Reflection.Assembly>()))
                .Returns((System.Reflection.Assembly assembly) => actualTelemetry.CreateMeter(assembly));
            await using var application = new ApplicationInstance(telemetry.Object)
            {
                ApplicationName = ApplicationName,
                DisableCertificateAutoCreation = true
            };
            application.Build(ApplicationUri, ProductUri).AsClient().AddSecurityConfiguration(
                [
                    new CertificateIdentifier
                    {
                        SubjectName = SubjectName,
                        StoreType = CertificateStoreType.Directory,
                        StorePath = m_pkiRoot,
                        CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                    }
                ],
                m_pkiRoot);
            var unattached = new ApplicationConfiguration(application.ApplicationConfiguration!);
            var custom = new Mock<ICertificateManager>(MockBehavior.Strict);
            int factoryCalls = 0;
            loggerFactory.Setup(value => value.CreateLogger(It.IsAny<string>()))
                .Returns((string category) =>
                {
                    if (category == typeof(CertificateManager).FullName &&
                        Interlocked.Increment(ref factoryCalls) == 1)
                    {
                        // The factory is paused after the initialization slot is claimed and before publication.
                        unattached.CertificateManager = custom.Object;
                    }
                    return actualTelemetry.LoggerFactory.CreateLogger(category);
                });

            Assert.That(unattached.CertificateManager, Is.Null);
            await unattached.ValidateAsync(ApplicationType.Client);
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(unattached.CertificateManager, Is.SameAs(custom.Object));
            Assert.That(application.ApplicationConfiguration!.CertificateManager, Is.Null);
            await unattached.ValidateAsync(ApplicationType.Client);
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(unattached.CertificateManager, Is.SameAs(custom.Object));
        }

        /// <summary>
        /// Base validation creates a manager owned by both applications when they attach before validation.
        /// </summary>
        [Test]
        public async Task SharedConfigurationValidationAfterBothApplicationsAttachAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var first = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            first.Build(ApplicationUri, ProductUri).AsClient().AddSecurityConfiguration(
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName, CertificateStoreType.Directory, m_pkiRoot),
                m_pkiRoot);
            ApplicationConfiguration configuration = first.ApplicationConfiguration!;
            await using var second = new ApplicationInstance(configuration, telemetry);
            Assert.That(configuration.CertificateManager, Is.Null);
            await configuration.ValidateAsync(ApplicationType.Client);
            CertificateManager manager = configuration.CertificateManager as CertificateManager ??
                throw new InvalidOperationException("Validation did not create a certificate manager.");
            await first.DisposeAsync();
            using (ICertificateStore store = manager.OpenCertificateStore(
                m_pkiRoot, CertificateStoreType.Directory, false))
            {
                Assert.That(store, Is.Not.Null);
            }
            await second.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => manager.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
        }

        /// <summary>
        /// Base validation cannot create an unowned manager after the last application releases an empty configuration.
        /// </summary>
        [Test]
        public async Task ValidationAfterFinalReleaseDoesNotCreateManagerAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var application = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            application.Build(ApplicationUri, ProductUri).AsClient().AddSecurityConfiguration(
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName, CertificateStoreType.Directory, m_pkiRoot),
                m_pkiRoot);
            ApplicationConfiguration configuration = application.ApplicationConfiguration!;
            await application.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => configuration.ValidateAsync(ApplicationType.Client));
            Assert.That(configuration.CertificateManager, Is.Null);
        }

        /// <summary>
        /// Independent configurations and template copies share manager identity and retain their own replacements.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task SharedManagerAcrossConfigurationsRetainsReplacementsAsync(
            bool reverseDisposal,
            bool templateCopy)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var manager = new CertificateManager(telemetry);
            var firstReplacement = new CertificateManager(telemetry);
            var secondReplacement = new CertificateManager(telemetry);
            var firstConfiguration = new ApplicationConfiguration(telemetry) { CertificateManager = manager };
            var secondConfiguration = templateCopy
                ? new ApplicationConfiguration(firstConfiguration)
                : new ApplicationConfiguration(telemetry) { CertificateManager = manager };
            await using var first = new ApplicationInstance(firstConfiguration, telemetry);
            await using var second = new ApplicationInstance(secondConfiguration, telemetry);
            firstConfiguration.CertificateManager = firstReplacement;
            firstConfiguration.CertificateManager = Mock.Of<ICertificateManager>();
            secondConfiguration.CertificateManager = secondReplacement;
            first.ApplicationConfiguration = new ApplicationConfiguration(telemetry);
            first.ApplicationConfiguration = firstConfiguration;

            ApplicationInstance released = reverseDisposal ? second : first;
            ApplicationInstance remaining = reverseDisposal ? first : second;
            CertificateManager releasedReplacement = reverseDisposal ? secondReplacement : firstReplacement;
            CertificateManager remainingReplacement = reverseDisposal ? firstReplacement : secondReplacement;
            await released.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => releasedReplacement.LoadApplicationCertificatesAsync(firstConfiguration.SecurityConfiguration));
            using (ICertificateStore store = manager.OpenCertificateStore(
                m_pkiRoot, CertificateStoreType.Directory, false))
            {
                Assert.That(store, Is.Not.Null);
            }
            using (ICertificateStore store = remainingReplacement.OpenCertificateStore(
                m_pkiRoot, CertificateStoreType.Directory, false))
            {
                Assert.That(store, Is.Not.Null);
            }

            await remaining.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => manager.LoadApplicationCertificatesAsync(firstConfiguration.SecurityConfiguration));
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => remainingReplacement.LoadApplicationCertificatesAsync(firstConfiguration.SecurityConfiguration));
        }

        /// <summary>
        /// Replacement after one application's disposal belongs to the remaining application.
        /// </summary>
        [Test]
        public async Task SharedConfigurationReplacementAfterFirstDisposalRemainsOwnedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var original = new CertificateManager(telemetry);
            var replacement = new CertificateManager(telemetry);
            var configuration = new ApplicationConfiguration(telemetry) { CertificateManager = original };
            await using var first = new ApplicationInstance(configuration, telemetry);
            await using var second = new ApplicationInstance(configuration, telemetry);
            await first.DisposeAsync();
            configuration.CertificateManager = replacement;
            configuration.CertificateManager = Mock.Of<ICertificateManager>();
            await first.DisposeAsync();
            using (ICertificateStore store = replacement.OpenCertificateStore(
                m_pkiRoot, CertificateStoreType.Directory, false))
            {
                Assert.That(store, Is.Not.Null);
            }
            await second.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => original.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => replacement.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
        }

        /// <summary>
        /// Container ownership applies to manager identity, including applications attached before borrowing is marked.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(false, null)]
        [TestCase(true, null)]
        public async Task BorrowedManagerSharedAcrossApplicationsRemainsContainerOwnedAsync(
            bool reverseDisposal,
            bool? templateCopy)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var manager = new CertificateManager(telemetry);
            var configuration = new ApplicationConfiguration(telemetry) { CertificateManager = manager };
            ApplicationConfiguration shared = templateCopy switch
            {
                true => new ApplicationConfiguration(configuration),
                false => configuration,
                null => new ApplicationConfiguration(telemetry) { CertificateManager = manager }
            };
            await using var first = new ApplicationInstance(configuration, telemetry);
            await using var second = new ApplicationInstance(shared, telemetry);
            first.MarkCertificateManagerBorrowed();
            await (reverseDisposal ? second : first).DisposeAsync();
            using (ICertificateStore store = manager.OpenCertificateStore(
                m_pkiRoot, CertificateStoreType.Directory, false))
            {
                Assert.That(store, Is.Not.Null);
            }
            await (reverseDisposal ? first : second).DisposeAsync();
            using (ICertificateStore store = manager.OpenCertificateStore(
                m_pkiRoot, CertificateStoreType.Directory, false))
            {
                Assert.That(store, Is.Not.Null);
            }
        }

        /// <summary>
        /// Disposal callers await server shutdown; another application may attach before the final release.
        /// </summary>
        [Test]
        public async Task ConcurrentApplicationDisposalRetainsSharedManagerUntilServerStopsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var manager = new CertificateManager(telemetry);
            var configuration = new ApplicationConfiguration(telemetry) { CertificateManager = manager };
            await using var first = new ApplicationInstance(configuration, telemetry);
            await using var second = new ApplicationInstance(configuration, telemetry);
            var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = new Mock<IServerBase>();
            server.Setup(value => value.StopAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask(stop.Task));
            await first.StartAsync(server.Object);
            Task disposal = first.DisposeAsync().AsTask();
            Task repeatedDisposal = first.DisposeAsync().AsTask();
            Assert.That(disposal.IsCompleted, Is.False);
            Assert.That(repeatedDisposal.IsCompleted, Is.False);
            try
            {
                Assert.Throws<ObjectDisposedException>(
                    () => first.ApplicationConfiguration = new ApplicationConfiguration(telemetry));
                await second.DisposeAsync();
                await using var third = new ApplicationInstance(configuration, telemetry);
                stop.SetResult(true);
                await Task.WhenAll(disposal, repeatedDisposal);
                server.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
                server.Verify(value => value.Dispose(), Times.Once);
                using (ICertificateStore store = manager.OpenCertificateStore(
                    m_pkiRoot, CertificateStoreType.Directory, false))
                {
                    Assert.That(store, Is.Not.Null);
                }
                await third.DisposeAsync();
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    () => manager.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
            }
            finally
            {
                stop.TrySetResult(true);
                await Task.WhenAll(disposal, repeatedDisposal);
            }
        }

        /// <summary>
        /// Parallel final releases across configurations retire one shared manager without losing disposal work.
        /// </summary>
        [Test]
        public async Task ConcurrentFinalReleasesDisposeSharedManagerAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var manager = new CertificateManager(telemetry);
            var configuration = new ApplicationConfiguration(telemetry) { CertificateManager = manager };
            await using var first = new ApplicationInstance(configuration, telemetry);
            await using var second = new ApplicationInstance(new ApplicationConfiguration(configuration), telemetry);
            var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstServer = new Mock<IServerBase>();
            var secondServer = new Mock<IServerBase>();
            firstServer.Setup(value => value.StopAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask(stop.Task));
            secondServer.Setup(value => value.StopAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask(stop.Task));
            await first.StartAsync(firstServer.Object);
            await second.StartAsync(secondServer.Object);
            Task firstDisposal = first.DisposeAsync().AsTask();
            Task secondDisposal = second.DisposeAsync().AsTask();
            try
            {
                Assert.That(firstDisposal.IsCompleted, Is.False);
                Assert.That(secondDisposal.IsCompleted, Is.False);
                using (ICertificateStore store = manager.OpenCertificateStore(
                    m_pkiRoot, CertificateStoreType.Directory, false))
                {
                    Assert.That(store, Is.Not.Null);
                }
            }
            finally
            {
                stop.TrySetResult(true);
                await Task.WhenAll(firstDisposal, secondDisposal);
            }
            firstServer.Verify(value => value.Dispose(), Times.Once);
            secondServer.Verify(value => value.Dispose(), Times.Once);
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => manager.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
        }

        /// <summary>
        /// An unattached template does not retain ownership or allow adoption after the final manager release.
        /// </summary>
        [Test]
        public async Task LateTemplateAdoptionRejectsRetiredManagerAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var manager = new CertificateManager(telemetry);
            var configuration = new ApplicationConfiguration(telemetry) { CertificateManager = manager };
            var template = new ApplicationConfiguration(configuration);
            await using var application = new ApplicationInstance(configuration, telemetry);
            await application.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() => new ApplicationInstance(template, telemetry));
            Assert.Throws<ObjectDisposedException>(() => new ApplicationInstance(configuration, telemetry));
            Assert.Throws<ObjectDisposedException>(
                () => new ApplicationConfiguration(telemetry).CertificateManager = manager);
        }

        /// <summary>
        /// A released configuration accepts a fresh manager without allowing resurrection of its retired manager.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FreshManagerAssignmentAfterFinalReleaseSupportsConfigurationReuseAsync(
            bool initiallyEmpty)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var configuration = new ApplicationConfiguration(telemetry);
            if (!initiallyEmpty)
            {
                configuration.CertificateManager = new CertificateManager(telemetry);
            }
            ICertificateManager? original = configuration.CertificateManager;
            await using var application = new ApplicationInstance(configuration, telemetry);
            await application.DisposeAsync();
            await using var fresh = new CertificateManager(telemetry);
            configuration.CertificateManager = fresh;
            Assert.That(configuration.CertificateManager, Is.SameAs(fresh));
            if (original is CertificateManager retired)
            {
                Assert.Throws<ObjectDisposedException>(() => configuration.CertificateManager = retired);
                Assert.That(configuration.CertificateManager, Is.SameAs(fresh));
            }
            using (ICertificateStore store = fresh.OpenCertificateStore(
                m_pkiRoot, CertificateStoreType.Directory, false))
            {
                Assert.That(store, Is.Not.Null);
            }
            await using var owner = new ApplicationInstance(configuration, telemetry);
            await application.DisposeAsync();
            using (ICertificateStore store = fresh.OpenCertificateStore(
                m_pkiRoot, CertificateStoreType.Directory, false))
            {
                Assert.That(store, Is.Not.Null);
            }
            await owner.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => fresh.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
        }

        /// <summary>
        /// Reattachment before or after final release preserves the new manager and retires old ownership safely.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConfigurationReplacementVersusFinalReleaseSupportsReattachmentAsync(
            bool reattachBeforeRelease)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var original = new CertificateManager(telemetry);
            await using var fresh = new CertificateManager(telemetry);
            var configuration = new ApplicationConfiguration(telemetry) { CertificateManager = original };
            await using var first = new ApplicationInstance(configuration, telemetry);
            var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = new Mock<IServerBase>();
            server.Setup(value => value.StopAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask(stop.Task));
            await first.StartAsync(server.Object);
            Task release = first.DisposeAsync().AsTask();
            ApplicationInstance? second = null;
            try
            {
                Assert.That(release.IsCompleted, Is.False);
                if (reattachBeforeRelease)
                {
                    configuration.CertificateManager = fresh;
                    second = new ApplicationInstance(configuration, telemetry);
                }
                stop.SetResult(true);
                await release;
                if (!reattachBeforeRelease)
                {
                    configuration.CertificateManager = fresh;
                    second = new ApplicationInstance(configuration, telemetry);
                    await Assert.ThrowsAsync<ObjectDisposedException>(
                        () => original.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
                }
                else
                {
                    using ICertificateStore store = original.OpenCertificateStore(
                        m_pkiRoot, CertificateStoreType.Directory, false);
                    Assert.That(store, Is.Not.Null);
                }
                Assert.That(second!.CertificateManager, Is.SameAs(fresh));
                using (ICertificateStore store = fresh.OpenCertificateStore(
                    m_pkiRoot, CertificateStoreType.Directory, false))
                {
                    Assert.That(store, Is.Not.Null);
                }
                await second.DisposeAsync();
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    () => original.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    () => fresh.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
            }
            finally
            {
                stop.TrySetResult(true);
                await release;
                if (second != null)
                {
                    await second.DisposeAsync();
                }
            }
        }

        /// <summary>
        /// Container-owned managers remain reusable through the same configuration after each application releases it.
        /// </summary>
        [Test]
        public async Task BorrowedManagerSupportsSequentialConfigurationReuseAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var manager = new CertificateManager(telemetry);
            var configuration = new ApplicationConfiguration(telemetry) { CertificateManager = manager };
            await using var first = new ApplicationInstance(configuration, telemetry);
            first.MarkCertificateManagerBorrowed();
            await first.DisposeAsync();
            await using var second = new ApplicationInstance(configuration, telemetry);
            Assert.That(second.CertificateManager, Is.SameAs(manager));
            await second.DisposeAsync();
            using ICertificateStore store = manager.OpenCertificateStore(
                m_pkiRoot, CertificateStoreType.Directory, false);
            Assert.That(store, Is.Not.Null);
        }

        /// <summary>
        /// A released configuration accepts a custom or container-borrowed non-concrete manager for reattachment.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CustomManagerAfterFinalReleaseSupportsConfigurationReuseAsync(bool borrowed)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var retired = new CertificateManager(telemetry);
            var configuration = new ApplicationConfiguration(telemetry) { CertificateManager = retired };
            await using var first = new ApplicationInstance(configuration, telemetry);
            await first.DisposeAsync();
            var custom = new Mock<ICertificateManager>(MockBehavior.Strict);
            Mock<IAsyncDisposable> disposable = custom.As<IAsyncDisposable>();
            configuration.CertificateManager = custom.Object;
            await using var second = new ApplicationInstance(configuration, telemetry);
            if (borrowed)
            {
                second.MarkCertificateManagerBorrowed();
            }
            Assert.That(second.CertificateManager, Is.Null);
            Assert.That(configuration.CertificateManager, Is.SameAs(custom.Object));
            Assert.Throws<ObjectDisposedException>(() => configuration.CertificateManager = retired);
            Assert.That(configuration.CertificateManager, Is.SameAs(custom.Object));
            await second.DisposeAsync();
            await using var third = new ApplicationInstance(configuration, telemetry);
            Assert.That(configuration.CertificateManager, Is.SameAs(custom.Object));
            await third.DisposeAsync();
            Assert.That(configuration.CertificateManager, Is.SameAs(custom.Object));
            disposable.Verify(value => value.DisposeAsync(), Times.Never);
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => retired.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
        }
        /// <summary>
        /// Validation rejects the retired current manager instead of returning it through initialization's fast path.
        /// </summary>
        [Test]
        public async Task ValidationAfterFinalReleaseRejectsRetiredCurrentManagerAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var application = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            ApplicationConfiguration configuration = await application.Build(ApplicationUri, ProductUri).AsClient()
                .AddSecurityConfiguration(
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        SubjectName, CertificateStoreType.Directory, m_pkiRoot),
                    m_pkiRoot)
                .CreateAsync();
            CertificateManager retired = application.CertificateManager ??
                throw new InvalidOperationException("Validation did not create a manager.");
            await application.DisposeAsync();
            ObjectDisposedException? failure = await Assert.ThrowsAsync<ObjectDisposedException>(
                () => configuration.ValidateAsync(ApplicationType.Client));
            Assert.That(failure!.ObjectName, Is.EqualTo(nameof(CertificateManager)));
            Assert.That(configuration.CertificateManager, Is.SameAs(retired));
            await using var fresh = new CertificateManager(telemetry);
            configuration.CertificateManager = fresh;
            await using var owner = new ApplicationInstance(configuration, telemetry);
            await configuration.ValidateAsync(ApplicationType.Client);
            Assert.That(configuration.CertificateManager, Is.SameAs(fresh));
            await owner.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => fresh.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
        }

        /// <summary>
        /// Failed adoption leaves the existing configuration and all prospective manager reference counts unchanged.
        /// </summary>
        [Test]
        public async Task FailedConfigurationAdoptionDoesNotAcquirePartialReferencesAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var retired = new CertificateManager(telemetry);
            var prospectiveManager = new CertificateManager(telemetry);
            var currentManager = new CertificateManager(telemetry);
            var retiringConfiguration = new ApplicationConfiguration(telemetry) { CertificateManager = retired };
            var prospective = new ApplicationConfiguration(telemetry)
            {
                CertificateManager = prospectiveManager
            };
            prospective.CertificateManager = retired;
            await using var retiringApplication = new ApplicationInstance(retiringConfiguration, telemetry);
            await retiringApplication.DisposeAsync();
            var current = new ApplicationConfiguration(telemetry) { CertificateManager = currentManager };
            await using var application = new ApplicationInstance(current, telemetry);
            Assert.Throws<ObjectDisposedException>(() => application.ApplicationConfiguration = prospective);
            Assert.That(application.ApplicationConfiguration, Is.SameAs(current));
            await application.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => currentManager.LoadApplicationCertificatesAsync(current.SecurityConfiguration));
            await using var finalOwner = new ApplicationInstance(
                new ApplicationConfiguration(telemetry) { CertificateManager = prospectiveManager }, telemetry);
            using (ICertificateStore store = prospectiveManager.OpenCertificateStore(
                m_pkiRoot, CertificateStoreType.Directory, false))
            {
                Assert.That(store, Is.Not.Null);
            }
            await finalOwner.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => prospectiveManager.LoadApplicationCertificatesAsync(current.SecurityConfiguration));
        }

        /// <summary>
        /// Server disposal failure still releases manager assignments, and repeated callers receive the same failure.
        /// </summary>
        [Test]
        public async Task ServerDisposalFailureStillReleasesCertificateManagersAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var original = new CertificateManager(telemetry);
            var replacement = new CertificateManager(telemetry);
            var configuration = new ApplicationConfiguration(telemetry) { CertificateManager = original };
            var application = new ApplicationInstance(configuration, telemetry);
            configuration.CertificateManager = replacement;
            var failure = new InvalidOperationException("Server disposal failed.");
            var server = new Mock<IServerBase>();
            server.Setup(value => value.Dispose()).Throws(failure);
            await application.StartAsync(server.Object);
            AggregateException? observed = await Assert.ThrowsAsync<AggregateException>(
                async () => await application.DisposeAsync());
            Assert.That(observed!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(observed.InnerExceptions[0], Is.SameAs(failure));
            AggregateException? repeated = await Assert.ThrowsAsync<AggregateException>(
                async () => await application.DisposeAsync());
            Assert.That(repeated, Is.SameAs(observed));
            server.Verify(value => value.Dispose(), Times.Once);
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => original.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => replacement.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration));
        }

        /// <summary>
        /// Verifies cleanup of the exact validation-created manager after a custom validator replaces it and fails.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task LoaderDisposesCreatedManagerWhenValidatorReplacesItBeforeFailureAsync(
            bool loadFile,
            bool cancel)
        {
            await VerifyLoadFailureManagerLifetimeAsync(loadFile, cancel, markReplacementBorrowed: true);
        }

        /// <summary>
        /// Verifies that unmarked external replacements survive failed loading while base-created managers are disposed.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task LoaderPreservesExternalReplacementAndDisposesBaseCreatedManagerAsync(
            bool loadFile,
            bool cancel)
        {
            await VerifyLoadFailureManagerLifetimeAsync(loadFile, cancel, markReplacementBorrowed: false);
        }

        /// <summary>
        /// Failed loading cannot dispose a validation-created manager still leased by another application.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task LoaderPreservesCreatedManagerSharedWithActiveApplicationAsync(bool loadFile, bool cancel)
        {
            await VerifyLoadFailureManagerLifetimeAsync(
                loadFile, cancel, markReplacementBorrowed: true, shareCreatedManager: true);
        }

        /// <summary>
        /// Checks both manager identities and the original failure after file or stream loading fails.
        /// </summary>
        private async Task VerifyLoadFailureManagerLifetimeAsync(
            bool loadFile,
            bool cancel,
            bool markReplacementBorrowed,
            bool shareCreatedManager = false)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var borrowedManager = new CertificateManager(telemetry);
            var token = new CancellationToken(canceled: cancel);
            Exception failure = cancel
                ? new OperationCanceledException(token)
                : new InvalidOperationException("Custom validation failed.");
            var scenario = new LoadFailureScenario(
                telemetry, borrowedManager, failure, markReplacementBorrowed, shareCreatedManager);
            ReplacingValidationConfiguration.CurrentScenario.Value = scenario;
            await using var application = new ApplicationInstance(telemetry)
            {
                ApplicationType = ApplicationType.Server,
                ConfigurationType = typeof(ReplacingValidationConfiguration)
            };
            string? configPath = Utils.GetAbsoluteFilePath(
                "Opc.Ua.Configuration.Tests.Config.xml", checkCurrentDirectory: true, createAlways: false);
            Assert.That(configPath, Is.Not.Null);
            try
            {
                Exception? observed;
                if (loadFile)
                {
                    observed = await Assert.CatchAsync<Exception>(
                        async () => await application.LoadApplicationConfigurationAsync(configPath!, cancel, token));
                }
                else
                {
                    using var stream = File.OpenRead(configPath!);
                    observed = await Assert.CatchAsync<Exception>(
                        async () => await application.LoadApplicationConfigurationAsync(stream, cancel, token));
                }

                Assert.That(cancel || !loadFile ? observed : observed!.InnerException, Is.SameAs(failure));
                if (cancel)
                {
                    Assert.That(((OperationCanceledException)observed!).CancellationToken, Is.EqualTo(token));
                }
                Assert.That(application.ApplicationConfiguration, Is.Null);
                Assert.That(scenario.CreatedManager, Is.Not.Null.And.Not.SameAs(borrowedManager));
                if (shareCreatedManager)
                {
                    using (ICertificateStore sharedStore = scenario.CreatedManager!.OpenCertificateStore(
                        m_pkiRoot, CertificateStoreType.Directory, false))
                    {
                        Assert.That(sharedStore, Is.Not.Null);
                    }
                    await scenario.BorrowingApplication!.DisposeAsync();
                }
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    () => scenario.CreatedManager!.LoadApplicationCertificatesAsync(
                        scenario.Configuration!.SecurityConfiguration));
                using ICertificateStore store = borrowedManager.OpenCertificateStore(
                    m_pkiRoot, CertificateStoreType.Directory, false);
                Assert.That(store, Is.Not.Null);
            }
            finally
            {
                ReplacingValidationConfiguration.CurrentScenario.Value = null;
                if (scenario.BorrowingApplication != null)
                {
                    await scenario.BorrowingApplication.DisposeAsync();
                }
            }
        }

        [Test]
        public async Task TestNoFileConfigAsClientAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                Assert.That(applicationInstance, Is.Not.Null);

                ArrayOf<CertificateIdentifier> applicationCerts =
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        SubjectName,
                        CertificateStoreType.Directory,
                        m_pkiRoot);

                ApplicationConfiguration config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsClient()
                    .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false);
                Assert.That(config, Is.Not.Null);
                bool certOK = await applicationInstance
                    .CheckApplicationInstanceCertificatesAsync(true)
                    .ConfigureAwait(false);
                Assert.That(certOK, Is.True);
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ScopedStoreProvisioningCreatesAndReusesIdentityAsync(bool customProvider, bool customTrust)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var provider = new ProvisioningStoreProvider();
            string? thumbprint = null;
            for (int iteration = 0; iteration < 2; iteration++)
            {
                var application = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
                CertificateManager certificateManager;
                SecurityConfiguration security;
                await using (application.ConfigureAwait(false))
                {
                    var builder = application.Build(ApplicationUri, ProductUri).AsClient()
                        .AddSecurityConfiguration(ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                            SubjectName, CertificateStoreType.Directory, m_pkiRoot), m_pkiRoot);
                    security = application.ApplicationConfiguration!.SecurityConfiguration;
                    if (customProvider)
                    {
                        foreach (CertificateIdentifier identifier in security.ApplicationCertificates)
                        {
                            identifier.StoreType = provider.StoreTypeName;
                        }
                    }
                    if (customTrust)
                    {
                        security.TrustedPeerCertificates.StoreType = provider.StoreTypeName;
                        security.TrustedIssuerCertificates.StoreType = provider.StoreTypeName;
                    }
                    security.AddAppCertToTrustedStore = true;
                    certificateManager = CertificateManagerFactory.Create(security,
                        telemetry, options =>
                        {
                            if (customProvider)
                            {
                                options.AddStoreProvider(provider);
                            }
                        });
                    application.ApplicationConfiguration.CertificateManager = certificateManager;
                    ApplicationConfiguration configuration = await builder.CreateAsync().ConfigureAwait(false);
                    bool valid = await application.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false);
                    Assert.That(valid, Is.True);
                    using CertificateEntry entry = configuration.CertificateManager.AcquireApplicationCertificateByType(
                        ObjectTypeIds.RsaSha256ApplicationCertificateType)!;
                    Assert.That(entry, Is.Not.Null);
                    Assert.That(entry.Certificate.HasPrivateKey, Is.True);
                    if (iteration == 0)
                    {
                        thumbprint = entry.Certificate.Thumbprint;
                    }
                    Assert.That(entry.Certificate.Thumbprint, Is.EqualTo(thumbprint));
                    using ICertificateStore trusted = configuration.CertificateManager.OpenTrustedStore(TrustListIdentifier.Peers);
                    using CertificateCollection peers = await trusted.FindByThumbprintAsync(thumbprint!).ConfigureAwait(false);
                    Assert.That(peers, Has.Count.EqualTo(1));
                    if (iteration == 1)
                    {
                        CertificateIdentifier identifier = security.ApplicationCertificates[0];
                        await application.DeleteApplicationInstanceCertificateAsync().ConfigureAwait(false);
                        using ICertificateStore own = ((ICertificateStoreResolver)configuration.CertificateManager)
                            .OpenCertificateStore(identifier.StorePath!, identifier.StoreType, false);
                        using CertificateCollection remaining = await own.EnumerateAsync().ConfigureAwait(false);
                        using CertificateCollection trustedAfterDelete = await trusted.FindByThumbprintAsync(thumbprint!)
                            .ConfigureAwait(false);
                        Assert.That(remaining, Is.Empty);
                        Assert.That(trustedAfterDelete, Is.Empty);
                    }
                }
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    () => certificateManager.LoadApplicationCertificatesAsync(security)).ConfigureAwait(false);
            }
            Assert.That(provider.OpenCount, customProvider ? Is.GreaterThan(0) : Is.Zero);
        }

        [TestCase(null)]
        [TestCase("")]
        public async Task DeleteCertificateSkipsMissingTrustedStorePathAsync(string? trustedPath)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Certificate certificate = CertificateBuilder.Create(SubjectName).SetRSAKeySize(2048).CreateForRSA();
            var identifier = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = Path.Combine(m_pkiRoot, "own"),
                Thumbprint = certificate.Thumbprint
            };
            await certificate.AddToStoreAsync(identifier.StoreType, identifier.StorePath, null, telemetry)
                .ConfigureAwait(false);
            var manager = new CertificateManager(telemetry);
            var application = new ApplicationInstance(telemetry)
            {
                ApplicationConfiguration = new ApplicationConfiguration(telemetry)
                {
                    CertificateManager = manager,
                    SecurityConfiguration = new SecurityConfiguration
                    {
                        ApplicationCertificates = [identifier],
                        TrustedPeerCertificates = new CertificateTrustList { StorePath = trustedPath }
                    }
                }
            };
            await using (application.ConfigureAwait(false))
            {
                await application.DeleteApplicationInstanceCertificateAsync().ConfigureAwait(false);
                using ICertificateStore own = CertificateIdentifierResolver.OpenStore(identifier, telemetry)!;
                using CertificateCollection remaining = await own!.EnumerateAsync().ConfigureAwait(false);
                Assert.That(remaining, Is.Empty);
            }
        }

        [Test]
        public async Task DeleteCertificateUsesConfiguredStoresForManagerWithoutResolverAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var application = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (application.ConfigureAwait(false))
            {
                ApplicationConfiguration configuration = await application.Build(ApplicationUri, ProductUri).AsClient()
                    .AddSecurityConfiguration(ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        SubjectName, CertificateStoreType.Directory, m_pkiRoot), m_pkiRoot)
                    .CreateAsync().ConfigureAwait(false);
                configuration.SecurityConfiguration.AddAppCertToTrustedStore = true;
                Assert.That(await application.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false),
                    Is.True);
                ICertificateManager originalManager = configuration.CertificateManager;
                using CertificateEntry entry = originalManager.AcquireApplicationCertificateByType(
                    ObjectTypeIds.RsaSha256ApplicationCertificateType)!;
                var legacyManager = new Mock<ICertificateManager>(MockBehavior.Strict);
                legacyManager.Setup(manager => manager.SnapshotApplicationCertificates())
                    .Returns(originalManager.SnapshotApplicationCertificates);
                configuration.CertificateManager = legacyManager.Object;
                try
                {
                    await application.DeleteApplicationInstanceCertificateAsync().ConfigureAwait(false);
                    CertificateIdentifier identifier = configuration.SecurityConfiguration.ApplicationCertificates[0];
                    using ICertificateStore own = CertificateIdentifierResolver.OpenStore(identifier, telemetry)!;
                    using CertificateCollection remaining = await own!.EnumerateAsync().ConfigureAwait(false);
                    using ICertificateStore trusted = configuration.SecurityConfiguration.TrustedPeerCertificates
                        .OpenStore(telemetry);
                    using CertificateCollection peers = await trusted.FindByThumbprintAsync(entry!.Certificate.Thumbprint)
                        .ConfigureAwait(false);
                    Assert.That(remaining, Is.Empty);
                    Assert.That(peers, Is.Empty);
                    legacyManager.Verify(manager => manager.OpenTrustedStore(It.IsAny<TrustListIdentifier>()),
                        Times.Never);
                }
                finally
                {
                    configuration.CertificateManager = originalManager;
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ScopedStoreProvisioningRejectsUnavailablePrivateKeyAsync(bool publicOnly)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var provider = new ProvisioningStoreProvider();
            var application = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                DisableCertificateAutoCreation = !publicOnly
            };
            await using (application.ConfigureAwait(false))
            {
                var builder = application.Build(ApplicationUri, ProductUri).AsClient()
                    .AddSecurityConfiguration(ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        SubjectName, CertificateStoreType.Directory, m_pkiRoot), m_pkiRoot);
                SecurityConfiguration security = application.ApplicationConfiguration!.SecurityConfiguration;
                CertificateIdentifier identifier = security.ApplicationCertificates[0];
                identifier.StoreType = provider.StoreTypeName;
                var manager = CertificateManagerFactory.Create(security, telemetry, options => options.AddStoreProvider(provider));
                application.ApplicationConfiguration.CertificateManager = manager;
                string? originalThumbprint = null;
                using (ICertificateStore store = manager.OpenCertificateStore(identifier.StorePath!, identifier.StoreType, false))
                {
                    if (publicOnly)
                    {
                        using Certificate keyPair = CertificateBuilder.Create(SubjectName).SetRSAKeySize(2048).CreateForRSA();
                        using var certificate = new Certificate(keyPair.RawData);
                        originalThumbprint = certificate.Thumbprint;
                        await store.AddAsync(certificate).ConfigureAwait(false);
                    }
                }
                await builder.CreateAsync().ConfigureAwait(false);
                ServiceResultException? failure = null;
                try
                {
                    await application.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false);
                }
                catch (ServiceResultException exception)
                {
                    failure = exception;
                }
                Assert.That(failure, Is.Not.Null);
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                using ICertificateStore own = manager.OpenCertificateStore(identifier.StorePath!, identifier.StoreType, false);
                using CertificateCollection remaining = await own.EnumerateAsync().ConfigureAwait(false);
                Assert.That(remaining, Has.Count.EqualTo(publicOnly ? 1 : 0));
                if (publicOnly)
                {
                    Assert.That(remaining[0].Thumbprint, Is.EqualTo(originalThumbprint));
                    Assert.That(remaining[0].HasPrivateKey, Is.False);
                }
            }
        }

        private sealed class ProvisioningStoreProvider : ICertificateStoreProvider
        {
            public string StoreTypeName => "ScopedProvisioningDirectory";
            public int OpenCount { get; private set; }

            public bool SupportsStorePath(string storePath) => false;

            public ICertificateStore CreateStore(ITelemetryContext telemetry)
            {
                OpenCount++;
                return new DirectoryCertificateStore(false, telemetry);
            }
        }

        [Test]
        public async Task TestNoFileConfigRespectsMinimumKeySizeOnCreationAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                Assert.That(applicationInstance, Is.Not.Null);

                ArrayOf<CertificateIdentifier> applicationCerts =
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        SubjectName,
                        CertificateStoreType.Directory,
                        m_pkiRoot);

                const ushort minimumKeySize = 4096;

                ApplicationConfiguration config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsClient()
                    .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                    .SetMinimumCertificateKeySize(minimumKeySize)
                    .CreateAsync()
                    .ConfigureAwait(false);
                Assert.That(config, Is.Not.Null);

                bool certOK = await applicationInstance
                    .CheckApplicationInstanceCertificatesAsync(true)
                    .ConfigureAwait(false);
                Assert.That(certOK, Is.True);

                CertificateIdentifier certId = config.SecurityConfiguration.ApplicationCertificates[0];
                using Certificate certificate = (await CertificateIdentifierResolver
                    .LoadPrivateKeyAsync(
                        certId,
                        passwordProvider: config.SecurityConfiguration.CertificatePasswordProvider,
                        config.ApplicationUri,
                        telemetry)
                    .ConfigureAwait(false))!;

                Assert.That(certificate, Is.Not.Null);
                Assert.That(X509Utils.GetPublicKeySize(certificate), Is.EqualTo(minimumKeySize));
            }
        }

        [Test]
        public async Task TestBadApplicationInstanceAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            ArrayOf<CertificateIdentifier> applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            // no app name
            await using (var applicationInstance = new ApplicationInstance(telemetry))
            {
                Assert.That(applicationInstance, Is.Not.Null);

                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await applicationInstance
                        .Build(ApplicationUri, ProductUri)
                        .AsServer([EndpointUrl])
                        .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                        .CreateAsync()
                        .ConfigureAwait(false));
            }

            // discoveryserver can not be combined with client/server
            await using (var applicationInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.DiscoveryServer
            })
            {
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await applicationInstance
                        .Build(ApplicationUri, ProductUri)
                        .AsClient()
                        .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                        .CreateAsync()
                        .ConfigureAwait(false));
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await applicationInstance
                        .Build(ApplicationUri, ProductUri)
                        .AsServer([EndpointUrl])
                        .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                        .CreateAsync()
                        .ConfigureAwait(false));
            }

            // server overrides client settings
            await using (var applicationInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.Client
            })
            {
                ApplicationConfiguration config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsServer([EndpointUrl])
                    .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false);
                Assert.That(applicationInstance.ApplicationType, Is.EqualTo(ApplicationType.Server));
            }

            // client overrides server setting
            await using (var applicationInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.Server
            })
            {
                await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsClient()
                    .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false);
                Assert.That(applicationInstance.ApplicationType, Is.EqualTo(ApplicationType.Client));
            }

            // invalid sec policy testing
            await using (var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName })
            {
                // invalid use, use AddUnsecurePolicyNone instead
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await applicationInstance
                        .Build(ApplicationUri, ProductUri)
                        .AsServer([EndpointUrl])
                        .AddPolicy(MessageSecurityMode.None, SecurityPolicies.None)
                        .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                        .CreateAsync()
                        .ConfigureAwait(false));
                // invalid mix sign / none
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await applicationInstance
                        .Build(ApplicationUri, ProductUri)
                        .AsServer([EndpointUrl])
                        .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.None)
                        .AddSecurityConfiguration(applicationCerts)
                        .CreateAsync()
                        .ConfigureAwait(false));
                // invalid policy
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await applicationInstance
                        .Build(ApplicationUri, ProductUri)
                        .AsServer([EndpointUrl])
                        .AddPolicy(MessageSecurityMode.Sign, "123")
                        .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                        .CreateAsync()
                        .ConfigureAwait(false));
                // invalid user token policy
                await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                    await applicationInstance
                        .Build(ApplicationUri, ProductUri)
                        .AsServer([EndpointUrl])
                        .AddUserTokenPolicy(null!)
                        .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                        .CreateAsync()
                        .ConfigureAwait(false));
            }
        }

        [Test]
        public async Task TestNoFileConfigAsServerMinimalAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                Assert.That(applicationInstance, Is.Not.Null);

                ArrayOf<CertificateIdentifier> applicationCerts =
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        SubjectName,
                        CertificateStoreType.Directory,
                        m_pkiRoot);

                ApplicationConfiguration config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .SetOperationTimeout(10000)
                    .AsServer([EndpointUrl])
                    .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false);
                Assert.That(config, Is.Not.Null);
                bool certOK = await applicationInstance
                    .CheckApplicationInstanceCertificatesAsync(true)
                    .ConfigureAwait(false);
                Assert.That(certOK, Is.True);
            }
        }

        [Test]
        public async Task TestNoFileConfigAsServerMaximalAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                Assert.That(applicationInstance, Is.Not.Null);

                ArrayOf<CertificateIdentifier> applicationCerts =
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        SubjectName,
                        CertificateStoreType.Directory,
                        m_pkiRoot);

                ApplicationConfiguration config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .SetTransportQuotas(new TransportQuotas { OperationTimeout = 10000 })
                    .AsServer([EndpointUrl])
                    .AddSignPolicies()
                    .AddSignAndEncryptPolicies()
                    .AddUnsecurePolicyNone()
                    .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic256)
                    .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic128Rsa15)
                    .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256)
                    .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic128Rsa15)
                    .AddUserTokenPolicy(UserTokenType.Anonymous)
                    .AddUserTokenPolicy(UserTokenType.UserName)
                    .AddUserTokenPolicy(
                        new UserTokenPolicy(UserTokenType.Certificate)
                        {
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                        })
                    .SetDiagnosticsEnabled(true)
                    .SetPublishingResolution(100)
                    .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                    .SetAddAppCertToTrustedStore(true)
                    .SetAutoAcceptUntrustedCertificates(true)
                    .SetMinimumCertificateKeySize(1024)
                    .SetRejectSHA1SignedCertificates(false)
                    .SetSendCertificateChain(true)
                    .SetSuppressNonceValidationErrors(true)
                    .SetRejectUnknownRevocationStatus(true)
                    .CreateAsync()
                    .ConfigureAwait(false);
                Assert.That(config, Is.Not.Null);
                bool certOK = await applicationInstance
                    .CheckApplicationInstanceCertificatesAsync(true)
                    .ConfigureAwait(false);
                Assert.That(certOK, Is.True);
            }
        }

        [Test]
        public async Task TestNoFileConfigAsClientAndServerAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                Assert.That(applicationInstance, Is.Not.Null);

                ArrayOf<CertificateIdentifier> applicationCerts =
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        SubjectName,
                        CertificateStoreType.Directory,
                        m_pkiRoot);

                ApplicationConfiguration config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .SetMaxBufferSize(32768)
                    .AsServer([EndpointUrl])
                    .AddUnsecurePolicyNone()
                    .AddSignPolicies()
                    .AddSignAndEncryptPolicies()
                    .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic256)
                    .SetDiagnosticsEnabled(true)
                    .AsClient()
                    .AddSecurityConfiguration(
                        applicationCerts,
                        CertificateStoreType.Directory,
                        CertificateStoreType.X509Store)
                    .CreateAsync()
                    .ConfigureAwait(false);
                Assert.That(config, Is.Not.Null);
                bool certOK = await applicationInstance
                    .CheckApplicationInstanceCertificatesAsync(true)
                    .ConfigureAwait(false);
                Assert.That(certOK, Is.True);
            }
        }

        /// <summary>
        /// Test case when app cert already exists or when new
        /// cert is created in X509Store.
        /// </summary>
        [Test]
        [Repeat(2)]
        public async Task TestNoFileConfigAsServerX509StoreAsync()
        {
#if NETCOREAPP2_1_OR_GREATER
            // this test fails on macOS, ignore
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("X509Store trust lists not supported on mac OS.");
            }
#endif
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                Assert.That(applicationInstance, Is.Not.Null);

                ArrayOf<CertificateIdentifier> applicationCerts =
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        SubjectName,
                        CertificateStoreType.Directory,
                        m_pkiRoot);

                ApplicationConfiguration config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsServer([EndpointUrl])
                    .AddUnsecurePolicyNone()
                    .AddSignAndEncryptPolicies()
                    .AddUserTokenPolicy(UserTokenType.UserName)
                    .AsClient()
                    .SetDefaultSessionTimeout(10000)
                    .AddSecurityConfiguration(applicationCerts, CertificateStoreType.X509Store)
                    .CreateAsync()
                    .ConfigureAwait(false);
                Assert.That(config, Is.Not.Null);
                CertificateIdentifier applicationCertificate = applicationInstance
                    .ApplicationConfiguration!
                    .SecurityConfiguration
                    .ApplicationCertificate!;

                bool certOK = await applicationInstance
                    .CheckApplicationInstanceCertificatesAsync(true)
                    .ConfigureAwait(false);

                // Resolve the cert via the resolver since the identifier no
                // longer caches it.
                using Certificate appCert = (await CertificateIdentifierResolver
                    .ResolveAsync(
                        applicationCertificate,
                        registry: null,
                        needPrivateKey: false,
                        applicationInstance.ApplicationConfiguration.ApplicationUri,
                        telemetry)
                    .ConfigureAwait(false))!;
                bool deleteAfterUse = appCert != null;

                Assert.That(certOK, Is.True);
                using (
                    ICertificateStore store =
                        applicationInstance.ApplicationConfiguration.SecurityConfiguration
                            .TrustedPeerCertificates
                            .OpenStore(telemetry))
                {
                    // store public key in trusted store
                    byte[] rawData = appCert!.RawData;
                    using var publicKey = Certificate.FromRawData(rawData);
                    await store.AddAsync(publicKey)
                        .ConfigureAwait(false);
                }

                if (deleteAfterUse)
                {
                    string thumbprint = appCert.Thumbprint;
                    using (ICertificateStore store = CertificateIdentifierResolver
                        .OpenStore(applicationCertificate, telemetry)!)
                    {
                        bool success = await store!.DeleteAsync(thumbprint).ConfigureAwait(false);
                        Assert.That(success, Is.True);
                    }
                    using (
                        ICertificateStore store =
                            applicationInstance.ApplicationConfiguration.SecurityConfiguration
                                .TrustedPeerCertificates
                                .OpenStore(telemetry))
                    {
                        bool success = await store.DeleteAsync(thumbprint).ConfigureAwait(false);
                        Assert.That(success, Is.True);
                    }
                }
            }
        }

        [Test]
        public async Task TestNoFileConfigAsServerCustomAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                Assert.That(applicationInstance, Is.Not.Null);

                ArrayOf<CertificateIdentifier> applicationCerts =
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        SubjectName,
                        CertificateStoreType.Directory,
                        m_pkiRoot);

                ApplicationConfiguration config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsServer([EndpointUrl, "opc.https://localhost:51001"], s_alternateBaseAddresses)
                    .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                    .SetAddAppCertToTrustedStore(true)
                    .CreateAsync()
                    .ConfigureAwait(false);
                Assert.That(config, Is.Not.Null);
                bool certOK = await applicationInstance
                    .CheckApplicationInstanceCertificatesAsync(true)
                    .ConfigureAwait(false);
                Assert.That(certOK, Is.True);
            }
        }

        public enum InvalidCertType
        {
            NoIssues,
            NoIssuer,
            Expired,
            IssuerExpired,
            NotYetValid,
            IssuerNotYetValid,
            KeySize1024,
            HostName
        }

        /// <summary>
        /// Test to verify that an existing cert with suppressible issues
        /// is not recreated/replaced.
        /// </summary>
        [Test]
        [TestCase(InvalidCertType.NoIssues, true, true)]
        [TestCase(InvalidCertType.NotYetValid, true, true)]
        [TestCase(InvalidCertType.Expired, true, true)]
        [TestCase(InvalidCertType.HostName, true, false)]
        [TestCase(InvalidCertType.HostName, false, true)]
        [TestCase(InvalidCertType.KeySize1024, true, false)]
        public async Task TestInvalidAppCertDoNotRecreateAsync(
            InvalidCertType certType,
            bool server,
            bool suppress)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // pki directory root for test runs.
            string pkiRoot = Path.GetTempPath() +
                Path.GetRandomFileName() +
                Path.DirectorySeparatorChar;

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                Assert.That(applicationInstance, Is.Not.Null);

                ArrayOf<CertificateIdentifier> applicationCerts =
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        SubjectName,
                        CertificateStoreType.Directory,
                        pkiRoot);

                ApplicationConfiguration config;
                if (server)
                {
                    config = await applicationInstance
                        .Build(ApplicationUri, ProductUri)
                        .AsServer(s_baseAddresses)
                        .AddSecurityConfiguration(applicationCerts, pkiRoot)
                        .CreateAsync()
                        .ConfigureAwait(false);
                }
                else
                {
                    config = await applicationInstance
                        .Build(ApplicationUri, ProductUri)
                        .AsClient()
                        .AddSecurityConfiguration(applicationCerts, pkiRoot)
                        .CreateAsync()
                        .ConfigureAwait(false);
                }

                Assert.That(config, Is.Not.Null);

                CertificateIdentifier applicationCertificate = applicationInstance
                    .ApplicationConfiguration!
                    .SecurityConfiguration
                    .ApplicationCertificate!;
                Assert.That(applicationCertificate!.Thumbprint, Is.Null.Or.Empty);

                Certificate? publicKey = null;
                using (Certificate testCert = CreateInvalidCert(certType))
                {
                    Assert.That(testCert, Is.Not.Null);
                    Assert.That(testCert.HasPrivateKey, Is.True);
                    await testCert.AddToStoreAsync(
                        applicationCertificate.StoreType!,
                        applicationCertificate.StorePath!,
                        password: null,
                        telemetry).ConfigureAwait(false);
                    publicKey = Certificate.FromRawData(testCert.RawData);
                }

                using (publicKey)
                {
                    if (suppress)
                    {
                        bool certOK = await applicationInstance
                            .CheckApplicationInstanceCertificatesAsync(true)
                            .ConfigureAwait(false);

                        Assert.That(certOK, Is.True);
                        Assert.That(applicationCertificate.Thumbprint, Is.EqualTo(publicKey.Thumbprint));
                    }
                    else
                    {
                        ServiceResultException sre = await Assert
                            .ThrowsAsync<ServiceResultException>(async () =>
                                await applicationInstance.CheckApplicationInstanceCertificatesAsync(
                                    true)
                                .ConfigureAwait(false));
                        Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                    }
                }
            }
        }

        /// <summary>
        /// Test to verify that an existing cert with suppressible issues
        /// is not recreated/replaced.
        /// </summary>
        [Test]
        [TestCase(InvalidCertType.NoIssues, true, true)]
        [TestCase(InvalidCertType.NoIssuer, true, false)]
        [TestCase(InvalidCertType.NotYetValid, true, true)]
        [TestCase(InvalidCertType.Expired, true, true)]
        [TestCase(InvalidCertType.IssuerNotYetValid, true, true)]
        [TestCase(InvalidCertType.IssuerExpired, true, true)]
        [TestCase(InvalidCertType.HostName, true, false)]
        [TestCase(InvalidCertType.HostName, false, true)]
        //TODO [TestCase(InvalidCertType.KeySize1024, true, false)]
        public async Task TestInvalidAppCertChainDoNotRecreateAsync(
            InvalidCertType certType,
            bool server,
            bool suppress)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // pki directory root for test runs.
            string pkiRoot = Path.GetTempPath() +
                Path.GetRandomFileName() +
                Path.DirectorySeparatorChar;

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                Assert.That(applicationInstance, Is.Not.Null);

                ArrayOf<CertificateIdentifier> applicationCerts =
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        SubjectName,
                        CertificateStoreType.Directory,
                        pkiRoot);

                ApplicationConfiguration config;
                if (server)
                {
                    config = await applicationInstance
                        .Build(ApplicationUri, ProductUri)
                        .AsServer(s_baseAddresses)
                        .AddSecurityConfiguration(applicationCerts, pkiRoot)
                        .CreateAsync()
                        .ConfigureAwait(false);
                }
                else
                {
                    config = await applicationInstance
                        .Build(ApplicationUri, ProductUri)
                        .AsClient()
                        .AddSecurityConfiguration(applicationCerts, pkiRoot)
                        .CreateAsync()
                        .ConfigureAwait(false);
                }
                Assert.That(config, Is.Not.Null);

                CertificateIdentifier applicationCertificate = applicationInstance
                    .ApplicationConfiguration!
                    .SecurityConfiguration
                    .ApplicationCertificate!;
                Assert.That(applicationCertificate!.Thumbprint, Is.Null.Or.Empty);

                using CertificateCollection testCerts = CreateInvalidCertChain(certType);
                if (certType != InvalidCertType.NoIssuer)
                {
                    using Certificate issuerCert = testCerts[1];
                    Assert.That(issuerCert, Is.Not.Null);
                    Assert.That(issuerCert.HasPrivateKey, Is.False);
                    await issuerCert.AddToStoreAsync(
                        applicationInstance
                            .ApplicationConfiguration
                            .SecurityConfiguration
                            .TrustedIssuerCertificates
                            .StoreType!,
                        applicationInstance
                            .ApplicationConfiguration
                            .SecurityConfiguration
                            .TrustedIssuerCertificates
                            .StorePath!,
                        password: null,
                        telemetry).ConfigureAwait(false);
                }

                Certificate? publicKey = null;
                using (Certificate testCert = testCerts[0])
                {
                    Assert.That(testCert, Is.Not.Null);
                    Assert.That(testCert.HasPrivateKey, Is.True);
                    await testCert.AddToStoreAsync(
                        applicationCertificate.StoreType!,
                        applicationCertificate.StorePath!,
                        password: null,
                        telemetry).ConfigureAwait(false);
                    publicKey = Certificate.FromRawData(testCert.RawData);
                }

                using (publicKey)
                {
                    if (suppress)
                    {
                        bool certOK = await applicationInstance
                            .CheckApplicationInstanceCertificatesAsync(true)
                            .ConfigureAwait(false);

                        Assert.That(certOK, Is.True);
                        Assert.That(applicationCertificate.Thumbprint, Is.EqualTo(publicKey.Thumbprint));
                    }
                    else
                    {
                        ServiceResultException sre = await Assert
                            .ThrowsAsync<ServiceResultException>(async () =>
                                await applicationInstance.CheckApplicationInstanceCertificatesAsync(
                                    true)
                                .ConfigureAwait(false));
                        Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                    }
                }
            }
        }

        /// <summary>
        /// Tests that a supplied certificate is stored in the Trusted store of the Server after calling method AddOwnCertificateToTrustedStoreAsync
        /// </summary>
        [Test]
        public async Task TestAddOwnCertificateToTrustedStoreAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            //Arrange Application Instance
            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                ApplicationConfiguration configuration = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .SetOperationTimeout(10000)
                    .AsServer([EndpointUrl])
                    .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false);

                //Arrange cert
                DateTime notBefore = DateTime.Today.AddDays(-30);
                DateTime notAfter = DateTime.Today.AddDays(30);

                using Certificate cert = DefaultCertificateFactory.Instance.CreateCertificate(SubjectName)
                    .SetNotBefore(notBefore)
                    .SetNotAfter(notAfter)
                    .SetCAConstraint(-1)
                    .CreateForRSA();
                //Act
                await applicationInstance
                    .AddOwnCertificateToTrustedStoreAsync(cert, new CancellationToken())
                    .ConfigureAwait(false);
                using ICertificateStore store = configuration.SecurityConfiguration.TrustedPeerCertificates
                    .OpenStore(telemetry);
                using CertificateCollection storedCertificates = await store
                    .FindByThumbprintAsync(cert.Thumbprint)
                    .ConfigureAwait(false);

                //Assert
                Assert.That(storedCertificates, Does.Contain(cert));
            }
        }

        /// <summary>
        /// This tests that instantiating three application instances the second with a SubjectName
        /// being a substring of the first one's CN, but without containing the "CN=", failes due to the legacy fuzzy search
        /// matching the first certificate, which is invalid for the second application instance (different ApplicationUri
        /// The test shows 3 cases of creating application instances:
        ///     1 succeeds since there is no certificate identification collision (store is empty).
        ///     2 fails because in the process of finding the Application certificate the
        ///         1'st - AppCertificate (Wrong one) is selected due to the Fuzzy search,
        ///         and further on the validation fails (different ApplicationUri, etc).
        ///     3 succeeds because in the process of finding the Application certificate the correct application certificate
        ///         with correct subjectName "CN=UA, O=...." and ApplicationUri is searched for, no fuzzy search performed,
        ///         none is found and a new one gets created, selected, further validations succeed.
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task TestAddTwoAppCertificatesToTrustedStoreAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            string subjectName = SubjectName;
            //Arrange Application Instance
            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                ApplicationConfiguration configuration = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsClient()
                    .AddSecurityConfigurationStores(subjectName,
                                                    $"{m_pkiRoot}/pki/own",
                                                    $"{m_pkiRoot}/pki/trusted",
                                                    $"{m_pkiRoot}/pki/issuer",
                                                    $"{m_pkiRoot}/pki/rejected")
                    .CreateAsync()
                    .ConfigureAwait(false);

                await Assert.DoesNotThrowAsync(
                    async () => await applicationInstance.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false));

                subjectName = "UA";// UA is a substring of the previous certificate SubjectName CN
                var applicationInstance2 = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
                await using (applicationInstance2.ConfigureAwait(false))
                {
                    ApplicationConfiguration configuration2 = await applicationInstance2
                        .Build(ApplicationUri + "2", ProductUri + "2")
                        .AsClient()
                        .AddSecurityConfigurationStores(subjectName,
                                                        $"{m_pkiRoot}/pki/own",
                                                        $"{m_pkiRoot}/pki/trusted",
                                                        $"{m_pkiRoot}/pki/issuer",
                                                        $"{m_pkiRoot}/pki/rejected")
                        .CreateAsync()
                        .ConfigureAwait(false);

                    // Since the SubjectName is a substring of the first one's CN,
                    // the matching algorithm will find the first certificate because a fuzzy match is done on the SubjectName when SubjectName does not contain CN=.
                    // However, since the ApplicationUri is different, the certificate will be considered invalid
                    ServiceResultException exception = await Assert
                        .ThrowsAsync<ServiceResultException>(async () =>
                            await applicationInstance2.CheckApplicationInstanceCertificatesAsync(true)
                                .ConfigureAwait(false));
                    Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                }

                subjectName = "CN=UA";// UA is a substring of the previous certificate SubjectName CN
                var applicationInstance3 = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
                await using (applicationInstance3.ConfigureAwait(false))
                {
                    ApplicationConfiguration configuration3 = await applicationInstance3
                        .Build(ApplicationUri + "3", ProductUri + "3")
                        .AsClient()
                        .AddSecurityConfigurationStores(subjectName,
                                                        $"{m_pkiRoot}/pki/own",
                                                        $"{m_pkiRoot}/pki/trusted",
                                                        $"{m_pkiRoot}/pki/issuer",
                                                        $"{m_pkiRoot}/pki/rejected")
                        .CreateAsync()
                        .ConfigureAwait(false);

                    // Since the SubjectName contains CN=UA, the matching algorithm will not do a fuzzy match and will not find the first certificate.
                    await Assert.DoesNotThrowAsync(
                        async () => await applicationInstance3.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false));
                }
            }
        }

        /// <summary>
        /// Test to verify that a new cert is not recreated/replaced if DisableCertificateAutoCreation is set.
        /// </summary>
        [Theory]
        public async Task TestDisableCertificateAutoCreationAsync(
            bool server,
            bool disableCertificateAutoCreation)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // pki directory root for test runs.
            string pkiRoot = Path.GetTempPath() +
                Path.GetRandomFileName() +
                Path.DirectorySeparatorChar;

            var applicationInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                DisableCertificateAutoCreation = disableCertificateAutoCreation
            };
            await using (applicationInstance.ConfigureAwait(false))
            {
                Assert.That(applicationInstance, Is.Not.Null);
                ApplicationConfiguration config;

                ArrayOf<CertificateIdentifier> applicationCerts =
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        SubjectName,
                        CertificateStoreType.Directory,
                        m_pkiRoot);

                if (server)
                {
                    config = await applicationInstance
                        .Build(ApplicationUri, ProductUri)
                        .AsServer(s_baseAddressesArray)
                        .AddSecurityConfiguration(applicationCerts, pkiRoot)
                        .CreateAsync()
                        .ConfigureAwait(false);
                }
                else
                {
                    config = await applicationInstance
                        .Build(ApplicationUri, ProductUri)
                        .AsClient()
                        .AddSecurityConfiguration(applicationCerts, pkiRoot)
                        .CreateAsync()
                        .ConfigureAwait(false);
                }
                Assert.That(config, Is.Not.Null);

                CertificateIdentifier applicationCertificate = applicationInstance
                    .ApplicationConfiguration!
                    .SecurityConfiguration
                    .ApplicationCertificate!;
                Assert.That(applicationCertificate!.Thumbprint, Is.Null.Or.Empty);

                if (disableCertificateAutoCreation)
                {
                    ServiceResultException sre = await Assert
                        .ThrowsAsync<ServiceResultException>(async () =>
                            await applicationInstance.CheckApplicationInstanceCertificatesAsync(true)
                            .ConfigureAwait(false));
                    Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                }
                else
                {
                    bool certOK = await applicationInstance
                        .CheckApplicationInstanceCertificatesAsync(true)
                        .ConfigureAwait(false);
                    Assert.That(certOK, Is.True);
                }
            }
        }

        /// <summary>
        /// Test that multiple certificates with different ApplicationUris throw an exception.
        /// Even though FindAsync might load certificates by SubjectName without checking ApplicationUri,
        /// the explicit validation against the configured ApplicationUri ensures all loaded certificates have the same URI.
        /// </summary>
        [Test]
        public async Task TestMultipleCertificatesDifferentUrisThrowsExceptionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                Assert.That(applicationInstance, Is.Not.Null);

                // Create two certificates with different ApplicationUris
                const string uri1 = "urn:localhost:opcfoundation.org:App1";
                const string uri2 = "urn:localhost:opcfoundation.org:App2";

                using Certificate cert1 = DefaultCertificateFactory.Instance
                    .CreateApplicationCertificate(uri1, ApplicationName, SubjectName, [Utils.GetHostName()])
                    .SetNotBefore(DateTime.Today.AddDays(-1))
                    .SetNotAfter(DateTime.Today.AddYears(1))
                    .CreateForRSA();

                const string subjectName2 = "CN=UA Configuration Test 2, O=OPC Foundation, C=US, S=Arizona";
                using Certificate cert2 = DefaultCertificateFactory.Instance
                    .CreateApplicationCertificate(uri2, ApplicationName, subjectName2, [Utils.GetHostName()])
                    .SetNotBefore(DateTime.Today.AddDays(-1))
                    .SetNotAfter(DateTime.Today.AddYears(1))
                    .SetRSAKeySize(CertificateFactory.DefaultKeySize)
                    .CreateForRSA();

                // Save certificates to stores
                string certStorePath = m_pkiRoot + "own";
                var certStoreIdentifier = new CertificateStoreIdentifier(certStorePath, CertificateStoreType.Directory, false);
                using (ICertificateStore certStore = certStoreIdentifier.OpenStore(telemetry))
                {
                    await certStore.AddAsync(cert1).ConfigureAwait(false);
                    await certStore.AddAsync(cert2).ConfigureAwait(false);
                }

                var certId1 = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = certStorePath,
                    SubjectName = SubjectName,
                    CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                };

                var certId2 = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = certStorePath,
                    SubjectName = subjectName2,
                    CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                };

                ApplicationConfiguration config = await applicationInstance
                    .Build(uri1, ProductUri)
                    .AsClient()
                    .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false);
                Assert.That(config, Is.Not.Null);

                // Set multiple certificates
                config.SecurityConfiguration.ApplicationCertificates =
                [
                    certId1,
                    certId2
                ];

                // This should throw because all certificates must have the same ApplicationUri
                ServiceResultException sre = await Assert
                    .ThrowsAsync<ServiceResultException>(async () =>
                        await applicationInstance.CheckApplicationInstanceCertificatesAsync(true)
                        .ConfigureAwait(false));
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(sre.Message, Does.Contain("certificate") & Does.Contain("invalid"));
            }
        }

        /// <summary>
        /// Test that multiple certificates with the same ApplicationUri succeed.
        /// </summary>
        [Test]
        public async Task TestMultipleCertificatesSameUriSucceedsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                Assert.That(applicationInstance, Is.Not.Null);

                // Create two certificates with the same ApplicationUri
                using Certificate cert1 = DefaultCertificateFactory.Instance
                    .CreateApplicationCertificate(ApplicationUri, ApplicationName, SubjectName, [Utils.GetHostName()])
                    .SetNotBefore(DateTime.Today.AddDays(-1))
                    .SetNotAfter(DateTime.Today.AddYears(1))
                    .CreateForRSA();

                const string subjectName2 = "CN=UA Configuration Test RSA, O=OPC Foundation, C=US, S=Arizona";
                using Certificate cert2 = DefaultCertificateFactory.Instance
                    .CreateApplicationCertificate(ApplicationUri, ApplicationName, subjectName2, [Utils.GetHostName()])
                    .SetNotBefore(DateTime.Today.AddDays(-1))
                    .SetNotAfter(DateTime.Today.AddYears(1))
                    .SetRSAKeySize(CertificateFactory.DefaultKeySize)
                    .CreateForRSA();

                // Save certificates to stores
                string certStorePath = m_pkiRoot + "own";
                var certStoreIdentifier = new CertificateStoreIdentifier(certStorePath, CertificateStoreType.Directory, false);
                using (ICertificateStore certStore = certStoreIdentifier.OpenStore(telemetry))
                {
                    await certStore.AddAsync(cert1).ConfigureAwait(false);
                    await certStore.AddAsync(cert2).ConfigureAwait(false);
                }

                var certId1 = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = certStorePath,
                    SubjectName = SubjectName,
                    CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                };

                var certId2 = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = certStorePath,
                    SubjectName = subjectName2,
                    CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                };

                ApplicationConfiguration config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsClient()
                    .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false);
                Assert.That(config, Is.Not.Null);

                // Set multiple certificates with same URI
                config.SecurityConfiguration.ApplicationCertificates =
                [
                    certId1,
                    certId2
                ];

                // This should succeed because all certificates have the same ApplicationUri
                bool certOK = await applicationInstance
                    .CheckApplicationInstanceCertificatesAsync(true)
                    .ConfigureAwait(false);
                Assert.That(certOK, Is.True);
            }
        }

        /// <summary>
        /// Test that a certificate with multiple SAN URI entries where one matches the ApplicationUri succeeds.
        /// Tests with different certificate types (RSA, ECC NIST P-256, ECC NIST P-384, ECC Brainpool).
        /// </summary>
        [Test]
        [TestCaseSource(nameof(CertificateTypes))]
        public async Task TestCertificateWithMultipleSanUrisMatchingSucceedsAsync(NodeId certificateType)
        {
            // Skip test if certificate type is not supported on this platform
            if (!Utils.IsSupportedCertificateType(certificateType))
            {
                Assert.Ignore($"Certificate type {certificateType} is not supported on this platform.");
            }

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                Assert.That(applicationInstance, Is.Not.Null);

                // Create a certificate with multiple URIs in SAN, including the ApplicationUri
                const string uri1 = "urn:localhost:opcfoundation.org:App1";
                const string uri2 = ApplicationUri; // This matches
                const string uri3 = "https://localhost:8080/OpcUaApp";

                using Certificate cert = CreateCertificateWithMultipleUris(
                    [uri1, uri2, uri3],
                    SubjectName,
                    [Utils.GetHostName()],
                    certificateType);

                // Save certificate to store
                string certStorePath = m_pkiRoot + "own";
                var certStoreIdentifier = new CertificateStoreIdentifier(certStorePath, CertificateStoreType.Directory, false);
                using (ICertificateStore certStore = certStoreIdentifier.OpenStore(telemetry))
                {
                    await certStore.AddAsync(cert).ConfigureAwait(false);
                }

                var certId = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = certStorePath,
                    SubjectName = SubjectName,
                    CertificateType = certificateType
                };

                ApplicationConfiguration config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsClient()
                    .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                    .SetMinimumCertificateKeySize(256)
                    .CreateAsync()
                    .ConfigureAwait(false);
                Assert.That(config, Is.Not.Null);

                config.SecurityConfiguration.ApplicationCertificates = [certId];

                // This should succeed because one of the URIs matches
                bool certOK = await applicationInstance
                    .CheckApplicationInstanceCertificatesAsync(true)
                    .ConfigureAwait(false);
                Assert.That(certOK, Is.True);

                // Verify the certificate has multiple URIs
                // Load the certificate to check its URIs
                using Certificate loadedCert = (await CertificateIdentifierResolver
                    .ResolveAsync(certId, registry: null, needPrivateKey: false, applicationUri: null, telemetry)
                    .ConfigureAwait(false))!;
                IReadOnlyList<string> uris = X509Utils.GetApplicationUrisFromCertificate(loadedCert!);
                Assert.That(uris, Has.Count.EqualTo(3));
                Assert.Contains(uri1, uris.ToList());
                Assert.Contains(uri2, uris.ToList());
                Assert.Contains(uri3, uris.ToList());
            }
        }

        /// <summary>
        /// Test that a certificate with multiple SAN URI entries where none matches the ApplicationUri fails.
        /// Tests with different certificate types (RSA, ECC NIST P-256, ECC NIST P-384, ECC Brainpool).
        /// </summary>
        [Test]
        [TestCaseSource(nameof(CertificateTypes))]
        public async Task TestCertificateWithMultipleSanUrisNotMatchingThrowsAsync(NodeId certificateType)
        {
            // Skip test if certificate type is not supported on this platform
            if (!Utils.IsSupportedCertificateType(certificateType))
            {
                Assert.Ignore($"Certificate type {certificateType} is not supported on this platform.");
            }

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                Assert.That(applicationInstance, Is.Not.Null);

                // Create a certificate with multiple URIs in SAN, but none matching ApplicationUri
                const string uri1 = "urn:localhost:opcfoundation.org:App1";
                const string uri2 = "urn:localhost:opcfoundation.org:App2";
                const string uri3 = "https://localhost:8080/OpcUaApp";

                using Certificate cert = CreateCertificateWithMultipleUris(
                    [uri1, uri2, uri3],
                    SubjectName,
                    [Utils.GetHostName()],
                    certificateType);

                // Save certificate to store
                string certStorePath = m_pkiRoot + "own";
                var certStoreIdentifier = new CertificateStoreIdentifier(certStorePath, CertificateStoreType.Directory, false);
                using (ICertificateStore certStore = certStoreIdentifier.OpenStore(telemetry))
                {
                    await certStore.AddAsync(cert).ConfigureAwait(false);
                }

                var certId = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = certStorePath,
                    SubjectName = SubjectName,
                    CertificateType = certificateType
                };

                ApplicationConfiguration config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsClient()
                    .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                    .SetMinimumCertificateKeySize(256)
                    .CreateAsync()
                    .ConfigureAwait(false);
                Assert.That(config, Is.Not.Null);

                config.SecurityConfiguration.ApplicationCertificates = [certId];

                // This should fail because none of the URIs match
                ServiceResultException sre = await Assert
                    .ThrowsAsync<ServiceResultException>(async () =>
                        await applicationInstance.CheckApplicationInstanceCertificatesAsync(true)
                        .ConfigureAwait(false));
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            }
        }

        /// <summary>
        /// Test with multiple certificates, each with multiple SAN URIs, where all match the ApplicationUri.
        /// Tests with different certificate types (RSA, ECC NIST P-256, ECC NIST P-384, ECC Brainpool).
        /// </summary>
        [Test]
        [TestCaseSource(nameof(CertificateTypes))]
        public async Task TestMultipleCertificatesWithMultipleSanUrisAllMatchingSucceedsAsync(NodeId certificateType)
        {
            // Skip test if certificate type is not supported on this platform
            if (!Utils.IsSupportedCertificateType(certificateType))
            {
                Assert.Ignore($"Certificate type {certificateType} is not supported on this platform.");
            }

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                Assert.That(applicationInstance, Is.Not.Null);

                // Create first certificate with multiple URIs including ApplicationUri
                using Certificate cert1 = CreateCertificateWithMultipleUris(
                    [ApplicationUri, "https://localhost:8080/Test1", "opc.tcp://localhost:4840/Test1"],
                    SubjectName,
                    [Utils.GetHostName()],
                    certificateType);

                const string subjectName2 = "CN=UA Configuration Test 2, O=OPC Foundation, C=US, S=Arizona";
                // Create second certificate with multiple URIs including ApplicationUri
                using Certificate cert2 = CreateCertificateWithMultipleUris(
                    ["urn:localhost:opcfoundation.org:OtherApp", ApplicationUri, "https://localhost:9443/Test2"],
                    subjectName2,
                    [Utils.GetHostName()],
                    certificateType);

                // Save certificates to store
                string certStorePath = m_pkiRoot + "own";
                var certStoreIdentifier = new CertificateStoreIdentifier(certStorePath, CertificateStoreType.Directory, false);
                using (ICertificateStore certStore = certStoreIdentifier.OpenStore(telemetry))
                {
                    await certStore.AddAsync(cert1).ConfigureAwait(false);
                    await certStore.AddAsync(cert2).ConfigureAwait(false);
                }

                var certId1 = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = certStorePath,
                    SubjectName = SubjectName,
                    CertificateType = certificateType
                };

                var certId2 = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = certStorePath,
                    SubjectName = subjectName2,
                    CertificateType = certificateType
                };

                ApplicationConfiguration config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsClient()
                    .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                    .SetMinimumCertificateKeySize(256)
                    .CreateAsync()
                    .ConfigureAwait(false);
                Assert.That(config, Is.Not.Null);

                config.SecurityConfiguration.ApplicationCertificates =
                [
                    certId1,
                    certId2
                ];

                // This should succeed because both certificates contain ApplicationUri in their SAN
                bool certOK = await applicationInstance
                    .CheckApplicationInstanceCertificatesAsync(true)
                    .ConfigureAwait(false);
                Assert.That(certOK, Is.True);
            }
        }

        /// <summary>
        /// Test with multiple certificates where only one has the matching ApplicationUri in its SAN URIs.
        /// Tests with different certificate types (RSA, ECC NIST P-256, ECC NIST P-384, ECC Brainpool).
        /// </summary>
        [Test]
        [TestCaseSource(nameof(CertificateTypes))]
        public async Task TestMultipleCertificatesWithOnlyOneMatchingSanUriThrowsAsync(NodeId certificateType)
        {
            // Skip test if certificate type is not supported on this platform
            if (!Utils.IsSupportedCertificateType(certificateType))
            {
                Assert.Ignore($"Certificate type {certificateType} is not supported on this platform.");
            }

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            await using (applicationInstance.ConfigureAwait(false))
            {
                Assert.That(applicationInstance, Is.Not.Null);

                // Create first certificate with ApplicationUri
                using Certificate cert1 = CreateCertificateWithMultipleUris(
                    [ApplicationUri, "https://localhost:8080/Test1"],
                    SubjectName,
                    [Utils.GetHostName()],
                    certificateType);

                const string subjectName2 = "CN=UA Configuration Test 2, O=OPC Foundation, C=US, S=Arizona";
                // Create second certificate WITHOUT ApplicationUri
                using Certificate cert2 = CreateCertificateWithMultipleUris(
                    ["urn:localhost:opcfoundation.org:OtherApp", "https://localhost:9443/Test2"],
                    subjectName2,
                    [Utils.GetHostName()],
                    certificateType);

                // Save certificates to store
                string certStorePath = m_pkiRoot + "own";
                var certStoreIdentifier = new CertificateStoreIdentifier(certStorePath, CertificateStoreType.Directory, false);
                using (ICertificateStore certStore = certStoreIdentifier.OpenStore(telemetry))
                {
                    await certStore.AddAsync(cert1).ConfigureAwait(false);
                    await certStore.AddAsync(cert2).ConfigureAwait(false);
                }

                var certId1 = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = certStorePath,
                    SubjectName = SubjectName,
                    CertificateType = certificateType
                };

                var certId2 = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = certStorePath,
                    SubjectName = subjectName2,
                    CertificateType = certificateType
                };

                ApplicationConfiguration config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsClient()
                    .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                    .SetMinimumCertificateKeySize(256)
                    .CreateAsync()
                    .ConfigureAwait(false);
                Assert.That(config, Is.Not.Null);

                config.SecurityConfiguration.ApplicationCertificates =
                [
                    certId1,
                    certId2
                ];

                // This should fail because cert2 doesn't contain ApplicationUri
                ServiceResultException sre = await Assert
                    .ThrowsAsync<ServiceResultException>(async () =>
                        await applicationInstance.CheckApplicationInstanceCertificatesAsync(true)
                        .ConfigureAwait(false));
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            }
        }

        private static Certificate CreateInvalidCert(InvalidCertType certType)
        {
            // reasonable defaults
            DateTime notBefore = DateTime.Today.AddDays(-30);
            DateTime notAfter = DateTime.Today.AddDays(30);
            ushort keySize = CertificateFactory.DefaultKeySize;
            string[] domainNames = [Utils.GetHostName()];
            switch (certType)
            {
                case InvalidCertType.Expired:
                    notBefore = DateTime.Today.AddMonths(-12);
                    notAfter = DateTime.Today.AddDays(-7);
                    break;
                case InvalidCertType.NotYetValid:
                    notBefore = DateTime.Today.AddDays(7);
                    notAfter = notBefore.AddMonths(12);
                    break;
                case InvalidCertType.KeySize1024:
                    keySize = 1024;
                    break;
                case InvalidCertType.HostName:
                    domainNames = ["myhost", "1.2.3.4"];
                    break;
                case InvalidCertType.NoIssues:
                case InvalidCertType.NoIssuer:
                case InvalidCertType.IssuerExpired:
                case InvalidCertType.IssuerNotYetValid:
                    break;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected InvalidCertType {certType}");
            }

            return DefaultCertificateFactory.Instance.CreateApplicationCertificate(ApplicationUri, ApplicationName, SubjectName, domainNames)
                .SetNotBefore(notBefore)
                .SetNotAfter(notAfter)
                .SetRSAKeySize(keySize)
                .CreateForRSA();
        }

        private static CertificateCollection CreateInvalidCertChain(InvalidCertType certType)
        {
            // reasonable defaults
            DateTime notBefore = DateTime.Today.AddYears(-1);
            DateTime notAfter = DateTime.Today.AddYears(1);
            DateTime issuerNotBefore = notBefore;
            DateTime issuerNotAfter = notAfter;
            ushort keySize = CertificateFactory.DefaultKeySize;
            string[] domainNames = [Utils.GetHostName()];
            switch (certType)
            {
                case InvalidCertType.Expired:
                    notAfter = DateTime.Today.AddDays(-7);
                    break;
                case InvalidCertType.IssuerExpired:
                    issuerNotAfter = DateTime.Today.AddDays(-7);
                    break;
                case InvalidCertType.NotYetValid:
                    notBefore = DateTime.Today.AddDays(7);
                    break;
                case InvalidCertType.IssuerNotYetValid:
                    issuerNotBefore = DateTime.Today.AddDays(7);
                    break;
                case InvalidCertType.KeySize1024:
                    keySize = 1024;
                    break;
                case InvalidCertType.HostName:
                    domainNames = ["myhost", "1.2.3.4"];
                    break;
                case InvalidCertType.NoIssues:
                case InvalidCertType.NoIssuer:
                    break;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected InvalidCertType {certType}");
            }

            const string rootCASubjectName = "CN=Root CA Test, O=OPC Foundation, C=US, S=Arizona";
            using Certificate rootCA = DefaultCertificateFactory.Instance.CreateCertificate(rootCASubjectName)
                .SetNotBefore(issuerNotBefore)
                .SetNotAfter(issuerNotAfter)
                .SetCAConstraint(-1)
                .CreateForRSA();
            using Certificate appCert = DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                ApplicationUri,
                ApplicationName,
                SubjectName,
                domainNames)
                .SetNotBefore(notBefore)
                .SetNotAfter(notAfter)
                .SetIssuer(rootCA)
                .SetRSAKeySize(keySize)
                .CreateForRSA();
            using var rootCAPublic = Certificate.FromRawData(rootCA.RawData);

            // Collection AddRefs each cert; the using directives dispose our
            // local references so the caller's collection holds the only refs.
            return [appCert, rootCAPublic];
        }

        /// <summary>
        /// Provides a collection of certificate types for parameterized tests.
        /// </summary>
        /// <returns>An enumerable collection of NodeIds representing different certificate types.</returns>
        /// <remarks>
        /// This method is used as a TestCaseSource to provide different certificate types (RSA, ECC) for testing.
        /// Individual tests check platform support using Utils.IsSupportedCertificateType().
        /// </remarks>
        private static IEnumerable<NodeId> CertificateTypes()
        {
            yield return ObjectTypeIds.RsaSha256ApplicationCertificateType;
            yield return ObjectTypeIds.EccNistP256ApplicationCertificateType;
            yield return ObjectTypeIds.EccNistP384ApplicationCertificateType;
            yield return ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType;
            yield return ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType;
        }

        /// <summary>
        /// Helper method to create a certificate with multiple URI entries in the SAN.
        /// </summary>
        /// <param name="applicationUris">The list of application URIs to include in the SAN</param>
        /// <param name="subjectName">The subject name for the certificate</param>
        /// <param name="domainNames">The domain names for the certificate</param>
        /// <returns>A certificate with multiple URIs in the SAN extension</returns>
        private static Certificate CreateCertificateWithMultipleUris(
            IList<string> applicationUris,
            string subjectName,
            IList<string> domainNames,
            NodeId certificateType = default)
        {
            DateTime notBefore = DateTime.Today.AddDays(-1);
            DateTime notAfter = DateTime.Today.AddYears(1);

            // Default to RSA if not specified
            if (certificateType.IsNull)
            {
                certificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType;
            }
            // Create the SAN extension with multiple URIs
            var subjectAltName = new X509SubjectAltNameExtension(applicationUris, domainNames);

            // Build the certificate with the custom SAN extension
            ICertificateBuilder builder = CertificateBuilder
                .Create(subjectName)
                .SetNotBefore(notBefore)
                .SetNotAfter(notAfter)
                .AddExtension(subjectAltName);

            // Create certificate based on type
            if (certificateType == ObjectTypeIds.RsaSha256ApplicationCertificateType ||
                certificateType == ObjectTypeIds.RsaMinApplicationCertificateType)
            {
                return builder.SetRSAKeySize(CertificateFactory.DefaultKeySize).CreateForRSA();
            }
            else if (certificateType == ObjectTypeIds.EccNistP256ApplicationCertificateType)
            {
                return builder.SetECCurve(ECCurve.NamedCurves.nistP256).CreateForECDsa();
            }
            else if (certificateType == ObjectTypeIds.EccNistP384ApplicationCertificateType)
            {
                return builder.SetECCurve(ECCurve.NamedCurves.nistP384).CreateForECDsa();
            }
            else if (certificateType == ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType)
            {
                return builder.SetECCurve(ECCurve.NamedCurves.brainpoolP256r1).CreateForECDsa();
            }
            else if (certificateType == ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType)
            {
                return builder.SetECCurve(ECCurve.NamedCurves.brainpoolP384r1).CreateForECDsa();
            }
            else
            {
                // Default to RSA for unknown types
                return builder.SetRSAKeySize(CertificateFactory.DefaultKeySize).CreateForRSA();
            }
        }

        /// <summary>
        /// Replaces the manager created by base validation before returning a controlled failure.
        /// </summary>
        public sealed class ReplacingValidationConfiguration : ApplicationConfiguration
        {
            /// <summary>
            /// Creates a configuration using the scenario in the current test's asynchronous context.
            /// </summary>
            public ReplacingValidationConfiguration(ITelemetryContext telemetry)
                : base(telemetry)
            {
                m_scenario = CurrentScenario.Value ??
                    throw new InvalidOperationException("No load-failure scenario was supplied.");
            }

            /// <summary>
            /// Supplies isolated scenario state to the configuration loader's reflection-based constructor.
            /// </summary>
            internal static AsyncLocal<LoadFailureScenario?> CurrentScenario { get; } = new();

            /// <inheritdoc/>
            public override async Task ValidateAsync(ApplicationType applicationType, CancellationToken ct = default)
            {
                await base.ValidateAsync(applicationType, CancellationToken.None);
                m_scenario.CreatedManager = CertificateManager as CertificateManager ??
                    throw new InvalidOperationException("Base validation did not create a certificate manager.");
                m_scenario.Configuration = this;
                if (m_scenario.ShareCreatedManager)
                {
                    m_scenario.BorrowingApplication = new ApplicationInstance(this, m_scenario.Telemetry);
                }
                CertificateManager = m_scenario.BorrowedManager;
                if (m_scenario.MarkReplacementBorrowed)
                {
                    m_scenario.BorrowingApplication ??= new ApplicationInstance(
                        new ApplicationConfiguration(m_scenario.Telemetry)
                        {
                            CertificateManager = m_scenario.BorrowedManager
                        },
                        m_scenario.Telemetry);
                    m_scenario.BorrowingApplication.MarkCertificateManagerBorrowed();
                }
                throw m_scenario.Failure;
            }

            /// <summary>
            /// The scenario retained before validation crosses an asynchronous boundary.
            /// </summary>
            private readonly LoadFailureScenario m_scenario;
        }

        /// <summary>
        /// Carries manager identities and the expected error for one failed configuration load.
        /// </summary>
        internal sealed class LoadFailureScenario(
            ITelemetryContext telemetry,
            CertificateManager borrowedManager,
            Exception failure,
            bool markReplacementBorrowed,
            bool shareCreatedManager)
        {
            /// <summary>
            /// The telemetry used by the borrowing application.
            /// </summary>
            public ITelemetryContext Telemetry { get; } = telemetry;

            /// <summary>
            /// The externally supplied replacement, optionally marked as container-owned.
            /// </summary>
            public CertificateManager BorrowedManager { get; } = borrowedManager;

            /// <summary>
            /// Whether the validator explicitly registers the replacement as borrowed.
            /// </summary>
            public bool MarkReplacementBorrowed { get; } = markReplacementBorrowed;

            /// <summary>
            /// Whether another application acquires the validation-created manager before loading fails.
            /// </summary>
            public bool ShareCreatedManager { get; } = shareCreatedManager;

            /// <summary>
            /// The exact error returned by custom validation.
            /// </summary>
            public Exception Failure { get; } = failure;

            /// <summary>
            /// The manager created by base validation, before replacement.
            /// </summary>
            public CertificateManager? CreatedManager { get; set; }

            /// <summary>
            /// The failed configuration, retained to inspect its security settings after loader cleanup.
            /// </summary>
            public ApplicationConfiguration? Configuration { get; set; }

            /// <summary>
            /// The application that marks replacement provenance without cleaning up before the assertions.
            /// </summary>
            public ApplicationInstance? BorrowingApplication { get; set; }
        }

        private string m_pkiRoot;

        private static readonly string[] s_alternateBaseAddresses
            = ["opc.tcp://192.168.1.100:51000"];

        private static readonly string[] s_baseAddresses
            = ["opc.tcp://localhost:12345/Configuration"];

        private static readonly string[] s_baseAddressesArray
            = ["opc.tcp://localhost:12345/Configuration"];
    }
}
