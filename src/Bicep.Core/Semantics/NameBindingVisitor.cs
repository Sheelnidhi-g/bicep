// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Bicep.Core.Diagnostics;
using Bicep.Core.Extensions;
using Bicep.Core.Syntax;
using Bicep.Core.TypeSystem;

namespace Bicep.Core.Semantics
{
    public sealed class NameBindingVisitor : SyntaxVisitor
    {
        private FunctionFlags allowedFlags;

        private readonly IReadOnlyDictionary<string, DeclaredSymbol> declarations;

        private readonly IDictionary<SyntaxBase, Symbol> bindings;

        private readonly ImmutableDictionary<string, NamespaceSymbol> namespaces;

        private readonly IReadOnlyDictionary<SyntaxBase, LocalScope> localScopes;

        // list of active local scopes - we're using it like a stack but with the ability
        // to peek at all the items that have been pushed already
        private readonly IList<LocalScope> activeScopes;

        public NameBindingVisitor(IReadOnlyDictionary<string, DeclaredSymbol> declarations, IDictionary<SyntaxBase, Symbol> bindings, ImmutableDictionary<string, NamespaceSymbol> namespaces, IReadOnlyDictionary<SyntaxBase, LocalScope> localScopes)
        {
            this.declarations = declarations;
            this.bindings = bindings;
            this.namespaces = namespaces;
            this.localScopes = localScopes;
            this.activeScopes = new List<LocalScope>();
        }

        public override void VisitProgramSyntax(ProgramSyntax syntax)
        {
            base.VisitProgramSyntax(syntax);

            // create bindings for all of the declarations to their corresponding symbol
            // this is needed to make find all references work correctly
            // (doing this here to avoid side-effects in the constructor)
            foreach (DeclaredSymbol declaredSymbol in this.declarations.Values)
            {
                this.bindings.Add(declaredSymbol.DeclaringSyntax, declaredSymbol);
            }
        }

        public override void VisitVariableAccessSyntax(VariableAccessSyntax syntax)
        {
            base.VisitVariableAccessSyntax(syntax);

            var symbol = this.LookupSymbolByName(syntax.Name, false);

            // bind what we got - the type checker will validate if it fits
            this.bindings.Add(syntax, symbol);
        }

        public override void VisitResourceDeclarationSyntax(ResourceDeclarationSyntax syntax)
        {
            allowedFlags = FunctionFlags.ResoureDecorator;
            this.VisitNodes(syntax.LeadingNodes);
            this.Visit(syntax.Keyword);
            this.Visit(syntax.Name);
            this.Visit(syntax.Type);
            this.Visit(syntax.Assignment);
            allowedFlags = FunctionFlags.RequiresInlining;
            this.Visit(syntax.Value);
            allowedFlags = FunctionFlags.Default;
        }

        public override void VisitModuleDeclarationSyntax(ModuleDeclarationSyntax syntax)
        {
            allowedFlags = FunctionFlags.ModuleDecorator;
            this.VisitNodes(syntax.LeadingNodes);
            this.Visit(syntax.Keyword);
            this.Visit(syntax.Name);
            this.Visit(syntax.Path);
            this.Visit(syntax.Assignment);
            allowedFlags = FunctionFlags.RequiresInlining;
            this.Visit(syntax.Value);
            allowedFlags = FunctionFlags.Default;
        }

        public override void VisitIfConditionSyntax(IfConditionSyntax syntax)
        {
            this.Visit(syntax.Keyword);
            allowedFlags = FunctionFlags.Default;
            this.Visit(syntax.ConditionExpression);
            // if-condition syntax parent is always a resource/module declaration
            // this means that we have to allow the functions that are only allowed
            // in resource bodies by our runtime (like reference() or listKeys())
            // TODO: Update when conditions can be composed together with loops
            allowedFlags = FunctionFlags.RequiresInlining;
            this.Visit(syntax.Body);
            allowedFlags = FunctionFlags.Default;
        }

