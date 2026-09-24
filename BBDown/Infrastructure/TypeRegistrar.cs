using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace BBDown;

public sealed class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    public ITypeResolver Build() => new TypeResolver(services.BuildServiceProvider());

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067",
        Justification = "Spectre.Console.Cli does not annotate ITypeRegistrar.Register; BBDown roots its command types from Program.Main.")]
    public void Register(Type service, Type implementation) => services.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) => services.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) => services.AddSingleton(service, _ => factory());
}

public sealed class TypeResolver(IServiceProvider provider) : ITypeResolver
{
    public object? Resolve(Type? type) => type == null ? null : provider.GetService(type);
}
