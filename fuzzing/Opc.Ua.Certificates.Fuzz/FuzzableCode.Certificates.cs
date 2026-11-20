// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Formats.Asn1;
using System.IO;
using System.Security.Cryptography;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Certificate loading and concatenated DER-chain fuzz targets.
    /// </summary>
    public static partial class FuzzableCode
    {
        /// <summary>
        /// Loads a standalone certificate from AFL input.
        /// </summary>
        public static void AflfuzzCertificateDecoder(Stream stream)
        {
            _ = FuzzCertificateDecoderCore(ReadAllBytes(stream));
        }

        /// <summary>
        /// Loads a standalone certificate from libFuzzer or OneFuzz input.
        /// </summary>
        public static void LibfuzzCertificateDecoder(ReadOnlySpan<byte> input)
        {
            _ = FuzzCertificateDecoderCore(input.ToArray());
        }

        /// <summary>
        /// Parses a certificate chain with the default platform path.
        /// </summary>
        public static void AflfuzzCertificateChainDecoder(Stream stream)
        {
            _ = FuzzCertificateChainDecoderCore(ReadAllBytes(stream), useAsnParser: false);
        }

        /// <summary>
        /// Parses a certificate chain with the default platform path.
        /// </summary>
        public static void LibfuzzCertificateChainDecoder(ReadOnlySpan<byte> input)
        {
            _ = FuzzCertificateChainDecoderCore(input.ToArray(), useAsnParser: false);
        }

        /// <summary>
        /// Parses a certificate chain with the forced ASN.1 path.
        /// </summary>
        public static void AflfuzzCertificateChainDecoderCustom(Stream stream)
        {
            _ = FuzzCertificateChainDecoderCore(ReadAllBytes(stream), useAsnParser: true);
        }

        /// <summary>
        /// Parses a certificate chain with the forced ASN.1 path.
        /// </summary>
        public static void LibfuzzCertificateChainDecoderCustom(ReadOnlySpan<byte> input)
        {
            _ = FuzzCertificateChainDecoderCore(input.ToArray(), useAsnParser: true);
        }

        internal static bool FuzzCertificateDecoderCore(byte[] input)
        {
            Certificate? certificate = null;
            try
            {
                byte[] rawData;
                try
                {
                    certificate = Certificate.FromRawData(input);
                    // .NET Framework can defer invalid-handle detection until the first property read.
                    rawData = certificate.RawData;
                }
                catch (CryptographicException exception) when (IsExpectedCertificateInputException(exception))
                {
                    return false;
                }

                using Certificate reloaded = Certificate.FromRawData(rawData);
                if (!rawData.AsSpan().SequenceEqual(reloaded.RawData) ||
                    !certificate.SubjectName.RawData.AsSpan().SequenceEqual(reloaded.SubjectName.RawData) ||
                    !certificate.IssuerName.RawData.AsSpan().SequenceEqual(reloaded.IssuerName.RawData) ||
                    certificate.SerialNumber != reloaded.SerialNumber ||
                    certificate.NotBefore != reloaded.NotBefore ||
                    certificate.NotAfter != reloaded.NotAfter ||
                    reloaded.HasPrivateKey)
                {
                    throw new InvalidOperationException("Reloading a decoded certificate changed its fields.");
                }
                return true;
            }
            finally
            {
                certificate?.Dispose();
            }
        }

        internal static bool FuzzCertificateChainDecoderCore(byte[] input, bool useAsnParser)
        {
            CertificateCollection chain;
            try
            {
                chain = Utils.ParseCertificateChainBlob(input, telemetry: null, useAsnParser: useAsnParser);
            }
            catch (ServiceResultException exception) when (IsExpectedCertificateInputException(exception))
            {
                return false;
            }

            using (chain)
            {
                foreach (Certificate certificate in chain)
                {
                    _ = certificate.SubjectName.RawData;
                    _ = certificate.IssuerName.RawData;
                    _ = certificate.SerialNumber;
                }
                if (!input.AsSpan().SequenceEqual(Utils.CreateCertificateChainBlob(chain)))
                {
                    throw new InvalidOperationException("Certificate-chain parsing changed the ordered DER bytes.");
                }
            }
            return true;
        }

        internal static bool IsExpectedCertificateInputException(Exception exception)
        {
            while (true)
            {
                if (exception is ServiceResultException serviceException)
                {
                    if (serviceException.StatusCode != StatusCodes.BadCertificateInvalid ||
                        serviceException.InnerException == null)
                    {
                        return false;
                    }
                }
                else if (exception is CryptographicException or AsnContentException)
                {
                    if (exception is AsnContentException)
                    {
                        return true;
                    }
                    if (exception.InnerException == null)
                    {
                        return true;
                    }
                }
                else
                {
                    return false;
                }

                // A status or crypto wrapper must not turn a programmer error into input rejection.
                exception = exception.InnerException!;
            }
        }
    }
}
