import assert from 'node:assert/strict';
import {test} from 'node:test';
import {createMixer} from '../WolfNET.Engine.Browser/wwwroot/sfx-mixer.js';

test('voice batches share an audio timestamp and preserve gain, pan, pitch and smoothing', () => {
    let reads = 0;
    const context = {get currentTime() { reads++; return 12; }};
    const mixer = createMixer(context), calls = [];
    const parameter = name => ({setTargetAtTime: (...args) => calls.push([name, ...args])});
    mixer.add(0, parameter('gain0'), parameter('pan0'), parameter('pitch0'));
    mixer.add(1, parameter('gain1'), parameter('pan1'), parameter('pitch1'));
    const packet = new Float32Array([0, .5, -1, 2, 1, 8, 1, .75]);
    mixer.apply(packet, 2);
    assert.equal(reads, 1);
    assert.deepEqual(calls, [
        ['gain0', .5, 12, .025], ['pan0', -1, 12, .025], ['pitch0', 2, 12, .025],
        ['gain1', 8, 12, .025], ['pan1', 1, 12, .025], ['pitch1', .75, 12, .025]
    ]); // Compensated mud gain may exceed one.
    mixer.apply(packet, 2);
    assert.equal(calls.length, 6, 'Unchanged parameters were rescheduled');
    packet[2] = 0;
    mixer.apply(packet, 2);
    assert.deepEqual(calls.at(-1), ['pan0', 0, 12, .025]);
    assert.equal(calls.length, 7, 'A pan change rescheduled unrelated parameters');
});

test('removed and reused voice slots and separate scenes retain independent state', () => {
    const first = createMixer({currentTime: 1}), second = createMixer({currentTime: 2});
    const packet = new Float32Array([3, 1, 0, 1, 99, 1, 0, 1]), calls = [];
    const parameter = {setTargetAtTime: (...args) => calls.push(args)};
    first.add(3, parameter, parameter, parameter);
    first.apply(packet, 1);
    first.remove(3);
    first.apply(packet, 2);
    assert.equal(calls.length, 3, 'Released voices were updated');
    first.add(3, parameter, parameter, parameter);
    first.apply(packet, 1);
    assert.equal(calls.length, 6, 'Reused slot retained the old voice parameter cache');
    second.apply(packet, 1);
    assert.equal(calls.length, 6, 'Another scene retained released audio parameters');
});
