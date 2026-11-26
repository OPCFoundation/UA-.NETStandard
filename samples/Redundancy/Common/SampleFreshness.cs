// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Redundancy;

namespace RedundancySample
{
    /// <summary>
    /// Applies a deployment's authority and record-secret providers to external sensitive state.
    /// </summary>
    internal static partial class SampleFreshness
    {
        /// <summary>
        /// Resolves an independently administered authority and registers the protected store.
        /// </summary>
        public static async Task<AesCbcHmacRecordProtector> AddAsync(
            IServiceCollection services,
            Func<string, string?> read,
            string prefix,
            CancellationToken ct = default)
        {
            Func<IServiceProvider, ISharedKeyValueStore>? authorityFactory = null;
            ISecretRegistry? secrets = null;
            ConfigureDeployment(services, read, ref authorityFactory, ref secrets);
            if (authorityFactory == null)
            {
                throw new InvalidOperationException(
                    "External shared state requires an independently scoped freshness authority. " +
                    "Supply a deployment provider in SampleFreshness.ConfigureDeployment; a record key alone " +
                    "does not enable this topology.");
            }

            Guid deploymentId = ReadGuid(read, $"{prefix}_DEPLOYMENT_ID");
            Guid epoch = ReadGuid(read, $"{prefix}_FRESHNESS_EPOCH");
            services.AddSharedRecordFreshness(authorityFactory, deploymentId, epoch);
            AesCbcHmacRecordProtector protector = await ReadProtectorAsync(secrets, read, prefix, ct)
                .ConfigureAwait(false);
            services.AddSingleton<IRecordProtector>(protector);
            return protector;
        }

        /// <summary>
        /// Verifies enrollment before ordinary startup, or explicitly enrolls and exits without starting the app.
        /// </summary>
        public static async ValueTask<bool> VerifyOrEnrollAsync(
            IServiceProvider services,
            Func<string, string?> read,
            string prefix,
            CancellationToken ct = default)
        {
            FreshnessProtectedKeyValueStore store = services.GetRequiredService<FreshnessProtectedKeyValueStore>();
            if (IsEnabled(read($"{prefix}_ENROLL_ONLY")))
            {
                await store.EnrollAsync(ct).ConfigureAwait(false);
                Console.WriteLine("Freshness authority enrollment verified; the sample will not start.");
                return true;
            }

            await store.VerifyEpochAsync(ct).ConfigureAwait(false);
            return false;
        }

        /// <summary>
        /// Recognizes an explicit opt-in rather than enabling a mode when configuration is absent.
        /// </summary>
        public static bool IsEnabled(string? value) => bool.TryParse(value, out bool enabled) && enabled;

        /// <summary>
        /// Supply a separately secured durable/linearizable backend and, optionally, a deployment secret registry.
        /// </summary>
        static partial void ConfigureDeployment(
            IServiceCollection services,
            Func<string, string?> read,
            ref Func<IServiceProvider, ISharedKeyValueStore>? authorityFactory,
            ref ISecretRegistry? secrets);

        private static Guid ReadGuid(Func<string, string?> read, string key) =>
            Guid.TryParse(SampleCluster.Require(read, key), out Guid value) && value != Guid.Empty
                ? value
                : throw new FormatException($"{key} must be a nonempty GUID.");

        private static async Task<AesCbcHmacRecordProtector> ReadProtectorAsync(
            ISecretRegistry? secrets,
            Func<string, string?> read,
            string prefix,
            CancellationToken ct)
        {
            if (secrets != null)
            {
                var name = new SecretIdentifier(
                    SampleCluster.Require(read, $"{prefix}_RECORD_SECRET_NAME"),
                    SampleCluster.Require(read, $"{prefix}_RECORD_SECRET_TYPE"),
                    read($"{prefix}_RECORD_SECRET_SCOPE"));
                using ISecret secret = await secrets.GetAsync(name, ct).ConfigureAwait(false) ??
                    throw new InvalidOperationException("The selected record protection secret was not found.");
                return CreateProtector(secret.Bytes);
            }

            byte[] key = await File.ReadAllBytesAsync(
                SampleCluster.Require(read, $"{prefix}_RECORD_KEY_FILE"), ct).ConfigureAwait(false);
            try
            {
                return CreateProtector(key);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        private static AesCbcHmacRecordProtector CreateProtector(ReadOnlySpan<byte> key) =>
            key.Length == 32
                ? new AesCbcHmacRecordProtector(key)
                : throw new InvalidOperationException("The record protection secret must contain 32 raw bytes.");
    }
}
