// Keep image resizing and GPU copies together, avoiding a managed/browser round
// trip for every mip. Bound live bitmaps, and drain the batch before returning an
// error so the caller can safely destroy scene resources.
export function createUpload() { return new AbortController(); }

export async function uploadMips(device, texture, image, options, operation) {
    const {layer, width, height, levels, crop} = options;
    const signal = operation.signal;
    for (let first = 0; first < levels; first += 4) {
        signal.throwIfAborted();
        const pending = [];
        for (let mip = first; mip < Math.min(first + 4, levels); mip++) {
            pending.push((async () => {
                const mipWidth = Math.max(1, width >> mip), mipHeight = Math.max(1, height >> mip);
                const resize = {resizeWidth: mipWidth, resizeHeight: mipHeight,
                    resizeQuality: 'high', premultiplyAlpha: 'premultiply'};
                const bitmap = crop
                    ? await createImageBitmap(image, ...crop, resize)
                    : await createImageBitmap(image, resize);
                try {
                    signal.throwIfAborted();
                    device.queue.copyExternalImageToTexture({source: bitmap},
                        {texture, mipLevel: mip, origin: {z: layer}, premultipliedAlpha: true},
                        {width: mipWidth, height: mipHeight, depthOrArrayLayers: 1});
                } finally { bitmap.close(); }
            })());
        }
        const completed = await Promise.allSettled(pending);
        const failed = completed.find(result => result.status === 'rejected');
        if (failed) throw failed.reason;
    }
}
