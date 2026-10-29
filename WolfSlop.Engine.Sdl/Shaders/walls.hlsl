// Native SDL GPU shaders. Uniforms and resources follow SDL's register-space conventions.
struct Camera { float2 position; float2 direction; float2 plane; float2 screenSize; float4 look; float4 columns; };
struct MapInfo { uint width; uint height; uint spriteCount; uint padding; };
struct RenderCell { uint materialIndex; uint floorLayer; uint ceilingLayer; uint flags; };
struct RayHit { float distance; float wallU; uint hitCell; uint side; };
struct WallMaterial { uint north; uint east; uint south; uint west; };
struct Sprite { float2 position; uint textureLayer; uint ownerCell; float2 scale; float elevation; uint kind; };
float choose(float a,float b,bool c) { return c?b:a; }
uint choose(uint a,uint b,bool c) { return c?b:a; }
float2 choose(float2 a,float2 b,bool c) { return c?b:a; }
float3 choose(float3 a,float3 b,bool c) { return c?b:a; }
cbuffer CameraBuffer : register(b0,space3) { Camera camera; };
cbuffer MapBuffer : register(b1,space3) { MapInfo mapInfo; };
cbuffer SkyBuffer : register(b2,space3) { float4 skySettings; uint textureLayers; };
Texture2DArray<float4> wallTextures : register(t0,space2);
SamplerState wallSampler : register(s0,space2);
Texture2DArray<float4> repeatedTextures : register(t1,space2);
SamplerState surfaceSampler : register(s1,space2);
Texture2D<float4> skyTexture : register(t2,space2);
SamplerState skySampler : register(s2,space2);
StructuredBuffer<RayHit> rayHits : register(t3,space2);
StructuredBuffer<RenderCell> worldMap : register(t4,space2);
StructuredBuffer<WallMaterial> materials : register(t5,space2);
float4 sampleTexture(uint layer, float2 uv, float2 gradientX, float2 gradientY, bool repeat) {
    if (layer == 0xffffffffu || layer >= textureLayers) {
        return float4(1.0, 0.0, 1.0, 1.0);
    }
    if (repeat) {
        return repeatedTextures.SampleGrad(surfaceSampler,float3(uv,layer),gradientX,gradientY);
    }
    return wallTextures.SampleGrad(wallSampler,float3(uv,layer),gradientX,gradientY);
}

static const float3 fogColor = float3(0.62, 0.67, 0.70);

float fogAmount(float distance) {
    if (camera.look.z <= 0.0) { return 0.0; }
    return smoothstep(camera.look.w, camera.look.z, distance);
}

float3 skyColor(float2 position) {
    if (skySettings.y == 0.0) { return float3(0.0, 0.0, 0.0); }
    float pi = 3.141592653589793;
    float focalLength = camera.screenSize.x / (2.0 * length(camera.plane));
    float2 ray = camera.direction + camera.plane * (2.0 * position.x / camera.screenSize.x - 1.0);
    float horizon = camera.screenSize.y * 0.5 + tan(camera.look.x) * focalLength;
    float vertical = (horizon - position.y) / focalLength;
    float horizontalLength = length(ray);
    float2 uv = float2(atan2(ray.y, ray.x) / (2.0 * pi) + 0.5 + skySettings.x,
                   atan2(horizontalLength, vertical) / pi);
    // Differentiate before horizontal wrapping to keep the panorama seam crisp.
    float2 rayStep = camera.plane * (2.0 / camera.screenSize.x);
    float lengthSquared = dot(ray, ray);
    float totalSquared = lengthSquared + vertical * vertical;
    float2 gradientX = float2((ray.x * rayStep.y - ray.y * rayStep.x) / (lengthSquared * 2.0 * pi),
        vertical * dot(ray, rayStep) / (horizontalLength * totalSquared * pi));
    float2 gradientY = float2(0.0, horizontalLength / (totalSquared * focalLength * pi));
    float3 color = skyTexture.SampleGrad(skySampler,uv,gradientX,gradientY).rgb;
    if (camera.look.z <= 0.0) { return color; }
    // Fade the panorama around the horizon so the distant ground has no seam.
    float haze = 1.0 - smoothstep(2.0 / camera.look.z, 8.0 / camera.look.z, abs(vertical) / horizontalLength);
    return lerp(color, fogColor, haze);
}

