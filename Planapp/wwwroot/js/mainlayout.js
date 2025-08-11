// mainlayout.js - Enhanced interactivity for Usage Meter

let dotNetRef = null;
let resizeObserver = null;
let keyboardListeners = [];

export function initializeMainLayout(dotNetReference) {
    dotNetRef = dotNetReference;

    console.log('🚀 Initializing enhanced MainLayout...');

    // Initialize all features
    initializeResponsiveDesign();
    initializeKeyboardShortcuts();
    initializeSmoothScrolling();
    initializeTooltips();
    initializeFocusManagement();
    initializePerformanceOptimizations();

    console.log('✅ MainLayout enhanced features initialized');
}

// Responsive Design Management
function initializeResponsiveDesign() {
    // Create a more sophisticated resize observer
    if (window.ResizeObserver) {
        resizeObserver = new ResizeObserver(entries => {
            for (let entry of entries) {
                const { width } = entry.contentRect;
                const isMobile = width < 768;

                // Update CSS custom properties based on screen size
                document.documentElement.style.setProperty(
                    '--viewport-width',
                    `${width}px`
                );

                // Notify Blazor component
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnWindowResize', isMobile);
                }

                // Adjust navbar behavior
                adjustNavbarForScreenSize(isMobile);
            }
        });

        resizeObserver.observe(document.body);
    }

    // Legacy fallback for older browsers
    window.addEventListener('resize', debounce(() => {
        const isMobile = window.innerWidth < 768;
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync('OnWindowResize', isMobile);
        }
        adjustNavbarForScreenSize(isMobile);
    }, 150));
}

// Keyboard Shortcuts
function initializeKeyboardShortcuts() {
    const shortcuts = [
        {
            key: 'm',
            ctrlKey: true,
            action: 'toggle-menu',
            description: 'Toggle navigation menu'
        },
        {
            key: 'Escape',
            action: 'escape',
            description: 'Close navigation menu'
        },
        {
            key: '/',
            action: 'focus-search',
            description: 'Focus search input'
        }
    ];

    shortcuts.forEach(shortcut => {
        const handler = (event) => {
            const isShortcut = shortcut.ctrlKey ?
                (event.ctrlKey && event.key === shortcut.key) :
                (event.key === shortcut.key);

            if (isShortcut && !isInputFocused()) {
                event.preventDefault();

                if (shortcut.action === 'focus-search') {
                    focusSearchInput();
                } else if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnKeyboardShortcut', shortcut.action);
                }
            }
        };

        document.addEventListener('keydown', handler);
        keyboardListeners.push(() => document.removeEventListener('keydown', handler));
    });

    // Show keyboard shortcuts help
    console.log('⌨️ Keyboard shortcuts available:', shortcuts.map(s =>
        `${s.ctrlKey ? 'Ctrl+' : ''}${s.key} - ${s.description}`
    ).join('\n'));
}

// Smooth Scrolling Enhancement
function initializeSmoothScrolling() {
    // Enhanced smooth scrolling for internal links
    document.addEventListener('click', (event) => {
        const link = event.target.closest('a[href^="#"]');
        if (link) {
            event.preventDefault();
            const targetId = link.getAttribute('href').substring(1);
            const targetElement = document.getElementById(targetId);

            if (targetElement) {
                targetElement.scrollIntoView({
                    behavior: 'smooth',
                    block: 'start',
                    inline: 'nearest'
                });

                // Update URL without triggering navigation
                history.replaceState(null, null, `#${targetId}`);
            }
        }
    });
}

// Modern Tooltips
function initializeTooltips() {
    // Create tooltip element
    const tooltip = document.createElement('div');
    tooltip.className = 'modern-tooltip';
    tooltip.style.cssText = `
        position: absolute;
        background: rgba(0, 0, 0, 0.9);
        color: white;
        padding: 0.5rem 0.75rem;
        border-radius: 0.5rem;
        font-size: 0.875rem;
        font-weight: 500;
        z-index: 10000;
        pointer-events: none;
        opacity: 0;
        transform: translateY(0.5rem);
        transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
        backdrop-filter: blur(8px);
        border: 1px solid rgba(255, 255, 255, 0.1);
        box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
    `;
    document.body.appendChild(tooltip);

    // Tooltip event handlers
    let tooltipTimeout;

    document.addEventListener('mouseover', (event) => {
        const element = event.target.closest('[title], [data-tooltip]');
        if (element) {
            const text = element.getAttribute('data-tooltip') || element.getAttribute('title');
            if (text) {
                clearTimeout(tooltipTimeout);

                // Remove title to prevent default tooltip
                if (element.hasAttribute('title')) {
                    element.setAttribute('data-original-title', element.getAttribute('title'));
                    element.removeAttribute('title');
                }

                tooltipTimeout = setTimeout(() => {
                    showTooltip(element, text, tooltip);
                }, 500);
            }
        }
    });

    document.addEventListener('mouseout', (event) => {
        const element = event.target.closest('[data-original-title], [data-tooltip]');
        if (element) {
            clearTimeout(tooltipTimeout);
            hideTooltip(tooltip);

            // Restore original title
            if (element.hasAttribute('data-original-title')) {
                element.setAttribute('title', element.getAttribute('data-original-title'));
                element.removeAttribute('data-original-title');
            }
        }
    });
}

