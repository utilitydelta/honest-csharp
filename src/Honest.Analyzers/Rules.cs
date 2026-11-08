using Microsoft.CodeAnalysis;

namespace UtilityDelta.Honest.Analyzers;

internal static class Rules
{
    private const string Category = "Honest";
    private const string Help = "https://github.com/utilitydelta/honest-csharp#fix-by-diagnostic-id";

    public static readonly DiagnosticDescriptor NoProperties = new(
        id: "HON001",
        title: "Properties are banned",
        messageFormat: "{0}; a property hides whether member access runs code",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: Help,
        description: "Public fields only. A field cannot hide cost or control flow behind field-access syntax. A computation is a method, with parens.");

    public static readonly DiagnosticDescriptor NoThrow = new(
        id: "HON002",
        title: "Thrown exceptions are banned",
        messageFormat: "Replace {0} with a Result<T,E> return for expected failures{1}, or Panic.Now(...) for an unrecoverable invariant. Where a framework contract requires the throw (JsonConverter.Read), suppress that one line with the reason.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: Help,
        description: "Errors are values. Expected failures return Result; unrecoverable ones call Panic, which aborts via Environment.FailFast. Throw helpers such as ArgumentNullException.ThrowIfNull and ExceptionDispatchInfo.Throw are throws too.");

    public static readonly DiagnosticDescriptor NoImplicitOperator = new(
        id: "HON003",
        title: "Implicit conversion operators are banned",
        messageFormat: "Make this conversion 'explicit'; an implicit operator is a hidden call on what looks like an assignment",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: Help,
        description: "Conversions must be visible at the call site.");

    public static readonly DiagnosticDescriptor NoDynamic = new(
        id: "HON004",
        title: "The dynamic type is banned",
        messageFormat: "Replace 'dynamic' with a static type; dynamic moves type errors to runtime",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: Help,
        description: "Keep the type system. No runtime dispatch.");
}
