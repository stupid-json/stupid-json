using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace StupidJson
{
    public static class EnumerableExt
    {
        public static IEnumerable<TR> Choose<T, TR>(this IEnumerable<T> source, Func<T, TR> selector)
        {
            return source.Select(selector).Where(it => it != null);
        }

        public static IEnumerable<T> Choose<T>(this IEnumerable<T> source)
        {
            return source.Where(it => it != null);
        }
    }

    public abstract class JsonCompatibility
    {
        public JsonCompatibility(ITypeSymbol typeSymbol)
        {
            TypeSymbol = typeSymbol;
        }

        public ITypeSymbol TypeSymbol { get; }

        public abstract bool IsJsonCompatible { get; }
    }

    public sealed class JsonCompatible : JsonCompatibility
    {
        public JsonCompatible(ITypeSymbol typeSymbol) : base(typeSymbol) { }

        public override bool IsJsonCompatible => true;
    }

    public sealed class JsonIncompatible : JsonCompatibility
    {
        public JsonIncompatible(ITypeSymbol typeSymbol, string explanation) : base(typeSymbol)
        {
            Explanation = explanation;
        }

        public override bool IsJsonCompatible => false;

        public string Explanation { get; }
    }

    public abstract class Incompatibility
    {
        public Incompatibility(ISymbol symbol)
        {
            Symbol = symbol;
        }

        public ISymbol Symbol { get; }
    }

    public class CtorIncompatibility : Incompatibility
    {
        public CtorIncompatibility(IMethodSymbol ctorSymbol)
            : base(ctorSymbol) { }
    }

    public class PropIncompatibility : Incompatibility
    {
        public PropIncompatibility(IPropertySymbol symbol, string explanation)
            : base(symbol)
        {
            Explanation = explanation;
        }

        public string Explanation { get; }
    }


    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class StupidJsonAnalyzer : DiagnosticAnalyzer
    {
        private const string Category = "Design";

        public const string CtorDiagnosticId = "StupidJsonCtor";
        private static readonly LocalizableString CtorAnalyzerTitle = new LocalizableResourceString(nameof(Resources.CtorAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString CtorAnalyzerMessageFormat = new LocalizableResourceString(nameof(Resources.CtorAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString CtorAnalyzerDescription = new LocalizableResourceString(nameof(Resources.CtorAnalyzerDescription), Resources.ResourceManager, typeof(Resources));

        public const string PropDiagnosticId = "StupidJsonProp";
        private static readonly LocalizableString PropAnalyzerTitle = new LocalizableResourceString(nameof(Resources.PropAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString PropAnalyzerMessageFormat = new LocalizableResourceString(nameof(Resources.PropAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString PropAnalyzerDescription = new LocalizableResourceString(nameof(Resources.PropAnalyzerDescription), Resources.ResourceManager, typeof(Resources));

        public const string DebugDiagnosticId = "StupidJsonDebug";
        private static readonly LocalizableString DebugAnalyzerTitle = new LocalizableResourceString(nameof(Resources.DebugAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString DebugAnalyzerMessageFormat = new LocalizableResourceString(nameof(Resources.DebugAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString DebugAnalyzerDescription = new LocalizableResourceString(nameof(Resources.DebugAnalyzerDescription), Resources.ResourceManager, typeof(Resources));

        public const string DeserializeDiagnosticId = "StupidJsonDeserializeObject";
        private static readonly LocalizableString DeserializeAnalyzerTitle = new LocalizableResourceString(nameof(Resources.DeserializeAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString DeserializeAnalyzerMessageFormat = new LocalizableResourceString(nameof(Resources.DeserializeAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString DeserializeAnalyzerDescription = new LocalizableResourceString(nameof(Resources.DeserializeAnalyzerDescription), Resources.ResourceManager, typeof(Resources));

        private static readonly DiagnosticDescriptor CtorRule = new DiagnosticDescriptor(CtorDiagnosticId, CtorAnalyzerTitle, CtorAnalyzerMessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: CtorAnalyzerDescription);
        private static readonly DiagnosticDescriptor PropRule = new DiagnosticDescriptor(PropDiagnosticId, PropAnalyzerTitle, PropAnalyzerMessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: PropAnalyzerDescription);
        private static readonly DiagnosticDescriptor DebugRule = new DiagnosticDescriptor(DebugDiagnosticId, DebugAnalyzerTitle, DebugAnalyzerMessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: DebugAnalyzerDescription);
        private static readonly DiagnosticDescriptor DeserializeRule = new DiagnosticDescriptor(DeserializeDiagnosticId, DeserializeAnalyzerTitle, DeserializeAnalyzerMessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: DeserializeAnalyzerDescription);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get
            {
                return ImmutableArray.Create(CtorRule, PropRule, DeserializeRule, DebugRule);
            }
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // TODO: Consider registering other actions that act on syntax instead of or in addition to symbols
            // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md for more information

            context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.NamedType);

            context.RegisterSyntaxNodeAction(AnalyzeInvocationExpression, SyntaxKind.InvocationExpression);
        }

        private void AnalyzeInvocationExpression(SyntaxNodeAnalysisContext context)
        {
            try
            {
                DoAnalyzeInvocationExpression(context);
            }
            catch (Exception e)
            {
                context.ReportDiagnostic(Diagnostic.Create(DebugRule, context.Node.GetLocation(), " >>> " + e.Message + " >>> " + e.StackTrace));
            }
        }

        private void DoAnalyzeInvocationExpression(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;

            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                if (memberAccess.Expression is IdentifierNameSyntax typeIdentifierSyntax)
                {
                    var textIdentifierText = typeIdentifierSyntax.Identifier.Text;
                    if (textIdentifierText == "JsonConvert")
                    {
                        if (memberAccess.Name is GenericNameSyntax genericName)
                        {
                            if (genericName.Identifier is SyntaxToken syntaxToken)
                            {
                                if (syntaxToken.Text == "DeserializeObject")
                                {
                                    AnalyzeDeserializeObjectCall(context, invocation, memberAccess, genericName);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void AnalyzeDeserializeObjectCall(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation, MemberAccessExpressionSyntax memberAccess, GenericNameSyntax genericName)
        {
            var typeArgs = genericName.TypeArgumentList.Arguments;
            if (typeArgs.Count == 1)
            {
                var typeArg = typeArgs[0];
                var symbolInfo = context.SemanticModel.GetSymbolInfo(typeArg);
                var typeSymbol = (INamedTypeSymbol)symbolInfo.Symbol;

                if (CheckJsonCompatibilityForType(typeSymbol) is JsonIncompatible)
                {
                    var incompatibilities = VerifyDeserializationType(typeSymbol);
                    if (incompatibilities.Any())
                    {
                        context.ReportDiagnostic(Diagnostic.Create(DeserializeRule, invocation.GetLocation(), typeSymbol.Name));
                    }
                }
                else if (HasStupidJsonAttribute(typeSymbol)) 
                {
                    var incompatibilities = VerifyStupidJsonType(typeSymbol);
                    if (incompatibilities.Any())
                    {
                        context.ReportDiagnostic(Diagnostic.Create(DeserializeRule, invocation.GetLocation(), typeSymbol.Name));
                    }
                }
            }
        }

        private static Diagnostic ToSymbolAnalysisDiagnostic(Incompatibility incompatibility)
        {
            var location = incompatibility.Symbol.Locations[0];

            if (incompatibility is PropIncompatibility propIncompatibility)
            {
                return Diagnostic.Create(PropRule, location, propIncompatibility.Symbol.Name, propIncompatibility.Explanation);
            }

            if (incompatibility is CtorIncompatibility)
            {
                return Diagnostic.Create(CtorRule, location, incompatibility.Symbol.ContainingSymbol.Name);
            }

            return Diagnostic.Create(DebugRule, location, "Unhandled incompatibility...");
        }

        private static void AnalyzeSymbol(SymbolAnalysisContext context)
        {
            try
            {
                var namedTypeSymbol = (INamedTypeSymbol)context.Symbol;

                if (HasStupidJsonAttribute(namedTypeSymbol))
                {
                    var incompatibilities = VerifyStupidJsonType(namedTypeSymbol);
                    var diagnostics = incompatibilities.Choose(it => ToSymbolAnalysisDiagnostic(it));
                    foreach (var d in diagnostics)
                    {
                        context.ReportDiagnostic(d);
                    }
                }
            }
            catch (Exception ex)
            {
                var diagnostic = Diagnostic.Create(DebugRule, context.Symbol.Locations[0], context.Symbol.Name, "Error: " + ex.Message);
                context.ReportDiagnostic(diagnostic);
            }
        }

        private static bool HasStupidJsonAttribute(ISymbol symbol)
        {
            return symbol.GetAttributes().Any(a => a.AttributeClass.Name == "StupidJsonAttribute");
        }

        private static bool IsConstructor(IMethodSymbol symbol)
        {
            return symbol.MethodKind == MethodKind.Constructor;
        }

        private static IEnumerable<Incompatibility> VerifyDeserializationType(INamedTypeSymbol namedTypeSymbol)
        {
            try
            {
                if (HasStupidJsonAttribute(namedTypeSymbol))
                {
                    // No need to check again.
                    return new Incompatibility[0];
                }
                else
                {
                    return VerifyStupidJsonType(namedTypeSymbol);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Something bad happened when verifying deserialization type.", ex);
            }
        }

        private static IEnumerable<Incompatibility> VerifyStupidJsonType(INamedTypeSymbol namedTypeSymbol)
        {
            var members = namedTypeSymbol.GetMembers();
            var methods = members.Where(it => it.Kind == SymbolKind.Method).Cast<IMethodSymbol>().ToList();

            var ctors = methods.Where(it => IsConstructor(it)).ToList();
            var ctorDiagnostics = ctors.Choose(it => VerifyStupidJsonConstructor(it));

            var properties = members.Where(it => it.Kind == SymbolKind.Property).Cast<IPropertySymbol>().ToList();
            var propDiagnostics = properties.Choose(it => VerifyStupidJsonProperty(it));

            return ctorDiagnostics.Concat(propDiagnostics);
        }

        private static readonly string[] JsonCompatibleClassTypes = new[] {
            "String", "Object"
        };

        private static readonly string[] JsonCompatibleStructTypes = new[] {
            "Boolean", "Int32", "Int64", "Single", "Double"
        };

        private static JsonCompatibility CheckJsonCompatibilityForType(ITypeSymbol typeSymbol)
        {
            switch (typeSymbol.TypeKind)
            {
                case TypeKind.Class:
                    return CheckJsonCompatibilityForClassType((INamedTypeSymbol)typeSymbol);
                case TypeKind.Struct:
                    return CheckJsonCompatibilityForStructType((INamedTypeSymbol)typeSymbol);
                case TypeKind.Array:
                    return CheckJsonCompatibilityForArrayType((IArrayTypeSymbol)typeSymbol);
                case TypeKind.Enum:
                    return new JsonIncompatible(typeSymbol, $"The type {typeSymbol.Name} is an enum, which is not a concept in JSON. Use a string or a number instead.");
                case TypeKind.Interface:
                    return new JsonIncompatible(typeSymbol, $"The type {typeSymbol.Name} is an interface, which is not a concept in JSON. Use a concrete type instead.");
                default:
                    return new JsonIncompatible(typeSymbol, $"The type {typeSymbol.Name} is of kind {typeSymbol.TypeKind}, which is not compatible with JSON.");
            }
        }

        private static JsonCompatibility CheckJsonCompatibilityForArrayType(IArrayTypeSymbol typeSymbol)
        {
            if (CheckJsonCompatibilityForType(typeSymbol.ElementType) is JsonIncompatible incompatible)
            {
                return new JsonIncompatible(typeSymbol, $"The Array has type {GetTypeName(typeSymbol)} which is not compatible with JSON. {incompatible.Explanation}");
            }
            else
            {
                return new JsonCompatible(typeSymbol);
            }
        }

        private static JsonCompatibility CheckJsonCompatibilityForClassType(ITypeSymbol typeSymbol)
        {
            if (JsonCompatibleClassTypes.Contains(typeSymbol.Name))
            {
                return new JsonCompatible(typeSymbol);
            }

            if (typeSymbol.Name == "Dictionary")
            {
                var namedTypeSymbol = (INamedTypeSymbol)typeSymbol;

                var typeArgs = namedTypeSymbol.TypeArguments;
                var keyType = typeArgs[0];
                var valType = typeArgs[1];
                if (keyType.Name != "String")
                {
                    return new JsonIncompatible(namedTypeSymbol, $"The Dictionary has keys of type {keyType.Name}, which is not compatible with JSON. Dictionaries must have string keys.");
                }

                if (CheckJsonCompatibilityForType(valType) is JsonIncompatible incompatible)
                {
                    return new JsonIncompatible(namedTypeSymbol, $"The Dictionary has values of type {valType.Name}, which is not compatible with JSON. {incompatible.Explanation}");
                }

                return new JsonCompatible(namedTypeSymbol);
            }

            if (typeSymbol.Name == "List")
            {
                var namedTypeSymbol = (INamedTypeSymbol)typeSymbol;

                var typeArgs = namedTypeSymbol.TypeArguments;
                var itemType = typeArgs[0];
                if (CheckJsonCompatibilityForType(itemType) is JsonIncompatible incompatible)
                {
                    return new JsonIncompatible(namedTypeSymbol, $"The List has values of type {itemType.Name}, which is not JSON compatible. {incompatible.Explanation}");
                }

                return new JsonCompatible(namedTypeSymbol);
            }

            if (HasStupidJsonAttribute(typeSymbol))
            {
                return new JsonCompatible(typeSymbol);
            }
            else
            {
                return new JsonIncompatible(typeSymbol, "There is no such type in JSON.");
            }
        }

        private static JsonCompatibility CheckJsonCompatibilityForStructType(INamedTypeSymbol typeSymbol)
        {
            if (JsonCompatibleStructTypes.Contains(typeSymbol.Name))
            {
                return new JsonCompatible(typeSymbol);
            }

            if (typeSymbol.Name == "Nullable")
            {
                var typeArgs = typeSymbol.TypeArguments;
                var wrappedType = typeArgs[0];

                if (CheckJsonCompatibilityForType(wrappedType) is JsonIncompatible incompatible)
                {
                    return new JsonIncompatible(typeSymbol, $"The Nullable wraps a value of type {wrappedType.Name}, which is not compatible with JSON. {incompatible.Explanation}");
                }

                return new JsonCompatible(typeSymbol);
            }

            if (typeSymbol.Name == "DateTime")
            {
                return new JsonIncompatible(typeSymbol, $"JSON doesn't have a DateTime type. Use a string instead.");
            }

            if (typeSymbol.Name == "DateTimeOffset")
            {
                return new JsonIncompatible(typeSymbol, $"JSON doesn't have a DateTimeOffset type. Use a string instead.");
            }


            if (HasStupidJsonAttribute(typeSymbol))
            {
                return new JsonCompatible(typeSymbol);
            }
            else
            {
                return new JsonIncompatible(typeSymbol, "There is no such type in JSON.");
            }
        }

        private static Incompatibility VerifyStupidJsonProperty(IPropertySymbol propertySymbol)
        {
            // TODO: Verify no static properties.

            if (CheckJsonCompatibilityForType(propertySymbol.Type) is JsonIncompatible incompatible)
            {
                return new PropIncompatibility(propertySymbol, incompatible.Explanation);
            }

            return null;
        }

        private static string GetTypeName(ITypeSymbol typeSymbol)
        {
            if (typeSymbol is INamedTypeSymbol namedTypeSymbol)
            {
                if (namedTypeSymbol.IsGenericType)
                {
                    var args = namedTypeSymbol.TypeArguments.Select(it => it is INamedTypeSymbol symbol ? GetTypeName(symbol) : it.Name);
                    return $"{namedTypeSymbol.Name}<{string.Join(", ", args)}>";
                }
                else
                {
                    return namedTypeSymbol.Name;
                }
            }
            else if (typeSymbol is IArrayTypeSymbol arrayTypeSymbol)
            {
                return $"{GetTypeName(arrayTypeSymbol.ElementType)}[]";
            }
            else
            {
                return "???";
            }
        }

        private static Incompatibility VerifyStupidJsonConstructor(IMethodSymbol ctorSymbol)
        {
            if (ctorSymbol.Parameters.Any())
            {
                return new CtorIncompatibility(ctorSymbol);
            }

            return null;
        }
    }
}
