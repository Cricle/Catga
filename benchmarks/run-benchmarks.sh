#!/bin/bash
# CatCat.Transit 性能基准测试运行脚本

FILTER="*"
QUICK=false
MEMORY=false
EXPORT=false

# 解析参数
while [[ $# -gt 0 ]]; do
    case $1 in
        --filter)
            FILTER="$2"
            shift 2
            ;;
        --quick)
            QUICK=true
            shift
            ;;
        --memory)
            MEMORY=true
            shift
            ;;
        --export)
            EXPORT=true
            shift
            ;;
        *)
            shift
            ;;
    esac
done

echo "==========================================="
echo "  CatCat.Transit 性能基准测试"
echo "==========================================="
echo ""

# 构建参数
ARGS="--filter $FILTER"

if [ "$QUICK" = true ]; then
    echo "⚡ 快速模式 (较少迭代)"
    ARGS="$ARGS --job short"
else
    echo "📊 完整模式 (完整迭代)"
fi

if [ "$MEMORY" = true ]; then
    echo "💾 启用内存诊断"
    ARGS="$ARGS --memory"
fi

if [ "$EXPORT" = true ]; then
    echo "📄 导出 HTML 和 JSON 报告"
    ARGS="$ARGS --exporters html json"
fi

echo ""
echo "🔨 编译 Release 版本..."
dotnet build benchmarks/Catga.Benchmarks -c Release --no-incremental

if [ $? -ne 0 ]; then
    echo ""
    echo "❌ 编译失败!"
    exit 1
fi

echo "✅ 编译成功"
echo ""
echo "🚀 开始运行基准测试..."
echo ""

dotnet run --project benchmarks/Catga.Benchmarks -c Release --no-build -- $ARGS

if [ $? -eq 0 ]; then
    echo ""
    echo "✅ 基准测试完成!"

    if [ "$EXPORT" = true ]; then
        echo ""
        echo "📁 报告位置: benchmarks/Catga.Benchmarks/BenchmarkDotNet.Artifacts/results/"
    fi
else
    echo ""
    echo "❌ 基准测试失败!"
fi

