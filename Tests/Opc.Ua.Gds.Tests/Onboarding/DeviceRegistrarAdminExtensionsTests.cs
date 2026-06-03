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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Onboarding;

#nullable enable
#pragma warning disable CA2000

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Tests for <see cref="DeviceRegistrarAdminExtensions.BindToTicketStore"/> —
    /// wires the RegisterTickets / UnregisterTickets methods of a
    /// <c>DeviceRegistrarAdminType</c> instance through an
    /// <see cref="ITicketStore"/>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Onboarding")]
    public sealed class DeviceRegistrarAdminExtensionsTests
    {
        private const ushort kNs = 2;

        private static (BaseObjectState Registrar, MethodState Register, MethodState Unregister)
            CreateRegistrar()
        {
            var registrar = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Registrar", kNs),
                BrowseName = new QualifiedName("DeviceRegistrar", kNs),
                DisplayName = new LocalizedText("DeviceRegistrar")
            };
            var register = new MethodState(registrar)
            {
                NodeId = new NodeId("Register", kNs),
                BrowseName = new QualifiedName("RegisterTickets", kNs),
                DisplayName = new LocalizedText("RegisterTickets")
            };
            var unregister = new MethodState(registrar)
            {
                NodeId = new NodeId("Unregister", kNs),
                BrowseName = new QualifiedName("UnregisterTickets", kNs),
                DisplayName = new LocalizedText("UnregisterTickets")
            };
            registrar.AddChild(register);
            registrar.AddChild(unregister);
            return (registrar, register, unregister);
        }

        [Test]
        public void BindRejectsNodeMissingMethods()
        {
            var bare = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Bare", kNs),
                BrowseName = new QualifiedName("Bare", kNs)
            };
            var store = new MemoryTicketStore();

            Assert.Throws<ServiceResultException>(
                () => bare.BindToTicketStore(store));
        }

        [Test]
        public void BindRejectsNullArgs()
        {
            (BaseObjectState reg, _, _) = CreateRegistrar();
            Assert.Throws<ArgumentNullException>(
                () => reg.BindToTicketStore(null!));
            BaseObjectState? nullState = null;
            Assert.Throws<ArgumentNullException>(
                () => nullState!.BindToTicketStore(new MemoryTicketStore()));
        }

        [Test]
        public void RegisterTicketsCallStoresTickets()
        {
            (BaseObjectState reg, MethodState register, _) = CreateRegistrar();
            var store = new MemoryTicketStore();
            reg.BindToTicketStore(store);

            byte[][] tickets = new[]
            {
                new byte[] { 1, 2, 3 },
                new byte[] { 4, 5, 6 }
            };
            ByteString[] ticketsBs = Array.ConvertAll(tickets, b => new ByteString(b));

            ArrayOf<Variant> inputs = new[] { new Variant(ticketsBs.ToArrayOf()) }.ToArrayOf();
            List<Variant> outputs = new List<Variant>();
            ServiceResult result = register.OnCallMethod2!(
                context: new SystemContext(telemetry: null!),
                method: register,
                objectId: reg.NodeId,
                inputArguments: inputs,
                outputArguments: outputs);

            Assert.That(ServiceResult.IsGood(result));
            Assert.That(outputs, Has.Count.EqualTo(1));
            int[] statuses = ((ArrayOf<int>)outputs[0].AsBoxedObject()!).ToArray()!;
            Assert.That(statuses, Has.Length.EqualTo(2));
            Assert.That(statuses[0], Is.EqualTo((int)(uint)StatusCodes.Good));
            Assert.That(statuses[1], Is.EqualTo((int)(uint)StatusCodes.Good));
        }

        [Test]
        public async Task UnregisterTicketsRemovesPreviouslyStored()
        {
            (BaseObjectState reg, MethodState register, MethodState unregister) = CreateRegistrar();
            var store = new MemoryTicketStore();
            reg.BindToTicketStore(store);

            byte[] ticket = new byte[] { 0xAA };
            ByteString[] ticketsBs = new[] { new ByteString(ticket) };
            ArrayOf<Variant> registerInputs = new[]
                { new Variant(ticketsBs.ToArrayOf()) }.ToArrayOf();
            List<Variant> registerOutputs = new List<Variant>();
            register.OnCallMethod2!(
                new SystemContext(telemetry: null!), register, reg.NodeId,
                registerInputs, registerOutputs);

            int count = 0;
            await foreach (TicketRecord _ in store.ListAsync())
            {
                count++;
            }
            Assert.That(count, Is.EqualTo(1));

            ArrayOf<Variant> unregisterInputs = new[]
                { new Variant(ticketsBs.ToArrayOf()) }.ToArrayOf();
            List<Variant> unregisterOutputs = new List<Variant>();
            unregister.OnCallMethod2!(
                new SystemContext(telemetry: null!), unregister, reg.NodeId,
                unregisterInputs, unregisterOutputs);

            int[] statuses = ((ArrayOf<int>)unregisterOutputs[0].AsBoxedObject()!).ToArray()!;
            Assert.That(statuses, Has.Length.EqualTo(1));
            Assert.That(statuses[0], Is.EqualTo((int)(uint)StatusCodes.Good));

            count = 0;
            await foreach (TicketRecord _ in store.ListAsync())
            {
                count++;
            }
            Assert.That(count, Is.Zero);
        }
    }
}
