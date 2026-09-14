Shader "Riverworks/Resident Vertex Color"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 150
        CGPROGRAM
        #pragma surface surf Lambert noforwardadd nolightmap nodynlightmap
        #pragma target 2.0

        fixed4 _Color;
        struct Input { fixed4 color : COLOR; };

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 c = IN.color * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "VertexLit"
}
