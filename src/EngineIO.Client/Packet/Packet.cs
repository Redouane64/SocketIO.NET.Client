using System.Text;

namespace EngineIO.Client.Packets;

/// <summary>
/// Represent a message packet.
/// </summary>
public struct Packet
{
    public Packet(PacketFormat format, PacketType type, ReadOnlyMemory<byte> content)
    {
        Format = format;
        Content = content;
        Type = format == PacketFormat.Binary ? PacketType.Message : type;
    }

    /// <summary>
    /// Represents packet type.
    /// </summary>
    public PacketType? Type { get; }

    /// <summary>
    /// Indicate content format stored in the packet.
    /// </summary>
    public PacketFormat Format { get; }

    /// <summary>
    /// Packet content.
    /// </summary>
    public ReadOnlyMemory<byte> Content { get; }

    public override string ToString()
    {
        return Encoding.UTF8.GetString(Content.Span);
    }
}
