import assert from 'node:assert/strict';
import {test} from 'node:test';
import {createUpload, uploadMips} from '../WolfSlop.Engine.Browser/wwwroot/texture-upload.js';

test('texture uploads preserve crop, filtering, every mip and close their bitmaps', async () => {
    const original = globalThis.createImageBitmap, calls = [], copies = [], closed = [];
    globalThis.createImageBitmap = async (...args) => {
        calls.push(args);
        return {close: () => closed.push(args)};
    };
    try {
        const device = {queue: {copyExternalImageToTexture: (...args) => copies.push(args)}};
        const image = {}, texture = {}, crop = [2, 3, 7, 5];
        await uploadMips(device, texture, image, {layer: 4, width: 9, height: 5, levels: 4, crop}, createUpload());
        assert.deepEqual(calls.map(args => args.slice(1, 5)), Array(4).fill(crop));
        assert(calls.every(args => args[5].resizeQuality === 'high' && args[5].premultiplyAlpha === 'premultiply'));
        assert.deepEqual(copies.map(args => args[1].mipLevel).sort(), [0, 1, 2, 3]);
        assert(copies.every(args => args[1].texture === texture && args[1].origin.z === 4 && args[1].premultipliedAlpha));
        assert.deepEqual(copies.map(args => [args[2].width, args[2].height]), [[9, 5], [4, 2], [2, 1], [1, 1]]);
        assert.equal(closed.length, 4);
    } finally { globalThis.createImageBitmap = original; }
});

test('failed or cancelled uploads drain pending work and close all decoded bitmaps', async () => {
    const original = globalThis.createImageBitmap;
    try {
        for (const cancel of [false, true]) {
            const operation = createUpload();
            let opened = 0, closed = 0, copies = 0;
            globalThis.createImageBitmap = async () => {
                await new Promise(resolve => setTimeout(resolve, 2));
                opened++;
                if (cancel) operation.abort();
                return {close: () => closed++};
            };
            const device = {queue: {copyExternalImageToTexture: () => { copies++; throw Error('Upload failed'); }}};
            await assert.rejects(uploadMips(device, {}, {}, {layer: 0, width: 16, height: 16, levels: 8}, operation));
            assert.equal(opened, 4, 'Failure started another batch');
            assert.equal(closed, opened, 'Upload returned while decoded bitmaps were still live');
            if (cancel) assert.equal(copies, 0, 'Cancelled upload wrote to the GPU');
        }
    } finally { globalThis.createImageBitmap = original; }
});
