using System;
using System.Buffers.Binary;

namespace ClientLogic.Protocol;

/// <summary>
/// A player's side, colour, start location and team, packed into one integer to keep lobby messages
/// short (the CnCNet IRC server kicks clients that send too much). Used by both the player options
/// broadcast and the player options request, with the same byte order.
/// Indices are lobby drop-down indices: 0 means random for side, colour and start, and no team.
/// </summary>
public readonly record struct PackedPlayerOptions(int Side, int Color, int Start, int Team)
{
    /// <summary>Packs the options; each index must be 0-255.</summary>
    public int Pack()
    {
        if ((Side | Color | Start | Team) is < 0 or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(PackedPlayerOptions), this, "Player option indices must be 0-255.");

        Span<byte> bytes = stackalloc byte[] { (byte)Team, (byte)Start, (byte)Color, (byte)Side };
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    public static PackedPlayerOptions Unpack(int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return new PackedPlayerOptions(Side: bytes[3], Color: bytes[2], Start: bytes[1], Team: bytes[0]);
    }
}
