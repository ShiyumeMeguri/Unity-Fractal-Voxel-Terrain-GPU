#ifndef RURI_COMMON_FRACTALLIBRARY_INCLUDED
#define RURI_COMMON_FRACTALLIBRARY_INCLUDED

float AmazingBoxDE(float3 pos, float scale, float foldingLimit, int iterations, out float3 finalPos)
{
    float3 z = pos;
    float dr = 1.0;

    for (int i = 0; i < iterations; i++)
    {
        // 盒子折叠
        z = abs(z);
        if (z.x > foldingLimit) z.x = 2.0 * foldingLimit - z.x;
        if (z.y > foldingLimit) z.y = 2.0 * foldingLimit - z.y;
        if (z.z > foldingLimit) z.z = 2.0 * foldingLimit - z.z;

        // 缩放和平移
        z = z * scale + pos;
        dr = dr * abs(scale) + 1.0;
    }

    finalPos = z; // 输出迭代后的坐标
    return length(z) / abs(dr);
}
float SierpinskiDE(float3 pos, out float3 finalPos)
{
    float3 p = pos;
    int iterations = 8;
    float scale = 2.0;

    for (int i = 0; i < iterations; i++)
    {
        // 对称折叠
        p = abs(p);

        if (p.x + p.y < 0.0) p.xy = -p.yx;
        if (p.x + p.z < 0.0) p.xz = -p.zx;
        if (p.y + p.z < 0.0) p.yz = -p.zy;

        // 缩放和平移
        p = scale * p - (scale - 1.0);
    }

    finalPos = p; // 输出迭代后的坐标
    return length(p) * pow(scale, -float(iterations));
}
float MandelbrotDE(float2 c, int maxIterations, out float2 finalPos)
{
    //分形反转
    //float denom = c.x*c.x + c.y*c.y;
    //c /= denom;
    float2 z = float2(0.0, 0.0);  // z 初始为 (0, 0)
    float dz = 1.0;
    finalPos = z;
    float r = 0.0;

    // 迭代曼德博公式
    for (int i = 0; i < maxIterations; i++)
    {
        r = dot(z, z);  // |z|^2

        // 如果 r 超过阈值，说明 z 逃逸
        if (r > 4.0) break;

        // 曼德博公式 z = z^2 + c
        float2 zNew = float2(
            z.x * z.x - z.y * z.y + c.x,
            2.0 * z.x * z.y + c.y
        );

        dz = 2.0 * length(z) * dz + 1.0;
        z = zNew;
        finalPos = z;
    }

    // 如果 z 逃逸，返回距离估算值
    if (r > 4.0)
        return 0.5 * log(r) * r / dz;
    
    // 否则返回一个大值，表示它属于集合
    return 0.0;
}
float QuaternionJuliaDE(float4 pos, float4 c, int iterations, out float4 finalPos)
{
    float4 z = pos;
    float dr = 1.0;
    float r = 0.0;

    for (int i = 0; i < iterations; i++)
    {
        r = length(z);
        if (r > 4.0)
            break;

        // 四元数乘法：z = z^2 + c
        float x = z.x, y = z.y, z1 = z.z, w = z.w;
        float4 z_new;
        z_new.x = x * x - y * y - z1 * z1 - w * w;
        z_new.y = 2.0 * x * y;
        z_new.z = 2.0 * x * z1;
        z_new.w = 2.0 * x * w;
        z = z_new + c;

        dr = 2.0 * r * dr;
    }

    finalPos = z; // 输出迭代后的坐标
    return 0.5 * log(r) * r / dr;
}
float MandelbulbDE(float3 pos, int iterations, float power, out float3 finalPos)
{
    float3 z = pos;
    float dr = 1.0;
    float r = 0.0;

    for (int i = 0; i < iterations; i++)
    {
        r = length(z);
        if (r > 8.0)
            break;

        // 计算导数
        float theta = acos(z.z / r);
        float phi = atan2(z.y, z.x);
        dr = pow(r, power - 1.0) * power * dr + 1.0;

        // 放大和旋转
        float zr = pow(r, power);
        theta *= power;
        phi *= power;

        // 转换回笛卡尔坐标
        z = zr * float3(sin(theta) * cos(phi), sin(theta) * sin(phi), cos(theta));
        z += pos;
    }

    finalPos = z; // 输出迭代后的坐标
    return 0.5 * log(r) * r / dr;
}
float MengerSpongeDE(float3 pos, out float3 finalPos)
{
    float3 p = pos;
    int iterations = 10;
    float scale = 3.0;

    for (int i = 0; i < iterations; i++)
    {
        p = abs(p);

        if (p.x > p.y) p.xy = p.yx;
        if (p.x > p.z) p.xz = p.zx;
        if (p.y > p.z) p.yz = p.zy;

        p = scale * p - (scale - 1.0);
    }

    finalPos = p; // 输出迭代后的坐标
    return length(p) * pow(scale, -float(iterations));
}
float JuliaDE(float3 pos, out float3 finalPos)
{
    float3 z = pos;
    float dr = 1.0;
    float r = 0.0;
    int Iterations = 10;
    float Bailout = 2.0;
    float Power = 8.0;
    float3 c = float3(0.355, 0.355, 0.355); // 常数项，可调整

    for (int i = 0; i < Iterations; i++) {
        r = length(z);
        if (r > Bailout)
            break;

        // 转换为球坐标
        float theta = acos(z.z / r);
        float phi = atan2(z.y, z.x);
        dr = pow(r, Power - 1.0) * Power * dr + 1.0;

        // 放大和旋转
        float zr = pow(r, Power);
        theta *= Power;
        phi *= Power;

        // 转换回笛卡尔坐标
        z = zr * float3(sin(theta) * cos(phi), sin(theta) * sin(phi), cos(theta));
        z += c;
    }

    finalPos = z; // 输出迭代后的坐标
    return 0.5 * log(r) * r / dr;
}
float KleinianDE(float3 fractalPos, float4 minFractalPos, float4 maxFractalPos, out float3 finalPos)
{
    float scale = 1.0;
    float3 kp = fractalPos;
    for (int i = 0; i < 7; i++) {
        float3 maxPos = max(minFractalPos.xyz, kp);
        float3 minPos = min(maxFractalPos.xyz, maxPos);
        kp = 2.0 * minPos - kp;
        float kleinian = max(minFractalPos.w / dot(kp, kp), 1.0);
        kp *= kleinian;
        scale *= kleinian;
    }
    float fractalViewLength = length(kp.xy);
    float fractalPosLength = length(kp.xyz);

    float viewHitDistance = fractalViewLength - maxFractalPos.w;
    float worldHitDistance = kp.z * fractalViewLength;

    float kleinianDistance = max(worldHitDistance / fractalPosLength, viewHitDistance) / scale;

    finalPos = kp; // 输出迭代后的坐标
    return kleinianDistance;
}

#endif // RURI_COMMON_FRACTALLIBRARY_INCLUDED
