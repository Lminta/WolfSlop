
struct Camera {
    position: vec2f,
    direction: vec2f,
    plane: vec2f,
    screenSize: vec2f,
    look: vec4f, // pitch, camera elevation, render distance, fog start.
};

struct RenderCell {
    materialIndex: u32,
    floorLayer: u32,
    ceilingLayer: u32,
    flags: u32,
};

struct WallMaterial {
    north: u32,
    east: u32,
    south: u32,
    west: u32,
};
struct RayHit {
    distance: f32,
    wallU: f32,
    hitCell: u32,
    side: u32,
};

struct VertexOutput {
    @builtin(position) position: vec4f,
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

@group(0) @binding(0)
var<storage, read> rayHits: array<RayHit>;

@group(0) @binding(1)
var<uniform> camera: Camera;

@group(0) @binding(2)
var wallTextures: texture_2d_array<f32>;

// Shared with the raycasting and sprite passes; no separate minimap upload.
@group(0) @binding(3) var<storage, read> worldMap: array<RenderCell>;
@group(0) @binding(4) var<uniform> mapInfo: MapInfo;
@group(0) @binding(5) var<storage, read> sprites: array<Sprite>;

@group(0) @binding(6) var<storage, read> materials: array<WallMaterial>;
@group(0) @binding(7) var wallSampler: sampler;
@group(0) @binding(8) var surfaceSampler: sampler;
@group(0) @binding(9) var skyTexture: texture_2d<f32>;
@group(0) @binding(10) var skySampler: sampler;
// x: panorama rotation in turns; y: sky enabled.
@group(0) @binding(11) var<uniform> skySettings: vec4f;

// A final overlay pass on the game canvas, using the compute pass's DDA results.
@fragment
fn minimapMain(input: VertexOutput) -> @location(0) vec4f {
    let mapSize = vec2f(f32(mapInfo.width), f32(mapInfo.height));
    let panelLimit = min(256.0, min(camera.screenSize.x, camera.screenSize.y) * 0.32);
    let padding = min(10.0, panelLimit * 0.05);
    let scale = (panelLimit - 2.0 * padding) / max(mapSize.x, mapSize.y);
    let panelSize = mapSize * scale + vec2f(2.0 * padding);
    let pixel = input.position.xy - vec2f(16.0);
    let radius = min(8.0, padding);
    let corner = abs(pixel - panelSize * 0.5) - (panelSize * 0.5 - vec2f(radius));
    let edge = length(max(corner, vec2f(0.0))) + min(max(corner.x, corner.y), 0.0) - radius;
    if (edge > 0.0) {
        discard;
    }
    if (edge > -1.0) {
        return vec4f(0.51, 0.57, 0.62, 1.0);
    }

    let mapPoint = (pixel - vec2f(padding)) / scale;
    if (any(mapPoint < vec2f(0.0)) || any(mapPoint >= mapSize)) {
        return vec4f(0.067, 0.098, 0.137, 1.0);
    }
    // Lode's camera has screen-right toward -X when facing north (-Y).
    // Mirror the complete overlay so strafing and its view cone match that handedness.
    let world = vec2f(mapSize.x - mapPoint.x, mapPoint.y);

    let cell = min(vec2u(world), vec2u(mapSize) - vec2u(1u));
    let wall = worldMap[cell.y * mapInfo.width + cell.x];
    var color = select(vec3f(0.137, 0.192, 0.239), vec3f(0.51, 0.57, 0.62), (wall.flags & 2u) != 0u);
    let cellEdge = min(fract(world), vec2f(1.0) - fract(world)) * scale;
    if (min(cellEdge.x, cellEdge.y) < 0.3) {
        color *= 0.55;
    }

    // Project this map point back to a screen column and use that ray's depth
    // to show the actual visible region, clipped by the first wall hit.
    let relative = world - camera.position;
    let inverseDet = 1.0 / (camera.plane.x * camera.direction.y - camera.direction.x * camera.plane.y);
    let across = inverseDet * (camera.direction.y * relative.x - camera.direction.x * relative.y);
    let depth = inverseDet * (-camera.plane.y * relative.x + camera.plane.x * relative.y);
    let withinRenderDistance = camera.look.z <= 0.0 || dot(relative, relative) <= camera.look.z * camera.look.z;
    if (depth > 0.0001 && abs(across) <= depth && withinRenderDistance) {
        let column = u32(clamp(
            (across / depth + 1.0) * 0.5 * camera.screenSize.x,
            0.0, camera.screenSize.x - 1.0
        ));
        let hit = rayHits[column];
        if (hit.hitCell == 0u || depth <= hit.distance) {
            color = mix(color, vec3f(0.27, 0.75, 1.0), 0.35);
        }
    }

    for (var i = 0u; i < mapInfo.spriteCount; i++) {
        let sprite = sprites[i];
        if (distance(world, sprite.position) < max(0.18, 1.5 / scale)) {
            color = vec3f(0.89, 0.68, 0.38);
            if (sprite.textureLayer == 9u) {
                color = vec3f(0.85, 0.88, 0.91);
            } else if (sprite.textureLayer == 10u) {
                color = vec3f(0.49, 0.90, 0.43);
            }
        }
    }

    let forward = normalize(camera.direction);
    let along = dot(relative, forward);
    let sideways = abs(dot(relative, vec2f(-forward.y, forward.x)));
    if (along >= -0.3 && along <= 0.55 && sideways <= (0.55 - along) * (0.32 / 0.85)) {
        color = vec3f(0.27, 0.75, 1.0);
    }
    return vec4f(color, 1.0);
}

@vertex
fn vertexMain(
    @builtin(vertex_index) index: u32
) -> VertexOutput {
    var positions = array<vec2f, 3>(
        vec2f(-1.0, -1.0),
        vec2f( 3.0, -1.0),
        vec2f(-1.0,  3.0)
    );

    var output: VertexOutput;
    output.position =
        vec4f(positions[index], 0.0, 1.0);

    return output;
}

fn sampleTexture(layer: u32, uv: vec2f, gradientX: vec2f, gradientY: vec2f, repeat: bool) -> vec4f {
    if (layer == 0xffffffffu || layer >= textureNumLayers(wallTextures)) {
        return vec4f(1.0, 0.0, 1.0, 1.0);
    }
    if (repeat) {
        return textureSampleGrad(wallTextures, surfaceSampler, uv, i32(layer), gradientX, gradientY);
    }
    return textureSampleGrad(wallTextures, wallSampler, uv, i32(layer), gradientX, gradientY);
}

const fogColor = vec3f(0.62, 0.67, 0.70);

fn fogAmount(distance: f32) -> f32 {
    if (camera.look.z <= 0.0) { return 0.0; }
    return smoothstep(camera.look.w, camera.look.z, distance);
}

fn skyColor(position: vec2f) -> vec3f {
    if (skySettings.y == 0.0) { return vec3f(0.0); }
    let pi = 3.141592653589793;
    let focalLength = camera.screenSize.x / (2.0 * length(camera.plane));
    let ray = camera.direction + camera.plane * (2.0 * position.x / camera.screenSize.x - 1.0);
    let horizon = camera.screenSize.y * 0.5 + tan(camera.look.x) * focalLength;
    let vertical = (horizon - position.y) / focalLength;
    let horizontalLength = length(ray);
    let uv = vec2f(atan2(ray.y, ray.x) / (2.0 * pi) + 0.5 + skySettings.x,
                   atan2(horizontalLength, vertical) / pi);
    // Differentiate before horizontal wrapping to keep the panorama seam crisp.
    let rayStep = camera.plane * (2.0 / camera.screenSize.x);
    let lengthSquared = dot(ray, ray);
    let totalSquared = lengthSquared + vertical * vertical;
    let gradientX = vec2f((ray.x * rayStep.y - ray.y * rayStep.x) / (lengthSquared * 2.0 * pi),
        vertical * dot(ray, rayStep) / (horizontalLength * totalSquared * pi));
    let gradientY = vec2f(0.0, horizontalLength / (totalSquared * focalLength * pi));
    let color = textureSampleGrad(skyTexture, skySampler, uv, gradientX, gradientY).rgb;
    if (camera.look.z <= 0.0) { return color; }
    // Fade the panorama around the horizon so the distant ground has no seam.
    let haze = 1.0 - smoothstep(2.0 / camera.look.z, 8.0 / camera.look.z, abs(vertical) / horizontalLength);
    return mix(color, fogColor, haze);
}

fn floorCeilingColor(position: vec2f) -> vec4f {
    let focalLength = camera.screenSize.x / (2.0 * length(camera.plane));
    let horizon = camera.screenSize.y * 0.5 + tan(camera.look.x) * focalLength;
    let horizonOffset = position.y - horizon;
    if (abs(horizonOffset) < 0.0001) {
        if (camera.look.z > 0.0) { return vec4f(fogColor, 1.0); }
        return vec4f(skyColor(position), 1.0);
    }
    let cameraHeight = 0.5 + camera.look.y;
    let planeDistance = select(1.0 - cameraHeight, cameraHeight, horizonOffset > 0.0);
    let distance = focalLength * planeDistance / abs(horizonOffset);
    let cameraX = 2.0 * position.x / camera.screenSize.x - 1.0;
    let rayDir = camera.direction + camera.plane * cameraX;
    let worldDistance = distance * length(rayDir);
    if (camera.look.z > 0.0 && worldDistance >= camera.look.z) {
        return vec4f(fogColor, 1.0);
    }
    let worldPosition = camera.position + rayDir * distance;
    // Surface textures belong to the projected cell, not the camera's current tile.
    if (any(worldPosition < vec2f(0.0)) ||
        worldPosition.x >= f32(mapInfo.width) || worldPosition.y >= f32(mapInfo.height)) {
        if (horizonOffset < 0.0 && skySettings.y != 0.0) { return vec4f(skyColor(position), 1.0); }
        return vec4f(1.0, 0.0, 1.0, 1.0);
    }
    let tile = vec2u(worldPosition);
    let cell = worldMap[tile.y * mapInfo.width + tile.x];
    if (horizonOffset < 0.0 && (cell.flags & 4u) != 0u) {
        return vec4f(skyColor(position), 1.0);
    }
    let layer = select(cell.ceilingLayer, cell.floorLayer, horizonOffset > 0.0);
    // Gradients of the continuous world coordinates, before fract() wraps at tile seams.
    let gradientX = camera.plane * (2.0 * distance / camera.screenSize.x);
    let gradientY = -rayDir * (distance / horizonOffset);
    let surface = sampleTexture(layer, fract(worldPosition), gradientX, gradientY, true);
    if (horizonOffset < 0.0 && surface.a < 1.0) {
        // Tile textures are uploaded with premultiplied alpha.
        let color = surface.rgb + skyColor(position) * (1.0 - surface.a);
        return vec4f(mix(color, fogColor, fogAmount(worldDistance)), 1.0);
    }
    return vec4f(mix(surface.rgb, fogColor, fogAmount(worldDistance)), 1.0);
}
@fragment
fn fragmentMain(
    input: VertexOutput
) -> @location(0) vec4f {
    let x = u32(input.position.x);

    if (x >= u32(camera.screenSize.x)) {
        return vec4f(0.0, 0.0, 0.0, 1.0);
    }

    let hit = rayHits[x];

    if (hit.hitCell == 0u || hit.distance <= 0.0) {
        return floorCeilingColor(input.position.xy);
    }

    let cell = worldMap[hit.hitCell - 1u];
    if ((cell.flags & 2u) == 0u) {
        return floorCeilingColor(input.position.xy);
    }
    // An opaque door already stopped this exact pixel-center ray. Draw its
    // panel here as well as in the sprite pass so subpixel triangle edge
    // rounding cannot expose the floor behind a ray-blocking door.
    let isDoor = (cell.flags & 8u) != 0u;
    let screenHeight = camera.screenSize.y;
    // Use the same focal length as horizontal ray projection so unit tiles stay square.
    let focalLength = camera.screenSize.x / (2.0 * length(camera.plane));
    let lineHeight = focalLength / hit.distance;
    let horizon = screenHeight * 0.5 + tan(camera.look.x) * focalLength;
    let wallCenter = horizon + camera.look.y * lineHeight;

    let drawStart = max(
        0.0,
        -lineHeight * 0.5 + wallCenter
    );

    let drawEnd = min(
        screenHeight,
        lineHeight * 0.5 + wallCenter
    );

    let y = input.position.y;

    if (y < drawStart || y >= drawEnd) {
        return floorCeilingColor(input.position.xy);
    }

    // Use the full projected wall, even when its top is above the screen.
    let wallV = (y - wallCenter) / lineHeight + 0.5;
    // Side 0 crosses an X face; side 1 crosses a Y face.
    let cameraX = 2.0 * input.position.x / camera.screenSize.x - 1.0;
    let rayDir = camera.direction + camera.plane * cameraX;
    let material = materials[cell.materialIndex];
    var layer = select(material.east, material.west, rayDir.x > 0.0);
    if (hit.side == 1u) {
        layer = select(material.south, material.north, rayDir.y > 0.0);
    }
    // Differentiate this wall plane analytically. Neighboring rays may hit a different
    // tile or face, so derivatives of the wrapped wallU would pick incorrect mip levels.
    let rayStep = camera.plane * (2.0 / camera.screenSize.x);
    let rayNormal = select(rayDir.y, rayDir.x, hit.side == 0u);
    let stepNormal = select(rayStep.y, rayStep.x, hit.side == 0u);
    let rayAlong = select(rayDir.x, rayDir.y, hit.side == 0u);
    let stepAlong = select(rayStep.x, rayStep.y, hit.side == 0u);
    let distanceStep = -hit.distance * stepNormal / rayNormal;
    var wallUStep = hit.distance * stepAlong + distanceStep * rayAlong;
    if (!isDoor && ((hit.side == 0u && rayDir.x > 0.0) || (hit.side == 1u && rayDir.y < 0.0))) {
        wallUStep = -wallUStep;
    }
    let gradientX = vec2f(wallUStep, (y - horizon) * distanceStep / focalLength);
    let gradientY = vec2f(0.0, 1.0 / lineHeight);
    let texel = sampleTexture(layer, vec2f(hit.wallU, wallV), gradientX, gradientY, false);
    var color = texel.rgb;
    if (isDoor) { color /= max(texel.a, 0.0001); }
    if (!isDoor && hit.side == 1u && any(color != vec3f(1.0, 0.0, 1.0))) {
        color *= 0.5;
    }
    return vec4f(mix(color, fogColor, fogAmount(hit.distance * length(rayDir))), 1.0);
}
