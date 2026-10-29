// Keep command encoding in one browser call instead of crossing WASM for every GPU command.
export function createSubmission(device, context, computePipeline, computeGroup, worldPipeline, spritePipeline, presentPipeline) {
    let worldGroup, spriteGroup, history, stripView, presentGroup, encoder, canvasView;
    const attachment = {view: null, loadOp: 'clear', storeOp: 'store', clearValue: [0, 0, 0, 1]};
    const worldPass = {label: 'WolfNET World Pass', colorAttachments: [attachment]};
    const presentAttachment = {...attachment};
    const presentPass = {label: 'WolfNET Interlaced Presentation Pass', colorAttachments: [presentAttachment]};
    const screenAttachment = {...attachment, loadOp: 'load'};
    const screenPass = {label: 'WolfNET Screen Layers Pass', colorAttachments: [screenAttachment]};
    const computePass = {label: 'WolfNET DDA Compute Pass'};
    const copySource = {texture: null}, copyDestination = {texture: null};
    const copySize = [0, 0, 1];

    function encode(step, workgroups, spriteCount) {
        encoder = device.createCommandEncoder();
        const compute = encoder.beginComputePass(computePass);
        compute.setPipeline(computePipeline);
        compute.setBindGroup(0, computeGroup);
        compute.dispatchWorkgroups(workgroups);
        compute.end();

        const canvas = context.getCurrentTexture();
        canvasView = canvas.createView();
        // A complete priming frame renders directly to the canvas. Later frames
        // shade only a width/2 or width/3 target, with no per-column discards.
        attachment.view = step === 1 ? canvasView : stripView;
        const world = encoder.beginRenderPass(worldPass);
        world.setPipeline(worldPipeline);
        world.setBindGroup(0, worldGroup);
        world.draw(3);
        if (spriteCount > 0) {
            world.setPipeline(spritePipeline);
            world.setBindGroup(0, spriteGroup);
            world.draw(6, spriteCount);
        }
        world.end();

        if (step > 1) {
            presentAttachment.view = canvasView;
            const present = encoder.beginRenderPass(presentPass);
            present.setPipeline(presentPipeline);
            present.setBindGroup(0, presentGroup);
            present.draw(3);
            present.end();
        }
        if (history) {
            // Retain the world before adding screen layers, so transparent HUD
            // elements never accumulate in the interlaced image.
            copySource.texture = canvas;
            encoder.copyTextureToTexture(copySource, copyDestination, copySize);
        }
    }

    function submit() {
        device.queue.submit([encoder.finish()]);
        encoder = canvasView = null;
        attachment.view = presentAttachment.view = screenAttachment.view = null;
        copySource.texture = null;
    }

    return {
        setScene(world, sprites) { worldGroup = world; spriteGroup = sprites; },
        setColumns(retained, strip, presentation) {
            history = retained; stripView = strip; presentGroup = presentation;
            copyDestination.texture = retained;
            copySize[0] = retained.width; copySize[1] = retained.height;
        },
        clearColumns() { history = stripView = presentGroup = copyDestination.texture = null; },
        releaseScene() { worldGroup = spriteGroup = null; this.clearColumns(); },
        render(step, workgroups, spriteCount) { encode(step, workgroups, spriteCount); submit(); },
        begin(step, workgroups, spriteCount) {
            encode(step, workgroups, spriteCount);
            screenAttachment.view = canvasView;
            return encoder.beginRenderPass(screenPass);
        },
        submit
    };
}
