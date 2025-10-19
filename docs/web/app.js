/**
 * Catga 官方文档网站 - 交互功能
 * 包含: 暗色模式、搜索、代码复制、移动端导航等
 */

// ========================================
// 1. Google Analytics 集成
// ========================================

// 注意: 需要在 index.html 中添加 Google Analytics 代码
// 这里提供事件追踪函数
function trackEvent(category, action, label) {
    if (typeof gtag !== 'undefined') {
        gtag('event', action, {
            'event_category': category,
            'event_label': label
        });
    }
}

// ========================================
// 2. 暗色模式
// ========================================

function initTheme() {
    // 获取保存的主题或系统偏好
    const savedTheme = localStorage.getItem('theme');
    const systemPrefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    const theme = savedTheme || (systemPrefersDark ? 'dark' : 'light');
    
    setTheme(theme);
    
    // 监听系统主题变化
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', e => {
        if (!localStorage.getItem('theme')) {
            setTheme(e.matches ? 'dark' : 'light');
        }
    });
}

function setTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('theme', theme);
    
    // 更新按钮图标
    const themeToggle = document.getElementById('theme-toggle');
    if (themeToggle) {
        themeToggle.innerHTML = theme === 'dark' ? '☀️' : '🌙';
        themeToggle.setAttribute('aria-label', theme === 'dark' ? '切换到亮色模式' : '切换到暗色模式');
    }
    
    trackEvent('Theme', 'toggle', theme);
}

function toggleTheme() {
    const currentTheme = document.documentElement.getAttribute('data-theme');
    const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
    setTheme(newTheme);
}

// ========================================
// 3. 简易搜索功能
// ========================================

const searchData = [
    { title: '快速开始', url: '../articles/getting-started.html', keywords: ['安装', '配置', 'hello world', '入门'] },
    { title: '架构设计', url: '../articles/architecture.html', keywords: ['CQRS', '事件溯源', '架构', '设计'] },
    { title: '配置指南', url: '../articles/configuration.html', keywords: ['配置', 'Redis', 'NATS', 'Outbox', 'Inbox'] },
    { title: 'OpenTelemetry 集成', url: '../articles/opentelemetry-integration.html', keywords: ['监控', '追踪', 'OpenTelemetry', 'Jaeger'] },
    { title: 'Native AOT 部署', url: '../articles/aot-deployment.html', keywords: ['AOT', '部署', '性能', '编译'] },
    { title: 'API 文档', url: '../api/index.html', keywords: ['API', '接口', '文档'] },
];

function initSearch() {
    const searchInput = document.getElementById('search-input');
    const searchResults = document.getElementById('search-results');
    const searchOverlay = document.getElementById('search-overlay');
    
    if (!searchInput) return;
    
    // 打开搜索框
    document.addEventListener('keydown', e => {
        if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
            e.preventDefault();
            openSearch();
        }
        if (e.key === 'Escape') {
            closeSearch();
        }
    });
    
    // 搜索输入
    searchInput.addEventListener('input', e => {
        const query = e.target.value.trim().toLowerCase();
        if (query.length < 2) {
            searchResults.innerHTML = '';
            return;
        }
        
        const results = search(query);
        displaySearchResults(results);
        
        trackEvent('Search', 'query', query);
    });
    
    // 点击遮罩关闭
    if (searchOverlay) {
        searchOverlay.addEventListener('click', closeSearch);
    }
}

function search(query) {
    return searchData.filter(item => 
        item.title.toLowerCase().includes(query) ||
        item.keywords.some(k => k.toLowerCase().includes(query))
    ).slice(0, 5); // 最多显示 5 个结果
}

function displaySearchResults(results) {
    const searchResults = document.getElementById('search-results');
    if (!searchResults) return;
    
    if (results.length === 0) {
        searchResults.innerHTML = '<div class="search-no-results">未找到相关结果</div>';
        return;
    }
    
    searchResults.innerHTML = results.map(item => `
        <a href="${item.url}" class="search-result-item">
            <div class="search-result-title">${item.title}</div>
            <div class="search-result-keywords">${item.keywords.slice(0, 3).join(', ')}</div>
        </a>
    `).join('');
}

function openSearch() {
    const searchModal = document.getElementById('search-modal');
    if (searchModal) {
        searchModal.style.display = 'flex';
        document.getElementById('search-input')?.focus();
        document.body.style.overflow = 'hidden';
    }
}

function closeSearch() {
    const searchModal = document.getElementById('search-modal');
    if (searchModal) {
        searchModal.style.display = 'none';
        document.body.style.overflow = '';
    }
}

// ========================================
// 4. 代码复制按钮
// ========================================

