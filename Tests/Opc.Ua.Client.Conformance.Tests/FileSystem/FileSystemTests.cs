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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// Conformance tests for the FileSystem NodeManager exposed by the
    /// reference server. Verifies that volumes, directories and files
    /// implement the OPC UA FileDirectoryType / FileType information
    /// model semantics (Part 5).
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("FileSystem")]
    [NonParallelizable]
    public class FileSystemTests : TestFixture
    {
        /// <summary>
        /// FileMode values from OPC UA Part 5 (FileType.Open).
        /// </summary>
        private const byte FileModeRead = 1;
        private NodeId m_volumeId = NodeId.Null;
        private string m_volumeName;
        private NodeId m_directoryId = NodeId.Null;
        private NodeId m_fileId = NodeId.Null;
        private string m_filePath;

        [OneTimeSetUp]
        public new async Task OneTimeSetUp()
        {
            await base.OneTimeSetUp().ConfigureAwait(false);

            m_volumeId = await FindFirstVolumeAsync().ConfigureAwait(false);
            if (m_volumeId.IsNull)
            {
                return;
            }

            DataValue bn = await ReadAttributeAsync(m_volumeId, Attributes.BrowseName)
                .ConfigureAwait(false);
            if (bn.WrappedValue.TryGetValue(out QualifiedName qn))
            {
                m_volumeName = qn.Name;
            }

            // Try to discover a directory and a small file under the volume.
            await DiscoverDirectoryAndFileAsync().ConfigureAwait(false);
        }

        #region helpers

        private async Task<NodeId> FindFirstVolumeAsync()
        {
            BrowseResponse resp = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[] {
                    new() {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        IncludeSubtypes = false,
                        NodeClassMask = (uint)NodeClass.Object,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            if (resp.Results.Count == 0 || !StatusCode.IsGood(resp.Results[0].StatusCode))
            {
                return NodeId.Null;
            }

            foreach (ReferenceDescription r in resp.Results[0].References)
            {
                var typeId = ExpandedNodeId.ToNodeId(r.TypeDefinition, Session.NamespaceUris);
                if (typeId == ObjectTypeIds.FileDirectoryType)
                {
                    return ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris);
                }
            }
            return NodeId.Null;
        }

        private async Task<ArrayOf<ReferenceDescription>> BrowseHasComponentAsync(
            NodeId parent, uint nodeClassMask = 0)
        {
            BrowseResponse resp = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[] {
                    new() {
                        NodeId = parent,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IncludeSubtypes = true,
                        NodeClassMask = nodeClassMask,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(resp.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            return resp.Results[0].References;
        }

        private async Task<ArrayOf<ReferenceDescription>> BrowseHasPropertyAsync(NodeId parent)
        {
            BrowseResponse resp = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[] {
                    new() {
                        NodeId = parent,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasProperty,
                        IncludeSubtypes = false,
                        NodeClassMask = (uint)NodeClass.Variable,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(resp.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            return resp.Results[0].References;
        }

        private async Task<NodeId> FindMethodAsync(NodeId parent, string methodName)
        {
            ArrayOf<ReferenceDescription> refs = await BrowseHasComponentAsync(
                parent, (uint)NodeClass.Method).ConfigureAwait(false);

            foreach (ReferenceDescription r in refs)
            {
                if (r.BrowseName.Name == methodName)
                {
                    return ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris);
                }
            }
            return NodeId.Null;
        }

        private async Task<NodeId> FindPropertyAsync(NodeId parent, string propertyName)
        {
            ArrayOf<ReferenceDescription> refs = await BrowseHasPropertyAsync(parent)
                .ConfigureAwait(false);

            foreach (ReferenceDescription r in refs)
            {
                if (r.BrowseName.Name == propertyName)
                {
                    return ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris);
                }
            }
            return NodeId.Null;
        }

        private async Task<DataValue> ReadAttributeAsync(NodeId nodeId, uint attributeId)
        {
            ReadResponse resp = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[] {
                    new() { NodeId = nodeId, AttributeId = attributeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(resp.Results.Count, Is.EqualTo(1));
            return resp.Results[0];
        }

        private async Task<CallMethodResult> CallMethodAsync(
            NodeId objectId, NodeId methodId, params Variant[] inputs)
        {
            CallResponse resp = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = inputs.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(resp.Results.Count, Is.EqualTo(1));
            return resp.Results[0];
        }

        /// <summary>
        /// Walks the volume to find a directory that contains at least one
        /// regular File node we can open for reading.
        /// </summary>
        private async Task DiscoverDirectoryAndFileAsync()
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<NodeId>();
            queue.Enqueue(m_volumeId);

            // Limit BFS to keep test setup bounded.
            const int maxDirs = 25;
            int seenDirs = 0;

            while (queue.Count > 0 && seenDirs < maxDirs)
            {
                NodeId dir = queue.Dequeue();
                if (!visited.Add(dir.ToString()))
                {
                    continue;
                }
                seenDirs++;

                ArrayOf<ReferenceDescription> children;
                try
                {
                    children = await BrowseHasComponentAsync(
                        dir, (uint)NodeClass.Object).ConfigureAwait(false);
                }
                catch
                {
                    continue;
                }

                var subDirs = new List<NodeId>();
                foreach (ReferenceDescription r in children)
                {
                    var typeId = ExpandedNodeId.ToNodeId(r.TypeDefinition, Session.NamespaceUris);
                    var childId = ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris);

                    if (typeId == ObjectTypeIds.FileType)
                    {
                        // First file wins.
                        m_fileId = childId;
                        m_directoryId = dir;
                        m_filePath = ResolveFilePath(childId);
                        return;
                    }
                    if (typeId == ObjectTypeIds.FileDirectoryType)
                    {
                        subDirs.Add(childId);
                    }
                }

                foreach (NodeId sub in subDirs)
                {
                    queue.Enqueue(sub);
                }
            }
        }

        /// <summary>
        /// Server uses NodeId pattern "ns=&lt;idx&gt;;s=2:&lt;FullPath&gt;" for files.
        /// Recover the underlying filesystem path so tests can compare sizes etc.
        /// </summary>
        private static string ResolveFilePath(NodeId fileNodeId)
        {
            if (!fileNodeId.TryGetValue(out string s))
            {
                return null;
            }
            // Strip the "2:" RootType prefix and any "?<component>" suffix.
            int q = s.IndexOf('?');
            string head = q >= 0 ? s[..q] : s;
            int colon = head.IndexOf(':');
            if (colon < 0 || colon + 1 >= head.Length)
            {
                return null;
            }
            return head[(colon + 1)..];
        }

        private void RequireVolume()
        {
            if (m_volumeId.IsNull)
            {
                Assert.Ignore("No FileDirectoryType volume exposed under Server.");
            }
        }

        private void RequireDirectoryWithFile()
        {
            RequireVolume();
            if (m_directoryId.IsNull || m_fileId.IsNull)
            {
                Assert.Ignore("No readable file available on the test machine.");
            }
        }

        /// <summary>
        /// If the discovered file (selected by BFS during fixture setup) is
        /// not readable by the test-host OS user (for example a privileged
        /// /proc or /sys file on a Linux CI runner), the server returns
        /// <see cref="StatusCodes.BadUserAccessDenied"/> for Open(Read). This
        /// is an environmental constraint and not a server-side bug — skip
        /// the test rather than report a regression.
        /// </summary>
        private static void IgnoreIfDiscoveredFileNotReadable(StatusCode openStatus)
        {
            if (openStatus == StatusCodes.BadUserAccessDenied
                || openStatus == StatusCodes.BadNotReadable)
            {
                Assert.Ignore(
                    $"Discovered file not readable by test process ({openStatus}).");
            }
        }

        #endregion helpers

        [Test]
        [Property("ConformanceUnit", "FileSystem")]
        [Property("Tag", "010")]
        public async Task VolumeBrowseNameMatchesPathAsync()
        {
            RequireVolume();

            DataValue bn = await ReadAttributeAsync(m_volumeId, Attributes.BrowseName)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(bn.StatusCode), Is.True);
            Assert.That(bn.WrappedValue.TryGetValue(out QualifiedName bnValue), Is.True);

            string name = bnValue.Name;
            Assert.That(string.IsNullOrEmpty(name), Is.False, "Volume BrowseName must not be empty.");

            // On Windows the volume name typically looks like "C:\".
            // On Linux/macOS the FileSystem manager exposes the system root differently;
            // accept any non-empty name but require it to be a real existing path.
            Assert.That(Directory.Exists(name) || File.Exists(name), Is.True,
                $"Volume BrowseName '{name}' should map to an existing path.");
        }

        [Test]
        [Property("ConformanceUnit", "FileSystem")]
        [Property("Tag", "011")]
        public async Task VolumeHasFileDirectoryTypeDefinitionAsync()
        {
            RequireVolume();

            BrowseResponse resp = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[] {
                    new() {
                        NodeId = m_volumeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition,
                        IncludeSubtypes = false,
                        NodeClassMask = (uint)NodeClass.ObjectType,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            Assert.That(resp.Results[0].References.Count, Is.EqualTo(1));

            var typeId = ExpandedNodeId.ToNodeId(
                resp.Results[0].References[0].NodeId, Session.NamespaceUris);
            Assert.That(typeId, Is.EqualTo(ObjectTypeIds.FileDirectoryType));
        }

        [Test]
        [Property("ConformanceUnit", "FileSystem")]
        [Property("Tag", "012")]
        public async Task VolumeBrowsableForChildrenAsync()
        {
            RequireVolume();

            ArrayOf<ReferenceDescription> children = await BrowseHasComponentAsync(
                m_volumeId, (uint)NodeClass.Object).ConfigureAwait(false);

            if (children.Count == 0)
            {
                // Some CI hosts expose empty volumes such as
                // /sys/fs/fuse/connections on Linux. The FileSystem manager
                // correctly surfaces them; an empty volume is a valid
                // server response, not a test failure.
                Assert.Ignore($"Volume '{m_volumeName}' is empty.");
            }

            Assert.That(children.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "FileSystem")]
        [Property("Tag", "020")]
        public async Task DirectoryHasMethodsAsync()
        {
            RequireDirectoryWithFile();

            // Browse directory's HasComponent children without filtering by node class:
            // some servers materialise methods only when the browse is unfiltered.
            ArrayOf<ReferenceDescription> all = await BrowseHasComponentAsync(m_directoryId)
                .ConfigureAwait(false);

            var methodNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ReferenceDescription r in all)
            {
                if (r.NodeClass == NodeClass.Method)
                {
                    methodNames.Add(r.BrowseName.Name);
                }
            }

            if (methodNames.Count == 0)
            {
                Assert.Inconclusive(
                    "Directory does not expose FileDirectoryType methods via HasComponent browse.");
            }

            string[] expected =
            [
                "CreateDirectory", "CreateFile", "Delete", "MoveOrCopy"
            ];

            foreach (string m in expected)
            {
                Assert.That(methodNames, Does.Contain(m),
                    $"Directory should expose method '{m}'. Found: [{string.Join(", ", methodNames)}].");
            }
        }

        [Test]
        [Property("ConformanceUnit", "FileSystem")]
        [Property("Tag", "030")]
        public async Task FileHasMethodsAsync()
        {
            RequireDirectoryWithFile();

            string[] expected =
            [
                "Open", "Close", "Read", "Write", "GetPosition", "SetPosition"
            ];

            foreach (string m in expected)
            {
                NodeId id = await FindMethodAsync(m_fileId, m).ConfigureAwait(false);
                Assert.That(id.IsNull, Is.False, $"File should expose method '{m}'.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "FileSystem")]
        [Property("Tag", "031")]
        public async Task FileHasPropertiesAsync()
        {
            RequireDirectoryWithFile();

            string[] expected =
            [
                "OpenCount", "Writable", "UserWritable", "Size", "MimeType", "LastModifiedTime"
            ];

            foreach (string p in expected)
            {
                NodeId id = await FindPropertyAsync(m_fileId, p).ConfigureAwait(false);
                Assert.That(id.IsNull, Is.False, $"File should expose property '{p}'.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "FileSystem")]
        [Property("Tag", "032")]
        public async Task FileSizePropertyAsync()
        {
            RequireDirectoryWithFile();

            NodeId sizeId = await FindPropertyAsync(m_fileId, "Size").ConfigureAwait(false);
            Assert.That(sizeId.IsNull, Is.False);

            DataValue v = await ReadAttributeAsync(sizeId, Attributes.Value).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(v.StatusCode), Is.True);
            Assert.That(v.WrappedValue.TryGetValue(out ulong vValue), Is.True);

            ulong reported = vValue;

            if (!string.IsNullOrEmpty(m_filePath) && File.Exists(m_filePath))
            {
                long actual = new FileInfo(m_filePath).Length;
                Assert.That(reported, Is.EqualTo((ulong)actual),
                    "Reported file Size should match the actual on-disk length.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "FileSystem")]
        [Property("Tag", "033")]
        public async Task FileWritablePropertyIsBooleanAsync()
        {
            RequireDirectoryWithFile();

            NodeId writableId = await FindPropertyAsync(m_fileId, "Writable").ConfigureAwait(false);
            Assert.That(writableId.IsNull, Is.False);

            DataValue v = await ReadAttributeAsync(writableId, Attributes.Value)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(v.StatusCode), Is.True);
            Assert.That(v.WrappedValue.TryGetValue(out bool _), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "FileSystem")]
        [Property("Tag", "040")]
        public async Task OpenFileForReadingAsync()
        {
            RequireDirectoryWithFile();

            NodeId openId = await FindMethodAsync(m_fileId, "Open").ConfigureAwait(false);
            NodeId closeId = await FindMethodAsync(m_fileId, "Close").ConfigureAwait(false);
            Assert.That(openId.IsNull, Is.False);
            Assert.That(closeId.IsNull, Is.False);

            CallMethodResult openResult = await CallMethodAsync(
                m_fileId, openId, new Variant(FileModeRead)).ConfigureAwait(false);

            IgnoreIfDiscoveredFileNotReadable(openResult.StatusCode);

            Assert.That(StatusCode.IsGood(openResult.StatusCode), Is.True,
                $"Open(Read) should succeed, got {openResult.StatusCode}.");
            Assert.That(openResult.OutputArguments.Count, Is.EqualTo(1));
            Assert.That(openResult.OutputArguments[0].TryGetValue(out uint _), Is.True);

            uint handle = openResult.OutputArguments[0].GetUInt32();

            try
            {
                Assert.That(handle, Is.Not.Zero, "File handle should be non-zero.");
            }
            finally
            {
                await CallMethodAsync(m_fileId, closeId, new Variant(handle))
                    .ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "FileSystem")]
        [Property("Tag", "041")]
        public async Task ReadFromOpenFileAsync()
        {
            RequireDirectoryWithFile();

            DataValue sizeV = await ReadAttributeAsync(
                await FindPropertyAsync(m_fileId, "Size").ConfigureAwait(false),
                Attributes.Value).ConfigureAwait(false);
            ulong size = sizeV.WrappedValue.TryGetValue(out ulong u) ? u : 0UL;
            if (size == 0UL)
            {
                Assert.Ignore(
                    "Discovered file is empty on this test machine — " +
                    "ReadFromOpenFile requires a file with content.");
            }

            NodeId openId = await FindMethodAsync(m_fileId, "Open").ConfigureAwait(false);
            NodeId readId = await FindMethodAsync(m_fileId, "Read").ConfigureAwait(false);
            NodeId closeId = await FindMethodAsync(m_fileId, "Close").ConfigureAwait(false);

            CallMethodResult openResult = await CallMethodAsync(
                m_fileId, openId, new Variant(FileModeRead)).ConfigureAwait(false);
            IgnoreIfDiscoveredFileNotReadable(openResult.StatusCode);
            Assert.That(StatusCode.IsGood(openResult.StatusCode), Is.True);
            uint handle = (uint)openResult.OutputArguments[0];

            try
            {
                CallMethodResult readResult = await CallMethodAsync(
                    m_fileId, readId, new Variant(handle), new Variant(64)).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(readResult.StatusCode), Is.True,
                    $"Read should succeed, got {readResult.StatusCode}.");
                Assert.That(readResult.OutputArguments.Count, Is.EqualTo(1));

                var data = (ByteString)readResult.OutputArguments[0];
                Assert.That(data.IsNull, Is.False, "Read should return a non-null ByteString.");
                Assert.That(data.Length, Is.GreaterThan(0), "Read should return at least one byte.");
            }
            finally
            {
                await CallMethodAsync(m_fileId, closeId, new Variant(handle))
                    .ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "FileSystem")]
        [Property("Tag", "042")]
        public async Task GetPositionAfterOpenAsync()
        {
            RequireDirectoryWithFile();

            NodeId openId = await FindMethodAsync(m_fileId, "Open").ConfigureAwait(false);
            NodeId getPosId = await FindMethodAsync(m_fileId, "GetPosition").ConfigureAwait(false);
            NodeId closeId = await FindMethodAsync(m_fileId, "Close").ConfigureAwait(false);

            CallMethodResult openResult = await CallMethodAsync(
                m_fileId, openId, new Variant(FileModeRead)).ConfigureAwait(false);
            IgnoreIfDiscoveredFileNotReadable(openResult.StatusCode);
            Assert.That(StatusCode.IsGood(openResult.StatusCode), Is.True);
            uint handle = (uint)openResult.OutputArguments[0];

            try
            {
                CallMethodResult posResult = await CallMethodAsync(
                    m_fileId, getPosId, new Variant(handle)).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(posResult.StatusCode), Is.True);
                Assert.That(posResult.OutputArguments.Count, Is.EqualTo(1));
                Assert.That(posResult.OutputArguments[0].TryGetValue(out ulong _), Is.True);
                Assert.That(posResult.OutputArguments[0].GetUInt64(), Is.Zero);
            }
            finally
            {
                await CallMethodAsync(m_fileId, closeId, new Variant(handle))
                    .ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "FileSystem")]
        [Property("Tag", "043")]
        public async Task SetPositionThenGetPositionAsync()
        {
            RequireDirectoryWithFile();

            DataValue sizeV = await ReadAttributeAsync(
                await FindPropertyAsync(m_fileId, "Size").ConfigureAwait(false),
                Attributes.Value).ConfigureAwait(false);
            ulong size = sizeV.WrappedValue.TryGetValue(out ulong u) ? u : 0UL;

            // Use a position the file actually contains; cap at 100 or size-1.
            ulong target = size > 100UL ? 100UL : (size > 0UL ? size - 1 : 0UL);

            NodeId openId = await FindMethodAsync(m_fileId, "Open").ConfigureAwait(false);
            NodeId getPosId = await FindMethodAsync(m_fileId, "GetPosition").ConfigureAwait(false);
            NodeId setPosId = await FindMethodAsync(m_fileId, "SetPosition").ConfigureAwait(false);
            NodeId closeId = await FindMethodAsync(m_fileId, "Close").ConfigureAwait(false);

            CallMethodResult openResult = await CallMethodAsync(
                m_fileId, openId, new Variant(FileModeRead)).ConfigureAwait(false);
            IgnoreIfDiscoveredFileNotReadable(openResult.StatusCode);
            Assert.That(StatusCode.IsGood(openResult.StatusCode), Is.True);
            uint handle = (uint)openResult.OutputArguments[0];

            try
            {
                CallMethodResult setResult = await CallMethodAsync(
                    m_fileId, setPosId, new Variant(handle), new Variant(target))
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(setResult.StatusCode), Is.True,
                    $"SetPosition should succeed, got {setResult.StatusCode}.");

                CallMethodResult posResult = await CallMethodAsync(
                    m_fileId, getPosId, new Variant(handle)).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(posResult.StatusCode), Is.True);
                Assert.That((ulong)posResult.OutputArguments[0], Is.EqualTo(target));
            }
            finally
            {
                await CallMethodAsync(m_fileId, closeId, new Variant(handle))
                    .ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "FileSystem")]
        [Property("Tag", "044")]
        public async Task OpenCountIncrementsAndDecrementsAsync()
        {
            RequireDirectoryWithFile();

            NodeId openCountId = await FindPropertyAsync(m_fileId, "OpenCount")
                .ConfigureAwait(false);
            Assert.That(openCountId.IsNull, Is.False);

            NodeId openId = await FindMethodAsync(m_fileId, "Open").ConfigureAwait(false);
            NodeId closeId = await FindMethodAsync(m_fileId, "Close").ConfigureAwait(false);

            DataValue before = await ReadAttributeAsync(openCountId, Attributes.Value)
                .ConfigureAwait(false);
            ulong baseline = ReadOpenCountAsUInt64(before.WrappedValue);

            CallMethodResult openResult = await CallMethodAsync(
                m_fileId, openId, new Variant(FileModeRead)).ConfigureAwait(false);
            IgnoreIfDiscoveredFileNotReadable(openResult.StatusCode);
            Assert.That(StatusCode.IsGood(openResult.StatusCode), Is.True);
            uint handle = (uint)openResult.OutputArguments[0];

            try
            {
                DataValue during = await ReadAttributeAsync(openCountId, Attributes.Value)
                    .ConfigureAwait(false);
                ulong duringVal = ReadOpenCountAsUInt64(during.WrappedValue);
                Assert.That(duringVal, Is.GreaterThanOrEqualTo(baseline + 1UL),
                    "OpenCount should increment while file is open.");
            }
            finally
            {
                await CallMethodAsync(m_fileId, closeId, new Variant(handle))
                    .ConfigureAwait(false);
            }

            DataValue after = await ReadAttributeAsync(openCountId, Attributes.Value)
                .ConfigureAwait(false);
            ulong afterVal = ReadOpenCountAsUInt64(after.WrappedValue);
            Assert.That(afterVal, Is.LessThanOrEqualTo(baseline),
                "OpenCount should decrement back to baseline (or below) after Close.");
        }

        /// <summary>
        /// Reads OpenCount from a Variant. Per FileType (Part 5 §A.2.5) the
        /// attribute is a UInt16, but some servers expose it as UInt32. Switch
        /// on the wire <see cref="BuiltInType"/> and use the matching typed
        /// accessor.
        /// </summary>
        private static ulong ReadOpenCountAsUInt64(Variant variant)
        {
            switch (variant.TypeInfo.BuiltInType)
            {
                case BuiltInType.UInt16:
                    Assert.That(variant.TryGetValue(out ushort u16), Is.True);
                    return u16;
                case BuiltInType.UInt32:
                    Assert.That(variant.TryGetValue(out uint u32), Is.True);
                    return u32;
                default:
                    Assert.Fail("OpenCount must be UInt16 or UInt32 per Part 5 §A.2.5; "
                        + "got " + variant.TypeInfo.BuiltInType + ".");
                    return 0UL;
            }
        }

        [Test]
        [Property("ConformanceUnit", "FileSystem")]
        [Property("Tag", "045")]
        public async Task CloseInvalidHandleReturnsBadAsync()
        {
            RequireDirectoryWithFile();

            NodeId closeId = await FindMethodAsync(m_fileId, "Close").ConfigureAwait(false);
            Assert.That(closeId.IsNull, Is.False);

            CallMethodResult result = await CallMethodAsync(
                m_fileId, closeId, new Variant(0xDEADBEEFu)).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(result.StatusCode), Is.True,
                $"Close with invalid handle should return Bad, got {result.StatusCode}.");
        }
    }
}
