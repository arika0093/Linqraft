/* Prism.js - Minimal implementation for C# syntax highlighting with line numbers */
(function() {
    var Prism = window.Prism = {
        languages: {},
        highlightAll: function() {
            var elements = document.querySelectorAll('code[class*="language-"]');
            elements.forEach(function(element) {
                Prism.highlightElement(element);
            });
        },
        highlightElement: function(element) {
            var language = element.className.match(/language-(\w+)/);
            if (language && Prism.languages[language[1]]) {
                var grammar = Prism.languages[language[1]];
                var code = element.textContent;
                element.innerHTML = Prism.highlight(code, grammar);
            }
            
            // Add line numbers
            var pre = element.parentElement;
            if (pre && pre.classList.contains('line-numbers')) {
                var lines = element.textContent.split('\n');
                var lineNumbersWrapper = document.createElement('span');
                lineNumbersWrapper.className = 'line-numbers-rows';
                for (var i = 0; i < lines.length; i++) {
                    lineNumbersWrapper.appendChild(document.createElement('span'));
                }
                pre.appendChild(lineNumbersWrapper);
            }
        },
        highlight: function(text, grammar) {
            var tokens = Prism.tokenize(text, grammar);
            return Prism.stringify(tokens);
        },
        tokenize: function(text, grammar) {
            var tokens = [text];
            for (var token in grammar) {
                var pattern = grammar[token];
                var regex = pattern.pattern || pattern;
                var inside = pattern.inside;
                
                for (var i = 0; i < tokens.length; i++) {
                    var str = tokens[i];
                    if (typeof str !== 'string') continue;
                    
                    var match;
                    var newTokens = [];
                    var lastIndex = 0;
                    
                    regex.lastIndex = 0;
                    while ((match = regex.exec(str)) !== null) {
                        if (match.index > lastIndex) {
                            newTokens.push(str.slice(lastIndex, match.index));
                        }
                        var content = match[0];
                        if (inside) {
                            content = Prism.tokenize(content, inside);
                        }
                        newTokens.push({ type: token, content: content });
                        lastIndex = match.index + match[0].length;
                    }
                    
                    if (lastIndex < str.length) {
                        newTokens.push(str.slice(lastIndex));
                    }
                    
                    if (newTokens.length > 0) {
                        tokens.splice(i, 1, ...newTokens);
                        i += newTokens.length - 1;
                    }
                }
            }
            return tokens;
        },
        stringify: function(tokens) {
            if (typeof tokens === 'string') {
                return Prism.encode(tokens);
            }
            if (Array.isArray(tokens)) {
                return tokens.map(Prism.stringify).join('');
            }
            var content = Array.isArray(tokens.content) 
                ? tokens.content.map(Prism.stringify).join('') 
                : Prism.stringify(tokens.content);
            return '<span class="token ' + tokens.type + '">' + content + '</span>';
        },
        encode: function(text) {
            return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        }
    };

    // C# language definition
    Prism.languages.csharp = {
        'comment': {
            pattern: /(\/\/.*$|\/\*[\s\S]*?\*\/)/gm
        },
        'string': {
            pattern: /(@"(?:[^"]|"")*"|"(?:\\.|[^\\"\r\n])*")/g
        },
        'keyword': {
            pattern: /\b(abstract|as|async|await|base|bool|break|byte|case|catch|char|checked|class|const|continue|decimal|default|delegate|do|double|else|enum|event|explicit|extern|false|finally|fixed|float|for|foreach|goto|if|implicit|in|int|interface|internal|is|lock|long|namespace|new|null|object|operator|out|override|params|private|protected|public|readonly|ref|return|sbyte|sealed|short|sizeof|stackalloc|static|string|struct|switch|this|throw|true|try|typeof|uint|ulong|unchecked|unsafe|ushort|using|var|virtual|void|volatile|while|yield|get|set|value|partial|where|add|remove|global|required)\b/g
        },
        'type-name': {
            pattern: /\b([A-Z][a-zA-Z0-9]*(?:<[^>]+>)?)\b/g
        },
        'number': {
            pattern: /\b(\d+\.?\d*[fFdDmM]?|\.\d+[fFdDmM]?|0x[0-9A-Fa-f]+)\b/g
        },
        'operator': {
            pattern: /([+\-*/%&|^!<>=?:]+|=>)/g
        },
        'punctuation': {
            pattern: /([{}[\];(),.])/g
        }
    };
    
    // Alias
    Prism.languages.cs = Prism.languages.csharp;
})();
