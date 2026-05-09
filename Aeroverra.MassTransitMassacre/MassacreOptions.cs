using Microsoft.Extensions.Logging;

namespace Aeroverra.MassTransitMassacre;

/// <summary>
/// Optional configuration passed to <see cref="MassTransitMassacre.Apply(MassacreOptions?)"/>.
/// All properties default to silent / opt-in behaviour so the library remains a drop-in
/// no-op for callers that don't need to observe its activity.
/// </summary>
public sealed class MassacreOptions
{
    /// <summary>
    /// If set, every diagnostic recorded during <see cref="MassTransitMassacre.Apply(MassacreOptions?)"/>
    /// is forwarded to this logger in real time, in addition to being appended to
    /// <see cref="MassTransitMassacre.Diagnostics"/>.
    ///
    /// If <see cref="MassTransitMassacre.Apply(MassacreOptions?)"/> has already run by the time
    /// the logger is attached, the buffered diagnostic history is replayed through the logger
    /// so a late-attached observer sees the full installation log.
    ///
    /// Default: <c>null</c> — diagnostics are recorded only to the in-memory list.
    /// </summary>
    public ILogger? Logger { get; set; }
}
