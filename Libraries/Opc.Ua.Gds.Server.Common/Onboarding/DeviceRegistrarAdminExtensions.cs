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

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Convenience extensions that wire an
    /// <see cref="ITicketStore"/> + <see cref="IManagedApplicationRegistry"/>
    /// pair to the OPC 10000-100 Part 21 <c>DeviceRegistrarAdminType</c>
    /// method children — <c>RegisterTickets</c> and
    /// <c>UnregisterTickets</c>. Uses
    /// <see cref="MethodState.OnCallMethod2"/> so the extension works
    /// against any concrete state derived from
    /// <see cref="BaseObjectState"/> that carries the expected
    /// method children.
    /// </summary>
    public static class DeviceRegistrarAdminExtensions
    {
        /// <summary>
        /// Routes the <c>RegisterTickets</c> /
        /// <c>UnregisterTickets</c> method calls on
        /// <paramref name="registrar"/> through
        /// <paramref name="store"/>.
        /// </summary>
        /// <param name="registrar">
        /// A <see cref="BaseObjectState"/> instance whose children
        /// include two <see cref="MethodState"/> nodes named
        /// <c>RegisterTickets</c> and <c>UnregisterTickets</c>.
        /// </param>
        /// <param name="store">The ticket store.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown when the supplied node does not expose the two
        /// expected child methods.
        /// </exception>
        public static void BindToTicketStore(
            this BaseObjectState registrar,
            ITicketStore store)
        {
            if (registrar == null)
            {
                throw new ArgumentNullException(nameof(registrar));
            }
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }
            MethodState? register = FindMethodChild(registrar, "RegisterTickets");
            MethodState? unregister = FindMethodChild(registrar, "UnregisterTickets");

            if (register == null || unregister == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "DeviceRegistrarAdmin instance '{0}' is missing one or both " +
                    "required method children (RegisterTickets, UnregisterTickets).",
                    registrar.BrowseName);
            }

            ITicketStore capturedStore = store;

            register.OnCallMethod2 = (ctx, m, objectId, inputs, outputs) =>
                HandleRegister(ctx, capturedStore, inputs, outputs);

            unregister.OnCallMethod2 = (ctx, m, objectId, inputs, outputs) =>
                HandleUnregister(ctx, capturedStore, inputs, outputs);
        }

        private static ServiceResult HandleRegister(
            ISystemContext context,
            ITicketStore store,
            ArrayOf<Variant> inputs,
            List<Variant> outputs)
        {
            if (inputs.Count < 1)
            {
                return StatusCodes.BadArgumentsMissing;
            }
            byte[][] tickets = ExtractTicketArray(inputs[0]);
            int[] results = new int[tickets.Length];

            for (int i = 0; i < tickets.Length; i++)
            {
                string ticketId = ComputeTicketId(tickets[i]);
                try
                {
                    store.AddAsync(
                        ticketId,
                        tickets[i],
                        new TicketMetadata(
                            Kind: TicketKind.Unspecified,
                            ManufacturerName: string.Empty,
                            ModelName: string.Empty,
                            SerialNumber: string.Empty,
                            ProductInstanceUri: string.Empty))
                        .AsTask().GetAwaiter().GetResult();
                    results[i] = (int)(uint)StatusCodes.Good;
                }
                catch (Exception ex)
                {
                    results[i] = (int)(uint)new ServiceResult(ex).StatusCode.Code;
                }
            }

            outputs.Add(new Variant(results.ToArrayOf()));
            return ServiceResult.Good;
        }

        private static ServiceResult HandleUnregister(
            ISystemContext context,
            ITicketStore store,
            ArrayOf<Variant> inputs,
            List<Variant> outputs)
        {
            if (inputs.Count < 1)
            {
                return StatusCodes.BadArgumentsMissing;
            }
            byte[][] tickets = ExtractTicketArray(inputs[0]);
            int[] results = new int[tickets.Length];

            for (int i = 0; i < tickets.Length; i++)
            {
                string ticketId = ComputeTicketId(tickets[i]);
                try
                {
                    bool removed = store.RemoveAsync(ticketId).AsTask().GetAwaiter().GetResult();
                    results[i] = removed
                        ? (int)(uint)StatusCodes.Good
                        : (int)(uint)StatusCodes.BadNotFound;
                }
                catch (Exception ex)
                {
                    results[i] = (int)(uint)new ServiceResult(ex).StatusCode.Code;
                }
            }

            outputs.Add(new Variant(results.ToArrayOf()));
            return ServiceResult.Good;
        }

        private static byte[][] ExtractTicketArray(Variant variant)
        {
            object? boxed = variant.AsBoxedObject();
            return boxed switch
            {
                byte[][] arr => arr,
                ByteString[] bs => Array.ConvertAll(bs, b => b.ToArray()),
                ArrayOf<ByteString> abs => abs.ToArray()
                    is { } abu ? Array.ConvertAll(abu, b => b.ToArray()) : Array.Empty<byte[]>(),
                _ => Array.Empty<byte[]>()
            };
        }

        private static string ComputeTicketId(byte[] ticket)
        {
            // Stable identifier — SHA-256 hash of the encoded ticket.
#if NET5_0_OR_GREATER
            byte[] hash = System.Security.Cryptography.SHA256.HashData(ticket);
#else
            byte[] hash;
            using (System.Security.Cryptography.SHA256 sha =
                System.Security.Cryptography.SHA256.Create())
            {
                hash = sha.ComputeHash(ticket);
            }
#endif
            var sb = new System.Text.StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("X2",
                    System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static MethodState? FindMethodChild(
            BaseObjectState parent, string browseName)
        {
            List<BaseInstanceState> children = new List<BaseInstanceState>();
            parent.GetChildren(context: null!, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is MethodState method &&
                    method.BrowseName.Name == browseName)
                {
                    return method;
                }
            }
            return null;
        }
    }
}
