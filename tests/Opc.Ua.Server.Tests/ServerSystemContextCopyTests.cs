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

                    // the values shared with the template must all be set as well.
                    if (field.Name == "m_values")
                    {
                        AssertEveryValueIsSet(expected!);
                    }
                }
            }
        }

        /// <summary>
        /// A copy for a request shares the template's values instead of copying each of
        /// them, so it is a small object.
        /// </summary>
        [Test]
        public void CopyForOperationIsSmall()
        {
            ServerSystemContext template = CreatePopulatedContext();
            var operation = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);

            // warm up the copy path.
            template.Copy(operation);

#if NET5_0_OR_GREATER
            long before = GC.GetAllocatedBytesForCurrentThread();
            ServerSystemContext copy = template.Copy(operation);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(copy.OperationContext, Is.SameAs(operation));
            Assert.That(allocated, Is.LessThanOrEqualTo(64), "a request's copy holds four references");
#else
            Assert.That(template.Copy(operation).OperationContext, Is.SameAs(operation));
#endif
        }

        /// <summary>
        /// A value changed on a copy stays on the copy, and a value changed on the template
        /// after the copy was made does not reach the copy: copies behave as snapshots.
        /// </summary>
        [Test]
        public void SharedValuesChangeOnlyWhereTheyAreSet()
        {
            ServerSystemContext template = CreatePopulatedContext();
            INodeIdFactory templateFactory = template.NodeIdFactory;
            object templateHandle = template.SystemHandle!;
            var operation = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);

            ServerSystemContext first = template.Copy(operation);
            ServerSystemContext second = template.Copy(operation);

            INodeIdFactory copyFactory = new Mock<INodeIdFactory>().Object;
            first.NodeIdFactory = copyFactory;
            Assert.That(first.NodeIdFactory, Is.SameAs(copyFactory));
            Assert.That(second.NodeIdFactory, Is.SameAs(templateFactory));
            Assert.That(template.NodeIdFactory, Is.SameAs(templateFactory));

            // the copy keeps every other value it shared.
            Assert.That(first.SystemHandle, Is.SameAs(templateHandle));
            Assert.That(first.TypeTable, Is.SameAs(template.TypeTable));

            var newHandle = new object();
            template.SystemHandle = newHandle;
            Assert.That(template.SystemHandle, Is.SameAs(newHandle));
            Assert.That(first.SystemHandle, Is.SameAs(templateHandle));
            Assert.That(second.SystemHandle, Is.SameAs(templateHandle));

            // a copy made now sees the template's new value.
            Assert.That(template.Copy(operation).SystemHandle, Is.SameAs(newHandle));
        }

        /// <summary>
        /// A copy for a session sets the session's values on the copy only.
        /// </summary>
        [Test]
        public void CopyForSessionSetsSessionValuesOnTheCopyOnly()
        {
            Mock<IServerInternal> server = CreateServer();
            var template = new ServerSystemContext(server.Object) { SessionId = new NodeId(7) };
            var identity = new Mock<IUserIdentity>().Object;
            var session = new Mock<ISession>();
            session.SetupGet(s => s.Id).Returns(new NodeId(99));
            session.SetupGet(s => s.Identity).Returns(identity);
            session.SetupGet(s => s.PreferredLocales).Returns(["de-DE"]);

            ServerSystemContext copy = template.Copy(session.Object);

            Assert.That(copy.SessionId, Is.EqualTo(new NodeId(99)));
            Assert.That(copy.UserIdentity, Is.SameAs(identity));
            Assert.That(copy.PreferredLocales.Count, Is.EqualTo(1));
            Assert.That(copy.PreferredLocales[0], Is.EqualTo("de-DE"));
            Assert.That(template.SessionId, Is.EqualTo(new NodeId(7)));
            Assert.That(template.UserIdentity, Is.Null);
            Assert.That(template.PreferredLocales.IsNull || template.PreferredLocales.Count == 0, Is.True);
        }

        /// <summary>
        /// A derived context, copied member-wise, is isolated from its template in both
        /// directions as well.
        /// </summary>
        [Test]
        public void CopyOfDerivedContextIsIsolatedFromItsTemplate()
        {
            var template = new DerivedContext(CreateServer().Object) { Extra = "extra" };
            NamespaceTable namespaces = template.NamespaceUris;

            var copy = (DerivedContext)template.Copy();
            copy.NamespaceUris = new NamespaceTable();
            Assert.That(template.NamespaceUris, Is.SameAs(namespaces));

            object handle = new();
            template.SystemHandle = handle;
            Assert.That(copy.SystemHandle, Is.Null);
            Assert.That(copy.Extra, Is.EqualTo("extra"));
        }

        /// <summary>
        /// Copies of copies keep sharing; changing the innermost one changes nothing else.
        /// </summary>
        [Test]
        public void CopyOfCopyIsIsolated()
        {
            ServerSystemContext template = CreatePopulatedContext();
            ServerSystemContext copy = template.Copy();
            ServerSystemContext copyOfCopy = copy.Copy();
            object handle = template.SystemHandle!;

            // SystemHandle is stored by the context, not forwarded to the operation.
            var inner = new object();
            copyOfCopy.SystemHandle = inner;
            Assert.That(copyOfCopy.SystemHandle, Is.SameAs(inner));
            Assert.That(copy.SystemHandle, Is.SameAs(handle));
            Assert.That(template.SystemHandle, Is.SameAs(handle));
        }

        /// <summary>
        /// A derived context that copies itself with a plain member-wise clone stays
        /// isolated from its source, in both directions.
        /// </summary>
        [Test]
        public void PlainMemberwiseCloneOfDerivedContextIsASnapshot()
        {
            var source = new DerivedContext(CreateServer().Object) { SystemHandle = "before" };
            NamespaceTable namespaces = source.NamespaceUris;

            DerivedContext clone = source.PlainClone();

            source.SystemHandle = "after";
            source.NamespaceUris = new NamespaceTable();
            Assert.That(clone.SystemHandle, Is.EqualTo("before"));
            Assert.That(clone.NamespaceUris, Is.SameAs(namespaces));

            clone.SystemHandle = "clone";
            Assert.That(source.SystemHandle, Is.EqualTo("after"));
        }

        private static void AssertEveryValueIsSet(object values)
        {
            foreach (FieldInfo field in values.GetType().GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {

                Assert.That(
                    field.GetValue(values),
                    Is.Not.Null.And.Not.EqualTo(DefaultOf(field.FieldType)),
                    $"Give the shared value {field.Name} a non-default value in this test.");
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

            /// <summary>
            /// Copies the context the way existing derived types do.
            /// </summary>
            public DerivedContext PlainClone()
            {
                return (DerivedContext)MemberwiseClone();
            }
        }
    }
}
