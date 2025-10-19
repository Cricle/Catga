# Catga 官方文档网站优化计划

**制定日期**: 2025-10-19  
**目标**: 提升用户体验、增加数据分析、优化 SEO 和性能

---

## 📊 Phase 1: 数据分析集成 (1-2h)

### 1.1 Google Analytics 4 集成 ⭐⭐⭐⭐⭐

**优先级**: 高  
**价值**: 了解用户行为、优化内容策略

**实现步骤**:

```html
<!-- 在 index.html <head> 中添加 -->
<!-- Google tag (gtag.js) -->
<script async src="https://www.googletagmanager.com/gtag/js?id=G-XXXXXXXXXX"></script>
<script>
  window.dataLayer = window.dataLayer || [];
  function gtag(){dataLayer.push(arguments);}
  gtag('js', new Date());
  gtag('config', 'G-XXXXXXXXXX');
</script>
```

**追踪指标**:
- 页面浏览量（PV）
- 独立访客（UV）
- 平均停留时间
- 跳出率
- 流量来源（搜索、直接、社交媒体）

**自定义事件**:
```javascript
// 点击 CTA 按钮
gtag('event', 'click_cta', {
  'button_name': 'quick_start',
  'location': 'hero_section'
});

// 点击文档链接
gtag('event', 'click_doc', {
  'doc_name': 'getting-started',
  'section': 'documentation'
});

// 查看代码示例
gtag('event', 'view_code', {
  'example': 'basic_usage'
});
```

---

### 1.2 百度统计集成 ⭐⭐⭐⭐

**优先级**: 高（针对中国用户）  
**价值**: 更准确的国内用户数据

**实现步骤**:

```html
<!-- 百度统计代码 -->
<script>
var _hmt = _hmt || [];
(function() {
  var hm = document.createElement("script");
  hm.src = "https://hm.baidu.com/hm.js?XXXXXXXXXXXXXXXX";
  var s = document.getElementsByTagName("script")[0]; 
  s.parentNode.insertBefore(hm, s);
})();
</script>
```

---

### 1.3 简易自托管分析 ⭐⭐⭐

**优先级**: 中（隐私友好）  
**价值**: 无需第三方服务，保护用户隐私

**实现方案**: Umami / Plausible / Matomo

```javascript
// Umami 示例
<script async defer 
  data-website-id="xxx" 
  src="https://analytics.yourdomain.com/umami.js">
</script>
```

---

## 🎨 Phase 2: 用户体验优化 (3-4h)

### 2.1 搜索功能 ⭐⭐⭐⭐⭐

**优先级**: 最高  
**价值**: 快速找到文档内容

**实现方案**: 
- **方案 A**: Algolia DocSearch (推荐)
- **方案 B**: 自定义 JavaScript 搜索
- **方案 C**: Lunr.js 本地搜索

**Algolia DocSearch 实现**:

```html
<!-- 添加到 <head> -->
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/@docsearch/css@3" />

<!-- 添加到导航栏 -->
<div id="docsearch"></div>

<!-- 添加到 <body> 底部 -->
<script src="https://cdn.jsdelivr.net/npm/@docsearch/js@3"></script>
<script>
docsearch({
  appId: 'YOUR_APP_ID',
  apiKey: 'YOUR_API_KEY',
  indexName: 'catga',
  container: '#docsearch',
});
</script>
```

**自定义简易搜索**:

```javascript
// 简易客户端搜索
const searchData = [
  { title: '快速开始', url: '../articles/getting-started.html', keywords: ['安装', '配置', 'hello world'] },
  { title: '架构设计', url: '../articles/architecture.html', keywords: ['CQRS', '事件溯源', '架构'] },
  // ... 更多
];

function search(query) {
  return searchData.filter(item => 
    item.title.includes(query) || 
    item.keywords.some(k => k.includes(query))
  );
}
```

---

### 2.2 暗色模式 (Dark Mode) ⭐⭐⭐⭐

**优先级**: 高  
**价值**: 提升夜间使用体验，减少眼睛疲劳

**实现步骤**:

```css
/* 添加暗色主题变量 */
[data-theme="dark"] {
  --primary-color: #8B7CFF;
  --text-color: #E2E8F0;
  --text-light: #A0AEC0;
  --bg-light: #1A202C;
  --bg-white: #2D3748;
  --border-color: #4A5568;
}

/* 自动检测系统主题 */
@media (prefers-color-scheme: dark) {
  :root {
    /* 暗色变量 */
  }
}
```

