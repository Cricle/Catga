#!/bin/bash
# Catga测试结果分析和报告生成工具（Linux/macOS版本）
# 用途: 运行测试、分析结果、生成可视化报告

set -e

# 参数解析
COVERAGE=false
DETAILED=false
OPEN_REPORT=false
FILTER=""
SKIP_INTEGRATION=true
HELP=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --coverage|-c)
            COVERAGE=true
            shift
            ;;
        --detailed|-d)
            DETAILED=true
            shift
            ;;
        --open|-o)
            OPEN_REPORT=true
            shift
            ;;
        --filter|-f)
            FILTER="$2"
            shift 2
            ;;
        --include-integration|-i)
            SKIP_INTEGRATION=false
            shift
            ;;
        --help|-h)
            HELP=true
            shift
            ;;
        *)
            echo "未知选项: $1"
            HELP=true
            shift
            ;;
    esac
done

if [ "$HELP" = true ]; then
    cat << EOF
🧪 Catga测试分析工具

用法: ./analyze-test-results.sh [选项]

选项:
  -c, --coverage             收集代码覆盖率
  -d, --detailed             生成详细报告
  -o, --open                 自动打开HTML报告
  -f, --filter <pattern>     过滤测试（例如: "CircuitBreaker"）
  -i, --include-integration  包含集成测试（默认跳过）
  -h, --help                 显示此帮助信息

示例:
  ./analyze-test-results.sh
  ./analyze-test-results.sh -c -o
  ./analyze-test-results.sh -f "CircuitBreaker" -d
  ./analyze-test-results.sh --coverage --detailed --open

EOF
    exit 0
fi

# 配置
TEST_PROJECT="tests/Catga.Tests/Catga.Tests.csproj"
REPORT_DIR="test-reports"
COVERAGE_DIR="coverage_report"
TIMESTAMP=$(date +"%Y-%m-%d_%H-%M-%S")

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
GRAY='\033[0;90m'
NC='\033[0m' # No Color

# 辅助函数
print_success() { echo -e "${GREEN}✅ $1${NC}"; }
print_info() { echo -e "${CYAN}ℹ️  $1${NC}"; }
print_warning() { echo -e "${YELLOW}⚠️  $1${NC}"; }
print_error() { echo -e "${RED}❌ $1${NC}"; }
print_header() {
    echo ""
    echo -e "${MAGENTA}$(printf '=%.0s' {1..60})${NC}"
    echo -e "${MAGENTA}  $1${NC}"
    echo -e "${MAGENTA}$(printf '=%.0s' {1..60})${NC}"
    echo ""
}

# 创建输出目录
print_info "创建报告目录..."
mkdir -p "$REPORT_DIR"
if [ "$COVERAGE" = true ]; then
    mkdir -p "$COVERAGE_DIR"
fi

print_header "🚀 Catga测试分析工具"

# 构建测试过滤器
TEST_FILTER=""
if [ "$SKIP_INTEGRATION" = true ]; then
    TEST_FILTER="FullyQualifiedName!~Integration"
fi
if [ -n "$FILTER" ]; then
    if [ -n "$TEST_FILTER" ]; then
        TEST_FILTER="${TEST_FILTER}&FullyQualifiedName~${FILTER}"
    else
        TEST_FILTER="FullyQualifiedName~${FILTER}"
    fi
fi

# 步骤1: 编译项目
print_header "📦 步骤1: 编译项目"
print_info "正在编译测试项目..."
if ! dotnet build "$TEST_PROJECT" --configuration Release > /dev/null 2>&1; then
    print_error "编译失败！"
    dotnet build "$TEST_PROJECT" --configuration Release
    exit 1
fi
print_success "编译成功"

# 步骤2: 运行测试
print_header "🧪 步骤2: 运行测试"

TEST_ARGS=(
    "test"
    "$TEST_PROJECT"
    "--no-build"
    "--configuration" "Release"
    "--logger" "trx;LogFileName=test-results-${TIMESTAMP}.trx"
    "--logger" "console;verbosity=minimal"
)

if [ -n "$TEST_FILTER" ]; then
    print_info "应用过滤器: $TEST_FILTER"
    TEST_ARGS+=("--filter" "$TEST_FILTER")
fi

if [ "$COVERAGE" = true ]; then
    print_info "启用代码覆盖率收集..."
    TEST_ARGS+=(
        "/p:CollectCoverage=true"
        "/p:CoverletOutputFormat=opencover"
        "/p:CoverletOutput=${COVERAGE_DIR}/opencover.xml"
    )
