// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for Address Space Atomicity.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AddressSpaceModel")]
    public class AddressSpaceAtomicityTests : TestFixture
    {
        [Description("Atomicity flags in the AccessLevelEx are used.")]
        [Test]
        public async Task AtomicityFlagsInAccessLevelExAreUsedAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = ToNodeId(Constants.ScalarStaticInt32), AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }
    }
}
