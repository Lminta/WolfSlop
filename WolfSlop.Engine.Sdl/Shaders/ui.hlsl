Texture2D<float4> image:register(t0,space2);
SamplerState imageSampler:register(s0,space2);
cbuffer SizeBuffer:register(b0,space3) { float4 dimensions; };
float4 main(float4 pixel:SV_Position):SV_Target0 { return image.Sample(imageSampler,pixel.xy/dimensions.xy); }
