# Requirements Document

## Introduction

Catga 框架当前实现了自定义的优雅停机（GracefulShutdownCoordinator）和恢复管理（GracefulRecoveryManager）机制。然而，Microsoft.Extensions.Hosting 已经提供了成熟、经过充分测试的生命周期管理功能，包括 IHostedService、IHostApplicationLifetime 和 BackgroundService。本需求旨在重构 Catga 以充分利用 .NET 标准的托管服务基础设施，消除重复代码，提高可维护性和互操作性。

## Glossary

- **Hosting_System**: Microsoft.Extensions.Hosting 提供的应用程序托管基础设施
- **Catga_Framework**: 本项目的 CQRS/消息传递框架
- **Lifecycle_Manager**: 管理应用程序启动、运行和停机的组件
- **Hosted_Service**: 实现 IHostedService 接口的后台服务
- **Recovery_Service**: 负责组件健康检查和自动恢复的服务
- **Shutdown_Coordinator**: 协调优雅停机过程的组件
- **Transport_Service**: 消息传输层服务（NATS、Redis、InMemory）
- **Persistence_Service**: 持久化层服务（EventStore、OutboxStore 等）

## Requirements

### Requirement 1: 集成 Microsoft.Extensions.Hosting

**User Story:** 作为框架开发者，我希望 Catga 完全集成 Microsoft.Extensions.Hosting，以便利用 .NET 标准的生命周期管理机制，减少自定义代码。

#### Acceptance Criteria

1. THE Catga_Framework SHALL 依赖 Microsoft.Extensions.Hosting.Abstractions 包
2. WHEN 应用程序启动时，THE Hosting_System SHALL 管理所有 Catga 服务的生命周期
3. THE Catga_Framework SHALL 使用 IHostApplicationLifetime 来响应应用程序生命周期事件
4. WHERE 需要后台任务时，THE Catga_Framework SHALL 使用 IHostedService 或 BackgroundService
5. THE Catga_Framework SHALL NOT 实现自定义的应用程序生命周期管理机制

### Requirement 2: 重构优雅停机机制

**User Story:** 作为框架开发者，我希望移除 GracefulShutdownCoordinator 的自定义实现，改用 IHostApplicationLifetime，以便与标准 .NET 应用程序保持一致。

#### Acceptance Criteria

1. THE Shutdown_Coordinator SHALL 使用 IHostApplicationLifetime.ApplicationStopping 来检测停机请求
2. WHEN ApplicationStopping 被触发时，THE Catga_Framework SHALL 停止接受新的消息
3. WHEN ApplicationStopping 被触发时，THE Catga_Framework SHALL 等待正在处理的消息完成
4. THE Catga_Framework SHALL 在 StopAsync 方法中实现优雅停机逻辑
5. THE Catga_Framework SHALL 支持配置停机超时时间
6. IF 停机超时时，THEN THE Catga_Framework SHALL 记录警告并强制停止

### Requirement 3: 将恢复管理转换为托管服务

**User Story:** 作为框架开发者，我希望将 GracefulRecoveryManager 重构为 IHostedService，以便它能够自动启动、运行和停止。

#### Acceptance Criteria

1. THE Recovery_Service SHALL 实现 IHostedService 接口
2. WHEN 应用程序启动时，THE Recovery_Service SHALL 在 StartAsync 中初始化
3. WHILE 应用程序运行时，THE Recovery_Service SHALL 定期检查组件健康状态
4. WHEN 检测到不健康组件时，THE Recovery_Service SHALL 尝试恢复
5. WHEN 应用程序停止时，THE Recovery_Service SHALL 在 StopAsync 中清理资源
6. THE Recovery_Service SHALL 使用 CancellationToken 来响应停机请求

### Requirement 4: 传输层服务托管化

**User Story:** 作为框架开发者，我希望所有传输层实现（NATS、Redis、InMemory）都作为托管服务运行，以便它们能够正确地启动和停止。

#### Acceptance Criteria

