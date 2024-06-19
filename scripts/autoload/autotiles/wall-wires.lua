-- setup autotile data
local autotile = rained.tiles.createAutotile("Wall Wires", "Misc")
autotile.type = "path"
autotile.allowIntersections = true
autotile:addToggleOption("cap", "Use End Tiles", false)
autotile:addToggleOption("junctions", "Allow Junctions", true)
autotile:addToggleOption("square", "Use Square Turns", false)
autotile:addIntOption("altChance", "Alt Chance", 4, 1, 20)

-- change "allowIntersections" property when junctions is turned on/off
function autotile:onOptionChanged(id)
    if id == "junctions" then
        autotile.allowIntersections = autotile:getOption(id)
    end
end

-- Rained will not allow the user to use this autotile
-- if any of the tiles in this table are not installed
autotile.requiredTiles = {
    "WallWires Horizontal A",
    "WallWires Horizontal B",
    "WallWires Vertical A",
    "WallWires Vertical B",
    "WallWires NE",
    "WallWires SE",
    "WallWires SW",
    "WallWires NW",
    "WallWires Square NE",
    "WallWires Square SE",
    "WallWires Square SW",
    "WallWires Square NW",
    "WallWires X Section",
    "WallWires T Section N",
    "WallWires T Section E",
    "WallWires T Section S",
    "WallWires T Section W",
    "WallWires End N",
    "WallWires End E",
    "WallWires End S",
    "WallWires End W",
}

-- table of tiles to use for the standard autotiler function
local tileTable = {
    ld = "WallWires SW",
    lu = "WallWires NW",
    rd = "WallWires SE",
    ru = "WallWires NE",
    vertical = "WallWires Vertical A",
    horizontal = "WallWires Horizontal A",
    tr = "WallWires T Section E",
    tu = "WallWires T Section N",
    tl = "WallWires T Section W",
    td = "WallWires T Section S",
    x = "WallWires X Section",
    capRight = "WallWires End E",
    capUp = "WallWires End N",
    capLeft = "WallWires End W",
    capDown = "WallWires End S",

    placeJunctions = false,
    placeCaps = false
}

-- this is the callback function that Rained invokes when the user
-- wants to autotile a given path
---@param layer integer The layer to run the autotiler on
---@param segments PathSegment[] The list of path segments
---@param forceModifier ForceModifier Force-placement mode, as a string. Can be nil, "force", or "geometry".
function autotile:tilePath(layer, segments, forceModifier)
    -- if the "square" option is checked, use square turns
    if self:getOption("square") then
        tileTable.ld = "WallWires Square SW"
        tileTable.lu = "WallWires Square NW"
        tileTable.rd = "WallWires Square SE"
        tileTable.ru = "WallWires Square NE"
    else
        tileTable.ld = "WallWires SW"
        tileTable.lu = "WallWires NW"
        tileTable.rd = "WallWires SE"
        tileTable.ru = "WallWires NE"
    end

    -- set options for the standard autotiler based on
    -- the GUI options
    tileTable.placeJunctions = self:getOption("junctions")
    tileTable.placeCaps = self:getOption("cap")

    -- run the standard autotiler
    rained.tiles.autotilePath(tileTable, layer, segments, forceModifier)

    -- replace random horizontal and vertical wall wires along
    -- the path with their alternate form
    local altChance = autotile:getOption("altChance")
    for i, seg in ipairs(segments) do
        local tileName = rained.tiles.getTileAt(seg.x, seg.y, layer)

        if (tileName == "WallWires Horizontal A" or tileName == "WallWires Vertical A") and math.random(altChance) == 1 then
            rained.tiles.deleteTile(seg.x, seg.y, layer)

            if tileName == "WallWires Horizontal A" then
                rained.tiles.placeTile("WallWires Horizontal B", seg.x, seg.y, layer, forceModifier)
            elseif tileName == "WallWires Vertical A" then
                rained.tiles.placeTile("WallWires Vertical B", seg.x, seg.y, layer, forceModifier)
            end
        end
    end
end