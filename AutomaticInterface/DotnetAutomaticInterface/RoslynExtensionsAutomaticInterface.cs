using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotnetAutomaticInterface;

/// <summary>
/// AutomaticInterface-specific additions to <see cref="RoslynExtensions"/>
/// </summary>
public static class RoslynExtensionsAutomaticInterface
{
    /// <summary>
    /// Extended version of <see cref="RoslynExtensions.GetWhereStatement" /> with support for generated interface name resolution
    /// </summary>
    public static string GetWhereStatement(
        this ITypeParameterSymbol typeParameterSymbol,
        SymbolDisplayFormat typeDisplayFormat,
        List<string> generatedInterfaceNames
    )
    {
        var result = $"where {typeParameterSymbol.Name} : ";

        var constraints = new List<string>();

        if (typeParameterSymbol.HasReferenceTypeConstraint)
        {
            constraints.Add("class");
        }

        if (typeParameterSymbol.HasValueTypeConstraint)
        {
            constraints.Add("struct");
        }

        if (typeParameterSymbol.HasNotNullConstraint)
        {
            constraints.Add("notnull");
        }

        constraints.AddRange(
            typeParameterSymbol.ConstraintTypes.Select(t =>
                t.ToDisplayString(typeDisplayFormat, generatedInterfaceNames)
            )
        );

        // The new() constraint must be last
        if (typeParameterSymbol.HasConstructorConstraint)
        {
            constraints.Add("new()");
        }

        if (constraints.Count == 0)
        {
            return "";
        }

        result += string.Join(", ", constraints);

        return result;
    }

    public static string ToDisplayString(
        this IParameterSymbol symbol,
        SymbolDisplayFormat displayFormat,
        bool nullableContextEnabled,
        List<string> generatedInterfaceNames
    )
    {
        string? RenderTypeSymbolWithNullableAnnotation(SymbolDisplayPart part) =>
            part.Symbol is ITypeSymbol typeSymbol
                ? typeSymbol
                    .WithNullableAnnotation(NullableAnnotation.Annotated)
                    .ToDisplayString(displayFormat)
                : null;

        // Special case for reference parameters with default value null (e.g. string x = null) - the nullable
        // context isn't applied automatically, so it must be forced explicitly
        var forceNullableAnnotation =
            nullableContextEnabled
            && symbol
                is {
                    Type.IsReferenceType: true,
                    HasExplicitDefaultValue: true,
                    ExplicitDefaultValue: null
                }
            && symbol.NullableAnnotation != NullableAnnotation.Annotated;

        return ToDisplayString(
            symbol,
            displayFormat,
            generatedInterfaceNames,
            forceNullableAnnotation ? RenderTypeSymbolWithNullableAnnotation : null
        );
    }

    public static string ToDisplayString(
        this ITypeSymbol symbol,
        SymbolDisplayFormat displayFormat,
        List<string> generatedInterfaceNames
    ) => ToDisplayString((ISymbol)symbol, displayFormat, generatedInterfaceNames);

    /// <summary>
    /// Wraps <see cref="ITypeSymbol.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat?)" /> with custom resolution for generated types
    /// </summary>
    private static string ToDisplayString(
        this ISymbol symbol,
        SymbolDisplayFormat displayFormat,
        List<string> generatedInterfaceNames,
        Func<SymbolDisplayPart, string?>? customRenderDisplayPart = null
    )
    {
        var displayStringBuilder = new StringBuilder();

        var displayParts = GetDisplayParts(symbol, displayFormat);

        foreach (var part in displayParts)
        {
            if (part.Kind == SymbolDisplayPartKind.ErrorTypeName)
            {
                var unrecognisedName = part.ToString();

                var inferredName = ReplaceWithInferredInterfaceName(
                    unrecognisedName,
                    generatedInterfaceNames
                );

                displayStringBuilder.Append(inferredName);
            }
            else
            {
                var customRender = customRenderDisplayPart?.Invoke(part);
                displayStringBuilder.Append(customRender ?? part.ToString());
            }
        }

        return displayStringBuilder.ToString();
    }

    /// <summary>
    /// The same as <see cref="ISymbol.ToDisplayParts"/> but with adjacent SymbolDisplayParts merged into qualified type references, e.g. [Parent, ., Child] => Parent.Child
    /// </summary>
    private static IEnumerable<SymbolDisplayPart> GetDisplayParts(
        ISymbol symbol,
        SymbolDisplayFormat displayFormat
    )
    {
        var cache = new List<SymbolDisplayPart>();

        foreach (var part in symbol.ToDisplayParts(displayFormat))
        {
            if (cache.Count == 0)
            {
                cache.Add(part);
                continue;
            }

            var previousPart = cache.Last();

            if (IsPartQualificationPunctuation(previousPart) ^ IsPartQualificationPunctuation(part))
            {
                cache.Add(part);
            }
            else
            {
                yield return CombineQualifiedTypeParts(cache);
                cache.Clear();
                cache.Add(part);
            }
        }

        if (cache.Count > 0)
        {
            yield return CombineQualifiedTypeParts(cache);
        }

        static SymbolDisplayPart CombineQualifiedTypeParts(
            ICollection<SymbolDisplayPart> qualifiedTypeParts
        )
        {
            var qualifiedType = qualifiedTypeParts.Last();

            return qualifiedTypeParts.Count == 1
                ? qualifiedType
                : new SymbolDisplayPart(
                    qualifiedType.Kind,
                    qualifiedType.Symbol,
                    string.Join("", qualifiedTypeParts)
                );
        }

        static bool IsPartQualificationPunctuation(SymbolDisplayPart part) =>
            part.ToString() is "." or "::";
    }

    private static string ReplaceWithInferredInterfaceName(
        string unrecognisedName,
        List<string> generatedInterfaceNames
    )
    {
        var matches = generatedInterfaceNames
            .Where(name => Regex.IsMatch(name, $"[.:]{Regex.Escape(unrecognisedName)}$"))
            .ToList();

        if (matches.Count != 1)
        {
            // Either there's no match or an ambiguous match - we can't safely infer the interface name.
            // This is very much a "best effort" approach - if there are two interfaces with the same name,
            // there's no obvious way to work out which one the symbol is referring to.
            return unrecognisedName;
        }

        return matches[0];
    }
}