fi

print_info "运行测试..."
TEST_OUTPUT=$(dotnet "${TEST_ARGS[@]}" 2>&1 | tee /dev/tty)
TEST_EXIT_CODE=$?

# 解析测试结果
print_header "📊 步骤3: 分析结果"

TOTAL_TESTS=0
PASSED=0
FAILED=0
SKIPPED=0

# 从输出中提取统计
if echo "$TEST_OUTPUT" | grep -q "总计:"; then
    TOTAL_TESTS=$(echo "$TEST_OUTPUT" | grep "总计:" | sed -E 's/.*总计:\s*([0-9]+).*/\1/')
    FAILED=$(echo "$TEST_OUTPUT" | grep "总计:" | sed -E 's/.*失败:\s*([0-9]+).*/\1/')
    PASSED=$(echo "$TEST_OUTPUT" | grep "总计:" | sed -E 's/.*成功:\s*([0-9]+).*/\1/')
    SKIPPED=$(echo "$TEST_OUTPUT" | grep "总计:" | sed -E 's/.*已跳过:\s*([0-9]+).*/\1/')
fi

# 计算通过率
if [ "$TOTAL_TESTS" -gt 0 ]; then
    PASS_RATE=$(echo "scale=1; ($PASSED * 100) / $TOTAL_TESTS" | bc)
else
    PASS_RATE=0
fi

# 显示结果摘要
echo ""
echo -e "${CYAN}$(printf '═%.0s' {1..60})${NC}"
echo -e "${CYAN}                   测试结果摘要                     ${NC}"
echo -e "${CYAN}$(printf '═%.0s' {1..60})${NC}"
echo -e "  总测试数:  ${NC}$TOTAL_TESTS${NC}"
echo -e "  ${GREEN}✅ 通过:   ${NC}$PASSED${NC}"
if [ "$FAILED" -gt 0 ]; then
    echo -e "  ${RED}❌ 失败:   ${NC}$FAILED${NC}"
else
    echo -e "  ${GRAY}❌ 失败:   ${NC}$FAILED${NC}"
fi
echo -e "  ${YELLOW}⏭️  跳过:   ${NC}$SKIPPED${NC}"

if [ "${PASS_RATE%.*}" -ge 95 ]; then
    RATE_COLOR=$GREEN
elif [ "${PASS_RATE%.*}" -ge 80 ]; then
    RATE_COLOR=$YELLOW
else
    RATE_COLOR=$RED
fi
echo -e "  📊 通过率: ${RATE_COLOR}$PASS_RATE%${NC}"
echo -e "${CYAN}$(printf '═%.0s' {1..60})${NC}"

# 生成进度条
BAR_LENGTH=50
PASSED_BARS=$(echo "scale=0; ($PASSED * $BAR_LENGTH) / $TOTAL_TESTS" | bc)
FAILED_BARS=$(echo "scale=0; ($FAILED * $BAR_LENGTH) / $TOTAL_TESTS" | bc)
SKIPPED_BARS=$((BAR_LENGTH - PASSED_BARS - FAILED_BARS))

echo ""
echo -n "  "
for ((i=0; i<PASSED_BARS; i++)); do echo -en "${GREEN}█${NC}"; done
for ((i=0; i<FAILED_BARS; i++)); do echo -en "${RED}█${NC}"; done
for ((i=0; i<SKIPPED_BARS; i++)); do echo -en "${GRAY}░${NC}"; done
echo ""
echo ""

