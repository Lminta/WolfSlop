export function createSelector() {
// Match .NET List.Sort's introsort partitioning, including equal-distance ties.
// Each key is [source index: u32, squared distance: f32]. All distances are finite.
const views = new WeakMap();
let source, sourceCount, radiusKeys, radiusValues, minX, minY, maxX, maxY, maximumRadius, candidateCount;
let gpuRecords, atlasViews, atlasFrames, gpuKeys, gpuPacked, gpuFloats, gpuActors;
let gpuCount = 0, uploadedCount = -1, uploadedMapInfo;
let cameraQueue, cameraBuffer, actorRecords = new Uint32Array(12);
let submission, spriteBuffer, infoBuffer, spriteCapacity = 0;
let actorByteView = new Uint8Array(actorRecords.buffer);
const noWidths = new Uint32Array(), countUpload = new Uint32Array(1);
const cameraUpload = new Uint8Array(64);
const grid = new Map(), nearby = [];
function prepare(positions, count) {
    atlasViews = atlasFrames = null;
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
    gpuRecords = atlasViews = atlasFrames = gpuKeys = gpuPacked = gpuFloats = gpuActors = null;
    gpuCount = 0; uploadedCount = -1; uploadedMapInfo = null;
}

function resolveAtlas(index, cx, cy, elapsedSeconds) {
    const view = atlasViews?.[index];
    if (!view) return;
    const f32 = Math.fround;
    let frame = 0;
    if (view.animationRate > 0) {
        const cycle = view.frames / view.animationRate;
        frame = Math.min(view.frames - 1, Math.trunc(((elapsedSeconds % cycle + view.phaseSeconds % cycle) % cycle) * view.animationRate));
    } else if (view.directions > 1) {
        const dx = f32(cx - source[index * 4]), dy = f32(cy - source[index * 4 + 1]);
        if (f32(f32(dx * dx) + f32(dy * dy)) >= f32(1e-8)) {
            const turns = f32(f32(f32(Math.atan2(dy, dx)) - view.rotation) / f32(Math.PI * 2));
            frame = Math.floor(f32(f32(turns * view.directions) + .5));
            frame = (frame % view.directions + view.directions) % view.directions;
        }
    }
    atlasFrames[index] = frame;
    source[index * 4 + 2] = view.widths[frame];
}

function collect(widths, widthCount, cx, cy, range, actors, actorCount, elapsedSeconds) {
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
                resolveAtlas(index, cx, cy, elapsedSeconds);
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
    actors = new Float32Array(), actorCount = 0, elapsedSeconds = 0) {
    collect(widths, widthCount, cx, cy, range, actors, actorCount, elapsedSeconds);
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

function prepareGpu(positions, records, atlases, count) {
    prepare(positions, count);
    gpuRecords = records;
    atlasViews = new Array(count);
    atlasFrames = new Uint32Array(count);
    for (const view of atlases) atlasViews[view.index] = view;
    gpuKeys = new Uint32Array((count + 4) * 2);
    gpuPacked = new Uint32Array(128);
    gpuFloats = new Float32Array(gpuPacked.buffer);
    gpuActors = new Float32Array(16);
    gpuCount = 0; uploadedCount = -1; uploadedMapInfo = null;
}

function selectGpu(cx, cy, range, dirX, dirY, planeScale, cullView, elapsedSeconds, actors, actorCount) {
    const needed = sourceCount + actorCount;
    if (gpuKeys.length < (needed + 1) * 2) gpuKeys = new Uint32Array((needed + 1) * 4);
    if (gpuActors.length < actorCount * 4) gpuActors = new Float32Array(actorCount * 8);
    let actorValues = views.get(actors);
    if (!actorValues) {
        actorValues = new Float32Array(actors.buffer, actors.byteOffset, actors.length);
        views.set(actors, actorValues);
    }
    for (let i = 0; i < actorCount; i++) {
        gpuActors[i * 4] = actorValues[i * 12]; gpuActors[i * 4 + 1] = actorValues[i * 12 + 1];
        gpuActors[i * 4 + 2] = actorValues[i * 12 + 4]; gpuActors[i * 4 + 3] = actorValues[i * 12 + 8];
    }
    gpuCount = select(gpuKeys, noWidths, 0, cx, cy, range, dirX, dirY, planeScale, cullView,
        gpuActors, actorCount, elapsedSeconds);
    if (gpuPacked.length < gpuCount * 8) {
        gpuPacked = new Uint32Array(Math.max(gpuCount * 16, gpuPacked.length * 2));
        gpuFloats = new Float32Array(gpuPacked.buffer);
    }
    for (let i = 0; i < gpuCount; i++) {
        const index = gpuKeys[2 + i * 2], dynamic = index >= sourceCount;
        const records = dynamic ? actors : gpuRecords;
        const offset = dynamic ? (index - sourceCount) * 12 : index * 8;
        for (let word = 0; word < 8; word++) gpuPacked[i * 8 + word] = records[offset + word];
        const atlas = dynamic ? null : atlasViews[index];
        if (atlas) {
            const frame = atlasFrames[index];
            gpuPacked[i * 8 + 2] += frame;
            gpuFloats[i * 8 + 4] = atlas.widths[frame];
            gpuPacked[i * 8 + 7] = atlas.kinds[frame];
        }
    }
    return gpuCount;
}

function selectFrame(cameraBytes, actorBytes, cx, cy, range, dirX, dirY, planeScale, cullView, elapsedSeconds, actorCount) {
    // Blazor's object-argument path can supply ordinary number arrays as well
    // as Uint8Array. Keep the WebGPU BufferSource stable in either case.
    cameraUpload.set(cameraBytes);
    cameraQueue.writeBuffer(cameraBuffer, 0, cameraUpload);
    const actorByteLength = actorBytes.byteLength ?? actorBytes.length;
    if (actorRecords.byteLength < actorByteLength) {
        actorRecords = new Uint32Array(actorByteLength / 4);
        actorByteView = new Uint8Array(actorRecords.buffer);
    }
    actorByteView.set(actorBytes);
    return selectGpu(cx, cy, range, dirX, dirY, planeScale, cullView, elapsedSeconds, actorRecords, actorCount);
}

function renderFrame(cameraBytes, actorBytes, cx, cy, range, dirX, dirY, planeScale, cullView, elapsedSeconds, actorCount, step, workgroups) {
    const count = selectFrame(cameraBytes, actorBytes, cx, cy, range, dirX, dirY, planeScale, cullView, elapsedSeconds, actorCount);
    // The managed owner grows GPU buffers and bindings. Leave this selection
    // ready for its fallback upload instead of submitting an undersized buffer.
    if (!submission || count > spriteCapacity) return ~count;
    uploadGpu(cameraQueue, spriteBuffer, infoBuffer);
    submission.render(step, workgroups, count);
    return count;
}

function uploadGpu(queue, buffer, mapInfo) {
    if (uploadedMapInfo !== mapInfo || uploadedCount !== gpuCount) {
        countUpload[0] = gpuCount;
        queue.writeBuffer(mapInfo, 8, countUpload);
        uploadedMapInfo = mapInfo; uploadedCount = gpuCount;
    }
    if (gpuCount) queue.writeBuffer(buffer, 0, gpuPacked, 0, gpuCount * 8);
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

return { prepare, release, select, sort, prepareGpu, selectGpu, uploadGpu, selectFrame, renderFrame,
    setSubmission(value, buffer, info, capacity) { submission = value; spriteBuffer = buffer; infoBuffer = info; spriteCapacity = capacity; },
    clearSubmission() { submission = spriteBuffer = infoBuffer = null; spriteCapacity = 0; },
    uploadFrame(buffer, mapInfo) { uploadGpu(cameraQueue, buffer, mapInfo); },
    setCamera(queue, buffer) { cameraQueue = queue; cameraBuffer = buffer; }, getCandidateCount: () => candidateCount };
}

const defaultSelector = createSelector();
export const prepare = (...args) => defaultSelector.prepare(...args);
export const release = (...args) => defaultSelector.release(...args);
export const select = (...args) => defaultSelector.select(...args);
export const sort = (...args) => defaultSelector.sort(...args);
