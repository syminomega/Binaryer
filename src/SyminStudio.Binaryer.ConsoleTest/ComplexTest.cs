using System;
using System.Collections.Generic;
using SyminStudio.Binaryer.Abstractions;

namespace SyminStudio.Binaryer.ConsoleTest;

[StreamSerializable]
public partial class ComplexTestModel
{
    [BinarySkip(Length = 4)] // 跳过前 4 bytes
    [BinaryProperty(Length = 8)] // 设置读取长度 8 bytes
    public double ModelSizeX { get; set; }

    [BinarySkip(Length = 8)] // 跳过 8 bytes
    [BinaryProperty] // 自动识别 int 长度
    public int BlockCount { get; set; }

    [BinaryProperty(Length = 32)] // 字符串固定长度
    public string Message { get; set; } = "";

    [BinaryProperty] // 自动识别 bool 长度
    public bool HasExtraData { get; set; }

    [BinaryCondition(ConditionFromProperty = nameof(HasExtraData))] // 根据 HasExtraData 决定是否读取
    [BinaryProperty(Length = 8)] // 设置读取长度 8 bytes
    public double ExtraData { get; set; }

    // 没有标注，不参与序列化与反序列化
    public int MyOtherValue { get; set; }

    [BinaryRepeat(RepeatCountFromProperty = nameof(BlockCount))] // 根据 BlockCount 的值重复读取
    [BinaryProperty(Length = 16)] // 每个元素固定长度
    public List<string> Messages { get; set; } = new();

    [BinaryRepeat(RepeatCount = 3)] // 固定重复读取 3 次
    [BinaryProperty] // 自动识别 int 长度
    public List<int> Numbers { get; set; } = new();
}

class AdvancedProgram
{
    public static void TestAdvancedFeatures()
    {
        Console.WriteLine("\n=== 测试高级功能 ===");
        
        var model = new ComplexTestModel
        {
            ModelSizeX = 123.456,
            BlockCount = 2,
            Message = "Test Message",
            HasExtraData = true,
            ExtraData = 789.012,
            MyOtherValue = 999, // 这个不会被序列化
            Messages = new List<string> { "Message1", "Message2" },
            Numbers = new List<int> { 100, 200, 300 }
        };

        Console.WriteLine($"原始数据:");
        Console.WriteLine($"  ModelSizeX: {model.ModelSizeX}");
        Console.WriteLine($"  BlockCount: {model.BlockCount}");
        Console.WriteLine($"  Message: {model.Message}");
        Console.WriteLine($"  HasExtraData: {model.HasExtraData}");
        Console.WriteLine($"  ExtraData: {model.ExtraData}");
        Console.WriteLine($"  MyOtherValue: {model.MyOtherValue}");
        Console.WriteLine($"  Messages: [{string.Join(", ", model.Messages)}]");
        Console.WriteLine($"  Numbers: [{string.Join(", ", model.Numbers)}]");

        try
        {
            using var stream = new System.IO.MemoryStream();
            model.WriteToStream(stream);
            Console.WriteLine($"\n写入成功，流长度: {stream.Length}");
            Console.WriteLine($"实际序列化大小: {model.BinaryActualSize}");
            
            stream.Position = 0;
            var newModel = new ComplexTestModel();
            newModel.ReadFromStream(stream);
            
            Console.WriteLine($"\n读取后的数据:");
            Console.WriteLine($"  ModelSizeX: {newModel.ModelSizeX}");
            Console.WriteLine($"  BlockCount: {newModel.BlockCount}");
            Console.WriteLine($"  Message: {newModel.Message}");
            Console.WriteLine($"  HasExtraData: {newModel.HasExtraData}");
            Console.WriteLine($"  ExtraData: {newModel.ExtraData}");
            Console.WriteLine($"  MyOtherValue: {newModel.MyOtherValue}"); // 应该是默认值
            Console.WriteLine($"  Messages: [{string.Join(", ", newModel.Messages)}]");
            Console.WriteLine($"  Numbers: [{string.Join(", ", newModel.Numbers)}]");
            Console.WriteLine($"实际反序列化大小: {newModel.BinaryActualSize}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"错误: {ex.Message}");
            Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
        }
    }
}
