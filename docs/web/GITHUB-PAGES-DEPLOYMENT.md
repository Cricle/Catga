# Catga 官方文档网站 - GitHub Pages 部署计划

**制定日期**: 2025-10-19  
**目标**: 将 Catga 官方文档网站部署到 GitHub Pages，实现自动化 CI/CD

---

## 📋 部署方案概览

### 方案选择

我们选择 **GitHub Pages + GitHub Actions** 方案，原因：
- ✅ 完全免费
- ✅ 自动 HTTPS
- ✅ 全球 CDN 加速
- ✅ 自动化部署（CI/CD）
- ✅ 支持自定义域名
- ✅ 与 GitHub 深度集成

---

## 🎯 Phase 1: 基础配置 (30 分钟)

### 1.1 调整目录结构

**当前结构**:
```
docs/
├── articles/          # Markdown 文档
├── api/              # API 文档（DocFX 生成）
└── web/              # 官方网站
    ├── index.html
    ├── style.css
    ├── app.js
    └── ...
```

**目标结构**（GitHub Pages 友好）:
```
docs/
├── index.html        # 重定向到 web/ 或直接作为主页
├── web/              # 官方网站（保持不变）
├── articles/         # Markdown 文档
└── api/              # API 文档
```

**实现步骤**:

```bash
# 选项 A: 创建根目录重定向
cat > docs/index.html << 'EOF'
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <meta http-equiv="refresh" content="0;url=web/">
    <title>Redirecting to Catga Official Website...</title>
</head>
<body>
    <p>Redirecting to <a href="web/">Catga Official Website</a>...</p>
</body>
</html>
EOF

# 选项 B: 将 web/ 内容移到 docs/ 根目录（推荐）
# 这样访问 https://your-org.github.io/Catga/ 直接显示主页
```

**推荐**: 选项 B（将 web/ 内容提升到 docs/ 根目录）

---

### 1.2 创建 GitHub Pages 配置文件

**创建 `docs/_config.yml`** (Jekyll 配置):

```yaml
# Catga 官方文档网站配置
title: Catga
description: 现代化、高性能的 .NET CQRS/Event Sourcing 框架
baseurl: "" # 如果是项目站点，填 "/Catga"
url: "https://your-org.github.io" # 你的 GitHub Pages 域名

# 禁用 Jekyll 处理某些文件
include:
  - _config.yml
  - .nojekyll

exclude:
  - README.md
  - OPTIMIZATION-PLAN.md
  - GITHUB-PAGES-DEPLOYMENT.md
  - node_modules/
  - package.json

# 禁用 Jekyll 主题（使用纯静态 HTML）
theme: null
```

**创建 `docs/.nojekyll`** (禁用 Jekyll):

```bash
# 告诉 GitHub Pages 不要用 Jekyll 处理
touch docs/.nojekyll
```

---

### 1.3 配置 CNAME（自定义域名，可选）

如果你有自定义域名（如 `catga.dev`）:

**创建 `docs/CNAME`**:
```
catga.dev
```

**DNS 配置**:
```
# A 记录（指向 GitHub Pages IP）
185.199.108.153
185.199.109.153
185.199.110.153
185.199.111.153

# 或 CNAME 记录（推荐）
your-org.github.io
```

---

## 🚀 Phase 2: GitHub Actions 自动化部署 (1 小时)

### 2.1 创建 GitHub Actions 工作流

**创建 `.github/workflows/deploy-docs.yml`**:

