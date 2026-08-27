# lua-math

A **pure Lua** library that ships a `.d.neb` declaration alongside its
`init.lua` so Nebra consumers can call it with full type information.

## Layout

```
lua-math/
├-- nebra.toml      -- still useful for `nebra install` to pick up the package name
├-- init.lua      -- the actual implementation (`lerp`, `clamp`, `sum`, …)
└-- init.d.neb    -- declare module "lua-math" with the matching Nebra types
```