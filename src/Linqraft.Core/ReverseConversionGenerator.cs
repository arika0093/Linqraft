using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Linqraft.Core.Formatting;
using Linqraft.Core.RoslynHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core;

public record ReverseConverterRequest
{
    public required INamedTypeSymbol ConverterSymbol { get; init; }
    public required ITypeSymbol DtoType { get; init; }
    public required bool IsStatic { get; init; }
}

public record GeneratedReverseConverter
{
    public required string HintName { get; init; }
    public required string SourceCode { get; init; }
}

public static class ReverseConversionGenerator
{
    public static IEnumerable<GeneratedReverseConverter> GenerateReverseConverters(
        IEnumerable<ReverseConverterRequest> requests,
        IEnumerable<GenerateDtoClassInfo> dtoInfos
    )
    {
        var dtoLookup = BuildDtoLookup(dtoInfos);
        foreach (var request in requests)
        {
            if (!TryFindDtoInfo(dtoLookup, request.DtoType, out var dtoInfo))
            {
                continue;
            }

            var builder = new ReverseConversionBuilder(request, dtoInfo, dtoLookup);
            yield return new GeneratedReverseConverter
            {
                HintName = request.ConverterSymbol.Name,
                SourceCode = builder.Build(),
            };
        }
    }

    private static Dictionary<string, GenerateDtoClassInfo> BuildDtoLookup(
        IEnumerable<GenerateDtoClassInfo> dtoInfos
    )
        {
            var dict = new Dictionary<string, GenerateDtoClassInfo>();
            foreach (var dto in dtoInfos)
            {
                var key = NormalizeFullName(dto.FullName);
                if (!dict.ContainsKey(key))
                {
                    dict[key] = dto;
                }
            }
            return dict;
        }

    private static bool TryFindDtoInfo(
        Dictionary<string, GenerateDtoClassInfo> dtoLookup,
        ITypeSymbol dtoType,
        out GenerateDtoClassInfo dtoInfo
    )
    {
        var dtoName = NormalizeFullName(
            dtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        );
        if (dtoLookup.TryGetValue(dtoName, out dtoInfo!))
        {
            return true;
        }

        var simpleName = dtoType.Name;
        var fallback = dtoLookup.FirstOrDefault(kvp =>
            kvp.Key.EndsWith($".{simpleName}", StringComparison.Ordinal)
            || kvp.Key.EndsWith($"::{simpleName}", StringComparison.Ordinal)
        );
        if (!string.IsNullOrEmpty(fallback.Key))
        {
            dtoInfo = fallback.Value;
            return true;
        }

        dtoInfo = default!;
        return false;
    }

    private static string NormalizeFullName(string name)
    {
        return name.StartsWith("global::", StringComparison.Ordinal) ? name : $"global::{name}";
    }

    private sealed class ReverseConversionBuilder
    {
        private readonly ReverseConverterRequest request;
        private readonly GenerateDtoClassInfo rootDto;
        private readonly Dictionary<string, GenerateDtoClassInfo> dtoLookup;
        private readonly Dictionary<string, string> mapMethods = [];
        private readonly List<string> methodBodies = [];

        public ReverseConversionBuilder(
            ReverseConverterRequest request,
            GenerateDtoClassInfo rootDto,
            Dictionary<string, GenerateDtoClassInfo> dtoLookup
        )
        {
            this.request = request;
            this.rootDto = rootDto;
            this.dtoLookup = dtoLookup;
        }

        public string Build()
        {
            var rootStructure = rootDto.Structure;
            var sourceTypeName = rootStructure.SourceTypeFullName;
            var dtoTypeName = request.DtoType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );
            var @namespace = request.ConverterSymbol.ContainingNamespace?.ToDisplayString() ?? "";
            var accessibility = GetAccessibilityString(request.ConverterSymbol);
            var staticClassKeyword = request.ConverterSymbol.IsStatic ? " static" : "";
            var className = request.ConverterSymbol.Name;
            var methodStaticModifier = request.IsStatic ? "static " : "";
            var indent1 = CodeFormatter.Indent(1);
            var indent2 = CodeFormatter.Indent(2);
            var indent3 = CodeFormatter.Indent(3);

