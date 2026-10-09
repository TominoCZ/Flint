using Flint.Core.Protocol;

namespace Flint.Core.Tests;

public class ChunkStreamTests
{
    // A full preset reply from a real Spark 40: 7 blocks, 15 chunks, several of which straddle a block boundary.
    // From soundshed-app's src/core/tests/messageParsingTest.ts.
    private static readonly string[] RealPresetBlocks =
    [
        "01fe000041ff6a000000000000000000f0013a640301200f001900015924004533394545313600412d444531302d00343634362d3832f7f0013a590301000f011935372d360037393137423630103842363228322d404372756e636823f7f0013a4d0301400f0219302e",
        "01fe000041ff6a000000000000000000372800322d4372756e6302682869636f6e2e28706e674a427000f7f0013a4a0301300f031900172e62006961732e6e6f6940736567617465436d1300114a3d6d7ef7f0013a5e0301680f04192501114a603d782c7802114a1000",
        "01fe000041ff6a0000000000000000000000002a436f006d70726573736ff7f0013a3a0301300f0519724312000b114a3e1b172c0173114a3f0578712d00446973746f7274f7f0013a790301000f0619696f6e546c5339421400114a643e06106a01114a623e05341602",
        "01fe000041ff6a000000000000000000114af7f0013a5d0301500f07193e771f3b0603114a000000000128414320426f6f6c7374431500114af7f0013a1e0301200f08193f0f5e261601114a3e7a586c1602114a3e1a177c7603114a3e502b3ff7f0013a260301300f09",
        "01fe000041ff6a0000000000000000001904114a3e0d322e0a27466c61306e6765724213003b114a3e535c3001f7f0013a210301180f0a19114a3f29193d3802114a3f270539612944656c6160794d6f6e6f4215f7f0013a4e0301300f0b1900114a3d313e3c3e01114a",
        "01fe000041ff6a0000000000000000003e3429237702114a3f3402637103114a3ff7f0013a770301300f0c1919191a044b114a3f0000002b00626961732e72653076657262431700f7f0013a330301180f0d19114a3e4b19072901114a3f651b410302114a3f1b19001e",
        "01fe000041ff3c00000000000000000003114a3e6cf7f0013a5a0301500f0e183e590411414a3f1507500511414a3f2666670611394a3e4c4c4d07f7",
    ];

    [Theory]
    [InlineData(int.MaxValue)] // one push per block, as an SPP read would deliver them
    [InlineData(20)]           // 20-byte pieces, as BLE notifications without an MTU exchange deliver them
    [InlineData(1)]
    public void Reassembles_a_real_multi_block_preset(int pieceSize)
    {
        var bytes = RealPresetBlocks.SelectMany(Convert.FromHexString).ToArray();
        var stream = new ChunkStream();
        var chunks = bytes.Chunk(pieceSize).SelectMany(piece => stream.Push(piece)).ToList();

        Assert.Equal(15, chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.True(Frame.TryDecodeChunk(chunks[i], out var chunk), $"chunk {i} fails its checksum");
            Assert.Equal((0x03, 0x01, 15, i), (chunk!.Cmd, chunk.Sub, chunk.Payload[0], (int)chunk.Payload[1]));
        }
    }
}
