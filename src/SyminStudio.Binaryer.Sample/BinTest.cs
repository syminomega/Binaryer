using System.Collections.Generic;

namespace SyminStudio.Binaryer.Sample;

using SyminStudio.Binaryer.Abstractions;

[StreamSerializable]
public partial class BinTest
{
    [BinarySkip(Length = 4)] // 跳过前 4 bytes
    [BinaryProperty(Length = 8)] // 设置读取长度 8 bytes
    public double ModelSizeX { get; set; }

    [BinarySkip(Length = 8)] // 跳过 8 bytes
    [BinaryProperty] // 自动识别 int 长度
    public int BlockCountA { get; set; }

    [BinaryProperty(Length = 20)] // 设置读取长度 20 bytes，如果对象的实际长度小于 20 bytes，则会自动填充或跳过
    public required BinBlock SingleBock { get; set; }

    // 没有标注，不参与序列化与反序列化
    public int MyOtherValue { get; set; }

    [BinaryRepeat(RepeatCountFromProperty = nameof(BlockCountA))] // 根据 BlockCountA 的值重复读取 BinBlock
    public required List<BinBlock> BinBlocksA { get; set; }

    [BinaryRepeat(RepeatCountFromMethod = nameof(GetBlockCountB))] // 根据 GetBlockCountB 方法的返回值重复读取 BinBlock
    public required List<BinBlock> BinBlocksB { get; set; }

    [BinaryProperty(Length = 20)] // 单个元素固定长度，如果对象的实际长度小于 20 bytes，则会自动填充或跳过
    [BinaryRepeat(RepeatCount = 2)] // 固定重复读取 2 次 BinBlock
    public required List<BinBlock> BinBlocksC { get; set; }

    private int GetBlockCountB()
    {
        // 这里可以实现一些逻辑来计算 BinBlocksB 的数量
        return BinBlocksA.Count * 2; // 例如，假设 B 的数量是 A 的两倍
    }
}

[StreamSerializable]
public partial class BinBlock
{
    [BinaryProperty(Length = 4)] // 设置读取长度 4 bytes
    public int BlockModelLength { get; set; }

    [BinarySkip(Length = 4)] // 跳过 4 bytes
    [BinaryProperty] // 自动识别 bool 长度，在 .NET 中为 1 byte
    public bool HasExtraData { get; set; }

    [BinaryCondition(ConditionFromProperty = nameof(HasExtraData))] // 根据前面读到的 HasExtraData 决定是否读取 ExtraData
    [BinaryProperty(Length = 8)] // 设置读取长度 8 bytes
    public double ExtraData { get; set; }

    [BinaryProperty(Length = 64)] // 字符串必须指定解析长度
    public required string Message { get; set; }

    [BinaryProperty(Length = 16)] // 设置读取长度 16 bytes
    public required byte[] Data1 { get; set; }

    [BinaryProperty] // 自动识别 byte[] 长度
    public required byte[] Data2 { get; set; } = new byte [16];
}

