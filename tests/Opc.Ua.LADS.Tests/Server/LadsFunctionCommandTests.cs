/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Moq;
using NUnit.Framework;
using Opc.Ua.LADS.Server;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Verifies parameter identity and the retained scalar command contract without hosting a server.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    public sealed class LadsFunctionCommandTests
    {
        /// <summary>
        /// Named commands preserve the exact escaped member identity and argument.
        /// </summary>
        [Test]
        public void NamedCommandPreservesIdentityAndValue()
        {
            ILadsFunction function = Mock.Of<ILadsFunction>();
            const string path = "ControllerModeSet/Heat%2FHigh/TargetValue";
            var command = new LadsFunctionCommand(
                function, LadsFunctionCommandKind.SetParameter, Variant.From(12.5), path);

            Assert.That(command.Function, Is.SameAs(function));
            Assert.That(command.Kind, Is.EqualTo(LadsFunctionCommandKind.SetParameter));
            Assert.That(command.ParameterPath, Is.EqualTo(path));
            Assert.That(command.Value, Is.EqualTo(Variant.From(12.5)));
        }

        /// <summary>
        /// A named write must identify its member.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t")]
        public void NamedCommandRejectsMissingPath(string? path)
        {
            ILadsFunction function = Mock.Of<ILadsFunction>();
            Assert.Throws<ArgumentException>(() => new LadsFunctionCommand(
                function, LadsFunctionCommandKind.SetParameter, Variant.From(1.0), path));
        }

        /// <summary>
        /// Scalar commands retain their existing constructor and do not accept a named parameter identity.
        /// </summary>
        [TestCase(LadsFunctionCommandKind.Start)]
        [TestCase(LadsFunctionCommandKind.SetTargetValue)]
        [TestCase(LadsFunctionCommandKind.ResetTotalizer)]
        [TestCase(LadsFunctionCommandKind.SetMode)]
        public void ScalarCommandRetainsExistingContract(LadsFunctionCommandKind kind)
        {
            ILadsFunction function = Mock.Of<ILadsFunction>();
            var command = new LadsFunctionCommand(function, kind, Variant.From(7.5));

            Assert.That(command.Function, Is.SameAs(function));
            Assert.That(command.Kind, Is.EqualTo(kind));
            Assert.That(command.Value, Is.EqualTo(Variant.From(7.5)));
            Assert.That(command.ParameterPath, Is.Null);
            Assert.Throws<ArgumentException>(() => new LadsFunctionCommand(
                function, kind, Variant.From(7.5), "ControllerTuningParameter/CtrlP"));
        }

        /// <summary>
        /// Both construction paths require a function handle.
        /// </summary>
        [Test]
        public void CommandRejectsMissingFunction()
        {
            Assert.Throws<ArgumentNullException>(() => new LadsFunctionCommand(
                null!, LadsFunctionCommandKind.SetTargetValue));
            Assert.Throws<ArgumentNullException>(() => new LadsFunctionCommand(
                null!, LadsFunctionCommandKind.SetParameter, Variant.From(1.0), "ControllerTuningParameter/CtrlP"));
        }
    }
}
