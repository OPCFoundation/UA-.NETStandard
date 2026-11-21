// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Di.Server.Hosting;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Unit tests for <see cref="DiAddressSpaceOwnershipExtensions.ClaimDiAddressSpace"/>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    public sealed class DiAddressSpaceOwnershipTests
    {
        [Test]
        public void ClaimSucceedsOnceAndRegistersMarker()
        {
            var services = new ServiceCollection();

            services.ClaimDiAddressSpace("OwnerA");

            Assert.That(services, Has.Count.EqualTo(1));
        }

        [Test]
        public void SecondClaimByDifferentOwnerNamesCurrentOwner()
        {
            var services = new ServiceCollection();
            services.ClaimDiAddressSpace("OwnerA");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => services.ClaimDiAddressSpace("OwnerB", "Use OwnerA."));

            Assert.That(ex!.Message, Does.Contain("OwnerA"));
            Assert.That(ex.Message, Does.Contain("OwnerB"));
            Assert.That(ex.Message, Does.Contain("Use OwnerA."));
        }

        [Test]
        public void SecondClaimBySameOwnerReportsDuplicateCall()
        {
            var services = new ServiceCollection();
            services.ClaimDiAddressSpace("OwnerA");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => services.ClaimDiAddressSpace("OwnerA"));

            Assert.That(ex!.Message, Does.Contain("already been called"));
        }

        [Test]
        public void ClaimRejectsNullServices()
        {
            Assert.Throws<ArgumentNullException>(
                () => DiAddressSpaceOwnershipExtensions.ClaimDiAddressSpace(null!, "OwnerA"));
        }

        [Test]
        public void ClaimReturnsTheOriginalCollectionAndRecordsOwnerName()
        {
            var services = new ServiceCollection();
            const string ownerName = "MachineryOwner";

            IServiceCollection result = services.ClaimDiAddressSpace(ownerName);
            DiAddressSpaceOwnership marker = services
                .Single(descriptor => descriptor.ServiceType == typeof(DiAddressSpaceOwnership))
                .ImplementationInstance as DiAddressSpaceOwnership
                ?? throw new AssertionException("The ownership descriptor must contain its marker.");

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.SameAs(services));
                Assert.That(services, Has.Count.EqualTo(1));
                Assert.That(marker.OwnerName, Is.EqualTo(ownerName));
            });
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t ")]
        public void ClaimRejectsInvalidOwnerNameWithoutMutatingCollection(string? ownerName)
        {
            var services = new ServiceCollection();

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => services.ClaimDiAddressSpace(ownerName!))!;

            Assert.Multiple(() =>
            {
                Assert.That(exception.ParamName, Is.EqualTo("ownerName"));
                Assert.That(services, Is.Empty);
            });

            IServiceCollection result = services.ClaimDiAddressSpace("ValidOwner");
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.SameAs(services));
                Assert.That(services, Has.Count.EqualTo(1));
                Assert.That(
                    ((DiAddressSpaceOwnership)services.Single().ImplementationInstance!).OwnerName,
                    Is.EqualTo("ValidOwner"));
            });
        }
    }
}