1. THE Transport_Service SHALL 实现 IHostedService 接口
2. WHEN 应用程序启动时，THE Transport_Service SHALL 在 StartAsync 中建立连接
3. WHEN 应用程序停止时，THE Transport_Service SHALL 在 StopAsync 中关闭连接
4. THE Transport_Service SHALL 使用 IHostApplicationLifetime.ApplicationStopping 来停止接受新消息
5. THE Transport_Service SHALL 等待正在处理的消息完成后再关闭连接
6. IF 连接失败时，THEN THE Transport_Service SHALL 使用 Recovery_Service 进行重连

### Requirement 5: 持久化层服务托管化

**User Story:** 作为框架开发者，我希望持久化层服务（EventStore、OutboxStore 等）作为托管服务运行，以便它们能够正确地初始化和清理资源。

#### Acceptance Criteria

1. WHERE 持久化服务需要初始化时，THE Persistence_Service SHALL 实现 IHostedService 接口
2. WHEN 应用程序启动时，THE Persistence_Service SHALL 在 StartAsync 中初始化连接池和资源
3. WHEN 应用程序停止时，THE Persistence_Service SHALL 在 StopAsync 中释放资源
4. THE Persistence_Service SHALL 支持健康检查接口
5. THE Persistence_Service SHALL 与 Recovery_Service 集成以支持自动恢复

### Requirement 6: Outbox 处理器托管化

**User Story:** 作为框架开发者，我希望 Outbox 处理器作为后台服务运行，以便它能够持续处理待发送的消息。

#### Acceptance Criteria

1. THE Outbox_Processor SHALL 继承 BackgroundService 基类
2. WHEN 应用程序启动时，THE Outbox_Processor SHALL 开始处理 Outbox 消息
3. WHILE 应用程序运行时，THE Outbox_Processor SHALL 定期扫描并发送待处理消息
4. WHEN 应用程序停止时，THE Outbox_Processor SHALL 完成当前批次后停止
5. THE Outbox_Processor SHALL 使用 CancellationToken 来响应停机请求
6. THE Outbox_Processor SHALL 支持配置扫描间隔和批次大小

### Requirement 7: 健康检查集成

**User Story:** 作为运维人员，我希望 Catga 服务集成 ASP.NET Core 健康检查，以便监控系统能够检测服务状态。

#### Acceptance Criteria

1. THE Catga_Framework SHALL 提供 IHealthCheck 实现
2. THE Transport_Service SHALL 报告连接状态
3. THE Persistence_Service SHALL 报告存储状态
4. THE Recovery_Service SHALL 报告恢复状态
5. WHEN 所有组件健康时，THE Health_Check SHALL 返回 Healthy 状态
6. WHEN 任何组件不健康时，THE Health_Check SHALL 返回 Degraded 或 Unhealthy 状态

### Requirement 8: 配置简化

**User Story:** 作为 Catga 用户，我希望服务配置更加简单直观，以便快速上手。

#### Acceptance Criteria

1. THE Catga_Framework SHALL 提供 AddCatga() 扩展方法用于注册所有服务
2. THE Catga_Framework SHALL 自动注册所有必需的托管服务
3. THE Catga_Framework SHALL 支持链式配置 API
4. THE Catga_Framework SHALL 提供合理的默认配置
5. WHERE 用户需要自定义时，THE Catga_Framework SHALL 提供清晰的配置选项

### Requirement 9: 测试支持

**User Story:** 作为测试开发者，我希望能够轻松测试托管服务，以便验证生命周期行为。

#### Acceptance Criteria

1. THE Catga_Framework SHALL 提供测试辅助类用于托管服务测试
2. THE Catga_Framework SHALL 支持在测试中模拟生命周期事件
3. THE Catga_Framework SHALL 提供 WebApplicationFactory 集成测试支持
4. THE Catga_Framework SHALL 文档化托管服务的测试最佳实践
5. THE Catga_Framework SHALL 提供示例测试代码
