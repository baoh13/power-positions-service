using Microsoft.Extensions.DependencyInjection;

namespace power_position_tracker_integration_tests.TestHelpers;

/// <summary>
/// Extension methods for manipulating IServiceCollection in tests
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Removes all registrations of the specified service type from the service collection
    /// </summary>
    public static IServiceCollection RemoveService<TService>(this IServiceCollection services)
    {
        var descriptors = services.Where(d => d.ServiceType == typeof(TService)).ToList();
        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
        return services;
    }

    /// <summary>
    /// Removes a service and replaces it with a new implementation
    /// </summary>
    public static IServiceCollection ReplaceService<TService>(this IServiceCollection services, TService implementation)
        where TService : class
    {
        services.RemoveService<TService>();
        services.AddSingleton(implementation);
        return services;
    }

    /// <summary>
    /// Removes a service and replaces it with a factory function
    /// </summary>
    public static IServiceCollection ReplaceService<TService>(this IServiceCollection services, Func<IServiceProvider, TService> implementationFactory)
        where TService : class
    {
        services.RemoveService<TService>();
        services.AddSingleton(implementationFactory);
        return services;
    }
}
