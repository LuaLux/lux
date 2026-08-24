<p align="center">
  <img src="assets/banner.png" alt="Lux" width="200px" />
</p>

<p align="center">
  <strong>A typed superset of Lua that transpiles to clean, portable Lua.</strong><br/>
  Classes, generics, pattern matching, async/await, modules, and a package manager &mdash; with zero runtime overhead on the Lua side.
</p>

<p align="center">
  <a href="https://github.com/LuaLux/lux/actions/workflows/release.yml"><img src="https://github.com/LuaLux/lux/actions/workflows/release.yml/badge.svg" alt="Release" /></a>
  <a href="https://github.com/LuaLux/lux/releases/latest"><img src="https://img.shields.io/github/v/release/LuaLux/lux?display_name=tag&sort=semver" alt="Latest release" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/LuaLux/lux" alt="License" /></a>
  <a href="https://github.com/LuaLux/lux/issues"><img src="https://img.shields.io/github/issues/LuaLux/lux" alt="Issues" /></a>
</p>

<p align="center">
  <a href="#why-lux">Why Lux</a> &bull;
  <a href="#install">Install</a> &bull;
  <a href="#five-minute-tour">Tour</a> &bull;
  <a href="#cli">CLI</a> &bull;
  <a href="#package-manager">Packages</a> &bull;
  <a href="https://lualux.github.io">Docs</a> &bull;
  <a href="examples/">Examples</a> &bull;
  <a href="#contributing">Contributing</a>
</p>

---

## Why Lux?

Lua is small, fast and embeddable &mdash; but writing big programs in it hurts: no static types, no module discipline, no class story, no async story. Lux fixes that **without leaving Lua**:

- Every valid Lua program is valid Lux &mdash; **types are optional**.
- The output is **idiomatic Lua** for any target between 5.1 and 5.4 (plus LuaJIT). No runtime library shipped, no magic at run-time.
- A full set of modern features are **lowered at compile time**: classes, generics, interfaces, pattern matching, `async/await`, doc comments, decorators-via-annotations, an ES-style module system, a strict-nil mode, and an immutability mode.
- A complete **toolchain** ships in one binary: compiler, interactive REPL, test runner, native binary bundler, package manager, language server, docs generator.

```lua
import { sum, clamp } from "lua-math"

class Counter
    count: number = 0

    function bump(by: number = 1): number
        self.count = self.count + by
        return self.count
    end
end

async function fetch(url: string): string
    return await http.get(url)
end

local c = new Counter()
c:bump()
c:bump(5)
print(c.count)              -- 6
print(clamp(c.count, 0, 5))  -- 5
```

Lux compiles the above into clean Lua &mdash; no runtime library, no helpers you can't read.

---

## Install

### One-line install (recommended)

Detects your OS + architecture, pulls the latest release archive, extracts it, and wires `lux` into your `PATH`. No admin rights required &mdash; everything lands under your user directory.

**Linux / macOS (bash / zsh):**

```bash
curl -fsSL https://raw.githubusercontent.com/LuaLux/lux/master/scripts/install.sh | bash
```

**Linux / macOS (fish &mdash; e.g. CachyOS):**

```fish
curl -fsSL https://raw.githubusercontent.com/LuaLux/lux/master/scripts/install.fish | fish
```

**Windows (PowerShell 5.1+):**

```powershell
irm https://raw.githubusercontent.com/LuaLux/lux/master/scripts/install.ps1 | iex
```

Open a new shell after the script finishes and `lux version` should resolve. Pin a specific tag with `LUX_VERSION=v0.2.0` (bash / fish) or `$env:LUX_VERSION = "v0.2.0"` (PowerShell) before running the installer.

### Manual install

