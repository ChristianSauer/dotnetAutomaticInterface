using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AutomaticInterface
{
    internal sealed class AddNullableAnnotationSyntaxRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitParameter(ParameterSyntax node)
        {
            var newNode = node;
            if (node.Type != null)
            {
                newNode = node.WithType(
                    SyntaxFactory.ParseTypeName(
                        node.Type.GetLeadingTrivia()
                            + node.Type.ToString()
                            + "?"
                            + node.Type.GetTrailingTrivia()
                    )
                );
            }
            return base.VisitParameter(newNode);
        }
    }
}