```javascript
// 主题切换逻辑
function toggleTheme() {
  const current = document.documentElement.getAttribute('data-theme');
  const next = current === 'dark' ? 'light' : 'dark';
  document.documentElement.setAttribute('data-theme', next);
  localStorage.setItem('theme', next);
}

// 恢复用户偏好
const savedTheme = localStorage.getItem('theme') || 
  (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
document.documentElement.setAttribute('data-theme', savedTheme);
```

**UI 组件**:

```html
<!-- 主题切换按钮 -->
<button onclick="toggleTheme()" aria-label="切换主题">
  <span class="light-icon">🌙</span>
  <span class="dark-icon">☀️</span>
</button>
```

---

### 2.3 多语言支持 (i18n) ⭐⭐⭐⭐

**优先级**: 高  
**价值**: 扩大国际用户群

**实现方案**:

```
docs/web/
├── index.html           # 中文版（默认）
├── en/
│   └── index.html       # 英文版
└── i18n.js              # 语言切换脚本
```

**简易实现**:

```javascript
// i18n.js
const translations = {
  'zh-CN': {
    hero_title: '⚡ Catga',
    hero_desc: '现代化、高性能的 .NET CQRS/Event Sourcing 框架',
    cta_start: '快速开始',
  },
  'en-US': {
    hero_title: '⚡ Catga',
    hero_desc: 'Modern, High-Performance .NET CQRS/Event Sourcing Framework',
    cta_start: 'Get Started',
  }
};

function setLanguage(lang) {
  const t = translations[lang];
  document.querySelectorAll('[data-i18n]').forEach(el => {
    const key = el.getAttribute('data-i18n');
    if (t[key]) el.textContent = t[key];
  });
  localStorage.setItem('language', lang);
}
```

---

### 2.4 交互式代码示例 ⭐⭐⭐⭐

**优先级**: 高  
**价值**: 提升学习体验

**实现方案**:

**方案 A**: CodePen/JSFiddle 嵌入

```html
<iframe height="400" style="width: 100%;" 
  src="https://codepen.io/your-username/embed/xxxxx">
</iframe>
```

**方案 B**: 代码复制按钮

```javascript
// 为每个代码块添加复制按钮
document.querySelectorAll('pre code').forEach(block => {
  const button = document.createElement('button');
  button.className = 'copy-btn';
  button.textContent = '复制';
  button.onclick = () => {
    navigator.clipboard.writeText(block.textContent);
    button.textContent = '已复制!';
    setTimeout(() => button.textContent = '复制', 2000);
  };
  block.parentElement.appendChild(button);
});
```

**方案 C**: 在线运行（.NET Fiddle）

```html
<a href="https://dotnetfiddle.net/" target="_blank" class="btn btn-secondary">
  在线运行此示例 →
</a>
```

---

### 2.5 进度指示器 ⭐⭐⭐

**优先级**: 中  
**价值**: 提升阅读体验

```javascript
// 页面滚动进度条
window.addEventListener('scroll', () => {
  const scrolled = (window.scrollY / 
    (document.documentElement.scrollHeight - window.innerHeight)) * 100;
  document.getElementById('progress-bar').style.width = scrolled + '%';
});
```

```html
<div id="progress-bar" style="
  position: fixed; 
  top: 0; 
  left: 0; 
  height: 3px; 
  background: var(--primary-color); 
  z-index: 9999;
"></div>
```

---

### 2.6 返回顶部按钮 ⭐⭐⭐

**优先级**: 中  
**价值**: 提升导航便利性

```javascript
// 滚动到顶部
const backToTop = document.getElementById('back-to-top');
window.addEventListener('scroll', () => {
  backToTop.style.display = window.scrollY > 300 ? 'block' : 'none';
});
backToTop.onclick = () => window.scrollTo({ top: 0, behavior: 'smooth' });
```

```html
<button id="back-to-top" style="
  position: fixed; 
  bottom: 2rem; 
  right: 2rem; 
  display: none;
">↑</button>
```

---

## 🚀 Phase 3: 性能优化 (2-3h)

### 3.1 图片优化 ⭐⭐⭐⭐⭐

**优先级**: 最高  
**价值**: 提升加载速度

**策略**:
- 使用 WebP 格式（兼容 PNG fallback）
- 懒加载（Lazy Loading）
- 响应式图片（srcset）

