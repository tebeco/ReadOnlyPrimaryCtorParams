using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ReadOnlyPrimaryCtorParams;

[Generator]
public class FixPrimaryConstructorGenerator : IIncrementalGenerator
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

        var x = initContext.SyntaxProvider.ForAttributeWithMetadataName(
            "ReadOnlyPrimaryCtorParams.ReadOnlyAttribute",
            predicate: static (syntaxNode, ct) => IsPrimatyCtorParam(syntaxNode, ct),
            transform: static (context, ct) =>
            {
                if (context.TargetSymbol is not IParameterSymbol parameterSymbol)
                {
                    return null;
                }

                var containingTypeMetadataName = context.TargetSymbol.ContainingType.MetadataName;
                var containingNamespace = context.TargetSymbol.ContainingNamespace.ToDisplayString(FullyQualifiedNoGlobalFormat);
                var containingTypeDeclarationTypeKind = context.TargetSymbol.ContainingType.TypeKind;
                var containingTypeDeclarationIsRecord = context.TargetSymbol.ContainingType.IsRecord;
                var containingTypeName = context.TargetSymbol.ContainingType.ToDisplayString(TypeSignatureDisplayFormat);
                var parameterTypeName = parameterSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var parameterName = parameterSymbol.Name;

                var result = new ReadonlyGeneratorDataModel(
                    ContainingTypeMetadataName: containingTypeMetadataName,
                    ContainingNamespace: containingNamespace,
                    ContainingTypeDeclarationTypeKind: containingTypeDeclarationTypeKind,
                    ContainingTypeDeclarationIsRecord: containingTypeDeclarationIsRecord,
                    ContainingTypeName: containingTypeName,
                    ParameterTypeName: parameterTypeName,
                    ParameterName: parameterName);

                return result;
            })
            .Where(static t => t is not null)
            .Select(static (t, ct) =>
            {
                var model = t!;

                var hintName = $"{model.ContainingTypeMetadataName}.g.cs";

                var containingTypeDeclarationName = (model.ContainingTypeDeclarationTypeKind, model.ContainingTypeDeclarationIsRecord) switch
                {
                    (TypeKind.Class, true) => "record",
                    (TypeKind.Struct, true) => "record struct",
                    (TypeKind.Struct, false) => "struct",
                    _ => "class",
                };

                return (
                        hintName,
                        $$"""
                        namespace {{model.ContainingNamespace}};

                        partial {{containingTypeDeclarationName}} {{model.ContainingTypeName}}
                        {
                            /// <summary>
                            /// The parameter {{model.ParameterName}} is <b><em>now</em></b> readonly.
                            /// </summary>
                            private readonly {{model.ParameterTypeName}} {{model.ParameterName}} = {{model.ParameterName}};
                        }
                        """);
            })
            ;

        initContext.RegisterSourceOutput(x, static (spc, source) =>
        {
            var (hintName, sourceText) = source;
            spc.AddSource(hintName, sourceText);
        });
    }

    public static bool IsPrimatyCtorParam(SyntaxNode syntaxNode, CancellationToken _)
    {
        return syntaxNode is ParameterSyntax
        {
            Parent: ParameterListSyntax
            {
                Parent: (ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax)
                    and { Parent: not TypeDeclarationSyntax }
            }
        };
    }
}

internal record ReadonlyGeneratorDataModel(
    string ContainingTypeMetadataName,
    string ContainingNamespace,
    TypeKind ContainingTypeDeclarationTypeKind,
    bool ContainingTypeDeclarationIsRecord,
    string ContainingTypeName,
    string ParameterTypeName,
    string ParameterName);
