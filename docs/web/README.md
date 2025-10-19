# Catga 官方文档网站

这是 Catga 框架的官方文档网站源文件。

## 📁 文件结构

```
docs/web/
├── index.html          # 主页（现代化设计）
└── README.md          # 本文件
```

## 🚀 本地预览

### 方法 1: 使用 Python 简易服务器

```bash
# 在 docs/web 目录下运行
python -m http.server 8000

# 或使用 Python 3
python3 -m http.server 8000

# 访问 http://localhost:8000
```

### 方法 2: 使用 .NET HTTP 服务器

```bash
# 安装 dotnet-serve
dotnet tool install --global dotnet-serve

# 在 docs/web 目录下运行
dotnet serve -o

# 自动打开浏览器访问
```

### 方法 3: 使用 Live Server (VS Code 扩展)

1. 安装 VS Code 扩展：Live Server
2. 右键 `index.html` -> "Open with Live Server"
3. 自动打开浏览器并支持热重载

### 方法 4: 直接在浏览器打开

直接双击 `index.html` 文件，用浏览器打开即可。

## 🎨 设计特性

- ✨ **现代化设计** - 渐变背景、卡片布局、流畅动画
- 📱 **响应式布局** - 完美适配桌面、平板、手机
- 🎯 **简洁优雅** - 清晰的信息层次，易于阅读
- ⚡ **性能优秀** - 纯静态 HTML/CSS，无框架依赖
- 🌈 **品牌一致** - 使用 .NET 紫色主题

## 🔧 自定义

### 修改颜色主题

在 `index.html` 的 `:root` CSS 变量中修改：

```css
:root {
    --primary-color: #512BD4;      /* 主色调 */
    --secondary-color: #6C63FF;    /* 次要颜色 */
    --accent-color: #00D9FF;       /* 强调色 */
    /* ... 更多颜色 */
}
```

### 修改内容

直接编辑 `index.html` 中的 HTML 内容即可。

### 添加新页面

在 `docs/web/` 目录下创建新的 `.html` 文件，并在 `index.html` 的导航中添加链接。

## 📊 数据统计

页面展示的数据（如"< 1μs"、"1M+ ops/s"）来自实际的性能测试结果。如需更新：

1. 运行性能基准测试
2. 更新 `index.html` 中的 Stats Section
3. 确保数据准确真实

## 🌐 部署

### GitHub Pages

```bash
# 1. 在 GitHub 仓库设置中启用 GitHub Pages
# 2. 选择 Source: docs 目录
# 3. 访问 https://your-username.github.io/Catga/web/
```

### Netlify

```bash
# 1. 连接 GitHub 仓库到 Netlify
# 2. Build settings:
#    - Build command: (留空)
#    - Publish directory: docs/web
# 3. 自动部署
```

### Vercel

```bash
# 1. 导入 GitHub 仓库到 Vercel
# 2. Root Directory: docs/web
# 3. 自动部署
```

### 自定义服务器

```bash
# 复制 docs/web 目录到服务器
scp -r docs/web user@server:/var/www/html/catga/

# 配置 Nginx
server {
    listen 80;
    server_name catga.example.com;
    root /var/www/html/catga/web;
    index index.html;
}
```

## 📝 维护建议

1. **保持简洁** - 首页应该简洁明了，详细内容链接到其他文档
2. **定期更新** - 随着框架更新，及时更新文档和示例
3. **性能优化** - 压缩图片，使用 CDN（如果有）
4. **SEO 优化** - 添加合适的 meta 标签和描述
5. **可访问性** - 确保残障人士也能访问（语义化 HTML、alt 文本等）

## 🎯 未来计划

- [ ] 添加搜索功能
- [ ] 添加多语言支持（英文版）
- [ ] 集成 API 文档（DocFX 生成）
- [ ] 添加交互式示例（CodePen/JSFiddle）
- [ ] 添加视频教程
- [ ] 添加博客/更新日志
- [ ] 添加社区论坛链接
- [ ] 添加 Star 数、下载量等实时数据

## 📞 反馈

如有问题或建议，请：
- 提交 [GitHub Issue](https://github.com/your-org/Catga/issues)
- 发送 PR 改进文档
- 在讨论区提问

---

**Catga 官方文档 - 让开发更简单！** ⚡

