/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    [TestFixture]
    [Category("XRegistry")]
    public sealed class RegistryNativeActivationTests
    {
        [Test]
        public async Task NoOpCannotReportSuccessWhileDurableProjectionIsPendingAsync()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(null);
            context.NamespaceUris.GetIndexOrAppend(Namespaces.xRegistry);
            context.Factory.Builder.AddOpcUaXRegistry().Commit();
            var mapper = new RegistryRecordMapper(RegistrySharedNativeCatalog.Catalog, context);
            await using var storage = new MemoryRegistryStateStore();
            using var host = new RegistryNativeHost(new RegistryNativeHostOptions
            {
                Store = storage,
                Mapper = mapper,
                MessageContext = context,
                Collections = ["messagegroups"],
                RegistryRecordType = nameof(RegistryMetadataDataType),
                GroupRecordType = _ => nameof(RegistryMetadataDataType),
                ResourceRecordType = nameof(RegistryMetadataDataType),
                InitialDocument = (RegistryObjectValueDataType)RegistryValues.Parse(
                    """{"registryid":"test","epoch":1,"specversion":"1.0-rc4"}"""u8)
            });
            await host.StartAsync().ConfigureAwait(false);
            host.Activation = (_, _) => throw new InvalidOperationException("Injected activation failure.");
            RegistryMutationResultDataType committed = await host.WriteAsync(new RegistryWriteRequestDataType
            {
                TargetXid = "/messagegroups/g",
                Definition = mapper.Project(
                    (RegistryObjectValueDataType)RegistryValues.Parse("""{"messagegroupid":"g"}"""u8),
                    nameof(RegistryMetadataDataType))
            }).ConfigureAwait(false);
            Assert.That(committed.StatusCode, Is.EqualTo(StatusCodes.UncertainNotAllNodesAvailable));
            ulong revision = host.Current.Revision;
            var noOp = new RegistryChangeRequestDataType
            {
                TargetXid = "/messagegroups/g",
                ExpectedEpoch = committed.Epoch,
                Changes = []
            };
            RegistryMutationResultDataType retry = await host.ApplyChangesAsync(noOp).ConfigureAwait(false);
            Assert.That(retry.StatusCode, Is.EqualTo(StatusCodes.UncertainNotAllNodesAvailable));
            Assert.That(retry.Issues[0].Code, Is.EqualTo("E_PROJECTION_PENDING"));
            Assert.That(host.Current.Revision, Is.EqualTo(revision));
            Assert.That((await storage.ReadAsync().ConfigureAwait(false)).Revision, Is.EqualTo(revision));

            host.Activation = (_, _) => default;
            RegistryMutationResultDataType recovered = await host.ApplyChangesAsync(new RegistryChangeRequestDataType
            {
                TargetXid = noOp.TargetXid,
                ExpectedEpoch = committed.Epoch,
                Changes =
                [
                    new RegistryChangeDataType
                    {
                        Operation = 0, Path = [new RegistryPathElementDataType { Kind = 0, Name = "description" }],
                        Value = new RegistryStringValueDataType { Kind = 2, Value = "Recovered" }
                    }
                ]
            }).ConfigureAwait(false);
            Assert.That(recovered.StatusCode, Is.EqualTo(StatusCodes.Good));
            noOp.ExpectedEpoch = recovered.Epoch;
            Assert.That((await host.ApplyChangesAsync(noOp).ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(host.Current.Revision, Is.EqualTo(revision + 1));
        }
    }
}
