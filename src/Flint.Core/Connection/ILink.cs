namespace Flint.Core.Connection;

/// <summary>
/// A byte pipe to one amp: the GATT write and notify characteristics. It knows nothing of the protocol;
/// <see cref="Session.AmpSession"/> frames what goes in and deframes what comes out. One instance per connection
/// attempt: once <see cref="Dropped"/> fires or it is disposed, it is not reused.
/// </summary>
public interface ILink : IAsyncDisposable
{
    /// <summary>Connects and subscribes to notifications. Throws if the amp cannot be reached.</summary>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Writes one block, returning once the radio has taken all of it. Callers keep one write in flight.</summary>
    Task WriteAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken);

    /// <summary>The bytes of one notification, in order of arrival.</summary>
    event Action<byte[]>? Received;

    /// <summary>The OS reported the connection gone, with a short reason for the log.</summary>
    event Action<string>? Dropped;
}
