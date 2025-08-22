using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SyminStudio.Binaryer.StreamSerialization;

namespace SyminStudio.Binaryer;

/// <summary>
/// Source generator for stream serialization with custom attributes.
/// </summary>
[Generator]
public class StreamSerializationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 添加所有自定义属性到编译中
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("BinaryerAttributes.g.cs",
                SourceText.From(BinaryerAttributeUtils.SourceCodeToGen, Encoding.UTF8));
        });

        // 过滤标注了 [StreamSerializable] 属性的类
        var provider = context.SyntaxProvider
            .CreateSyntaxProvider(
                (s, _) => s is ClassDeclarationSyntax,
                (ctx, _) => GetClassDeclarationForSourceGen(ctx))
            .Where(t => t.streamSerializableFound)
            .Select((t, _) => t.Item1);

        // 生成源代码
        context.RegisterSourceOutput(context.CompilationProvider.Combine(provider.Collect()),
            ((ctx, t) => GenerateStreamSerializationCode(ctx, t.Left, t.Right)));
    }

    /// <summary>
    /// 检查节点是否标注了 [StreamSerializable] 属性并映射语法上下文到特定节点类型 (ClassDeclarationSyntax)。
    /// </summary>
    /// <param name="context">基于 CreateSyntaxProvider 谓词的语法上下文</param>
    /// <returns>特定的转换和是否找到属性。</returns>
    private static (ClassDeclarationSyntax, bool streamSerializableFound) GetClassDeclarationForSourceGen(
        GeneratorSyntaxContext context)
    {
        var classDeclarationSyntax = (ClassDeclarationSyntax)context.Node;

        // 遍历类的所有属性
        foreach (AttributeListSyntax attributeListSyntax in classDeclarationSyntax.AttributeLists)
        foreach (AttributeSyntax attributeSyntax in attributeListSyntax.Attributes)
        {
            if (context.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol is not IMethodSymbol attributeSymbol)
                continue; // 如果无法获取符号，则忽略它

            string attributeName = attributeSymbol.ContainingType.ToDisplayString();

            // 检查 [StreamSerializable] 属性的全名
            if (attributeName ==
                $"{BinaryerAttributeUtils.NamespaceStr}.{BinaryerAttributeUtils.StreamSerializableAttributeName}")
                return (classDeclarationSyntax, true);
        }

        return (classDeclarationSyntax, false);
    }

    /// <summary>
    /// 生成代码操作。
    /// 它将在用户更改的特定节点（标注了 [StreamSerializable] 属性的 ClassDeclarationSyntax）上执行。
    /// </summary>
    /// <param name="context">用于添加源文件的源生成上下文。</param>
    /// <param name="compilation">用于提供对语义模型访问的编译。</param>
    /// <param name="classDeclarations">标注了 [StreamSerializable] 属性的 ClassDeclarationSyntax 节点。</param>
    private void GenerateStreamSerializationCode(SourceProductionContext context, Compilation compilation,
        ImmutableArray<ClassDeclarationSyntax> classDeclarations)
    {
        // 遍历所有过滤的类声明
        foreach (var classDeclarationSyntax in classDeclarations)
        {
            // 我们需要获取类的语义模型来检索元数据
            var semanticModel = compilation.GetSemanticModel(classDeclarationSyntax.SyntaxTree);

            // 符号允许我们获取编译时信息
            if (semanticModel.GetDeclaredSymbol(classDeclarationSyntax) is not INamedTypeSymbol classSymbol)
                continue;

            var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

            // 'Identifier' 表示节点的令牌。从语法节点获取类名。
            var className = classDeclarationSyntax.Identifier.Text;

            // 分析类的属性并验证类型
            var (propertyInfos, hasErrors) = AnalyzePropertiesWithValidation(context, classSymbol, compilation);

            // 如果有错误，跳过代码生成
            if (hasErrors)
                continue;

            // 生成序列化和反序列化代码
            var code = GenerateClassCode(namespaceName, className, propertyInfos);

            // 将源代码添加到编译中
            context.AddSource($"{className}.Binaryer.g.cs", SourceText.From(code, Encoding.UTF8));
        }
    }

    /// <summary>
    /// 分析属性并验证类型支持，对不支持的类型报告错误
    /// </summary>
    private (List<PropertyInfo>, bool hasErrors) AnalyzePropertiesWithValidation(SourceProductionContext context,
        INamedTypeSymbol classSymbol, Compilation compilation)
    {
        var properties = new List<PropertyInfo>();
        bool hasErrors = false;

        foreach (var member in classSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.IsStatic) continue;

            var propertyInfo = new PropertyInfo
            {
                Name = member.Name,
                TypeName = member.Type.ToDisplayString()
            };

            // 获取属性的语法节点以保持属性声明顺序
            var propertySyntax =
                member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as PropertyDeclarationSyntax;
            if (propertySyntax == null) continue;

            // 按照属性在源代码中的声明顺序分析特性
            foreach (var attributeList in propertySyntax.AttributeLists)
            {
                foreach (var attributeSyntax in attributeList.Attributes)
                {
                    var semanticModel = compilation.GetSemanticModel(attributeSyntax.SyntaxTree);
                    if (semanticModel.GetSymbolInfo(attributeSyntax).Symbol is not IMethodSymbol attributeSymbol)
                        continue;

                    var attributeName = attributeSymbol.ContainingType.Name;
                    var attributeData = member.GetAttributes().FirstOrDefault(attr =>
                        attr.AttributeClass?.Name == attributeName);

                    if (attributeData == null) continue;

                    switch (attributeName)
                    {
                        case BinaryerAttributeUtils.BinaryConditionAttributeName:
                            var conditionFromProperty = GetAttributeStringValue(attributeData, "ConditionFromProperty");
                            var conditionFromMethod = GetAttributeStringValue(attributeData, "ConditionFromMethod");

                            // 添加条件开始
                            propertyInfo.AttributeActions.Add(new AttributeAction
                            {
                                Type = AttributeActionType.BinaryConditionStart,
                                Data = new ConditionData
                                {
                                    ConditionFromProperty = conditionFromProperty,
                                    ConditionFromMethod = conditionFromMethod
                                }
                            });

                            propertyInfo.HasCondition = true;
                            propertyInfo.ConditionFromProperty = conditionFromProperty;
                            propertyInfo.ConditionFromMethod = conditionFromMethod;
                            break;

                        case BinaryerAttributeUtils.BinarySkipAttributeName:
                            var skipBytesInfo = new SkipBytesInfo
                            {
                                Length = GetAttributeIntValue(attributeData, "Length", 0),
                                LengthFromProperty = GetAttributeStringValue(attributeData, "LengthFromProperty"),
                                LengthFromMethod = GetAttributeStringValue(attributeData, "LengthFromMethod")
                            };

                            propertyInfo.AttributeActions.Add(new AttributeAction
                            {
                                Type = AttributeActionType.SkipBytes,
                                Data = skipBytesInfo
                            });

                            propertyInfo.SkipBytesList.Add(skipBytesInfo);
                            break;

                        case BinaryerAttributeUtils.BinaryPropertyAttributeName:
                            propertyInfo.IsBinaryProperty = true;
                            propertyInfo.Length = GetAttributeIntValue(attributeData, "Length", -1);
                            propertyInfo.LengthFromProperty =
                                GetAttributeStringValue(attributeData, "LengthFromProperty");
                            propertyInfo.LengthFromMethod = GetAttributeStringValue(attributeData, "LengthFromMethod");

                            // 验证类型是否支持
                            if (!IsTypeSupported(propertyInfo.TypeName))
                            {
                                ReportUnsupportedTypeError(context, member, propertyInfo.TypeName);
                                hasErrors = true;
                            }

                            propertyInfo.AttributeActions.Add(new AttributeAction
                            {
                                Type = AttributeActionType.BinaryProperty,
                                Data = propertyInfo
                            });
                            break;

                        case BinaryerAttributeUtils.BinaryRepeatAttributeName:
                            propertyInfo.IsRepeat = true;
                            propertyInfo.RepeatCount = GetAttributeIntValue(attributeData, "RepeatCount", -1);
                            propertyInfo.RepeatCountFromProperty =
                                GetAttributeStringValue(attributeData, "RepeatCountFromProperty");
                            propertyInfo.RepeatCountFromMethod =
                                GetAttributeStringValue(attributeData, "RepeatCountFromMethod");
                            propertyInfo.RepeatTillPosition =
                                GetAttributeLongValue(attributeData, "RepeatTillPosition", -1);
                            propertyInfo.RepeatTillPositionFromProperty =
                                GetAttributeStringValue(attributeData, "RepeatTillPositionFromProperty");
                            propertyInfo.RepeatTillPositionFromMethod =
                                GetAttributeStringValue(attributeData, "RepeatTillPositionFromMethod");

                            propertyInfo.AttributeActions.Add(new AttributeAction
                            {
                                Type = AttributeActionType.BinaryRepeat,
                                Data = propertyInfo
                            });
                            break;
                    }
                }
            }

            // 如果有条件，需要在最后添加条件结束
            if (propertyInfo.HasCondition)
            {
                propertyInfo.AttributeActions.Add(new AttributeAction
                {
                    Type = AttributeActionType.BinaryConditionEnd,
                    Data = null
                });
            }

            // 只有标记了 BinaryProperty 或者有其他二进制相关特性的属性才需要处理
            if (propertyInfo.IsBinaryProperty || propertyInfo.IsRepeat || propertyInfo.HasCondition ||
                propertyInfo.SkipBytesList.Count > 0)
            {
                properties.Add(propertyInfo);
            }
        }

        return (properties, hasErrors);
    }

    /// <summary>
    /// 检查类型是否受支持
    /// </summary>
    private bool IsTypeSupported(string typeName)
    {
        // 移除nullable标记和required修饰符
        var cleanTypeName = typeName
            .Replace("?", "")
            .Replace("required ", "")
            .Trim();

        // 检查不支持的类型
        var unsupportedTypes = new[]
        {
            "decimal",
            "System.Decimal"
        };

        if (unsupportedTypes.Contains(cleanTypeName))
        {
            return false;
        }

        // 检查支持的基本类型
        if (IsSimpleType(cleanTypeName))
        {
            return true;
        }

        // 检查字符串类型
        if (cleanTypeName == "string" || cleanTypeName == "System.String")
        {
            return true;
        }

        // 检查字节数组
        if (cleanTypeName == "byte[]" || cleanTypeName == "System.Byte[]")
        {
            return true;
        }

        // 检查集合类型 - 需要验证元素类型
        if (IsCollectionType(cleanTypeName))
        {
            var elementType = GetCollectionElementType(cleanTypeName);
            return IsTypeSupported(elementType);
        }

        // 对于其他复杂类型，假设它们也实现了StreamSerializable（暂时允许）
        return true;
    }

    /// <summary>
    /// 报告不支持的类型错误
    /// </summary>
    private void ReportUnsupportedTypeError(SourceProductionContext context, IPropertySymbol property, string typeName)
    {
        var location = property.Locations.FirstOrDefault();
        if (location != null)
        {
            var diagnostic = Diagnostic.Create(
                new DiagnosticDescriptor(
                    "BINARYER001",
                    "Property Type Not Supported",
                    "属性 '{0}' 的类型 '{1}' 暂时不支持二进制序列化。支持的类型包括：基本数值类型（bool, byte, short, int, long, float, double等）、string、byte[]、以及这些类型的集合。",
                    "BinaryerGenerator",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location,
                property.Name,
                typeName);

            context.ReportDiagnostic(diagnostic);
        }
    }

    private int GetAttributeIntValue(AttributeData attribute, string parameterName, int defaultValue)
    {
        var namedArg = attribute.NamedArguments.FirstOrDefault(x => x.Key == parameterName);
        if (namedArg.Value.Value is int intValue)
            return intValue;
        return defaultValue;
    }

    private long GetAttributeLongValue(AttributeData attribute, string parameterName, long defaultValue)
    {
        var namedArg = attribute.NamedArguments.FirstOrDefault(x => x.Key == parameterName);
        if (namedArg.Value.Value is long longValue)
            return longValue;
        return defaultValue;
    }

    private string? GetAttributeStringValue(AttributeData attribute, string parameterName)
    {
        var namedArg = attribute.NamedArguments.FirstOrDefault(x => x.Key == parameterName);
        return namedArg.Value.Value as string;
    }

    private string GenerateClassCode(string namespaceName, string className, List<PropertyInfo> properties)
    {
        // 计算最大缓冲区大小
        int maxBufferSize = CalculateMaxBufferSize(properties);

        // 生成读取方法
        var readMethod = GenerateReadMethod(properties, maxBufferSize);

        // 生成写入方法
        var writeMethod = GenerateWriteMethod(properties);

        var code = $@"// <auto-generated/>
#pragma warning disable CS8618
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace {namespaceName}
{{
    partial class {className}
    {{
        public int BinaryActualSize {{ get; private set; }}

{readMethod}

{writeMethod}
    }}
}}";

        return code;
    }

    private int CalculateMaxBufferSize(List<PropertyInfo> properties)
    {
        int maxSize = 64; // 默认最小缓冲区大小

        foreach (var prop in properties)
        {
            if (prop.Length > 0)
            {
                maxSize = Math.Max(maxSize, prop.Length);
            }
            else
            {
                // 根据类型推断大小
                var typeSize = GetTypeBinarySize(prop.TypeName);
                if (typeSize > 0)
                {
                    maxSize = Math.Max(maxSize, typeSize);
                }
            }
        }

        return maxSize;
    }

    private int GetTypeBinarySize(string typeName)
    {
        return typeName switch
        {
            "bool" => 1,
            "byte" => 1,
            "sbyte" => 1,
            "short" => 2,
            "ushort" => 2,
            "int" => 4,
            "uint" => 4,
            "long" => 8,
            "ulong" => 8,
            "float" => 4,
            "double" => 8,
            "char" => 2,
            _ => -1 // 未知类型或复杂类型
        };
    }

    private string GenerateReadMethod(List<PropertyInfo> properties, int maxBufferSize)
    {
        var sb = new StringBuilder();

        sb.AppendLine("        public void ReadFromStream(global::System.IO.Stream stream)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var buffer = new byte[{maxBufferSize}];");
        sb.AppendLine("            BinaryActualSize = 0;");
        sb.AppendLine();

        foreach (var prop in properties)
        {
            GeneratePropertyReadCode(sb, prop);
        }

        sb.AppendLine("        }");

        return sb.ToString();
    }

    private void GeneratePropertyReadCode(StringBuilder sb, PropertyInfo prop)
    {
        // 使用新的基于 AttributeActions 的代码生成
        GeneratePropertyReadCodeByActions(sb, prop);
    }

    private void GeneratePropertyReadCodeByActions(StringBuilder sb, PropertyInfo prop)
    {
        var currentIndent = "            ";

        foreach (var action in prop.AttributeActions)
        {
            switch (action.Type)
            {
                case AttributeActionType.BinaryConditionStart:
                    var conditionData = action.Data as ConditionData;
                    var condition = "";
                    if (!string.IsNullOrEmpty(conditionData?.ConditionFromProperty))
                    {
                        condition = conditionData.ConditionFromProperty;
                    }
                    else if (!string.IsNullOrEmpty(conditionData?.ConditionFromMethod))
                    {
                        condition = $"{conditionData.ConditionFromMethod}()";
                    }

                    if (!string.IsNullOrEmpty(condition))
                    {
                        sb.AppendLine($"{currentIndent}if ({condition})");
                        sb.AppendLine($"{currentIndent}{{");
                        currentIndent = "                ";
                    }

                    break;

                case AttributeActionType.SkipBytes:
                    var skipBytesInfo = action.Data as SkipBytesInfo;
                    if (skipBytesInfo != null)
                    {
                        if (skipBytesInfo.Length > 0)
                        {
                            sb.AppendLine(
                                $"{currentIndent}stream.Seek({skipBytesInfo.Length}, global::System.IO.SeekOrigin.Current); // 跳过 {skipBytesInfo.Length} bytes");
                            sb.AppendLine($"{currentIndent}BinaryActualSize += {skipBytesInfo.Length};");
                        }
                        else if (!string.IsNullOrEmpty(skipBytesInfo.LengthFromProperty))
                        {
                            sb.AppendLine(
                                $"{currentIndent}stream.Seek({skipBytesInfo.LengthFromProperty}, global::System.IO.SeekOrigin.Current);");
                            sb.AppendLine($"{currentIndent}BinaryActualSize += {skipBytesInfo.LengthFromProperty};");
                        }
                        else if (!string.IsNullOrEmpty(skipBytesInfo.LengthFromMethod))
                        {
                            sb.AppendLine($"{currentIndent}var skipBytes = {skipBytesInfo.LengthFromMethod}();");
                            sb.AppendLine(
                                $"{currentIndent}stream.Seek(skipBytes, global::System.IO.SeekOrigin.Current);");
                            sb.AppendLine($"{currentIndent}BinaryActualSize += skipBytes;");
                        }
                    }

                    break;

                case AttributeActionType.BinaryProperty:
                    // 判断是否包含重复属性，防止重复生成
                    if (!prop.IsRepeat)
                    {
                        GenerateSinglePropertyReadCodeWithIndent(sb, prop, currentIndent);
                    }

                    break;

                case AttributeActionType.BinaryRepeat:
                    GenerateRepeatReadCodeWithIndent(sb, prop, currentIndent);
                    break;

                case AttributeActionType.BinaryConditionEnd:
                    if (currentIndent == "                ")
                    {
                        currentIndent = "            ";
                        sb.AppendLine($"{currentIndent}}}");
                    }

                    break;
            }
        }

        sb.AppendLine();
    }

    private void GenerateSinglePropertyReadCodeWithIndent(StringBuilder sb, PropertyInfo prop, string indent)
    {
        int length = GetPropertyLength(prop);

        if (IsCollectionType(prop.TypeName))
        {
            if (prop.TypeName.Contains("byte[]"))
            {
                sb.AppendLine($"{indent}stream.ReadExactly(buffer, 0, {length});");
                sb.AppendLine($"{indent}{prop.Name} = new byte[{length}];");
                sb.AppendLine($"{indent}System.Array.Copy(buffer, 0, {prop.Name}, 0, {length});");
                sb.AppendLine($"{indent}BinaryActualSize += {length};");
            }
        }
        else if (prop.TypeName == "string")
        {
            sb.AppendLine($"{indent}stream.ReadExactly(buffer, 0, {length});");
            sb.AppendLine(
                $"{indent}{prop.Name} = global::System.Text.Encoding.UTF8.GetString(buffer, 0, {length}).TrimEnd('\\0');");
            sb.AppendLine($"{indent}BinaryActualSize += {length};");
        }
        else if (IsSimpleType(prop.TypeName))
        {
            int typeSize = GetTypeBinarySize(prop.TypeName);
            if (typeSize > 0)
            {
                var converterMethod = GetConverterMethod(prop.TypeName);
                sb.AppendLine($"{indent}stream.ReadExactly(buffer, 0, {typeSize});");
                sb.AppendLine($"{indent}{prop.Name} = {converterMethod}(buffer[0..{typeSize}], 0);");
                sb.AppendLine($"{indent}BinaryActualSize += {typeSize};");
            }
        }
        else
        {
            // 复杂类型，假设也实现了 StreamSerializable
            if (prop.TypeName.Contains("required"))
            {
                var cleanTypeName = prop.TypeName.Replace("required ", "");
                sb.AppendLine($"{indent}{prop.Name} = new {cleanTypeName}();");
            }

            sb.AppendLine($"{indent}{prop.Name}.ReadFromStream(stream);");
            if (length > 0)
            {
                sb.AppendLine($"{indent}BinaryActualSize += {length};");
                sb.AppendLine(
                    $"{indent}stream.Seek({length} - {prop.Name}.BinaryActualSize, global::System.IO.SeekOrigin.Current);");
            }
            else
            {
                sb.AppendLine($"{indent}BinaryActualSize += {prop.Name}.BinaryActualSize;");
            }
        }
    }

    private void GenerateRepeatReadCodeWithIndent(StringBuilder sb, PropertyInfo prop, string indent)
    {
        // 确定重复次数
        string repeatCountExpression = "";
        if (prop.RepeatCount > 0)
        {
            repeatCountExpression = prop.RepeatCount.ToString();
        }
        else if (!string.IsNullOrEmpty(prop.RepeatCountFromProperty))
        {
            repeatCountExpression = prop.RepeatCountFromProperty!;
        }
        else if (!string.IsNullOrEmpty(prop.RepeatCountFromMethod))
        {
            repeatCountExpression = $"{prop.RepeatCountFromMethod}()";
        }

        string repeatTillPositionExpression = "";
        // 如果没有指定重复次数，查看是否重复读取到指定位置
        if (string.IsNullOrEmpty(repeatCountExpression))
        {
            if (prop.RepeatTillPosition > 0)
            {
                repeatTillPositionExpression = prop.RepeatTillPosition.ToString();
            }
            else if (!string.IsNullOrEmpty(prop.RepeatTillPositionFromProperty))
            {
                repeatTillPositionExpression = prop.RepeatTillPositionFromProperty!;
            }
            else if (!string.IsNullOrEmpty(prop.RepeatTillPositionFromMethod))
            {
                repeatTillPositionExpression = $"{prop.RepeatTillPositionFromMethod}()";
            }
        }

        var useRepeatCount = !string.IsNullOrEmpty(repeatCountExpression);
        var useRepeatTillPosition = !string.IsNullOrEmpty(repeatTillPositionExpression);

        if (useRepeatCount || useRepeatTillPosition)
        {
            // 获取集合元素类型
            var elementType = GetCollectionElementType(prop.TypeName);

            sb.AppendLine($"");
            sb.AppendLine($"{indent}{prop.Name} = new List<{elementType}>();");

            if (useRepeatCount)
            {
                sb.AppendLine($"{indent}for (int i = 0; i < {repeatCountExpression}; i++)");
            }
            else
            {
                sb.AppendLine($"{indent}long {prop.Name}_Gen_RTG = {repeatTillPositionExpression};");
                sb.AppendLine($"{indent}while (stream.Position < {prop.Name}_Gen_RTG )");
            }

            sb.AppendLine($"{indent}{{");

            if (IsSimpleType(elementType) || elementType == "string" || elementType == "byte[]")
            {
                // 简单类型的重复读取
                if (elementType == "string")
                {
                    sb.AppendLine($"{indent}    stream.ReadExactly(buffer, 0, {prop.Length});");
                    sb.AppendLine(
                        $"{indent}    var item = global::System.Text.Encoding.UTF8.GetString(buffer, 0, {prop.Length}).TrimEnd('\\0');");
                    sb.AppendLine($"{indent}    {prop.Name}.Add(item);");
                    sb.AppendLine($"{indent}    BinaryActualSize += {prop.Length};");
                }
                else if (IsSimpleType(elementType))
                {
                    int typeSize = GetTypeBinarySize(elementType);
                    var converterMethod = GetConverterMethod(elementType);
                    sb.AppendLine($"{indent}    stream.ReadExactly(buffer, 0, {typeSize});");
                    sb.AppendLine($"{indent}    var item = {converterMethod}(buffer[0..{typeSize}], 0);");
                    sb.AppendLine($"{indent}    {prop.Name}.Add(item);");
                    sb.AppendLine($"{indent}    BinaryActualSize += {typeSize};");
                }
                else if (elementType == "byte[]")
                {
                    sb.AppendLine($"{indent}    stream.ReadExactly(buffer, 0, {prop.Length});");
                    sb.AppendLine($"{indent}    var item = new byte[{prop.Length}];");
                    sb.AppendLine($"{indent}    System.Array.Copy(buffer, 0, item, 0, {prop.Length});");
                    sb.AppendLine($"{indent}    {prop.Name}.Add(item);");
                    sb.AppendLine($"{indent}    BinaryActualSize += {prop.Length};");
                }
            }
            else
            {
                // 复杂类型的重复读取
                sb.AppendLine($"{indent}    var item = new {elementType}();");
                sb.AppendLine($"{indent}    item.ReadFromStream(stream);");
                sb.AppendLine($"{indent}    {prop.Name}.Add(item);");
                if (prop.Length > 0)
                {
                    sb.AppendLine($"{indent}    BinaryActualSize += {prop.Length};");
                    sb.AppendLine(
                        $"{indent}    stream.Seek({prop.Length} - item.BinaryActualSize, global::System.IO.SeekOrigin.Current);");
                }
                else
                {
                    sb.AppendLine($"{indent}    BinaryActualSize += item.BinaryActualSize;");
                }
            }

            sb.AppendLine($"{indent}}}");
        }
    }

    private int GetPropertyLength(PropertyInfo prop)
    {
        if (prop.Length > 0)
        {
            return prop.Length;
        }

        if (!string.IsNullOrEmpty(prop.LengthFromProperty))
        {
            // 返回一个占位符，实际代码中会用属性值
            return -1;
        }

        if (!string.IsNullOrEmpty(prop.LengthFromMethod))
        {
            // 返回一个占位符，实际代码中会用方法返回值
            return -1;
        }

        // 根据类型自动推断长度
        return GetTypeBinarySize(prop.TypeName);
    }

    private bool IsSimpleType(string typeName)
    {
        return typeName switch
        {
            "bool" or "byte" or "sbyte" or "short" or "ushort" or
                "int" or "uint" or "long" or "ulong" or
                "float" or "double" or "char" => true,
            _ => false
        };
    }

    private bool IsCollectionType(string typeName)
    {
        return typeName.Contains("List<") || typeName.Contains("[]") || typeName.Contains("IEnumerable<");
    }

    private string GetCollectionElementType(string typeName)
    {
        if (typeName.Contains("List<"))
        {
            var start = typeName.IndexOf('<') + 1;
            var end = typeName.LastIndexOf('>');
            return typeName.Substring(start, end - start);
        }

        if (typeName.Contains("[]"))
        {
            return typeName.Replace("[]", "");
        }

        return "object";
    }

    private string GetConverterMethod(string typeName)
    {
        return typeName switch
        {
            "bool" => "global::System.BitConverter.ToBoolean",
            "short" => "global::System.BitConverter.ToInt16",
            "ushort" => "global::System.BitConverter.ToUInt16",
            "int" => "global::System.BitConverter.ToInt32",
            "uint" => "global::System.BitConverter.ToUInt32",
            "long" => "global::System.BitConverter.ToInt64",
            "ulong" => "global::System.BitConverter.ToUInt64",
            "float" => "global::System.BitConverter.ToSingle",
            "double" => "global::System.BitConverter.ToDouble",
            "char" => "global::System.BitConverter.ToChar",
            _ => "throw new NotSupportedException"
        };
    }

    private string GenerateWriteMethod(List<PropertyInfo> properties)
    {
        var sb = new StringBuilder();

        sb.AppendLine("        public void WriteToStream(global::System.IO.Stream stream)");
        sb.AppendLine("        {");

        foreach (var prop in properties)
        {
            GeneratePropertyWriteCode(sb, prop);
        }

        sb.AppendLine("        }");

        return sb.ToString();
    }

    private void GeneratePropertyWriteCode(StringBuilder sb, PropertyInfo prop)
    {
        // 使用新的基于 AttributeActions 的代码生成
        GeneratePropertyWriteCodeByActions(sb, prop);
    }

    private void GeneratePropertyWriteCodeByActions(StringBuilder sb, PropertyInfo prop)
    {
        string currentIndent = "            ";

        foreach (var action in prop.AttributeActions)
        {
            switch (action.Type)
            {
                case AttributeActionType.BinaryConditionStart:
                    var conditionData = action.Data as ConditionData;
                    string condition = "";
                    if (!string.IsNullOrEmpty(conditionData?.ConditionFromProperty))
                    {
                        condition = conditionData.ConditionFromProperty;
                    }
                    else if (!string.IsNullOrEmpty(conditionData?.ConditionFromMethod))
                    {
                        condition = $"{conditionData.ConditionFromMethod}()";
                    }

                    if (!string.IsNullOrEmpty(condition))
                    {
                        sb.AppendLine($"{currentIndent}if ({condition})");
                        sb.AppendLine($"{currentIndent}{{");
                        currentIndent = "                ";
                    }

                    break;

                case AttributeActionType.SkipBytes:
                    var skipBytesInfo = action.Data as SkipBytesInfo;
                    if (skipBytesInfo != null)
                    {
                        if (skipBytesInfo.Length > 0)
                        {
                            sb.AppendLine($"{currentIndent}// 跳过 {skipBytesInfo.Length} bytes");
                            sb.AppendLine(
                                $"{currentIndent}stream.Write(new byte[{skipBytesInfo.Length}], 0, {skipBytesInfo.Length});");
                        }
                        else if (!string.IsNullOrEmpty(skipBytesInfo.LengthFromProperty))
                        {
                            sb.AppendLine(
                                $"{currentIndent}stream.Write(new byte[{skipBytesInfo.LengthFromProperty}], 0, {skipBytesInfo.LengthFromProperty});");
                        }
                        else if (!string.IsNullOrEmpty(skipBytesInfo.LengthFromMethod))
                        {
                            sb.AppendLine($"{currentIndent}var skipBytes = {skipBytesInfo.LengthFromMethod}();");
                            sb.AppendLine($"{currentIndent}stream.Write(new byte[skipBytes], 0, skipBytes);");
                        }
                    }

                    break;

                case AttributeActionType.BinaryProperty:
                    // 判断是否包含重复属性，防止重复生成
                    if (!prop.IsRepeat)
                    {
                        GenerateSinglePropertyWriteCodeWithIndent(sb, prop, currentIndent);
                    }

                    break;

                case AttributeActionType.BinaryRepeat:
                    GenerateRepeatWriteCodeWithIndent(sb, prop, currentIndent);
                    break;

                case AttributeActionType.BinaryConditionEnd:
                    if (currentIndent == "                ")
                    {
                        currentIndent = "            ";
                        sb.AppendLine($"{currentIndent}}}");
                    }

                    break;
            }
        }

        sb.AppendLine();
    }

    private void GenerateSinglePropertyWriteCodeWithIndent(StringBuilder sb, PropertyInfo prop, string indent)
    {
        int length = GetPropertyLength(prop);

        if (IsCollectionType(prop.TypeName))
        {
            if (prop.TypeName.Contains("byte[]"))
            {
                if (length > 0)
                {
                    sb.AppendLine($"{indent}var {prop.Name}Data = new byte[{length}];");
                    sb.AppendLine(
                        $"{indent}System.Array.Copy({prop.Name}, 0, {prop.Name}Data, 0, System.Math.Min({prop.Name}.Length, {length}));");
                    sb.AppendLine($"{indent}stream.Write({prop.Name}Data, 0, {length});");
                }
                else
                {
                    sb.AppendLine($"{indent}stream.Write({prop.Name}, 0, {prop.Name}.Length);");
                }
            }
        }
        else if (prop.TypeName == "string")
        {
            if (length > 0)
            {
                sb.AppendLine(
                    $"{indent}var {prop.Name}StringBytes = global::System.Text.Encoding.UTF8.GetBytes({prop.Name});");
                sb.AppendLine($"{indent}var {prop.Name}Data = new byte[{length}];");
                sb.AppendLine(
                    $"{indent}System.Array.Copy({prop.Name}StringBytes, 0, {prop.Name}Data, 0, System.Math.Min({prop.Name}StringBytes.Length, {length}));");
                sb.AppendLine($"{indent}stream.Write({prop.Name}Data, 0, {length});");
            }
            else
            {
                sb.AppendLine(
                    $"{indent}var {prop.Name}StringBytes = global::System.Text.Encoding.UTF8.GetBytes({prop.Name});");
                sb.AppendLine($"{indent}stream.Write({prop.Name}StringBytes, 0, {prop.Name}StringBytes.Length);");
            }
        }
        else if (IsSimpleType(prop.TypeName))
        {
            int typeSize = GetTypeBinarySize(prop.TypeName);
            if (typeSize > 0)
            {
                var converterMethod = GetWriteConverterMethod(prop.TypeName);
                sb.AppendLine($"{indent}{string.Format(converterMethod, prop.Name)}");
            }
        }
        else
        {
            // 复杂类型
            sb.AppendLine($"{indent}{prop.Name}.WriteToStream(stream);");
            if (length > 0)
            {
                sb.AppendLine($"{indent}// 如果需要填充到固定长度 {length}");
                sb.AppendLine($"{indent}var {prop.Name}PaddingSize = {length} - {prop.Name}.BinaryActualSize;");
                sb.AppendLine($"{indent}if ({prop.Name}PaddingSize > 0)");
                sb.AppendLine(
                    $"{indent}    stream.Write(new byte[{prop.Name}PaddingSize], 0, {prop.Name}PaddingSize);");
            }
        }
    }

    private void GenerateRepeatWriteCodeWithIndent(StringBuilder sb, PropertyInfo prop, string indent)
    {
        string repeatCountExpression = "";
        if (prop.RepeatCount > 0)
        {
            repeatCountExpression = prop.RepeatCount.ToString();
        }
        else if (!string.IsNullOrEmpty(prop.RepeatCountFromProperty))
        {
            repeatCountExpression = prop.RepeatCountFromProperty!;
        }
        else if (!string.IsNullOrEmpty(prop.RepeatCountFromMethod))
        {
            repeatCountExpression = $"{prop.RepeatCountFromMethod}()";
        }

        if (!string.IsNullOrEmpty(repeatCountExpression))
        {
            sb.AppendLine($"{indent}for (int i = 0; i < {prop.Name}.Count; i++)");
            sb.AppendLine($"{indent}{{");

            var elementType = GetCollectionElementType(prop.TypeName);

            if (IsSimpleType(elementType) || elementType == "string" || elementType == "byte[]")
            {
                // 简单类型的重复写入
                if (elementType == "string")
                {
                    if (prop.Length > 0)
                    {
                        sb.AppendLine(
                            $"{indent}    var {prop.Name}ItemStringBytes = global::System.Text.Encoding.UTF8.GetBytes({prop.Name}[i]);");
                        sb.AppendLine($"{indent}    var {prop.Name}ItemData = new byte[{prop.Length}];");
                        sb.AppendLine(
                            $"{indent}    System.Array.Copy({prop.Name}ItemStringBytes, 0, {prop.Name}ItemData, 0, System.Math.Min({prop.Name}ItemStringBytes.Length, {prop.Length}));");
                        sb.AppendLine($"{indent}    stream.Write({prop.Name}ItemData, 0, {prop.Length});");
                    }
                    else
                    {
                        sb.AppendLine(
                            $"{indent}    var {prop.Name}ItemStringBytes = global::System.Text.Encoding.UTF8.GetBytes({prop.Name}[i]);");
                        sb.AppendLine(
                            $"{indent}    stream.Write({prop.Name}ItemStringBytes, 0, {prop.Name}ItemStringBytes.Length);");
                    }
                }
                else if (IsSimpleType(elementType))
                {
                    var converterMethod = GetWriteConverterMethod(elementType);
                    sb.AppendLine($"{indent}    {string.Format(converterMethod, $"{prop.Name}[i]")}");
                }
                else if (elementType == "byte[]")
                {
                    if (prop.Length > 0)
                    {
                        sb.AppendLine($"{indent}    var {prop.Name}ItemData = new byte[{prop.Length}];");
                        sb.AppendLine(
                            $"{indent}    System.Array.Copy({prop.Name}[i], 0, {prop.Name}ItemData, 0, System.Math.Min({prop.Name}[i].Length, {prop.Length}));");
                        sb.AppendLine($"{indent}    stream.Write({prop.Name}ItemData, 0, {prop.Length});");
                    }
                    else
                    {
                        sb.AppendLine($"{indent}    stream.Write({prop.Name}[i], 0, {prop.Name}[i].Length);");
                    }
                }
            }
            else
            {
                // 复杂类型的重复写入
                sb.AppendLine($"{indent}    {prop.Name}[i].WriteToStream(stream);");
                if (prop.Length > 0)
                {
                    sb.AppendLine($"{indent}    // 如果需要填充到固定长度 {prop.Length}");
                    sb.AppendLine($"{indent}    var paddingSize = {prop.Length} - {prop.Name}[i].BinaryActualSize;");
                    sb.AppendLine($"{indent}    if (paddingSize > 0)");
                    sb.AppendLine($"{indent}        stream.Write(new byte[paddingSize], 0, paddingSize);");
                }
            }

            sb.AppendLine($"{indent}}}");
        }
    }

    private string GetWriteConverterMethod(string typeName)
    {
        return typeName switch
        {
            "bool" => "stream.Write(global::System.BitConverter.GetBytes({0}), 0, 1);",
            "short" => "stream.Write(global::System.BitConverter.GetBytes({0}), 0, 2);",
            "ushort" => "stream.Write(global::System.BitConverter.GetBytes({0}), 0, 2);",
            "int" => "stream.Write(global::System.BitConverter.GetBytes({0}), 0, 4);",
            "uint" => "stream.Write(global::System.BitConverter.GetBytes({0}), 0, 4);",
            "long" => "stream.Write(global::System.BitConverter.GetBytes({0}), 0, 8);",
            "ulong" => "stream.Write(global::System.BitConverter.GetBytes({0}), 0, 8);",
            "float" => "stream.Write(global::System.BitConverter.GetBytes({0}), 0, 4);",
            "double" => "stream.Write(global::System.BitConverter.GetBytes({0}), 0, 8);",
            "char" => "stream.Write(global::System.BitConverter.GetBytes({0}), 0, 2);",
            _ => "throw new NotSupportedException();"
        };
    }

    private class PropertyInfo
    {
        public string Name { get; set; } = "";
        public string TypeName { get; set; } = "";

        // 按照属性声明顺序存储的操作列表
        public List<AttributeAction> AttributeActions { get; set; } = new List<AttributeAction>();

        // BinaryProperty
        public bool IsBinaryProperty { get; set; }
        public int Length { get; set; } = -1;
        public string? LengthFromProperty { get; set; }
        public string? LengthFromMethod { get; set; }

        // SkipBytes - 支持多个 skip
        public List<SkipBytesInfo> SkipBytesList { get; set; } = new List<SkipBytesInfo>();

        // BinaryRepeat
        public bool IsRepeat { get; set; }
        public int RepeatCount { get; set; } = -1;
        public string? RepeatCountFromProperty { get; set; }
        public string? RepeatCountFromMethod { get; set; }
        public long RepeatTillPosition { get; set; } = -1;
        public string? RepeatTillPositionFromProperty { get; set; }
        public string? RepeatTillPositionFromMethod { get; set; }

        // BinaryCondition
        public bool HasCondition { get; set; }
        public string? ConditionFromProperty { get; set; }
        public string? ConditionFromMethod { get; set; }
    }

    private class SkipBytesInfo
    {
        public int Length { get; set; }
        public string? LengthFromProperty { get; set; }
        public string? LengthFromMethod { get; set; }
    }

    // 新增：表示属性上的操作
    private class AttributeAction
    {
        public AttributeActionType Type { get; set; }
        public object? Data { get; set; }
    }

    private enum AttributeActionType
    {
        SkipBytes,
        BinaryConditionStart,
        BinaryConditionEnd,
        BinaryProperty,
        BinaryRepeat
    }

    // 新增：专门存储条件数据的类
    private class ConditionData
    {
        public string? ConditionFromProperty { get; set; }
        public string? ConditionFromMethod { get; set; }
    }
}