        public override void VisitVariableDeclarationSyntax(VariableDeclarationSyntax syntax)
        {
            allowedFlags = FunctionFlags.VariableDecorator;
            this.VisitNodes(syntax.LeadingNodes);
            this.Visit(syntax.Keyword);
            this.Visit(syntax.Name);
            this.Visit(syntax.Assignment);
            allowedFlags = FunctionFlags.RequiresInlining;
            this.Visit(syntax.Value);
            allowedFlags = FunctionFlags.Default;
        }

        public override void VisitOutputDeclarationSyntax(OutputDeclarationSyntax syntax)
        {
            allowedFlags = FunctionFlags.OutputDecorator;
            this.VisitNodes(syntax.LeadingNodes);
            this.Visit(syntax.Keyword);
            this.Visit(syntax.Name);
            this.Visit(syntax.Type);
            this.Visit(syntax.Assignment);
            allowedFlags = FunctionFlags.RequiresInlining;
            this.Visit(syntax.Value);
            allowedFlags = FunctionFlags.Default;
        }

        public override void VisitParameterDeclarationSyntax(ParameterDeclarationSyntax syntax)
        {
            allowedFlags = FunctionFlags.ParameterDecorator;
            this.VisitNodes(syntax.LeadingNodes);
            this.Visit(syntax.Keyword);
            this.Visit(syntax.Name);
            this.Visit(syntax.Type);
            allowedFlags = FunctionFlags.ParamDefaultsOnly;
            this.Visit(syntax.Modifier);
            allowedFlags = FunctionFlags.Default;
        }

        public override void VisitFunctionCallSyntax(FunctionCallSyntax syntax)
        {
            FunctionFlags currentFlags = allowedFlags;
            this.Visit(syntax.Name);
            this.Visit(syntax.OpenParen);
            allowedFlags = allowedFlags.HasDecoratorFlag() ? FunctionFlags.Default : allowedFlags;
            this.VisitNodes(syntax.Arguments);
            this.Visit(syntax.CloseParen);
            allowedFlags = currentFlags;

            var symbol = this.LookupSymbolByName(syntax.Name, true);

            // bind what we got - the type checker will validate if it fits
            this.bindings.Add(syntax, symbol);
        }

        public override void VisitInstanceFunctionCallSyntax(InstanceFunctionCallSyntax syntax)
        {
            FunctionFlags currentFlags = allowedFlags;
            this.Visit(syntax.BaseExpression);
            this.Visit(syntax.Dot);
            this.Visit(syntax.Name);
            this.Visit(syntax.OpenParen);
            allowedFlags = allowedFlags.HasDecoratorFlag() ? FunctionFlags.Default : allowedFlags;
            this.VisitNodes(syntax.Arguments);
            this.Visit(syntax.CloseParen);
            allowedFlags = currentFlags;

            if (!syntax.Name.IsValid)
            {
                // the parser produced an instance function calls with an invalid name
                // all instance function calls must be bound to a symbol, so let's
                // bind to a symbol without any errors (there's already a parse error)
                this.bindings.Add(syntax, new ErrorSymbol());
                return;
            }

            if (bindings.TryGetValue(syntax.BaseExpression, out var baseSymbol) && baseSymbol is NamespaceSymbol namespaceSymbol)
            {
                var functionSymbol = allowedFlags.HasDecoratorFlag()
                    // Decorator functions are only valid when HasDecoratorFlag() is true which means
                    // the instance function call is the top level expression of a DecoratorSyntax node.
                    ? namespaceSymbol.Type.MethodResolver.TryGetSymbol(syntax.Name) ?? namespaceSymbol.Type.DecoratorResolver.TryGetSymbol(syntax.Name)
                    : namespaceSymbol.Type.MethodResolver.TryGetSymbol(syntax.Name);

                var foundSymbol = SymbolValidator.ResolveNamespaceQualifiedFunction(allowedFlags, functionSymbol, syntax.Name, namespaceSymbol);
                
                this.bindings.Add(syntax, foundSymbol);
            }
        }

