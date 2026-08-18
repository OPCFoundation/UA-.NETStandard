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

using Opc.Ua.Aas.V3;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Aas.V2;

namespace Opc.Ua.Aas.Tests.V2
{
    /// <summary>
    /// Tests that the two AAS models this assembly carries stay apart. OPC 30270
    /// maps the AAS V2 metamodel and the draft maps V3, and the two name their
    /// types alike, so they only coexist because each generates into its own
    /// namespace. Loading either has to work without the other.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasV2ModelTests
    {
        [Test]
        public void V2ModelLoadsItsPredefinedNodes()
        {
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaAasV2(CreateContext(Opc.Ua.Aas.V2.Namespaces.AasV2));

            Assert.That(nodes, Is.Not.Empty);
        }

        [Test]
        public void V2AndV3ModelsDeclareDifferentNamespaces()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Opc.Ua.Aas.V2.Namespaces.AasV2,
                    Is.EqualTo("http://opcfoundation.org/UA/I4AAS/"));
                Assert.That(Opc.Ua.Aas.V3.Namespaces.AasV3,
                    Is.EqualTo("http://opcfoundation.org/UA/I4AAS/v3/"));
                Assert.That(Opc.Ua.Aas.V2.Namespaces.AasV2,
                    Is.Not.EqualTo(Opc.Ua.Aas.V3.Namespaces.AasV3));
            });
        }

        /// <summary>
        /// The two models share type names - both declare AASSubmodelType and
        /// AASReferenceType - so a build only succeeds because the generated
        /// classes live in different namespaces. Asserting the full names keeps
        /// that from being undone silently.
        /// </summary>
        [Test]
        public void CollidingTypeNamesAreSeparatedByNamespace()
        {
            Assert.Multiple(() =>
            {
                Assert.That(typeof(Opc.Ua.Aas.V2.AASSubmodelState).Namespace,
                    Is.EqualTo("Opc.Ua.Aas.V2"));
                Assert.That(typeof(Opc.Ua.Aas.V3.AASSubmodelState).Namespace,
                    Is.EqualTo("Opc.Ua.Aas.V3"));
                Assert.That(typeof(Opc.Ua.Aas.V2.AASSubmodelState).Name,
                    Is.EqualTo(typeof(Opc.Ua.Aas.V3.AASSubmodelState).Name),
                    "The two models really do name this type alike.");
            });
        }

        /// <summary>
        /// AASOrderedSubmodelElementCollectionType redeclares the SubmodelElement
        /// placeholder its base type already declares, so the generated Add
        /// method hides the base one and has to be declared new. Without that the
        /// model does not compile at all, which is how the need was found.
        /// </summary>
        [Test]
        public void OrderedCollectionRedeclaresTheSubmodelElementPlaceholder()
        {
            MethodInfo? derived = typeof(Opc.Ua.Aas.V2.AASOrderedSubmodelElementCollectionState)
                .GetMethods()
                .FirstOrDefault(method => method.Name == "AddSubmodelElement_Placeholder" &&
                    method.DeclaringType == typeof(Opc.Ua.Aas.V2.AASOrderedSubmodelElementCollectionState));

            Assert.Multiple(() =>
            {
                Assert.That(derived, Is.Not.Null);
                Assert.That(typeof(Opc.Ua.Aas.V2.AASOrderedSubmodelElementCollectionState)
                    .IsSubclassOf(typeof(Opc.Ua.Aas.V2.AASSubmodelElementCollectionState)), Is.True);
            });
        }

        private static SystemContext CreateContext(string namespaceUri)
        {
            var namespaces = new NamespaceTable();
            namespaces.Append(namespaceUri);
            return new SystemContext(telemetry: null!)
            {
                NamespaceUris = namespaces,
                ServerUris = new StringTable()
            };
        }
    }
}
