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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PackML;

namespace Opc.Ua.Scales.Server.Runtime
{
    /// <summary>
    /// The processing state of a recipe scale.
    /// </summary>
    public enum RecipeRunState
    {
        /// <summary>No recipe is being processed.</summary>
        Idle,

        /// <summary>A recipe is being processed.</summary>
        Running,

        /// <summary>The recipe was paused with <c>StopRecipe</c>.</summary>
        Paused
    }

    /// <summary>
    /// Reports a recipe element that became current, for the application to
    /// execute.
    /// </summary>
    public sealed class RecipeElementEventArgs : EventArgs
    {
        internal RecipeElementEventArgs(RecipeState recipe, RecipeElementState element)
        {
            Recipe = recipe;
            Element = element;
        }

        /// <summary>
        /// Gets the recipe.
        /// </summary>
        public RecipeState Recipe { get; }

        /// <summary>
        /// Gets the element that became current.
        /// </summary>
        public RecipeElementState Element { get; }
    }

    /// <summary>
    /// Runtime of a <c>RecipeScaleType</c> (OPC 40200 §7.29 - §7.33,
    /// Annex B): recipe management and recipe processing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A recipe is a directed acyclic graph of recipe elements linked with
    /// <c>NextRecipeElement</c> references; the start elements are the ones the
    /// recipe node itself points to. <c>AddRecipeElement</c> only ever adds
    /// edges into a new element, so the graph cannot become cyclic.
    /// </para>
    /// <para>
    /// Processing follows the graph: an element becomes current when all its
    /// predecessors are complete (a join), and completing one makes all its
    /// successors eligible (a fork). The application executes the current
    /// elements - it is told through <see cref="ElementStarted"/> - and reports
    /// each one done with <see cref="CompleteElement"/>;
    /// <c>SkipCurrentRecipeElement</c> completes them from the client side.
    /// <c>StopRecipe</c> pauses (§7.29.5), <c>ContinueRecipe</c> resumes and
    /// <c>AbortRecipe</c> ends the processing. Every step is appended to the
    /// <c>Report</c> of the recipe product in processing.
    /// </para>
    /// </remarks>
    public sealed class RecipeController
    {
        internal RecipeController(ScaleHandle owner, RecipeScaleState scale, bool recipeFiles = false)
        {
            m_owner = owner;
            Scale = scale;
            m_recipeFiles = recipeFiles;
            m_nextRecipeElement = owner.Services.ScalesId(ReferenceTypes.NextRecipeElement);

            if (scale.Recipes is { } management)
            {
                var children = new List<BaseInstanceState>();
                management.GetChildren(owner.Services.Context, children);
                foreach (RecipeState recipe in children.OfType<RecipeState>())
                {
                    if (recipe.RecipeId?.Value is { Length: > 0 } id)
                    {
                        m_recipes[id] = recipe;
                        BindRecipe(recipe);
                        AttachRecipeFile(recipe);
                    }
                }
                if (management.AddRecipe != null)
                {
                    management.AddRecipe.OnCall = (
                        ISystemContext context,
                        MethodState method,
                        NodeId objectId,
                        string recipeId,
                        LocalizedText recipeName,
                        ref NodeId recipeNodeId) =>
                    {
                        RecipeState? recipe = AddRecipeCore(recipeId, recipeName, out ServiceResult result);
                        if (recipe != null)
                        {
                            recipeNodeId = recipe.NodeId;
                        }
                        return result;
                    };
                }
                if (management.RemoveRecipe != null)
                {
                    management.RemoveRecipe.OnCallAsync = async (context, method, objectId, recipeId, ct) =>
                        new RemoveRecipeMethodStateResult
                        {
                            ServiceResult = await RemoveRecipeAsync(recipeId, ct).ConfigureAwait(false)
                        };
                }
            }

            BindRun(scale.StartRecipe, ScaleCommand.StartRecipe, StartCore);
            BindRun(scale.StopRecipe, ScaleCommand.StopRecipe, Stop);
            BindRun(scale.ContinueRecipe, ScaleCommand.ContinueRecipe, Continue);
            BindRun(scale.SkipCurrentRecipeElement, ScaleCommand.SkipCurrentRecipeElement, SkipCore);
            BindRun(scale.AbortRecipe, ScaleCommand.AbortRecipe, Abort);
        }

