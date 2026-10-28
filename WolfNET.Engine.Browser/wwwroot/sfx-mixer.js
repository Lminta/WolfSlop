// Mixing decisions stay in C#. Apply all changed voices with one browser call
// and one audio-clock read, preserving the existing 25 ms parameter smoothing.
export function createMixer(context) {
    const voices = [];
    return {
        add(id, gain, pan, pitch) {
            voices[id] = {parameters: [gain, pan, pitch], previous: [NaN, NaN, NaN]};
        },
        remove(id) { voices[id] = null; },
        apply(updates, count) {
            const now = context.currentTime;
            for (let i = 0; i < count; i++) {
                const voice = voices[updates[i * 4]];
                if (!voice) continue;
                for (let p = 0; p < 3; p++) {
                    const value = updates[i * 4 + 1 + p];
                    if (voice.previous[p] === value) continue;
                    voice.parameters[p].setTargetAtTime(value, now, .025);
                    voice.previous[p] = value;
                }
            }
        }
    };
}
