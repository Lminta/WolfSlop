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
cbuffer CameraBuffer:register(b0,space1) { Camera camera; };
StructuredBuffer<Sprite> sprites:register(t0,space0);
VertexOutput vertexMain(uint vertex : SV_VertexID, uint instance : SV_InstanceID) {
    float2 corners[6] = {
        float2(0.0, 0.0), float2(1.0, 0.0), float2(0.0, 1.0),
        float2(0.0, 1.0), float2(1.0, 0.0), float2(1.0, 1.0)
    };
    Sprite sprite = sprites[instance];
    float2 relative = sprite.position - camera.position;

    // Lode's inverse camera matrix gives horizontal position and perpendicular depth.
    float inverseDet = 1.0 / (
        camera.plane.x * camera.direction.y - camera.direction.x * camera.plane.y
    );
    float cameraX = inverseDet * (
        camera.direction.y * relative.x - camera.direction.x * relative.y
    );
    float depth = inverseDet * (
        -camera.plane.y * relative.x + camera.plane.x * relative.y
    );

    VertexOutput output;
    output.uv = corners[vertex];
    output.depth = depth;
    output.textureLayer = sprite.textureLayer;
    output.ownerCell = sprite.ownerCell;
    output.kind = sprite.kind;
    bool directionalPlane = (sprite.kind & 16u) != 0u;
    if ((sprite.kind & 3u) != 0u || directionalPlane) {
        // Doors and fixed decorations use world-aligned quads.
        float2 axis = choose(float2(1.0, 0.0), float2(0.0, 1.0), (sprite.kind & 3u) == 1u);
        if (directionalPlane) {
            float angle = float(sprite.kind >> 16u) * (6.28318530718 / 65536.0);
            axis = float2(-sin(angle), cos(angle));
        }
        float2 worldPoint = sprite.position + axis * ((corners[vertex].x - 0.5) * sprite.scale.x);
        float2 pointRelative = worldPoint - camera.position;
        float across = inverseDet * (camera.direction.y * pointRelative.x - camera.direction.x * pointRelative.y);
        float pointDepth = inverseDet * (-camera.plane.y * pointRelative.x + camera.plane.x * pointRelative.y);
        output.depth = pointDepth;
        // A sliding door reveals a cropped panel; a fixed decoration uses its full image.
        output.uv = float2(corners[vertex].x * choose(sprite.scale.x, 1.0, (sprite.kind & 8u) != 0u), corners[vertex].y);
        // Keep every corner in homogeneous clip space, including corners behind
        // the eye. The GPU clips the original triangles at z=0 (depth=near).
        // Moving individual corners offscreen instead creates spurious diagonals
        // whenever the camera passes beside or through a door's plane.
        float near = 0.0001;
        float focalLength = camera.screenSize.x / (2.0 * length(camera.plane));
        float vertical = camera.look.y - sprite.elevation + (corners[vertex].y - 0.5) * sprite.scale.y;
        output.position = float4(
            across,
            -2.0 * focalLength / camera.screenSize.y * (tan(camera.look.x) * pointDepth + vertical),
            pointDepth - near,
            pointDepth
        );
        return output;
    }
    // Collapse sprites behind or on the camera plane to an offscreen point.
    output.position = float4(2.0, 2.0, 0.0, 1.0);
    if (depth <= 0.0001) {
        return output;
    }

    // Per-member billboard scale; positive elevation moves the center upward in world units.
    float focalLength = camera.screenSize.x / (2.0 * length(camera.plane));
    float size = focalLength / depth;
    float2 center = float2(
        camera.screenSize.x * 0.5 * (1.0 + cameraX / depth),
        camera.screenSize.y * 0.5 + tan(camera.look.x) * focalLength + (camera.look.y - sprite.elevation) * size
    );
    float2 pixel = center + (output.uv - float2(0.5, 0.5)) * size * sprite.scale;
    float2 ndc = pixel / camera.screenSize * float2(2.0, -2.0) + float2(-1.0, 1.0);
    output.position = float4(ndc, 0.0, 1.0);
    return output;
}