float4 floorCeilingColor(float2 position) {
    float focalLength = camera.screenSize.x / (2.0 * length(camera.plane));
    float horizon = camera.screenSize.y * 0.5 + tan(camera.look.x) * focalLength;
    float horizonOffset = position.y - horizon;
    if (abs(horizonOffset) < 0.0001) {
        if (camera.look.z > 0.0) { return float4(fogColor, 1.0); }
        return float4(skyColor(position), 1.0);
    }
    float cameraHeight = 0.5 + camera.look.y;
    float planeDistance = choose(1.0 - cameraHeight, cameraHeight, horizonOffset > 0.0);
    float distance = focalLength * planeDistance / abs(horizonOffset);
    float cameraX = 2.0 * position.x / camera.screenSize.x - 1.0;
    float2 rayDir = camera.direction + camera.plane * cameraX;
    float worldDistance = distance * length(rayDir);
    if (camera.look.z > 0.0 && worldDistance >= camera.look.z) {
        return float4(fogColor, 1.0);
    }
    float2 worldPosition = camera.position + rayDir * distance;
    // Surface textures belong to the projected cell, not the camera's current tile.
    if (any(worldPosition < float2(0.0, 0.0)) ||
        worldPosition.x >= float(mapInfo.width) || worldPosition.y >= float(mapInfo.height)) {
        if (horizonOffset < 0.0 && skySettings.y != 0.0) { return float4(skyColor(position), 1.0); }
        return float4(1.0, 0.0, 1.0, 1.0);
    }
    uint2 tile = uint2(worldPosition);
    RenderCell cell = worldMap[tile.y * mapInfo.width + tile.x];
    if (horizonOffset < 0.0 && (cell.flags & 4u) != 0u) {
        return float4(skyColor(position), 1.0);
    }
    uint layer = choose(cell.ceilingLayer, cell.floorLayer, horizonOffset > 0.0);
    // Gradients of the continuous world coordinates, before frac() wraps at tile seams.
    float2 gradientX = camera.plane * (2.0 * distance / camera.screenSize.x);
    float2 gradientY = -rayDir * (distance / horizonOffset);
    float4 surface = sampleTexture(layer, frac(worldPosition), gradientX, gradientY, true);
    if (horizonOffset < 0.0 && surface.a < 1.0) {
        // Tile textures are uploaded with premultiplied alpha.
        float3 color = surface.rgb + skyColor(position) * (1.0 - surface.a);
        return float4(lerp(color, fogColor, fogAmount(worldDistance)), 1.0);
    }
    return float4(lerp(surface.rgb, fogColor, fogAmount(worldDistance)), 1.0);
}
float4 main(float4 pixel : SV_Position) : SV_Target0 {
    // Packed target pixels correspond to full-resolution pixel-center rays.
    float2 position = pixel.xy;
    if (uint(pixel.x) % uint(camera.columns.x) != uint(camera.columns.y)) discard;
    uint x = uint(position.x);

    if (x >= uint(camera.screenSize.x)) {
        return float4(0.0, 0.0, 0.0, 1.0);
    }

    RayHit hit = rayHits[x];

    if (hit.hitCell == 0u || hit.distance <= 0.0) {
        return floorCeilingColor(position);
    }

    RenderCell cell = worldMap[hit.hitCell - 1u];
    if ((cell.flags & 2u) == 0u) {
        return floorCeilingColor(position);
    }
    // An opaque door already stopped this exact pixel-center ray. Draw its
    // panel here as well as in the sprite pass so subpixel triangle edge
    // rounding cannot expose the floor behind a ray-blocking door.
    bool isDoor = (cell.flags & 8u) != 0u;
    float screenHeight = camera.screenSize.y;
    // Use the same focal length as horizontal ray projection so unit tiles stay square.
    float focalLength = camera.screenSize.x / (2.0 * length(camera.plane));
    float lineHeight = focalLength / hit.distance;
    float horizon = screenHeight * 0.5 + tan(camera.look.x) * focalLength;
    float wallCenter = horizon + camera.look.y * lineHeight;

    float drawStart = max(
        0.0,
        -lineHeight * 0.5 + wallCenter
    );

    float drawEnd = min(
        screenHeight,
        lineHeight * 0.5 + wallCenter
    );

    float y = position.y;

    if (y < drawStart || y >= drawEnd) {
        return floorCeilingColor(position);
    }

    // Use the full projected wall, even when its top is above the screen.
    float wallV = (y - wallCenter) / lineHeight + 0.5;
    // Side 0 crosses an X face; side 1 crosses a Y face.
    float cameraX = 2.0 * position.x / camera.screenSize.x - 1.0;
    float2 rayDir = camera.direction + camera.plane * cameraX;
    WallMaterial material = materials[cell.materialIndex];
    uint layer = choose(material.east, material.west, rayDir.x > 0.0);
    if (hit.side == 1u) {
        layer = choose(material.south, material.north, rayDir.y > 0.0);
    }
    // Differentiate this wall plane analytically. Neighboring rays may hit a different
    // tile or face, so derivatives of the wrapped wallU would pick incorrect mip levels.
    float2 rayStep = camera.plane * (2.0 / camera.screenSize.x);
    float rayNormal = choose(rayDir.y, rayDir.x, hit.side == 0u);
    float stepNormal = choose(rayStep.y, rayStep.x, hit.side == 0u);
    float rayAlong = choose(rayDir.x, rayDir.y, hit.side == 0u);
    float stepAlong = choose(rayStep.x, rayStep.y, hit.side == 0u);
    float distanceStep = -hit.distance * stepNormal / rayNormal;
    float wallUStep = hit.distance * stepAlong + distanceStep * rayAlong;
    if (!isDoor && ((hit.side == 0u && rayDir.x > 0.0) || (hit.side == 1u && rayDir.y < 0.0))) {
        wallUStep = -wallUStep;
    }
    float2 gradientX = float2(wallUStep, (y - horizon) * distanceStep / focalLength);
    float2 gradientY = float2(0.0, 1.0 / lineHeight);
    float4 texel = sampleTexture(layer, float2(hit.wallU, wallV), gradientX, gradientY, false);
    float3 color = texel.rgb;
    if (isDoor) { color /= max(texel.a, 0.0001); }
    if (!isDoor && hit.side == 1u && any(color != float3(1.0, 0.0, 1.0))) {
        color *= 0.5;
    }
    return float4(lerp(color, fogColor, fogAmount(hit.distance * length(rayDir))), 1.0);
}
