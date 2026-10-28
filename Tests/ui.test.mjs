import test from 'node:test';
import assert from 'node:assert/strict';
import {inversePoint, hitTest, drawNode, drawShapes} from '../WolfNET.Engine.Browser/wwwroot/ui.js';

const identity = {a: 1, b: 0, c: 0, d: 1, e: 0, f: 0};
const node = (id, extra = {}) => ({id, parentId: null, width: 100, height: 40, transform: identity,
    clips: [], interactive: true, enabled: true, opacity: 1, ...extra});
test('browser and managed UI share inverse transforms, clips, draw order and modal boundaries', () => {
    assert.deepEqual(inversePoint({a: 0, b: 2, c: -2, d: 0, e: 100, f: 20}, 60, 60), {x: 20, y: 20});
    assert.equal(inversePoint({a: 0, b: 0, c: 0, d: 0, e: 0, f: 0}, 1, 1), null);
    const frame = {nodes: [node('bottom'), node('top')], modalId: null};
    assert.equal(hitTest(frame, 10, 10).id, 'top');
    frame.nodes[1].clips = [{rect: {x: 0, y: 0, width: 5, height: 5}, transform: identity}];
    assert.equal(hitTest(frame, 10, 10).id, 'bottom');
    frame.modalId = 'top';
    assert.equal(hitTest(frame, 10, 10).id, 'top', 'Modal backdrop must block world input');
    frame.nodes[1].interactive = false; frame.modalId = null;
    frame.nodes[0].enabled = false;
    assert.equal(hitTest(frame, 10, 10), null);
});
function context() {
    const calls = [];
    return new Proxy({calls, globalAlpha: 1}, {get(target, name) {
        if (name in target) return target[name];
        return (...args) => calls.push([name, ...args]);
    }});
}
test('image drawing preserves atlas cropping and uses independent image fitting and opacity', () => {
    const ctx = context(), image = {naturalWidth: 200, naturalHeight: 100};
    drawNode(ctx, node('image', {kind: 'image', sourceRect: {x: .5, y: 0, width: .5, height: 1}, imageFit: 1,
        width: 100, height: 50, opacity: .4}), image, null);
    assert.deepEqual(ctx.calls.find(call => call[0] === 'drawImage').slice(2), [100, 0, 100, 100, 25, 0, 50, 50]);
    assert.equal(ctx.globalAlpha, .4);
});
test('missing images, outlined text and custom vector drawing use the same backend', () => {
    const ctx = context();
    drawNode(ctx, node('missing', {kind: 'image'}), null, null);
    assert(ctx.calls.some(call => call[0] === 'fillRect'));
    drawNode(ctx, node('text', {kind: 'text', font: {family: 'sans-serif', size: 20, weight: 400},
        padding: {left: 2, top: 3, right: 2, bottom: 3}, textLayout: {height: 25, lineHeight: 25, lines: [{text: 'Hello', width: 50}]},
        textOutlineColor: '#000', textOutlineWidth: 2, color: '#fff'}), null, null);
    assert(ctx.calls.some(call => call[0] === 'strokeText' && call[1] === 'Hello'));
    drawShapes(ctx, {shapes: [{kind: 'polyline', points: [0, 0, 10, 20], stroke: '#fff', lineWidth: 2}]});
    assert(ctx.calls.some(call => call[0] === 'lineTo' && call[1] === 10 && call[2] === 20));
});
