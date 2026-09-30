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

using System;
using Opc.Ua.Di;

namespace Opc.Ua.Scales.Server.Builders
{
    /// <summary>
    /// Shapes an OPC 40200 scale before it is published.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The builder runs before the scale is registered with the node manager,
    /// so every member it adds is published together with the scale. What the
    /// builder materialises decides what the runtime binds: a scale built
    /// <see cref="WithZeroAndTare"/> gets working <c>SetZero</c>,
    /// <c>SetTare</c> and <c>ClearTare</c> methods, one built
    /// <see cref="WithPackMLState"/> a driven PackML state machine, and so on.
    /// </para>
    /// <para>
    /// <see cref="WithIdentification"/> and at least one
    /// <see cref="WithWeighingRange"/> are required: <c>Identification</c> and
    /// <c>&lt;ListOfWeighingRanges&gt;</c> are mandatory (§7.4.2).
    /// </para>
    /// </remarks>
    public interface IScaleBuilder
    {
        /// <summary>
        /// Gets the scale node being built.
        /// </summary>
        ScaleDeviceState Scale { get; }

        /// <summary>
        /// Gets the kind of scale being built.
        /// </summary>
        ScaleKind Kind { get; }

        /// <summary>
        /// Publishes the identification add-in (mandatory Manufacturer,
        /// SerialNumber and ProductInstanceUri) and mirrors it onto the DI
        /// nameplate of the component.
        /// </summary>
        /// <param name="identification">The identification.</param>
        IScaleBuilder WithIdentification(ScaleIdentification identification);

        /// <summary>
        /// Adds a weighing range (a <c>WeighingRangeElementType</c> instance).
        /// </summary>
        /// <param name="range">The range.</param>
        IScaleBuilder WithWeighingRange(WeighingRangeDefinition range);

        /// <summary>
        /// Sets the unit the scale weighs in. Defaults to the kilogram.
        /// </summary>
        /// <param name="unit">The unit.</param>
        IScaleBuilder WithUnit(EUInformation unit);

        /// <summary>
        /// Publishes <c>AllowedEngineeringUnits</c>, the units methods accept.
        /// Defaults to the scale's unit when a method takes a unit.
        /// </summary>
        /// <param name="units">The allowed units.</param>
        IScaleBuilder WithAllowedEngineeringUnits(params EUInformation[] units);

        /// <summary>
        /// Publishes <c>MinimalWeight</c>.
        /// </summary>
        /// <param name="minimalWeight">The minimal weight.</param>
        IScaleBuilder WithMinimalWeight(double minimalWeight);

        /// <summary>
        /// Marks the scale as verified (legal for trade): weights are rounded
        /// to the verification scale interval.
        /// </summary>
        /// <param name="legalForTrade">Whether the scale is legal for trade.</param>
        IScaleBuilder WithLegalForTrade(bool legalForTrade = true);

        /// <summary>
        /// Materialises the optional <c>WeightItemType</c> properties of
        /// <c>CurrentWeight</c>: Gross, Net, Tare, InsideZero, CenterOfZero,
        /// GrossNegative, WeightStable, CurrentRangeId, WeightId,
        /// LegalForTrade, HighResolutionValue and PrintableValue.
        /// </summary>
        IScaleBuilder WithWeightDetails();

        /// <summary>
        /// Adds <c>SetZero</c>, <c>SetTare</c>, <c>ClearTare</c> and,
        /// optionally, <c>SetPresetTare</c>.
        /// </summary>
        /// <param name="presetTare">Whether to add <c>SetPresetTare</c>.</param>
        IScaleBuilder WithZeroAndTare(bool presetTare = true);

        /// <summary>
        /// Adds <c>RegisterWeight</c> and the <c>RegisteredWeight</c> it fills.
        /// </summary>
        IScaleBuilder WithRegisterWeight();

        /// <summary>
        /// Adds the production preset: the <c>Products</c> folder and, as
        /// configured, product selection and management.
        /// </summary>
        /// <param name="configure">Configures the preset.</param>
        IScaleBuilder WithProductionPreset(Action<IProductionPresetBuilder> configure);

        /// <summary>
        /// Adds the <c>ProductionOutput</c> statistics with the package
        /// counters and the last item.
        /// </summary>
        IScaleBuilder WithProductionOutput();

        /// <summary>
        /// Adds the PackML state machine (<c>State</c>) with all its methods
        /// (the "PackML State Information" conformance unit).
        /// </summary>
        IScaleBuilder WithPackMLState();

