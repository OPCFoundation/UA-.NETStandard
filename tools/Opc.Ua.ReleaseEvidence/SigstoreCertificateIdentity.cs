// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System.Formats.Asn1;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.ReleaseEvidence
{
    internal static class SigstoreCertificateIdentity
    {
        public static bool MatchesVerifiedCertificate(
            X509Certificate2 certificate, VerificationAuthority authority)
        {
            return ReadExtension(certificate, "1.3.6.1.4.1.57264.1.8") == authority.Issuer &&
                ReadExtension(certificate, "1.3.6.1.4.1.57264.1.9") == authority.CertificateIdentity &&
                ReadExtension(certificate, "1.3.6.1.4.1.57264.1.10") == authority.DefinitionSha &&
                ReadExtension(certificate, "1.3.6.1.4.1.57264.1.11") == "github-hosted" &&
                ReadExtension(certificate, "1.3.6.1.4.1.57264.1.12") ==
                    "https://github.com/" + authority.Repository &&
                ReadExtension(certificate, "1.3.6.1.4.1.57264.1.14") == authority.Ref;
        }

        private static string? ReadExtension(X509Certificate2 certificate, string oid)
        {
            X509Extension[] extensions = [.. certificate.Extensions.Cast<X509Extension>()
                .Where(e => e.Oid?.Value == oid)];
            if (extensions.Length != 1)
            {
                return null;
            }
            try
            {
                var reader = new AsnReader(extensions[0].RawData, AsnEncodingRules.DER);
                string value = reader.ReadCharacterString(UniversalTagNumber.UTF8String);
                reader.ThrowIfNotEmpty();
                return value;
            }
            catch (AsnContentException)
            {
                return null;
            }
        }
    }
}
