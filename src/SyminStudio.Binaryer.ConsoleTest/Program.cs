using System;
using SyminStudio.Binaryer.Abstractions;

namespace SyminStudio.Binaryer.ConsoleTest;

[StreamSerializable]
public partial class SimpleTestModel
{
    [BinaryProperty(Length = 4)]
    public int Value1 { get; set; }

    [BinaryProperty(Length = 8)]
    public double Value2 { get; set; }

    [BinaryProperty(Length = 32)]
    public string Message { get; set; } = "";
}

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("测试二进制序列化源生成器");
        
        var model = new SimpleTestModel
        {
            Value1 = 42,
            Value2 = 3.14159,
            Message = "Hello World"
        };

        Console.WriteLine($"Value1: {model.Value1}");
        Console.WriteLine($"Value2: {model.Value2}");
        Console.WriteLine($"Message: {model.Message}");

        // 测试是否生成了方法
        try
        {
            using var stream = new System.IO.MemoryStream();
            model.WriteToStream(stream);
            Console.WriteLine($"写入成功，流长度: {stream.Length}");
            
            stream.Position = 0;
            var newModel = new SimpleTestModel();
            newModel.ReadFromStream(stream);
            
            Console.WriteLine($"读取后 - Value1: {newModel.Value1}");
            Console.WriteLine($"读取后 - Value2: {newModel.Value2}");
            Console.WriteLine($"读取后 - Message: {newModel.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"错误: {ex.Message}");
        }

        // 测试高级功能
        AdvancedProgram.TestAdvancedFeatures();
    }
}
