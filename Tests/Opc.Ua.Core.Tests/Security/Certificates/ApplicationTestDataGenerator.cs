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
using System.Text.RegularExpressions;
using Opc.Ua.Test;


namespace Opc.Ua.Core.Tests
{
    public class ApplicationTestDataGenerator
    {
        private int m_randomStart = 1;
        private RandomSource m_randomSource;
        private DataGenerator m_dataGenerator;

        public ApplicationTestDataGenerator(int randomStart)
        {
            m_randomStart = randomStart;
            m_randomSource = new RandomSource(randomStart);
            m_dataGenerator = new DataGenerator(m_randomSource);
        }

        public RandomSource RandomSource => m_randomSource;
        public DataGenerator DataGenerator => m_dataGenerator;

        public IList<ApplicationTestData> ApplicationTestSet(int count)
        {
            var testDataSet = new List<ApplicationTestData>();
            for (int i = 0; i < count; i++)
            {
                testDataSet.Add(RandomApplicationTestData());
            }
            return testDataSet;
        }

        private ApplicationTestData RandomApplicationTestData()
        {
            // TODO: set to discoveryserver
            ApplicationType appType = (ApplicationType)m_randomSource.NextInt32((int)ApplicationType.ClientAndServer);
            string pureAppName = m_dataGenerator.GetRandomString("en");
            pureAppName = Regex.Replace(pureAppName, @"[^\w\d\s]", "");
            string pureAppUri = Regex.Replace(pureAppName, @"[^\w\d]", "");
            string appName = "UA " + pureAppName;
            StringCollection domainNames = RandomDomainNames();
            string localhost = domainNames[0];
            string privateKeyFormat = m_randomSource.NextInt32(1) == 0 ? "PEM" : "PFX";
            string appUri = ("urn:localhost:opcfoundation.org:" + pureAppUri.ToLower()).Replace("localhost", localhost);
            string prodUri = "http://opcfoundation.org/UA/" + pureAppUri;
            StringCollection discoveryUrls = new StringCollection();
            int port = (m_dataGenerator.GetRandomInt16() & 0x1fff) + 50000;
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
                    break;
                case ApplicationType.Server:
                    appName += " Server";
                    discoveryUrls = RandomDiscoveryUrl(domainNames, port, pureAppUri);
                    break;
            }
            ApplicationTestData testData = new ApplicationTestData {
                ApplicationName = appName,
                ApplicationUri = appUri,
                DomainNames = domainNames,
                Subject = String.Format("CN={0},DC={1},O=OPC Foundation", appName, localhost),
                PrivateKeyFormat = privateKeyFormat
            };
            return testData;
        }

        private string RandomLocalHost()
        {
            string localhost = Regex.Replace(m_dataGenerator.GetRandomSymbol("en").Trim().ToLower(), @"[^\w\d]", "");
            if (localhost.Length >= 12)
            {
                localhost = localhost.Substring(0, 12);
            }
            return localhost;
        }

        private string[] RandomDomainNames()
        {
            int count = m_randomSource.NextInt32(8) + 1;
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
                int random = m_randomSource.NextInt32(7);
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
}
