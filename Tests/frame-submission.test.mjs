import assert from 'node:assert/strict';
import {test} from 'node:test';
import {createSubmission} from '../WolfSlop.Engine.Browser/wwwroot/frame-submission.js';

function fixture() {
    const events = [], computeBindings = [];
    const canvas = {width: 259, height: 145, createView: () => 'canvas'};
    const device = {
        queue: {submit: () => events.push({type: 'submit'})},
        createCommandEncoder: () => ({
            beginComputePass: () => ({
                setPipeline() {}, setBindGroup(index, group) { computeBindings.push(group); },
                dispatchWorkgroups: count => events.push({type: 'rays', count}), end() {}
            }),
            beginRenderPass: descriptor => {
                const attachment = descriptor.colorAttachments[0];
                const record = {type: 'pass', view: attachment.view, load: attachment.loadOp, draws: []};
                events.push(record);
                return {setPipeline() {}, setBindGroup() {}, draw: (...args) => record.draws.push(args), end() {}};
            },
            copyTextureToTexture: (source, destination, size) =>
                events.push({type: 'retain', source: source.texture, destination: destination.texture, size: [...size]}),
            finish: () => ({})
        })
    };
    const submission = createSubmission(device, {getCurrentTexture: () => canvas}, {}, {}, {}, {}, {});
    submission.setScene({}, {});
    return {submission, events, canvas, computeBindings};
}

test('full rendering submits one world pass without history or presentation work', () => {
    const {submission, events} = fixture();
    submission.render(1, 5, 12);
    assert.deepEqual(events.map(event => event.type), ['rays', 'pass', 'submit']);
    assert.equal(events[0].count, 5);
    assert.equal(events[1].view, 'canvas');
    assert.deepEqual(events[1].draws, [[3], [6, 12]]);
});

test('resizing replaces the compute bindings before the next frame', () => {
    const {submission, computeBindings} = fixture();
    const resized = {};
    submission.setComputeGroup(resized);
    submission.render(1, 13, 0);
    assert.equal(computeBindings.at(-1), resized);
});

test('priming retains a full frame; subsequent frames shade the compact target', () => {
    const {submission, events, canvas} = fixture();
    const history = {width: 259, height: 145};
    submission.setColumns(history, 'compact', {});
    submission.render(1, 5, 0);
    assert.deepEqual(events.map(event => event.type), ['rays', 'pass', 'retain', 'submit']);
    assert.equal(events[1].view, 'canvas');
    assert.deepEqual(events[2], {type: 'retain', source: canvas, destination: history, size: [259, 145, 1]});
    events.length = 0;
    submission.render(3, 2, 9);
    assert.deepEqual(events.map(event => event.type), ['rays', 'pass', 'pass', 'retain', 'submit']);
    assert.equal(events[1].view, 'compact');
    assert.equal(events[1].load, 'clear');
    assert.equal(events[2].view, 'canvas');
    assert.deepEqual(events[1].draws, [[3], [6, 9]]);
    assert.deepEqual(events[2].draws, [[3]]);
});

test('screen layers are drawn after retaining the world and release removes history', () => {
    const {submission, events} = fixture();
    submission.setColumns({width: 259, height: 145}, 'compact', {});
    const screen = submission.begin(2, 3, 0);
    screen.draw(6); screen.end(); submission.submit();
    assert.deepEqual(events.map(event => event.type), ['rays', 'pass', 'pass', 'retain', 'pass', 'submit']);
    assert.equal(events[4].load, 'load');
    assert.equal(events[4].view, 'canvas');
    submission.releaseScene();
    submission.setScene({}, {});
    events.length = 0;
    submission.render(1, 5, 0);
    assert.deepEqual(events.map(event => event.type), ['rays', 'pass', 'submit']);
});
