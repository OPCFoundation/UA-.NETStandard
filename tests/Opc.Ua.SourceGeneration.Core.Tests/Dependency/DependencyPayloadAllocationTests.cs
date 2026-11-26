// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.Schema.Model;
using Opc.Ua.SourceGeneration.Dependency;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Verifies allocation reductions without changing payload or materialization semantics.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    public class DependencyPayloadAllocationTests
    {
        /// <summary>
        /// Repeated identifiers are decoded once, while long and non-ASCII strings stay intact.
        /// </summary>
        [Test]
        public void ShortStringsAreReusedWithoutTemporaryStringAllocations()
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                for (int index = 0; index < 1000; index++)
                {
                    writer.Write("http://opcfoundation.org/UA/");
                }
                writer.Write("\u00E9\U0001F642");
                writer.Write(new string('x', 2048));
            }
            stream.Position = 0;
            using var reader = new DependencyBinaryReader(stream, CancellationToken.None);
            string first = reader.ReadString();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 1; index < 1000; index++)
            {
                string value = reader.ReadString();
                if (!ReferenceEquals(value, first))
                {
                    Assert.Fail("Repeated identifiers must use the decode-scoped canonical string.");
                }
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
            Assert.That(reader.ReadString(), Is.EqualTo("\u00E9\U0001F642"));
            Assert.That(reader.ReadString(), Is.EqualTo(new string('x', 2048)));
        }

        /// <summary>
        /// Hash collisions must not merge distinct identifier contents.
        /// </summary>
        [Test]
        public void StringHashCollisionsPreserveDistinctValues()
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write("Aa");
                writer.Write("BB");
                writer.Write("Aa");
            }
            stream.Position = 0;
            using var reader = new DependencyBinaryReader(stream, CancellationToken.None);
            string first = reader.ReadString();

            Assert.That(reader.ReadString(), Is.EqualTo("BB"));
            Assert.That(reader.ReadString(), Is.SameAs(first));
        }

        /// <summary>
        /// Direct compression must not introduce a flush block into the embedded metadata.
        /// </summary>
        [Test]
        public void DirectCompressionMatchesBufferedCompression()
        {
            var model = new ModelDependencyV1 { ModelUri = "urn:compression" };
            model.Nodes.Add(new DependencyNode
            {
                SymbolicName = "Example",
                SymbolicNamespace = model.ModelUri,
                Kind = DependencyNodeKind.ObjectType
            });
            byte[] actual = Convert.FromBase64String(model.ToBase64Payload());
            using var input = new MemoryStream(actual, 4, actual.Length - 4, writable: false);
            using var inflater = new DeflateStream(input, CompressionMode.Decompress);
            using var raw = new MemoryStream();
            inflater.CopyTo(raw);
            raw.Position = 0;
            using var expected = new MemoryStream();
            expected.Write(actual, 0, 4);
            using (var deflater = new DeflateStream(expected, CompressionLevel.Optimal, leaveOpen: true))
            {
                raw.CopyTo(deflater);
            }

            Assert.That(actual, Is.EqualTo(expected.ToArray()));
        }

        /// <summary>
        /// Model validation must never mutate ACLs retained by a shared decoded graph.
        /// </summary>
        [Test]
        public void SecurityMaterializationDetachesMutablePermissions()
        {
            var security = new DependencySecurity
            {
                RolePermissions = new RolePermissionSet
                {
                    RolePermission =
                    [
                        new RolePermission
                        {
                            RoleId = "i=15644",
                            Permission = [Permissions.Browse]
                        }
                    ]
                }
            };
            var first = new ObjectTypeDesign();
            var second = new ObjectTypeDesign();
            security.ApplyTo(first);
            security.ApplyTo(second);
            first.RolePermissions.RolePermission[0].RoleId = "i=15656";
            first.RolePermissions.RolePermission[0].Permission[0] = Permissions.Read;

            Assert.That(second.RolePermissions.RolePermission[0].RoleId, Is.EqualTo("i=15644"));
            Assert.That(second.RolePermissions.RolePermission[0].Permission, Is.EqualTo([Permissions.Browse]));
            Assert.That(security.RolePermissions.RolePermission[0].RoleId, Is.EqualTo("i=15644"));
        }

        /// <summary>
        /// A token cancelled after inflation starts still interrupts the inner string-reading loop.
        /// </summary>
        [Test]
        public void ReaderObservesCancellationBetweenStrings()
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write("first");
                writer.Write("second");
            }
            stream.Position = 0;
            using var cancellation = new CancellationTokenSource();
            using var reader = new DependencyBinaryReader(stream, cancellation.Token);
            Assert.That(reader.ReadString(), Is.EqualTo("first"));
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => reader.ReadString());
        }
    }
}