        /// <summary>
        /// Adds the OPC 40001-1 <c>MachineryItemState</c> and
        /// <c>MachineryOperationMode</c> add-ins and the
        /// <c>MachineryBuildingBlocks</c> folder referencing them.
        /// </summary>
        IScaleBuilder WithMachineryBuildingBlocks();

        /// <summary>
        /// Publishes <c>ProcessStateId</c> and <c>ProcessStateMessage</c>.
        /// </summary>
        /// <param name="processStateId">The state id.</param>
        /// <param name="processStateMessage">The state message.</param>
        IScaleBuilder WithProcessState(string processStateId, LocalizedText processStateMessage);

        /// <summary>
        /// Materialises every optional member specific to the scale's kind -
        /// the laboratory draft shields and ionisator, the vehicle weighing
        /// methods, the recipe management, the continuous-scale figures and
        /// master totalizer, and so on - so the scale presents the complete
        /// feature set of its type.
        /// </summary>
        IScaleBuilder WithTypeFeatures();

        /// <summary>
        /// Gives every recipe of a recipe scale a <c>RecipeFile</c> a client
        /// can upload the recipe through (the "Scales FileRecipeManagement"
        /// conformance unit). The application parses the vendor format with
        /// <c>RecipeController.RecipeFileHandler</c>.
        /// </summary>
        /// <exception cref="InvalidOperationException">The scale is not a recipe scale.</exception>
        IScaleBuilder WithRecipeFiles();

        /// <summary>
        /// Adds a totalizer to a continuous or loss-in-weight scale (the
        /// <c>&lt;Totalizer&gt;</c> placeholder).
        /// </summary>
        /// <param name="name">The totalizer name.</param>
        IScaleBuilder AddTotalizer(string name);

        /// <summary>
        /// Adds a feeder module below <c>SubDevices</c>.
        /// </summary>
        /// <param name="name">The module name.</param>
        /// <param name="configure">Configures the module.</param>
        IScaleBuilder AddFeederModule(string name, Action<IScaleModuleBuilder>? configure = null);

        /// <summary>
        /// Adds a printer module below <c>SubDevices</c>.
        /// </summary>
        /// <param name="name">The module name.</param>
        /// <param name="configure">Configures the module.</param>
        IScaleBuilder AddPrinterModule(string name, Action<IScaleModuleBuilder>? configure = null);

        /// <summary>
        /// Adds a weighing module (weighing bridge) below <c>SubDevices</c>.
        /// </summary>
        /// <param name="name">The module name.</param>
        /// <param name="configure">Configures the weighing module like any scale.</param>
        IScaleBuilder AddWeighingModule(string name, Action<IScaleBuilder> configure);

        /// <summary>
        /// Configures the scale node directly - for example to add optional
        /// members of a scale subtype with its generated <c>Add…</c> helpers.
        /// </summary>
        /// <typeparam name="TState">The expected scale state type.</typeparam>
        /// <param name="configure">Configures the node.</param>
        /// <exception cref="InvalidOperationException">The scale is not a <typeparamref name="TState"/>.</exception>
        IScaleBuilder With<TState>(Action<ISystemContext, TState> configure)
            where TState : ScaleDeviceState;
    }

    /// <summary>
    /// Shapes the production preset of a scale or scale system.
    /// </summary>
    public interface IProductionPresetBuilder
    {
        /// <summary>
        /// Adds <c>SelectProduct</c>, <c>DeselectProduct</c>,
        /// <c>SwitchProduct</c> and <c>CurrentProducts</c> (the "Scales
        /// SelectProduct" conformance unit).
        /// </summary>
        IProductionPresetBuilder AllowSelection();

        /// <summary>
        /// Adds <c>AddProduct</c> and <c>RemoveProduct</c> (the "Scales
        /// ManageProduct" and "DynamicProductAddressSpace" conformance units).
        /// </summary>
        IProductionPresetBuilder AllowManagement();

        /// <summary>
        /// Gives every product a DI <c>Lock</c>; changing a product then needs
        /// its lock (§7.8.3).
        /// </summary>
        IProductionPresetBuilder WithLocking();

        /// <summary>
        /// Adds a product of the scale's product type.
        /// </summary>
        /// <param name="productId">The unique product id.</param>
        /// <param name="productName">The product name.</param>
        /// <param name="configure">Configures the product node.</param>
        IProductionPresetBuilder AddProduct(
            string productId,
            LocalizedText productName,
            Action<ISystemContext, ProductState>? configure = null);

