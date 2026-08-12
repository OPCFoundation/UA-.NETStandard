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
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Reports which crypto providers are in use and whether any of them carry
    /// no validation.
    /// </summary>
    /// <remarks>
    /// Three surfaces are used, all of them ones the stack already has. Logs
    /// carry a warning per uncertified provider, following the pattern the
    /// deprecated security policies already use. Metrics expose the same facts
    /// for scraping. The caller decides whether to surface the result in the
    /// address space, since only a server has one.
    /// <para>
    /// Under <see cref="CryptoCompliancePolicy.Permissive"/> nothing is warned
    /// about, because that mode exists to leave existing deployments exactly as
    /// they were. The metrics are still published: they are pull based and cost
    /// nothing when nobody reads them, so the information remains available
    /// without changing behaviour.
    /// </para>
    /// </remarks>
    public sealed class CryptoProviderAuditor : IDisposable
    {
        /// <summary>
        /// The name of the meter carrying the crypto provider instruments.
        /// </summary>
        public const string MeterName = "Opc.Ua.Core";

        /// <summary>
        /// Initializes an auditor.
        /// </summary>
        /// <param name="registry">The registry to report on.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="policy">How strictly validation is enforced.</param>
        /// <param name="policies">
        /// The security policies the application offers, whose algorithms the
        /// providers must actually perform. Defaults to the built-in set.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="registry"/> or <paramref name="telemetry"/> is <c>null</c>.
        /// </exception>
        public CryptoProviderAuditor(
            ICryptoProviderRegistry registry,
            ITelemetryContext telemetry,
            CryptoCompliancePolicy policy = CryptoCompliancePolicy.Permissive,
            ISecurityPolicyRegistry? policies = null)
        {
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            m_logger = telemetry.CreateLogger<CryptoProviderAuditor>();
            m_policy = policy;
            m_policies = policies ?? SecurityPolicies.Default;
            m_meter = telemetry.CreateMeter();

            m_meter.CreateObservableGauge(
                "opc.ua.crypto.providers",
                ObserveProviders,
                description: "Crypto providers in use, tagged by name and validation level.");

            m_meter.CreateObservableGauge(
                "opc.ua.crypto.providers.uncertified",
                ObserveUncertified,
                description: "Number of crypto providers in use that carry no validation.");
        }

        /// <summary>
        /// The providers currently in use that carry no validation.
        /// </summary>
        public ArrayOf<ICryptoProvider> UncertifiedProviders
        {
            get
            {
                var uncertified = new List<ICryptoProvider>();
                foreach (ICryptoProvider provider in m_registry.Providers)
                {
                    if (!provider.Validation.IsAcceptableForFips)
                    {
                        uncertified.Add(provider);
                    }
                }
                return new ArrayOf<ICryptoProvider>(uncertified.ToArray());
            }
        }

        /// <summary>
        /// Writes the effective crypto configuration to the log.
        /// </summary>
        /// <returns>
        /// The providers that carry no validation, so a caller can surface them
        /// elsewhere or refuse to start.
        /// </returns>
        /// <remarks>
        /// Call this once the configuration is settled, typically at start up.
        /// </remarks>
        public ArrayOf<ICryptoProvider> Report()
        {
            ArrayOf<ICryptoProvider> uncertified = UncertifiedProviders;

            if (m_policy == CryptoCompliancePolicy.Permissive)
            {
                // Existing deployments are left exactly as they were.
                return uncertified;
            }

            if (m_logger.IsEnabled(LogLevel.Information))
            {
                foreach (ICryptoProvider provider in m_registry.Providers)
                {
                    m_logger.CryptoProviderInUse(
                        provider.Name,
                        provider.Validation.Level.ToString(),
                        provider.Validation.ModuleName ?? "unspecified");
                }
            }

            if (m_logger.IsEnabled(LogLevel.Warning))
            {
                foreach (ICryptoProvider provider in uncertified)
                {
                    m_logger.CryptoProviderUncertified(
                        provider.Name, provider.Validation.Level.ToString());
                }
            }

            return uncertified;
        }

        /// <summary>
        /// Throws when the policy forbids the providers currently in use.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The policy is <see cref="CryptoCompliancePolicy.FipsOnly"/> and at
        /// least one provider carries no validation.
        /// </exception>
        /// <remarks>
        /// Failing at start up is deliberate. A deployment that asked for
        /// validated cryptography and did not get it should not run and quietly
        /// use something else.
        /// </remarks>
        public void ThrowIfNotCompliant()
        {
            if (m_policy != CryptoCompliancePolicy.FipsOnly)
            {
                return;
            }

            ArrayOf<ICryptoProvider> uncertified = UncertifiedProviders;
            if (uncertified.Count > 0)
            {
                var names = new List<string>();
                foreach (ICryptoProvider provider in uncertified)
                {
                    names.Add(provider.Name);
                }

                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "The compliance policy requires validated cryptography but these " +
                    $"providers carry none: {string.Join(", ", names)}.");
            }

            // Carrying the facet is not enough. Supports(algorithm) is consulted
            // again at the point of use, and a provider that answers false for the
            // algorithm a negotiated policy needs is bypassed in favour of the
            // platform. Under this policy that would mean the validated module
            // performs only part of the work while a deployment believes it
            // performs all of it, so the shortfall is named and refused.
            ArrayOf<UnservedCryptoOperation> unserved =
                CryptoCompliance.GetUnservedOperations(m_registry, m_policies);

            if (unserved.Count > 0)
            {
                var operations = new List<string>();
                foreach (UnservedCryptoOperation operation in unserved)
                {
                    operations.Add(operation.ToString());
                }

                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "The compliance policy requires validated cryptography to perform every " +
                    "operation, but the provider resolved for these cannot perform " +
                    $"them and the platform would be used instead: {string.Join(", ", operations)}.");
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_meter.Dispose();
        }

        private IEnumerable<Measurement<int>> ObserveProviders()
        {
            // ArrayOf<T> enumerates over a span, which cannot cross a yield, so
            // the snapshot is materialised before the iterator starts.
            var providers = new List<ICryptoProvider>();
            foreach (ICryptoProvider provider in m_registry.Providers)
            {
                providers.Add(provider);
            }

            var measurements = new List<Measurement<int>>(providers.Count);
            foreach (ICryptoProvider provider in providers)
            {
                measurements.Add(new Measurement<int>(
                    1,
                    new KeyValuePair<string, object?>("opc.ua.crypto.provider", provider.Name),
                    new KeyValuePair<string, object?>(
                        "opc.ua.crypto.validation", provider.Validation.Level.ToString())));
            }

            return measurements;
        }

        private int ObserveUncertified()
        {
            return UncertifiedProviders.Count;
        }

        private readonly ICryptoProviderRegistry m_registry;
        private readonly ISecurityPolicyRegistry m_policies;
        private readonly ILogger m_logger;
        private readonly CryptoCompliancePolicy m_policy;
        private readonly Meter m_meter;
    }

    /// <summary>
    /// Log messages for the crypto provider model.
    /// </summary>
    internal static partial class CryptoProviderAuditorLog
    {
        [LoggerMessage(EventId = CoreEventIds.CryptoProvider + 0, Level = LogLevel.Information,
            Message = "Crypto provider {ProviderName} in use ({ValidationLevel}, module {ModuleName}).")]
        public static partial void CryptoProviderInUse(
            this ILogger logger,
            string providerName,
            string validationLevel,
            string moduleName);

        [LoggerMessage(EventId = CoreEventIds.CryptoProvider + 1, Level = LogLevel.Warning,
            Message = "Crypto provider {ProviderName} carries no validation ({ValidationLevel}). " +
                "Cryptographic operations routed to it are not performed by a validated module.")]
        public static partial void CryptoProviderUncertified(
            this ILogger logger,
            string providerName,
            string validationLevel);
    }
}
