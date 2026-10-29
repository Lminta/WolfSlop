import test from 'node:test';
import assert from 'node:assert/strict';
import {inversePoint, hitTest, drawNode, drawShapes, mergeUiFrame} from '../WolfSlop.Engine.Browser/wwwroot/ui.js';

const identity = {a: 1, b: 0, c: 0, d: 1, e: 0, f: 0};
const node = (id, extra = {}) => ({id, parentId: null, width: 100, height: 40, transform: identity,
    clips: [], interactive: true, enabled: true, opacity: 1, ...extra});
test('display-list deltas retain geometry, apply changes, reorder and remove nodes', () => {
    const drawing = {revision: 1, shapes: []}, text = node('text');
    const previous = {nodes: [node('course', {kind: 'canvas', drawing}), text, node('removed')]};
    const update = {version: 2, order: ['text', 'course'], nodes: [
        node('course', {kind: 'canvas', drawing: null, drawingTransform: {...identity, e: 5}})]};
    const merged = mergeUiFrame(previous, update);
    assert.deepEqual(merged.nodes.map(n => n.id), ['text', 'course']);
    assert.equal(merged.nodes[0], text);
    assert.equal(merged.nodes[1].drawing, drawing);
    assert.equal(merged.nodes[1].drawingTransform.e, 5);
    const replacement = {revision: 2, shapes: []};
    assert.equal(mergeUiFrame(merged, {...update, nodes: [node('course', {kind: 'canvas', drawing: replacement})]})
        .nodes[1].drawing, replacement);
});
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

test('transform deltas preserve text, images, clipping and immutable frame snapshots', () => {
    const before = node('wheel', {kind:'image', source:'wheel.png', clips:[{rect:{x:0,y:0,width:50,height:50},transform:identity}]});
    const previous = {nodes:[before]};
    const transform = {...identity, e:25};
    const after = mergeUiFrame(previous, {order:['wheel'], nodes:[], transforms:[{id:'wheel',transform}]}).nodes[0];
    assert.equal(before.transform, identity);
    assert.equal(after.transform, transform);
    assert.equal(after.source, before.source);
    assert.equal(after.clips, before.clips);
    assert.equal(hitTest({nodes:[after]}, 30, 20).id, 'wheel');
    assert.equal(hitTest({nodes:[after]}, 60, 20), null, 'Moving an image moved its inherited clip');
});

test('text and drawing deltas preserve styling, replace geometry and explicitly clear drawings', () => {
    const originalDrawing = {revision:1,shapes:[]}, newDrawing = {revision:2,shapes:[]};
    const previous = {nodes:[node('timer',{kind:'text',font:{size:22},content:'0:00.00',color:'#fff'}),
        node('map',{kind:'canvas',drawing:originalDrawing})]};
    const update = {order:['timer','map'],nodes:[],texts:[{id:'timer',content:'0:01.00',textLayout:{lines:[]},width:120}],
        canvases:[{id:'map',drawing:null,replaceDrawing:false,drawingTransform:{...identity,e:5}}]};
    const changed = mergeUiFrame(previous, update);
    assert.equal(changed.nodes[0].content,'0:01.00');
    assert.equal(changed.nodes[0].color,'#fff');
    assert.equal(changed.nodes[0].font,previous.nodes[0].font);
    assert.equal(changed.nodes[1].drawing,originalDrawing);
    assert.equal(changed.nodes[1].drawingTransform.e,5);
    const replace = drawing => mergeUiFrame(changed,{order:update.order,nodes:[],canvases:[{id:'map',drawing,replaceDrawing:true,drawingTransform:identity}]});
    assert.equal(replace(newDrawing).nodes[1].drawing,newDrawing);
    assert.equal(replace(null).nodes[1].drawing,null);
    assert.equal(previous.nodes[0].content,'0:00.00');
});
function context() {
    const calls = [];
    return new Proxy({calls, globalAlpha: 1}, {get(target, name) {
        if (name in target) return target[name];
        return (...args) => calls.push([name, ...args]);
    }});
}
test('retained drawing transforms compose with shape transforms and preserve stroke width', () => {
    const ctx = context();
    drawShapes(ctx, {shapes: [{kind: 'polyline', points: [0, 0, 10, 20], stroke: '#fff', lineWidth: 4,
        transform: {a: 1, b: 0, c: 0, d: 1, e: 3, f: 4}}]},
        {a: 0, b: 2, c: -2, d: 0, e: 5, f: 6});
    assert(ctx.calls.some(call => call[0] === 'transform' &&
        JSON.stringify(call.slice(1)) === JSON.stringify([0, 2, -2, 0, -3, 12])));
    assert.equal(ctx.lineWidth, 2, 'A view transform must not change the intended stroke width');
});
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

test('cached glyphs retain overhang, opacity and clipping without drawing the text again', () => {
    const ctx = context(), surface = {canvas:{},margin:24,width:148,height:88};
    drawNode(ctx,node('text',{textLayout:{},font:{size:20},opacity:.4,
        clips:[{rect:{x:0,y:0,width:60,height:40},transform:identity}]}),null,null,surface);
    assert.deepEqual(ctx.calls.find(call=>call[0]==='drawImage').slice(1),[surface.canvas,-24,-24,148,88]);
    assert(ctx.calls.some(call=>call[0]==='clip'));
    assert(!ctx.calls.some(call=>call[0]==='strokeText'||call[0]==='fillText'));
    assert.equal(ctx.globalAlpha,.4);
});
