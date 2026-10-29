// Read all music clocks in one WASM call; queue and crossfade policy stay in C#.
export function createStateReader() {
    const streams = new Map();
    return {
        add(id, audio) { streams.set(id, audio); },
        remove(id) { streams.delete(id); },
        read() {
            const states = [];
            for (const [id, audio] of streams)
                states.push(id, audio.currentTime, Number.isFinite(audio.duration) ? audio.duration : -1, +audio.paused, +audio.ended);
            return states;
        }
    };
}
