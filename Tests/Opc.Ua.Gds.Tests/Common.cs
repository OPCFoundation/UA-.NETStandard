/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Client;
using Opc.Ua.Test;


namespace Opc.Ua.Gds.Tests
{
    public class ApplicationTestDataGenerator
    {
        private int _randomStart = 1;
        private RandomSource _randomSource;
        private DataGenerator _dataGenerator;
        private ServerCapabilities _serverCapabilities;

        public ApplicationTestDataGenerator(int randomStart)
        {
            this._randomStart = randomStart;
            _serverCapabilities = new ServerCapabilities();
            _randomSource = new RandomSource(randomStart);
            _dataGenerator = new DataGenerator(_randomSource);
        }

        public RandomSource RandomSource => _randomSource;
        public DataGenerator DataGenerator => _dataGenerator;

        public IList<ApplicationTestData> ApplicationTestSet(int count, bool invalidateSet)
        {
            var testDataSet = new List<ApplicationTestData>();
            for (int i = 0; i < count; i++)
            {
                var testData = RandomApplicationTestData();
                if (invalidateSet)
                {
                    ApplicationRecordDataType appRecord = testData.ApplicationRecord;
                    appRecord.ApplicationId = new NodeId(Guid.NewGuid());
                    switch (i % 4)
                    {
                        case 0:
                            appRecord.ApplicationUri = _dataGenerator.GetRandomString();
                            break;
                        case 1:
                            appRecord.ApplicationType = (ApplicationType)_randomSource.NextInt32(100) + 8;
                            break;
                        case 2:
                            appRecord.ProductUri = _dataGenerator.GetRandomString();
                            break;
                        case 3:
                            appRecord.DiscoveryUrls = appRecord.ApplicationType == ApplicationType.Client ?
                                RandomDiscoveryUrl(new StringCollection { "xxxyyyzzz" }, _randomSource.NextInt32(0x7fff), "TestClient") : null;
                            break;
                        case 4:
                            appRecord.ServerCapabilities = appRecord.ApplicationType == ApplicationType.Client ?
                                RandomServerCapabilities() : null;
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
            ApplicationType appType = (ApplicationType)_randomSource.NextInt32((int)ApplicationType.ClientAndServer);
            string pureAppName = _dataGenerator.GetRandomString("en");
            pureAppName = Regex.Replace(pureAppName, @"[^\w\d\s]", "");
            string pureAppUri = Regex.Replace(pureAppName, @"[^\w\d]", "");
            string appName = "UA " + pureAppName;
            StringCollection domainNames = RandomDomainNames();
            string localhost = domainNames[0];
            string privateKeyFormat = _randomSource.NextInt32(1) == 0 ? "PEM" : "PFX";
            string appUri = ("urn:localhost:opcfoundation.org:" + pureAppUri.ToLower()).Replace("localhost", localhost);
            string prodUri = "http://opcfoundation.org/UA/" + pureAppUri;
            StringCollection discoveryUrls = new StringCollection();
            StringCollection serverCapabilities = new StringCollection();
            int port = (_dataGenerator.GetRandomInt16() & 0x1fff) + 50000;
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
            ApplicationTestData testData = new ApplicationTestData {
                ApplicationRecord = new ApplicationRecordDataType {
                    ApplicationNames = new LocalizedTextCollection { new LocalizedText("en-us", appName) },
                    ApplicationUri = appUri,
                    ApplicationType = appType,
                    ProductUri = prodUri,
                    DiscoveryUrls = discoveryUrls,
                    ServerCapabilities = serverCapabilities
                },
                DomainNames = domainNames,
                Subject = String.Format("CN={0},DC={1},O=OPC Foundation", appName, localhost),
                PrivateKeyFormat = privateKeyFormat
            };
            return testData;
        }

        private StringCollection RandomServerCapabilities()
        {
            var serverCapabilities = new StringCollection();
            int capabilities = _randomSource.NextInt32(8);
            foreach (var cap in _serverCapabilities)
            {
                if (_randomSource.NextInt32(100) > 50)
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
            string localhost = Regex.Replace(_dataGenerator.GetRandomSymbol("en").Trim().ToLower(), @"[^\w\d]", "");
            if (localhost.Length >= 12)
            {
                localhost = localhost.Substring(0, 12);
            }
            return localhost;
        }

        private string[] RandomDomainNames()
        {
            int count = _randomSource.NextInt32(8) + 1;
            var result = new string[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = RandomLocalHost();
            }
            return result;
        }

        private StringCollection RandomDiscoveryUrl(StringCollection domainNames, int port, string appUri)
        {
            var result = new StringCollection();
            foreach (var name in domainNames)
            {
                int random = _randomSource.NextInt32(7);
                if ((result.Count == 0) || (random & 1) == 0)
                {
                    result.Add(String.Format("opc.tcp://{0}:{1}/{2}", name, (port++).ToString(), appUri));
                }
                if ((random & 2) == 0)
                {
                    result.Add(String.Format("http://{0}:{1}/{2}", name, (port++).ToString(), appUri));
                }
                if ((random & 4) == 0)
                {
                    result.Add(String.Format("https://{0}:{1}/{2}", name, (port++).ToString(), appUri));
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
            DomainNames = new StringCollection();
            Subject = null;
            PrivateKeyFormat = "PFX";
            PrivateKeyPassword = "";
            Certificate = null;
            PrivateKey = null;
            IssuerCertificates = null;
        }

        public ApplicationRecordDataType ApplicationRecord;
        public NodeId CertificateGroupId;
        public NodeId CertificateTypeId;
        public NodeId CertificateRequestId;
        public StringCollection DomainNames;
        public String Subject;
        public String PrivateKeyFormat;
        public String PrivateKeyPassword;
        public byte[] Certificate;
        public byte[] PrivateKey;
        public byte[][] IssuerCertificates;
    }

    public class ApplicationMessageDlg : IApplicationMessageDlg
    {
        private string message = string.Empty;
        private bool ask = false;

        public override void Message(string text, bool ask)
        {
            this.message = text;
            this.ask = ask;
        }

        public override async Task<bool> ShowAsync()
        {
            if (ask)
            {
                message += " (y/n, default y): ";
                Console.Write(message);
            }
            else
            {
                Console.WriteLine(message);
            }
            if (ask)
            {
                try
                {
                    ConsoleKeyInfo result = Console.ReadKey();
                    Console.WriteLine();
                    return await Task.FromResult((result.KeyChar == 'y') || (result.KeyChar == 'Y') || (result.KeyChar == '\r'));
                }
                catch
                {
                    // intentionally fall through
                }
            }
            return await Task.FromResult(true);
        }
    }

    public static class TestUtils
    {
        public static void CleanupTrustList(ICertificateStore store, bool dispose = true)
        {
            var certs = store.Enumerate().Result;
            foreach (var cert in certs)
            {
                store.Delete(cert.Thumbprint);
            }
            var crls = store.EnumerateCRLs();
            foreach (var crl in crls)
            {
                store.DeleteCRL(crl);
            }
            if (dispose)
            {
                store.Dispose();
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

        const int MaxPort = 64000;
        const int MinPort = Opc.Ua.Utils.UaTcpDefaultPort;
        public static void PatchBaseAddressesPorts(ApplicationConfiguration config, int basePort)
        {
            if (basePort >= MinPort && basePort <= MaxPort)
            {
                StringCollection newBaseAddresses = new StringCollection();
                foreach (var baseAddress in config.ServerConfiguration.BaseAddresses)
                {
                    UriBuilder baseAddressUri = new UriBuilder(baseAddress);
                    baseAddressUri.Port = basePort++;
                    newBaseAddresses.Add(baseAddressUri.Uri.AbsoluteUri);
                }
                config.ServerConfiguration.BaseAddresses = newBaseAddresses;
            }
        }

        public static string PatchOnlyGDSEndpointUrlPort(string url, int port)
        {
            if (port >= MinPort && port <= MaxPort)
            {
                UriBuilder newUrl = new UriBuilder(url);
                if (newUrl.Path.Contains("GlobalDiscoveryTestServer"))
                {
                    newUrl.Port = port;
                    return newUrl.Uri.AbsoluteUri;
                }
            }
            return url;
        }

        public static async Task<GlobalDiscoveryTestServer> StartGDS(bool clean)
        {
            GlobalDiscoveryTestServer server = null;
            Random random = new Random();
            int testPort;
            bool retryStartServer = false;
            int serverStartRetries = 10;
            do
            {
                try
                {
                    // work around travis issue by selecting different ports on every run
                    testPort = random.Next(50000, 60000);
                    server = new GlobalDiscoveryTestServer(true);
                    await server.StartServer(clean, testPort);
                }
                catch (ServiceResultException sre)
                {
                    serverStartRetries--;
                    if (serverStartRetries == 0 ||
                        sre.StatusCode != StatusCodes.BadNoCommunication)
                    {
                        throw;
                    }
                    retryStartServer = true;
                }
                await Task.Delay(1000);
            } while (retryStartServer);

            return server;
        }
    }
}
