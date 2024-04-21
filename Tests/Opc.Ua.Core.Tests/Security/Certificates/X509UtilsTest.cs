using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the <see cref="X509Utils"/> class
    /// </summary>
    [TestFixture, Category("X509Utils")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class X509UtilsTest
    {
        [TestCase("CN=UA Yellow Green Server,DC=dogblueberry,O=OPC\"=\"Foundation")]
        [TestCase("CN=UA Yellow Green Server,DC=dogblueberry,O=\"OPC=Foundation\"")]
        public void ParseDistinguishedNameHandlesEscapingOfEqualsChar(string input)
        {
            List<string> result = X509Utils.ParseDistinguishedName(input);

            Assert.True(result.Count == 3);
            Assert.Contains("CN=UA Yellow Green Server", result);
            Assert.Contains("DC=dogblueberry", result);
            Assert.Contains("O=\"OPC=Foundation\"", result);
        }



        [TestCase("CN = UA Yellow Blue Server, DC = dogredberry, O =\"OPC/Foundation\"")]
        [TestCase("CN = UA Yellow Blue Server, DC = dogredberry, O =OPC\"/\"Foundation")]
        public void ParseDistinguishedNameHandlesEscapingOfSlashChar(string input)
        {
            List<string> result = X509Utils.ParseDistinguishedName(input);

            Assert.True(result.Count == 3);
            Assert.Contains("CN=UA Yellow Blue Server", result);
            Assert.Contains("DC=dogredberry", result);
            Assert.Contains("O=\"OPC/Foundation\"", result);
        }
    }
}
