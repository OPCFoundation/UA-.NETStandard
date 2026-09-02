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

using System.Collections.Generic;
using System.Linq;
using Generators;
using NUnit.Framework;
using Opc.Ua.Generators;
using GeneratorModel = Opc.Ua.Generators;

namespace Opc.Ua.OpenUsd.Tests.Generator
{
    /// <summary>
    /// Holds the protection alarm nodes to the shape a client can actually read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ProtectionFunction</c> is mandatory on <c>GeneratorProtectionAlarmType</c>
    /// and so is materialised by the generated factory. <c>IsShutdown</c> and
    /// <c>SubsystemName</c> are <b>optional</b> and are not. Writing to an optional
    /// member with <c>CreateOrReplace</c> alone produces a child with no
    /// <c>ReferenceTypeId</c> and no NodeId - it appears in <c>GetChildren</c>, so
    /// the code looks right, but there is no reference for a browse to follow and
    /// the property is simply absent from a client's view.
    /// </para>
    /// <para>
    /// That was a real defect here, found by reading the server with a client rather
    /// than by reading the code: every write succeeded. These tests assert the node
    /// shape - specifically that each published member carries the reference that
    /// makes it reachable - instead of asserting the assignment.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("Generators")]
    public sealed class GeneratorProtectionAlarmNodeTests
    {
        /// <summary>
        /// The factory leaves the optional members unmaterialised.
        /// </summary>
        /// <remarks>
        /// If this ever starts failing, the opt-in below became unnecessary rather
        /// than wrong - but it is the premise the rest of this fixture rests on, so
        /// it is worth stating out loud.
        /// </remarks>
        [Test]
        public void TheFactoryDoesNotMaterialiseOptionalMembers()
        {
            SystemContext context = CreateContext();
            GeneratorModel.GeneratorProtectionAlarmState alarm = CreateAlarm(context);

            Assert.Multiple(() =>
            {
                Assert.That(alarm.IsShutdown, Is.Null);
                Assert.That(alarm.SubsystemName, Is.Null);
            });
        }

        /// <summary>
        /// Completing the generated alarm instance binds the standard condition
        /// methods supplied by its base classes.
        /// </summary>
        [Test]
        public void CompletingFactoryCreatedAlarmBindsConditionMethods()
        {
            SystemContext context = CreateContext();
            GeneratorModel.GeneratorProtectionAlarmState alarm = CreateAlarm(context);

            Assert.That(alarm.IsCreated, Is.False);
            Assert.That(alarm.Enable!.OnCallMethod, Is.Null);
            Assert.That(alarm.Disable!.OnCallMethod, Is.Null);
            Assert.That(alarm.AddComment!.OnCall, Is.Null);

            alarm.CreateAsPredefinedNode(context);

            Assert.That(alarm.IsCreated, Is.True);
            Assert.That(alarm.Enable.OnCallMethod, Is.Not.Null);
            Assert.That(alarm.Disable.OnCallMethod, Is.Not.Null);
            Assert.That(alarm.AddComment.OnCall, Is.Not.Null);
        }

        /// <summary>
        /// The production path gives every published member a reference a browse can
        /// follow.
        /// </summary>
        /// <remarks>
        /// This is the assertion that bites. A child with no
        /// <c>ReferenceTypeId</c> is still in <c>GetChildren</c> and still holds the
        /// value that was written to it, so nothing in the code looks wrong - but no
        /// client can reach it.
        /// </remarks>
        [Test]
        public void EveryPublishedMemberCarriesAReferenceAClientCanFollow()
        {
            SystemContext context = CreateContext();
            GeneratorModel.GeneratorProtectionAlarmState alarm = CreateAlarm(context);

            GeneratorProtections.ApplyDefinition(
                alarm, context, GeneratorProtections.Definitions[0]);

            var children = new List<BaseInstanceState>();
            alarm.GetChildren(context, children);

            var unreachable = new List<string>();
            foreach (BaseInstanceState child in children)
            {
                if (child.ReferenceTypeId.IsNull)
                {
                    unreachable.Add(child.BrowseName.Name ?? "<unnamed>");
                }
            }

            Assert.That(
                unreachable,
                Is.Empty,
                "Children with no reference type cannot be browsed to.");
        }

