# SyminStudio.Binaryer - 二进制序列化源生成器

## 概述

这是一个基于 C# Source Generator 的二进制序列化框架，通过 Attribute 标注自动生成高效的二进制读写代码。设计目标是简化二进制数据格式的处理，特别适用于需要精确控制二进制布局的场景。

## 主要特性

### 1. 基础序列化 (`StreamSerializable`)
- 标记需要生成序列化代码的类
- 自动生成 `ReadFromStream` 和 `WriteToStream` 方法
- 跟踪实际序列化大小 (`BinaryActualSize`)

### 2. 属性序列化 (`BinaryProperty`)
- 支持基本类型：`int`, `double`, `float`, `bool`, `byte`, `char` 等
- 支持字符串和字节数组
- 支持固定长度和自动长度检测
- 支持从属性或方法获取长度

```csharp
[BinaryProperty(Length = 4)]
public int Value { get; set; }

[BinaryProperty] // 自动检测长度
public double AutoSize { get; set; }
```

### 3. 跳过字节 (`SkipBytes`)
- 在序列化时插入指定字节数的填充
- 在反序列化时跳过指定字节数
- 支持固定长度和动态长度

```csharp
[SkipBytes(Length = 4)] // 跳过 4 字节
[BinaryProperty]
public int ValueAfterSkip { get; set; }
```

### 4. 条件序列化 (`BinaryCondition`)
- 根据其他属性或方法的值决定是否序列化
- 支持复杂的条件逻辑

```csharp
[BinaryProperty]
public bool HasExtraData { get; set; }

[BinaryCondition(ConditionFromProperty = nameof(HasExtraData))]
[BinaryProperty(Length = 8)]
public double ExtraData { get; set; }
```

### 5. 重复序列化 (`BinaryRepeat`)
- 支持集合类型的序列化
- 支持固定次数重复和动态次数重复
- 支持从属性或方法获取重复次数

```csharp
[BinaryProperty]
public int Count { get; set; }

[BinaryRepeat(RepeatCountFromProperty = nameof(Count))]
[BinaryProperty(Length = 16)]
public List<string> Messages { get; set; }

[BinaryRepeat(RepeatCount = 3)] // 固定重复 3 次
[BinaryProperty]
public List<int> Numbers { get; set; }
```

## 使用示例

### 基础使用

```csharp
using SyminStudio.Binaryer.Abstractions;

[StreamSerializable]
public partial class MyBinaryModel
{
    [BinaryProperty(Length = 4)]
    public int Value1 { get; set; }

    [BinaryProperty(Length = 8)]
    public double Value2 { get; set; }

    [BinaryProperty(Length = 32)]
    public string Message { get; set; } = "";
}

// 使用
var model = new MyBinaryModel
{
    Value1 = 42,
    Value2 = 3.14159,
    Message = "Hello World"
};

using var stream = new MemoryStream();

// 序列化
model.WriteToStream(stream);

// 反序列化
stream.Position = 0;
var newModel = new MyBinaryModel();
newModel.ReadFromStream(stream);
```

### 复杂示例

```csharp
[StreamSerializable]
public partial class ComplexBinaryFormat
{
    [SkipBytes(Length = 4)] // 跳过文件头
    [BinaryProperty(Length = 8)]
    public double Version { get; set; }

    [BinaryProperty]
    public int RecordCount { get; set; }

    [BinaryProperty]
    public bool HasMetadata { get; set; }

    [BinaryCondition(ConditionFromProperty = nameof(HasMetadata))]
    [BinaryProperty(Length = 64)]
    public string Metadata { get; set; } = "";

    [BinaryRepeat(RepeatCountFromProperty = nameof(RecordCount))]
    [BinaryProperty(Length = 32)]
    public List<string> Records { get; set; } = new();

    // 这个属性不会被序列化
    public int CalculatedValue { get; set; }
}
```

## 技术实现

### 源生成器架构

1. **属性分析器** - 分析类和属性上的特性标注
2. **代码生成器** - 生成对应的序列化和反序列化代码
3. **类型系统** - 支持多种数据类型的自动处理
4. **缓冲区优化** - 自动计算所需的最大缓冲区大小

### 生成的代码特点

- 高效的二进制读写操作
- 精确的内存管理
- 完整的错误处理
- 大小端序自动处理（使用 BitConverter）
- 字符串编码统一使用 UTF-8

## 项目结构

```
SyminStudio.Binaryer/
├── SyminStudio.Binaryer/           # 源生成器核心
│   ├── BinarySerializationGenerator.cs
│   └── SyminStudio.Binaryer.csproj
├── SyminStudio.Binaryer.Sample/    # 使用示例
│   ├── BinVbf.cs
│   ├── TestModel.cs
│   └── SyminStudio.Binaryer.Sample.csproj
├── SyminStudio.Binaryer.Tests/     # 单元测试
│   ├── BinarySerializationTests.cs
│   └── SyminStudio.Binaryer.Tests.csproj
└── SyminStudio.Binaryer.ConsoleTest/ # 控制台测试
    ├── Program.cs
    ├── ComplexTest.cs
    └── SyminStudio.Binaryer.ConsoleTest.csproj
```

## 测试结果

所有功能都通过了完整的单元测试验证：

✅ 基础类型序列化测试  
✅ 条件序列化测试（有条件和无条件）  
✅ 重复序列化测试  
✅ 跳过字节功能测试  
✅ 二进制大小跟踪测试  

## 性能特点

- **零反射** - 使用源生成器生成静态代码，无运行时反射开销
- **内存友好** - 使用缓冲区重用，减少内存分配
- **高效编码** - 直接使用 BitConverter 进行类型转换
- **精确控制** - 可以精确控制二进制布局和填充

## 扩展性

框架设计具有良好的扩展性：

1. **新增数据类型支持** - 可以轻松添加新的基础类型支持
2. **自定义序列化逻辑** - 支持复杂类型的嵌套序列化
3. **动态长度计算** - 支持从方法获取长度和重复次数
4. **条件逻辑扩展** - 支持复杂的条件表达式

这个源生成器为处理二进制数据格式提供了一个强大、灵活且高效的解决方案，特别适用于文件格式解析、网络协议处理、设备通信等场景。