```yaml
name: Deploy Documentation to GitHub Pages

on:
  push:
    branches:
      - master
      - main
    paths:
      - 'docs/**'
      - '.github/workflows/deploy-docs.yml'
  workflow_dispatch: # 允许手动触发

permissions:
  contents: read
  pages: write
  id-token: write

concurrency:
  group: "pages"
  cancel-in-progress: false

jobs:
  # Job 1: 构建文档
  build:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup Pages
        uses: actions/configure-pages@v4

      - name: Build with Jekyll
        uses: actions/jekyll-build-pages@v1
        with:
          source: ./docs
          destination: ./_site

      - name: Upload artifact
        uses: actions/upload-pages-artifact@v3

  # Job 2: 部署到 GitHub Pages
  deploy:
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    runs-on: ubuntu-latest
    needs: build
    steps:
      - name: Deploy to GitHub Pages
        id: deployment
        uses: actions/deploy-pages@v4
```

---

### 2.2 创建 DocFX API 文档构建工作流（可选）

**创建 `.github/workflows/build-api-docs.yml`**:

```yaml
name: Build API Documentation

on:
  push:
    branches:
      - master
      - main
    paths:
      - 'src/**/*.cs'
      - 'docfx.json'
  workflow_dispatch:

jobs:
  build-api-docs:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Install DocFX
        run: dotnet tool install -g docfx

      - name: Build API Documentation
        run: docfx docfx.json

      - name: Commit generated docs
        run: |
          git config --local user.email "github-actions[bot]@users.noreply.github.com"
          git config --local user.name "github-actions[bot]"
          git add docs/api/
          git diff --quiet && git diff --staged --quiet || git commit -m "docs: Update API documentation [skip ci]"
          git push
```

---

### 2.3 添加构建状态徽章

在 `README.md` 中添加：

```markdown
[![Deploy Docs](https://github.com/your-org/Catga/actions/workflows/deploy-docs.yml/badge.svg)](https://github.com/your-org/Catga/actions/workflows/deploy-docs.yml)
[![Pages](https://img.shields.io/badge/docs-GitHub%20Pages-blue)](https://your-org.github.io/Catga/)
```

---

## 📊 Phase 3: 集成 Google Analytics (30 分钟)

### 3.1 创建 Google Analytics 4 账号

