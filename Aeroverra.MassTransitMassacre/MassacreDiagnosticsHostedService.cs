using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aeroverra.MassTransitMassacre;

/// <summary>
/// Hosted service that resolves an <see cref="ILogger"/> from the host's
/// <see cref="ILoggerFactory"/> and attaches it to <see cref="MassTransitMassacre"/> when the
/// host starts. Buffered diagnostics recorded during <c>Apply</c> are replayed through the
/// logger so the install log is visible without callers having to construct a logger or pass
/// it through configuration callbacks.
/// </summary>
internal sealed class MassacreDiagnosticsHostedService : IHostedService
{
    private readonly ILogger _logger;

    public MassacreDiagnosticsHostedService(ILoggerFactory loggerFactory, MassacreOptions options)
    {
        _logger = loggerFactory.CreateLogger(options.LoggerCategoryName);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        MassTransitMassacre.Apply(new MassacreOptions { Logger = _logger });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