# 步骤4: 生成详细报告
if [ "$DETAILED" = true ]; then
    print_header "📝 步骤4: 生成详细报告"

    REPORT_PATH="$REPORT_DIR/test-report-$TIMESTAMP.html"

    # 计算百分比
    PASSED_PERCENT=$(echo "scale=2; ($PASSED * 100) / $TOTAL_TESTS" | bc)
    FAILED_PERCENT=$(echo "scale=2; ($FAILED * 100) / $TOTAL_TESTS" | bc)
    SKIPPED_PERCENT=$(echo "scale=2; ($SKIPPED * 100) / $TOTAL_TESTS" | bc)

    # 质量评估
    if [ "$FAILED" -eq 0 ]; then
        QUALITY_BADGE='<span class="badge badge-success">优秀 ⭐⭐⭐⭐⭐</span>'
        RECOMMENDATION="保持当前质量，继续维护"
    elif [ "${PASS_RATE%.*}" -ge 95 ]; then
        QUALITY_BADGE='<span class="badge badge-success">良好 ⭐⭐⭐⭐</span>'
        RECOMMENDATION="修复少量失败测试"
    elif [ "${PASS_RATE%.*}" -ge 90 ]; then
        QUALITY_BADGE='<span class="badge badge-warning">合格 ⭐⭐⭐</span>'
        RECOMMENDATION="优先修复关键测试"
    else
        QUALITY_BADGE='<span class="badge badge-danger">需改进 ⭐⭐</span>'
        RECOMMENDATION="全面审查测试和代码"
    fi

    # 覆盖率部分
    COVERAGE_SECTION=""
    if [ "$COVERAGE" = true ]; then
        COVERAGE_SECTION='
            <h2 class="section-title">📈 代码覆盖率</h2>
            <div class="info-item">
                <strong>覆盖率报告</strong>
                已生成在: '"$COVERAGE_DIR"'/opencover.xml<br>
                运行 <code>reportgenerator</code> 查看详细HTML报告
            </div>'
    fi

    cat > "$REPORT_PATH" << EOF
