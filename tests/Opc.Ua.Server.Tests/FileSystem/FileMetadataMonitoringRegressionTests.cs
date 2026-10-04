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

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Tests.NodeManager;

namespace Opc.Ua.Server.Tests.FileSystem
{
    [TestFixture]
    [Category("FileSystem")]
    public sealed class FileMetadataMonitoringRegressionTests
    {
        [Test]
        public async Task FirstSizeNotificationContainsActualFileSizeWithoutRetainingHandlesAsync()
        {
            string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "payload.bin");
            try
            {
                using (var writer = new StreamWriter(path))
                {
                    await writer.WriteAsync(new string('x', 10240)).ConfigureAwait(false);
                }

                Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
                using (queues)
                using (var manager = new FileSystemNodeManager(
                    server.Object,
                    new ApplicationConfiguration
                    {
                        ServerConfiguration = new ServerConfiguration { MaxNotificationQueueSize = 100 }
                    },
                    new PhysicalFileSystemProvider(directory, "Metadata")))
                {
                    await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                        .ConfigureAwait(false);
                    var request = new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = new FileSystemNodeId(
                                FileSystemNodeId.File, "payload.bin", manager.NamespaceIndex, BrowseNames.Size)
                                .ToNodeId(),
                            AttributeId = Attributes.Value
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 0,
                            QueueSize = 1,
                            DiscardOldest = true
                        }
                    };
                    var errors = new List<ServiceResult> { null };
                    var filterErrors = new List<MonitoringFilterResult> { null };
                    var items = new List<IMonitoredItem> { null };
                    using var createContext = new OperationContext(
                        new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
                    await manager.CreateMonitoredItemsAsync(
                        createContext, 1, 100, TimestampsToReturn.Both, [request],
                        errors, filterErrors, items, false, new MonitoredItemIdFactory()).ConfigureAwait(false);

                    Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(items[0], Is.InstanceOf<MonitoredItem>());
                    using var item = (MonitoredItem)items[0];
                    var notifications = new Queue<MonitoredItemNotification>();
                    using var publishContext = new OperationContext(
                        new RequestHeader(), null, RequestType.Publish, RequestLifetime.None);
                    item.Publish(publishContext, notifications, new Queue<DiagnosticInfo>(), 10, NullLogger.Instance);

                    Assert.That(notifications, Has.Count.EqualTo(1));
                    DataValue first = notifications.Dequeue().Value;
                    Assert.Multiple(() =>
                    {
                        Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(first.WrappedValue.TryGetValue(out ulong size), Is.True);
                        Assert.That(size, Is.EqualTo(10240));
                        Assert.That(manager.TrackedFileCount, Is.Zero);
                    });
                }
            }
            finally
            {
                File.Delete(path);
                Directory.Delete(directory);
            }
        }
    }
}
