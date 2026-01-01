using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexusMods.Abstractions.Loadouts.Sorting;

namespace NexusMods.DataModel.Sorting;

public static class SortingExtensions
{
    /// <summary>
    /// Registers the <see cref="KahnSorter"/> as the default <see cref="ISorter"/> implementation.
    /// </summary>
    public static IServiceCollection AddKahnSorter(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<ISorter, KahnSorter>());
        return services;
    }
}