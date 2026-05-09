using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Aeroverra.MassTransitMassacre;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register MassTransit Massacre. Patches install **during this call**, before
    /// <c>BuildServiceProvider</c> returns, so they are always in place before MassTransit's
    /// own hosted services run. A hosted service is also registered that resolves an
    /// <see cref="Microsoft.Extensions.Logging.ILogger"/> from the host's
    /// <see cref="Microsoft.Extensions.Logging.ILoggerFactory"/> and emits the buffered
    /// diagnostic snapshot through it once the host starts.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional configuration. Set <see cref="MassacreOptions.Enabled"/> to <c>false</c> to
    /// register the library without installing patches (useful for conditional rollouts).
    /// </param>
    public static IServiceCollection AddMassTransitMassacre(
        this IServiceCollection services,
        Action<MassacreOptions>? configure = null)
    {
        MassacreOptions options = new();
        configure?.Invoke(options);

        if (!options.Enabled)
        {
            return services;
        }

        MassTransitMassacre.Apply(options);

        services.TryAddSingleton(options);
        services.AddHostedService<MassacreDiagnosticsHostedService>();

        return services;
    }
}
