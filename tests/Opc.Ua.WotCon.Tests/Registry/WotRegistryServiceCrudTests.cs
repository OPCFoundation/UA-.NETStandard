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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Registry
{
    /// <summary>
    /// Exercises the xRegistry CRUD additions used by the OPC UA management
    /// Methods: group create/delete, resource placeholder create-or-get and
    /// document validation.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotRegistryServiceCrudTests
    {
        [Test]
        public async Task TryCreateGroup_CreatesThenFailsOnDuplicate()
        {
            using var service = new WotRegistryService();

            WotResourceGroup? first = await service.TryCreateGroupAsync(
                "sensors", WoTDocumentKindEnum.ThingDescription);
            WotResourceGroup? second = await service.TryCreateGroupAsync(
                "sensors", WoTDocumentKindEnum.ThingDescription);

            Assert.That(first, Is.Not.Null);
            Assert.That(first!.GroupId, Is.EqualTo("sensors"));
            Assert.That(second, Is.Null, "A second CreateGroup with the same id must fail.");
        }

        [Test]
        public async Task DeleteGroup_RemovesGroupAndResources()
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "sensors",
                ResourceId = "a",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = TestMaterialization.Td("urn:a")
            });

            WotRegistryMutationResult result = await service.DeleteGroupAsync("sensors");

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(service.Current.FindGroup("sensors"), Is.Null);
            Assert.That(service.Current.FindResource("sensors", "a"), Is.Null);
        }

        [Test]
        public async Task DeleteGroup_WithWrongEpoch_Rejected()
        {
            using var service = new WotRegistryService();
            WotResourceGroup group = await service.GetOrCreateGroupAsync(
                "sensors", WoTDocumentKindEnum.ThingDescription);

            WotRegistryMutationResult result = await service.DeleteGroupAsync(
                "sensors", expectedEpoch: group.Epoch + 999);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(service.Current.FindGroup("sensors"), Is.Not.Null);
        }

        [Test]
        public async Task GetOrCreateResource_CreatesPlaceholderThenReturnsExisting()
        {
            using var service = new WotRegistryService();

            (WotResource created, bool createdFlag) = await service.GetOrCreateResourceAsync(
                "sensors", "a", WoTDocumentKindEnum.ThingDescription);
            (WotResource fetched, bool fetchedFlag) = await service.GetOrCreateResourceAsync(
                "sensors", "a", WoTDocumentKindEnum.ThingDescription);

            Assert.That(createdFlag, Is.True);
            Assert.That(fetchedFlag, Is.False);
            Assert.That(created.Versions, Is.Empty, "A placeholder resource carries no versions.");
            Assert.That(created.DefaultVersion, Is.Null);
            Assert.That(fetched.ResourceId, Is.EqualTo("a"));
        }

        [Test]
        public async Task TryCreateResource_FailsWhenResourceExists()
        {
            using var service = new WotRegistryService();
            await service.TryCreateResourceAsync("sensors", "a", WoTDocumentKindEnum.ThingDescription);

            WotResource? duplicate = await service.TryCreateResourceAsync(
                "sensors", "a", WoTDocumentKindEnum.ThingDescription);

            Assert.That(duplicate, Is.Null);
        }

        [Test]
        public async Task Validate_ValidDocument_ReportsSuccess()
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "sensors",
                ResourceId = "a",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = TestMaterialization.Td("urn:a")
            });

            WoTValidationOutcomeDataType outcome = await service.ValidateResourceAsync("sensors", "a");

            Assert.That(outcome.FormatValidated, Is.True);
            Assert.That(outcome.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Success));
            WotResource? resource = service.Current.FindResource("sensors", "a");
            Assert.That(resource!.Validation, Is.Not.Null);
            Assert.That(resource.Validation!.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Success));
        }

        [Test]
        public async Task Validate_InvalidDocument_ReportsFailure()
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "sensors",
                ResourceId = "bad",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = TestMaterialization.InvalidJson()
            });

            WoTValidationOutcomeDataType outcome = await service.ValidateResourceAsync("sensors", "bad");

            Assert.That(outcome.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Failed));
        }

        [Test]
        public void Validate_NoDefaultVersion_Throws()
        {
            using var service = new WotRegistryService();
            _ = service.TryCreateResourceAsync(
                "sensors", "empty", WoTDocumentKindEnum.ThingDescription).AsTask().GetAwaiter().GetResult();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await service.ValidateResourceAsync("sensors", "empty"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }
    }
}
