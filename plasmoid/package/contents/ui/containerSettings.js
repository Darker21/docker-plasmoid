.pragma library

// Pure logic over the `containerSettings` config entry.
// No sort-mode-aware freeze logic here — that only matters once Phase 4
// introduces non-manual sort modes; "manual" is the only mode that exists so far.

function parseSettings(json) {
    try {
        const parsed = JSON.parse(json)
        return Array.isArray(parsed) ? parsed : []
    } catch (e) {
        return []
    }
}

function findSetting(settings, id) {
    return settings.find(s => s.id === id) || null
}

function getOrderedIds(settings, scope) {
    return settings
        .filter(s => s.scope === scope)
        .sort((a, b) => a.order - b.order)
        .map(s => s.id)
}

function maxOrderInScope(settings, scope) {
    const inScope = settings.filter(s => s.scope === scope)
    if (inScope.length === 0)
        return -1
    return Math.max(...inScope.map(s => s.order))
}

// Ensures every currently-known top-level container/stack-header id, and every
// container id inside every known stack, has a containerSettings entry —
// appending new ones to the bottom of their scope. Returns a NEW
// array; never mutates the input.
function mergeNewItems(settings, standalone, stacks) {
    let next = settings.slice()

    function ensure(id, scope) {
        if (next.some(s => s.id === id && s.scope === scope))
            return
        const order = maxOrderInScope(next, scope) + 1
        next.push({ id: id, scope: scope, order: order, cpuOverride: null, memOverride: null })
    }

    for (const container of standalone)
        ensure(container.id, "top")

    for (const stack of stacks) {
        ensure("stack:" + stack.projectName, "top")
        for (const container of stack.containers)
            ensure(container.id, stack.projectName)
    }

    return next
}

function resolveShowCpu(settings, id, globalDefault) {
    const setting = findSetting(settings, id)
    if (setting && setting.cpuOverride !== null && setting.cpuOverride !== undefined)
        return setting.cpuOverride
    return globalDefault
}

function resolveShowMem(settings, id, globalDefault) {
    const setting = findSetting(settings, id)
    if (setting && setting.memOverride !== null && setting.memOverride !== undefined)
        return setting.memOverride
    return globalDefault
}

// Swaps itemId with its neighbor in the given direction (-1 up, +1 down)
// within its scope, using the manual `order` field directly. Returns a NEW
// array; never mutates the input.
function moveItem(settings, id, scope, direction) {
    const next = settings.map(s => Object.assign({}, s))
    const ordered = next
        .filter(s => s.scope === scope)
        .sort((a, b) => a.order - b.order)

    const index = ordered.findIndex(s => s.id === id)
    if (index === -1)
        return settings

    const swapIndex = index + direction
    if (swapIndex < 0 || swapIndex >= ordered.length)
        return settings

    const a = ordered[index]
    const b = ordered[swapIndex]
    const aOrder = a.order
    a.order = b.order
    b.order = aOrder

    return next
}
