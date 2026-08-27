# annotation-demo

Minimal showcase of Nebra's compile-time annotation system. The `@log`
annotation defined in `annotations/log.neb` prepends a `print("[log] …")`
statement to the body of every function it decorates, so the generated Lua
behaves as if the call had been written by hand.

## Layout

```
annotation-demo/
├── nebra.toml              # project config; registers ./annotations/
├── annotations/
│   └── log.neb           # @log definition (meta + apply hook)
└── src/
    └── main.neb          # uses @log on three functions
```

The relevant bit of `nebra.toml`:

```toml
annotations = ["annotations"]

[code]
index_base = 1
```

`annotations = […]` tells the compiler which directories to scan for
annotation definitions. Every `.neb` file in there that exports
`annotation = { … }` and an `apply` function becomes available as `@name`
project-wide. `index_base = 1` aligns Nebra array indexing with Lua's
native 1-based indexing so the `apply` hook can write `target.namePath[1]`
without surprises (the IR helpers run inside a stock Lua sandbox).

## How `@log` works

`annotations/log.neb` declares the annotation metadata and the transform:

```lua
export local annotation = {
    target = "Function",
    params = {
        message = { type = "string", required = false }
    }
}

export function apply(target, args)
    local fnName = target.namePath[1].name
    local label = args.message or fnName
    local logStmt = ir.exprStmt(ir.call("print", {
        ir.stringLiteral("[log] " .. label)
    }))
    table.insert(target.body, 1, logStmt)
    return target
end
```

- `annotation.target` whitelists what `@log` can decorate (here: top-level
  functions). The compiler rejects `@log` on classes, fields, etc.
- `annotation.params` declares the typed argument schema. `@log("msg")`
  binds positionally; `@log(message = "msg")` binds by name.
- `apply(target, args)` receives the encoded `FunctionDecl` as a Lua
  table and the validated `args` dictionary. It mutates `target.body`
  via the `ir` helper module (`runtime/ir_helpers.lua`, auto-loaded into
  the sandbox) and returns the replacement node.

The compiler then splices the rewritten declaration back into the program
and continues with name resolution, type checking and codegen as if the
function had always looked that way.

## Building and running

```sh
cd examples/annotation-demo
nebra build
lua out/main.lua
```

Expected output:

```
[log] greet
Hello, Nebra!
[log] computing sum
2 + 3 =	5
[log] stringifying number
the number is 42
```

The `[log] …` lines come from the prepended `print` calls. Inspect
`out/main.lua` to see exactly what the compiler emitted &mdash; the
annotation produces vanilla Lua with no runtime support library.
