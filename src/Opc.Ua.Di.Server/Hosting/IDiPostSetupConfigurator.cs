// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading.Tasks;

namespace Opc.Ua.Di.Server.Hosting
{
    /// <summary>
    /// A delegate that runs once a DI node manager's address space is
    /// fully initialised. Configurators are registered through
    /// <c>ConfigureDevicesFor&lt;TNodeManager&gt;</c> and dispatched by
    /// the <see cref="IDiPostSetupRunner"/> in registration order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each configurator declares its <see cref="TargetManagerType"/>
    /// so only matching manager subclasses receive the callback. A
    /// configurator registered with <c>TargetManagerType = typeof(DiNodeManager)</c>
    /// runs against the plain DI manager only; one registered with
    /// <c>typeof(PumpNodeManager)</c> runs only against pump managers.
    /// Subclass matching uses <see cref="Type.IsAssignableFrom(Type)"/>
    /// so a configurator for a base class also matches its derived
    /// classes.
    /// </para>
    /// </remarks>
    public interface IDiPostSetupConfigurator
    {
        /// <summary>
        /// The DI node-manager type this configurator targets. Set to
        /// <c>typeof(DiNodeManager)</c> for plain DI managers, or a
        /// subclass type (e.g. <c>typeof(PumpNodeManager)</c>) to
        /// restrict the delegate to a specific companion-spec manager.
        /// </summary>
        Type TargetManagerType { get; }

        /// <summary>
        /// Performs the post-setup work against
        /// <see cref="IDiPostSetupContext.Manager"/>. Exceptions
        /// thrown from this method abort the hosted server startup;
        /// they are not caught or swallowed.
        /// </summary>
        ValueTask RunAsync(IDiPostSetupContext context);
    }
}
