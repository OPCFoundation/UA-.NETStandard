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
using System.Globalization;
using System.Text.RegularExpressions;
using Opc.Ua.Test;

namespace Opc.Ua.Core.Tests
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

        public ApplicationTestDataGenerator(int randomStart)
        {
            RandomSource = new RandomSource(randomStart);
            DataGenerator = new DataGenerator(RandomSource);
        }

        public RandomSource RandomSource { get; }
        public DataGenerator DataGenerator { get; }

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
            var appType = (ApplicationType)RandomSource.NextInt32(
                (int)ApplicationType.ClientAndServer);
            string pureAppName = DataGenerator.GetRandomString("en");
            pureAppName = Regex1().Replace(pureAppName, string.Empty);
            string pureAppUri = Regex2().Replace(pureAppName, string.Empty);
            string appName = "UA " + pureAppName;
            StringCollection domainNames = RandomDomainNames();
            string localhost = domainNames[0];
            string privateKeyFormat = RandomSource.NextInt32(1) == 0 ? "PEM" : "PFX";
            string appUri = ("urn:localhost:opcfoundation.org:" + pureAppUri.ToLower()).Replace(
                "localhost",
                localhost,
                StringComparison.Ordinal);

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
                    _ = RandomDiscoveryUrl(domainNames, 4840, pureAppUri);
                    break;
                case ApplicationType.Server:
                    appName += " Server";
                    _ = RandomDiscoveryUrl(domainNames, port, pureAppUri);
                    break;
            }
            return new ApplicationTestData
            {
                ApplicationName = appName,
                ApplicationUri = appUri,
                DomainNames = domainNames,
                Subject = Utils.Format("CN={0},O=OPC Foundation,DC={1}", appName, localhost),
                PrivateKeyFormat = privateKeyFormat
            };
        }

        private string RandomLocalHost()
        {
            string localhost = Regex2().Replace(
                DataGenerator.GetRandomSymbol("en").Trim().ToLower(),
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
}
