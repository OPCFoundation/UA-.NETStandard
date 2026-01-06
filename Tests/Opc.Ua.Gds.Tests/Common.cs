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
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Client;
using Opc.Ua.Server.Tests;
using Opc.Ua.Test;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests
{
    public
#if NET7_0_OR_GREATER && !NET_STANDARD_TESTS
    partial
#endif
    class ApplicationTestDataGenerator
    {
#if NET7_0_OR_GREATER && !NET_STANDARD_TESTS
        [GeneratedRegex(@"[^\w\d\s]")]
        private static partial Regex Regex1();

        [GeneratedRegex(@"[^\w\d]")]
        private static partial Regex Regex2();
#else
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable SYSLIB1045 //Use 'GeneratedRegexAttribute' to generate the regular expression implementation at compile-time.
        private static Regex Regex1()
        {
            return new(@"[^\w\d\s]");
        }

        private static Regex Regex2()
        {
            return new(@"[^\w\d]");
        }
#pragma warning restore SYSLIB1045 //Use 'GeneratedRegexAttribute' to generate the regular expression implementation at compile-time.
#pragma warning restore IDE0079 // Remove unnecessary suppression
#endif

        private readonly ServerCapabilities m_serverCapabilities;

        public ApplicationTestDataGenerator(int randomStart, ITelemetryContext telemetry)
        {
            m_serverCapabilities = new ServerCapabilities();
            RandomSource = new RandomSource(randomStart);
            DataGenerator = new DataGenerator(RandomSource, telemetry);
        }

        public RandomSource RandomSource { get; }
        public DataGenerator DataGenerator { get; }

        public IList<ApplicationTestData> ApplicationTestSet(int count, bool invalidateSet)
        {
            var testDataSet = new List<ApplicationTestData>();
            for (int i = 0; i < count; i++)
            {
                ApplicationTestData testData = RandomApplicationTestData();
                if (invalidateSet)
                {
                    ApplicationRecordDataType appRecord = testData.ApplicationRecord;
                    appRecord.ApplicationId = new NodeId(Guid.NewGuid());
                    switch (i % 4)
                    {
                        case 0:
                            appRecord.ApplicationUri = DataGenerator.GetRandomString();
                            break;
                        case 1:
                            appRecord.ApplicationType = (ApplicationType)RandomSource.NextInt32(
                                100) +
                                8;
                            break;
                        case 2:
                            appRecord.ProductUri = DataGenerator.GetRandomString();
                            break;
                        case 3:
                            appRecord.DiscoveryUrls =
                                appRecord.ApplicationType == ApplicationType.Client
                                    ? RandomDiscoveryUrl(
                                        ["xxxyyyzzz"],
                                        RandomSource.NextInt32(0x7fff),
                                        "TestClient")
                                    : null;
                            break;
                        case 4:
                            appRecord.ServerCapabilities =
                                appRecord.ApplicationType == ApplicationType.Client
                                    ? RandomServerCapabilities()
                                    : null;
                            break;
                        case 5:
                            appRecord.ApplicationId = new NodeId(100);
                            break;
                    }
                }
                testDataSet.Add(testData);
            }
            return testDataSet;
        }

        private ApplicationTestData RandomApplicationTestData()
        {
            // TODO: set to discoveryserver
            var appType = (ApplicationType)RandomSource.NextInt32(
                (int)ApplicationType.ClientAndServer);
            string pureAppName = DataGenerator.GetRandomString("en");
            pureAppName = Regex1().Replace(pureAppName, string.Empty);
            string pureAppUri = Regex2().Replace(pureAppName, string.Empty);
            string appName = "UA " + pureAppName;
            StringCollection domainNames = RandomDomainNames();
            string localhost = domainNames[0];
            string locale = RandomSource.NextInt32(10) == 0 ? null : "en-US";
            string privateKeyFormat = RandomSource.NextInt32(1) == 0 ? "PEM" : "PFX";
            string appUri = ("urn:localhost:opcfoundation.org:" + pureAppUri.ToLowerInvariant())
                .Replace("localhost", localhost, StringComparison.Ordinal);
            string prodUri = "http://opcfoundation.org/UA/" + pureAppUri;
            var discoveryUrls = new StringCollection();
            var serverCapabilities = new StringCollection();
            int port = (DataGenerator.GetRandomInt16() & 0x1fff) + 50000;
            switch (appType)
            {
                case ApplicationType.Client:
                    appName += " Client";
                    break;
                case ApplicationType.ClientAndServer:
                    appName += " Client and";
                    goto case ApplicationType.Server;
                case ApplicationType.DiscoveryServer:
                    appName += " DiscoveryServer";
                    discoveryUrls = RandomDiscoveryUrl(domainNames, 4840, pureAppUri);
                    serverCapabilities.Add("LDS");
                    break;
                case ApplicationType.Server:
                    appName += " Server";
                    discoveryUrls = RandomDiscoveryUrl(domainNames, port, pureAppUri);
                    serverCapabilities = RandomServerCapabilities();
                    break;
            }
            return new ApplicationTestData
            {
                ApplicationRecord = new ApplicationRecordDataType
                {
                    ApplicationNames = [new LocalizedText(locale, appName)],
                    ApplicationUri = appUri,
                    ApplicationType = appType,
                    ProductUri = prodUri,
                    DiscoveryUrls = discoveryUrls,
                    ServerCapabilities = serverCapabilities
                },
                DomainNames = domainNames,
                Subject = Utils.Format("CN={0},DC={1},O=OPC Foundation", appName, localhost),
                PrivateKeyFormat = privateKeyFormat
            };
        }

        private StringCollection RandomServerCapabilities()
        {
            var serverCapabilities = new StringCollection();
            int capabilities = RandomSource.NextInt32(8);
            foreach (ServerCapability cap in m_serverCapabilities)
            {
                if (RandomSource.NextInt32(100) > 50)
                {
                    serverCapabilities.Add(cap.Id);
                    if (capabilities-- == 0)
                    {
                        break;
                    }
                }
            }
            return serverCapabilities;
        }

        private string RandomLocalHost()
        {
            string localhost = Regex2().Replace(
                DataGenerator.GetRandomSymbol("en").Trim().ToLowerInvariant(),
                string.Empty);
            if (localhost.Length >= 12)
            {
                localhost = localhost[..12];
            }
            return localhost;
        }

        private string[] RandomDomainNames()
        {
            int count = RandomSource.NextInt32(8) + 1;
            string[] result = new string[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = RandomLocalHost();
            }
            return result;
        }

        private StringCollection RandomDiscoveryUrl(
            StringCollection domainNames,
            int port,
            string appUri)
        {
            var result = new StringCollection();
            foreach (string name in domainNames)
            {
                int random = RandomSource.NextInt32(7);
                if ((result.Count == 0) || (random & 1) == 0)
                {
                    result.Add(
                        Utils.Format(
                            "opc.tcp://{0}:{1}/{2}",
                            name,
                            port++.ToString(CultureInfo.InvariantCulture),
                            appUri));
                }
                if ((random & 2) == 0)
                {
                    result.Add(
                        Utils.Format(
                            "http://{0}:{1}/{2}",
                            name,
                            port++.ToString(CultureInfo.InvariantCulture),
                            appUri));
                }
                if ((random & 4) == 0)
                {
                    result.Add(
                        Utils.Format(
                            "opc.https://{0}:{1}/{2}",
                            name,
                            port++.ToString(CultureInfo.InvariantCulture),
                            appUri));
                }
            }
            return result;
        }
    }

    public class ApplicationTestData
    {
        public ApplicationTestData()
        {
            Initialize();
        }

        private void Initialize()
        {
            ApplicationRecord = new ApplicationRecordDataType();
            CertificateGroupId = null;
            CertificateTypeId = null;
            CertificateRequestId = null;
            DomainNames = [];
            Subject = null;
            PrivateKeyFormat = "PFX";
            PrivateKeyPassword = null;
            Certificate = null;
            PrivateKey = null;
            IssuerCertificates = null;
        }

        public ApplicationRecordDataType ApplicationRecord;
        public NodeId CertificateGroupId;
        public NodeId CertificateTypeId;
        public NodeId CertificateRequestId;
        public StringCollection DomainNames;
        public string Subject;
        public string PrivateKeyFormat;
        public char[] PrivateKeyPassword;
        public byte[] Certificate;
        public byte[] PrivateKey;
        public byte[][] IssuerCertificates;
    }

    public class ApplicationMessageDlg : IApplicationMessageDlg
    {
        private readonly ILogger m_logger;
        private string m_message = string.Empty;
        private bool m_ask;

        public ApplicationMessageDlg(ILogger logger)
        {
            m_logger = logger;
        }

        public override void Message(string text, bool ask)
        {
            m_message = text;
            m_ask = ask;
        }

        public override Task<bool> ShowAsync()
        {
            if (m_ask)
            {
                m_message += " (y/n, default y): ";
                m_logger.LogInformation("ASK: {Message}", m_message);
            }
            else
            {
                m_logger.LogInformation("MSG: {Message}", m_message);
            }
            return Task.FromResult(true);
        }
    }

    public static class TestUtils
    {
        public static async Task CleanupTrustListAsync(IOpenStore id, ITelemetryContext telemetry)
        {
            using ICertificateStore store = id.OpenStore(telemetry);
            System.Security.Cryptography.X509Certificates.X509Certificate2Collection certs
                = await store
                .EnumerateAsync()
                .ConfigureAwait(false);
            foreach (System.Security.Cryptography.X509Certificates.X509Certificate2 cert in certs)
            {
                await store.DeleteAsync(cert.Thumbprint).ConfigureAwait(false);
            }
            if (store.SupportsCRLs)
            {
                Security.Certificates.X509CRLCollection crls = await store.EnumerateCRLsAsync()
                    .ConfigureAwait(false);
                foreach (Security.Certificates.X509CRL crl in crls)
                {
                    await store.DeleteCRLAsync(crl).ConfigureAwait(false);
                }
            }
        }

        public static void DeleteDirectory(string storePath)
        {
            string fullStorePath = Utils.ReplaceSpecialFolderNames(storePath);
            if (Directory.Exists(fullStorePath))
            {
                Directory.Delete(fullStorePath, true);
            }
        }

        private const int kMinPort = Utils.UaTcpDefaultPort;

        public static void PatchBaseAddressesPorts(ApplicationConfiguration config, int basePort)
        {
            if (basePort is >= kMinPort and <= ServerFixtureUtils.MaxTestPort)
            {
                var newBaseAddresses = new StringCollection();
                foreach (string baseAddress in config.ServerConfiguration.BaseAddresses)
                {
                    var baseAddressUri = new UriBuilder(baseAddress) { Port = basePort++ };
                    newBaseAddresses.Add(baseAddressUri.Uri.AbsoluteUri);
                }
                config.ServerConfiguration.BaseAddresses = newBaseAddresses;
            }
        }

        public static string PatchOnlyGDSEndpointUrlPort(string url, int port)
        {
            if (port is >= kMinPort and <= ServerFixtureUtils.MaxTestPort)
            {
                var newUrl = new UriBuilder(url);
                if (newUrl.Path.Contains("GlobalDiscoveryTestServer", StringComparison.Ordinal))
                {
                    newUrl.Port = port;
                    return newUrl.Uri.AbsoluteUri;
                }
            }
            return url;
        }

        public static async Task<GlobalDiscoveryTestServer> StartGDSAsync(
            bool clean,
            string storeType = CertificateStoreType.Directory,
            int maxTrustListSize = 0)
        {
            GlobalDiscoveryTestServer server = null;
            int testPort = ServerFixtureUtils.GetNextFreeIPPort();
            bool retryStartServer = false;
            int serverStartRetries = 25;
            do
            {
                try
                {
                    server = new GlobalDiscoveryTestServer(true, NUnitTelemetryContext.Create(true), maxTrustListSize);
                    await server.StartServerAsync(clean, testPort, storeType).ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    serverStartRetries--;
                    testPort = UnsecureRandom.Shared.Next(
                        ServerFixtureUtils.MinTestPort,
                        ServerFixtureUtils.MaxTestPort);
                    if (serverStartRetries == 0 || sre.StatusCode != StatusCodes.BadNoCommunication)
                    {
                        throw;
                    }
                    retryStartServer = true;
                }
                await Task.Delay(UnsecureRandom.Shared.Next(100, 1000)).ConfigureAwait(false);
            } while (retryStartServer);

            return server;
        }
    }
}
