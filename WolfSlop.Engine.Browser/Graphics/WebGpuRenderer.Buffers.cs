using System.Numerics;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Engine.World;
using WolfNET.Engine.Actors;

using WolfNET.Engine.Graphics;

namespace WolfNET.Engine.Browser.Graphics;

public sealed partial class WebGpuRenderer
{
    private void CreateBuffers()
    {
        if (_device == null)
        {
            throw new InvalidOperationException(
                "GPU device is not initialized.");
        }

        int mapCapacity = checked(
            _settings.MaxMapWidth *
            _settings.MaxMapHeight);



        // Camera:
        // position   vec2f
        // direction  vec2f
        // plane      vec2f
        // screenSize vec2f
        // look       vec4f (pitch, elevation, render distance, fog start)
        // columns    vec4f (column step, rotating phase, unused, unused)
        // Total: 64 bytes

        _cameraBuffer = _device.CreateBuffer(
            new GPUBufferDescriptor
            {
                Label = "WolfNET Camera",

                Size = (ulong)CameraBufferSize,

                Usage =
                    GPUBufferUsage.Uniform |
                    GPUBufferUsage.CopyDst
            });

        // Map dimensions:
        // width  u32
        // height u32
        // spriteCount u32 (updated with the sprite buffer)
        // Padding to 16 bytes

        _mapInfoBuffer = _device.CreateBuffer(
            new GPUBufferDescriptor
            {
                Label = "WolfNET Map Info",

                Size = (ulong)MapInfoBufferSize,

                Usage =
                    GPUBufferUsage.Uniform |
                    GPUBufferUsage.CopyDst
            });

        // Maximum map capacity.
        // Actual map can be smaller and change at runtime.

        _worldMapBuffer = _device.CreateBuffer(
            new GPUBufferDescriptor
            {
                Label = "WolfNET World Map",

                Size = checked(
                    (ulong)mapCapacity * 16),

                Usage =
                    GPUBufferUsage.Storage |
                    GPUBufferUsage.CopyDst
            });

        // One RayHit structure per screen column.
        // RayHit = 4 x 32-bit values = 16 bytes.

        _rayHitsBuffer = _device.CreateBuffer(
            new GPUBufferDescriptor
            {
                Label = "WolfNET Ray Hits",

                Size = checked(
                    (ulong)_settings.Width * RayHitSize),

                Usage = GPUBufferUsage.Storage
            });

        Console.WriteLine(
            $"[GPU] Buffers allocated. " +
            $"Max map: {_settings.MaxMapWidth}x" +
            $"{_settings.MaxMapHeight}, " +
            $"Screen: {_settings.Width}x{_settings.Height}");
    }

    private static GPUBindGroupEntry BufferEntry(
        uint binding,
        GPUBuffer buffer)
    {
        return new GPUBindGroupEntry
        {
            Binding = binding,

            Resource = new GPUBufferBinding
            {
                Buffer = buffer
            }
        };
    }

    /// <summary>Uploads compiled row-major cells only when their snapshot changes.</summary>
    private void UpdateMap(RenderMap map, GPUQueue queue)
    {
        if (ReferenceEquals(_uploadedMap, map)) return;
        if (!ReferenceEquals(_preparedTextures, map.Textures))
            throw new InvalidOperationException("Prepare the scene's texture catalog before rendering it.");
        if (map.Width > _settings.MaxMapWidth || map.Height > _settings.MaxMapHeight)
            throw new InvalidDataException("Compiled map exceeds GPU buffer capacity.");
        var cells = new uint[checked(map.Cells.Count * 4)];
        for (int i = 0; i < map.Cells.Count; i++)
        {
            var cell = map.Cells[i];
            cells[i * 4] = cell.MaterialIndex;
            cells[i * 4 + 1] = cell.FloorTextureLayer;
            cells[i * 4 + 2] = cell.CeilingTextureLayer;
            cells[i * 4 + 3] = (uint)cell.Flags;
        }
        using (var data = new Uint32Array(cells))
            queue.WriteBuffer(_worldMapBuffer!, 0, data);
        using (var info = new Uint32Array(new uint[] { (uint)map.Width, (uint)map.Height, (uint)_spriteCount, 0 }))
            queue.WriteBuffer(_mapInfoBuffer!, 0, info);
        _uploadedMap = map;
        Console.WriteLine($"[GPU] Map updated: {map.Width}x{map.Height}, revision={map.Revision}, cells={map.Cells.Count}");
    }
    /// <summary>
    /// Updates GPU camera data every frame.
    /// </summary>
    private void UpdateCamera(
        Vector2 position,
        Vector2 direction,
        Vector2 plane,
        float pitch,
        float elevation, int columnStep, int columnPhase)
    {
        if (_device == null || _cameraBuffer == null)
        {
            throw new InvalidOperationException(
                "GPU camera buffer is not initialized.");
        }

        // Must match Camera in raycast.wgsl
        // and walls.wgsl / sprites.wgsl (64 bytes including interlacing).

        _cameraData[0] = position.X;
        _cameraData[1] = position.Y;

        _cameraData[2] = direction.X;
        _cameraData[3] = direction.Y;

        _cameraData[4] = plane.X;
        _cameraData[5] = plane.Y;

        _cameraData[6] = _settings.Width;
        _cameraData[7] = _settings.Height;
        _cameraData[8] = pitch;
        _cameraData[9] = elevation;
        _cameraData[10] = _settings.RenderDistance;
        _cameraData[11] = _settings.FogStartDistance;
        _cameraData[12] = columnStep;
        _cameraData[13] = columnPhase;

        Buffer.BlockCopy(_cameraData, 0, _cameraBytes, 0, CameraBufferSize);
    }

}