        public override void VisitForSyntax(ForSyntax syntax)
        {
            if (!this.localScopes.TryGetValue(syntax, out var localScope))
            {
                // code defect in the declaration visitor
                throw new InvalidOperationException($"Local scope is missing for {syntax.GetType().Name} at {syntax.Span}");
            }

            // push it to the stack of active scopes
            // as a result this scope will be used to resolve symbols first
            // (then all the previous one and then finally the global scope)
            this.activeScopes.Add(localScope);

            // visit all the children
            base.VisitForSyntax(syntax);

            // we are leaving the loop scope
            // pop the scope - no symbols will be resolved against it ever again
            Debug.Assert(ReferenceEquals(this.activeScopes[^1], localScope), "ReferenceEquals(this.activeScopes[^1], localScope)");
            this.activeScopes.RemoveAt(this.activeScopes.Count - 1);
        }

        private Symbol LookupSymbolByName(IdentifierSyntax identifierSyntax, bool isFunctionCall) => 
            this.LookupLocalSymbolByName(identifierSyntax, isFunctionCall) ?? LookupGlobalSymbolByName(identifierSyntax, isFunctionCall);

        private Symbol? LookupLocalSymbolByName(IdentifierSyntax identifierSyntax, bool isFunctionCall)
        {
            if (isFunctionCall)
            {
                // functions can't be local symbols
                return null;
            }

            // loop iterates in reverse order
            for (int depth = this.activeScopes.Count - 1; depth >= 0; depth--)
            {
                // resolve symbol against current scope
                // this has a side-effect of binding to the innermost symbol even if there exists 
                // a symbol with duplicate name in one of the parent scopes
                // the duplicate detection errors will be emitted by logic elsewhere
                var scope = this.activeScopes[depth];
                var symbol = LookupLocalSymbolByName(scope, identifierSyntax);
                if (symbol != null)
                {
                    // found a symbol - return it
                    return symbol;
                }
            }

            return null;
        }

        private static Symbol? LookupLocalSymbolByName(LocalScope scope, IdentifierSyntax identifierSyntax) => 
            // bind to first symbol matching the specified identifier
            // (errors about duplicate identifiers are emitted elsewhere)
            // loops currently are the only source of local symbols
            // as a result a local scope can contain between 1 to 2 local symbols
            // linear search should be fine, but this should be revisited if the above is no longer holds true
            scope.DeclaredSymbols.FirstOrDefault(symbol => string.Equals(identifierSyntax.IdentifierName, symbol.Name, LanguageConstants.IdentifierComparison));

        private Symbol LookupGlobalSymbolByName(IdentifierSyntax identifierSyntax, bool isFunctionCall)
        {
            // attempt to find name in the imported namespaces
            if (this.namespaces.TryGetValue(identifierSyntax.IdentifierName, out var namespaceSymbol))
            {
                // namespace symbol found
                return namespaceSymbol;
            }

            // declarations must not have a namespace value, namespaces are used to fully qualify a function access.
            // There might be instances where a variable declaration for example uses the same name as one of the imported
            // functions, in this case to differentiate a variable declaration vs a function access we check the namespace value,
            // the former case must have an empty namespace value whereas the latter will have a namespace value.
            if (this.declarations.TryGetValue(identifierSyntax.IdentifierName, out var localSymbol))
            {
                // we found the symbol in the local namespace
                return localSymbol;
            }

            // attempt to find function in all imported namespaces
            var foundSymbols = this.namespaces
                .Select(kvp => allowedFlags.HasDecoratorFlag()
                    ? kvp.Value.Type.MethodResolver.TryGetSymbol(identifierSyntax) ?? kvp.Value.Type.DecoratorResolver.TryGetSymbol(identifierSyntax)
                    : kvp.Value.Type.MethodResolver.TryGetSymbol(identifierSyntax))
                .Where(symbol => symbol != null)
                .ToList();

            if (foundSymbols.Count > 1)
            {
                // ambiguous symbol
                return new ErrorSymbol(DiagnosticBuilder.ForPosition(identifierSyntax).AmbiguousSymbolReference(identifierSyntax.IdentifierName, this.namespaces.Keys));
            }

            var foundSymbol = foundSymbols.FirstOrDefault();
            return isFunctionCall ? SymbolValidator.ResolveUnqualifiedFunction(allowedFlags, foundSymbol, identifierSyntax, namespaces.Values) : SymbolValidator.ResolveUnqualifiedSymbol(foundSymbol, identifierSyntax, namespaces.Values, declarations.Keys);
        }
    }
}