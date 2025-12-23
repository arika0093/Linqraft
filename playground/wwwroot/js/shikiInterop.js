// Shiki syntax highlighter interop for Blazor
window.shikiInterop = {
    codeToHtml: null,
    
    // Initialize Shiki with the web bundle (optimized for browser use)
    initialize: async function() {
        if (this.codeToHtml) {
            return; // Already initialized
        }
        
        try {
            // Import Shiki from esm.sh with web bundle (includes common languages)
            const shiki = await import('https://esm.sh/shiki@1.24.2');
            
            // Store the codeToHtml function
            this.codeToHtml = shiki.codeToHtml;
            
            console.log('Shiki initialized successfully');
        } catch (error) {
            console.error('Failed to initialize Shiki:', error);
            throw error;
        }
    },
    
    // Highlight code and return HTML
    highlightCode: async function(code, language, theme) {
        if (!this.codeToHtml) {
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
