using System.Numerics;
using System.Runtime.InteropServices;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfSlop.Engine.World;
using WolfSlop.Engine.Actors;

using WolfSlop.Engine.Graphics;

namespace WolfSlop.Engine.Browser.Graphics;

public sealed partial class WebGpuRenderer
{
    public async Task PrepareSceneAsync(RenderMap map, CancellationToken cancellationToken = default,
        Func<string, Task>? reportProgress = null, SceneResources? resources = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized) throw new InvalidOperationException("Initialize the GPU before preparing a scene.");
        ReleaseScene();
        try
        {
            _textures = await GpuSceneTextures.LoadAsync(_device!, _js, _settings, map.Sky,
                resources?.Textures ?? map.Textures, cancellationToken, reportProgress);
            await PrepareOverlaysAsync(resources?.OverlayTextures ?? [], cancellationToken, reportProgress);

            if (reportProgress != null) await reportProgress("Preparing scene…");
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);

            _materialsBuffer = _device!.CreateBuffer(new GPUBufferDescriptor
            {
                Label = "WolfSlop Wall Materials",
                Size = checked((ulong)map.Materials.Count * 16),
                Usage = GPUBufferUsage.Storage | GPUBufferUsage.CopyDst
            });
            var materials = new uint[map.Materials.Count * 4];
            for (int i = 0; i < map.Materials.Count; i++)
            {
                var material = map.Materials[i];
                materials[i * 4] = material.North;
                materials[i * 4 + 1] = material.East;
                materials[i * 4 + 2] = material.South;
                materials[i * 4 + 3] = material.West;
            }
            using var queue = _device.Queue;
            using (var data = new Uint32Array(materials))
                queue.WriteBuffer(_materialsBuffer, 0, data);
            using var layout = _renderPipeline!.GetBindGroupLayout(0);
            _renderBindGroup = _device.CreateBindGroup(new GPUBindGroupDescriptor
            {
                Label = "WolfSlop Render Bindings",
                Layout = layout,
                Entries =
                [
                    BufferEntry(0, _rayHitsBuffer!),
                    BufferEntry(1, _cameraBuffer!),
                    new GPUBindGroupEntry { Binding = 2, Resource = _textures!.WallView },
                    BufferEntry(3, _worldMapBuffer!),
                    BufferEntry(4, _mapInfoBuffer!),
                    BufferEntry(6, _materialsBuffer),
                    new GPUBindGroupEntry { Binding = 7, Resource = _textures!.TextureSampler },
                    new GPUBindGroupEntry { Binding = 8, Resource = _textures!.SurfaceSampler },
                    new GPUBindGroupEntry { Binding = 9, Resource = _textures!.SkyView },
                    new GPUBindGroupEntry { Binding = 10, Resource = _textures!.SkySampler },
                    BufferEntry(11, _textures!.SkyBuffer)
                ]
            });
            _preparedTextures = map.Textures;
            _uploadedMap = null;
            UpdateMap(map, queue);
            _sceneAnimationClock.Restart();
            Console.WriteLine($"[GPU] Scene prepared: {map.Width}x{map.Height}, {map.Sprites.Count} sprites");
        }
        catch
        {
            ReleaseScene();
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }

    public void ReleaseScene()
    {
        ReleaseOverlays();
        _sceneAnimationClock.Reset();
        _preparedTextures = null;
        _renderBindGroup?.Dispose();
        _spriteBindGroup?.Dispose();
        _minimapBindGroup?.Dispose();
        _renderBindGroup = _spriteBindGroup = _minimapBindGroup = null;
        DestroyBuffer(_spriteBuffer);
        _spriteBuffer = null;
        _spriteCapacity = 0;
        _spriteUpload?.Dispose();
        _spriteUpload = null;
        _spriteBytes = [];
        _uploadedSpriteCount = -1;
        DestroyBuffer(_materialsBuffer);
        _materialsBuffer = null;
        _textures?.Dispose();
        _textures = null;
        _uploadedMap = null;
        _spriteSelection.Clear();
        _browserSprites?.ReleaseScene();
    }

    private void UpdateSprites(GPUQueue queue)
    {
        var sprites = _spriteSelection.Visible;
        var spriteCount = sprites.Length;

        if (Math.Max(1, spriteCount) > _spriteCapacity)
        {
            _spriteBindGroup?.Dispose();
            _minimapBindGroup?.Dispose();
            DestroyBuffer(_spriteBuffer);
            _spriteCapacity = Math.Max(16, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)spriteCount));
            _spriteUpload?.Dispose();
            _spriteUpload = new Uint32Array(_spriteCapacity * 8);
            _spriteBytes = new byte[checked(_spriteCapacity * SpriteSize)];
            _spriteBuffer = _device!.CreateBuffer(new GPUBufferDescriptor
            {
                Label = "WolfSlop Sprites",
                Size = checked((ulong)_spriteCapacity * SpriteSize),
                Usage = GPUBufferUsage.Storage | GPUBufferUsage.CopyDst
            });

            using var layout = _spritePipeline!.GetBindGroupLayout(0);
            _spriteBindGroup = _device.CreateBindGroup(new GPUBindGroupDescriptor
            {
                Label = "WolfSlop Sprite Bindings",
                Layout = layout,
                Entries =
                [
                    BufferEntry(0, _rayHitsBuffer!),
                    BufferEntry(1, _cameraBuffer!),
                    new GPUBindGroupEntry { Binding = 2, Resource = _textures!.WallView },
                    BufferEntry(3, _spriteBuffer),
                    new GPUBindGroupEntry { Binding = 4, Resource = _textures!.TextureSampler },
                    BufferEntry(5, _worldMapBuffer!),
                    BufferEntry(6, _mapInfoBuffer!)
                ]
            });

            using var minimapLayout = _minimapPipeline!.GetBindGroupLayout(0);
            _minimapBindGroup = _device.CreateBindGroup(new GPUBindGroupDescriptor
            {
                Label = "WolfSlop Minimap Bindings",
                Layout = minimapLayout,
                Entries =
                [
                    BufferEntry(0, _rayHitsBuffer!),
                    BufferEntry(1, _cameraBuffer!),
                    BufferEntry(3, _worldMapBuffer!),
                    BufferEntry(4, _mapInfoBuffer!),
                    BufferEntry(5, _spriteBuffer)
                ]
            });
        }

        // The third u32 of MapInfo counts active sprites, excluding spare buffer capacity.
        if (_uploadedSpriteCount != spriteCount)
        {
            BitConverter.TryWriteBytes(_spriteCountBytes, (uint)spriteCount);
            _spriteCountUpload!.WriteBytes(_spriteCountBytes);
            queue.WriteBuffer(_mapInfoBuffer!, 8, _spriteCountUpload);
            _uploadedSpriteCount = spriteCount;
        }

        if (spriteCount == 0)
            return;

        MemoryMarshal.AsBytes(sprites).CopyTo(_spriteBytes);
        _spriteUpload!.Write(_spriteBytes, 0, 0, spriteCount * SpriteSize);
        // writeBuffer offsets/sizes are in elements for a Uint32Array, not bytes.
        queue.WriteBuffer(_spriteBuffer!, 0UL, _spriteUpload, 0, (ulong)spriteCount * 8);
    }

}
