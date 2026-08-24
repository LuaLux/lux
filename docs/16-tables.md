# Tables

Tables are Lua's only data structure. Lux supports all Lua table syntax with optional type annotations.

## Table Constructors

### Array-Style

```lux
local arr = {1, 2, 3, 4, 5}
local names: string[] = {"Alice", "Bob", "Charlie"}
```

### Record-Style

```lux
local point = {x = 10, y = 20}
local config: {[string]: any} = {host = "localhost", port = 8080}
```

### Bracket Keys

```lux
local map = {
    ["key with spaces"] = 1,
    [42] = "number key",
    [true] = "bool key"
}
```

### Mixed

```lux
local mixed = {
    1, 2, 3,              -- positional
    name = "test",          -- named
    [10] = "explicit"       -- bracket
}
```

### Named Functions

A field may be written as a named function. It is shorthand for the `name = function ... end` form
and produces the same string key:

```lux
local ops = {
    function add(a: number, b: number): number
        return a + b
    end,
    function negate(v: number): number
        return -v
    end,
}

-- exactly equivalent to
local ops = {
    add = function(a: number, b: number): number
        return a + b
    end,
    negate = function(v: number): number
        return -v
    end,
}
```

`async` works the same way:

```lux
local io = {
    async function read(path: string): string
        return await fs.readFile(path)
    end,
}
```

The shorthand needs an identifier after `function`. A `function` followed by `(` is still an
anonymous function and still takes an integer key, so both forms mix freely:

```lux
local handlers = {
    function onStart() print "start" end,   -- key "onStart"
    function() print "anonymous" end,        -- key 1
    label = "tail",
}
```

The separator between fields is required, exactly as for any other field, and a trailing one is
allowed. Integer and computed keys still need the bracket form (`[1] = function() end`).

There is no `function t:method() end` form inside a table constructor. Write the `self` parameter
out when you want the field to be callable with `:`:

```lux
local counter = {
    total = 0,
    function bump(self: any, by: number): number
        self.total = self.total + by
        return self.total
    end,
}

counter:bump(3)
```

Calling a table field with `:` when it has no `self` parameter is a warning, because Lua passes the
receiver anyway and every declared parameter ends up shifted by one:

```lux
local t = { function greet(name: string) print(name) end }
t:greet("world")    -- warning: 'greet' declares no 'self' parameter
t.greet("world")    -- fine
```

### Empty Table

```lux
local empty = {}
local typed: number[] = {}
local map: {[string]: number} = {}
```

## Field Separators

Both `,` and `;` are valid separators. Trailing separator is allowed:

```lux
local a = {1, 2, 3,}
local b = {x = 1; y = 2; z = 3;}
```

## Type Annotations on Tables

```lux
-- Typed as array
local nums: number[] = {1, 2, 3}

-- Typed as map
local scores: {[string]: number} = {alice = 100, bob = 85}

-- Typed as struct
local user: {name: string, age: number} = {name = "Alice", age = 30}
```

## Index Base

With `index_base = 0` in config, array indices are adjusted from 0-based to Lua's 1-based:

```lux
local arr = {10, 20, 30}
print(arr[0])    -- compiles to arr[1], prints 10
print(arr[1])    -- compiles to arr[2], prints 20
```