            var mapMethodName = EnsureMapMethod(rootStructure, dtoTypeName);
            var classBody = new StringBuilder();
            classBody.AppendLine(
                $"{indent1}public {methodStaticModifier}{sourceTypeName} FromDto({dtoTypeName} dto)"
            );
            classBody.AppendLine($"{indent1}{{");
            classBody.AppendLine($"{indent2}if (dto is null) return default!;");
            classBody.AppendLine($"{indent2}return {mapMethodName}(dto);");
            classBody.AppendLine($"{indent1}}}");
            classBody.AppendLine();
            classBody.AppendLine(
                $"{indent1}public {methodStaticModifier}IEnumerable<{sourceTypeName}> FromDtoProjection(IEnumerable<{dtoTypeName}> source)"
            );
            classBody.AppendLine($"{indent1}{{");
            classBody.AppendLine($"{indent2}if (source is null) yield break;");
            classBody.AppendLine($"{indent2}foreach (var item in source)");
            classBody.AppendLine($"{indent2}{{");
            classBody.AppendLine($"{indent3}yield return {mapMethodName}(item);");
            classBody.AppendLine($"{indent2}}}");
            classBody.AppendLine($"{indent1}}}");
            classBody.AppendLine();
            foreach (var method in methodBodies.Distinct())
            {
                classBody.AppendLine(method);
            }

            var classContent = $$"""
            {{indent1}}{{accessibility}}{{staticClassKeyword}} partial class {{className}}
            {{indent1}}{
            {{classBody}}
            {{indent1}}}
            """;

            var bodyWithNamespace = string.IsNullOrEmpty(@namespace)
                ? classContent
                : $$"""
                namespace {{@namespace}}
                {
                {{classContent}}
                }
                """;

            return $$"""
            // <auto-generated>
            // This file is auto-generated by Linqraft.
            // </auto-generated>
            #nullable enable
            #pragma warning disable IDE0060
            #pragma warning disable CS8601
            #pragma warning disable CS8602
            #pragma warning disable CS8603
            #pragma warning disable CS8604
            #pragma warning disable CS8618
            using System;
            using System.Linq;
            using System.Collections.Generic;

            {{bodyWithNamespace}}
            """;
        }

        private string EnsureMapMethod(DtoStructure structure, string dtoTypeName)
        {
            var structureId = structure.GetUniqueId();
            if (mapMethods.TryGetValue(structureId, out var existing))
            {
                return existing;
            }

            var methodName = $"Map_{structureId}";
            mapMethods[structureId] = methodName;

            var sourceTypeName = structure.SourceTypeFullName;
            var indent1 = CodeFormatter.Indent(1);
            var indent2 = CodeFormatter.Indent(2);
            var indent3 = CodeFormatter.Indent(3);

            var statements = GenerateAssignments(structure);
            var assignmentLines = statements.Count == 0
                ? ""
                : CodeFormatter.IndentCode(
                    string.Join(CodeFormatter.DefaultNewLine, statements),
                    CodeFormatter.IndentSize * 2
                ) + CodeFormatter.DefaultNewLine;

            var method = new StringBuilder();
            method.AppendLine($"{indent1}private static {sourceTypeName} {methodName}({dtoTypeName} dto)");
            method.AppendLine($"{indent1}{{");
            method.AppendLine($"{indent2}if (dto is null) return default!;");
            method.AppendLine($"{indent2}var entity = new {sourceTypeName}();");
            method.Append(assignmentLines);
            method.AppendLine($"{indent2}return entity;");
            method.AppendLine($"{indent1}}}");

            methodBodies.Add(method.ToString());
            return methodName;
        }

