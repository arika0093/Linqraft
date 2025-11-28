// Enhanced C# syntax highlighting for Prism.js
// This module post-processes Prism.js output to add VSCode-like semantic highlighting
// for generics, classes, methods, variables, and properties.

window.csharpHighlighting = {
    // Constants for span detection lookback/lookahead limits
    SPAN_LOOKBACK_LIMIT: 50,
    SPAN_LOOKAHEAD_LIMIT: 20,

    // C# keywords that should not be treated as identifiers
    keywords: new Set([
        'abstract', 'as', 'base', 'bool', 'break', 'byte', 'case', 'catch',
        'char', 'checked', 'class', 'const', 'continue', 'decimal', 'default',
        'delegate', 'do', 'double', 'else', 'enum', 'event', 'explicit',
        'extern', 'false', 'finally', 'fixed', 'float', 'for', 'foreach',
        'goto', 'if', 'implicit', 'in', 'int', 'interface', 'internal', 'is',
        'lock', 'long', 'namespace', 'new', 'null', 'object', 'operator',
        'out', 'override', 'params', 'private', 'protected', 'public',
        'readonly', 'ref', 'return', 'sbyte', 'sealed', 'short', 'sizeof',
        'stackalloc', 'static', 'string', 'struct', 'switch', 'this', 'throw',
        'true', 'try', 'typeof', 'uint', 'ulong', 'unchecked', 'unsafe',
        'ushort', 'using', 'virtual', 'void', 'volatile', 'while',
        'async', 'await', 'var', 'dynamic', 'yield', 'partial', 'global',
        'where', 'select', 'from', 'orderby', 'ascending', 'descending',
        'group', 'into', 'join', 'let', 'on', 'equals', 'by', 'get', 'set',
        'add', 'remove', 'value', 'init', 'record', 'with', 'and', 'or', 'not',
        'when', 'nameof', 'required', 'scoped', 'file', 'allows', 'notnull'
    ]),

    // Check if position is inside an existing span by looking at surrounding context
    isInsideSpan: function(fullString, offset, lookback) {
        const beforeMatch = fullString.substring(Math.max(0, offset - lookback), offset);
        return beforeMatch.includes('<span') && !beforeMatch.includes('</span>');
    },

    // Enhanced highlighting for C# code blocks
    enhanceHighlighting: function() {
        const codeBlocks = document.querySelectorAll('code.language-csharp');
        
        codeBlocks.forEach(codeBlock => {
            // Skip if already enhanced
            if (codeBlock.dataset.enhanced === 'true') return;
            codeBlock.dataset.enhanced = 'true';

            // Get the HTML content and process it
            let html = codeBlock.innerHTML;
            
            // Process generic types like List<T>, IEnumerable<T>, etc.
            html = this.processGenericTypes(html);
            
            // Process type names after 'new' keyword
            html = this.processNewExpressions(html);
            
            // Process method calls (identifier followed by parenthesis)
            html = this.processMethodCalls(html);
            
            // Process property access (identifier after dot, not followed by parenthesis)
            html = this.processPropertyAccess(html);
            
            // Process lambda parameters
            html = this.processLambdaParameters(html);

            codeBlock.innerHTML = html;
        });
    },

    // Process generic types like List<OrderDto>, IQueryable<T>
    processGenericTypes: function(html) {
        // Match PascalCase identifiers followed by &lt; (HTML-encoded <)
        // We need to be careful not to match inside existing spans
        const self = this;
        
        // Process text nodes only - match TypeName&lt; patterns
        return html.replace(/([A-Z][a-zA-Z0-9]*)(&lt;)/g, function(match, typeName, bracket, offset, fullString) {
            // Check if this is inside a span (look for unbalanced tags before)
            if (self.isInsideSpan(fullString, offset, self.SPAN_LOOKBACK_LIMIT)) {
                return match; // Skip if inside a span
            }
            if (self.keywords.has(typeName)) return match;
            return '<span class="token type-name">' + typeName + '</span>' + bracket;
        });
    },

    // Process 'new TypeName' expressions
    processNewExpressions: function(html) {
        const self = this;
        // Match: <span class="token keyword">new</span> followed by whitespace and PascalCase name
        return html.replace(/(<span class="token keyword">new<\/span>)(\s+)([A-Z][a-zA-Z0-9]*)/g, function(match, newSpan, space, typeName) {
            if (self.keywords.has(typeName)) return match;
            // Check if already wrapped
            if (typeName.includes('class="token')) return match;
            return newSpan + space + '<span class="token type-name">' + typeName + '</span>';
        });
    },

    // Process method calls (identifier followed by open parenthesis)
    processMethodCalls: function(html) {
        const self = this;
        
        // Match: .MethodName( pattern
        html = html.replace(/(\.)([A-Z][a-zA-Z0-9]*)(\s*\()/g, function(match, dot, methodName, paren, offset, fullString) {
            // Check if this is inside a span
            if (self.isInsideSpan(fullString, offset, self.SPAN_LOOKBACK_LIMIT)) {
                return match;
            }
            if (self.keywords.has(methodName)) return match;
            return dot + '<span class="token method-name">' + methodName + '</span>' + paren;
        });

        // Also process standalone method calls like SelectExpr(
        html = html.replace(/(^|[^.\w>])([A-Z][a-zA-Z0-9]*)(\s*\()/gm, function(match, prefix, methodName, paren, offset, fullString) {
            // Check if this is inside a span
            if (self.isInsideSpan(fullString, offset, self.SPAN_LOOKBACK_LIMIT)) {
                return match;
            }
            if (self.keywords.has(methodName)) return match;
            // Skip if it looks like a type name in a generic context
            if (prefix === '') return match;
            return prefix + '<span class="token method-name">' + methodName + '</span>' + paren;
        });

        return html;
    },

    // Process property access (identifier after dot, not followed by parenthesis)
    processPropertyAccess: function(html) {
        const self = this;
        
        // Match: .PropertyName where not followed by (
        // Use a more careful pattern
        return html.replace(/(\.)([A-Z][a-zA-Z0-9]*)(?!\s*\()/g, function(match, dot, propName, offset, fullString) {
            // Check if this is inside a span
            if (self.isInsideSpan(fullString, offset, self.SPAN_LOOKBACK_LIMIT)) {
                return match;
            }
            // Check if already processed (look ahead for closing span tag)
            const afterMatch = fullString.substring(offset + match.length, offset + match.length + self.SPAN_LOOKAHEAD_LIMIT);
            if (afterMatch.startsWith('</span>')) {
                return match;
            }
            if (self.keywords.has(propName)) return match;
            return dot + '<span class="token property-access">' + propName + '</span>';
        });
    },

    // Process lambda parameters (x => or (x) =>)
    processLambdaParameters: function(html) {
        const self = this;
        
        // Match: identifier followed by => (HTML: =&gt;)
        html = html.replace(/([a-z_][a-zA-Z0-9]*)(\s*)(=&gt;)/g, function(match, param, space, arrow, offset, fullString) {
            // Check if this is inside a span
            if (self.isInsideSpan(fullString, offset, self.SPAN_LOOKBACK_LIMIT)) {
                return match;
            }
            if (self.keywords.has(param)) return match;
            return '<span class="token parameter-name">' + param + '</span>' + space + arrow;
        });

        // Match: (identifier) followed by => 
        html = html.replace(/\(([a-z_][a-zA-Z0-9]*)\)(\s*)(=&gt;)/g, function(match, param, space, arrow, offset, fullString) {
            // Check if this is inside a span
            if (self.isInsideSpan(fullString, offset, self.SPAN_LOOKBACK_LIMIT)) {
                return match;
            }
            if (self.keywords.has(param)) return match;
            return '(<span class="token parameter-name">' + param + '</span>)' + space + arrow;
        });

        return html;
    },

    // Initialize: hook into Prism's highlight callback
    init: function() {
        // Run enhancement after Prism highlights
        if (typeof Prism !== 'undefined') {
            const self = this;
            Prism.hooks.add('complete', function(env) {
                if (env.language === 'csharp') {
                    // Use requestAnimationFrame for better performance than setTimeout(0)
                    requestAnimationFrame(function() { self.enhanceHighlighting(); });
                }
            });
        }
    }
};

// Auto-initialize when script loads
document.addEventListener('DOMContentLoaded', function() {
    window.csharpHighlighting.init();
});
