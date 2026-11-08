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

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;

namespace Opc.Ua
{
    /// <summary>
    /// Loads the configuration section for an application.
    /// </summary>
    public class ApplicationConfigurationSection
    {
        /// <summary>
        /// Creates the configuration object from the configuration section.
        /// </summary>
        /// <param name="parent">The parent object.</param>
        /// <param name="configContext">The configuration context object.</param>
        /// <param name="section">The section as XML node.</param>
        /// <returns>The created section handler object.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="section"/> is <c>null</c>.</exception>
        public object Create(object parent, object configContext, XmlNode section)
        {
            if (section == null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            XmlNode? element = section.FirstChild;

            while (element != null && typeof(XmlElement) != element.GetType())
            {
                element = element.NextSibling;
            }

            using var parser = new XmlParser(
                typeof(ConfigurationLocation),
                element!.OuterXml,
                ServiceMessageContext.CreateEmpty(null!));
            return new ConfigurationLocation { FilePath = parser.ReadString("FilePath") };
        }
    }

    /// <summary>
    /// Represents the location of a configuration file.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ConfigurationLocation
    {
        /// <summary>
        /// Gets or sets the relative or absolute path to the configuration file.
        /// </summary>
        /// <value>The file path.</value>
        [DataMember(IsRequired = true, Order = 0)]
        public string? FilePath { get; set; }
    }

    /// <summary>
    /// Stores the configurable configuration information for a UA application.
    /// </summary>
    public partial class ApplicationConfiguration
    {
        /// <summary>
        /// Gets the file that was used to load the configuration.
        /// </summary>
        /// <value>The source file path.</value>
        public string? SourceFilePath { get; private set; }

        /// <summary>
        /// Gets or sets the certificate manager that owns this application's
        /// certificates and trust lists.
        /// </summary>
        /// <remarks>
        /// Populated by <c>ApplicationInstance</c> during
        /// <c>CheckApplicationInstanceCertificatesAsync</c>. The
        /// <c>ICertificateManager</c> aggregates registry, trust-list,
        /// validation, lifecycle, and trust-list-file capabilities; use it
        /// directly for validation, lifecycle, and trust-list operations.
        /// Directly assigned concrete managers, including replacements, are reference counted
        /// across attached <c>ApplicationInstance</c> objects and configuration template copies.
        /// The last application releases the manager. Dependency-injection providers borrow
        /// injected managers from their container, which remains their only disposer.
        /// A configuration alone does not extend a manager's lifetime. Attach template copies
        /// before the last owning application releases the manager; retired managers cannot be adopted again.
        /// A released configuration can be reused by assigning a fresh manager and attaching an application.
        /// Assignment history belonging to retired managers is pruned before the new ownership period begins.
        /// </remarks>
        public ICertificateManager CertificateManager
        {
            get
            {
                lock (m_certificateManagerLock)
                {
                    // Preserve the public signature while lazy initialization can still leave the backing field null.
                    return m_certificateManager!;
                }
            }
            set
            {
                lock (m_certificateManagerLock)
                {
                    lock (s_certificateManagerLifetimeLock)
                    {
                        if (value is CertificateManager manager)
                        {
                            CertificateManagerLifetime lifetime = GetCertificateManagerLifetime(manager);
                            lifetime.ThrowIfDisposing();
                            PruneRetiredCertificateManagers();
                            TrackCertificateManager(manager, lifetime);
                        }
                        else
                        {
                            // Custom managers are never owned, but reuse still clears retired concrete history.
                            PruneRetiredCertificateManagers();
                        }
                        m_certificateManagerLeaseReleased = false;
                    }
                    m_certificateManager = value;
                }
            }
        }

        /// <summary>
        /// Captures admission to the current ownership period without invoking manager factories or callbacks.
        /// </summary>
        internal int CaptureCertificateManagerGeneration()
        {
            lock (m_certificateManagerLock)
            {
                if (m_certificateManagerLeaseReleased)
                {
                    throw new ObjectDisposedException(nameof(ApplicationConfiguration));
                }
                return m_certificateManagerGeneration;
            }
        }

        /// <summary>
        /// Checks manager lifetime before validation opens stores and captures this ownership period.
        /// </summary>
        /// <param name="manager">The manager owned by the captured generation.</param>
        private int CaptureValidationCertificateManagerGeneration(out ICertificateManager? manager)
        {
            lock (m_certificateManagerLock)
            {
                if (m_certificateManager is CertificateManager concreteManager)
                {
                    lock (s_certificateManagerLifetimeLock)
                    {
                        GetCertificateManagerLifetime(concreteManager).ThrowIfDisposing();
                    }
                }
                if (m_certificateManagerLeaseReleased)
                {
                    throw new ObjectDisposedException(nameof(ApplicationConfiguration));
                }
                manager = m_certificateManager;
                return m_certificateManagerGeneration;
            }
        }

        /// <summary>
        /// Rejects a retired validation without substituting the current manager for its captured identity.
        /// </summary>
        /// <param name="generation">The ownership generation captured before validation.</param>
        /// <param name="manager">The manager captured or initialized for that generation.</param>
        private void RequireValidationCertificateManagerGeneration(int generation, ICertificateManager? manager)
        {
            lock (m_certificateManagerLock)
            {
                if (manager is CertificateManager concreteManager)
                {
                    lock (s_certificateManagerLifetimeLock)
                    {
                        GetCertificateManagerLifetime(concreteManager).ThrowIfDisposing();
                    }
                }
                if (m_certificateManagerLeaseReleased || generation != m_certificateManagerGeneration)
                {
                    throw new ObjectDisposedException(nameof(ApplicationConfiguration));
                }
            }
        }

