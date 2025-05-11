#ifndef RURI_COMMON_FRACTALWORLDLIBRARY_INCLUDED
#define RURI_COMMON_FRACTALWORLDLIBRARY_INCLUDED

// === 公共辅助函数 ===
static float modf(float x, float y)
{
    return x - y * floor(x / y);
}
static float2 modf(float2 x, float2 y)
{
    return x - y * floor(x / y);
}
static float3 modf(float3 x, float3 y)
{
    return x - y * floor(x / y);
}

static float2x2 r2(float r)
{
    return float2x2(float2(cos(r), sin(r)), float2(-sin(r), cos(r)));
}

static float bo(inout float3 p, float3 r)
{
    p = abs(p) - r;
    return max(max(p.x, p.y), p.z);
}

// 分形细节函数 fb：计算局部细节的距离和材质信息
static float2 fb(inout float3 p, float i, float s, float b)
{
    float2 t = float2(
        (length(p.xz) - 2.0) - (clamp(sin(p.y * 5.0), -0.2, 0.2) * 0.2),
        5.0
    );
    t.x = abs(t.x) - 0.2;

    float3 pp = p;
    pp.y += 1.0 - (i * 2.0);

    // Box 折叠
    float3 boxR = float3(0.65, 2.0, 200.0);
    float dbox = bo(pp, boxR);
    float a = max(abs(dbox) - 0.2, abs(pp.y) - 1.0);
    t.x = min(t.x, lerp(a, length(pp.xy - sin(p.z * 0.5).xx) - 0.5, b));

    // 第二个 Box
    pp.x = lerp(abs(pp.x) - 0.7, (pp.y * 0.5) - 0.8, b);
    pp.z = modf(pp.z, 3.0) - 1.5;
    pp -= lerp(
        float3(0.0,  1.0, 0.0),
        float3(0.0, -1.3, 0.0) + sin(p.z * 0.5).xxx,
        b.xxx
    );
    float3 boxR2 = float3(0.1, 2.0, 0.1);
    float dbox2 = bo(pp, boxR2);
    t.x = min(t.x, dbox2);

    // 平面切除
    pp.y -= 2.0;
    float la = length(pp) - 0.1;
    t.x = min(t.x, la);

    // 缩放归一
    t.x /= s;

    // 其他裁剪
    t.x = max(t.x, -(length(pp.xy - float2(-2.0 * b, 6.0 - (i * 0.1))) - 5.0));
    t.x = max(t.x, (abs(pp.y) - 5.0) + i);

    // 第二个裁剪比较
    float2 h = float2(
        (length(p.xz) - 1.0) + ((pp.y * 0.1) / ((i * 2.0) + 1.0)),
        3.0
    );
    h.x /= s;
    h.x = max(h.x, -(length(pp.xy - float2(0.0, (6.1 + (3.0 * b)) - (i * 0.1))) - 5.0));
    h.x = max(h.x, ((abs(pp.y) - 5.5) - (5.0 * b)) + i);
    t = (t.x < h.x) ? t : h;

    // 第三次裁剪（仅 i<2 时）
    if (i < 2.0)
    {
        h = float2(abs(length(p.xz) - 1.2) - 0.1, 6.0);
        h.x /= s;
        h.x = max(h.x, -(length(pp.xy - float2(-1.0 * b, 6.2 - (i * 0.1))) - 5.0));
        h.x = max(h.x, (abs(pp.y) - 6.0) + i);
        t = (t.x < h.x) ? t : h;
    }

    return t;
}

// 主 SDF 函数 mp：旋转、迭代细节，返回 (distance, material)
static float2 mp(inout float3 p, float time, float blend)
{
    // 1. 两次旋转
    float2x2 m1 = r2(lerp(-0.785, -0.6154, blend));
    p.yz = mul(m1, p.yz);
    float2x2 m2 = r2(lerp(0.0, 0.785, blend));
    p.xz = mul(m2, p.xz);

    // 2. 生成动态偏移
    p.z = modf(p.z - time, 10.0) - 5.0;

    // 3. 初始化最小距离
    float2 best = float2(1000.0, 1000.0);

    // 4. 5 次 fb 迭代
    float4 np = float4(p, 1.0);
    for (int i = 0; i < 5; i++)
    {
        np = float4(abs(np.xyz), np.w);
        np.xz = mul(r2(-0.785), np.xz);
        np.xyz *= 2.0999999;

        float2 f = fb(np.xyz, i, np.w, blend);
        f.x *= 0.75;
        best = (best.x < f.x) ? best : f;
    }

    // 5. 最后一个平面裁剪
    float2 h = float2(
        (p.y + 2.0) + (3.0 * cos(p.x * 0.35)),
        6.0
    );
    h.x = max(h.x, p.y);
    h.x *= 0.5;
    best = (best.x < h.x) ? best : h;

    return best;
}

// === 对外暴露的 DE 接口 ===
// | pos：查询点坐标
// | time：场景时间（对应 Shader 中的 tt）
// | blend：控制参数（对应 Shader 中的 bb）
// | finalPos：迭代后的位置输出
float FdSGWwWorldDE(float3 pos, out float3 finalPos)
{
    float3 p = pos;
    float time = 1;
    float blend = 2;
    float2 d = mp(p, time, blend);
    finalPos = p;
    return d.x;
}

#endif // RURI_COMMON_FRACTALWORLDLIBRARY_INCLUDED
