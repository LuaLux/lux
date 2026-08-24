# Functions

## Named Functions

```lux
function add(a: number, b: number): number
    return a + b
end
```

## Local Functions

```lux
local function multiply(x: number, y: number): number
    return x * y
end
```

## Method Syntax

```lux
function obj:method(x: number): string
    return tostring(x)
end
```

## Anonymous Functions

```lux
local square = function(x: number): number
    return x * x
end

table.sort(items, function(a, b) return a < b end)
```

## Default Parameters

```lux
function greet(name: string, greeting: string = "Hello"): string
    return greeting .. ", " .. name
end

greet("Alice")              -- "Hello, Alice"
greet("Bob", "Hi")          -- "Hi, Bob"
```

Default parameters compile to `if param == nil then param = default end` checks at the start of the function body.

## Variadic Functions

### Standard Varargs

```lux
function printf(fmt: string, ...)
    print(string.format(fmt, ...))
end
```

### Named Varargs

Give a name and type to the variadic parameter:

```lux
function sum(...values: number): number
    local total = 0
    for _, v in ipairs({...}) do
        total = total + v
    end
    return total
end
```

## Overloading

Multiple functions with the same name but different parameter counts:

```lux
function format(x: number): string
    return tostring(x)
end

function format(x: number, decimals: number): string
    return string.format("%." .. decimals .. "f", x)
end

format(3.14)        -- "3.14"
format(3.14, 1)     -- "3.1"
```

The compiler generates a single dispatch function that selects the right implementation based on argument count.

## Multi-Return

```lux
function divide(a: number, b: number): (number, number)
    return math.floor(a / b), a % b
end

local quotient, remainder = divide(10, 3)
```

## Return Statement

```lux
function getValue(): number
    return 42
end

-- No return value
function doWork(): void
    print("working")
end

-- Never returns: always raises, exits, or loops forever
function panic(message: string): never
    error("panic: " .. message)
end
```

A `never` function may not return a value, and the compiler rejects it if any path can reach its
end. Code after a call to one is reported as unreachable. See
[The `never` Type](02-types.md#the-never-type).
