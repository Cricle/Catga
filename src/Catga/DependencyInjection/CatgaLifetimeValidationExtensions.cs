using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Catga.Abstractions;
using Catga.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.DependencyInjection;

/// <summary>
/// Validates Catga-critical service registrations for invalid singleton-to-scoped dependency chains.
/// </summary>
public static class CatgaLifetimeValidationExtensions
{
    /// <summary>
    /// Validates Catga-related DI registrations and throws when a singleton root depends on a scoped service.
    /// </summary>
    public static IServiceCollection ValidateCatgaLifetimes(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var validator = new CatgaLifetimeValidator(services);
        validator.Validate();
        return services;
    }

    private sealed class CatgaLifetimeValidator(IServiceCollection services)
    {
        private static readonly Type RequestHandlerWithResponseType = typeof(IRequestHandler<,>);
        private static readonly Type RequestHandlerWithoutResponseType = typeof(IRequestHandler<>);
        private static readonly Type EventHandlerType = typeof(IEventHandler<>);
        private static readonly Type PipelineBehaviorWithResponseType = typeof(IPipelineBehavior<,>);
        private static readonly Type PipelineBehaviorWithoutResponseType = typeof(IPipelineBehavior<>);
        private static readonly Type EnumerableType = typeof(IEnumerable<>);
        private const string FlowExecutorTypeName = "Catga.Flow.Dsl.IFlowExecutor";
        private const string FlowResumeHandlerTypeName = "Catga.Flow.IFlowResumeHandler";
        private const string FlowTypeName = "Catga.Flow.IFlow`1";
        private const string StateMachineExecutorTypeName = "Catga.Flow.StateMachine.IStateMachineExecutor`2";

        private readonly IServiceCollection _services = services;
        private readonly Dictionary<Type, List<ServiceDescriptor>> _descriptorsByServiceType = services
            .GroupBy(static descriptor => descriptor.ServiceType)
            .ToDictionary(static group => group.Key, static group => group.ToList());

        public void Validate()
        {
            var errors = new HashSet<string>(StringComparer.Ordinal);

            foreach (var descriptor in _services)
            {
                if (descriptor.Lifetime != ServiceLifetime.Singleton || !IsValidationRoot(descriptor.ServiceType))
                    continue;

                var implementationType = GetImplementationType(descriptor);
                if (implementationType is null)
                    continue;

                var chain = new List<string>
                {
                    DescribeRoot(descriptor, implementationType)
                };

                ValidateImplementation(
                    descriptor.ServiceType,
                    implementationType,
                    chain,
                    new HashSet<Type>(),
                    errors);
            }

            if (errors.Count == 0)
                return;

            throw new InvalidOperationException(
                "[Catga] Invalid DI lifetimes detected. Singleton Catga services cannot depend on scoped services." +
                Environment.NewLine +
                string.Join(Environment.NewLine, errors.OrderBy(static error => error, StringComparer.Ordinal)) +
                Environment.NewLine +
                "Fix: register the root service as Scoped/Transient, or remove scoped dependencies from the singleton graph.");
        }

        private void ValidateImplementation(
            Type currentServiceType,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType,
            List<string> chain,
            HashSet<Type> visitedImplementationTypes,
            HashSet<string> errors)
        {
            if (!visitedImplementationTypes.Add(implementationType))
                return;

            try
            {
                var constructor = SelectConstructor(implementationType);
                if (constructor is null)
                    return;

                foreach (var parameter in constructor.GetParameters())
                {
                    if (IsSpecialCase(parameter.ParameterType))
                        continue;

                    if (TryGetEnumerableElementType(parameter.ParameterType, out var elementType))
                    {
                        foreach (var descriptor in GetAllDescriptors(elementType))
                        {
                            ValidateDependency(currentServiceType, descriptor, elementType, chain, visitedImplementationTypes, errors);
                        }

                        continue;
                    }

                    var dependencyDescriptor = GetLastDescriptor(parameter.ParameterType);
                    if (dependencyDescriptor is null)
                    {
                        if (parameter.HasDefaultValue)
                            continue;

                        continue;
                    }

                    ValidateDependency(currentServiceType, dependencyDescriptor, parameter.ParameterType, chain, visitedImplementationTypes, errors);
                }
            }
            finally
            {
                visitedImplementationTypes.Remove(implementationType);
            }
        }

        private void ValidateDependency(
            Type currentServiceType,
            ServiceDescriptor dependencyDescriptor,
            Type requestedDependencyType,
            List<string> chain,
            HashSet<Type> visitedImplementationTypes,
            HashSet<string> errors)
        {
            var resolvedDependencyType = CloseServiceTypeIfNeeded(dependencyDescriptor.ServiceType, requestedDependencyType);
            var dependencyLabel = DescribeDependency(resolvedDependencyType);

            if (dependencyDescriptor.Lifetime == ServiceLifetime.Scoped)
            {
                errors.Add(
                    "- " +
                    string.Join(" -> ", chain.Append($"{dependencyLabel} (Scoped)")) +
                    $" [while resolving {DescribeDependency(currentServiceType)}]");
                return;
            }

            var dependencyImplementationType = GetImplementationType(dependencyDescriptor);
            if (dependencyImplementationType is null)
                return;

            chain.Add($"{dependencyLabel} ({dependencyDescriptor.Lifetime})");
            ValidateImplementation(
                resolvedDependencyType,
                dependencyImplementationType,
                chain,
                visitedImplementationTypes,
                errors);
            chain.RemoveAt(chain.Count - 1);
        }

