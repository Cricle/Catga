#!/bin/bash
# 运行新增TDD测试的便捷脚本
# 用法: ./run-new-tests.sh [选项]

set -e

# 颜色定义
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

echo -e "${BLUE}=====================================${NC}"
echo -e "${BLUE}   Catga TDD 测试运行脚本${NC}"
echo -e "${BLUE}=====================================${NC}"
echo ""

# 默认参数
TEST_FILTER=""
VERBOSITY="normal"
COLLECT_COVERAGE=false
RUN_ALL=true

# 解析命令行参数
while [[ $# -gt 0 ]]; do
  case $1 in
    --circuit-breaker)
      TEST_FILTER="CircuitBreakerTests"
      RUN_ALL=false
      shift
      ;;
    --concurrency)
      TEST_FILTER="ConcurrencyLimiterTests"
      RUN_ALL=false
      shift
      ;;
    --stream)
      TEST_FILTER="StreamProcessingTests"
      RUN_ALL=false
      shift
      ;;
    --correlation)
      TEST_FILTER="CorrelationTrackingTests"
      RUN_ALL=false
      shift
      ;;
    --batch)
      TEST_FILTER="BatchProcessingEdgeCasesTests"
      RUN_ALL=false
      shift
      ;;
    --event-failure)
      TEST_FILTER="EventHandlerFailureTests"
      RUN_ALL=false
      shift
      ;;
    --handler-cache)
      TEST_FILTER="HandlerCachePerformanceTests"
      RUN_ALL=false
      shift
      ;;
    --ecommerce)
      TEST_FILTER="ECommerceOrderFlowTests"
      RUN_ALL=false
      shift
      ;;
    --coverage)
      COLLECT_COVERAGE=true
      shift
      ;;
    --verbose)
      VERBOSITY="detailed"
      shift
      ;;
    --help)
      echo "用法: $0 [选项]"
      echo ""
      echo "选项:"
      echo "  --circuit-breaker   只运行熔断器测试"
      echo "  --concurrency       只运行并发限制器测试"
      echo "  --stream            只运行流式处理测试"
      echo "  --correlation       只运行消息追踪测试"
      echo "  --batch             只运行批处理测试"
      echo "  --event-failure     只运行事件失败测试"
      echo "  --handler-cache     只运行Handler缓存测试"
      echo "  --ecommerce         只运行电商订单测试"
      echo "  --coverage          收集测试覆盖率"
      echo "  --verbose           详细输出"
      echo "  --help              显示此帮助信息"
      echo ""
      echo "示例:"
      echo "  $0                           # 运行所有新增测试"
      echo "  $0 --circuit-breaker         # 只运行熔断器测试"
      echo "  $0 --coverage                # 运行测试并收集覆盖率"
      echo "  $0 --verbose --coverage      # 详细输出并收集覆盖率"
      exit 0
      ;;
    *)
      echo -e "${RED}未知选项: $1${NC}"
      echo "使用 --help 查看帮助"
      exit 1
      ;;
  esac
done

# 切换到项目根目录
cd "$(dirname "$0")/.."

# 构建测试命令
TEST_CMD="dotnet test tests/Catga.Tests/Catga.Tests.csproj"
TEST_CMD="$TEST_CMD --logger \"console;verbosity=$VERBOSITY\""

if [ "$RUN_ALL" = false ]; then
  echo -e "${YELLOW}运行测试: $TEST_FILTER${NC}"
  TEST_CMD="$TEST_CMD --filter \"FullyQualifiedName~$TEST_FILTER\""
else
  echo -e "${YELLOW}运行所有新增测试${NC}"
fi

if [ "$COLLECT_COVERAGE" = true ]; then
  echo -e "${YELLOW}收集测试覆盖率...${NC}"
  TEST_CMD="$TEST_CMD /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura"
fi

echo -e "${BLUE}执行命令: $TEST_CMD${NC}"
echo ""

# 运行测试
eval $TEST_CMD

# 检查退出状态
if [ $? -eq 0 ]; then
  echo ""
  echo -e "${GREEN}=====================================${NC}"
  echo -e "${GREEN}   ✅ 所有测试通过！${NC}"
  echo -e "${GREEN}=====================================${NC}"

  if [ "$COLLECT_COVERAGE" = true ]; then
    echo ""
    echo -e "${YELLOW}覆盖率报告已生成: coverage.cobertura.xml${NC}"
    echo -e "${YELLOW}使用 reportgenerator 生成HTML报告:${NC}"
    echo -e "${BLUE}reportgenerator -reports:coverage.cobertura.xml -targetdir:coveragereport${NC}"
  fi
else
  echo ""
  echo -e "${RED}=====================================${NC}"
  echo -e "${RED}   ❌ 测试失败！${NC}"
  echo -e "${RED}=====================================${NC}"
  exit 1
fi


