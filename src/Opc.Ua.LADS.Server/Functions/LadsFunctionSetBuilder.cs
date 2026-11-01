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

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// Creates the functions of a function set.
    /// </summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    internal abstract class LadsFunctionSetBuilder<TBuilder> : ILadsFunctionSetBuilder<TBuilder>
        where TBuilder : ILadsFunctionSetBuilder<TBuilder>
    {
        /// <summary>
        /// Creates a builder that adds functions to a function set.
        /// </summary>
        protected LadsFunctionSetBuilder(LadsFunctionalUnit unit, Func<NodeState> functionSet, string pathPrefix)
        {
            Unit = unit;
            m_functionSet = functionSet;
            m_pathPrefix = pathPrefix;
        }

        /// <summary>
        /// Gets the functional unit.
        /// </summary>
        public LadsFunctionalUnit Unit { get; }

        /// <summary>
        /// Gets the builder that is returned by the fluent methods.
        /// </summary>
        protected abstract TBuilder Self { get; }

        /// <inheritdoc/>
        public TBuilder AnalogControl(string name, Action<LadsAnalogControlOptions>? configure = null)
        {
            return AnalogControl(name, configure, out _);
        }

        /// <inheritdoc/>
        public TBuilder AnalogControl(
            string name,
            Action<LadsAnalogControlOptions>? configure,
            out IAnalogControlFunction function)
        {
            LadsAnalogControlOptions options = Configure(configure);
            ISystemContext context = Unit.Runtime.Context;
            NodeState parent = m_functionSet();
            QualifiedName browseName = BrowseName(name);
            AnalogControlFunctionState node = options.Kind switch
            {
                LadsAnalogControlKind.Totalizer =>
                    context.CreateInstanceOfAnalogControlFunctionWithTotalizerType(parent, browseName),
                LadsAnalogControlKind.Relative =>
                    context.CreateInstanceOfAnalogControlFunctionWithRelativeTargetValueType(parent, browseName),
                LadsAnalogControlKind.Composed =>
                    context.CreateInstanceOfAnalogControlFunctionWithComposedTargetValueType(parent, browseName),
                _ => context.CreateInstanceOfAnalogControlFunctionType(parent, browseName)
            };
            var handle = new LadsAnalogControlFunction(Unit, Attach(parent, node), Path(name), options);
            function = handle;
            return Register(handle);
        }

        /// <inheritdoc/>
        public TBuilder MultiModeAnalogControl(string name, Action<LadsMultiModeAnalogControlOptions> configure)
        {
            return MultiModeAnalogControl(name, configure, out _);
        }

        /// <inheritdoc/>
        public TBuilder MultiModeAnalogControl(
            string name,
            Action<LadsMultiModeAnalogControlOptions> configure,
            out IMultiModeAnalogControlFunction function)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            LadsMultiModeAnalogControlOptions options = Configure(configure);
            NodeState parent = m_functionSet();
            MultiModeAnalogControlFunctionState node = Unit.Runtime.Context
                .CreateInstanceOfMultiModeAnalogControlFunctionType(parent, BrowseName(name));
            var handle = new LadsMultiModeAnalogControlFunction(Unit, Attach(parent, node), Path(name), options);
            function = handle;
            return Register(handle);
        }

        /// <inheritdoc/>
        public TBuilder TwoStateDiscreteControl(string name, Action<LadsTwoStateDiscreteOptions>? configure = null)
        {
            return TwoStateDiscreteControl(name, configure, out _);
        }

        /// <inheritdoc/>
        public TBuilder TwoStateDiscreteControl(
            string name,
            Action<LadsTwoStateDiscreteOptions>? configure,
            out ITwoStateDiscreteControlFunction function)
        {
            LadsTwoStateDiscreteOptions options = Configure(configure);
            NodeState parent = m_functionSet();
            TwoStateDiscreteControlFunctionState node = Unit.Runtime.Context
                .CreateInstanceOfTwoStateDiscreteControlFunctionType(parent, BrowseName(name));
            var handle = new LadsTwoStateDiscreteControlFunction(Unit, Attach(parent, node), Path(name), options);
            function = handle;
            return Register(handle);
        }

        /// <inheritdoc/>
        public TBuilder MultiStateDiscreteControl(string name, Action<LadsMultiStateDiscreteOptions> configure)
        {
            return MultiStateDiscreteControl(name, configure, out _);
        }

        /// <inheritdoc/>
        public TBuilder MultiStateDiscreteControl(
            string name,
            Action<LadsMultiStateDiscreteOptions> configure,
            out IMultiStateDiscreteControlFunction function)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            LadsMultiStateDiscreteOptions options = Configure(configure);
            NodeState parent = m_functionSet();
            MultiStateDiscreteControlFunctionState node = Unit.Runtime.Context
                .CreateInstanceOfMultiStateDiscreteControlFunctionType(parent, BrowseName(name));
            var handle = new LadsMultiStateDiscreteControlFunction(Unit, Attach(parent, node), Path(name), options);
            function = handle;
            return Register(handle);
        }

        /// <inheritdoc/>
        public TBuilder Timer(string name, Action<LadsTimerOptions>? configure = null)
        {
            return Timer(name, configure, out _);
        }

        /// <inheritdoc/>
        public TBuilder Timer(string name, Action<LadsTimerOptions>? configure, out ITimerFunction function)
        {
            LadsTimerOptions options = Configure(configure);
            NodeState parent = m_functionSet();
            TimerControlFunctionState node = Unit.Runtime.Context
                .CreateInstanceOfTimerControlFunctionType(parent, BrowseName(name));
            var handle = new LadsTimerFunction(Unit, Attach(parent, node), Path(name), options);
            function = handle;
            return Register(handle);
        }

        /// <inheritdoc/>
        public TBuilder AnalogScalarSensor(string name, Action<LadsAnalogSensorOptions>? configure = null)
        {
            return AnalogScalarSensor(name, configure, out _);
        }

        /// <inheritdoc/>
        public TBuilder AnalogScalarSensor(
            string name,
            Action<LadsAnalogSensorOptions>? configure,
            out IAnalogScalarSensorFunction function)
        {
            LadsAnalogSensorOptions options = Configure(configure);
            ISystemContext context = Unit.Runtime.Context;
            NodeState parent = m_functionSet();
            AnalogScalarSensorFunctionState node = options.WithCompensation
                ? context.CreateInstanceOfAnalogScalarSensorFunctionWithCompensationType(parent, BrowseName(name))
                : context.CreateInstanceOfAnalogScalarSensorFunctionType(parent, BrowseName(name));
            var handle = new LadsAnalogScalarSensorFunction(Unit, Attach(parent, node), Path(name), options);
            function = handle;
            return Register(handle);
        }

        /// <inheritdoc/>
        public TBuilder AnalogArraySensor(string name, Action<LadsAnalogSensorOptions>? configure = null)
        {
            return AnalogArraySensor(name, configure, out _);
        }

        /// <inheritdoc/>
        public TBuilder AnalogArraySensor(
            string name,
            Action<LadsAnalogSensorOptions>? configure,
            out IAnalogArraySensorFunction function)
        {
            LadsAnalogSensorOptions options = Configure(configure);
            NodeState parent = m_functionSet();
            AnalogArraySensorFunctionState node = Unit.Runtime.Context
                .CreateInstanceOfAnalogArraySensorFunctionType(parent, BrowseName(name));
            var handle = new LadsAnalogArraySensorFunction(Unit, Attach(parent, node), Path(name), options);
            function = handle;
            return Register(handle);
        }

        /// <inheritdoc/>
        public TBuilder TwoStateDiscreteSensor(string name, Action<LadsTwoStateDiscreteOptions>? configure = null)
        {
            return TwoStateDiscreteSensor(name, configure, out _);
        }

        /// <inheritdoc/>
        public TBuilder TwoStateDiscreteSensor(
            string name,
            Action<LadsTwoStateDiscreteOptions>? configure,
            out ITwoStateDiscreteSensorFunction function)
        {
            LadsTwoStateDiscreteOptions options = Configure(configure);
            NodeState parent = m_functionSet();
            TwoStateDiscreteSensorFunctionState node = Unit.Runtime.Context
                .CreateInstanceOfTwoStateDiscreteSensorFunctionType(parent, BrowseName(name));
            var handle = new LadsTwoStateDiscreteSensorFunction(Unit, Attach(parent, node), Path(name), options);
            function = handle;
            return Register(handle);
        }

        /// <inheritdoc/>
        public TBuilder MultiStateDiscreteSensor(string name, Action<LadsMultiStateDiscreteOptions> configure)
        {
            return MultiStateDiscreteSensor(name, configure, out _);
        }

        /// <inheritdoc/>
        public TBuilder MultiStateDiscreteSensor(
            string name,
            Action<LadsMultiStateDiscreteOptions> configure,
            out IMultiStateDiscreteSensorFunction function)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            LadsMultiStateDiscreteOptions options = Configure(configure);
            NodeState parent = m_functionSet();
            MultiStateDiscreteSensorFunctionState node = Unit.Runtime.Context
                .CreateInstanceOfMultiStateDiscreteSensorFunctionType(parent, BrowseName(name));
            var handle = new LadsMultiStateDiscreteSensorFunction(Unit, Attach(parent, node), Path(name), options);
            function = handle;
            return Register(handle);
        }

        /// <inheritdoc/>
        public TBuilder MultiSensor(string name, Action<ILadsMultiSensorBuilder> configure)
        {
            return MultiSensor(name, configure, out _);
        }

        /// <inheritdoc/>
        public TBuilder MultiSensor(
            string name,
            Action<ILadsMultiSensorBuilder> configure,
            out IMultiSensorFunction function)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            ISystemContext context = Unit.Runtime.Context;
            NodeState parent = m_functionSet();
            MultiSensorFunctionState node = Attach(
                parent,
                context.CreateInstanceOfMultiSensorFunctionType(parent, BrowseName(name)));
            string path = Path(name);
            var handle = new LadsMultiSensorFunction(Unit, node, path);
            Register(handle);
            var builder = new LadsMultiSensorBuilder(
                Unit,
                handle,
                () =>
                {
                    if (node.FunctionSet == null)
                    {
                        node.AddFunctionSet(context);
                    }
                    return node.FunctionSet!;
                },
                path + "/");
            configure(builder);
            function = handle;
            return Self;
        }

        /// <inheritdoc/>
        public TBuilder Cover(string name, Action<LadsCoverOptions>? configure = null)
        {
            return Cover(name, configure, out _);
        }

        /// <inheritdoc/>
        public TBuilder Cover(string name, Action<LadsCoverOptions>? configure, out ICoverFunction function)
        {
            LadsCoverOptions options = Configure(configure);
            NodeState parent = m_functionSet();
            CoverFunctionState node = Unit.Runtime.Context
                .CreateInstanceOfCoverFunctionType(parent, BrowseName(name));
            var handle = new LadsCoverFunction(Unit, Attach(parent, node), Path(name), options);
            function = handle;
            return Register(handle);
        }

        /// <summary>
        /// Registers a created function.
        /// </summary>
        protected virtual TBuilder Register(LadsFunction function)
        {
            Unit.AddFunction(function);
            return Self;
        }

        /// <summary>
        /// Creates and configures options.
        /// </summary>
        /// <typeparam name="TOptions">The options type.</typeparam>
        private static TOptions Configure<TOptions>(Action<TOptions>? configure)
            where TOptions : new()
        {
            var options = new TOptions();
            configure?.Invoke(options);
            return options;
        }

        /// <summary>
        /// Adds a created function node to the function set.
        /// </summary>
        /// <typeparam name="T">The node type.</typeparam>
        private static T Attach<T>(NodeState parent, T node)
            where T : BaseInstanceState
        {
            parent.AddChild(LadsRuntime.Named(node, global::Opc.Ua.ReferenceTypeIds.HasComponent));
            return node;
        }

        /// <summary>
        /// Validates the name of a function and returns its browse name.
        /// </summary>
        private QualifiedName BrowseName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains('/', StringComparison.Ordinal))
            {
                throw new ArgumentException("A function name must not be empty or contain '/'.", nameof(name));
            }
            if (Unit.HasFunction(Path(name)))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated,
                    "The function '{0}' already exists.",
                    Path(name));
            }
            return Unit.Runtime.InstanceName(name);
        }

        /// <summary>
        /// Gets the path of a function.
        /// </summary>
        private string Path(string name)
        {
            return m_pathPrefix + name;
        }

        private readonly Func<NodeState> m_functionSet;
        private readonly string m_pathPrefix;
    }

    /// <summary>
    /// Creates the functions of a multi sensor function.
    /// </summary>
    internal sealed class LadsMultiSensorBuilder : LadsFunctionSetBuilder<ILadsMultiSensorBuilder>,
        ILadsMultiSensorBuilder
    {
        /// <summary>
        /// Creates the builder.
        /// </summary>
        public LadsMultiSensorBuilder(
            LadsFunctionalUnit unit,
            LadsMultiSensorFunction sensor,
            Func<NodeState> functionSet,
            string pathPrefix)
            : base(unit, functionSet, pathPrefix)
        {
            m_sensor = sensor;
        }

        /// <inheritdoc/>
        protected override ILadsMultiSensorBuilder Self => this;

        /// <inheritdoc/>
        protected override ILadsMultiSensorBuilder Register(LadsFunction function)
        {
            m_sensor.Add(function);
            return base.Register(function);
        }

        private readonly LadsMultiSensorFunction m_sensor;
    }

    /// <summary>
    /// Creates a functional unit.
    /// </summary>
    internal sealed class LadsFunctionalUnitBuilder : LadsFunctionSetBuilder<ILadsFunctionalUnitBuilder>,
        ILadsFunctionalUnitBuilder
    {
        /// <summary>
        /// Creates the builder.
        /// </summary>
        public LadsFunctionalUnitBuilder(LadsFunctionalUnit unit)
            : base(unit, () => unit.EnsureFunctionSet(), string.Empty)
        {
        }

        /// <inheritdoc/>
        public string Name => Unit.Name;

        /// <inheritdoc/>
        protected override ILadsFunctionalUnitBuilder Self => this;

        /// <inheritdoc/>
        public ILadsFunctionalUnitBuilder Programs(Action<ILadsProgramManagerBuilder>? configure = null)
        {
            LadsProgramManager manager = Unit.EnsureProgramManager();
            configure?.Invoke(new LadsProgramManagerBuilder(manager));
            return this;
        }

        /// <inheritdoc/>
        public ILadsFunctionalUnitBuilder SupportedProperty(string name)
        {
            Unit.AddSupportedProperty(name);
            return this;
        }

        /// <inheritdoc/>
        public ILadsFunctionalUnitBuilder FunctionProvider(ILadsFunctionProvider provider)
        {
            Unit.SetFunctionProvider(provider ?? throw new ArgumentNullException(nameof(provider)));
            return this;
        }
    }

    /// <summary>
    /// Configures the program manager of a functional unit.
    /// </summary>
    internal sealed class LadsProgramManagerBuilder : ILadsProgramManagerBuilder
    {
        /// <summary>
        /// Creates the builder.
        /// </summary>
        public LadsProgramManagerBuilder(LadsProgramManager manager)
        {
            m_manager = manager;
        }

        /// <inheritdoc/>
        public ILadsProgramManagerBuilder Template(string id, Action<LadsProgramTemplate> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            var template = new LadsProgramTemplate(id);
            configure(template);
            return Template(template);
        }

        /// <inheritdoc/>
        public ILadsProgramManagerBuilder Template(LadsProgramTemplate template)
        {
            m_manager.AddTemplateNode(template ?? throw new ArgumentNullException(nameof(template)));
            return this;
        }

        /// <inheritdoc/>
        public ILadsProgramManagerBuilder Executor(ILadsProgramExecutor executor)
        {
            m_manager.Executor = executor ?? throw new ArgumentNullException(nameof(executor));
            return this;
        }

        private readonly LadsProgramManager m_manager;
    }
}