        /// <summary>
        /// Puts a product into processing once the scale is running.
        /// </summary>
        /// <param name="productId">The product id.</param>
        IProductionPresetBuilder Select(string productId);
    }

    /// <summary>
    /// Shapes a feeder or printer module of a scale.
    /// </summary>
    public interface IScaleModuleBuilder
    {
        /// <summary>
        /// Gets the module node.
        /// </summary>
        ComponentState Module { get; }

        /// <summary>
        /// Publishes the module's identification (mandatory Manufacturer and
        /// SerialNumber).
        /// </summary>
        /// <param name="identification">The identification.</param>
        IScaleModuleBuilder WithIdentification(ScaleIdentification identification);

        /// <summary>
        /// Adds the OPC 40001-1 state add-ins and building-blocks folder.
        /// </summary>
        IScaleModuleBuilder WithMachineryBuildingBlocks();

        /// <summary>
        /// Adds the feeder speed members and the <c>SetFeederSpeed</c> method.
        /// </summary>
        /// <param name="minimum">The minimal feeder speed.</param>
        /// <param name="maximum">The maximum feeder speed.</param>
        /// <param name="unit">The speed unit.</param>
        /// <param name="initial">The initial target speed.</param>
        /// <exception cref="InvalidOperationException">The module is not a feeder.</exception>
        IScaleModuleBuilder WithFeederSpeed(double minimum, double maximum, EUInformation unit, double initial);

        /// <summary>
        /// Adds the printer's label and print-media members.
        /// </summary>
        /// <param name="labelTypeId">The label type id.</param>
        /// <param name="labelLength">The label length.</param>
        /// <param name="labelWidth">The label width.</param>
        /// <param name="lengthUnit">The unit of length and width.</param>
        /// <exception cref="InvalidOperationException">The module is not a printer.</exception>
        IScaleModuleBuilder WithLabel(string labelTypeId, double labelLength, double labelWidth, EUInformation lengthUnit);

        /// <summary>
        /// Configures the module node directly.
        /// </summary>
        /// <param name="configure">Configures the node.</param>
        IScaleModuleBuilder With(Action<ISystemContext, ComponentState> configure);
    }

    /// <summary>
    /// Shapes an OPC 40200 scale system before it is published.
    /// </summary>
    public interface IScaleSystemBuilder
    {
        /// <summary>
        /// Gets the scale system node being built.
        /// </summary>
        ScaleSystemState System { get; }

        /// <summary>
        /// Publishes the identification add-in.
        /// </summary>
        /// <param name="identification">The identification.</param>
        IScaleSystemBuilder WithIdentification(ScaleIdentification identification);

        /// <summary>
        /// Publishes the process state; <c>ProcessStateMessage</c> is
        /// mandatory on a scale system.
        /// </summary>
        /// <param name="processStateId">The state id.</param>
        /// <param name="processStateMessage">The state message.</param>
        IScaleSystemBuilder WithProcessState(string? processStateId, LocalizedText processStateMessage);

        /// <summary>
        /// Adds the system's production preset.
        /// </summary>
        /// <param name="configure">Configures the preset.</param>
        IScaleSystemBuilder WithProductionPreset(Action<IProductionPresetBuilder> configure);

        /// <summary>
        /// Adds the <c>ProductionOutput</c> statistics and
        /// <c>ResetGlobalStatistics</c>.
        /// </summary>
        IScaleSystemBuilder WithProductionOutput();

        /// <summary>
        /// Adds the PackML state machine (<c>SystemState</c>).
        /// </summary>
        IScaleSystemBuilder WithPackMLState();

        /// <summary>
        /// Adds the OPC 40001-1 state add-ins and building-blocks folder.
        /// </summary>
        IScaleSystemBuilder WithMachineryBuildingBlocks();

        /// <summary>
        /// Adds a scale to the system (the <c>&lt;ScaleDevice&gt;</c>
        /// placeholder of <c>SubDevices</c>).
        /// </summary>
        /// <param name="name">The scale name.</param>
        /// <param name="kind">The kind of scale.</param>
        /// <param name="configure">Configures the scale.</param>
        IScaleSystemBuilder AddScale(string name, ScaleKind kind, Action<IScaleBuilder> configure);
    }
}
