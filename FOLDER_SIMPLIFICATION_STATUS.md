# Catga 文件夹精简 - 当前状态

## ✅ 已完成

### Phase 1-3: 文件夹重组完成

**删除的空文件夹** (2个):
- ✅ `src/Catga/Rpc/`
- ✅ `src/Catga/Common/`

**移动的单文件文件夹** (2个):
- ✅ `Mediator/CatgaMediator.cs` → `CatgaMediator.cs` (根目录)
- ✅ `Serialization/Serialization.cs` → `Serialization.cs` (根目录)

**合并的小文件夹** (6个):
- ✅ `Handlers/` → `Core/` + `Abstractions/`
  - `HandlerCache.cs` → `Core/`
  - `HandlerContracts.cs` → `Abstractions/`
- ✅ `Messages/` → `Core/` + `Abstractions/`
  - `MessageExtensions.cs` → `Core/`
  - `MessageContracts.cs` → `Abstractions/`
  - `MessageIdentifiers.cs` → `Abstractions/`
- ✅ `Pooling/` → `Core/`
  - `MemoryPoolManager.cs` → `Core/`
  - `PooledBufferWriter.cs` → `Core/`
- ✅ `Http/` → `DependencyInjection/`
  - `CorrelationIdDelegatingHandler.cs` → `DependencyInjection/`
- ✅ `Mediator/` → 根目录
- ✅ `Serialization/` → 根目录

---

## 📊 精简效果

| 指标 | Before | After | 改进 |
|------|--------|-------|------|
| **源码文件夹** | 14 个 | 6 个 | **-57%** ✅ |
| **单文件文件夹** | 5 个 | 0 个 | **-100%** ✅ |
| **空文件夹** | 2 个 | 0 个 | **-100%** ✅ |
| **核心文件** | 54 个 | 54 个 | ✅ 无变化 |

---

## 🎯 新的文件夹结构

```
src/Catga/
├── Abstractions/          (15 files) ⬆️ +4
│   ├── HandlerContracts.cs      (from Handlers/)
│   ├── MessageContracts.cs      (from Messages/)
│   └── MessageIdentifiers.cs    (from Messages/)
│
├── Core/                  (22 files) ⬆️ +4
│   ├── HandlerCache.cs          (from Handlers/)
│   ├── MessageExtensions.cs     (from Messages/)
│   ├── MemoryPoolManager.cs     (from Pooling/)
│   └── PooledBufferWriter.cs    (from Pooling/)
│
├── DependencyInjection/   (3 files) ⬆️ +1
│   └── CorrelationIdDelegatingHandler.cs (from Http/)
│
├── Observability/         (4 files) ✅ 保留
├── Pipeline/              (1 file + Behaviors/) ✅ 保留
├── Polyfills/             (2 files) ✅ 保留
│
├── CatgaMediator.cs       (from Mediator/) ⬆️ 新增
├── Serialization.cs       (from Serialization/) ⬆️ 新增
└── ...
```

---

## ⚠️ 待完成

### Phase 4: 更新命名空间引用 (进行中)

**状态**: 部分完成，还有编译错误

**已更新的命名空间**:
```csharp
// 移动后的文件
namespace Catga.Mediator;      → namespace Catga;
namespace Catga.Serialization; → namespace Catga;
namespace Catga.Handlers;      → namespace Catga.Core; / Catga.Abstractions;
namespace Catga.Messages;      → namespace Catga.Core; / Catga.Abstractions;
namespace Catga.Pooling;       → namespace Catga.Core;
namespace Catga.Http;          → namespace Catga.DependencyInjection;
```

**需要修复的using指令**:
```csharp
// 需要替换的引用
using Catga.Mediator;       → using Catga;
using Catga.Serialization;  → using Catga;
using Catga.Handlers;       → using Catga.Core;
using Catga.Messages;       → using Catga.Abstractions; (对于接口)
using Catga.Messages;       → using Catga.Core; (对于扩展方法)
using Catga.Pooling;        → using Catga.Core;
using Catga.Http;           → using Catga.DependencyInjection;
```

**当前问题**:
- ❌ 74个编译错误
- 主要是 `Catga.SourceGenerator` 和 `Catga.Tests` 项目缺少 `using Catga.Abstractions;`

**修复策略**:
1. **手动修复关键文件** - 识别最影响的文件
2. **批量替换简单引用** - `using Catga.Handlers;` 等
3. **添加缺失的using** - 在需要接口的地方添加 `using Catga.Abstractions;`
4. **验证编译** - 确保所有项目成功编译
5. **运行测试** - 确保功能没有破坏

---

## 🎯 下一步

**推荐方案A - 简单粗暴**:
```bash
# 在所有需要接口的文件中添加 using Catga.Abstractions;
# 在所有需要核心类的文件中添加 using Catga.Core;
```

**推荐方案B - 精确修复**:
1. 查看每个错误
2. 根据缺失的类型添加相应的using
3. 逐个项目修复
4. 验证编译

**预计时间**: 5-10分钟

---

## 📝 Git提交

**已提交**:
- ✅ Commit: "refactor: Simplify folder structure (WIP - namespace fixes needed)"
- ✅ 文件移动和删除已完成
- ✅ 部分命名空间更新

**待提交**:
- ⏳ 完成所有using指令修复
- ⏳ 验证编译成功
- ⏳ 验证测试通过

---

## 🎨 Philosophy

**Simple > Perfect**
- 文件夹更少 → 更易导航
- 结构更扁平 → 更易理解
- 关注核心 → 删除冗余

**当前进度**: 85% 完成 ✨

