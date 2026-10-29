
struct Camera {
    position: vec2f,
    direction: vec2f,
    plane: vec2f,
    screenSize: vec2f,
    look: vec4f, // pitch, camera elevation, render distance, fog start.
    columns: vec4f, // column step, rotating phase, unused, unused.
};

struct MapInfo {
    width: u32,
    height: u32,
    spriteCount: u32,
    padding: u32,
};

struct RenderCell {
    materialIndex: u32,
    floorLayer: u32,
    ceilingLayer: u32,
    flags: u32,
};
struct RayHit {
    distance: f32,
    wallU: f32,
    hitCell: u32,
    side: u32,
};

@group(0) @binding(0)
var<uniform> camera: Camera;

@group(0) @binding(1)
var<storage, read> worldMap: array<RenderCell>;

@group(0) @binding(2)
var<storage, read_write> rayHits: array<RayHit>;

@group(0) @binding(3)
var<uniform> mapInfo: MapInfo;

@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) id: vec3u) {
    let x = id.x * u32(camera.columns.x) + u32(camera.columns.y);
    let screenWidth = u32(camera.screenSize.x);

    if (x >= screenWidth) {
        return;
    }

    // Match fragment/rasterizer pixel centers, including thin door silhouettes.
    let cameraX =
        2.0 * (f32(x) + 0.5) / camera.screenSize.x - 1.0;

    let rayDir =
        camera.direction + camera.plane * cameraX;
    // DDA stores perpendicular depth; the cutoff is a circle in world space.
    let rayLimit = select(1e30, camera.look.z / length(rayDir), camera.look.z > 0.0);

    var mapX = i32(floor(camera.position.x));
    var mapY = i32(floor(camera.position.y));

    let deltaDistX = select(
        abs(1.0 / rayDir.x),
        1e30,
        rayDir.x == 0.0
    );

    let deltaDistY = select(
        abs(1.0 / rayDir.y),
        1e30,
        rayDir.y == 0.0
    );

    var stepX: i32;
    var stepY: i32;

    var sideDistX: f32;
    var sideDistY: f32;

    if (rayDir.x < 0.0) {
        stepX = -1;
        sideDistX =
            (camera.position.x - f32(mapX)) * deltaDistX;
    } else {
        stepX = 1;
        sideDistX =
            (f32(mapX) + 1.0 - camera.position.x) * deltaDistX;
    }

    if (rayDir.y < 0.0) {
        stepY = -1;
        sideDistY =
            (camera.position.y - f32(mapY)) * deltaDistY;
    } else {
        stepY = 1;
        sideDistY =
            (f32(mapY) + 1.0 - camera.position.y) * deltaDistY;
    }

    var side = 0u;
    var hitCell = 0u;
    var distance = 1e30;
    var doorU = 0.0;
    var doorHit = false;

    // Protect against invalid maps or open boundaries.
    let maxSteps = mapInfo.width + mapInfo.height + 1u;

    for (var i = 0u; i < maxSteps; i++) {
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
            mapX >= i32(mapInfo.width) ||
            mapY >= i32(mapInfo.height)) {
            break;
        }

        // C# map[x][y] has been flattened row-major.
        let index =
            u32(mapY) * mapInfo.width + u32(mapX);

        let cell = worldMap[index];

        if ((cell.flags & 8u) != 0u) {
            // Thin sliding plane at the cell center; the opening is a real ray gap.
            let normalX = (cell.flags & 16u) != 0u;
            let normal = select(rayDir.y, rayDir.x, normalX);
            if ((cell.flags & 1u) != 0u && abs(normal) > 0.000001) {
                let plane = select(f32(mapY), f32(mapX), normalX) + 0.5;
                let origin = select(camera.position.y, camera.position.x, normalX);
                let t = (plane - origin) / normal;
                let along = select(camera.position.x + t * rayDir.x - f32(mapX),
                    camera.position.y + t * rayDir.y - f32(mapY), normalX);
                let opened = f32(cell.flags >> 16u) / 65535.0;
                if (t >= 0.0 && t <= rayLimit && t <= min(sideDistX, sideDistY) + 0.00001 && along >= opened && along < 1.0) {
                    hitCell = index + 1u;
                    distance = t;
                    side = select(1u, 0u, normalX);
                    doorU = along - opened;
                    doorHit = true;
                    break;
                }
            }
            continue;
        }

        if ((cell.flags & 1u) != 0u) {
            hitCell = index + 1u;

            distance = select(
                sideDistY - deltaDistY,
                sideDistX - deltaDistX,
                side == 0u
            );

            break;
        }
    }

    var wallU = 0.0;

    if (doorHit) {
        wallU = doorU;
    } else if (hitCell != 0u) {
        let hitCoordinate = select(
            camera.position.x + distance * rayDir.x,
            camera.position.y + distance * rayDir.y,
            side == 0u
        );

        wallU = fract(hitCoordinate);

        // Keep opposite wall faces oriented as in Lode's textured raycaster.
        if ((side == 0u && rayDir.x > 0.0) ||
            (side == 1u && rayDir.y < 0.0)) {
            wallU = 1.0 - wallU;
        }
    }

    rayHits[x] = RayHit(
        distance,
        wallU,
        hitCell,
        side
    );
}