        /// <summary>
        /// The two optional members are published as properties of the alarm.
        /// </summary>
        [Test]
        public void TheOptionalMembersArePublishedAsProperties()
        {
            SystemContext context = CreateContext();
            GeneratorModel.GeneratorProtectionAlarmState alarm = CreateAlarm(context);

            GeneratorProtections.ApplyDefinition(
                alarm, context, GeneratorProtections.Definitions[0]);

            Assert.Multiple(() =>
            {
                Assert.That(
                    alarm.IsShutdown!.ReferenceTypeId,
                    Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasProperty));
                Assert.That(
                    alarm.SubsystemName!.ReferenceTypeId,
                    Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasProperty));
                Assert.That(
                    alarm.IsShutdown.TypeDefinitionId,
                    Is.EqualTo(Opc.Ua.VariableTypeIds.PropertyType));
                Assert.That(
                    alarm.SubsystemName.TypeDefinitionId,
                    Is.EqualTo(Opc.Ua.VariableTypeIds.PropertyType));
            });
        }

        /// <summary>
        /// The production path makes the optional members real children.
        /// </summary>
        /// <remarks>
        /// Browsing an alarm is how a client discovers these, so being a child is
        /// the property that matters - not merely being a non-null C# reference.
        /// This asserts the shape <see cref="GeneratorProtections.ApplyDefinition"/>
        /// produces, so removing the opt-in from it fails here.
        /// </remarks>
        [Test]
        public void ApplyingADefinitionMakesTheOptionalMembersBrowsableChildren()
        {
            SystemContext context = CreateContext();
            GeneratorModel.GeneratorProtectionAlarmState alarm = CreateAlarm(context);

            GeneratorProtections.ApplyDefinition(
                alarm, context, GeneratorProtections.Definitions[0]);

            List<string> children = ChildNames(context, alarm);

            Assert.Multiple(() =>
            {
                Assert.That(alarm.IsShutdown, Is.Not.Null);
                Assert.That(alarm.SubsystemName, Is.Not.Null);
                Assert.That(children, Does.Contain("IsShutdown"));
                Assert.That(children, Does.Contain("SubsystemName"));
                Assert.That(children, Does.Contain("ProtectionFunction"));
            });
        }

        /// <summary>
        /// Applying a definition twice does not produce two children.
        /// </summary>
        [Test]
        public void ApplyingADefinitionIsIdempotent()
        {
            SystemContext context = CreateContext();
            GeneratorModel.GeneratorProtectionAlarmState alarm = CreateAlarm(context);
            ProtectionDefinition definition = GeneratorProtections.Definitions[0];

            GeneratorProtections.ApplyDefinition(alarm, context, definition);
            PropertyState<bool>? first = alarm.IsShutdown;
            GeneratorProtections.ApplyDefinition(alarm, context, definition);

            Assert.Multiple(() =>
            {
                Assert.That(alarm.IsShutdown, Is.SameAs(first));
                Assert.That(
                    ChildNames(context, alarm).Count(n => n == "IsShutdown"),
                    Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Every protection's declared class and subsystem reach the child nodes.
        /// </summary>
        /// <remarks>
        /// This is the link between "the table says so" and "a client can read it".
        /// The defect this catches is silent: the C# assignment succeeds either way.
        /// </remarks>
        [Test]
        public void EveryProtectionPublishesItsClassAndSubsystem()
        {
            Assert.That(GeneratorProtections.Definitions.Count, Is.GreaterThan(0));

            SystemContext context = CreateContext();
            for (int i = 0; i < GeneratorProtections.Definitions.Count; i++)
            {
                ProtectionDefinition definition = GeneratorProtections.Definitions[i];
                GeneratorModel.GeneratorProtectionAlarmState alarm = CreateAlarm(context);
                GeneratorProtections.ApplyDefinition(alarm, context, definition);

                var children = new List<BaseInstanceState>();
                alarm.GetChildren(context, children);

                BaseInstanceState? shutdown = children.Find(
                    c => c.BrowseName.Name == "IsShutdown");
                BaseInstanceState? subsystem = children.Find(
                    c => c.BrowseName.Name == "SubsystemName");
                BaseInstanceState? function = children.Find(
                    c => c.BrowseName.Name == "ProtectionFunction");

                Assert.Multiple(() =>
                {
                    Assert.That(
                        (shutdown as PropertyState<bool>)?.Value,
                        Is.EqualTo(definition.IsShutdown),
                        $"{definition.Name} does not publish its shutdown class.");
                    Assert.That(
                        (subsystem as PropertyState<string>)?.Value,
                        Is.EqualTo(definition.Subsystem),
                        $"{definition.Name} does not publish its subsystem.");
                    Assert.That(
                        (function as PropertyState<GeneratorModel.GeneratorProtectionFunctionEnum>)?.Value,
                        Is.EqualTo(definition.Function),
                        $"{definition.Name} does not publish its protection function.");
                });
            }
        }

        private static GeneratorModel.GeneratorProtectionAlarmState CreateAlarm(ISystemContext context)
        {
            // The same factory the node manager uses, so the mandatory members are
            // materialised exactly as they are in the running server. Constructing
            // the state directly would leave them unmaterialised too, and the
            // fixture would be testing a shape the server never produces.
            var owner = new BaseObjectState(null)
            {
                NodeId = new NodeId(1, 1),
                BrowseName = new QualifiedName("GeneratorSet_1", 1),
            };
            return context.CreateInstanceOfGeneratorProtectionAlarmType(
                owner, new QualifiedName("LowOilPressureAlarm", 1));
        }

        private static List<string> ChildNames(
            ISystemContext context,
            GeneratorModel.GeneratorProtectionAlarmState alarm)
        {
            var children = new List<BaseInstanceState>();
            alarm.GetChildren(context, children);
            return children.ConvertAll(
                c => c.BrowseName.IsNull ? string.Empty : c.BrowseName.Name ?? string.Empty);
        }

        private static SystemContext CreateContext()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(GeneratorModel.Namespaces.Generators);
            return new SystemContext(null!)
            {
                NamespaceUris = namespaceUris,
                TypeTable = new TypeTable(namespaceUris),
            };
        }
    }
}
