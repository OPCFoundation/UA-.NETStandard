/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Convenience extensions that wire an <see cref="ITicketStore"/>
    /// to the OPC 10000-21 <c>DeviceRegistrarAdminType</c>
    /// method children — <c>RegisterTickets</c> and
    /// <c>UnregisterTickets</c>. Uses
    /// <see cref="MethodState.OnCallMethod2Async"/> so the extension works
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
        /// <exception cref="ArgumentNullException"><paramref name="registrar"/> is <c>null</c>.</exception>
        [Obsolete("Supply an IOnboardingTicketValidator. Registration without a validator returns BadConfigurationError.")]
        public static void BindToTicketStore(
            this BaseObjectState registrar,
            ITicketStore store)
        {
            BindToTicketStoreCore(registrar, store, validator: null);
        }

        /// <summary>
        /// Binds ticket administration to a store and verifies every registered ticket
        /// against out-of-band authority trust before retaining it.
        /// </summary>
        public static void BindToTicketStore(
            this BaseObjectState registrar,
            ITicketStore store,
            IOnboardingTicketValidator validator)
        {
            if (validator == null)
            {
                throw new ArgumentNullException(nameof(validator));
            }
            BindToTicketStoreCore(registrar, store, validator);
        }

        /// <summary>
        /// Installs authorized handlers without accepting unverified registrations.
        /// </summary>
        private static void BindToTicketStoreCore(
            BaseObjectState registrar,
            ITicketStore store,
            IOnboardingTicketValidator? validator)
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

            register.OnCallMethod2Async = (ctx, m, objectId, inputs, outputs, ct) =>
                HasRegistrarAdminRole(ctx)
                    ? HandleRegisterAsync(capturedStore, validator, inputs, outputs, ct)
                    : new ValueTask<ServiceResult>(RegistrarAdminRequired());

            unregister.OnCallMethod2Async = (ctx, m, objectId, inputs, outputs, ct) =>
                HasRegistrarAdminRole(ctx)
                    ? HandleUnregisterAsync(capturedStore, inputs, outputs, ct)
                    : new ValueTask<ServiceResult>(RegistrarAdminRequired());
        }

        /// <summary>
        /// OPC 10000-21 §9.2.11 / §9.2.12: RegisterTickets and UnregisterTickets
        /// shall be called from a Session that has access to the RegistrarAdmin
        /// Role. Calls without a Session identity cannot establish this permission.
        /// </summary>
        private static bool HasRegistrarAdminRole(ISystemContext context)
        {
            if (context is not ISessionSystemContext { UserIdentity: { } identity })
            {
                return false;
            }

            NodeId roleId = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.ObjectIds.WellKnownRole_RegistrarAdmin,
                context.NamespaceUris);
            return !roleId.IsNull && identity.GrantedRoleIds.Contains(roleId);
        }

        private static ServiceResult RegistrarAdminRequired()
        {
            return new ServiceResult(
                StatusCodes.BadUserAccessDenied,
                LocalizedText.From("The RegistrarAdmin Role is required to manage tickets."));
        }

        private static async ValueTask<ServiceResult> HandleRegisterAsync(
            ITicketStore store,
            IOnboardingTicketValidator? validator,
            ArrayOf<Variant> inputs,
            List<Variant> outputs,
            CancellationToken cancellationToken)
        {
            if (inputs.Count < 1)
            {
                return StatusCodes.BadArgumentsMissing;
            }
            if (inputs.Count > 1)
            {
                return StatusCodes.BadTooManyArguments;
            }
            if (!inputs[0].TryGetValue(out ArrayOf<string> tickets))
            {
                return StatusCodes.BadTypeMismatch;
            }
            if (outputs.Count < 1)
            {
                return StatusCodes.BadUnexpectedError;
            }
            if (validator == null)
            {
                return new ServiceResult(
                    StatusCodes.BadConfigurationError,
                    LocalizedText.From("A ticket validator must be configured before registering tickets."));
            }
            var results = new StatusCode[tickets.Count];

            for (int i = 0; i < tickets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (tickets[i] == null)
                    {
                        results[i] = StatusCodes.BadTicketInvalid;
                        continue;
                    }
                    ValidatedOnboardingTicket accepted = await validator.ValidateAsync(
                        tickets[i], OnboardingTicketSource.OutOfBand, cancellationToken).ConfigureAwait(false);
                    byte[] ticket = System.Text.Encoding.UTF8.GetBytes(tickets[i]);
                    string ticketId = ComputeTicketId(ticket);
                    await store.AddAsync(
                            ticketId,
                            ticket,
                            accepted.Metadata,
                            cancellationToken)
                        .ConfigureAwait(false);
                    results[i] = StatusCodes.Good;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (ServiceResultException ex)
                {
                    results[i] = ex.StatusCode;
                }
                catch (Exception)
                {
                    results[i] = StatusCodes.BadUnexpectedError;
                }
            }

            outputs[0] = Variant.From(results.ToArrayOf());
            return ServiceResult.Good;
        }

        private static async ValueTask<ServiceResult> HandleUnregisterAsync(
            ITicketStore store,
            ArrayOf<Variant> inputs,
            List<Variant> outputs,
            CancellationToken cancellationToken)
        {
            if (inputs.Count < 1)
            {
                return StatusCodes.BadArgumentsMissing;
            }
            if (inputs.Count > 1)
            {
                return StatusCodes.BadTooManyArguments;
            }
            if (!inputs[0].TryGetValue(out ArrayOf<string> tickets))
            {
                return StatusCodes.BadTypeMismatch;
            }
            if (outputs.Count < 1)
            {
                return StatusCodes.BadUnexpectedError;
            }
            var results = new StatusCode[tickets.Count];

            for (int i = 0; i < tickets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (tickets[i] == null)
                {
                    results[i] = StatusCodes.BadTicketInvalid;
                    continue;
                }
                byte[] ticket = System.Text.Encoding.UTF8.GetBytes(tickets[i]);
                string ticketId = ComputeTicketId(ticket);
                try
                {
                    bool removed = await store
                        .RemoveAsync(ticketId, cancellationToken)
                        .ConfigureAwait(false);
                    results[i] = removed
                        ? StatusCodes.Good
                        : StatusCodes.BadNotFound;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (ServiceResultException ex)
                {
                    results[i] = ex.StatusCode;
                }
                catch (Exception)
                {
                    results[i] = StatusCodes.BadUnexpectedError;
                }
            }

            outputs[0] = Variant.From(results.ToArrayOf());
            return ServiceResult.Good;
        }

        private static string ComputeTicketId(byte[] ticket)
        {
            // Stable identifier — SHA-256 hash of the encoded ticket.
#if NET5_0_OR_GREATER
            byte[] hash = System.Security.Cryptography.SHA256.HashData(ticket);
#else
            byte[] hash;
            using (var sha =
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
            var children = new List<BaseInstanceState>();
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