// Focus Management
function initializeFocusManagement() {
    // Improve focus indicators
    document.addEventListener('keydown', (event) => {
        if (event.key === 'Tab') {
            document.body.classList.add('keyboard-navigation');
        }
    });

    document.addEventListener('mousedown', () => {
        document.body.classList.remove('keyboard-navigation');
    });

    // Add enhanced focus styles
    const focusStyles = document.createElement('style');
    focusStyles.textContent = `
        .keyboard-navigation *:focus {
            outline: 2px solid #3b82f6 !important;
            outline-offset: 2px !important;
            box-shadow: 0 0 0 4px rgba(59, 130, 246, 0.2) !important;
        }
        
        .keyboard-navigation .nav-item:focus {
            outline: 2px solid rgba(255, 255, 255, 0.8) !important;
            box-shadow: 0 0 0 4px rgba(255, 255, 255, 0.2) !important;
        }
    `;
    document.head.appendChild(focusStyles);
}

// Performance Optimizations
function initializePerformanceOptimizations() {
    // Intersection Observer for lazy loading
    if (window.IntersectionObserver) {
        const imageObserver = new IntersectionObserver((entries) => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    const img = entry.target;
                    if (img.dataset.src) {
                        img.src = img.dataset.src;
                        img.removeAttribute('data-src');
                        imageObserver.unobserve(img);
                    }
                }
            });
        }, {
            rootMargin: '50px'
        });

        // Observe all images with data-src
        document.querySelectorAll('img[data-src]').forEach(img => {
            imageObserver.observe(img);
        });
    }

    // Optimize animations for performance
    const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    if (prefersReducedMotion.matches) {
        document.documentElement.style.setProperty('--transition-fast', '0ms');
        document.documentElement.style.setProperty('--transition-base', '0ms');
        document.documentElement.style.setProperty('--transition-slow', '0ms');
    }

    // Monitor performance
    if (window.PerformanceObserver) {
        const perfObserver = new PerformanceObserver((list) => {
            list.getEntries().forEach(entry => {
                if (entry.entryType === 'measure' && entry.duration > 100) {
                    console.warn(`⚠️ Slow operation detected: ${entry.name} took ${entry.duration}ms`);
                }
            });
        });

        try {
            perfObserver.observe({ entryTypes: ['measure'] });
        } catch (e) {
            // Performance API not fully supported
        }
    }
}

// Helper Functions
function adjustNavbarForScreenSize(isMobile) {
    const navbar = document.querySelector('.sidebar');
    if (navbar) {
        if (isMobile) {
            navbar.setAttribute('aria-hidden', 'true');
        } else {
            navbar.removeAttribute('aria-hidden');
        }
    }
}

function isInputFocused() {
    const activeElement = document.activeElement;
    return activeElement && (
        activeElement.tagName === 'INPUT' ||
        activeElement.tagName === 'TEXTAREA' ||
        activeElement.tagName === 'SELECT' ||
        activeElement.contentEditable === 'true'
    );
}

function focusSearchInput() {
    const searchInput = document.querySelector('input[type="search"], .search-input, input[placeholder*="search" i]');
    if (searchInput) {
        searchInput.focus();
        searchInput.select();
    }
}

function showTooltip(element, text, tooltip) {
    tooltip.textContent = text;
    tooltip.style.opacity = '1';
    tooltip.style.transform = 'translateY(0)';

    // Position tooltip
    const rect = element.getBoundingClientRect();
    const tooltipRect = tooltip.getBoundingClientRect();

    let top = rect.bottom + 8;
    let left = rect.left + (rect.width / 2) - (tooltipRect.width / 2);

    // Adjust if tooltip goes off screen
    if (left < 8) left = 8;
    if (left + tooltipRect.width > window.innerWidth - 8) {
        left = window.innerWidth - tooltipRect.width - 8;
    }
    if (top + tooltipRect.height > window.innerHeight - 8) {
        top = rect.top - tooltipRect.height - 8;
    }

    tooltip.style.top = `${top}px`;
    tooltip.style.left = `${left}px`;
}

function hideTooltip(tooltip) {
    tooltip.style.opacity = '0';
    tooltip.style.transform = 'translateY(0.5rem)';
}

function debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
        const later = () => {
            clearTimeout(timeout);
            func(...args);
        };
        clearTimeout(timeout);
        timeout = setTimeout(later, wait);
    };
}

// Theme Management
export function setTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('usage-meter-theme', theme);

    // Dispatch theme change event
    window.dispatchEvent(new CustomEvent('themechange', { detail: { theme } }));
}

export function getTheme() {
    return localStorage.getItem('usage-meter-theme') || 'light';
}

// Notification System
export function showNotification(message, type = 'info', duration = 5000) {
    const notification = document.createElement('div');
    notification.className = `notification notification-${type}`;
    notification.style.cssText = `
        position: fixed;
        top: 1rem;
        right: 1rem;
        background: white;
        border-radius: 0.75rem;
        padding: 1rem 1.5rem;
        box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
        border-left: 4px solid var(--${type === 'error' ? 'error' : type === 'success' ? 'success' : type === 'warning' ? 'warning' : 'primary'}-500);
        z-index: 10000;
        max-width: 24rem;
        transform: translateX(100%);
        transition: transform 0.3s cubic-bezier(0.4, 0, 0.2, 1);
    `;

    notification.textContent = message;
    document.body.appendChild(notification);

    // Animate in
    requestAnimationFrame(() => {
        notification.style.transform = 'translateX(0)';
    });

    // Auto remove
    setTimeout(() => {
        notification.style.transform = 'translateX(100%)';
        setTimeout(() => notification.remove(), 300);
    }, duration);
}

// Cleanup function
export function disconnect() {
    if (resizeObserver) {
        resizeObserver.disconnect();
    }

    keyboardListeners.forEach(cleanup => cleanup());
    keyboardListeners = [];

    // Remove tooltip
    const tooltip = document.querySelector('.modern-tooltip');
    if (tooltip) {
        tooltip.remove();
    }

    console.log('🧹 MainLayout enhanced features cleaned up');
}