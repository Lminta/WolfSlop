struct Camera {
    position: vec2f,
    direction: vec2f,
    plane: vec2f,
    screenSize: vec2f,
    look: vec4f, // pitch, camera elevation, render distance, fog start.
};

struct RayHit {
    distance: f32,
    wallU: f32,
    hitCell: u32,
    side: u32,
};

struct RenderCell {
    materialIndex: u32,
    floorLayer: u32,
    ceilingLayer: u32,
    flags: u32,
};

struct MapInfo {
    width: u32,
    height: u32,
    spriteCount: u32,
    padding: u32,
};

struct Sprite {
    position: vec2f,
    textureLayer: u32,
    ownerCell: u32,
    scale: vec2f,
    elevation: f32,
    kind: u32,
};

struct VertexOutput {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
    @location(1) depth: f32,
    @location(2) @interpolate(flat) textureLayer: u32,
    @location(3) @interpolate(flat) ownerCell: u32,
    @location(4) @interpolate(flat) kind: u32,
};

@group(0) @binding(0) var<storage, read> rayHits: array<RayHit>;
@group(0) @binding(1) var<uniform> camera: Camera;
@group(0) @binding(2) var spriteTextures: texture_2d_array<f32>;
@group(0) @binding(3) var<storage, read> sprites: array<Sprite>;
@group(0) @binding(4) var spriteSampler: sampler;
@group(0) @binding(5) var<storage, read> worldMap: array<RenderCell>;
@group(0) @binding(6) var<uniform> mapInfo: MapInfo;

@vertex
fn vertexMain(
    @builtin(vertex_index) vertex: u32,
    @builtin(instance_index) instance: u32
) -> VertexOutput {
    let corners = array<vec2f, 6>(
        vec2f(0.0, 0.0), vec2f(1.0, 0.0), vec2f(0.0, 1.0),
        vec2f(0.0, 1.0), vec2f(1.0, 0.0), vec2f(1.0, 1.0)
    );
    let sprite = sprites[instance];
    let relative = sprite.position - camera.position;

    // Lode's inverse camera matrix gives horizontal position and perpendicular depth.
    let inverseDet = 1.0 / (
        camera.plane.x * camera.direction.y - camera.direction.x * camera.plane.y
    );
    let cameraX = inverseDet * (
        camera.direction.y * relative.x - camera.direction.x * relative.y
    );
    let depth = inverseDet * (
        -camera.plane.y * relative.x + camera.plane.x * relative.y
    );

    var output: VertexOutput;
    output.uv = corners[vertex];
    output.depth = depth;
    output.textureLayer = sprite.textureLayer;
    output.ownerCell = sprite.ownerCell;
    output.kind = sprite.kind;
    let directionalPlane = (sprite.kind & 16u) != 0u;
    if ((sprite.kind & 3u) != 0u || directionalPlane) {
        // Doors and fixed decorations use world-aligned quads.
        var axis = select(vec2f(1.0, 0.0), vec2f(0.0, 1.0), (sprite.kind & 3u) == 1u);
        if (directionalPlane) {
            let angle = f32(sprite.kind >> 16u) * (6.28318530718 / 65536.0);
            axis = vec2f(-sin(angle), cos(angle));
        }
        let point = sprite.position + axis * ((corners[vertex].x - 0.5) * sprite.scale.x);
        let pointRelative = point - camera.position;
        let across = inverseDet * (camera.direction.y * pointRelative.x - camera.direction.x * pointRelative.y);
        let pointDepth = inverseDet * (-camera.plane.y * pointRelative.x + camera.plane.x * pointRelative.y);
        output.depth = pointDepth;
        // A sliding door reveals a cropped panel; a fixed decoration uses its full image.
        output.uv = vec2f(corners[vertex].x * select(sprite.scale.x, 1.0, (sprite.kind & 8u) != 0u), corners[vertex].y);
        // Keep every corner in homogeneous clip space, including corners behind
        // the eye. The GPU clips the original triangles at z=0 (depth=near).
        // Moving individual corners offscreen instead creates spurious diagonals
        // whenever the camera passes beside or through a door's plane.
        let near = 0.0001;
        let focalLength = camera.screenSize.x / (2.0 * length(camera.plane));
        let vertical = camera.look.y - sprite.elevation + (corners[vertex].y - 0.5) * sprite.scale.y;
        output.position = vec4f(
            across,
            -2.0 * focalLength / camera.screenSize.y * (tan(camera.look.x) * pointDepth + vertical),
            pointDepth - near,
            pointDepth
        );
        return output;
    }
    // Collapse sprites behind or on the camera plane to an offscreen point.
    output.position = vec4f(2.0, 2.0, 0.0, 1.0);
    if (depth <= 0.0001) {
        return output;
    }

    // Per-member billboard scale; positive elevation moves the center upward in world units.
    let focalLength = camera.screenSize.x / (2.0 * length(camera.plane));
    let size = focalLength / depth;
    let center = vec2f(
        camera.screenSize.x * 0.5 * (1.0 + cameraX / depth),
        camera.screenSize.y * 0.5 + tan(camera.look.x) * focalLength + (camera.look.y - sprite.elevation) * size
    );
    let pixel = center + (output.uv - vec2f(0.5)) * size * sprite.scale;
    let ndc = pixel / camera.screenSize * vec2f(2.0, -2.0) + vec2f(-1.0, 1.0);
    output.position = vec4f(ndc, 0.0, 1.0);
    return output;
}