// 源生成器所要实现的
/*
public partial class BinTest
{
    public int BinaryActualSize { get; private set; } // 实际的二进制大小

    public void WriteToStream(global::System.IO.Stream stream)
    {
        // 实现将当前对象写入到流中
    }

    public void ReadFromStream(global::System.IO.Stream stream)
    {
        var buffer = new byte[8]; // 遍历后判断当前类中，最长的单个属性读取所需缓存 (不包含class)
        BinaryActualSize = 0; // 初始化实际大小

        stream.Seek(4, System.IO.SeekOrigin.Current); // 跳过 SkipBytes 的 4 bytes
        BinaryActualSize += 4; // 更新实际大小

        stream.ReadExactly(buffer, 0, 8); // 读取 ModelSizeX
        ModelSizeX = global::System.BitConverter.ToDouble(buffer[0..8], 0);
        BinaryActualSize += 8; // 更新实际大小

        stream.Seek(8, System.IO.SeekOrigin.Current); // 跳过 SkipBytes 的 8 bytes
        BinaryActualSize += 8; // 更新实际大小

        stream.ReadExactly(buffer, 0, 4); // 读取 BlockCountA
        BlockCountA = global::System.BitConverter.ToInt32(buffer[0..4], 0);
        BinaryActualSize += 4; // 更新实际大小

        // 需要读取 SingleBock 类，因此需要先创建。遇到 required 属性时，必须先创建
        SingleBock = new BinBlock
        {
            Message = null!, // required 属性先强制赋值为 null
            Data1 = null!,
            Data2 = null!
        };
        SingleBock.ReadFromStream(stream); // 读取 SingleBock 的数据，必须也是 StreamSerializable
        BinaryActualSize += 20; // 更新实际大小
        stream.Seek(20 - SingleBock.BinaryActualSize,
            System.IO.SeekOrigin.Current); // 跳过 SingleBock 的剩余部分，确保读取长度为 20 bytes

        // 跳过 MyOtherValue 的读取，不进行任何操作

        // repeat 用于集合类型
        BinBlocksA = new List<BinBlock>();
        for (int i = 0; i < BlockCountA; i++) // 从属性确定重复值
        {
            var repeatData = new BinBlock
            {
                Message = null!,
                Data1 = null!,
                Data2 = null!
            };
            repeatData.ReadFromStream(stream);
            BinBlocksA.Add(repeatData);
            BinaryActualSize += repeatData.BinaryActualSize; // 更新实际大小
        }

        int countForBinBlocksB = GetBlockCountB(); // 从方法确定重复值
        BinBlocksB = new List<BinBlock>();
        for (int i = 0; i < countForBinBlocksB; i++)
        {
            var repeatData = new BinBlock
            {
                Message = null!,
                Data1 = null!,
                Data2 = null!
            };
            repeatData.ReadFromStream(stream);
            BinBlocksB.Add(repeatData);
            BinaryActualSize += repeatData.BinaryActualSize; // 更新实际大小
        }

        BinBlocksC = new List<BinBlock>();
        for (int i = 0; i < 2; i++) // 固定读取 2 次
        {
            var block = new BinBlock
            {
                Message = null!,
                Data1 = null!,
                Data2 = null!
            };
            block.ReadFromStream(stream);
            BinBlocksC.Add(block);
            BinaryActualSize += 20; // 更新实际大小，这里因为使用了 Length = 20，所以每次读取的大小都是 20 bytes
            stream.Seek(20 - block.BinaryActualSize, System.IO.SeekOrigin.Current); // 跳过 block 的剩余部分，确保读取长度为 20 bytes
        }

        // 结束读取此对象的所有标注的属性
    }
}

public partial class BinBlock
{
    public int BinaryActualSize { get; private set; } // 实际的二进制大小

    public void WriteToStream(global::System.IO.Stream stream)
    {
        // 实现将当前对象写入到流中
    }

    public void ReadFromStream(global::System.IO.Stream stream)
    {
        var buffer = new byte[64]; // 遍历后判断当前类中，最长的单个属性读取所需缓存
        BinaryActualSize = 0; // 初始化实际大小

        stream.ReadExactly(buffer, 0, 4); // 读取 BlockModelLength
        BlockModelLength = global::System.BitConverter.ToInt32(buffer[0..4], 0);
        BinaryActualSize += 4; // 更新实际大小

        stream.Seek(4, System.IO.SeekOrigin.Current); // 跳过 SkipBytes 的 4 bytes
        BinaryActualSize += 4; // 更新实际大小

        stream.ReadExactly(buffer, 0, 1); // 读取 HasExtraData
        HasExtraData = global::System.BitConverter.ToBoolean(buffer[0..1], 0);
        BinaryActualSize += 1; // 更新实际大小

        if (HasExtraData) // 如果 HasExtraData 为 true，则读取 ExtraData
        {
            stream.ReadExactly(buffer, 0, 8); // 读取 ExtraData
            ExtraData = global::System.BitConverter.ToDouble(buffer[0..8], 0);
            BinaryActualSize += 8; // 更新实际大小
        }

        stream.ReadExactly(buffer, 0, 64); // 读取 Message
        Message = global::System.Text.Encoding.UTF8.GetString(buffer, 0, 64).TrimEnd('\0');
        BinaryActualSize += 64; // 更新实际大小

        stream.ReadExactly(buffer, 0, 16); // 读取 Data1
        Data1 = new byte[16];
        System.Array.Copy(buffer, 0, Data1, 0, 16);
        BinaryActualSize += 16; // 更新实际大小

        stream.ReadExactly(buffer, 0, 16); // 读取 Data2
        Data2 = new byte[16];
        System.Array.Copy(buffer, 0, Data2, 0, 16);
        BinaryActualSize += 16; // 更新实际大小

        // 结束读取此对象的所有标注的属性
    }
}
*/