1. 访问 [Google Analytics](https://analytics.google.com/)
2. 创建新的 GA4 属性
3. 获取 Measurement ID（格式: `G-XXXXXXXXXX`）

---

### 3.2 添加 GA4 到网站

**修改 `docs/web/index.html`**（或所有 HTML 文件）:

```html
<head>
    <!-- ... 其他 meta 标签 ... -->
    
    <!-- Google tag (gtag.js) -->
    <script async src="https://www.googletagmanager.com/gtag/js?id=G-XXXXXXXXXX"></script>
    <script>
      window.dataLayer = window.dataLayer || [];
      function gtag(){dataLayer.push(arguments);}
      gtag('js', new Date());
      gtag('config', 'G-XXXXXXXXXX', {
        'page_title': document.title,
        'page_location': window.location.href,
        'page_path': window.location.pathname
      });
    </script>
</head>
```

---

### 3.3 使用环境变量保护 GA ID（推荐）

**方案 A: 使用 GitHub Actions 替换**

```yaml
# 在 deploy-docs.yml 中添加
- name: Replace GA ID
  run: |
    sed -i 's/G-XXXXXXXXXX/${{ secrets.GA_MEASUREMENT_ID }}/g' docs/**/*.html
  env:
    GA_MEASUREMENT_ID: ${{ secrets.GA_MEASUREMENT_ID }}
```

**方案 B: 使用 JavaScript 动态加载**

```html
<!-- index.html -->
<script>
  // 从环境变量或配置文件读取
  const GA_ID = 'G-XXXXXXXXXX'; // 或从 config.js 读取
  
  // 动态加载 GA
  const script = document.createElement('script');
  script.src = `https://www.googletagmanager.com/gtag/js?id=${GA_ID}`;
  script.async = true;
  document.head.appendChild(script);
  
  window.dataLayer = window.dataLayer || [];
  function gtag(){dataLayer.push(arguments);}
  gtag('js', new Date());
  gtag('config', GA_ID);
</script>
```

---

## 🔍 Phase 4: SEO 优化 (1 小时)

### 4.1 添加 robots.txt

**创建 `docs/robots.txt`**:

```txt
User-agent: *
Allow: /

# Sitemap
Sitemap: https://your-org.github.io/Catga/sitemap.xml

# 不索引的路径（如果有）
Disallow: /api/search/
Disallow: /_site/
```

---

### 4.2 生成 sitemap.xml

**创建 `docs/sitemap.xml`**:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <!-- 主页 -->
  <url>
    <loc>https://your-org.github.io/Catga/</loc>
    <lastmod>2025-10-19</lastmod>
    <changefreq>weekly</changefreq>
    <priority>1.0</priority>
  </url>
  
  <!-- 快速开始 -->
  <url>
    <loc>https://your-org.github.io/Catga/articles/getting-started.html</loc>
    <lastmod>2025-10-19</lastmod>
    <changefreq>monthly</changefreq>
    <priority>0.9</priority>
  </url>
  
  <!-- 架构设计 -->
  <url>
    <loc>https://your-org.github.io/Catga/articles/architecture.html</loc>
    <lastmod>2025-10-19</lastmod>
    <changefreq>monthly</changefreq>
    <priority>0.8</priority>
  </url>
  
  <!-- 更多页面... -->
</urlset>
```

**自动生成 sitemap 脚本** (可选):

```javascript
// scripts/generate-sitemap.js
const fs = require('fs');
const path = require('path');

const baseUrl = 'https://your-org.github.io/Catga';
const docsDir = path.join(__dirname, '../docs');

// 递归查找所有 HTML 文件
function findHtmlFiles(dir, files = []) {
  const items = fs.readdirSync(dir);
  items.forEach(item => {
    const fullPath = path.join(dir, item);
    if (fs.statSync(fullPath).isDirectory()) {
      findHtmlFiles(fullPath, files);
    } else if (item.endsWith('.html')) {
      files.push(fullPath);
    }
  });
  return files;
}

// 生成 sitemap
const files = findHtmlFiles(docsDir);
const urls = files.map(file => {
  const relativePath = path.relative(docsDir, file);
  const url = `${baseUrl}/${relativePath.replace(/\\/g, '/')}`;
  const stat = fs.statSync(file);
  return {
    loc: url,
    lastmod: stat.mtime.toISOString().split('T')[0],
    changefreq: 'monthly',
    priority: file.includes('index.html') ? '1.0' : '0.8'
  };
});

const sitemap = `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${urls.map(u => `  <url>
    <loc>${u.loc}</loc>
    <lastmod>${u.lastmod}</lastmod>
    <changefreq>${u.changefreq}</changefreq>
    <priority>${u.priority}</priority>
  </url>`).join('\n')}
</urlset>`;

fs.writeFileSync(path.join(docsDir, 'sitemap.xml'), sitemap);
console.log('✅ Sitemap generated!');
```

**在 GitHub Actions 中自动生成**:

```yaml
# 在 deploy-docs.yml 中添加
- name: Generate Sitemap
  run: node scripts/generate-sitemap.js
```

---

### 4.3 添加 Open Graph 和 Twitter Card

**修改所有 HTML 文件的 `<head>` 部分**:

```html
<!-- Open Graph -->
<meta property="og:site_name" content="Catga">
<meta property="og:type" content="website">
<meta property="og:title" content="Catga - 现代化 .NET CQRS 框架">
<meta property="og:description" content="高性能、支持 Native AOT 的 .NET CQRS/Event Sourcing 框架">
<meta property="og:url" content="https://your-org.github.io/Catga/">
<meta property="og:image" content="https://your-org.github.io/Catga/og-image.png">
<meta property="og:image:width" content="1200">
<meta property="og:image:height" content="630">

<!-- Twitter Card -->
<meta name="twitter:card" content="summary_large_image">
<meta name="twitter:title" content="Catga - 现代化 .NET CQRS 框架">
<meta name="twitter:description" content="高性能、支持 Native AOT">
<meta name="twitter:image" content="https://your-org.github.io/Catga/twitter-card.png">
<meta name="twitter:site" content="@YourTwitter">
```

**创建社交分享图片**:
- `og-image.png`: 1200x630px（Open Graph）
- `twitter-card.png`: 1200x600px（Twitter）

---

### 4.4 提交到 Google Search Console

1. 访问 [Google Search Console](https://search.google.com/search-console)
2. 添加属性：`https://your-org.github.io/Catga/`
3. 验证所有权（HTML 文件验证或 DNS 验证）
4. 提交 sitemap: `https://your-org.github.io/Catga/sitemap.xml`

---

## 📱 Phase 5: 性能优化 (1 小时)

### 5.1 启用 GitHub Pages 压缩

GitHub Pages 自动启用 gzip 压缩，无需额外配置 ✅

---

### 5.2 优化资源加载

**添加资源预加载**:

```html
<head>
    <!-- 预连接到 Google Analytics -->
    <link rel="preconnect" href="https://www.googletagmanager.com">
    <link rel="dns-prefetch" href="https://www.googletagmanager.com">
    
    <!-- 预加载关键 CSS -->
    <link rel="preload" href="style.css" as="style">
    
    <!-- 预加载关键字体（如果有） -->
    <link rel="preload" href="fonts/main.woff2" as="font" type="font/woff2" crossorigin>
</head>
```

---

### 5.3 添加缓存策略

**创建 `docs/_headers`** (Netlify 格式，GitHub Pages 不直接支持):

```
/*
  Cache-Control: public, max-age=31536000, immutable

/*.html
  Cache-Control: public, max-age=3600

/api/*
  Cache-Control: public, max-age=86400
```

**注意**: GitHub Pages 有默认的缓存策略，如果需要自定义，考虑使用 Cloudflare Pages。

---

## 🔧 Phase 6: 监控和维护 (持续)

### 6.1 设置 GitHub Actions 通知

**在 Slack/Discord/邮箱接收部署通知**:

```yaml
# 在 deploy-docs.yml 最后添加
- name: Notify Deployment
  if: always()
  uses: 8398a7/action-slack@v3
  with:
    status: ${{ job.status }}
    text: 'Documentation deployment ${{ job.status }}'
    webhook_url: ${{ secrets.SLACK_WEBHOOK }}
```

---

### 6.2 定期检查和更新

**每周任务**:
- [ ] 检查 Google Analytics 数据
- [ ] 查看 GitHub Actions 构建日志
- [ ] 更新文档内容

**每月任务**:
- [ ] 审查 SEO 排名（Google Search Console）
- [ ] 检查断链（使用工具如 broken-link-checker）
- [ ] 更新性能基准数据

**每季度任务**:
- [ ] 全面 UX 审查
- [ ] A/B 测试新功能
- [ ] 用户反馈收集

---

## 📋 完整执行清单

### 立即执行（今天）✅

- [ ] 1. 调整目录结构（30 分钟）
  - [ ] 决定是否将 web/ 提升到 docs/
  - [ ] 创建 `.nojekyll` 文件
  - [ ] 创建 `_config.yml`
  
- [ ] 2. 启用 GitHub Pages（5 分钟）
  - [ ] 进入仓库 Settings -> Pages
  - [ ] Source: Deploy from a branch
  - [ ] Branch: master/main -> /docs
  - [ ] Save

- [ ] 3. 创建 GitHub Actions（30 分钟）
  - [ ] 创建 `.github/workflows/deploy-docs.yml`
  - [ ] 测试部署
  
- [ ] 4. 验证部署（10 分钟）
  - [ ] 访问 `https://your-org.github.io/Catga/`
  - [ ] 检查所有链接
  - [ ] 测试移动端

---

### 短期执行（本周）📅

- [ ] 5. 集成 Google Analytics（30 分钟）
  - [ ] 创建 GA4 账号
  - [ ] 添加跟踪代码
  - [ ] 验证数据收集

- [ ] 6. SEO 优化（1 小时）
  - [ ] 创建 `robots.txt`
  - [ ] 生成 `sitemap.xml`
  - [ ] 添加 Open Graph 标签
  - [ ] 提交到 Google Search Console

- [ ] 7. 性能优化（1 小时）
  - [ ] 添加资源预加载
  - [ ] 优化图片（WebP 格式）
  - [ ] 测试 Lighthouse 评分

---

### 中期执行（本月）📆

- [ ] 8. 自定义域名（可选，2 小时）
  - [ ] 购买域名
  - [ ] 配置 DNS
  - [ ] 添加 CNAME 文件
  - [ ] 等待 DNS 生效（24-48h）

- [ ] 9. 高级功能（3-5 小时）
  - [ ] Algolia DocSearch 集成
  - [ ] 多语言版本
  - [ ] 暗色模式完善

- [ ] 10. 文档完善（持续）
  - [ ] 补充示例代码
  - [ ] 添加视频教程
  - [ ] 社区贡献指南

---

## 🎯 成功验收标准

### 技术指标
- ✅ 网站可访问：`https://your-org.github.io/Catga/`
- ✅ HTTPS 启用（GitHub Pages 自动）
- ✅ 所有页面正常加载
- ✅ 移动端适配正常
- ✅ Lighthouse 评分 > 90

### 功能指标
- ✅ Google Analytics 正常收集数据
- ✅ 搜索功能正常工作
- ✅ 代码复制按钮正常
- ✅ 暗色模式切换正常

### SEO 指标
- ✅ Google Search Console 已索引
- ✅ sitemap.xml 提交成功
- ✅ robots.txt 可访问
- ✅ Open Graph 标签正确

---

## 🚨 常见问题和解决方案

### Q1: GitHub Pages 显示 404

**解决方案**:
1. 检查 Settings -> Pages 是否正确配置
2. 确保 docs/ 目录存在且有 index.html
3. 等待 1-2 分钟让部署完成
4. 清除浏览器缓存

---

### Q2: 样式/脚本加载失败

**解决方案**:
```html
<!-- 使用相对路径 -->
<link rel="stylesheet" href="./style.css">
<script src="./app.js"></script>

<!-- 或使用绝对路径（如果是项目站点） -->
<link rel="stylesheet" href="/Catga/web/style.css">
```

---

### Q3: GitHub Actions 部署失败

**解决方案**:
1. 检查 Actions 日志
2. 验证 YAML 语法
3. 确保有正确的权限（Settings -> Actions -> General -> Workflow permissions）

---

### Q4: 自定义域名不工作

**解决方案**:
1. 检查 CNAME 文件内容正确
2. 验证 DNS 配置（使用 `dig` 或 `nslookup`）
3. 等待 DNS 传播（最多 48 小时）
4. 在 GitHub Settings -> Pages 中启用自定义域名

---

## 📞 获取帮助

如遇到问题：
1. 查看 [GitHub Pages 文档](https://docs.github.com/pages)
2. 查看 [GitHub Community](https://github.com/orgs/community/discussions)
3. 提交 Issue 到 Catga 仓库

---

## 📈 预期时间线

| 阶段 | 任务 | 时间 | 负责人 |
|------|------|------|--------|
| **Day 1** | 基础配置 + 部署 | 1h | DevOps |
| **Day 2** | GA + SEO | 2h | Marketing |
| **Week 1** | 性能优化 | 2h | Frontend |
| **Week 2** | 监控 + 调整 | 1h | Team |
| **Month 1** | 高级功能 | 5h | Team |

**总计**: ~11 小时（分散执行）

---

**制定人**: AI Assistant  
**审核**: 待审核  
**执行**: 立即开始

🚀 准备好部署到 GitHub Pages 了吗？

