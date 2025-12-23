// Shiki syntax highlighter interop for Blazor
window.shikiInterop = {
    highlighter: null,
    
    // Initialize Shiki with common languages and theme
    initialize: async function() {
        if (this.highlighter) {
            return; // Already initialized
        }
        
        try {
            // Import Shiki from CDN using esm.sh
            const { codeToHtml } = await import('https://esm.sh/shiki@0.14.7');
            
            this.codeToHtml = codeToHtml;
            this.highlighter = true; // Mark as initialized
            
            console.log('Shiki initialized successfully');
        } catch (error) {
            console.error('Failed to initialize Shiki:', error);
            throw error;
        }
    },
    
    // Highlight code and return HTML
    highlightCode: async function(code, language, theme) {
        if (!this.highlighter) {
            await this.initialize();
        }
        
        try {
            const html = await this.codeToHtml(code, {
                lang: language || 'csharp',
                theme: theme || 'dark-plus'
            });
            
            return html;
        } catch (error) {
            console.error('Failed to highlight code:', error);
            // Return plain code wrapped in pre/code if highlighting fails
            return `<pre><code>${this.escapeHtml(code)}</code></pre>`;
        }
    },
    
    // Escape HTML to prevent XSS
    escapeHtml: function(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
};
