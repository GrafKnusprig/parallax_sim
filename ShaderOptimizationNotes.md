# GPU Optimization & Shader Fundamentals

This document explains the technical rationale behind the design of the `StarCulling.compute` shader, focusing on performance, memory safety, and hardware architecture.

## 1. Why the Dot Product is "Cheap"

In shader programming, the **Dot Product** is considered one of the most efficient operations available.

*   **Hardware Implementation**: Modern GPUs have dedicated hardware for "Fused Multiply-Add" (FMA) instructions. A dot product of two `float3` vectors is essentially 3 multiplies and 2 additions. The GPU can often execute these in a single clock cycle through SIMD (Single Instruction, Multiple Data) processing.
*   **Vectorization**: GPUs are designed to think in 4-component vectors (`float4`). A dot product fits perfectly into the ALU (Arithmetic Logic Unit) pipelines, making it significantly faster than operations involving square roots (`length`/`normalize`) or trigonometric functions.
*   **Early Exit**: Using a dot product for FOV culling allows the shader to "Fail Fast." If the dot product shows a star is behind the camera, the GPU can skip all subsequent complex math for that thread, saving thousands of cycles across the 2.4 million stars.

## 2. The Risks of Implicit Alignment

When passing data from the CPU (C#) to the GPU (HLSL), how the data is laid out in memory is critical.

*   **The 16-Byte Rule**: Most GPU architectures and APIs (like DirectX/Metal) prefer or require data to be aligned to **16-byte boundaries** (the size of a `float4`).
*   **Implicit Padding**: If you define a struct with a `float3` followed by a `float`, the C# compiler might pack them tightly (16 bytes total), but the HLSL compiler might expect the `float3` to start on a new 16-byte "slot" or add its own padding.
*   **Data Corruption**: If the CPU and GPU don't agree on where one variable ends and the next begins, the GPU will read "garbage" or shifted values. For example, the `w` component of a `float4` might be read as the `x` component of the next vector.
*   **The Solution**: By using explicit `float4` types in the Compute Shader, we force both sides to respect a clear 16-byte stride, making the data interface "type-safe" and predictable.

## 3. Using `float` (and the `float4` container)

The choice of `float` (32-bit) vs other types and why we pack them:

*   **Precision**: `float` is the standard for spatial coordinates in Unity. While `half` (16-bit) is faster on mobile, it lacks the precision needed for parsec-scale astronomical distances.
*   **Register Usage**: GPUs use wide registers. Storing data in `float4` is not just about alignment; it's about matching the "width" of the hardware registers. A `float4` can be loaded into a single register in a single memory fetch.
*   **Bandwidth Efficiency**: Moving 2.4 million stars from RAM to VRAM is a bottleneck. By packing `position` and `distance` into one `float4`, and `direction` and `inverse distance` into another, we minimize the number of "fetch" operations the GPU has to perform. We are essentially saturating the memory bus with useful data rather than empty padding.
