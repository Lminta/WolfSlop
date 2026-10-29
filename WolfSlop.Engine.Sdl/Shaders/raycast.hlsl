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
cbuffer CameraBuffer : register(b0,space2) { Camera camera; };
cbuffer MapBuffer : register(b1,space2) { MapInfo mapInfo; };
StructuredBuffer<RenderCell> worldMap : register(t0,space0);
RWStructuredBuffer<RayHit> rayHits : register(u0,space1);
[numthreads(64,1,1)]
void main(uint3 id : SV_DispatchThreadID) {
    uint x = id.x * uint(camera.columns.x) + uint(camera.columns.y);
    uint screenWidth = uint(camera.screenSize.x);

    if (x >= screenWidth) {
        return;
    }

    // Match fragment/rasterizer pixel centers, including thin door silhouettes.
    float cameraX =
        2.0 * (float(x) + 0.5) / camera.screenSize.x - 1.0;

    float2 rayDir =
        camera.direction + camera.plane * cameraX;
    // DDA stores perpendicular depth; the cutoff is a circle in world space.
    float rayLimit = choose(1e30, camera.look.z / length(rayDir), camera.look.z > 0.0);

    int mapX = int(floor(camera.position.x));
    int mapY = int(floor(camera.position.y));

    float deltaDistX = choose(
        abs(1.0 / rayDir.x),
        1e30,
        rayDir.x == 0.0
    );

    float deltaDistY = choose(
        abs(1.0 / rayDir.y),
        1e30,
        rayDir.y == 0.0
    );

    int stepX;
    int stepY;

    float sideDistX;
    float sideDistY;

    if (rayDir.x < 0.0) {
        stepX = -1;
        sideDistX =
            (camera.position.x - float(mapX)) * deltaDistX;
    } else {
        stepX = 1;
        sideDistX =
            (float(mapX) + 1.0 - camera.position.x) * deltaDistX;
    }

    if (rayDir.y < 0.0) {
        stepY = -1;
        sideDistY =
            (camera.position.y - float(mapY)) * deltaDistY;
    } else {
        stepY = 1;
        sideDistY =
            (float(mapY) + 1.0 - camera.position.y) * deltaDistY;
    }

    uint side = 0u;
    uint hitCell = 0u;
    float distance = 1e30;
    float doorU = 0.0;
    bool doorHit = false;

    // Protect against invalid maps or open boundaries.
    uint maxSteps = mapInfo.width + mapInfo.height + 1u;

    for (uint i = 0u; i < maxSteps; i++) {
        if (min(sideDistX, sideDistY) > rayLimit) {
            distance = rayLimit;
            break;
        }
        if (sideDistX < sideDistY) {
            sideDistX += deltaDistX;
            mapX += stepX;
            side = 0u;
        } else {
            sideDistY += deltaDistY;
            mapY += stepY;
            side = 1u;
        }

        if (mapX < 0 || mapY < 0 ||
            mapX >= int(mapInfo.width) ||
            mapY >= int(mapInfo.height)) {
            break;
        }

        // C# map[x][y] has been flattened row-major.
        uint index =
            uint(mapY) * mapInfo.width + uint(mapX);

        RenderCell cell = worldMap[index];

        if ((cell.flags & 8u) != 0u) {
            // Thin sliding plane at the cell center; the opening is a real ray gap.
            bool normalX = (cell.flags & 16u) != 0u;
            float normal = choose(rayDir.y, rayDir.x, normalX);
            if ((cell.flags & 1u) != 0u && abs(normal) > 0.000001) {
                float plane = choose(float(mapY), float(mapX), normalX) + 0.5;
                float origin = choose(camera.position.y, camera.position.x, normalX);
                float t = (plane - origin) / normal;
                float along = choose(camera.position.x + t * rayDir.x - float(mapX),
                    camera.position.y + t * rayDir.y - float(mapY), normalX);
                float opened = float(cell.flags >> 16u) / 65535.0;
                if (t >= 0.0 && t <= rayLimit && t <= min(sideDistX, sideDistY) + 0.00001 && along >= opened && along < 1.0) {
                    hitCell = index + 1u;
                    distance = t;
                    side = choose(1u, 0u, normalX);
                    doorU = along - opened;
                    doorHit = true;
                    break;
                }
            }
            continue;
        }

        if ((cell.flags & 1u) != 0u) {
            hitCell = index + 1u;

            distance = choose(
                sideDistY - deltaDistY,
                sideDistX - deltaDistX,
                side == 0u
            );

            break;
        }
    }

    float wallU = 0.0;

    if (doorHit) {
        wallU = doorU;
    } else if (hitCell != 0u) {
        float hitCoordinate = choose(
            camera.position.x + distance * rayDir.x,
            camera.position.y + distance * rayDir.y,
            side == 0u
        );

        wallU = frac(hitCoordinate);

        // Keep opposite wall faces oriented as in Lode's textured raycaster.
        if ((side == 0u && rayDir.x > 0.0) ||
            (side == 1u && rayDir.y < 0.0)) {
            wallU = 1.0 - wallU;
        }
    }

    RayHit result; result.distance=distance; result.wallU=wallU; result.hitCell=hitCell; result.side=side;
    rayHits[x] = result;
}
