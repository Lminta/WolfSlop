export function createSelector() {
// Match .NET List.Sort's introsort partitioning, including equal-distance ties.
// Each key is [source index: u32, squared distance: f32]. All distances are finite.
const views = new WeakMap();
let source, sourceCount, radiusKeys, radiusValues, minX, minY, maxX, maxY, maximumRadius, candidateCount;
const grid = new Map(), nearby = [];
function prepare(positions, count) {
    source = positions; sourceCount = count;
    radiusKeys = new Uint32Array((count + 3) * 2); radiusValues = new Float32Array(radiusKeys.buffer);
    grid.clear(); maximumRadius = 0;
    minX = minY = Infinity; maxX = maxY = -Infinity;
    for (let i = 0; i < count; i++) {
        const x = Math.floor(source[i * 4] / 16), y = Math.floor(source[i * 4 + 1] / 16), key = x + ',' + y;
        let bucket = grid.get(key);
        if (!bucket) grid.set(key, bucket = []);
        bucket.push(i);
        maximumRadius = Math.max(maximumRadius, source[i * 4 + 3] / 2);
        minX = Math.min(minX, x); maxX = Math.max(maxX, x); minY = Math.min(minY, y); maxY = Math.max(maxY, y);
    }
}

function release() {
    source = radiusKeys = radiusValues = null;
    sourceCount = 0; grid.clear(); nearby.length = 0;
}

function collect(widths, widthCount, cx, cy, range, actors, actorCount) {
    const f32 = Math.fround;
    const widthValues = new Float32Array(widths.buffer, widths.byteOffset, widths.length);
    for (let i = 0; i < widthCount; i++) source[widths[i * 2] * 4 + 2] = widthValues[i * 2 + 1];
    nearby.length = 0;
    candidateCount = 0;
    const radius = range + maximumRadius;
    for (let x = Math.max(minX, Math.floor((cx - radius) / 16)); x <= Math.min(maxX, Math.floor((cx + radius) / 16)); x++)
        for (let y = Math.max(minY, Math.floor((cy - radius) / 16)); y <= Math.min(maxY, Math.floor((cy + radius) / 16)); y++) {
            const bucket = grid.get(x + ',' + y);
            if (!bucket) continue;
            for (const index of bucket) {
                candidateCount++;
                const dx = f32(source[index * 4] - cx), dy = f32(source[index * 4 + 1] - cy);
                const distance = f32(f32(dx * dx) + f32(dy * dy)), limit = f32(range + source[index * 4 + 2] * .5);
                if (distance <= f32(limit * limit)) nearby.push(index);
            }
        }
    const needed = (sourceCount + actorCount) * 2;
    if (radiusKeys.length < needed) {
        radiusKeys = new Uint32Array(Math.max(needed, radiusKeys.length * 2));
        radiusValues = new Float32Array(radiusKeys.buffer);
    }
    for (let i = 0; i < actorCount; i++) {
        const dx = f32(actors[i * 4] - cx), dy = f32(actors[i * 4 + 1] - cy), limit = f32(range + actors[i * 4 + 2] * .5);
        if (f32(f32(dx * dx) + f32(dy * dy)) <= f32(limit * limit)) nearby.push(sourceCount + i);
    }
    nearby.sort((a, b) => a - b);
    for (let i = 0; i < nearby.length; i++) {
        const index = nearby[i];
        const dx = f32((index >= sourceCount ? actors[(index - sourceCount) * 4 + 0] : source[index * 4 + 0]) - cx);
        const dy = f32((index >= sourceCount ? actors[(index - sourceCount) * 4 + 1] : source[index * 4 + 1]) - cy);
        radiusKeys[i * 2] = index; radiusValues[i * 2 + 1] = f32(f32(dx * dx) + f32(dy * dy));
    }
    sort(radiusKeys, nearby.length);
}

function select(output, widths, widthCount, cx, cy, range, dirX, dirY, planeScale, cullView,
    actors = new Float32Array(), actorCount = 0) {
    collect(widths, widthCount, cx, cy, range, actors, actorCount);
    const f32 = Math.fround, marginScale = f32(Math.sqrt(f32(1 + f32(planeScale * planeScale))));
    let written = 0;
    for (let i = 0; i < nearby.length; i++) {
        const index = radiusKeys[i * 2];
        if (cullView) {
            const dx = f32((index >= sourceCount ? actors[(index - sourceCount) * 4 + 0] : source[index * 4 + 0]) - cx);
            const dy = f32((index >= sourceCount ? actors[(index - sourceCount) * 4 + 1] : source[index * 4 + 1]) - cy);
            const radius = (index >= sourceCount ? actors[(index - sourceCount) * 4 + 3] : source[index * 4 + 3]) * .5;
            const depth = f32(f32(dx * dirX) + f32(dy * dirY)), across = f32(f32(dx * dirY) - f32(dy * dirX));
            // Keep planes tangent to the frustum visible across browser/.NET rounding.
            const margin = f32(f32(radius * marginScale) + .0001), sideDepth = f32(depth * planeScale);
            if (f32(depth + radius) <= 0 || f32(across - sideDepth) > margin || f32(-across - sideDepth) > margin) continue;
        }
        output[2 + written * 2] = index; output[3 + written * 2] = radiusKeys[i * 2 + 1];
        written++;
    }
    output[0] = written; output[1] = candidateCount;
    return written;
}

function sort(keys, count) {
    let distances = views.get(keys);
    if (!distances) {
        distances = new Float32Array(keys.buffer, keys.byteOffset, keys.length);
        views.set(keys, distances);
    }
    const distance = i => distances[i * 2 + 1];
    function swap(a, b) {
        const id = keys[a * 2], bits = keys[a * 2 + 1];
        keys[a * 2] = keys[b * 2]; keys[a * 2 + 1] = keys[b * 2 + 1];
        keys[b * 2] = id; keys[b * 2 + 1] = bits;
    }
    function swapIfGreater(a, b) { if (distance(a) < distance(b)) swap(a, b); }
    function downHeap(start, i, length) {
        const id = keys[(start + i - 1) * 2], bits = keys[(start + i - 1) * 2 + 1], value = distance(start + i - 1);
        while (i <= (length >> 1)) {
            let child = 2 * i;
            if (child < length && distance(start + child - 1) > distance(start + child)) child++;
            if (value <= distance(start + child - 1)) break;
            keys[(start + i - 1) * 2] = keys[(start + child - 1) * 2];
            keys[(start + i - 1) * 2 + 1] = keys[(start + child - 1) * 2 + 1];
            i = child;
        }
        keys[(start + i - 1) * 2] = id; keys[(start + i - 1) * 2 + 1] = bits;
    }
    function intro(start, length, depth) {
        while (length > 1) {
            if (length <= 16) {
                if (length === 2) swapIfGreater(start, start + 1);
                else if (length === 3) {
                    swapIfGreater(start, start + 1); swapIfGreater(start, start + 2); swapIfGreater(start + 1, start + 2);
                } else for (let i = 1; i < length; i++) {
                    const index = start + i, id = keys[index * 2], bits = keys[index * 2 + 1], value = distance(index);
                    let j = i - 1;
                    while (j >= 0 && value > distance(start + j)) {
                        keys[(start + j + 1) * 2] = keys[(start + j) * 2];
                        keys[(start + j + 1) * 2 + 1] = keys[(start + j) * 2 + 1]; j--;
                    }
                    keys[(start + j + 1) * 2] = id; keys[(start + j + 1) * 2 + 1] = bits;
                }
                return;
            }
            if (depth-- === 0) {
                for (let i = length >> 1; i >= 1; i--) downHeap(start, i, length);
                for (let i = length; i > 1; i--) { swap(start, start + i - 1); downHeap(start, 1, i - 1); }
                return;
            }
            const high = start + length - 1, middle = start + ((length - 1) >> 1);
            swapIfGreater(start, middle); swapIfGreater(start, high); swapIfGreater(middle, high);
            const pivot = distance(middle);
            swap(middle, high - 1);
            let left = start, right = high - 1;
            while (left < right) {
                while (distance(++left) > pivot) { }
                while (distance(--right) < pivot) { }
                if (left >= right) break;
                swap(left, right);
            }
            if (left !== high - 1) swap(left, high - 1);
            intro(left + 1, high - left, depth);
            length = left - start;
        }
    }
    intro(0, count, 2 * (Math.floor(Math.log2(Math.max(1, count))) + 1));
}

return { prepare, release, select, sort };
}

const defaultSelector = createSelector();
export const prepare = (...args) => defaultSelector.prepare(...args);
export const release = (...args) => defaultSelector.release(...args);
export const select = (...args) => defaultSelector.select(...args);
export const sort = (...args) => defaultSelector.sort(...args);
