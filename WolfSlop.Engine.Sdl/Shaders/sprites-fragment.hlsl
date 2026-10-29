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
struct VertexOutput { float4 position:SV_Position; float2 uv:TEXCOORD0; float depth:TEXCOORD1;
 nointerpolation uint textureLayer:TEXCOORD2; nointerpolation uint ownerCell:TEXCOORD3; nointerpolation uint kind:TEXCOORD4; };
cbuffer CameraBuffer:register(b0,space3) { Camera camera; };
cbuffer MapBuffer:register(b1,space3) { MapInfo mapInfo; };
Texture2DArray<float4> spriteTextures:register(t0,space2);
SamplerState spriteSampler:register(s0,space2);
StructuredBuffer<RayHit> rayHits:register(t1,space2);
StructuredBuffer<RenderCell> worldMap:register(t2,space2);
float4 fragmentMain(VertexOutput input) : SV_Target0 {
    // Evaluate derivatives before the per-column occlusion branch/discard.
    float2 gradientX = ddx(input.uv);
    float2 gradientY = ddy(input.uv);
    float2 position = input.position.xy;
    if (uint(position.x) % uint(camera.columns.x) != uint(camera.columns.y)) discard;
    if (position.x >= camera.screenSize.x) { discard; }
    float2 ray = camera.direction + camera.plane * (2.0 * position.x / camera.screenSize.x - 1.0);
    float worldDistance = input.depth * length(ray);
    if (camera.look.z > 0.0 && worldDistance >= camera.look.z) { discard; }
    RayHit hit = rayHits[uint(position.x)];
    // Ray depth only occludes the projected wall, not the sky above its roof.
    // Match the wall pass's bounds, including pitch and camera elevation.
    if (hit.hitCell != 0u && hit.hitCell != input.ownerCell && input.depth >= hit.distance) {
        // Non-wall ray blockers retain their existing occlusion behavior.
        if ((worldMap[hit.hitCell - 1u].flags & 2u) == 0u) { discard; }
        float focalLength = camera.screenSize.x / (2.0 * length(camera.plane));
        float lineHeight = focalLength / hit.distance;
        float horizon = camera.screenSize.y * 0.5 + tan(camera.look.x) * focalLength;
        float wallCenter = horizon + camera.look.y * lineHeight;
        if (input.position.y >= wallCenter - lineHeight * 0.5 &&
            input.position.y < wallCenter + lineHeight * 0.5) {
            discard;
        }
        // Below the wall, the ground still hides the sprite. Above it, only
        // an open ceiling reveals the sky; a building's ceiling remains solid.
        float offset = input.position.y - horizon;
        if (offset >= -0.0001) { discard; }
        float ceilingDepth = focalLength * (0.5 - camera.look.y) / -offset;
        float2 ceilingPosition = camera.position + ray * ceilingDepth;
        if (ceilingDepth > 0.0 && ceilingDepth < input.depth &&
            all(ceilingPosition >= float2(0.0, 0.0)) &&
            ceilingPosition.x < float(mapInfo.width) && ceilingPosition.y < float(mapInfo.height)) {
            uint2 tile = uint2(ceilingPosition);
            if ((worldMap[tile.y * mapInfo.width + tile.x].flags & 4u) == 0u) { discard; }
        }
    }

    // Mips store premultiplied RGBA; blending uses ONE, ONE_MINUS_SRC_ALPHA.
    float4 color = spriteTextures.SampleGrad(spriteSampler,float3(input.uv,input.textureLayer),gradientX,gradientY);
    if ((input.kind & 32u) != 0u) { color = color.bgra; }
    if ((input.kind & 64u) != 0u) { color = color.grba; }
    float fog = 0.0;
    float visibility = 1.0;
    if (camera.look.z > 0.0) {
        fog = smoothstep(camera.look.w, camera.look.z, worldDistance);
        // Tall silhouettes also dissolve into sky away from the hazy horizon.
        visibility = 1.0 - smoothstep(camera.look.z * 0.9, camera.look.z, worldDistance);
    }
    float3 fogColor = float3(0.62, 0.67, 0.70);
    if ((input.kind & 4u) != 0u) {
        // Opaque doors seal the plane even when their chosen texture contains alpha.
        float3 shaded = lerp(color.rgb / max(color.a, 0.0001), fogColor, fog);
        return float4(shaded * visibility, visibility);
    }
    if (color.a == 0.0) {
        discard;
    }
    return float4(lerp(color.rgb, fogColor * color.a, fog) * visibility, color.a * visibility);
}