        [UnconditionalSuppressMessage(
            "AOT",
            "IL2070",
            Justification = "Startup-only DI validation intentionally reflects over registered implementation constructors.")]
        private ConstructorInfo? SelectConstructor([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
        {
            var constructors = implementationType
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(static constructor => constructor.GetParameters().Length)
                .ToArray();

            if (constructors.Length == 0)
                return null;

            foreach (var constructor in constructors)
            {
                if (CanResolve(constructor))
                    return constructor;
            }

            return constructors[0];
        }

        private bool CanResolve(ConstructorInfo constructor)
        {
            foreach (var parameter in constructor.GetParameters())
            {
                if (IsSpecialCase(parameter.ParameterType) || parameter.HasDefaultValue)
                    continue;

                if (TryGetEnumerableElementType(parameter.ParameterType, out _))
                    continue;

                if (GetLastDescriptor(parameter.ParameterType) is null)
                    return false;
            }

            return true;
        }

        private ServiceDescriptor? GetLastDescriptor(Type serviceType)
        {
            if (_descriptorsByServiceType.TryGetValue(serviceType, out var directDescriptors) && directDescriptors.Count > 0)
                return directDescriptors[^1];

            if (!serviceType.IsGenericType)
                return null;

            var genericDefinition = serviceType.GetGenericTypeDefinition();
            if (_descriptorsByServiceType.TryGetValue(genericDefinition, out var openGenericDescriptors) && openGenericDescriptors.Count > 0)
                return openGenericDescriptors[^1];

            return null;
        }

        private IEnumerable<ServiceDescriptor> GetAllDescriptors(Type serviceType)
        {
            var results = new List<ServiceDescriptor>();

            if (_descriptorsByServiceType.TryGetValue(serviceType, out var directDescriptors))
                results.AddRange(directDescriptors);

            if (serviceType.IsGenericType)
            {
                var genericDefinition = serviceType.GetGenericTypeDefinition();
                if (_descriptorsByServiceType.TryGetValue(genericDefinition, out var openGenericDescriptors))
                    results.AddRange(openGenericDescriptors);
            }

            return results;
        }

        private static bool IsValidationRoot(Type serviceType)
        {
            if (serviceType == typeof(ICatgaMediator) ||
                serviceType == typeof(IRequestClientFactory) ||
                serviceType.FullName == FlowExecutorTypeName ||
                serviceType.FullName == FlowResumeHandlerTypeName)
            {
                return true;
            }

            if (!serviceType.IsGenericType)
                return false;

            var genericDefinition = serviceType.GetGenericTypeDefinition();
            return genericDefinition == RequestHandlerWithResponseType ||
                   genericDefinition == RequestHandlerWithoutResponseType ||
                   genericDefinition == EventHandlerType ||
                   genericDefinition == PipelineBehaviorWithResponseType ||
                   genericDefinition == PipelineBehaviorWithoutResponseType ||
                   genericDefinition.FullName == FlowTypeName ||
                   genericDefinition.FullName == StateMachineExecutorTypeName;
        }

        private static bool TryGetEnumerableElementType(Type type, out Type elementType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == EnumerableType)
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }

            elementType = null!;
            return false;
        }

        private static bool IsSpecialCase(Type serviceType)
        {
            if (serviceType == typeof(IServiceProvider) ||
                serviceType == typeof(IServiceScopeFactory) ||
                serviceType.FullName == "Microsoft.Extensions.DependencyInjection.IServiceProviderIsService" ||
                serviceType.FullName == "Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider" ||
                serviceType.FullName == "Microsoft.Extensions.DependencyInjection.IServiceProviderIsKeyedService")
            {
                return true;
            }

            return false;
        }

        [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        private static Type? GetImplementationType(ServiceDescriptor descriptor)
        {
            if (descriptor.ImplementationType is null)
                return null;

            return descriptor.ImplementationType;
        }

        private static Type CloseServiceTypeIfNeeded(Type registeredServiceType, Type requestedServiceType)
        {
            return registeredServiceType.IsGenericTypeDefinition && requestedServiceType.IsGenericType
                ? requestedServiceType
                : registeredServiceType;
        }

        private static string DescribeRoot(ServiceDescriptor descriptor, Type implementationType)
        {
            return $"{DescribeDependency(descriptor.ServiceType)} (Singleton via {DescribeDependency(implementationType)})";
        }

        private static string DescribeDependency(Type type)
        {
            return type.FullName ?? type.Name;
        }
    }
}