        /// <summary>
        /// Initializes a missing manager once per ownership period without overwriting replacements.
        /// </summary>
        /// <remarks>
        /// Callers from an ownership period that ends during creation receive <see cref="ObjectDisposedException"/>.
        /// Applications attached after that release start or join initialization for the new period.
        /// </remarks>
        internal async ValueTask<ICertificateManager> GetOrCreateCertificateManagerAsync(
            ITelemetryContext telemetry,
            CertificateManager? fallback = null,
            bool validationCreated = false,
            int? expectedGeneration = null)
        {
            TaskCompletionSource<ICertificateManager>? completion = null;
            Task<ICertificateManager> initialization;
            int generation;
            lock (m_certificateManagerLock)
            {
                generation = m_certificateManagerGeneration;
                if (expectedGeneration.HasValue && expectedGeneration.Value != generation)
                {
                    throw new ObjectDisposedException(nameof(ApplicationConfiguration));
                }
                if (m_certificateManager != null)
                {
                    if (m_certificateManager is CertificateManager manager)
                    {
                        lock (s_certificateManagerLifetimeLock)
                        {
                            GetCertificateManagerLifetime(manager).ThrowIfDisposing();
                        }
                    }
                    return m_certificateManager;
                }
                if (m_certificateManagerLeaseReleased)
                {
                    throw new ObjectDisposedException(nameof(ApplicationConfiguration));
                }
                if (m_certificateManagerInitialization == null ||
                    m_certificateManagerInitializationGeneration != generation)
                {
                    if (fallback != null)
                    {
                        CertificateManager = fallback;
                        return fallback;
                    }
                    completion = new TaskCompletionSource<ICertificateManager>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    m_certificateManagerInitialization = completion.Task;
                    m_certificateManagerInitializationGeneration = generation;
                }
                initialization = m_certificateManagerInitialization;
            }
            if (completion != null)
            {
                try
                {
                    bool releasedDuringCreation;
                    ICertificateManager? selected = null;
                    CertificateManagerLifetime? createdLifetime = null;
                    CertificateManager? created = null;
                    try
                    {
                        created = CertificateManagerFactory.Create(SecurityConfiguration, telemetry);
                        lock (m_certificateManagerLock)
                        {
                            releasedDuringCreation = m_certificateManagerLeaseReleased ||
                                generation != m_certificateManagerGeneration;
                            // The factory runs outside the lock, so another caller may have published a manager.
                            ICertificateManager? current = Volatile.Read(ref m_certificateManager);
                            if (!releasedDuringCreation && current == null)
                            {
                                lock (s_certificateManagerLifetimeLock)
                                {
                                    createdLifetime = GetCertificateManagerLifetime(created);
                                    TrackCertificateManager(created, createdLifetime);
                                    if (validationCreated)
                                    {
                                        m_validationCreatedCertificateManagers.Add(created);
                                    }
                                }
                                selected = created;
                                m_certificateManager = selected;
                                created = null; // ownership transferred to the configuration leases
                            }
                            else if (!releasedDuringCreation)
                            {
                                // The setter already tracked a manager assigned during creation, so the unpublished
                                // manager has no consumers and is disposed below instead of joining the leases.
                                selected = current;
                            }
                        }
                    }
                    finally
                    {
                        // Disposes a manager that was never published, including when publication throws.
                        if (created != null)
                        {
                            await created.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    CertificateManagerLifetime? releasedCreation = null;
                    bool retiredBeforePublication;
                    lock (m_certificateManagerLock)
                    {
                        retiredBeforePublication = releasedDuringCreation || m_certificateManagerLeaseReleased ||
                            generation != m_certificateManagerGeneration;
                        selected = Volatile.Read(ref m_certificateManager) ?? selected;
                        lock (s_certificateManagerLifetimeLock)
                        {
                            // A release between publication and this check owns disposal of the created manager.
                            if (createdLifetime != null && createdLifetime.Disposing)
                            {
                                releasedCreation = createdLifetime;
                                retiredBeforePublication = true;
                            }
                            if (selected is CertificateManager current &&
                                GetCertificateManagerLifetime(current).Disposing)
                            {
                                retiredBeforePublication = true;
                            }
                        }
                    }
                    if (releasedCreation != null)
                    {
                        try
                        {
                            await releasedCreation.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            // The releasing application reports the disposal failure; callers only observe retirement.
                            throw new ObjectDisposedException(
                                "The certificate manager ownership period ended during initialization.", e);
                        }
                    }
                    if (retiredBeforePublication || selected == null)
                    {
                        throw new ObjectDisposedException(nameof(ApplicationConfiguration));
                    }
                    completion.SetResult(selected);
                }
                catch (Exception e)
                {
                    completion.SetException(e);
                }
                finally
                {
                    lock (m_certificateManagerLock)
                    {
                        if (ReferenceEquals(m_certificateManagerInitialization, completion.Task))
                        {
                            m_certificateManagerInitialization = null;
                        }
                    }
                }
            }
            return await initialization.ConfigureAwait(false);
        }

        /// <summary>
        /// Records one assignment and acquires references for the applications already attached to this configuration.
        /// </summary>
        private void TrackCertificateManager(CertificateManager manager, CertificateManagerLifetime lifetime)
        {
            if (m_certificateManagers.Add(manager))
            {
                lifetime.References += m_certificateManagerUsers;
            }
        }

        /// <summary>
        /// Removes retired ownership history only when no application still holds this configuration.
        /// </summary>
        private void PruneRetiredCertificateManagers()
        {
            if (m_certificateManagerUsers == 0)
            {
                m_certificateManagers.RemoveWhere(
                    static manager => GetCertificateManagerLifetime(manager).Disposing);
                m_validationCreatedCertificateManagers.RemoveWhere(
                    static manager => GetCertificateManagerLifetime(manager).Disposing);
            }
        }

        /// <summary>
        /// Records container ownership before publishing an injected manager to application consumers.
        /// </summary>
        internal void SetBorrowedCertificateManager(ICertificateManager certificateManager)
        {
            if (certificateManager is null)
            {
                throw new ArgumentNullException(nameof(certificateManager));
            }
            lock (m_certificateManagerLock)
            {
                if (certificateManager is CertificateManager manager)
                {
                    lock (s_certificateManagerLifetimeLock)
                    {
                        CertificateManagerLifetime lifetime = GetCertificateManagerLifetime(manager);
                        lifetime.ThrowIfDisposing();
                        lifetime.Borrowed = true;
                    }
                }
                CertificateManager = certificateManager;
            }
        }

        /// <summary>
        /// Acquires one application reference to every assigned manager, including replacements and later assignments.
        /// </summary>
        internal void AcquireCertificateManagers()
        {
            lock (m_certificateManagerLock)
            {
                lock (s_certificateManagerLifetimeLock)
                {
                    if (m_certificateManager is CertificateManager current)
                    {
                        GetCertificateManagerLifetime(current).ThrowIfDisposing();
                    }
                    PruneRetiredCertificateManagers();
                    foreach (CertificateManager manager in m_certificateManagers)
                    {
                        GetCertificateManagerLifetime(manager).ThrowIfDisposing();
                    }
                    foreach (CertificateManager manager in m_certificateManagers)
                    {
                        GetCertificateManagerLifetime(manager).References++;
                    }
                    m_certificateManagerUsers++;
                    m_certificateManagerLeaseReleased = false;
                }
            }
        }

        /// <summary>
        /// Releases one application's references and awaits disposal of managers with no remaining application.
        /// </summary>
        internal async ValueTask ReleaseCertificateManagersAsync()
        {
            var disposing = new List<CertificateManagerLifetime>();
            lock (m_certificateManagerLock)
            {
                lock (s_certificateManagerLifetimeLock)
                {
                    if (m_certificateManagerUsers == 0)
                    {
                        throw new InvalidOperationException("The configuration has no certificate-manager lease.");
                    }
                    m_certificateManagerUsers--;
                    m_certificateManagerLeaseReleased = m_certificateManagerUsers == 0;
                    if (m_certificateManagerLeaseReleased)
                    {
                        m_certificateManagerGeneration++;
                    }
                    foreach (CertificateManager manager in m_certificateManagers)
                    {
                        CertificateManagerLifetime lifetime = GetCertificateManagerLifetime(manager);
                        lifetime.References--;
                        if (lifetime.References == 0 && !lifetime.Borrowed)
                        {
                            lifetime.Disposing = true;
                            disposing.Add(lifetime);
                        }
                    }
                }
            }
            await DisposeCertificateManagerLifetimesAsync(disposing).ConfigureAwait(false);
        }

        /// <summary>
        /// Disposes every retired manager even if an earlier disposal fails.
        /// </summary>
        private static async ValueTask DisposeCertificateManagerLifetimesAsync(
            List<CertificateManagerLifetime> disposing)
        {
            List<Exception>? failures = null;
            foreach (CertificateManagerLifetime lifetime in disposing)
            {
                try
                {
                    await lifetime.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    failures ??= [];
                    failures.Add(e);
                }
            }
            if (failures != null)
            {
                throw new AggregateException(failures);
            }
        }

        /// <summary>
        /// Determines whether a manager was supplied by a dependency-injection container.
        /// </summary>
        internal bool IsCertificateManagerBorrowed(CertificateManager certificateManager)
        {
            lock (m_certificateManagerLock)
            {
                lock (s_certificateManagerLifetimeLock)
                {
                    return GetCertificateManagerLifetime(certificateManager).Borrowed;
                }
            }
        }

        /// <summary>
        /// Resolves shared ownership by manager identity, independently of configuration identity.
        /// </summary>
        private static CertificateManagerLifetime GetCertificateManagerLifetime(CertificateManager manager)
        {
            return s_certificateManagerLifetimes.GetValue(
                manager, static value => new CertificateManagerLifetime(value));
        }

        /// <summary>
        /// Returns the domain names which the server is configured to use.
        /// </summary>
        /// <remarks>
        /// TLS endpoints retain the configured DNS name as well as the normalized host name
        /// so application certificates cover the name used for the TLS connection.
        /// </remarks>
        /// <returns>A list of domain names.</returns>
        public ArrayOf<string> GetServerDomainNames()
        {
            var baseAddresses = new List<string>();

            if (ServerConfiguration != null)
            {
                if (!ServerConfiguration.BaseAddresses.IsEmpty)
                {
                    baseAddresses.AddRange(ServerConfiguration.BaseAddresses);
                }

                if (!ServerConfiguration.AlternateBaseAddresses.IsEmpty)
                {
                    baseAddresses.AddRange(ServerConfiguration.AlternateBaseAddresses);
                }
            }

            if (DiscoveryServerConfiguration != null)
            {
                if (!DiscoveryServerConfiguration.BaseAddresses.IsEmpty)
                {
                    baseAddresses.AddRange(DiscoveryServerConfiguration.BaseAddresses);
                }

                if (!DiscoveryServerConfiguration.AlternateBaseAddresses.IsEmpty)
                {
                    baseAddresses.AddRange(DiscoveryServerConfiguration.AlternateBaseAddresses);
                }
            }

            var domainNames = new List<string>();
            for (int ii = 0; ii < baseAddresses.Count; ii++)
            {
                Uri? url = Utils.ParseUri(baseAddresses[ii]);

                if (url == null)
                {
                    continue;
                }

                string domainName = url.IdnHost;

                if (url.HostNameType == UriHostNameType.Dns)
                {
                    domainName = Utils.ReplaceLocalhost(domainName);
                }
                else // IPv4/IPv6 address
                {
                    domainName = Utils.NormalizedIPAddress(domainName);
                }

                if (!Utils.FindStringIgnoreCase(domainNames, domainName))
                {
                    domainNames.Add(domainName);
                }

                // TLS validates the literal endpoint name, not the hostname substituted for localhost.
                if ((Utils.IsUriHttpsScheme(url.AbsoluteUri) || Utils.IsUriWssScheme(url.AbsoluteUri)) &&
                    url.HostNameType == UriHostNameType.Dns &&
                    !Utils.FindStringIgnoreCase(domainNames, url.IdnHost))
                {
                    domainNames.Add(url.IdnHost);
                }
            }

            return domainNames;
        }

        /// <summary>
        /// Creates the message context from the configuration.
        /// </summary>
        /// <param name="clonedFactory">This parameter is obsolete and ignored. A new factory instance is always created.</param>
        /// <returns>A new instance of a ServiceMessageContext object.</returns>
        [Obsolete("Use CreateMessageContext() without parameters or CreateMessageContext(IEncodeableFactory) instead.")]
        public ServiceMessageContext CreateMessageContext(bool clonedFactory)
        {
            return CreateMessageContext(null);
        }

        /// <summary>
        /// Creates the message context from the configuration.
        /// </summary>
        /// <returns>A new instance of a ServiceMessageContext object with a new encodeable factory.</returns>
        public ServiceMessageContext CreateMessageContext()
        {
            return CreateMessageContext(null);
        }

        /// <summary>
        /// Creates the message context from the configuration with a private encodeable factory.
        /// </summary>
        /// <param name="factory">The private encodeable factory to use. If null, a new factory will be created.</param>
        /// <returns>A new instance of a ServiceMessageContext object.</returns>
        public ServiceMessageContext CreateMessageContext(IEncodeableFactory? factory)
        {
            var messageContext = new ServiceMessageContext(
                m_telemetry,
                factory ?? EncodeableFactory.Create());

            if (TransportQuotas != null)
            {
                messageContext.MaxArrayLength = TransportQuotas.MaxArrayLength;
                messageContext.MaxByteStringLength = TransportQuotas.MaxByteStringLength;
                messageContext.MaxStringLength = TransportQuotas.MaxStringLength;
                messageContext.MaxMessageSize = TransportQuotas.MaxMessageSize;
                messageContext.MaxEncodingNestingLevels = TransportQuotas.MaxEncodingNestingLevels;
                messageContext.MaxDecoderRecoveries = TransportQuotas.MaxDecoderRecoveries;
            }

            return messageContext;
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="sectionName">Name of configuration section for the current application's
        /// default configuration containing <see cref="ConfigurationLocation"/>.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <returns>Application configuration</returns>
        [Obsolete("Use LoadAsync instead.")]
        public static Task<ApplicationConfiguration> Load(
            string sectionName,
            ApplicationType applicationType)
        {
            return LoadAsync(sectionName, applicationType, LoggerUtils.Null.Logger, null);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="sectionName">Name of configuration section for the current application's
        /// default configuration containing <see cref="ConfigurationLocation"/>.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="logger">A contextual logger to log to</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="ct"></param>
        /// <returns>Application configuration</returns>
        public static Task<ApplicationConfiguration> LoadAsync(
            string sectionName,
            ApplicationType applicationType,
            ILogger logger,
            ITelemetryContext? telemetry,
            CancellationToken ct = default)
        {
            return LoadAsync(
                sectionName,
                applicationType,
                typeof(ApplicationConfiguration),
                logger,
                telemetry,
                ct);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="sectionName">Name of configuration section for the current application's
        /// default configuration containing <see cref="ConfigurationLocation"/>.</param>
        /// <param name="applicationType">A description for the ApplicationType DataType.</param>
        /// <param name="systemType">A user type of the configuration instance.</param>
        /// <returns>Application configuration</returns>
        [Obsolete("Use LoadAsync instead.")]
        public static Task<ApplicationConfiguration> Load(
            string sectionName,
            ApplicationType applicationType,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type systemType)
        {
            return LoadAsync(sectionName, applicationType, systemType, LoggerUtils.Null.Logger, null);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="sectionName">Name of configuration section for the current application's
        /// default configuration containing <see cref="ConfigurationLocation"/>.</param>
        /// <param name="applicationType">A description for the ApplicationType DataType.</param>
        /// <param name="systemType">A user type of the configuration instance.</param>
        /// <param name="logger">A contextual logger to log to</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="ct"></param>
        /// <returns>Application configuration</returns>
        /// <exception cref="ServiceResultException"></exception>
        public static Task<ApplicationConfiguration> LoadAsync(
            string sectionName,
            ApplicationType applicationType,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type systemType,
            ILogger logger,
            ITelemetryContext? telemetry,
            CancellationToken ct = default)
        {
            string filePath = GetFilePathFromAppConfig(sectionName, logger);

            var file = new FileInfo(filePath);

            if (!file.Exists)
            {
                throw ServiceResultException.ConfigurationError(
                    "Configuration file does not exist: {0}\nCurrent directory is: {1}",
                    filePath,
                    Directory.GetCurrentDirectory());
            }

            return LoadAsync(file, applicationType, systemType, telemetry, ct);
        }

        /// <summary>
        /// Loads but does not validate the application configuration from a configuration section.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <returns>Application configuration</returns>
        /// <remarks>Use this method to ensure the configuration is not changed during loading.</remarks>
        /// <exception cref="ServiceResultException"></exception>
        public static ApplicationConfiguration LoadWithNoValidation(
            FileInfo file,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? systemType,
            ITelemetryContext? telemetry)
        {
            systemType ??= typeof(ApplicationConfiguration);

            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
            try
            {
                using IDisposable scope = AmbientMessageContext.SetScopedContext(telemetry!);
                IServiceMessageContext context = AmbientMessageContext.CurrentContext ??
                    ServiceMessageContext.CreateEmpty(telemetry!);
                using var parser = new XmlParser(typeof(ApplicationConfiguration), stream, context);
                ApplicationConfiguration configuration;
                if (systemType == typeof(ApplicationConfiguration))
                {
                    configuration = new ApplicationConfiguration(telemetry!);
                }
                else
                {
                    configuration = (ApplicationConfiguration)Activator.CreateInstance(systemType, [telemetry])!;
                }

                configuration.Decode(parser);
                configuration.ServerConfiguration?.ValidateSecurityPolicies();
                configuration.DiscoveryServerConfiguration?.ValidateSecurityPolicies();
                configuration.SourceFilePath = file.FullName;
                return configuration;
            }
            catch (Exception e)
            {
                throw ServiceResultException.ConfigurationError(
                    e,
                    "Configuration file could not be loaded: {0}\nError: {1}",
                    file.FullName,
                    e.Message);
            }
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <returns>Application configuration</returns>
        [Obsolete("Use LoadAsync instead.")]
        public static Task<ApplicationConfiguration> Load(
            FileInfo file,
            ApplicationType applicationType,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? systemType)
        {
            return LoadAsync(file, applicationType, systemType, null);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="ct"></param>
        /// <returns>Application configuration</returns>
        public static Task<ApplicationConfiguration> LoadAsync(
            FileInfo file,
            ApplicationType applicationType,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? systemType,
            ITelemetryContext? telemetry,
            CancellationToken ct = default)
        {
            return LoadAsync(file, applicationType, systemType, true, telemetry, ct: ct);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <param name="applyTraceSettings">Retained for backward compatibility; ignored.</param>
        /// <param name="certificatePasswordProvider">The certificate password provider.</param>
        /// <returns>Application configuration</returns>
        [Obsolete("Use LoadAsync instead.")]
        public static Task<ApplicationConfiguration> Load(
            FileInfo file,
            ApplicationType applicationType,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? systemType,
            bool applyTraceSettings,
            ICertificatePasswordProvider? certificatePasswordProvider = null)
        {
            return LoadAsync(
                file,
                applicationType,
                systemType,
                applyTraceSettings,
                null,
                certificatePasswordProvider);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <param name="applyTraceSettings">Retained for backward compatibility; ignored.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="certificatePasswordProvider">The certificate password provider.</param>
        /// <param name="ct">Cancellation token to cancel action</param>
        /// <returns>Application configuration</returns>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<ApplicationConfiguration> LoadAsync(
            FileInfo file,
            ApplicationType applicationType,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? systemType,
            bool applyTraceSettings,
            ITelemetryContext? telemetry,
            ICertificatePasswordProvider? certificatePasswordProvider = null,
            CancellationToken ct = default)
        {
            ApplicationConfiguration configuration;

            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
                configuration = await LoadAsync(
                    stream,
                    applicationType,
                    systemType,
                    applyTraceSettings,
                    telemetry,
                    certificatePasswordProvider,
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw ServiceResultException.ConfigurationError(
                    e,
                    "Configuration file could not be loaded: {0}\nError is: {1}",
                    file.FullName,
                    e.Message);
            }

            configuration.SourceFilePath = file.FullName;

            return configuration;
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <param name="applyTraceSettings">Retained for backward compatibility; ignored.</param>
        /// <param name="certificatePasswordProvider">The certificate password provider.</param>
        /// <returns>Application configuration</returns>
        [Obsolete("Use LoadAsync instead.")]
        public static Task<ApplicationConfiguration> Load(
            Stream stream,
            ApplicationType applicationType,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? systemType,
            bool applyTraceSettings,
            ICertificatePasswordProvider? certificatePasswordProvider = null)
        {
            return LoadAsync(
                stream,
                applicationType,
                systemType,
                applyTraceSettings,
                null,
                certificatePasswordProvider);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <param name="applyTraceSettings">Retained for backward compatibility; ignored.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="certificatePasswordProvider">The certificate password provider.</param>
        /// <param name="ct">Cancellation token to cancel action</param>
        /// <returns>Application configuration</returns>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<ApplicationConfiguration> LoadAsync(
            Stream stream,
            ApplicationType applicationType,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? systemType,
            bool applyTraceSettings,
            ITelemetryContext? telemetry,
            ICertificatePasswordProvider? certificatePasswordProvider = null,
            CancellationToken ct = default)
        {
            // Retained for binary/source compatibility with 1.5.378 callers.
            _ = applyTraceSettings;
            systemType ??= typeof(ApplicationConfiguration);

            ApplicationConfiguration? configuration = null;
            try
            {
                using IDisposable scope = AmbientMessageContext.SetScopedContext(telemetry!);
                IServiceMessageContext ctx = AmbientMessageContext.CurrentContext ??
                    ServiceMessageContext.CreateEmpty(telemetry!);
                using var parser = new XmlParser(typeof(ApplicationConfiguration), stream, ctx);
                if (systemType == typeof(ApplicationConfiguration))
                {
                    configuration = new ApplicationConfiguration(telemetry!);
                }
                else
                {
                    configuration = (ApplicationConfiguration)Activator.CreateInstance(systemType, [telemetry])!;
                }
                configuration.Decode(parser);
                configuration.ServerConfiguration?.ValidateSecurityPolicies();
                configuration.DiscoveryServerConfiguration?.ValidateSecurityPolicies();
            }
            catch (OperationCanceledException e)
            {
                if (configuration != null)
                {
                    await configuration.DisposeCertificateManagersOnLoadFailureAsync(e).ConfigureAwait(false);
                }
                throw;
            }
            catch (Exception e)
            {
                if (configuration != null)
                {
                    await configuration.DisposeCertificateManagersOnLoadFailureAsync(e).ConfigureAwait(false);
                }
                throw ServiceResultException.ConfigurationError(
                    e,
                    "Configuration could not be loaded.\nError is: {0}",
                    e.Message);
            }

            try
            {
                if (certificatePasswordProvider != null)
                {
                    configuration.SecurityConfiguration.CertificatePasswordProvider = certificatePasswordProvider;
                }
                await configuration.ValidateAsync(applicationType, ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await configuration.DisposeCertificateManagersOnLoadFailureAsync(e).ConfigureAwait(false);
                throw;
            }

            return configuration;
        }

        /// <summary>
        /// Cleans up managers created by base validation, preserving externally assigned replacements and load errors.
        /// </summary>
        private async ValueTask DisposeCertificateManagersOnLoadFailureAsync(Exception failure)
        {
            List<Exception>? failures = null;
            var disposing = new List<CertificateManagerLifetime>();
            lock (m_certificateManagerLock)
            {
                lock (s_certificateManagerLifetimeLock)
                {
                    foreach (CertificateManager manager in m_validationCreatedCertificateManagers)
                    {
                        CertificateManagerLifetime lifetime = GetCertificateManagerLifetime(manager);
                        if (lifetime.References == 0 && !lifetime.Borrowed)
                        {
                            lifetime.Disposing = true;
                            disposing.Add(lifetime);
                        }
                    }
                }
            }
            foreach (CertificateManagerLifetime lifetime in disposing)
            {
                try
                {
                    await lifetime.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    failures ??= [failure];
                    failures.Add(e);
                }
            }
            if (failures != null)
            {
                throw new AggregateException(failures);
            }
        }

        /// <summary>
        /// Reads the file path from the application configuration file.
        /// </summary>
        /// <param name="sectionName">Name of configuration section for the current application's default configuration containing <see cref="ConfigurationLocation"/>.
        /// </param>
        /// <param name="logger">A contextual logger to log to</param>
        /// <returns>File path from the application configuration file.</returns>
        public static string GetFilePathFromAppConfig(string sectionName, ILogger logger)
        {
            // convert to absolute file path (expands environment strings).
            try
            {
                string? absolutePath = Utils.GetAbsoluteFilePath(
                    sectionName + ".Config.xml",
                    checkCurrentDirectory: true,
                    createAlways: false);
                return absolutePath ?? $"{sectionName}.Config.xml";
            }
            catch (Exception e)
            {
                logger.AppConfigLog0(e, sectionName);
                return $"{sectionName}.Config.xml";
            }
        }

        /// <summary>
        /// Saves the configuration file.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        public void SaveToFile(string filePath)
        {
            using Stream ostrm = File.Open(filePath, FileMode.Create, FileAccess.ReadWrite);
            using IDisposable scope = AmbientMessageContext.SetScopedContext(m_telemetry);
            IServiceMessageContext context = AmbientMessageContext.CurrentContext
                ?? ServiceMessageContext.CreateEmpty(m_telemetry);
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            settings.CloseOutput = true;
            using var writer = XmlWriter.Create(ostrm, settings);
            using var encoder = new XmlEncoder(typeof(ApplicationConfiguration), writer, context);
            Encode(encoder);
            encoder.Close();
        }

        /// <summary>
        /// Ensures that the application configuration is valid.
        /// </summary>
        /// <param name="applicationType">Type of the application.</param>
        [Obsolete("Use ValidateAsync instead.")]
        public virtual Task Validate(ApplicationType applicationType)
        {
            return ValidateAsync(applicationType);
        }

        /// <summary>
        /// Ensures that the application configuration is valid.
        /// </summary>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="ct">Cancellation token to cancel action</param>
        /// <exception cref="ServiceResultException"></exception>
        public virtual async Task ValidateAsync(
            ApplicationType applicationType,
            CancellationToken ct = default)
        {
            int certificateManagerGeneration =
                CaptureValidationCertificateManagerGeneration(out ICertificateManager? validationManager);
            if (string.IsNullOrEmpty(ApplicationName))
            {
                throw ServiceResultException.ConfigurationError(
                    "ApplicationName must be specified.");
            }

            if (SecurityConfiguration == null)
            {
                throw ServiceResultException.ConfigurationError(
                    "SecurityConfiguration must be specified.");
            }

            try
            {
                SecurityConfiguration.Validate(m_telemetry, validationManager as ICertificateStoreResolver);
            }
            catch (ServiceResultException)
            {
                RequireValidationCertificateManagerGeneration(certificateManagerGeneration, validationManager);
                throw;
            }
            RequireValidationCertificateManagerGeneration(certificateManagerGeneration, validationManager);

            string GenerateDefaultUri()
            {
                var sb = new StringBuilder();
                sb.Append("urn:")
                    .Append(Utils.GetHostName())
                    .Append(':')
                    .Append(ApplicationName);
                return sb.ToString();
            }

            if (string.IsNullOrEmpty(ApplicationUri))
            {
                ApplicationUri = GenerateDefaultUri();
            }

            // Ensure the transport quotas are always available. The transport
            // and secure channel layers rely on them being non-null (e.g. during
            // server bring-up), so default them to sensible values when a
            // configuration was assembled manually without specifying them.
            TransportQuotas ??= new TransportQuotas();

            if (applicationType is ApplicationType.Client or ApplicationType.ClientAndServer)
            {
                if (ClientConfiguration == null)
                {
                    throw ServiceResultException.ConfigurationError(
                        "ClientConfiguration must be specified.");
                }

                ClientConfiguration.Validate();
            }

            if (applicationType is ApplicationType.Server or ApplicationType.ClientAndServer)
            {
                if (ServerConfiguration == null)
                {
                    throw ServiceResultException.ConfigurationError(
                        "ServerConfiguration must be specified.");
                }

                ServerConfiguration.Validate();
            }

            if (applicationType == ApplicationType.DiscoveryServer)
            {
                if (DiscoveryServerConfiguration == null)
                {
                    throw ServiceResultException.ConfigurationError(
                        "DiscoveryServerConfiguration must be specified.");
                }

                DiscoveryServerConfiguration.Validate();
            }

            // toggle the state of the hi-res clock.
#pragma warning disable CS0618 // Type / member is obsolete.
            HiResClock.Disabled = DisableHiResClock;

            if (HiResClock.Disabled &&
                ServerConfiguration != null &&
                ServerConfiguration.PublishingResolution < 50)
            {
                ServerConfiguration.PublishingResolution = 50;
            }
#pragma warning restore CS0618

            // Eagerly create a CertificateManager from the security
            // configuration so consumers (Session, ClientChannelManager,
            // server bring-up) can rely on it being non-null even when
            // the configuration was not built through ApplicationInstance.
            // Attached applications share ownership, including assignments made after attachment.
            ICertificateManager initializedManager = await GetOrCreateCertificateManagerAsync(
                m_telemetry, validationCreated: true, expectedGeneration: certificateManagerGeneration)
                .ConfigureAwait(false);
            RequireValidationCertificateManagerGeneration(certificateManagerGeneration, initializedManager);

            // Eagerly load configured application certificates into the
            // manager registry so consumers (Session, validator, etc) can
            // resolve them via ICertificateRegistry without depending on
            // the legacy CertificateIdentifier cache.
            if (initializedManager is CertificateManager certManager)
            {
                await certManager
                    .LoadApplicationCertificatesAsync(SecurityConfiguration, ApplicationUri, ct)
                    .ConfigureAwait(false);
            }
            RequireValidationCertificateManagerGeneration(certificateManagerGeneration, initializedManager);
        }

        /// <summary>
        /// Loads the endpoints cached on disk.
        /// </summary>
        /// <param name="createAlways">if set to <c>true</c> ConfiguredEndpointCollection is always returned,
        ///	even if loading from disk fails</param>
        /// <returns>Collection of configured endpoints from the disk.</returns>
        public ConfiguredEndpointCollection LoadCachedEndpoints(bool createAlways)
        {
            return LoadCachedEndpoints(createAlways, false);
        }

        /// <summary>
        /// Loads the endpoints cached on disk.
        /// </summary>
        /// <param name="createAlways">if set to <c>true</c> ConfiguredEndpointCollection is always returned,
        /// even if loading from disk fails</param>
        /// <param name="overrideConfiguration">if set to <c>true</c> overrides the configuration.</param>
        /// <returns>
        /// Collection of configured endpoints from the disk.
        /// </returns>
        /// <exception cref="InvalidOperationException"></exception>
        public ConfiguredEndpointCollection LoadCachedEndpoints(
            bool createAlways,
            bool overrideConfiguration)
        {
            if (ClientConfiguration == null)
            {
                throw new InvalidOperationException("Only valid for client configurations.");
            }

            string? filePath;
            try
            {
                filePath = Utils.GetAbsoluteFilePath(
                    ClientConfiguration.EndpointCacheFilePath,
                    checkCurrentDirectory: true,
                    createAlways: false,
                    writable: false);
            }
            catch (Exception e)
            {
                if (m_logger.IsEnabled(LogLevel.Error))
                {
                    m_logger.AppConfigLog1(
                        e,
                        ClientConfiguration.EndpointCacheFilePath);
                }
                filePath = null;
            }

            if (filePath == null)
            {
                filePath = ClientConfiguration.EndpointCacheFilePath;

                if (!Utils.IsPathRooted(filePath))
                {
                    var sourceFile = new FileInfo(SourceFilePath!);
                    filePath = Utils.Format(
                        "{0}{1}{2}",
                        sourceFile.DirectoryName!,
                        Path.DirectorySeparatorChar,
                        filePath);
                }
            }

            if (!createAlways)
            {
                return ConfiguredEndpointCollection.Load(
                    this,
                    filePath,
                    overrideConfiguration,
                    m_telemetry);
            }

            var endpoints = new ConfiguredEndpointCollection(this);
            try
            {
                endpoints = ConfiguredEndpointCollection.Load(
                    this,
                    filePath,
                    overrideConfiguration,
                    m_telemetry);
            }
            catch (Exception e)
            {
                m_logger.AppConfigLog2(e, filePath);
            }
            finally
            {
                try
                {
                    string localFilePath = Utils.GetAbsoluteFilePath(
                        ClientConfiguration.EndpointCacheFilePath,
                        checkCurrentDirectory: true,
                        createAlways: true,
                        writable: true);
                    if (localFilePath != filePath)
                    {
                        endpoints.Save(localFilePath);
                    }
                }
                catch (Exception e2)
                {
                    if (m_logger.IsEnabled(LogLevel.Error))
                    {
                        m_logger.AppConfigLog3(
                            e2,
                            ClientConfiguration.EndpointCacheFilePath);
                    }
                }
            }
            return endpoints;
        }

        /// <summary>
        /// Looks for an extension with the specified type and uses the supplied decoder function to parse it.
        /// </summary>
        /// <typeparam name="T">The type of extension.</typeparam>
        /// <param name="elementName">Name of the element (required).</param>
        /// <param name="decoderFunc">A function that reads the value from an <see cref="IDecoder"/>.</param>
        /// <returns>The extension if found. Default otherwise.</returns>
        public T? ParseExtension<T>(XmlQualifiedName elementName, Func<IDecoder, T> decoderFunc)
        {
            return Utils.ParseExtension(m_extensions, elementName, m_telemetry, decoderFunc);
        }

        /// <summary>
        /// Updates the extension using the supplied encoder function.
        /// </summary>
        /// <typeparam name="T">The type of extension.</typeparam>
        /// <param name="elementName">Name of the element (required).</param>
        /// <param name="value">The value.</param>
        /// <param name="encoderFunc">A function that writes the value to an <see cref="IEncoder"/>.</param>
        public void UpdateExtension<T>(XmlQualifiedName elementName, T value, Action<IEncoder, T> encoderFunc)
        {
            Utils.UpdateExtension(ref m_extensions, elementName, value, m_telemetry, encoderFunc);
        }

        /// <summary>
        /// Looks for an extension with the specified IEncodeable type and decodes it.
        /// </summary>
        /// <typeparam name="T">The type of extension (must implement IEncodeable).</typeparam>
        /// <param name="elementName">Name of the element (null to derive from type).</param>
        /// <returns>The extension if found. Default otherwise.</returns>
        public T? ParseExtension<T>(XmlQualifiedName? elementName = null)
            where T : IEncodeable, new()
        {
            return Utils.ParseExtension<T>(m_extensions, elementName, m_telemetry);
        }

        /// <summary>
        /// Updates or adds an extension using the IEncodeable implementation.
        /// </summary>
        /// <typeparam name="T">The type of extension (must implement IEncodeable).</typeparam>
        /// <param name="elementName">Name of the element (null to derive from type).</param>
        /// <param name="value">The value to encode.</param>
        public void UpdateExtension<T>(XmlQualifiedName? elementName, T value)
            where T : IEncodeable
        {
            Utils.UpdateExtension(ref m_extensions, elementName, value, m_telemetry);
        }

        /// <summary>
        /// Protects certificate-manager assignment provenance.
        /// </summary>
        private readonly Lock m_certificateManagerLock = new();

        /// <summary>
        /// Records every concrete manager assigned to the configuration.
        /// </summary>
        private readonly HashSet<CertificateManager> m_certificateManagers = [];

        /// <summary>
        /// Records exact factory-created managers for cleanup when configuration loading fails.
        /// </summary>
        private readonly HashSet<CertificateManager> m_validationCreatedCertificateManagers = [];

        /// <summary>
        /// Counts applications attached to this configuration, including its assignment history.
        /// </summary>
        private int m_certificateManagerUsers;

        /// <summary>
        /// Prevents validation from creating an unowned manager after the last application releases
        /// an empty configuration.
        /// </summary>
        private bool m_certificateManagerLeaseReleased;

        /// <summary>
        /// Distinguishes pending initialization from later ownership periods after configuration reuse.
        /// </summary>
        private int m_certificateManagerGeneration;

        /// <summary>
        /// Serializes reference acquisition, release, and borrowed provenance across configuration copies.
        /// </summary>
        private static readonly Lock s_certificateManagerLifetimeLock = new();

        /// <summary>
        /// Shares manager lifetimes without keeping unreachable configurations or managers alive.
        /// </summary>
        private static readonly ConditionalWeakTable<CertificateManager, CertificateManagerLifetime>
            s_certificateManagerLifetimes = new();

        /// <summary>
        /// The currently configured certificate manager.
        /// </summary>
        private ICertificateManager? m_certificateManager;

        /// <summary>
        /// Shares pending initialization across application instances without holding ownership locks across callbacks.
        /// </summary>
        private Task<ICertificateManager>? m_certificateManagerInitialization;

        /// <summary>
        /// Prevents callers from a later ownership period joining initialization started before final release.
        /// </summary>
        private int m_certificateManagerInitializationGeneration;

        /// <summary>
        /// Retains one disposal result for a manager shared by multiple application configurations.
        /// </summary>
        private sealed class CertificateManagerLifetime
        {
            /// <summary>
            /// Creates an ownership record for one concrete manager identity.
            /// </summary>
            public CertificateManagerLifetime(CertificateManager manager)
            {
                m_manager = manager;
            }

            /// <summary>
            /// Gets or sets the number of attached application references.
            /// </summary>
            public int References { get; set; }

            /// <summary>
            /// Gets or sets whether the manager's disposer is its dependency-injection container.
            /// </summary>
            public bool Borrowed { get; set; }

            /// <summary>
            /// Gets or sets whether the final application release has retired this manager.
            /// </summary>
            public bool Disposing { get; set; }

            /// <summary>
            /// Rejects adoption after the final application release, including while disposal is pending.
            /// </summary>
            public void ThrowIfDisposing()
            {
                if (Disposing)
                {
                    throw new ObjectDisposedException(nameof(CertificateManager));
                }
            }

            /// <summary>
            /// Runs manager disposal once outside ownership locks and shares its result with cleanup callers.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                if (Interlocked.CompareExchange(ref m_disposalRunner, 1, 0) == 0)
                {
                    try
                    {
                        await m_manager.DisposeAsync().ConfigureAwait(false);
                        m_disposalCompletion.SetResult(true);
                    }
                    catch (Exception e)
                    {
                        m_disposalCompletion.SetException(e);
                    }
                }
                await m_disposalCompletion.Task.ConfigureAwait(false);
            }

            /// <summary>Shares the completed disposal or its failure.</summary>
            private readonly TaskCompletionSource<bool> m_disposalCompletion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Identifies the single caller that runs manager disposal.</summary>
            private int m_disposalRunner;

            /// <summary>The manager released after its last application reference.</summary>
            private readonly CertificateManager m_manager;
        }
    }

    /// <summary>
    /// Specifies the configuration for a server application.
    /// </summary>
    public partial class ServerBaseConfiguration
    {
        /// <summary>
        /// Validates the configuration.
        /// </summary>
        public virtual void Validate()
        {
            if (m_securityPolicies.Count == 0)
            {
                m_securityPolicies = m_securityPolicies.AddItem(new ServerSecurityPolicy());
            }
        }
    }

    /// <summary>
    /// Specifies the configuration for a server application.
    /// </summary>
    public partial class ServerConfiguration : ServerBaseConfiguration
    {
        /// <summary>
        /// Validates the configuration.
        /// </summary>
        public override void Validate()
        {
            base.Validate();

            if (m_userTokenPolicies.IsEmpty)
            {
                m_userTokenPolicies = [new UserTokenPolicy()];
            }
        }
    }

    /// <summary>
    /// The configuration for a client application.
    /// </summary>
    public partial class ClientConfiguration
    {
        /// <summary>
        /// Validates the configuration.
        /// </summary>
        public void Validate()
        {
            if (WellKnownDiscoveryUrls.IsEmpty)
            {
                WellKnownDiscoveryUrls = Utils.DiscoveryUrls;
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for ApplicationConfiguration.
    /// </summary>
    internal static partial class ApplicationConfigurationLog
    {
        [LoggerMessage(EventId = CoreEventIds.ApplicationConfiguration + 0, Level = LogLevel.Error,
            Message = "Could not get file path from app config - returning: {SectionName}.Config.xml")]
        public static partial void AppConfigLog0(
            this ILogger logger,
            Exception? exception,
            string sectionName);

        [LoggerMessage(EventId = CoreEventIds.ApplicationConfiguration + 1, Level = LogLevel.Error,
            Message = "Could not get file path {FilePath}")]
        public static partial void AppConfigLog1(
            this ILogger logger,
            Exception? exception,
            [FilePath] string? filePath);

        [LoggerMessage(EventId = CoreEventIds.ApplicationConfiguration + 2, Level = LogLevel.Error,
            Message = "Could not load configuration from file: {FilePath}")]
        public static partial void AppConfigLog2(
            this ILogger logger,
            Exception? exception,
            [FilePath] string? filePath);

        [LoggerMessage(EventId = CoreEventIds.ApplicationConfiguration + 3, Level = LogLevel.Error,
            Message = "Could not save configuration to file: {FilePath}")]
        public static partial void AppConfigLog3(
            this ILogger logger,
            Exception? exception,
            [FilePath] string? filePath);
    }
}
