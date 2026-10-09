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

namespace Opc.Ua.Pumps.Server.Builders
{
    /// <summary>
    /// Materialises and fills the optional children of one OPC 40223 group.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every child a group declares is addressed by its browse name, which is
    /// what the generated <see cref="BrowseNames"/> constants carry. That is
    /// deliberate: <c>DesignType</c> alone declares 78 optional variables and
    /// the model has some 3000 across all its types, so a method per child
    /// would be an unusable API and an unmaintainable one. Passing a
    /// generated constant keeps the call compile-time checked all the same -
    /// a rename in the model breaks the build.
    /// </para>
    /// <para>
    /// A name the group does not declare is rejected rather than silently
    /// creating a non-conforming node: a server that invents children is
    /// worse than one that omits them, because a client cannot tell the
    /// difference between a vendor extension and a typo.
    /// </para>
    /// </remarks>
    public interface IPumpGroupBuilder
    {
        /// <summary>
        /// Gets the group node being built.
        /// </summary>
        NodeState Node { get; }

        /// <summary>
        /// Materialises the named optional children without giving them
        /// values.
        /// </summary>
        /// <param name="browseNames">
        /// Browse names the group declares, normally
        /// <see cref="BrowseNames"/> constants.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The group declares no child of that name.
        /// </exception>
        IPumpGroupBuilder Add(params string[] browseNames);

        /// <summary>
        /// Materialises the named child and sets its value.
        /// </summary>
        /// <param name="browseName">A browse name the group declares.</param>
        /// <param name="value">The value to publish.</param>
        /// <exception cref="ArgumentException">
        /// The group declares no child of that name, or it is not a variable.
        /// </exception>
        IPumpGroupBuilder Set(string browseName, Variant value);

        /// <summary>
        /// Materialises the named analog child, sets its value and publishes
        /// the engineering unit and instrument range that make it
        /// interpretable.
        /// </summary>
        /// <param name="browseName">A browse name the group declares.</param>
        /// <param name="value">The reading.</param>
        /// <param name="engineeringUnits">
        /// The unit, for a child typed by <c>AnalogUnitType</c> or
        /// <c>AnalogUnitRangeType</c>. Ignored when the child declares no
        /// <c>EngineeringUnits</c> property.
        /// </param>
        /// <param name="euRange">
        /// The instrument range. Ignored when the child declares no
        /// <c>EURange</c> property.
        /// </param>
        IPumpGroupBuilder SetAnalog(
            string browseName,
            double value,
            EUInformation? engineeringUnits = null,
            Range? euRange = null);

        /// <summary>
        /// Materialises the named discrete child and sets its state.
        /// </summary>
        /// <remarks>
        /// Handles both shapes OPC 40223 uses for a boolean: a
        /// <c>TwoStateDiscreteType</c> variable, as the supervision groups
        /// declare, and a <c>DiscreteInputObjectType</c> or
        /// <c>DiscreteOutputObjectType</c> object whose value lives one level
        /// down, as the signal and actuation groups declare.
        /// </remarks>
        /// <param name="browseName">A browse name the group declares.</param>
        /// <param name="value">The state to publish.</param>
        IPumpGroupBuilder SetDiscrete(string browseName, bool value);

        /// <summary>
        /// Materialises the named child and hands it to
        /// <paramref name="configure"/> for anything this interface does not
        /// cover - access levels, historizing, a method callback.
        /// </summary>
        /// <param name="browseName">A browse name the group declares.</param>
        /// <param name="configure">Mutates the materialised child.</param>
        IPumpGroupBuilder With(string browseName, Action<BaseInstanceState> configure);

        /// <summary>
        /// Materialises the named child and returns it, or
        /// <see langword="null"/> when the group declares no such child.
        /// </summary>
        /// <param name="browseName">A browse name the group declares.</param>
        BaseInstanceState? Child(string browseName);

        /// <summary>
        /// Materialises a nested group - a supervision category below
        /// <c>Events</c>, say - and returns a builder for it.
        /// </summary>
        /// <param name="browseName">A browse name the group declares.</param>
        /// <exception cref="ArgumentException">
        /// The group declares no child of that name, or it is not an object.
        /// </exception>
        IPumpGroupBuilder Nested(string browseName);

        /// <summary>
        /// Adds one vibration measurement to a <c>Measurements</c> group and
        /// returns a builder for its variables.
        /// </summary>
        /// <remarks>
        /// <c>MeasurementsType</c> declares its vibration measurements as the
        /// OptionalPlaceholder <c>&lt;Vibration&gt;</c> of
        /// <c>VibrationMeasurementType</c> (OPC 40223 §7.32), so a pump can
        /// publish several - one per bearing, say - each named by the
        /// application. Asking again for an existing name returns its builder.
        /// </remarks>
        /// <param name="name">The measurement's browse name.</param>
        /// <exception cref="ArgumentException">
        /// The name is empty, or the group declares no <c>&lt;Vibration&gt;</c>
        /// placeholder.
        /// </exception>
        IPumpGroupBuilder AddVibration(string name);
    }
}
