// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Generic;
using System.Collections.Immutable;
using Bicep.Core.Syntax;

namespace Bicep.Core.Semantics
{
    /// <summary>
    /// Represents a language scope that declares local symbols. (For example the item or index variables in loops are local symbols.)
    /// </summary>
    public class LocalScope
    {
        public LocalScope(SyntaxBase enclosingSyntax, IEnumerable<DeclaredSymbol> declaredSymbols)
        {
            this.EnclosingSyntax = enclosingSyntax;
            this.DeclaredSymbols = declaredSymbols.ToImmutableArray();
        }

        public SyntaxBase EnclosingSyntax { get; }

        public ImmutableArray<DeclaredSymbol> DeclaredSymbols { get; }
    }
}