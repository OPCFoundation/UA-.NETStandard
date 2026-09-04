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
 *
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

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.SpecTraceability;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Holds this assembly's half of the WoT specification evidence ledger.
    /// </summary>
    /// <remarks>
    /// The ledger spans three test assemblies and none of them can see the
    /// others, so each embeds the same file and resolves only the mappings that
    /// name it. Most of the WoT Connectivity requirements land here, because
    /// most of what that specification states is about a running registry.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotSpecRequirementTests
    {
        private const string ThisAssembly = "Opc.Ua.WotCon.Bindings.Tests";

        [Test]
        public void EveryMappingNamingThisAssemblyResolvesAndWillRun()
        {
            Assembly assembly = typeof(WotSpecRequirementTests).Assembly;
            List<WotSpecRequirement> requirements =
                WotSpecRequirementLedger.Load(assembly);

            Assert.Multiple(() =>
            {
                foreach (WotSpecRequirement requirement in requirements
                    .Where(r => r.Assembly == ThisAssembly))
                {
                    foreach (string name in requirement.Tests)
                    {
                        Assert.That(
                            WotSpecRequirementLedger.DescribeResolution(assembly, name),
                            Is.EqualTo("runs"),
                            $"{requirement.SpecId} maps onto '{name}'.");
                    }
                }
            });
        }

        /// <summary>
        /// The mappings that name this assembly are not an empty set: if they
        /// were, this fixture would pass by checking nothing.
        /// </summary>
        [Test]
        public void ThisAssemblyCarriesEvidenceForSomeRequirement()
        {
            List<WotSpecRequirement> requirements = WotSpecRequirementLedger.Load(
                typeof(WotSpecRequirementTests).Assembly);

            Assert.That(
                requirements.Count(r => r.Assembly == ThisAssembly && r.Tests.Count > 0),
                Is.GreaterThan(0));
        }
    }
}
