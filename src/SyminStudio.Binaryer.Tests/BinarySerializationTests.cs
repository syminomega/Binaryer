using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using SyminStudio.Binaryer.Abstractions;
using Xunit.Abstractions;

namespace SyminStudio.Binaryer.Tests;

[StreamSerializable]
public partial class TestBinaryModel
{
    [BinaryProperty(Length = 4)]
    public int IntValue { get; set; }

    [BinaryProperty(Length = 8)]
    public double DoubleValue { get; set; }

    [BinaryProperty(Length = 20)]
    public string StringValue { get; set; } = "";

    [BinaryProperty]
    public bool BoolValue { get; set; }

    [BinarySkip(Length = 4)]
    [BinaryProperty(Length = 16)]
    public string MessageAfterSkip { get; set; } = "";
}

[StreamSerializable]
public partial class ConditionalTestModel
{
    [BinaryProperty]
    public bool HasOptionalData { get; set; }

    [BinaryCondition(ConditionFromProperty = nameof(HasOptionalData))]
    [BinaryProperty(Length = 8)]
    public double OptionalData { get; set; }

    [BinaryProperty(Length = 10)]
    public string AlwaysPresent { get; set; } = "";
}

[StreamSerializable]
public partial class RepeatTestModel
{
    [BinaryProperty]
    public int Count { get; set; }

    [BinaryRepeat(RepeatCountFromProperty = nameof(Count))]
    [BinaryProperty(Length = 8)]
    public List<string> DynamicMessages { get; set; } = new();

    [BinaryRepeat(RepeatCount = 3)]
    [BinaryProperty]
    public List<int> FixedNumbers { get; set; } = new();
}

public class BinarySerializationTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public void TestBasicSerialization()
    {
        var original = new TestBinaryModel
        {
            IntValue = 42,
            DoubleValue = 3.14159,
            StringValue = "Hello",
            BoolValue = true,
            MessageAfterSkip = "After Skip"
        };

        using var stream = new MemoryStream();
        
        // 序列化
        original.WriteToStream(stream);
        Assert.True(stream.Length > 0);

        // 反序列化
        stream.Position = 0;
        var deserialized = new TestBinaryModel();
        deserialized.ReadFromStream(stream);

        // 验证
        Assert.Equal(original.IntValue, deserialized.IntValue);
        Assert.Equal(original.DoubleValue, deserialized.DoubleValue);
        Assert.Equal(original.StringValue, deserialized.StringValue);
        Assert.Equal(original.BoolValue, deserialized.BoolValue);
        Assert.Equal(original.MessageAfterSkip, deserialized.MessageAfterSkip);
    }

    [Fact]
    public void TestConditionalSerialization_WithOptionalData()
    {
        var original = new ConditionalTestModel
        {
            HasOptionalData = true,
            OptionalData = 123.456,
            AlwaysPresent = "Always"
        };

        using var stream = new MemoryStream();
        
        original.WriteToStream(stream);
        stream.Position = 0;
        
        var deserialized = new ConditionalTestModel();
        deserialized.ReadFromStream(stream);

        Assert.Equal(original.HasOptionalData, deserialized.HasOptionalData);
        Assert.Equal(original.OptionalData, deserialized.OptionalData);
        Assert.Equal(original.AlwaysPresent, deserialized.AlwaysPresent);
    }

    [Fact]
    public void TestConditionalSerialization_WithoutOptionalData()
    {
        var original = new ConditionalTestModel
        {
            HasOptionalData = false,
            OptionalData = 123.456, // 这个值不应该被序列化
            AlwaysPresent = "Always"
        };

        using var stream = new MemoryStream();
        
        original.WriteToStream(stream);
        stream.Position = 0;
        
        var deserialized = new ConditionalTestModel();
        deserialized.ReadFromStream(stream);

        Assert.Equal(original.HasOptionalData, deserialized.HasOptionalData);
        Assert.Equal(0.0, deserialized.OptionalData); // 应该是默认值
        Assert.Equal(original.AlwaysPresent, deserialized.AlwaysPresent);
    }

    [Fact]
    public void TestRepeatSerialization()
    {
        var original = new RepeatTestModel
        {
            Count = 2,
            DynamicMessages = new List<string> { "Msg1", "Msg2" },
            FixedNumbers = new List<int> { 100, 200, 300 }
        };

        using var stream = new MemoryStream();
        
        original.WriteToStream(stream);
        stream.Position = 0;
        
        var deserialized = new RepeatTestModel();
        deserialized.ReadFromStream(stream);

        Assert.Equal(original.Count, deserialized.Count);
        Assert.Equal(original.DynamicMessages.Count, deserialized.DynamicMessages.Count);
        Assert.Equal(original.FixedNumbers.Count, deserialized.FixedNumbers.Count);
        
        for (int i = 0; i < original.DynamicMessages.Count; i++)
        {
            Assert.Equal(original.DynamicMessages[i], deserialized.DynamicMessages[i]);
        }
        
        for (int i = 0; i < original.FixedNumbers.Count; i++)
        {
            Assert.Equal(original.FixedNumbers[i], deserialized.FixedNumbers[i]);
        }
    }

    [Fact]
    public void TestBinaryActualSizeTracking()
    {
        var model = new TestBinaryModel
        {
            IntValue = 42,
            DoubleValue = 3.14159,
            StringValue = "Hello",
            BoolValue = true,
            MessageAfterSkip = "After Skip"
        };

        using var stream = new MemoryStream();
        model.WriteToStream(stream);
        
        stream.Position = 0;
        var deserialized = new TestBinaryModel();
        deserialized.ReadFromStream(stream);

        // BinaryActualSize 应该记录实际读取的字节数
        Assert.True(deserialized.BinaryActualSize > 0);
        testOutputHelper.WriteLine($"Binary actual size: {deserialized.BinaryActualSize}");
    }
}