@fragment
fn fragmentMain(input: VertexOutput) -> @location(0) vec4f {
    // Evaluate derivatives before the per-column occlusion branch/discard.
    let gradientX = dpdx(input.uv);
    let gradientY = dpdy(input.uv);
    let ray = camera.direction + camera.plane * (2.0 * input.position.x / camera.screenSize.x - 1.0);
    let worldDistance = input.depth * length(ray);
    if (camera.look.z > 0.0 && worldDistance >= camera.look.z) { discard; }
    let hit = rayHits[u32(input.position.x)];
    // Ray depth only occludes the projected wall, not the sky above its roof.
    // Match the wall pass's bounds, including pitch and camera elevation.
    if (hit.hitCell != 0u && hit.hitCell != input.ownerCell && input.depth >= hit.distance) {
        // Non-wall ray blockers retain their existing occlusion behavior.
        if ((worldMap[hit.hitCell - 1u].flags & 2u) == 0u) { discard; }
        let focalLength = camera.screenSize.x / (2.0 * length(camera.plane));
        let lineHeight = focalLength / hit.distance;
        let horizon = camera.screenSize.y * 0.5 + tan(camera.look.x) * focalLength;
        let wallCenter = horizon + camera.look.y * lineHeight;
        if (input.position.y >= wallCenter - lineHeight * 0.5 &&
            input.position.y < wallCenter + lineHeight * 0.5) {
            discard;
        }
        // Below the wall, the ground still hides the sprite. Above it, only
        // an open ceiling reveals the sky; a building's ceiling remains solid.
        let offset = input.position.y - horizon;
        if (offset >= -0.0001) { discard; }
        let ceilingDepth = focalLength * (0.5 - camera.look.y) / -offset;
        let ceilingPosition = camera.position + ray * ceilingDepth;
        if (ceilingDepth > 0.0 && ceilingDepth < input.depth &&
            all(ceilingPosition >= vec2f(0.0)) &&
            ceilingPosition.x < f32(mapInfo.width) && ceilingPosition.y < f32(mapInfo.height)) {
            let tile = vec2u(ceilingPosition);
            if ((worldMap[tile.y * mapInfo.width + tile.x].flags & 4u) == 0u) { discard; }
        }
    }

    // Mips store premultiplied RGBA; blending uses ONE, ONE_MINUS_SRC_ALPHA.
    var color = textureSampleGrad(spriteTextures, spriteSampler, input.uv, i32(input.textureLayer), gradientX, gradientY);
    if ((input.kind & 32u) != 0u) { color = color.bgra; }
    if ((input.kind & 64u) != 0u) { color = color.grba; }
    var fog = 0.0;
    var visibility = 1.0;
    if (camera.look.z > 0.0) {
        fog = smoothstep(camera.look.w, camera.look.z, worldDistance);
        // Tall silhouettes also dissolve into sky away from the hazy horizon.
        visibility = 1.0 - smoothstep(camera.look.z * 0.9, camera.look.z, worldDistance);
    }
    let fogColor = vec3f(0.62, 0.67, 0.70);
    if ((input.kind & 4u) != 0u) {
        // Opaque doors seal the plane even when their chosen texture contains alpha.
        let shaded = mix(color.rgb / max(color.a, 0.0001), fogColor, fog);
        return vec4f(shaded * visibility, visibility);
    }
    if (color.a == 0.0) {
        discard;
    }
    return vec4f(mix(color.rgb, fogColor * color.a, fog) * visibility, color.a * visibility);
}
