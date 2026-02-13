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

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using IIncrementalGenerator = SGF.IncrementalGenerator;
using IncrementalGeneratorAttribute = SGF.IncrementalGeneratorAttribute;
using IncrementalGeneratorInitializationContext = SGF.SgfInitializationContext;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generates server and client models using the model generator library
    /// </summary>
    [IncrementalGenerator]
    public class ModelSourceGenerator : IIncrementalGenerator
    {
        /// <inheritdoc/>
        public ModelSourceGenerator()
            : base(SourceGenerator.Name)
        {
        }

        /// <inheritdoc/>
        public override void OnInitialize(IncrementalGeneratorInitializationContext context)
        {
#if DEBUGX
            AttachDebugger();
#endif
            IncrementalValueProvider<ImmutableArray<(AdditionalText Left, NodesetFileOptions)>> inputFiles =
                context.AdditionalTextsProvider
                    .Where(f => f.IsDesignOrNodeset2File())
                    .Combine(context.AnalyzerConfigOptionsProvider)
                    .Select((pair, _) => (
                        pair.Left,
                        pair.Right.GetOptions(pair.Left).ToNodeSetOptions()))
                    .Collect();
            IncrementalValueProvider<ImmutableArray<AdditionalText>> identiferFile =
                context.AdditionalTextsProvider
                    .Where(f => f.IsIdentifierFile())
                    .Collect();
            IncrementalValueProvider<ModelCompilationOptions> options =
                context.AnalyzerConfigOptionsProvider
                    .Select((p, _) => ModelCompilationOptions.From(p));
            IncrementalValueProvider<CompilationOptions> settings =
                context.CompilationProvider
                    .Select((c, _) => CompilationOptions.From(c));

            context.RegisterSourceOutput(
                inputFiles
                    .Combine(identiferFile)
                    .Combine(options)
                    .Combine(settings),
                (context, combination) => new ModelCompilation(
                    context,
                    combination.Left.Left.Left,
                    combination.Left.Left.Right,
                    combination.Left.Right,
                    combination.Right,
                    Logger).Emit(context.CancellationToken));

            context.RegisterSourceOutput(context.SyntaxProvider.ForAttributeWithMetadataName(
                    "Opc.Ua.DataTypeAttribute",
                    static (node, ct) => DataTypeCompilation.Handles(node, ct),
                    static (context, ct) => new DataTypeCompilation(context, ct))
                .Where(static m => m is not null),
                static (spc, source) => source.Emit(spc));
        }
    }
}
