using SyminStudio.Binaryer.Abstractions;

namespace SyminStudio.Binaryer.Sample;

[StreamSerializable]
public partial class TestModel
{
    [BinaryProperty(Length = 4)]
    public int Value1 { get; set; }

    [BinaryProperty(Length = 8)]
    public double Value2 { get; set; }

    [BinaryProperty(Length = 16)]
    public string Message { get; set; } = "";
    
}
