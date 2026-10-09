namespace Flint.Core.Protocol.Payloads;

/// <summary>A decoded chunk payload. <see cref="Encode"/> gives back exactly the bytes it was decoded from.</summary>
public interface IPayload
{
    byte[] Encode();
}
