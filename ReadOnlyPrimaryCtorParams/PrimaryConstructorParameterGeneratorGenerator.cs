using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace ReadOnlyPrimaryCtorParams;

[Generator]
public class FixPrimaryConstructorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext initContext)
    {
        initContext.RegisterPostInitializationOutput(igpic =>
        {
            igpic.AddSource(
                "ReadonlyAttribute.g.cs", """
                namespace ReadOnlyPrimaryCtorParams;

                [AttributeUsage(AttributeTargets.Parameter)]
                public class ReadOnlyAttribute : Attribute { }

                """);
        });

        var outputProvider = initContext.SyntaxProvider.ForAttributeWithMetadataName(
            "ReadOnlyPrimaryCtorParams.ReadOnlyAttribute",
            predicate: static (syntaxNode, ct) => IsPrimaryCtorParam(syntaxNode, ct),
            transform: static (context, ct) =>
            {
                if (context.TargetSymbol is not IParameterSymbol parameterSymbol)
                {
                    return null;
                }

                var topLevelTypeModel = TopLevelTypeModel.Create(context.TargetSymbol.ContainingType, ct);
                var parameterTypeName = parameterSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var parameterName = parameterSymbol.Name;

                var result = new ReadonlyGeneratorDataModel(
                   TopLevelTypeModel: topLevelTypeModel,
                   ParameterTypeName: parameterTypeName,
                   ParameterName: parameterName);

                return result;
            })
            .Where(static t => t is not null)
            .Select(static (t, ct) =>
            {
                var model = t!;

                var source = new StringBuilder();
                if (!string.IsNullOrEmpty(model.TopLevelTypeModel.Namespace))
                {
                    source.AppendLine(
                        $$"""
                        namespace {{model.TopLevelTypeModel.Namespace}}
                        {
                        """);
                }

                AppendSource(source, model, model.TopLevelTypeModel.Type);
                if (!string.IsNullOrEmpty(model.TopLevelTypeModel.Namespace))
                {
                    source.AppendLine("}\n");
                }
                return source.ToString();
            })
            .Collect()
            .Select(static (sources, ct) =>
            {
                ct.ThrowIfCancellationRequested();

                return string.Join("\n", sources);
            });

        initContext.RegisterSourceOutput(outputProvider, static (spc, source) =>
        {
            spc.AddSource("ReadOnlyPrimaryCtorParams.g.cs", source);
        });
    }

    internal static void AppendSource(StringBuilder source, ReadonlyGeneratorDataModel model, TypeModel typeModel)
    {
        var typeDeclarationName = (typeModel.TypeKind, typeModel.IsRecord) switch
        {
            (TypeKind.Class, true) => "record",
            (TypeKind.Struct, true) => "record struct",
            (TypeKind.Struct, false) => "struct",
            _ => "class",
        };
        source.AppendLine($"partial {typeDeclarationName} {typeModel.ParameterizedName}");
        source.AppendLine("{");
        if (typeModel.InnerType is { } innerType)
        {
            AppendSource(source, model, innerType);
        }
        else
        {
            source.AppendLine($$"""
                /// <summary>
                /// The parameter {{model.ParameterName}} is <b><em>now</em></b> readonly.
                /// </summary>"
                private readonly {{model.ParameterTypeName}} {{model.ParameterName}} = {{model.ParameterName}};
                """);
        }
        source.AppendLine("}");
    }

    public static bool IsPrimaryCtorParam(SyntaxNode syntaxNode, CancellationToken ct)
        => syntaxNode is ParameterSyntax
        {
            Parent: ParameterListSyntax
            {
                Parent: TypeDeclarationSyntax
            }
        };
}

public static class DisplayFormatters
{
    public static SymbolDisplayFormat FullyQualifiedNoGlobalFormat { get; } =
        new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions:
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static SymbolDisplayFormat TypeSignatureDisplayFormat { get; } =
        new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    public static SymbolDisplayFormat HintNameFormat { get; } =
        new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.None);
}

internal record ReadonlyGeneratorDataModel(
    TopLevelTypeModel TopLevelTypeModel,
    string ParameterTypeName,
    string ParameterName);

public record TopLevelTypeModel(
    string Namespace,
    TypeModel Type)
{
    public static TopLevelTypeModel Create(INamedTypeSymbol namedTypeSymbol, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var @namespace = namedTypeSymbol.ContainingNamespace.ToDisplayString(DisplayFormatters.FullyQualifiedNoGlobalFormat);

        var topLevelType = TypeModel.Create(namedTypeSymbol, ct);
        return new TopLevelTypeModel(@namespace, topLevelType);
    }
}

public record TypeModel(
    TypeKind TypeKind,
    bool IsRecord,
    string ParameterizedName,
    TypeModel? InnerType)
{
    public static TypeModel Create(INamedTypeSymbol typeSymbol, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var parentTypeSymbols = new Stack<INamedTypeSymbol>();
        var currentTypeSymbol = typeSymbol;
        do
        {
            parentTypeSymbols.Push(currentTypeSymbol);
            currentTypeSymbol = currentTypeSymbol.ContainingType;
        }
        while (currentTypeSymbol is not null);

        return CreateTypeModel(parentTypeSymbols, parentTypeSymbols.Pop(), ct);
    }

    private static TypeModel CreateTypeModel(Stack<INamedTypeSymbol> parentTypeSymbols, INamedTypeSymbol parentTypeSymbol, CancellationToken ct)
    {
        var typeKind = parentTypeSymbol.TypeKind;
        var isRecord = parentTypeSymbol.IsRecord;
        var parameterizedName = parentTypeSymbol.ToDisplayString(DisplayFormatters.TypeSignatureDisplayFormat);

        var innerType = TryCreate(parentTypeSymbols, ct, out var i)
            ? i
            : null;

        return new TypeModel(
            typeKind,
            isRecord,
            parameterizedName,
            innerType);
    }

    public static bool TryCreate(Stack<INamedTypeSymbol> parentTypeSymbols, CancellationToken ct, [NotNullWhen(true)] out TypeModel? typeModel)
    {
        ct.ThrowIfCancellationRequested();

        if (parentTypeSymbols.Count == 0)
        {
            typeModel = null;
            return false;
        }

        var parentTypeSymbol = parentTypeSymbols.Pop();
        typeModel = CreateTypeModel(parentTypeSymbols, parentTypeSymbol, ct);

        return true;
    }
}
