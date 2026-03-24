# Local Agentic Coding Benchmarks

Minimaler C#-Orchestrator fuer lokale Coding-Agent-Benchmarks.

## Konfiguration

```yaml
version: 1

defaults:
  artifactsRoot: .benchmarks/runs
  reportsRoot: .benchmarks/reports
  timeoutSeconds: 900
  warmupPrompt: "Say Hello World!"

tools:
  - id: codex
    enabled: true
  - id: opencode
    enabled: true
  - id: claude
    enabled: true

models:
  - id: qwen3-coder-next:q4_K_M
    provider: ollama
    enabled: true

tasks:
  - id: run-tests
    repoPath: ~/src/my-repo
    prompt: "Fuehre die Tests aus."
```

## Befehle

```bash
dotnet run --project src/cli -- run benchmark.yaml
dotnet run --project src/cli -- report benchmark.yaml
dotnet run --project test/runner/LocalAgenticCodingBenchmark.TestRunner.csproj
```

## Artefakte

Jeder Run schreibt:

- `run.json`
- `agent-output.md`
- `stdout.log`
- `stderr.log`
- `code-diff.patch`
- `warmup-stdout.log`
- `warmup-stderr.log`
