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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    internal sealed class ClientChannelCertificateSnapshot : IDisposable
    {
        public ClientChannelCertificateSnapshot(
            Certificate? certificate,
            CertificateCollection? chain,
            long version)
        {
            Certificate = certificate?.AddRef();
            try
            {
                Chain = chain?.AddRef();
            }
            catch
            {
                Certificate?.Dispose();
                throw;
            }
            Version = version;
        }

        public Certificate? Certificate { get; }
        public CertificateCollection? Chain { get; }
        public long Version { get; }

        public void Dispose()
        {
            Certificate?.Dispose();
            Chain?.Dispose();
        }

        internal static bool HaveSameMaterial(
            Certificate? first,
            CertificateCollection? firstChain,
            Certificate? second,
            CertificateCollection? secondChain)
        {
            if (!SameCertificate(first, second) || first?.HasPrivateKey != second?.HasPrivateKey)
            {
                return false;
            }
            int firstStart = IssuerStart(first, firstChain);
            int secondStart = IssuerStart(second, secondChain);
            int count = (firstChain?.Count ?? 0) - firstStart;
            if (count != (secondChain?.Count ?? 0) - secondStart)
            {
                return false;
            }
            for (int i = 0; i < count; i++)
            {
                if (!SameCertificate(firstChain![i + firstStart], secondChain![i + secondStart]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SameCertificate(Certificate? first, Certificate? second)
        {
            return first is null ? second is null :
                second is not null && first.RawData.AsSpan().SequenceEqual(second.RawData);
        }

        private static int IssuerStart(Certificate? certificate, CertificateCollection? chain)
        {
            return chain is { Count: > 0 } && SameCertificate(certificate, chain[0]) ? 1 : 0;
        }
    }
}