        private List<string> GenerateAssignments(DtoStructure structure)
        {
            var assignments = new List<string>();
            foreach (var property in structure.Properties)
            {
                var targetPaths = TryGetMemberPath(property.OriginalSyntax, out var path)
                    ? path
                    : TryGetMemberByName(structure.SourceType, property.Name);
                if (targetPaths is null || targetPaths.Count == 0)
                {
                    continue;
                }

                var currentType = structure.SourceType;
                var currentExpr = "entity";
                var sb = new List<string>();
                var skip = false;

                for (int i = 0; i < targetPaths.Count - 1; i++)
                {
                    var memberName = targetPaths[i];
                    var memberSymbol = FindMemberSymbol(currentType, memberName);
                    if (memberSymbol is null)
                    {
                        skip = true;
                        break;
                    }

                    var memberType = memberSymbol switch
                    {
                        IPropertySymbol propSym => propSym.Type,
                        IFieldSymbol fieldSym => fieldSym.Type,
                        _ => null
                    };

                    if (memberType is null || !CanInstantiate(memberType))
                    {
                        skip = true;
                        break;
                    }

                    var targetTypeName = memberType.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    );
                    targetTypeName = targetTypeName.EndsWith("?")
                        ? targetTypeName[..^1]
                        : targetTypeName;

                    sb.Add($"{currentExpr}.{memberName} ??= new {targetTypeName}();");
                    currentExpr = $"{currentExpr}.{memberName}";
                    currentType = memberType;
                }

                if (skip)
                {
                    continue;
                }

                var lastMember = targetPaths[^1];
                var lastSymbol = FindMemberSymbol(currentType, lastMember);
                if (lastSymbol is IPropertySymbol prop && prop.SetMethod is null)
                {
                    continue;
                }
                if (lastSymbol is null)
                {
                    continue;
                }

                var assignmentExpr = BuildAssignmentExpression(property, currentExpr);
                if (assignmentExpr is null)
                {
                    continue;
                }

                sb.Add($"{currentExpr}.{lastMember} = {assignmentExpr};");
                assignments.AddRange(sb);
            }

