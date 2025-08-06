class SaturnChat {
    constructor() {
        this.messagesContainer = document.getElementById('messages');
        this.messageInput = document.getElementById('message-input');
        this.sendBtn = document.getElementById('send-btn');
        this.clearBtn = document.getElementById('clear-btn');
        this.statusIndicator = document.getElementById('status-indicator');
        this.statusText = document.getElementById('status-text');
        
        this.apiUrl = `http://localhost:${window.location.port || 8080}/api`;
        this.isProcessing = false;
        
        this.init();
    }
    
    init() {
        this.sendBtn.addEventListener('click', () => this.sendMessage());
        this.clearBtn.addEventListener('click', () => this.clearHistory());
        
        this.messageInput.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                this.sendMessage();
            }
        });
        
        this.checkStatus();
        setInterval(() => this.checkStatus(), 30000);
    }
    
    async checkStatus() {
        try {
            const response = await fetch(`${this.apiUrl}/status`);
            if (response.ok) {
                const data = await response.json();
                this.setStatus(true, `Agent: ${data.agent} | Model: ${data.model}`);
            } else {
                this.setStatus(false, 'Server error');
            }
        } catch (error) {
            this.setStatus(false, 'Disconnected');
        }
    }
    
    setStatus(active, text) {
        this.statusIndicator.classList.toggle('active', active);
        this.statusText.textContent = text;
    }
    
    async sendMessage() {
        const message = this.messageInput.value.trim();
        if (!message || this.isProcessing) return;
        
        this.isProcessing = true;
        this.sendBtn.disabled = true;
        this.sendBtn.textContent = 'Processing...';
        
        this.addMessage(message, 'user');
        this.messageInput.value = '';
        
        const loadingId = this.addLoadingMessage();
        
        try {
            const response = await fetch(`${this.apiUrl}/chat`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({ message })
            });
            
            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }
            
            const data = await response.json();
            this.removeMessage(loadingId);
            
            let responseText = data.response;
            if (data.toolCalls > 0) {
                responseText += `\n\n*[Used ${data.toolCalls} tool${data.toolCalls > 1 ? 's' : ''}]*`;
            }
            
            this.addMessage(responseText, 'assistant');
        } catch (error) {
            this.removeMessage(loadingId);
            this.addMessage(`Error: ${error.message}`, 'error');
        } finally {
            this.isProcessing = false;
            this.sendBtn.disabled = false;
            this.sendBtn.textContent = 'Send';
        }
    }
    
    async clearHistory() {
        if (!confirm('Clear chat history?')) return;
        
        try {
            const response = await fetch(`${this.apiUrl}/clear`, {
                method: 'POST'
            });
            
            if (response.ok) {
                this.messagesContainer.innerHTML = '';
                this.addMessage('Chat history cleared', 'system');
            }
        } catch (error) {
            this.addMessage(`Error clearing history: ${error.message}`, 'error');
        }
    }
    
    addMessage(content, type) {
        const messageId = `msg-${Date.now()}-${Math.random()}`;
        const messageDiv = document.createElement('div');
        messageDiv.className = `message ${type}`;
        messageDiv.id = messageId;
        
        const contentDiv = document.createElement('div');
        contentDiv.className = 'message-content';
        
        if (type === 'assistant' || type === 'system') {
            contentDiv.innerHTML = this.formatMarkdown(content);
        } else {
            contentDiv.textContent = content;
        }
        
        messageDiv.appendChild(contentDiv);
        this.messagesContainer.appendChild(messageDiv);
        this.scrollToBottom();
        
        return messageId;
    }
    
    addLoadingMessage() {
        const messageId = `loading-${Date.now()}`;
        const messageDiv = document.createElement('div');
        messageDiv.className = 'message assistant';
        messageDiv.id = messageId;
        
        const contentDiv = document.createElement('div');
        contentDiv.className = 'message-content';
        contentDiv.innerHTML = '<div class="loading"></div>';
        
        messageDiv.appendChild(contentDiv);
        this.messagesContainer.appendChild(messageDiv);
        this.scrollToBottom();
        
        return messageId;
    }
    
    removeMessage(messageId) {
        const message = document.getElementById(messageId);
        if (message) {
            message.remove();
        }
    }
    
    formatMarkdown(text) {
        text = this.escapeHtml(text);
        
        text = text.replace(/```(\w+)?\n([\s\S]*?)```/g, (match, lang, code) => {
            return `<pre><code class="language-${lang || 'plaintext'}">${code.trim()}</code></pre>`;
        });
        
        text = text.replace(/`([^`]+)`/g, '<code>$1</code>');
        
        text = text.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
        
        text = text.replace(/\*([^*]+)\*/g, '<em>$1</em>');
        
        text = text.replace(/\n/g, '<br>');
        
        return text;
    }
    
    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
    
    scrollToBottom() {
        this.messagesContainer.scrollTop = this.messagesContainer.scrollHeight;
    }
}

document.addEventListener('DOMContentLoaded', () => {
    new SaturnChat();
});