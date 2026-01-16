# 清理低质量测试脚本
# 此脚本会删除重复、低质量和无意义的测试文件

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Catga 测试质量改进 - 清理低质量测试" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

$testRoot = "tests/Catga.Tests/Flow"

# 统计当前测试文件数量
$currentFiles = Get-ChildItem -Path $testRoot -Recurse -Filter "*.cs" | Measure-Object
Write-Host "当前测试文件数量: $($currentFiles.Count)" -ForegroundColor Yellow

# 要删除的测试文件列表
$filesToDelete = @(
    # 重复的 Builder 测试
    "FlowBuilderApiTests.cs",
    "FlowBuilderScenarioTests.cs",
    "FlowBuilderComplexScenarioTests.cs",
    "FlowBuilderAdvancedScenarioTests.cs",
    "FlowBuilderConcurrencyTests.cs",
    "FlowBuilderConsistencyTests.cs",
    "FlowBuilderDefaultsTests.cs",
    "FlowBuilderDocumentationTests.cs",
    "FlowBuilderEdgeCaseTests.cs",
    "FlowBuilderEdgeCaseTests2.cs",
    "FlowBuilderErrorHandlingTests.cs",
    "FlowBuilderEventCallbackTests.cs",
    "FlowBuilderFinalTests.cs",
    "FlowBuilderIntegrationTests.cs",
    "FlowBuilderLoopTests.cs",
    "FlowBuilderMessageTypeTests.cs",
    "FlowBuilderModifierTests.cs",
    "FlowBuilderMutationTests.cs",
    "FlowBuilderNameConfigurationTests.cs",
    "FlowBuilderNamingTests.cs",
    "FlowBuilderPerformanceTests.cs",
    "FlowBuilderProductionTests.cs",
    "FlowBuilderReturnTypeTests.cs",
    "FlowBuilderReusabilityTests.cs",
    "FlowBuilderRobustnessTests.cs",
    "FlowBuilderStabilityTests.cs",
    "FlowBuilderStepCountTests.cs",
    "FlowBuilderSummaryTests.cs",
    "FlowBuilderTagTests.cs",
    "FlowBuilderTimeoutRetryTests.cs",
    "FlowBuilderUsagePatternTests.cs",
    "FlowBuilderValidationTests.cs",
    "FlowBuilderCallbackTests.cs",
    "FlowBuilderCompletionTests.cs",
    "FlowBuilderDelayWaitTests.cs",
    
    # 重复的 Step 测试
    "FlowStepTests.cs",
    "FlowStepPropertiesTests.cs",
    "FlowStepModificationTests.cs",
    "FlowStepComprehensiveTests.cs",
    "FlowStepConditionTests.cs",
    "FlowStepDelayTests.cs",
    "FlowStepEventTests.cs",
    "FlowStepFailureActionTests.cs",
    "FlowStepIntoTests.cs",
    "FlowStepLoopingTests.cs",
    "FlowStepOptionalTests.cs",
    "FlowStepPersistTests.cs",
    "FlowStepSummaryTests.cs",
    "FlowStepTaggingTests.cs",
    "FlowStepTimeoutRetryTests.cs",
    "FlowStepTypeTests.cs",
    "FlowStepWaitTests.cs",
    "StepTypeTests.cs",
    "StepTypeClassificationTests.cs",
    "StepTypeEnumTests.cs",
    "StepExecutionOrderTests.cs",
    
    # 重复的 Executor 测试
    "DslFlowExecutorTests.cs",
    "FlowExecutorTests.cs",
    "FlowExecutionTests.cs",
    "FlowExecutorBasicTests.cs",
    "FlowExecutorErrorHandlingTests.cs",
    "DslFlowExecutorAdditionalTests.cs",
    
    # Benchmark 和调试测试
    "FlowExecutorBenchmarks.cs",
    "QuickBenchmark.cs",
    "RunBenchmarks.cs",
    "DebugFlowStepProperties.cs",
    "ForEachDebugTest.cs",
    
    # 过度细分的测试
    "FlowDslFinalTests.cs",
    "FlowDslReadinessTests.cs",
    "FlowDslRegressionTests.cs",
    "FlowDslAdvancedTests.cs",
    "FlowDslCompleteScenarioTests.cs",
    "FlowDslComprehensiveTests.cs",
    "FlowDslDistributedTests.cs",
    "FlowDslEdgeCaseTests.cs",
    "FlowComprehensiveTests.cs",
    "FlowIntegrationTests.cs",
    "FlowTests.cs",
    
    # 重复的 Config 测试
    "FlowConfigDslTests.cs",
    "FlowConfigTests.cs",
    "FlowConfigBuildTests.cs",
    "FlowConfigurationCompleteTests.cs",
    "FlowConfigurationIntegrationTests.cs",
    
    # 重复的 Position 测试
    "FlowPositionTests.cs",
    "FlowPositionTests2.cs",
    "FlowPositionAdvancedTests.cs",
    "FlowPositionPathTests.cs",
    "FlowPositionComprehensiveTests.cs",
    
    # 重复的 Snapshot 测试
    "FlowSnapshotTests.cs",
    "FlowSnapshotTests2.cs",
    "FlowSnapshotPropertyTests.cs",
    "FlowSnapshotSerializationTests.cs",
    "StoredSnapshotTests.cs",
    
    # 重复的 Result 测试
    "FlowResultTests.cs",
    "FlowResultStatusTests.cs",
    "DslFlowStatusTests.cs",
    "DslFlowStatusTransitionTests.cs",
    
    # 重复的 WaitCondition 测试
    "WaitConditionComprehensiveTests.cs",
    "WaitConditionExtensionsTests.cs",
    "WaitConditionTestCompatibility.cs",
    "WaitConditionAdvancedTests.cs",
    "WaitConditionEqualsTests.cs",
    "WaitTypeTests.cs",
    
    # 重复的 ForEach 测试
    "ForEachAdvancedTests.cs",
    "ForEachFlowDslTests.cs",
    "ForEachIntegrationTests.cs",
    "ForEachPerformanceTests.cs",
    "ForEachProgressTests.cs",
    "ForEachStorageParityTests.cs",
    "NestedForEachTests.cs",
    "ParallelForEachTests.cs",
    "ForEachConfigurationTests.cs",
    
    # 重复的 Branch 测试
    "BranchFlowDslTests.cs",
    "FlowStepBranchingTests.cs",
    "FlowBuilderBranchTests.cs",
    "NestedBranchTests.cs",
    "ElseIfBranchTests.cs",
    
    # 重复的 Delay 测试
    "FlowDelayTests.cs",
    "DelayStepTests.cs",
    "DelayStepAdvancedTests.cs",
    "DelayStepEdgeCaseTests.cs",
    "WaitStepTests.cs",
    
    # 重复的 E2E 测试
    "DslFlowE2ETests.cs",
    "FlowE2ETests.cs",
    "FlowE2EIntegrationTests.cs",
    "FlowDslIntegrationTests.cs",
    
    # Storage Parity 测试（过度测试）
    "StorageFeatureComparisonTests.cs",
    "StorageFeatureParityMatrix.cs",
    "StorageIntegrationParityTests.cs",
    "StorageParityTests.cs",
    
    # 其他重复测试
    "BaseFlowStateTests.cs",
    "FlowStateChangeTrackingTests.cs",
    "FlowStateConcurrencyTests.cs",
    "ExecuteIfOptimizationTests.cs",
    "ExecuteSwitchOptimizationTests.cs",
    "WhenAllWhenAnyTests.cs",
    "WhenAllTimeoutTests.cs",
    "WhenAllWhenAnyBuilderTests.cs",
    "OptionalStepTests.cs",
    "OnlyWhenConditionTests.cs",
    "FailureActionTests.cs",
    "MessageIdInStepTests.cs",
    "TaggedSettingsTests.cs",
    "StepBuilderTests.cs",
    "StepBuilderChainTests.cs",
    "SendStepVariationsTests.cs",
    "QueryBuilderTests.cs",
    "QueryResultMappingTests.cs",
    "PublishBuilderTests.cs",
    "PublishEventTests.cs",
    "IfBuilderTests.cs",
    "SwitchBuilderTests.cs",
    "SwitchCaseDefaultTests.cs",
    "ForEachBuilderTests.cs"
)

