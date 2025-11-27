// Monaco Editor semantic highlighting via decorations
window.monacoSemanticHighlighting = {
    // Store decoration IDs for each editor
    decorationIds: {},
    
    // CSS classes for semantic tokens (matching Visual Studio theme)
    tokenClasses: {
        class: 'semantic-class',
        interface: 'semantic-interface',
        struct: 'semantic-struct',
        enum: 'semantic-enum',
        delegate: 'semantic-delegate',
        method: 'semantic-method',
        property: 'semantic-property',
        field: 'semantic-field',
        variable: 'semantic-variable',
        parameter: 'semantic-parameter',
        namespace: 'semantic-namespace'
    },
    
    // Apply semantic tokens as decorations to a Monaco editor
    applySemanticTokens: function(editorId, tokens) {
        const editor = blazorMonaco.editors.find(e => e.id === editorId)?.editor;
        if (!editor) {
            console.warn('Editor not found:', editorId);
            return;
        }
        
        // Convert tokens to decoration options
        const decorations = tokens.map(token => {
            const className = this.getClassName(token.type);
            return {
                range: new monaco.Range(token.startLine, token.startColumn, token.endLine, token.endColumn),
                options: {
                    inlineClassName: className
                }
            };
        });
        
        // Clear old decorations and apply new ones
        const oldDecorations = this.decorationIds[editorId] || [];
        this.decorationIds[editorId] = editor.deltaDecorations(oldDecorations, decorations);
    },
    
    // Clear all semantic decorations from an editor
    clearSemanticTokens: function(editorId) {
        const editor = blazorMonaco.editors.find(e => e.id === editorId)?.editor;
        if (!editor) return;
        
        const oldDecorations = this.decorationIds[editorId] || [];
        this.decorationIds[editorId] = editor.deltaDecorations(oldDecorations, []);
    },
    
    // Get CSS class name for token type
    getClassName: function(type) {
        const types = ['class', 'interface', 'struct', 'enum', 'delegate', 'method', 'property', 'field', 'variable', 'parameter', 'namespace'];
        if (type < 0 || type >= types.length) {
            return this.tokenClasses['class'];
        }
        return this.tokenClasses[types[type]];
    }
};