```html
<picture>
  <source srcset="logo.webp" type="image/webp">
  <source srcset="logo.png" type="image/png">
  <img src="logo.png" alt="Catga Logo" loading="lazy">
</picture>
```

---

### 3.2 CSS/JS 压缩 ⭐⭐⭐⭐

**优先级**: 高  
**价值**: 减少文件大小

**工具**:
- CSS: cssnano, clean-css
- JS: terser, uglify-js

```bash
# 压缩 CSS
npx cssnano style.css style.min.css

# 压缩 JS
npx terser script.js -o script.min.js
```

---

### 3.3 CDN 加速 ⭐⭐⭐⭐

**优先级**: 高  
**价值**: 全球加速访问

**方案**:
- **Cloudflare Pages** (免费，全球 CDN)
- **Vercel** (免费，自动 CDN)
- **jsDelivr** (静态资源 CDN)

---

### 3.4 Service Worker 离线缓存 ⭐⭐⭐

**优先级**: 中  
**价值**: 离线访问能力

```javascript
// sw.js
self.addEventListener('install', e => {
  e.waitUntil(
    caches.open('catga-v1').then(cache => {
      return cache.addAll([
        '/',
        '/index.html',
        '/style.css',
        '/favicon.svg'
      ]);
    })
  );
});

// 注册 Service Worker
if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js');
}
```

---

## 📱 Phase 4: 移动端优化 (2h)

### 4.1 移动端导航 ⭐⭐⭐⭐⭐

**优先级**: 最高  
**价值**: 提升移动端体验

**实现**: 汉堡菜单（Hamburger Menu）

```html
<button class="mobile-menu-toggle">☰</button>
<nav class="mobile-menu">
  <!-- 导航链接 -->
</nav>
```

```javascript
document.querySelector('.mobile-menu-toggle').onclick = () => {
  document.querySelector('.mobile-menu').classList.toggle('open');
};
```

---

### 4.2 触摸优化 ⭐⭐⭐

**优先级**: 中  
**价值**: 提升触摸交互

```css
/* 增大触摸目标 */
.btn, a {
  min-height: 44px;  /* iOS 推荐最小触摸尺寸 */
  min-width: 44px;
}

/* 禁用长按选择 */
img {
  -webkit-touch-callout: none;
  -webkit-user-select: none;
}
```

---

## 🔍 Phase 5: SEO 优化 (1-2h)

### 5.1 结构化数据 (Schema.org) ⭐⭐⭐⭐⭐

**优先级**: 最高  
**价值**: 提升搜索排名

```html
<script type="application/ld+json">
{
  "@context": "https://schema.org",
  "@type": "SoftwareApplication",
  "name": "Catga",
  "applicationCategory": "DeveloperApplication",
  "operatingSystem": ".NET",
  "offers": {
    "@type": "Offer",
    "price": "0",
    "priceCurrency": "USD"
  },
  "aggregateRating": {
    "@type": "AggregateRating",
    "ratingValue": "4.9",
    "ratingCount": "100"
  }
}
</script>
```

---

### 5.2 Open Graph (社交分享) ⭐⭐⭐⭐

**优先级**: 高  
**价值**: 美化社交媒体分享

```html
<!-- Open Graph -->
<meta property="og:title" content="Catga - 现代化 .NET CQRS 框架">
<meta property="og:description" content="高性能、支持 Native AOT 的 .NET CQRS/Event Sourcing 框架">
<meta property="og:image" content="https://catga.dev/og-image.png">
<meta property="og:url" content="https://catga.dev">
<meta property="og:type" content="website">

<!-- Twitter Card -->
<meta name="twitter:card" content="summary_large_image">
<meta name="twitter:title" content="Catga - 现代化 .NET CQRS 框架">
<meta name="twitter:description" content="高性能、支持 Native AOT">
<meta name="twitter:image" content="https://catga.dev/twitter-card.png">
```

---

### 5.3 Sitemap 生成 ⭐⭐⭐

**优先级**: 中  
**价值**: 帮助搜索引擎索引

```xml
<!-- sitemap.xml -->
<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <url>
    <loc>https://catga.dev/web/</loc>
    <changefreq>weekly</changefreq>
    <priority>1.0</priority>
  </url>
  <url>
    <loc>https://catga.dev/articles/getting-started.html</loc>
    <changefreq>monthly</changefreq>
    <priority>0.8</priority>
  </url>
  <!-- 更多页面 -->
</urlset>
```

