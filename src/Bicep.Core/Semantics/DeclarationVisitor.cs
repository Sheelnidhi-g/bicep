// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Generic;
using Bicep.Core.Extensions;
using Bicep.Core.Syntax;

namespace Bicep.Core.Semantics
{
    public class DeclarationVisitor: SyntaxVisitor
    {
        private readonly ISymbolContext context;

        private readonly IList<DeclaredSymbol> declaredSymbols;

        private readonly IDictionary<SyntaxBase, LocalScope> localScopes; 

        public DeclarationVisitor(ISymbolContext context, IList<DeclaredSymbol> declaredSymbols, IDictionary<SyntaxBase, LocalScope> localScopes)
        {
            this.context = context;
            this.declaredSymbols = declaredSymbols;
            this.localScopes = localScopes;
        }

        public override void VisitParameterDeclarationSyntax(ParameterDeclarationSyntax syntax)
        {
            base.VisitParameterDeclarationSyntax(syntax);

            var symbol = new ParameterSymbol(this.context, syntax.Name.IdentifierName, syntax, syntax.Modifier);
            this.declaredSymbols.Add(symbol);
        }

        public override void VisitVariableDeclarationSyntax(VariableDeclarationSyntax syntax)
        {
            base.VisitVariableDeclarationSyntax(syntax);

            var symbol = new VariableSymbol(this.context, syntax.Name.IdentifierName, syntax, syntax.Value);
            this.declaredSymbols.Add(symbol);
        }

        public override void VisitResourceDeclarationSyntax(ResourceDeclarationSyntax syntax)
        {
            base.VisitResourceDeclarationSyntax(syntax);

            var symbol = new ResourceSymbol(this.context, syntax.Name.IdentifierName, syntax);
            this.declaredSymbols.Add(symbol);
        }

        public override void VisitModuleDeclarationSyntax(ModuleDeclarationSyntax syntax)
        {
            base.VisitModuleDeclarationSyntax(syntax);

            var symbol = new ModuleSymbol(this.context, syntax.Name.IdentifierName, syntax);
            this.declaredSymbols.Add(symbol);
        }

        public override void VisitOutputDeclarationSyntax(OutputDeclarationSyntax syntax)
        {
            base.VisitOutputDeclarationSyntax(syntax);

            var symbol = new OutputSymbol(this.context, syntax.Name.IdentifierName, syntax, syntax.Value);
            this.declaredSymbols.Add(symbol);
        }

        public override void VisitForSyntax(ForSyntax syntax)
        {
            base.VisitForSyntax(syntax);

            var itemVariable = new LocalSymbol(this.context, syntax.ItemVariable.Name.IdentifierName, syntax.ItemVariable);
            var scope = new LocalScope(syntax, itemVariable.AsEnumerable());
            this.localScopes.Add(syntax, scope);
        }
    }
}

