using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
namespace MusicRpc.Utils;
internal static class ObjectMemoryAnalyzer
{
    private static string AnalyzeManagedMemory()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== 托管内存分析 ===");
        sb.AppendLine($"托管内存总量: {GC.GetTotalMemory(false) / 1024 / 1024:F1} MB");
        sb.AppendLine($"LOH大小: {GC.GetTotalMemory(false) / 1024 / 1024:F1} MB");
        sb.AppendLine("\n=== 大对象堆分析 ===");
        AnalyzeLargeObjectHeap(sb);
        sb.AppendLine("\n=== 静态字段分析 ===");
        AnalyzeStaticFields(sb);
        sb.AppendLine("\n=== 已加载类型分析 ===");
        AnalyzeLoadedTypes(sb);
        return sb.ToString();
    }
    private static void AnalyzeLargeObjectHeap(StringBuilder sb)
    {
        try
        {
            sb.AppendLine("注意：.NET不直接暴露LOH中的对象列表");
            sb.AppendLine("建议使用dotnet-counters或PerfView等工具进行详细分析");
            var internedStrings = typeof(string).GetField("Empty", BindingFlags.Static | BindingFlags.Public);
            if (internedStrings != null)
            {
                sb.AppendLine($"字符串池存在");
            }
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            long totalAssemblySize = 0;
            foreach (var assembly in assemblies)
            {
                try
                {
#if DEBUG
                    if (string.IsNullOrEmpty(assembly.Location)) continue;
                    var fileInfo = new FileInfo(assembly.Location);
                    totalAssemblySize += fileInfo.Length;
#endif
                }
                catch
                {
                }
            }
            sb.AppendLine($"程序集文件大小: {totalAssemblySize / 1024 / 1024:F1} MB");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"LOH分析失败: {ex.Message}");
        }
    }
    private static void AnalyzeStaticFields(StringBuilder sb)
    {
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var staticFields = new List<(string TypeName, string FieldName, object Value)>();
            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes();
                    foreach (var type in types)
                    {
                        var fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        foreach (var field in fields)
                        {
                            try
                            {
                                var value = field.GetValue(null);
                                if (value == null) continue;
                                var valueType = value.GetType();
                                if (valueType.IsArray || valueType.Name.Contains("Dictionary") ||
                                    valueType.Name.Contains("List") || valueType.Name.Contains("Cache"))
                                {
                                    staticFields.Add(($"{type.FullName}", field.Name, $"类型: {valueType.Name}"));
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            if (staticFields.Count != 0)
            {
                sb.AppendLine($"发现 {staticFields.Count} 个可能的缓存容器:");
                foreach (var (typeName, fieldName, value) in staticFields.Take(10))
                {
                    sb.AppendLine($"  {typeName}.{fieldName}: {value}");
                }
            }
            else
            {
                sb.AppendLine("未发现明显的静态缓存容器");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"静态字段分析失败: {ex.Message}");
        }
    }
    private static void AnalyzeLoadedTypes(StringBuilder sb)
    {
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var typeCount = 0;
            var problematicTypes = new List<string>();
            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes();
                    typeCount += types.Length;
                    problematicTypes.AddRange(from type in types
                        let typeName = type.Name
                        where typeName.Contains("Cache") || typeName.Contains("Buffer") || typeName.Contains("Pool") ||
                              typeName.Contains("Memory")
                        select $"{assembly.GetName().Name}.{type.FullName}");
                }
                catch
                {
                }
            }
            sb.AppendLine($"已加载类型总数: {typeCount}");
            if (problematicTypes.Count == 0) return;
            sb.AppendLine("\n可能与内存相关的类型:");
            foreach (var type in problematicTypes.Take(20))
            {
                sb.AppendLine($"  {type}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"类型分析失败: {ex.Message}");
        }
    }
    public static void LogMemoryAnalysis()
    {
#if DEBUG
        var analysis = AnalyzeManagedMemory();
        Debug.WriteLine("\n[MEMORY ANALYSIS]");
        Debug.WriteLine(analysis);
#endif
    }
}