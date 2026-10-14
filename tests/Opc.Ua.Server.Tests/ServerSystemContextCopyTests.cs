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

using System;
using System.Reflection;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies that copies of a <see cref="ServerSystemContext"/> carry every field of the
    /// original.
    /// </summary>
    [TestFixture]
    [Category("ServerSystemContext")]
    [Parallelizable]
    public class ServerSystemContextCopyTests
    {
        /// <summary>
        /// Every instance field of the context hierarchy is copied by the copy constructor.
        /// A field added later without a copy fails here.
        /// </summary>
        [Test]
        public void CopyCopiesEveryField()
        {
            ServerSystemContext template = CreatePopulatedContext();

            ServerSystemContext copy = template.Copy();

            Assert.That(copy, Is.Not.SameAs(template));
            Assert.That(copy.GetType(), Is.EqualTo(typeof(ServerSystemContext)));
            for (Type? type = typeof(ServerSystemContext);
                type != null && type != typeof(object);
                type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    // the per-request cache marker is deliberately not copied.
                    if (field.Name.StartsWith("<" + nameof(ServerSystemContext.CopiedFrom) + ">", StringComparison.Ordinal))
                    {
                        Assert.That(copy.CopiedFrom, Is.Null);
                        continue;
                    }

                    object? expected = field.GetValue(template);
                    Assert.That(
                        expected,
                        Is.Not.Null.And.Not.EqualTo(DefaultOf(field.FieldType)),
                        $"Give {type.Name}.{field.Name} a non-default value in this test.");
                    Assert.That(
                        field.GetValue(copy),
                        Is.EqualTo(expected),
                        $"{type.Name}.{field.Name} is not copied.");
                }
            }
        }

        /// <summary>
        /// Copying for an operation keeps every value of the template and binds the operation.
        /// </summary>
        [Test]
        public void CopyForOperationBindsTheOperation()
        {
            ServerSystemContext template = CreatePopulatedContext();
            var operation = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);

            ServerSystemContext copy = template.Copy(operation);

            Assert.That(copy.OperationContext, Is.SameAs(operation));
            Assert.That(template.OperationContext, Is.Not.SameAs(operation));
            Assert.That(copy.Server, Is.SameAs(template.Server));
            Assert.That(copy.SystemHandle, Is.SameAs(template.SystemHandle));
            Assert.That(copy.NodeIdFactory, Is.SameAs(template.NodeIdFactory));
            Assert.That(copy.NodeStateFactory, Is.SameAs(template.NodeStateFactory));
            Assert.That(copy.TypeTable, Is.SameAs(template.TypeTable));
        }

        /// <summary>
        /// A derived context keeps its type and its own fields when copied.
        /// </summary>
        [Test]
        public void CopyOfDerivedContextKeepsItsType()
        {
            var template = new DerivedContext(CreateServer().Object) { Extra = "extra" };

            ServerSystemContext copy = template.Copy();

            Assert.That(copy, Is.TypeOf<DerivedContext>());
            Assert.That(((DerivedContext)copy).Extra, Is.EqualTo("extra"));
            Assert.That(copy, Is.Not.SameAs(template));
        }

        private static ServerSystemContext CreatePopulatedContext()
        {
            Mock<IServerInternal> server = CreateServer();
            var bound = new OperationContext(new RequestHeader(), null, RequestType.Browse, RequestLifetime.None);
            var template = new ServerSystemContext(server.Object, bound)
            {
                SystemHandle = new object(),
                SessionId = new NodeId(42),
                UserIdentity = new Mock<IUserIdentity>().Object,
                PreferredLocales = ["en-US"],
                AuditEntryId = "audit",
                NodeIdFactory = new Mock<INodeIdFactory>().Object,
                NodeStateFactory = new NodeStateFactory()
            };
            return template;
        }

        private static Mock<IServerInternal> CreateServer()
        {
            var server = new Mock<IServerInternal>();
            var namespaces = new NamespaceTable();
            server.Setup(s => s.NamespaceUris).Returns(namespaces);
            server.Setup(s => s.ServerUris).Returns(new StringTable());
            server.Setup(s => s.TypeTree).Returns(new TypeTable(namespaces));
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.Telemetry).Returns(new Mock<ITelemetryContext>().Object);
            return server;
        }

        private static object? DefaultOf(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        private sealed class DerivedContext : ServerSystemContext
        {
            public DerivedContext(IServerInternal server)
                : base(server)
            {
            }

            public string? Extra { get; set; }
        }
    }
}
