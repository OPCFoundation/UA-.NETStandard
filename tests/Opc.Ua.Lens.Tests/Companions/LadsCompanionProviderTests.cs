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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.LADS;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions;

/// <summary>
/// Tests LADS provider boundaries using the in-memory companion session.
/// </summary>
[TestFixture]
public sealed class LadsCompanionProviderTests
{
    /// <summary>
    /// The provider identifies the LADS model and exposes explicit task safety classifications.
    /// </summary>
    [Test]
    public async Task ProviderIdentifiesLadsModelAndRejectsUnknownTargetAsync()
    {
        var session = new IndustrialCompanionTestSession(includeModels: false);
        session.NamespaceUris.GetIndexOrAppend(LadsModel.NamespaceUri);
        var provider = new LadsCompanionProvider();
        NodeId device = session.Id("device");
        var target = new CompanionTarget("lads", device, "Device", "LADSDevice");
        var context = session.Context();

        Assert.That(provider.Descriptor.ModelUri, Is.EqualTo(LadsModel.NamespaceUri));
        await Assert.ThatAsync(
            async () => await provider.InspectAsync(context, target, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadNodeIdUnknown))
            .ConfigureAwait(false);
        Assert.That(session.BrowseCalls.IsEmpty, Is.True);
    }

    /// <summary>
    /// Discovery rejects a session that does not publish the LADS namespace before browsing.
    /// </summary>
    [Test]
    public async Task DiscoveryRejectsSessionsWithoutLadsNamespaceAsync()
    {
        var session = new IndustrialCompanionTestSession(includeModels: false);
        var provider = new LadsCompanionProvider();

        await Assert.ThatAsync(
            async () => await provider.DiscoverAsync(session.Context(), CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadNotSupported))
            .ConfigureAwait(false);
        Assert.That(session.BrowseCalls.IsEmpty, Is.True);
    }

    /// <summary>
    /// Unsupported operations cannot be used as an alternate path to control a LADS device.
    /// </summary>
    [Test]
    public async Task ExecuteRejectsUnlistedCommandsBeforeServiceCallsAsync()
    {
        var session = new IndustrialCompanionTestSession(includeModels: false);
        var provider = new LadsCompanionProvider();
        var target = new CompanionTarget("lads", session.Id("device"), "Device", "LADSDevice");

        await Assert.ThatAsync(
            async () => await provider.ExecuteAsync(
                session.Context(), target, "start", null, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadNotSupported))
            .ConfigureAwait(false);
        Assert.That(session.BrowseCalls.IsEmpty, Is.True);
        Assert.That(session.Calls.IsEmpty, Is.True);
    }
}
