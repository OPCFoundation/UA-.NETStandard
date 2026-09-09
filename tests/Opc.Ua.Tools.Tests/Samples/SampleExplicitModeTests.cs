/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

#if NET10_0
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests.Samples
{
    [TestFixture]
    [NonParallelizable]
    public sealed class SampleExplicitModeTests
    {
        [TestCase("--help")]
        [TestCase("-h")]
        public async Task ReferenceClientHelpExplainsTestModeConsentWithoutStartingClientAsync(string help)
        {
            string configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
            DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
            {
                directory = directory.Parent;
            }
            Assert.That(directory, Is.Not.Null);
            string assemblyPath = Path.Combine(
                directory!.FullName, "samples", "Reference", "ConsoleReferenceClient",
                "bin", configuration, "net10.0", "ConsoleReferenceClient.dll");
            Assert.That(File.Exists(assemblyPath), Is.True,
                "Build ConsoleReferenceClient for net10.0 before running these CLI tests.");
            Type program = Assembly.LoadFrom(assemblyPath).GetType("Quickstarts.ConsoleReferenceClient.Program", true)!;
            Func<string[], Task<int>> main = program.GetMethod("Main", BindingFlags.Public | BindingFlags.Static)!
                .CreateDelegate<Func<string[], Task<int>>>();
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            TextWriter previousOutput = Console.Out;
            TextWriter previousError = Console.Error;
            try
            {
                Console.SetOut(output);
                Console.SetError(error);

                int exitCode = await main(["--testall", "--autoaccept=false", help]).ConfigureAwait(false);

                Assert.That(exitCode, Is.Zero);
                Assert.That(output.ToString(), Does.Contain("--testall").And.Contain("BadCertificateUntrusted")
                    .And.Contain("--autoaccept=false").And.Contain("isolated testing"));
                Assert.That(output.ToString(), Does.Not.Contain("WARNING:"));
                Assert.That(error.ToString(), Is.Empty);
            }
            finally
            {
                Console.SetOut(previousOutput);
                Console.SetError(previousError);
            }
        }
    }
}
#endif
