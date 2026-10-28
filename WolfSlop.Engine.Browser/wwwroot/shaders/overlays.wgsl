struct Overlay {
    rect: vec4f,
    uv: vec4f,
    pose: vec4f,
    screen: vec4f,
};

struct VertexOutput {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
};

@group(0) @binding(0) var<storage, read> layers: array<Overlay>;
@group(0) @binding(1) var image: texture_2d<f32>;
@group(0) @binding(2) var imageSampler: sampler;

@vertex
fn vertexMain(@builtin(vertex_index) vertex: u32, @builtin(instance_index) instance: u32) -> VertexOutput {
    let corners = array<vec2f, 6>(
        vec2f(0.0, 0.0), vec2f(1.0, 0.0), vec2f(0.0, 1.0),
        vec2f(0.0, 1.0), vec2f(1.0, 0.0), vec2f(1.0, 1.0)
    );
    let layer = layers[instance];
    let corner = corners[vertex];
    let point = (corner - layer.pose.xy) * layer.rect.zw * layer.screen.xy;
    let c = cos(layer.pose.z);
    let s = sin(layer.pose.z);
    let pixel = layer.rect.xy * layer.screen.xy + vec2f(c * point.x - s * point.y, s * point.x + c * point.y);
    var output: VertexOutput;
    output.position = vec4f(pixel / layer.screen.xy * vec2f(2.0, -2.0) + vec2f(-1.0, 1.0), 0.0, 1.0);
    output.uv = layer.uv.xy + corner * layer.uv.zw;
    return output;
}

@fragment
fn fragmentMain(input: VertexOutput) -> @location(0) vec4f {
    return textureSampleLevel(image, imageSampler, input.uv, 0.0);
}
