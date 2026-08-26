/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Aas.Server;

namespace Opc.Ua.Aas.Tests.Server
{
    /// <summary>
    /// Tests the conformance units of clause 10. A Server declares these in
    /// ServerCapabilities, so a name that drifts from the specification makes
    /// the Server claim a unit no Client can recognise.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasConformanceUnitsTests
    {
        /// <summary>
        /// The sixteen units of clause 10, spelled as the specification spells
        /// them.
        /// </summary>
        private static readonly string[] s_clause10Units =
        [
            "AAS-Metamodel",
            "AAS-SubmodelElements",
            "AAS-ValueFidelity",
            "AAS-InstanceMaterialization",
            "AAS-LosslessRoundTrip",
            "AAS-Registry",
            "AAS-RegistryIdentity",
            "AAS-RegistryVersioning",
            "AAS-Discovery",
            "AAS-OperationInvoke",
            "AAS-Federation",
            "AAS-DisclosureTiers",
            "AAS-UpdateableRegistry",
            "AAS-EnvironmentExport",
            "AAS-Packages",
            "AAS-PackageIntegrity"
        ];

        [Test]
        public void DeclaredUnitsAreExactlyTheSixteenOfClause10()
        {
            Assert.That(Declared(), Is.EquivalentTo(s_clause10Units));
        }

        [Test]
        public void NoUnitIsDeclaredTwice()
        {
            List<string> declared = Declared();

            Assert.That(declared.Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(declared.Count));
        }

        [Test]
        public void EveryUnitIsAValidBrowseName()
        {
            Assert.Multiple(() =>
            {
                foreach (string unit in Declared())
                {
                    var name = new QualifiedName(unit);
                    Assert.That(name.Name, Is.EqualTo(unit));
                    Assert.That(name.NamespaceIndex, Is.Zero,
                        "Conformance units are published in namespace zero.");
                }
            });
        }

        private static List<string> Declared()
        {
            return [.. typeof(AasConformanceUnits)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue()!)];
        }
    }
}
