import assert from 'node:assert/strict';
import {test} from 'node:test';
import {createStateReader} from '../WolfSlop.Engine.Browser/wwwroot/audio-state.js';

test('music snapshots preserve clocks, pending metadata, pause, completion and stream lifetime', () => {
    const reader = createStateReader();
    const first = {currentTime: 8.5, duration: 12, paused: false, ended: false};
    const second = {currentTime: 0, duration: NaN, paused: true, ended: false};
    reader.add(0, first); reader.add(1, second);
    const snapshot = reader.read();
    assert.deepEqual(snapshot, [0, 8.5, 12, 0, 0, 1, 0, -1, 1, 0]);
    assert.deepEqual(JSON.parse(JSON.stringify(snapshot)), snapshot, 'Pending metadata must survive JSON interop');
    first.currentTime = 12; first.paused = first.ended = true;
    second.duration = 15; second.currentTime = 3; second.paused = false;
    assert.deepEqual(reader.read(), [0, 12, 12, 1, 1, 1, 3, 15, 0, 0]);
    assert.equal(snapshot[1], 8.5, 'Later sampling changed a previous snapshot');
    reader.remove(0);
    assert.deepEqual(reader.read(), [1, 3, 15, 0, 0]);
    const other = createStateReader();
    assert.deepEqual(other.read(), [], 'Music scenes share retained streams');
});