            return assignments;
        }

        private List<string>? TryGetMemberByName(ITypeSymbol typeSymbol, string propertyName)
        {
            var member = FindMemberSymbol(typeSymbol, propertyName);
            if (member is null)
                return null;
            return new List<string> { propertyName };
        }

        private string? BuildAssignmentExpression(DtoProperty property, string dtoPrefix)
        {
            var dtoAccess = $"dto.{property.Name}";
            if (property.NestedStructure is null)
            {
                return dtoAccess;
            }

            var nestedDtoTypeName =
                GetDtoFullName(property.NestedStructure) ?? GetNestedDtoTypeName(property);
            var nestedSourceType = property.NestedStructure.SourceType;
            var mapMethod = EnsureMapMethod(
                property.NestedStructure,
                nestedDtoTypeName ?? property.TypeName
            );

            var memberType = RoslynTypeHelper.GetNonNullableType(property.TypeSymbol);
            if (memberType is null)
            {
                return null;
            }
            if (memberType is IArrayTypeSymbol arrayType)
            {
                var elementType = property.NestedStructure.SourceType.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                );
                return $"{dtoAccess}?.Select({mapMethod}).ToArray() ?? global::System.Array.Empty<{elementType}>()";
            }

            var element = RoslynTypeHelper.GetGenericTypeArgument(memberType, 0);
            if (element is not null && IsEnumerableType(memberType))
            {
                var targetElementTypeName = property.NestedStructure.SourceType.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                );
                var selector = $"{dtoAccess}?.Select({mapMethod})";
                if (IsListType(memberType))
                {
                    return $"{selector}?.ToList() ?? new global::System.Collections.Generic.List<{targetElementTypeName}>()";
                }
                return $"{selector} ?? global::System.Linq.Enumerable.Empty<{targetElementTypeName}>()";
            }

            // Single nested object
            return $"{mapMethod}({dtoAccess})";
        }

        private static string? GetNestedDtoTypeName(DtoProperty property)
        {
            var memberType = RoslynTypeHelper.GetNonNullableType(property.TypeSymbol);
            if (memberType is null)
                return null;
            if (memberType is IArrayTypeSymbol arrayType)
            {
                return arrayType.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            var element = RoslynTypeHelper.GetGenericTypeArgument(memberType, 0);
            if (element is not null)
            {
                return element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            return property.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private string? GetDtoFullName(DtoStructure structure)
        {
            var id = structure.GetUniqueId();
            var dto = dtoLookup.Values.FirstOrDefault(v => v.Structure.GetUniqueId() == id);
            return dto?.FullName.StartsWith("global::", StringComparison.Ordinal) == true
                ? dto.FullName
                : dto is not null
                    ? $"global::{dto.FullName}"
                    : null;
        }

        private static ISymbol? FindMemberSymbol(ITypeSymbol typeSymbol, string name)
        {
            return typeSymbol
                .GetMembers(name)
                .FirstOrDefault(m => m is IPropertySymbol || m is IFieldSymbol);
        }

        private static bool TryGetMemberPath(ExpressionSyntax syntax, out List<string> path)
        {
            path = new List<string>();

            if (syntax is IdentifierNameSyntax identifier)
            {
                path.Add(identifier.Identifier.Text);
                return true;
            }

            if (syntax is MemberAccessExpressionSyntax memberAccess)
            {
                var current = memberAccess;
                while (true)
                {
                    path.Insert(0, current.Name.Identifier.Text);
                    if (current.Expression is MemberAccessExpressionSyntax innerAccess)
                    {
                        current = innerAccess;
                        continue;
                    }

                    if (current.Expression is IdentifierNameSyntax)
                    {
                        return path.Count > 0;
                    }

                    return false;
                }
            }

            if (syntax is InvocationExpressionSyntax)
            {
                return false;
            }

            return false;
        }

        private static bool CanInstantiate(ITypeSymbol typeSymbol)
        {
            var nonNullable = RoslynTypeHelper.GetNonNullableType(typeSymbol) ?? typeSymbol;
            if (nonNullable is not INamedTypeSymbol namedType)
                return false;
            if (!nonNullable.IsReferenceType)
                return false;
            if (namedType.IsAbstract)
                return false;
            if (namedType.SpecialType == SpecialType.System_String)
                return false;

            return namedType.InstanceConstructors.Any(ctor =>
                ctor.Parameters.Length == 0 && ctor.DeclaredAccessibility is not Accessibility.Private
            );
        }

        private static string GetAccessibilityString(INamedTypeSymbol typeSymbol)
        {
            return typeSymbol.DeclaredAccessibility switch
            {
                Accessibility.Public => "public",
                Accessibility.Internal => "internal",
                Accessibility.Private => "private",
                Accessibility.Protected => "protected",
                Accessibility.ProtectedAndInternal => "private protected",
                Accessibility.ProtectedOrInternal => "protected internal",
                _ => "public",
            };
        }

        private static bool IsListType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol is not INamedTypeSymbol namedType)
                return false;

            var nonNullableType = RoslynTypeHelper.GetNonNullableType(namedType) ?? namedType;
            if (nonNullableType is not INamedTypeSymbol nonNullableNamedType)
                return false;

            var typeName = nonNullableNamedType.Name;
            var containingNamespace = nonNullableNamedType.ContainingNamespace?.ToDisplayString();
            return typeName == "List" && containingNamespace == "System.Collections.Generic";
        }

        private static bool IsEnumerableType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol is IArrayTypeSymbol)
                return true;
            if (typeSymbol is null)
                return false;
            return typeSymbol.AllInterfaces.Any(i =>
                i.SpecialType == SpecialType.System_Collections_IEnumerable
                || (i is INamedTypeSymbol ins && ins.Name == "IEnumerable")
            );
        }
    }
}
