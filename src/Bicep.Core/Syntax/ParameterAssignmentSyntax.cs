// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using Bicep.Core.Parsing;

namespace Bicep.Core.Syntax
{
    public class ParameterAssignmentSyntax : StatementSyntax
    {
        public ParameterAssignmentSyntax(Token keyword, IdentifierSyntax name, SyntaxBase assignment, SyntaxBase value)
            : base(Enumerable.Empty<SyntaxBase>())
        {
            AssertKeyword(keyword, nameof(keyword), LanguageConstants.ParameterKeyword);
            AssertSyntaxType(name, nameof(name), typeof(IdentifierSyntax));
            
            this.Keyword = keyword;
            this.Name = name;
            this.Assignment = assignment;
            this.Value = value;
        }

        public Token Keyword { get; }

        public IdentifierSyntax Name { get; }
        public SyntaxBase Assignment { get; }
        public SyntaxBase Value { get; }

        // This is a modifier of the parameter and not a modifier of the type
        public SyntaxBase? Modifier { get; }

        public override void Accept(ISyntaxVisitor visitor)
            => visitor.VisitParameterAssignmentSyntax(this);

        public override TextSpan Span => TextSpan.Between(this.Keyword, this.Value);

        /// <summary>
        /// Gets the declared type syntax of this parameter declaration. Certain parse errors will cause it to be null.
        /// </summary>
        public TypeSyntax? ParameterType => null; // TODO: fix when implementing semantic analysis
    }
}
