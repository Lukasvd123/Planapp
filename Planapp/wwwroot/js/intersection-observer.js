// Intersection Observer for Lazy Loading App Icons
let observers = new Map();

export function observe(element, dotNetRef) {
    if (!element || !dotNetRef) {
        console.warn('LazyAppIcon: Invalid element or dotNetRef');
        return;
    }

    // Check if Intersection Observer is supported
    if (!('IntersectionObserver' in window)) {
        console.warn('LazyAppIcon: IntersectionObserver not supported, loading immediately');
        dotNetRef.invokeMethodAsync('OnIntersection', true);
        return;
    }

    try {
        // Create observer with optimized options
        const observer = new IntersectionObserver((entries) => {
            entries.forEach(entry => {
                if (entry.target === element) {
                    try {
                        dotNetRef.invokeMethodAsync('OnIntersection', entry.isIntersecting);

                        // Stop observing after first intersection for performance
                        if (entry.isIntersecting) {
                            observer.unobserve(element);
                            observers.delete(element);
                        }
                    } catch (error) {
                        console.error('LazyAppIcon: Error calling OnIntersection:', error);
                    }
                }
            });
        }, {
            // Start loading when element is 100px away from viewport
            rootMargin: '100px',
            // Trigger when any part becomes visible
            threshold: 0.01
        });

        observer.observe(element);
        observers.set(element, observer);

        console.debug('LazyAppIcon: Observer started for element');
    } catch (error) {
        console.error('LazyAppIcon: Error setting up observer:', error);
        // Fallback to immediate loading
        dotNetRef.invokeMethodAsync('OnIntersection', true);
    }
}

export function disconnect() {
    try {
        observers.forEach((observer, element) => {
            observer.unobserve(element);
            observer.disconnect();
        });
        observers.clear();
        console.debug('LazyAppIcon: All observers disconnected');
    } catch (error) {
        console.error('LazyAppIcon: Error disconnecting observers:', error);
    }
}

// Utility function to check if element is in viewport (fallback)
function isElementInViewport(element) {
    if (!element) return false;

    const rect = element.getBoundingClientRect();
    const windowHeight = window.innerHeight || document.documentElement.clientHeight;
    const windowWidth = window.innerWidth || document.documentElement.clientWidth;

    return (
        rect.top >= -100 && // 100px preload margin
        rect.left >= 0 &&
        rect.bottom <= windowHeight + 100 &&
        rect.right <= windowWidth
    );
}

// Performance monitoring (debug)
let loadStartTime = performance.now();
let iconsLoaded = 0;

export function trackIconLoad() {
    iconsLoaded++;
    const elapsed = performance.now() - loadStartTime;
    console.debug(`LazyAppIcon: ${iconsLoaded} icons loaded in ${elapsed.toFixed(2)}ms`);
}

// Export for debugging
window.lazyAppIconDebug = {
    getObserverCount: () => observers.size,
    getIconsLoaded: () => iconsLoaded,
    resetStats: () => {
        loadStartTime = performance.now();
        iconsLoaded = 0;
    }
};