function initCodeCopy() {
    document.querySelectorAll('pre code').forEach(block => {
        const pre = block.parentElement;
        if (!pre.querySelector('.copy-btn')) {
            const button = document.createElement('button');
            button.className = 'copy-btn';
            button.textContent = '复制';
            button.setAttribute('aria-label', '复制代码');
            
            button.onclick = async () => {
                try {
                    await navigator.clipboard.writeText(block.textContent);
                    button.textContent = '已复制!';
                    button.classList.add('copied');
                    
                    trackEvent('Code', 'copy', 'success');
                    
                    setTimeout(() => {
                        button.textContent = '复制';
                        button.classList.remove('copied');
                    }, 2000);
                } catch (err) {
                    button.textContent = '复制失败';
                    setTimeout(() => button.textContent = '复制', 2000);
                }
            };
            
            pre.style.position = 'relative';
            pre.appendChild(button);
        }
    });
}

// ========================================
// 5. 移动端导航
// ========================================

function initMobileNav() {
    const mobileToggle = document.getElementById('mobile-menu-toggle');
    const mobileMenu = document.getElementById('mobile-menu');
    
    if (mobileToggle && mobileMenu) {
        mobileToggle.addEventListener('click', () => {
            const isOpen = mobileMenu.classList.toggle('open');
            mobileToggle.setAttribute('aria-expanded', isOpen);
            mobileToggle.innerHTML = isOpen ? '✕' : '☰';
            document.body.style.overflow = isOpen ? 'hidden' : '';
            
            trackEvent('Navigation', 'mobile_menu', isOpen ? 'open' : 'close');
        });
        
        // 点击链接后关闭菜单
        mobileMenu.querySelectorAll('a').forEach(link => {
            link.addEventListener('click', () => {
                mobileMenu.classList.remove('open');
                mobileToggle.setAttribute('aria-expanded', 'false');
                mobileToggle.innerHTML = '☰';
                document.body.style.overflow = '';
            });
        });
    }
}

// ========================================
// 6. 返回顶部按钮
// ========================================

function initBackToTop() {
    const backToTopBtn = document.getElementById('back-to-top');
    if (!backToTopBtn) return;
    
    window.addEventListener('scroll', () => {
        if (window.scrollY > 300) {
            backToTopBtn.classList.add('visible');
        } else {
            backToTopBtn.classList.remove('visible');
        }
    });
    
    backToTopBtn.addEventListener('click', () => {
        window.scrollTo({ top: 0, behavior: 'smooth' });
        trackEvent('Navigation', 'back_to_top', 'click');
    });
}

// ========================================
// 7. 滚动进度条
// ========================================

function initScrollProgress() {
    const progressBar = document.getElementById('scroll-progress');
    if (!progressBar) return;
    
    window.addEventListener('scroll', () => {
        const windowHeight = document.documentElement.scrollHeight - window.innerHeight;
        const scrolled = (window.scrollY / windowHeight) * 100;
        progressBar.style.width = Math.min(scrolled, 100) + '%';
    });
}

// ========================================
// 8. CTA 按钮追踪
// ========================================

function initCTATracking() {
    document.querySelectorAll('.btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const btnText = btn.textContent.trim();
            const btnHref = btn.getAttribute('href');
            trackEvent('CTA', 'click', `${btnText} - ${btnHref}`);
        });
    });
}

// ========================================
// 9. 平滑滚动
// ========================================

function initSmoothScroll() {
    document.querySelectorAll('a[href^="#"]').forEach(anchor => {
        anchor.addEventListener('click', function (e) {
            e.preventDefault();
            const target = document.querySelector(this.getAttribute('href'));
            if (target) {
                target.scrollIntoView({ behavior: 'smooth', block: 'start' });
                trackEvent('Navigation', 'smooth_scroll', this.getAttribute('href'));
            }
        });
    });
}

// ========================================
// 10. 性能监控
// ========================================

function trackPerformance() {
    if (typeof gtag === 'undefined') return;
    
    // 页面加载性能
    window.addEventListener('load', () => {
        const perfData = window.performance.timing;
        const pageLoadTime = perfData.loadEventEnd - perfData.navigationStart;
        const connectTime = perfData.responseEnd - perfData.requestStart;
        const renderTime = perfData.domComplete - perfData.domLoading;
        
        gtag('event', 'timing_complete', {
            'name': 'page_load',
            'value': pageLoadTime,
            'event_category': 'Performance'
        });
        
        console.log('Performance:', {
            pageLoad: pageLoadTime + 'ms',
            connect: connectTime + 'ms',
            render: renderTime + 'ms'
        });
    });
}

// ========================================
// 初始化所有功能
// ========================================

document.addEventListener('DOMContentLoaded', () => {
    initTheme();
    initSearch();
    initCodeCopy();
    initMobileNav();
    initBackToTop();
    initScrollProgress();
    initCTATracking();
    initSmoothScroll();
    trackPerformance();
    
    console.log('✨ Catga 官方网站已加载');
});

// 导出函数供 HTML 直接调用
window.toggleTheme = toggleTheme;
window.openSearch = openSearch;
window.closeSearch = closeSearch;