<!DOCTYPE html>
<html lang="zh-CN">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Catga测试报告 - $TIMESTAMP</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            padding: 20px;
            min-height: 100vh;
        }
        .container {
            max-width: 1200px;
            margin: 0 auto;
            background: white;
            border-radius: 15px;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
            overflow: hidden;
        }
        .header {
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 40px;
            text-align: center;
        }
        .header h1 { font-size: 2.5em; margin-bottom: 10px; }
        .header .subtitle { font-size: 1.2em; opacity: 0.9; }
        .summary {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 20px;
            padding: 40px;
            background: #f8f9fa;
        }
        .stat-card {
            background: white;
            padding: 25px;
            border-radius: 10px;
            box-shadow: 0 4px 6px rgba(0,0,0,0.1);
            text-align: center;
            transition: transform 0.3s;
        }
        .stat-card:hover { transform: translateY(-5px); }
        .stat-card .number { font-size: 3em; font-weight: bold; margin: 10px 0; }
        .stat-card .label { color: #666; font-size: 1.1em; }
        .stat-card.total .number { color: #667eea; }
        .stat-card.passed .number { color: #28a745; }
        .stat-card.failed .number { color: #dc3545; }
        .stat-card.skipped .number { color: #ffc107; }
        .progress-bar {
            margin: 40px;
            background: #e9ecef;
            height: 40px;
            border-radius: 20px;
            overflow: hidden;
            box-shadow: inset 0 2px 4px rgba(0,0,0,0.1);
        }
        .progress-fill { height: 100%; display: flex; }
        .progress-passed { background: #28a745; }
        .progress-failed { background: #dc3545; }
        .progress-skipped { background: #ffc107; }
        .details { padding: 40px; }
        .section-title {
            font-size: 1.8em;
            color: #333;
            margin-bottom: 20px;
            padding-bottom: 10px;
            border-bottom: 3px solid #667eea;
        }
        .info-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
            gap: 20px;
            margin-bottom: 30px;
        }
        .info-item {
            background: #f8f9fa;
            padding: 15px;
            border-radius: 8px;
            border-left: 4px solid #667eea;
        }
        .info-item strong {
            color: #667eea;
            display: block;
            margin-bottom: 5px;
        }
        .footer {
            background: #2c3e50;
            color: white;
            padding: 30px;
            text-align: center;
        }
        .badge {
            display: inline-block;
            padding: 5px 15px;
            border-radius: 20px;
            font-size: 0.9em;
            font-weight: bold;
            margin: 5px;
        }
        .badge-success { background: #28a745; color: white; }
        .badge-danger { background: #dc3545; color: white; }
        .badge-warning { background: #ffc107; color: #000; }
        .badge-info { background: #17a2b8; color: white; }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>🧪 Catga测试报告</h1>
            <div class="subtitle">$TIMESTAMP</div>
        </div>

        <div class="summary">
            <div class="stat-card total">
                <div class="label">总测试数</div>
                <div class="number">$TOTAL_TESTS</div>
            </div>
            <div class="stat-card passed">
                <div class="label">✅ 通过</div>
                <div class="number">$PASSED</div>
            </div>
            <div class="stat-card failed">
                <div class="label">❌ 失败</div>
                <div class="number">$FAILED</div>
            </div>
            <div class="stat-card skipped">
                <div class="label">⏭️ 跳过</div>
                <div class="number">$SKIPPED</div>
            </div>
        </div>

        <div class="progress-bar">
            <div class="progress-fill">
                <div class="progress-passed" style="width: ${PASSED_PERCENT}%"></div>
                <div class="progress-failed" style="width: ${FAILED_PERCENT}%"></div>
                <div class="progress-skipped" style="width: ${SKIPPED_PERCENT}%"></div>
            </div>
        </div>

        <div class="details">
            <h2 class="section-title">📊 测试详情</h2>

            <div class="info-grid">
                <div class="info-item">
                    <strong>通过率</strong>
                    <span class="badge badge-success">$PASS_RATE%</span>
                </div>
                <div class="info-item">
                    <strong>测试项目</strong>
                    Catga.Tests
                </div>
                <div class="info-item">
                    <strong>配置</strong>
                    Release
                </div>
                <div class="info-item">
                    <strong>过滤器</strong>
                    ${TEST_FILTER:-无}
                </div>
            </div>

            <h2 class="section-title">🎯 质量评估</h2>

            <div class="info-grid">
                <div class="info-item">
                    <strong>整体状态</strong>
                    $QUALITY_BADGE
                </div>
                <div class="info-item">
                    <strong>推荐行动</strong>
                    $RECOMMENDATION
                </div>
            </div>

            $COVERAGE_SECTION
        </div>

        <div class="footer">
            <p>Catga TDD测试套件 | 生成时间: $TIMESTAMP</p>
            <p>查看详细日志: test-reports/test-results-$TIMESTAMP.trx</p>
        </div>
    </div>
</body>
</html>
EOF

    print_success "HTML报告已生成: $REPORT_PATH"

    if [ "$OPEN_REPORT" = true ]; then
        print_info "打开报告..."
        if command -v xdg-open &> /dev/null; then
            xdg-open "$REPORT_PATH"
        elif command -v open &> /dev/null; then
            open "$REPORT_PATH"
        else
            print_warning "无法自动打开浏览器，请手动打开: $REPORT_PATH"
        fi
    fi
fi

# 步骤5: 覆盖率报告
if [ "$COVERAGE" = true ]; then
    print_header "📈 步骤5: 生成覆盖率报告"

    COVERAGE_FILE="$COVERAGE_DIR/opencover.xml"
    if [ -f "$COVERAGE_FILE" ]; then
        print_success "覆盖率数据已生成: $COVERAGE_FILE"

        # 检查是否安装了reportgenerator
        if command -v reportgenerator &> /dev/null; then
            print_info "使用ReportGenerator生成HTML报告..."
            reportgenerator \
                -reports:"$COVERAGE_FILE" \
                -targetdir:"$COVERAGE_DIR/html" \
                -reporttypes:Html

            print_success "覆盖率HTML报告: $COVERAGE_DIR/html/index.htm"

            if [ "$OPEN_REPORT" = true ]; then
                if command -v xdg-open &> /dev/null; then
                    xdg-open "$COVERAGE_DIR/html/index.htm"
                elif command -v open &> /dev/null; then
                    open "$COVERAGE_DIR/html/index.htm"
                fi
            fi
        else
            print_warning "未找到ReportGenerator工具"
            print_info "安装命令: dotnet tool install -g dotnet-reportgenerator-globaltool"
        fi
    else
        print_warning "未找到覆盖率文件"
    fi
fi

# 最终总结
print_header "🎉 完成"

if [ "$TEST_EXIT_CODE" -eq 0 ]; then
    print_success "所有测试通过！"
else
    print_warning "存在失败的测试"
    print_info "查看详细信息: $REPORT_DIR/"
    if [ "$FAILED" -gt 0 ]; then
        print_info "修复指南: tests/FIX_FAILING_TESTS_GUIDE.md"
    fi
fi

echo -e "\n${CYAN}生成的文件:${NC}"
echo -e "  ${GRAY}📄 测试结果:    $REPORT_DIR/test-results-$TIMESTAMP.trx${NC}"
if [ "$DETAILED" = true ]; then
    echo -e "  ${GRAY}📊 HTML报告:    $REPORT_DIR/test-report-$TIMESTAMP.html${NC}"
fi
if [ "$COVERAGE" = true ]; then
    echo -e "  ${GRAY}📈 覆盖率数据:  $COVERAGE_DIR/opencover.xml${NC}"
    if [ -f "$COVERAGE_DIR/html/index.htm" ]; then
        echo -e "  ${GRAY}📈 覆盖率报告:  $COVERAGE_DIR/html/index.htm${NC}"
    fi
fi

echo ""
exit $TEST_EXIT_CODE


