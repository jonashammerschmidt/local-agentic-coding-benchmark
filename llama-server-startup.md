## Model

```bash
cd ~/models
llama-server \
  --model Qwen3-Coder-Next-UD-Q4_K_XL.gguf \
  --alias Qwen3-Coder-Next \
  --port 8001 \
  --n-gpu-layers 999 \
  --ctx-size 131072 \
  --parallel 4 \
  --jinja
```

## Tools

`~/.config/opencode/opencode.json`

```
{
  "$schema": "https://opencode.ai/config.json",
  "provider": {
    "local-llama": {
      "npm": "@ai-sdk/openai-compatible",
      "name": "llama-server (local)",
      "options": {
        "baseURL": "http://127.0.0.1:8001/v1",
        "apiKey": "sk-no-key-required",
        "timeout": 3000000,
        "chunkTimeout": 3000000,
        "setCacheKey": true
      },
      "models": {
        "Qwen3-Coder-Next": {
          "name": "Qwen3-Coder-Next (local)",
          "tool_call": true,
          "reasoning": true,
          "limit": {
            "context": 131072,
            "output": 4096
          }
        }
      }
    }
  },
  "model": "local-llama/Qwen3-Coder-Next"
}
```

opencode web