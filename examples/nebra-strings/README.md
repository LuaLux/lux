# nebra-strings

A tiny **Nebra-only** library that exposes a handful of string utilities. The
implementation is fully written in Nebra; the generated Lua sits under `out/`
once you run `nebra build`.

## Layout

```
nebra-strings/
├-- nebra.toml      -- package manifest (source = ".")
├-- init.neb      -- main entry: trim / repeatN / padLeft / padRight / ...
└-- case.neb      -- sub-module: capitalize / lower / upper
```

Because the consumer's `nebra_modules/nebra-strings/` link points straight at this
directory, `init.neb` becomes the default entry and `case.neb` is reachable as
`nebra-strings/case`.

## Build / inspect

```bash
cd examples/nebra-strings
nebra build
```

`out/` ends up with the transpiled Lua and an `init.d.neb` declaration mirror.