        /// <summary>
        /// Gets the recipe scale node.
        /// </summary>
        public RecipeScaleState Scale { get; }

        /// <summary>
        /// Gets the processing state.
        /// </summary>
        public RecipeRunState State { get; private set; }

        /// <summary>
        /// Gets the recipe being processed.
        /// </summary>
        public RecipeState? ActiveRecipe { get; private set; }

        /// <summary>
        /// Gets the recipes, keyed by recipe id.
        /// </summary>
        public IReadOnlyDictionary<string, RecipeState> Recipes
        {
            get
            {
                lock (m_lock)
                {
                    return new Dictionary<string, RecipeState>(m_recipes, StringComparer.Ordinal);
                }
            }
        }

        /// <summary>
        /// Gets the elements currently being executed.
        /// </summary>
        public ArrayOf<RecipeElementState> CurrentElements
        {
            get
            {
                lock (m_lock)
                {
                    return m_current.ToArrayOf();
                }
            }
        }

        /// <summary>
        /// Raised when an element becomes current.
        /// </summary>
        public event EventHandler<RecipeElementEventArgs>? ElementStarted;

        /// <summary>
        /// Raised when the active recipe completed all its elements.
        /// </summary>
        public event EventHandler<RecipeState>? RecipeCompleted;

        /// <summary>
        /// Gets or sets the parser for recipes uploaded through their
        /// <c>RecipeFile</c> (the "Scales FileRecipeManagement" conformance
        /// unit). OPC 40200 leaves the file format to the vendor, so the
        /// application supplies it; a good result marks the recipe as managed
        /// by its file, a bad one rejects the upload. Without a parser an
        /// upload is rejected with <c>Bad_NotSupported</c>.
        /// </summary>
        public Func<RecipeState, ByteString, ServiceResult>? RecipeFileHandler { get; set; }

        /// <summary>
        /// Gets whether recipes carry a <c>RecipeFile</c> clients can upload
        /// through (the "Scales FileRecipeManagement" conformance unit).
        /// </summary>
        public bool RecipeFiles => m_recipeFiles;

        /// <summary>
        /// Gets the content last uploaded to a recipe's <c>RecipeFile</c>.
        /// </summary>
        /// <param name="recipe">The recipe.</param>
        public ByteString ReadRecipeFile(RecipeState recipe)
        {
            return recipe?.RecipeFile == null
                ? ByteString.Empty
                : ByteString.From(m_files.Snapshot(recipe.RecipeFile));
        }

        /// <summary>
        /// Adds a recipe from the application.
        /// </summary>
        /// <param name="recipeId">The unique recipe id.</param>
        /// <param name="recipeName">The recipe name.</param>
        /// <exception cref="ServiceResultException">The recipe cannot be added.</exception>
        public RecipeState AddRecipe(string recipeId, LocalizedText recipeName)
        {
            RecipeState? recipe = AddRecipeCore(recipeId, recipeName, out ServiceResult result);
            return recipe ?? throw new ServiceResultException(result);
        }

        /// <summary>
        /// Adds an element to a recipe from the application.
        /// </summary>
        /// <param name="recipe">The recipe.</param>
        /// <param name="elementType">
        /// The element type: <c>ActivationType</c>, <c>TimerType</c>,
        /// <c>UserInstructionType</c>, <c>WeighingType</c>,
        /// <c>AnalogConditionSleepType</c> or <c>EdgeTriggeredSleepType</c>.
        /// </param>
        /// <param name="elementName">The element name.</param>
        /// <param name="previousElements">
        /// The predecessors; the recipe itself for a start element.
        /// </param>
        /// <exception cref="ServiceResultException">The element cannot be added.</exception>
        public RecipeElementState AddRecipeElement(
            RecipeState recipe,
            NodeId elementType,
            string elementName,
            params NodeId[] previousElements)
        {
            RecipeElementState? element = AddElementCore(
                recipe,
                elementType,
                elementName,
                previousElements.ToArrayOf(),
                out ServiceResult result);
            return element ?? throw new ServiceResultException(result);
        }

