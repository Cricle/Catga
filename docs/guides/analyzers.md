# Catga 分析器完整指南

> 这篇总览页只负责导航。
> 规则说明和触发示例、配置方式和 CI 接入现在拆开维护。

[返回主文档](../README.md) · [源生成器](./source-generator.md)

---

## 先看这里

如果你第一次接入 Catga 分析器，建议按这个顺序读：

1. [分析器规则参考](./analyzers-rules.md)
2. [分析器配置与接入](./analyzers-configuration.md)

---

## 怎么分工

### 你要看规则本身

看：

- [分析器规则参考](./analyzers-rules.md)

这页包含：
- 为什么需要分析器
- 安装方式
- `CATGA001`
- `CATGA002`
- `CAT2004`
- 常见误用与修复
- 完整规则列表

### 你要看如何启用和落地

看：

- [分析器配置与接入](./analyzers-configuration.md)

这页包含：
- `Directory.Build.props` / `.csproj` / `.editorconfig` 配置
- 代码级抑制
- 新项目 / 迁移项目 / CI 场景
- 最佳实践
- 故障排查
- 快速参考

---

## 当前重点规则

如果你只关心当前最重要的接入约束，优先看：

- `CATGA002`：缺少序列化器注册
- `CAT2004`：singleton 依赖 scoped Catga 服务

其中：
- `CATGA002` 主要避免“能编译、启动就炸”的序列化器遗漏
- `CAT2004` 主要避免 `singleton -> scoped` 生命周期误用

---

## 推荐默认做法

- 新项目默认把 `CAT2004` 当成阻断规则
- 先在团队里统一 `MemoryPack` / 自定义序列化器路线
- 在 CI 里跑 `dotnet build /warnaserror`

具体配置请直接看：

- [分析器配置与接入](./analyzers-configuration.md)