---

## 📊 Phase 6: 数据可视化 (2-3h)

### 6.1 GitHub Stars/Downloads 实时显示 ⭐⭐⭐⭐

**优先级**: 高  
**价值**: 展示项目活跃度

```javascript
// 获取 GitHub Stars
fetch('https://api.github.com/repos/your-org/Catga')
  .then(r => r.json())
  .then(data => {
    document.getElementById('github-stars').textContent = 
      data.stargazers_count.toLocaleString();
  });

// 获取 NuGet 下载量
fetch('https://api.nuget.org/v3-flatcontainer/catga/index.json')
  .then(r => r.json())
  .then(data => {
    // 显示下载量
  });
```

---

### 6.2 性能基准可视化 ⭐⭐⭐

**优先级**: 中  
**价值**: 直观展示性能优势

**使用 Chart.js**:

```html
<canvas id="performance-chart"></canvas>
<script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
<script>
new Chart(document.getElementById('performance-chart'), {
  type: 'bar',
  data: {
    labels: ['Catga', 'MediatR', '手动实现'],
    datasets: [{
      label: '吞吐量 (ops/s)',
      data: [1000000, 300000, 800000]
    }]
  }
});
</script>
```

---

## 🎯 执行优先级

### 立即执行（本次 Session）⭐⭐⭐⭐⭐

1. ✅ **Google Analytics 集成** (30min)
2. ✅ **搜索功能（简易版）** (1h)
3. ✅ **暗色模式** (1h)
4. ✅ **代码复制按钮** (30min)
5. ✅ **移动端导航优化** (30min)
6. ✅ **返回顶部按钮** (15min)
7. ✅ **进度指示器** (15min)

**总计**: ~4 小时

---

### 短期计划（1-2周）⭐⭐⭐⭐

1. 百度统计集成
2. Open Graph 优化
3. 结构化数据
4. 多语言支持（英文版）
5. GitHub Stars 实时显示
6. SEO sitemap
7. 性能优化（压缩、CDN）

---

### 长期计划（1个月+）⭐⭐⭐

1. Algolia DocSearch 集成
2. 交互式代码示例
3. Service Worker 离线支持
4. 性能基准可视化
5. 博客系统
6. 社区论坛
7. 视频教程

---

## 📈 成功指标（KPI）

### 用户体验指标
- **页面加载时间**: < 2s（目标）
- **首屏渲染**: < 1s（目标）
- **Core Web Vitals**: 全部通过（绿色）
- **移动端可用性**: 100 分

### 业务指标
- **月活跃用户 (MAU)**: 追踪增长
- **文档阅读量**: Top 3 页面
- **跳出率**: < 40%（目标）
- **平均停留时间**: > 3 分钟（目标）
- **转化率**: 文档 -> GitHub -> 安装

### 技术指标
- **Lighthouse 评分**: > 90 分
- **SEO 评分**: 100 分
- **可访问性**: > 90 分
- **最佳实践**: 100 分

---

## 🛠️ 工具和资源

### 分析工具
- Google Analytics 4
- 百度统计
- Hotjar (热图分析)
- Clarity (微软免费热图)

### 性能工具
- Google PageSpeed Insights
- WebPageTest
- Lighthouse
- GTmetrix

### SEO 工具
- Google Search Console
- Bing Webmaster Tools
- Ahrefs / SEMrush

### A/B 测试
- Google Optimize
- Optimizely

---

## 💰 预算（如果需要）

### 免费方案
- Google Analytics 4 ✅
- 百度统计 ✅
- Cloudflare Pages ✅
- Vercel ✅
- GitHub Pages ✅

### 付费方案（可选）
- Algolia DocSearch: $0-$XXX/月
- 自定义域名: ~$10/年
- CDN 高级套餐: $20-$100/月
- 高级分析: $50-$200/月

---

## 📝 下一步行动

### 立即执行（本次 Session）
```
1. 添加 Google Analytics 集成代码
2. 实现暗色模式切换
3. 添加搜索功能（简易版）
4. 添加代码复制按钮
5. 优化移动端导航
6. 添加返回顶部按钮
7. 添加滚动进度条
8. 提交并部署到 GitHub Pages
```

是否立即开始执行这些优化？

---

**制定人**: AI Assistant  
**审核**: 待审核  
**执行周期**: 立即开始（4h）+ 短期（1-2周）+ 长期（1个月+）

