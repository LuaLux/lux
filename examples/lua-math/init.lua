-- lua-math: a tiny pure-Lua library used as a reference for "Nebra project that
-- consumes a Lua dependency". The runtime side lives entirely in this file;
-- the matching `init.d.neb` provides Nebra type information for the consumer.

local M = {}

--- Linear interpolation between a and b at parameter t in [0, 1].
function M.lerp(a, b, t)
    return a + (b - a) * t
end

--- Clamps x into the closed interval [lo, hi].
function M.clamp(x, lo, hi)
    if x < lo then return lo end
    if x > hi then return hi end
    return x
end

--- Returns the sum of all numeric arguments passed in.
function M.sum(...)
    local args = { ... }
    local total = 0
    for i = 1, #args do
        total = total + args[i]
    end
    return total
end

--- Computes the average of a numeric array. Returns 0 for empty input rather
--- than dividing by zero.
function M.average(values)
    local n = #values
    if n == 0 then return 0 end
    local total = 0
    for i = 1, n do
        total = total + values[i]
    end
    return total / n
end

--- Returns a 2D vector table with x/y fields. Used to demonstrate that
--- consumers can call into Lua factories that build structured data.
function M.vec2(x, y)
    return { x = x, y = y }
end

--- Magnitude of a vec2 produced by `vec2(...)`.
function M.length2(v)
    return math.sqrt(v.x * v.x + v.y * v.y)
end

return M
