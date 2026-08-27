# zoo-app

The consumer in the `examples/` triple. Pulls in
[`nebra-strings`](../nebra-strings) and [`lua-math`](../lua-math) through the
package manager (file dependencies) and exercises the import / module / type
system end to end.

## What it demonstrates

| | |
|-|-|
| **Nebra → Nebra import** | `import { trim, padLeft, startsWith } from "nebra-strings"` resolves to the linked Nebra source and compiles to `require("nebra-strings")`. |
| **Sub-module import** | `import { capitalize } from "nebra-strings/case"` resolves to `nebra_modules/nebra-strings/case.lua`. |
| **Lua → Nebra import via .d.neb** | `import { lerp, clamp, vec2 } from "lua-math"` finds the `declare module "lua-math"` block in `init.d.neb`; the type-checker uses it while the generated code falls through to the real `init.lua`. |
| **Cross-package types** | `Vec2` (declared in lua-math) flows through `length2` and stays type-safe in zoo-app. |
| **Local file import** | `import { formatLabel, padCols } from "utils"` picks up the sibling `src/utils.neb` — no package-manager wiring needed. |
| **Local folder-as-module** | `import { kinds } from "animals"` resolves to `src/animals/init.neb` because the folder has an `init.neb`. |
| **Local submodule import** | `import { makeCat } from "animals/cat"` resolves to `src/animals/cat.neb` — a file inside a folder, addressable with a slash. |

## Run it

```bash
# 1. Build the Nebra library so its .lua files exist alongside the sources.
#    (lua-math already ships its init.lua, no build needed.)
cd ../nebra-strings && nebra build

# 2. Wire the file deps into nebra_modules/.
cd ../zoo-app && nebra install

# 3. Compile + execute via the embedded runtime.
nebra run
```

Expected output:

```
  > Welcome, Whiskers!
loud (95 dB)
quiet (120 dB)
|origin| = 5.0
lerp(0,10,0.25) = 2.5
sum(1..5) = 15
kinds => cat, dog
Whiskers   => meow
Rex        => woof
```