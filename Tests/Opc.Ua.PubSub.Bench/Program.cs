/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Diagnostics;
using System.Security.Cryptography;
using Opc.Ua.PubSub.Udp.Dtls;

namespace Opc.Ua.PubSub.Bench
{
    /// <summary>
    /// Focused post-handshake DTLS record throughput benchmark for Part 14 §7.3.2.4.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            int iterations = args.Length > 0 && int.TryParse(args[0], out int parsedIterations)
                ? parsedIterations
                : 100_000;
            int payloadSize = args.Length > 1 && int.TryParse(args[1], out int parsedPayloadSize)
                ? parsedPayloadSize
                : 256;
            var registry = new DtlsProfileRegistry();
            DtlsProfile profile = registry.Resolve("ECC_nistP256_AesGcm");
            byte[] trafficSecret = RandomNumberGenerator.GetBytes(32);
            byte[] payload = RandomNumberGenerator.GetBytes(payloadSize);
            try
            {
                using var writer = new DtlsRecordProtection(profile, trafficSecret, epoch: 3);
                using var reader = new DtlsRecordProtection(profile, trafficSecret, epoch: 3);
                for (int ii = 0; ii < 1_000; ii++)
                {
                    byte[] warmupRecord = writer.Seal(payload);
                    _ = reader.Open(warmupRecord);
                }

                Stopwatch stopwatch = Stopwatch.StartNew();
                long protectedBytes = 0;
                for (int ii = 0; ii < iterations; ii++)
                {
                    byte[] record = writer.Seal(payload);
                    byte[] plaintext = reader.Open(record);
                    protectedBytes += record.Length + plaintext.Length;
                }

                stopwatch.Stop();
                double seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, double.Epsilon);
                double operationsPerSecond = iterations / seconds;
                double megabytesPerSecond = protectedBytes / seconds / (1024 * 1024);
                Console.WriteLine(
                    $"DTLS post-handshake {profile.Name}: {iterations} seal/open ops, " +
                    $"payload={payloadSize}B, {operationsPerSecond:F0} ops/s, {megabytesPerSecond:F2} MiB/s, " +
                    $"elapsed={stopwatch.Elapsed}.");
                return 0;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(trafficSecret);
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }
}