If you prefer to handle PATH yourself, grab the archive for your platform from the [latest release](https://github.com/LuaLux/lux/releases/latest):

| Platform        | Archive                                |
|-----------------|----------------------------------------|
| Linux x64       | `lux-linux-x64.tar.gz`                 |
| Linux arm64     | `lux-linux-arm64.tar.gz`               |
| macOS x64       | `lux-osx-x64.tar.gz`                   |
| macOS arm64     | `lux-osx-arm64.tar.gz` (Apple Silicon) |
| Windows x64     | `lux-win-x64.zip`                      |
| Windows arm64   | `lux-win-arm64.zip`                    |

Each archive contains a single self-contained `lux` (or `lux.exe`) binary &mdash; **no .NET runtime, no Lua runtime required on the target machine**. The Lua 5.4 interpreter (via KeraLua), the Lux compiler, and all stdlib type declarations are embedded.

Extract somewhere on your `PATH`:

```bash
# Linux / macOS
tar xzf lux-linux-x64.tar.gz
sudo mv lux /usr/local/bin/
lux version
```

### From source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/LuaLux/lux.git
cd lux
dotnet build Lux.sln
./compiler/bin/Debug/net10.0/Lux version
```

### Editor support

The [**Lux VS Code extension**](https://marketplace.visualstudio.com/items?itemName=DasDarki.lux-lang) is on the Marketplace. Install it from inside VS Code (Quick Open `Ctrl+P` / `Cmd+P`):

```
ext install DasDarki.lux-lang
```

Or via the CLI:

```bash
code --install-extension DasDarki.lux-lang
```

Prefer to build from source? The extension lives in [`vscode-lux/`](vscode-lux/):

```bash
cd vscode-lux
npm install && npm run package
code --install-extension lux-*.vsix
```

The extension launches the bundled `lux lps` language server &mdash; you get hover, go-to-definition, completion, rename, find-references, semantic highlighting, code actions ("Implement interface", "Auto-import"), and signature help out of the box.

---

## Five-minute tour

### Scaffold a project

```bash
lux init
```

You get a `lux.toml`, a `src/` folder, and a `.gitignore`. Drop a `src/main.lux`:

```lua
function greet(name: string): string
    return "Hello, " .. name .. "!"
end

print(greet("Lux"))
```

### Build, run, or just iterate

```bash
lux build        # → out/main.lua
lux watch        # recompile src/**/*.lux on every change (Ctrl+C to stop)
lux run          # compile + run via embedded Lua 5.4
lux repl         # interactive prompt (state survives across inputs)
```

`lux watch` recompiles the whole project whenever a `*.lux` file under `src/` changes
(recursively, debounced), much like `dotnet watch` or `tstl --watch`. Errors are printed
and the watcher keeps running. Tune the debounce window with `--debounce <ms>` (default 300).

### Types are optional, inference does the rest

```lua
local x = 42            -- inferred number
local name = "Lux"       -- inferred string
local arr = {1, 2, 3}    -- inferred number[]

function len<T>(xs: T[]): number  -- generics
    return #xs
end
```

### Strict-nil mode kills the billion-dollar mistake

```toml
preset = "strict"
```

```lua
local name: string? = maybeName()
print(name:upper())             -- ✗ compile error: name may be nil
print(name!:upper())             -- ✓ explicit non-null assertion
print(name?:upper())             -- ✓ optional chaining: yields nil if name is nil
print((name ?? "anon"):upper())  -- ✓ nil-coalescing
```

### Classes + interfaces

```lua
interface Greetable
    function greet(): string
end

abstract class Animal
    name: string
    legs: number = 4

    constructor(name: string)
        self.name = name
    end

    abstract function speak(): string
end

class Cat extends Animal implements Greetable
    constructor(name: string)
        super(name)
    end

    override function speak(): string
        return "meow"
    end

    function greet(): string
        return "hi, I'm " .. self.name
    end
end
```

Generated Lua uses metatables and `setmetatable(self, Class)` &mdash; no helper library, no shimming.

### Pattern matching with exhaustiveness checks

```lua
enum Status { Pending, Done, Failed }

local result = match status
    case Status.Pending then "still working"
    case Status.Done then "✓"
    case Status.Failed when retries < 3 then "retrying"
    case _ then "giving up"
end
```

Type-patterns work too: `case x: Dog then x:bark()`.

### Async / await on coroutines

```lua
async function load(url: string): string
    return await http.get(url)
end

local body = await load("https://example.com")
```

Compiles to a coroutine + a `__done` callback. No external scheduler required &mdash; works on every Lua target.

### Modules, the ES way

```lua
-- math/vec2.lux
export class Vec2
    x: number
    y: number
    constructor(x: number, y: number) self.x, self.y = x, y end
end

-- main.lux
import { Vec2 } from "math/vec2"
import * as utils from "lib/utils"
import "polyfill"            -- side-effect import

local v = new Vec2(1, 2)
```

### Declaration files

Type any existing Lua code without recompiling it. `stdlib/std.d.lux` ships built-in declarations for `print`, `string`, `math`, `table`, `io`, `os`, etc. Add your own with `globals = ["lib/myproject.d.lux"]` in `lux.toml`.

```lua
-- redis.d.lux
declare module "redis"
    function connect(host: string, port: number): RedisClient
    interface RedisClient
        function get(key: string): string?
        function set(key: string, value: string): boolean
    end
end
```

### Annotations (compile-time metaprogramming)

```lua
@deprecated("use Vec2 instead")
function oldVec(x, y): { x: number, y: number }
    return { x = x, y = y }
end
```

Annotations are Lux functions that run at compile time and rewrite the IR &mdash; powerful enough to implement decorators, lazy initialization, runtime validators, anything you can express by transforming a syntax tree.

### Bundle as a standalone binary

```bash
lux compile                 # produces ./<project-name> on Linux/macOS
lux compile --out ./myapp   # custom output path
```

The result is a single self-contained executable: your compiled Lua + KeraLua + all transitive deps from `lux_modules/`, packed into one file. Ships on a machine that has neither Lua nor .NET.

---

## CLI

| Command                  | Description                                              |
|--------------------------|----------------------------------------------------------|
| `lux init`               | Scaffold a new project in the current directory          |
| `lux create <spec>`      | Scaffold from a git template (e.g. `gh:owner/template`)  |
| `lux build [files...]`   | Compile the project (or specific files) to Lua           |
| `lux watch`              | Recompile `src/**/*.lux` on every change (debounced)     |
| `lux run [files...] [-- args]` | Compile and execute via embedded Lua 5.4           |
| `lux test [filter]`      | Discover and run unit tests (`*_test.lux`, `tests/`)     |
| `lux repl`               | Interactive REPL with persistent runtime state           |
| `lux compile`            | Bundle the project into a standalone native binary       |
| `lux docs [--out dir]`   | Generate documentation site (Markdown + HTML)            |
| `lux install`            | Install dependencies into `lux_modules/`                 |
| `lux add <spec>`         | Add a runtime dependency (e.g. `github:owner/repo@v1`)   |
| `lux remove <name>`      | Remove a declared dependency                             |
| `lux registry refresh`   | Refresh the cached alias registry                        |
| `lux lps`                | Start the LSP language server over stdio                 |
| `lux version`            | Print the Lux version                                    |
| `lux help`               | Show CLI help                                            |

Detailed flags and behavior live in the [CLI reference](https://lualux.github.io/docs/toolchain/cli).

---

## Package Manager

Lux ships with a built-in, git-based package manager. Dependencies are declared in `lux.toml` and installed into a local `lux_modules/` directory:

```toml
[dependencies]
lux-strings = "github:DasDarki/lux-strings@v1.2.0"
lua-math    = { git = "https://example.com/lua-math.git", tag = "v0.5.0" }
my-utils    = "file:../my-utils"   # local path for development
```

```bash
lux install          # fetch + link everything
lux add github:owner/cool-lib@v1
lux remove cool-lib
```

**Monorepos** — when a single repository contains more than one Lux package, point at the
package's subdirectory. Either append the in-repo path to the host shortcut, or use the
table form with `subdir`:

```bash
lux add github:owner/monorepo/packages/math@v1   # path after owner/repo = subdir
```

```toml
[dependencies]
math = { git = "https://github.com/owner/monorepo.git", tag = "v1", subdir = "packages/math" }
```

Lux sparse-checks only that subdirectory and reads its `lux.toml` for the dependency name.

The package manager is roundtrip-safe with `lux.toml` (preserves formatting + comments on `lux add`/`remove`), supports lifecycle scripts gated behind `--allow-scripts`, and resolves transitive dependencies via per-package `lux.toml` files.

See the [package manager guide](https://lualux.github.io/docs/toolchain/package-manager) for the full specification.

---

## Project Structure

```
.
├-- compiler/          Lux compiler + CLI (.NET 10)
│   ├-- Compiler/       Pass pipeline (ResolveLibs → BindDeclare → … → Codegen)
│   ├-- IR/             High-level IR (Node hierarchy, ScopeGraph, SymbolArena, TypeTable)
│   ├-- Configuration/  lux.toml schema
│   ├-- Diagnostics/    Error/warning bag with formatted codes
│   ├-- PackageManager/ Git-based dependency installer
│   ├-- LPS/            Language server (OmniSharp LSP framework)
│   ├-- Doc/            Doc comment parser + markdown/HTML renderer
│   └-- stdlib/         Built-in .d.lux declarations + test framework Lua
├-- runtime/           Embedded Lua 5.4 runtime (KeraLua wrapper, stdlib bindings)
├-- examples/          Example projects (lux-strings, lua-math, zoo-app)
├-- test/              Runtime test suite (164 tests, all passing)
├-- vscode-lux/        VS Code extension source
└-- assets/            Logo & branding
```

The compiler and runtime are split: `compiler/Lux.csproj` produces the `lux` CLI and references `runtime/Lux.Runtime.csproj`. The runtime project contains everything a standalone binary produced by `lux compile` needs &mdash; no compiler types.

---

## Architecture

```
.lux source
    ↓ ANTLR4 lexer + parser
CST
    ↓ IRVisitor (visitor over the parse tree)
HIR (Node tree)
    ↓ Pass pipeline:
    │   ResolveLibs       Load .d.lux declarations + installed packages
    │   ResolveAnnotations  Pre-load annotation plugins
    │   ApplyAnnotations  Run compile-time IR rewrites
    │   BindDeclare       Build scope graph + declare symbols
    │   ResolveImports    Inject imported module ASTs into the package
    │   ResolveNames      Bind every NameRef to its SymID
    │   ResolveTypeRefs   Resolve type annotations to TypIDs
    │   CheckImmutability Enforce const + deep-freeze rules
    │   InferTypes        Propagate types, narrow nilability, check operators
    │   ValidateGenericConstraints
    │   DetectUnused      Mark unreferenced symbols for stripping
    │   DeclGen           Emit .d.lux declarations for the project (optional)
    │   Mangle            Rename for minification (optional)
    │   Codegen           Emit target Lua source
    ↓
.lua output
```

Each pass declares its scope (per-file or per-build) and dependencies. The `PassManager` topologically orders them. A separate `CheckPipeline` (same minus `Mangle` and `Codegen`) is used by the language server for fast incremental feedback while you type.

---

## Documentation

The full language reference, guides and examples live at **[lualux.github.io](https://lualux.github.io)**.

- [Getting Started](https://lualux.github.io/docs/getting-started/installation) - install, first project, editor setup
- [Type System](https://lualux.github.io/docs/language/types) - primitives, unions, generics, `never`
- [Classes](https://lualux.github.io/docs/language/classes) - inheritance, abstract, `protected`, `static`, operators
- [Interfaces](https://lualux.github.io/docs/language/interfaces) - `implements`, interface inheritance, default methods
- [Modules](https://lualux.github.io/docs/language/modules) - `import`/`export`, declaration modules
- [Pattern Matching](https://lualux.github.io/docs/language/pattern-matching) - value/type/wildcard patterns, guards
- [Nilability](https://lualux.github.io/docs/language/nilability) - strict-nil, `??`, `!`, `?.`, flow narrowing
- [CLI Reference](https://lualux.github.io/docs/toolchain/cli) - every command, every flag
- [Package Manager](https://lualux.github.io/docs/toolchain/package-manager) - dependency specs, install pipeline
- [Annotations](https://lualux.github.io/docs/advanced/annotations) - compile-time IR rewrites
- [Configuration](https://lualux.github.io/docs/advanced/configuration) - every `lux.toml` key
- [Examples](https://lualux.github.io/docs/examples/overview) - complete, runnable programs

The site is built from [LuaLux/lualux.github.io](https://github.com/LuaLux/lualux.github.io).
---

## Examples

Three runnable projects in [`examples/`](examples/):

- [`lux-strings/`](examples/lux-strings/) &mdash; a tiny Lux library exposing string utilities (`trim`, `padLeft`, `startsWith`, `capitalize`).
- [`lua-math/`](examples/lua-math/) &mdash; a pre-built Lua library with typed `.d.lux` declarations (`clamp`, `lerp`, `vec2`, `length2`).
- [`zoo-app/`](examples/zoo-app/) &mdash; an app that consumes both libraries via the package manager. Demonstrates cross-language imports and standalone binary bundling.

```bash
cd examples/zoo-app
lux install      # pulls lux-strings + lua-math
lux run          # → Welcome, Whiskers! …
lux compile      # → ./zoo-app  (standalone)
```

---

## Roadmap

Current status:

- ✅ Type system with inference, nilability, generics, exhaustive matching, immutability, operator overloading
- ✅ Classes, interfaces, abstract, override, protected, static, operator overloading
- ✅ Pattern matching, async/await, defer/guard, label-free continue, multi-level break
- ✅ Module system, declaration files, declaration generation
- ✅ Annotations (compile-time IR rewriting)
- ✅ Package manager (5 phases: install, toml round-trip, alias registry, auto-discovery, lifecycle scripts)
- ✅ Doc comments + markdown/HTML doc generation
- ✅ Embedded Lua 5.4 runtime + `lux run`
- ✅ Standard library declarations + stdlib bindings (HTTP, JSON, FS, Console, Project)
- ✅ Test runner (`lux test`) with built-in `lux:test` framework
- ✅ Standalone binary compiler (`lux compile`)
- ✅ Interactive REPL (`lux repl`)
- ✅ Language server (hover, go-to-def, completion, rename, references, code actions, sig help, semantic tokens)
- ⏳ Formatter for consistent code style

---

## Contributing

Issues and pull requests are welcome at [github.com/LuaLux/lux](https://github.com/LuaLux/lux). Before opening a PR:

1. Run the test suite &mdash; it must stay at 164/164.
   ```bash
   cd test && lux test
   ```
2. Keep generated parser files (`compiler/CodeAnalysis/`) out of your diff &mdash; regenerate via `cd compiler && ./gen_antlr4.sh` only when you touch the grammar.
3. Follow the existing style: XML doc comments on public APIs, no comments on obvious code.
4. New language features should also extend the LSP (`compiler/LPS/`) so editor support stays consistent.

For substantial changes, please open an issue first to discuss the design.

---

## License

[MIT](LICENSE) © DasDarki
