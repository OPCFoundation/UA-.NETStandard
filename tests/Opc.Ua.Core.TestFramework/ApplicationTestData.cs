/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Core.TestFramework
{
    public class ApplicationTestData
    {
        public ApplicationTestData()
        {
            Initialize();
        }

        private void Initialize()
        {
            ApplicationUri = null!;
            ApplicationName = null!;
            DomainNames = [];
            Subject = null!;
            PrivateKeyFormat = "PFX";
            PrivateKeyPassword = string.Empty;
            Certificate = null!;
            PrivateKey = null!;
            IssuerCertificates = null!;
        }

        public string ApplicationUri = null!;
        public string ApplicationName = null!;
        public ArrayOf<string> DomainNames;
        public string Subject = null!;
        public string PrivateKeyFormat = null!;
        public string PrivateKeyPassword = null!;
        public byte[] Certificate = null!;
        public byte[] PrivateKey = null!;
        public byte[][] IssuerCertificates = null!;
    }
}
