# 🧪 测试快速开始

3 个命令，搞定所有测试！

---

## ⚡ 最快方式（2 分钟）

```powershell
# 1. 启动服务（一个终端）
cd examples/OrderSystem.AppHost && dotnet run

# 2. 运行快速测试（另一个终端）
.\quick-test.ps1
```

**预期输出**:
```
⚡ Catga OrderSystem 快速测试

✅ 服务运行中

🧪 测试核心功能...

1️⃣  创建订单... ✅
2️⃣  查询订单... ✅
3️⃣  Debugger API... ✅ (3 个流)
4️⃣  页面访问... ✅
5️⃣  Debugger UI... ✅

─────────────────────────────────
🎉 全部通过! (5/5, 100%)
```

---

## 🔬 完整测试（5 分钟）

```powershell
# 1. 启动服务
cd examples/OrderSystem.AppHost && dotnet run

# 2. 运行全面测试
.\test-ordersystem-full.ps1
```

**测试覆盖**: 28+ 项测试
- ✅ 健康检查
- ✅ 订单 API（成功/失败）
- ✅ Debugger API
- ✅ 所有页面
- ✅ 静态资源
- ✅ Swagger
- ✅ SignalR

---

## 🚀 一键自动化（10 分钟）

```powershell
# 自动构建、启动、测试、清理
.\run-and-test.ps1
```

**流程**:
1. 构建项目
2. 后台启动服务
3. 等待就绪
4. 运行全面测试
5. 询问是否保持运行

---

## 🎯 选择哪个？

| 场景 | 脚本 | 时间 |
|------|------|------|
| 快速验证核心功能 | `quick-test.ps1` | 5 秒 |
| 完整功能验证 | `test-ordersystem-full.ps1` | 15 秒 |
| CI/CD 自动化 | `run-and-test.ps1` | 全流程 |

---

## 💡 常见问题

**Q: 服务未启动怎么办？**
```powershell
# 快速测试会提示
❌ 服务未运行，请先启动服务:
   cd examples/OrderSystem.AppHost && dotnet run
```

**Q: 测试失败怎么办？**
```powershell
# 查看详细错误信息
# 所有脚本都会显示具体失败原因
```

**Q: 如何停止服务？**
```powershell
# 在运行 dotnet run 的终端按 Ctrl+C
```

---

## 📚 更多信息

- 完整文档: [TESTING-GUIDE.md](TESTING-GUIDE.md)
- OrderSystem 示例: [examples/README-ORDERSYSTEM.md](examples/README-ORDERSYSTEM.md)

---

**开始测试，确保质量！** 🚀