        /// <summary>
        /// Reports that the application finished executing a current element.
        /// </summary>
        /// <param name="element">The element.</param>
        /// <remarks>
        /// <see cref="ElementStarted"/> and <see cref="RecipeCompleted"/> are
        /// raised after the controller's lock is released, so a handler may
        /// call back into the controller - for example complete an element
        /// that needs no time from its <see cref="ElementStarted"/> handler.
        /// </remarks>
        public ServiceResult CompleteElement(RecipeElementState element)
        {
            ServiceResult result;
            lock (m_lock)
            {
                if (ActiveRecipe == null || !m_current.Contains(element))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "The element is not being executed.");
                }
                CompleteCore(element, "completed");
                result = ServiceResult.Good;
            }
            RaisePending();
            return result;
        }

        private RecipeState? AddRecipeCore(string recipeId, LocalizedText recipeName, out ServiceResult result)
        {
            RecipeState recipe;
            lock (m_lock)
            {
                if (Scale.Recipes == null)
                {
                    result = ServiceResult.Create(StatusCodes.BadNotSupported, "This scale has no recipe management.");
                    return null;
                }
                if (string.IsNullOrEmpty(recipeId))
                {
                    result = ServiceResult.Create(StatusCodes.BadInvalidArgument, "A recipe id is required.");
                    return null;
                }
                if (m_recipes.ContainsKey(recipeId))
                {
                    result = ServiceResult.Create(
                        StatusCodes.BadNodeIdExists,
                        "A recipe with id '{0}' already exists.",
                        recipeId);
                    return null;
                }
                ISystemContext context = m_owner.Services.Context;
                recipe = context.CreateInstanceOfRecipeType(Scale.Recipes, m_owner.Services.InstanceName(recipeId));
                recipe.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
                recipe.DisplayName = new LocalizedText(recipeId);
                ScaleValues.Set(context, recipe.RecipeId, recipeId);
                ScaleValues.Set(context, recipe.RecipeName, recipeName);
                recipe.AddRecipeElements(context);
                recipe.AddAddRecipeElement(context);
                recipe.AddRemoveRecipeElement(context);
                if (m_recipeFiles)
                {
                    recipe.AddRecipeFile(context);
                }
                Scale.Recipes.AddChild(recipe);
                m_recipes[recipeId] = recipe;
            }
            m_owner.Services.Register(recipe);
            BindRecipe(recipe);
            AttachRecipeFile(recipe);
            result = ServiceResult.Good;
            return recipe;
        }

        private async ValueTask<ServiceResult> RemoveRecipeAsync(string recipeId, CancellationToken ct)
        {
            RecipeState? recipe;
            lock (m_lock)
            {
                if (!m_recipes.TryGetValue(recipeId ?? string.Empty, out recipe))
                {
                    return ServiceResult.Create(StatusCodes.BadNotFound, "No recipe with id '{0}' exists.", recipeId ?? string.Empty);
                }
                if (ReferenceEquals(ActiveRecipe, recipe))
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState, "Recipe '{0}' is being processed.", recipeId ?? string.Empty);
                }
                m_recipes.Remove(recipeId!);
            }
            await m_owner.Services.Delete(recipe.NodeId, ct).ConfigureAwait(false);
            Scale.Recipes?.RemoveChild(recipe);
            return ServiceResult.Good;
        }

        private void BindRecipe(RecipeState recipe)
        {
            if (recipe.AddRecipeElement != null)
            {
                recipe.AddRecipeElement.OnCall = (
                    ISystemContext context,
                    MethodState method,
                    NodeId objectId,
                    NodeId elementType,
                    string elementName,
                    ArrayOf<NodeId> previousElements,
                    ref NodeId elementNodeId) =>
                {
                    RecipeElementState? element = AddElementCore(
                        recipe,
                        elementType,
                        elementName,
                        previousElements,
                        out ServiceResult result);
                    if (element != null)
                    {
                        elementNodeId = element.NodeId;
                    }
                    return result;
                };
            }
            if (recipe.RemoveRecipeElement != null)
            {
                recipe.RemoveRecipeElement.OnCallAsync = async (context, method, objectId, elementNodeId, ct) =>
                    new RemoveRecipeElementMethodStateResult
                    {
                        ServiceResult = await RemoveElementAsync(recipe, elementNodeId, ct).ConfigureAwait(false)
                    };
            }
        }

        private RecipeElementState? AddElementCore(
            RecipeState recipe,
            NodeId elementType,
            string elementName,
            ArrayOf<NodeId> previousElements,
            out ServiceResult result)
        {
            RecipeElementState? element;
            lock (m_lock)
            {
                if (recipe.RecipeFile != null && m_uploadedRecipes.Contains(recipe))
                {
                    // §7.31.4: a recipe uploaded as a file must not diverge
                    // from its file through the element methods.
                    result = ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "The recipe was uploaded as a file; edit the file instead.");
                    return null;
                }
                if (ReferenceEquals(ActiveRecipe, recipe))
                {
                    result = ServiceResult.Create(StatusCodes.BadInvalidState, "The recipe is being processed.");
                    return null;
                }
                if (string.IsNullOrEmpty(elementName))
                {
                    result = ServiceResult.Create(StatusCodes.BadInvalidArgument, "An element name is required.");
                    return null;
                }
                if (previousElements.Count == 0)
                {
                    result = ServiceResult.Create(
                        StatusCodes.BadInvalidArgument,
                        "An element needs at least one predecessor; use the recipe itself for a start element.");
                    return null;
                }
                List<NodeState> previous = [];
                List<RecipeElementState> elements = ElementsOf(recipe);
                foreach (NodeId id in previousElements)
                {
                    NodeState? predecessor = id == recipe.NodeId
                        ? recipe
                        : elements.FirstOrDefault(e => e.NodeId == id);
                    if (predecessor == null)
                    {
                        result = ServiceResult.Create(
                            StatusCodes.BadNodeIdUnknown,
                            "'{0}' is neither the recipe nor one of its elements.",
                            id);
                        return null;
                    }
                    previous.Add(predecessor);
                }
                if (recipe.RecipeElements == null)
                {
                    result = ServiceResult.Create(StatusCodes.BadInvalidState, "The recipe has no RecipeElements folder.");
                    return null;
                }
                if (elements.Any(e => e.BrowseName.Name == elementName))
                {
                    result = ServiceResult.Create(
                        StatusCodes.BadBrowseNameDuplicated,
                        "The recipe already has an element named '{0}'.",
                        elementName);
                    return null;
                }

                element = CreateElement(elementType, recipe.RecipeElements, m_owner.Services.InstanceName(elementName));
                if (element == null)
                {
                    result = ServiceResult.Create(
                        StatusCodes.BadTypeMismatch,
                        "'{0}' is not a concrete recipe element type.",
                        elementType);
                    return null;
                }
                element.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.Organizes;
                element.DisplayName = new LocalizedText(elementName);
                recipe.RecipeElements.AddChild(element);

                // The new element has no successors yet, so edges into it
                // cannot close a cycle - the graph stays acyclic (Annex B).
                foreach (NodeState predecessor in previous)
                {
                    predecessor.AddReference(m_nextRecipeElement, false, element.NodeId);
                    element.AddReference(m_nextRecipeElement, true, predecessor.NodeId);
                }
            }
            m_owner.Services.Register(element);
            recipe.ClearChangeMasks(m_owner.Services.Context, includeChildren: true);
            result = ServiceResult.Good;
            return element;
        }

        private async ValueTask<ServiceResult> RemoveElementAsync(RecipeState recipe, NodeId elementNodeId, CancellationToken ct)
        {
            RecipeElementState? element;
            lock (m_lock)
            {
                if (ReferenceEquals(ActiveRecipe, recipe))
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState, "The recipe is being processed.");
                }
                element = ElementsOf(recipe).FirstOrDefault(e => e.NodeId == elementNodeId);
                if (element == null)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadNodeIdUnknown,
                        "'{0}' is not an element of this recipe.",
                        elementNodeId);
                }
                // §7.31.5: the element goes together with all its references.
                recipe.RemoveReference(m_nextRecipeElement, false, element.NodeId);
                foreach (RecipeElementState other in ElementsOf(recipe))
                {
                    other.RemoveReference(m_nextRecipeElement, false, element.NodeId);
                    other.RemoveReference(m_nextRecipeElement, true, element.NodeId);
                }
            }
            await m_owner.Services.Delete(element.NodeId, ct).ConfigureAwait(false);
            recipe.RecipeElements?.RemoveChild(element);
            return ServiceResult.Good;
        }

        private void AttachRecipeFile(RecipeState recipe)
        {
            if (recipe.RecipeFile == null)
            {
                return;
            }
            m_files.Attach(recipe.RecipeFile, [], bytes =>
            {
                if (RecipeFileHandler == null)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadNotSupported,
                        "This server has no parser for uploaded recipe files.");
                }
                bool active;
                lock (m_lock)
                {
                    active = ReferenceEquals(ActiveRecipe, recipe);
                }
                if (active)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState, "The recipe is being processed.");
                }

                // The parser is application code and runs outside the lock.
                ServiceResult result = RecipeFileHandler(recipe, ByteString.From(bytes));
                if (ServiceResult.IsGood(result))
                {
                    MarkUploadedFromFile(recipe);
                }
                return result;
            });
        }

        /// <summary>
        /// Marks a recipe as uploaded through its <c>RecipeFile</c>, which
        /// blocks the element methods (§7.31.4).
        /// </summary>
        /// <param name="recipe">The recipe.</param>
        public void MarkUploadedFromFile(RecipeState recipe)
        {
            lock (m_lock)
            {
                m_uploadedRecipes.Add(recipe);
            }
        }

        private RecipeElementState? CreateElement(NodeId elementType, NodeState parent, QualifiedName browseName)
        {
            ISystemContext context = m_owner.Services.Context;
            if (elementType.NamespaceIndex != m_owner.Services.Namespaces.Scales ||
                !elementType.TryGetValue(out uint id))
            {
                return null;
            }
            return id switch
            {
                ObjectTypes.ActivationType => context.CreateInstanceOfActivationType(parent, browseName),
                ObjectTypes.TimerType => context.CreateInstanceOfTimerType(parent, browseName),
                ObjectTypes.UserInstructionType => context.CreateInstanceOfUserInstructionType(parent, browseName),
                ObjectTypes.WeighingType => context.CreateInstanceOfWeighingType(parent, browseName),
                ObjectTypes.AnalogConditionSleepType => context.CreateInstanceOfAnalogConditionSleepType(parent, browseName),
                ObjectTypes.EdgeTriggeredSleepType => context.CreateInstanceOfEdgeTriggeredSleepType(parent, browseName),
                _ => null
            };
        }

        private List<RecipeElementState> ElementsOf(RecipeState recipe)
        {
            var children = new List<BaseInstanceState>();
            recipe.RecipeElements?.GetChildren(m_owner.Services.Context, children);
            return [.. children.OfType<RecipeElementState>()];
        }

        private void BindRun(MethodState? method, ScaleCommand command, Func<NodeId, ServiceResult> action)
        {
            if (method == null)
            {
                return;
            }
            method.OnCallMethod2 = (context, called, objectId, inputs, outputs) =>
            {
                NodeId recipeNodeId = inputs.Count > 0 && inputs[0].TryGetValue(out NodeId id) ? id : NodeId.Null;
                ServiceResult result = m_owner.Execute(command, () => action(recipeNodeId));

                // Handlers run after the scale's and the controller's lock are
                // released, so they may call back into the controller.
                RaisePending();
                return result;
            };
        }

        /// <summary>
        /// Starts processing a recipe (§7.29.4).
        /// </summary>
        /// <param name="recipeNodeId">The recipe.</param>
        public ServiceResult Start(NodeId recipeNodeId)
        {
            ServiceResult result = StartCore(recipeNodeId);
            RaisePending();
            return result;
        }

        private ServiceResult StartCore(NodeId recipeNodeId)
        {
            lock (m_lock)
            {
                if (State != RecipeRunState.Idle)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState, "A recipe is already being processed.");
                }
                RecipeState? recipe = m_recipes.Values.FirstOrDefault(r => r.NodeId == recipeNodeId);
                if (recipe == null)
                {
                    return ServiceResult.Create(StatusCodes.BadNodeIdUnknown, "'{0}' is not a recipe of this scale.", recipeNodeId);
                }
                // §7.29.4: the scale has to be released for processing. With a
                // PackML state machine that is the Idle or Execute state.
                if (m_owner.PackML is { } packMl &&
                    packMl.CurrentState is not (PackMLStateNumbers.Idle or PackMLStateNumbers.Execute))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "The scale is not released for processing (PackML state {0}).",
                        packMl.CurrentState);
                }
                List<RecipeElementState> starts = Successors(recipe, recipe);
                if (starts.Count == 0)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState, "The recipe has no start element.");
                }
                ActiveRecipe = recipe;
                State = RecipeRunState.Running;
                m_done.Clear();
                m_current.Clear();
                Report($"Recipe '{recipe.BrowseName.Name}' started.");
                foreach (RecipeElementState element in starts)
                {
                    Begin(element);
                }
                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Pauses processing (§7.29.5).
        /// </summary>
        /// <param name="recipeNodeId">The recipe.</param>
        public ServiceResult Stop(NodeId recipeNodeId)
        {
            lock (m_lock)
            {
                ServiceResult check = CheckActive(recipeNodeId, RecipeRunState.Running);
                if (ServiceResult.IsBad(check))
                {
                    return check;
                }
                State = RecipeRunState.Paused;
                Report("Recipe paused.");
                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Resumes a paused recipe (§7.29.6).
        /// </summary>
        /// <param name="recipeNodeId">The recipe.</param>
        public ServiceResult Continue(NodeId recipeNodeId)
        {
            lock (m_lock)
            {
                ServiceResult check = CheckActive(recipeNodeId, RecipeRunState.Paused);
                if (ServiceResult.IsBad(check))
                {
                    return check;
                }
                State = RecipeRunState.Running;
                Report("Recipe continued.");
                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Completes the current elements from the client side (§7.29.7).
        /// </summary>
        /// <param name="recipeNodeId">The recipe.</param>
        public ServiceResult Skip(NodeId recipeNodeId)
        {
            ServiceResult result = SkipCore(recipeNodeId);
            RaisePending();
            return result;
        }

        private ServiceResult SkipCore(NodeId recipeNodeId)
        {
            lock (m_lock)
            {
                ServiceResult check = CheckActive(recipeNodeId, RecipeRunState.Running);
                if (ServiceResult.IsBad(check))
                {
                    return check;
                }
                foreach (RecipeElementState element in m_current.ToList())
                {
                    CompleteCore(element, "skipped");
                }
                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Ends processing (§7.29.8).
        /// </summary>
        /// <param name="recipeNodeId">The recipe.</param>
        public ServiceResult Abort(NodeId recipeNodeId)
        {
            lock (m_lock)
            {
                if (ActiveRecipe == null || ActiveRecipe.NodeId != recipeNodeId)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState, "'{0}' is not being processed.", recipeNodeId);
                }
                Report("Recipe aborted.");
                Finish();
                return ServiceResult.Good;
            }
        }

        private ServiceResult CheckActive(NodeId recipeNodeId, RecipeRunState expected)
        {
            if (ActiveRecipe == null || ActiveRecipe.NodeId != recipeNodeId)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState, "'{0}' is not being processed.", recipeNodeId);
            }
            if (State != expected)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState, "The recipe is {0}, not {1}.", State, expected);
            }
            return ServiceResult.Good;
        }

        private void CompleteCore(RecipeElementState element, string how)
        {
            m_current.Remove(element);
            m_done.Add(element);
            Report($"Element '{element.BrowseName.Name}' {how}.");
            RecipeState recipe = ActiveRecipe!;
            foreach (RecipeElementState next in Successors(recipe, element))
            {
                // A join waits for every predecessor.
                if (!m_current.Contains(next) && !m_done.Contains(next) &&
                    Predecessors(recipe, next).All(p => ReferenceEquals(p, recipe) || m_done.Contains(p)))
                {
                    Begin(next);
                }
            }
            if (m_current.Count == 0)
            {
                Report($"Recipe '{recipe.BrowseName.Name}' completed.");
                Finish();
                m_pending.Add(() => RecipeCompleted?.Invoke(this, recipe));
            }
        }

        private void Begin(RecipeElementState element)
        {
            m_current.Add(element);
            Report($"Element '{element.BrowseName.Name}' started.");
            var args = new RecipeElementEventArgs(ActiveRecipe!, element);
            m_pending.Add(() => ElementStarted?.Invoke(this, args));
        }

        /// <summary>
        /// Raises the notifications queued under the lock, outside of it. Each
        /// caller takes the batch queued so far, so a handler that calls back
        /// into the controller raises its own notifications in turn.
        /// </summary>
        private void RaisePending()
        {
            List<Action> pending;
            lock (m_lock)
            {
                if (m_pending.Count == 0)
                {
                    return;
                }
                pending = m_pending;
                m_pending = [];
            }
            foreach (Action notify in pending)
            {
                notify();
            }
        }

        private void Finish()
        {
            ActiveRecipe = null;
            State = RecipeRunState.Idle;
            m_current.Clear();
            m_done.Clear();
        }

        private List<RecipeElementState> Successors(RecipeState recipe, NodeState node)
        {
            var references = new List<IReference>();
            node.GetReferences(m_owner.Services.Context, references, m_nextRecipeElement, false);
            List<RecipeElementState> elements = ElementsOf(recipe);
            var successors = new List<RecipeElementState>();
            foreach (IReference reference in references)
            {
                RecipeElementState? element = elements.FirstOrDefault(e => e.NodeId == (NodeId)reference.TargetId);
                if (element != null)
                {
                    successors.Add(element);
                }
            }
            return successors;
        }

        private List<NodeState> Predecessors(RecipeState recipe, RecipeElementState element)
        {
            var references = new List<IReference>();
            element.GetReferences(m_owner.Services.Context, references, m_nextRecipeElement, true);
            List<RecipeElementState> elements = ElementsOf(recipe);
            var predecessors = new List<NodeState>();
            foreach (IReference reference in references)
            {
                var target = (NodeId)reference.TargetId;
                if (target == recipe.NodeId)
                {
                    predecessors.Add(recipe);
                }
                else if (elements.FirstOrDefault(e => e.NodeId == target) is { } predecessor)
                {
                    predecessors.Add(predecessor);
                }
            }
            return predecessors;
        }

        /// <summary>
        /// Appends a line to the <c>Report</c> of the recipe product in
        /// processing, when there is one (§7.30).
        /// </summary>
        private void Report(string message)
        {
            if (m_owner.ProductionPreset?.ActiveProduct is RecipeProductState product && product.Report != null)
            {
                var entries = new List<RecipeReportElementType>();
                foreach (RecipeReportElementType entry in product.Report.Value)
                {
                    entries.Add(entry);
                }
                entries.Add(new RecipeReportElementType
                {
                    ReportMessage = new LocalizedText(message),
                    Timestamp = DateTimeUtc.Now
                });
                product.Report.Value = entries.ToArrayOf();
                ScaleValues.Touch(m_owner.Services.Context, product.Report);
                PublishReportFile(product, entries);
            }
        }

        /// <summary>
        /// Serves the recipe report as the product's read-only
        /// <c>ReportFile</c>, one line per entry, when the product has one.
        /// </summary>
        private void PublishReportFile(RecipeProductState product, List<RecipeReportElementType> entries)
        {
            if (product.ReportFile == null)
            {
                return;
            }
            if (m_reportFiles.Add(product.ReportFile))
            {
                m_files.Attach(product.ReportFile, [], uploaded: null);
            }
            var text = new StringBuilder();
            foreach (RecipeReportElementType entry in entries)
            {
                text.Append(((DateTime)entry.Timestamp).ToString("O", System.Globalization.CultureInfo.InvariantCulture))
                    .Append('\t')
                    .Append(entry.ReportMessage.Text)
                    .Append('\n');
            }
            m_files.Replace(product.ReportFile, Encoding.UTF8.GetBytes(text.ToString()));
        }

        private readonly ScaleHandle m_owner;
        private readonly ScaleFileBinder m_files = new();
        private readonly HashSet<FileState> m_reportFiles = [];
        private readonly bool m_recipeFiles;
        private readonly NodeId m_nextRecipeElement;
        private readonly Dictionary<string, RecipeState> m_recipes = new(StringComparer.Ordinal);
        private readonly HashSet<RecipeState> m_uploadedRecipes = [];
        private readonly List<RecipeElementState> m_current = [];
        private List<Action> m_pending = [];
        private readonly HashSet<RecipeElementState> m_done = [];
        private readonly Lock m_lock = new();
    }
}