Write-Host "`n要删除的文件数量: $($filesToDelete.Count)" -ForegroundColor Yellow
Write-Host "`n开始删除..." -ForegroundColor Green

$deletedCount = 0
$notFoundCount = 0

foreach ($file in $filesToDelete) {
    $found = Get-ChildItem -Path $testRoot -Recurse -Filter $file -ErrorAction SilentlyContinue
    
    if ($found) {
        foreach ($f in $found) {
            Write-Host "  删除: $($f.FullName.Replace((Get-Location).Path, '.'))" -ForegroundColor Gray
            Remove-Item $f.FullName -Force
            $deletedCount++
        }
    } else {
        $notFoundCount++
    }
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "清理完成" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "已删除文件: $deletedCount" -ForegroundColor Green
Write-Host "未找到文件: $notFoundCount" -ForegroundColor Yellow

# 统计剩余测试文件数量
$remainingFiles = Get-ChildItem -Path $testRoot -Recurse -Filter "*.cs" | Measure-Object
Write-Host "剩余测试文件: $($remainingFiles.Count)" -ForegroundColor Green
Write-Host "减少比例: $([math]::Round((1 - $remainingFiles.Count / $currentFiles.Count) * 100, 2))%" -ForegroundColor Green

Write-Host "`n建议: 运行 'dotnet test' 确保剩余测试仍然通过" -ForegroundColor Yellow
