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

using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Verifies that native Organizes targets are address-space node identifiers rather
    /// than document dependencies.
    /// </summary>
    [TestFixture]
    public sealed class WotNativeOrganizesGraphTests
    {
        /// <summary>
        /// A node identifier target names an address-space node, so it is not a document dependency.
        /// </summary>
        [Test]
        public void OrganizesNativeNodeIdTargetIsNotADocumentDependency()
        {
            const string href = "nsu=http://opcfoundation.org/UA/DI/;i=15034";

            var references = WotDependencyGraph.ExtractReferences(Document(href), 64);

            Assert.That(references, Is.Empty);
        }

        /// <summary>
        /// A target that is not a node identifier remains a document dependency.
        /// </summary>
        [Test]
        public void OrganizesDocumentTargetRemainsADocumentDependency()
        {
            const string href = "urn:r30:device-features";

            var references = WotDependencyGraph.ExtractReferences(Document(href), 64);

            Assert.That(references, Has.Count.EqualTo(1));
            Assert.That(references.Single().Href, Is.EqualTo(href));
            Assert.That(references.Single().RefType, Is.EqualTo("ua:Organizes"));
        }

        private static byte[] Document(string href)
        {
            return Encoding.UTF8.GetBytes(
                "{\"links\":[{\"rel\":\"ua:Organizes\",\"href\":\"" + href + "\"}]}");
        }
    }
}