using System.Reflection;
using NetArchTest.Rules;

namespace DocuMind.ArchitectureTests;

/// <summary>Enforces the Clean Architecture dependency rule: dependencies only point inwards.</summary>
public sealed class LayerTests
{
    private static readonly Assembly Domain = typeof(DocuMind.Domain.Common.Entity).Assembly;
    private static readonly Assembly Application = typeof(DocuMind.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(DocuMind.Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void Domain_has_no_dependencies_on_other_layers_or_frameworks()
    {
        var result = Types.InAssembly(Domain).ShouldNot()
            .HaveDependencyOnAny("DocuMind.Application", "DocuMind.Infrastructure", "DocuMind.Api",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions.AI")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_vendors()
    {
        var result = Types.InAssembly(Application).ShouldNot()
            .HaveDependencyOnAny("DocuMind.Infrastructure", "DocuMind.Api", "Qdrant", "OllamaSharp", "OpenAI", "Anthropic",
                "Npgsql", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_api()
    {
        var result = Types.InAssembly(Infrastructure).ShouldNot().HaveDependencyOn("DocuMind.Api").GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Domain_entities_have_no_public_setters()
    {
        var offenders = Domain.GetTypes()
            .Where(t => t.IsClass && t.Namespace?.StartsWith("DocuMind.Domain", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod?.IsPublic == true && !IsInitOnly(p))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    private static bool IsInitOnly(PropertyInfo property) =>
        property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()
            .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
}
