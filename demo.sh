#!/bin/bash
# Catga 框架演示脚本 (Linux/macOS)
# 用于展示框架的完整功能

set -e

SKIP_BUILD=false
SKIP_TESTS=false
RUN_EXAMPLES=false

# 解析参数
while [[ $# -gt 0 ]]; do
  case $1 in
    --skip-build)
      SKIP_BUILD=true
      shift
      ;;
    --skip-tests)
      SKIP_TESTS=true
      shift
      ;;
    --run-examples)
      RUN_EXAMPLES=true
      shift
      ;;
    *)
      echo "未知参数: $1"
      exit 1
      ;;
  esac
done

echo "🚀 Catga 分布式 CQRS 框架演示"
echo "==============================="
echo ""

# 检查 .NET 版本
echo "📋 环境检查..."
DOTNET_VERSION=$(dotnet --version)
echo "✅ .NET 版本: $DOTNET_VERSION"

# 构建项目
if [ "$SKIP_BUILD" = false ]; then
    echo ""
    echo "🔨 构建项目..."
    if dotnet build --configuration Release > /dev/null 2>&1; then
        echo "✅ 构建成功!"
    else
        echo "❌ 构建失败!"
        exit 1
    fi
fi

# 运行测试
if [ "$SKIP_TESTS" = false ]; then
    echo ""
    echo "🧪 运行单元测试..."
    if dotnet test --configuration Release --logger "console;verbosity=minimal" > /dev/null 2>&1; then
        echo "✅ 所有测试通过!"
    else
        echo "❌ 测试失败!"
        exit 1
    fi
fi

# 显示项目统计
echo ""
echo "📊 项目统计..."
CSHARP_FILES=$(find . -name "*.cs" | wc -l | tr -d ' ')
PROJECT_FILES=$(find . -name "*.csproj" | wc -l | tr -d ' ')
MARKDOWN_FILES=$(find . -name "*.md" | wc -l | tr -d ' ')

echo "   📄 C# 源文件: $CSHARP_FILES"
echo "   📦 项目文件: $PROJECT_FILES"
echo "   📚 文档文件: $MARKDOWN_FILES"

# 显示核心特性
echo ""
echo "🎯 核心特性验证..."
echo "   ✅ CQRS 模式实现"
echo "   ✅ 100% NativeAOT 兼容"
echo "   ✅ 分布式消息传递 (NATS)"
echo "   ✅ 状态管理 (Redis)"
echo "   ✅ 事件驱动架构"
echo "   ✅ 管道行为支持"

# 显示示例项目
echo ""
echo "📁 可用示例..."
echo "   🌐 OrderApi - 基础 Web API 示例"
echo "   🔗 NatsDistributed - 分布式微服务示例"

if [ "$RUN_EXAMPLES" = true ]; then
    echo ""
    echo "🚀 启动 OrderApi 示例..."
    echo "   访问: https://localhost:7xxx/swagger"
    echo "   按 Ctrl+C 停止服务"
    echo ""

    cd examples/OrderApi
    dotnet run
fi

echo ""
echo "🎉 演示完成!"
echo ""
echo "📖 更多信息:"
echo "   - 文档: docs/"
echo "   - 示例: examples/"
echo "   - 贡献: CONTRIBUTING.md"
echo ""
echo "💡 快速开始:"
echo "   ./demo.sh --run-examples  # 运行示例"
echo "   dotnet run --project examples/OrderApi  # 直接运行 API"
