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


using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for the per-request system context copy cached on <see cref="OperationContext"/>,
    /// which node managers reuse for the per-node callbacks of one request.
    /// </summary>
    [TestFixture]
    [Category("OperationContext")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class OperationContextSystemContextCacheTests
    {
        /// <summary>
        /// Repeated lookups with the same template return the same copy, bound to the operation.
        /// </summary>
        [Test]
        public void SameTemplateReturnsOneCopyBoundToTheOperation()
        {
            ServerSystemContext template = CreateTemplate();
            using OperationContext operation = CreateOperation();

            ServerSystemContext first = operation.GetSystemContext(template);
            ServerSystemContext second = operation.GetSystemContext(template);

            Assert.That(second, Is.SameAs(first));
            Assert.That(first, Is.Not.SameAs(template));
            Assert.That(first.OperationContext, Is.SameAs(operation));
        }

        /// <summary>
        /// A copy made from one node manager's template is never handed to another node manager.
        /// </summary>
        [Test]
        public void SwitchingTemplatesNeverReturnsAnotherManagersCopy()
        {
            ServerSystemContext templateA = CreateTemplate(systemHandle: "A");
            ServerSystemContext templateB = CreateTemplate(systemHandle: "B");
            using OperationContext operation = CreateOperation();

            ServerSystemContext a1 = operation.GetSystemContext(templateA);
            ServerSystemContext b1 = operation.GetSystemContext(templateB);
            ServerSystemContext a2 = operation.GetSystemContext(templateA);

            Assert.That(a1.SystemHandle, Is.EqualTo("A"));
            Assert.That(b1.SystemHandle, Is.EqualTo("B"));
            Assert.That(a2.SystemHandle, Is.EqualTo("A"));
            Assert.That(b1, Is.Not.SameAs(a1));
            Assert.That(b1.OperationContext, Is.SameAs(operation));
            Assert.That(a2.OperationContext, Is.SameAs(operation));
        }

        /// <summary>
        /// Concurrent lookups with alternating templates always get a copy of the template they
        /// passed, bound to the same operation.
        /// </summary>
        [Test]
        public void ConcurrentLookupsAlwaysReturnACopyOfTheRequestedTemplate()
        {
            ServerSystemContext[] templates =
            [
                CreateTemplate(systemHandle: "A"),
                CreateTemplate(systemHandle: "B"),
                CreateTemplate(systemHandle: "C")
            ];
            using OperationContext operation = CreateOperation();

            int mismatches = 0;
            Parallel.For(0, 30000, ii =>
            {
                ServerSystemContext template = templates[ii % templates.Length];
                ServerSystemContext copy = operation.GetSystemContext(template);
                if (!Equals(copy.SystemHandle, template.SystemHandle) ||
                    !ReferenceEquals(copy.OperationContext, operation))
                {
                    System.Threading.Interlocked.Increment(ref mismatches);
                }
            });

            Assert.That(mismatches, Is.Zero);
            Assert.That(
                templates.Select(t => operation.GetSystemContext(t).SystemHandle),
                Is.EqualTo(new object[] { "A", "B", "C" }));
        }

        private static ServerSystemContext CreateTemplate(object? systemHandle = null)
        {
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            server.Setup(s => s.ServerUris).Returns(new StringTable());
            return new ServerSystemContext(server.Object) { SystemHandle = systemHandle };
        }

        private static OperationContext CreateOperation()
        {
            return new OperationContext(
                new RequestHeader(),
                null,
                RequestType.Read,
                RequestLifetime.None);
        }
    }
}
