import assert from 'node:assert/strict';
import fs from 'node:fs';
import {createSelector} from '../WolfSlop.Engine.Browser/wwwroot/sprite-sort.js';

const fixture = JSON.parse(fs.readFileSync(new URL('../artifacts/performance/sprite-upload-oracle.json', import.meta.url)));
const selector = createSelector(), buffer = {}, mapInfo = {}, camera = {};
selector.prepareGpu(new Float32Array(fixture.positions), new Uint32Array(fixture.records), fixture.atlases, fixture.positions.length / 4);
let upload = [], uploadedCount, countWrites = 0, cameraBytes;
const queue = {writeBuffer(target, offset, data, start = 0, size = data.length - start) {
    if (target === camera) { assert.equal(offset, 0); cameraBytes = Array.from(data); }
    else if (target === buffer) upload = Array.from(data.subarray(start, start + size));
    else {assert.equal(target, mapInfo); assert.equal(offset, 8); uploadedCount = data[0]; countWrites++;}
}};
selector.setCamera(queue, camera);
for (const view of fixture.views) {
    upload = [];
    const uniforms = new Uint8Array(64).fill(17);
    const count = selector.selectFrame(uniforms, new Uint8Array(new Uint32Array(view.actors).buffer),
        view.x, view.y, 40, view.dx, view.dy, view.plane, view.cull, view.time, view.actors.length / 12);
    assert.deepEqual(cameraBytes, Array.from(uniforms), 'Batched camera upload changed the uniforms');
    selector.uploadFrame(buffer, mapInfo);
    assert.equal(count, view.expected.length / 8);
    assert.equal(uploadedCount, count);
    assert.deepEqual(upload, view.expected, `Packed sprites differ from native rendering at ${view.x},${view.y}, time ${view.time}`);
    const previousWrites = countWrites;
    selector.uploadGpu(queue, buffer, mapInfo);
    assert.equal(countWrites, previousWrites, 'Unchanged sprite count was uploaded twice');
}
selector.release();
selector.prepareGpu(new Float32Array(4), new Uint32Array(8), [], 0);
assert.equal(selector.selectGpu(0, 0, 40, 1, 0, 1, true, 0, new Uint32Array(12), 0), 0);
upload = []; selector.uploadGpu(queue, buffer, mapInfo);
assert.equal(uploadedCount, 0); assert.deepEqual(upload, []);
console.log(`PASS: ${fixture.views.length} GPU uploads match independent native animation, directional views, widths, geometry, moving actors, and painter ordering.`);
