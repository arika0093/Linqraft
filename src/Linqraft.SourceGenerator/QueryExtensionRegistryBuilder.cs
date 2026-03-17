using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Linqraft.Core.Collections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.SourceGenerator;

internal static class QueryExtensionRegistryBuilder
{
    private const string AttributeMetadataName = "Linqraft.LinqraftExtensionsAttribute";
    private const string DeclarationBaseTypeName = "LinqraftExtensionDeclaration";

    public static QueryExtensionRegistryModel Build(
        Compilation compilation,
        CancellationToken cancellationToken
    )
    {
        var registrations = new Dictionary<string, QueryExtensionRegistrationModel>(
            StringComparer.Ordinal
        );

        foreach (
            var type in EnumerateAllTypes(compilation.Assembly.GlobalNamespace).Concat(
                compilation.SourceModule.ReferencedAssemblySymbols
                    .Where(assembly =>
                        string.Equals(
                            assembly.Name,
                            "Linqraft.QueryExtensions",
                            StringComparison.Ordinal
                        )
                    )
                    .SelectMany(assembly => EnumerateAllTypes(assembly.GlobalNamespace))
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryCreateRegistration(type, compilation, cancellationToken, out var registration))
            {
                continue;
            }

            registrations[
                $"{registration.Namespace}|{registration.ExtensionClassName}|{registration.MethodName}"
            ] = registration;
        }

        var orderedRegistrations = registrations
            .Values.OrderBy(registration => registration.Namespace, StringComparer.Ordinal)
            .ThenBy(registration => registration.ExtensionClassName, StringComparer.Ordinal)
            .ThenBy(registration => registration.MethodName, StringComparer.Ordinal)
            .ToArray();

        return new QueryExtensionRegistryModel
        {
            Registrations = orderedRegistrations,
            LeftJoinMethodNames = orderedRegistrations
                .Where(registration =>
                    string.Equals(registration.BehaviorKey, "AsLeftJoin", StringComparison.Ordinal)
                )
                .Select(registration => registration.MethodName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray(),
        };
    }

    private static bool TryCreateRegistration(
        INamedTypeSymbol type,
        Compilation compilation,
        CancellationToken cancellationToken,
        out QueryExtensionRegistrationModel registration
    )
    {
        registration = null!;

        if (!InheritsFromDeclarationBase(type))
        {
            return false;
        }

        var attribute = type.GetAttributes()
            .FirstOrDefault(data =>
                string.Equals(
                    data.AttributeClass?.ToDisplayString(),
                    AttributeMetadataName,
                    StringComparison.Ordinal
                )
            );
        if (attribute?.ConstructorArguments.FirstOrDefault().Value is not string methodName)
        {
            return false;
        }

        if (
            TryReadStringProperty(type, "Namespace", compilation, cancellationToken, out var @namespace)
            && TryReadStringProperty(
                type,
                "ExtensionClassName",
                compilation,
                cancellationToken,
                out var className
            )
            && TryReadStringProperty(
                type,
                "BehaviorKey",
                compilation,
                cancellationToken,
                out var behaviorKey
            )
            && TryReadStringProperty(
                type,
                "MethodDeclarations",
                compilation,
                cancellationToken,
                out var methodDeclarations
            )
            && TryParseMethodDeclarations(methodDeclarations, out var methods)
        )
        {
            registration = new QueryExtensionRegistrationModel
            {
                MethodName = methodName,
                Namespace = @namespace,
                ExtensionClassName = className,
                BehaviorKey = behaviorKey,
                Methods = methods,
            };
            return true;
        }

        return false;
    }

    private static bool InheritsFromDeclarationBase(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (
                string.Equals(
                    current.ToDisplayString(),
                    "Linqraft.LinqraftExtensionDeclaration",
                    StringComparison.Ordinal
                )
                || string.Equals(current.Name, DeclarationBaseTypeName, StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadStringProperty(
        INamedTypeSymbol type,
        string propertyName,
        Compilation compilation,
        CancellationToken cancellationToken,
        out string value
    )
    {
        if (TryReadStringPropertyFromSource(type, propertyName, compilation, cancellationToken, out value))
        {
            return true;
        }

        return TryReadStringConstantField(type, $"{propertyName}Value", out value);
    }

    private static bool TryReadStringPropertyFromSource(
        INamedTypeSymbol type,
        string propertyName,
        Compilation compilation,
        CancellationToken cancellationToken,
        out string value
    )
    {
        foreach (var property in type.GetMembers(propertyName).OfType<IPropertySymbol>())
        {
            foreach (var syntaxReference in property.DeclaringSyntaxReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (syntaxReference.GetSyntax(cancellationToken) is not PropertyDeclarationSyntax syntax)
                {
                    continue;
                }

                var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
                var expression =
                    syntax.ExpressionBody?.Expression
                    ?? syntax
                        .AccessorList?.Accessors.FirstOrDefault(accessor =>
                            accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GetAccessorDeclaration)
                        )
                        ?.ExpressionBody?.Expression
                    ?? syntax
                        .AccessorList?.Accessors.FirstOrDefault(accessor =>
                            accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GetAccessorDeclaration)
                        )
                        ?.Body?.Statements.OfType<ReturnStatementSyntax>()
                        .FirstOrDefault()
                        ?.Expression;
                if (expression is null)
                {
                    continue;
                }

                var constant = semanticModel.GetConstantValue(expression, cancellationToken);
                if (constant.HasValue && constant.Value is string text)
                {
                    value = text;
                    return true;
                }
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool TryReadStringConstantField(
        INamedTypeSymbol type,
        string fieldName,
        out string value
    )
    {
        foreach (var field in type.GetMembers(fieldName).OfType<IFieldSymbol>())
        {
            if (field.HasConstantValue && field.ConstantValue is string text)
            {
                value = text;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool TryParseMethodDeclarations(
        string declarations,
        out EquatableArray<QueryExtensionMethodDeclarationModel> methods
    )
    {
        var parsed = new List<QueryExtensionMethodDeclarationModel>();
        var blocks = declarations.Replace("\r\n", "\n").Split(["\n\n"], StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            var lines = block
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line))
                .ToArray();
            var summary = lines
                .FirstOrDefault(line => line.StartsWith("summary:", StringComparison.Ordinal))
                ?.Substring("summary:".Length)
                .Trim()
                ?? string.Empty;
            var signature = lines
                .FirstOrDefault(line => line.StartsWith("signature:", StringComparison.Ordinal))
                ?.Substring("signature:".Length)
                .Trim()
                ?? string.Empty;
            if (string.IsNullOrWhiteSpace(summary) || string.IsNullOrWhiteSpace(signature))
            {
                methods = Array.Empty<QueryExtensionMethodDeclarationModel>();
                return false;
            }

            parsed.Add(
                new QueryExtensionMethodDeclarationModel
                {
                    Summary = summary,
                    Signature = signature,
                }
            );
        }

        methods = parsed.ToArray();
        return parsed.Count > 0;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateAllTypes(INamespaceSymbol @namespace)
    {
        foreach (var member in @namespace.GetTypeMembers())
        {
            yield return member;
            foreach (var nested in EnumerateNestedTypes(member))
            {
                yield return nested;
            }
        }

        foreach (var childNamespace in @namespace.GetNamespaceMembers())
        {
            foreach (var member in EnumerateAllTypes(childNamespace))
            {
                yield return member;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var child in EnumerateNestedTypes(nested))
            {
                yield return child;
            }
        }
    }
}
